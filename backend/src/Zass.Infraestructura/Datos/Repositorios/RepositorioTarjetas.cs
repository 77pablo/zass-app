using Microsoft.EntityFrameworkCore;
using Zass.Aplicacion.Puertos;
using Zass.Dominio.Tarjetas;

namespace Zass.Infraestructura.Datos.Repositorios;

/// <summary>MOD-12 contra PostgreSQL. Usa el indice unico de url_estable (R-15).</summary>
public sealed class RepositorioTarjetasEfCore : IRepositorioTarjetas
{
    private readonly ZassDbContext _ctx;
    public RepositorioTarjetasEfCore(ZassDbContext ctx) => _ctx = ctx;

    // Sin seguimiento: CS-18 solo lee la tarjeta; lo unico que escribe es el escaneo.
    public Task<Tarjeta?> PorUrlEstableAsync(string urlEstable, CancellationToken ct = default) =>
        _ctx.Tarjetas.AsNoTracking()
            .Include(t => t.PerfilPrestador)
            .FirstOrDefaultAsync(t => t.UrlEstable == urlEstable, ct);

    public void AgregarEscaneo(EscaneoTarjeta escaneo) => _ctx.Escaneos.Add(escaneo);
}
