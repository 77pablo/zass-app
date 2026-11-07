using Zass.Aplicacion.Pruebas.Dobles;
using Zass.Aplicacion.Suscripciones;
using Zass.Dominio.Comun;
using Zass.Dominio.Identidad;
using Zass.Dominio.Suscripciones;

namespace Zass.Aplicacion.Pruebas.Suscripciones;

/// <summary>
/// MOD-13 - S7. Lo que decide si un prestador aparece en la busqueda (RN-S3-09).
/// </summary>
public class SuscripcionesPruebas
{
    private static readonly DateTime Ahora = new(2026, 11, 7, 12, 0, 0, DateTimeKind.Utc);

    private readonly BaseEnMemoria _base = new();
    private readonly RelojFijo _reloj = new(Ahora);
    private readonly AuditoriaEnMemoria _auditor = new();
    private readonly ServicioSuscripciones _servicio;

    public SuscripcionesPruebas()
    {
        _base.Planes.Add(new Plan
        {
            Id = 1,
            Codigo = "prueba",
            Nombre = "Prueba",
            PrecioMensual = 0,
            DiasPrueba = 30,
            Activo = true,
        });
        _servicio = new ServicioSuscripciones(
            new RepositorioSuscripcionesEnMemoria(_base), _base, _reloj, _auditor);
    }

    private PerfilPrestador UnPrestador()
    {
        var perfil = new PerfilPrestador { Id = Guid.NewGuid(), UsuarioId = Guid.NewGuid() };
        _base.PerfilesPrestador.Add(perfil);
        return perfil;
    }

    private Suscripcion ConSuscripcion(Guid perfilId, EstadoSuscripcion estado, DateTime hasta)
    {
        var s = new Suscripcion
        {
            Id = Guid.NewGuid(),
            PerfilPrestadorId = perfilId,
            PlanId = 1,
            Estado = estado,
            IniciaEn = Ahora.AddDays(-10),
            VigenteHasta = hasta,
        };
        _base.Suscripciones.Add(s);
        return s;
    }

    // ------------------------------------------------- Que significa "vigente"

    [Fact]
    public async Task Activa_con_fecha_futura_esta_vigente()
    {
        var p = UnPrestador();
        ConSuscripcion(p.Id, EstadoSuscripcion.Activa, Ahora.AddDays(5));

        Assert.True(await _servicio.EstaVigenteAsync(p.Id));
    }

    [Fact]
    public async Task En_prueba_con_fecha_futura_esta_vigente()
    {
        var p = UnPrestador();
        ConSuscripcion(p.Id, EstadoSuscripcion.Prueba, Ahora.AddDays(20));

        Assert.True(await _servicio.EstaVigenteAsync(p.Id));
    }

    // El trabajo de vencimiento puede no haber corrido todavia. Si solo se mirara el
    // estado, un prestador impago seguiria apareciendo hasta que alguien lo marcara.
    [Fact]
    public async Task Activa_con_fecha_pasada_NO_esta_vigente_aunque_nadie_la_haya_vencido()
    {
        var p = UnPrestador();
        ConSuscripcion(p.Id, EstadoSuscripcion.Activa, Ahora.AddDays(-1));

        Assert.False(await _servicio.EstaVigenteAsync(p.Id));
    }

    [Theory]
    [InlineData(EstadoSuscripcion.Vencida)]
    [InlineData(EstadoSuscripcion.Cancelada)]
    public async Task Vencida_o_cancelada_no_esta_vigente_aunque_la_fecha_sea_futura(
        EstadoSuscripcion estado)
    {
        var p = UnPrestador();
        ConSuscripcion(p.Id, estado, Ahora.AddDays(30));

        Assert.False(await _servicio.EstaVigenteAsync(p.Id));
    }

    [Fact]
    public async Task Sin_ninguna_suscripcion_no_esta_vigente()
    {
        var p = UnPrestador();

        Assert.False(await _servicio.EstaVigenteAsync(p.Id));
    }

    // ------------------------------------------------- La consulta en lote

    [Fact]
    public async Task VigentesEntre_devuelve_solo_los_vigentes()
    {
        var bueno = UnPrestador();
        var vencido = UnPrestador();
        var sinNada = UnPrestador();
        ConSuscripcion(bueno.Id, EstadoSuscripcion.Activa, Ahora.AddDays(5));
        ConSuscripcion(vencido.Id, EstadoSuscripcion.Vencida, Ahora.AddDays(-5));

        var vigentes = await _servicio.VigentesEntreAsync(
            new[] { bueno.Id, vencido.Id, sinNada.Id });

        Assert.Equal(new[] { bueno.Id }, vigentes);
    }

    // El N+1 en la ruta mas caliente del producto: 20 resultados no pueden ser 20
    // consultas. Por eso la busqueda llama a VigentesEntre y no a EstaVigente.
    [Fact]
    public async Task VigentesEntre_consulta_el_repositorio_UNA_sola_vez()
    {
        var repo = new RepositorioSuscripcionesEnMemoria(_base);
        var servicio = new ServicioSuscripciones(repo, _base, _reloj, _auditor);
        var ids = Enumerable.Range(0, 20).Select(_ => UnPrestador().Id).ToArray();
        foreach (var id in ids) ConSuscripcion(id, EstadoSuscripcion.Activa, Ahora.AddDays(5));

        await servicio.VigentesEntreAsync(ids);

        Assert.Equal(1, repo.ConsultasEnLote);
    }

    [Fact]
    public async Task VigentesEntre_con_lista_vacia_no_toca_el_repositorio()
    {
        var repo = new RepositorioSuscripcionesEnMemoria(_base);
        var servicio = new ServicioSuscripciones(repo, _base, _reloj, _auditor);

        var vigentes = await servicio.VigentesEntreAsync(Array.Empty<Guid>());

        Assert.Empty(vigentes);
        Assert.Equal(0, repo.ConsultasEnLote);
    }

    // ------------------------------------------------- Iniciar la prueba

    [Fact]
    public async Task Iniciar_prueba_crea_una_suscripcion_de_los_dias_del_plan()
    {
        var p = UnPrestador();

        var r = await _servicio.IniciarPruebaAsync(p.Id);

        Assert.True(r.Exito);
        var s = Assert.Single(_base.Suscripciones);
        Assert.Equal(EstadoSuscripcion.Prueba, s.Estado);
        Assert.Equal(Ahora.AddDays(30), s.VigenteHasta);
        Assert.True(await _servicio.EstaVigenteAsync(p.Id));
    }

    // Lo hace cumplir el indice unico parcial ux_suscripcion_vigente, pero llegar a el
    // con un 23505 convertiria un doble clic en un error del sistema.
    [Fact]
    public async Task Iniciar_prueba_dos_veces_no_crea_la_segunda()
    {
        var p = UnPrestador();
        await _servicio.IniciarPruebaAsync(p.Id);

        var r = await _servicio.IniciarPruebaAsync(p.Id);

        Assert.True(r.Exito);
        Assert.Single(_base.Suscripciones);
    }

    [Fact]
    public async Task Quien_tuvo_una_vencida_puede_empezar_otra()
    {
        var p = UnPrestador();
        ConSuscripcion(p.Id, EstadoSuscripcion.Vencida, Ahora.AddDays(-30));

        var r = await _servicio.IniciarPruebaAsync(p.Id);

        Assert.True(r.Exito);
        Assert.Equal(2, _base.Suscripciones.Count);
        Assert.True(await _servicio.EstaVigenteAsync(p.Id));
    }

    [Fact]
    public async Task Iniciar_prueba_de_un_perfil_que_no_existe_falla()
    {
        var r = await _servicio.IniciarPruebaAsync(Guid.NewGuid());

        Assert.False(r.Exito);
        Assert.Equal(Fallas.NoEncontrado, r.Codigo);
    }

    [Fact]
    public async Task Iniciar_prueba_queda_auditado()
    {
        var p = UnPrestador();

        await _servicio.IniciarPruebaAsync(p.Id);

        Assert.Contains(_auditor.Registros, e => e.Accion == "suscripcion_prueba_iniciada");
    }

    // ------------------------------------------------- El pago

    // Quien paga el dia 25 de un mes de 30 no puede perder los 5 que le quedaban.
    [Fact]
    public async Task Pagar_antes_de_vencer_suma_al_tiempo_que_quedaba()
    {
        var p = UnPrestador();
        var s = ConSuscripcion(p.Id, EstadoSuscripcion.Activa, Ahora.AddDays(5));

        await _servicio.RegistrarPeriodoPagadoAsync(s.Id, 1);

        Assert.Equal(Ahora.AddDays(5).AddMonths(1), s.VigenteHasta);
    }

    [Fact]
    public async Task Pagar_despues_de_vencer_cuenta_desde_hoy_no_desde_la_fecha_vieja()
    {
        var p = UnPrestador();
        var s = ConSuscripcion(p.Id, EstadoSuscripcion.Vencida, Ahora.AddDays(-40));

        await _servicio.RegistrarPeriodoPagadoAsync(s.Id, 1);

        Assert.Equal(Ahora.AddMonths(1), s.VigenteHasta);
        Assert.Equal(EstadoSuscripcion.Activa, s.Estado);
    }

    [Fact]
    public async Task Pagar_deja_registro_del_cobro()
    {
        var p = UnPrestador();
        var s = ConSuscripcion(p.Id, EstadoSuscripcion.Activa, Ahora.AddDays(5));

        await _servicio.RegistrarPeriodoPagadoAsync(s.Id, 1, 9990, "trx-123");

        var pago = Assert.Single(_base.Pagos);
        Assert.Equal(9990, pago.Monto);
        Assert.Equal(EstadoPago.Pagado, pago.Estado);
        Assert.Equal("trx-123", pago.ReferenciaPasarela);
    }

    [Fact]
    public async Task Pagar_sobre_una_suscripcion_que_no_existe_falla()
    {
        var r = await _servicio.RegistrarPeriodoPagadoAsync(Guid.NewGuid(), 1);

        Assert.False(r.Exito);
        Assert.Equal(Fallas.NoEncontrado, r.Codigo);
    }

    // ------------------------------------------------- Vencer

    [Fact]
    public async Task Vencer_expiradas_marca_solo_las_que_pasaron_de_fecha()
    {
        var a = UnPrestador();
        var b = UnPrestador();
        var c = UnPrestador();
        var vencible = ConSuscripcion(a.Id, EstadoSuscripcion.Activa, Ahora.AddDays(-1));
        var alDia = ConSuscripcion(b.Id, EstadoSuscripcion.Activa, Ahora.AddDays(1));
        var yaCancelada = ConSuscripcion(c.Id, EstadoSuscripcion.Cancelada, Ahora.AddDays(-9));

        var cuantas = await _servicio.VencerExpiradasAsync();

        Assert.Equal(1, cuantas);
        Assert.Equal(EstadoSuscripcion.Vencida, vencible.Estado);
        Assert.Equal(EstadoSuscripcion.Activa, alDia.Estado);
        Assert.Equal(EstadoSuscripcion.Cancelada, yaCancelada.Estado);
    }

    [Fact]
    public async Task Al_vencer_se_apaga_la_renovacion_automatica()
    {
        var p = UnPrestador();
        var s = ConSuscripcion(p.Id, EstadoSuscripcion.Activa, Ahora.AddDays(-1));
        s.RenovacionAutomatica = true;

        await _servicio.VencerExpiradasAsync();

        Assert.False(s.RenovacionAutomatica);
    }

    // Desaparecer de la busqueda sin aviso es lo que volveria cruel a RN-S3-09.
    [Fact]
    public async Task Las_que_estan_por_vencer_se_pueden_listar_para_avisar()
    {
        var a = UnPrestador();
        var b = UnPrestador();
        ConSuscripcion(a.Id, EstadoSuscripcion.Activa, Ahora.AddDays(3));
        ConSuscripcion(b.Id, EstadoSuscripcion.Activa, Ahora.AddDays(20));

        var porVencer = await _servicio.PorVencerAsync(7);

        Assert.Single(porVencer);
        Assert.Equal(a.Id, porVencer[0].PerfilPrestadorId);
    }

    // ------------------------------------------------- Cancelar y consultar

    [Fact]
    public async Task Cancelar_la_saca_de_la_busqueda_de_inmediato()
    {
        var p = UnPrestador();
        var s = ConSuscripcion(p.Id, EstadoSuscripcion.Activa, Ahora.AddDays(20));

        var r = await _servicio.CancelarAsync(s.Id);

        Assert.True(r.Exito);
        Assert.Equal(EstadoSuscripcion.Cancelada, s.Estado);
        Assert.False(await _servicio.EstaVigenteAsync(p.Id));
    }

    [Fact]
    public async Task La_consulta_del_prestador_dice_los_dias_que_le_quedan()
    {
        var p = UnPrestador();
        ConSuscripcion(p.Id, EstadoSuscripcion.Prueba, Ahora.AddDays(12));

        var r = await _servicio.DePrestadorAsync(p.Id);

        Assert.True(r.Exito);
        Assert.Equal(12, r.Valor!.DiasRestantes);
        Assert.True(r.Valor.Vigente);
        Assert.Equal("Prueba", r.Valor.Plan);
    }

    [Fact]
    public async Task Un_prestador_sin_suscripcion_lo_dice_sin_reventar()
    {
        var p = UnPrestador();

        var r = await _servicio.DePrestadorAsync(p.Id);

        Assert.True(r.Exito);
        Assert.False(r.Valor!.Vigente);
        Assert.Equal(0, r.Valor.DiasRestantes);
    }

    // El DTO lo ve el prestador en su panel: no puede llevar el identificador de la
    // pasarela, que es dato de facturacion y no tiene nada que hacer ahi.
    [Fact]
    public void El_dto_no_expone_la_referencia_de_la_pasarela()
    {
        var campos = typeof(SuscripcionDto).GetProperties().Select(x => x.Name);

        Assert.DoesNotContain(
            campos, n => n.Contains("Pasarela", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Solo_se_ofrecen_los_planes_activos()
    {
        _base.Planes.Add(new Plan { Id = 2, Codigo = "viejo", Nombre = "Retirado", Activo = false });

        var planes = await _servicio.PlanesActivosAsync();

        Assert.Single(planes);
        Assert.Equal("prueba", planes[0].Codigo);
    }
}
