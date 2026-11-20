using Zass.Aplicacion.Pruebas.Dobles;
using Zass.Aplicacion.Puertos;
using Zass.Aplicacion.Suscripciones;
using Zass.Dominio.Comun;
using Zass.Dominio.Identidad;
using Zass.Dominio.Suscripciones;

namespace Zass.Aplicacion.Pruebas.Suscripciones;

/// <summary>
/// C3 · S7. El cobro que no lo pide nadie.
///
/// <para>
/// Esto es lo que faltaba para poder lanzar: el camino de cobro estaba entero y probado, y
/// no habia quien lo disparara. ZASS! lanza el lunes 23 de noviembre de 2026 con el primer
/// mes gratis; el 23 de diciembre, a los fundadores se les acaba. Sin esto, ese dia se les
/// vence la suscripcion y desaparecen de la busqueda (RN-S3-09) sin que nadie les haya
/// cobrado un peso. Por eso las pruebas corren sobre ESAS fechas y no sobre fechas
/// inventadas: la que tiene que quedar verde es la del lanzamiento.
/// </para>
///
/// <para>
/// Lo que vigilan y ninguna otra: que el mes gratis termine en un cobro, que correr el
/// trabajo dos veces el mismo dia cobre UNA vez, que una tarjeta rechazada no mueva la
/// vigencia (RN-S7-10), y que sin medio de pago inscrito NO se cobre ni se suponga nada.
/// </para>
///
/// <para>
/// Los numeros van escritos a mano (7 dias, 5990, el 23 de diciembre): una prueba que saca
/// el valor esperado de la misma constante que vigila no vigila nada.
/// </para>
/// </summary>
public class CobroAutomaticoPruebas
{
    /// <summary>El dia del lanzamiento. Todo lo demas se cuenta desde aqui.</summary>
    private static readonly DateTime Lanzamiento = new(2026, 11, 23, 12, 0, 0, DateTimeKind.Utc);

    /// <summary>
    /// El dia en que se le acaba el mes gratis a quien tomo su cupo el lanzamiento.
    /// Escrito a mano: es la fecha limite de C3, no una cuenta que haga la prueba.
    /// </summary>
    private static readonly DateTime FinDelMesGratis = new(2026, 12, 23, 12, 0, 0, DateTimeKind.Utc);

    private readonly BaseEnMemoria _base = new();
    private readonly RelojFijo _reloj = new(Lanzamiento);
    private readonly PasarelaFalsa _pasarela = new();
    private readonly ColaEnMemoria _cola = new();
    private readonly ServicioSuscripciones _suscripciones;
    private readonly ServicioCobros _cobros;

    public CobroAutomaticoPruebas()
    {
        var repo = new RepositorioSuscripcionesEnMemoria(_base);
        _suscripciones = new ServicioSuscripciones(
            repo, _base, _reloj, new AuditoriaEnMemoria(), _cola);
        _cobros = new ServicioCobros(
            repo, _suscripciones, _pasarela, _base, _reloj, new AuditoriaEnMemoria());

        _base.Planes.Add(new Plan { Id = 1, Codigo = "prueba", Nombre = "ZASS! Prueba", PrecioMensual = 0 });
        _base.Planes.Add(new Plan { Id = 2, Codigo = "mensual", Nombre = "ZASS! Mensual", PrecioMensual = 7990 });
        _base.Planes.Add(new Plan { Id = 3, Codigo = "fundador", Nombre = "ZASS! Fundador", PrecioMensual = 5990 });
    }

    // ------------------------------------------------------------------- Montaje

    /// <summary>
    /// Un fundador como los del lunes 23: publica su ficha (API-22 arranca la prueba),
    /// toma su cupo e inscribe su medio de pago. Se hace por el camino de verdad y no a
    /// mano, para que la fecha de vigencia sea la que va a ser en produccion.
    /// </summary>
    private PerfilPrestador UnFundadorDelLanzamiento()
    {
        var perfil = new PerfilPrestador { Id = Guid.NewGuid(), UsuarioId = Guid.NewGuid() };
        _base.PerfilesPrestador.Add(perfil);
        _suscripciones.IniciarPruebaAsync(perfil.Id).GetAwaiter().GetResult();
        Assert.True(_suscripciones.TomarCupoFundadorAsync(perfil.Id).GetAwaiter().GetResult().Exito);

        var inscripcion = _cobros.IniciarInscripcionFundadorAsync(perfil.Id, "ins-" + perfil.Id)
            .GetAwaiter().GetResult();
        Assert.True(inscripcion.Exito, "El montaje fallo al inscribir: " + inscripcion.Mensaje);

        var intento = _base.IntentosDePago.Single(i => i.PerfilPrestadorId == perfil.Id);
        _pasarela.Veredicto = new CobroResuelto(intento.Id, "ref-inscripcion", true, 0, null);
        var confirmada = _cobros.ConfirmarAsync(UnAviso()).GetAwaiter().GetResult();
        Assert.True(confirmada.Exito, "El montaje fallo al confirmar: " + confirmada.Mensaje);

        return perfil;
    }

    /// <summary>
    /// Deja en la suscripcion la referencia del medio de pago inscrito.
    ///
    /// <para>
    /// Se pone a mano para no depender del camino de la inscripcion en una prueba que
    /// habla del COBRO. Desde el 14-nov-2026 la inscripcion si deja esa columna llena
    /// —antes no, y ese era el agujero que encontro C3: el cobro recurrente lee
    /// <c>suscripcion.referencia_pasarela</c> y la inscripcion solo guardaba la del
    /// intento—, asi que este ayudante ya no tapa nada: solo fija el escenario.
    /// </para>
    /// </summary>
    private Suscripcion ConMedioInscrito(Guid perfilId, string referencia = "medio-inscrito-1")
    {
        var s = _base.Suscripciones.Single(x => x.PerfilPrestadorId == perfilId);
        s.ReferenciaPasarela = referencia;
        return s;
    }

    /// <summary>
    /// Deja la suscripcion SIN medio de pago, a proposito. Es un escenario real y no un
    /// defecto: alguien cuya inscripcion fue rechazada, o un perfil que se publico antes
    /// de que existiera la inscripcion.
    /// </summary>
    private Suscripcion SinMedioInscrito(Guid perfilId)
    {
        var s = _base.Suscripciones.Single(x => x.PerfilPrestadorId == perfilId);
        s.ReferenciaPasarela = null;
        return s;
    }

    private static AvisoDePasarela UnAviso() => new(new Dictionary<string, string>());

    private Suscripcion SuscripcionDe(Guid perfilId) =>
        _base.Suscripciones.Single(s => s.PerfilPrestadorId == perfilId);

    private IntentoDePago ElCobro(Guid perfilId) =>
        _base.IntentosDePago.Single(
            i => i.PerfilPrestadorId == perfilId && i.Tipo == TipoDeIntento.Cobro);

    /// <summary>Mueve el reloj hasta N dias antes de que se acabe el mes gratis.</summary>
    private void DiasAntesDelFinDelMesGratis(int dias) =>
        _reloj.Avanzar(FinDelMesGratis.AddDays(-dias) - _reloj.Ahora);

    // ------------------------------------------------------- Los candados con literal

    /// <summary>
    /// El calendario del cobro no es nuevo: es el umbral de aviso mas grande que ya
    /// existia. Siete, escrito a mano. Si alguien cambia la antelacion sin pensar en los
    /// avisos, esta prueba se lo dice.
    /// </summary>
    [Fact]
    public void Se_cobra_con_SIETE_dias_de_antelacion()
    {
        Assert.Equal(7, ServicioCobros.DiasAntesDeVencer);
        Assert.Equal(7, ServicioSuscripciones.DiasDeAviso.Max());
    }

    /// <summary>
    /// La clave sale de la suscripcion y del PERIODO que se cobra, no de la hora en que
    /// corrio el trabajo. Con la fecha de hoy dentro, dos corridas en dos dias de la misma
    /// ventana cobrarian dos veces el mismo mes.
    /// </summary>
    [Fact]
    public void La_clave_del_cobro_lleva_la_suscripcion_y_el_periodo()
    {
        var id = new Guid("11111111-2222-3333-4444-555555555555");

        Assert.Equal(
            "cobro-automatico:11111111-2222-3333-4444-555555555555:2026-12-23",
            ServicioCobros.ClaveDeCobroAutomatico(id, FinDelMesGratis));

        // El mismo dia a otra hora es el MISMO periodo y por tanto la misma clave.
        Assert.Equal(
            ServicioCobros.ClaveDeCobroAutomatico(id, FinDelMesGratis),
            ServicioCobros.ClaveDeCobroAutomatico(id, FinDelMesGratis.AddHours(5)));

        // El mes siguiente es otro periodo y otra clave: el cobro del mes que viene no lo
        // bloquea el de este.
        Assert.NotEqual(
            ServicioCobros.ClaveDeCobroAutomatico(id, FinDelMesGratis),
            ServicioCobros.ClaveDeCobroAutomatico(id, FinDelMesGratis.AddMonths(1)));
    }

    // ------------------------------------------------------------- El cobro que sale bien

    /// <summary>
    /// La prueba del lanzamiento. Quien tomo su cupo el 23 de noviembre llega al 16 de
    /// diciembre —siete dias antes de que se le acabe el mes gratis— y se le cobra su
    /// mensualidad de fundador. No desaparece de la busqueda: queda vigente un mes mas.
    /// </summary>
    [Fact]
    public async Task Al_fundador_se_le_cobra_antes_de_que_se_le_acabe_el_mes_gratis()
    {
        var perfil = UnFundadorDelLanzamiento();
        var s = ConMedioInscrito(perfil.Id);
        Assert.Equal(FinDelMesGratis, s.VigenteHasta);   // el mes gratis, como lo dejo C1

        DiasAntesDelFinDelMesGratis(7);
        var resumen = await _cobros.CobrarVencimientosAsync();

        Assert.Equal(1, resumen.Cobradas);
        Assert.Equal(0, resumen.Rechazadas);
        Assert.Equal(0, resumen.SinMedioDePago);

        // El precio congelado del fundador, escrito a mano.
        var cobro = ElCobro(perfil.Id);
        Assert.Equal(5990, cobro.Monto);
        Assert.Equal(EstadoIntentoPago.Pagado, cobro.Estado);
        Assert.Equal(5990, Assert.Single(_base.Pagos).Monto);

        // El mes se suma SOBRE lo que le quedaba, no desde hoy: cobrar una semana antes no
        // le quita esa semana. 23 de diciembre + un mes.
        var despues = SuscripcionDe(perfil.Id);
        Assert.Equal(new DateTime(2027, 1, 23, 12, 0, 0, DateTimeKind.Utc), despues.VigenteHasta);
        Assert.Equal(EstadoSuscripcion.Activa, despues.Estado);
        Assert.True(despues.EstaVigente(_reloj.Ahora));
    }

    /// <summary>
    /// Con QUE se cobra: con el medio que la persona inscribio, que es lo unico que
    /// permite cobrar sin nadie delante.
    /// </summary>
    [Fact]
    public async Task El_cobro_va_con_el_medio_inscrito_de_la_suscripcion()
    {
        var perfil = UnFundadorDelLanzamiento();
        ConMedioInscrito(perfil.Id, "oneclick-abc123");

        DiasAntesDelFinDelMesGratis(7);
        await _cobros.CobrarVencimientosAsync();

        Assert.Equal(1, _pasarela.CobrosRecurrentes);
        Assert.Equal("oneclick-abc123", _pasarela.UltimoCobroRecurrente!.MedioInscrito);
        Assert.Equal(5990, _pasarela.UltimoCobroRecurrente!.Monto);
    }

    /// <summary>
    /// Ocho dias antes todavia no le toca. Junto con la prueba de los siete dias, esto es
    /// lo que fija la ventana: ni antes ni despues.
    /// </summary>
    [Fact]
    public async Task Antes_de_entrar_en_la_ventana_no_se_cobra()
    {
        var perfil = UnFundadorDelLanzamiento();
        ConMedioInscrito(perfil.Id);

        DiasAntesDelFinDelMesGratis(8);
        var resumen = await _cobros.CobrarVencimientosAsync();

        Assert.Equal(0, resumen.Cobradas);
        Assert.Equal(0, _pasarela.CobrosRecurrentes);
        Assert.Empty(_base.Pagos);
        Assert.Equal(FinDelMesGratis, SuscripcionDe(perfil.Id).VigenteHasta);
    }

    // ------------------------------------------------------------- La idempotencia

    /// <summary>
    /// CC-04. El trabajo diario puede correr dos veces el mismo dia —un despliegue a media
    /// mañana, una replica que retoma una reserva vencida— y eso no puede cobrar dos veces.
    ///
    /// Se prueba sobre el cobro RECHAZADO a proposito: cuando el cobro sale bien, la fecha
    /// de vigencia se mueve un mes y la suscripcion sale sola de la ventana, asi que la
    /// clave no se llega a ejercitar. Es con el rechazo —que deja la fecha quieta— cuando
    /// la clave es lo unico que separa una corrida repetida de un segundo cobro.
    /// </summary>
    [Fact]
    public async Task Correr_el_trabajo_dos_veces_el_mismo_dia_no_cobra_dos_veces()
    {
        var perfil = UnFundadorDelLanzamiento();
        ConMedioInscrito(perfil.Id);
        _pasarela.RechazaElCobro = true;

        DiasAntesDelFinDelMesGratis(7);
        var primera = await _cobros.CobrarVencimientosAsync();
        var segunda = await _cobros.CobrarVencimientosAsync();

        Assert.Equal(1, primera.Rechazadas);
        Assert.Equal(0, segunda.Rechazadas);
        Assert.Equal(1, segunda.YaIntentadas);

        // Lo que de verdad importa: UNA sola salida a la pasarela y UN solo intento.
        Assert.Equal(1, _pasarela.CobrosRecurrentes);
        Assert.Single(_base.IntentosDePago, i => i.Tipo == TipoDeIntento.Cobro);
    }

    /// <summary>
    /// Y con el cobro aprobado, tambien: dos corridas, un pago y un mes. Aqui lo que
    /// defiende es la fecha ya movida, pero el resultado que se promete es el mismo.
    /// </summary>
    [Fact]
    public async Task Dos_corridas_con_el_cobro_aprobado_dejan_UN_pago()
    {
        var perfil = UnFundadorDelLanzamiento();
        ConMedioInscrito(perfil.Id);

        DiasAntesDelFinDelMesGratis(7);
        await _cobros.CobrarVencimientosAsync();
        await _cobros.CobrarVencimientosAsync();

        Assert.Single(_base.Pagos);
        Assert.Equal(1, _pasarela.CobrosRecurrentes);
        Assert.Equal(
            new DateTime(2027, 1, 23, 12, 0, 0, DateTimeKind.Utc),
            SuscripcionDe(perfil.Id).VigenteHasta);
    }

    // ------------------------------------------------------------- El rechazo (RN-S7-10)

    /// <summary>
    /// RN-S7-10. Una tarjeta rechazada no es un error del sistema: es un desenlace normal.
    /// El intento queda Fallido y la suscripcion se queda EXACTAMENTE como estaba.
    ///
    /// <para>
    /// No hay aqui ninguna politica de gracia, y es a proposito: los «3 dias de gracia» que
    /// se mencionaron no existen en el codigo y son una decision de Pablo. Mientras no la
    /// tome, el rechazo deja a la persona con los dias que le quedaban —siete, por cobrar
    /// con antelacion— y con el aviso de 1 dia todavia por llegar.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Una_tarjeta_rechazada_deja_el_intento_fallido_y_la_suscripcion_igual()
    {
        var perfil = UnFundadorDelLanzamiento();
        ConMedioInscrito(perfil.Id);
        _pasarela.RechazaElCobro = true;

        DiasAntesDelFinDelMesGratis(7);
        var resumen = await _cobros.CobrarVencimientosAsync();

        Assert.Equal(1, resumen.Rechazadas);
        Assert.Equal(0, resumen.Cobradas);

        Assert.Equal(EstadoIntentoPago.Fallido, ElCobro(perfil.Id).Estado);
        Assert.Empty(_base.Pagos);

        var s = SuscripcionDe(perfil.Id);
        Assert.Equal(FinDelMesGratis, s.VigenteHasta);          // ni un dia menos
        Assert.Equal(EstadoSuscripcion.Prueba, s.Estado);       // ni un estado tocado
        Assert.True(s.EstaVigente(_reloj.Ahora));               // sigue en la busqueda

        // Y su cupo fundador sigue siendo suyo: un cobro rechazado no se lo quita. Eso solo
        // pasa con una INSCRIPCION rechazada, que es otra cosa.
        var cupo = _base.CuposFundador.Single(c => c.PerfilPrestadorId == perfil.Id);
        Assert.Null(cupo.ReservadoHasta);   // confirmado, no reservado: sigue siendo suyo
    }

    /// <summary>
    /// La pasarela que no contesta es otra cosa que un rechazo: no se sabe si cobro. El
    /// intento se queda Pendiente, no se extiende nada, y se cuenta aparte para que una
    /// persona lo mire. Reintentar a ciegas lo que no se sabe si se cobro es como se cobra
    /// dos veces.
    /// </summary>
    [Fact]
    public async Task Si_la_pasarela_no_contesta_el_intento_queda_pendiente_para_revisar()
    {
        var perfil = UnFundadorDelLanzamiento();
        ConMedioInscrito(perfil.Id);
        _pasarela.FallaAlCobrar = true;

        DiasAntesDelFinDelMesGratis(7);
        var resumen = await _cobros.CobrarVencimientosAsync();

        Assert.Equal(1, resumen.ParaRevisar);
        Assert.Equal(0, resumen.Cobradas);
        Assert.Equal(0, resumen.Rechazadas);
        Assert.Equal(EstadoIntentoPago.Pendiente, ElCobro(perfil.Id).Estado);
        Assert.Empty(_base.Pagos);
        Assert.Equal(FinDelMesGratis, SuscripcionDe(perfil.Id).VigenteHasta);
    }

    // ------------------------------------------- Lo que NO se cobra, y por que

    /// <summary>
    /// Cuando no hay con que cobrar, NO se cobra y no se supone nada: se cuenta, se
    /// audita, y se ve en el resumen del trabajo diario. Sigue siendo un caso real aunque
    /// la inscripcion ya deje el medio guardado: una inscripcion rechazada no deja medio,
    /// y los perfiles publicados antes de que existiera la inscripcion tampoco tienen.
    /// </summary>
    [Fact]
    public async Task Sin_medio_de_pago_inscrito_no_se_cobra_nada()
    {
        var perfil = UnFundadorDelLanzamiento();

        // Se deja sin medio A PROPOSITO. Antes esta linea era
        // `Assert.Null(SuscripcionDe(perfil.Id).ReferenciaPasarela)` con el comentario «lo
        // que deja C1 hoy»: o sea, la prueba codificaba el agujero como estado esperado y
        // se puso roja el dia que se tapo (14-nov-2026). Un rojo que aparece porque
        // alguien ARREGLO algo no vigila nada.
        SinMedioInscrito(perfil.Id);
        Assert.Null(SuscripcionDe(perfil.Id).ReferenciaPasarela);

        DiasAntesDelFinDelMesGratis(7);
        var resumen = await _cobros.CobrarVencimientosAsync();

        Assert.Equal(1, resumen.SinMedioDePago);
        Assert.Equal(0, resumen.Cobradas);
        Assert.Equal(0, _pasarela.CobrosRecurrentes);
        Assert.DoesNotContain(_base.IntentosDePago, i => i.Tipo == TipoDeIntento.Cobro);
        Assert.Empty(_base.Pagos);
    }

    /// <summary>
    /// A quien esta en el plan de prueba no se le cobra nunca, aunque tenga medio de pago
    /// inscrito y aunque se le acabe manana: ese mes es gratis y no hay plan elegido
    /// todavia. Cobrarle seria cobrarle algo que no acepto.
    /// </summary>
    [Fact]
    public async Task Al_del_plan_de_prueba_no_se_le_cobra_nunca()
    {
        var perfil = new PerfilPrestador { Id = Guid.NewGuid(), UsuarioId = Guid.NewGuid() };
        _base.PerfilesPrestador.Add(perfil);
        await _suscripciones.IniciarPruebaAsync(perfil.Id);
        ConMedioInscrito(perfil.Id);

        DiasAntesDelFinDelMesGratis(7);
        var resumen = await _cobros.CobrarVencimientosAsync();

        Assert.Equal(0, resumen.Cobradas);
        Assert.Equal(0, resumen.SinMedioDePago);
        Assert.Equal(0, _pasarela.CobrosRecurrentes);
        Assert.Empty(_base.Pagos);
    }

    /// <summary>
    /// El plan mensual se cobra igual que el fundador y a su precio, que es otro. Mismo
    /// camino, numero distinto: 7990 escrito a mano.
    /// </summary>
    [Fact]
    public async Task El_plan_mensual_se_cobra_a_su_precio()
    {
        var perfil = new PerfilPrestador { Id = Guid.NewGuid(), UsuarioId = Guid.NewGuid() };
        _base.PerfilesPrestador.Add(perfil);
        _base.Suscripciones.Add(new Suscripcion
        {
            PerfilPrestadorId = perfil.Id,
            PlanId = 2,                                  // mensual
            Estado = EstadoSuscripcion.Activa,
            IniciaEn = Lanzamiento,
            VigenteHasta = FinDelMesGratis,
            ReferenciaPasarela = "medio-inscrito-2",
        });

        DiasAntesDelFinDelMesGratis(7);
        var resumen = await _cobros.CobrarVencimientosAsync();

        Assert.Equal(1, resumen.Cobradas);
        Assert.Equal(7990, Assert.Single(_base.Pagos).Monto);
    }

    // ------------------------------------------------- ES-1: la gracia de 7 dias

    /// <summary>
    /// Lo que corre el planificador cada dia, en su orden: primero cobrar, despues vencer y
    /// avisar (Planificador.Tareas). Las pruebas de la gracia necesitan las dos mitades.
    /// </summary>
    private async Task CorreElDia()
    {
        await _cobros.CobrarVencimientosAsync();
        await _suscripciones.CorrerDiarioAsync();
    }

    private void UnDiaDespues() => _reloj.Avanzar(TimeSpan.FromDays(1));

    private List<IntentoDePago> LosCobros(Guid perfilId) =>
        _base.IntentosDePago
            .Where(i => i.PerfilPrestadorId == perfilId && i.Tipo == TipoDeIntento.Cobro)
            .ToList();

    private List<Notificacion> AvisosDeCobroRechazado() =>
        _cola.Encoladas.Where(n => n.Tipo == TipoNotificacion.CobroRechazado).ToList();

    private CupoFundador? CupoDe(Guid perfilId) =>
        _base.CuposFundador.SingleOrDefault(c => c.PerfilPrestadorId == perfilId);

    /// <summary>
    /// El rechazo manda «actualiza tu tarjeta» UNA vez por periodo: el reintento de manana,
    /// rechazado tambien, no manda otro.
    /// </summary>
    [Fact]
    public async Task ES1_falla_el_cobro_sale_un_correo_y_no_dos()
    {
        var perfil = UnFundadorDelLanzamiento();
        ConMedioInscrito(perfil.Id);
        _pasarela.RechazaElCobro = true;

        DiasAntesDelFinDelMesGratis(7);
        await CorreElDia();
        UnDiaDespues();
        await CorreElDia();

        Assert.Equal(2, _pasarela.CobrosRecurrentes);           // si hubo reintento
        var aviso = Assert.Single(AvisosDeCobroRechazado());     // pero un solo correo
        Assert.Equal(perfil.UsuarioId, aviso.DestinatarioId);
        Assert.Contains("fundador", aviso.Cuerpo);
    }

    /// <summary>
    /// Se reintenta una vez por dia y no mas: correr el trabajo dos veces el dia del
    /// reintento sale UNA vez a la pasarela, con la clave «…:reintento:yyyy-MM-dd».
    /// </summary>
    [Fact]
    public async Task ES1_el_reintento_es_uno_por_dia_con_su_clave()
    {
        var perfil = UnFundadorDelLanzamiento();
        var s = ConMedioInscrito(perfil.Id);
        _pasarela.RechazaElCobro = true;

        DiasAntesDelFinDelMesGratis(7);                    // 16-dic, 09:00 de Chile
        await _cobros.CobrarVencimientosAsync();
        UnDiaDespues();                                     // 17-dic
        await _cobros.CobrarVencimientosAsync();
        await _cobros.CobrarVencimientosAsync();

        Assert.Equal(2, _pasarela.CobrosRecurrentes);
        var claves = LosCobros(perfil.Id).Select(i => i.ClaveIdempotencia).ToList();
        Assert.Equal(2, claves.Count);
        Assert.Contains(
            $"cobro-automatico:{s.Id:D}:2026-12-23:reintento:2026-12-17", claves);
    }

    /// <summary>
    /// El reintento de manana paga: el fundador conserva su cupo y su precio, y el mes se
    /// extiende desde el vencimiento, no desde hoy.
    /// </summary>
    [Fact]
    public async Task ES1_el_reintento_del_dia_siguiente_paga_y_conserva_el_cupo()
    {
        var perfil = UnFundadorDelLanzamiento();
        ConMedioInscrito(perfil.Id);
        _pasarela.RechazaElCobro = true;

        DiasAntesDelFinDelMesGratis(7);
        await CorreElDia();
        _pasarela.RechazaElCobro = false;
        UnDiaDespues();
        await CorreElDia();

        var pago = Assert.Single(_base.Pagos);
        Assert.Equal(5990, pago.Monto);
        Assert.Equal(new DateTime(2027, 1, 23, 12, 0, 0, DateTimeKind.Utc), SuscripcionDe(perfil.Id).VigenteHasta);
        Assert.NotNull(CupoDe(perfil.Id));

        // Pasado el vencimiento original, sigue: el diario no lo vence ni le suelta el cupo.
        _reloj.Avanzar(FinDelMesGratis.AddDays(1) - _reloj.Ahora);
        await CorreElDia();
        Assert.True(SuscripcionDe(perfil.Id).EstaVigente(_reloj.Ahora));
        Assert.NotNull(CupoDe(perfil.Id));
    }

    /// <summary>
    /// Pasan los 7 dias sin pago: la suscripcion vence, el cupo queda libre aunque tenga
    /// tarjeta, y no se le cobra nada mas, ni a precio fundador ni a ningun otro (decision de
    /// Pablo, 20-nov-2026: no hay paso automatico al Mensual).
    /// </summary>
    [Fact]
    public async Task ES1_pasan_los_7_dias_sin_pago_pierde_el_cupo_y_no_se_cobra_mas()
    {
        var perfil = UnFundadorDelLanzamiento();
        ConMedioInscrito(perfil.Id);
        _pasarela.RechazaElCobro = true;

        DiasAntesDelFinDelMesGratis(7);
        for (var dia = 0; dia < 7; dia++)
        {
            await CorreElDia();
            UnDiaDespues();
        }
        // Ya es el 23-dic a las 12:00 UTC: justo el vencimiento.
        _reloj.Avanzar(TimeSpan.FromHours(1));
        await CorreElDia();

        Assert.Equal(7, _pasarela.CobrosRecurrentes);    // el del dia -7 y seis reintentos
        Assert.Empty(_base.Pagos);
        Assert.Equal(EstadoSuscripcion.Vencida, SuscripcionDe(perfil.Id).Estado);
        Assert.Null(CupoDe(perfil.Id));

        // Y despues no se le vuelve a cobrar nada.
        _pasarela.RechazaElCobro = false;
        for (var dia = 0; dia < 10; dia++)
        {
            UnDiaDespues();
            await CorreElDia();
        }
        Assert.Equal(7, _pasarela.CobrosRecurrentes);
        Assert.Empty(_base.Pagos);
    }

    /// <summary>
    /// Si la pasarela no contesto (intento Pendiente: no se sabe si cobro), no se reintenta.
    /// Reintentar a ciegas lo que quiza ya se cobro es como se cobra dos veces.
    /// </summary>
    [Fact]
    public async Task ES1_no_se_reintenta_si_hay_un_intento_pendiente()
    {
        var perfil = UnFundadorDelLanzamiento();
        ConMedioInscrito(perfil.Id);
        _pasarela.FallaAlCobrar = true;

        DiasAntesDelFinDelMesGratis(7);
        await _cobros.CobrarVencimientosAsync();
        Assert.Equal(EstadoIntentoPago.Pendiente, Assert.Single(LosCobros(perfil.Id)).Estado);

        _pasarela.FallaAlCobrar = false;
        UnDiaDespues();
        var resumen = await _cobros.CobrarVencimientosAsync();

        Assert.Equal(1, resumen.YaIntentadas);
        Assert.Single(LosCobros(perfil.Id));
        Assert.Equal(1, _pasarela.CobrosRecurrentes);
        Assert.Empty(AvisosDeCobroRechazado());
    }

    /// <summary>
    /// «Mi ficha» sabe cuantos dias de gracia quedan, y deja de decirlo en cuanto paga.
    /// </summary>
    [Fact]
    public async Task ES1_mi_ficha_ve_los_dias_de_gracia_mientras_dura()
    {
        var perfil = UnFundadorDelLanzamiento();
        ConMedioInscrito(perfil.Id);

        DiasAntesDelFinDelMesGratis(10);
        Assert.Null((await _suscripciones.DePrestadorAsync(perfil.Id)).Valor!.DiasDeGracia);

        _pasarela.RechazaElCobro = true;
        DiasAntesDelFinDelMesGratis(7);
        await CorreElDia();
        Assert.Equal(7, (await _suscripciones.DePrestadorAsync(perfil.Id)).Valor!.DiasDeGracia);

        UnDiaDespues();
        _pasarela.RechazaElCobro = false;
        await CorreElDia();
        Assert.Null((await _suscripciones.DePrestadorAsync(perfil.Id)).Valor!.DiasDeGracia);
    }
}
