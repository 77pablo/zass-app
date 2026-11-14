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
}

/// <summary>El slug viene solo con <see cref="DestinoTarjeta.Ficha"/>; en los demas es nulo.</summary>
public sealed record EscaneoResuelto(DestinoTarjeta Destino, string? Slug);
