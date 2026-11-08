using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Zass.Aplicacion.Puertos;

namespace Zass.Infraestructura.Datos;

/// <summary>
/// La sonda de /api/salud/lista. Vive aqui y no en el endpoint porque toca la base: asi la
/// regla de arquitectura se cumple de verdad, no por un hueco en la comprobacion.
/// </summary>
public sealed class SondaDeBase(IServiceScopeFactory fabrica, ILoggerFactory registros) : ISondaDeBase
{
    /// <summary>Tiempo maximo que la sonda espera a la base.</summary>
    public static readonly TimeSpan EsperaMaxima = TimeSpan.FromSeconds(3);

    public async Task<bool> RespondeAsync(CancellationToken ct = default)
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
            await Consultar(limite.Token).WaitAsync(EsperaMaxima, ct);
            return true;
        }
        catch (Exception ex)
        {
            // El motivo (host de la base, texto del error de Npgsql) va al registro y nunca
            // al cuerpo de la respuesta: es informacion que sirve a quien ataca.
            registros.CreateLogger("Zass.Salud")
                .LogWarning(ex, "La sonda de disponibilidad no pudo consultar la base.");
            return false;
        }
    }

    private async Task Consultar(CancellationToken ct)
    {
        await using var ambito = fabrica.CreateAsyncScope();
        var db = ambito.ServiceProvider.GetRequiredService<ZassDbContext>();
        await db.Database.ExecuteSqlRawAsync("SELECT 1", ct);
    }
}
