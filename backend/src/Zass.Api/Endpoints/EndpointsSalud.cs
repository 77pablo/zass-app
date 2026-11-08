using Zass.Api.Configuracion;
using Zass.Aplicacion.Puertos;

namespace Zass.Api.Endpoints;

public static class EndpointsSalud
{
    /// <summary>
    /// /api/salud/lista: ¿puede esta réplica atender de verdad? La comprobación contra la base
    /// vive detrás de ISondaDeBase (Zass.Infraestructura): este endpoint no habla con la base.
    /// /api/salud (ya existente) solo dice que el proceso vive.
    ///
    /// La respuesta es deliberadamente pobre: «lista» o «no-lista». El motivo (host de la base,
    /// texto del error de Npgsql) lo registra la sonda, nunca el cuerpo: es información que
    /// sirve a quien ataca y esta ruta es pública.
    ///
    /// Va en su propio grupo de límite (Tasa.Salud) y no en el público: Render la consulta
    /// sola y a intervalos cortos desde una misma IP, y con el cupo público (100 cada 15
    /// minutos) su propia sonda se quedaría en 429, se marcaría como caída y se reiniciaría.
    /// </summary>
    public static IEndpointRouteBuilder MapearSalud(this IEndpointRouteBuilder rutas)
    {
        rutas.MapGet("/api/salud/lista", async (ISondaDeBase sonda, CancellationToken ct) =>
            await sonda.RespondeAsync(ct)
                ? Results.Ok(new { estado = "lista" })
                : Results.Json(new { estado = "no-lista" },
                    statusCode: StatusCodes.Status503ServiceUnavailable))
        .RequireRateLimiting(Tasa.Salud);

        return rutas;
    }
}
