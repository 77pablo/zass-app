using System.Net;
using System.Text;
using System.Text.Json;
using System.Web;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Zass.Aplicacion.Puertos;
using Zass.Dominio.Comun;
using Zass.Infraestructura.Configuracion;
using Zass.Infraestructura.Pagos;

namespace Zass.Integracion.Pruebas.Pagos;

/// <summary>
/// Pruebas de FlowPasarela con un HttpMessageHandler falso que devuelve respuestas fijas
/// y guarda cada peticion. NO usan la base ni la red.
/// </summary>
public class FlowPasarelaPruebas
{
    private const string ApiKeyPrueba = "test-api-key-12345";
    private const string SecretKeyPrueba = "secreto-de-prueba";
    private const string UrlBasePrueba = "https://sandbox.flow.cl/api";
    private const string UrlAvisosPrueba = "https://api.zass.cl/api/pagos/aviso";
    private const string UrlPublicaPrueba = "https://zass.cl";

    /// <summary>
    /// Handler falso que guarda las peticiones y devuelve respuestas fijas.
    /// </summary>
    private sealed class HandlerFalso : HttpMessageHandler
    {
        public List<HttpRequestMessage> Peticiones { get; } = new();
        public Func<HttpRequestMessage, Task<HttpResponseMessage>>? Responder { get; set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Peticiones.Add(request);
            if (Responder is not null)
                return await Responder(request);

            // Por defecto, respuesta vacia
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{}", Encoding.UTF8, "application/json"),
            };
        }
    }

    private static FlowPasarela CrearPasarela(HandlerFalso handler, ILogger<FlowPasarela>? log = null)
    {
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri(UrlBasePrueba) };
        var opciones = Options.Create(new OpcionesZass
        {
            CadenaConexion = "Host=no-se-conecta;Database=no-existe",
            ClaveDireccionesBase64 = Convert.ToBase64String(Enumerable.Range(0, 32).Select(i => (byte)i).ToArray()),
            UrlPublica = UrlPublicaPrueba,
            SecretoAvisoPagos = Convert.ToBase64String(Enumerable.Range(0, 32).Select(i => (byte)i).ToArray()),
            Flow = new OpcionesFlow
            {
                ApiKey = ApiKeyPrueba,
                SecretKey = SecretKeyPrueba,
                UrlBase = UrlBasePrueba,
                UrlAvisos = UrlAvisosPrueba,
            },
        });

        return new FlowPasarela(httpClient, opciones, log ?? NullLogger<FlowPasarela>.Instance);
    }

    /// <summary>
    /// Calcula la firma HMAC-SHA256 de unos parametros, igual que FlowPasarela.
    /// </summary>
    private static string CalcularFirma(string secretKey, IDictionary<string, string> parametros)
    {
        var ordenados = parametros
            .Where(p => p.Key != "s")
            .OrderBy(p => p.Key, StringComparer.Ordinal)
            .Select(p => $"{p.Key}{p.Value}")
            .Aggregate("", (a, b) => a + b);

        using var hmac = new System.Security.Cryptography.HMACSHA256(Encoding.UTF8.GetBytes(secretKey));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(ordenados));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    // ========================================== P1: Vector de prueba de firma

    /// <summary>
    /// P1: La firma del vector de prueba da exactamente el hex esperado.
    /// Vector: clave "secreto-de-prueba", parametros amount=5000, apiKey=XXXX-XXXX-XXXX, currency=CLP
    /// Cadena: "amount5000apiKeyXXXX-XXXX-XXXXcurrencyCLP"
    /// s = 23a2e1765aa31ff2e0e242d8eb5650ae3302d8089159c3a5c968faa14b3578c8
    /// </summary>
    [Fact]
    public void La_firma_del_vector_de_prueba_da_el_hex_esperado()
    {
        var parametros = new Dictionary<string, string>
        {
            ["amount"] = "5000",
            ["apiKey"] = "XXXX-XXXX-XXXX",
            ["currency"] = "CLP",
        };

        var firma = CalcularFirma(SecretKeyPrueba, parametros);

        Assert.Equal("23a2e1765aa31ff2e0e242d8eb5650ae3302d8089159c3a5c968faa14b3578c8", firma);
    }

    // ========================================== P2: payment/create

    /// <summary>
    /// P2: payment/create manda los 8 parametros, el "s" correcto, POST form-urlencoded,
    /// y devuelve url?token=... y flowOrder como referencia.
    /// </summary>
    [Fact]
    public async Task IniciarCobroAsync_manda_los_8_parametros_y_devuelve_url_y_flowOrder()
    {
        var handler = new HandlerFalso();
        var pasarela = CrearPasarela(handler);

        handler.Responder = _ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                JsonSerializer.Serialize(new { url = "https://sandbox.flow.cl/pagar", token = "abc123", flowOrder = 99999 }),
                Encoding.UTF8,
                "application/json"),
        });

        var intento = Guid.NewGuid();
        var resultado = await pasarela.IniciarCobroAsync(new SolicitudDeCobro(
            IntentoDePagoId: intento,
            Monto: 5990,
            Moneda: "CLP",
            Descripcion: "ZASS! Plan Mensual - 1 mes")
        {
            CorreoDelPagador = "prestador@zass.cl",
        });

        Assert.True(resultado.Exito, resultado.Mensaje);
        Assert.NotNull(resultado.Valor);
        Assert.Equal("99999", resultado.Valor!.ReferenciaPasarela);
        Assert.Equal("https://sandbox.flow.cl/pagar?token=abc123", resultado.Valor.UrlParaPagar);

        // Verificar la peticion
        Assert.Single(handler.Peticiones);
        var peticion = handler.Peticiones[0];
        Assert.Equal(HttpMethod.Post, peticion.Method);
        Assert.Equal("/api/payment/create", peticion.RequestUri!.AbsolutePath);

        // Leer los parametros form-urlencoded
        var contenido = await peticion.Content!.ReadAsStringAsync();
        var formData = HttpUtility.ParseQueryString(contenido);

        // Verificar los 8 parametros (menos "s")
        Assert.Equal(intento.ToString("D"), formData["commerceOrder"]);
        Assert.Equal("ZASS! Plan Mensual - 1 mes", formData["subject"]);
        Assert.Equal("5990", formData["amount"]);
        Assert.Equal("CLP", formData["currency"]);
        Assert.Equal("prestador@zass.cl", formData["email"]);
        Assert.Equal($"{UrlAvisosPrueba}?flow=pago", formData["urlConfirmation"]);
        // La vuelta del navegador pasa por la API (API-75), no por el sitio estatico.
        Assert.Equal("https://api.zass.cl/api/pagos/vuelta?flow=pago", formData["urlReturn"]);
        Assert.Equal(ApiKeyPrueba, formData["apiKey"]);

        // Verificar la firma
        var firmaEsperada = CalcularFirma(SecretKeyPrueba, formData.AllKeys!
            .Where(k => k != "s")
            .ToDictionary(k => k!, k => formData[k]!));
        Assert.Equal(firmaEsperada, formData["s"]);
    }

    // ========================================== P3 y P4: LeerConfirmacion de pago

    /// <summary>
    /// P3: Aviso flow=pago con getStatus status 2 y commerceOrder = un Guid -> Aprobado, ese Guid, monto correcto.
    /// </summary>
    [Fact]
    public async Task LeerConfirmacion_flow_pago_con_status_2_devuelve_Aprobado()
    {
        var handler = new HandlerFalso();
        var pasarela = CrearPasarela(handler);

        var intento = Guid.NewGuid();
        var token = "token-de-prueba";

        handler.Responder = req =>
        {
            // Simular payment/getStatus
            if (req.RequestUri!.AbsolutePath == "/api/payment/getStatus")
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(JsonSerializer.Serialize(new
                    {
                        flowOrder = 88888,
                        commerceOrder = intento.ToString("D"),
                        status = 2,
                        amount = 5990m,
                        currency = "CLP",
                    }), Encoding.UTF8, "application/json"),
                });
            }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        };

        var aviso = new AvisoDePasarela(new Dictionary<string, string>
        {
            ["token"] = token,
            ["flow"] = "pago",
        });

        var resultado = await pasarela.LeerConfirmacionAsync(aviso);

        Assert.True(resultado.Exito, resultado.Mensaje);
        Assert.NotNull(resultado.Valor);
        Assert.True(resultado.Valor!.Aprobado);
        Assert.Equal(intento, resultado.Valor.IntentoDePagoId);
        Assert.Equal(5990, resultado.Valor.MontoConfirmado);
        Assert.Equal("88888", resultado.Valor.ReferenciaPasarela);
    }

    /// <summary>
    /// P4: Lo mismo con status 1, 3 y 4 -> no aprobado (los tres). Y desde el 22-nov-2026 (N3)
    /// el 1 es PENDIENTE, no rechazo: la documentacion oficial (es-openApiFlow.yaml, esquema
    /// PaymentStatus) dice 1 pendiente de pago, 2 pagada, 3 rechazada, 4 anulada. Leer el 1 como
    /// rechazo cerraba el intento y el pago que llegaba despues no se acreditaba.
    /// </summary>
    [Theory]
    [InlineData(1, true)]
    [InlineData(3, false)]
    [InlineData(4, false)]
    public async Task LeerConfirmacion_flow_pago_con_status_no_2_devuelve_No_Aprobado(int status, bool pendiente)
    {
        var handler = new HandlerFalso();
        var pasarela = CrearPasarela(handler);

        var intento = Guid.NewGuid();
        var token = "token-de-prueba";

        handler.Responder = req =>
        {
            if (req.RequestUri!.AbsolutePath == "/api/payment/getStatus")
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(JsonSerializer.Serialize(new
                    {
                        flowOrder = 88888,
                        commerceOrder = intento.ToString("D"),
                        status = status,
                        amount = 5990m,
                        currency = "CLP",
                    }), Encoding.UTF8, "application/json"),
                });
            }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        };

        var aviso = new AvisoDePasarela(new Dictionary<string, string>
        {
            ["token"] = token,
            ["flow"] = "pago",
        });

        var resultado = await pasarela.LeerConfirmacionAsync(aviso);

        Assert.True(resultado.Exito, resultado.Mensaje);
        Assert.False(resultado.Valor!.Aprobado);
        Assert.Equal(intento, resultado.Valor.IntentoDePagoId);
        Assert.Equal(pendiente, resultado.Valor.Pendiente);
    }

    /// <summary>
    /// N3: el estado de Flow en tres desenlaces. Un valor fuera de la lista NO es rechazo ni
    /// pago: cerrar como fallido algo que pudo cobrarse es como se pierde el mes de alguien.
    /// </summary>
    [Theory]
    [InlineData(1, VeredictoDelCobro.Pendiente)]
    [InlineData(2, VeredictoDelCobro.Pagado)]
    [InlineData(3, VeredictoDelCobro.Rechazado)]
    [InlineData(4, VeredictoDelCobro.Rechazado)]
    [InlineData(0, VeredictoDelCobro.Pendiente)]
    [InlineData(9, VeredictoDelCobro.Pendiente)]
    public void El_estado_de_Flow_se_lee_en_tres_desenlaces(int status, VeredictoDelCobro esperado) =>
        Assert.Equal(esperado, FlowPasarela.VeredictoDe(status));

    /// <summary>N3: customer/charge con status 1 tampoco es un rechazo.</summary>
    [Fact]
    public async Task CobrarAsync_con_status_1_devuelve_Pendiente_y_no_rechazo()
    {
        var handler = new HandlerFalso();
        var pasarela = CrearPasarela(handler);
        var intento = Guid.NewGuid();
        handler.Responder = req => Task.FromResult(
            req.RequestUri!.AbsolutePath == "/api/customer/charge"
                ? new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(JsonSerializer.Serialize(new
                    {
                        flowOrder = 77778, commerceOrder = intento.ToString("D"),
                        status = 1, amount = 5990m, currency = "CLP",
                    }), Encoding.UTF8, "application/json"),
                }
                : new HttpResponseMessage(HttpStatusCode.NotFound));

        var resultado = await pasarela.CobrarAsync(new SolicitudDeCobro(intento, 5990, "CLP", "test")
        {
            MedioInscrito = "cust-12345", CorreoDelPagador = "prestador@zass.cl",
        });

        Assert.True(resultado.Exito, resultado.Mensaje);
        Assert.True(resultado.Valor!.Pendiente);
        Assert.False(resultado.Valor.Aprobado);
        Assert.Equal(VeredictoDelCobro.Pendiente, resultado.Valor.Veredicto);
    }

    // ========================================== P5: commerceOrder que no es Guid

    /// <summary>
    /// P5: commerceOrder que no es Guid -> falla.
    /// </summary>
    [Fact]
    public async Task LeerConfirmacion_con_commerceOrder_no_Guid_falla()
    {
        var handler = new HandlerFalso();
        var pasarela = CrearPasarela(handler);

        var token = "token-de-prueba";

        handler.Responder = req =>
        {
            if (req.RequestUri!.AbsolutePath == "/api/payment/getStatus")
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(JsonSerializer.Serialize(new
                    {
                        flowOrder = 88888,
                        commerceOrder = "no-es-un-guid",
                        status = 2,
                        amount = 5990m,
                        currency = "CLP",
                    }), Encoding.UTF8, "application/json"),
                });
            }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        };

        var aviso = new AvisoDePasarela(new Dictionary<string, string>
        {
            ["token"] = token,
            ["flow"] = "pago",
        });

        var resultado = await pasarela.LeerConfirmacionAsync(aviso);

        Assert.False(resultado.Exito);
        Assert.Equal(Fallas.Validacion, resultado.Codigo);
        Assert.Contains("identificador", resultado.Mensaje, StringComparison.OrdinalIgnoreCase);
    }

    // ========================================== P6: Aviso sin token y con flow desconocido

    /// <summary>
    /// P6: Aviso sin token -> falla, y el handler NO recibio ninguna peticion.
    /// </summary>
    [Fact]
    public async Task LeerConfirmacion_sin_token_falla_sin_hacer_peticion()
    {
        var handler = new HandlerFalso();
        var pasarela = CrearPasarela(handler);

        var aviso = new AvisoDePasarela(new Dictionary<string, string>
        {
            ["flow"] = "pago",
            // Sin token
        });

        var resultado = await pasarela.LeerConfirmacionAsync(aviso);

        Assert.False(resultado.Exito);
        Assert.Equal(Fallas.Validacion, resultado.Codigo);
        Assert.Empty(handler.Peticiones);
    }

    /// <summary>
    /// P6: Aviso con flow desconocido -> falla, y el handler NO recibio ninguna peticion.
    /// </summary>
    [Fact]
    public async Task LeerConfirmacion_con_flow_desconocido_falla_sin_hacer_peticion()
    {
        var handler = new HandlerFalso();
        var pasarela = CrearPasarela(handler);

        var aviso = new AvisoDePasarela(new Dictionary<string, string>
        {
            ["token"] = "abc123",
            ["flow"] = "desconocido",
        });

        var resultado = await pasarela.LeerConfirmacionAsync(aviso);

        Assert.False(resultado.Exito);
        Assert.Equal(Fallas.Validacion, resultado.Codigo);
        Assert.Empty(handler.Peticiones);
    }

    // ========================================== P7: Inscripcion (create + register)

    /// <summary>
    /// P7: Inscripcion: customer/create + customer/register en ese orden, externalId = intento,
    /// y la referencia devuelta es el customerId.
    /// </summary>
    [Fact]
    public async Task IniciarInscripcionAsync_hace_create_y_register_y_devuelve_customerId()
    {
        var handler = new HandlerFalso();
        var pasarela = CrearPasarela(handler);

        var intento = Guid.NewGuid();
        var customerId = "cust-12345";

        handler.Responder = req =>
        {
            if (req.RequestUri!.AbsolutePath == "/api/customer/create")
            {
                // Verificar que externalId es el intento
                var contenido = req.Content!.ReadAsStringAsync().Result;
                var formData = HttpUtility.ParseQueryString(contenido);
                Assert.Equal(intento.ToString("D"), formData["externalId"]);

                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(JsonSerializer.Serialize(new { customerId }), Encoding.UTF8, "application/json"),
                });
            }
            if (req.RequestUri!.AbsolutePath == "/api/customer/register")
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(JsonSerializer.Serialize(new { url = "https://sandbox.flow.cl/register", token = "reg-token" }), Encoding.UTF8, "application/json"),
                });
            }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        };

        var resultado = await pasarela.IniciarInscripcionAsync(new SolicitudDeInscripcion(
            IntentoDePagoId: intento,
            Descripcion: "ZASS! Fundador - inscripcion")
        {
            CorreoDelPagador = "prestador@zass.cl",
        });

        Assert.True(resultado.Exito, resultado.Mensaje);
        Assert.NotNull(resultado.Valor);
        Assert.Equal(customerId, resultado.Valor!.ReferenciaPasarela);
        Assert.Equal("https://sandbox.flow.cl/register?token=reg-token", resultado.Valor.UrlParaPagar);

        // Verificar el orden de las peticiones
        Assert.Equal(2, handler.Peticiones.Count);
        Assert.Equal("/api/customer/create", handler.Peticiones[0].RequestUri!.AbsolutePath);
        Assert.Equal("/api/customer/register", handler.Peticiones[1].RequestUri!.AbsolutePath);
    }

    /// <summary>Logger de prueba que guarda cada mensaje ya formateado.</summary>
    private sealed class LoggerQueGuarda<T> : ILogger<T>
    {
        public List<string> Mensajes { get; } = new();
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
            => Mensajes.Add($"{logLevel}: {formatter(state, exception)}");
    }

    private static HttpResponseMessage Json(HttpStatusCode codigo, string cuerpo) =>
        new(codigo) { Content = new StringContent(cuerpo, Encoding.UTF8, "application/json") };

    private const string Error7001 = "{\"code\":7001,\"message\":\"Commerce has not automatic charge contract\"}";

    /// <summary>Responde create OK (customerId), register con 400 y delete segun se pida.</summary>
    private static HandlerFalso HandlerDeInscripcionConRegisterRoto(
        string customerId, HttpStatusCode respuestaDelDelete, List<string> cuerposDelDelete)
    {
        var handler = new HandlerFalso();
        handler.Responder = req =>
        {
            switch (req.RequestUri!.AbsolutePath)
            {
                case "/api/customer/create":
                    return Task.FromResult(Json(HttpStatusCode.OK, JsonSerializer.Serialize(new { customerId })));
                case "/api/customer/register":
                    return Task.FromResult(Json(HttpStatusCode.BadRequest, Error7001));
                case "/api/customer/delete":
                    cuerposDelDelete.Add(req.Content!.ReadAsStringAsync().Result);
                    return Task.FromResult(Json(respuestaDelDelete, "{}"));
                default:
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            }
        };
        return handler;
    }

    private static SolicitudDeInscripcion SolicitudDeInscripcionPrueba() =>
        new(IntentoDePagoId: Guid.NewGuid(), Descripcion: "ZASS! Fundador - inscripcion")
        {
            CorreoDelPagador = "prestador@zass.cl",
        };

    /// <summary>Si register falla, se borra el cliente creado (create, register, delete en orden).</summary>
    [Fact]
    public async Task IniciarInscripcionAsync_si_register_falla_borra_el_cliente_creado()
    {
        var cuerpos = new List<string>();
        var handler = HandlerDeInscripcionConRegisterRoto("cust-777", HttpStatusCode.OK, cuerpos);
        var pasarela = CrearPasarela(handler);

        var resultado = await pasarela.IniciarInscripcionAsync(SolicitudDeInscripcionPrueba());

        Assert.False(resultado.Exito);
        Assert.Equal(3, handler.Peticiones.Count);
        Assert.Equal("/api/customer/create", handler.Peticiones[0].RequestUri!.AbsolutePath);
        Assert.Equal("/api/customer/register", handler.Peticiones[1].RequestUri!.AbsolutePath);
        Assert.Equal("/api/customer/delete", handler.Peticiones[2].RequestUri!.AbsolutePath);
        var form = HttpUtility.ParseQueryString(Assert.Single(cuerpos));
        Assert.Equal("cust-777", form["customerId"]);
    }

    /// <summary>Si create falla no hay cliente que borrar: una sola peticion.</summary>
    [Fact]
    public async Task IniciarInscripcionAsync_si_create_falla_no_llama_a_delete()
    {
        var handler = new HandlerFalso();
        handler.Responder = _ => Task.FromResult(Json(HttpStatusCode.BadRequest, Error7001));
        var pasarela = CrearPasarela(handler);

        var resultado = await pasarela.IniciarInscripcionAsync(SolicitudDeInscripcionPrueba());

        Assert.False(resultado.Exito);
        Assert.Single(handler.Peticiones);
        Assert.Equal("/api/customer/create", handler.Peticiones[0].RequestUri!.AbsolutePath);
    }

    /// <summary>Si ademas delete falla, se devuelve la falla del register, sin excepcion.</summary>
    [Fact]
    public async Task IniciarInscripcionAsync_si_register_y_delete_fallan_devuelve_la_falla_del_register()
    {
        var log = new LoggerQueGuarda<FlowPasarela>();
        var handler = HandlerDeInscripcionConRegisterRoto("cust-888", HttpStatusCode.InternalServerError, new List<string>());
        var pasarela = CrearPasarela(handler, log);

        var resultado = await pasarela.IniciarInscripcionAsync(SolicitudDeInscripcionPrueba());

        Assert.False(resultado.Exito);
        Assert.Equal(Fallas.ReglaDeNegocio, resultado.Codigo);
        Assert.Equal("La pasarela de pagos no respondio correctamente.", resultado.Mensaje);
        Assert.Equal(3, handler.Peticiones.Count);
        Assert.Contains(log.Mensajes, m => m.Contains("No se pudo borrar el cliente cust-888"));
    }

    /// <summary>El log lleva codigo y mensaje de Flow, y nunca la apiKey.</summary>
    [Fact]
    public async Task Un_400_de_Flow_registra_codigo_y_mensaje_sin_la_apiKey()
    {
        var log = new LoggerQueGuarda<FlowPasarela>();
        var handler = new HandlerFalso();
        handler.Responder = _ => Task.FromResult(Json(HttpStatusCode.BadRequest, Error7001));
        var pasarela = CrearPasarela(handler, log);

        var resultado = await pasarela.IniciarInscripcionAsync(SolicitudDeInscripcionPrueba());

        Assert.False(resultado.Exito);
        var todo = string.Join(" | ", log.Mensajes);
        Assert.Contains("7001", todo);
        Assert.Contains("Commerce has not automatic charge contract", todo);
        Assert.DoesNotContain(ApiKeyPrueba, todo);
    }

    // ========================================== P8: LeerConfirmacion de inscripcion

    /// <summary>
    /// P8: Aviso flow=inscripcion con status "1" -> Aprobado, intento = externalId de customer/get,
    /// referencia = customerId.
    /// </summary>
    [Fact]
    public async Task LeerConfirmacion_flow_inscripcion_con_status_1_devuelve_Aprobado()
    {
        var handler = new HandlerFalso();
        var pasarela = CrearPasarela(handler);

        var intento = Guid.NewGuid();
        var token = "token-inscripcion";
        var customerId = "cust-12345";

        handler.Responder = req =>
        {
            if (req.RequestUri!.AbsolutePath == "/api/customer/getRegisterStatus")
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(JsonSerializer.Serialize(new
                    {
                        status = "1",
                        customerId = customerId,
                        creditCardType = "VISA",
                        last4CardDigits = "1234",
                    }), Encoding.UTF8, "application/json"),
                });
            }
            if (req.RequestUri!.AbsolutePath == "/api/customer/get")
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(JsonSerializer.Serialize(new { externalId = intento.ToString("D") }), Encoding.UTF8, "application/json"),
                });
            }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        };

        var aviso = new AvisoDePasarela(new Dictionary<string, string>
        {
            ["token"] = token,
            ["flow"] = "inscripcion",
        });

        var resultado = await pasarela.LeerConfirmacionAsync(aviso);

        Assert.True(resultado.Exito, resultado.Mensaje);
        Assert.NotNull(resultado.Valor);
        Assert.True(resultado.Valor!.Aprobado);
        Assert.Equal(intento, resultado.Valor.IntentoDePagoId);
        Assert.Equal(customerId, resultado.Valor.ReferenciaPasarela);
        Assert.Equal(0, resultado.Valor.MontoConfirmado);
    }

    /// <summary>
    /// P8: Aviso flow=inscripcion con status "0" -> no aprobado.
    /// </summary>
    [Fact]
    public async Task LeerConfirmacion_flow_inscripcion_con_status_0_devuelve_No_Aprobado()
    {
        var handler = new HandlerFalso();
        var pasarela = CrearPasarela(handler);

        var token = "token-inscripcion";
        var customerId = "cust-12345";

        handler.Responder = req =>
        {
            if (req.RequestUri!.AbsolutePath == "/api/customer/getRegisterStatus")
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(JsonSerializer.Serialize(new
                    {
                        status = "0",
                        customerId = customerId,
                        creditCardType = "",
                        last4CardDigits = "",
                    }), Encoding.UTF8, "application/json"),
                });
            }
            if (req.RequestUri!.AbsolutePath == "/api/customer/get")
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(JsonSerializer.Serialize(new { externalId = Guid.NewGuid().ToString("D") }), Encoding.UTF8, "application/json"),
                });
            }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        };

        var aviso = new AvisoDePasarela(new Dictionary<string, string>
        {
            ["token"] = token,
            ["flow"] = "inscripcion",
        });

        var resultado = await pasarela.LeerConfirmacionAsync(aviso);

        Assert.True(resultado.Exito, resultado.Mensaje);
        Assert.False(resultado.Valor!.Aprobado);
    }

    // ========================================== P9: CobrarAsync

    /// <summary>
    /// P9: Cobrar sin MedioInscrito -> ReglaDeNegocio y ninguna peticion.
    /// </summary>
    [Fact]
    public async Task CobrarAsync_sin_MedioInscrito_devuelve_ReglaDeNegocio_sin_peticion()
    {
        var handler = new HandlerFalso();
        var pasarela = CrearPasarela(handler);

        var resultado = await pasarela.CobrarAsync(new SolicitudDeCobro(
            IntentoDePagoId: Guid.NewGuid(),
            Monto: 5990,
            Moneda: "CLP",
            Descripcion: "test"));

        Assert.False(resultado.Exito);
        Assert.Equal(Fallas.ReglaDeNegocio, resultado.Codigo);
        Assert.Contains("medio", resultado.Mensaje, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(handler.Peticiones);
    }

    /// <summary>
    /// P9: Cobrar con medio y status 2 -> Aprobado y MontoConfirmado = el de la respuesta
    /// (pon en la respuesta un monto DISTINTO al pedido y comprueba que gana el de la respuesta).
    /// </summary>
    [Fact]
    public async Task CobrarAsync_con_medio_y_status_2_devuelve_monto_de_la_respuesta()
    {
        var handler = new HandlerFalso();
        var pasarela = CrearPasarela(handler);

        var intento = Guid.NewGuid();
        var montoPedido = 5990;
        // DISTINTO a proposito: lo que vale es lo que Flow dice que cobro, no lo que se pidio.
        // Con el mismo monto, esta prueba no distinguia una cosa de la otra (mutacion F3).
        var montoRespuesta = 5900m;

        handler.Responder = req =>
        {
            if (req.RequestUri!.AbsolutePath == "/api/customer/charge")
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(JsonSerializer.Serialize(new
                    {
                        flowOrder = 77777,
                        commerceOrder = intento.ToString("D"),
                        status = 2,
                        amount = montoRespuesta,
                        currency = "CLP",
                    }), Encoding.UTF8, "application/json"),
                });
            }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        };

        var resultado = await pasarela.CobrarAsync(new SolicitudDeCobro(
            IntentoDePagoId: intento,
            Monto: montoPedido,
            Moneda: "CLP",
            Descripcion: "test")
        {
            MedioInscrito = "cust-12345",
            CorreoDelPagador = "prestador@zass.cl",
        });

        Assert.True(resultado.Exito, resultado.Mensaje);
        Assert.NotNull(resultado.Valor);
        Assert.True(resultado.Valor!.Aprobado);
        Assert.Equal((int)montoRespuesta, resultado.Valor.MontoConfirmado);
        Assert.Equal("77777", resultado.Valor.ReferenciaPasarela);
    }

    // ========================================== P10: Errores de red

    /// <summary>
    /// P10: Flow responde 500 -> Fallas.ReglaDeNegocio y el mensaje NO contiene la apiKey.
    /// </summary>
    [Fact]
    public async Task IniciarCobroAsync_con_500_de_Flow_devuelve_ReglaDeNegocio_sin_apiKey()
    {
        var handler = new HandlerFalso();
        var pasarela = CrearPasarela(handler);

        handler.Responder = _ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError));

        var resultado = await pasarela.IniciarCobroAsync(new SolicitudDeCobro(
            IntentoDePagoId: Guid.NewGuid(),
            Monto: 5990,
            Moneda: "CLP",
            Descripcion: "test")
        {
            CorreoDelPagador = "prestador@zass.cl",
        });

        Assert.False(resultado.Exito);
        Assert.Equal(Fallas.ReglaDeNegocio, resultado.Codigo);
        Assert.DoesNotContain(ApiKeyPrueba, resultado.Mensaje, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("secret", resultado.Mensaje, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// P10: El handler lanza HttpRequestException -> Fallas.ReglaDeNegocio y el mensaje NO contiene la apiKey.
    /// </summary>
    [Fact]
    public async Task IniciarCobroAsync_con_HttpRequestException_devuelve_ReglaDeNegocio_sin_apiKey()
    {
        var handler = new HandlerFalso();
        var pasarela = CrearPasarela(handler);

        handler.Responder = _ => throw new HttpRequestException("Error de red simulado");

        var resultado = await pasarela.IniciarCobroAsync(new SolicitudDeCobro(
            IntentoDePagoId: Guid.NewGuid(),
            Monto: 5990,
            Moneda: "CLP",
            Descripcion: "test")
        {
            CorreoDelPagador = "prestador@zass.cl",
        });

        Assert.False(resultado.Exito);
        Assert.Equal(Fallas.ReglaDeNegocio, resultado.Codigo);
        Assert.DoesNotContain(ApiKeyPrueba, resultado.Mensaje, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("secret", resultado.Mensaje, StringComparison.OrdinalIgnoreCase);
    }

    // ========================================== P11: Sin CorreoDelPagador

    /// <summary>
    /// P11: Sin CorreoDelPagador -> Fallas.Validacion y ninguna peticion.
    /// </summary>
    [Fact]
    public async Task IniciarCobroAsync_sin_CorreoDelPagador_devuelve_Validacion_sin_peticion()
    {
        var handler = new HandlerFalso();
        var pasarela = CrearPasarela(handler);

        var resultado = await pasarela.IniciarCobroAsync(new SolicitudDeCobro(
            IntentoDePagoId: Guid.NewGuid(),
            Monto: 5990,
            Moneda: "CLP",
            Descripcion: "test"));
        // Sin CorreoDelPagador

        Assert.False(resultado.Exito);
        Assert.Equal(Fallas.Validacion, resultado.Codigo);
        Assert.Contains("correo", resultado.Mensaje, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(handler.Peticiones);
    }

    /// <summary>
    /// P11: IniciarInscripcionAsync sin CorreoDelPagador -> Fallas.Validacion y ninguna peticion.
    /// </summary>
    [Fact]
    public async Task IniciarInscripcionAsync_sin_CorreoDelPagador_devuelve_Validacion_sin_peticion()
    {
        var handler = new HandlerFalso();
        var pasarela = CrearPasarela(handler);

        var resultado = await pasarela.IniciarInscripcionAsync(new SolicitudDeInscripcion(
            IntentoDePagoId: Guid.NewGuid(),
            Descripcion: "test"));
        // Sin CorreoDelPagador

        Assert.False(resultado.Exito);
        Assert.Equal(Fallas.Validacion, resultado.Codigo);
        Assert.Contains("correo", resultado.Mensaje, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(handler.Peticiones);
    }

    // P12 (Pasarela "flow" sin claves): vive en PasarelaEnProduccionPruebas, donde se arma el
    // contenedor de verdad con AgregarZass. La version de aqui solo probaba su propio if.

    // ========================================== Fase 2: a donde vuelve el navegador (API-75)

    [Theory]
    [InlineData(null, "https://api.zass.cl/api/pagos/aviso", "https://api.zass.cl/api/pagos/vuelta")]
    [InlineData(null, "https://api.zass.cl/api/pagos/aviso/", "https://api.zass.cl/api/pagos/vuelta")]
    [InlineData("https://otra.zass.cl/v/", "https://api.zass.cl/api/pagos/aviso", "https://otra.zass.cl/v")]
    public void La_vuelta_sale_del_aviso_o_de_la_configuracion(string? configurada, string aviso, string esperada)
    {
        Assert.Equal(esperada, FlowPasarela.UrlDeLaVuelta(configurada, aviso));
    }

    [Fact]
    public void Si_el_aviso_no_termina_en_aviso_hay_que_configurar_la_vuelta_a_mano()
    {
        Assert.Throws<OptionsValidationException>(
            () => FlowPasarela.UrlDeLaVuelta(null, "https://api.zass.cl/otra-cosa"));
    }
}
