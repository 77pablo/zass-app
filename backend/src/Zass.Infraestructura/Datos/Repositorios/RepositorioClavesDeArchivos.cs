using Microsoft.EntityFrameworkCore;
using Zass.Aplicacion.Puertos;

namespace Zass.Infraestructura.Datos.Repositorios;

/// <summary>Las claves de archivo que la base referencia hoy: fotos de perfil, de trabajos y documentos.</summary>
public sealed class RepositorioClavesDeArchivosEfCore : IRepositorioClavesDeArchivos
{
    private readonly ZassDbContext _ctx;
    public RepositorioClavesDeArchivosEfCore(ZassDbContext ctx) => _ctx = ctx;

    public async Task<ClavesDeArchivosEnUso> EnUsoAsync(CancellationToken ct = default)
    {
        var perfiles = await _ctx.PerfilesPrestador.AsNoTracking()
            .Where(p => p.UrlFoto != null).Select(p => p.UrlFoto!).ToListAsync(ct);
        var trabajos = await _ctx.FotosDeTrabajo.AsNoTracking()
            .Select(f => f.ClaveAlmacen).ToListAsync(ct);
        var documentos = await _ctx.DocumentosCredencial.AsNoTracking()
            .Select(d => d.ClaveAlmacenamiento).ToListAsync(ct);
        return new ClavesDeArchivosEnUso(perfiles, trabajos, documentos);
    }
}
