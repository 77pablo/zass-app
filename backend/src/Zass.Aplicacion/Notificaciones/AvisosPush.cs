using Zass.Dominio.Comun;

namespace Zass.Aplicacion.Notificaciones;

/// <summary>
/// ES-3 (CU-15, MOD-07). Las reglas del canal push, en un solo lugar.
///
/// Cada aviso que admite push y cuyo destinatario tiene al menos un navegador registrado lleva
/// DOS filas en TBL-27: la de correo (como siempre) y su gemela de push, con la misma clave mas
/// <see cref="SufijoClave"/>. Cada canal reintenta por su lado y la clave unica impide duplicar.
/// La gemela la crea la capa de datos al guardar, no cada caso de uso (ver ZassDbContext).
/// </summary>
public static class AvisosPush
{
    public const string SufijoClave = ":push";

    /// <summary>Tope de navegadores por persona. Pasado el tope se borra el que lleva mas sin usarse.</summary>
    public const int MaximoPorUsuario = 10;

    /// <summary>
    /// Los tipos que pueden ir por push (decision del 21-nov). Verificar el correo y recuperar la
    /// contrasena quedan FUERA: llevan un token, y un enlace con token no se manda por un canal
    /// que se lee en la pantalla bloqueada. Cobro y suscripcion SI van, pero su correo sigue
    /// saliendo siempre (API-43 no deja apagarlo: ver PreferenciasAviso.CorreoFijo).
    /// </summary>
    public static bool AdmitePush(TipoNotificacion tipo) => tipo is
        TipoNotificacion.CitaSolicitada or TipoNotificacion.CitaConfirmada or TipoNotificacion.CitaRechazada
        or TipoNotificacion.CitaCancelada or TipoNotificacion.CitaReprogramada
        or TipoNotificacion.Recordatorio24h or TipoNotificacion.Recordatorio2h
        or TipoNotificacion.ResenaNueva or TipoNotificacion.ResenaRespondida
        or TipoNotificacion.CredencialResuelta or TipoNotificacion.CredencialVencida
        // JQ-16: decision de Pablo del 22-nov, el aviso del borrador por eliminarse tambien va por push.
        or TipoNotificacion.BorradorPorEliminarse
        or TipoNotificacion.PerfilPublicado or TipoNotificacion.TarjetaActivada
        or TipoNotificacion.SuscripcionPorVencer or TipoNotificacion.SuscripcionVencida
        or TipoNotificacion.CobroRechazado;

    public static readonly string[] TiposConPush =
        Enum.GetValues<TipoNotificacion>().Where(AdmitePush).Select(t => t.ToString()).ToArray();

    /// <summary>La gemela de push de un aviso de correo: mismo destinatario, tipo, cita, carga y hora.</summary>
    public static Notificacion Gemela(Notificacion correo) => new()
    {
        DestinatarioId = correo.DestinatarioId,
        CitaId = correo.CitaId,
        Tipo = correo.Tipo,
        Canal = CanalNotificacion.Push,
        ClaveIdempotencia = correo.ClaveIdempotencia + SufijoClave,
        Estado = EstadoEnvio.Programada,
        Asunto = correo.Asunto,
        Cuerpo = correo.Cuerpo,
        ProgramadaPara = correo.ProgramadaPara,
    };

    /// <summary>Si a este aviso de correo le corresponde gemela (el destinatario aun no se mira).</summary>
    public static bool LlevaGemela(Notificacion aviso) =>
        aviso.Canal == CanalNotificacion.Correo
        && AdmitePush(aviso.Tipo)
        && aviso.Estado == EstadoEnvio.Programada
        && !string.IsNullOrEmpty(aviso.ClaveIdempotencia);

    /// <summary>
    /// Los servicios de push de los navegadores. El servidor hace un POST al endpoint que manda el
    /// navegador, asi que aceptar cualquier URL dejaria a cualquiera con sesion usar a ZASS! para
    /// golpear direcciones internas o ajenas (SSRF). Solo HTTPS, puerto por omision, y estos
    /// dominios: Chrome, Edge, Opera y Samsung (FCM), Firefox (Mozilla), Edge en Windows (WNS) y
    /// Safari (Apple).
    /// </summary>
    private static readonly string[] DominiosExactos = { "fcm.googleapis.com", "web.push.apple.com" };
    private static readonly string[] DominiosPorSufijo =
        { ".push.services.mozilla.com", ".notify.windows.com", ".push.apple.com" };

    public static bool EndpointPermitido(string? endpoint)
    {
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var u)) return false;
        if (u.Scheme != Uri.UriSchemeHttps || !u.IsDefaultPort) return false;
        if (!string.IsNullOrEmpty(u.UserInfo)) return false;
        var host = u.IdnHost.ToLowerInvariant();
        return DominiosExactos.Contains(host) || DominiosPorSufijo.Any(host.EndsWith);
    }
}
