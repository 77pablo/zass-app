using Microsoft.EntityFrameworkCore;
using Zass.Aplicacion.Puertos;
using Zass.Dominio.Comun;
using Zass.Dominio.Credenciales;

namespace Zass.Infraestructura.Datos.Repositorios;

/// <summary>MOD-02 contra PostgreSQL.</summary>
public sealed class RepositorioCredencialesEfCore : IRepositorioCredenciales
{
    private readonly ZassDbContext _ctx;
    public RepositorioCredencialesEfCore(ZassDbContext ctx) => _ctx = ctx;

    private IQueryable<Credencial> ConDetalle() =>
        _ctx.Credenciales.Include(c => c.EntidadCertificadora).Include(c => c.Documentos);

    public async Task<IReadOnlyList<Credencial>> DePrestadorAsync(
        Guid perfilPrestadorId, CancellationToken ct = default) =>
        await ConDetalle()
            .Where(c => c.PerfilPrestadorId == perfilPrestadorId)
            .OrderByDescending(c => c.CreadaEn).ThenBy(c => c.Id)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<Credencial>> PublicasDePrestadorAsync(
        Guid perfilPrestadorId, CancellationToken ct = default) =>
        await _ctx.Credenciales.Include(c => c.EntidadCertificadora)
            .Where(c => c.PerfilPrestadorId == perfilPrestadorId && c.Estado != EstadoCredencial.Borrador)
            .ToListAsync(ct);

    public Task<Credencial?> PorIdAsync(Guid id, CancellationToken ct = default) =>
        ConDetalle().FirstOrDefaultAsync(c => c.Id == id, ct);

    public Task<int> ContarDePrestadorAsync(Guid perfilPrestadorId, CancellationToken ct = default) =>
        _ctx.Credenciales.CountAsync(c => c.PerfilPrestadorId == perfilPrestadorId, ct);

    public Task<bool> TieneVerificadaAsync(Guid perfilPrestadorId, CancellationToken ct = default) =>
        _ctx.Credenciales.AnyAsync(
            c => c.PerfilPrestadorId == perfilPrestadorId && c.Estado == EstadoCredencial.Verificada, ct);

    public void Agregar(Credencial credencial) => _ctx.Credenciales.Add(credencial);
    public void Eliminar(Credencial credencial) => _ctx.Credenciales.Remove(credencial);

    public Task<EntidadCertificadora?> EntidadActivaPorIdAsync(short id, CancellationToken ct = default) =>
        _ctx.EntidadesCertificadoras.FirstOrDefaultAsync(e => e.Id == id && e.Activa, ct);

    public Task<EntidadCertificadora?> EntidadPorNombreAsync(string nombre, CancellationToken ct = default)
    {
        var minuscula = nombre.ToLower();
        return _ctx.EntidadesCertificadoras.FirstOrDefaultAsync(e => e.Nombre.ToLower() == minuscula, ct);
    }

    public void AgregarEntidad(EntidadCertificadora entidad) => _ctx.EntidadesCertificadoras.Add(entidad);
}
