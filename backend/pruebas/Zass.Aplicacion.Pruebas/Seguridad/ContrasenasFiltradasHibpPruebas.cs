using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Zass.Infraestructura.Seguridad;

namespace Zass.Aplicacion.Pruebas.Seguridad;

/// <summary>
/// C) Adaptador con un HttpMessageHandler falso (sin red):
/// - respuesta con el sufijo y cuenta 3 -> true
/// - sufijo con cuenta 0 (padding) -> false
/// - sufijo ausente -> false
/// - el handler recibe SOLO /range/ + 5 caracteres (verifica que la URL no contiene el hash completo)
/// - error 500 -> false
/// - timeout -> false
/// </summary>
public class ContrasenasFiltradasHibpPruebas
{
    private readonly ILogger<ContrasenasFiltradasHibp> _logger = new LoggerFactory().CreateLogger<ContrasenasFiltradasHibp>();

    // SHA-1 de "password" calculado en tiempo de ejecucion para evitar falsos positivos en gitleaks
    private const string Password = "password";
    private static readonly string HashCompleto = Convert.ToHexString(System.Security.Cryptography.SHA1.HashData(System.Text.Encoding.UTF8.GetBytes(Password)));
    private static readonly string Prefijo = HashCompleto.Substring(0, 5);
    private static readonly string Sufijo = HashCompleto.Substring(5);

    private static HttpClient CrearCliente(HttpMessageHandler handler)
    {
        var client = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://api.pwnedpasswords.com/"),
            Timeout = TimeSpan.FromSeconds(3)
        };
        return client;
    }

    // ----------------------------------------------------------- C1: respuesta con el sufijo y cuenta 3 -> true

    [Fact]
    public async Task Sufijo_con_cuenta_mayor_a_cero_devuelve_true()
    {
        var handler = new FakeHandler($"{Sufijo}:3\nOTRO:1");
        var client = CrearCliente(handler);
        var adaptador = new ContrasenasFiltradasHibp(client, _logger);

        var resultado = await adaptador.EstaFiltradaAsync(Password);

        Assert.True(resultado);
        Assert.Equal($"/range/{Prefijo}", handler.RequestUri?.PathAndQuery);
    }

    // ----------------------------------------------------------- C2: sufijo con cuenta 0 (padding) -> false

    [Fact]
    public async Task Sufijo_con_cuenta_cero_padding_devuelve_false()
    {
        var handler = new FakeHandler($"{Sufijo}:0\nOTRO:5");
        var client = CrearCliente(handler);
        var adaptador = new ContrasenasFiltradasHibp(client, _logger);

        var resultado = await adaptador.EstaFiltradaAsync(Password);

        Assert.False(resultado);
        Assert.Equal($"/range/{Prefijo}", handler.RequestUri?.PathAndQuery);
    }

    // ----------------------------------------------------------- C3: sufijo ausente -> false

    [Fact]
    public async Task Sufijo_ausente_devuelve_false()
    {
        var handler = new FakeHandler("OTRO:5\nOTRO2:10");
        var client = CrearCliente(handler);
        var adaptador = new ContrasenasFiltradasHibp(client, _logger);

        var resultado = await adaptador.EstaFiltradaAsync(Password);

        Assert.False(resultado);
        Assert.Equal($"/range/{Prefijo}", handler.RequestUri?.PathAndQuery);
    }

    // ----------------------------------------------------------- C4: el handler recibe SOLO /range/ + 5 caracteres

    [Fact]
    public async Task La_peticion_contiene_solo_el_prefijo_de_5_caracteres_no_el_hash_completo()
    {
        var handler = new FakeHandler($"{Sufijo}:3");
        var client = CrearCliente(handler);
        var adaptador = new ContrasenasFiltradasHibp(client, _logger);

        await adaptador.EstaFiltradaAsync(Password);

        // Verificar que la URL NO contiene el hash completo
        var ruta = handler.RequestUri?.PathAndQuery ?? "";
        Assert.Equal($"/range/{Prefijo}", ruta);
        Assert.DoesNotContain(Sufijo, ruta);
        Assert.DoesNotContain(HashCompleto, ruta);
    }

    // ----------------------------------------------------------- C5: error 500 -> false

    [Fact]
    public async Task Error_500_devuelve_false_y_loguea_warning()
    {
        var handler = new FakeHandler(HttpStatusCode.InternalServerError, "Error interno");
        var client = CrearCliente(handler);
        var adaptador = new ContrasenasFiltradasHibp(client, _logger);

        var resultado = await adaptador.EstaFiltradaAsync(Password);

        Assert.False(resultado);
    }

    // ----------------------------------------------------------- C6: timeout -> false

    [Fact]
    public async Task Timeout_devuelve_false_y_loguea_warning()
    {
        var handler = new FakeHandler(TimeSpan.FromSeconds(5)); // mas que el timeout de 3s
        var client = CrearCliente(handler);
        var adaptador = new ContrasenasFiltradasHibp(client, _logger);

        var resultado = await adaptador.EstaFiltradaAsync(Password);

        Assert.False(resultado);
    }

    // ----------------------------------------------------------- Clase FakeHandler

    private sealed class FakeHandler : HttpMessageHandler
    {
        private readonly string? _contenido;
        private readonly HttpStatusCode? _statusCode;
        private readonly TimeSpan? _retraso;

        public HttpRequestMessage? Request { get; private set; }
        public Uri? RequestUri => Request?.RequestUri;

        public FakeHandler(string contenido)
        {
            _contenido = contenido;
            _statusCode = HttpStatusCode.OK;
        }

        public FakeHandler(HttpStatusCode statusCode, string contenido)
        {
            _statusCode = statusCode;
            _contenido = contenido;
        }

        public FakeHandler(TimeSpan retraso)
        {
            _retraso = retraso;
            _statusCode = HttpStatusCode.OK;
            _contenido = "";
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Request = request;

            if (_retraso.HasValue)
            {
                return Task.Delay(_retraso.Value, cancellationToken)
                    .ContinueWith(_ => new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(_contenido ?? "")
                    }, cancellationToken);
            }

            var response = new HttpResponseMessage(_statusCode ?? HttpStatusCode.OK)
            {
                Content = new StringContent(_contenido ?? "")
            };
            return Task.FromResult(response);
        }
    }
}
