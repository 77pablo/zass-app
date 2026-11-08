namespace Zass.Infraestructura.Fondo;

/// <summary>
/// Sección <c>Zass:Fondo</c>. El planificador A-09: cada cuánto mira si le toca correr algo.
/// </summary>
public sealed class OpcionesFondo
{
    public const string Seccion = "Zass:Fondo";

    /// <summary>
    /// Apagarlo deja los trabajos sin correr, sin romper nada más. Lo apagan las pruebas de
    /// la API, que levantan el servidor entero: un trabajo que vence suscripciones en medio
    /// de otra prueba la pondría roja sin que nadie entienda por qué.
    /// </summary>
    public bool Activo { get; set; } = true;

    /// <summary>
    /// Cada cuánto se pregunta a la base si algún trabajo está vencido. No es cada cuánto
    /// corre el trabajo: eso lo decide <c>proxima_corrida_en</c> en la fila, que es lo único
    /// que comparten las réplicas.
    /// </summary>
    public int SondeoSegundos { get; set; } = 300;

    /// <summary>Cada cuánto corre el trabajo diario de suscripciones.</summary>
    public int IntervaloHoras { get; set; } = 24;

    /// <summary>
    /// Cuánto se reserva el trabajo quien lo toma. Tiene que superar de sobra lo que tarda:
    /// si vence a mitad, otra réplica lo retomaría en paralelo. Vencer y avisar son dos
    /// consultas y unos cuantos INSERT, así que diez minutos es holgado.
    /// </summary>
    public int ReservaMinutos { get; set; } = 10;

    /// <summary>
    /// Espera antes de volver a intentar un trabajo que se cayó. Corto a propósito: si falló
    /// por algo pasajero conviene reintentar hoy, no mañana.
    /// </summary>
    public int EsperaTrasFalloMinutos { get; set; } = 30;
}
