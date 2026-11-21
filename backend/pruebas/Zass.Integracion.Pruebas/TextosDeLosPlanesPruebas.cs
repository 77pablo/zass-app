using Microsoft.EntityFrameworkCore;
using Zass.Infraestructura.Datos;

namespace Zass.Integracion.Pruebas;

/// <summary>
/// Los textos de los planes no repiten el precio y llevan tildes (T64, PR #184).
/// Desde la T67 hay cuatro planes activos (prueba, Esencial, Fundador y Profesional) y uno
/// inactivo (Equipo, «próximamente»). Se ejecuta contra la base real después de migrar.
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
    public async Task Ningun_plan_activo_repite_el_precio_en_su_descripcion()
    {
        var planes = await _prueba.Ctx.Planes
            .Where(p => p.Activo)
            .OrderBy(p => p.Id)
            .ToListAsync();

        Assert.Equal(4, planes.Count);

        foreach (var plan in planes)
        {
            Assert.DoesNotContain("pesos", plan.Descripcion, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("5990", plan.Descripcion);
            Assert.DoesNotContain("7990", plan.Descripcion);
            Assert.DoesNotContain("12990", plan.Descripcion);
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
    public async Task Textos_exactos_de_los_planes()
    {
        var planes = await _prueba.Ctx.Planes.OrderBy(p => p.Id).ToListAsync();

        Assert.Equal(
            "30 días para publicar tu perfil y aparecer en las búsquedas.",
            planes.Single(p => p.Id == 1).Descripcion);
        Assert.Equal(
            "Para empezar: tu ficha en la búsqueda, agenda en línea, reseñas y sello Verificado. Hasta 5 servicios.",
            planes.Single(p => p.Id == 2).Descripcion);
        Assert.Equal(
            "Todo lo del plan Profesional a precio de lanzamiento, congelado mientras mantengas tu suscripción activa. Solo para las primeras 50 personas.",
            planes.Single(p => p.Id == 3).Descripcion);
        Assert.Equal(
            "Para quien vive de su trabajo: estadísticas de tu ficha, fotos de tus trabajos, verificación prioritaria, hasta 15 servicios y 3 tarjetas.",
            planes.Single(p => p.Id == 4).Descripcion);
        Assert.Equal(
            "Para estudios, consultas y talleres: hasta 5 profesionales, cada uno con todo lo del Profesional. Próximamente.",
            planes.Single(p => p.Id == 5).Descripcion);
    }
}
