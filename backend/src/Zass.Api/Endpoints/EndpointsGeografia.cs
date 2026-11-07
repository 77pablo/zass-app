using System.Security.Claims;
using Zass.Api.Configuracion;
using Zass.Aplicacion.Geografia;
using Zass.Aplicacion.Identidad;
using Zass.Dominio.Comun;

namespace Zass.Api.Endpoints;

/// <summary>API-21. La lista completa de zonas cubiertas, no un parche.</summary>
public sealed record ReemplazarCoberturaHttpCmd(IReadOnlyList<ZonaCubiertaCmd> Zonas);

/// <summary>
/// API-04 y API-21. MOD-04 Geolocalizacion.
///
/// API-04 es publico porque el selector de zonas lo necesitan la busqueda (CU-08) y el
/// formulario de perfil (CU-03) antes de que haya sesion.
/// </summary>
public static class EndpointsGeografia
{
    public static IEndpointRouteBuilder MapearGeografia(this IEndpointRouteBuilder app)
    {
        MapearPublico(app.MapGroup("/api"));
        MapearMiCobertura(app.MapGroup("/api/mi"));
        return app;
    }

    private static void MapearPublico(RouteGroupBuilder grupo)
    {
        // API-04 - Barrios y sectores, con ?sector= opcional.
        grupo.MapGet("/zonas", async (IServicioGeografia geografia, string? sector) =>
                Results.Ok(await geografia.ZonasAsync(sector)))
            .RequireRateLimiting(Tasa.Publico)
            .WithName("API-04");
    }

    private static void MapearMiCobertura(RouteGroupBuilder grupo)
    {
        grupo.RequireAuthorization();
        grupo.AddEndpointFilter<FiltroCsrf>();

        // Leer la cobertura guardada. Igual que en API-20, el documento solo preveia el
        // PUT: sin el GET, el formulario de CU-10 no puede pintarse con lo que ya hay.
        grupo.MapGet("/perfil-prestador/cobertura", async (
                IServicioGeografia geografia, IServicioIdentidad identidad,
                ClaimsPrincipal quien) =>
            {
                var usuarioId = quien.UsuarioId();
                if (usuarioId is null) return Results.Unauthorized();

                var perfilId = await identidad.PerfilPrestadorIdAsync(usuarioId.Value);
                if (perfilId is null)
                    return Respuestas.Problema(Resultado.Falla(
                        Fallas.NoEncontrado, "Esta cuenta no tiene perfil de prestador."));

                return Results.Ok(await geografia.ZonasDeCoberturaAsync(perfilId.Value));
            })
            .RequireRateLimiting(Tasa.General)
            .WithName("API-21b");

        // API-21 - Reemplaza las zonas cubiertas (CU-10).
        grupo.MapPut("/perfil-prestador/cobertura", async (
                ReemplazarCoberturaHttpCmd cmd, IServicioGeografia geografia,
                IServicioIdentidad identidad, ClaimsPrincipal quien) =>
            {
                var usuarioId = quien.UsuarioId();
                if (usuarioId is null) return Results.Unauthorized();

                // El perfil se busca POR el usuario de la sesion, nunca por un id que
                // venga en la peticion: asi no hay identificador que cambiar para editar
                // la cobertura de otro.
                var perfilId = await identidad.PerfilPrestadorIdAsync(usuarioId.Value);
                if (perfilId is null)
                    return Respuestas.Problema(Resultado.Falla(
                        Fallas.NoEncontrado, "Esta cuenta no tiene perfil de prestador."));

                return Respuestas.DeResultado(await geografia.ReemplazarCoberturaAsync(
                    new ReemplazarCoberturaCmd(perfilId.Value, cmd.Zonas)));
            })
            .ConValidacion<ReemplazarCoberturaHttpCmd>()
            .RequireRateLimiting(Tasa.General)
            .WithName("API-21");
    }
}
