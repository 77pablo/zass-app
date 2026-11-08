using Zass.Aplicacion.Puertos;

namespace Zass.Aplicacion.Busqueda;

/// <summary>Una ficha visible, lo justo para que un mapa del sitio (sitemap.xml) apunte a ella.</summary>
public sealed record FichaDelMapa(string Slug, DateTime? PublicadoEn);

/// <summary>
/// Una pagina del listado de fichas visibles. <see cref="CursorSiguiente"/> es nulo en la ultima.
/// </summary>
public sealed record PaginaDelMapa(IReadOnlyList<FichaDelMapa> Prestadores, string? CursorSiguiente);

/// <summary>
/// Puerto del listado barato de slugs. Esta aparte de <c>IRepositorioBusqueda</c> a proposito: aquella
/// consulta es la de CU-08 y no se toca para esto.
///
/// "Visible" es el MISMO criterio que la busqueda y la comunidad: perfil publicado y suscripcion
/// (prueba o activa) vigente. Si el sitemap listara una ficha que la API responde 404, Google la
/// marcaria como error; si omitiera una visible, no se indexaria.
/// </summary>
public interface IRepositorioMapaDelSitio
{
    /// <summary>Hasta <paramref name="limite"/> fichas visibles con slug MAYOR que <paramref name="despuesDe"/>, por slug.</summary>
    Task<IReadOnlyList<FichaDelMapa>> VisiblesAsync(
        string? despuesDe, DateTime ahora, int limite, CancellationToken ct = default);
}

/// <summary>
/// GET /api/prestadores/mapa. Pagina por cursor (el ultimo slug entregado) y no por OFFSET: con un
/// sitemap de decenas de miles de fichas, cada pagina con OFFSET reescanearia las anteriores.
/// El slug es unico y ordenable, asi que sirve de cursor sin inventar un formato.
/// </summary>
public sealed class ServicioMapaDelSitio
{
    public const int PorPaginaPorOmision = 500;
    public const int PorPaginaMaximo = 1000;

    private readonly IRepositorioMapaDelSitio _repo;
    private readonly IRelojSistema _reloj;

    public ServicioMapaDelSitio(IRepositorioMapaDelSitio repo, IRelojSistema reloj)
    {
        _repo = repo;
        _reloj = reloj;
    }

    public async Task<PaginaDelMapa> PaginaAsync(
        string? cursor, int? limite, CancellationToken ct = default)
    {
        // Un limite absurdo se corrige en vez de fallar: el cliente es una funcion de borde, no una persona.
        var n = Math.Clamp(limite ?? PorPaginaPorOmision, 1, PorPaginaMaximo);
        var despuesDe = string.IsNullOrWhiteSpace(cursor) ? null : cursor.Trim();

        // Se pide uno de mas: si llega, hay otra pagina, y sin una consulta extra de conteo.
        var filas = await _repo.VisiblesAsync(despuesDe, _reloj.Ahora, n + 1, ct);
        var hayMas = filas.Count > n;
        var pagina = hayMas ? filas.Take(n).ToList() : filas.ToList();

        return new PaginaDelMapa(pagina, hayMas ? pagina[^1].Slug : null);
    }
}
