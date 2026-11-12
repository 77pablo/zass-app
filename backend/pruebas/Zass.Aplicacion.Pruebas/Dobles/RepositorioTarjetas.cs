using Zass.Aplicacion.Puertos;
using Zass.Dominio.Tarjetas;

namespace Zass.Aplicacion.Pruebas.Dobles;

/// <summary>MOD-12 en memoria. Comparacion exacta, como la columna en PostgreSQL.</summary>
public sealed class RepositorioTarjetasEnMemoria : IRepositorioTarjetas
{
    public List<Tarjeta> Tarjetas { get; } = new();
    public List<EscaneoTarjeta> Escaneos { get; } = new();

    /// <summary>Para probar que un segmento malformado ni siquiera consulta.</summary>
    public int Consultas { get; private set; }

    public Task<Tarjeta?> PorUrlEstableAsync(string urlEstable, CancellationToken ct = default)
    {
        Consultas++;
        return Task.FromResult(Tarjetas.FirstOrDefault(t => t.UrlEstable == urlEstable));
    }

    public void AgregarEscaneo(EscaneoTarjeta escaneo) => Escaneos.Add(escaneo);
}
