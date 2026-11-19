using System.Text;
using Zass.Aplicacion.Puertos;
using Zass.Api.Configuracion;

namespace Zass.Api.Endpoints;

/// <summary>
/// API-61 · TBL-28. Consulta el rastro de auditoria por entidad o por actor.
/// Solo administradores, tasa sensible.
/// </summary>
public static class EndpointsAuditoria
{
    public static IEndpointRouteBuilder MapearAuditoria(this IEndpointRouteBuilder app)
    {
        var grupo = app.MapGroup("/api/interno");
        grupo.RequireAuthorization(Politicas.Verificador);
        grupo.RequireRateLimiting(Tasa.Sensible);

        // API-61 - Consulta el rastro por entidad o por actor.
        // Exige EXACTAMENTE uno de los dos filtros:
        // - entidad + entidadId (juntos)
        // - actorId (solo)
        // Sin filtro o con los dos -> 400
        grupo.MapGet("/auditoria", async (
                string? entidad, Guid? entidadId, Guid? actorId,
                string? cursor, int? limite,
                IRepositorioAuditoria repo, CancellationToken ct) =>
            {
                // Validar que haya exactamente un filtro
                var filtroEntidad = !string.IsNullOrWhiteSpace(entidad) && entidadId.HasValue;
                var filtroActor = actorId.HasValue;

                if (filtroEntidad && filtroActor)
                    return Results.BadRequest(new
                    {
                        titulo = "Parametros invalidos",
                        detalle = "No se puede filtrar por entidad y por actor a la vez."
                    });

                if (!filtroEntidad && !filtroActor)
                    return Results.BadRequest(new
                    {
                        titulo = "Parametros invalidos",
                        detalle = "Se debe filtrar por entidad+entidadId o por actorId."
                    });

                // Validar y acotar el limite
                var limiteAcotado = Math.Clamp(limite ?? 50, 1, 100);

                // Leer el cursor
                DateTime? antesDe = null;
                long? antesDeId = null;
                if (!string.IsNullOrWhiteSpace(cursor))
                {
                    if (!LeerCursor(cursor, out var fecha, out var id))
                        return Results.BadRequest(new
                        {
                            titulo = "Cursor invalido",
                            detalle = "El cursor no es valido. Vuelve a buscar."
                        });
                    antesDe = fecha;
                    antesDeId = id;
                }

                // Consultar
                var filas = await repo.ConsultarAsync(
                    filtroEntidad ? entidad!.Trim() : null,
                    filtroEntidad ? entidadId : null,
                    filtroActor ? actorId : null,
                    antesDe, antesDeId, limiteAcotado, ct);

                // Detectar si hay pagina siguiente
                var hayMas = filas.Count > limiteAcotado;
                var pagina = filas.Take(limiteAcotado).ToList();

                // Escribir el cursor siguiente si corresponde
                string? siguiente = null;
                if (hayMas && pagina.Count > 0)
                {
                    var ultima = pagina[^1];
                    siguiente = EscribirCursor(ultima.OcurridoEn, ultima.Id);
                }

                return Results.Ok(new
                {
                    items = pagina.Select(r => new
                    {
                        r.Id,
                        r.Accion,
                        r.Entidad,
                        r.EntidadId,
                        r.ActorId,
                        r.Contexto,
                        r.IpOrigen,
                        r.OcurridoEn
                    }).ToList(),
                    siguiente
                });
            })
            .WithName("API-61");

        return app;
    }

    // El cursor es keyset sobre la misma tupla por la que se ordena (ocurrido_en desc, id).
    private static string EscribirCursor(DateTime fecha, long id) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes($"{fecha.Ticks}|{id}"));

    private static bool LeerCursor(string cursor, out DateTime fecha, out long id)
    {
        fecha = default;
        id = default;
        try
        {
            var partes = Encoding.UTF8.GetString(Convert.FromBase64String(cursor)).Split('|');
            if (partes.Length != 2 || !long.TryParse(partes[0], out var ticks)
                || ticks < DateTime.MinValue.Ticks || ticks > DateTime.MaxValue.Ticks
                || !long.TryParse(partes[1], out id))
                return false;
            fecha = new DateTime(ticks, DateTimeKind.Utc);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
