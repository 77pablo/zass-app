using Zass.Dominio.Comun;
using Zass.Dominio.Tarjetas;

namespace Zass.Aplicacion.Tarjetas;

/// <summary>MOD-12 - S6. La interfaz publica del modulo.</summary>
public interface IServicioTarjetas
{
    /// <summary>
    /// CS-18 (incluye CU-09). Resuelve la URL estable y, solo si lleva a una ficha, cuenta el
    /// escaneo. Nunca falla: un segmento malformado, inexistente, desactivado o reemplazado
    /// devuelven EXACTAMENTE lo mismo (RN-S6-06).
    /// </summary>
    Task<EscaneoResuelto> RegistrarEscaneoAsync(
        string? segmento, MedioEscaneo medio, CancellationToken ct = default);

    /// <summary>
    /// API-56 (CU-20). Las tarjetas del prestador, activas y desactivadas, la mas reciente
    /// primero. Se permite siempre, aunque la suscripcion este vencida.
    /// </summary>
    Task<IReadOnlyList<MiTarjetaDto>> MisTarjetasAsync(
        Guid perfilPrestadorId, CancellationToken ct = default);

    /// <summary>
    /// API-57 (CU-20, RN-S6-05). Baja inmediata. Ajena e inexistente fallan IGUAL
    /// (<see cref="Fallas.NoEncontrado"/>): decir «existe pero no es tuya» ya es informacion.
    /// Desactivar una ya desactivada no cambia nada y no es error. Sin reemplazo: ni
    /// Reemplazada ni sucesora (queda pendiente).
    /// </summary>
    Task<Resultado> DesactivarAsync(
        Guid perfilPrestadorId, Guid tarjetaId, CancellationToken ct = default);

    /// <summary>
    /// API-58 (CS-18). Escaneos agregados por medio y periodo. Sin conversion (JQ-8) y sin
    /// identidad (R-20): por diseno, cualquiera puede inflar estas cifras escaneando.
    /// Ajena e inexistente fallan igual que en <see cref="DesactivarAsync"/>.
    /// </summary>
    Task<Resultado<EstadisticasDeTarjetaDto>> EstadisticasAsync(
        Guid perfilPrestadorId, Guid tarjetaId, CancellationToken ct = default);

    /// <summary>
    /// API-59 (CU-19, CU-20). Emite una tarjeta nueva con su codigo de activacion.
    /// Devuelve el codigo EN CLARO una sola vez (201). Tasa S. Requiere rol Administrador.
    /// </summary>
    Task<Resultado<EmitirTarjetaResultado>> EmitirAsync(CancellationToken ct = default);

    /// <summary>
    /// API-55 (CU-19). Activa la tarjeta con codigo de un solo uso.
    /// Implementa CC-05: consumo atomico con bloqueo de fila. Tasa A.
    /// Requiere sesion prestador con perfil publicado.
    /// </summary>
    Task<Resultado<ActivarTarjetaResultado>> ActivarAsync(
        string segmento, string codigo, Guid perfilPrestadorId, string? ipOrigen, CancellationToken ct = default);
}

/// <summary>El slug viene solo con <see cref="DestinoTarjeta.Ficha"/>; en los demas es nulo.</summary>
public sealed record EscaneoResuelto(DestinoTarjeta Destino, string? Slug);

/// <summary>
/// Lo que el dueno ve de su tarjeta. A proposito NO lleva el hash, el vencimiento del codigo,
/// los intentos, el bloqueo ni la sucesora: nada de la activacion sale de aqui.
/// </summary>
public sealed record MiTarjetaDto(
    Guid Id,
    string UrlEstable,
    string Estado,
    DateTime? ActivadaEn,
    DateTime? DesactivadaEn);

/// <summary>
/// Resultado de API-59: codigo en claro (solo esta vez), segmento y vencimiento.
/// </summary>
public sealed record EmitirTarjetaResultado(
    string Segmento,
    string Codigo,
    DateTime VenceEn);

/// <summary>
/// Resultado de API-55: mensaje y URL de la ficha.
/// </summary>
public sealed record ActivarTarjetaResultado(
    string Mensaje,
    string UrlFicha);

public sealed record ConteoPorMedio(int Nfc, int Qr)
{
    public int Total => Nfc + Qr;
}

/// <summary>Periodos corridos desde ahora: 7 y 30 dias, mas el total desde siempre.</summary>
public sealed record EstadisticasDeTarjetaDto(
    ConteoPorMedio Ultimos7Dias,
    ConteoPorMedio Ultimos30Dias,
    ConteoPorMedio Total);
