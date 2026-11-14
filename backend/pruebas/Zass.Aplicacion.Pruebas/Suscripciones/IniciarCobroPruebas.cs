using Zass.Aplicacion.Pruebas.Dobles;
using Zass.Aplicacion.Suscripciones;
using Zass.Dominio.Comun;
using Zass.Dominio.Identidad;
using Zass.Dominio.Suscripciones;

namespace Zass.Aplicacion.Pruebas.Suscripciones;

/// <summary>
/// MOD-13 · S7. Iniciar fija el precio en un intento (TBL-36) y le pide a la pasarela un
/// cobro. Es la pieza que faltaba: hasta el 13 de noviembre de 2026
/// <c>RegistrarPeriodoPagadoAsync</c> no tenia ni un llamador.
///
/// Dos cosas se prueban aqui y no en otra parte:
///
/// 1. <b>CC-04.</b> El doble envio del mismo formulario devuelve el MISMO intento, con la
///    misma direccion de pago, y no crea un segundo cobro. La clave la pone el cliente
///    porque una clave que genera el servidor no protege de nada: cada reintento traeria
///    una distinta.
/// 2. <b>El cupo fundador se reserva al EMPEZAR a pagar</b>, no al confirmar. Si se
///    reservara despues, dos personas podrian pagar por el cupo 50 y una se quedaria sin
///    lo que pago.
/// </summary>
public class IniciarCobroPruebas
{
    private static readonly DateTime Ahora = new(2026, 11, 13, 12, 0, 0, DateTimeKind.Utc);

    private readonly BaseEnMemoria _base = new();
    private readonly RelojFijo _reloj = new(Ahora);
    private readonly PasarelaFalsa _pasarela = new();
    private readonly ServicioSuscripciones _suscripciones;
    private readonly ServicioCobros _cobros;

    public IniciarCobroPruebas()
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

    private PerfilPrestador UnPrestador()
    {
        var perfil = new PerfilPrestador { Id = Guid.NewGuid(), UsuarioId = Guid.NewGuid() };
        _base.PerfilesPrestador.Add(perfil);
        return perfil;
    }

    /// <summary>
    /// Con suscripcion, que es el caso normal: la prueba gratuita nace al publicar el
    /// perfil (API-22), asi que quien llega a pagar ya tiene una.
    /// </summary>
    private PerfilPrestador UnPrestadorConSuscripcion(DateTime? vigenteHasta = null)
    {
        var perfil = UnPrestador();
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

    private void UnCupon(string codigo, short porcentaje = 30)
        => _base.Cupones.Add(new Cupon
        {
            Codigo = codigo, PorcentajeDescuento = porcentaje,
            VenceEn = Ahora.AddDays(30), Activo = true,
        });

    private void SinCuposLibres()
    {
        foreach (var cupo in _base.CuposFundador)
        {
            cupo.PerfilPrestadorId = Guid.NewGuid();
            cupo.ReservadoHasta = null;
        }
    }

    // ---------------------------------------------------------------- El intento

    [Fact]
    public async Task Iniciar_crea_el_intento_con_el_monto_fijado_en_el_SERVIDOR()
    {
        var perfil = UnPrestadorConSuscripcion();

        var r = await _cobros.IniciarAsync(new IniciarCobroCmd(perfil.Id, "mensual", null, "k1"));

        Assert.True(r.Exito);
        Assert.True(r.Valor!.Nueva);
        Assert.Equal(7990, r.Valor!.Total);

        var intento = Assert.Single(_base.IntentosDePago);
        Assert.Equal(7990, intento.Monto);            // el precio queda guardado aqui
        Assert.Equal(7990, intento.MontoBruto);
        Assert.Equal(0, intento.MontoDescuento);
        Assert.Equal(EstadoIntentoPago.Pendiente, intento.Estado);
        Assert.Equal("falsa", intento.Pasarela);      // que adaptador cobro, para siempre
        Assert.Equal((short)1, intento.Meses);
        Assert.NotNull(intento.UrlParaPagar);
        Assert.NotNull(intento.ReferenciaPasarela);
    }

    [Fact]
    public async Task Con_cupon_lo_que_se_manda_a_cobrar_es_el_total_con_descuento()
    {
        var perfil = UnPrestadorConSuscripcion();
        UnCupon("LANZAMIENTO30");

        await _cobros.IniciarAsync(new IniciarCobroCmd(perfil.Id, "mensual", "LANZAMIENTO30", "k1"));

        var intento = Assert.Single(_base.IntentosDePago);
        Assert.Equal(2397, intento.MontoDescuento);   // 30 % de 7990, a mano
        Assert.Equal(5593, intento.Monto);
        Assert.Equal("LANZAMIENTO30", intento.CodigoCupon);
        // Y es ESE monto el que viaja a la pasarela, no el precio de lista.
        Assert.Equal(5593, _pasarela.UltimaSolicitud!.Monto);
    }

    [Fact]
    public async Task El_canje_del_cupon_NO_nace_al_iniciar()
    {
        var perfil = UnPrestadorConSuscripcion();
        UnCupon("LANZAMIENTO30");

        await _cobros.IniciarAsync(new IniciarCobroCmd(perfil.Id, "mensual", "LANZAMIENTO30", "k1"));

        // Si naciera aqui, una tarjeta rechazada le quemaria el cupon a quien no llego a
        // pagar nada.
        Assert.Empty(_base.Canjes);
    }

    // ------------------------------------------------------------------ CC-04

    [Fact]
    public async Task CC_04_el_mismo_envio_dos_veces_crea_UN_intento()
    {
        var perfil = UnPrestadorConSuscripcion();

        var primero = await _cobros.IniciarAsync(new IniciarCobroCmd(perfil.Id, "mensual", null, "k1"));
        var segundo = await _cobros.IniciarAsync(new IniciarCobroCmd(perfil.Id, "mensual", null, "k1"));

        Assert.True(primero.Valor!.Nueva);
        Assert.False(segundo.Valor!.Nueva);
        Assert.Equal(primero.Valor!.Id, segundo.Valor!.Id);
        Assert.Equal(primero.Valor!.UrlParaPagar, segundo.Valor!.UrlParaPagar);
        Assert.Single(_base.IntentosDePago);
        Assert.Equal(1, _pasarela.CobrosPedidos);     // y no se le pidio un segundo cobro
    }

    [Fact]
    public async Task Dos_claves_distintas_si_son_dos_intentos()
    {
        var perfil = UnPrestadorConSuscripcion();

        await _cobros.IniciarAsync(new IniciarCobroCmd(perfil.Id, "mensual", null, "k1"));
        await _cobros.IniciarAsync(new IniciarCobroCmd(perfil.Id, "mensual", null, "k2"));

        Assert.Equal(2, _base.IntentosDePago.Count);
    }

    [Fact]
    public async Task Sin_clave_de_idempotencia_no_se_inicia_nada()
    {
        var perfil = UnPrestadorConSuscripcion();

        var r = await _cobros.IniciarAsync(new IniciarCobroCmd(perfil.Id, "mensual", null, "   "));

        Assert.False(r.Exito);
        Assert.Equal(Fallas.Validacion, r.Codigo);
        Assert.Empty(_base.IntentosDePago);
        Assert.Equal(0, _pasarela.CobrosPedidos);
    }

    // ------------------------------------------------------- El cupo fundador

    [Fact]
    public async Task El_plan_fundador_RESERVA_el_cupo_al_iniciar_y_la_reserva_dura_25_minutos()
    {
        var perfil = UnPrestadorConSuscripcion();

        await _cobros.IniciarAsync(new IniciarCobroCmd(perfil.Id, "fundador", null, "k1"));

        var cupo = _base.CuposFundador.Single(c => c.PerfilPrestadorId == perfil.Id);
        Assert.Equal(Ahora.AddMinutes(25), cupo.ReservadoHasta);   // 25, escrito a mano
        Assert.Equal(49, await _suscripciones.CuposFundadorDisponiblesAsync());
    }

    [Fact]
    public async Task El_plan_mensual_NO_toca_ningun_cupo()
    {
        var perfil = UnPrestadorConSuscripcion();

        await _cobros.IniciarAsync(new IniciarCobroCmd(perfil.Id, "mensual", null, "k1"));

        Assert.Equal(50, await _suscripciones.CuposFundadorDisponiblesAsync());
    }

    [Fact]
    public async Task Si_alguien_se_llevo_el_ultimo_cupo_el_mensaje_ofrece_el_plan_mensual()
    {
        SinCuposLibres();
        var perfil = UnPrestadorConSuscripcion();

        var r = await _cobros.IniciarAsync(new IniciarCobroCmd(perfil.Id, "fundador", null, "k1"));

        Assert.False(r.Exito);
        Assert.Equal(Fallas.Conflicto, r.Codigo);
        Assert.Contains("mensual", r.Mensaje!);
        Assert.Empty(_base.IntentosDePago);           // ni un intento huerfano
        Assert.Equal(0, _pasarela.CobrosPedidos);
    }

    [Fact]
    public async Task Reintentar_el_fundador_con_la_misma_clave_no_toma_un_segundo_cupo()
    {
        var perfil = UnPrestadorConSuscripcion();

        await _cobros.IniciarAsync(new IniciarCobroCmd(perfil.Id, "fundador", null, "k1"));
        await _cobros.IniciarAsync(new IniciarCobroCmd(perfil.Id, "fundador", null, "k1"));

        Assert.Equal(49, await _suscripciones.CuposFundadorDisponiblesAsync());
        Assert.Single(_base.IntentosDePago);
    }

    // ----------------------------------------------------- Los dos bordes feos

    [Fact]
    public async Task Sin_suscripcion_todavia_no_se_puede_pagar()
    {
        var perfil = UnPrestador();   // el perfil no se publico: no hay nada que extender

        var r = await _cobros.IniciarAsync(new IniciarCobroCmd(perfil.Id, "mensual", null, "k1"));

        Assert.False(r.Exito);
        Assert.Equal(Fallas.Conflicto, r.Codigo);
        Assert.Empty(_base.IntentosDePago);
    }

    [Fact]
    public async Task Si_la_pasarela_no_contesta_el_intento_queda_pendiente_y_se_retoma_con_la_misma_clave()
    {
        var perfil = UnPrestadorConSuscripcion();
        _pasarela.FallaAlIniciar = true;

        var primero = await _cobros.IniciarAsync(new IniciarCobroCmd(perfil.Id, "mensual", null, "k1"));

        Assert.False(primero.Exito);
        var intento = Assert.Single(_base.IntentosDePago);
        Assert.Equal(EstadoIntentoPago.Pendiente, intento.Estado);
        Assert.Null(intento.UrlParaPagar);

        // Que la pasarela no conteste no es culpa de quien queria pagar: el mismo envio
        // vuelve a pedir el cobro sobre el MISMO intento.
        _pasarela.FallaAlIniciar = false;
        var segundo = await _cobros.IniciarAsync(new IniciarCobroCmd(perfil.Id, "mensual", null, "k1"));

        Assert.True(segundo.Exito);
        Assert.NotNull(segundo.Valor!.UrlParaPagar);
        Assert.False(segundo.Valor!.Nueva);
        Assert.Single(_base.IntentosDePago);
    }
}
