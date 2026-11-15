using System.Text.RegularExpressions;
using Zass.Aplicacion.Puertos;
using Zass.Dominio.Comun;
using Zass.Dominio.Tarjetas;

namespace Zass.Aplicacion.Tarjetas;

/// <summary>
/// MOD-12 - S6. CS-18 (API-54) y lo del dueno: listar, dar de baja y ver escaneos (API-56 a
/// API-58). La emision y la activacion (API-59, API-55) todavia no.
/// </summary>
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

    // ------------------------------------------------------------- API-56 a API-58 (CU-20)
    // Ninguno mira la suscripcion: listar y desactivar se permiten siempre (perder una tarjeta
    // no puede quedar sin salida porque el plan vencio).

    /// <summary>Ajena e inexistente dicen exactamente lo mismo (403 y 404 responden igual).</summary>
    private static readonly Resultado NoEncontrada =
        Resultado.Falla(Fallas.NoEncontrado, "Esa tarjeta no existe.");

    public async Task<IReadOnlyList<MiTarjetaDto>> MisTarjetasAsync(
        Guid perfilPrestadorId, CancellationToken ct = default)
    {
        var tarjetas = await _repo.DePrestadorAsync(perfilPrestadorId, ct);
        return tarjetas
            .Select(t => new MiTarjetaDto(
                t.Id, t.UrlEstable, EstadoVisible(t.Estado), t.ActivadaEn, t.DesactivadaEn))
            .ToList();
    }

    public async Task<Resultado> DesactivarAsync(
        Guid perfilPrestadorId, Guid tarjetaId, CancellationToken ct = default)
    {
        var tarjeta = await PropiaAsync(perfilPrestadorId, tarjetaId, ct);
        if (tarjeta is null) return NoEncontrada;

        // Ya muerta: no se toca, para que la fecha de baja siga siendo la primera.
        if (tarjeta.Estado != EstadoTarjeta.Activa) return Resultado.Ok();

        tarjeta.Desactivar(_reloj.Ahora);
        await _trabajo.GuardarAsync(ct);
        return Resultado.Ok();
    }

    public async Task<Resultado<EstadisticasDeTarjetaDto>> EstadisticasAsync(
        Guid perfilPrestadorId, Guid tarjetaId, CancellationToken ct = default)
    {
        var tarjeta = await PropiaAsync(perfilPrestadorId, tarjetaId, ct);
        if (tarjeta is null)
            return Resultado.Falla<EstadisticasDeTarjetaDto>(NoEncontrada.Codigo!, NoEncontrada.Mensaje!);

        var ahora = _reloj.Ahora;
        var c = await _repo.EscaneosPorMedioAsync(tarjeta.Id, ahora.AddDays(-7), ahora.AddDays(-30), ct);
        return Resultado.Ok(new EstadisticasDeTarjetaDto(
            new ConteoPorMedio(c.Nfc7, c.Qr7),
            new ConteoPorMedio(c.Nfc30, c.Qr30),
            new ConteoPorMedio(c.NfcTotal, c.QrTotal)));
    }

    /// <summary>
    /// La comprobacion de dueno vive AQUI y no en la consulta: el repositorio busca por id, y
    /// si este filtro se cae, la prueba unitaria lo ve.
    /// </summary>
    private async Task<Tarjeta?> PropiaAsync(Guid perfilPrestadorId, Guid tarjetaId, CancellationToken ct)
    {
        var tarjeta = await _repo.PorIdAsync(tarjetaId, ct);
        return tarjeta is not null && tarjeta.PerfilPrestadorId == perfilPrestadorId ? tarjeta : null;
    }

    /// <summary>Sin reemplazo todavia (pendiente): para el dueno, una reemplazada es una desactivada.</summary>
    private static string EstadoVisible(EstadoTarjeta estado) =>
        estado == EstadoTarjeta.Reemplazada ? nameof(EstadoTarjeta.Desactivada) : estado.ToString();
}
