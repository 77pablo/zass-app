using System.Text.Json;
using Zass.Dominio.Comun;

namespace Zass.Aplicacion.Comun;

/// <summary>
/// Arma una entrada de TBL-28 sin repetir el objeto en cada caso de uso.
///
/// El contexto se serializa a JSON porque la columna es `jsonb`: no es un capricho del
/// mapeo, es lo que permite preguntarle a la auditoria cosas como "cuantos intentos
/// fallidos con motivo suspendida hubo esta semana". Texto plano no se puede consultar,
/// y ademas PostgreSQL lo rechaza.
///
/// Lo que este metodo NO acepta es igual de importante: ningun parametro recibe
/// contrasenas, tokens ni direcciones (seguridad.md, seccion 6).
/// </summary>
public static class Auditar
{
    public static RegistroAuditoria Entrada(
        string accion, string entidad, Guid? entidadId, DateTime ahora,
        Guid? actorId = null, object? contexto = null, string? ip = null) => new()
        {
            Accion = accion,
            Entidad = entidad,
            EntidadId = entidadId,
            ActorId = actorId,
            Contexto = contexto is null ? null : JsonSerializer.Serialize(contexto),
            IpOrigen = ip,
            OcurridoEn = ahora,
        };
}
