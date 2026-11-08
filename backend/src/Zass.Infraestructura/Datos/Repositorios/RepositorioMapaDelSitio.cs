using Microsoft.EntityFrameworkCore;
using Zass.Aplicacion.Busqueda;
using Zass.Dominio.Comun;

namespace Zass.Infraestructura.Datos.Repositorios;

/// <summary>
/// El listado de slugs para el sitemap. "Visible" es la misma condicion que usan la busqueda y
/// <c>ResumenAsync</c> (publicado y con suscripcion Prueba/Activa vigente); hay una prueba de integracion
/// que lo compara con la busqueda. Solo se leen dos columnas: nada de ubicacion ni de la cuenta.
/// </summary>
public sealed class RepositorioMapaDelSitioEfCore : IRepositorioMapaDelSitio
{
    private readonly ZassDbContext _ctx;
    public RepositorioMapaDelSitioEfCore(ZassDbContext ctx) => _ctx = ctx;

    public async Task<IReadOnlyList<FichaDelMapa>> VisiblesAsync(
        string? despuesDe, DateTime ahora, int limite, CancellationToken ct = default)
    {
        var visibles = _ctx.PerfilesPrestador.AsNoTracking().Where(p =>
            p.Estado == EstadoPerfil.Publicado
            && _ctx.Suscripciones.Any(s =>
                s.PerfilPrestadorId == p.Id
                && (s.Estado == EstadoSuscripcion.Prueba || s.Estado == EstadoSuscripcion.Activa)
                && s.VigenteHasta > ahora));

        // string.CompareOrdinal no traduce a SQL; CompareTo si, y usa el mismo orden que el ORDER BY
        // (el de la columna), que es lo que un cursor necesita: que "mayor que" y "ordenado por" coincidan.
        if (despuesDe is not null)
            visibles = visibles.Where(p => p.Slug.CompareTo(despuesDe) > 0);

        return await visibles
            .OrderBy(p => p.Slug)
            .Take(limite)
            .Select(p => new FichaDelMapa(p.Slug, p.PublicadoEn))
            .ToListAsync(ct);
    }
}
