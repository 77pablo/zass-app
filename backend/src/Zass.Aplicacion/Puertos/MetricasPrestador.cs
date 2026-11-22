using Zass.Dominio.Comun;

namespace Zass.Aplicacion.Puertos;

/// <summary>
/// JQ-8 · MI-METRICAS. Lo que el prestador puede saber de su actividad con lo que ya se registra:
/// los contactos que recibió (JQ-5, tabla `contactos`) y los escaneos de TODAS sus tarjetas
/// (tabla `escaneos`; API-58 ya los da tarjeta por tarjeta).
///
/// Las VISTAS de la ficha no están: nada las registra hoy, y contarlas necesita una tabla nueva
/// (migración). Queda como pregunta en el PR.
/// </summary>
public interface IConsultaMetricasPrestador
{
    Task<MetricasDePrestador> CalcularAsync(Guid perfilPrestadorId, DateTime ahora, CancellationToken ct = default);
}

/// <summary>Un conteo en las mismas tres ventanas que API-58: 7 días, 30 días y desde siempre.</summary>
public sealed record ConteoEnVentanas(int Ultimos7Dias, int Ultimos30Dias, int Total);

/// <summary>Contactos de los últimos 30 días, por canal (`CanalContacto`).</summary>
public sealed record ContactosPorCanal(int Whatsapp, int Telefono, int Correo, int Formulario);

public sealed record MetricasDePrestador(
    ConteoEnVentanas Contactos,
    ContactosPorCanal ContactosPorCanal30Dias,
    ConteoEnVentanas Escaneos,
    DateTime CalculadoEn);
