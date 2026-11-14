using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;

namespace Zass.Integracion.Pruebas;

/// <summary>
/// API-66 a API-70 por HTTP. S7 · Suscripciones y pagos.
///
/// Lo que SOLO aquí se puede comprobar, porque son cosas de ASP.NET y no de la capa de
/// aplicación: que API-66 sea público de verdad y no filtre a nadie, que los tres
/// endpoints del prestador respondan sin sesión como responden los demás, que el filtro
/// de idempotencia corra ANTES del endpoint, y —la más importante— que API-70 esté
/// exento de CSRF a propósito y no por accidente.
///
/// Ninguna de estas pruebas escribe en la base: no crean cuentas ni cobros, así que no
/// dejan filas que alguien tenga que limpiar después. El camino completo con sesión,
/// cobro y navegador es la tarea 11 del plan.
/// </summary>
public class ApiSuscripcionesPruebas : IAsyncLifetime
{
    private readonly Aplicacion _app = new();
    private HttpClient _cliente = null!;

    public Task InitializeAsync()
    {
        _cliente = _app.CreateClient(new WebApplicationFactoryClientOptions
        {
            HandleCookies = true,
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost"),
        });
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        _cliente.Dispose();
        await _app.DisposeAsync();
    }

    private static async Task<int> CuposTomadosAsync()
    {
        using var ctx = ContextoDePrueba.Crear();
        return await ctx.CuposFundador.CountAsync(c => c.PerfilPrestadorId != null);
    }

    private static async Task<int> IntentosAsync()
    {
        using var ctx = ContextoDePrueba.Crear();
        return await ctx.IntentosDePago.CountAsync();
    }

    // --------------------------------------------------------------- API-66 público

    [Fact]
    public async Task API_66_es_publico_y_dice_cuantos_cupos_quedan()
    {
        var respuesta = await _cliente.GetAsync("/api/planes");

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);

        var cuerpo = await respuesta.Content.ReadFromJsonAsync<JsonDocument>();
        var raiz = cuerpo!.RootElement;

        // El contador es el gancho del lanzamiento: si no viene, la página de planes no
        // puede decir «quedan N de 50».
        var disponibles = raiz.GetProperty("cuposFundadorDisponibles").GetInt32();
        Assert.InRange(disponibles, 0, 50);

        // Y los planes, con su precio: sin esto no hay nada que mostrar.
        var planes = raiz.GetProperty("planes").EnumerateArray().ToList();
        Assert.NotEmpty(planes);
        Assert.Contains(planes, p => p.GetProperty("codigo").GetString() == "mensual");
        Assert.Contains(planes, p => p.GetProperty("codigo").GetString() == "fundador");
    }

    [Fact]
    public async Task API_66_no_devuelve_ni_un_dato_de_ninguna_persona()
    {
        var texto = await (await _cliente.GetAsync("/api/planes")).Content.ReadAsStringAsync();

        // Es público y se cachea: lo que salga por aquí sale para cualquiera.
        foreach (var prohibido in new[]
                 { "perfilPrestador", "usuarioId", "correo", "telefono", "referenciaPasarela" })
        {
            Assert.DoesNotContain(prohibido, texto, StringComparison.OrdinalIgnoreCase);
        }
    }

    // ------------------------------------------------- Lo del prestador, sin sesión

    [Theory]
    [InlineData("/api/mi/cobros/cotizar")]
    [InlineData("/api/mi/cobros")]
    [InlineData("/api/mi/suscripcion/fundador")]
    public async Task Sin_sesion_ninguno_de_los_tres_deja_hacer_nada(string ruta)
    {
        var antes = await IntentosAsync();

        var respuesta = await _cliente.PostAsJsonAsync(ruta, new { codigoPlan = "fundador" });

        Assert.True(
            respuesta.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden,
            $"{ruta} respondió {(int)respuesta.StatusCode}");
        Assert.Equal(antes, await IntentosAsync());
    }

    [Fact]
    public async Task Sin_sesion_tampoco_se_toma_un_cupo_aunque_la_peticion_este_perfecta()
    {
        var tomadosAntes = await CuposTomadosAsync();

        var peticion = new HttpRequestMessage(
            HttpMethod.Post, "/api/mi/suscripcion/fundador");
        peticion.Headers.Add("Idempotency-Key", "prueba-sin-sesion-0001");
        peticion.Headers.Add("X-CSRF-Token", await TokenCsrfAsync());

        var respuesta = await _cliente.SendAsync(peticion);

        Assert.True(
            respuesta.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden);
        Assert.Equal(tomadosAntes, await CuposTomadosAsync());
    }

    // -------------------------------------------- API-70: el aviso de la pasarela

    [Fact]
    public async Task API_70_NO_pide_CSRF_y_eso_es_a_proposito()
    {
        // Esta es la prueba que vigila la excepción: lo llama un servidor, no un navegador,
        // así que no hay cookie que proteger y pedirle un token sería pedirle algo que no
        // puede tener. Si alguien «arregla» esto añadiéndole el filtro de CSRF, la pasarela
        // deja de poder confirmar pagos y nadie se entera hasta que falte el dinero.
        var contenido = new StringContent(
            """{"intento":"00000000-0000-0000-0000-000000000000","monto":"7990"}""",
            Encoding.UTF8, "application/json");

        var respuesta = await _cliente.PostAsync("/api/pagos/aviso", contenido);

        // No importa qué contesta el caso de uso (ese intento no existe): importa que NO
        // sea un 403 por falta de token ni un 401 por falta de sesión.
        Assert.NotEqual(HttpStatusCode.Forbidden, respuesta.StatusCode);
        Assert.NotEqual(HttpStatusCode.Unauthorized, respuesta.StatusCode);
    }

    [Fact]
    public async Task API_70_con_un_aviso_de_un_intento_que_no_existe_no_cambia_nada()
    {
        var tomadosAntes = await CuposTomadosAsync();
        var intentosAntes = await IntentosAsync();

        var contenido = new StringContent(
            $$"""{"intento":"{{Guid.NewGuid()}}","monto":"5990","estado":"aprobado"}""",
            Encoding.UTF8, "application/json");

        var respuesta = await _cliente.PostAsync("/api/pagos/aviso", contenido);

        Assert.True((int)respuesta.StatusCode >= 400, $"respondió {(int)respuesta.StatusCode}");
        Assert.Equal(tomadosAntes, await CuposTomadosAsync());
        Assert.Equal(intentosAntes, await IntentosAsync());
    }

    [Fact]
    public async Task API_70_con_un_cuerpo_que_no_es_JSON_responde_400_y_no_revienta()
    {
        var contenido = new StringContent("{esto no es json", Encoding.UTF8, "application/json");

        var respuesta = await _cliente.PostAsync("/api/pagos/aviso", contenido);

        // 400 y no 500: un cuerpo roto es una petición mala, no un error del servidor.
        Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);
    }

    [Fact]
    public async Task API_70_sin_cuerpo_tampoco_revienta()
    {
        var respuesta = await _cliente.PostAsync(
            "/api/pagos/aviso", new StringContent("", Encoding.UTF8, "application/json"));

        Assert.True((int)respuesta.StatusCode >= 400 && (int)respuesta.StatusCode < 500,
            $"respondió {(int)respuesta.StatusCode}");
    }

    private async Task<string> TokenCsrfAsync()
    {
        var respuesta = await _cliente.GetAsync("/api/auth/csrf");
        respuesta.EnsureSuccessStatusCode();
        var json = await respuesta.Content.ReadFromJsonAsync<JsonDocument>();
        return json!.RootElement.GetProperty("token").GetString()!;
    }
}
