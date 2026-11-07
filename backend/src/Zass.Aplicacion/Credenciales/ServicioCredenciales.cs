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

    private readonly IRepositorioCredenciales _repo;
    private readonly IUnidadDeTrabajo _trabajo;
    private readonly IRelojSistema _reloj;
    private readonly IAuditor _auditor;

    public ServicioCredenciales(
        IRepositorioCredenciales repo, IUnidadDeTrabajo trabajo,
        IRelojSistema reloj, IAuditor auditor)
    {
        _repo = repo;
        _trabajo = trabajo;
        _reloj = reloj;
        _auditor = auditor;
    }

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
            c.TieneRespaldo(), c.Documentos.Count)).ToList();
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
        _repo.Eliminar(credencial);

        // 4F.4: queda constancia de que existió y fue eliminada, sin su contenido.
        _auditor.Registrar(Auditar.Entrada(
            "eliminar_credencial", "credencial", credencial.Id, _reloj.Ahora,
            contexto: new { teniaSello }));

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
