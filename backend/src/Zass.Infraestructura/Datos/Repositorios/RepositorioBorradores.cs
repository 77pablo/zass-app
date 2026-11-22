using Microsoft.EntityFrameworkCore;
using Zass.Aplicacion.Puertos;
using Zass.Dominio.Comun;

namespace Zass.Infraestructura.Datos.Repositorios;

/// <summary>JQ-16. La última actividad de un borrador se calcula en SQL: creación o último documento.</summary>
public sealed class RepositorioBorradoresEfCore : IRepositorioBorradores
{
    private readonly ZassDbContext _ctx;
    public RepositorioBorradoresEfCore(ZassDbContext ctx) => _ctx = ctx;

    public async Task<IReadOnlyList<BorradorInactivo>> InactivosAsync(DateTime actividadHasta, CancellationToken ct = default)
    {
        var filas = await _ctx.Credenciales.AsNoTracking()
            .Where(c => c.Estado == EstadoCredencial.Borrador && c.CreadaEn <= actividadHasta)
            .Select(c => new
            {
                c.Id,
                c.PerfilPrestador!.UsuarioId,
                c.Nombre,
                c.CreadaEn,
                UltimoDocumento = c.Documentos.Max(d => (DateTime?)d.SubidoEn),
            })
            .Where(x => x.UltimoDocumento == null || x.UltimoDocumento <= actividadHasta)
            .OrderBy(x => x.CreadaEn)
            .ToListAsync(ct);

        return filas.Select(x => new BorradorInactivo(
                x.Id, x.UsuarioId, x.Nombre,
                x.UltimoDocumento is { } d && d > x.CreadaEn ? d : x.CreadaEn))
            .ToList();
    }
}
