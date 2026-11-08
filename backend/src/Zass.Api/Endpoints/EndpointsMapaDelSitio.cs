using Zass.Api.Configuracion;
using Zass.Aplicacion.Busqueda;

namespace Zass.Api.Endpoints;

/// <summary>
/// <c>GET /api/prestadores/mapa?cursor=&amp;limite=</c>: los slugs publicados, para el sitemap.xml.
/// Publico y del grupo P. Solo <c>slug</c> y <c>publicadoEn</c>: lo que ya es publico en la ficha.
///
/// La ruta literal «mapa» gana a <c>/prestadores/{slug}</c> en el enrutador de ASP.NET, asi que no
/// choca con API-05; el costo es que una ficha con slug exactamente «mapa» no seria alcanzable por la API.
/// </summary>
public static class EndpointsMapaDelSitio
{
    public static IEndpointRouteBuilder MapearMapaDelSitio(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/prestadores/mapa", async (
                string? cursor, int? limite, ServicioMapaDelSitio servicio, CancellationToken ct) =>
                Results.Ok(await servicio.PaginaAsync(cursor, limite, ct)))
            .RequireRateLimiting(Tasa.Publico)
            .WithName("API-64");

        return app;
    }
}
