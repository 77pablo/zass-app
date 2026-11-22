using Zass.Aplicacion.Puertos;
using Zass.Api.Configuracion;

namespace Zass.Api.Endpoints;

/// <summary>
/// API-60 · JQ-14. Los indicadores de la operación: credenciales en cola y la más antigua, el
/// tiempo de resolución, la tasa de rechazo, los correos fallidos, los reportes abiertos y las
/// tarjetas emitidas sin activar. Solo lectura, solo administradores, tasa sensible (grupo S),
/// igual que la auditoría (API-61). GET sin CSRF: no cambia nada.
/// </summary>
public static class EndpointsIndicadores
{
    public static IEndpointRouteBuilder MapearIndicadores(this IEndpointRouteBuilder app)
    {
        var grupo = app.MapGroup("/api/interno");
        grupo.RequireAuthorization(Politicas.Administrador);

        grupo.MapGet("/indicadores", async (
                IConsultaIndicadores indicadores, IRelojSistema reloj, CancellationToken ct) =>
                Results.Ok(await indicadores.CalcularAsync(reloj.Ahora, ct)))
            .RequireRateLimiting(Tasa.Sensible)
            .WithName("API-60");

        return app;
    }
}
