using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Zass.Aplicacion.Puertos;
using Zass.Dominio.Comun;
using Zass.Infraestructura.Configuracion;

namespace Zass.Infraestructura.Pagos;

/// <summary>
/// La pasarela que no sale a internet.
///
/// <para>
/// No es un doble de pruebas: es un adaptador de verdad, registrado en el contenedor y
/// con su propia pantalla de pago. Existe por dos razones.
/// </para>
///
/// <para>
/// La primera es que la pasarela de verdad no esta decidida: Pablo elegira entre
/// Transbank, Flow y Mercado Pago, y mientras no elija, el flujo completo —cotizar,
/// cobrar, confirmar, extender el mes— tiene que poder recorrerse y verse en pantalla.
/// La segunda es que este adaptador es la medida de si el puerto quedo bien: si para
/// escribir el real hay que tocar <see cref="IPasarelaDePagos"/>, el puerto estaba mal
/// pensado, y eso se descubre ahora y no el dia del lanzamiento.
/// </para>
///
/// <para>
/// Decide por regla y no al azar a proposito: un adaptador que aprueba o rechaza segun el
/// monto se puede probar; uno que tira una moneda produce pruebas intermitentes, que son
/// peores que no tener pruebas. Rechaza los montos que terminan en 13 (ver
/// <see cref="MontoQueRechaza"/>), que es la unica forma de recorrer el camino del pago
/// rechazado sin tocar la base a mano.
/// </para>
///
/// <para>
/// **NO se resuelve en Produccion.** <c>AgregarZass</c> lanza al resolver
/// <see cref="IPasarelaDePagos"/> si el entorno es Produccion y <c>Zass:Pasarela</c>
/// resuelve a <see cref="NombreDeLaPasarela"/> —tanto si se pidio a proposito como si la
/// variable no esta puesta y se cayo en el valor por omision—, porque un ZASS! en
/// produccion que regala meses sin cobrar nada es peor que un ZASS! que todavia no cobra.
/// En Development y en Staging si se resuelve, que es para lo que existe.
/// </para>
///
/// <para>
/// Lo que decia este comentario hasta el 13-nov-2026 era mentira: prometia que la
/// aplicacion no arrancaba con este adaptador fuera de desarrollo, y el <c>switch</c> de
/// <c>AgregarZass</c> solo fallaba con un nombre DESCONOCIDO. Sin la variable puesta, la
/// simulada se resolvia en cualquier entorno. La guarda existe desde entonces, y la
/// vigila <c>PasarelaEnProduccionPruebas</c>.
/// </para>
/// </summary>
public sealed class PasarelaSimulada : IPasarelaDePagos
{
    /// <summary>El nombre que queda escrito en cada intento que cobro esta pasarela.</summary>
    public const string NombreDeLaPasarela = "simulada";

    /// <summary>
    /// Los montos cuyos dos ultimos digitos son estos se rechazan. Es la palanca para
    /// recorrer el camino del pago rechazado de punta a punta.
    /// </summary>
    public const int MontoQueRechaza = 13;

    /// <summary>Lo que manda la confirmacion para decir que el cobro salio bien.</summary>
    public const string CampoIntento = "intento";
    public const string CampoEstado = "estado";
    public const string CampoMonto = "monto";
    public const string CampoReferencia = "referencia";
    public const string EstadoAprobado = "aprobado";
    public const string EstadoRechazado = "rechazado";

    private readonly ILogger<PasarelaSimulada> _registro;
    private readonly IOptions<OpcionesZass> _opciones;

    public PasarelaSimulada(IOptions<OpcionesZass> opciones, ILogger<PasarelaSimulada> registro)
    {
        _opciones = opciones;
        _registro = registro;
    }

    public string Nombre => NombreDeLaPasarela;

    public Task<Resultado<CobroIniciado>> IniciarCobroAsync(
        SolicitudDeCobro solicitud, CancellationToken ct = default)
    {
        if (solicitud.Monto <= 0)
            return Task.FromResult(Resultado.Falla<CobroIniciado>(
                Fallas.Validacion, "No se puede cobrar un monto de cero."));

        // La referencia la inventa la pasarela, igual que haria la real. Lleva el prefijo
        // para que un cobro simulado se reconozca a simple vista en la tabla de pagos.
        var referencia = $"sim-{Guid.NewGuid():N}"[..20];

        // La direccion de vuelta la arma el adaptador con la configuracion, nunca con algo
        // que venga del cliente: una direccion de retorno elegida por quien llama es un
        // redirector abierto con forma de formulario de pago.
        var raiz = (_opciones.Value.UrlPublica ?? "").TrimEnd('/');
        var url = $"{raiz}/pagar/simulado?{CampoIntento}={solicitud.IntentoDePagoId}"
                  + $"&{CampoReferencia}={referencia}&{CampoMonto}={solicitud.Monto}";

        _registro.LogWarning(
            "Cobro SIMULADO de {Monto} {Moneda} para el intento {Intento}. "
            + "No se cobro nada de verdad.",
            solicitud.Monto, solicitud.Moneda, solicitud.IntentoDePagoId);

        return Task.FromResult(Resultado.Ok(new CobroIniciado(referencia, url)));
    }

    public Task<Resultado<CobroResuelto>> LeerConfirmacionAsync(
        AvisoDePasarela aviso, CancellationToken ct = default)
    {
        // Lo que una pasarela real haria aqui: comprobar la firma del aviso, o volver a
        // preguntarle por el estado del cobro. Esta no tiene con quien hablar, asi que lo
        // unico que puede verificar es la forma de lo que llego.
        if (!Guid.TryParse(aviso.Campo(CampoIntento), out var intentoId))
            return Task.FromResult(Resultado.Falla<CobroResuelto>(
                Fallas.Validacion,
                $"La confirmación no trae un «{CampoIntento}» que sea un identificador."));

        // Cero es un monto VALIDO: es el de una inscripcion de medio de pago (RN-S7-01),
        // que autoriza el cobro de despues sin cobrar nada. Hasta el 13-nov-2026 esto
        // exigia monto > 0 y por eso una inscripcion no se podia confirmar nunca por este
        // adaptador: las pruebas unitarias no lo veian porque su doble no valida el monto.
        if (!int.TryParse(aviso.Campo(CampoMonto), out var monto) || monto < 0)
            return Task.FromResult(Resultado.Falla<CobroResuelto>(
                Fallas.Validacion, $"La confirmación no trae un «{CampoMonto}» válido."));

        // El estado puede venir forzado (asi se prueba el rechazo desde la pantalla) o
        // deducirse del monto. Cualquier valor distinto de 'aprobado' es un rechazo: ante
        // un campo que no se entiende, no se regala un mes.
        var declarado = aviso.Campo(CampoEstado);
        var aprobado = declarado is null
            ? monto % 100 != MontoQueRechaza
            : declarado.Equals(EstadoAprobado, StringComparison.OrdinalIgnoreCase);

        return Task.FromResult(Resultado.Ok(new CobroResuelto(
            IntentoDePagoId: intentoId,
            ReferenciaPasarela: aviso.Campo(CampoReferencia),
            Aprobado: aprobado,
            MontoConfirmado: monto,
            MotivoFallo: aprobado
                ? null
                : "La pasarela simulada rechazó el pago. No se cobró nada.")));
    }
}
