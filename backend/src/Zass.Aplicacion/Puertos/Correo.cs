using System.Text.Json;

namespace Zass.Aplicacion.Puertos;

/// <summary>Un correo ya armado, listo para entregarse a un proveedor.</summary>
public sealed record CorreoSaliente(
    string Para, string NombreDestinatario, string Asunto, string Texto, string Html);

/// <summary>
/// Que hacer despues de un intento de envio. La distincion Reintentable / Rechazado es la
/// que importa: un 4xx (clave mala, remitente no verificado, direccion invalida) no mejora
/// por esperar, y reintentarlo solo gasta la cuota diaria del plan gratuito.
/// </summary>
public enum ResultadoEnvio { Enviado, Reintentable, Rechazado }

/// <param name="Detalle">Texto breve y SEGURO para guardar: nunca el cuerpo de la respuesta
/// del proveedor, que podria repetir la direccion del destinatario.</param>
public sealed record ResultadoDeEnvio(ResultadoEnvio Resultado, string? Detalle = null)
{
    public static ResultadoDeEnvio Ok() => new(ResultadoEnvio.Enviado);
}

/// <summary>Puerto de salida del correo. Brevo, Resend o nada (desarrollo).</summary>
public interface IEnviadorDeCorreo
{
    Task<ResultadoDeEnvio> EnviarAsync(CorreoSaliente correo, CancellationToken ct = default);
}

/// <summary>
/// La carga util de los avisos que llevan un token en claro (verificacion y recuperacion).
///
/// En la tabla de usuarios solo vive el HASH del token; el unico lugar donde existe en claro
/// es este aviso, porque alguien tiene que ponerlo en el correo. Por eso la carga es
/// estructurada: el enviador la lee, y en cuanto el correo sale (o el aviso fracasa para
/// siempre) la <see cref="Purgada"/> la reemplaza y el token deja de existir en la base.
/// </summary>
public static class CargaDeAviso
{
    public const string Purgada = "{\"purgado\":true}";

    public static string ConToken(string token) => ConCampos(("token", token));

    /// <summary>
    /// Una carga sin token, para los avisos que no llevan secreto: los de suscripcion solo
    /// necesitan cuantos dias quedan. Se guardan igual como JSON para que haya UN formato de
    /// cuerpo y no dos, y para que <see cref="Purgada"/> siga queriendo decir lo mismo.
    /// </summary>
    public static string ConCampos(params (string Clave, string Valor)[] campos) =>
        JsonSerializer.Serialize(campos.ToDictionary(c => c.Clave, c => c.Valor));

    /// <summary>El token en claro, o null si la carga ya fue purgada o no es de este formato.</summary>
    public static string? LeerToken(string cuerpo) => LeerCampo(cuerpo, "token");

    /// <summary>Un campo de la carga, o null si no esta, fue purgada o no es JSON.</summary>
    public static string? LeerCampo(string cuerpo, string clave)
    {
        try
        {
            using var doc = JsonDocument.Parse(cuerpo);
            return doc.RootElement.ValueKind == JsonValueKind.Object
                   && doc.RootElement.TryGetProperty(clave, out var v)
                   && v.ValueKind == JsonValueKind.String
                ? v.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
