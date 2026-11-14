using System.Text.RegularExpressions;
using Zass.Aplicacion.Puertos;
using Zass.Dominio.Comun;
using Zass.Dominio.Tarjetas;

namespace Zass.Aplicacion.Tarjetas;

/// <summary>MOD-12 - S6. Por ahora solo CS-18; la activacion y la baja (CU-19, CU-20) no son de J4.</summary>
public sealed class ServicioTarjetas : IServicioTarjetas
{
    /// <summary>
    /// La forma de <see cref="Tarjeta.GenerarUrlEstable"/>: 4+4 del alfabeto sin I, L, O, 0 ni 1.
    /// Lo que no calza ni llega a la base.
    /// </summary>
    private static readonly Regex FormaDeUrlEstable = new(
        "^[A-HJKMNP-Z2-9]{4}-[A-HJKMNP-Z2-9]{4}$", RegexOptions.CultureInvariant);

    private static readonly EscaneoResuelto NoValida = new(DestinoTarjeta.NoValida, null);

    private readonly IRepositorioTarjetas _repo;
    private readonly IUnidadDeTrabajo _trabajo;
    private readonly IRelojSistema _reloj;

    public ServicioTarjetas(IRepositorioTarjetas repo, IUnidadDeTrabajo trabajo, IRelojSistema reloj)
    {
        _repo = repo;
        _trabajo = trabajo;
        _reloj = reloj;
    }

    public async Task<EscaneoResuelto> RegistrarEscaneoAsync(
        string? segmento, MedioEscaneo medio, CancellationToken ct = default)
    {
        // Impresa va en mayusculas, pero hay lectores de QR que la bajan a minusculas.
        var urlEstable = (segmento ?? "").Trim().ToUpperInvariant();
        if (urlEstable.Length != 9 || !FormaDeUrlEstable.IsMatch(urlEstable)) return NoValida;

        var tarjeta = await _repo.PorUrlEstableAsync(urlEstable, ct);
        if (tarjeta is null) return NoValida;

        var destino = tarjeta.Resolver();
        // Solo cuenta lo que llega a una ficha: el prestador ve sus escaneos (API-58), y una
        // tarjeta muerta o sin activar no es de nadie que deba verlos. Ademas, asi una tarjeta
        // desactivada y una inexistente hacen exactamente el mismo trabajo.
        if (destino != DestinoTarjeta.Ficha) return new EscaneoResuelto(destino, null);

        _repo.AgregarEscaneo(new EscaneoTarjeta
        {
            TarjetaId = tarjeta.Id,
            Medio = Enum.IsDefined(medio) ? medio : MedioEscaneo.Nfc,
            OcurridoEn = _reloj.Ahora,
        });
        await _trabajo.GuardarAsync(ct);

        return new EscaneoResuelto(DestinoTarjeta.Ficha, tarjeta.PerfilPrestador!.Slug);
    }
}
