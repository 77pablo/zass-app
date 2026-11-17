using Zass.Aplicacion.Puertos;
using Zass.Dominio.Comun;
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

    public Task<IReadOnlyList<Tarjeta>> DePrestadorAsync(
        Guid perfilPrestadorId, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<Tarjeta>>(Tarjetas
            .Where(t => t.PerfilPrestadorId == perfilPrestadorId)
            .OrderByDescending(t => t.EmitidaEn).ToList());

    /// <summary>
    /// Por id y NADA mas, como la consulta real: si este doble filtrara por dueno, la prueba
    /// unitaria seguiria verde aunque el servicio dejara de comprobarlo.
    /// </summary>
    public Task<Tarjeta?> PorIdAsync(Guid id, CancellationToken ct = default) =>
        Task.FromResult(Tarjetas.FirstOrDefault(t => t.Id == id));

    public Task<EscaneosContados> EscaneosPorMedioAsync(
        Guid tarjetaId, DateTime desde7, DateTime desde30, CancellationToken ct = default)
    {
        var suyos = Escaneos.Where(e => e.TarjetaId == tarjetaId).ToList();
        int Contar(MedioEscaneo m, DateTime? desde) =>
            suyos.Count(e => e.Medio == m && (desde is null || e.OcurridoEn >= desde));
        return Task.FromResult(new EscaneosContados(
            Contar(MedioEscaneo.Nfc, desde7), Contar(MedioEscaneo.Qr, desde7),
            Contar(MedioEscaneo.Nfc, desde30), Contar(MedioEscaneo.Qr, desde30),
            Contar(MedioEscaneo.Nfc, null), Contar(MedioEscaneo.Qr, null)));
    }
}
