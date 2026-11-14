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

    public Task<Resultado<CobroResuelto>> LeerConfirmacionAsync(
        AvisoDePasarela aviso, CancellationToken ct = default)
    {
        if (FallaAlLeer || Veredicto is null)
            return Task.FromResult(Resultado.Falla<CobroResuelto>(
                Fallas.NoAutorizado, "Aviso no reconocido."));

        return Task.FromResult(Resultado.Ok(Veredicto));
    }
}
