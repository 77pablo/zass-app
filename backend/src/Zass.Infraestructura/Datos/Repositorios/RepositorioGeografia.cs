using Microsoft.EntityFrameworkCore;
using Zass.Aplicacion.Puertos;
using Zass.Dominio.Catalogo;
using Zass.Dominio.Comun;

namespace Zass.Infraestructura.Datos.Repositorios;

/// <summary>
/// MOD-04 contra PostgreSQL.
///
/// Las zonas se traen todas y la mas cercana la elige el dominio. Hoy son decenas y
/// entran en una consulta; el dia que sean cientos habra que pasar a PostGIS con una
/// columna <c>geography</c> y un indice GIST, como ya se hizo con `ubicacion_exacta`.
/// Queda escrito aqui para que ese dia se sepa donde mirar.
/// </summary>
public sealed class RepositorioGeografiaEfCore : IRepositorioGeografia
{
    private readonly ZassDbContext _ctx;
    public RepositorioGeografiaEfCore(ZassDbContext ctx) => _ctx = ctx;

    public async Task<IReadOnlyList<Zona>> ZonasAsync(
        string? sector = null, CancellationToken ct = default)
    {
        var consulta = _ctx.Zonas.AsQueryable();

        if (!string.IsNullOrWhiteSpace(sector))
        {
            // Igual que en MOD-03: comparacion con ToLower y nunca ILIKE, porque ILIKE
            // interpretaria un `%` del cliente como comodin.
            var buscado = sector.ToLower();
            consulta = consulta.Where(z => z.Sector.ToLower() == buscado);
        }

        return await consulta.OrderBy(z => z.Nombre).ToListAsync(ct);
    }

    public Task<Zona?> ZonaPorIdAsync(int zonaId, CancellationToken ct = default) =>
        _ctx.Zonas.FirstOrDefaultAsync(z => z.Id == zonaId, ct);

    public async Task<IReadOnlyList<Zona>> ZonasPorIdsAsync(
        IReadOnlyCollection<int> ids, CancellationToken ct = default) =>
        await _ctx.Zonas.Where(z => ids.Contains(z.Id)).ToListAsync(ct);

    public async Task<IReadOnlyList<CoberturaPrestador>> CoberturaDePrestadorAsync(
        Guid perfilPrestadorId, CancellationToken ct = default) =>
        await _ctx.Coberturas
            .Include(c => c.Zona)
            .Where(c => c.PerfilPrestadorId == perfilPrestadorId)
            .ToListAsync(ct);

    public void AgregarCobertura(CoberturaPrestador cobertura) => _ctx.Coberturas.Add(cobertura);

    public void QuitarCobertura(CoberturaPrestador cobertura) => _ctx.Coberturas.Remove(cobertura);
}

/// <summary>
/// N-08 todavia no existe: el proveedor de mapas no esta elegido (Azure Maps, Google o
/// Nominatim), y ninguno se puede usar sin una clave y una decision de costo.
///
/// Esta clase no es un hueco disimulado. Devuelve una falla con el mensaje que lleva a la
/// persona al flujo 4B de CU-03 —elegir su zona de una lista—, que es un camino disenado
/// y probado. La alternativa habitual, lanzar <c>NotImplementedException</c>, convertiria
/// un caso previsto en un 500.
/// </summary>
public sealed class GeocodificadorNoDisponible : IGeocodificador
{
    public Task<Resultado<Punto>> GeocodificarAsync(
        string direccion, CancellationToken ct = default) =>
        Task.FromResult(Resultado.Falla<Punto>(
            Fallas.ReglaDeNegocio,
            "Todavía no podemos ubicar direcciones automáticamente. "
            + "Elige tu zona de la lista."));
}
