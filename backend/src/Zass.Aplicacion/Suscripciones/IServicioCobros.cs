using Zass.Aplicacion.Puertos;
using Zass.Dominio.Comun;

namespace Zass.Aplicacion.Suscripciones;

/// <summary>
/// MOD-13 - S7. Cobrar la mensualidad.
///
/// Es la pieza que faltaba para poder lanzar: hasta el 13 de noviembre de 2026
/// <see cref="IServicioSuscripciones.RegistrarPeriodoPagadoAsync"/> existia y no tenia
/// ni un llamador, asi que nadie podia pasar de la prueba gratuita a un mes pagado.
///
/// El flujo entero, en tres pasos y con una sola salida a internet:
/// <list type="number">
/// <item><see cref="CotizarAsync"/> dice cuanto se va a cobrar, sin cobrar nada. La
/// pantalla necesita poder mostrar «$4.792 con tu cupon» ANTES de que exista un cobro.</item>
/// <item><see cref="IniciarAsync"/> fija ese precio en un intento (TBL-36) y le pide a la
/// pasarela un cobro. El precio queda guardado en el servidor: es contra ese numero que
/// se compara lo que conteste la pasarela.</item>
/// <item><see cref="ConfirmarAsync"/> recibe el aviso de la pasarela y, si cobro,
/// extiende la suscripcion un mes. Dos veces el mismo aviso extienden UN mes.</item>
/// </list>
/// </summary>
public interface IServicioCobros
{
    /// <summary>
    /// Lo que se cobraria hoy por ese plan, con ese cupon, a esa cuenta. No escribe nada.
    ///
    /// Falla, y no devuelve el precio sin descuento, cuando el cupon no sirve: cobrarle el
    /// precio completo a quien escribio un codigo seria cobrarle algo que no acepto.
    /// </summary>
    Task<Resultado<CotizacionDto>> CotizarAsync(
        Guid perfilPrestadorId, string codigoPlan, string? codigoCupon,
        CancellationToken ct = default);

    /// <summary>
    /// Crea el intento de pago y pide el cobro a la pasarela.
    ///
    /// Idempotente por <c>ClaveIdempotencia</c> (CC-04), igual que API-36: el doble envio
    /// del mismo formulario devuelve el MISMO intento, con la misma direccion de pago, y
    /// no crea un segundo cobro.
    /// </summary>
    Task<Resultado<IntentoDePagoDto>> IniciarAsync(
        IniciarCobroCmd cmd, CancellationToken ct = default);

    /// <summary>
    /// RN-S7-01. Empieza la INSCRIPCION del medio de pago del plan fundador: un intento de
    /// monto CERO que no cobra nada y que, al confirmarse, deja el cupo definitivo y arranca
    /// el mes gratis.
    ///
    /// Es un camino aparte de <see cref="IniciarAsync"/> y no un plan mas barato: aqui no
    /// hay cupon, no hay meses comprados y no nace fila de pago. Lo que se autoriza es el
    /// cobro de DESPUES, que es exactamente lo que hace Oneclick de Transbank.
    ///
    /// Idempotente por la misma clave que el cobro (CC-04): el doble clic no toma dos cupos.
    /// </summary>
    Task<Resultado<IntentoDePagoDto>> IniciarInscripcionFundadorAsync(
        Guid perfilPrestadorId, string claveIdempotencia, CancellationToken ct = default);

    /// <summary>
    /// El aviso de la pasarela, en crudo. Lo traduce el adaptador; aqui se decide.
    ///
    /// Idempotente: el segundo aviso del mismo cobro encuentra el intento ya pagado y
    /// devuelve lo mismo que el primero sin extender nada otra vez.
    /// </summary>
    Task<Resultado<ConfirmacionDto>> ConfirmarAsync(
        AvisoDePasarela aviso, CancellationToken ct = default);

    /// <summary>
    /// C3. El cobro que no lo pide nadie: corre en el trabajo diario y cobra, con el medio
    /// de pago que la persona ya inscribio, la mensualidad de cada suscripcion a la que se
    /// le esta acabando el periodo.
    ///
    /// <para>
    /// Es lo que faltaba para que el mes gratis del lanzamiento termine en un cobro y no
    /// en una desaparicion de la busqueda: hasta que esto existio, el camino de cobro
    /// estaba entero y no habia quien lo disparara.
    /// </para>
    ///
    /// <para>
    /// Idempotente por periodo (CC-04): correr el trabajo dos veces el mismo dia cobra UNA
    /// vez, porque la clave del intento sale de la suscripcion y del periodo y no de la
    /// hora en que corrio. Un rechazo deja el intento <c>Fallido</c> y la suscripcion
    /// intacta (RN-S7-10), y no se reintenta solo.
    /// </para>
    /// </summary>
    Task<ResumenDeCobros> CobrarVencimientosAsync(CancellationToken ct = default);
}

/// <summary>
/// Lo que hizo el cobro automatico en una corrida. Son numeros y nada mas: este resumen
/// se guarda en <c>trabajos_de_fondo.ultimo_resumen</c> y lo lee cualquiera (S7 §6).
/// </summary>
/// <param name="Cobradas">Cobros aprobados, con el mes ya extendido.</param>
/// <param name="Rechazadas">Tarjetas rechazadas. Desenlace normal, no error (RN-S7-10).</param>
/// <param name="SinMedioDePago">
/// Suscripciones a las que les toca cobrar y no tienen medio inscrito que usar. Mientras
/// este numero sea el total, no se esta cobrando a nadie.
/// </param>
/// <param name="YaIntentadas">Ya tenian intento para ese periodo: no se cobra dos veces.</param>
/// <param name="ParaRevisar">
/// Ni cobradas ni rechazadas: la pasarela no contesto, o el monto que confirmo no coincide.
/// Las tiene que mirar una persona.
/// </param>
public sealed record ResumenDeCobros(
    int Cobradas, int Rechazadas, int SinMedioDePago, int YaIntentadas, int ParaRevisar)
{
    public override string ToString() =>
        $"{Cobradas} cobradas, {Rechazadas} rechazadas, {SinMedioDePago} sin medio, "
        + $"{YaIntentadas} ya intentadas, {ParaRevisar} para revisar";
}

/// <param name="ClaveIdempotencia">
/// La genera el cliente al abrir el formulario de pago y la repite en cada reintento del
/// MISMO envio. No se genera aqui: una clave que pone el servidor no protege de nada,
/// porque cada reintento traeria una distinta.
/// </param>
public sealed record IniciarCobroCmd(
    Guid PerfilPrestadorId,
    string CodigoPlan,
    string? CodigoCupon,
    string ClaveIdempotencia);

/// <summary>
/// El desglose que ve la persona antes de pagar. Los tres numeros van separados a
/// proposito: un total sin el precio de lista y el descuento no se puede comprobar.
/// </summary>
public sealed record CotizacionDto(
    string CodigoPlan,
    string NombrePlan,
    int PrecioMensual,
    int Descuento,
    int Total,
    string? Cupon,
    short? PorcentajeDescuento,
    bool EsElPrimerMesQueSePaga,
    string Detalle);

/// <param name="Nueva">
/// Falso cuando es la respuesta a un envio repetido. Lo mira la pantalla para no decir
/// «cobro creado» dos veces, y la prueba para comprobar que de verdad no se creo otro.
/// </param>
public sealed record IntentoDePagoDto(
    Guid Id,
    string CodigoPlan,
    int PrecioMensual,
    int Descuento,
    int Total,
    string? Cupon,
    string Estado,
    string? UrlParaPagar,
    string? ReferenciaPasarela,
    bool Nueva);

public sealed record ConfirmacionDto(
    Guid IntentoDePagoId,
    bool Aprobado,
    string Estado,
    DateTime? VigenteHasta,
    string Mensaje,
    bool Nueva);
