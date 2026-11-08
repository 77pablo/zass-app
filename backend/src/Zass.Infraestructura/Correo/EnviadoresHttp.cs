using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Zass.Aplicacion.Puertos;

namespace Zass.Infraestructura.Correo;

/// <summary>
/// Clasificación común de las respuestas HTTP. 2xx entregado; 408/425/429 y 5xx son fallos
/// del momento (se reintentan); el resto de los 4xx son definitivos: reintentar una clave
/// mala o un remitente sin verificar solo quemaría la cuota diaria.
/// </summary>
internal static class Clasificar
{
    public static ResultadoDeEnvio Respuesta(HttpStatusCode codigo)
    {
        var n = (int)codigo;
        if (n is >= 200 and < 300) return ResultadoDeEnvio.Ok();
        var transitorio = n is 408 or 425 or 429 || n >= 500;
        // Solo el código: el cuerpo de error del proveedor puede repetir la dirección.
        return new(transitorio ? ResultadoEnvio.Reintentable : ResultadoEnvio.Rechazado, $"HTTP {n}");
    }
}

/// <summary>Base: envía el JSON y traduce excepciones. La API key viaja solo en la cabecera.</summary>
public abstract class EnviadorHttpBase : IEnviadorDeCorreo
{
    private readonly HttpClient _http;
    private readonly ILogger _log;

    protected EnviadorHttpBase(HttpClient http, ILogger log)
    {
        _http = http;
        _log = log;
    }

    protected abstract HttpRequestMessage Armar(CorreoSaliente correo);

    public async Task<ResultadoDeEnvio> EnviarAsync(CorreoSaliente correo, CancellationToken ct = default)
    {
        try
        {
            using var peticion = Armar(correo);
            using var respuesta = await _http.SendAsync(peticion, HttpCompletionOption.ResponseHeadersRead, ct);
            return Clasificar.Respuesta(respuesta.StatusCode);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;   // el apagado de la aplicación no es un fallo del proveedor
        }
        catch (OperationCanceledException)
        {
            // HttpClient.Timeout: llega como cancelación sin que nadie haya cancelado.
            _log.LogWarning("El proveedor de correo no respondió a tiempo.");
            return new(ResultadoEnvio.Reintentable, "tiempo de espera agotado");
        }
        catch (HttpRequestException ex)
        {
            // Solo el tipo: el mensaje de la excepción puede traer la URL y datos.
            _log.LogWarning("No se pudo contactar al proveedor de correo ({Tipo}).", ex.GetType().Name);
            return new(ResultadoEnvio.Reintentable, "sin conexión con el proveedor");
        }
    }
}

/// <summary>Brevo, API v3: POST /v3/smtp/email con la cabecera <c>api-key</c>.</summary>
public sealed class EnviadorBrevo : EnviadorHttpBase
{
    public const string Url = "https://api.brevo.com/v3/smtp/email";
    private readonly OpcionesCorreo _o;

    public EnviadorBrevo(HttpClient http, IOptions<OpcionesCorreo> o, ILogger<EnviadorBrevo> log)
        : base(http, log) => _o = o.Value;

    protected override HttpRequestMessage Armar(CorreoSaliente c)
    {
        var msg = new HttpRequestMessage(HttpMethod.Post, Url)
        {
            Content = JsonContent.Create(new
            {
                sender = new { name = _o.NombreRemitente, email = _o.Remitente },
                to = new[] { new { email = c.Para, name = c.NombreDestinatario } },
                subject = c.Asunto,
                htmlContent = c.Html,
                textContent = c.Texto,
            }),
        };
        msg.Headers.Add("api-key", _o.ApiKey);
        msg.Headers.Add("accept", "application/json");
        return msg;
    }
}

/// <summary>Resend: POST /emails con <c>Authorization: Bearer</c>.</summary>
public sealed class EnviadorResend : EnviadorHttpBase
{
    public const string Url = "https://api.resend.com/emails";
    private readonly OpcionesCorreo _o;

    public EnviadorResend(HttpClient http, IOptions<OpcionesCorreo> o, ILogger<EnviadorResend> log)
        : base(http, log) => _o = o.Value;

    protected override HttpRequestMessage Armar(CorreoSaliente c)
    {
        var msg = new HttpRequestMessage(HttpMethod.Post, Url)
        {
            Content = JsonContent.Create(new
            {
                from = $"{_o.NombreRemitente.Replace("<", "").Replace(">", "")} <{_o.Remitente}>",
                to = new[] { c.Para },
                subject = c.Asunto,
                html = c.Html,
                text = c.Texto,
            }),
        };
        msg.Headers.TryAddWithoutValidation("Authorization", "Bearer " + _o.ApiKey);
        return msg;
    }
}

/// <summary>Proveedor «Ninguno»: nunca se invoca (el trabajo ni arranca), pero el puerto queda resuelto.</summary>
public sealed class EnviadorNulo : IEnviadorDeCorreo
{
    public Task<ResultadoDeEnvio> EnviarAsync(CorreoSaliente correo, CancellationToken ct = default) =>
        Task.FromResult(new ResultadoDeEnvio(ResultadoEnvio.Reintentable, "sin proveedor configurado"));
}
