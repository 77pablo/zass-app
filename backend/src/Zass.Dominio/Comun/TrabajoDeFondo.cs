namespace Zass.Dominio.Comun;

/// <summary>
/// A-09 · CC-08 · TBL-32. Una fila por trabajo programado: cuando le toca correr y quien lo tiene
/// reservado ahora mismo.
///
/// Existe porque el planificador corre en TODAS las replicas de la API (N-02: de 2 a 10 en
/// produccion). Sin un dato compartido que diga "esto ya se hizo hoy", el trabajo diario se
/// ejecutaria una vez por replica, y avisar dos veces manda dos correos a la misma persona.
///
/// La memoria del proceso no sirve para esto por dos razones distintas: no la comparten las
/// replicas, y no sobrevive a un reinicio (un despliegue a media manana volveria a correr el
/// trabajo del dia). Por eso la decision vive en la fila, no en el proceso.
/// </summary>
public class TrabajoDeFondo
{
    /// <summary>Identificador estable del trabajo. Lo eligen las constantes de <c>Trabajos</c>.</summary>
    public string Nombre { get; set; } = "";

    /// <summary>Desde cuando se puede volver a correr. Es el horario, guardado.</summary>
    public DateTime ProximaCorridaEn { get; set; }

    /// <summary>
    /// Hasta cuando lo tiene tomado un trabajador. La reserva vence a proposito: si la
    /// replica muere a mitad del trabajo, otra lo retoma pasado el plazo en vez de que el
    /// trabajo quede trabado para siempre.
    /// </summary>
    public DateTime ReservadoHasta { get; set; }

    public DateTime? UltimaCorridaEn { get; set; }

    /// <summary>Cuantas veces se completo. Sirve para ver de un vistazo que el trabajo vive.</summary>
    public int Corridas { get; set; }

    /// <summary>Resumen breve de la ultima corrida. Nunca datos personales.</summary>
    public string? UltimoResumen { get; set; }
}

/// <summary>Los nombres de los trabajos programados. Son datos de la base: no se renombran a la ligera.</summary>
public static class Trabajos
{
    /// <summary>Vencer las suscripciones pasadas de fecha y avisar a quien esta por vencer.</summary>
    public const string Suscripciones = "suscripciones-diario";

    /// <summary>
    /// T5, T8 y T11 de la cita: expirar las manuales sin respuesta, pasar a «pendiente de
    /// asistencia» las que ya ocurrieron y asumir realizadas las que nadie marcó en 72 h.
    /// </summary>
    public const string Citas = "citas-periodico";

    /// <summary>JQ-16 (CU-05 4D.3): avisar y borrar los borradores de credencial abandonados.</summary>
    public const string Borradores = "borradores-abandonados";

    /// <summary>CS-06 · T8 de la credencial: pasar a «vencida» las verificadas con vence_en &lt; hoy.</summary>
    public const string CredencialesVencidas = "credenciales-vencidas-diario";

    /// <summary>
    /// Auditoria 22-nov-2026: borrar las cuentas que siguen sin verificar el correo pasadas 72 h
    /// desde su creacion, para que no retengan un RUT o un correo ajeno para siempre.
    /// </summary>
    public const string CuentasSinVerificar = "cuentas-sin-verificar";

    /// <summary>
    /// Auditoria 22-nov-2026: borrar los archivos huerfanos del almacenamiento (guardados pero sin
    /// ninguna fila que los referencie).
    /// </summary>
    public const string ArchivosHuerfanos = "archivos-huerfanos";
}
