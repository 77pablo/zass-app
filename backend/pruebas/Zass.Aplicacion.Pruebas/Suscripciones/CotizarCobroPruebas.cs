using Zass.Aplicacion.Pruebas.Dobles;
using Zass.Aplicacion.Suscripciones;
using Zass.Dominio.Comun;
using Zass.Dominio.Identidad;
using Zass.Dominio.Suscripciones;

namespace Zass.Aplicacion.Pruebas.Suscripciones;

/// <summary>
/// MOD-13 · S7. Cotizar no escribe nada: dice cuanto se cobraria hoy por ese plan, con
/// ese cupon, a esa cuenta.
///
/// Existe porque la pantalla necesita mostrar «pagas $5.593 con tu codigo» ANTES de que
/// exista un cobro, y porque el precio que se le promete a la persona es el que despues
/// se compara contra lo que conteste la pasarela.
///
/// Los numeros van escritos a mano a proposito (7990, 5990, 2397, 5593). Una prueba que
/// saca el valor esperado de la misma constante que vigila no vigila nada: si alguien
/// cambia el precio, la prueba cambia con el y sigue verde.
/// </summary>
public class CotizarCobroPruebas
{
    private static readonly DateTime Ahora = new(2026, 11, 13, 12, 0, 0, DateTimeKind.Utc);

    private readonly BaseEnMemoria _base = new();
    private readonly RelojFijo _reloj = new(Ahora);
    private readonly PasarelaFalsa _pasarela = new();
    private readonly ServicioCobros _cobros;

    public CotizarCobroPruebas()
    {
        var repo = new RepositorioSuscripcionesEnMemoria(_base);
        var suscripciones = new ServicioSuscripciones(
            repo, _base, _reloj, new AuditoriaEnMemoria(), new ColaEnMemoria());
        _cobros = new ServicioCobros(
            repo, suscripciones, _pasarela, _base, _reloj, new AuditoriaEnMemoria());

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

    private void UnCupon(string codigo, short porcentaje = 30, DateTime? vence = null, bool activo = true)
        => _base.Cupones.Add(new Cupon
        {
            Codigo = codigo,
            PorcentajeDescuento = porcentaje,
            VenceEn = vence ?? Ahora.AddDays(30),
            Activo = activo,
        });

    /// <summary>Deja los 50 cupos tomados por otras personas.</summary>
    private void SinCuposLibres()
    {
        foreach (var cupo in _base.CuposFundador)
        {
            cupo.PerfilPrestadorId = Guid.NewGuid();
            cupo.ReservadoHasta = null;
        }
    }

    // ------------------------------------------------------------------ Los precios

    [Fact]
    public async Task El_plan_mensual_cuesta_7990_sin_cupon()
    {
        var perfil = UnPrestador();

        var r = await _cobros.CotizarAsync(perfil.Id, "mensual", null);

        Assert.True(r.Exito);
        Assert.Equal(7990, r.Valor!.PrecioMensual);
        Assert.Equal(0, r.Valor!.Descuento);
        Assert.Equal(7990, r.Valor!.Total);
        Assert.Null(r.Valor!.Cupon);
    }

    [Fact]
    public async Task El_plan_fundador_cuesta_5990()
    {
        var perfil = UnPrestador();

        var r = await _cobros.CotizarAsync(perfil.Id, "fundador", null);

        Assert.True(r.Exito);
        Assert.Equal(5990, r.Valor!.Total);
    }

    [Fact]
    public async Task El_plan_de_prueba_no_se_cotiza_porque_no_se_paga()
    {
        var perfil = UnPrestador();

        var r = await _cobros.CotizarAsync(perfil.Id, "prueba", null);

        // Dejar pasar un cobro de cero crearia un intento que ninguna pasarela aceptaria.
        Assert.False(r.Exito);
        Assert.Equal(Fallas.Validacion, r.Codigo);
    }

    [Fact]
    public async Task Un_plan_que_no_existe_no_se_cotiza()
    {
        var perfil = UnPrestador();

        var r = await _cobros.CotizarAsync(perfil.Id, "platino", null);

        Assert.False(r.Exito);
        Assert.Equal(Fallas.NoEncontrado, r.Codigo);
    }

    [Fact]
    public async Task Un_perfil_que_no_existe_no_cotiza()
    {
        var r = await _cobros.CotizarAsync(Guid.NewGuid(), "mensual", null);

        Assert.False(r.Exito);
        Assert.Equal(Fallas.NoEncontrado, r.Codigo);
    }

    // -------------------------------------------------------------------- El cupon

    [Fact]
    public async Task El_cupon_del_30_por_ciento_descuenta_2397_del_mensual()
    {
        var perfil = UnPrestador();
        UnCupon("LANZAMIENTO30");

        var r = await _cobros.CotizarAsync(perfil.Id, "mensual", "LANZAMIENTO30");

        Assert.True(r.Exito);
        // 30 % de 7990 = 2397. Los tres numeros, a mano.
        Assert.Equal(7990, r.Valor!.PrecioMensual);
        Assert.Equal(2397, r.Valor!.Descuento);
        Assert.Equal(5593, r.Valor!.Total);
        Assert.Equal((short)30, r.Valor!.PorcentajeDescuento);
        Assert.Equal("LANZAMIENTO30", r.Valor!.Cupon);
    }

    [Fact]
    public async Task El_mismo_cupon_sobre_el_fundador_descuenta_1797()
    {
        var perfil = UnPrestador();
        UnCupon("LANZAMIENTO30");

        var r = await _cobros.CotizarAsync(perfil.Id, "fundador", "LANZAMIENTO30");

        Assert.True(r.Exito);
        // 30 % de 5990 = 1797 exacto.
        Assert.Equal(1797, r.Valor!.Descuento);
        Assert.Equal(4193, r.Valor!.Total);
    }

    [Fact]
    public async Task El_descuento_se_trunca_y_nunca_deja_el_total_sobre_el_precio()
    {
        var perfil = UnPrestador();
        UnCupon("TERCIO", porcentaje: 33);

        var r = await _cobros.CotizarAsync(perfil.Id, "mensual", "TERCIO");

        // 33 % de 7990 son 2636,7: se trunca a 2636, no se redondea a 2637.
        Assert.Equal(2636, r.Valor!.Descuento);
        Assert.Equal(5354, r.Valor!.Total);
        Assert.True(r.Valor!.Total <= r.Valor!.PrecioMensual);
    }

    [Fact]
    public async Task Un_cupon_que_no_existe_FALLA_en_vez_de_cobrar_el_precio_completo()
    {
        var perfil = UnPrestador();

        var r = await _cobros.CotizarAsync(perfil.Id, "mensual", "NOEXISTE");

        // Cobrarle el precio completo a quien escribio un codigo seria cobrarle algo que
        // no acepto.
        Assert.False(r.Exito);
        Assert.Equal(Fallas.NoEncontrado, r.Codigo);
    }

    [Fact]
    public async Task Un_cupon_vencido_no_sirve()
    {
        var perfil = UnPrestador();
        UnCupon("VIEJO", vence: Ahora.AddDays(-1));

        var r = await _cobros.CotizarAsync(perfil.Id, "mensual", "VIEJO");

        Assert.False(r.Exito);
        Assert.Equal(Fallas.Validacion, r.Codigo);
    }

    [Fact]
    public async Task Un_cupon_apagado_no_sirve_aunque_la_fecha_no_haya_llegado()
    {
        var perfil = UnPrestador();
        UnCupon("CORTADO", activo: false);

        var r = await _cobros.CotizarAsync(perfil.Id, "mensual", "CORTADO");

        // El interruptor y la fecha son dos cosas distintas a proposito: se puede cortar
        // un cupon antes de su vencimiento sin falsear la fecha.
        Assert.False(r.Exito);
        Assert.Equal(Fallas.Validacion, r.Codigo);
    }

    [Fact]
    public async Task La_cuenta_que_ya_canjeo_un_cupon_no_puede_usar_otro()
    {
        var perfil = UnPrestador();
        UnCupon("LANZAMIENTO30");
        _base.Canjes.Add(new CanjeDeCupon
        {
            CodigoCupon = "OTRO", PerfilPrestadorId = perfil.Id, CanjeadoEn = Ahora.AddDays(-5),
        });

        var r = await _cobros.CotizarAsync(perfil.Id, "mensual", "LANZAMIENTO30");

        // Uno por cuenta y para siempre (decision de Pablo, 12-nov-2026).
        Assert.False(r.Exito);
        Assert.Equal(Fallas.Conflicto, r.Codigo);
    }

    [Fact]
    public async Task Sin_cupon_la_cuenta_que_ya_canjeo_si_puede_cotizar()
    {
        var perfil = UnPrestador();
        _base.Canjes.Add(new CanjeDeCupon
        {
            CodigoCupon = "OTRO", PerfilPrestadorId = perfil.Id, CanjeadoEn = Ahora.AddDays(-5),
        });

        var r = await _cobros.CotizarAsync(perfil.Id, "mensual", null);

        // Haber gastado el cupon no deja a nadie sin poder pagar su mes.
        Assert.True(r.Exito);
        Assert.Equal(7990, r.Valor!.Total);
    }

    // ------------------------------------------------- El cierre al cupo 50 (RN-S7-06)

    [Fact]
    public async Task Sin_cupos_libres_el_plan_fundador_no_se_cotiza_a_quien_no_tiene_cupo()
    {
        SinCuposLibres();
        var perfil = UnPrestador();

        var r = await _cobros.CotizarAsync(perfil.Id, "fundador", null);

        Assert.False(r.Exito);
        Assert.Equal(Fallas.Conflicto, r.Codigo);
        Assert.Contains("mensual", r.Mensaje!);   // ofrece la salida, no deja a nadie sin nada
    }

    [Fact]
    public async Task Sin_cupos_libres_pero_CON_cupo_propio_si_se_cotiza_al_precio_de_lanzamiento()
    {
        var perfil = UnPrestador();
        SinCuposLibres();
        var mio = _base.CuposFundador.First();
        mio.PerfilPrestadorId = perfil.Id;
        mio.ReservadoHasta = null;

        var r = await _cobros.CotizarAsync(perfil.Id, "fundador", null);

        // El precio congelado es suyo mientras mantenga la suscripcion, y eso vale tambien
        // cuando ya no quedan cupos para nadie mas.
        Assert.True(r.Exito);
        Assert.Equal(5990, r.Valor!.Total);
    }

    [Fact]
    public async Task Sin_cupos_libres_el_plan_mensual_se_cotiza_igual()
    {
        SinCuposLibres();
        var perfil = UnPrestador();

        var r = await _cobros.CotizarAsync(perfil.Id, "mensual", null);

        Assert.True(r.Exito);
        Assert.Equal(7990, r.Valor!.Total);
    }

    // ------------------------------------------------------------------ No escribe

    [Fact]
    public async Task Cotizar_no_escribe_NADA()
    {
        var perfil = UnPrestador();
        UnCupon("LANZAMIENTO30");

        await _cobros.CotizarAsync(perfil.Id, "mensual", "LANZAMIENTO30");

        Assert.Empty(_base.IntentosDePago);
        Assert.Empty(_base.Canjes);
        Assert.Empty(_base.Pagos);
        Assert.Equal(0, _pasarela.CobrosPedidos);
        Assert.Equal(50, await new ServicioSuscripciones(
            new RepositorioSuscripcionesEnMemoria(_base), _base, _reloj,
            new AuditoriaEnMemoria(), new ColaEnMemoria()).CuposFundadorDisponiblesAsync());
    }
}
