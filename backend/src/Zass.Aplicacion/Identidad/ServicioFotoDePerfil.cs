using Zass.Aplicacion.Comun;
using Zass.Aplicacion.Puertos;
using Zass.Dominio.Comun;

namespace Zass.Aplicacion.Identidad;

/// <summary>
/// JQ-9. La foto del perfil del prestador: subirla, reemplazarla y quitarla.
///
/// <para>
/// La foto es PUBLICA (sale en la ficha y en la busqueda), asi que lo que importa aqui no es
/// esconderla sino que lo publicado sea solo una imagen: se lee el tipo real por dentro, se
/// redimensiona en el servidor y se vuelve a codificar sin metadatos. El GPS que trae una foto de
/// celular no llega nunca al almacenamiento.
/// </para>
///
/// <para>
/// El dueño se decide con la sesion (el usuario), nunca con un id que venga en la peticion, igual
/// que en API-19.
/// </para>
/// </summary>
public sealed class ServicioFotoDePerfil
{
    /// <summary>Lo maximo que se acepta subir. Lo que se GUARDA es mucho menos: ver <see cref="LadoMaximo"/>.</summary>
    public const long TamanoMaximo = 10 * 1024 * 1024;

    /// <summary>El lado mayor de la foto guardada, en pixeles. Sobra para una ficha y pesa poco.</summary>
    public const int LadoMaximo = 800;

    /// <summary>De donde la sirve la API. Ver EndpointsArchivos.</summary>
    public const string RutaPublica = "/api/archivos/fotos/";

    private readonly IRepositorioPerfiles _perfiles;
    private readonly IUnidadDeTrabajo _unidad;
    private readonly IAlmacenDeArchivos _almacen;
    private readonly IProcesadorDeImagenes _imagenes;
    private readonly IAuditor _auditor;
    private readonly IRelojSistema _reloj;

    public ServicioFotoDePerfil(
        IRepositorioPerfiles perfiles, IUnidadDeTrabajo unidad, IAlmacenDeArchivos almacen,
        IProcesadorDeImagenes imagenes, IAuditor auditor, IRelojSistema reloj)
    {
        _perfiles = perfiles;
        _unidad = unidad;
        _almacen = almacen;
        _imagenes = imagenes;
        _auditor = auditor;
        _reloj = reloj;
    }

    /// <summary>La clave de almacenamiento que corresponde a una UrlFoto nuestra, o nulo si no es nuestra.</summary>
    public static string? ClaveDe(string? urlFoto)
    {
        if (urlFoto is null || !urlFoto.StartsWith(RutaPublica, StringComparison.Ordinal)) return null;
        var clave = TiposDeArchivo.CarpetaFotos + "/" + urlFoto[RutaPublica.Length..];
        return TiposDeArchivo.EsClaveValida(clave) ? clave : null;
    }

    public async Task<Resultado<string>> SubirAsync(
        Guid usuarioId, Stream contenido, long largoDeclarado, CancellationToken ct = default)
    {
        var perfil = await _perfiles.PrestadorDeUsuarioAsync(usuarioId, ct);
        if (perfil is null)
            return Resultado.Falla<string>(Fallas.NoEncontrado, "Esta cuenta no tiene perfil de prestador.");

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
        var preparada = await _imagenes.PrepararFotoAsync(entrada, LadoMaximo, ct);
        if (!preparada.Exito || preparada.Valor is null)
            return Resultado.Falla<string>(preparada.Codigo ?? Fallas.TipoDeArchivoNoAceptado,
                preparada.Mensaje ?? "No pudimos leer esa foto. Prueba con otra.");

        var clave = TiposDeArchivo.NuevaClave(TiposDeArchivo.CarpetaFotos, TipoArchivo.Jpeg);
        using (var salida = new MemoryStream(preparada.Valor, writable: false))
        {
            var guardada = await _almacen.GuardarAsync(clave, salida, TiposDeArchivo.Mime(TipoArchivo.Jpeg), ct);
            if (!guardada.Exito)
                return Resultado.Falla<string>(Fallas.AlmacenamientoNoDisponible,
                    guardada.Mensaje ?? "No pudimos guardar la foto. Intenta de nuevo.");
        }

        var anterior = ClaveDe(perfil.UrlFoto);

        // No se sube la Version (CC-02): cambiar la foto no tiene que invalidar el formulario de
        // datos que la persona pueda tener abierto en otra pestaña.
        perfil.UrlFoto = RutaPublica + clave[(TiposDeArchivo.CarpetaFotos.Length + 1)..];
        perfil.ActualizadoEn = _reloj.Ahora;

        _auditor.Registrar(Auditar.Entrada(
            anterior is null ? "subir_foto" : "reemplazar_foto", "perfil_prestador", perfil.Id, _reloj.Ahora,
            actorId: usuarioId, contexto: new { bytes = preparada.Valor.Length }));

        try
        {
            await _unidad.GuardarAsync(ct);
        }
        catch
        {
            // CU-05 «el almacenamiento no guarda huérfanos»: si la base no la registró, se borra.
            await _almacen.BorrarAsync(clave, CancellationToken.None);
            throw;
        }

        // La anterior se borra DESPUÉS de guardar: si se borrara antes y la base fallara, el
        // perfil apuntaría a una foto que ya no existe.
        if (anterior is not null) await _almacen.BorrarAsync(anterior, CancellationToken.None);

        return Resultado.Ok(perfil.UrlFoto);
    }

    public async Task<Resultado> QuitarAsync(Guid usuarioId, CancellationToken ct = default)
    {
        var perfil = await _perfiles.PrestadorDeUsuarioAsync(usuarioId, ct);
        if (perfil is null)
            return Resultado.Falla(Fallas.NoEncontrado, "Esta cuenta no tiene perfil de prestador.");

        var anterior = ClaveDe(perfil.UrlFoto);
        if (perfil.UrlFoto is null) return Resultado.Ok();

        perfil.UrlFoto = null;
        perfil.ActualizadoEn = _reloj.Ahora;
        _auditor.Registrar(Auditar.Entrada(
            "quitar_foto", "perfil_prestador", perfil.Id, _reloj.Ahora, actorId: usuarioId));
        await _unidad.GuardarAsync(ct);

        if (anterior is not null) await _almacen.BorrarAsync(anterior, CancellationToken.None);
        return Resultado.Ok();
    }
}
