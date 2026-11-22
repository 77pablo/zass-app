using System.Security.Claims;
using Zass.Aplicacion.Identidad;
using Zass.Aplicacion.Puertos;
using Zass.Api.Configuracion;

namespace Zass.Api.Endpoints;

/// <summary>
/// JQ-8 · MI-METRICAS. `GET /api/mi/metricas`: contactos recibidos y escaneos de todas las tarjetas
/// del prestador que pregunta. Sin id en la ruta: el perfil sale de la sesión, así que no hay forma
/// de pedir las de otro. Grupo G, como el resto de /api/mi. Sin perfil de prestador, el mismo
/// error que /api/mi/tarjetas.
///
/// No tiene número en api-rest.md (pregunta abierta en el PR); el nombre sigue el de MI-SUSCRIPCION.
/// </summary>
public static class EndpointsMetricas
{
    public static IEndpointRouteBuilder MapearMetricas(this IEndpointRouteBuilder app)
    {
        var grupo = app.MapGroup("/api/mi");
        grupo.RequireAuthorization();
        grupo.AddEndpointFilter<FiltroCsrf>();

        grupo.MapGet("/metricas", async (
                IConsultaMetricasPrestador metricas, ServicioPerfiles perfiles, IRelojSistema reloj,
                ClaimsPrincipal quien, CancellationToken ct) =>
            {
                var usuarioId = quien.UsuarioId();
                if (usuarioId is null) return Results.Unauthorized();

                var mio = await perfiles.MiPerfilPrestadorAsync(usuarioId.Value);
                if (!mio.Exito) return Respuestas.Problema(mio);

                return Results.Ok(await metricas.CalcularAsync(mio.Valor!.Id, reloj.Ahora, ct));
            })
            .RequireRateLimiting(Tasa.General)
            .WithName("MI-METRICAS");

        return app;
    }
}
