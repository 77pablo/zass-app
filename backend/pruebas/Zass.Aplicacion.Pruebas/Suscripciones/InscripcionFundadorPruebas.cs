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

    public InscripcionFundadorPruebas()
    {
        var repo = new RepositorioSuscripcionesEnMemoria(_base);
        _suscripciones = new ServicioSuscripciones(
            repo, _base, _reloj, new AuditoriaEnMemoria(), new ColaEnMemoria());
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
    public async Task La_inscripcion_crea_un_intento_de_monto_CERO_y_reserva_el_cupo()
    {
        var perfil = UnPrestadorConSuscripcion();

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

        // Reserva de 25 minutos, el numero escrito a mano.
        Assert.Equal(Ahora.AddMinutes(25), CupoDe(perfil.Id)!.ReservadoHasta);
        Assert.Equal(49, await _suscripciones.CuposFundadorDisponiblesAsync());
    }

    [Fact]
    public async Task CC_04_el_doble_clic_no_toma_dos_cupos()
    {
        var perfil = UnPrestadorConSuscripcion();

        var primero = await _cobros.IniciarInscripcionFundadorAsync(perfil.Id, "k1");
        var segundo = await _cobros.IniciarInscripcionFundadorAsync(perfil.Id, "k1");

        Assert.Equal(primero.Valor!.Id, segundo.Valor!.Id);
        Assert.False(segundo.Valor!.Nueva);
        Assert.Single(_base.IntentosDePago);
        Assert.Equal(49, await _suscripciones.CuposFundadorDisponiblesAsync());
    }

    [Fact]
    public async Task Sin_cupos_no_hay_inscripcion()
    {
        SinCuposLibres();
        var perfil = UnPrestadorConSuscripcion();

        var r = await _cobros.IniciarInscripcionFundadorAsync(perfil.Id, "k1");

        Assert.False(r.Exito);
        Assert.Equal(Fallas.Conflicto, r.Codigo);
        Assert.Contains("mensual", r.Mensaje!);
        Assert.Empty(_base.IntentosDePago);
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
    public async Task Confirmada_la_inscripcion_el_cupo_es_suyo_y_el_mes_gratis_corre()
    {
        var perfil = UnPrestadorConSuscripcion(vigenteHasta: Ahora.AddDays(3));
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
        Assert.Equal(Ahora.AddDays(30), s.VigenteHasta);   // 30, escrito a mano
        Assert.True(s.EstaVigente(Ahora));                 // aparece en la busqueda desde ya
    }

    [Fact]
    public async Task Una_inscripcion_NO_crea_una_fila_de_pago_porque_no_se_cobro_nada()
    {
        var perfil = UnPrestadorConSuscripcion();
        await _cobros.IniciarInscripcionFundadorAsync(perfil.Id, "k1");
        LaPasarelaResponde(ElIntento());

        await _cobros.ConfirmarAsync(UnAviso());

        Assert.Empty(_base.Pagos);
        Assert.Equal(EstadoIntentoPago.Pagado, ElIntento().Estado);
    }

    [Fact]
    public async Task RN_S7_07_a_quien_le_quedaban_45_dias_no_se_le_quitan_15()
    {
        var perfil = UnPrestadorConSuscripcion(vigenteHasta: Ahora.AddDays(45));
        await _cobros.IniciarInscripcionFundadorAsync(perfil.Id, "k1");
        LaPasarelaResponde(ElIntento());

        await _cobros.ConfirmarAsync(UnAviso());

        Assert.Equal(Ahora.AddDays(45), SuscripcionDe(perfil.Id).VigenteHasta);
    }

    [Fact]
    public async Task RN_S7_07_a_quien_le_quedaban_tres_dias_se_le_dan_los_30_y_no_33()
    {
        var perfil = UnPrestadorConSuscripcion(vigenteHasta: Ahora.AddDays(3));
        await _cobros.IniciarInscripcionFundadorAsync(perfil.Id, "k1");
        LaPasarelaResponde(ElIntento());

        await _cobros.ConfirmarAsync(UnAviso());

        Assert.Equal(Ahora.AddDays(30), SuscripcionDe(perfil.Id).VigenteHasta);
    }

    [Fact]
    public async Task RN_S7_07_no_nace_una_segunda_suscripcion()
    {
        var perfil = UnPrestadorConSuscripcion();
        await _cobros.IniciarInscripcionFundadorAsync(perfil.Id, "k1");
        LaPasarelaResponde(ElIntento());

        await _cobros.ConfirmarAsync(UnAviso());

        Assert.Single(_base.Suscripciones.Where(s => s.PerfilPrestadorId == perfil.Id));
    }

    [Fact]
    public async Task La_misma_inscripcion_confirmada_dos_veces_no_estira_el_mes_gratis()
    {
        var perfil = UnPrestadorConSuscripcion();
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
    public async Task La_inscripcion_rechazada_libera_el_cupo_al_tiro()
    {
        var perfil = UnPrestadorConSuscripcion();
        await _cobros.IniciarInscripcionFundadorAsync(perfil.Id, "k1");
        LaPasarelaResponde(ElIntento(), aprobado: false);

        var r = await _cobros.ConfirmarAsync(UnAviso());

        Assert.True(r.Exito);              // el caso de uso funciono
        Assert.False(r.Valor!.Aprobado);   // la inscripcion no
        // El cupo 50 es escaso: no se queda bloqueado los 25 minutos de la reserva.
        Assert.Null(CupoDe(perfil.Id));
        Assert.Equal(50, await _suscripciones.CuposFundadorDisponiblesAsync());
        Assert.Equal(EstadoSuscripcion.Prueba, SuscripcionDe(perfil.Id).Estado);
        Assert.Equal((short)1, SuscripcionDe(perfil.Id).PlanId);   // sigue en el plan de prueba
    }

    [Fact]
    public async Task Un_aviso_con_monto_distinto_de_cero_no_confirma_una_inscripcion()
    {
        var perfil = UnPrestadorConSuscripcion();
        await _cobros.IniciarInscripcionFundadorAsync(perfil.Id, "k1");
        LaPasarelaResponde(ElIntento(), monto: 5990);

        var r = await _cobros.ConfirmarAsync(UnAviso());

        // El monto confirmado se compara con el que se fijo en el servidor, que es 0.
        Assert.False(r.Exito);
        Assert.Equal(Fallas.Conflicto, r.Codigo);
        Assert.NotNull(CupoDe(perfil.Id)!.ReservadoHasta);   // sigue siendo solo una reserva
        Assert.Equal((short)1, SuscripcionDe(perfil.Id).PlanId);
    }
}
