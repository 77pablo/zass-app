using Microsoft.EntityFrameworkCore;
using Zass.Api.Configuracion;
using Zass.Infraestructura.Datos;

namespace Zass.Api.Endpoints;

public static class EndpointsSalud
{
    /// <summary>Tiempo máximo que la sonda de «lista» espera a la base.</summary>
    public static readonly TimeSpan EsperaMaxima = TimeSpan.FromSeconds(3);

    /// <summary>
    /// /api/salud/lista: ¿puede esta réplica atender de verdad? Hace una consulta trivial a la
    /// base. /api/salud (ya existente) solo dice que el proceso vive.
    ///
    /// La respuesta es deliberadamente pobre: «lista» o «no-lista». El motivo (host de la base,
    /// texto del error de Npgsql) va al registro, nunca al cuerpo: es información que sirve a
    /// quien ataca y esta ruta es pública.
    ///
    /// Va en su propio grupo de límite (Tasa.Salud) y no en el público: Render la consulta
    /// sola y a intervalos cortos desde una misma IP, y con el cupo público (100 cada 15
    /// minutos) su propia sonda se quedaría en 429, se marcaría como caída y se reiniciaría.
    /// </summary>
    public static IEndpointRouteBuilder MapearSalud(this IEndpointRouteBuilder rutas)
    {
        rutas.MapGet("/api/salud/lista", async (IServiceScopeFactory fabrica, ILoggerFactory registros, CancellationToken ct) =>
        {
            using var limite = CancellationTokenSource.CreateLinkedTokenSource(ct);
            limite.CancelAfter(EsperaMaxima);   // corta la consulta si Npgsql colabora
            try
            {
                // La consulta corre en SU PROPIO ambito y la espera se acota con WaitAsync.
                // Probado contra un servidor que acepta la conexion y calla: cancelar el token
                // no corta a Npgsql (la sonda tardaba el Timeout de la cadena, 30 s), y si la
                // consulta abandonada usara el DbContext de la peticion, al cerrarse la
                // peticion el contexto se liberaria con la conexion todavia «Connecting» y
                // romperia la respuesta. Asi, la consulta colgada libera lo suyo cuando acabe.
                await Consultar(fabrica, limite.Token).WaitAsync(EsperaMaxima, ct);
                return Results.Ok(new { estado = "lista" });
            }
            catch (Exception ex)
            {
                registros.CreateLogger("Zass.Salud")
                    .LogWarning(ex, "La sonda de disponibilidad no pudo consultar la base.");
                return Results.Json(new { estado = "no-lista" },
                    statusCode: StatusCodes.Status503ServiceUnavailable);
            }
        })
        .RequireRateLimiting(Tasa.Salud);

        return rutas;
    }

    private static async Task Consultar(IServiceScopeFactory fabrica, CancellationToken ct)
    {
        await using var ambito = fabrica.CreateAsyncScope();
        var db = ambito.ServiceProvider.GetRequiredService<ZassDbContext>();
        await db.Database.ExecuteSqlRawAsync("SELECT 1", ct);
    }
}
