using Microsoft.EntityFrameworkCore;
using Zass.Infraestructura.Datos;

namespace Zass.Integracion.Pruebas;

/// <summary>
/// Los textos de los planes no repiten el precio y llevan tildes.
/// Se ejecuta contra la base real despues de migrar.
/// </summary>
public class TextosDeLosPlanesPruebas : IDisposable
{
    private readonly ContextoDePrueba _prueba;

    public TextosDeLosPlanesPruebas()
    {
        _prueba = new ContextoDePrueba();
    }

    public void Dispose() => _prueba.Dispose();

    [Fact]
    public async Task Los_tres_planes_activos_no_contienen_pesos_ni_precio()
    {
        var planes = await _prueba.Ctx.Planes
            .Where(p => p.Activo)
            .OrderBy(p => p.Id)
            .ToListAsync();

        Assert.Equal(3, planes.Count);

        foreach (var plan in planes)
        {
            Assert.DoesNotContain("pesos", plan.Descripcion, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("5990", plan.Descripcion);
            Assert.DoesNotContain("7990", plan.Descripcion);
        }
    }

    [Fact]
    public async Task El_plan_fundador_contiene_suscripcion_con_tilde()
    {
        var fundador = await _prueba.Ctx.Planes
            .Where(p => p.Codigo == "fundador")
            .SingleAsync();

        Assert.Contains("suscripción", fundador.Descripcion, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Textos_exactos_de_los_tres_planes()
    {
        var planes = await _prueba.Ctx.Planes
            .Where(p => p.Activo)
            .OrderBy(p => p.Id)
            .ToListAsync();

        Assert.Equal(3, planes.Count);

        var prueba = planes.First(p => p.Id == 1);
        Assert.Equal("30 días para publicar tu perfil y aparecer en las búsquedas.", prueba.Descripcion);

        var mensual = planes.First(p => p.Id == 2);
        Assert.Equal("Publica tu perfil, aparece en las búsquedas y recibe solicitudes de hora. Cancela cuando quieras.", mensual.Descripcion);

        var fundador = planes.First(p => p.Id == 3);
        Assert.Equal("Precio de lanzamiento para las primeras 50 personas, congelado mientras mantengas tu suscripción activa.", fundador.Descripcion);
    }
}