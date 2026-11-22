using System.Net;
using System.Net.Http.Json;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Zass.Api.Configuracion;

namespace Zass.Integracion.Pruebas;

/// <summary>
/// N3 (auditoría del 22-nov-2026): captcha Turnstile en el registro (API-08) y al tomar un cupo
/// fundador (API-69). Nada de esto sale a internet: siteverify lo contesta un doble.
/// </summary>
public class TurnstilePruebas
{
    private const string SecretoReal = "0x4AAAAAAA-secreto-de-mentira-para-pruebas";
    private const string SecretoDePruebaQuePasa = "1x0000000000000000000000000000000AA";

    /// <summary>Lo que contestaría siteverify, y lo que le llegó.</summary>
    private sealed class SiteverifyFalso : HttpMessageHandler
    {
        private readonly Func<string> _cuerpo;
        public int Llamadas;
        public string? Formulario;
        public bool Caido;

        public SiteverifyFalso(Func<string> cuerpo) => _cuerpo = cuerpo;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage peticion, CancellationToken ct)
        {
            Llamadas++;
            Assert.Equal(Turnstile.UrlVerificacion, peticion.RequestUri!.ToString());
            Formulario = await peticion.Content!.ReadAsStringAsync(ct);
            if (Caido) throw new HttpRequestException("sin red");
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(_cuerpo(), Encoding.UTF8, "application/json"),
            };
        }
    }

    private static string Respuesta(bool exito, string? hostname, string? accion) =>
        System.Text.Json.JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["success"] = exito,
            ["hostname"] = hostname,
            ["action"] = accion,
            ["error-codes"] = exito ? Array.Empty<string>() : new[] { "invalid-input-response" },
        });

    private static (VerificadorTurnstile, SiteverifyFalso) Verificador(
        string? secreto, string cuerpo, string? hostnames = null)
    {
        var ajustes = new Dictionary<string, string?> { [Turnstile.ClaveSecreto] = secreto };
        if (hostnames is not null) ajustes[Turnstile.ClaveHostnames] = hostnames;
        var config = new ConfigurationBuilder().AddInMemoryCollection(ajustes).Build();
        var falso = new SiteverifyFalso(() => cuerpo);
        return (new VerificadorTurnstile(
            new HttpClient(falso), config, NullLogger<VerificadorTurnstile>.Instance), falso);
    }

    private static Task<VeredictoCaptcha> Verificar(VerificadorTurnstile v, string? token = "token-ok",
        string accion = Turnstile.AccionCupoFundador) =>
        v.VerificarAsync(token, accion, "190.0.0.1", CancellationToken.None);

    // ------------------------------------------------------------ El verificador

    [Fact]
    public async Task Sin_secreto_no_se_exige_y_ni_se_llama_a_Cloudflare()
    {
        var (v, falso) = Verificador(null, Respuesta(false, null, null));

        Assert.True((await Verificar(v, token: null)).Aceptado);
        Assert.Equal(0, falso.Llamadas);
    }

    [Fact]
    public async Task Con_secreto_sin_token_se_rechaza_sin_llamar_a_Cloudflare()
    {
        var (v, falso) = Verificador(SecretoReal, Respuesta(true, "zass.cl", Turnstile.AccionCupoFundador));

        Assert.False((await Verificar(v, token: null)).Aceptado);
        Assert.False((await Verificar(v, token: "  ")).Aceptado);
        Assert.False((await Verificar(v, token: new string('x', Turnstile.LargoMaximoToken + 1))).Aceptado);
        Assert.Equal(0, falso.Llamadas);
    }

    [Fact]
    public async Task Un_token_valido_de_zass_cl_y_de_esta_pantalla_pasa_y_se_manda_bien()
    {
        var (v, falso) = Verificador(SecretoReal, Respuesta(true, "zass.cl", Turnstile.AccionCupoFundador));

        Assert.True((await Verificar(v)).Aceptado);
        Assert.Equal(1, falso.Llamadas);
        Assert.Contains("secret=" + Uri.EscapeDataString(SecretoReal), falso.Formulario);
        Assert.Contains("response=token-ok", falso.Formulario);
        Assert.Contains("remoteip=190.0.0.1", falso.Formulario);
    }

    [Fact]
    public async Task Cloudflare_dice_que_no_y_se_rechaza()
    {
        var (v, _) = Verificador(SecretoReal, Respuesta(false, "zass.cl", Turnstile.AccionCupoFundador));
        Assert.False((await Verificar(v)).Aceptado);
    }

    [Theory]
    [InlineData("otro-sitio.cl")]
    [InlineData("zass.cl.atacante.com")]
    [InlineData("example.com")]      // el hostname de las claves de prueba, con una clave REAL
    [InlineData("")]
    public async Task Un_token_resuelto_en_otro_hostname_se_rechaza(string hostname)
    {
        var (v, _) = Verificador(SecretoReal, Respuesta(true, hostname, Turnstile.AccionCupoFundador));

        var r = await Verificar(v);

        Assert.False(r.Aceptado);
        Assert.Contains("hostname", r.Motivo);
    }

    [Fact]
    public async Task Los_hostnames_se_pueden_configurar()
    {
        var (v, _) = Verificador(SecretoReal,
            Respuesta(true, "zass-app.pages.dev", Turnstile.AccionCupoFundador), "zass.cl, zass-app.pages.dev");
        Assert.True((await Verificar(v)).Aceptado);
    }

    [Theory]
    [InlineData(Turnstile.AccionRegistro)]
    [InlineData(null)]
    [InlineData("otra")]
    public async Task Un_token_de_otra_pantalla_no_sirve_para_tomar_el_cupo(string? accion)
    {
        var (v, _) = Verificador(SecretoReal, Respuesta(true, "zass.cl", accion));

        var r = await Verificar(v, accion: Turnstile.AccionCupoFundador);

        Assert.False(r.Aceptado);
        Assert.Contains("acción", r.Motivo);
    }

    [Fact]
    public async Task Si_Cloudflare_no_contesta_se_falla_cerrado()
    {
        var (v, falso) = Verificador(SecretoReal, Respuesta(true, "zass.cl", Turnstile.AccionCupoFundador));
        falso.Caido = true;

        Assert.False((await Verificar(v)).Aceptado);
    }

    [Fact]
    public async Task Con_la_clave_de_prueba_se_acepta_example_com_sin_accion()
    {
        // Lo que de verdad contesta Cloudflare con 1x0000000000000000000000000000000AA.
        var (v, _) = Verificador(SecretoDePruebaQuePasa, Respuesta(true, "example.com", null));
        Assert.True((await Verificar(v)).Aceptado);
    }

    // ------------------------------------------------------------ Arranque

    [Fact]
    public void Una_clave_de_prueba_en_produccion_no_deja_arrancar_y_en_desarrollo_si()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { [Turnstile.ClaveSecreto] = SecretoDePruebaQuePasa }).Build();

        Assert.Single(Turnstile.Problemas(config, esProductivo: true));
        Assert.Empty(Turnstile.Problemas(config, esProductivo: false));
    }

    [Fact]
    public void Sin_secreto_en_produccion_se_advierte_y_no_se_dice_ningun_valor()
    {
        var vacia = new ConfigurationBuilder().Build();
        var con = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { [Turnstile.ClaveSecreto] = SecretoReal }).Build();

        Assert.NotNull(Turnstile.Advertencia(vacia, esProductivo: true));
        Assert.Null(Turnstile.Advertencia(vacia, esProductivo: false));
        Assert.Null(Turnstile.Advertencia(con, esProductivo: true));
        Assert.Empty(Turnstile.Problemas(con, esProductivo: true));
    }

    // ------------------------------------------------------------ En la API

    private static WebApplicationFactory<Program> ApiConCaptcha(SiteverifyFalso falso, string? secreto = SecretoReal) =>
        new AplicacionConProxy(extra: new() { [Turnstile.ClaveSecreto] = secreto })
            .WithWebHostBuilder(b => b.ConfigureServices(s =>
                s.AddHttpClient<IVerificadorCaptcha, VerificadorTurnstile>()
                    .ConfigurePrimaryHttpMessageHandler(() => falso)));

    [Theory]
    [InlineData("API-08", Turnstile.AccionRegistro)]
    [InlineData("API-69", Turnstile.AccionCupoFundador)]
    public void El_registro_y_el_cupo_fundador_piden_captcha_de_su_propia_accion(string ruta, string accion)
    {
        using var app = new AplicacionConProxy();
        using var _ = app.Cliente();
        var endpoint = app.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .Single(e => e.Metadata.GetMetadata<IEndpointNameMetadata>()?.EndpointName == ruta);

        Assert.Equal(accion, endpoint.Metadata.GetMetadata<CaptchaExigido>()?.Accion);
    }

    private static object UnRegistro(string correo) => new
    {
        tipo = "particular", apellido = "Prueba", rut = DatosDeRegistro.Rut(),
        telefono = "+56 9 1234 5678", mayorDeEdad = true, correo,
        contrasena = "contrasena-larga-1", nombre = "Persona de Prueba", rol = "prestador",
    };

    private static async Task<bool> ExisteAsync(string correo)
    {
        using var ctx = ContextoDePrueba.Crear();
        return await ctx.Usuarios.IgnoreQueryFilters().AnyAsync(u => u.Correo == correo);
    }

    [Fact]
    public async Task Con_secreto_el_registro_sin_captcha_valido_responde_400_y_no_crea_la_cuenta()
    {
        var falso = new SiteverifyFalso(() => Respuesta(false, "zass.cl", Turnstile.AccionRegistro));
        using var app = ApiConCaptcha(falso);
        using var c = app.CreateClient(new WebApplicationFactoryClientOptions
        { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });

        var sinToken = $"capt-a-{Guid.NewGuid():N}@zass.local";
        var r1 = await c.PostAsJsonAsync("/api/auth/registro", UnRegistro(sinToken));
        Assert.Equal(HttpStatusCode.BadRequest, r1.StatusCode);
        Assert.Contains("eres una persona", await r1.Content.ReadAsStringAsync());
        Assert.Equal(0, falso.Llamadas);                      // sin token ni se pregunta

        var conMalo = $"capt-b-{Guid.NewGuid():N}@zass.local";
        var pedido = new HttpRequestMessage(HttpMethod.Post, "/api/auth/registro")
        { Content = JsonContent.Create(UnRegistro(conMalo)) };
        pedido.Headers.Add(Turnstile.Cabecera, "token-rechazado");
        var r2 = await c.SendAsync(pedido);
        Assert.Equal(HttpStatusCode.BadRequest, r2.StatusCode);
        Assert.Equal(1, falso.Llamadas);

        Assert.False(await ExisteAsync(sinToken));
        Assert.False(await ExisteAsync(conMalo));
    }

    [Fact]
    public async Task Con_secreto_el_registro_con_captcha_valido_crea_la_cuenta()
    {
        var falso = new SiteverifyFalso(() => Respuesta(true, "zass.cl", Turnstile.AccionRegistro));
        using var app = ApiConCaptcha(falso);
        using var c = app.CreateClient(new WebApplicationFactoryClientOptions
        { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });

        var correo = $"capt-c-{Guid.NewGuid():N}@zass.local";
        var pedido = new HttpRequestMessage(HttpMethod.Post, "/api/auth/registro")
        { Content = JsonContent.Create(UnRegistro(correo)) };
        pedido.Headers.Add(Turnstile.Cabecera, "token-bueno");
        var r = await c.SendAsync(pedido);

        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        Assert.True(await ExisteAsync(correo));
    }

    [Fact]
    public async Task Un_formulario_mal_llenado_no_gasta_el_token()
    {
        var falso = new SiteverifyFalso(() => Respuesta(true, "zass.cl", Turnstile.AccionRegistro));
        using var app = ApiConCaptcha(falso);
        using var c = app.CreateClient(new WebApplicationFactoryClientOptions
        { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });

        var pedido = new HttpRequestMessage(HttpMethod.Post, "/api/auth/registro")
        { Content = JsonContent.Create(new { correo = "no-es-un-correo" }) };
        pedido.Headers.Add(Turnstile.Cabecera, "token-bueno");
        var r = await c.SendAsync(pedido);

        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        Assert.Equal(0, falso.Llamadas);
    }
}
