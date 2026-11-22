namespace Zass.Aplicacion.Puertos;

/// <summary>
/// API-60 · JQ-14. Los indicadores de la operación, calculados al vuelo con las tablas que ya
/// existen (sin migración): credenciales, verificaciones, notificaciones, reportes y tarjetas.
/// Solo lectura. El instante «ahora» lo pone quien pregunta (el reloj), para poder probar los
/// cortes de 7 y 30 días sin esperar.
/// </summary>
public interface IConsultaIndicadores
{
    Task<IndicadoresDeOperacion> CalcularAsync(DateTime ahora, CancellationToken ct = default);
}

/// <summary>Las ventanas de tiempo que usa cada cifra. Públicas para que la pantalla y las pruebas digan lo mismo.</summary>
public static class VentanasDeIndicadores
{
    public const int DiasVerificacion = 30;
    public const int DiasAvisos = 7;
}

public sealed record IndicadoresDeOperacion(
    IndicadoresDeVerificacion Verificacion,
    IndicadoresDeAvisos Avisos,
    int ReportesAbiertos,
    int TarjetasEmitidasSinActivar,
    DateTime CalculadoEn);

/// <param name="EnCola">Credenciales en estado EnRevision.</param>
/// <param name="MasAntiguaDesde">
/// `CreadaEn` de la más antigua en cola: la misma fecha que API-29 muestra como «en cola desde».
/// El envío a revisión no se guarda con fecha (ver el PR de JQ-14).
/// </param>
/// <param name="Resueltas">Verificaciones resueltas en los últimos 30 días (verificadas, rechazadas o con antecedentes pedidos).</param>
/// <param name="Rechazadas">De esas, las rechazadas.</param>
/// <param name="TasaDeRechazo">Rechazadas / Resueltas, entre 0 y 1; nula si no hubo ninguna resuelta.</param>
/// <param name="HorasPromedioDesdeCreacion">
/// NO es «desde el envío a revisión»: es el promedio, en horas, entre `Credencial.CreadaEn` y
/// `Verificacion.ResueltaEn` de las resueltas en 30 días, porque el envío no tiene fecha. Incluye el
/// tiempo en borrador. Nula si no hubo ninguna.
/// </param>
public sealed record IndicadoresDeVerificacion(
    int EnCola,
    DateTime? MasAntiguaDesde,
    int Resueltas,
    int Rechazadas,
    double? TasaDeRechazo,
    double? HorasPromedioDesdeCreacion);

/// <param name="CorreosFallidos">
/// Correos en estado Fallida con `ProgramadaPara` en los últimos 7 días. No existe una fecha de
/// «cuándo falló»; la programada es la que mejor la aproxima.
/// </param>
public sealed record IndicadoresDeAvisos(int CorreosFallidos);
