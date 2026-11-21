using Zass.Aplicacion.Comun;
using Zass.Aplicacion.Puertos;
using Zass.Aplicacion.Suscripciones;
using Zass.Dominio.Comun;
using Zass.Dominio.Identidad;

namespace Zass.Aplicacion.Identidad;

/// <summary>
/// Interfaz para las fotos de trabajos realizados. Permite mocking en pruebas.
/// </summary>
public interface IServicioFotosDeTrabajo
{
    /// <summary>Sube una foto de trabajo. Solo el dueno del perfil.</summary>
    Task<Resultado<string>> SubirAsync(Guid usuarioId, Stream contenido, long largoDeclarado, CancellationToken ct = default);

    /// <summary>Borra una foto propia. Solo el dueno del perfil.</summary>
    Task<Resultado> BorrarAsync(Guid usuarioId, Guid fotoId, CancellationToken ct = default);

    /// <summary>Obtiene las fotos de trabajo de un perfil para la ficha publica (ordenadas por Orden).</summary>
    Task<IReadOnlyList<FotoTrabajoPublicaDto>> ObtenerParaFichaAsync(Guid perfilPrestadorId, CancellationToken ct = default);
}

/// <summary>
/// Fotos de trabajos realizados que sube el prestador para su ficha publica.
/// Son PUBLICAS como la foto de perfil (no como los documentos de credenciales).
/// El tope de cuantas puede tener lo dice su plan vigente (Plan.MaxFotosTrabajos).
/// </summary>
public sealed class ServicioFotosDeTrabajo : IServicioFotosDeTrabajo
{
    /// <summary>Lo maximo que se acepta subir. Lo que se GUARDA se redimensiona a 1200 px de lado mayor.</summary>
    public const long TamanoMaximo = 10 * 1024 * 1024;

    /// <summary>El lado mayor de la foto guardada, en pixeles. WebP, sin metadatos.</summary>
    public const int LadoMaximo = 1200;

    /// <summary>De donde la sirve la API. Ver EndpointsArchivos.</summary>
    public const string RutaPublica = "/api/archivos/fotos/";

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

    /// <summary>La clave de almacenamiento que corresponde a una UrlFoto nuestra, o nulo si no es nuestra.</summary>
    public static string? ClaveDe(string? urlFoto)
    {
        if (urlFoto is null || !urlFoto.StartsWith(RutaPublica, StringComparison.Ordinal)) return null;
        var clave = TiposDeArchivo.CarpetaFotos + "/" + urlFoto[RutaPublica.Length..];
        return TiposDeArchivo.EsClaveValida(clave) ? clave : null;
    }

    /// <summary>
    /// Sube una foto de trabajo. Solo el dueno del perfil.
    /// Procesa como la foto de perfil pero en un tamano mas grande (lado mayor 1200 px, WebP, sin metadatos).
    /// Rechaza si ya tiene MaxFotosTrabajos de su plan VIGENTE.
    /// </summary>
    public async Task<Resultado<string>> SubirAsync(
        Guid usuarioId, Stream contenido, long largoDeclarado, CancellationToken ct = default)
    {
        var perfil = await _perfiles.PrestadorDeUsuarioAsync(usuarioId, ct);
        if (perfil is null)
            return Resultado.Falla<string>(Fallas.NoEncontrado, "Esta cuenta no tiene perfil de prestador.");

        // Verificar el limite del plan VIGENTE
        var maxFotos = await _suscripciones.LimiteDeFotosDeTrabajoAsync(perfil.Id, ct);
        if (maxFotos == 0)
            return Resultado.Falla<string>(Fallas.ReglaDeNegocio,
                "Tu plan no permite subir fotos de trabajos. El plan Profesional permite 12.");

        var actuales = await _perfiles.ContarFotosDeTrabajoAsync(perfil.Id, ct);
        if (actuales >= maxFotos)
            return Resultado.Falla<string>(Fallas.ReglaDeNegocio,
                $"Ya tienes {maxFotos} fotos de trabajos (el maximo de tu plan). Borra alguna para subir otra.");

        if (largoDeclarado > TamanoMaximo)
            return Resultado.Falla<string>(Fallas.ArchivoDemasiadoGrande,
                $"La foto pesa {LecturaAcotada.Megas(largoDeclarado)} y el máximo es {LecturaAcotada.Megas(TamanoMaximo)}.");

        var bytes = await LecturaAcotada.LeerAsync(contenido, TamanoMaximo, ct);
        if (bytes is null)
            return Resultado.Falla<string>(Fallas.ArchivoDemasiadoGrande,
                $"La foto pesa más de {LecturaAcotada.Megas(TamanoMaximo)}, que es el máximo.");
        if (bytes.Length == 0)
            return Resultado.Falla<string>(Fallas.Validacion, "No llegó ninguna foto.");

        var tipo = TiposDeArchivo.Detectar(bytes.AsSpan(0, Math.Min(bytes.Length, TiposDeArchivo.BytesParaDetectar)));
        if (tipo is not (TipoArchivo.Jpeg or TipoArchivo.Png or TipoArchivo.Webp))
            return Resultado.Falla<string>(Fallas.TipoDeArchivoNoAceptado, "Usa una foto JPG, PNG o WebP.");

        using var entrada = new MemoryStream(bytes, writable: false);
        var preparada = await _imagenes.PrepararFotoTrabajoAsync(entrada, LadoMaximo, ct);
        if (!preparada.Exito || preparada.Valor is null)
            return Resultado.Falla<string>(preparada.Codigo ?? Fallas.TipoDeArchivoNoAceptado,
                preparada.Mensaje ?? "No pudimos leer esa foto. Prueba con otra.");

        var clave = TiposDeArchivo.NuevaClave(TiposDeArchivo.CarpetaFotos, TipoArchivo.Webp);
        var mime = TiposDeArchivo.Mime(TipoArchivo.Webp);

        using (var salida = new MemoryStream(preparada.Valor, writable: false))
        {
            var guardada = await _almacen.GuardarAsync(clave, salida, mime, ct);
            if (!guardada.Exito)
                return Resultado.Falla<string>(Fallas.AlmacenamientoNoDisponible,
                    guardada.Mensaje ?? "No pudimos guardar la foto. Intenta de nuevo.");
        }

        // Obtener el siguiente orden
        var siguienteOrden = await _perfiles.ObtenerSiguienteOrdenFotoTrabajoAsync(perfil.Id, ct);

        var foto = new FotoTrabajo
        {
            PerfilPrestadorId = perfil.Id,
            ClaveAlmacen = clave,
            Orden = siguienteOrden,
            CreadaEn = _reloj.Ahora
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
            await _almacen.BorrarAsync(clave, CancellationToken.None);
            throw;
        }

        return Resultado.Ok(RutaPublica + clave[(TiposDeArchivo.CarpetaFotos.Length + 1)..]);
    }

    /// <summary>Borra una foto propia. Solo el dueno del perfil.</summary>
    public async Task<Resultado> BorrarAsync(Guid usuarioId, Guid fotoId, CancellationToken ct = default)
    {
        var perfil = await _perfiles.PrestadorDeUsuarioAsync(usuarioId, ct);
        if (perfil is null)
            return Resultado.Falla(Fallas.NoEncontrado, "Esta cuenta no tiene perfil de prestador.");

        var foto = await _perfiles.FotoTrabajoPorIdAsync(fotoId, ct);
        if (foto is null)
            return Resultado.Falla(Fallas.NoEncontrado, "Esa foto no existe.");

        if (foto.PerfilPrestadorId != perfil.Id)
            return Resultado.Falla(Fallas.NoEncontrado, "No puedes borrar la foto de otro prestador.");

        var clave = foto.ClaveAlmacen;
        _perfiles.EliminarFotoTrabajo(foto);

        _auditor.Registrar(Auditar.Entrada(
            "borrar_foto_trabajo", "foto_trabajo", foto.Id, _reloj.Ahora, actorId: usuarioId));

        await _unidad.GuardarAsync(ct);

        await _almacen.BorrarAsync(clave, CancellationToken.None);
        return Resultado.Ok();
    }

    /// <summary>Obtiene las fotos de trabajo de un perfil para la ficha publica (ordenadas por Orden).</summary>
    public async Task<IReadOnlyList<FotoTrabajoPublicaDto>> ObtenerParaFichaAsync(Guid perfilPrestadorId, CancellationToken ct = default)
    {
        return await _perfiles.FotosDeTrabajoParaFichaAsync(perfilPrestadorId, ct);
    }
}