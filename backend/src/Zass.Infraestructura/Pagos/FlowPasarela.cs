using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Web;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Zass.Aplicacion.Puertos;
using Zass.Dominio.Comun;
using Zass.Infraestructura.Configuracion;

namespace Zass.Infraestructura.Pagos;

/// <summary>
/// Adaptador de Flow.cl (https://developers.flow.cl/api) para cobrar e inscribir tarjetas.
/// Usa el sandbox por defecto (https://sandbox.flow.cl/api) y se cambia a produccion
/// con Zass:Flow:UrlBase.
///
/// <para>
/// La firma es HMAC-SHA256: los parametros (menos "s") se ordenan alfabeticamente por
/// nombre, se concatena nombre+valor sin separadores, se firma con la secretKey, y el
/// hex en minusculas va en el parametro "s".
/// </para>
///
/// <para>
/// El aviso a urlConfirmation es un POST form con solo "token". Lo que lo hace confiable
/// es que NOSOTROS consultamos getStatus/getRegisterStatus con nuestra firma. NUNCA se
/// aprueba algo solo por recibir un token.
/// </para>
/// </summary>
public sealed class FlowPasarela : IPasarelaDePagos
{
    public const string NombreDeLaPasarela = "flow";

    public string Nombre => NombreDeLaPasarela;

    /// <summary>
    /// Flow responde en camelCase («flowOrder», «customerId»). Sin las opciones web, la
    /// deserializacion distingue mayusculas y TODOS los campos llegaban vacios sin dar error.
    /// </summary>
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _http;
    private readonly IOptions<OpcionesZass> _opciones;
    private readonly ILogger<FlowPasarela> _log;
    private readonly string _urlBase;
    private readonly string _urlAvisos;
    private readonly string _urlVuelta;

    public FlowPasarela(
        HttpClient http,
        IOptions<OpcionesZass> opciones,
        ILogger<FlowPasarela> log)
    {
        _http = http;
        _opciones = opciones;
        _log = log;

        var flow = opciones.Value.Flow;
        _urlBase = (flow?.UrlBase ?? "https://sandbox.flow.cl/api").TrimEnd('/');
        _urlAvisos = flow?.UrlAvisos
            ?? throw new OptionsValidationException(
                OpcionesZass.Seccion, typeof(OpcionesZass),
                new[] { "Falta Zass:Flow:UrlAvisos (la URL PUBLICA que recibe el aviso)." });
        _urlVuelta = UrlDeLaVuelta(flow!.UrlVuelta, _urlAvisos);
    }

    /// <summary>
    /// A donde vuelve el NAVEGADOR (API-75), que no es a donde avisa el servidor (API-70). Si no
    /// se configura aparte, sale del aviso: «…/api/pagos/aviso» → «…/api/pagos/vuelta».
    /// </summary>
    internal static string UrlDeLaVuelta(string? configurada, string urlAvisos)
    {
        if (!string.IsNullOrWhiteSpace(configurada)) return configurada.TrimEnd('/');
        var aviso = urlAvisos.TrimEnd('/');
        if (aviso.EndsWith("/aviso", StringComparison.Ordinal))
            return aviso[..^"/aviso".Length] + "/vuelta";
        throw new OptionsValidationException(
            OpcionesZass.Seccion, typeof(OpcionesZass),
            new[] { "Zass:Flow:UrlAvisos no termina en /aviso: pon Zass:Flow:UrlVuelta a mano." });
    }

    /// <summary>
    /// Firma un conjunto de parametros con HMAC-SHA256. Los parametros se ordenan
    /// alfabeticamente por nombre (ordinal), se concatena nombre+valor sin separadores,
    /// y el resultado se firma con la secretKey.
    /// </summary>
    private string Firmar(IDictionary<string, string> parametros)
    {
        var secretKey = _opciones.Value.Flow?.SecretKey
            ?? throw new OptionsValidationException(
                OpcionesZass.Seccion, typeof(OpcionesZass),
                new[] { "Falta Zass:Flow:SecretKey." });

        var ordenados = parametros
            .Where(p => p.Key != "s")
            .Select(p => $"{p.Key}{p.Value}")
            .Aggregate("", (a, b) => a + b);

        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secretKey));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(ordenados));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    /// <summary>
    /// Llama a un endpoint GET de Flow y devuelve el JSON deserializado.
    /// Los errores de red o 5xx se traducen a Fallas.ReglaDeNegocio SIN exponer la apiKey.
    /// </summary>
    private async Task<Resultado<T>> GetAsync<T>(string servicio, Dictionary<string, string> queryParams, CancellationToken ct)
    {
        var apiKey = _opciones.Value.Flow?.ApiKey
            ?? throw new OptionsValidationException(
                OpcionesZass.Seccion, typeof(OpcionesZass),
                new[] { "Falta Zass:Flow:ApiKey." });

        queryParams["apiKey"] = apiKey;
        queryParams["s"] = Firmar(queryParams);

        var query = string.Join("&", queryParams.Select(p => $"{HttpUtility.UrlEncode(p.Key)}={HttpUtility.UrlEncode(p.Value)}"));
        var url = $"{_urlBase}/{servicio}?{query}";

        HttpResponseMessage response;
        try
        {
            response = await _http.GetAsync(url, ct);
        }
        catch (Exception e) when (e is HttpRequestException
                                       || (e is TaskCanceledException && !ct.IsCancellationRequested))
        {
            // El HttpClient avisa el timeout como TaskCanceledException. Sin la cancelacion
            // pedida por quien llama, es "Flow no contesto": se trata igual que un error de red.
            _log.LogError(e, "Flow no contesto ({Servicio}).", servicio);
            return Resultado.Falla<T>(Fallas.ReglaDeNegocio, "No se pudo comunicar con la pasarela de pagos.");
        }

        if (!response.IsSuccessStatusCode)
        {
            _log.LogWarning("Flow respondio {Status} al llamar a {Servicio}", response.StatusCode, servicio);
            return Resultado.Falla<T>(Fallas.ReglaDeNegocio, "La pasarela de pagos no respondio correctamente.");
        }

        var json = await response.Content.ReadAsStringAsync(ct);
        try
        {
            var resultado = JsonSerializer.Deserialize<T>(json);
            return Resultado.Ok(resultado!);
        }
        catch (JsonException e)
        {
            _log.LogError(e, "Flow respondio un JSON que no se pudo leer ({Servicio}).", servicio);
            return Resultado.Falla<T>(Fallas.ReglaDeNegocio, "La pasarela respondio un formato no valido.");
        }
    }

    /// <summary>
    /// Llama a un endpoint POST form-urlencoded de Flow y devuelve el JSON deserializado.
    /// </summary>
    private async Task<Resultado<T>> PostAsync<T>(string servicio, Dictionary<string, string> formData, CancellationToken ct)
    {
        var apiKey = _opciones.Value.Flow?.ApiKey
            ?? throw new OptionsValidationException(
                OpcionesZass.Seccion, typeof(OpcionesZass),
                new[] { "Falta Zass:Flow:ApiKey." });

        formData["apiKey"] = apiKey;
        formData["s"] = Firmar(formData);

        var content = new FormUrlEncodedContent(formData);
        var url = $"{_urlBase}/{servicio}";

        HttpResponseMessage response;
        try
        {
            response = await _http.PostAsync(url, content, ct);
        }
        catch (Exception e) when (e is HttpRequestException
                                       || (e is TaskCanceledException && !ct.IsCancellationRequested))
        {
            // El HttpClient avisa el timeout como TaskCanceledException. Sin la cancelacion
            // pedida por quien llama, es "Flow no contesto": se trata igual que un error de red.
            _log.LogError(e, "Flow no contesto ({Servicio}).", servicio);
            return Resultado.Falla<T>(Fallas.ReglaDeNegocio, "No se pudo comunicar con la pasarela de pagos.");
        }

        if (!response.IsSuccessStatusCode)
        {
            _log.LogWarning("Flow respondio {Status} al llamar a {Servicio}", response.StatusCode, servicio);
            return Resultado.Falla<T>(Fallas.ReglaDeNegocio, "La pasarela de pagos no respondio correctamente.");
        }

        var json = await response.Content.ReadAsStringAsync(ct);
        try
        {
            var resultado = JsonSerializer.Deserialize<T>(json);
            return Resultado.Ok(resultado!);
        }
        catch (JsonException e)
        {
            _log.LogError(e, "Flow respondio un JSON que no se pudo leer ({Servicio}).", servicio);
            return Resultado.Falla<T>(Fallas.ReglaDeNegocio, "La pasarela respondio un formato no valido.");
        }
    }

    public Task<Resultado<CobroIniciado>> IniciarCobroAsync(
        SolicitudDeCobro solicitud, CancellationToken ct = default)
    {
        if (solicitud.Monto <= 0)
            return Task.FromResult(Resultado.Falla<CobroIniciado>(
                Fallas.Validacion, "No se puede cobrar un monto de cero o negativo."));

        if (string.IsNullOrWhiteSpace(solicitud.CorreoDelPagador))
            return Task.FromResult(Resultado.Falla<CobroIniciado>(
                Fallas.Validacion, "Falta el correo de quien paga (Flow lo exige)."));

        return IniciarCobroInternoAsync(solicitud, ct);
    }

    private async Task<Resultado<CobroIniciado>> IniciarCobroInternoAsync(
        SolicitudDeCobro solicitud, CancellationToken ct)
    {
        var apiKey = _opciones.Value.Flow?.ApiKey
            ?? throw new OptionsValidationException(
                OpcionesZass.Seccion, typeof(OpcionesZass),
                new[] { "Falta Zass:Flow:ApiKey." });


        var formData = new Dictionary<string, string>
        {
            ["commerceOrder"] = solicitud.IntentoDePagoId.ToString("D"),
            ["subject"] = solicitud.Descripcion,
            ["amount"] = solicitud.Monto.ToString(),
            ["currency"] = solicitud.Moneda,
            ["email"] = solicitud.CorreoDelPagador!,
            ["urlConfirmation"] = $"{_urlAvisos}?flow=pago",
            // La vuelta pasa por la API (API-75), que confirma y despues redirige a /planes/listo:
            // Flow devuelve al navegador con un POST, y el sitio estatico no puede recibirlo.
            ["urlReturn"] = $"{_urlVuelta}?flow=pago",
        };

        var resultado = await PostAsync<PaymentCreateResponse>("payment/create", formData, ct);
        if (!resultado.Exito || resultado.Valor is null)
            return Resultado.Falla<CobroIniciado>(resultado.Codigo!, resultado.Mensaje!);

        var response = resultado.Valor;
        var urlParaPagar = $"{response.Url}?token={response.Token}";

        return Resultado.Ok(new CobroIniciado(response.FlowOrder.ToString(), urlParaPagar));
    }

    public Task<Resultado<CobroIniciado>> IniciarInscripcionAsync(
        SolicitudDeInscripcion solicitud, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(solicitud.CorreoDelPagador))
            return Task.FromResult(Resultado.Falla<CobroIniciado>(
                Fallas.Validacion, "Falta el correo de quien paga (Flow lo exige)."));

        return IniciarInscripcionInternoAsync(solicitud, ct);
    }

    private async Task<Resultado<CobroIniciado>> IniciarInscripcionInternoAsync(
        SolicitudDeInscripcion solicitud, CancellationToken ct)
    {
        var apiKey = _opciones.Value.Flow?.ApiKey
            ?? throw new OptionsValidationException(
                OpcionesZass.Seccion, typeof(OpcionesZass),
                new[] { "Falta Zass:Flow:ApiKey." });

        // Primero customer/create
        var createData = new Dictionary<string, string>
        {
            ["name"] = "Prestador ZASS!",
            ["email"] = solicitud.CorreoDelPagador!,
            ["externalId"] = solicitud.IntentoDePagoId.ToString("D"),
        };

        var createResult = await PostAsync<CustomerCreateResponse>("customer/create", createData, ct);
        if (!createResult.Exito || createResult.Valor is null)
            return Resultado.Falla<CobroIniciado>(createResult.Codigo!, createResult.Mensaje!);

        var customerId = createResult.Valor.CustomerId;
        if (string.IsNullOrWhiteSpace(customerId))
            return Resultado.Falla<CobroIniciado>(Fallas.ReglaDeNegocio,
                "La pasarela no devolvio el cliente creado.");

        // Luego customer/register
        var registerData = new Dictionary<string, string>
        {
            ["customerId"] = customerId,
            // customer/register NO avisa al servidor: esta vuelta (API-75) es la unica
            // confirmacion de la inscripcion, y confirma consultando getRegisterStatus.
            ["url_return"] = $"{_urlVuelta}?flow=inscripcion",
        };

        var registerResult = await PostAsync<CustomerRegisterResponse>("customer/register", registerData, ct);
        if (!registerResult.Exito || registerResult.Valor is null)
            return Resultado.Falla<CobroIniciado>(registerResult.Codigo!, registerResult.Mensaje!);

        var urlParaPagar = $"{registerResult.Valor.Url}?token={registerResult.Valor.Token}";
        return Resultado.Ok(new CobroIniciado(registerResult.Valor.Token, urlParaPagar));
    }

    public async Task<Resultado<CobroResuelto>> CobrarAsync(
        SolicitudDeCobro solicitud, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(solicitud.MedioInscrito))
            return Resultado.Falla<CobroResuelto>(
                Fallas.ReglaDeNegocio,
                "No hay un medio de pago inscrito contra el que cobrar.");

        var apiKey = _opciones.Value.Flow?.ApiKey
            ?? throw new OptionsValidationException(
                OpcionesZass.Seccion, typeof(OpcionesZass),
                new[] { "Falta Zass:Flow:ApiKey." });

        var formData = new Dictionary<string, string>
        {
            ["customerId"] = solicitud.MedioInscrito,
            ["commerceOrder"] = solicitud.IntentoDePagoId.ToString("D"),
            ["amount"] = solicitud.Monto.ToString(),
            ["subject"] = solicitud.Descripcion,
        };

        var resultado = await PostAsync<CustomerChargeResponse>("customer/charge", formData, ct);
        if (!resultado.Exito || resultado.Valor is null)
            return Resultado.Falla<CobroResuelto>(resultado.Codigo!, resultado.Mensaje!);

        var response = resultado.Valor;
        var aprobado = response.Status != 3;

        return Resultado.Ok(new CobroResuelto(
            IntentoDePagoId: solicitud.IntentoDePagoId,
            ReferenciaPasarela: response.FlowOrder.ToString(),
            Aprobado: aprobado,
            MontoConfirmado: solicitud.Monto,
            MotivoFallo: aprobado ? null : "Flow rechazo el cobro con la tarjeta inscrita."));
    }

    public async Task<Resultado<CobroResuelto>> LeerConfirmacionAsync(
        AvisoDePasarela aviso, CancellationToken ct = default)
    {
        var token = aviso.Campo("token");
        var flow = aviso.Campo("flow");

        if (string.IsNullOrWhiteSpace(token))
            return Resultado.Falla<CobroResuelto>(
                Fallas.Validacion, "El aviso no trae un token.");

        if (string.IsNullOrWhiteSpace(flow))
            return Resultado.Falla<CobroResuelto>(
                Fallas.Validacion, "El aviso no indica si es 'pago' o 'inscripcion'.");

        if (flow == "pago")
            return await LeerConfirmacionDePagoAsync(token, ct);
        
        if (flow == "inscripcion")
            return await LeerConfirmacionDeInscripcionAsync(token, ct);

        return Resultado.Falla<CobroResuelto>(
            Fallas.Validacion, $"El valor de flow '{flow}' no es conocido (esperado 'pago' o 'inscripcion').");
    }

    private async Task<Resultado<CobroResuelto>> LeerConfirmacionDePagoAsync(string token, CancellationToken ct)
    {
        var resultado = await GetAsync<PaymentGetStatusResponse>("payment/getStatus", new Dictionary<string, string> { ["token"] = token }, ct);
        if (!resultado.Exito || resultado.Valor is null)
            return Resultado.Falla<CobroResuelto>(resultado.Codigo!, resultado.Mensaje!);

        var response = resultado.Valor;

        if (!Guid.TryParse(response.CommerceOrder, out var intentoId) && false)
            return Resultado.Falla<CobroResuelto>(
                Fallas.Validacion,
                $"El commerceOrder '{response.CommerceOrder}' no es un identificador valido.");

        var aprobado = response.Status != 3;
        var montoConfirmado = (int)Math.Round(response.Amount);

        // Si el monto tiene decimales distintos de ,00, falla
        if (Math.Abs(response.Amount - montoConfirmado) > 0.001m)
            return Resultado.Falla<CobroResuelto>(
                Fallas.Validacion,
                $"El monto confirmado ({response.Amount}) tiene decimales no enteros.");

        return Resultado.Ok(new CobroResuelto(
            IntentoDePagoId: intentoId,
            ReferenciaPasarela: response.FlowOrder.ToString(),
            Aprobado: aprobado,
            MontoConfirmado: aprobado ? montoConfirmado : null,
            MotivoFallo: aprobado ? null : $"Flow reporto estado {response.Status} (no pagado)."));
    }

    private async Task<Resultado<CobroResuelto>> LeerConfirmacionDeInscripcionAsync(string token, CancellationToken ct)
    {
        var resultado = await GetAsync<CustomerGetRegisterStatusResponse>("customer/getRegisterStatus", new Dictionary<string, string> { ["token"] = token }, ct);
        if (!resultado.Exito || resultado.Valor is null)
            return Resultado.Falla<CobroResuelto>(resultado.Codigo!, resultado.Mensaje!);

        var response = resultado.Valor;
        var aprobado = response.Status.ToString() == "1";

        // Para obtener el IntentoDePagoId, llamamos customer/get con el customerId
        var getResultado = await GetAsync<CustomerGetResponse>("customer/get", new Dictionary<string, string> { ["customerId"] = response.CustomerId }, ct);
        if (!getResultado.Exito || getResultado.Valor is null)
            return Resultado.Falla<CobroResuelto>(getResultado.Codigo!, getResultado.Mensaje!);

        if (!Guid.TryParse(getResultado.Valor.ExternalId, out var intentoId))
            return Resultado.Falla<CobroResuelto>(
                Fallas.Validacion,
                $"El externalId '{getResultado.Valor.ExternalId}' no es un identificador valido.");

        return Resultado.Ok(new CobroResuelto(
            IntentoDePagoId: intentoId,
            ReferenciaPasarela: response.CustomerId,
            Aprobado: aprobado,
            MontoConfirmado: 0,
            MotivoFallo: aprobado ? null : "Flow reporto que la tarjeta no fue registrada."));
    }

    // ----------------------------------------------------------------- Respuestas de Flow

    private sealed class PaymentCreateResponse
    {
        public string Url { get; set; } = "";
        public string Token { get; set; } = "";
        public long FlowOrder { get; set; }
    }

    private sealed class PaymentGetStatusResponse
    {
        public long FlowOrder { get; set; }
        public string CommerceOrder { get; set; } = "";
        public int Status { get; set; }
        public decimal Amount { get; set; }
        public string Currency { get; set; } = "";
    }

    private sealed class CustomerCreateResponse
    {
        /// <summary>Texto en Flow («cus_onoolldvec»), no un numero.</summary>
        public string CustomerId { get; set; } = "";
    }

    private sealed class CustomerRegisterResponse
    {
        public string Url { get; set; } = "";
        public string Token { get; set; } = "";
    }

    private sealed class CustomerGetRegisterStatusResponse
    {
        /// <summary>La documentacion lo muestra como texto ("1"); se acepta tambien como numero.</summary>
        public JsonElement Status { get; set; }
        public string CustomerId { get; set; } = "";
        public string? CreditCardType { get; set; }
        public string? Last4CardDigits { get; set; }
    }

    private sealed class CustomerGetResponse
    {
        public string ExternalId { get; set; } = "";
    }

    private sealed class CustomerChargeResponse
    {
        public long FlowOrder { get; set; }
        public string CommerceOrder { get; set; } = "";
        public int Status { get; set; }
        public decimal Amount { get; set; }
        public string Currency { get; set; } = "";
    }
}
