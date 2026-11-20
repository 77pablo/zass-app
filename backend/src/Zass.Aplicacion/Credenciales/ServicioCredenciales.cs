using Zass.Aplicacion.Comun;
using Zass.Aplicacion.Puertos;
using Zass.Dominio.Comun;
using Zass.Dominio.Credenciales;

namespace Zass.Aplicacion.Credenciales;

/// <summary>MOD-02 - Credenciales. Ver <see cref="IServicioCredenciales"/>.</summary>
public sealed class ServicioCredenciales : IServicioCredenciales
{
    public const int LargoMinimoNombre = 3;
    public const int LargoMaximoNombre = 150;
    public const int LargoMaximoEntidad = 200;

    /// <summary>
    /// Cuántas credenciales puede tener un prestador. Cinco veces lo que dice CU-05 de
    /// "1 a 5 al inicio": holgura para quien tiene una carrera larga, y un tope para que nadie
    /// llene la tabla ni el trabajo de los Verificadores con basura.
    /// </summary>
    public const int MaximoPorPrestador = 25;

    /// <summary>API-26: tope por archivo (CU-05 paso 6).</summary>
    public const long TamanoMaximoDocumento = 10 * 1024 * 1024;

    /// <summary>API-26: cuántos documentos puede tener una credencial (E-409).</summary>
    public const int MaximoDocumentosPorCredencial = 5;

    private readonly IRepositorioCredenciales _repo;
    private readonly IUnidadDeTrabajo _trabajo;
    private readonly IRelojSistema _reloj;
    private readonly IAuditor _auditor;
    private readonly OpcionesVerificacion _opciones;
    private readonly IAlmacenDeArchivos? _almacen;
    private readonly IProcesadorDeImagenes? _imagenes;

    /// <summary>
    /// El almacén y el procesador son opcionales para no obligar a las pruebas que no tocan
    /// archivos a fabricarlos. En la aplicación de verdad siempre están (AgregarZass); si faltan,
    /// subir un documento responde AlmacenamientoNoDisponible en vez de romperse.
    /// </summary>
    public ServicioCredenciales(
        IRepositorioCredenciales repo, IUnidadDeTrabajo trabajo,
        IRelojSistema reloj, IAuditor auditor, OpcionesVerificacion? opciones = null,
        IAlmacenDeArchivos? almacen = null, IProcesadorDeImagenes? imagenes = null)
    {
        _opciones = opciones ?? new OpcionesVerificacion();
        _repo = repo;
        _trabajo = trabajo;
        _reloj = reloj;
        _auditor = auditor;
        _almacen = almacen;
        _imagenes = imagenes;
    }

    internal static MiDocumentoDto ADto(DocumentoCredencial d) =>
        new(d.Id, d.NombreOriginal, d.TipoMime, d.TamanoBytes, d.Estado, d.SubidoEn);

    // ------------------------------------------------------------- API-24

    public async Task<IReadOnlyList<MiCredencialDto>> MisCredencialesAsync(
        Guid perfilPrestadorId, CancellationToken ct = default)
    {
        var credenciales = await _repo.DePrestadorAsync(perfilPrestadorId, ct);
        return credenciales.Select(c => new MiCredencialDto(
            c.Id, c.Tipo, c.Nombre,
            c.EntidadCertificadora?.Nombre,
            c.EntidadCertificadora?.Catalogada,
            c.ObtenidaEn, c.VenceEn, c.Estado,
            c.TieneRespaldo(), c.Documentos.Count)
        {
            Respaldo = c.Documentos.OrderBy(d => d.SubidoEn).Select(ADto).ToList(),
        }).ToList();
    }

    // ------------------------------------------------------------- API-25

    public async Task<Resultado<Guid>> CrearAsync(
        CrearCredencialCmd cmd, CancellationToken ct = default)
    {
        var nombre = cmd.Nombre?.Trim() ?? "";
        if (nombre.Length is < LargoMinimoNombre or > LargoMaximoNombre)
            return Resultado.Falla<Guid>(Fallas.Validacion,
                $"El nombre de la credencial debe tener entre {LargoMinimoNombre} y {LargoMaximoNombre} caracteres.");

        if (!Enum.IsDefined(cmd.Tipo))
            return Resultado.Falla<Guid>(Fallas.Validacion, "Elige un tipo de credencial.");

        var hoy = DateOnly.FromDateTime(_reloj.Ahora);
        if (cmd.ObtenidaEn > hoy)
            return Resultado.Falla<Guid>(Fallas.Validacion,
                "La fecha de obtención no puede ser futura.");
        if (cmd.VenceEn is not null && cmd.VenceEn <= cmd.ObtenidaEn)
            return Resultado.Falla<Guid>(Fallas.Validacion,
                "El vencimiento tiene que ser posterior a la fecha de obtención.");

        var entidadNombre = cmd.EntidadNombre?.Trim();
        if (cmd.EntidadId is not null && !string.IsNullOrEmpty(entidadNombre))
            return Resultado.Falla<Guid>(Fallas.Validacion,
                "Elige la entidad de la lista o escribe su nombre, pero no las dos cosas.");
        if (entidadNombre is { Length: > 0 and < LargoMinimoNombre } or { Length: > LargoMaximoEntidad })
            return Resultado.Falla<Guid>(Fallas.Validacion,
                $"El nombre de la entidad debe tener entre {LargoMinimoNombre} y {LargoMaximoEntidad} caracteres.");

        // Después de las reglas baratas: no se cuenta ni se consulta lo que ya es inválido.
        if (await _repo.ContarDePrestadorAsync(cmd.PerfilPrestadorId, ct) >= MaximoPorPrestador)
            return Resultado.Falla<Guid>(Fallas.ReglaDeNegocio,
                $"Llegaste al máximo de {MaximoPorPrestador} credenciales. Elimina alguna para agregar otra.");

        EntidadCertificadora? entidad = null;
        if (cmd.EntidadId is not null)
        {
            entidad = await _repo.EntidadActivaPorIdAsync(cmd.EntidadId.Value, ct);
            if (entidad is null)
                return Resultado.Falla<Guid>(Fallas.Validacion, "Esa entidad no existe o ya no está disponible.");
        }
        else if (!string.IsNullOrEmpty(entidadNombre))
        {
            // Si ya existe con ese nombre (del catálogo o escrita por otro antes), se reutiliza:
            // crear otra fila repetida ensuciaría el catálogo y chocaría con el índice único.
            entidad = await _repo.EntidadPorNombreAsync(entidadNombre, ct);
            if (entidad is null)
            {
                // Paso 4 de CU-05: se marca como no catalogada para que alguien la revise.
                entidad = new EntidadCertificadora { Nombre = entidadNombre, Catalogada = false, Activa = true };
                _repo.AgregarEntidad(entidad);
            }
            else if (!entidad.Activa)
                return Resultado.Falla<Guid>(Fallas.Validacion, "Esa entidad no existe o ya no está disponible.");
        }

        var credencial = new Credencial
        {
            PerfilPrestadorId = cmd.PerfilPrestadorId,
            EntidadCertificadora = entidad,
            Tipo = cmd.Tipo,
            Nombre = nombre,
            ObtenidaEn = cmd.ObtenidaEn,
            VenceEn = cmd.VenceEn,
            Estado = EstadoCredencial.Borrador,
            CreadaEn = _reloj.Ahora,
        };
        _repo.Agregar(credencial);

        // RN-S2-08: quién, qué y cuándo. Sin el contenido de nada.
        _auditor.Registrar(Auditar.Entrada(
            "crear_credencial", "credencial", credencial.Id, _reloj.Ahora,
            contexto: new { tipo = cmd.Tipo.ToString(), entidadNueva = entidad is { Catalogada: false } }));

        await _trabajo.GuardarAsync(ct);
        return Resultado.Ok(credencial.Id);
    }

    // ------------------------------------------------------------- API-28

    public async Task<Resultado> EliminarAsync(
        Guid perfilPrestadorId, Guid credencialId, CancellationToken ct = default)
    {
        var credencial = await _repo.PorIdAsync(credencialId, ct);

        // Mismo mensaje para "no existe" y "es de otra persona".
        if (credencial is null || credencial.PerfilPrestadorId != perfilPrestadorId)
            return Resultado.Falla(Fallas.NoEncontrado, "No encontramos esa credencial.");

        var teniaSello = credencial.Estado == EstadoCredencial.Verificada;
        var archivos = credencial.Documentos.Select(d => d.ClaveAlmacenamiento).ToList();
        _repo.Eliminar(credencial);

        // 4F.4: queda constancia de que existió y fue eliminada, sin su contenido.
        _auditor.Registrar(Auditar.Entrada(
            "eliminar_credencial", "credencial", credencial.Id, _reloj.Ahora,
            contexto: new { teniaSello, documentos = archivos.Count }));

        await _trabajo.GuardarAsync(ct);

        // 4F.3: los archivos se van DESPUÉS de que la base confirmó. Al revés, una base que
        // fallara dejaría filas apuntando a documentos borrados; así, lo peor es un huérfano.
        await BorrarArchivosAsync(archivos);
        return Resultado.Ok();
    }

    private async Task BorrarArchivosAsync(IEnumerable<string> claves)
    {
        if (_almacen is null) return;
        foreach (var clave in claves)
            await _almacen.BorrarAsync(clave, CancellationToken.None);
    }

    // ------------------------------------------------------------- API-26

    /// <summary>
    /// <para>
    /// Lo que NO hace, a sabiendas: el análisis antivirus de CU-05 paso 8 (RN-S2-05). El diseño lo
    /// apoyaba en Defender for Storage de Azure, que no se desplegó; con R2 no hay un equivalente
    /// incluido. Mientras Pablo no decida cómo, el documento queda <c>Disponible</c> al pasar las
    /// validaciones de aquí, y lo que reduce el riesgo es: el tipo real leído por dentro, las
    /// imágenes vueltas a codificar (lo guardado son píxeles nuestros), y que un PDF solo lo abre el
    /// Verificador, servido con <c>Content-Security-Policy: sandbox</c>.
    /// </para>
    /// </summary>
    public async Task<Resultado<MiDocumentoDto>> SubirDocumentoAsync(
        SubirDocumentoCmd cmd, CancellationToken ct = default)
    {
        var credencial = await _repo.PorIdAsync(cmd.CredencialId, ct);
        if (credencial is null || credencial.PerfilPrestadorId != cmd.PerfilPrestadorId)
            return Resultado.Falla<MiDocumentoDto>(Fallas.NoEncontrado, "No encontramos esa credencial.");

        // Cambiar el respaldo de una credencial con sello cambiaría lo que el Verificador aprobó.
        if (credencial.Estado is EstadoCredencial.Verificada)
            return Resultado.Falla<MiDocumentoDto>(Fallas.ReglaDeNegocio,
                "Esta credencial ya está verificada. Para cambiar sus documentos, crea una nueva.");

        if (credencial.Documentos.Count >= MaximoDocumentosPorCredencial)
            return Resultado.Falla<MiDocumentoDto>(Fallas.Conflicto,
                $"Máximo {MaximoDocumentosPorCredencial} documentos por credencial.");

        if (cmd.LargoDeclarado > TamanoMaximoDocumento)
            return Resultado.Falla<MiDocumentoDto>(Fallas.ArchivoDemasiadoGrande,
                $"El archivo pesa {LecturaAcotada.Megas(cmd.LargoDeclarado)} y el máximo es {LecturaAcotada.Megas(TamanoMaximoDocumento)}.");

        var bytes = await LecturaAcotada.LeerAsync(cmd.Contenido, TamanoMaximoDocumento, ct);
        if (bytes is null)
            return Resultado.Falla<MiDocumentoDto>(Fallas.ArchivoDemasiadoGrande,
                $"El archivo pesa más de {LecturaAcotada.Megas(TamanoMaximoDocumento)}, que es el máximo.");
        if (bytes.Length == 0)
            return Resultado.Falla<MiDocumentoDto>(Fallas.Validacion, "No llegó ningún archivo.");

        var tipo = TiposDeArchivo.Detectar(bytes.AsSpan(0, Math.Min(bytes.Length, TiposDeArchivo.BytesParaDetectar)));
        if (tipo is not (TipoArchivo.Pdf or TipoArchivo.Jpeg or TipoArchivo.Png))
            return Resultado.Falla<MiDocumentoDto>(Fallas.TipoDeArchivoNoAceptado, "Usa PDF, JPG o PNG.");

        if (_almacen is null || _imagenes is null)
            return Resultado.Falla<MiDocumentoDto>(Fallas.AlmacenamientoNoDisponible,
                "Subir documentos todavía no está disponible.");

        var aGuardar = bytes;
        if (tipo is TipoArchivo.Jpeg or TipoArchivo.Png)
        {
            using var imagen = new MemoryStream(bytes, writable: false);
            var limpia = await _imagenes.LimpiarImagenAsync(imagen, tipo.Value, ct);
            if (!limpia.Exito || limpia.Valor is null)
                return Resultado.Falla<MiDocumentoDto>(limpia.Codigo ?? Fallas.TipoDeArchivoNoAceptado,
                    limpia.Mensaje ?? "No pudimos leer esa imagen. Prueba con otra.");
            aGuardar = limpia.Valor;
        }

        var clave = TiposDeArchivo.NuevaClave(TiposDeArchivo.CarpetaDocumentos, tipo.Value);
        using (var salida = new MemoryStream(aGuardar, writable: false))
        {
            var guardado = await _almacen.GuardarAsync(clave, salida, TiposDeArchivo.Mime(tipo.Value), ct);
            if (!guardado.Exito)
                return Resultado.Falla<MiDocumentoDto>(Fallas.AlmacenamientoNoDisponible,
                    "No pudimos guardar el archivo. Intenta de nuevo.");
        }

        var documento = new DocumentoCredencial
        {
            CredencialId = credencial.Id,
            ClaveAlmacenamiento = clave,
            NombreOriginal = TiposDeArchivo.NombreParaMostrar(cmd.NombreOriginal, tipo.Value),
            TipoMime = TiposDeArchivo.Mime(tipo.Value),
            TamanoBytes = aGuardar.Length,
            Estado = EstadoDocumento.Disponible,
            SubidoEn = _reloj.Ahora,
        };
        _repo.AgregarDocumento(documento);

        // RN-S2-08: quién, qué y cuándo. Sin el nombre del archivo ni su contenido.
        _auditor.Registrar(Auditar.Entrada(
            "subir_documento", "credencial", credencial.Id, _reloj.Ahora,
            contexto: new { documento = documento.Id, tipo = tipo.Value.ToString(), bytes = aGuardar.Length }));

        try
        {
            await _trabajo.GuardarAsync(ct);
        }
        catch
        {
            // E-507 y «el almacenamiento no guarda huérfanos»: sin fila, tampoco archivo.
            await _almacen.BorrarAsync(clave, CancellationToken.None);
            throw;
        }

        return Resultado.Ok(ADto(documento));
    }

    public async Task<Resultado> QuitarDocumentoAsync(
        Guid perfilPrestadorId, Guid credencialId, Guid documentoId, CancellationToken ct = default)
    {
        var credencial = await _repo.PorIdAsync(credencialId, ct);
        if (credencial is null || credencial.PerfilPrestadorId != perfilPrestadorId)
            return Resultado.Falla(Fallas.NoEncontrado, "No encontramos ese documento.");

        var documento = credencial.Documentos.FirstOrDefault(d => d.Id == documentoId);
        if (documento is null)
            return Resultado.Falla(Fallas.NoEncontrado, "No encontramos ese documento.");

        if (credencial.Estado is EstadoCredencial.Verificada)
            return Resultado.Falla(Fallas.ReglaDeNegocio,
                "Esta credencial ya está verificada: su respaldo no se puede quitar.");

        var clave = documento.ClaveAlmacenamiento;
        _repo.QuitarDocumento(documento);
        _auditor.Registrar(Auditar.Entrada(
            "quitar_documento", "credencial", credencial.Id, _reloj.Ahora,
            contexto: new { documento = documento.Id }));
        await _trabajo.GuardarAsync(ct);

        await BorrarArchivosAsync(new[] { clave });
        return Resultado.Ok();
    }

    public async Task<Resultado<DocumentoParaVer>> AbrirDocumentoAsync(
        Guid perfilPrestadorId, Guid credencialId, Guid documentoId, CancellationToken ct = default)
    {
        var credencial = await _repo.PorIdAsync(credencialId, ct);
        var documento = credencial is not null && credencial.PerfilPrestadorId == perfilPrestadorId
            ? credencial.Documentos.FirstOrDefault(d => d.Id == documentoId)
            : null;
        if (documento is null)
            return Resultado.Falla<DocumentoParaVer>(Fallas.NoEncontrado, "No encontramos ese documento.");

        return await AbrirAsync(_almacen, documento, ct);
    }

    /// <summary>Compartido con el Verificador: abrir el archivo de un documento ya autorizado.</summary>
    internal static async Task<Resultado<DocumentoParaVer>> AbrirAsync(
        IAlmacenDeArchivos? almacen, DocumentoCredencial documento, CancellationToken ct)
    {
        if (almacen is null)
            return Resultado.Falla<DocumentoParaVer>(Fallas.AlmacenamientoNoDisponible,
                "Los documentos no están disponibles en este momento.");

        var archivo = await almacen.AbrirAsync(documento.ClaveAlmacenamiento, ct);
        if (archivo is null)
            return Resultado.Falla<DocumentoParaVer>(Fallas.NoEncontrado, "No encontramos ese documento.");

        return Resultado.Ok(new DocumentoParaVer(archivo, documento.NombreOriginal));
    }

    // ------------------------------------------------------------- API-27

    public async Task<Resultado> EnviarARevisionAsync(
        Guid perfilPrestadorId, Guid credencialId, CancellationToken ct = default)
    {
        var credencial = await _repo.PorIdAsync(credencialId, ct);

        // Mismo mensaje para "no existe" y "es de otra persona", como en API-28.
        if (credencial is null || credencial.PerfilPrestadorId != perfilPrestadorId)
            return Resultado.Falla(Fallas.NoEncontrado, "No encontramos esa credencial.");

        var estabaEsperando = credencial.Estado == EstadoCredencial.EnRevision;
        var r = credencial.EnviarARevision(_opciones.PermitirSinDocumentos);
        if (!r.Exito) return r;

        _auditor.Registrar(Auditar.Entrada(
            estabaEsperando ? "responder_antecedentes" : "enviar_credencial_a_revision",
            "credencial", credencial.Id, _reloj.Ahora,
            contexto: new { sinDocumentos = !credencial.TieneRespaldo() }));

        await _trabajo.GuardarAsync(ct);
        return Resultado.Ok();
    }

    // ------------------------------------------------------- lo que ve el publico

    public async Task<IReadOnlyList<CredencialPublicaDto>> CredencialesPublicasAsync(
        Guid perfilPrestadorId, CancellationToken ct = default)
    {
        var credenciales = await _repo.PublicasDePrestadorAsync(perfilPrestadorId, ct);

        // El repositorio ya sacó los borradores; esta guarda es por si algún día otro lo
        // implementa distinto: un borrador es de su dueño y de nadie más.
        return credenciales
            .Where(c => c.Estado != EstadoCredencial.Borrador)
            .OrderByDescending(c => c.Estado == EstadoCredencial.Verificada)
            .ThenByDescending(c => c.ObtenidaEn)
            .ThenBy(c => c.Nombre, StringComparer.Create(new System.Globalization.CultureInfo("es-CL"), true))
            .Select(c => new CredencialPublicaDto(
                c.Tipo, c.Nombre, c.EntidadCertificadora?.Nombre, c.ObtenidaEn,
                c.EtiquetaPublica(), c.Estado == EstadoCredencial.Verificada))
            .ToList();
    }

    public Task<bool> TieneVerificadasAsync(Guid perfilPrestadorId, CancellationToken ct = default) =>
        _repo.TieneVerificadaAsync(perfilPrestadorId, ct);
}
