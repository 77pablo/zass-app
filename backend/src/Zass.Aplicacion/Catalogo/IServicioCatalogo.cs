using Zass.Dominio.Comun;

namespace Zass.Aplicacion.Catalogo;

/// <summary>
/// MOD-03 - S1, S3. Rubros y servicios disponibles (TBL-07, TBL-08, TBL-09).
///
/// Modulo hoja: no consume ningun otro modulo y no debe empezar a hacerlo. Por eso
/// <see cref="ReemplazarServiciosAsync"/> recibe el rubro del perfil y el tope del plan
/// en el comando en vez de ir a buscarlos a MOD-01 y MOD-13; quien compone es la capa
/// de API, igual que ya hace API-22 con publicar + arrancar la prueba.
/// </summary>
public interface IServicioCatalogo
{
    /// <summary>API-02. Catalogo de rubros activos, con texto opcional para autocompletar.</summary>
    Task<IReadOnlyList<RubroDto>> BuscarRubrosAsync(
        string? texto = null, CancellationToken ct = default);

    /// <summary>
    /// Si ese rubro existe y esta activo. Lo necesita MOD-05 para la excepcion E-422 de
    /// CU-08 sin traerse los servicios del rubro solo para mirar si hay alguno.
    /// </summary>
    Task<bool> ExisteRubroActivoAsync(short rubroId, CancellationToken ct = default);

    /// <summary>
    /// El rubro por su codigo, que es como llega en la URL de API-01. Nulo si no existe
    /// o esta apagado.
    /// </summary>
    Task<RubroDto?> RubroPorCodigoAsync(string codigo, CancellationToken ct = default);

    /// <summary>
    /// El rubro por su identificador, activo o no: la ficha publica de alguien tiene que
    /// poder decir su rubro aunque despues se apague del selector.
    /// </summary>
    Task<RubroDto?> RubroPorIdAsync(short rubroId, CancellationToken ct = default);

    /// <summary>
    /// Lo que un prestador ofrece, como lo ve el publico: solo lo ACTIVO, y solo nombre,
    /// duracion y precio de referencia. Sin el modo de agenda ni identificadores internos.
    /// Ordenado por nombre, en espanol (la ene y las tildes no se van al final).
    /// </summary>
    Task<IReadOnlyList<ServicioPublicoDto>> ServiciosPublicosDePrestadorAsync(
        Guid perfilPrestadorId, CancellationToken ct = default);

    /// <summary>API-03. Los servicios sugeridos de un rubro (CU-03 paso 3).</summary>
    Task<Resultado<IReadOnlyList<ServicioDto>>> ServiciosDeRubroAsync(
        short rubroId, CancellationToken ct = default);

    /// <summary>Lo que el prestador ya ofrece, para pintar el formulario de CU-03 paso 4.</summary>
    Task<IReadOnlyList<ServicioOfrecidoDto>> ServiciosDePrestadorAsync(
        Guid perfilPrestadorId, CancellationToken ct = default);

    /// <summary>
    /// API-20. Reemplaza la oferta del prestador (CU-03 paso 4).
    ///
    /// "Reemplazar" no es borrar y volver a crear: TBL-18 `cita` y TBL-13
    /// `disponibilidad` apuntan a `prestador_servicio` con borrado en cascada, asi que
    /// recrear las filas se llevaria por delante las citas y los horarios. Lo que sale
    /// de la lista se desactiva y se conserva; lo que vuelve, se reactiva.
    /// </summary>
    Task<Resultado> ReemplazarServiciosAsync(
        ReemplazarServiciosCmd cmd, CancellationToken ct = default);

    /// <summary>
    /// MOD-06 lo necesita para agendar: cuanto dura y si la agenda es automatica.
    /// Es la operacion `DatosDePrestadorServicio` de la tabla de modulos.
    /// </summary>
    Task<Resultado<DatosOfertaDto>> DatosDePrestadorServicioAsync(
        Guid prestadorServicioId, CancellationToken ct = default);
}

public sealed record RubroDto(short Id, string Codigo, string Nombre, string? Icono);

/// <summary>Un servicio en la ficha publica. Nada de lo interno: ni ids ni modo de agenda.</summary>
public sealed record ServicioPublicoDto(string Nombre, short DuracionMinutos, int? PrecioReferencia);

/// <summary>
/// `EsCatalogo` viaja al cliente a proposito: la pantalla distingue entre lo que
/// sugiere ZASS! y lo que escribio el propio prestador.
/// </summary>
public sealed record ServicioDto(int Id, string Nombre, string? Descripcion, bool EsCatalogo);

public sealed record ServicioOfrecidoDto(
    Guid Id,
    int ServicioId,
    string Nombre,
    bool EsCatalogo,
    short DuracionMinutos,
    string ModoAgenda,
    int? PrecioReferencia,
    bool Activo);

/// <summary>Duracion y modo del par prestador-servicio. Nada mas: MOD-06 no necesita mas.</summary>
public sealed record DatosOfertaDto(
    Guid Id,
    Guid PerfilPrestadorId,
    int ServicioId,
    string Nombre,
    short DuracionMinutos,
    string ModoAgenda,
    bool Activo);

/// <summary>
/// Un servicio ofrecido, tal como llega de la pantalla. O se elige del catalogo
/// (<paramref name="ServicioId"/>) o se escribe uno nuevo (<paramref name="NombreNuevo"/>),
/// nunca las dos cosas: CU-03 paso 4 dice "selecciona o agrega".
/// </summary>
public sealed record ServicioOfrecidoCmd(
    int? ServicioId,
    string? NombreNuevo,
    short DuracionMinutos,
    string ModoAgenda,
    int? PrecioReferencia);

/// <summary>
/// API-20. <paramref name="RubroId"/> y <paramref name="MaxServicios"/> los aporta quien
/// compone: el primero es de MOD-01 (TBL-05) y el segundo del plan en MOD-13. Entran
/// como datos para que el catalogo siga sin depender de nadie.
/// </summary>
public sealed record ReemplazarServiciosCmd(
    Guid PerfilPrestadorId,
    short RubroId,
    int MaxServicios,
    IReadOnlyList<ServicioOfrecidoCmd> Servicios);
