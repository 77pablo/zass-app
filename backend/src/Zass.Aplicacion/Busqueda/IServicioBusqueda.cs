using Zass.Aplicacion.Credenciales;
using Zass.Aplicacion.Catalogo;
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

    /// <summary>
    /// CU-09 · API-05. La ficha publica de un prestador, ya compuesta: quien es, que ofrece,
    /// donde atiende y como contactarlo.
    ///
    /// Es de MOD-05 porque la tabla de modulos lo pone ahi (consume MOD-01, MOD-03 y
    /// MOD-04) y porque ninguno de los tres puede armarla solo: MOD-01 es dueno del perfil
    /// pero no de los servicios, y MOD-03 es dueno de los servicios pero no del perfil.
    ///
    /// Nulo si no existe o no esta publicada, y es el MISMO nulo para los dos casos:
    /// distinguirlos permitiria sondear quien se dio de alta sin publicar.
    /// </summary>
    Task<FichaPublicaDto?> FichaPublicaAsync(string slug, CancellationToken ct = default);

    /// <summary>
    /// API-62. Lo que la portada, los barrios y los oficios necesitan para mostrar una comunidad
    /// y no un buscador vacio: cuanta gente hay en cada barrio y en cada oficio, y quien se
    /// sumo recien. Cuenta SOLO a quien la busqueda mostraria.
    /// </summary>
    Task<ResumenDeComunidadDto> ResumenDeComunidadAsync(CancellationToken ct = default);

    /// <summary>
    /// API-63. La barra de busqueda: la persona escribe «abogado», «gasfiter» o un nombre, y se le
    /// devuelve gente de cualquier oficio que coincida, ordenada como pida (lo mas cerca, lo mas
    /// recomendado, lo mas barato o lo mas caro). No pide rubro ni zona: eso es API-01.
    /// </summary>
    Task<Resultado<PaginaDeTexto>> BuscarPorTextoAsync(
        CriteriosDeTextoDeBusqueda criterios, CancellationToken ct = default);
}

/// <summary>Como ordenar. Los nombres viajan como texto por HTTP.</summary>
public enum OrdenDeTexto { Recomendados, Cercania, PrecioAsc, PrecioDesc }

/// <summary>`Pagina` empieza en 1. Sin punto ni zona se busca en toda la ciudad.</summary>
public sealed record CriteriosDeTextoDeBusqueda(
    string? Texto,
    double? Latitud,
    double? Longitud,
    int? ZonaId,
    OrdenDeTexto Orden,
    int Pagina,
    bool SoloVerificados = false,
    decimal? CalificacionMinima = null,
    /// <summary>
    /// Hasta donde buscar, en kilometros (RN-S3-04). Solo significa algo con punto: sin saber
    /// desde donde se mide, no hay radio. Nulo = sin limite de distancia, que es lo que hacia la
    /// barra de busqueda desde que existe; el buscador por rubro (API-01) en cambio aplica 10 km
    /// por omision. Son dos busquedas distintas: la barra es nacional y de texto libre.
    /// </summary>
    short? RadioKm = null);

/// <summary>
/// Lo que ve cualquiera. Como en API-01: la distancia es TEXTO redondeado, nunca un numero, y no
/// hay direccion ni coordenadas (RN-S3-02 y RN-S3-03).
/// </summary>
public sealed record ResultadoDeTexto(
    Guid PerfilId,
    string Slug,
    string NombrePublico,
    string? Descripcion,
    string? UrlFoto,
    string? Rubro,
    string? ZonaPrincipal,
    string? Distancia,
    bool TieneCredencialVerificada,
    decimal? PromedioCalificacion,
    int TotalResenas,
    int? PrecioDesde);

public sealed record PaginaDeTexto(
    IReadOnlyList<ResultadoDeTexto> Resultados,
    int Total,
    int Pagina,
    bool HayMas,
    /// <summary>El orden que SE aplico: puede no ser el pedido (cercania sin ubicacion).</summary>
    OrdenDeTexto OrdenAplicado,
    /// <summary>Si las distancias del resultado salen de la ubicacion de la persona.</summary>
    bool ConUbicacion,
    string? Aviso,
    /// <summary>
    /// El radio que SE aplico, en km, o nulo si no se limito la distancia. Puede no ser el pedido:
    /// uno fuera de rango se corrige y se dice en <see cref="Aviso"/>. La pantalla lo necesita para
    /// marcar el boton correcto cuando la URL trae un valor raro.
    /// </summary>
    short? RadioAplicadoKm = null);

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

/// <summary>
/// La ficha publica completa. Igual que <see cref="ResultadoDeBusqueda"/>, lo que NO tiene
/// es la mitad de su razon de existir (RN-S3-02): ni direccion, ni latitud, ni longitud,
/// ni el correo de la cuenta, ni el identificador del usuario. El contacto que trae es el
/// que el prestador marco como publico, y nada mas.
/// </summary>
public sealed record FichaPublicaDto(
    Guid Id,
    string Slug,
    string NombrePublico,
    string? Descripcion,
    string? UrlFoto,
    RubroDto? Rubro,
    IReadOnlyList<ZonaPublicaDto> Zonas,
    IReadOnlyList<ServicioPublicoDto> Servicios,
    /// <summary>De MOD-02: solo "Verificada" o "Declarada por el usuario", nunca el estado interno ni archivos.</summary>
    IReadOnlyList<CredencialPublicaDto> Credenciales,
    /// <summary>Nulo con menos de 3 resenas (RN-S5-11): entonces solo se muestra el recuento.</summary>
    decimal? PromedioCalificacion,
    int TotalResenas,
    ContactoPublicoDto Contacto,
    DateTime? PublicadoEn,
    /// <summary>
    /// RN-S6-10. Falso cuando el prestador dejo de pagar: la ficha SIGUE viendose (por la
    /// tarjeta NFC, por un enlace guardado o por el buscador interno del navegador), pero
    /// sin boton de contactar ni de agendar.
    ///
    /// Va aqui y no se deja a la pantalla: esconder los botones en el navegador no basta,
    /// porque el telefono y el correo seguirian viajando en la respuesta. Por eso, cuando
    /// esto es falso, <see cref="Contacto"/> llega VACIO desde el servidor.
    ///
    /// Sin valor por omision a proposito: quien componga esta ficha tiene que decidirlo.
    /// </summary>
    bool SuscripcionVigente,
    /// <summary>
    /// Verdadero cuando quien pregunta NO tiene sesion y por eso <see cref="Contacto"/> viene vacio. Buscar y ver
    /// fichas es publico; contactar exige cuenta gratis. Lo pone el endpoint, que es quien conoce la sesion: el
    /// servicio de busqueda no sabe quien pregunta.
    /// </summary>
    bool ContactoRequiereCuenta = false);

/// <summary>Una zona que cubre. La principal es la que se muestra primero (RN-S3-08).</summary>
public sealed record ZonaPublicaDto(
    string Nombre, string Sector, bool EsPrincipal, string Comuna = "", string Region = "");

/// <summary>Lo que el prestador eligio mostrar. Cualquiera puede ser nulo.</summary>
public sealed record ContactoPublicoDto(
    string? Telefono, string? Whatsapp, string? Correo, string? SitioWeb);

/// <summary>
/// API-62. Datos AGREGADOS y publicos: contar personas por barrio no revela donde vive nadie,
/// porque la zona que cuenta es una zona que cada prestador eligio mostrar en su ficha.
/// </summary>
public sealed record ResumenDeComunidadDto(
    int Total,
    IReadOnlyList<PersonasPorZonaDto> PorZona,
    IReadOnlyList<PersonasPorRubroDto> PorRubro,
    IReadOnlyList<RecienLlegadoDto> RecienLlegados,
    /// <summary>Personas por region, contadas UNA vez cada una aunque cubra varias comunas.</summary>
    IReadOnlyList<PersonasPorRegionDto> PorRegion);

public sealed record PersonasPorRegionDto(string Region, int Personas);

public sealed record PersonasPorZonaDto(int ZonaId, int Personas);

public sealed record PersonasPorRubroDto(short RubroId, int Personas);

/// <summary>Quien se sumo recien. Lo justo para mostrar una tarjeta y llevar a su ficha.</summary>
public sealed record RecienLlegadoDto(
    string Slug, string NombrePublico, string? Rubro, string? Zona, bool Verificada);
