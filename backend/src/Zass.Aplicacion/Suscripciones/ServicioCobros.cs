using Zass.Aplicacion.Comun;
using Zass.Aplicacion.Puertos;
using Zass.Dominio.Comun;
using Zass.Dominio.Suscripciones;

namespace Zass.Aplicacion.Suscripciones;

/// <summary>
/// MOD-13 - S7. El cobro de la mensualidad, con la pasarela detras de un puerto.
///
/// <para>
/// Lo que este servicio NO hace, y es deliberado: no sabe quien cobra. No hay aqui ni un
/// nombre de pasarela, ni un token, ni una direccion de internet. Todo eso vive en el
/// adaptador de <see cref="IPasarelaDePagos"/>. El dia que Pablo elija entre Transbank,
/// Flow y Mercado Pago, este archivo no cambia.
/// </para>
///
/// <para>
/// Lo que SI hace, y tampoco puede estar en el adaptador: fijar el precio. El monto se
/// calcula y se guarda ANTES de salir a internet, y lo que conteste la pasarela se
/// compara contra ese numero guardado. Sin eso, quien manipule la vuelta del navegador
/// compra un mes por un peso.
/// </para>
/// </summary>
public sealed class ServicioCobros : IServicioCobros
{
    /// <summary>
    /// Lo que se le cobra a la persona cada vez. Un mes, siempre. No hay plan anual ni
    /// descuento por pagar varios meses: lo prometido es «cancela cuando quieras», y
    /// cobrar seis meses por adelantado lo volveria mentira.
    /// </summary>
    public const short MesesPorCobro = 1;

    private readonly IRepositorioSuscripciones _repo;
    private readonly IServicioSuscripciones _suscripciones;
    private readonly IPasarelaDePagos _pasarela;
    private readonly IUnidadDeTrabajo _trabajo;
    private readonly IRelojSistema _reloj;
    private readonly IAuditor _auditor;

    public ServicioCobros(
        IRepositorioSuscripciones repo, IServicioSuscripciones suscripciones,
        IPasarelaDePagos pasarela, IUnidadDeTrabajo trabajo, IRelojSistema reloj,
        IAuditor auditor)
    {
        _repo = repo;
        _suscripciones = suscripciones;
        _pasarela = pasarela;
        _trabajo = trabajo;
        _reloj = reloj;
        _auditor = auditor;
    }

    /// <summary>
    /// Los codigos se escriben en mayusculas y sin espacios. Quien escribe su cupon en un
    /// telefono no tiene por que acertar con las mayusculas.
    /// </summary>
    public static string NormalizarCupon(string codigo) =>
        codigo.Trim().ToUpperInvariant();

    // ------------------------------------------------------------- Cotizar

    public async Task<Resultado<CotizacionDto>> CotizarAsync(
        Guid perfilPrestadorId, string codigoPlan, string? codigoCupon,
        CancellationToken ct = default)
    {
        var calculo = await CalcularAsync(perfilPrestadorId, codigoPlan, codigoCupon, ct);
        if (!calculo.Exito || calculo.Valor is null)
            return Resultado.Falla<CotizacionDto>(calculo.Codigo!, calculo.Mensaje!);

        var c = calculo.Valor;
        return Resultado.Ok(new CotizacionDto(
            CodigoPlan: c.Plan.Codigo,
            NombrePlan: c.Plan.Nombre,
            PrecioMensual: c.Plan.PrecioMensual,
            Descuento: c.Descuento,
            Total: c.Total,
            Cupon: c.Cupon?.Codigo,
            PorcentajeDescuento: c.Cupon?.PorcentajeDescuento,
            EsElPrimerMesQueSePaga: c.EsElPrimerMesQueSePaga,
            Detalle: c.Detalle()));
    }

    /// <summary>
    /// El calculo del precio, en UN sitio. Lo usan la cotizacion y el inicio del cobro: si
    /// cada uno lo hiciera por su cuenta, la pantalla podria mostrar un precio y el cobro
    /// salir con otro, y nadie se enteraria hasta que alguien reclamara.
    /// </summary>
    private async Task<Resultado<Calculo>> CalcularAsync(
        Guid perfilPrestadorId, string codigoPlan, string? codigoCupon, CancellationToken ct)
    {
        var ahora = _reloj.Ahora;

        if (!await _repo.ExistePerfilPrestadorAsync(perfilPrestadorId, ct))
            return Resultado.Falla<Calculo>(
                Fallas.NoEncontrado, "Ese perfil de prestador no existe.");

        var plan = await _repo.PlanPorCodigoAsync((codigoPlan ?? "").Trim().ToLowerInvariant(), ct);
        if (plan is null)
            return Resultado.Falla<Calculo>(Fallas.NoEncontrado, "Ese plan no existe.");

        // El plan de prueba no se cobra: es el mes gratis con el que empieza la cuenta.
        // Dejar pasar un cobro de cero crearia un intento que ninguna pasarela aceptaria.
        if (plan.PrecioMensual <= 0)
            return Resultado.Falla<Calculo>(
                Fallas.Validacion,
                $"El plan «{plan.Nombre}» no se paga: es el periodo de prueba.");

        // El cierre al cupo 50, dicho antes de cobrar nada. Quien YA tiene su cupo pasa
        // igual: el precio de lanzamiento es suyo mientras mantenga la suscripcion, y eso
        // vale tambien cuando ya no quedan cupos para nadie mas.
        if (plan.Codigo == ServicioSuscripciones.PlanFundador
            && await _repo.CupoFundadorDeAsync(perfilPrestadorId, ct) is null)
        {
            var estado = await _suscripciones.EstadoDelPlanFundadorAsync(ct);
            if (!estado.Exito) return Resultado.Falla<Calculo>(estado.Codigo!, estado.Mensaje!);
            if (!estado.Valor!.Abierto)
                return Resultado.Falla<Calculo>(
                    Fallas.Conflicto,
                    estado.Valor.Mensaje + " Puedes suscribirte al plan mensual.");
        }

        var yaPago = await _repo.TuvoMesPagadoAsync(perfilPrestadorId, ct);

        Cupon? cupon = null;
        var descuento = 0;
        if (!string.IsNullOrWhiteSpace(codigoCupon))
        {
            var codigo = NormalizarCupon(codigoCupon);
            cupon = await _repo.CuponPorCodigoAsync(codigo, ct);

            if (cupon is null)
                return Resultado.Falla<Calculo>(
                    Fallas.NoEncontrado, "Ese código de descuento no existe.");

            if (!cupon.Sirve(ahora))
                return Resultado.Falla<Calculo>(
                    Fallas.Validacion, "Ese código de descuento ya no está vigente.");

            // Una sola vez por cuenta (decision de Pablo, 12-nov-2026).
            if (await _repo.YaCanjeoCuponAsync(perfilPrestadorId, ct))
                return Resultado.Falla<Calculo>(
                    Fallas.Conflicto, "Esta cuenta ya usó un código de descuento.");

            // El descuento es del primer mes PAGADO, que no es el primer mes de la cuenta:
            // el primero es la prueba gratuita de 30 dias. Quien ya pago uno, ya lo gasto.
            if (yaPago)
                return Resultado.Falla<Calculo>(
                    Fallas.Conflicto,
                    "El descuento es para el primer mes que se paga, y esta cuenta ya pagó uno.");

            descuento = cupon.DescuentoSobre(plan.PrecioMensual);
        }

        return Resultado.Ok(new Calculo(plan, cupon, descuento, !yaPago));
    }

    private sealed record Calculo(
        Plan Plan, Cupon? Cupon, int Descuento, bool EsElPrimerMesQueSePaga)
    {
        public int Total => Plan.PrecioMensual - Descuento;

        public string Detalle() => Cupon is null
            ? $"{Plan.Nombre}: ${Plan.PrecioMensual} al mes."
            : $"{Plan.Nombre}: ${Plan.PrecioMensual} al mes, menos {Cupon.PorcentajeDescuento} % "
              + $"del primer mes con el código {Cupon.Codigo}. Pagas ${Total}.";
    }

    // ------------------------------------------------------------- Iniciar

    public async Task<Resultado<IntentoDePagoDto>> IniciarAsync(
        IniciarCobroCmd cmd, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(cmd.ClaveIdempotencia))
            return Resultado.Falla<IntentoDePagoDto>(
                Fallas.Validacion, "Falta la clave de idempotencia de la solicitud.");

        // CC-04, antes de abrir nada: el doble envio devuelve el MISMO intento. Se vuelve
        // a comprobar dentro de la transaccion, porque entre esta lectura y el INSERT cabe
        // otra solicitud con la misma clave.
        if (await _repo.IntentoPorClaveAsync(cmd.ClaveIdempotencia, ct) is { } yaExiste)
            return await RetomarAsync(yaExiste, ct);

        var calculo = await CalcularAsync(cmd.PerfilPrestadorId, cmd.CodigoPlan, cmd.CodigoCupon, ct);
        if (!calculo.Exito || calculo.Valor is null)
            return Resultado.Falla<IntentoDePagoDto>(calculo.Codigo!, calculo.Mensaje!);
        var c = calculo.Valor;

        // Se paga SOBRE la suscripcion que ya tiene: la prueba se extiende, la vencida
        // revive. Sin suscripcion no hay nada que extender, y eso pasa cuando el perfil
        // todavia no se publico (API-22 es quien arranca la prueba).
        var suscripcion = await _repo.UltimaDePrestadorAsync(cmd.PerfilPrestadorId, ct);
        if (suscripcion is null)
            return Resultado.Falla<IntentoDePagoDto>(
                Fallas.Conflicto,
                "Publica tu perfil antes de pagar: la suscripción nace al publicarlo.");

        var intento = await _trabajo.EnTransaccionAsync(async t =>
        {
            if (await _repo.IntentoPorClaveAsync(cmd.ClaveIdempotencia, t) is { } carrera)
                return carrera;

            // El cupo fundador se RESERVA al empezar a pagar, no al confirmar: si se
            // reservara despues, dos personas podrian pagar por el cupo 50 y una se
            // quedaria sin lo que pago. La reserva dura 25 minutos y, si el pago no
            // llega, el trabajo diario la suelta.
            if (c.Plan.Codigo == ServicioSuscripciones.PlanFundador)
            {
                var cupo = await _suscripciones.ReservarCupoFundadorAsync(
                    cmd.PerfilPrestadorId, ct: t);
                if (!cupo.Exito) return null;
            }

            var nuevo = new IntentoDePago
            {
                SuscripcionId = suscripcion.Id,
                PerfilPrestadorId = cmd.PerfilPrestadorId,
                PlanId = c.Plan.Id,
                Meses = MesesPorCobro,
                MontoBruto = c.Plan.PrecioMensual,
                MontoDescuento = c.Descuento,
                Monto = c.Total,
                Moneda = "CLP",
                CodigoCupon = c.Cupon?.Codigo,
                Estado = EstadoIntentoPago.Pendiente,
                ClaveIdempotencia = cmd.ClaveIdempotencia,
                Pasarela = _pasarela.Nombre,
                CreadoEn = _reloj.Ahora,
            };
            _repo.AgregarIntento(nuevo);

            _auditor.Registrar(Auditar.Entrada(
                "cobro_iniciado", "intentos_de_pago", nuevo.Id, _reloj.Ahora,
                contexto: new
                {
                    plan = c.Plan.Codigo,
                    monto = nuevo.Monto,
                    descuento = nuevo.MontoDescuento,
                    pasarela = _pasarela.Nombre,
                }));

            await _trabajo.GuardarAsync(t);
            return nuevo;
        }, ct);

        // Null solo sale de la reserva fallida: alguien se llevo el ultimo cupo entre la
        // cotizacion y el cobro. Se vuelve a preguntar el estado para contarlo con el
        // mensaje de cierre, y no con un «no se pudo».
        if (intento is null)
        {
            var estado = await _suscripciones.EstadoDelPlanFundadorAsync(ct);
            return Resultado.Falla<IntentoDePagoDto>(
                Fallas.Conflicto,
                (estado.Valor?.Mensaje ?? "El plan fundador está cerrado.")
                + " Puedes suscribirte al plan mensual.");
        }

        // Si ya tenia referencia es porque la transaccion devolvio uno de la carrera.
        if (intento.ReferenciaPasarela is not null)
            return await RetomarAsync(intento, ct);

        return await PedirElCobroAsync(intento, c.Plan, nueva: true, ct);
    }

    /// <summary>
    /// La unica salida a internet. Va FUERA de la transaccion a proposito: una llamada de
    /// red dentro de una transaccion abierta deja una fila bloqueada el tiempo que tarde
    /// la pasarela en contestar, que no es tiempo que controlemos.
    ///
    /// Si la pasarela no contesta, el intento queda Pendiente y SIN referencia. Reintentar
    /// con la misma clave vuelve a pedirselo sin crear un segundo cobro, que es justo lo
    /// que hace falta cuando la caida fue de ellos.
    /// </summary>
    private async Task<Resultado<IntentoDePagoDto>> PedirElCobroAsync(
        IntentoDePago intento, Plan plan, bool nueva, CancellationToken ct)
    {
        var cobro = await _pasarela.IniciarCobroAsync(
            new SolicitudDeCobro(
                IntentoDePagoId: intento.Id,
                Monto: intento.Monto,
                Moneda: intento.Moneda,
                Descripcion: $"ZASS! {plan.Nombre} - {MesesPorCobro} mes"),
            ct);

        if (!cobro.Exito || cobro.Valor is null)
            return Resultado.Falla<IntentoDePagoDto>(
                cobro.Codigo ?? Fallas.ReglaDeNegocio,
                cobro.Mensaje ?? "La pasarela de pagos no respondió. Vuelve a intentarlo.");

        intento.ReferenciaPasarela = cobro.Valor.ReferenciaPasarela;
        intento.UrlParaPagar = cobro.Valor.UrlParaPagar;
        await _trabajo.GuardarAsync(ct);

        return Resultado.Ok(ADto(intento, plan.Codigo, nueva));
    }

    /// <summary>
    /// La respuesta a un envio repetido. Si el intento se quedo sin referencia (la pasarela
    /// no contesto la vez anterior) se le vuelve a pedir; si ya la tenia, se devuelve tal
    /// cual y nadie cobra dos veces.
    /// </summary>
    private async Task<Resultado<IntentoDePagoDto>> RetomarAsync(
        IntentoDePago intento, CancellationToken ct)
    {
        var plan = await _repo.PlanPorIdAsync(intento.PlanId, ct);
        var codigo = plan?.Codigo ?? "desconocido";

        if (intento.Estado == EstadoIntentoPago.Pendiente && intento.ReferenciaPasarela is null
            && plan is not null)
            return await PedirElCobroAsync(intento, plan, nueva: false, ct);

        return Resultado.Ok(ADto(intento, codigo, nueva: false));
    }

    // -------------------------------------------- Inscripcion del fundador (RN-S7-01)

    public async Task<Resultado<IntentoDePagoDto>> IniciarInscripcionFundadorAsync(
        Guid perfilPrestadorId, string claveIdempotencia, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(claveIdempotencia))
            return Resultado.Falla<IntentoDePagoDto>(
                Fallas.Validacion, "Falta la clave de idempotencia de la solicitud.");

        // CC-04, igual que el cobro: el doble clic devuelve el MISMO intento y no toma un
        // segundo cupo.
        if (await _repo.IntentoPorClaveAsync(claveIdempotencia, ct) is { } yaExiste)
            return await RetomarAsync(yaExiste, ct);

        // Se reusa el calculo por dos cosas que si importan —que el perfil exista y que el
        // plan fundador este ABIERTO (RN-S7-06)—, y por ninguna mas: el monto de una
        // inscripcion es cero y no se consulta.
        var calculo = await CalcularAsync(
            perfilPrestadorId, ServicioSuscripciones.PlanFundador, null, ct);
        if (!calculo.Exito || calculo.Valor is null)
            return Resultado.Falla<IntentoDePagoDto>(calculo.Codigo!, calculo.Mensaje!);
        var plan = calculo.Valor.Plan;

        var suscripcion = await _repo.UltimaDePrestadorAsync(perfilPrestadorId, ct);
        if (suscripcion is null)
            return Resultado.Falla<IntentoDePagoDto>(
                Fallas.Conflicto,
                "Publica tu perfil antes de tomar tu cupo: la suscripción nace al publicarlo.");

        var intento = await _trabajo.EnTransaccionAsync(async t =>
        {
            if (await _repo.IntentoPorClaveAsync(claveIdempotencia, t) is { } carrera)
                return carrera;

            // El cupo se reserva al EMPEZAR, por el mismo motivo que en el cobro: si se
            // reservara al confirmar, dos personas podrian inscribirse por el cupo 50.
            var cupo = await _suscripciones.ReservarCupoFundadorAsync(perfilPrestadorId, ct: t);
            if (!cupo.Exito) return null;

            var nuevo = new IntentoDePago
            {
                SuscripcionId = suscripcion.Id,
                PerfilPrestadorId = perfilPrestadorId,
                PlanId = plan.Id,
                Tipo = TipoDeIntento.Inscripcion,
                // Cero meses y cero pesos: no compra nada, autoriza el cobro de despues.
                Meses = 0,
                MontoBruto = 0,
                MontoDescuento = 0,
                Monto = 0,
                Moneda = "CLP",
                CodigoCupon = null,
                Estado = EstadoIntentoPago.Pendiente,
                ClaveIdempotencia = claveIdempotencia,
                Pasarela = _pasarela.Nombre,
                CreadoEn = _reloj.Ahora,
            };
            _repo.AgregarIntento(nuevo);

            _auditor.Registrar(Auditar.Entrada(
                "inscripcion_fundador_iniciada", "intentos_de_pago", nuevo.Id, _reloj.Ahora,
                contexto: new { cupo = cupo.Valor, pasarela = _pasarela.Nombre }));

            await _trabajo.GuardarAsync(t);
            return nuevo;
        }, ct);

        // Null solo sale de la reserva fallida: alguien se llevo el ultimo cupo entre la
        // comprobacion y la reserva.
        if (intento is null)
        {
            var estado = await _suscripciones.EstadoDelPlanFundadorAsync(ct);
            return Resultado.Falla<IntentoDePagoDto>(
                Fallas.Conflicto,
                (estado.Valor?.Mensaje ?? "El plan fundador está cerrado.")
                + " Puedes suscribirte al plan mensual.");
        }

        if (intento.ReferenciaPasarela is not null) return await RetomarAsync(intento, ct);

        return await PedirElCobroAsync(intento, plan, nueva: true, ct);
    }

    private static IntentoDePagoDto ADto(IntentoDePago i, string codigoPlan, bool nueva) =>
        new(i.Id, codigoPlan, i.MontoBruto, i.MontoDescuento, i.Monto, i.CodigoCupon,
            i.Estado.ToString(), i.UrlParaPagar, i.ReferenciaPasarela, nueva);

    // -------------------------------------------- El cobro del vencimiento (C3)

    /// <summary>
    /// Con cuanta antelacion se cobra, en dias.
    ///
    /// <para>
    /// NO es un calendario nuevo: es el umbral de aviso mas grande que ya existe
    /// (<see cref="ServicioSuscripciones.DiasDeAviso"/>). Se cobra el dia en que la
    /// suscripcion entra en «por vencer», no el dia del vencimiento, por dos razones que
    /// valen plata:
    /// </para>
    ///
    /// <list type="number">
    /// <item>Un rechazo deja margen. Cobrar el ultimo dia convierte una tarjeta sin saldo
    /// en desaparecer de la busqueda (RN-S3-09) el mismo dia, sin que nadie se enterara.
    /// Cobrando aqui, al rechazo le quedan los dias del umbral, y el aviso del umbral
    /// pequeño —el de 1 dia, que ya existe y ya se manda— llega como ultima llamada
    /// porque un cobro rechazado no mueve <c>VigenteHasta</c>.</item>
    /// <item>Cobrar antes no regala ni quita dias:
    /// <see cref="IServicioSuscripciones.RegistrarPeriodoPagadoAsync"/> suma el mes SOBRE
    /// lo que quedaba, asi que quien se adelanta una semana sigue teniendo su mes
    /// completo.</item>
    /// </list>
    ///
    /// <para>
    /// Y un efecto que es correcto y conviene saber: a quien se le cobra bien ya no le
    /// llega el aviso de «te queda poco», porque deja de estar por vencer. Avisarle de un
    /// vencimiento que acabamos de pagar seria mentirle.
    /// </para>
    /// </summary>
    public static int DiasAntesDeVencer => ServicioSuscripciones.DiasDeAviso.Max();

    /// <summary>
    /// CC-04. La clave del cobro automatico, determinista: la suscripcion y el periodo
    /// que se esta cobrando.
    ///
    /// <para>
    /// No la pone un cliente porque aqui no hay cliente: la pone el servidor, y por eso
    /// tiene que salir siempre igual de los mismos datos. Correr el trabajo dos veces el
    /// mismo dia produce la MISMA clave, y entonces el segundo intento no nace: lo para
    /// la comprobacion de abajo y, si dos replicas entran a la vez, lo para el indice
    /// unico <c>ux_intento_pago_clave</c>, que es la garantia de verdad.
    /// </para>
    ///
    /// <para>
    /// El periodo es <c>VigenteHasta</c>, no la fecha de hoy: con la fecha de hoy, correr
    /// el trabajo dos dias seguidos dentro de la ventana de cobro daria dos claves
    /// distintas y cobraria dos veces el mismo mes. Y como un cobro bueno mueve
    /// <c>VigenteHasta</c> un mes, el mes siguiente la clave cambia sola.
    /// </para>
    /// </summary>
    public static string ClaveDeCobroAutomatico(Guid suscripcionId, DateTime vigenteHasta) =>
        $"cobro-automatico:{suscripcionId:D}:{vigenteHasta:yyyy-MM-dd}";

    public async Task<ResumenDeCobros> CobrarVencimientosAsync(CancellationToken ct = default)
    {
        // La misma consulta que usa el aviso, con el mismo umbral. Si algun dia cambia
        // DiasDeAviso, el cobro se mueve con el y no se queda en otro calendario.
        var porVencer = await _suscripciones.PorVencerAsync(DiasAntesDeVencer, ct);

        int cobradas = 0, rechazadas = 0, sinMedio = 0, yaIntentadas = 0, paraRevisar = 0;

        foreach (var cercana in porVencer)
        {
            var s = await _repo.PorIdAsync(cercana.SuscripcionId, ct);
            if (s is null) continue;

            var plan = await _repo.PlanPorIdAsync(s.PlanId, ct);
            if (plan is null) continue;

            // El precio sale del MISMO sitio que la cotizacion y el cobro a mano, y sin
            // cupon: un codigo de descuento lo escribe una persona, y aqui no hay nadie
            // escribiendo. Esto tambien es lo que descarta, sin contarlo, a quien esta en
            // el plan de prueba: ese mes es gratis y no se cobra nunca.
            var calculo = await CalcularAsync(cercana.PerfilPrestadorId, plan.Codigo, null, ct);
            if (!calculo.Exito || calculo.Valor is null) continue;

            // Con QUE se cobra. La suscripcion tiene que traer la referencia del medio de
            // pago que la persona inscribio; sin ella no hay contra que cobrar y NO se
            // supone nada. Se cuenta y se audita, que es como un agujero se ve en vez de
            // taparse: hoy esta columna no la escribe nadie (ver el informe de C3).
            if (string.IsNullOrWhiteSpace(s.ReferenciaPasarela))
            {
                sinMedio++;
                _auditor.Registrar(Auditar.Entrada(
                    "cobro_sin_medio_inscrito", "suscripciones", s.Id, _reloj.Ahora,
                    contexto: new { diasRestantes = cercana.DiasRestantes }));
                continue;
            }

            var clave = ClaveDeCobroAutomatico(s.Id, s.VigenteHasta);

            // Ya hubo un intento para esta suscripcion y este periodo. Da igual como
            // acabo: no se cobra otra vez hoy. Un rechazo NO se reintenta solo, porque la
            // politica de reintento y la «gracia» son decision de Pablo y todavia no
            // existen (RN-S7-10 solo dice que el rechazo es normal).
            if (await _repo.IntentoPorClaveAsync(clave, ct) is not null)
            {
                yaIntentadas++;
                continue;
            }

            var intento = await _trabajo.EnTransaccionAsync(async t =>
            {
                // Entre la lectura de arriba y este INSERT cabe otra corrida, igual que en
                // IniciarAsync.
                if (await _repo.IntentoPorClaveAsync(clave, t) is not null)
                    return (IntentoDePago?)null;

                var nuevo = new IntentoDePago
                {
                    SuscripcionId = s.Id,
                    PerfilPrestadorId = cercana.PerfilPrestadorId,
                    PlanId = plan.Id,
                    Meses = MesesPorCobro,
                    MontoBruto = plan.PrecioMensual,
                    MontoDescuento = 0,
                    Monto = calculo.Valor.Total,
                    Moneda = "CLP",
                    CodigoCupon = null,
                    Estado = EstadoIntentoPago.Pendiente,
                    ClaveIdempotencia = clave,
                    Pasarela = _pasarela.Nombre,
                    CreadoEn = _reloj.Ahora,
                };
                _repo.AgregarIntento(nuevo);

                // El cupo fundador NO se toca: quien renueva ya lo tiene confirmado, y
                // volverle a poner plazo de reserva seria arriesgarse a que el trabajo
                // diario se lo suelte.
                _auditor.Registrar(Auditar.Entrada(
                    "cobro_automatico_iniciado", "intentos_de_pago", nuevo.Id, _reloj.Ahora,
                    contexto: new
                    {
                        plan = plan.Codigo,
                        monto = nuevo.Monto,
                        diasRestantes = cercana.DiasRestantes,
                        pasarela = _pasarela.Nombre,
                    }));

                await _trabajo.GuardarAsync(t);
                return nuevo;
            }, ct);

            if (intento is null)
            {
                yaIntentadas++;
                continue;
            }

            // La salida a internet, FUERA de la transaccion, por lo mismo que en
            // PedirElCobroAsync: una llamada de red con una transaccion abierta deja filas
            // bloqueadas el tiempo que tarde la pasarela en contestar.
            var cobro = await _pasarela.CobrarAsync(
                new SolicitudDeCobro(
                    IntentoDePagoId: intento.Id,
                    Monto: intento.Monto,
                    Moneda: intento.Moneda,
                    Descripcion: $"ZASS! {plan.Nombre} - {MesesPorCobro} mes")
                {
                    MedioInscrito = s.ReferenciaPasarela,
                },
                ct);

            // La pasarela no contesto: no se sabe si cobro. El intento se queda Pendiente
            // con su clave, nadie extiende nada, y lo mira una persona. Reintentar a
            // ciegas lo que no se sabe si se cobro es como se cobra dos veces.
            if (!cobro.Exito || cobro.Valor is null)
            {
                paraRevisar++;
                continue;
            }

            // El mismo camino que el aviso de la pasarela. El rechazo deja el intento
            // Fallido y la suscripcion EXACTAMENTE como estaba (RN-S7-10).
            var resuelto = await ResolverAsync(cobro.Valor, ct);
            if (!resuelto.Exito || resuelto.Valor is null) paraRevisar++;
            else if (resuelto.Valor.Aprobado) cobradas++;
            else rechazadas++;
        }

        // Los contadores de arriba son numeros y nada mas: este resumen lo lee cualquiera
        // que mire trabajos_de_fondo (S7 §6).
        await _trabajo.GuardarAsync(ct);
        return new ResumenDeCobros(cobradas, rechazadas, sinMedio, yaIntentadas, paraRevisar);
    }

    // ------------------------------------------------------------- Confirmar

    public async Task<Resultado<ConfirmacionDto>> ConfirmarAsync(
        AvisoDePasarela aviso, CancellationToken ct = default)
    {
        // Traducir y verificar el aviso es trabajo del adaptador: es el unico que sabe
        // como firma su pasarela. Si no lo puede leer, no se toca nada.
        var leido = await _pasarela.LeerConfirmacionAsync(aviso, ct);
        if (!leido.Exito || leido.Valor is null)
            return Resultado.Falla<ConfirmacionDto>(
                leido.Codigo ?? Fallas.Validacion,
                leido.Mensaje ?? "No se pudo leer la confirmación de la pasarela.");

        return await ResolverAsync(leido.Valor, ct);
    }

    /// <summary>
    /// Lo que se hace con un veredicto, venga del aviso de la pasarela (
    /// <see cref="ConfirmarAsync"/>) o del cobro automatico (
    /// <see cref="CobrarVencimientosAsync"/>).
    ///
    /// <para>
    /// Esta extraido para que esos dos caminos no se bifurquen: la idempotencia del
    /// intento, la comparacion del monto, el canje del cupon y la UNICA llamada a
    /// <see cref="IServicioSuscripciones.RegistrarPeriodoPagadoAsync"/> viven aqui y en
    /// ningun otro sitio. Dos copias de esta logica serian dos reglas de vigencia, y la
    /// segunda se enteraria tarde de cada arreglo de la primera.
    /// </para>
    /// </summary>
    private Task<Resultado<ConfirmacionDto>> ResolverAsync(
        CobroResuelto r, CancellationToken ct)
    {
        return _trabajo.EnTransaccionAsync(async t =>
        {
            // El bloqueo de fila. La segunda confirmacion del mismo cobro ESPERA aqui y,
            // cuando entra, lee el estado ya cambiado. Es lo mismo que hace CC-01 con el
            // bloque horario, y por la misma razon: comprobar y escribir sin bloqueo deja
            // una ventana en la que los dos creen ser el primero.
            var intento = await _repo.TomarIntentoAsync(r.IntentoDePagoId, t);
            if (intento is null)
                return Resultado.Falla<ConfirmacionDto>(
                    Fallas.NoEncontrado, "Ese cobro no existe.");

            // Aqui esta la idempotencia que importa: ya pagado, se devuelve lo mismo que
            // la primera vez y NO se vuelve a extender. La garantia ultima no es este
            // «if», es el indice unico ux_pago_intento: un intento, un pago como maximo.
            if (intento.Estado == EstadoIntentoPago.Pagado)
            {
                var s = await _repo.PorIdAsync(intento.SuscripcionId, t);
                return Resultado.Ok(new ConfirmacionDto(
                    intento.Id, Aprobado: true, intento.Estado.ToString(),
                    s?.VigenteHasta, "Este pago ya estaba registrado.", Nueva: false));
            }

            if (intento.Estado != EstadoIntentoPago.Pendiente)
                return Resultado.Falla<ConfirmacionDto>(
                    Fallas.Conflicto,
                    $"Ese cobro ya se cerró como {intento.Estado.ToString().ToLowerInvariant()}. "
                    + "Empieza un pago nuevo.");

            if (!r.Aprobado)
            {
                intento.MarcarFallido(_reloj.Ahora, r.MotivoFallo);

                // Una inscripcion rechazada suelta el cupo al tiro en vez de dejarlo
                // bloqueado los 25 minutos de la reserva: el cupo 50 es escaso y quien no
                // pudo inscribir su medio de pago no lo esta usando.
                if (intento.Tipo == TipoDeIntento.Inscripcion)
                    await _suscripciones.LiberarCupoFundadorAsync(intento.PerfilPrestadorId, t);

                _auditor.Registrar(Auditar.Entrada(
                    "cobro_rechazado", "intentos_de_pago", intento.Id, _reloj.Ahora,
                    contexto: new { motivo = r.MotivoFallo }));
                await _trabajo.GuardarAsync(t);

                // Exito del caso de uso, no del cobro: una tarjeta rechazada es un
                // desenlace normal y la pantalla tiene que poder contarlo con calma.
                return Resultado.Ok(new ConfirmacionDto(
                    intento.Id, Aprobado: false, intento.Estado.ToString(), null,
                    r.MotivoFallo ?? "El pago no se completó. Puedes intentarlo otra vez.",
                    Nueva: true));
            }

            // Lo que la pasarela dice que cobro, contra lo que guardamos antes de salir a
            // internet. No se extiende nada si no coinciden: un mes a mitad de precio por
            // un aviso manipulado es peor que un cobro que hay que revisar a mano.
            if (r.MontoConfirmado is { } confirmado && confirmado != intento.Monto)
            {
                _auditor.Registrar(Auditar.Entrada(
                    "cobro_monto_no_coincide", "intentos_de_pago", intento.Id, _reloj.Ahora,
                    contexto: new { esperado = intento.Monto, confirmado }));
                await _trabajo.GuardarAsync(t);
                return Resultado.Falla<ConfirmacionDto>(
                    Fallas.Conflicto,
                    "El monto confirmado no coincide con el del cobro. No se registró nada.");
            }

            intento.MarcarPagado(_reloj.Ahora);
            if (r.ReferenciaPasarela is not null) intento.ReferenciaPasarela = r.ReferenciaPasarela;

            // RN-S7-01: una INSCRIPCION no extiende un mes pagado ni crea fila de pago,
            // porque no se cobro nada. Lo que hace es dejar el cupo definitivo y arrancar
            // el mes gratis.
            if (intento.Tipo == TipoDeIntento.Inscripcion)
            {
                await _suscripciones.ConfirmarCupoFundadorAsync(intento.PerfilPrestadorId, t);

                var cambio = await _suscripciones.PasarAFundadorConMesGratisAsync(
                    intento.SuscripcionId, intento.ReferenciaPasarela, t);
                if (!cambio.Exito)
                    return Resultado.Falla<ConfirmacionDto>(cambio.Codigo!, cambio.Mensaje!);

                _auditor.Registrar(Auditar.Entrada(
                    "inscripcion_fundador_confirmada", "intentos_de_pago", intento.Id,
                    _reloj.Ahora, contexto: new { vigenteHasta = cambio.Valor }));

                await _trabajo.GuardarAsync(t);

                return Resultado.Ok(new ConfirmacionDto(
                    intento.Id, Aprobado: true, intento.Estado.ToString(), cambio.Valor,
                    "Tu cupo de fundador quedó confirmado y tu mes gratis empezó a correr.",
                    Nueva: true));
            }

            // El llamador que faltaba. Toda la regla de vigencia sigue viviendo alli y no
            // aqui: quien paga el dia 25 de un mes de 30 no pierde los 5 que le quedaban.
            var pagado = await _suscripciones.RegistrarPeriodoPagadoAsync(
                intento.SuscripcionId, intento.Meses, intento.Monto,
                intento.ReferenciaPasarela, intento.Id, t);
            if (!pagado.Exito)
                return Resultado.Falla<ConfirmacionDto>(pagado.Codigo!, pagado.Mensaje!);

            // El canje nace con el pago y no antes: si naciera al crear el intento, una
            // tarjeta rechazada le quemaria el cupon a quien no llego a pagar nada.
            if (intento.CodigoCupon is { } codigoCupon)
                _repo.AgregarCanje(new CanjeDeCupon
                {
                    CodigoCupon = codigoCupon,
                    PerfilPrestadorId = intento.PerfilPrestadorId,
                    PagoId = pagado.Valor,
                    MontoDescontado = intento.MontoDescuento,
                    CanjeadoEn = _reloj.Ahora,
                });

            // Pagado el cupo fundador, deja de ser una reserva con plazo y es suyo.
            var plan = await _repo.PlanPorIdAsync(intento.PlanId, t);
            if (plan?.Codigo == ServicioSuscripciones.PlanFundador)
                await _suscripciones.ConfirmarCupoFundadorAsync(intento.PerfilPrestadorId, t);

            _auditor.Registrar(Auditar.Entrada(
                "cobro_confirmado", "intentos_de_pago", intento.Id, _reloj.Ahora,
                contexto: new { monto = intento.Monto, cupon = intento.CodigoCupon }));

            await _trabajo.GuardarAsync(t);

            var suscripcion = await _repo.PorIdAsync(intento.SuscripcionId, t);
            return Resultado.Ok(new ConfirmacionDto(
                intento.Id, Aprobado: true, intento.Estado.ToString(),
                suscripcion?.VigenteHasta,
                "Pago registrado. Tu suscripción quedó vigente un mes más.", Nueva: true));
        }, ct);
    }
}
