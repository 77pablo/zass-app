using System.Security.Claims;
using Zass.Aplicacion.Credenciales;
using Zass.Api.Configuracion;

namespace Zass.Api.Endpoints;

/// <summary>
/// MOD-02 - Verificación manual (CU-06). Todo vive bajo /api/interno y exige el rol
/// `verificador`: quien no lo tiene recibe 403 sin que nada se ejecute, tampoco para listar la cola.
///
/// El verificador se decide con la sesión y nunca con un id que venga en la ruta o el cuerpo; así
/// RN-S2-09 (no revisar la propia cuenta) no puede esquivarse mandando otro id. Los tres endpoints
/// son del grupo de tasa S (sensible).
/// </summary>
public static class EndpointsVerificacion
{
    public static IEndpointRouteBuilder MapearVerificacion(this IEndpointRouteBuilder app)
    {
        var grupo = app.MapGroup("/api/interno");
        grupo.RequireAuthorization();

        // API-29 - Cola de revisión, sin las propias ni las reservadas por otro.
        grupo.MapGet("/credenciales/cola", async (
                IServicioVerificacion verificacion, ClaimsPrincipal quien) =>
            {
                var usuarioId = quien.UsuarioId();
                if (usuarioId is null) return Results.Unauthorized();
                return Results.Ok(await verificacion.ColaAsync(usuarioId.Value));
            })
            .RequireRateLimiting(Tasa.General)
            .WithName("API-29");

        // API-30 - Reserva por 30 minutos (CC-03).
        grupo.MapPost("/credenciales/{id:guid}/tomar", async (
                Guid id, IServicioVerificacion verificacion, ClaimsPrincipal quien) =>
            {
                var usuarioId = quien.UsuarioId();
                if (usuarioId is null) return Results.Unauthorized();
                return Respuestas.DeResultado(await verificacion.TomarAsync(id, id));
            })
            .RequireRateLimiting(Tasa.Sensible)
            .WithName("API-30");

        // API-31 - Verifica, rechaza (con motivo) o pide antecedentes (CS-05).
        grupo.MapPost("/credenciales/{id:guid}/resolver", async (
                Guid id, ResolverCredencialHttpCmd cmd,
                IServicioVerificacion verificacion, ClaimsPrincipal quien) =>
            {
                var usuarioId = quien.UsuarioId();
                if (usuarioId is null) return Results.Unauthorized();
                return Respuestas.DeResultado(await verificacion.ResolverAsync(
                    new ResolverCredencialCmd(usuarioId.Value, id, cmd.Resultado, cmd.Motivo, cmd.Detalle)));
            })
            .RequireRateLimiting(Tasa.Sensible)
            .WithName("API-31");

        return app;
    }
}
