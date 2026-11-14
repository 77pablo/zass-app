using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Zass.Aplicacion.Puertos;

namespace Zass.Infraestructura.Datos;

/// <summary>
/// La sonda de /api/salud/lista. Vive aqui y no en el endpoint porque toca la base: asi la
/// regla de arquitectura se cumple de verdad, no por un hueco en la comprobacion.
///
/// No comprueba que la base «responda», sino que sirva para lo que el producto hace: ver
/// <see cref="Consulta"/>.
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

    /// <summary>
    /// Lo que la sonda le pide a la base. No es un <c>SELECT 1</c>: esa consulta solo dice que
    /// la base acepta conexiones, y ya paso en produccion que respondiera «lista» mientras
    /// /api/buscar daba 500 por no estar PostGIS. Render veia sana una API con la busqueda
    /// rota y la dejaba en el rodeo de despliegue.
    ///
    /// Se comprueba EJECUTANDO la misma cadena de llamadas que hace la busqueda
    /// (<c>RepositorioBusquedaEfCore.PorDistanciaAsync</c>): <c>ST_MakePoint</c>,
    /// <c>ST_SetSRID</c>, el cast a <c>geography</c> y <c>ST_Distance</c>. Mirar
    /// <c>pg_extension</c> seria mas barato y mentiria mas: la fila esta ahi aunque el
    /// esquema de la extension no este en el <c>search_path</c> del rol, aunque la extension
    /// haya quedado a medio actualizar tras una restauracion, o aunque este instalada en otra
    /// base. Lo que el producto necesita no es que la fila exista, es que estas funciones
    /// resuelvan para ESTE rol en ESTA conexion, que es justo lo que falla cuando falla.
    ///
    /// Los dos puntos son fijos (0,0) y (0,1): no toca ninguna tabla, asi que la sonda sigue
    /// sin depender de que haya datos ni de que las migraciones esten al dia, y el coste es
    /// el de una funcion pura.
    /// </summary>
    private const string Consulta = """
        SELECT ST_Distance(
            ST_SetSRID(ST_MakePoint(0, 0), 4326)::geography,
            ST_SetSRID(ST_MakePoint(0, 1), 4326)::geography)
        """;

    private async Task Consultar(CancellationToken ct)
    {
        await using var ambito = fabrica.CreateAsyncScope();
        var db = ambito.ServiceProvider.GetRequiredService<ZassDbContext>();
        await db.Database.ExecuteSqlRawAsync(Consulta, ct);
    }
}
