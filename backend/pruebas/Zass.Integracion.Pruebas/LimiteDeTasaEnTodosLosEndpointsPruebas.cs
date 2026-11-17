using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Zass.Integracion.Pruebas;

/// <summary>
/// seguridad.md, regla 1: «Límite de tasa en todos los endpoints». Hoy los 74 endpoints declaran
/// una política con `.RequireRateLimiting(Tasa.*)`, pero **ninguna prueba lo exige**: un endpoint
/// nuevo sin límite pasaría en verde. Esta es la prueba de arquitectura que la regla cita.
/// </summary>
/// <remarks>
/// El mecanismo es el mismo que <c>ApiPruebas.Ningun_endpoint_repite_el_nombre_de_otro</c>: lee
/// los endpoints registrados en <c>EndpointDataSource</c> y falla la compilación (la prueba roja)
/// antes de que nadie los toque. Un endpoint sin <c>EnableRateLimitingAttribute</c> no tiene
/// límite, y el valor por omisión no es «sin límite»: es error de build.
/// </remarks>
public class LimiteDeTasaEnTodosLosEndpointsPruebas : IAsyncLifetime
{
    private readonly AplicacionConProxy _app = new();

    public Task InitializeAsync()
    {
        _app.CreateClient();   // arranca la app entera (y valida la configuración)
        return Task.CompletedTask;
    }

    public async Task DisposeAsync() => await _app.DisposeAsync();

    /// <summary>
    /// Todo endpoint de ruta tiene que haber declarado una política de límite. Swagger no es un
    /// endpoint de la aplicación: se sirve desde su propio middleware y no lleva límite, así que
    /// se excluye. Lo que no se excluye es un endpoint nuevo que olvida `.RequireRateLimiting`.
    /// </summary>
    [Fact]
    public void Todo_endpoint_tiene_limite_de_tasa()
    {
        var sinLimite = _app.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Where(e => !EsSwagger(e))
            .Where(e => e.Metadata.GetMetadata<EnableRateLimitingAttribute>() is null)
            .Select(e => $"{e.DisplayName} ({e.RoutePattern.RawText})")
            .ToList();

        Assert.True(sinLimite.Count == 0,
            "Endpoints sin limite de tasa:\n  " + string.Join("\n  ", sinLimite));
    }

    /// <summary>
    /// Una búsqueda rota (cero endpoints, o la mitad) pasaría en verde arriba. El 17-nov-2026
    /// eran 74 endpoints de ruta. Con esto, si el registrations dejan de funcionar, caemos.
    /// </summary>
    [Fact]
    public void La_prueba_ve_los_endpoints_que_hay()
    {
        var total = _app.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Count();

        Assert.True(total >= 70,
            $"Solo se ven {total} endpoints: la busqueda esta rota. Deian ser 74 el 17-nov-2026.");
    }

    /// <summary>
    /// Swagger (UI y JSON) se registra con rutas que empiezan por «swagger». No son endpoints de
    /// la aplicación y no llevan límite de tasa: se sirven desde su propio middleware, que ya
    /// tiene su propio control de acceso. Excluirlos no debilita la regla: la regla es sobre los
    /// endpoints de ZASS, no sobre los de la herramienta de documentación.
    /// </summary>
    private static bool EsSwagger(RouteEndpoint e) =>
        (e.RoutePattern.RawText ?? "").StartsWith("swagger", StringComparison.OrdinalIgnoreCase);
}