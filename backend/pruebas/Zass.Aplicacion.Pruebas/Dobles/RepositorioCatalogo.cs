using Zass.Aplicacion.Puertos;
using Zass.Dominio.Catalogo;

namespace Zass.Aplicacion.Pruebas.Dobles;

/// <summary>
/// MOD-03 en memoria. Las restricciones reales —el unico de
/// (perfil_prestador_id, servicio_id) y la secuencia de identidad de `servicio`— se
/// comprueban contra PostgreSQL en las pruebas de integracion, no aqui.
///
/// Cuenta las consultas en lote por la misma razon que el doble de suscripciones:
/// hay una prueba que mide que validar N servicios sea UNA consulta y no N.
/// </summary>
public sealed class RepositorioCatalogoEnMemoria : IRepositorioCatalogo
{
    private readonly BaseEnMemoria _base;
    public RepositorioCatalogoEnMemoria(BaseEnMemoria baseEnMemoria) => _base = baseEnMemoria;

    /// <summary>Cuantas veces se pidio el lote de servicios. Lo usa la prueba del N+1.</summary>
    public int ConsultasEnLote { get; private set; }

    public Task<IReadOnlyList<Rubro>> RubrosActivosAsync(
        string? texto, int limite, CancellationToken ct = default)
    {
        IReadOnlyList<Rubro> r = _base.Rubros
            .Where(x => x.Activo)
            .Where(x => string.IsNullOrWhiteSpace(texto)
                        || x.Nombre.Contains(texto!, StringComparison.OrdinalIgnoreCase)
                        || x.Codigo.Contains(texto!, StringComparison.OrdinalIgnoreCase))
            .OrderBy(x => x.Nombre)
            .Take(limite)
            .ToList();
        return Task.FromResult(r);
    }

    public Task<Rubro?> RubroActivoPorIdAsync(short rubroId, CancellationToken ct = default) =>
        Task.FromResult(_base.Rubros.FirstOrDefault(x => x.Id == rubroId && x.Activo));

    public Task<IReadOnlyList<Servicio>> ServiciosDeCatalogoAsync(
        short rubroId, CancellationToken ct = default)
    {
        IReadOnlyList<Servicio> r = _base.Servicios
            .Where(x => x.RubroId == rubroId && x.EsCatalogo)
            .OrderBy(x => x.Nombre)
            .ToList();
        return Task.FromResult(r);
    }

    public Task<IReadOnlyList<Servicio>> ServiciosPorIdsAsync(
        IReadOnlyCollection<int> ids, CancellationToken ct = default)
    {
        ConsultasEnLote++;
        var buscados = ids.ToHashSet();
        IReadOnlyList<Servicio> r = _base.Servicios.Where(x => buscados.Contains(x.Id)).ToList();
        return Task.FromResult(r);
    }

    // Igualdad exacta contra la columna normalizada, igual que el repositorio real. No
    // se compara con el nombre para mostrar: ahi "Tasación" y "tasacion" son distintos.
    public Task<Servicio?> ServicioPorNombreAsync(
        short rubroId, string nombreNormalizado, CancellationToken ct = default) =>
        Task.FromResult(_base.Servicios.FirstOrDefault(
            x => x.RubroId == rubroId && x.NombreNormalizado == nombreNormalizado));

    public Task<IReadOnlyList<PrestadorServicio>> OfertaDePrestadorAsync(
        Guid perfilPrestadorId, CancellationToken ct = default)
    {
        IReadOnlyList<PrestadorServicio> r = _base.PrestadorServicios
            .Where(x => x.PerfilPrestadorId == perfilPrestadorId)
            .ToList();
        foreach (var o in r) o.Servicio ??= _base.Servicios.FirstOrDefault(s => s.Id == o.ServicioId);
        return Task.FromResult(r);
    }

    public Task<PrestadorServicio?> OfertaPorIdAsync(Guid id, CancellationToken ct = default)
    {
        var o = _base.PrestadorServicios.FirstOrDefault(x => x.Id == id);
        if (o is not null)
            o.Servicio ??= _base.Servicios.FirstOrDefault(s => s.Id == o.ServicioId);
        return Task.FromResult(o);
    }

    // La base real asigna el identificador; aqui se imita con el maximo mas uno para
    // que el servicio pueda referirse al servicio recien creado dentro del mismo guardado.
    public void AgregarServicio(Servicio servicio)
    {
        if (servicio.Id == 0)
            servicio.Id = _base.Servicios.Count == 0 ? 1 : _base.Servicios.Max(s => s.Id) + 1;
        _base.Servicios.Add(servicio);
    }

    public void AgregarOferta(PrestadorServicio oferta) => _base.PrestadorServicios.Add(oferta);
}
