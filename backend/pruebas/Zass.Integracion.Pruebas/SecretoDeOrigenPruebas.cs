using System.Net;
using System.Text;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Zass.Api.Configuracion;

namespace Zass.Integracion.Pruebas;

/// <summary>
/// P-11: con Zass:SecretoDeOrigen, la API solo atiende lo que trae la cabecera X-Origen-Zass que
/// pone la Function de Cloudflare. Sin la opcion, todo igual que antes.
/// </summary>
public class SecretoDeOrigenPruebas
{
    private const string Secreto = "0123456789abcdef0123456789abcdef-origen-de-prueba";

    private static AplicacionConProxy ConSecreto() =>
        new(extra: new() { [SecretoDeOrigen.Clave] = Secreto });

    private static HttpRequestMessage Pedir(HttpMethod metodo, string ruta, params string[] valoresDeCabecera)
    {
        var p = new HttpRequestMessage(metodo, ruta);
        if (valoresDeCabecera.Length > 0) p.Headers.Add(SecretoDeOrigen.Cabecera, valoresDeCabecera);
        return p;
    }

    private static async Task<HttpStatusCode> Estado(HttpClient c, string ruta, params string[] cabecera) =>
        (await c.SendAsync(Pedir(HttpMethod.Get, ruta, cabecera))).StatusCode;

    // ------------------------------------------------------------ sin la opcion

    [Fact]
    public async Task Sin_la_opcion_la_API_atiende_sin_cabecera_como_siempre()
    {
        using var app = new AplicacionConProxy();
        using var c = app.Cliente();

        Assert.Equal(HttpStatusCode.OK, await Estado(c, "/api/planes"));
        Assert.Equal(HttpStatusCode.Unauthorized, await Estado(c, "/api/mi/cuenta"));
    }

    // ------------------------------------------------------------ con la opcion

    [Fact]
    public async Task Con_la_opcion_sin_cabecera_responde_403()
    {
        using var app = ConSecreto();
        using var c = app.Cliente();

        Assert.Equal(HttpStatusCode.Forbidden, await Estado(c, "/api/planes"));
        // Antes que la autenticacion: no llega ni a decir "no hay sesion".
        Assert.Equal(HttpStatusCode.Forbidden, await Estado(c, "/api/mi/cuenta"));
        // Las mayusculas no son otra puerta.
        Assert.Equal(HttpStatusCode.Forbidden, await Estado(c, "/API/PLANES"));
        // La tarjeta (/t/...) tambien pasa por la Function.
        Assert.Equal(HttpStatusCode.Forbidden, await Estado(c, "/t/ABCDEFGH"));
    }

    [Fact]
    public async Task Con_la_opcion_el_403_lleva_las_cabeceras_de_seguridad_y_no_dice_el_secreto()
    {
        using var app = ConSecreto();
        using var c = app.Cliente();

        var r = await c.SendAsync(Pedir(HttpMethod.Get, "/api/planes"));
        var cuerpo = await r.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
        Assert.True(r.Headers.Contains("X-Frame-Options"));
        Assert.True(r.Headers.Contains("Content-Security-Policy"));
        Assert.DoesNotContain(Secreto, cuerpo);
    }

    [Theory]
    [InlineData("otro-secreto-cualquiera-de-largo-parecido-000000000")]
    [InlineData("0123456789abcdef0123456789abcdef-origen-de-prueb")]   // un caracter menos
    [InlineData("0123456789abcdef0123456789abcdef-origen-de-pruebaX")]  // un caracter mas
    [InlineData("0123456789ABCDEF0123456789ABCDEF-ORIGEN-DE-PRUEBA")]   // otras mayusculas
    public async Task Con_la_opcion_y_una_cabecera_equivocada_responde_403(string valor)
    {
        using var app = ConSecreto();
        using var c = app.Cliente();

        Assert.Equal(HttpStatusCode.Forbidden, await Estado(c, "/api/planes", valor));
    }

    [Fact]
    public async Task Con_la_opcion_dos_valores_en_la_cabecera_aunque_uno_sea_el_bueno_responde_403()
    {
        using var app = ConSecreto();
        using var c = app.Cliente();

        Assert.Equal(HttpStatusCode.Forbidden, await Estado(c, "/api/planes", Secreto, "otro"));
    }

    [Fact]
    public async Task Con_la_opcion_y_la_cabecera_correcta_atiende()
    {
        using var app = ConSecreto();
        using var c = app.Cliente();

        Assert.Equal(HttpStatusCode.OK, await Estado(c, "/api/planes", Secreto));
        // Llega a la autenticacion: el 401 de siempre, no el 403 del filtro.
        Assert.Equal(HttpStatusCode.Unauthorized, await Estado(c, "/api/mi/cuenta", Secreto));
    }

    [Theory]
    [InlineData("/api/salud")]
    [InlineData("/api/salud/lista")]
    [InlineData("/API/SALUD")]
    public async Task Con_la_opcion_las_sondas_de_salud_pasan_sin_cabecera(string ruta)
    {
        using var app = ConSecreto();
        using var c = app.Cliente();

        Assert.Equal(HttpStatusCode.OK, await Estado(c, ruta));
    }

    [Fact]
    public async Task Con_la_opcion_el_aviso_de_Flow_API_70_pasa_sin_cabecera()
    {
        using var app = ConSecreto();
        using var c = app.Cliente();

        var r = await c.PostAsync("/api/pagos/aviso",
            new StringContent("{esto no es json", Encoding.UTF8, "application/json"));

        // El 400 de siempre ante un cuerpo roto: llego al endpoint.
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
    }

    [Fact]
    public async Task Con_la_opcion_la_vuelta_de_Flow_API_75_pasa_sin_cabecera()
    {
        using var app = ConSecreto();
        using var c = app.Cliente();

        var r = await c.SendAsync(Pedir(HttpMethod.Get, "/api/pagos/vuelta"));

        Assert.Equal(HttpStatusCode.Redirect, r.StatusCode);
    }

    [Theory]
    [InlineData("/api/saludx")]           // empieza igual, pero es otro segmento
    [InlineData("/api/pagos/avisos")]
    [InlineData("/api/pagos/aviso/otra")]
    [InlineData("/api/pagos")]
    public async Task Con_la_opcion_lo_exento_no_es_mas_ancho_que_su_ruta(string ruta)
    {
        using var app = ConSecreto();
        using var c = app.Cliente();

        Assert.Equal(HttpStatusCode.Forbidden, await Estado(c, ruta));
    }

    // ------------------------------------------------------------ arranque

    private static IConfiguration Config(string? secreto) =>
        new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { [SecretoDeOrigen.Clave] = secreto }).Build();

    [Fact]
    public void Un_secreto_corto_impide_arrancar_y_el_mensaje_no_lo_cita()
    {
        var problemas = SecretoDeOrigen.Problemas(Config("corto-de-31-caracteres-0000000")).ToList();

        Assert.Contains(problemas, p => p.Contains(SecretoDeOrigen.Clave));
        Assert.DoesNotContain(problemas, p => p.Contains("corto-de-31"));
    }

    [Fact]
    public void Un_secreto_con_espacios_o_tildes_impide_arrancar()
    {
        Assert.NotEmpty(SecretoDeOrigen.Problemas(Config("0123456789abcdef 0123456789abcdef")));
        Assert.NotEmpty(SecretoDeOrigen.Problemas(Config("0123456789abcdefñ0123456789abcdef")));
    }

    [Fact]
    public void Un_secreto_bueno_o_ninguno_no_dan_problemas()
    {
        Assert.Empty(SecretoDeOrigen.Problemas(Config(Secreto)));
        Assert.Empty(SecretoDeOrigen.Problemas(Config(null)));
    }

    [Fact]
    public void La_validacion_de_arranque_incluye_el_secreto_de_origen()
    {
        var problemas = ValidacionDeArranque.Validar(Config("corto"), esProductivo: false);

        Assert.Contains(problemas, p => p.Contains(SecretoDeOrigen.Clave));
    }

    [Fact]
    public void En_produccion_sin_secreto_se_advierte_pero_no_se_impide_arrancar()
    {
        Assert.NotNull(SecretoDeOrigen.Advertencia(Config(null), esProductivo: true));
        Assert.Null(SecretoDeOrigen.Advertencia(Config(Secreto), esProductivo: true));
        Assert.Null(SecretoDeOrigen.Advertencia(Config(null), esProductivo: false));
        Assert.DoesNotContain(ValidacionDeArranque.Validar(Config(null), esProductivo: true),
            p => p.Contains(SecretoDeOrigen.Clave));
    }
}

/// <summary>HSTS: dos anos, con subdominios y SIN preload (preload es decision de Pablo).</summary>
public class HstsPruebas
{
    private const string Esperado = "max-age=63072000; includeSubDomains";

    [Fact]
    public async Task UseHsts_manda_dos_anos_con_subdominios_y_sin_preload()
    {
        // UseHsts no actua sobre localhost (ExcludedHosts): hace falta otro host, y HTTPS.
        using var app = new AplicacionConProxy();
        using var c = app.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://api.zass.test"),
        });

        var r = await c.GetAsync("/api/salud");

        Assert.Equal(Esperado, string.Join(",", r.Headers.GetValues("Strict-Transport-Security")));
    }

    [Fact]
    public async Task La_cabecera_escrita_a_mano_es_la_misma_cuando_UseHsts_no_actua()
    {
        using var app = new AplicacionConProxy();
        using var c = app.Cliente();

        var r = await c.GetAsync("/api/salud");

        Assert.Equal(Esperado, string.Join(",", r.Headers.GetValues("Strict-Transport-Security")));
    }
}
