using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace Zass.Api.Configuracion;

/// <summary>
/// N3 (auditoría del 22-nov-2026): los 50 cupos fundador se podían acaparar con cuentas falsas.
/// Decisión de Pablo: captcha Cloudflare Turnstile en el registro (API-08) y al tomar un cupo
/// fundador (API-69), más la tarjeta para confirmar el cupo (ver ServicioSuscripciones).
///
/// <para>
/// El navegador resuelve el widget y manda el token en la cabecera <see cref="Cabecera"/>. La API
/// se lo pregunta a Cloudflare (<see cref="UrlVerificacion"/>) con el secreto de
/// <see cref="ClaveSecreto"/> y además comprueba que el token salió de NUESTRO sitio (hostname) y
/// de ESTA pantalla (acción): un token resuelto en otro sitio que use la misma clave pública, o
/// en el registro, no sirve para tomar un cupo.
/// </para>
///
/// Reglas (las mismas que el secreto de origen, P-11):
/// <list type="number">
/// <item>Sin secreto configurado NO se exige nada: desarrollo y CI siguen como antes, y producción
/// no se cae el día que se despliega esto, antes de que Pablo cree el widget. En producción lo
/// advierte el registro (<see cref="Advertencia"/>).</item>
/// <item>Con secreto: sin token, con token rechazado, de otro hostname o de otra acción → 400.
/// Si Cloudflare no contesta, también 400 (se falla cerrado: un captcha que se salta cuando
/// Cloudflare tarda no protege nada).</item>
/// <item>Las claves de PRUEBA de Cloudflare (<see cref="EsSecretoDePrueba"/>) contestan siempre
/// hostname <c>example.com</c> y sin acción; con ellas se aceptan así. En producción una clave de
/// prueba es un error de arranque: dejaría pasar a cualquiera.</item>
/// <item>El secreto NUNCA se escribe en un registro ni en un mensaje.</item>
/// </list>
/// </summary>
public static class Turnstile
{
    public const string ClaveSecreto = "Zass:Turnstile:Secreto";
    public const string ClaveHostnames = "Zass:Turnstile:Hostnames";
    public const string Cabecera = "X-Turnstile-Token";
    public const string UrlVerificacion = "https://challenges.cloudflare.com/turnstile/v0/siteverify";

    /// <summary>La acción del widget de crear cuenta (frontend: <c>action="registro"</c>).</summary>
    public const string AccionRegistro = "registro";

    /// <summary>La acción del widget de «Quiero mi cupo» (frontend: <c>action="cupo_fundador"</c>).</summary>
    public const string AccionCupoFundador = "cupo_fundador";

    /// <summary>Cloudflare documenta tokens de hasta 2048 caracteres; más largo ni se envía.</summary>
    public const int LargoMaximoToken = 2048;

    public static readonly string[] HostnamesPorDefecto = { "zass.cl", "www.zass.cl" };

    /// <summary>Lo que contestan las claves de prueba de Cloudflare en lugar del hostname real.</summary>
    public const string HostnameDePrueba = "example.com";

    // https://developers.cloudflare.com/turnstile/troubleshooting/testing/
    private static readonly HashSet<string> SecretosDePrueba = new(StringComparer.Ordinal)
    {
        "1x0000000000000000000000000000000AA", // siempre pasa
        "2x0000000000000000000000000000000AA", // siempre falla
        "3x0000000000000000000000000000000AA", // «token ya usado»
    };

    public static string? Leer(IConfiguration configuracion)
    {
        var valor = configuracion[ClaveSecreto];
        return string.IsNullOrWhiteSpace(valor) ? null : valor.Trim();
    }

    public static bool EsSecretoDePrueba(string secreto) => SecretosDePrueba.Contains(secreto);

    /// <summary>Los hostnames aceptados: <see cref="ClaveHostnames"/> separados por coma, o zass.cl y www.zass.cl.</summary>
    public static IReadOnlyList<string> Hostnames(IConfiguration configuracion)
    {
        var lista = (configuracion[ClaveHostnames] ?? "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(h => h.ToLowerInvariant())
            .ToList();
        return lista.Count > 0 ? lista : HostnamesPorDefecto;
    }

    /// <summary>
    /// Para la validación de arranque. Nunca cita el valor.
    /// <paramref name="exigirSecreto"/> es SOLO para el entorno Production (22-nov-2026): sin el
    /// secreto el captcha queda apagado sin que nadie se entere, y eso no se acepta en producción.
    /// Staging y desarrollo siguen como antes: sin secreto no se exige el captcha y se advierte.
    /// </summary>
    public static IEnumerable<string> Problemas(
        IConfiguration configuracion, bool esProductivo, bool exigirSecreto = false)
    {
        var secreto = Leer(configuracion);
        if (secreto is null)
        {
            if (exigirSecreto)
                yield return $"Falta {ClaveSecreto} (variable Zass__Turnstile__Secreto): sin el secreto de "
                             + "Cloudflare Turnstile el registro y los cupos fundador quedarían SIN captcha.";
            yield break;
        }
        if (esProductivo && EsSecretoDePrueba(secreto))
            yield return $"{ClaveSecreto} es una clave de PRUEBA de Cloudflare: en producción dejaría "
                         + "pasar a cualquiera. Pon la clave secreta del widget de zass.cl.";
    }

    public static string? Advertencia(IConfiguration configuracion, bool esProductivo) =>
        esProductivo && Leer(configuracion) is null
            ? $"{ClaveSecreto} no está configurado: el registro y los cupos fundador NO piden captcha "
              + "(cualquiera puede crear cuentas falsas en serie). Ver los pasos del PR [N3]."
            : null;

    /// <summary>Exige el captcha de <paramref name="accion"/> en este endpoint.</summary>
    public static TBuilder ConCaptcha<TBuilder>(this TBuilder builder, string accion)
        where TBuilder : IEndpointConventionBuilder =>
        builder.AddEndpointFilter(new FiltroCaptcha(accion)).WithMetadata(new CaptchaExigido(accion));

    public static IServiceCollection AgregarTurnstile(this IServiceCollection servicios)
    {
        servicios.AddHttpClient<IVerificadorCaptcha, VerificadorTurnstile>(c =>
            c.Timeout = TimeSpan.FromSeconds(10));
        return servicios;
    }
}

/// <summary>Marca en el endpoint: las pruebas leen que API-08 y API-69 piden captcha, y de qué acción.</summary>
public sealed record CaptchaExigido(string Accion);

/// <summary>El veredicto: aceptado, o por qué no (el motivo va al registro, no a la persona).</summary>
public sealed record VeredictoCaptcha(bool Aceptado, string? Motivo = null)
{
    public static readonly VeredictoCaptcha SinExigir = new(true, "sin secreto configurado");
}

public interface IVerificadorCaptcha
{
    Task<VeredictoCaptcha> VerificarAsync(string? token, string accion, string? ip, CancellationToken ct);
}

public sealed class VerificadorTurnstile : IVerificadorCaptcha
{
    private readonly HttpClient _http;
    private readonly IConfiguration _configuracion;
    private readonly ILogger<VerificadorTurnstile> _registro;

    public VerificadorTurnstile(
        HttpClient http, IConfiguration configuracion, ILogger<VerificadorTurnstile> registro)
    {
        _http = http;
        _configuracion = configuracion;
        _registro = registro;
    }

    public async Task<VeredictoCaptcha> VerificarAsync(
        string? token, string accion, string? ip, CancellationToken ct)
    {
        var secreto = Turnstile.Leer(_configuracion);
        if (secreto is null) return VeredictoCaptcha.SinExigir;

        if (string.IsNullOrWhiteSpace(token)) return new(false, "sin token");
        if (token.Length > Turnstile.LargoMaximoToken) return new(false, "token demasiado largo");

        var campos = new Dictionary<string, string> { ["secret"] = secreto, ["response"] = token };
        if (!string.IsNullOrWhiteSpace(ip)) campos["remoteip"] = ip;

        RespuestaSiteverify? r;
        try
        {
            using var respuesta = await _http.PostAsync(
                Turnstile.UrlVerificacion, new FormUrlEncodedContent(campos), ct);
            if (!respuesta.IsSuccessStatusCode)
                return new(false, $"siteverify respondió {(int)respuesta.StatusCode}");
            r = await respuesta.Content.ReadFromJsonAsync<RespuestaSiteverify>(ct);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException
                                      or System.Text.Json.JsonException)
        {
            // Falla cerrado. El registro dice el tipo, no el secreto.
            _registro.LogWarning("Turnstile: no se pudo consultar siteverify ({Tipo})", e.GetType().Name);
            return new(false, "siteverify no respondió");
        }

        if (r is null || !r.Exito)
            return new(false, "rechazado: " + string.Join(",", r?.Errores ?? Array.Empty<string>()));

        var dePrueba = Turnstile.EsSecretoDePrueba(secreto);
        var hostname = (r.Hostname ?? "").ToLowerInvariant();
        var hostnameValido = Turnstile.Hostnames(_configuracion).Contains(hostname)
                             || (dePrueba && hostname == Turnstile.HostnameDePrueba);
        if (!hostnameValido) return new(false, $"hostname ajeno: {hostname}");

        // Las claves de prueba no devuelven acción; las reales siempre.
        if (!(dePrueba && string.IsNullOrEmpty(r.Accion)) && !string.Equals(r.Accion, accion, StringComparison.Ordinal))
            return new(false, $"acción equivocada: {r.Accion}");

        return new(true);
    }

    private sealed class RespuestaSiteverify
    {
        [JsonPropertyName("success")] public bool Exito { get; set; }
        [JsonPropertyName("hostname")] public string? Hostname { get; set; }
        [JsonPropertyName("action")] public string? Accion { get; set; }
        [JsonPropertyName("error-codes")] public string[]? Errores { get; set; }
    }
}

/// <summary>
/// El filtro de endpoint. Va DESPUÉS de la validación del cuerpo y de la idempotencia: un
/// formulario mal llenado no gasta el token (Turnstile solo acepta cada token una vez).
/// </summary>
public sealed class FiltroCaptcha : IEndpointFilter
{
    public const string Mensaje =
        "No pudimos comprobar que eres una persona. Recarga la página, resuelve la verificación "
        + "e inténtalo de nuevo.";

    private readonly string _accion;

    public FiltroCaptcha(string accion) => _accion = accion;

    public async ValueTask<object?> InvokeAsync(
        EndpointFilterInvocationContext contexto, EndpointFilterDelegate siguiente)
    {
        var http = contexto.HttpContext;
        var verificador = http.RequestServices.GetRequiredService<IVerificadorCaptcha>();
        var valores = http.Request.Headers[Turnstile.Cabecera];
        var token = valores.Count == 1 ? valores[0] : null;

        var veredicto = await verificador.VerificarAsync(
            token, _accion, http.Connection.RemoteIpAddress?.ToString(), http.RequestAborted);
        if (veredicto.Aceptado) return await siguiente(contexto);

        http.RequestServices.GetRequiredService<ILogger<FiltroCaptcha>>()
            .LogWarning("Captcha rechazado en {Accion}: {Motivo}", _accion, veredicto.Motivo);
        return Results.Problem(
            title: "Verificación pendiente",
            detail: Mensaje,
            statusCode: StatusCodes.Status400BadRequest);
    }
}
