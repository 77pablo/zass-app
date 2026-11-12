using Zass.Dominio.Agenda;
using Zass.Dominio.Catalogo;
using Zass.Dominio.Comun;
using Zass.Dominio.Credenciales;
using Zass.Dominio.Identidad;
using Zass.Dominio.Reputacion;
using Zass.Dominio.Suscripciones;
using Zass.Dominio.Tarjetas;

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

    /// <summary>
    /// Corre el cuerpo dentro de UNA transaccion y la confirma solo si termina bien. Si
    /// lanza, se deshace todo: no puede quedar un bloque reservado sin cita, ni una cita sin
    /// bloque (postcondicion de fallo de CU-12).
    ///
    /// Existe porque CC-01 lo exige: el <c>SELECT ... FOR UPDATE</c> sobre el bloque solo
    /// sirve si el INSERT de la cita y el UPDATE del bloque viven en la misma transaccion que
    /// ese bloqueo. Con el <c>GuardarAsync</c> suelto, EF abriria una transaccion propia por
    /// cada SaveChanges y el bloqueo se habria liberado antes de insertar.
    ///
    /// Si ya hay una transaccion abierta (las pruebas de integracion abren una y la
    /// deshacen), se reutiliza en vez de anidar: PostgreSQL no anida transacciones.
    /// </summary>
    Task<T> EnTransaccionAsync<T>(
        Func<CancellationToken, Task<T>> cuerpo, CancellationToken ct = default);
}

/// <summary>
/// La sonda de disponibilidad de /api/salud/lista: ?responde la base de verdad?
///
/// Es un puerto y no una consulta dentro del endpoint porque los endpoints no hablan con la
/// base (la prueba de arquitectura «los endpoints no tocan la base ni los repositorios» lo
/// exige, y el motivo de fondo es que el dia que cambie como se comprueba la base se cambia
/// en un solo sitio).
///
/// NUNCA lanza: devuelve false y deja el motivo en el registro. La ruta es publica y el texto
/// de un error de Npgsql lleva el host y la cadena de conexion.
/// </summary>
public interface ISondaDeBase
{
    Task<bool> RespondeAsync(CancellationToken ct = default);
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

    /// <summary>
    /// A quien pertenece cada perfil, para poder avisarle. UNA consulta, por el mismo motivo
    /// que <see cref="VigentesEntreAsync"/>: el trabajo diario puede tener cientos de
    /// suscripciones por vencer y preguntar una por una serian cientos de idas a la base.
    ///
    /// Los perfiles que no existan simplemente no aparecen en el resultado.
    /// </summary>
    Task<IReadOnlyDictionary<Guid, Guid>> UsuariosDePerfilesAsync(
        IReadOnlyCollection<Guid> perfilPrestadorIds, CancellationToken ct = default);

    void Agregar(Suscripcion suscripcion);
    void AgregarPago(Pago pago);

    /// <summary>Cuantos de los 20 cupos fundador estan libres o con reserva expirada.</summary>
    Task<int> CuposFundadorDisponiblesAsync(DateTime ahora, CancellationToken ct = default);

    /// <summary>
    /// Reserva atomicamente uno de los 20 cupos fundador hasta <paramref name="hasta"/>.
    /// Idempotente si <paramref name="perfilPrestadorId"/> ya tiene uno asignado.
    /// Devuelve el numero (1..20), o null si los 20 estan ocupados.
    /// </summary>
    Task<short?> IntentarReservarCupoFundadorAsync(
        Guid perfilPrestadorId, DateTime ahora, DateTime hasta, CancellationToken ct = default);

    /// <summary>Deja el cupo de ese perfil sin fecha de vencimiento. False si no tenia cupo.</summary>
    Task<bool> ConfirmarCupoFundadorAsync(Guid perfilPrestadorId, CancellationToken ct = default);

    /// <summary>Libera el cupo ocupado o reservado por ese perfil. False si no tenia cupo.</summary>
    Task<bool> LiberarCupoFundadorAsync(Guid perfilPrestadorId, CancellationToken ct = default);

    /// <summary>Suelta reservas temporales expiradas (reservado_hasta &lt;= ahora).</summary>
    Task<int> LiberarReservasFundadorVencidasAsync(DateTime ahora, CancellationToken ct = default);
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
    /// Un rubro por identificador, ACTIVO O NO. Una ficha publicada tiene que poder decir en
    /// que rubro esta aunque el equipo apague despues ese rubro del selector.
    /// </summary>
    Task<Rubro?> RubroPorIdAsync(short rubroId, CancellationToken ct = default);

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

    /// <summary>
    /// API-62. Cuanta gente hay y quien se sumo recien, con EL MISMO criterio de "visible" que la
    /// busqueda: publicado y con suscripcion vigente (RN-S3-01 y RN-S3-09). Si la portada
    /// contara a quien la busqueda no muestra, diria "14 personas en el Centro" y al entrar
    /// aparecerian 12.
    /// </summary>
    Task<ResumenCrudo> ResumenAsync(
        DateTime ahora, int recientes, CancellationToken ct = default);

    /// <summary>
    /// API-63. La barra de busqueda: texto libre, ordenado como pida la persona. Mismo criterio
    /// de "visible" que la busqueda por rubro (publicado y con suscripcion vigente).
    /// </summary>
    Task<IReadOnlyList<FilaDeTexto>> PorTextoAsync(
        CriteriosDeTexto criterios, CancellationToken ct = default);
}

/// <summary>
/// Lo que la consulta de texto recibe ya masticado: las formas y el grupo de cada una (ver
/// <c>TextoDeBusqueda.Grupos</c>), el punto desde el que se mide y el orden pedido.
/// </summary>
public sealed record CriteriosDeTexto(
    string[] Formas,
    int[] Grupos,
    double? Latitud,
    double? Longitud,
    int? ZonaId,
    string Orden,
    DateTime Ahora,
    int Limite,
    int Desplazamiento,
    bool SoloVerificados = false,
    decimal? CalificacionMinima = null);

/// <summary>Una fila de la busqueda por texto. Como FilaDeBusqueda: lleva los metros, y el servicio los redondea.</summary>
public class FilaDeTexto
{
    public Guid Id { get; set; }
    public string Slug { get; set; } = "";
    public string NombrePublico { get; set; } = "";
    public string? Descripcion { get; set; }
    public string? UrlFoto { get; set; }
    public string? RubroNombre { get; set; }
    public string? ZonaPrincipal { get; set; }
    public double? Metros { get; set; }
    public int? Tramo { get; set; }
    public bool TieneVerificada { get; set; }
    public decimal? PromedioCalificacion { get; set; }
    public decimal ReputacionOrden { get; set; }
    public int TotalResenas { get; set; }
    public int? PrecioDesde { get; set; }

    /// <summary>Cuantas personas cumplen la busqueda en total, no solo en esta pagina.</summary>
    public long TotalFilas { get; set; }
}

public sealed record ConteoCrudo(int Id, int Personas);

public sealed record ConteoDeRegion(string Region, int Personas);

public sealed record RecienLlegadoFila(
    string Slug, string NombrePublico, string? RubroNombre, string? ZonaPrincipal, bool Verificada);

public sealed record ResumenCrudo(
    int Total,
    IReadOnlyList<ConteoCrudo> PorZona,
    IReadOnlyList<ConteoCrudo> PorRubro,
    IReadOnlyList<RecienLlegadoFila> Recientes,
    IReadOnlyList<ConteoDeRegion>? PorRegion = null);

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

/// <summary>MOD-02 - Credenciales (TBL-12, TBL-13, TBL-14, TBL-15).</summary>
public interface IRepositorioCredenciales
{
    /// <summary>Las de un prestador con su entidad y sus documentos, la mas nueva primero.</summary>
    Task<IReadOnlyList<Credencial>> DePrestadorAsync(
        Guid perfilPrestadorId, CancellationToken ct = default);

    /// <summary>Las que el publico puede ver: todas menos los borradores.</summary>
    Task<IReadOnlyList<Credencial>> PublicasDePrestadorAsync(
        Guid perfilPrestadorId, CancellationToken ct = default);

    Task<Credencial?> PorIdAsync(Guid id, CancellationToken ct = default);

    Task<int> ContarDePrestadorAsync(Guid perfilPrestadorId, CancellationToken ct = default);

    Task<bool> TieneVerificadaAsync(Guid perfilPrestadorId, CancellationToken ct = default);

    void Agregar(Credencial credencial);
    void Eliminar(Credencial credencial);

    Task<EntidadCertificadora?> EntidadActivaPorIdAsync(short id, CancellationToken ct = default);

    /// <summary>Por nombre sin distinguir mayusculas: "Universidad de la Frontera" y "universidad de la frontera" son la misma.</summary>
    Task<EntidadCertificadora?> EntidadPorNombreAsync(string nombre, CancellationToken ct = default);

    void AgregarEntidad(EntidadCertificadora entidad);

    // ---- Cola del Verificador (CU-06, CC-03) ----

    /// <summary>
    /// La cola: en revisión, sin esperar al prestador, que NO sean del propio verificador
    /// (RN-S2-09) y sin las que otro tiene reservadas y vigentes. Más antigua primero. Con su
    /// perfil, entidad y documentos.
    /// </summary>
    Task<IReadOnlyList<Credencial>> ColaAsync(
        Guid verificadorId, DateTime ahora, CancellationToken ct = default);

    /// <summary>La credencial con su perfil (para saber de quién es y cómo se llama).</summary>
    Task<Credencial?> PorIdParaRevisionAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    /// CC-03. Reserva ATÓMICA en una sola sentencia: gana quien la encuentre libre (o vencida, o
    /// ya suya). Devuelve falso si perdió la carrera, no era revisable o es una cuenta propia. Es una
    /// condición dentro del UPDATE y no un "leer y luego escribir": con dos verificadores a la vez,
    /// PostgreSQL serializa por la fila y el segundo reevalúa la condición ya con la reserva puesta.
    /// </summary>
    Task<bool> ReservarAsync(
        Guid credencialId, Guid verificadorId, DateTime ahora, DateTime hasta,
        CancellationToken ct = default);

    /// <summary>
    /// Consume la reserva vigente de este verificador, de forma atómica. Solo una llamada concurrente
    /// puede ganarla: así dos resoluciones simultáneas de la misma credencial (dos pestañas) no
    /// pueden ganar las dos.
    /// </summary>
    Task<bool> ConsumirReservaAsync(
        Guid credencialId, Guid verificadorId, DateTime ahora, CancellationToken ct = default);

    /// <summary>
    /// Registra una decisión. Explícito y no "agregarla a la lista de la credencial": su Id ya viene
    /// puesto, y EF toma un Guid con valor por una fila que YA existe (UPDATE de 0 filas y un error
    /// de concurrencia en vez de un INSERT).
    /// </summary>
    void AgregarVerificacion(Verificacion verificacion);
}

/// <summary>
/// El buzón de contactos (TBL-21). **Es un puerto de salida, no el repositorio de MOD-11.**
///
/// TBL-21 es tabla propia de MOD-11 Reseñas, y `MOD-06 → MOD-11` está prohibido
/// (dependencias-entre-modulos.md, sección 5): sería un ciclo. Pero el paso 11 de CU-12 exige
/// dejar el registro de contacto **dentro de la misma transacción** que crea la cita, porque
/// es la única puerta a la elegibilidad de reseña y perderlo la perdería en silencio.
///
/// Se resuelve igual que TBL-27: <see cref="INotificaciones"/> deja avisos en la tabla de
/// MOD-07 sin que nadie dependa de MOD-07. Esto es lo mismo, de salida y de una sola
/// dirección: quien escribe no sabe quién lee, y MOD-11 sigue sin conocer la tabla `cita`
/// (pregunta por <c>IServicioAgenda.CitaRealizadaAsync</c>, sección 4.1).
/// </summary>
public interface IContactos
{
    void Registrar(Contacto contacto);
}

/// <summary>
/// MOD-06. Lo que la agenda necesita de la base: TBL-16 a TBL-20.
///
/// Es el puerto más grande del sistema, y eso es consecuencia del diseño, no un descuido:
/// «toda la complejidad de CC-01, CC-02, CC-04 y CC-06 vive adentro y no se filtra a la
/// interfaz» (tabla-de-modulos.md, MOD-06). Los tres métodos que bloquean filas están aquí
/// porque solo PostgreSQL puede darlos; el resto de la regla vive en la capa de aplicación.
/// </summary>
public interface IRepositorioAgenda
{
    // ------------------------------------------------------- TBL-16 disponibilidad (CU-11)

    Task<IReadOnlyList<Disponibilidad>> DisponibilidadesDeServicioAsync(
        Guid prestadorServicioId, CancellationToken ct = default);

    Task<IReadOnlyList<Disponibilidad>> DisponibilidadesDePrestadorAsync(
        Guid perfilPrestadorId, CancellationToken ct = default);

    void AgregarDisponibilidad(Disponibilidad disponibilidad);

    // ------------------------------------------------------- TBL-17 bloque_horario

    /// <summary>Los bloques de un servicio en una ventana, para pintar el calendario (API-35).</summary>
    Task<IReadOnlyList<BloqueHorario>> BloquesDeServicioAsync(
        Guid prestadorServicioId, DateTime desde, DateTime hasta, CancellationToken ct = default);

    /// <summary>
    /// Los inicios que YA existen en la ventana. Generar bloques es idempotente gracias a
    /// esto: el Planificador extiende la ventana cada noche y no debe duplicar lo de ayer.
    /// </summary>
    Task<IReadOnlyCollection<DateTime>> IniciosYaGeneradosAsync(
        Guid prestadorServicioId, DateTime desde, DateTime hasta, CancellationToken ct = default);

    void AgregarBloques(IEnumerable<BloqueHorario> bloques);

    /// <summary>
    /// Quita los bloques futuros que esa regla generó y que **nadie reservó**. Es RN-S4-09:
    /// cambiar la disponibilidad no toca las citas ya confirmadas. Devuelve cuántos quitó.
    /// </summary>
    Task<int> QuitarBloquesLibresDeAsync(
        Guid disponibilidadId, DateTime desde, CancellationToken ct = default);

    // ------------------------------------------------------- TBL-20 bloqueo_agenda (CS-11)

    Task<IReadOnlyList<BloqueoAgenda>> BloqueosDePrestadorAsync(
        Guid perfilPrestadorId, DateTime desde, DateTime hasta, CancellationToken ct = default);

    void AgregarBloqueo(BloqueoAgenda bloqueo);

    /// <summary>Los bloqueos que aún no terminaron. Los pasados no sirven para nada en pantalla.</summary>
    Task<IReadOnlyList<BloqueoAgenda>> BloqueosVigentesDeAsync(
        Guid perfilPrestadorId, DateTime ahora, CancellationToken ct = default);

    /// <summary>
    /// Rastreado, y con el dueño dentro de la consulta: un bloqueo ajeno no se distingue de
    /// uno que no existe.
    /// </summary>
    Task<BloqueoAgenda?> BloqueoDeAsync(
        Guid perfilPrestadorId, Guid bloqueoId, CancellationToken ct = default);

    void QuitarBloqueo(BloqueoAgenda bloqueo);

    // ------------------------------------------------------- CC-01: el bloqueo de fila

    /// <summary>
    /// <c>SELECT ... FOR UPDATE</c> sobre la fila del bloque. La segunda transacción que
    /// llega **espera** aquí; cuando el bloqueo se libera lee el estado ya actualizado y se
    /// retira limpiamente con un 409 (CC-01).
    ///
    /// Solo tiene sentido dentro de <see cref="IUnidadDeTrabajo.EnTransaccionAsync{T}"/>:
    /// fuera de una transacción el bloqueo se suelta al terminar la consulta y no protege nada.
    /// </summary>
    Task<BloqueHorario?> TomarBloqueAsync(Guid bloqueId, CancellationToken ct = default);

    /// <summary>Si existe cita activa sobre ese bloque. La última palabra la tiene R-1.</summary>
    Task<bool> TieneCitaActivaAsync(Guid bloqueId, CancellationToken ct = default);

    // ------------------------------------------------------- TBL-18 cita, TBL-19 cambio_cita

    Task<Cita?> CitaPorIdAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    /// La cita que creó esta clave, si ya existe (CC-04, flujo 4B de CU-12). Devolverla es lo
    /// que convierte un doble envío en la misma respuesta en vez de una segunda cita.
    /// </summary>
    Task<Cita?> CitaPorClaveAsync(string claveIdempotencia, CancellationToken ct = default);

    /// <summary>Cuántas activas tiene ese buscador con ese prestador (RN-S4-06, tope de 3).</summary>
    Task<int> CitasActivasEntreAsync(
        Guid buscadorId, Guid perfilPrestadorId, CancellationToken ct = default);

    /// <summary>
    /// La consulta de elegibilidad que usa MOD-11 (sección 4.1). Un `bool`: reseñas no sabe
    /// que existe una tabla `cita` ni cuántos estados tiene.
    /// </summary>
    Task<bool> HuboCitaRealizadaAsync(
        Guid buscadorId, Guid perfilPrestadorId, CancellationToken ct = default);

    /// <summary>Las citas de alguien, de los dos lados. Con el bloque y el servicio incluidos.</summary>
    Task<IReadOnlyList<Cita>> CitasDeBuscadorAsync(
        Guid buscadorId, CancellationToken ct = default);

    Task<IReadOnlyList<Cita>> CitasDePrestadorAsync(
        Guid perfilPrestadorId, CancellationToken ct = default);

    // ------------------------------------------------------- lo que dispara el tiempo (A-09)

    /// <summary>T5. Solicitadas cuyo plazo de 24 h ya venció. Solo los ids: cada una se procesa en su transacción.</summary>
    Task<IReadOnlyList<Guid>> CitasPorExpirarAsync(DateTime ahora, int maximo, CancellationToken ct = default);

    /// <summary>T8. Confirmadas cuya hora ya terminó.</summary>
    Task<IReadOnlyList<Guid>> CitasTerminadasAsync(DateTime ahora, int maximo, CancellationToken ct = default);

    /// <summary>T11. Pendientes de asistencia cuya hora terminó antes de <paramref name="limite"/>.</summary>
    Task<IReadOnlyList<Guid>> CitasSinAsistenciaAsync(DateTime limite, int maximo, CancellationToken ct = default);

    /// <summary>
    /// La cita RASTREADA, con su bloque, para poder cambiarle el estado. Separada de
    /// <see cref="CitaPorIdAsync"/> porque esa es de lectura y puede venir sin rastreo.
    /// </summary>
    Task<Cita?> CitaParaCambioAsync(Guid id, CancellationToken ct = default);

    // ------------------------------------------------------- CC-06: el abrazo mortal

    /// <summary>
    /// Los dos bloques de una reprogramación, bloqueados **ordenados por id**. Con un orden
    /// global el ciclo no puede formarse: dos reprogramaciones cruzadas piden X antes que Y
    /// las dos, una espera a la otra y las dos terminan (CC-06).
    ///
    /// Devuelve los que existan, de uno o de dos en dos: quien llama comprueba cuáles faltan.
    /// </summary>
    Task<IReadOnlyList<BloqueHorario>> TomarDosBloquesAsync(
        Guid unoId, Guid otroId, CancellationToken ct = default);

    /// <summary>
    /// <c>SET LOCAL lock_timeout</c>. CC-06 lo pide: si algo imprevisto hace esperar más de
    /// unos segundos, mejor fallar rápido con un mensaje claro que dejar a alguien mirando
    /// una pantalla que no responde. `LOCAL` significa «solo en esta transacción».
    /// </summary>
    Task FijarEsperaDeBloqueoAsync(TimeSpan limite, CancellationToken ct = default);

    void AgregarCita(Cita cita);
    void AgregarCambio(CambioCita cambio);
}

/// <summary>MOD-12 · TBL-25/26. Solo lo que CS-18 necesita; API-55 a API-59 agregaran lo suyo.</summary>
public interface IRepositorioTarjetas
{
    /// <summary>La tarjeta con su perfil, o nulo. Comparacion exacta: el servicio ya normalizo.</summary>
    Task<Tarjeta?> PorUrlEstableAsync(string urlEstable, CancellationToken ct = default);

    /// <summary>Sin identidad del visitante (R-20): la entidad no tiene donde guardarla.</summary>
    void AgregarEscaneo(EscaneoTarjeta escaneo);
}
