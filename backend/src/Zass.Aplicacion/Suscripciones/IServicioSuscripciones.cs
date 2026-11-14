using Zass.Dominio.Comun;

namespace Zass.Aplicacion.Suscripciones;

/// <summary>
/// MOD-13 - S7. La interfaz publica del modulo. Todo lo demas es interno.
///
/// Existe por RN-S3-09: un prestador sin suscripcion vigente DESAPARECE de la busqueda.
/// Alguien tiene que poder responder "este prestador, hoy, aparece o no", y ese alguien
/// es este modulo. Antes del 7 de noviembre de 2026 las tres tablas de S7 existian en la
/// base sin modulo que las gobernara.
/// </summary>
public interface IServicioSuscripciones
{
    /// <summary>Un solo prestador. Para la ficha, el panel y las comprobaciones puntuales.</summary>
    Task<bool> EstaVigenteAsync(Guid perfilPrestadorId, CancellationToken ct = default);

    /// <summary>
    /// Varios de una vez, en UNA consulta. Es lo que usa la busqueda.
    ///
    /// No es una comodidad: preguntar <see cref="EstaVigenteAsync"/> por cada resultado
    /// convertiria una pagina de 20 en 21 consultas, y en la ruta mas caliente del
    /// producto. Devuelve solo los identificadores que estan vigentes.
    /// </summary>
    Task<IReadOnlyCollection<Guid>> VigentesEntreAsync(
        IReadOnlyCollection<Guid> perfilPrestadorIds, CancellationToken ct = default);

    /// <summary>Lo que el prestador ve en su panel. Nunca falla por no tener suscripcion.</summary>
    Task<Resultado<SuscripcionDto>> DePrestadorAsync(
        Guid perfilPrestadorId, CancellationToken ct = default);

    /// <summary>
    /// Arranca el periodo de prueba. Es idempotente: si ya hay una vigente no crea otra
    /// y devuelve exito igual, porque un doble clic no es un error de nadie.
    /// </summary>
    Task<Resultado> IniciarPruebaAsync(Guid perfilPrestadorId, CancellationToken ct = default);

    /// <summary>
    /// Extiende el periodo y deja el cobro registrado. Lo llama <see cref="IServicioCobros"/>
    /// cuando la pasarela confirma, y es el unico sitio del sistema donde una suscripcion
    /// pasa a estar pagada.
    ///
    /// Devuelve el identificador del <c>Pago</c> creado, que es lo que necesita quien
    /// registra el canje del cupon para poder decir a QUE pago se le aplico.
    /// </summary>
    /// <param name="intentoDePagoId">
    /// El intento (TBL-36) que origino el cobro, si lo hubo. Va a la columna con indice
    /// unico parcial: si llegara una segunda confirmacion del mismo intento, la base la
    /// rechaza aunque fallara toda comprobacion anterior.
    /// </param>
    Task<Resultado<Guid>> RegistrarPeriodoPagadoAsync(
        Guid suscripcionId, int meses = 1, int montoPagado = 0, string? referencia = null,
        Guid? intentoDePagoId = null, CancellationToken ct = default);

    /// <summary>El prestador deja de pagar por voluntad propia. Sale de la busqueda de inmediato.</summary>
    Task<Resultado> CancelarAsync(Guid suscripcionId, CancellationToken ct = default);

    /// <summary>Trabajo de fondo. Devuelve cuantas marco. Vencer no borra: solo cambia el estado.</summary>
    Task<int> VencerExpiradasAsync(CancellationToken ct = default);

    /// <summary>
    /// Las que vencen dentro de N dias, para avisar ANTES.
    ///
    /// Desaparecer de la busqueda sin aviso previo es lo que volveria cruel a RN-S3-09:
    /// el perfil se apaga de golpe y el prestador se entera porque dejan de llamarlo.
    /// </summary>
    Task<IReadOnlyList<SuscripcionPorVencer>> PorVencerAsync(
        int dentroDeDias, CancellationToken ct = default);

    /// <summary>
    /// El trabajo diario entero: vence lo pasado de fecha y encola los avisos de las que
    /// estan por vencer. Devuelve que hizo, para que el planificador lo deje registrado.
    ///
    /// Es seguro llamarlo dos veces el mismo dia: los avisos llevan clave determinista
    /// (CC-04), asi que el segundo no manda un segundo correo.
    /// </summary>
    Task<ResumenDelDiario> CorrerDiarioAsync(CancellationToken ct = default);

    Task<IReadOnlyList<PlanDto>> PlanesActivosAsync(CancellationToken ct = default);

    /// <summary>
    /// Cuantos servicios permite el plan de ese prestador. Lo necesita quien compone
    /// API-20: el tope es del plan, y el plan es cosa de este modulo.
    ///
    /// Sin suscripcion todavia manda el plan inicial. Es el caso normal, no un borde:
    /// el prestador elige sus servicios ANTES de publicar, y la prueba empieza justo
    /// al publicar (API-22). Devolver cero ahi le impediria llenar el formulario que
    /// lo lleva a tener suscripcion.
    /// </summary>
    Task<int> LimiteDeServiciosAsync(Guid perfilPrestadorId, CancellationToken ct = default);

    /// <summary>Cuantos de los 50 cupos del plan fundador quedan disponibles hoy.</summary>
    Task<int> CuposFundadorDisponiblesAsync(CancellationToken ct = default);

    /// <summary>
    /// Si el plan fundador sigue abierto, y con que mensaje contarlo.
    ///
    /// Existe porque hasta hoy el cierre del cupo 50 solo se podia conocer intentando
    /// reservar y recibiendo un conflicto: un error tecnico en lugar de una respuesta.
    /// Decision de Pablo del 12-nov-2026: al llegar a 50 se cierra y punto, sin lista de
    /// espera y sin pasar a nadie al plan normal por su cuenta. Quien quiera el mensual
    /// lo elige; el sistema no lo elige por el.
    /// </summary>
    Task<Resultado<PlanFundadorDto>> EstadoDelPlanFundadorAsync(CancellationToken ct = default);

    /// <summary>
    /// Reserva temporalmente un cupo fundador para este prestador mientras inscribe su tarjeta.
    /// Devuelve el numero de cupo (1..50) o falla con Conflicto si los 50 ya estan tomados.
    /// </summary>
    Task<Resultado<short>> ReservarCupoFundadorAsync(
        Guid perfilPrestadorId, TimeSpan? duracion = null, CancellationToken ct = default);

    /// <summary>Marca como definitivo el cupo fundador tras confirmarse el pago o la inscripcion.</summary>
    Task<Resultado> ConfirmarCupoFundadorAsync(Guid perfilPrestadorId, CancellationToken ct = default);

    /// <summary>Libera el cupo fundador si el prestador cancela antes de pagar o pierde el beneficio.</summary>
    Task<Resultado> LiberarCupoFundadorAsync(Guid perfilPrestadorId, CancellationToken ct = default);

    /// <summary>
    /// RN-S7-03 y RN-S7-07. Pasa la suscripcion que YA EXISTE al plan fundador con el mes
    /// gratis corriendo, y devuelve hasta cuando queda vigente.
    ///
    /// La vigencia queda en la fecha MAS LEJANA entre la que ya tenia y el mes gratis: ni
    /// se le quita tiempo que ya estaba prometido, ni se le regalan treinta dias encima de
    /// los que le quedaban. Y nunca crea una segunda suscripcion, porque quien llega aqui
    /// ya tiene una: publicar el perfil arranca la prueba (API-22).
    ///
    /// Vive en este modulo y no en el del cobro porque la vigencia es regla de S7, no del
    /// camino de pago.
    /// </summary>
    /// <param name="medioInscrito">
    /// El identificador del medio de pago que la pasarela acaba de inscribir, para poder
    /// cobrarle el mes siguiente. Se guarda en la suscripcion y no solo en el intento: el
    /// cobro automatico lee la suscripcion, y sin esto **no tendria con que cobrar**.
    /// Nulo lo deja como estaba.
    /// </param>
    Task<Resultado<DateTime>> PasarAFundadorConMesGratisAsync(
        Guid suscripcionId, string? medioInscrito = null, CancellationToken ct = default);
}

/// <summary>
/// Lo que el prestador ve de su propia suscripcion. Sin <c>ReferenciaPasarela</c> a
/// proposito: es dato de facturacion y no tiene nada que hacer en una pantalla.
/// </summary>
public sealed record SuscripcionDto(
    string Plan,
    string Estado,
    bool Vigente,
    bool EnPrueba,
    DateTime? VigenteHasta,
    int DiasRestantes,
    bool RenovacionAutomatica);

/// <summary>
/// Que hizo la corrida diaria. Lo guarda el planificador en <c>ultimo_resumen</c>, asi que
/// son numeros y nunca datos de una persona.
/// </summary>
public sealed record ResumenDelDiario(int Vencidas, int Avisadas)
{
    public override string ToString() => $"{Vencidas} vencidas, {Avisadas} avisadas";
}

/// <summary>Lo minimo para armar el aviso: a quien y cuanto le queda.</summary>
public sealed record SuscripcionPorVencer(
    Guid SuscripcionId,
    Guid PerfilPrestadorId,
    DateTime VigenteHasta,
    int DiasRestantes);

/// <summary>
/// El estado del plan de lanzamiento, listo para pintarse. <paramref name="Mensaje"/> es
/// el texto que ve la persona: la pantalla no tiene que componer el cierre a partir de
/// numeros, ni enterarse por un 409.
/// </summary>
public sealed record PlanFundadorDto(
    bool Abierto,
    int CuposTotales,
    int CuposDisponibles,
    int PrecioMensual,
    string Mensaje);

public sealed record PlanDto(
    short Id,
    string Codigo,
    string Nombre,
    string? Descripcion,
    int PrecioMensual,
    short DiasPrueba,
    int MaxServicios,
    int MaxTarjetas,
    bool PermiteCredenciales);
