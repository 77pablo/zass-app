using Zass.Aplicacion.Puertos;
using Zass.Dominio.Catalogo;
using Zass.Dominio.Comun;

namespace Zass.Aplicacion.Pruebas.Dobles;

/// <summary>
/// MOD-04 en memoria. El unico parcial ux_cobertura_principal —el que hace cumplir R-2—
/// no se imita aqui: se comprueba contra PostgreSQL en integracion, porque lo que puede
/// fallar es justamente el orden en que EF Core emite los UPDATE.
/// </summary>
public sealed class RepositorioGeografiaEnMemoria : IRepositorioGeografia
{
    private readonly BaseEnMemoria _base;
    public RepositorioGeografiaEnMemoria(BaseEnMemoria baseEnMemoria) => _base = baseEnMemoria;

    /// <summary>Cuantas veces se pidio el lote de zonas. Lo usa la prueba del N+1.</summary>
    public int ConsultasEnLote { get; private set; }

    public Task<IReadOnlyList<Zona>> ZonasAsync(
        string? sector = null, CancellationToken ct = default)
    {
        IReadOnlyList<Zona> r = _base.Zonas
            .Where(z => sector is null
                        || string.Equals(z.Sector, sector, StringComparison.OrdinalIgnoreCase))
            .OrderBy(z => z.Nombre)
            .ToList();
        return Task.FromResult(r);
    }

    public Task<Zona?> ZonaPorIdAsync(int zonaId, CancellationToken ct = default) =>
        Task.FromResult(_base.Zonas.FirstOrDefault(z => z.Id == zonaId));

    public Task<IReadOnlyList<Zona>> ZonasPorIdsAsync(
        IReadOnlyCollection<int> ids, CancellationToken ct = default)
    {
        ConsultasEnLote++;
        var buscadas = ids.ToHashSet();
        IReadOnlyList<Zona> r = _base.Zonas.Where(z => buscadas.Contains(z.Id)).ToList();
        return Task.FromResult(r);
    }

    public Task<IReadOnlyList<CoberturaPrestador>> CoberturaDePrestadorAsync(
        Guid perfilPrestadorId, CancellationToken ct = default)
    {
        IReadOnlyList<CoberturaPrestador> r = _base.Coberturas
            .Where(c => c.PerfilPrestadorId == perfilPrestadorId)
            .ToList();

        // El repositorio real trae la zona con Include; aqui se enlaza a mano.
        foreach (var c in r) c.Zona ??= _base.Zonas.FirstOrDefault(z => z.Id == c.ZonaId);
        return Task.FromResult(r);
    }

    public void AgregarCobertura(CoberturaPrestador cobertura) => _base.Coberturas.Add(cobertura);

    public void QuitarCobertura(CoberturaPrestador cobertura) => _base.Coberturas.Remove(cobertura);
}

/// <summary>
/// El geocodificador que no esta: es lo que hay en produccion hasta que se elija el
/// proveedor (N-08), y sirve para probar que quien llama trata el flujo 4B de CU-03.
/// </summary>
public sealed class GeocodificadorApagado : IGeocodificador
{
    public int Llamadas { get; private set; }

    public Task<Resultado<Punto>> GeocodificarAsync(
        string direccion, CancellationToken ct = default)
    {
        Llamadas++;
        return Task.FromResult(Resultado.Falla<Punto>(
            Fallas.ReglaDeNegocio,
            "No podemos ubicar direcciones todavia. Elige tu zona de la lista."));
    }
}

/// <summary>Un geocodificador que sí responde, para probar el camino del dia que exista.</summary>
public sealed class GeocodificadorFalso : IGeocodificador
{
    private readonly Punto _punto;
    public GeocodificadorFalso(Punto punto) => _punto = punto;
    public string? UltimaDireccion { get; private set; }

    public Task<Resultado<Punto>> GeocodificarAsync(
        string direccion, CancellationToken ct = default)
    {
        UltimaDireccion = direccion;
        return Task.FromResult(Resultado.Ok(_punto));
    }
}
