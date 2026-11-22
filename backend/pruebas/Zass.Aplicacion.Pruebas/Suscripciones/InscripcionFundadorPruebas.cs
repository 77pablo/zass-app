using Zass.Aplicacion.Pruebas.Dobles;
using Zass.Aplicacion.Puertos;
using Zass.Aplicacion.Suscripciones;
using Zass.Dominio.Comun;
using Zass.Dominio.Identidad;
using Zass.Dominio.Suscripciones;

namespace Zass.Aplicacion.Pruebas.Suscripciones;

/// <summary>
/// S7 · RN-S7-01, RN-S7-03 y RN-S7-07. La inscripcion del medio de pago.
///
/// Decision de Pablo del 13 de noviembre de 2026: el cupo fundador se confirma al
/// INSCRIBIR el medio de pago con un cobro de cero —lo que hace Oneclick de Transbank—,
/// no al primer pago real. Si se confirmara al pagar, durante todo el mes gratis nadie
/// sabria cuantos cupos estan de verdad tomados; y si se confirmara al pedirlo, los 50 se
/// agotarian en una tarde con gente que no piensa pagar.
///
/// Lo que estas pruebas vigilan y ninguna otra: que una inscripcion NO cobre (ni una fila
/// en pagos), que arranque el mes gratis, y que no le quite ni le regale dias a quien ya
/// tenia una suscripcion corriendo.
/// </summary>
public class InscripcionFundadorPruebas
{
    private static readonly DateTime Ahora = new(2026, 11, 13, 12, 0, 0, DateTimeKind.Utc);

    private readonly BaseEnMemoria _base = new();
    private readonly RelojFijo _reloj = new(Ahora);
    private readonly PasarelaFalsa _pasarela = new();
    private readonly ServicioSuscripciones _suscripciones;
    private readonly ServicioCobros _cobros;
    private readonly ColaEnMemoria _cola = new();

    public InscripcionFundadorPruebas()
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

    /// <summary>Con suscripcion, que es el caso normal: publicar el perfil arranca la prueba (API-22).</summary>
    private PerfilPrestador UnPrestadorConSuscripcion(DateTime? vigenteHasta = null)
    {
        var perfil = new PerfilPrestador { Id = Guid.NewGuid(), UsuarioId = Guid.NewGuid() };
        _base.PerfilesPrestador.Add(perfil);
        _base.Suscripciones.Add(new Suscripcion
        {
            PerfilPrestadorId = perfil.Id,
            PlanId = 1,
            Estado = EstadoSuscripcion.Prueba,
            IniciaEn = Ahora.AddDays(-5),
            VigenteHasta = vigenteHasta ?? Ahora.AddDays(25),
        });
        return perfil;
    }

    /// <summary>
    /// N3 (22-nov-2026): tomar el cupo (API-69) lo RESERVA 30 minutos y la tarjeta se inscribe
    /// sobre esa reserva. Este es el punto de partida de toda inscripcion.
    /// </summary>
    private async Task<PerfilPrestador> UnFundadorConCupoReservado(DateTime? vigenteHasta = null)
    {
        var perfil = UnPrestadorConSuscripcion(vigenteHasta);
        var r = await _suscripciones.TomarCupoFundadorAsync(perfil.Id);
        Assert.True(r.Exito, "El montaje fallo al tomar el cupo: " + r.Mensaje);
        return perfil;
    }

    /// <summary>
    /// Un fundador de la regla del 20-nov-2026 (cupo confirmado y mes gratis SIN tarjeta). Ya no
    /// nace ninguno asi, pero los que hubiera siguen existiendo y el vencimiento los trata igual.
    /// </summary>
    private PerfilPrestador UnFundadorDeAntesSinTarjeta(DateTime? vigenteHasta = null)
    {
        var perfil = UnPrestadorConSuscripcion();
        var s = SuscripcionDe(perfil.Id);
        s.PlanId = 3;
        s.VigenteHasta = vigenteHasta ?? Ahora.AddDays(30);
        var cupo = _base.CuposFundador.OrderBy(c => c.Numero).First(c => c.PerfilPrestadorId is null);
        cupo.PerfilPrestadorId = perfil.Id;
        cupo.ReservadoHasta = null;
        return perfil;
    }

    private IntentoDePago ElIntento() => Assert.Single(_base.IntentosDePago);

    private Suscripcion SuscripcionDe(Guid perfilId) =>
        _base.Suscripciones.Single(s => s.PerfilPrestadorId == perfilId);

    private CupoFundador? CupoDe(Guid perfilId) =>
        _base.CuposFundador.SingleOrDefault(c => c.PerfilPrestadorId == perfilId);

    private static AvisoDePasarela UnAviso() => new(new Dictionary<string, string>());

    /// <summary>El veredicto que devolveria la pasarela para ese intento.</summary>
    private void LaPasarelaResponde(IntentoDePago intento, bool aprobado = true, int monto = 0) =>
        _pasarela.Veredicto = new CobroResuelto(
            intento.Id, aprobado ? "ref_1" : null, aprobado, monto,
            aprobado ? null : "No se pudo inscribir el medio de pago");

    /// <summary>Llena los cupos LIBRES y deja quieto el que ya es de alguien.</summary>
    private void SinCuposLibresSalvoLosTomados()
    {
        foreach (var cupo in _base.CuposFundador.Where(c => c.PerfilPrestadorId is null))
            cupo.PerfilPrestadorId = Guid.NewGuid();
    }

    private void SinCuposLibres()
    {
        foreach (var cupo in _base.CuposFundador)
        {
            cupo.PerfilPrestadorId = Guid.NewGuid();
            cupo.ReservadoHasta = null;
        }
    }

    // ------------------------------------------------------------------ Empezar

    [Fact]
    public async Task La_inscripcion_crea_un_intento_de_monto_CERO_y_no_renueva_la_reserva()
    {
        var perfil = await UnFundadorConCupoReservado();

        var r = await _cobros.IniciarInscripcionFundadorAsync(perfil.Id, "k1");

        Assert.True(r.Exito);
        var intento = ElIntento();
        Assert.Equal(TipoDeIntento.Inscripcion, intento.Tipo);
        Assert.Equal(0, intento.Monto);
        Assert.Equal(0, intento.MontoBruto);
        Assert.Equal(0, intento.MontoDescuento);
        Assert.Equal((short)0, intento.Meses);       // no compra ningun mes
        Assert.Null(intento.CodigoCupon);
        Assert.NotNull(intento.UrlParaPagar);

        // N3: el cupo sigue RESERVADO con el plazo que le dio tomarlo. Inscribir no lo confirma
        // (eso lo hace el aviso de la pasarela) ni le estira el plazo.
        Assert.Equal(Ahora.AddMinutes(30), CupoDe(perfil.Id)!.ReservadoHasta);
        Assert.Equal(49, await _suscripciones.CuposFundadorDisponiblesAsync());
    }

    /// <summary>
    /// El defecto del 14-nov-2026, fijado. Una inscripcion NO es un cobro de cero pesos: es
    /// otro camino del puerto. Mientras fue un cobro, la pasarela de verdad respondia «No se
    /// puede cobrar un monto de cero» y NADIE podia tomar un cupo fundador — con todas las
    /// pruebas en verde, porque el doble aceptaba el cero que la real rechaza.
    /// </summary>
    [Fact]
    public async Task La_inscripcion_INSCRIBE_y_no_le_pide_ningun_cobro_a_la_pasarela()
    {
        var perfil = await UnFundadorConCupoReservado();

        var r = await _cobros.IniciarInscripcionFundadorAsync(perfil.Id, "k-inscribe");

        Assert.True(r.Exito);
        Assert.Equal(1, _pasarela.InscripcionesPedidas);
        Assert.Equal(0, _pasarela.CobrosPedidos);
        Assert.NotNull(_pasarela.UltimaInscripcion);
        Assert.Equal(ElIntento().Id, _pasarela.UltimaInscripcion!.IntentoDePagoId);
    }

    /// <summary>
    /// El mismo defecto por la puerta de atras: si la pasarela no contesta, el intento queda
    /// Pendiente y sin referencia, y reintentar con la misma clave tiene que volver a
    /// INSCRIBIR. Pedir un cobro aqui fallaria por monto cero igual que fallaba antes.
    /// </summary>
    [Fact]
    public async Task Si_la_pasarela_no_contesta_el_reintento_vuelve_a_INSCRIBIR()
    {
        var perfil = await UnFundadorConCupoReservado();
        _pasarela.FallaAlInscribir = true;

        var primera = await _cobros.IniciarInscripcionFundadorAsync(perfil.Id, "k-reintento");
        Assert.False(primera.Exito);
        Assert.Null(ElIntento().ReferenciaPasarela);

        _pasarela.FallaAlInscribir = false;
        var segunda = await _cobros.IniciarInscripcionFundadorAsync(perfil.Id, "k-reintento");

        Assert.True(segunda.Exito);
        Assert.Equal(2, _pasarela.InscripcionesPedidas);
        Assert.Equal(0, _pasarela.CobrosPedidos);
        Assert.NotNull(ElIntento().ReferenciaPasarela);
    }

    [Fact]
    public async Task CC_04_el_doble_clic_no_toma_dos_cupos()
    {
        var perfil = await UnFundadorConCupoReservado();

        var primero = await _cobros.IniciarInscripcionFundadorAsync(perfil.Id, "k1");
        var segundo = await _cobros.IniciarInscripcionFundadorAsync(perfil.Id, "k1");

        Assert.Equal(primero.Valor!.Id, segundo.Valor!.Id);
        Assert.False(segundo.Valor!.Nueva);
        Assert.Single(_base.IntentosDePago);
        Assert.Equal(49, await _suscripciones.CuposFundadorDisponiblesAsync());
    }

    /// <summary>
    /// 20-nov-2026: inscribir la tarjeta NO es otra puerta para tomar un cupo. Quien no tiene
    /// el suyo no inscribe nada, aunque sobren cupos.
    /// </summary>
    [Fact]
    public async Task Sin_cupo_propio_no_se_inscribe_la_tarjeta_aunque_sobren_cupos()
    {
        var perfil = UnPrestadorConSuscripcion();

        var r = await _cobros.IniciarInscripcionFundadorAsync(perfil.Id, "k1");

        Assert.False(r.Exito);
        Assert.Equal(Fallas.Conflicto, r.Codigo);
        Assert.Contains("Primero toma tu cupo", r.Mensaje!);
        Assert.Empty(_base.IntentosDePago);
        Assert.Equal(0, _pasarela.InscripcionesPedidas);
        Assert.Equal(50, await _suscripciones.CuposFundadorDisponiblesAsync());
    }

    /// <summary>Con los 50 tomados, quien tiene el suyo sigue pudiendo inscribir su tarjeta.</summary>
    [Fact]
    public async Task Con_el_plan_cerrado_quien_tiene_su_cupo_igual_inscribe_la_tarjeta()
    {
        var perfil = await UnFundadorConCupoReservado();
        SinCuposLibresSalvoLosTomados();

        var r = await _cobros.IniciarInscripcionFundadorAsync(perfil.Id, "k1");

        Assert.True(r.Exito, r.Mensaje);
        Assert.Equal(0, await _suscripciones.CuposFundadorDisponiblesAsync());
    }

    [Fact]
    public async Task Sin_suscripcion_todavia_no_se_puede_tomar_el_cupo()
    {
        var perfil = new PerfilPrestador { Id = Guid.NewGuid(), UsuarioId = Guid.NewGuid() };
        _base.PerfilesPrestador.Add(perfil);   // publicado NO: no hay nada que pasar a fundador

        var r = await _cobros.IniciarInscripcionFundadorAsync(perfil.Id, "k1");

        Assert.False(r.Exito);
        Assert.Equal(Fallas.Conflicto, r.Codigo);
        Assert.Equal(50, await _suscripciones.CuposFundadorDisponiblesAsync());
    }

    // ---------------------------------------------------------------- Confirmar

    [Fact]
    public async Task Confirmada_la_inscripcion_sigue_fundador_y_el_mes_gratis_no_se_mueve()
    {
        var perfil = await UnFundadorConCupoReservado(vigenteHasta: Ahora.AddDays(3));
        await _cobros.IniciarInscripcionFundadorAsync(perfil.Id, "k1");
        LaPasarelaResponde(ElIntento());

        var r = await _cobros.ConfirmarAsync(UnAviso());

        Assert.True(r.Exito);
        Assert.True(r.Valor!.Aprobado);

        // Sin fecha de reserva = confirmado: el cupo ya no vence.
        Assert.Null(CupoDe(perfil.Id)!.ReservadoHasta);

        var s = SuscripcionDe(perfil.Id);
        Assert.Equal("fundador", _base.Planes.Single(p => p.Id == s.PlanId).Codigo);
        Assert.Equal(EstadoSuscripcion.Prueba, s.Estado);
        Assert.Equal(Ahora.AddDays(30), s.VigenteHasta);   // el mes gratis empieza al CONFIRMAR (N3)
        Assert.True(s.EstaVigente(Ahora));
        Assert.Equal("ref_1", s.ReferenciaPasarela);
    }

    [Fact]
    public async Task La_inscripcion_deja_en_la_SUSCRIPCION_el_medio_con_que_cobrar_despues()
    {
        // El hueco que encontró C3 al construir el cobro automático, y que ninguna prueba
        // de C1 veía: la inscripción guardaba su referencia solo en el intento, y el cobro
        // del día 31 lee la SUSCRIPCIÓN. Sin esto, el 23 de diciembre no se le cobra a
        // nadie: la inscripción habría servido para tomar el cupo y para nada más.
        var perfil = await UnFundadorConCupoReservado();
        await _cobros.IniciarInscripcionFundadorAsync(perfil.Id, "k1");
        var intento = ElIntento();
        LaPasarelaResponde(intento);

        await _cobros.ConfirmarAsync(UnAviso());

        var s = SuscripcionDe(perfil.Id);
        Assert.False(
            string.IsNullOrWhiteSpace(s.ReferenciaPasarela),
            $"la suscripcion quedo sin medio de pago; el intento traia "
            + $"'{intento.ReferenciaPasarela ?? "(nulo)"}' y la suscripcion "
            + $"'{s.ReferenciaPasarela ?? "(nulo)"}'");
        Assert.Equal(intento.ReferenciaPasarela, s.ReferenciaPasarela);
    }

    [Fact]
    public async Task Una_inscripcion_rechazada_NO_deja_medio_de_pago_en_la_suscripcion()
    {
        var perfil = await UnFundadorConCupoReservado();
        await _cobros.IniciarInscripcionFundadorAsync(perfil.Id, "k1");
        LaPasarelaResponde(ElIntento(), aprobado: false);

        await _cobros.ConfirmarAsync(UnAviso());

        // Si quedara guardado, el cobro automático intentaría cobrarle a quien nunca
        // llegó a inscribir nada.
        Assert.True(string.IsNullOrWhiteSpace(SuscripcionDe(perfil.Id).ReferenciaPasarela));
    }

    [Fact]
    public async Task Una_inscripcion_NO_crea_una_fila_de_pago_porque_no_se_cobro_nada()
    {
        var perfil = await UnFundadorConCupoReservado();
        await _cobros.IniciarInscripcionFundadorAsync(perfil.Id, "k1");
        LaPasarelaResponde(ElIntento());

        await _cobros.ConfirmarAsync(UnAviso());

        Assert.Empty(_base.Pagos);
        Assert.Equal(EstadoIntentoPago.Pagado, ElIntento().Estado);
    }

    [Fact]
    public async Task RN_S7_07_a_quien_le_quedaban_45_dias_no_se_le_quitan_15()
    {
        var perfil = await UnFundadorConCupoReservado(vigenteHasta: Ahora.AddDays(45));
        await _cobros.IniciarInscripcionFundadorAsync(perfil.Id, "k1");
        LaPasarelaResponde(ElIntento());

        await _cobros.ConfirmarAsync(UnAviso());

        Assert.Equal(Ahora.AddDays(45), SuscripcionDe(perfil.Id).VigenteHasta);
    }

    [Fact]
    public async Task RN_S7_07_a_quien_le_quedaban_tres_dias_se_le_dan_los_30_y_no_33()
    {
        var perfil = await UnFundadorConCupoReservado(vigenteHasta: Ahora.AddDays(3));
        await _cobros.IniciarInscripcionFundadorAsync(perfil.Id, "k1");
        LaPasarelaResponde(ElIntento());

        await _cobros.ConfirmarAsync(UnAviso());

        Assert.Equal(Ahora.AddDays(30), SuscripcionDe(perfil.Id).VigenteHasta);
    }

    [Fact]
    public async Task RN_S7_07_no_nace_una_segunda_suscripcion()
    {
        var perfil = await UnFundadorConCupoReservado();
        await _cobros.IniciarInscripcionFundadorAsync(perfil.Id, "k1");
        LaPasarelaResponde(ElIntento());

        await _cobros.ConfirmarAsync(UnAviso());

        Assert.Single(_base.Suscripciones, s => s.PerfilPrestadorId == perfil.Id);
    }

    [Fact]
    public async Task La_misma_inscripcion_confirmada_dos_veces_no_estira_el_mes_gratis()
    {
        var perfil = await UnFundadorConCupoReservado();
        await _cobros.IniciarInscripcionFundadorAsync(perfil.Id, "k1");
        LaPasarelaResponde(ElIntento());

        await _cobros.ConfirmarAsync(UnAviso());
        var vigencia = SuscripcionDe(perfil.Id).VigenteHasta;
        var segundo = await _cobros.ConfirmarAsync(UnAviso());

        Assert.True(segundo.Exito);                   // 200, no error: la pasarela reintenta
        Assert.False(segundo.Valor!.Nueva);
        Assert.Equal(vigencia, SuscripcionDe(perfil.Id).VigenteHasta);
        Assert.Empty(_base.Pagos);
    }

    [Fact]
    public async Task La_tarjeta_rechazada_deja_la_reserva_hasta_que_venza_y_no_confirma_nada()
    {
        var perfil = await UnFundadorConCupoReservado();
        await _cobros.IniciarInscripcionFundadorAsync(perfil.Id, "k1");
        LaPasarelaResponde(ElIntento(), aprobado: false);

        var r = await _cobros.ConfirmarAsync(UnAviso());

        Assert.True(r.Exito);              // el caso de uso funciono
        Assert.False(r.Valor!.Aprobado);   // la inscripcion no
        // N3: puede probar con otra tarjeta mientras dure la reserva, y nada mas.
        Assert.Equal(Ahora.AddMinutes(30), CupoDe(perfil.Id)!.ReservadoHasta);
        Assert.Equal(49, await _suscripciones.CuposFundadorDisponiblesAsync());
        Assert.Equal((short)1, SuscripcionDe(perfil.Id).PlanId);   // NO pasa a fundador
        Assert.True(string.IsNullOrWhiteSpace(SuscripcionDe(perfil.Id).ReferenciaPasarela));

        _reloj.Avanzar(TimeSpan.FromMinutes(31));
        Assert.Equal(50, await _suscripciones.CuposFundadorDisponiblesAsync());
    }

    [Fact]
    public async Task Un_aviso_con_monto_distinto_de_cero_no_confirma_una_inscripcion()
    {
        var perfil = await UnFundadorConCupoReservado();
        await _cobros.IniciarInscripcionFundadorAsync(perfil.Id, "k1");
        LaPasarelaResponde(ElIntento(), monto: 5990);

        var r = await _cobros.ConfirmarAsync(UnAviso());

        // El monto confirmado se compara con el que se fijo en el servidor, que es 0.
        Assert.False(r.Exito);
        Assert.Equal(Fallas.Conflicto, r.Codigo);
        Assert.True(string.IsNullOrWhiteSpace(SuscripcionDe(perfil.Id).ReferenciaPasarela));
    }

    // ------------------------------------------------- Informe JQ-7 de Joaquín (PR #151)

    /// <summary>
    /// Defecto 1: la reserva de esta persona vencio, otra tomo ESE cupo y lo confirmo, y
    /// despues llega el aviso de la primera. Con los 50 tomados no puede quedar como
    /// fundadora sin cupo.
    /// </summary>
    [Fact]
    public async Task Un_aviso_tardio_cuando_el_cupo_ya_es_de_otra_persona_no_crea_un_fundador_sin_cupo()
    {
        var tarde = await UnFundadorConCupoReservado();
        await _cobros.IniciarInscripcionFundadorAsync(tarde.Id, "k1");
        var intento = ElIntento();
        var cupo = CupoDe(tarde.Id)!;

        // Otra persona se queda con ESE cupo, confirmado, y los demas tambien estan tomados.
        var otra = Guid.NewGuid();
        cupo.PerfilPrestadorId = otra;
        cupo.ReservadoHasta = null;
        foreach (var c in _base.CuposFundador.Where(c => c.PerfilPrestadorId is null))
            c.PerfilPrestadorId = Guid.NewGuid();

        LaPasarelaResponde(intento);
        var r = await _cobros.ConfirmarAsync(UnAviso());

        Assert.True(r.Exito);                       // 200: la pasarela no tiene que reintentar
        Assert.False(r.Valor!.Aprobado);
        Assert.Equal(EstadoIntentoPago.Fallido, intento.Estado);

        var s = SuscripcionDe(tarde.Id);
        Assert.True(string.IsNullOrWhiteSpace(s.ReferenciaPasarela));   // no se guarda la tarjeta
        Assert.Null(CupoDe(tarde.Id));               // no tiene cupo, y nadie le inventa uno
        Assert.Equal(otra, cupo.PerfilPrestadorId);  // y el de la otra persona no se toca
    }

    /// <summary>
    /// Igual que la anterior, pero quedan cupos libres: se le da uno de esos (decisión de
    /// Pablo, 20-nov) y queda como fundadora, sin tocar el cupo de la otra persona.
    /// </summary>
    [Fact]
    public async Task Un_aviso_tardio_con_cupos_libres_le_da_otro_cupo()
    {
        var tarde = await UnFundadorConCupoReservado();
        await _cobros.IniciarInscripcionFundadorAsync(tarde.Id, "k1");
        var intento = ElIntento();
        var cupo = CupoDe(tarde.Id)!;
        var numeroPerdido = cupo.Numero;

        var otra = Guid.NewGuid();
        cupo.PerfilPrestadorId = otra;
        cupo.ReservadoHasta = null;

        LaPasarelaResponde(intento);
        var r = await _cobros.ConfirmarAsync(UnAviso());

        Assert.True(r.Exito);
        Assert.True(r.Valor!.Aprobado);
        var nuevo = CupoDe(tarde.Id);
        Assert.NotNull(nuevo);
        Assert.NotEqual(numeroPerdido, nuevo!.Numero);
        Assert.Null(nuevo.ReservadoHasta);                 // confirmado, no solo reservado
        Assert.Equal(otra, cupo.PerfilPrestadorId);        // el de la otra persona no se toca
        var s = SuscripcionDe(tarde.Id);
        Assert.Equal("fundador", _base.Planes.Single(p => p.Id == s.PlanId).Codigo);
    }

    /// <summary>Defecto 2: volver a inscribir la tarjeta no regala otros 30 días.</summary>
    [Fact]
    public async Task Volver_a_inscribirse_siendo_fundador_no_corre_el_mes_gratis()
    {
        var perfil = await UnFundadorConCupoReservado();
        await _cobros.IniciarInscripcionFundadorAsync(perfil.Id, "k1");
        LaPasarelaResponde(ElIntento());
        await _cobros.ConfirmarAsync(UnAviso());
        var vigencia = SuscripcionDe(perfil.Id).VigenteHasta;   // Ahora + 30

        _reloj.Avanzar(TimeSpan.FromDays(10));
        await _cobros.IniciarInscripcionFundadorAsync(perfil.Id, "k2");
        var segunda = _base.IntentosDePago.Last();
        _pasarela.Veredicto = new CobroResuelto(segunda.Id, "ref_2", true, 0, null);
        var r = await _cobros.ConfirmarAsync(UnAviso());

        Assert.True(r.Exito);
        Assert.Equal(vigencia, SuscripcionDe(perfil.Id).VigenteHasta);   // no pasa a «hoy + 30»
        Assert.Equal("ref_2", SuscripcionDe(perfil.Id).ReferenciaPasarela); // la tarjeta nueva sí queda
    }

    /// <summary>
    /// Defensa en profundidad (22-nov-2026). API-68 (pagar el plan fundador) tambien RESERVA un
    /// cupo, y con esa reserva se inscribe la tarjeta: por ahi llegaba al mes gratis quien ya
    /// habia pagado otro plan, que API-69 rechaza. El cupo reservado se arma a mano porque
    /// API-69 ya no lo entrega a esta persona.
    /// </summary>
    private PerfilPrestador UnPagadorDeOtroPlanConCupoReservado()
    {
        var perfil = UnPrestadorConSuscripcion(vigenteHasta: Ahora.AddDays(3));
        SuscripcionDe(perfil.Id).PlanId = 2;   // mensual: ya pago otro plan
        var cupo = _base.CuposFundador.OrderBy(c => c.Numero).First(c => c.PerfilPrestadorId is null);
        cupo.PerfilPrestadorId = perfil.Id;
        cupo.ReservadoHasta = Ahora.AddMinutes(30);
        return perfil;
    }

    [Fact]
    public async Task Quien_ya_pago_otro_plan_no_recibe_mes_gratis_al_confirmar_la_inscripcion()
    {
        var perfil = UnPagadorDeOtroPlanConCupoReservado();
        var r0 = await _cobros.IniciarInscripcionFundadorAsync(perfil.Id, "k1");
        Assert.True(r0.Exito, r0.Mensaje);
        var intento = ElIntento();
        LaPasarelaResponde(intento);

        var r = await _cobros.ConfirmarAsync(UnAviso());

        Assert.True(r.Exito);                        // 200: la pasarela no tiene que reintentar
        Assert.False(r.Valor!.Aprobado);
        Assert.Contains("No se cobró nada", r.Valor.Mensaje);
        Assert.Equal(EstadoIntentoPago.Fallido, intento.Estado);
        Assert.Contains("mes gratis de fundador", intento.MotivoFallo);

        var s = SuscripcionDe(perfil.Id);
        Assert.Equal((short)2, s.PlanId);                       // sigue en su plan
        Assert.Equal(Ahora.AddDays(3), s.VigenteHasta);         // sin mes gratis
        Assert.True(string.IsNullOrWhiteSpace(s.ReferenciaPasarela));
        Assert.Null(CupoDe(perfil.Id));                         // el cupo se libero
        Assert.Equal(50, await _suscripciones.CuposFundadorDisponiblesAsync());
        Assert.Empty(_base.Pagos);
    }

    [Fact]
    public async Task Quien_viene_del_plan_inicial_sigue_recibiendo_su_mes_gratis()
    {
        var perfil = await UnFundadorConCupoReservado(vigenteHasta: Ahora.AddDays(3));
        await _cobros.IniciarInscripcionFundadorAsync(perfil.Id, "k1");
        var intento = ElIntento();
        LaPasarelaResponde(intento);

        var r = await _cobros.ConfirmarAsync(UnAviso());

        Assert.True(r.Valor!.Aprobado);
        Assert.Equal(EstadoIntentoPago.Pagado, intento.Estado);
        Assert.Equal(Ahora.AddDays(30), SuscripcionDe(perfil.Id).VigenteHasta);
        Assert.NotNull(CupoDe(perfil.Id));
    }

    // --------------------- Tomar el cupo: RESERVA hasta inscribir la tarjeta (N3, 22-nov-2026)

    /// <summary>
    /// N3. Reemplaza a «tomar el cupo lo deja suyo y arranca el mes gratis» (RN-S7-15 del
    /// 20-nov): tomar el cupo solo lo reserva 30 minutos. Ni lo confirma, ni cambia el plan,
    /// ni arranca el mes gratis: eso es de la tarjeta inscrita.
    /// </summary>
    [Fact]
    public async Task Tomar_el_cupo_solo_lo_RESERVA_30_minutos_y_no_arranca_el_mes_gratis()
    {
        var perfil = UnPrestadorConSuscripcion(vigenteHasta: Ahora.AddDays(3));

        var r = await _suscripciones.TomarCupoFundadorAsync(perfil.Id);

        Assert.True(r.Exito, r.Mensaje);
        Assert.False(r.Valor!.TarjetaInscrita);
        Assert.Null(r.Valor.MesGratisHasta);
        Assert.Equal(CupoTomadoDto.Inscribir, r.Valor.Siguiente);   // viene del plan inicial: mes gratis
        Assert.Equal(Ahora.AddMinutes(30), r.Valor.ReservadoHasta);
        Assert.Equal(CupoDe(perfil.Id)!.Numero, r.Valor.Numero);
        Assert.Equal(Ahora.AddMinutes(30), CupoDe(perfil.Id)!.ReservadoHasta);   // reserva, no definitivo
        Assert.Equal(49, await _suscripciones.CuposFundadorDisponiblesAsync());

        var s = SuscripcionDe(perfil.Id);
        Assert.Equal((short)1, s.PlanId);                            // sigue en la prueba
        Assert.Equal(Ahora.AddDays(3), s.VigenteHasta);              // sin mes gratis todavia
        Assert.False((await _suscripciones.DePrestadorAsync(perfil.Id)).Valor!.EsFundador);
        Assert.Empty(_base.IntentosDePago);
    }

    /// <summary>Repetir el llamado devuelve la MISMA reserva y no le estira el plazo.</summary>
    [Fact]
    public async Task Tomar_dos_veces_devuelve_el_mismo_cupo_y_no_renueva_la_reserva()
    {
        var perfil = UnPrestadorConSuscripcion();
        var primero = await _suscripciones.TomarCupoFundadorAsync(perfil.Id);

        _reloj.Avanzar(TimeSpan.FromMinutes(20));
        var segundo = await _suscripciones.TomarCupoFundadorAsync(perfil.Id);

        Assert.True(segundo.Exito);
        Assert.Equal(primero.Valor!.Numero, segundo.Valor!.Numero);
        Assert.Equal(Ahora.AddMinutes(30), segundo.Valor.ReservadoHasta);
        Assert.Equal(Ahora.AddMinutes(30), CupoDe(perfil.Id)!.ReservadoHasta);
        Assert.Equal(49, await _suscripciones.CuposFundadorDisponiblesAsync());
    }

    /// <summary>
    /// Sin tarjeta a tiempo, el cupo vuelve a estar libre AL INSTANTE (el conteo no espera al
    /// trabajo diario), otra persona lo puede tomar, y quien lo perdio ya no inscribe sobre esa
    /// reserva: vuelve a tomar uno (con captcha) si quedan.
    /// </summary>
    [Fact]
    public async Task La_reserva_vencida_libera_el_cupo_y_ya_no_sirve_para_inscribir()
    {
        var lento = UnPrestadorConSuscripcion();
        var numero = (await _suscripciones.TomarCupoFundadorAsync(lento.Id)).Valor!.Numero;

        _reloj.Avanzar(TimeSpan.FromMinutes(31));
        Assert.Equal(50, await _suscripciones.CuposFundadorDisponiblesAsync());

        var rapido = UnPrestadorConSuscripcion();
        var deRapido = await _suscripciones.TomarCupoFundadorAsync(rapido.Id);
        Assert.Equal(numero, deRapido.Valor!.Numero);                // se lo lleva otra persona

        var r = await _cobros.IniciarInscripcionFundadorAsync(lento.Id, "k-tarde");
        Assert.False(r.Exito);
        Assert.Equal(Fallas.Conflicto, r.Codigo);
        Assert.Equal(0, _pasarela.InscripcionesPedidas);

        var otraVez = await _suscripciones.TomarCupoFundadorAsync(lento.Id);
        Assert.True(otraVez.Exito);
        Assert.NotEqual(numero, otraVez.Valor!.Numero);
    }

    /// <summary>
    /// La reserva vencida pero todavia sin limpiar (el diario corre una vez al dia) tampoco
    /// sirve: inscribir no la revive. Si la reviviera, pedir una inscripcion cada media hora
    /// retendria el cupo para siempre.
    /// </summary>
    [Fact]
    public async Task Con_la_reserva_vencida_sin_limpiar_no_se_inscribe_ni_se_renueva()
    {
        var perfil = await UnFundadorConCupoReservado();
        _reloj.Avanzar(TimeSpan.FromMinutes(31));

        var r = await _cobros.IniciarInscripcionFundadorAsync(perfil.Id, "k1");

        Assert.False(r.Exito);
        Assert.Contains("venció", r.Mensaje!);
        Assert.Empty(_base.IntentosDePago);
        Assert.Equal(Ahora.AddMinutes(30), CupoDe(perfil.Id)!.ReservadoHasta);   // no se renovo
    }

    [Fact]
    public async Task Sin_cupos_libres_no_se_toma_y_se_ofrece_el_mensual()
    {
        SinCuposLibres();
        var perfil = UnPrestadorConSuscripcion();

        var r = await _suscripciones.TomarCupoFundadorAsync(perfil.Id);

        Assert.False(r.Exito);
        Assert.Equal(Fallas.Conflicto, r.Codigo);
        Assert.Contains("mensual", r.Mensaje!);
        Assert.Equal((short)1, SuscripcionDe(perfil.Id).PlanId);
    }

    [Fact]
    public async Task Sin_suscripcion_no_se_toma_el_cupo()
    {
        var perfil = new PerfilPrestador { Id = Guid.NewGuid(), UsuarioId = Guid.NewGuid() };
        _base.PerfilesPrestador.Add(perfil);

        var r = await _suscripciones.TomarCupoFundadorAsync(perfil.Id);

        Assert.False(r.Exito);
        Assert.Equal(Fallas.Conflicto, r.Codigo);
        Assert.Equal(50, await _suscripciones.CuposFundadorDisponiblesAsync());
    }

    /// <summary>
    /// La regla de Pablo, entera: termina el mes gratis sin tarjeta, el cupo queda libre y la
    /// ficha deja de aparecer. Y no se puede volver a tomar para tener otro mes gratis.
    /// </summary>
    [Fact]
    public async Task Sin_tarjeta_al_terminar_el_mes_gratis_pierde_el_cupo_y_no_lo_vuelve_a_tomar()
    {
        // Los fundadores de la regla del 20-nov (cupo sin tarjeta) que ya existieran.
        var perfil = UnFundadorDeAntesSinTarjeta();

        _reloj.Avanzar(TimeSpan.FromDays(31));
        await _suscripciones.CorrerDiarioAsync();

        Assert.Null(CupoDe(perfil.Id));
        Assert.Equal(50, await _suscripciones.CuposFundadorDisponiblesAsync());
        Assert.False(await _suscripciones.EstaVigenteAsync(perfil.Id));     // fuera de la busqueda

        // 22-nov-2026: puede volver a RESERVAR (con captcha), pero sin otro mes gratis: su
        // siguiente paso es pagar el plan fundador, no inscribir la tarjeta.
        var vigenteAntes = SuscripcionDe(perfil.Id).VigenteHasta;
        var otraVez = await _suscripciones.TomarCupoFundadorAsync(perfil.Id);
        Assert.True(otraVez.Exito);
        Assert.Equal(CupoTomadoDto.Pagar, otraVez.Valor!.Siguiente);
        Assert.Null(otraVez.Valor.MesGratisHasta);
        Assert.NotNull(CupoDe(perfil.Id)!.ReservadoHasta);
        Assert.Equal(vigenteAntes, SuscripcionDe(perfil.Id).VigenteHasta);   // ni un dia mas
    }

    /// <summary>
    /// ES-1 (decision de Pablo, 20-nov-2026). Antes esta prueba decia lo contrario
    /// («con tarjeta inscrita el vencimiento no le quita el cupo»). Ahora la tarjeta sola no
    /// basta: llegar al vencimiento sin que se haya podido cobrar le quita el cupo igual que
    /// a quien nunca la inscribio. A quien SI se le cobro no le llega el vencimiento.
    /// </summary>
    [Fact]
    public async Task Con_tarjeta_inscrita_pero_sin_pago_el_vencimiento_tambien_le_quita_el_cupo()
    {
        var perfil = await UnFundadorConCupoReservado(vigenteHasta: Ahora.AddDays(3));
        await _cobros.IniciarInscripcionFundadorAsync(perfil.Id, "k1");
        LaPasarelaResponde(ElIntento());
        await _cobros.ConfirmarAsync(UnAviso());

        _reloj.Avanzar(TimeSpan.FromDays(31));
        await _suscripciones.CorrerDiarioAsync();

        Assert.Null(CupoDe(perfil.Id));
        Assert.Equal(50, await _suscripciones.CuposFundadorDisponiblesAsync());
    }

    [Fact]
    public async Task El_aviso_de_que_termina_el_mes_gratis_le_pide_la_tarjeta_a_quien_no_la_tiene()
    {
        var sinTarjeta = UnFundadorDeAntesSinTarjeta();   // de la regla del 20-nov
        var conTarjeta = await UnFundadorConCupoReservado(vigenteHasta: Ahora.AddDays(3));
        await _cobros.IniciarInscripcionFundadorAsync(conTarjeta.Id, "k1");
        LaPasarelaResponde(ElIntento());
        await _cobros.ConfirmarAsync(UnAviso());

        _reloj.Avanzar(TimeSpan.FromDays(25));   // le quedan 5 dias: toca el aviso de 7
        await _suscripciones.CorrerDiarioAsync();

        var avisos = _cola.Encoladas.Where(n => n.Tipo == TipoNotificacion.SuscripcionPorVencer).ToList();
        Assert.Equal(2, avisos.Count);
        var delSinTarjeta = Assert.Single(avisos, n => n.DestinatarioId == sinTarjeta.UsuarioId);
        var delConTarjeta = Assert.Single(avisos, n => n.DestinatarioId == conTarjeta.UsuarioId);
        Assert.Equal("si", CargaDeAviso.LeerCampo(delSinTarjeta.Cuerpo, "sinTarjeta"));
        Assert.Null(CargaDeAviso.LeerCampo(delConTarjeta.Cuerpo, "sinTarjeta"));
    }

    [Fact]
    public async Task Mi_suscripcion_dice_si_es_fundador_y_si_tiene_tarjeta()
    {
        var perfil = await UnFundadorConCupoReservado();
        var antes = (await _suscripciones.DePrestadorAsync(perfil.Id)).Valor!;

        await _cobros.IniciarInscripcionFundadorAsync(perfil.Id, "k1");
        LaPasarelaResponde(ElIntento());
        await _cobros.ConfirmarAsync(UnAviso());
        var despues = (await _suscripciones.DePrestadorAsync(perfil.Id)).Valor!;

        // N3: con el cupo solo reservado todavia no es fundador; lo es al quedar la tarjeta.
        Assert.False(antes.EsFundador);
        Assert.False(antes.TarjetaInscrita);
        Assert.True(despues.EsFundador);
        Assert.True(despues.TarjetaInscrita);
    }
}
