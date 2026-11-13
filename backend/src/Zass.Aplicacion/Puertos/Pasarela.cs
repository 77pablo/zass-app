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
