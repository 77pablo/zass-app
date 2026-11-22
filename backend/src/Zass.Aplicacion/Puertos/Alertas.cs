namespace Zass.Aplicacion.Puertos;

/// <summary>
/// Alerta de tarjeta bloqueada por intentos fallidos (CU-19 4B.5).
/// Se envia por correo al administrador cuando una tarjeta alcanza el 5.o fallo en 15 minutos.
/// </summary>
public sealed record AlertaTarjetaBloqueada(
    string UrlEstable,
    DateTime QuintoFalloUtc,
    DateTime BloqueadaHastaUtc);

/// <summary>
/// Puerto para notificar al administrador eventos criticos de seguridad.
/// Implementacion en Zass.Infraestructura que envia un correo.
/// </summary>
public interface IAlertasAdministrador
{
    /// <summary>
    /// Notifica al administrador que una tarjeta fue bloqueada por intentos fallidos.
    /// Se llama SOLO despues de que la transaccion de ActivarAsync haya hecho commit,
    /// y SOLO cuando ese intento fue el que disparó el bloqueo (una sola vez por bloqueo).
    /// </summary>
    Task AvisarTarjetaBloqueadaAsync(AlertaTarjetaBloqueada alerta, CancellationToken ct = default);
}
