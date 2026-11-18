using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Zass.Integracion.Pruebas;

/// <summary>
/// Comprobar que ningun endpoint privado se queda sin autorizacion.
///
/// Los grupos bajo /api/mi, /api/citas/{id} y /api/interno llaman a
/// RequireAuthorization() (y los de /interno, ademas, RequireRole/RequireAuthorization con
/// politica). Si alguien crea un grupo nuevo y olvida esa linea, o mueve un endpoint al
/// grupo equivocado, ninguna prueba cae. Esta prueba de arquitectura lo detecta antes.
/// </summary>
public class AutorizacionEnRutasPrivadasPruebas : IAsyncLifetime
{
    private readonly AplicacionConProxy _app = new();

    public Task InitializeAsync()
    {
        _app.CreateClient();   // arranca la app entera (y valida la configuracion)
        return Task.CompletedTask;
    }

    public async Task DisposeAsync() => await _app.DisposeAsync();

    /// <summary>
    /// Todo endpoint cuya ruta comienza con /api/mi debe pedir sesion
    /// (RequireAuthorization). Anularse con AllowAnonymous no basta.
    /// </summary>
    [Fact]
    public void Todo_lo_de_api_mi_pide_sesion()
    {
        var fallos = _app.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Where(e => Ruta(e).StartsWith("/api/mi", StringComparison.Ordinal))
            .Where(e => !PideSesion(e))
            .Select(e => $"{e.DisplayName} ({Ruta(e)})")
            .ToList();

        Assert.True(fallos.Count == 0,
            "Rutas de /api/mi sin autorizacion:\n  " + string.Join("\n  ", fallos));
    }

    /// <summary>
    /// Todo endpoint cuya ruta comienza con /api/citas/ debe pedir sesion.
    /// Es el grupo de acciones sobre una cita concreta (acceptar, rechazar,
    /// completar), declarado en EndpointsAgenda.cs.
    /// </summary>
    [Fact]
    public void Todo_lo_de_api_citas_pide_sesion()
    {
        var fallos = _app.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Where(e => Ruta(e).StartsWith("/api/citas/", StringComparison.Ordinal))
            .Where(e => !PideSesion(e))
            .Select(e => $"{e.DisplayName} ({Ruta(e)})")
            .ToList();

        Assert.True(fallos.Count == 0,
            "Rutas de /api/citas sin autorizacion:\n  " + string.Join("\n  ", fallos));
    }

    /// <summary>
    /// Todo endpoint de /api/interno debe pedir mas que "tener sesion": requiere un rol o
    /// una politica. Solo RequireAuthorization() no basta; tiene que ser
    /// RequireAuthorization("politica"), RequireRole(...) o equivalente.
    /// </summary>
    [Fact]
    public void Todo_lo_de_api_interno_pide_un_rol_o_politica()
    {
        var fallos = _app.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Where(e => Ruta(e).StartsWith("/api/interno", StringComparison.Ordinal))
            .Where(e => !PideRolOPolitica(e))
            .Select(e => $"{e.DisplayName} ({Ruta(e)})")
            .ToList();

        Assert.True(fallos.Count == 0,
            "Rutas de /api/interno sin rol ni politica:\n  " + string.Join("\n  ", fallos));
    }

    /// <summary>
    /// Si la busqueda se rompe y no ve ningun endpoint de /api/mi, las tres pruebas
    /// anteriores pasarían en verde con cero rutas. Esta prueba exige al menos el
    /// numero medido para detectar que la busqueda esta rota.
    /// </summary>
    [Fact]
    public void La_prueba_ve_las_rutas_privadas()
    {
        var total = _app.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Count(e => Ruta(e).StartsWith("/api/mi", StringComparison.Ordinal));

        Assert.True(total >= 32,
            $"Solo se ven {total} endpoints de /api/mi: la busqueda esta rota. Debian ser 32 el 17-nov-2026.");
    }

    // -------------------------------------------------- funciones de ayuda

    /// <summary>
    /// La ruta completa del endpoint, normalizada para empezar con /.
    /// </summary>
    private static string Ruta(RouteEndpoint e) =>
        "/" + (e.RoutePattern.RawText ?? "").TrimStart('/');

    /// <summary>
    /// El endpoint pide sesion si:
    /// - no tiene AllowAnonymous, Y
    /// - tiene al menos un IAuthorizeData o un AuthorizationPolicy registrado.
    /// </summary>
    private static bool PideSesion(RouteEndpoint e) =>
        e.Metadata.GetMetadata<IAllowAnonymous>() is null
        && (e.Metadata.GetOrderedMetadata<IAuthorizeData>().Any()
            || e.Metadata.GetOrderedMetadata<AuthorizationPolicy>().Any());

    /// <summary>
    /// El endpoint pide rol o politica si ademas de pedir sesion tiene:
    /// - IAuthorizeData con Policy o Roles no vacio, O
    /// - AuthorizationPolicy con algun Requirement que no sea DenyAnonymous.
    /// </summary>
    private static bool PideRolOPolitica(RouteEndpoint e) =>
        PideSesion(e)
        && (e.Metadata.GetOrderedMetadata<IAuthorizeData>()
                .Any(a => !string.IsNullOrEmpty(a.Policy) || !string.IsNullOrEmpty(a.Roles))
            || e.Metadata.GetOrderedMetadata<AuthorizationPolicy>()
                .Any(p => p.Requirements.Any(r => r is not DenyAnonymousAuthorizationRequirement)));
}
