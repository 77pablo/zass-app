using Zass.Dominio.Identidad;

namespace Zass.Dominio.Comun;

/// <summary>CL-18 · TBL-27. Un aviso que debe llegar una sola vez.</summary>
public class Notificacion
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid DestinatarioId { get; set; }
    public Guid? CitaId { get; set; }
    public TipoNotificacion Tipo { get; set; }
    public CanalNotificacion Canal { get; set; } = CanalNotificacion.Correo;
    /// <summary>Impide el aviso duplicado (R-18). Determinista, no aleatoria.</summary>
    public string ClaveIdempotencia { get; set; } = "";
    public EstadoEnvio Estado { get; set; } = EstadoEnvio.Programada;
    public string Asunto { get; set; } = "";
    public string Cuerpo { get; set; } = "";
    public DateTime ProgramadaPara { get; set; } = DateTime.UtcNow;
    public DateTime? EnviadaEn { get; set; }
    public DateTime? LeidaEn { get; set; }
    public short Intentos { get; set; }
    public string? UltimoError { get; set; }

    public Usuario? Destinatario { get; set; }

    public void Anular() => Estado = EstadoEnvio.Anulada;
    public void MarcarEnviada()
    {
        Estado = EstadoEnvio.Enviada;
        EnviadaEn = DateTime.UtcNow;
    }
}

/// <summary>
/// ES-3 · API-45. Un navegador (celular, computador) donde la persona activo los avisos push.
/// Una persona puede tener varios; un navegador pertenece a una sola cuenta a la vez.
/// El endpoint es una URL «de capacidad» del servicio de push: quien la tiene puede mandar
/// avisos a ese navegador. Por eso no se registra en logs ni se devuelve en ninguna respuesta.
/// </summary>
public class SuscripcionPush
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UsuarioId { get; set; }
    public string Endpoint { get; set; } = "";
    /// <summary>Clave publica del navegador (P-256), en base64url. Para cifrar el aviso.</summary>
    public string ClaveP256dh { get; set; } = "";
    /// <summary>Secreto de autenticacion del navegador (16 bytes), en base64url.</summary>
    public string ClaveAuth { get; set; } = "";
    public DateTime CreadaEn { get; set; } = DateTime.UtcNow;
    /// <summary>Ultimo registro o ultimo aviso entregado: decide cual se borra al pasar el tope.</summary>
    public DateTime UltimoUsoEn { get; set; } = DateTime.UtcNow;

    public Usuario? Usuario { get; set; }
}

/// <summary>
/// ES-3b · API-43 (CS-14). Por que canal quiere una persona un tipo de aviso. Solo se guardan las
/// excepciones: sin fila, el aviso sale por correo y por push (si tiene navegador).
/// </summary>
public class PreferenciaAviso
{
    public Guid UsuarioId { get; set; }
    public TipoNotificacion Tipo { get; set; }
    public bool Correo { get; set; } = true;
    public bool Push { get; set; } = true;
    public DateTime ActualizadaEn { get; set; } = DateTime.UtcNow;

    public Usuario? Usuario { get; set; }
}

/// <summary>
/// TBL-28. Rastro de acciones sensibles. Solo insercion y lectura.
/// Nunca guarda contrasenas, tokens, contenido de documentos ni direcciones.
/// </summary>
public class RegistroAuditoria
{
    public long Id { get; set; }
    public Guid? ActorId { get; set; }
    public string Accion { get; set; } = "";
    public string Entidad { get; set; } = "";
    public Guid? EntidadId { get; set; }
    public string? Contexto { get; set; }
    public string? IpOrigen { get; set; }
    public DateTime OcurridoEn { get; set; } = DateTime.UtcNow;
}
