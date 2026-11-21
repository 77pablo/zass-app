using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Zass.Infraestructura.Datos.Repositorios;

namespace Zass.Integracion.Pruebas;

/// <summary>
/// T67 (decisión de Pablo, 20-nov-2026): tres planes. Esencial $7.990 (5 servicios, 1 tarjeta),
/// Profesional $12.990 (15 servicios, 3 tarjetas, estadísticas, 12 fotos, verificación
/// prioritaria) y Equipo $24.990, inactivo («próximamente»). El Fundador recibe todo lo del
/// Profesional. Contra la base real, después de la migración PlanesEsencialProfesionalEquipo.
///
/// Ninguna de estas pruebas escribe en la base.
/// </summary>
public class TresPlanesPruebas : IAsyncLifetime
{
    private readonly Aplicacion _app = new();
    private HttpClient _cliente = null!;

    public Task InitializeAsync()
    {
        _cliente = _app.CreateClient(new WebApplicationFactoryClientOptions
        {
            HandleCookies = true,
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost"),
        });
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        _cliente.Dispose();
        await _app.DisposeAsync();
    }

    private async Task<List<JsonElement>> PlanesPublicadosAsync()
    {
        var respuesta = await _cliente.GetAsync("/api/planes");
        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        var cuerpo = await respuesta.Content.ReadFromJsonAsync<JsonDocument>();
        return cuerpo!.RootElement.GetProperty("planes").EnumerateArray().ToList();
    }

    private static JsonElement Plan(List<JsonElement> planes, string codigo) =>
        planes.Single(p => p.GetProperty("codigo").GetString() == codigo);

    // ------------------------------------------------------------ lo que ve /planes

    [Fact]
    public async Task P1_la_pagina_de_planes_trae_el_Profesional_y_NO_el_Equipo()
    {
        var planes = await PlanesPublicadosAsync();

        var profesional = Plan(planes, "profesional");
        Assert.Equal(12990, profesional.GetProperty("precioMensual").GetInt32());
        Assert.Equal(15, profesional.GetProperty("maxServicios").GetInt32());
        Assert.Equal(3, profesional.GetProperty("maxTarjetas").GetInt32());
        Assert.Equal(12, profesional.GetProperty("maxFotosTrabajos").GetInt32());
        Assert.True(profesional.GetProperty("permiteEstadisticas").GetBoolean());
        Assert.True(profesional.GetProperty("verificacionPrioritaria").GetBoolean());

        // El Equipo está «próximamente»: si saliera aquí, alguien podría intentar contratarlo.
        Assert.DoesNotContain(planes, p => p.GetProperty("codigo").GetString() == "equipo");
    }

    [Fact]
    public async Task P1b_el_Esencial_es_el_plan_mensual_con_5_servicios_y_sin_extras()
    {
        var esencial = Plan(await PlanesPublicadosAsync(), "mensual");

        Assert.Equal("ZASS! Esencial", esencial.GetProperty("nombre").GetString());
        Assert.Equal(7990, esencial.GetProperty("precioMensual").GetInt32());
        Assert.Equal(5, esencial.GetProperty("maxServicios").GetInt32());
        Assert.Equal(1, esencial.GetProperty("maxTarjetas").GetInt32());
        Assert.Equal(0, esencial.GetProperty("maxFotosTrabajos").GetInt32());
        Assert.False(esencial.GetProperty("permiteEstadisticas").GetBoolean());
        Assert.False(esencial.GetProperty("verificacionPrioritaria").GetBoolean());
    }

    // ------------------------------------------- lo que se puede contratar, en la base

    [Fact]
    public async Task P2_el_Equipo_no_se_puede_contratar_porque_el_cobro_no_lo_encuentra()
    {
        // El cobro (ServicioCobros.CalcularAsync) busca el plan con PlanPorCodigoAsync, que
        // solo devuelve planes activos. Si esto devolviera el Equipo, se podría pagar algo
        // que todavía no existe.
        using var ctx = ContextoDePrueba.Crear();
        var repo = new RepositorioSuscripcionesEfCore(ctx);

        Assert.Null(await repo.PlanPorCodigoAsync("equipo"));

        var profesional = await repo.PlanPorCodigoAsync("profesional");
        Assert.NotNull(profesional);
        Assert.Equal(12990, profesional!.PrecioMensual);

        // Y el Equipo sí existe en el catálogo, con sus datos, listo para cuando se abra.
        var equipo = await ctx.Planes.SingleAsync(p => p.Codigo == "equipo");
        Assert.False(equipo.Activo);
        Assert.Equal(24990, equipo.PrecioMensual);
        Assert.Equal(10, equipo.MaxTarjetas);
    }

    [Fact]
    public async Task P4_el_Fundador_tiene_todo_lo_del_Profesional_salvo_el_precio()
    {
        using var ctx = ContextoDePrueba.Crear();
        var fundador = await ctx.Planes.SingleAsync(p => p.Codigo == "fundador");
        var profesional = await ctx.Planes.SingleAsync(p => p.Codigo == "profesional");

        Assert.Equal(5990, fundador.PrecioMensual);
        Assert.Equal(profesional.MaxServicios, fundador.MaxServicios);
        Assert.Equal(profesional.MaxTarjetas, fundador.MaxTarjetas);
        Assert.Equal(profesional.MaxFotosTrabajos, fundador.MaxFotosTrabajos);
        Assert.Equal(profesional.PermiteEstadisticas, fundador.PermiteEstadisticas);
        Assert.Equal(profesional.VerificacionPrioritaria, fundador.VerificacionPrioritaria);
        Assert.True(fundador.VerificacionPrioritaria);
    }
}
