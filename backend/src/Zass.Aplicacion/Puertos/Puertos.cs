using Zass.Dominio.Catalogo;
using Zass.Dominio.Comun;
using Zass.Dominio.Identidad;
using Zass.Dominio.Suscripciones;

namespace Zass.Aplicacion.Puertos;

// Esta capa DECLARA lo que necesita; Zass.Infraestructura lo IMPLEMENTA.
// La flecha de dependencia apunta de infraestructura hacia aqui, nunca al revés.
// Consecuencia buscada: los casos de uso se prueban sin base de datos y sin red.

/// <summary>
/// El reloj, como puerto y no como DateTime.UtcNow. Permite probar "pasaron 24 h" sin
/// esperar 24 horas, y la mitad de las reglas de S4 dependen del tiempo.
/// </summary>
public interface IRelojSistema
{
    DateTime Ahora { get; }
}

/// <summary>RN-S1-03. La implementacion real es Argon2id: 19 MiB, 2 iteraciones, paralelismo 1 (DT-07).</summary>
public interface IHashContrasenas
{
    string Hash(string contrasena);
    bool Verifica(string contrasena, string hash);
}

/// <summary>
/// Secretos de un solo uso: verificacion de correo, recuperacion de contrasena, token de
/// refresco. Nunca se guarda el secreto, solo su hash, igual que TBL-04.
/// </summary>
public interface IGeneradorSecretos
{
    string NuevoToken();
    string HashToken(string token);
}

/// <summary>RN-S1-06. La direccion literal se guarda cifrada y no se indexa.</summary>
public interface ICifradoDeDatos
{
    byte[] Cifrar(string texto);
    string Descifrar(byte[] datos);
}

/// <summary>El buzon de avisos (TBL-27). Encolar no envia: el envio es trabajo de fondo.</summary>
public interface INotificaciones
{
    void Encolar(Notificacion aviso);
}

/// <summary>TBL-28. Solo insercion. Nunca contrasenas, tokens ni direcciones.</summary>
public interface IAuditor
{
    void Registrar(RegistroAuditoria entrada);
}

/// <summary>
/// Una transaccion por caso de uso. Quien orquesta decide cuando se confirma; los
/// repositorios solo acumulan cambios.
/// </summary>
public interface IUnidadDeTrabajo
{
    Task<int> GuardarAsync(CancellationToken ct = default);
}

public interface IRepositorioUsuarios
{
    Task<Usuario?> PorIdAsync(Guid id, CancellationToken ct = default);
    Task<Usuario?> PorCorreoAsync(string correo, CancellationToken ct = default);
    Task<Usuario?> PorTokenVerificacionAsync(string hashToken, CancellationToken ct = default);
    Task<Usuario?> PorTokenRecuperacionAsync(string hashToken, CancellationToken ct = default);
    Task<bool> ExisteCorreoAsync(string correo, CancellationToken ct = default);
    Task<Rol?> RolPorCodigoAsync(string codigo, CancellationToken ct = default);
    void Agregar(Usuario usuario);
}

public interface IRepositorioSesiones
{
    Task<Sesion?> PorHashTokenAsync(string hashToken, CancellationToken ct = default);
    Task<List<Sesion>> VigentesDeUsuarioAsync(Guid usuarioId, DateTime ahora, CancellationToken ct = default);
    void Agregar(Sesion sesion);
}

public interface IRepositorioPerfiles
{
    Task<PerfilPrestador?> PrestadorDeUsuarioAsync(Guid usuarioId, CancellationToken ct = default);
    Task<PerfilPrestador?> PrestadorPorIdAsync(Guid id, CancellationToken ct = default);
    Task<PerfilPrestador?> PrestadorPorSlugAsync(string slug, CancellationToken ct = default);
    Task<bool> ExisteSlugAsync(string slug, CancellationToken ct = default);
    Task<PerfilBuscador?> BuscadorDeUsuarioAsync(Guid usuarioId, CancellationToken ct = default);
    void Agregar(PerfilPrestador perfil);
    void Agregar(PerfilBuscador perfil);
}

/// <summary>
/// MOD-13. Lo que el modulo de suscripciones necesita de la base.
///
/// VigentesEntreAsync esta aparte de VigenteDePrestadorAsync a proposito: la busqueda
/// no puede preguntar prestador por prestador, y si el puerto no ofreciera la version
/// en lote, quien lo implemente acabaria haciendo el bucle igual.
/// </summary>
public interface IRepositorioSuscripciones
{
    Task<Suscripcion?> PorIdAsync(Guid id, CancellationToken ct = default);

    /// <summary>La vigente de ese prestador, o nulo. Vigente = (Prueba|Activa) y fecha futura.</summary>
    Task<Suscripcion?> VigenteDePrestadorAsync(
        Guid perfilPrestadorId, DateTime ahora, CancellationToken ct = default);

    /// <summary>La ultima que tuvo, vigente o no. Para el panel del prestador.</summary>
    Task<Suscripcion?> UltimaDePrestadorAsync(
        Guid perfilPrestadorId, CancellationToken ct = default);

    /// <summary>De los identificadores dados, cuales estan vigentes. UNA consulta.</summary>
    Task<IReadOnlyCollection<Guid>> VigentesEntreAsync(
        IReadOnlyCollection<Guid> perfilPrestadorIds, DateTime ahora,
        CancellationToken ct = default);

    /// <summary>Pasadas de fecha pero todavia marcadas Prueba o Activa.</summary>
    Task<IReadOnlyList<Suscripcion>> ExpiradasSinMarcarAsync(
        DateTime ahora, CancellationToken ct = default);

    /// <summary>Vigentes que vencen antes de una fecha, para avisar con tiempo.</summary>
    Task<IReadOnlyList<Suscripcion>> VigentesQueVencenAntesDeAsync(
        DateTime ahora, DateTime hasta, CancellationToken ct = default);

    Task<Plan?> PlanPorCodigoAsync(string codigo, CancellationToken ct = default);
    Task<IReadOnlyList<Plan>> PlanesActivosAsync(CancellationToken ct = default);

    /// <summary>Comprueba el perfil sin traerlo entero ni depender del repositorio de MOD-01.</summary>
    Task<bool> ExistePerfilPrestadorAsync(Guid perfilPrestadorId, CancellationToken ct = default);

    void Agregar(Suscripcion suscripcion);
    void AgregarPago(Pago pago);
}

/// <summary>
/// MOD-03. Lo que el catalogo necesita de la base: TBL-07, TBL-08 y TBL-09.
///
/// No hay aqui ni una sola operacion que lea TBL-05: el catalogo es un modulo hoja
/// (dependencias-entre-modulos.md) y debe quedarse asi. El rubro del perfil y el tope
/// de servicios del plan los pasa quien compone, no los busca este modulo.
/// </summary>
public interface IRepositorioCatalogo
{
    /// <summary>API-02. Solo rubros activos. Con texto, filtra para el autocompletado.</summary>
    Task<IReadOnlyList<Rubro>> RubrosActivosAsync(
        string? texto, int limite, CancellationToken ct = default);

    Task<Rubro?> RubroActivoPorIdAsync(short rubroId, CancellationToken ct = default);

    /// <summary>
    /// Por codigo, que es como lo pide API-01: `?rubro=corretaje` y no `?rubro=1`. Un
    /// enlace a una busqueda se comparte y se lee, y el identificador numerico ademas
    /// ataria las URLs publicas al orden en que se sembro el catalogo.
    /// </summary>
    Task<Rubro?> RubroActivoPorCodigoAsync(string codigo, CancellationToken ct = default);

    /// <summary>API-03. Los del catalogo del equipo; los que agrego un prestador no se sugieren.</summary>
    Task<IReadOnlyList<Servicio>> ServiciosDeCatalogoAsync(
        short rubroId, CancellationToken ct = default);

    /// <summary>
    /// Los servicios de esos identificadores, en UNA consulta.
    ///
    /// Validar una lista de veinte servicios preguntando uno por uno serian veinte
    /// consultas en el guardado del perfil; el puerto ofrece el lote para que quien lo
    /// implemente no acabe haciendo el bucle igual (misma razon que VigentesEntreAsync).
    /// </summary>
    Task<IReadOnlyList<Servicio>> ServiciosPorIdsAsync(
        IReadOnlyCollection<int> ids, CancellationToken ct = default);

    /// <summary>
    /// Un servicio del rubro con ese nombre ya normalizado, sea del catalogo o agregado
    /// por alguien. Evita que "Tasacion" y "tasacion " se conviertan en dos servicios.
    /// </summary>
    Task<Servicio?> ServicioPorNombreAsync(
        short rubroId, string nombreNormalizado, CancellationToken ct = default);

    /// <summary>TBL-09 completo de un prestador, activos e inactivos.</summary>
    Task<IReadOnlyList<PrestadorServicio>> OfertaDePrestadorAsync(
        Guid perfilPrestadorId, CancellationToken ct = default);

    /// <summary>Lo que MOD-06 necesita para agendar: duracion y modo. Con el servicio incluido.</summary>
    Task<PrestadorServicio?> OfertaPorIdAsync(Guid id, CancellationToken ct = default);

    void AgregarServicio(Servicio servicio);
    void AgregarOferta(PrestadorServicio oferta);
}

/// <summary>
/// MOD-04. TBL-10 zona y TBL-11 cobertura_prestador.
///
/// Las zonas se traen TODAS y la mas cercana se elige en memoria. Es deliberado: hoy son
/// decenas, caben en una consulta, y asi la regla vive en el dominio y se prueba sin base
/// de datos. Cuando pasen de unos cientos habra que mover esto a PostGIS con una columna
/// geography y un indice GIST, y se notara en esta firma, no en todo el modulo.
/// </summary>
public interface IRepositorioGeografia
{
    Task<IReadOnlyList<Zona>> ZonasAsync(string? sector = null, CancellationToken ct = default);

    Task<Zona?> ZonaPorIdAsync(int zonaId, CancellationToken ct = default);

    /// <summary>Las zonas pedidas, en UNA consulta, para validar una lista de cobertura.</summary>
    Task<IReadOnlyList<Zona>> ZonasPorIdsAsync(
        IReadOnlyCollection<int> ids, CancellationToken ct = default);

    Task<IReadOnlyList<CoberturaPrestador>> CoberturaDePrestadorAsync(
        Guid perfilPrestadorId, CancellationToken ct = default);

    void AgregarCobertura(CoberturaPrestador cobertura);
    void QuitarCobertura(CoberturaPrestador cobertura);
}

/// <summary>
/// N-08, el servicio externo de mapas. CS-10 lo necesita para pasar de una direccion
/// escrita a un punto.
///
/// Esta aparte del repositorio porque falla distinto: una base que no responde es una
/// caida; un geocodificador que no responde es un caso de uso previsto, el flujo 4B de
/// CU-03, donde el prestador elige su zona de una lista. Por eso devuelve Resultado y no
/// lanza, y por eso hoy existe una implementacion que siempre dice que no esta
/// disponible: el proveedor todavia no esta elegido.
/// </summary>
public interface IGeocodificador
{
    Task<Resultado<Punto>> GeocodificarAsync(string direccion, CancellationToken ct = default);
}

/// <summary>
/// MOD-05. La consulta de CU-08, que es la mas caliente del producto.
///
/// Devuelve <see cref="FilaDeBusqueda"/> y no entidades del dominio a proposito: una
/// busqueda no necesita perfiles completos, y traerlos obligaria a cargar la direccion
/// cifrada y la ubicacion exacta solo para despues descartarlas. Lo que no se trae no se
/// puede filtrar por accidente (RN-S3-02).
/// </summary>
public interface IRepositorioBusqueda
{
    /// <summary>
    /// Por tramo de distancia. El punto ya viene resuelto a zona por MOD-04, porque la
    /// cobertura de la zona es un filtro duro del paso 5 de CU-08.
    ///
    /// <b>El orden es (tramo, verificado, reputacion publica, resenas, id).</b> Se ordena
    /// por el TRAMO —el kilometro entero que la pantalla muestra— y no por los metros
    /// exactos, por dos razones: (1) hoy todos los prestadores de una zona estan a la
    /// misma distancia exacta, la de su centroide, asi que sin desempate el orden seria
    /// el de un UUID; (2) ordenar por la distancia exacta filtra mas que la redondeada:
    /// dos prestadores de "a 3 km" que cambian de orden segun desde donde se busca
    /// delatan de que lado de una linea estan.
    ///
    /// El cursor es keyset sobre esa misma tupla: nada de OFFSET.
    /// </summary>
    Task<IReadOnlyList<FilaDeBusqueda>> PorDistanciaAsync(
        CriteriosDeConsulta criterios, double latitud, double longitud, int radioMetros,
        PosicionDeBusqueda? desde, CancellationToken ct = default);

    /// <summary>
    /// Sin punto: filtra por zona y ordena por (verificado, reputacion publica, resenas,
    /// id). Es el camino normal mientras N-08 no exista (RN-S3-07 y flujo 4C.4 de CU-08).
    /// </summary>
    Task<IReadOnlyList<FilaDeBusqueda>> PorZonaAsync(
        CriteriosDeConsulta criterios, PosicionDeBusqueda? desde,
        CancellationToken ct = default);
}

/// <summary>
/// Donde quedo la ultima fila servida. Es lo que viaja en el cursor, y es EXACTAMENTE la
/// tupla por la que se ordena: si el cursor llevara menos, la pagina siguiente podria
/// repetir o saltarse a alguien con un empate.
///
/// `Tramo` es nulo en la busqueda por zona, que no ordena por distancia.
/// </summary>
public sealed record PosicionDeBusqueda(
    int? Tramo, bool Verificada, decimal Reputacion, int Resenas, Guid Id);

/// <summary>Lo que los dos caminos de la consulta tienen en comun.</summary>
public sealed record CriteriosDeConsulta(
    short RubroId,
    int ZonaId,
    int? ServicioId,
    bool SoloVerificados,
    decimal? CalificacionMinima,
    DateTime Ahora,
    int Limite);

/// <summary>
/// Una fila cruda de la consulta de busqueda.
///
/// Es una clase con propiedades asignables, y no un record posicional, porque EF Core la
/// materializa desde SQL: se declara aqui —en la capa que la necesita— y
/// <c>ZassDbContext</c> la configura como tipo sin clave. La flecha de dependencia sigue
/// apuntando de infraestructura hacia aqui.
///
/// `Metros` es la distancia EXACTA y no sale nunca de la capa de aplicacion: el servicio
/// la convierte en el texto redondeado de RN-S3-03 antes de armar el resultado publico.
/// </summary>
public class FilaDeBusqueda
{
    public Guid Id { get; set; }
    public string Slug { get; set; } = "";
    public string NombrePublico { get; set; } = "";
    public string? Descripcion { get; set; }
    public string? UrlFoto { get; set; }
    public short RubroId { get; set; }
    public string? ZonaPrincipal { get; set; }
    public double? Metros { get; set; }

    /// <summary>El kilometro entero que se muestra, calculado por la base. Nulo sin punto.</summary>
    public int? Tramo { get; set; }

    public bool TieneVerificada { get; set; }

    /// <summary>
    /// El promedio TAL COMO ESTA GUARDADO. Con menos de 3 resenas no es publico (RN-S5-11):
    /// el servicio decide si se muestra, y este campo nunca se usa para ordenar.
    /// </summary>
    public decimal? PromedioCalificacion { get; set; }

    /// <summary>
    /// Lo que cuenta para ORDENAR: el promedio si es publico, y -1 si no. Es un campo
    /// aparte del anterior a proposito: ordenar por el promedio guardado hacia que quien
    /// tiene 2 resenas de 5 estrellas pasara delante de quien tiene 4,6 con 12, por un
    /// numero que la pantalla ni siquiera muestra.
    /// </summary>
    public decimal ReputacionOrden { get; set; }

    public int TotalResenas { get; set; }
}
