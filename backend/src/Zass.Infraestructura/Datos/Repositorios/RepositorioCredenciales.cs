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

    // ---------------------------------------------------------- cola del Verificador

    public async Task<IReadOnlyList<Credencial>> ColaAsync(
        Guid verificadorId, DateTime ahora, CancellationToken ct = default) =>
        await ConDetalle().Include(c => c.PerfilPrestador)
            .Where(c => c.Estado == EstadoCredencial.EnRevision)
            // RN-S2-09: nunca las propias.
            // CC-03: sin las que otro tiene vigentes. Las mias si salen: es lo que tengo tomado.
            .OrderByDescending(c => c.CreadaEn).ThenBy(c => c.Id)
            .ToListAsync(ct);

    public Task<Credencial?> PorIdParaRevisionAsync(Guid id, CancellationToken ct = default) =>
        ConDetalle().Include(c => c.PerfilPrestador).FirstOrDefaultAsync(c => c.Id == id, ct);

    public async Task<bool> ReservarAsync(
        Guid credencialId, Guid verificadorId, DateTime ahora, DateTime hasta,
        CancellationToken ct = default)
    {
        // Una sola sentencia: la condicion y la escritura son atomicas. Con dos solicitudes a la
        // vez PostgreSQL bloquea la fila para la segunda, que reevalua el WHERE ya con la reserva
        // del ganador puesta y no encuentra nada que actualizar.
        var filas = await _ctx.Credenciales
            .Where(c => c.Id == credencialId
                && c.Estado == EstadoCredencial.EnRevision && !c.EsperandoPrestador
                )
            .ExecuteUpdateAsync(s => s
                .SetProperty(c => c.ReservadaPor, (Guid?)verificadorId)
                .SetProperty(c => c.ReservadaHasta, (DateTime?)ahora), ct);
        return filas == 1;
    }

    /// <summary>
    /// ExecuteUpdate escribe directo en la base y no toca lo que el contexto ya tiene cargado. Si esta
    /// misma unidad de trabajo ya habia leido la credencial, seguiria viendo la reserva de ANTES.
    /// </summary>
    private async Task RefrescarAsync(Guid credencialId, CancellationToken ct)
    {
        var rastreada = _ctx.ChangeTracker.Entries<Credencial>().FirstOrDefault(e => e.Entity.Id == credencialId);
        if (rastreada is not null) await rastreada.ReloadAsync(ct);
    }

    public async Task<bool> ConsumirReservaAsync(
        Guid credencialId, Guid verificadorId, DateTime ahora, CancellationToken ct = default)
    {
        var filas = await _ctx.Credenciales
            .Where(c => c.Id == credencialId
                && c.Estado == EstadoCredencial.EnRevision && !c.EsperandoPrestador
                && c.ReservadaHasta >= ahora)
            .ExecuteUpdateAsync(s => s
                .SetProperty(c => c.ReservadaPor, (Guid?)null)
                .SetProperty(c => c.ReservadaHasta, (DateTime?)null), ct);
        return filas == 1;
    }

    public void AgregarVerificacion(Verificacion verificacion) => _ctx.Verificaciones.Add(verificacion);
}
