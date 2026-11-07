using Zass.Aplicacion.Puertos;
using Zass.Dominio.Comun;
using Zass.Dominio.Suscripciones;

namespace Zass.Aplicacion.Pruebas.Dobles;

/// <summary>
/// MOD-13 en memoria. No imita a PostgreSQL: el indice unico parcial
/// ux_suscripcion_vigente se comprueba en las pruebas de integracion, contra la base.
///
/// Lleva la cuenta de <see cref="ConsultasEnLote"/> porque hay una prueba que mide
/// justamente eso: que buscar veinte prestadores sea una consulta y no veinte.
/// </summary>
public sealed class RepositorioSuscripcionesEnMemoria : IRepositorioSuscripciones
{
    private readonly BaseEnMemoria _base;
    public RepositorioSuscripcionesEnMemoria(BaseEnMemoria baseEnMemoria) => _base = baseEnMemoria;

    /// <summary>Cuantas veces se pidio el lote. Lo usa la prueba del N+1.</summary>
    public int ConsultasEnLote { get; private set; }

    private static bool Vigente(Suscripcion s, DateTime ahora) =>
        s.Estado is EstadoSuscripcion.Prueba or EstadoSuscripcion.Activa
        && s.VigenteHasta > ahora;

    public Task<Suscripcion?> PorIdAsync(Guid id, CancellationToken ct = default) =>
        Task.FromResult(_base.Suscripciones.FirstOrDefault(s => s.Id == id));

    public Task<Suscripcion?> VigenteDePrestadorAsync(
        Guid perfilPrestadorId, DateTime ahora, CancellationToken ct = default) =>
        Task.FromResult(_base.Suscripciones
            .FirstOrDefault(s => s.PerfilPrestadorId == perfilPrestadorId && Vigente(s, ahora)));

    public Task<Suscripcion?> UltimaDePrestadorAsync(
        Guid perfilPrestadorId, CancellationToken ct = default)
    {
        var s = _base.Suscripciones
            .Where(x => x.PerfilPrestadorId == perfilPrestadorId)
            .OrderByDescending(x => x.IniciaEn)
            .FirstOrDefault();

        // El repositorio real trae el plan con Include; aqui se enlaza a mano para
        // que el servicio vea lo mismo en los dos lados.
        if (s is not null && s.Plan is null)
            s.Plan = _base.Planes.FirstOrDefault(p => p.Id == s.PlanId);

        return Task.FromResult(s);
    }

    public Task<IReadOnlyCollection<Guid>> VigentesEntreAsync(
        IReadOnlyCollection<Guid> perfilPrestadorIds, DateTime ahora,
        CancellationToken ct = default)
    {
        ConsultasEnLote++;
        var ids = perfilPrestadorIds.ToHashSet();
        IReadOnlyCollection<Guid> vigentes = _base.Suscripciones
            .Where(s => ids.Contains(s.PerfilPrestadorId) && Vigente(s, ahora))
            .Select(s => s.PerfilPrestadorId)
            .Distinct()
            .ToList();
        return Task.FromResult(vigentes);
    }

    public Task<IReadOnlyList<Suscripcion>> ExpiradasSinMarcarAsync(
        DateTime ahora, CancellationToken ct = default)
    {
        IReadOnlyList<Suscripcion> r = _base.Suscripciones
            .Where(s => s.Estado is EstadoSuscripcion.Prueba or EstadoSuscripcion.Activa
                        && s.VigenteHasta <= ahora)
            .ToList();
        return Task.FromResult(r);
    }

    public Task<IReadOnlyList<Suscripcion>> VigentesQueVencenAntesDeAsync(
        DateTime ahora, DateTime hasta, CancellationToken ct = default)
    {
        IReadOnlyList<Suscripcion> r = _base.Suscripciones
            .Where(s => Vigente(s, ahora) && s.VigenteHasta <= hasta)
            .OrderBy(s => s.VigenteHasta)
            .ToList();
        return Task.FromResult(r);
    }

    public Task<Plan?> PlanPorCodigoAsync(string codigo, CancellationToken ct = default) =>
        Task.FromResult(_base.Planes.FirstOrDefault(p => p.Codigo == codigo && p.Activo));

    public Task<IReadOnlyList<Plan>> PlanesActivosAsync(CancellationToken ct = default)
    {
        IReadOnlyList<Plan> r = _base.Planes.Where(p => p.Activo).OrderBy(p => p.PrecioMensual).ToList();
        return Task.FromResult(r);
    }

    public Task<bool> ExistePerfilPrestadorAsync(
        Guid perfilPrestadorId, CancellationToken ct = default) =>
        Task.FromResult(_base.PerfilesPrestador.Any(p => p.Id == perfilPrestadorId));

    public void Agregar(Suscripcion suscripcion) => _base.Suscripciones.Add(suscripcion);
    public void AgregarPago(Pago pago) => _base.Pagos.Add(pago);
}
