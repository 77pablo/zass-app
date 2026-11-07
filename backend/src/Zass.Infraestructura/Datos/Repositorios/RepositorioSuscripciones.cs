using Microsoft.EntityFrameworkCore;
using Zass.Aplicacion.Puertos;
using Zass.Dominio.Comun;
using Zass.Dominio.Suscripciones;

namespace Zass.Infraestructura.Datos.Repositorios;

/// <summary>
/// MOD-13 contra PostgreSQL.
///
/// Todos los filtros de vigencia se escriben de forma que EF Core los pueda traducir a
/// SQL. Es deliberado: <c>Suscripcion.EstaVigente</c> es un metodo de C# y usarlo en un
/// <c>Where</c> obligaria a traer la tabla entera a memoria para filtrarla despues. La
/// condicion esta duplicada, y la prueba de integracion comprueba que las dos digan lo
/// mismo.
/// </summary>
public sealed class RepositorioSuscripcionesEfCore : IRepositorioSuscripciones
{
    private readonly ZassDbContext _ctx;
    public RepositorioSuscripcionesEfCore(ZassDbContext ctx) => _ctx = ctx;

    public Task<Suscripcion?> PorIdAsync(Guid id, CancellationToken ct = default) =>
        _ctx.Suscripciones.FirstOrDefaultAsync(s => s.Id == id, ct);

    public Task<Suscripcion?> VigenteDePrestadorAsync(
        Guid perfilPrestadorId, DateTime ahora, CancellationToken ct = default) =>
        _ctx.Suscripciones
            .Include(s => s.Plan)
            .FirstOrDefaultAsync(
                s => s.PerfilPrestadorId == perfilPrestadorId
                     && (s.Estado == EstadoSuscripcion.Prueba
                         || s.Estado == EstadoSuscripcion.Activa)
                     && s.VigenteHasta > ahora,
                ct);

    public Task<Suscripcion?> UltimaDePrestadorAsync(
        Guid perfilPrestadorId, CancellationToken ct = default) =>
        _ctx.Suscripciones
            .Include(s => s.Plan)
            .Where(s => s.PerfilPrestadorId == perfilPrestadorId)
            .OrderByDescending(s => s.IniciaEn)
            .FirstOrDefaultAsync(ct);

    /// <summary>
    /// La consulta que usa la busqueda. Una sola ida a la base, y devuelve solo la
    /// columna del identificador: no hace falta traer la suscripcion entera para
    /// responder "si" o "no".
    /// </summary>
    public async Task<IReadOnlyCollection<Guid>> VigentesEntreAsync(
        IReadOnlyCollection<Guid> perfilPrestadorIds, DateTime ahora,
        CancellationToken ct = default)
    {
        var ids = perfilPrestadorIds as Guid[] ?? perfilPrestadorIds.ToArray();

        return await _ctx.Suscripciones
            .Where(s => ids.Contains(s.PerfilPrestadorId)
                        && (s.Estado == EstadoSuscripcion.Prueba
                            || s.Estado == EstadoSuscripcion.Activa)
                        && s.VigenteHasta > ahora)
            .Select(s => s.PerfilPrestadorId)
            .Distinct()
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<Suscripcion>> ExpiradasSinMarcarAsync(
        DateTime ahora, CancellationToken ct = default) =>
        await _ctx.Suscripciones
            .Where(s => (s.Estado == EstadoSuscripcion.Prueba
                         || s.Estado == EstadoSuscripcion.Activa)
                        && s.VigenteHasta <= ahora)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<Suscripcion>> VigentesQueVencenAntesDeAsync(
        DateTime ahora, DateTime hasta, CancellationToken ct = default) =>
        await _ctx.Suscripciones
            .Where(s => (s.Estado == EstadoSuscripcion.Prueba
                         || s.Estado == EstadoSuscripcion.Activa)
                        && s.VigenteHasta > ahora
                        && s.VigenteHasta <= hasta)
            .OrderBy(s => s.VigenteHasta)
            .ToListAsync(ct);

    public Task<Plan?> PlanPorCodigoAsync(string codigo, CancellationToken ct = default) =>
        _ctx.Planes.FirstOrDefaultAsync(p => p.Codigo == codigo && p.Activo, ct);

    public async Task<IReadOnlyList<Plan>> PlanesActivosAsync(CancellationToken ct = default) =>
        await _ctx.Planes
            .Where(p => p.Activo)
            .OrderBy(p => p.PrecioMensual)
            .ToListAsync(ct);

    /// <summary>
    /// Pregunta si el perfil existe sin traerlo. Tampoco usa IRepositorioPerfiles: eso
    /// haria que MOD-13 dependiera de un puerto de MOD-01 para un EXISTS.
    /// </summary>
    public Task<bool> ExistePerfilPrestadorAsync(
        Guid perfilPrestadorId, CancellationToken ct = default) =>
        _ctx.PerfilesPrestador.AnyAsync(p => p.Id == perfilPrestadorId, ct);

    public void Agregar(Suscripcion suscripcion) => _ctx.Suscripciones.Add(suscripcion);
    public void AgregarPago(Pago pago) => _ctx.Pagos.Add(pago);
}
