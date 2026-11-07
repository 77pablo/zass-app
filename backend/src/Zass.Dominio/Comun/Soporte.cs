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
