using Zass.Aplicacion.Puertos;
using Zass.Dominio.Comun;

namespace Zass.Aplicacion.Pruebas.Dobles;

/// <summary>
/// La pasarela, en memoria. No imita a ninguna de verdad: deja que la prueba FIJE el
/// veredicto, que es lo unico que el caso de uso necesita de ella.
///
/// Las dos palancas existen porque son los dos desenlaces que mas importan y que en
/// produccion no se pueden provocar a voluntad: que la pasarela no conteste al crear el
/// cobro, y que el aviso de vuelta no se pueda leer.
///
/// <para>
/// **Lo que este doble aprendio el 14-nov-2026:** un doble MAS PERMISIVO que la cosa real
/// no es un doble, es un tapon. Aceptaba cobrar cero pesos; <c>PasarelaSimulada</c> lo
/// rechaza, igual que las tres candidatas de verdad. Resultado: 1.650 pruebas en verde y
/// el boton «Quiero mi cupo» devolviendo 400 a todo el mundo. La guarda de monto esta
/// aqui para que ese camino no pueda volver a estar verde estando roto.
/// </para>
/// </summary>
public sealed class PasarelaFalsa : IPasarelaDePagos
{
    public string Nombre => "falsa";

    /// <summary>La pasarela no contesta al crear el cobro.</summary>
    public bool FallaAlIniciar { get; set; }

    /// <summary>El aviso de vuelta no se puede leer ni verificar.</summary>
    public bool FallaAlLeer { get; set; }

    /// <summary>Lo que devuelve <see cref="LeerConfirmacionAsync"/>. Lo fija la prueba.</summary>
    public CobroResuelto? Veredicto { get; set; }

    /// <summary>Cuantas veces se le pidio un cobro. Hay pruebas que miden justamente eso.</summary>
    public int CobrosPedidos { get; private set; }

    /// <summary>La ultima solicitud, para comprobar QUE monto se le mando a cobrar.</summary>
    public SolicitudDeCobro? UltimaSolicitud { get; private set; }

    public Task<Resultado<CobroIniciado>> IniciarCobroAsync(
        SolicitudDeCobro solicitud, CancellationToken ct = default)
    {
        CobrosPedidos++;
        UltimaSolicitud = solicitud;

        // La MISMA guarda que PasarelaSimulada, a proposito: un cobro de cero no existe.
        if (solicitud.Monto <= 0)
            return Task.FromResult(Resultado.Falla<CobroIniciado>(
                Fallas.Validacion, "No se puede cobrar un monto de cero."));

        if (FallaAlIniciar)
            return Task.FromResult(Resultado.Falla<CobroIniciado>(
                Fallas.ReglaDeNegocio, "La pasarela no respondio."));

        return Task.FromResult(Resultado.Ok(new CobroIniciado(
            "ref_" + solicitud.IntentoDePagoId.ToString("N")[..8],
            "https://pasarela.falsa/pagar/" + solicitud.IntentoDePagoId)));
    }

    /// <summary>La pasarela no contesta al cobrar contra el medio inscrito.</summary>
    public bool FallaAlCobrar { get; set; }

    /// <summary>
    /// Cuantas veces se le pidio un cobro RECURRENTE. Separado de
    /// <see cref="CobrosPedidos"/> porque la prueba de idempotencia del trabajo diario
    /// mide justamente este: dos corridas, un cobro.
    /// </summary>
    public int CobrosRecurrentes { get; private set; }

    /// <summary>La ultima solicitud de cobro recurrente, para ver QUE medio se uso.</summary>
    public SolicitudDeCobro? UltimoCobroRecurrente { get; private set; }

    /// <summary>
    /// Lo que devuelve <see cref="CobrarAsync"/>. Lo fija la prueba, igual que
    /// <see cref="Veredicto"/>; si no se fija, se aprueba por el monto pedido.
    /// </summary>
    public bool RechazaElCobro { get; set; }

    public Task<Resultado<CobroResuelto>> CobrarAsync(
        SolicitudDeCobro solicitud, CancellationToken ct = default)
    {
        CobrosRecurrentes++;
        UltimoCobroRecurrente = solicitud;

        if (solicitud.Monto <= 0)
            return Task.FromResult(Resultado.Falla<CobroResuelto>(
                Fallas.Validacion, "No se puede cobrar un monto de cero."));

        if (FallaAlCobrar)
            return Task.FromResult(Resultado.Falla<CobroResuelto>(
                Fallas.ReglaDeNegocio, "La pasarela no respondio."));

        return Task.FromResult(Resultado.Ok(new CobroResuelto(
            IntentoDePagoId: solicitud.IntentoDePagoId,
            ReferenciaPasarela: "rec_" + solicitud.IntentoDePagoId.ToString("N")[..8],
            Aprobado: !RechazaElCobro,
            MontoConfirmado: solicitud.Monto,
            MotivoFallo: RechazaElCobro ? "Tarjeta rechazada." : null)));
    }

    /// <summary>La pasarela no contesta al inscribir el medio de pago.</summary>
    public bool FallaAlInscribir { get; set; }

    /// <summary>Cuantas veces se le pidio una inscripcion.</summary>
    public int InscripcionesPedidas { get; private set; }

    /// <summary>La ultima inscripcion pedida, para comprobar QUE se le mando.</summary>
    public SolicitudDeInscripcion? UltimaInscripcion { get; private set; }

    public Task<Resultado<CobroIniciado>> IniciarInscripcionAsync(
        SolicitudDeInscripcion solicitud, CancellationToken ct = default)
    {
        InscripcionesPedidas++;
        UltimaInscripcion = solicitud;

        if (FallaAlInscribir)
            return Task.FromResult(Resultado.Falla<CobroIniciado>(
                Fallas.ReglaDeNegocio, "La pasarela no respondio."));

        return Task.FromResult(Resultado.Ok(new CobroIniciado(
            "ins_" + solicitud.IntentoDePagoId.ToString("N")[..8],
            "https://pasarela.falsa/inscribir/" + solicitud.IntentoDePagoId)));
    }

    public Task<Resultado<CobroResuelto>> LeerConfirmacionAsync(
        AvisoDePasarela aviso, CancellationToken ct = default)
    {
        if (FallaAlLeer || Veredicto is null)
            return Task.FromResult(Resultado.Falla<CobroResuelto>(
                Fallas.NoAutorizado, "Aviso no reconocido."));

        return Task.FromResult(Resultado.Ok(Veredicto));
    }
}
