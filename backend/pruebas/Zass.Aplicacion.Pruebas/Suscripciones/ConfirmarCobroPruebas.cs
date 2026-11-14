using Zass.Aplicacion.Pruebas.Dobles;
using Zass.Aplicacion.Puertos;
using Zass.Aplicacion.Suscripciones;
using Zass.Dominio.Comun;
using Zass.Dominio.Identidad;
using Zass.Dominio.Suscripciones;

namespace Zass.Aplicacion.Pruebas.Suscripciones;

/// <summary>
/// MOD-13 · S7. Confirmar es donde se regala plata, y por eso es lo que mas necesitaba
/// pruebas: aqui se extiende el mes, se registra el pago, se quema el cupon y el cupo
/// fundador deja de ser una reserva.
///
/// Lo que estas pruebas vigilan, en una frase cada cosa: que el MISMO aviso dos veces no
/// regale un segundo mes, que un monto que no coincide no extienda nada, que una tarjeta
/// rechazada sea un desenlace normal y no un error del sistema, y que el canje del cupon
/// nazca con el pago y no antes.
///
/// Los numeros van escritos a mano a proposito (7990, 2397, 5593). Una prueba que saca el
/// valor esperado de la misma constante que vigila no vigila nada.
/// </summary>
public class ConfirmarCobroPruebas
{
    private static readonly DateTime Ahora = new(2026, 11, 13, 12, 0, 0, DateTimeKind.Utc);

    private readonly BaseEnMemoria _base = new();
    private readonly RelojFijo _reloj = new(Ahora);
    private readonly PasarelaFalsa _pasarela = new();
    private readonly ServicioSuscripciones _suscripciones;
    private readonly ServicioCobros _cobros;

    public ConfirmarCobroPruebas()
    {
        var repo = new RepositorioSuscripcionesEnMemoria(_base);
        _suscripciones = new ServicioSuscripciones(
            repo, _base, _reloj, new AuditoriaEnMemoria(), new ColaEnMemoria());
        _cobros = new ServicioCobros(
            repo, _suscripciones, _pasarela, _base, _reloj, new AuditoriaEnMemoria());

        // Los tres planes, como los siembra el esquema. El de prueba vale 0: es el mes
        // gratis con el que empieza la cuenta, y no se cobra nunca.
        _base.Planes.Add(new Plan { Id = 1, Codigo = "prueba", Nombre = "ZASS! Prueba", PrecioMensual = 0 });
        _base.Planes.Add(new Plan { Id = 2, Codigo = "mensual", Nombre = "ZASS! Mensual", PrecioMensual = 7990 });
        _base.Planes.Add(new Plan { Id = 3, Codigo = "fundador", Nombre = "ZASS! Fundador", PrecioMensual = 5990 });
    }

    private PerfilPrestador UnPrestador()
    {
        var perfil = new PerfilPrestador { Id = Guid.NewGuid(), UsuarioId = Guid.NewGuid() };
        _base.PerfilesPrestador.Add(perfil);
        return perfil;
    }

    /// <summary>
    /// Iniciar exige tener suscripcion: «la suscripcion nace al publicar el perfil». Se
    /// crea con el mismo camino que usa la aplicacion de verdad (API-22), y no a mano,
    /// para que la fecha de vigencia sea la que seria en produccion.
    /// </summary>
    private PerfilPrestador UnPrestadorConSuscripcion()
    {
        var perfil = UnPrestador();
        _suscripciones.IniciarPruebaAsync(perfil.Id).GetAwaiter().GetResult();
        return perfil;
    }

    private void UnCupon(string codigo, short porcentaje = 30)
        => _base.Cupones.Add(new Cupon
        {
            Codigo = codigo,
            PorcentajeDescuento = porcentaje,
            VenceEn = Ahora.AddDays(30),
            Activo = true,
        });

    /// <summary>El aviso en crudo. No lleva nada: lo que vale es lo que el adaptador LEE.</summary>
    private static AvisoDePasarela UnAviso() =>
        new(new Dictionary<string, string>());

    private async Task<IntentoDePago> UnIntentoPendiente(string plan = "mensual", string? cupon = null)
    {
        var perfil = UnPrestadorConSuscripcion();
        var r = await _cobros.IniciarAsync(new IniciarCobroCmd(perfil.Id, plan, cupon, "k1"));
        Assert.True(r.Exito, "El montaje de la prueba fallo al iniciar: " + r.Mensaje);
        return _base.IntentosDePago.Single();
    }

    // ------------------------------------------------------- El cobro que sale bien

    [Fact]
    public async Task Un_cobro_aprobado_extiende_la_suscripcion_un_mes()
    {
        var intento = await UnIntentoPendiente();
        _pasarela.Veredicto = new CobroResuelto(intento.Id, "ref_1", true, intento.Monto, null);

        var r = await _cobros.ConfirmarAsync(UnAviso());

        Assert.True(r.Exito);
        Assert.True(r.Valor!.Aprobado);
        Assert.True(r.Valor!.Nueva);
        Assert.Equal(EstadoIntentoPago.Pagado, _base.IntentosDePago.Single().Estado);
        var s = _base.Suscripciones.Single(x => x.Id == intento.SuscripcionId);
        Assert.Equal(EstadoSuscripcion.Activa, s.Estado);
        Assert.Single(_base.Pagos);
        // El mes se suma SOBRE lo que le quedaba de prueba (30 dias), no desde hoy: quien
        // paga antes de que se le acabe no pierde los dias que tenia.
        Assert.Equal(Ahora.AddDays(30).AddMonths(1), s.VigenteHasta);
        Assert.Equal(7990, _base.Pagos.Single().Monto);
    }

    // ------------------------------------------------------------- La idempotencia

    [Fact]
    public async Task El_MISMO_aviso_dos_veces_NO_regala_un_segundo_mes()
    {
        var intento = await UnIntentoPendiente();
        _pasarela.Veredicto = new CobroResuelto(intento.Id, "ref_1", true, intento.Monto, null);
        var aviso = UnAviso();

        var primero = await _cobros.ConfirmarAsync(aviso);
        var vigenciaTrasElPrimero = primero.Valor!.VigenteHasta;
        var segundo = await _cobros.ConfirmarAsync(aviso);

        Assert.True(segundo.Exito);                     // 200, no error
        Assert.False(segundo.Valor!.Nueva);
        Assert.Equal(vigenciaTrasElPrimero, segundo.Valor!.VigenteHasta);
        Assert.Single(_base.Pagos);                     // UN pago, no dos
    }

    /// <summary>
    /// La segunda linea de defensa, probada donde vive. <c>ServicioCobros</c> descarta hoy
    /// el booleano de <c>MarcarPagado</c>, asi que ninguna prueba del servicio nota si la
    /// entidad deja de defenderse: esta si.
    /// </summary>
    [Fact]
    public void La_entidad_tampoco_se_deja_pagar_dos_veces()
    {
        var intento = new IntentoDePago { Monto = 7990, Estado = EstadoIntentoPago.Pendiente };

        Assert.True(intento.MarcarPagado(Ahora));
        Assert.False(intento.MarcarPagado(Ahora.AddHours(1)));
        Assert.Equal(EstadoIntentoPago.Pagado, intento.Estado);
        Assert.Equal(Ahora, intento.ResueltoEn);        // la segunda no movio la fecha
    }

    // ------------------------------------------------------------ El monto cuadrado

    [Fact]
    public async Task Un_monto_que_no_coincide_NO_extiende_nada()
    {
        var intento = await UnIntentoPendiente();
        _pasarela.Veredicto = new CobroResuelto(intento.Id, "ref_1", true, 1, null);

        var r = await _cobros.ConfirmarAsync(UnAviso());

        // Un mes por un peso, por un aviso manipulado, es peor que un cobro que hay que
        // revisar a mano.
        Assert.False(r.Exito);
        Assert.Equal(Fallas.Conflicto, r.Codigo);
        Assert.Empty(_base.Pagos);
        Assert.Equal(EstadoIntentoPago.Pendiente, _base.IntentosDePago.Single().Estado);
    }

    // ------------------------------------------------------- Los desenlaces malos

    [Fact]
    public async Task Una_tarjeta_rechazada_es_un_desenlace_normal_y_no_un_error_del_sistema()
    {
        var intento = await UnIntentoPendiente();
        _pasarela.Veredicto = new CobroResuelto(intento.Id, null, false, null, "Tarjeta rechazada");

        var r = await _cobros.ConfirmarAsync(UnAviso());

        Assert.True(r.Exito);                 // el caso de uso funciono
        Assert.False(r.Valor!.Aprobado);      // el cobro no
        Assert.Equal(EstadoIntentoPago.Fallido, _base.IntentosDePago.Single().Estado);
        Assert.Empty(_base.Pagos);
    }

    [Fact]
    public async Task Un_aviso_ilegible_no_toca_nada()
    {
        await UnIntentoPendiente();
        _pasarela.FallaAlLeer = true;

        var r = await _cobros.ConfirmarAsync(UnAviso());

        // Traducir y verificar el aviso es trabajo del adaptador. Si no lo puede leer, no
        // se toca nada: ni el intento, ni la suscripcion, ni los pagos.
        Assert.False(r.Exito);
        Assert.Equal(EstadoIntentoPago.Pendiente, _base.IntentosDePago.Single().Estado);
        Assert.Empty(_base.Pagos);
    }

    [Fact]
    public async Task Un_aviso_de_un_intento_que_no_existe_no_hace_nada()
    {
        _pasarela.Veredicto = new CobroResuelto(Guid.NewGuid(), "ref_1", true, 7990, null);

        var r = await _cobros.ConfirmarAsync(UnAviso());

        Assert.False(r.Exito);
        Assert.Equal(Fallas.NoEncontrado, r.Codigo);
        Assert.Empty(_base.Pagos);
    }

    // ------------------------------------------------------------------- El cupon

    [Fact]
    public async Task El_canje_del_cupon_nace_con_el_pago_y_no_antes()
    {
        UnCupon("LANZAMIENTO30");
        var intento = await UnIntentoPendiente(cupon: "LANZAMIENTO30");
        Assert.Empty(_base.Canjes);           // al iniciar no hay canje
        // 30 % de 7990 = 2397, y se paga 5593. Los tres numeros, a mano.
        Assert.Equal(2397, intento.MontoDescuento);
        Assert.Equal(5593, intento.Monto);

        _pasarela.Veredicto = new CobroResuelto(intento.Id, "ref_1", true, intento.Monto, null);
        await _cobros.ConfirmarAsync(UnAviso());

        var canje = Assert.Single(_base.Canjes);
        Assert.Equal(2397, canje.MontoDescontado);      // candado con literal
        Assert.Equal("LANZAMIENTO30", canje.CodigoCupon);
        Assert.Equal(_base.Pagos.Single().Id, canje.PagoId);
    }

    [Fact]
    public async Task Una_tarjeta_rechazada_NO_le_quema_el_cupon_a_nadie()
    {
        UnCupon("LANZAMIENTO30");
        var intento = await UnIntentoPendiente(cupon: "LANZAMIENTO30");
        _pasarela.Veredicto = new CobroResuelto(intento.Id, null, false, null, "Rechazada");

        await _cobros.ConfirmarAsync(UnAviso());

        // Si el canje naciera al crear el intento, una tarjeta rechazada le quemaria el
        // cupon a quien no llego a pagar nada.
        Assert.Empty(_base.Canjes);
    }

    // ---------------------------------------------------------- El cupo fundador

    [Fact]
    public async Task Pagado_el_plan_fundador_el_cupo_deja_de_ser_una_reserva_y_es_suyo()
    {
        var intento = await UnIntentoPendiente(plan: "fundador");
        _pasarela.Veredicto = new CobroResuelto(intento.Id, "ref_1", true, intento.Monto, null);

        await _cobros.ConfirmarAsync(UnAviso());

        var cupo = _base.CuposFundador.Single(c => c.PerfilPrestadorId == intento.PerfilPrestadorId);
        Assert.Null(cupo.ReservadoHasta);     // sin fecha = confirmado
        Assert.Equal(49, await _suscripciones.CuposFundadorDisponiblesAsync());
    }
}
