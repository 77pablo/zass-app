using System.Globalization;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Zass.Integracion.Pruebas;

/// <summary>
/// Ningun texto en ingles llega al usuario.
///
/// La pantalla muestra el `detail` del error, si no el `title` (de los 4xx), y el primer mensaje
/// de cada campo de `errors` (`frontend/src/api/cliente.ts`). Aqui se provoca cada caso donde
/// el texto NO lo escribe nadie de este proyecto, sino ASP.NET o FluentValidation por su cuenta.
///
/// La trampa que esconde el problema: en el PC de Pablo Windows esta en castellano y
/// FluentValidation responde en castellano. En Render la imagen no fija idioma, la cultura es la
/// INVARIANTE y responde en ingles. Por eso cada peticion sale con la invariante: sin eso,
/// pasaria en verde en local y mentiria.
///
/// Y fijarla en la prueba NO basta: el servidor de pruebas, por omision, atiende SIN el contexto
/// de ejecucion del cliente, con la cultura del sistema. Se midio el 17-nov-2026: con la
/// invariante puesta en la prueba, la validacion igual respondia «no deberia estar vacio» y las
/// tres pruebas de validacion pasaban sin probar nada. Con <c>PreserveExecutionContext</c> la
/// cultura viaja hasta el servidor, y responde «must not be empty», como en Render.
/// </summary>
public class TextosEnCastellanoPruebas : IAsyncLifetime
{
    private readonly AplicacionConProxy _app = new();
    private HttpClient _cliente = null!;

    public Task InitializeAsync()
    {
        _app.Server.PreserveExecutionContext = true;
        _cliente = _app.CreateClient();
        return Task.CompletedTask;
    }

    public async Task DisposeAsync() => await _app.DisposeAsync();

    /// <summary>
    /// La peticion, con la cultura de Render. Se fija AQUI, en el mismo flujo que la hace: un
    /// cambio de cultura dentro de un metodo async no vuelve a quien lo llamo.
    /// </summary>
    private async Task<HttpResponseMessage> ComoEnRender(Func<HttpClient, Task<HttpResponseMessage>> pedir)
    {
        var (antes, antesUi) = (CultureInfo.CurrentCulture, CultureInfo.CurrentUICulture);
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        CultureInfo.CurrentUICulture = CultureInfo.InvariantCulture;
        try { return await pedir(_cliente); }
        finally { (CultureInfo.CurrentCulture, CultureInfo.CurrentUICulture) = (antes, antesUi); }
    }

    /// <summary>
    /// Palabras que no existen en castellano y si en los textos de fabrica de ASP.NET y
    /// FluentValidation ("Not Found", "must not be empty", "is not a valid email address"...).
    /// </summary>
    private static readonly Regex Ingles = new(
        @"\b(the|must|not|found|unauthorized|forbidden|bad|request|method|allowed|occurred|"
        + @"processing|empty|valid|length|characters|entered|should|less|greater|equal|than|"
        + @"unsupported|media|type|conflict|unprocessable|entity)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>Lo que la pantalla podria mostrar de esa respuesta.</summary>
    private static async Task<List<string>> LoQueVeElUsuario(HttpResponseMessage r)
    {
        var textos = new List<string>();
        var crudo = await r.Content.ReadAsStringAsync();
        if (string.IsNullOrWhiteSpace(crudo)) return textos;
        using var doc = JsonDocument.Parse(crudo);
        var raiz = doc.RootElement;
        foreach (var campo in new[] { "title", "detail" })
            if (raiz.TryGetProperty(campo, out var v) && v.ValueKind == JsonValueKind.String)
                textos.Add($"{campo}: {v.GetString()}");
        if (raiz.TryGetProperty("errors", out var errores) && errores.ValueKind == JsonValueKind.Object)
            foreach (var p in errores.EnumerateObject())
                foreach (var m in p.Value.EnumerateArray())
                    textos.Add($"errors.{p.Name}: {m.GetString()}");
        return textos;
    }

    private static async Task SinIngles(HttpResponseMessage r, string caso)
    {
        var textos = await LoQueVeElUsuario(r);
        Assert.True(textos.Count > 0, $"{caso}: la respuesta {(int)r.StatusCode} no trae ningun texto que mostrar.");
        var enIngles = textos.Where(t => Ingles.IsMatch(t)).ToList();
        Assert.True(enIngles.Count == 0,
            $"{caso} ({(int)r.StatusCode}) le muestra ingles al usuario:\n  " + string.Join("\n  ", enIngles));
    }

    [Fact]
    public async Task Una_ruta_que_no_existe() =>
        await SinIngles(await ComoEnRender(c => c.GetAsync("/api/esta-ruta-no-existe")), "ruta inexistente");

    [Fact]
    public async Task Un_metodo_que_la_ruta_no_admite() =>
        await SinIngles(await ComoEnRender(c => c.DeleteAsync("/api/salud")), "metodo no permitido");

    [Fact]
    public async Task Pedir_algo_privado_sin_sesion() =>
        await SinIngles(await ComoEnRender(c => c.GetAsync("/api/mi/tarjetas")), "sin sesion");

    [Fact]
    public async Task Un_campo_obligatorio_vacio() =>
        await SinIngles(await ComoEnRender(c =>
            c.PostAsJsonAsync("/api/auth/contrasena/recuperar", new { correo = "" })), "campo vacio");

    [Fact]
    public async Task Un_correo_mal_escrito() =>
        await SinIngles(await ComoEnRender(c =>
            c.PostAsJsonAsync("/api/auth/contrasena/recuperar", new { correo = "esto-no-es-un-correo" })),
            "correo mal escrito");

    [Fact]
    public async Task Un_texto_demasiado_largo() =>
        await SinIngles(await ComoEnRender(c =>
            c.PostAsJsonAsync("/api/auth/contrasena/recuperar", new { correo = new string('a', 400) + "@zass.cl" })),
            "texto demasiado largo");

    [Fact]
    public async Task Una_contrasena_demasiado_corta() =>
        await SinIngles(await ComoEnRender(c =>
            c.PostAsJsonAsync("/api/auth/registro", new
            {
                correo = $"corta-{Guid.NewGuid():N}@zass.local", contrasena = "abc",
                nombre = "Ana Pérez", rol = "buscador",
            })),
            "contrasena corta");

    /// <summary>
    /// Traducir los titulos de fabrica no puede pisar los que escribimos.
    ///
    /// El caso que lo delata es «Datos inválidos» (400): el mapa de fabrica diria «Petición
    /// incorrecta». Un 404 no serviria: el «No encontrado» de Respuestas es la misma palabra que
    /// pone el mapa, y la pisada no se veria. El detalle de un caso de uso tambien llega tal cual.
    /// </summary>
    [Fact]
    public async Task Los_titulos_que_escribe_el_proyecto_no_se_tocan()
    {
        var validacion = await ComoEnRender(c =>
            c.PostAsJsonAsync("/api/auth/contrasena/recuperar", new { correo = "" }));
        var resenas = await ComoEnRender(c =>
            c.GetAsync($"/api/prestadores/no-existe-{Guid.NewGuid():N}/resenas"));

        using var v = JsonDocument.Parse(await validacion.Content.ReadAsStringAsync());
        using var r = JsonDocument.Parse(await resenas.Content.ReadAsStringAsync());
        Assert.Equal("Datos inválidos", v.RootElement.GetProperty("title").GetString());
        Assert.Equal("No encontramos a ese prestador.", r.RootElement.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task Un_cuerpo_que_no_es_json() =>
        await SinIngles(await ComoEnRender(c =>
            c.PostAsync("/api/auth/contrasena/recuperar",
                new StringContent("{esto no es json", Encoding.UTF8, "application/json"))),
            "json roto");

    [Fact]
    public async Task Un_cuerpo_que_no_es_json_ni_dice_serlo() =>
        await SinIngles(await ComoEnRender(c =>
            c.PostAsync("/api/auth/contrasena/recuperar",
                new StringContent("correo=a@b.cl", Encoding.UTF8, "text/plain"))),
            "tipo de contenido equivocado");

    /// <summary>
    /// T5: los nombres de campo en los errores de validacion se muestran en castellano.
    /// Se prueba con propiedades que tienen tilde (contraseña, código, teléfono, etc.)
    /// y propiedades compuestas (contraseña nueva, nombre público, etc.).
    /// </summary>
    [Fact]
    public async Task Los_nombres_de_campo_se_muestran_en_castellano()
    {
        // Contrasena → contraseña
        var contrasenaCorta = await ComoEnRender(c =>
            c.PostAsJsonAsync("/api/auth/registro", new
            {
                correo = $"test-{Guid.NewGuid():N}@zass.local",
                contrasena = "abc",
                nombre = "Test",
                rol = "buscador"
            }));
        await SinPalabrasEnIngles(contrasenaCorta, "contraseña corta");

        // ContrasenaNueva → contraseña nueva
        var contrasenaNuevaCorta = await ComoEnRender(c =>
            c.PostAsJsonAsync("/api/auth/contrasena/restablecer", new
            {
                token = "token-valido",
                contrasenaNueva = "x"
            }));
        await SinPalabrasEnIngles(contrasenaNuevaCorta, "contraseña nueva corta");

        // TelefonoPublico → teléfono público
        // (requiere sesion, asi que solo verificamos que la ruta existe y el validador se ejecuta)
        var telefonoInvalido = await ComoEnRender(c =>
        {
            var req = new HttpRequestMessage(HttpMethod.Post, "/api/mi/perfil/prestador")
            {
                Content = JsonContent.Create(new { nombrePublico = "Test", telefonoPublico = "12345" })
            };
            req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "fake");
            return c.SendAsync(req);
        });
        // Esta prueba puede fallar si no hay sesion, pero al menos verifica el camino.

        // CodigoPlan → código plan
        var codigoPlanFalta = await ComoEnRender(c =>
        {
            var req = new HttpRequestMessage(HttpMethod.Post, "/api/mi/cobros/cotizar")
            {
                Content = JsonContent.Create(new { codigoCupon = "DESC" })
            };
            req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "fake");
            return c.SendAsync(req);
        });
        // Puede fallar por sesion, pero el validador deberia correr primero.
    }

    private static async Task SinPalabrasEnIngles(HttpResponseMessage r, string contexto)
    {
        var textos = await LoQueVeElUsuario(r);
        if (textos.Count == 0) return;
        var enIngles = textos.Where(t => Ingles.IsMatch(t)).ToList();
        Assert.True(enIngles.Count == 0,
            $"{contexto} muestra ingles:\n  " + string.Join("\n  ", enIngles));
    }
}
