using Zass.Dominio.Comun;

namespace Zass.Aplicacion.Busqueda;

/// <summary>
/// MOD-05 - S3. Encuentra y ordena prestadores (CU-08).
///
/// Es el caso mas ejecutado del sistema y el unico donde una filtracion de datos de
/// ubicacion seria invisible hasta que alguien la explotara. De ahi la forma del DTO de
/// salida: ver <see cref="ResultadoDeBusqueda"/>.
///
/// No tiene tablas propias y no debe tenerlas nunca: solo lee.
/// </summary>
public interface IServicioBusqueda
{
    /// <summary>
    /// API-01. Una pagina de resultados publicos, ordenada de forma explicable.
    ///
    /// Los filtros duros del paso 5 de CU-08, todos dentro de la misma consulta:
    /// perfil `publicado` (RN-S3-01), rubro coincidente, cobertura que incluya la zona,
    /// y **suscripcion vigente** (RN-S3-09).
    /// </summary>
    Task<Resultado<PaginaDeBusqueda>> BuscarAsync(
        CriteriosDeBusqueda criterios, CancellationToken ct = default);
}

/// <summary>
/// Lo que entra a una busqueda. El punto y la zona son alternativas, no complementos:
///
/// <list type="bullet">
/// <item>Con <see cref="Latitud"/> y <see cref="Longitud"/> se ordena por distancia.</item>
/// <item>Con <see cref="ZonaId"/> se filtra por zona y NO se ordena por distancia
/// (RN-S3-07). Es el flujo 4A de CU-08 —el navegador niega la ubicacion— y tambien el
/// unico camino posible mientras no exista el geocodificador (N-08).</item>
/// </list>
/// </summary>
public sealed record CriteriosDeBusqueda(
    short RubroId,
    int? ServicioId = null,
    double? Latitud = null,
    double? Longitud = null,
    int? ZonaId = null,
    short RadioKm = LimitesDeBusqueda.RadioPorOmisionKm,
    bool SoloVerificados = false,
    decimal? CalificacionMinima = null,
    string? Cursor = null,
    Guid? QuienBuscaPerfilId = null)
{
    public bool TienePunto => Latitud is not null && Longitud is not null;
}

/// <summary>
/// Los numeros de CU-08, en un solo sitio. Estan aqui y no dentro de los registros porque
/// un valor por omision de parametro no puede referirse a una constante del propio tipo.
/// </summary>
public static class LimitesDeBusqueda
{
    /// <summary>RN-S3-04.</summary>
    public const short RadioPorOmisionKm = 10;
    public const short RadioMinimoKm = 1;
    public const short RadioMaximoKm = 50;

    /// <summary>RN-S3-05.</summary>
    public const int PorPagina = 20;
}

/// <summary>
/// Una pagina de resultados. <see cref="OrdenadoPorDistancia"/> en falso es la respuesta
/// degradada de RN-S3-07, y va con <see cref="Aviso"/> para que el cliente pueda decirlo:
/// "No pudimos ordenar por distancia. Mostramos por zona."
///
/// La paginacion es **por cursor y no por OFFSET** (CU-08, seccion 7): con OFFSET, la
/// pagina 50 obliga al motor a contar y descartar las 1.000 filas anteriores en cada
/// peticion.
/// </summary>
public sealed record PaginaDeBusqueda(
    IReadOnlyList<ResultadoDeBusqueda> Resultados,
    string? CursorSiguiente,
    bool OrdenadoPorDistancia,
    string? ZonaBuscada,
    string? Aviso);

/// <summary>
/// Un prestador, como lo ve cualquiera.
///
/// <b>Lo que este tipo NO tiene es la mitad de su razon de existir</b> (RN-S3-02, y la
/// seccion 3 de la secuencia PR-02): ni direccion, ni latitud, ni longitud, ni correo
/// privado, ni telefono no publico. Ninguna vista puede filtrar lo que el DTO no trae.
///
/// Y la distancia es <b>texto</b>, no un numero. Es deliberado: con la distancia exacta
/// desde tres puntos distintos cualquiera triangula la ubicacion con precision de metros.
/// Siendo texto —"a 3 km", "a menos de 1 km"— no hay forma de que un numero exacto se
/// escape por aqui ni hoy ni cuando alguien agregue un campo al vuelo.
/// </summary>
public sealed record ResultadoDeBusqueda(
    Guid PerfilId,
    string Slug,
    string NombrePublico,
    string? Descripcion,
    string? UrlFoto,
    short RubroId,
    string? ZonaPrincipal,
    string? Distancia,
    bool TieneCredencialVerificada,
    decimal? PromedioCalificacion,
    int TotalResenas,
    /// <summary>Flujo 4F de CU-08: un prestador que busca su rubro se ve, y sabe que es el.</summary>
    bool EsTuPerfil);
