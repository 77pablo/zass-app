using Microsoft.Extensions.Logging;
using Zass.Aplicacion.Puertos;
using Zass.Dominio.Comun;

namespace Zass.Infraestructura.Pagos;

/// <summary>
/// Una pasarela que puede INSCRIBIR un medio de pago pero no puede COBRAR nunca.
///
/// <para>
/// **El problema que resuelve.** ZASS! abre con 50 cupos fundador y el primer mes gratis:
/// la persona inscribe su medio de pago —tres montos en cero, no se mueve un peso— y el
/// primer cobro llega 30 dias despues. Pero la pasarela de verdad no esta decidida todavia
/// (Flow, y su cuenta de comercio tarda en aprobarse), y <see cref="PasarelaSimulada"/> no
/// se resuelve en Produccion a proposito, porque una pasarela que finge cobrar es la clase
/// de fallo que no se nota hasta que falta el dinero.
/// </para>
///
/// <para>
/// Las dos salidas obvias eran malas. Dejar la simulada en Produccion abre justo ese
/// agujero: el dia 31 el cobro recurrente «aprobaria» pagos que no existen y extenderia
/// suscripciones como pagadas. Y declarar el entorno como Staging para colarla es lo mismo,
/// con una mentira encima.
/// </para>
///
/// <para>
/// **Esta es la tercera.** El invariante que importa se mantiene entero —una pasarela falsa
/// JAMAS finge un cobro— y lo unico que se permite es lo que no mueve dinero: autorizar el
/// medio de pago. El dia que llegue el primer vencimiento sin pasarela real, el cobro
/// **falla de forma ruidosa** en vez de regalar un mes: el intento queda Pendiente, se
/// audita, y alguien lo mira. Que es exactamente lo que uno quiere que pase.
/// </para>
///
/// <para>
/// No se resuelve por omision: hace falta poner <c>Zass:Pasarela=simulada</c> a proposito.
/// Un despliegue que olvide la variable sigue fallando al arrancar el cobro, que era el caso
/// que de verdad preocupaba.
/// </para>
/// </summary>
public sealed class PasarelaSoloInscripcion : IPasarelaDePagos
{
    /// <summary>
    /// El nombre dice lo que es. Queda escrito en cada intento de pago, asi que mirando la
    /// tabla se sabe que ese medio se inscribio sin pasarela real detras.
    /// </summary>
    public const string NombreDeLaPasarela = "solo-inscripcion";

    private const string NoCobra =
        "Esta instalación no tiene pasarela de pagos real configurada, así que no puede "
        + "cobrar. Configura Zass:Pasarela con una pasarela de verdad.";

    private readonly IPasarelaDePagos _interna;
    private readonly ILogger<PasarelaSoloInscripcion> _registro;

    public PasarelaSoloInscripcion(IPasarelaDePagos interna, ILogger<PasarelaSoloInscripcion> registro)
    {
        _interna = interna;
        _registro = registro;

        // Al nivel de aviso y no de informacion: que quien lea los registros de produccion
        // tropiece con esto, porque es un estado transitorio y no una configuracion normal.
        _registro.LogWarning(
            "Pasarela en modo SOLO INSCRIPCION ({Interna} por debajo). Se pueden inscribir "
            + "medios de pago; NINGUN cobro va a funcionar hasta que se configure una "
            + "pasarela real.", _interna.Nombre);
    }

    public string Nombre => NombreDeLaPasarela;

    /// <summary>Lo unico que se permite: autorizar sin mover dinero.</summary>
    public Task<Resultado<CobroIniciado>> IniciarInscripcionAsync(
        SolicitudDeInscripcion solicitud, CancellationToken ct = default) =>
        _interna.IniciarInscripcionAsync(solicitud, ct);

    /// <summary>
    /// La vuelta de la pasarela se sigue leyendo: sin esto, una inscripcion empezada no se
    /// podria confirmar y el cupo se quedaria reservado hasta que venciera la reserva.
    /// </summary>
    public Task<Resultado<CobroResuelto>> LeerConfirmacionAsync(
        AvisoDePasarela aviso, CancellationToken ct = default) =>
        _interna.LeerConfirmacionAsync(aviso, ct);

    /// <summary>
    /// Un cobro con alguien delante. Falla con regla de negocio y no con una excepcion: la
    /// pantalla tiene que poder contarlo con calma, no reventar.
    /// </summary>
    public Task<Resultado<CobroIniciado>> IniciarCobroAsync(
        SolicitudDeCobro solicitud, CancellationToken ct = default)
    {
        _registro.LogError(
            "Se pidio INICIAR UN COBRO de {Monto} {Moneda} (intento {Intento}) y no hay "
            + "pasarela real: no se cobro nada y no se extendio nada.",
            solicitud.Monto, solicitud.Moneda, solicitud.IntentoDePagoId);

        return Task.FromResult(Resultado.Falla<CobroIniciado>(Fallas.ReglaDeNegocio, NoCobra));
    }

    /// <summary>
    /// El cobro recurrente, el del dia del vencimiento. Este es el que importa: aqui no hay
    /// nadie mirando, y es donde una pasarela que finge «aprobado» regalaria meses en
    /// silencio. Falla, se audita, y el intento queda para que lo mire una persona.
    /// </summary>
    public Task<Resultado<CobroResuelto>> CobrarAsync(
        SolicitudDeCobro solicitud, CancellationToken ct = default)
    {
        _registro.LogError(
            "Vencio una suscripcion y se pidio COBRAR {Monto} {Moneda} (intento {Intento}) "
            + "sin pasarela real. No se cobro: el intento queda sin resolver a proposito.",
            solicitud.Monto, solicitud.Moneda, solicitud.IntentoDePagoId);

        return _interna.CobrarAsync(solicitud, ct);   // MUTACION: el modo solo inscripcion SI cobra
    }
}
