using Zass.Aplicacion.Pruebas.Dobles;
using Zass.Aplicacion.Puertos;
using Zass.Aplicacion.Suscripciones;
using Zass.Dominio.Comun;
using Zass.Dominio.Identidad;
using Zass.Dominio.Suscripciones;

namespace Zass.Aplicacion.Pruebas.Suscripciones;

/// <summary>
/// N3 (auditoria del 22-nov-2026). Cinco agujeros por los que se perdia plata de clientes:
/// pagar un plan no cambiaba el plan, un pago «pendiente» de Flow se cerraba como fallido, un
/// cupon usado en dos intentos reventaba al pagar el segundo, no se podia cancelar (y eliminar
/// la cuenta seguia cobrando), y el mes de prueba se repetia gratis cada mes.
///
/// <para>
/// Cada prueba de aqui se vio ROJA rompiendo a proposito el arreglo que vigila (la tabla de
/// mutaciones esta en el PR). Los numeros van escritos a mano: 7990, 12990, 5990.
/// </para>
/// </summary>
public class PagosCorrectosPruebas
{
    private static readonly DateTime Ahora = new(2026, 11, 23, 12, 0, 0, DateTimeKind.Utc);

    private const short Prueba = 1, Mensual = 2, Fundador = 3, Profesional = 4;

    private readonly BaseEnMemoria _base = new();
    private readonly RelojFijo _reloj = new(Ahora);
    private readonly PasarelaFalsa _pasarela = new();
    private readonly ColaEnMemoria _cola = new();
    private readonly ServicioSuscripciones _suscripciones;
    private readonly ServicioCobros _cobros;

    public PagosCorrectosPruebas()
    {
        var repo = new RepositorioSuscripcionesEnMemoria(_base);
        _suscripciones = new ServicioSuscripciones(repo, _base, _reloj, new AuditoriaEnMemoria(), _cola);
        _cobros = new ServicioCobros(repo, _suscripciones, _pasarela, _base, _reloj, new AuditoriaEnMemoria());

        _base.Planes.Add(new Plan { Id = Prueba, Codigo = "prueba", Nombre = "ZASS! Prueba", PrecioMensual = 0 });
        _base.Planes.Add(new Plan { Id = Mensual, Codigo = "mensual", Nombre = "ZASS! Esencial", PrecioMensual = 7990 });
        _base.Planes.Add(new Plan { Id = Fundador, Codigo = "fundador", Nombre = "ZASS! Fundador", PrecioMensual = 5990 });
        _base.Planes.Add(new Plan
        {
            Id = Profesional, Codigo = "profesional", Nombre = "ZASS! Profesional", PrecioMensual = 12990,
            MaxFotosTrabajos = 12, PermiteEstadisticas = true, VerificacionPrioritaria = true,
        });
    }

    // ------------------------------------------------------------------- Montaje

    private PerfilPrestador UnPrestadorConPrueba(EstadoCuenta cuenta = EstadoCuenta.Activa)
    {
        var usuario = new Usuario { Correo = $"p-{Guid.NewGuid():N}@zass.local", Estado = cuenta };
        _base.Usuarios.Add(usuario);
        var perfil = new PerfilPrestador { Id = Guid.NewGuid(), UsuarioId = usuario.Id };
        _base.PerfilesPrestador.Add(perfil);
        Assert.True(_suscripciones.IniciarPruebaAsync(perfil.Id).GetAwaiter().GetResult().Exito);
        return perfil;
    }

    private Suscripcion SuscripcionDe(Guid perfilId) =>
        _base.Suscripciones.Single(s => s.PerfilPrestadorId == perfilId);

    private static AvisoDePasarela UnAviso() => new(new Dictionary<string, string>());

    private async Task<IntentoDePago> IniciarAsync(Guid perfilId, string plan, string? cupon = null, string? clave = null)
    {
        var r = await _cobros.IniciarAsync(new IniciarCobroCmd(perfilId, plan, cupon, clave ?? Guid.NewGuid().ToString()));
        Assert.True(r.Exito, "El montaje fallo al iniciar: " + r.Mensaje);
        return _base.IntentosDePago.Single(i => i.Id == r.Valor!.Id);
    }

    private async Task<ConfirmacionDto> PagarAsync(IntentoDePago intento)
    {
        _pasarela.Veredicto = new CobroResuelto(intento.Id, "ref-" + intento.Id.ToString("N")[..6], true, intento.Monto, null);
        var r = await _cobros.ConfirmarAsync(UnAviso());
        Assert.True(r.Exito, "La confirmacion fallo: " + r.Mensaje);
        return r.Valor!;
    }

    /// <summary>N3: fundador de verdad = cupo tomado (reserva) + tarjeta inscrita y confirmada.</summary>
    private async Task UnFundadorConTarjetaAsync(Guid perfilId)
    {
        Assert.True((await _suscripciones.TomarCupoFundadorAsync(perfilId)).Exito);
        var ins = await _cobros.IniciarInscripcionFundadorAsync(perfilId, "ins-" + perfilId.ToString("N"));
        Assert.True(ins.Exito, "El montaje fallo al inscribir: " + ins.Mensaje);
        var intento = _base.IntentosDePago.Single(i => i.Id == ins.Valor!.Id);
        _pasarela.Veredicto = new CobroResuelto(intento.Id, "ref-ins", true, 0, null);
        Assert.True((await _cobros.ConfirmarAsync(UnAviso())).Exito);
    }

    private void UnCupon(string codigo) => _base.Cupones.Add(new Cupon
    {
        Codigo = codigo, PorcentajeDescuento = 30, VenceEn = Ahora.AddDays(60), Activo = true,
    });

    // ============================================== 1. El plan pagado es el que queda

    [Fact]
    public async Task H1_Pagar_Profesional_deja_la_suscripcion_en_Profesional()
    {
        var p = UnPrestadorConPrueba();
        Assert.Equal(Prueba, SuscripcionDe(p.Id).PlanId);

        await PagarAsync(await IniciarAsync(p.Id, "profesional"));

        var s = SuscripcionDe(p.Id);
        Assert.Equal(Profesional, s.PlanId);
        Assert.Equal(EstadoSuscripcion.Activa, s.Estado);
        // Los 30 dias de prueba que quedaban NO se pierden: el mes se suma encima.
        Assert.Equal(Ahora.AddDays(30).AddMonths(1), s.VigenteHasta);
        Assert.Equal(12990, Assert.Single(_base.Pagos).Monto);
        // Y recibe lo que pago: el tope de fotos del plan Profesional.
        Assert.Equal(12, await _suscripciones.LimiteDeFotosDeTrabajoAsync(p.Id));
    }

    [Fact]
    public async Task H1_Pagar_Esencial_estando_en_Profesional_baja_de_plan_sin_perder_dias()
    {
        var p = UnPrestadorConPrueba();
        await PagarAsync(await IniciarAsync(p.Id, "profesional"));
        var hasta = SuscripcionDe(p.Id).VigenteHasta;

        await PagarAsync(await IniciarAsync(p.Id, "mensual"));

        var s = SuscripcionDe(p.Id);
        Assert.Equal(Mensual, s.PlanId);
        Assert.Equal(hasta.AddMonths(1), s.VigenteHasta);
    }

    [Fact]
    public async Task H1_Pagar_el_plan_fundador_a_mano_deja_el_plan_fundador_y_el_cupo_definitivo()
    {
        var p = UnPrestadorConPrueba();

        await PagarAsync(await IniciarAsync(p.Id, "fundador"));

        Assert.Equal(Fundador, SuscripcionDe(p.Id).PlanId);
        var cupo = _base.CuposFundador.Single(c => c.PerfilPrestadorId == p.Id);
        Assert.Null(cupo.ReservadoHasta);   // confirmado, no reservado
    }

    [Fact]
    public async Task H1_Quien_pago_fundador_y_perdio_la_reserva_sin_cupos_libres_recibe_su_mes_en_Esencial()
    {
        var p = UnPrestadorConPrueba();
        var intento = await IniciarAsync(p.Id, "fundador");
        // Mientras pagaba, se le vencio la reserva y los 50 cupos se los llevaron otros.
        foreach (var c in _base.CuposFundador)
        {
            c.PerfilPrestadorId = Guid.NewGuid();
            c.ReservadoHasta = null;
        }

        var r = await PagarAsync(intento);

        Assert.True(r.Aprobado);
        var s = SuscripcionDe(p.Id);
        Assert.Equal(Mensual, s.PlanId);                  // nunca un fundador numero 51
        Assert.Equal(5990, Assert.Single(_base.Pagos).Monto);   // pero el pago SI se acredita
        Assert.Equal(Ahora.AddDays(30).AddMonths(1), s.VigenteHasta);
    }

    [Fact]
    public async Task H1_El_fundador_que_se_paso_a_Profesional_suelta_el_cupo_al_vencer_sin_pagar()
    {
        var p = UnPrestadorConPrueba();
        await PagarAsync(await IniciarAsync(p.Id, "fundador"));
        await PagarAsync(await IniciarAsync(p.Id, "profesional"));
        Assert.Equal(Profesional, SuscripcionDe(p.Id).PlanId);
        Assert.Contains(_base.CuposFundador, c => c.PerfilPrestadorId == p.Id);

        _reloj.Avanzar(SuscripcionDe(p.Id).VigenteHasta - Ahora + TimeSpan.FromHours(1));
        await _suscripciones.VencerExpiradasAsync();

        Assert.DoesNotContain(_base.CuposFundador, c => c.PerfilPrestadorId == p.Id);
    }

    // ============================================== 2. Pendiente no cierra el intento

    [Fact]
    public async Task H2_Un_aviso_pendiente_no_cierra_el_intento_y_el_pagado_de_despues_acredita()
    {
        var p = UnPrestadorConPrueba();
        var intento = await IniciarAsync(p.Id, "mensual");

        _pasarela.Veredicto = CobroResuelto.EnCurso(intento.Id, "ref-1");
        var pendiente = await _cobros.ConfirmarAsync(UnAviso());

        Assert.True(pendiente.Exito);
        Assert.True(pendiente.Valor!.Pendiente);
        Assert.False(pendiente.Valor.Aprobado);
        Assert.Equal(EstadoIntentoPago.Pendiente, intento.Estado);
        Assert.Empty(_base.Pagos);

        var pagado = await PagarAsync(intento);

        Assert.True(pagado.Aprobado);
        Assert.Equal(EstadoIntentoPago.Pagado, intento.Estado);
        Assert.Equal(7990, Assert.Single(_base.Pagos).Monto);
        Assert.Equal(Mensual, SuscripcionDe(p.Id).PlanId);
    }

    [Fact]
    public async Task H2_Un_cobro_automatico_pendiente_queda_para_revisar_y_no_se_avisa_como_rechazo()
    {
        var p = UnPrestadorConPrueba();
        await PagarAsync(await IniciarAsync(p.Id, "mensual"));
        var s = SuscripcionDe(p.Id);
        s.ReferenciaPasarela = "medio-inscrito";
        var hasta = s.VigenteHasta;
        _reloj.Avanzar(hasta.AddDays(-5) - Ahora);
        _pasarela.DejaPendienteElCobro = true;

        var resumen = await _cobros.CobrarVencimientosAsync();

        Assert.Equal(0, resumen.Rechazadas);
        Assert.Equal(1, resumen.ParaRevisar);
        var cobro = _base.IntentosDePago.Single(i => i.ClaveIdempotencia.StartsWith("cobro-automatico:"));
        Assert.Equal(EstadoIntentoPago.Pendiente, cobro.Estado);
        Assert.Equal(hasta, SuscripcionDe(p.Id).VigenteHasta);
        Assert.DoesNotContain(_cola.Encoladas, n => n.Tipo == TipoNotificacion.CobroRechazado);
    }

    // ============================================== 3. El cupon en dos intentos

    [Fact]
    public async Task H3_No_se_abre_un_segundo_intento_con_cupon_mientras_el_primero_esta_en_curso()
    {
        UnCupon("LANZAMIENTO");
        var p = UnPrestadorConPrueba();
        await IniciarAsync(p.Id, "mensual", "LANZAMIENTO");

        var segundo = await _cobros.IniciarAsync(new IniciarCobroCmd(p.Id, "mensual", "lanzamiento", "otra-clave"));

        Assert.False(segundo.Exito);
        Assert.Equal(Fallas.Conflicto, segundo.Codigo);
        Assert.Single(_base.IntentosDePago);
    }

    [Fact]
    public async Task H3_Si_se_pagan_dos_intentos_con_el_mismo_cupon_los_dos_meses_se_acreditan_y_hay_UN_canje()
    {
        UnCupon("LANZAMIENTO");
        var p = UnPrestadorConPrueba();
        var primero = await IniciarAsync(p.Id, "mensual", "LANZAMIENTO");
        _reloj.Avanzar(TimeSpan.FromMinutes(31));   // pasada la ventana, el segundo se deja abrir
        var segundo = await IniciarAsync(p.Id, "mensual", "LANZAMIENTO");
        Assert.Equal(5593, primero.Monto);
        Assert.Equal(5593, segundo.Monto);

        await PagarAsync(primero);
        _pasarela.Veredicto = new CobroResuelto(segundo.Id, "ref-2", true, segundo.Monto, null);
        var r = await _cobros.ConfirmarAsync(UnAviso());

        Assert.True(r.Exito, r.Mensaje);
        Assert.True(r.Valor!.Aprobado);
        Assert.Equal(EstadoIntentoPago.Pagado, segundo.Estado);
        Assert.Equal(2, _base.Pagos.Count);        // los dos pagos acreditados
        Assert.Single(_base.Canjes);               // y un solo canje
        Assert.Equal(Ahora.AddDays(30).AddMonths(2), SuscripcionDe(p.Id).VigenteHasta);
    }

    // ============================================== 4. Cancelar, y la cuenta eliminada

    [Fact]
    public async Task H4_Cancelar_deja_la_suscripcion_vigente_y_el_cobro_automatico_no_cobra()
    {
        var p = UnPrestadorConPrueba();
        await PagarAsync(await IniciarAsync(p.Id, "mensual"));
        var s = SuscripcionDe(p.Id);
        s.ReferenciaPasarela = "medio-inscrito";
        var hasta = s.VigenteHasta;

        var r = await _suscripciones.CancelarRenovacionAsync(p.Id);

        Assert.True(r.Exito, r.Mensaje);
        Assert.True(r.Valor!.Vigente);
        Assert.True(r.Valor.RenovacionCancelada);
        Assert.False(r.Valor.RenovacionAutomatica);
        Assert.Equal(EstadoSuscripcion.Activa, s.Estado);   // sigue en la busqueda hasta su fecha

        _reloj.Avanzar(hasta.AddDays(-5) - Ahora);
        await _cobros.CobrarVencimientosAsync();
        Assert.Equal(0, _pasarela.CobrosRecurrentes);

        _reloj.Avanzar(TimeSpan.FromDays(6));
        await _cobros.CobrarVencimientosAsync();            // tampoco el cobro tardio
        await _suscripciones.VencerExpiradasAsync();
        Assert.Equal(0, _pasarela.CobrosRecurrentes);
        Assert.Equal(EstadoSuscripcion.Vencida, s.Estado);
    }

    [Fact]
    public async Task H4_El_fundador_que_cancela_pierde_el_cupo_al_vencer()
    {
        var p = UnPrestadorConPrueba();
        await UnFundadorConTarjetaAsync(p.Id);
        await _suscripciones.CancelarRenovacionAsync(p.Id);

        _reloj.Avanzar(SuscripcionDe(p.Id).VigenteHasta - Ahora + TimeSpan.FromHours(1));
        await _suscripciones.VencerExpiradasAsync();

        Assert.DoesNotContain(_base.CuposFundador, c => c.PerfilPrestadorId == p.Id);
    }

    [Fact]
    public async Task H4_Cancelar_sin_suscripcion_vigente_es_un_conflicto()
    {
        var p = UnPrestadorConPrueba();
        _reloj.Avanzar(TimeSpan.FromDays(31));

        var r = await _suscripciones.CancelarRenovacionAsync(p.Id);

        Assert.False(r.Exito);
        Assert.Equal(Fallas.Conflicto, r.Codigo);
    }

    [Theory]
    [InlineData(EstadoCuenta.Eliminada)]
    [InlineData(EstadoCuenta.Suspendida)]
    public async Task H4_A_una_cuenta_eliminada_o_suspendida_no_se_le_cobra(EstadoCuenta estado)
    {
        var p = UnPrestadorConPrueba();
        await PagarAsync(await IniciarAsync(p.Id, "mensual"));
        var s = SuscripcionDe(p.Id);
        s.ReferenciaPasarela = "medio-inscrito";
        _base.Usuarios.Single(u => u.Id == p.UsuarioId).Estado = estado;

        _reloj.Avanzar(s.VigenteHasta.AddDays(-5) - Ahora);
        await _cobros.CobrarVencimientosAsync();

        Assert.Equal(0, _pasarela.CobrosRecurrentes);
    }

    [Fact]
    public async Task H4_Una_cuenta_activa_si_se_cobra_el_control_del_caso_de_arriba()
    {
        var p = UnPrestadorConPrueba();
        await PagarAsync(await IniciarAsync(p.Id, "mensual"));
        var s = SuscripcionDe(p.Id);
        s.ReferenciaPasarela = "medio-inscrito";

        _reloj.Avanzar(s.VigenteHasta.AddDays(-5) - Ahora);
        await _cobros.CobrarVencimientosAsync();

        Assert.Equal(1, _pasarela.CobrosRecurrentes);
    }

    [Fact]
    public async Task H4_Terminar_por_cuenta_eliminada_cancela_olvida_la_tarjeta_y_suelta_el_cupo()
    {
        var p = UnPrestadorConPrueba();
        await UnFundadorConTarjetaAsync(p.Id);

        var r = await _suscripciones.TerminarPorCuentaEliminadaAsync(p.Id);

        Assert.True(r.Exito);
        var s = SuscripcionDe(p.Id);
        Assert.Equal(EstadoSuscripcion.Cancelada, s.Estado);
        Assert.Null(s.ReferenciaPasarela);
        Assert.True(s.RenovacionCancelada);
        Assert.DoesNotContain(_base.CuposFundador, c => c.PerfilPrestadorId == p.Id);
    }

    // ============================================== 5. Una sola prueba gratis

    [Fact]
    public async Task H5_Republicar_con_la_prueba_vencida_NO_regala_otra_prueba()
    {
        var p = UnPrestadorConPrueba();
        _reloj.Avanzar(TimeSpan.FromDays(31));
        await _suscripciones.VencerExpiradasAsync();

        var r = await _suscripciones.IniciarPruebaAsync(p.Id);

        Assert.True(r.Exito);                       // publicar no falla por esto
        Assert.Single(_base.Suscripciones);
        Assert.False(await _suscripciones.EstaVigenteAsync(p.Id));
    }

    [Fact]
    public async Task H5_El_mes_gratis_de_fundador_no_se_encadena_pagando_otro_plan()
    {
        var p = UnPrestadorConPrueba();
        await PagarAsync(await IniciarAsync(p.Id, "mensual"));

        var r = await _suscripciones.TomarCupoFundadorAsync(p.Id);

        Assert.False(r.Exito);
        Assert.Equal(Fallas.Conflicto, r.Codigo);
        Assert.DoesNotContain(_base.CuposFundador, c => c.PerfilPrestadorId == p.Id);
    }
}
