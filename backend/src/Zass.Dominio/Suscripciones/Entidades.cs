using Zass.Dominio.Comun;
using Zass.Dominio.Identidad;

namespace Zass.Dominio.Suscripciones;

// S7 · Suscripciones y Pagos.
//
// Quien OFRECE un servicio paga una mensualidad; quien BUSCA no paga nunca.
// La pasarela de pago llega despues: estas tres tablas existen ya para que
// conectarla no obligue a migrar la base ni a tocar el resto del modelo.
//
// Regla de visibilidad: la busqueda solo muestra prestadores con suscripcion
// en estado Prueba o Activa (ver EstaVigente).

/// <summary>TBL-29. Catalogo de planes.</summary>
public class Plan
{
    public short Id { get; set; }
    public string Codigo { get; set; } = "";
    public string Nombre { get; set; } = "";
    public string? Descripcion { get; set; }
    /// <summary>Precio mensual en pesos chilenos. 0 = plan gratuito.</summary>
    public int PrecioMensual { get; set; }
    public short DiasPrueba { get; set; } = 30;
    public int MaxServicios { get; set; } = 10;
    public int MaxTarjetas { get; set; } = 3;
    public bool PermiteCredenciales { get; set; } = true;
    public bool Activo { get; set; } = true;
}

/// <summary>TBL-30. La suscripcion de un prestador. Un prestador, una suscripcion vigente.</summary>
public class Suscripcion
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid PerfilPrestadorId { get; set; }
    public short PlanId { get; set; }
    public EstadoSuscripcion Estado { get; set; } = EstadoSuscripcion.Prueba;
    public DateTime IniciaEn { get; set; } = DateTime.UtcNow;
    /// <summary>Fin del periodo pagado o de la prueba.</summary>
    public DateTime VigenteHasta { get; set; }
    public DateTime? CanceladaEn { get; set; }
    public bool RenovacionAutomatica { get; set; } = true;

    /// <summary>
    /// Identificador del cliente en la pasarela. Nulo hasta que exista la pasarela;
    /// aqui se guardara el id de Transbank, Flow, Mercado Pago o quien sea.
    /// Nunca se guarda un numero de tarjeta en esta base.
    /// </summary>
    public string? ReferenciaPasarela { get; set; }

    public PerfilPrestador? PerfilPrestador { get; set; }
    public Plan? Plan { get; set; }
    public List<Pago> Pagos { get; set; } = new();

    /// <summary>Lo que decide si el prestador aparece en la busqueda.</summary>
    public bool EstaVigente(DateTime ahora) =>
        Estado is EstadoSuscripcion.Prueba or EstadoSuscripcion.Activa
        && VigenteHasta > ahora;

    public bool EnPrueba(DateTime ahora) =>
        Estado == EstadoSuscripcion.Prueba && VigenteHasta > ahora;

    public int DiasRestantes(DateTime ahora) =>
        Math.Max(0, (int)Math.Ceiling((VigenteHasta - ahora).TotalDays));

    public void Vencer()
    {
        Estado = EstadoSuscripcion.Vencida;
        RenovacionAutomatica = false;
    }

    public void Cancelar()
    {
        Estado = EstadoSuscripcion.Cancelada;
        CanceladaEn = DateTime.UtcNow;
        RenovacionAutomatica = false;
    }

    /// <summary>Extiende el periodo tras un pago confirmado. La llamara la pasarela.</summary>
    public void RegistrarPeriodoPagado(DateTime ahora, int meses = 1)
    {
        var baseFecha = VigenteHasta > ahora ? VigenteHasta : ahora;
        VigenteHasta = baseFecha.AddMonths(meses);
        Estado = EstadoSuscripcion.Activa;
    }
}

/// <summary>TBL-31. Historial de cobros. Sin datos de tarjeta: solo la referencia externa.</summary>
public class Pago
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SuscripcionId { get; set; }
    public int Monto { get; set; }
    public string Moneda { get; set; } = "CLP";
    public EstadoPago Estado { get; set; } = EstadoPago.Pendiente;
    public DateTime PeriodoDesde { get; set; }
    public DateTime PeriodoHasta { get; set; }
    /// <summary>Identificador de la transaccion en la pasarela.</summary>
    public string? ReferenciaPasarela { get; set; }

    /// <summary>
    /// El intento que lo origino (TBL-36). Nulo en los pagos registrados a mano, que
    /// son los que existian antes de que hubiera forma de cobrar.
    ///
    /// El indice unico parcial ux_pago_intento sobre esta columna es la garantia DE LA
    /// BASE de que la misma confirmacion no regala dos meses: no es una comprobacion en
    /// C# que se pueda saltar una condicion de carrera.
    /// </summary>
    public Guid? IntentoDePagoId { get; set; }
    public string? MotivoFallo { get; set; }
    public DateTime CreadoEn { get; set; } = DateTime.UtcNow;
    public DateTime? PagadoEn { get; set; }

    public Suscripcion? Suscripcion { get; set; }
}

/// <summary>
/// TBL-33. Cupos del plan fundador: exactamente 50 filas, una por numero.
/// perfil_prestador_id nulo = cupo libre. El indice unico parcial
/// (ux_cupo_fundador_perfil) impide que un mismo perfil tome dos cupos.
/// La reserva temporal (reservado_hasta) evita que dos inscripciones
/// simultaneas tomen el mismo cupo; si vence sin pago confirmado, el
/// trabajo diario la libera.
/// </summary>
public class CupoFundador
{
    /// <summary>1..50. PK fija, sembrada con el esquema; nunca se genera.</summary>
    public short Numero { get; set; }

    /// <summary>Perfil que reservo u ocupo el cupo. Nulo = libre.</summary>
    public Guid? PerfilPrestadorId { get; set; }

    /// <summary>Hasta cuando vale la reserva temporal. Con perfil asignado y fecha nula = cupo confirmado.</summary>
    public DateTime? ReservadoHasta { get; set; }

    public PerfilPrestador? PerfilPrestador { get; set; }
}

// ----------------------------------------------------------------- El cobro
//
// Hasta el 13 de noviembre de 2026 no habia ninguna forma de cobrar: Suscripcion y Pago
// existian, y RegistrarPeriodoPagado no tenia ni un solo llamador. Lo que faltaba no era
// una pasarela, era la pieza intermedia: ALGO que represente "esta persona quiere pagar
// esto, por este precio, y todavia no se sabe si pago". Eso es IntentoDePago.
//
// La pasarela real (Transbank, Flow, Mercado Pago) no esta decidida. El intento es
// justamente lo que permite que no haga falta decidirla todavia: el adaptador de la
// pasarela traduce de su mundo al intento, y nadie mas sabe quien cobra.

/// <summary>
/// TBL-36. Un cobro empezado: cuanto, a quien, por que plan, y como termino.
///
/// Existe por dos razones que no se pueden resolver con la tabla de pagos:
///
/// 1. El precio se fija AQUI, en el servidor, antes de salir a la pasarela. Si el monto
///    viniera en la confirmacion sin nada contra lo que compararlo, quien manipule la
///    vuelta de la pasarela compraria un mes por un peso.
/// 2. La idempotencia (CC-04). El intento es lo que hace que confirmar dos veces no
///    regale dos meses: la segunda confirmacion encuentra el intento ya en
///    <see cref="EstadoIntentoPago.Pagado"/> y no vuelve a extender nada. La garantia
///    ultima no es esa comprobacion, es el indice unico ux_pago_intento sobre
///    <see cref="Pago.IntentoDePagoId"/>: un intento no puede tener dos pagos.
/// </summary>
public class IntentoDePago
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid SuscripcionId { get; set; }

    /// <summary>Repetido a proposito: la suscripcion puede cambiar y este dato no.</summary>
    public Guid PerfilPrestadorId { get; set; }

    /// <summary>El plan que se esta comprando. 'fundador' y 'mensual' cuestan distinto.</summary>
    public short PlanId { get; set; }

    /// <summary>Cuantos meses compra este intento. Hoy siempre 1.</summary>
    public short Meses { get; set; } = 1;

    /// <summary>Precio de lista del plan, antes del cupon.</summary>
    public int MontoBruto { get; set; }

    /// <summary>Lo que descuenta el cupon. Cero si no hay cupon.</summary>
    public int MontoDescuento { get; set; }

    /// <summary>Lo que se le cobra de verdad: <see cref="MontoBruto"/> menos el descuento.</summary>
    public int Monto { get; set; }

    public string Moneda { get; set; } = "CLP";

    /// <summary>El cupon aplicado, si hubo. El canje no nace hasta que el pago se confirma.</summary>
    public string? CodigoCupon { get; set; }

    public EstadoIntentoPago Estado { get; set; } = EstadoIntentoPago.Pendiente;

    /// <summary>
    /// CC-04. La genera el cliente al abrir la pantalla de pago y la manda en cada
    /// reintento del MISMO envio. Con el indice unico, dos envios del mismo formulario
    /// producen UN intento, no dos cobros.
    /// </summary>
    public string ClaveIdempotencia { get; set; } = "";

    /// <summary>Que adaptador creo este intento ('simulada', y manana el que decida Pablo).</summary>
    public string Pasarela { get; set; } = "";

    /// <summary>El identificador del cobro en la pasarela. Nulo hasta que la pasarela responde.</summary>
    public string? ReferenciaPasarela { get; set; }

    /// <summary>A donde hay que mandar a la persona para que pague.</summary>
    public string? UrlParaPagar { get; set; }

    public DateTime CreadoEn { get; set; } = DateTime.UtcNow;
    public DateTime? ResueltoEn { get; set; }
    public string? MotivoFallo { get; set; }

    public Suscripcion? Suscripcion { get; set; }
    public Plan? Plan { get; set; }

    /// <summary>
    /// Pasa a pagado. Devuelve falso si ya estaba resuelto, y ese falso es lo que impide
    /// el segundo mes: quien llama no debe extender la suscripcion si esto dice que no.
    /// </summary>
    public bool MarcarPagado(DateTime ahora)
    {
        if (Estado != EstadoIntentoPago.Pendiente) return false;
        Estado = EstadoIntentoPago.Pagado;
        ResueltoEn = ahora;
        return true;
    }

    /// <summary>Lo rechazo la pasarela. Tampoco se puede resolver dos veces.</summary>
    public bool MarcarFallido(DateTime ahora, string? motivo)
    {
        if (Estado != EstadoIntentoPago.Pendiente) return false;
        Estado = EstadoIntentoPago.Fallido;
        ResueltoEn = ahora;
        MotivoFallo = motivo;
        return true;
    }
}

/// <summary>
/// TBL-34. Un codigo de descuento. Decision de Pablo del 12-nov-2026: 20 % sobre el
/// primer mes PAGADO.
///
/// El detalle que hay que tener presente para no equivocarse: el primer mes de vida de
/// la cuenta es gratis (la suscripcion 'Prueba' de 30 dias que crea IniciarPruebaAsync),
/// asi que el cupon cae sobre el SEGUNDO mes, que es el primero que se cobra. Sobre el
/// precio fundador congelado de 5990 eso son 4792.
/// </summary>
public class Cupon
{
    /// <summary>El codigo que escribe la persona. Se guarda y se compara en mayusculas.</summary>
    public string Codigo { get; set; } = "";

    public string? Descripcion { get; set; }

    /// <summary>1..100. El 20 de la decision de Pablo vive en la fila, no en el codigo.</summary>
    public short PorcentajeDescuento { get; set; }

    /// <summary>Despues de esta fecha no se canjea. Un cupon sin vencimiento es para siempre.</summary>
    public DateTime VenceEn { get; set; }

    public bool Activo { get; set; } = true;

    /// <summary>Sirve hoy. La fecha y el interruptor son dos cosas distintas a proposito:
    /// se puede cortar un cupon antes de su vencimiento sin falsear la fecha.</summary>
    public bool Sirve(DateTime ahora) => Activo && VenceEn > ahora;

    /// <summary>
    /// Lo que descuenta sobre un monto. Division entera: el descuento se trunca, nunca
    /// se redondea hacia arriba, para que el cobro no pueda salir mayor que el precio.
    /// Con 20 % sobre 5990 da 1198 exacto.
    /// </summary>
    public int DescuentoSobre(int monto) =>
        monto <= 0 ? 0 : monto * PorcentajeDescuento / 100;
}

/// <summary>
/// TBL-35. Que cupon se aplico a que pago.
///
/// El indice unico ux_canje_cupon_perfil es sobre el PERFIL, no sobre la pareja
/// (cupon, perfil): «una sola vez por cuenta» de la decision de Pablo. Con un unico
/// cupon en circulacion las dos cosas coinciden, pero el dia que haya dos codigos la
/// pareja permitiria encadenarlos, y el descuento es del primer mes pagado, que ocurre
/// una vez en la vida de la cuenta.
///
/// Nace junto con el pago y nunca antes: si naciera al crear el intento, una tarjeta
/// rechazada le quemaria el cupon a quien no llego a pagar nada.
/// </summary>
public class CanjeDeCupon
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string CodigoCupon { get; set; } = "";
    public Guid PerfilPrestadorId { get; set; }

    /// <summary>El pago al que se le aplico. Es lo que hace auditable el descuento.</summary>
    public Guid PagoId { get; set; }

    public int MontoDescontado { get; set; }
    public DateTime CanjeadoEn { get; set; } = DateTime.UtcNow;

    public Cupon? Cupon { get; set; }
    public Pago? Pago { get; set; }
}
