using System.Net;
using Lib.Net.Http.WebPush;
using Lib.Net.Http.WebPush.Authentication;
using Microsoft.Extensions.Options;
using Zass.Dominio.Comun;

namespace Zass.Infraestructura.Push;

public enum ResultadoPush
{
    Entregado,
    /// <summary>404 o 410: el navegador ya no existe (desinstalo, revoco el permiso). Se borra.</summary>
    Vencida,
    /// <summary>429, 5xx, red o tiempo agotado: se reintenta mas tarde.</summary>
    Reintentable,
    /// <summary>Cualquier otro 4xx: reintentar no lo arregla.</summary>
    Rechazado,
}

/// <summary>El aviso ya armado, en claro. Lo cifra el enviador para ese navegador (RFC 8291).</summary>
public sealed record MensajeSaliente(string Json, TimeSpan Vigencia, bool Urgente);

public interface IEnviadorDePush
{
    Task<ResultadoPush> EnviarAsync(SuscripcionPush destino, MensajeSaliente mensaje, CancellationToken ct);
}

/// <summary>Web Push con VAPID, por Lib.Net.Http.WebPush (criptografia de .NET, sin BouncyCastle).</summary>
public sealed class EnviadorWebPush : IEnviadorDePush, IDisposable
{
    private readonly PushServiceClient _cliente;
    private readonly VapidAuthentication _vapid;

    public EnviadorWebPush(HttpClient http, IOptions<OpcionesPush> opciones)
    {
        var o = opciones.Value;
        _vapid = new VapidAuthentication(o.ClavePublica, o.ClavePrivada) { Subject = o.Sujeto };
        // Sin reintento propio de la libreria: los reintentos los decide el procesador, con su
        // espera creciente y su tope, igual que el correo.
        _cliente = new PushServiceClient(http) { DefaultAuthentication = _vapid, AutoRetryAfter = false };
    }

    public async Task<ResultadoPush> EnviarAsync(SuscripcionPush destino, MensajeSaliente mensaje, CancellationToken ct)
    {
        var suscripcion = new PushSubscription { Endpoint = destino.Endpoint };
        suscripcion.SetKey(PushEncryptionKeyName.P256DH, destino.ClaveP256dh);
        suscripcion.SetKey(PushEncryptionKeyName.Auth, destino.ClaveAuth);

        var push = new PushMessage(mensaje.Json)
        {
            TimeToLive = (int)Math.Clamp(mensaje.Vigencia.TotalSeconds, 60, 4 * 7 * 24 * 3600),
            Urgency = mensaje.Urgente ? PushMessageUrgency.High : PushMessageUrgency.Normal,
        };

        try
        {
            await _cliente.RequestPushMessageDeliveryAsync(suscripcion, push, ct);
            return ResultadoPush.Entregado;
        }
        catch (PushServiceClientException ex)
        {
            return Clasificar(ex.StatusCode);
        }
        catch (HttpRequestException)
        {
            return ResultadoPush.Reintentable;
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            return ResultadoPush.Reintentable;   // tiempo agotado del HttpClient
        }
    }

    internal static ResultadoPush Clasificar(HttpStatusCode estado) => (int)estado switch
    {
        404 or 410 => ResultadoPush.Vencida,
        429 or >= 500 => ResultadoPush.Reintentable,
        _ => ResultadoPush.Rechazado,
    };

    public void Dispose() => _vapid.Dispose();
}

/// <summary>Sin claves VAPID. No deberia llamarse nunca (el procesador no toma nada), pero si pasa, no envia.</summary>
public sealed class EnviadorPushNulo : IEnviadorDePush
{
    public Task<ResultadoPush> EnviarAsync(SuscripcionPush destino, MensajeSaliente mensaje, CancellationToken ct) =>
        Task.FromResult(ResultadoPush.Reintentable);
}
