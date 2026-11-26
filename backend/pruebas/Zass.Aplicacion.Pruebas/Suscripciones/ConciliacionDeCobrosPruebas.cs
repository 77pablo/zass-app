using Zass.Aplicacion.Pruebas.Dobles;
using Zass.Aplicacion.Puertos;
using Zass.Aplicacion.Suscripciones;
using Zass.Dominio.Comun;
using Zass.Dominio.Identidad;
using Zass.Dominio.Suscripciones;

namespace Zass.Aplicacion.Pruebas.Suscripciones;

/// <summary>
/// Conciliacion (revision de seguridad del 25-nov-2026). Un cobro automatico cuya respuesta
/// se pierde quedaba Pendiente para siempre: la persona pagaba y no recibia su mes, o se le
/// rechazaba la tarjeta y nadie le avisaba ni se reintentaba. Ahora, cada hora, se le pregunta
/// a la pasarela por los Pendientes de mas de 30 minutos.
///
/// <para>
/// Lo que vigilan: pagado extiende el mes por el camino de siempre; rechazado cierra el intento
/// (y entonces el reintento diario de ES-1 sigue); lo que la pasarela no sabe NO se toca ni se
/// reintenta; y ninguna respuesta rara (otro intento, otro monto) regala un mes.
/// </para>
/// </summary>
public class ConciliacionDeCobrosPruebas
{
    private static readonly DateTime Lanzamiento = new(2026, 11, 23, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime FinDelMesGratis = new(2026, 12, 23, 12, 0, 0, DateTimeKind.Utc);

    private readonly BaseEnMemoria _base = new();
    private readonly RelojFijo _reloj = new(Lanzamiento);
    private readonly PasarelaFalsa _pasarela = new();
    private readonly ColaEnMemoria _cola = new();
    private readonly AuditoriaEnMemoria _auditoria = new();
    private readonly ServicioSuscripciones _suscripciones;
    private readonly ServicioCobros _cobros;

    public ConciliacionDeCobrosPruebas()
    {
        var repo = new RepositorioSuscripcionesEnMemoria(_base);
        _suscripciones = new ServicioSuscripciones(repo, _base, _reloj, new AuditoriaEnMemoria(), _cola);
        _cobros = new ServicioCobros(repo, _suscripciones, _pasarela, _base, _reloj, _auditoria);

        _base.Planes.Add(new Plan { Id = 1, Codigo = "prueba", Nombre = "ZASS! Prueba", PrecioMensual = 0 });
        _base.Planes.Add(new Plan { Id = 2, Codigo = "mensual", Nombre = "ZASS! Mensual", PrecioMensual = 7990 });
        _base.Planes.Add(new Plan { Id = 3, Codigo = "fundador", Nombre = "ZASS! Fundador", PrecioMensual = 5990 });
    }

    // ------------------------------------------------------------------- Montaje

    /// <summary>
    /// Un fundador del lanzamiento con su tarjeta inscrita y, siete dias antes de que se le
    /// acabe el mes gratis, un cobro automatico cuya respuesta se perdio: queda Pendiente.
    /// </summary>
    private async Task<(PerfilPrestador Perfil, IntentoDePago Cobro)> UnCobroConLaRespuestaPerdida()
    {
        var perfil = new PerfilPrestador { Id = Guid.NewGuid(), UsuarioId = Guid.NewGuid() };
        _base.PerfilesPrestador.Add(perfil);
        await _suscripciones.IniciarPruebaAsync(perfil.Id);
        Assert.True((await _suscripciones.TomarCupoFundadorAsync(perfil.Id)).Exito);
        Assert.True((await _cobros.IniciarInscripcionFundadorAsync(perfil.Id, "ins-" + perfil.Id)).Exito);
        var inscripcion = _base.IntentosDePago.Single(i => i.PerfilPrestadorId == perfil.Id);
        _pasarela.Veredicto = new CobroResuelto(inscripcion.Id, "ref-inscripcion", true, 0, null);
        Assert.True((await _cobros.ConfirmarAsync(new AvisoDePasarela(new Dictionary<string, string>()))).Exito);
        _base.Suscripciones.Single(s => s.PerfilPrestadorId == perfil.Id).ReferenciaPasarela = "medio-1";

        _reloj.Avanzar(FinDelMesGratis.AddDays(-7) - _reloj.Ahora);
        _pasarela.FallaAlCobrar = true;
        Assert.Equal(1, (await _cobros.CobrarVencimientosAsync()).ParaRevisar);
        _pasarela.FallaAlCobrar = false;

        var cobro = Cobros(perfil.Id).Single();
        Assert.Equal(EstadoIntentoPago.Pendiente, cobro.Estado);
        return (perfil, cobro);
    }

    private List<IntentoDePago> Cobros(Guid perfilId) =>
        _base.IntentosDePago.Where(i => i.PerfilPrestadorId == perfilId && i.Tipo == TipoDeIntento.Cobro).ToList();

    private Suscripcion SuscripcionDe(Guid perfilId) =>
        _base.Suscripciones.Single(s => s.PerfilPrestadorId == perfilId);

    private List<Notificacion> AvisosDeCobroRechazado() =>
        _cola.Encoladas.Where(n => n.Tipo == TipoNotificacion.CobroRechazado).ToList();

    private void Pasan(TimeSpan cuanto) => _reloj.Avanzar(cuanto);

    private static Resultado<CobroResuelto> Pagado(Guid id, int monto) =>
        Resultado.Ok(new CobroResuelto(id, "flow-9001", Aprobado: true, MontoConfirmado: monto, MotivoFallo: null));

    private static Resultado<CobroResuelto> Rechazado(Guid id) =>
        Resultado.Ok(new CobroResuelto(id, "flow-9001", Aprobado: false, MontoConfirmado: null, MotivoFallo: "Flow rechazó el pago."));

    // ------------------------------------------------------------------- Pruebas

    [Fact]
    public void La_espera_es_media_hora_y_el_tope_cincuenta()
    {
        Assert.Equal(TimeSpan.FromMinutes(30), ServicioCobros.EsperaAntesDeConciliar);
        Assert.Equal(50, ServicioCobros.TopeDeConciliacionPorCorrida);
    }

    [Fact]
    public async Task Si_la_pasarela_dice_pagado_se_extiende_el_mes_como_con_el_aviso()
    {
        var (perfil, cobro) = await UnCobroConLaRespuestaPerdida();
        _pasarela.Consulta = id => Pagado(id, 5990);

        Pasan(TimeSpan.FromHours(1));
        var resumen = await _cobros.ConciliarCobrosAutomaticosAsync();

        Assert.Equal(new ResumenDeConciliacion(1, 1, 0, 0), resumen);
        Assert.Equal(EstadoIntentoPago.Pagado, cobro.Estado);
        Assert.Equal(5990, Assert.Single(_base.Pagos).Monto);
        Assert.Equal(new DateTime(2027, 1, 23, 12, 0, 0, DateTimeKind.Utc), SuscripcionDe(perfil.Id).VigenteHasta);
        Assert.Contains(_auditoria.Registros, r => r.Accion == "cobro_conciliado" && r.EntidadId == cobro.Id);

        // Correr otra vez no vuelve a preguntar ni a extender: ya no esta Pendiente.
        Pasan(TimeSpan.FromHours(1));
        Assert.Equal(new ResumenDeConciliacion(0, 0, 0, 0), await _cobros.ConciliarCobrosAutomaticosAsync());
        Assert.Single(_pasarela.Consultados);
        Assert.Single(_base.Pagos);
    }

    [Fact]
    public async Task Si_la_pasarela_dice_rechazado_se_cierra_se_avisa_una_vez_y_al_dia_siguiente_se_reintenta()
    {
        var (perfil, cobro) = await UnCobroConLaRespuestaPerdida();
        _pasarela.Consulta = Rechazado;

        Pasan(TimeSpan.FromHours(1));
        var resumen = await _cobros.ConciliarCobrosAutomaticosAsync();

        Assert.Equal(new ResumenDeConciliacion(1, 0, 1, 0), resumen);
        Assert.Equal(EstadoIntentoPago.Fallido, cobro.Estado);
        Assert.Equal(FinDelMesGratis, SuscripcionDe(perfil.Id).VigenteHasta);
        Assert.Equal(perfil.UsuarioId, Assert.Single(AvisosDeCobroRechazado()).DestinatarioId);

        // Antes de esto el Pendiente bloqueaba el reintento de ES-1 para siempre.
        Pasan(TimeSpan.FromDays(1));
        await _cobros.CobrarVencimientosAsync();
        Assert.Equal(2, Cobros(perfil.Id).Count);
        Assert.Equal(EstadoIntentoPago.Pagado, Cobros(perfil.Id).Single(i => i.Id != cobro.Id).Estado);
        Assert.Single(AvisosDeCobroRechazado());
    }

    [Fact]
    public async Task Si_la_pasarela_no_sabe_el_cobro_sigue_pendiente_y_no_se_reintenta()
    {
        var (perfil, cobro) = await UnCobroConLaRespuestaPerdida();
        // Sin Consulta fijada, la pasarela no conoce la orden.

        Pasan(TimeSpan.FromHours(1));
        var resumen = await _cobros.ConciliarCobrosAutomaticosAsync();

        Assert.Equal(new ResumenDeConciliacion(1, 0, 0, 1), resumen);
        Assert.Equal(EstadoIntentoPago.Pendiente, cobro.Estado);
        Assert.DoesNotContain(_auditoria.Registros, r => r.Accion == "cobro_conciliado");

        Pasan(TimeSpan.FromDays(1));
        await _cobros.CobrarVencimientosAsync();
        Assert.Single(Cobros(perfil.Id));
        Assert.Equal(1, _pasarela.CobrosRecurrentes);
    }

    [Fact]
    public async Task Si_la_pasarela_sigue_en_pendiente_no_se_toca_nada()
    {
        var (_, cobro) = await UnCobroConLaRespuestaPerdida();
        _pasarela.Consulta = id => Resultado.Ok(CobroResuelto.EnCurso(id, "flow-9001"));

        Pasan(TimeSpan.FromHours(1));
        Assert.Equal(new ResumenDeConciliacion(1, 0, 0, 1), await _cobros.ConciliarCobrosAutomaticosAsync());
        Assert.Equal(EstadoIntentoPago.Pendiente, cobro.Estado);
        Assert.DoesNotContain(_auditoria.Registros, r => r.EntidadId == cobro.Id
            && (r.Accion == "cobro_conciliado" || r.Accion == "cobro_pendiente"));
    }

    [Fact]
    public async Task Un_cobro_de_menos_de_media_hora_no_se_consulta()
    {
        var (_, cobro) = await UnCobroConLaRespuestaPerdida();
        _pasarela.Consulta = id => Pagado(id, 5990);

        Pasan(TimeSpan.FromMinutes(29));
        Assert.Equal(new ResumenDeConciliacion(0, 0, 0, 0), await _cobros.ConciliarCobrosAutomaticosAsync());
        Assert.Empty(_pasarela.Consultados);

        Pasan(TimeSpan.FromMinutes(2));
        await _cobros.ConciliarCobrosAutomaticosAsync();
        Assert.Equal(cobro.Id, Assert.Single(_pasarela.Consultados));
    }

    [Fact]
    public async Task Un_pagado_con_otro_monto_no_regala_el_mes()
    {
        var (perfil, cobro) = await UnCobroConLaRespuestaPerdida();
        _pasarela.Consulta = id => Pagado(id, 1);

        Pasan(TimeSpan.FromHours(1));
        Assert.Equal(new ResumenDeConciliacion(1, 0, 0, 1), await _cobros.ConciliarCobrosAutomaticosAsync());
        Assert.Equal(EstadoIntentoPago.Pendiente, cobro.Estado);
        Assert.Empty(_base.Pagos);
        Assert.Equal(FinDelMesGratis, SuscripcionDe(perfil.Id).VigenteHasta);
        Assert.Contains(_auditoria.Registros, r => r.Accion == "cobro_monto_no_coincide");
    }

    /// <summary>
    /// Si la pasarela contesta sobre OTRO intento que existe (aqui, un pago desde el navegador
    /// todavia Pendiente), no se resuelve ni el preguntado ni el otro: el otro lo resuelve su
    /// propio aviso.
    /// </summary>
    [Fact]
    public async Task Una_respuesta_sobre_otro_intento_no_resuelve_nada()
    {
        var (perfil, cobro) = await UnCobroConLaRespuestaPerdida();
        var otro = new IntentoDePago
        {
            SuscripcionId = SuscripcionDe(perfil.Id).Id, PerfilPrestadorId = perfil.Id, PlanId = 3,
            Meses = 1, MontoBruto = 5990, Monto = 5990, Moneda = "CLP",
            Estado = EstadoIntentoPago.Pendiente, ClaveIdempotencia = "clave-del-navegador",
            Pasarela = "falsa", CreadoEn = _reloj.Ahora,
        };
        _base.IntentosDePago.Add(otro);
        _pasarela.Consulta = _ => Pagado(otro.Id, 5990);

        Pasan(TimeSpan.FromHours(1));
        Assert.Equal(new ResumenDeConciliacion(1, 0, 0, 1), await _cobros.ConciliarCobrosAutomaticosAsync());
        Assert.Equal(EstadoIntentoPago.Pendiente, cobro.Estado);
        Assert.Equal(EstadoIntentoPago.Pendiente, otro.Estado);
        Assert.Empty(_base.Pagos);
        Assert.Equal(FinDelMesGratis, SuscripcionDe(perfil.Id).VigenteHasta);
    }

    [Fact]
    public async Task Un_pago_manual_pendiente_no_se_consulta()
    {
        var (perfil, cobro) = await UnCobroConLaRespuestaPerdida();
        var manual = new IntentoDePago
        {
            SuscripcionId = SuscripcionDe(perfil.Id).Id, PerfilPrestadorId = perfil.Id, PlanId = 3,
            Meses = 1, MontoBruto = 5990, Monto = 5990, Moneda = "CLP",
            Estado = EstadoIntentoPago.Pendiente, ClaveIdempotencia = "clave-del-navegador",
            Pasarela = "falsa", CreadoEn = _reloj.Ahora,
        };
        _base.IntentosDePago.Add(manual);

        Pasan(TimeSpan.FromHours(1));
        await _cobros.ConciliarCobrosAutomaticosAsync();
        Assert.Equal(cobro.Id, Assert.Single(_pasarela.Consultados));
    }
}
