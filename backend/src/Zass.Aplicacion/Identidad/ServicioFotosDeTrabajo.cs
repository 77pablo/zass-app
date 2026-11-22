using Zass.Aplicacion.Comun;
using Zass.Aplicacion.Puertos;
using Zass.Aplicacion.Suscripciones;
using Zass.Dominio.Comun;
using Zass.Dominio.Identidad;

namespace Zass.Aplicacion.Identidad;

/// <summary>Una foto de trabajo vista por su dueño: con el id, para poder ordenarla o borrarla.</summary>
public sealed record FotoTrabajoPropiaDto(Guid Id, string Url, int Orden);

/// <summary>
/// Las fotos de trabajos del prestador y cuántas le permite su plan vigente. El dueño ve TODAS las
/// suyas, aunque pasen del tope (si bajó de plan), para poder borrar las que sobran.
/// </summary>
public sealed record MisFotosDeTrabajoDto(int Maximo, IReadOnlyList<FotoTrabajoPropiaDto> Fotos);

/// <summary>Las fotos de trabajos realizados (T69). Separado en interfaz para la búsqueda y sus dobles.</summary>
public interface IServicioFotosDeTrabajo
{
    /// <summary>API-77. Sube una foto de trabajo. Solo el dueño del perfil, dentro del tope de su plan.</summary>
    Task<Resultado<FotoTrabajoPropiaDto>> SubirAsync(Guid usuarioId, Stream contenido, long largoDeclarado, CancellationToken ct = default);

    /// <summary>API-77-listar. Las fotos propias, en su orden, y el tope del plan vigente.</summary>
    Task<Resultado<MisFotosDeTrabajoDto>> MisFotosAsync(Guid usuarioId, CancellationToken ct = default);

    /// <summary>API-77-ordenar. Recibe TODOS los ids propios en el orden nuevo.</summary>
    Task<Resultado> OrdenarAsync(Guid usuarioId, IReadOnlyList<Guid> ids, CancellationToken ct = default);

    /// <summary>API-77-quitar. Borra una foto propia y su archivo.</summary>
    Task<Resultado> BorrarAsync(Guid usuarioId, Guid fotoId, CancellationToken ct = default);

    /// <summary>
    /// Las fotos para la ficha pública: en su orden y SOLO hasta el tope del plan vigente. Quien baja
    /// del Profesional al Esencial deja de mostrarlas, aunque sigan guardadas.
    /// </summary>
    Task<IReadOnlyList<FotoTrabajoPublicaDto>> ObtenerParaFichaAsync(Guid perfilPrestadorId, CancellationToken ct = default);
}

/// <summary>
/// Fotos de trabajos realizados que sube el prestador para su ficha pública.
/// Son PÚBLICAS como la foto de perfil (no como los documentos de credenciales).
/// El tope lo dice su plan vigente (<c>Plan.MaxFotosTrabajos</c>): Esencial 0, Profesional y Fundador 12.
///
/// <para>
/// El tope se comprueba contando antes de insertar. Dos subidas simultáneas del mismo prestador
/// podrían dejarlo con una foto de más; es su propia cuenta y la ficha igual muestra solo hasta el
/// tope (<see cref="ObtenerParaFichaAsync"/>), así que no se paga un bloqueo por eso.
/// </para>
/// </summary>
public sealed class ServicioFotosDeTrabajo : IServicioFotosDeTrabajo
{
    /// <summary>Lo máximo que se acepta subir. Lo que se GUARDA se redimensiona a 1200 px de lado mayor.</summary>
    public const long TamanoMaximo = 10 * 1024 * 1024;

    /// <summary>El lado mayor de la foto guardada, en píxeles. WebP, sin metadatos.</summary>
    public const int LadoMaximo = 1200;

    private readonly IRepositorioPerfiles _perfiles;
    private readonly IUnidadDeTrabajo _unidad;
    private readonly IAlmacenDeArchivos _almacen;
    private readonly IProcesadorDeImagenes _imagenes;
    private readonly IAuditor _auditor;
    private readonly IRelojSistema _reloj;
    private readonly IServicioSuscripciones _suscripciones;

    public ServicioFotosDeTrabajo(
        IRepositorioPerfiles perfiles, IUnidadDeTrabajo unidad, IAlmacenDeArchivos almacen,
        IProcesadorDeImagenes imagenes, IAuditor auditor, IRelojSistema reloj,
        IServicioSuscripciones suscripciones)
    {
        _perfiles = perfiles;
        _unidad = unidad;
        _almacen = almacen;
        _imagenes = imagenes;
        _auditor = auditor;
        _reloj = reloj;
        _suscripciones = suscripciones;
    }

    /// <summary>La URL pública de una clave «fotos/abc.webp».</summary>
    public static string UrlDe(string clave) =>
        TiposDeArchivo.RutaPublica + clave[(TiposDeArchivo.CarpetaFotos.Length + 1)..];

    private static FotoTrabajoPropiaDto Propia(FotoTrabajo f) => new(f.Id, UrlDe(f.ClaveAlmacen), f.Orden);

    public async Task<Resultado<FotoTrabajoPropiaDto>> SubirAsync(
        Guid usuarioId, Stream contenido, long largoDeclarado, CancellationToken ct = default)
    {
        var perfil = await _perfiles.PrestadorDeUsuarioAsync(usuarioId, ct);
        if (perfil is null)
            return Resultado.Falla<FotoTrabajoPropiaDto>(Fallas.NoEncontrado, "Esta cuenta no tiene perfil de prestador.");

        var maximo = await _suscripciones.LimiteDeFotosDeTrabajoAsync(perfil.Id, ct);
        if (maximo <= 0)
            return Resultado.Falla<FotoTrabajoPropiaDto>(Fallas.ReglaDeNegocio,
                "Tu plan no incluye fotos de tus trabajos. El plan Profesional sí las incluye.");

        var actuales = await _perfiles.ContarFotosDeTrabajoAsync(perfil.Id, ct);
        if (actuales >= maximo)
            return Resultado.Falla<FotoTrabajoPropiaDto>(Fallas.ReglaDeNegocio,
                $"Ya tienes {maximo} fotos de trabajos, el máximo de tu plan. Quita una para subir otra.");

        if (largoDeclarado > TamanoMaximo)
            return Resultado.Falla<FotoTrabajoPropiaDto>(Fallas.ArchivoDemasiadoGrande,
                $"La foto pesa {LecturaAcotada.Megas(largoDeclarado)} y el máximo es {LecturaAcotada.Megas(TamanoMaximo)}.");

        var bytes = await LecturaAcotada.LeerAsync(contenido, TamanoMaximo, ct);
        if (bytes is null)
            return Resultado.Falla<FotoTrabajoPropiaDto>(Fallas.ArchivoDemasiadoGrande,
                $"La foto pesa más de {LecturaAcotada.Megas(TamanoMaximo)}, que es el máximo.");
        if (bytes.Length == 0)
            return Resultado.Falla<FotoTrabajoPropiaDto>(Fallas.Validacion, "No llegó ninguna foto.");

        var tipo = TiposDeArchivo.Detectar(bytes.AsSpan(0, Math.Min(bytes.Length, TiposDeArchivo.BytesParaDetectar)));
        if (tipo is not (TipoArchivo.Jpeg or TipoArchivo.Png or TipoArchivo.Webp))
            return Resultado.Falla<FotoTrabajoPropiaDto>(Fallas.TipoDeArchivoNoAceptado, "Usa una foto JPG, PNG o WebP.");

        using var entrada = new MemoryStream(bytes, writable: false);
        var preparada = await _imagenes.PrepararFotoTrabajoAsync(entrada, LadoMaximo, ct);
        if (!preparada.Exito || preparada.Valor is null)
            return Resultado.Falla<FotoTrabajoPropiaDto>(preparada.Codigo ?? Fallas.TipoDeArchivoNoAceptado,
                preparada.Mensaje ?? "No pudimos leer esa foto. Prueba con otra.");

        var clave = TiposDeArchivo.NuevaClave(TiposDeArchivo.CarpetaFotos, TipoArchivo.Webp);
        using (var salida = new MemoryStream(preparada.Valor, writable: false))
        {
            var guardada = await _almacen.GuardarAsync(clave, salida, TiposDeArchivo.Mime(TipoArchivo.Webp), ct);
            if (!guardada.Exito)
                return Resultado.Falla<FotoTrabajoPropiaDto>(Fallas.AlmacenamientoNoDisponible,
                    guardada.Mensaje ?? "No pudimos guardar la foto. Intenta de nuevo.");
        }

        var foto = new FotoTrabajo
        {
            PerfilPrestadorId = perfil.Id,
            ClaveAlmacen = clave,
            Orden = await _perfiles.ObtenerSiguienteOrdenFotoTrabajoAsync(perfil.Id, ct),
            CreadaEn = _reloj.Ahora,
        };
        _perfiles.AgregarFotoTrabajo(foto);

        _auditor.Registrar(Auditar.Entrada(
            "subir_foto_trabajo", "foto_trabajo", foto.Id, _reloj.Ahora,
            actorId: usuarioId, contexto: new { bytes = preparada.Valor.Length }));

        try
        {
            await _unidad.GuardarAsync(ct);
        }
        catch
        {
            // La base no la guardó: el archivo quedaría huérfano.
            await _almacen.BorrarAsync(clave, CancellationToken.None);
            throw;
        }

        return Resultado.Ok(Propia(foto));
    }

    public async Task<Resultado<MisFotosDeTrabajoDto>> MisFotosAsync(Guid usuarioId, CancellationToken ct = default)
    {
        var perfil = await _perfiles.PrestadorDeUsuarioAsync(usuarioId, ct);
        if (perfil is null)
            return Resultado.Falla<MisFotosDeTrabajoDto>(Fallas.NoEncontrado, "Esta cuenta no tiene perfil de prestador.");

        var maximo = await _suscripciones.LimiteDeFotosDeTrabajoAsync(perfil.Id, ct);
        var fotos = await _perfiles.FotosDeTrabajoDePerfilAsync(perfil.Id, ct);
        return Resultado.Ok(new MisFotosDeTrabajoDto(Math.Max(maximo, 0), fotos.Select(Propia).ToList()));
    }

    public async Task<Resultado> OrdenarAsync(Guid usuarioId, IReadOnlyList<Guid> ids, CancellationToken ct = default)
    {
        var perfil = await _perfiles.PrestadorDeUsuarioAsync(usuarioId, ct);
        if (perfil is null)
            return Resultado.Falla(Fallas.NoEncontrado, "Esta cuenta no tiene perfil de prestador.");

        var fotos = await _perfiles.FotosDeTrabajoDePerfilAsync(perfil.Id, ct);

        // Tienen que venir TODAS las propias, una vez cada una, y ninguna ajena: así un id de otro
        // prestador no puede colarse y una lista a medias no deja dos fotos con el mismo orden.
        if (ids.Count != fotos.Count || ids.Distinct().Count() != ids.Count
            || !ids.All(id => fotos.Any(f => f.Id == id)))
            return Resultado.Falla(Fallas.Validacion,
                "El orden tiene que traer todas tus fotos de trabajos, una vez cada una.");

        for (var i = 0; i < ids.Count; i++)
            fotos.First(f => f.Id == ids[i]).Orden = i;

        _auditor.Registrar(Auditar.Entrada(
            "ordenar_fotos_trabajo", "perfil_prestador", perfil.Id, _reloj.Ahora, actorId: usuarioId));
        await _unidad.GuardarAsync(ct);
        return Resultado.Ok();
    }

    public async Task<Resultado> BorrarAsync(Guid usuarioId, Guid fotoId, CancellationToken ct = default)
    {
        var perfil = await _perfiles.PrestadorDeUsuarioAsync(usuarioId, ct);
        if (perfil is null)
            return Resultado.Falla(Fallas.NoEncontrado, "Esta cuenta no tiene perfil de prestador.");

        // Una foto ajena responde igual que una que no existe: no se confirma que exista.
        var foto = await _perfiles.FotoTrabajoPorIdAsync(fotoId, ct);
        if (foto is null || foto.PerfilPrestadorId != perfil.Id)
            return Resultado.Falla(Fallas.NoEncontrado, "Esa foto no existe.");

        var clave = foto.ClaveAlmacen;
        _perfiles.EliminarFotoTrabajo(foto);
        _auditor.Registrar(Auditar.Entrada(
            "borrar_foto_trabajo", "foto_trabajo", foto.Id, _reloj.Ahora, actorId: usuarioId));
        await _unidad.GuardarAsync(ct);

        // Después de que la base confirmó: si falla el borrado del archivo, queda un huérfano, no
        // una fila apuntando a un archivo que ya no está.
        await _almacen.BorrarAsync(clave, CancellationToken.None);
        return Resultado.Ok();
    }

    public async Task<IReadOnlyList<FotoTrabajoPublicaDto>> ObtenerParaFichaAsync(
        Guid perfilPrestadorId, CancellationToken ct = default)
    {
        var maximo = await _suscripciones.LimiteDeFotosDeTrabajoAsync(perfilPrestadorId, ct);
        if (maximo <= 0) return Array.Empty<FotoTrabajoPublicaDto>();

        var fotos = await _perfiles.FotosDeTrabajoParaFichaAsync(perfilPrestadorId, ct);
        return fotos.Take(maximo).ToList();
    }
}
