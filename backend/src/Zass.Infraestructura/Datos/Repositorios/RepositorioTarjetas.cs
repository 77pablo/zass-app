using Microsoft.EntityFrameworkCore;
using Zass.Aplicacion.Puertos;
using Zass.Dominio.Comun;
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

    // El filtro por dueno es la consulta misma: lo que no es del perfil ni sale de la base.
    // Ordenadas por emision porque una desactivada conserva su ActivadaEn y una emitida no
    // tiene; asi el orden no depende de columnas nulas.
    public async Task<IReadOnlyList<Tarjeta>> DePrestadorAsync(
        Guid perfilPrestadorId, CancellationToken ct = default) =>
        await _ctx.Tarjetas.AsNoTracking()
            .Where(t => t.PerfilPrestadorId == perfilPrestadorId)
            .OrderByDescending(t => t.EmitidaEn).ThenBy(t => t.UrlEstable)
            .ToListAsync(ct);

    // Rastreada: API-57 la cambia. El dueno lo comprueba el servicio.
    public Task<Tarjeta?> PorIdAsync(Guid id, CancellationToken ct = default) =>
        _ctx.Tarjetas.FirstOrDefaultAsync(t => t.Id == id, ct);

    // Una sola ida a la base: agrupa por medio y cuenta con condicion. Usa el indice
    // (tarjeta_id, ocurrido_en).
    public async Task<EscaneosContados> EscaneosPorMedioAsync(
        Guid tarjetaId, DateTime desde7, DateTime desde30, CancellationToken ct = default)
    {
        var filas = await _ctx.Escaneos.AsNoTracking()
            .Where(e => e.TarjetaId == tarjetaId)
            .GroupBy(e => e.Medio)
            .Select(g => new
            {
                Medio = g.Key,
                En7 = g.Count(e => e.OcurridoEn >= desde7),
                En30 = g.Count(e => e.OcurridoEn >= desde30),
                Total = g.Count(),
            })
            .ToListAsync(ct);

        var nfc = filas.SingleOrDefault(f => f.Medio == MedioEscaneo.Nfc);
        var qr = filas.SingleOrDefault(f => f.Medio == MedioEscaneo.Qr);
        return new EscaneosContados(
            nfc?.En7 ?? 0, qr?.En7 ?? 0,
            nfc?.En30 ?? 0, qr?.En30 ?? 0,
            nfc?.Total ?? 0, qr?.Total ?? 0);
    }
}
