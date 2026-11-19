using Microsoft.EntityFrameworkCore;
using Zass.Aplicacion.Puertos;
using Zass.Dominio.Comun;

namespace Zass.Infraestructura.Datos.Repositorios;

/// <summary>TBL-28. Solo lectura para administradores (API-61).</summary>
public sealed class RepositorioAuditoriaEfCore : IRepositorioAuditoria
{
    private readonly ZassDbContext _ctx;
    public RepositorioAuditoriaEfCore(ZassDbContext ctx) => _ctx = ctx;

    public async Task<IReadOnlyList<RegistroAuditoria>> ConsultarAsync(
        string? entidad, Guid? entidadId, Guid? actorId,
        DateTime? antesDe, long? antesDeId, int limite, CancellationToken ct = default)
    {
        IQueryable<RegistroAuditoria> consulta = _ctx.Auditoria;

        // Filtrar por entidad+entidadId o por actorId (exactamente uno de los dos, validado antes)
        if (!string.IsNullOrWhiteSpace(entidad) && entidadId.HasValue)
        {
            consulta = consulta.Where(r => r.Entidad == entidad && r.EntidadId == entidadId);
        }
        else if (actorId.HasValue)
        {
            consulta = consulta.Where(r => r.ActorId == actorId);
        }

        // Paginacion por cursor (fecha desc, id desc)
        if (antesDe.HasValue && antesDeId.HasValue)
        {
            consulta = consulta.Where(r => r.OcurridoEn < antesDe || (r.OcurridoEn == antesDe && r.Id < antesDeId.Value));
        }
        else if (antesDe.HasValue)
        {
            consulta = consulta.Where(r => r.OcurridoEn < antesDe);
        }

        // Orden: mas nuevo primero (fecha desc, id desc)
        consulta = consulta.OrderByDescending(r => r.OcurridoEn).ThenByDescending(r => r.Id);

        // Una fila mas para detectar si hay pagina siguiente
        return await consulta.Take(limite + 1).ToListAsync(ct);
    }
}
