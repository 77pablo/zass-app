using Zass.Dominio.Agenda;
using Zass.Dominio.Comun;

namespace Zass.Aplicacion.Agenda;

/// <summary>
/// JQ-3 (RN-S4-07): recordatorio de 24 h y de 2 h, al buscador y al prestador. Las ventanas
/// viven aqui y solo aqui: el planificador las usa para encolar y el procesador de correo
/// para volver a mirar justo antes de enviar.
/// </summary>
public static class RecordatoriosDeCita
{
    public static readonly TimeSpan Desde24h = TimeSpan.FromHours(24);
    /// <summary>Con menos de 12 h ya no sale el de 24 h: para eso esta el de 2 h.</summary>
    public static readonly TimeSpan Hasta24h = TimeSpan.FromHours(12);
    public static readonly TimeSpan Desde2h = TimeSpan.FromHours(2);
    /// <summary>Con menos de 30 min ya no sale el de 2 h: llegaria cuando la persona ya va en camino.</summary>
    public static readonly TimeSpan Hasta2h = TimeSpan.FromMinutes(30);

    public static readonly TipoNotificacion[] Tipos =
        { TipoNotificacion.Recordatorio24h, TipoNotificacion.Recordatorio2h };

    /// <summary>CC-04: determinista, una por cita, tipo y rol.</summary>
    public static string Clave(TipoNotificacion tipo, Guid citaId, string rol) => tipo switch
    {
        TipoNotificacion.Recordatorio24h => $"recordatorio-24h:{citaId}:{rol}",
        TipoNotificacion.Recordatorio2h => $"recordatorio-2h:{citaId}:{rol}",
        _ => throw new ArgumentOutOfRangeException(nameof(tipo)),
    };

    /// <summary>
    /// Toca si la cita sigue confirmada, falta lo justo (ni mas que la ventana ni menos que su
    /// cierre) y se agendo con al menos esa anticipacion. Se mide desde <c>CreadaEn</c>: no hay
    /// columna de «confirmada en» (ver el PR: confirmacion tardia).
    /// </summary>
    public static bool Corresponde(TipoNotificacion tipo, EstadoCita estado,
        DateTime inicioEn, DateTime creadaEn, DateTime ahora)
    {
        if (estado != EstadoCita.Confirmada) return false;
        var (desde, hasta) = tipo == TipoNotificacion.Recordatorio24h
            ? (Desde24h, Hasta24h) : (Desde2h, Hasta2h);
        var falta = inicioEn - ahora;
        return falta <= desde && falta >= hasta && inicioEn - creadaEn >= desde;
    }
}
