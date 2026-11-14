using Zass.Dominio.Comun;

namespace Zass.Aplicacion.Puertos;

/// <summary>
/// El cobro, como puerto. Igual que <see cref="IEnviadorDeCorreo"/>: la aplicacion dice
/// QUE necesita y la infraestructura decide CON QUIEN se hace.
///
/// <para>
/// Cual sera la pasarela de verdad (Transbank, Flow, Mercado Pago) lo decide Pablo y
/// todavia no esta decidido. Este puerto esta escrito para que esa decision sea escribir
/// UN adaptador mas y una linea en AgregarZass, nada mas. Lo que lo hace posible es que
/// aqui no aparece ni un concepto de ninguna de las tres: no hay token, ni buy_order, ni
/// commerceOrder, ni preference_id. Solo «cobra esto» y «esto es lo que contesto».
/// </para>
///
/// <para>
/// Las tres se parecen mas de lo que su documentacion sugiere, y el puerto se quedo con
/// lo que tienen en comun:
/// <list type="number">
/// <item>Se crea el cobro en la pasarela y devuelve un identificador suyo y una
/// direccion a donde mandar a la persona.</item>
/// <item>La persona paga fuera de ZASS!, que es justamente el motivo de usar una
/// pasarela: aqui no entra nunca un numero de tarjeta.</item>
/// <item>La pasarela avisa de como termino, por la vuelta del navegador o por un aviso
/// servidor a servidor. Lo que llega es un puñado de campos distintos en cada una.</item>
/// </list>
/// </para>
///
/// <para>
/// Lo que el puerto deja DELIBERADAMENTE del lado del adaptador: verificar que el aviso
/// viene de quien dice venir (firma, consulta de confirmacion, lista de IP) y traducir
/// sus campos. Lo que NO le deja: decidir cuanto vale un mes ni extender la suscripcion.
/// El adaptador informa; quien decide es <c>ServicioCobros</c>, que compara el monto
/// confirmado contra el que guardo antes de salir a internet.
/// </para>
/// </summary>
public interface IPasarelaDePagos
{
    /// <summary>
    /// Como se llama este adaptador. Queda escrito en el intento: el dia que se cambie de
    /// pasarela, los cobros viejos siguen diciendo quien los cobro.
    /// </summary>
    string Nombre { get; }

    /// <summary>
    /// Crea el cobro en la pasarela. Falla con <see cref="Fallas.ReglaDeNegocio"/> si la
    /// pasarela no contesta: eso no es culpa de quien queria pagar, y el intento queda
    /// pendiente para reintentar con la misma clave.
    /// </summary>
    Task<Resultado<CobroIniciado>> IniciarCobroAsync(
        SolicitudDeCobro solicitud, CancellationToken ct = default);

    /// <summary>
    /// Autoriza un medio de pago SIN cobrar nada. Es lo que necesita el cupo fundador
    /// (RN-S7-01): la persona inscribe su tarjeta, su primer mes es gratis (RN-S7-03) y el
    /// primer cobro llega 30 dias despues.
    ///
    /// <para>
    /// Es un metodo aparte de <see cref="IniciarCobroAsync"/>, y su solicitud NO lleva
    /// monto, porque una inscripcion no tiene monto que llevar. Antes del 14-nov-2026 el
    /// camino del fundador pedia un COBRO de cero pesos, y las tres pasarelas candidatas
    /// rechazan eso igual que lo rechazaba la simulada: nadie podia tomar un cupo. Que el
    /// monto no exista en el tipo es justamente el arreglo — asi el error no se puede
    /// volver a escribir.
    /// </para>
    ///
    /// <para>
    /// Devuelve lo mismo que iniciar un cobro —una referencia y una direccion a la que
    /// mandar a la persona— porque del otro lado pasa lo mismo: la persona autoriza fuera
    /// de ZASS! y la respuesta vuelve por <see cref="LeerConfirmacionAsync"/>. Las tres
    /// candidatas tienen este camino (Oneclick Inscripcion en Transbank, la autorizacion
    /// de Flow, la tarjeta guardada de Mercado Pago).
    /// </para>
    /// </summary>
    Task<Resultado<CobroIniciado>> IniciarInscripcionAsync(
        SolicitudDeInscripcion solicitud, CancellationToken ct = default);

    /// <summary>
    /// Cobra AHORA contra un medio de pago que la persona ya inscribio, sin navegador y
    /// sin que nadie apriete nada: es el cobro del dia del vencimiento (C3).
    ///
    /// <para>
    /// Es un metodo aparte de <see cref="IniciarCobroAsync"/> y no un parametro suyo
    /// porque los dos caminos son distintos de verdad: iniciar devuelve una direccion a
    /// la que mandar a la persona y la respuesta vuelve despues por
    /// <see cref="LeerConfirmacionAsync"/>; esto resuelve en la misma llamada, porque del
    /// otro lado no hay nadie esperando a quien redirigir. Es lo que hacen los cobros
    /// recurrentes de las tres candidatas (Oneclick de Transbank, Flow, Mercado Pago):
    /// se autoriza una vez y despues se cobra servidor a servidor.
    /// </para>
    ///
    /// <para>
    /// Devuelve el MISMO <see cref="CobroResuelto"/> que el aviso entrante, a proposito:
    /// asi el cobro automatico y el aviso de la pasarela terminan en el mismo sitio y la
    /// regla de vigencia, la idempotencia y la comparacion del monto no se escriben dos
    /// veces. <c>Aprobado = false</c> no es un error del sistema: una tarjeta rechazada es
    /// un desenlace normal (RN-S7-10) y deja el intento Fallido.
    /// </para>
    ///
    /// <para>
    /// Falla con <see cref="Fallas.ReglaDeNegocio"/> cuando la pasarela no contesta, que
    /// es distinto de un rechazo: ahi no se sabe si cobro o no, y el intento se queda
    /// Pendiente para que lo mire una persona.
    /// </para>
    /// </summary>
    Task<Resultado<CobroResuelto>> CobrarAsync(
        SolicitudDeCobro solicitud, CancellationToken ct = default);

    /// <summary>
    /// Traduce lo que llego de la pasarela a un veredicto. Es el UNICO sitio donde se
    /// interpretan sus campos.
    ///
    /// Falla si el aviso no es legible o no se puede verificar, y entonces no se toca
    /// nada: preferimos un cobro que hay que revisar a mano antes que un mes regalado
    /// por un aviso falsificado.
    /// </summary>
    Task<Resultado<CobroResuelto>> LeerConfirmacionAsync(
        AvisoDePasarela aviso, CancellationToken ct = default);
}

/// <summary>
/// Lo que la pasarela necesita saber para cobrar, y nada mas. Sin datos de la persona a
/// proposito: ninguna de las tres los exige para un cobro web, y mandarlos seria
/// entregar la lista de prestadores a un tercero sin necesidad.
/// </summary>
/// <param name="IntentoDePagoId">
/// NUESTRO identificador. El adaptador lo manda como su propio numero de orden
/// (buy_order, commerceOrder, external_reference) y es lo que permite que el aviso de
/// vuelta se pueda enlazar con el intento.
/// </param>
/// <param name="Monto">Lo que hay que cobrar, en la unidad menor de la moneda (pesos).</param>
/// <param name="Descripcion">Para el comprobante de la pasarela. Nunca datos personales.</param>
///
/// <remarks>
/// NO lleva la direccion de retorno a proposito, y menos venida del cliente: una
/// direccion de vuelta que elige quien llama es un redirector abierto con forma de
/// formulario de pago. A donde vuelve la persona lo sabe el adaptador, que lo arma con
/// la direccion publica de la configuracion.
/// </remarks>
public sealed record SolicitudDeCobro(
    Guid IntentoDePagoId,
    int Monto,
    string Moneda,
    string Descripcion)
{
    /// <summary>
    /// La referencia del medio de pago que la persona ya inscribio, tal como la devolvio
    /// la pasarela en su dia. Solo la usa <see cref="IPasarelaDePagos.CobrarAsync"/>: es
    /// lo unico que le permite cobrar sin nadie delante.
    ///
    /// <para>
    /// Nula en <see cref="IPasarelaDePagos.IniciarCobroAsync"/>, donde no hace falta
    /// porque es la persona la que va a pagar. Va como propiedad y no como parametro
    /// posicional para no cambiarle la forma al registro que ya usan C1 y sus pruebas.
    /// </para>
    ///
    /// <para>
    /// Nunca es un numero de tarjeta. Es el identificador opaco del medio inscrito, que
    /// es lo unico de la pasarela que entra en esta base (S7 §6).
    /// </para>
    /// </summary>
    public string? MedioInscrito { get; init; }
}

/// <summary>
/// Lo que la pasarela necesita para autorizar un medio de pago sin cobrarlo.
///
/// <para>
/// No lleva monto NI moneda a proposito: una inscripcion no cobra. Si manana hiciera falta
/// cobrar un peso para validar la tarjeta (algunas pasarelas lo hacen y lo devuelven), eso
/// es un detalle del adaptador y se resuelve ahi, no en el puerto.
/// </para>
/// </summary>
public sealed record SolicitudDeInscripcion(
    Guid IntentoDePagoId,
    string Descripcion);

/// <summary>El cobro ya creado en la pasarela.</summary>
public sealed record CobroIniciado(string ReferenciaPasarela, string? UrlParaPagar);

/// <summary>
/// Lo que llego de la pasarela, en crudo y sin interpretar: los campos de la vuelta del
/// navegador o del aviso servidor a servidor.
///
/// Es un diccionario y no un tipo con nombres porque cada pasarela manda los suyos, y
/// ponerle nombres aqui seria elegir la pasarela en el puerto, que es exactamente lo que
/// este diseño existe para evitar.
/// </summary>
public sealed record AvisoDePasarela(IReadOnlyDictionary<string, string> Campos)
{
    public string? Campo(string nombre) =>
        Campos.TryGetValue(nombre, out var v) && !string.IsNullOrWhiteSpace(v) ? v : null;
}

/// <summary>
/// El veredicto, ya traducido.
/// </summary>
/// <param name="IntentoDePagoId">A que intento se refiere, sacado del aviso.</param>
/// <param name="Aprobado">
/// Si la pasarela cobro de verdad. Falso NO es un error del sistema: una tarjeta
/// rechazada es un desenlace normal y deja el intento Fallido.
/// </param>
/// <param name="MontoConfirmado">
/// Cuanto dice la pasarela que cobro. Se compara contra el monto guardado antes de salir
/// a internet; si no coinciden no se extiende nada. Nulo si la pasarela no lo informa, y
/// entonces no hay nada que comparar.
/// </param>
public sealed record CobroResuelto(
    Guid IntentoDePagoId,
    string? ReferenciaPasarela,
    bool Aprobado,
    int? MontoConfirmado,
    string? MotivoFallo);
