using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Zass.Api;
using Zass.Api.Configuracion;
using Zass.Infraestructura.Datos;

namespace Zass.Integracion.Pruebas;

/// <summary>
/// La API como la veria un proxy. El servidor de pruebas no tiene IP remota, asi que un filtro
/// de arranque la fija ANTES de que corra el resto de la tuberia (y por tanto antes de
/// UseForwardedHeaders), a partir de la cabecera X-Prueba-Remota. Despues de la tuberia
/// añade /__eco, que devuelve la IP y el esquema que la API termino creyendo.
/// </summary>
public sealed class AplicacionConProxy : WebApplicationFactory<Program>
{
    public const string CabeceraRemota = "X-Prueba-Remota";
    private readonly Dictionary<string, string?> _ajustes;
    private readonly string _entorno;

    public AplicacionConProxy(
        string? proxies = null, int limitePublico = 10_000, int? saltos = null,
        string? cadena = null, string entorno = "Development",
        Dictionary<string, string?>? extra = null, int? limiteSalud = null)
    {
        _entorno = entorno;
        _ajustes = new Dictionary<string, string?>
        {
            ["Zass:CadenaConexion"] = cadena ?? Conexion.Cadena,
            ["Zass:ClaveDireccionesBase64"] =
                Convert.ToBase64String(Enumerable.Range(0, 32).Select(i => (byte)i).ToArray()),
            ["Zass:Tasa:A"] = "10000", ["Zass:Tasa:G"] = "10000", ["Zass:Tasa:S"] = "10000",
            ["Zass:Tasa:P"] = limitePublico.ToString(),
        };
        if (limiteSalud is not null) _ajustes["Zass:Tasa:Salud"] = limiteSalud.ToString();
        if (proxies is not null) _ajustes[ProxiesDeConfianza.ClaveRedes] = proxies;
        if (saltos is not null) _ajustes[ProxiesDeConfianza.ClaveSaltos] = saltos.ToString();
        if (extra is not null) foreach (var (k, v) in extra) _ajustes[k] = v;
    }

    protected override void ConfigureWebHost(IWebHostBuilder constructor)
    {
        constructor.UseEnvironment(_entorno);
        constructor.ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(_ajustes));
        constructor.ConfigureServices(s => s.AddTransient<IStartupFilter, FiltroDeProxy>());
    }

    private sealed class FiltroDeProxy : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> siguiente) => app =>
        {
            app.Use(async (http, sig) =>
            {
                var remota = http.Request.Headers[CabeceraRemota].ToString();
                http.Connection.RemoteIpAddress = IPAddress.Parse(
                    string.IsNullOrEmpty(remota) ? "127.0.0.1" : remota);
                await sig();
            });

            siguiente(app);

            app.Run(async http =>
            {
                if (http.Request.Path != "/__eco") { http.Response.StatusCode = 404; return; }
                await http.Response.WriteAsJsonAsync(new
                {
                    ip = http.Connection.RemoteIpAddress?.ToString(),
                    esquema = http.Request.Scheme,
                    https = http.Request.IsHttps,
                });
            });
        };
    }

    public HttpClient Cliente(bool https = true) => CreateClient(new WebApplicationFactoryClientOptions
    {
        AllowAutoRedirect = false,
        BaseAddress = new Uri(https ? "https://localhost" : "http://localhost"),
    });

    public static HttpRequestMessage Pedir(
        string ruta, string remota, string? xff = null, string? proto = null)
    {
        var p = new HttpRequestMessage(HttpMethod.Get, ruta);
        p.Headers.Add(CabeceraRemota, remota);
        if (xff is not null) p.Headers.Add("X-Forwarded-For", xff);
        if (proto is not null) p.Headers.Add("X-Forwarded-Proto", proto);
        return p;
    }
}

/// <summary>El limite por IP detras de un proxy: que cuente al visitante y que no se deje engañar.</summary>
public class ProxiesDeConfianzaPruebas
{
    private static async Task<HttpStatusCode> Salud(HttpClient c, string remota, string? xff = null) =>
        (await c.SendAsync(AplicacionConProxy.Pedir("/api/salud", remota, xff))).StatusCode;

    private static async Task<JsonElement> Eco(HttpClient c, string remota, string? xff, string? proto = null)
    {
        var r = await c.SendAsync(AplicacionConProxy.Pedir("/__eco", remota, xff, proto));
        return (await r.Content.ReadFromJsonAsync<JsonElement>());
    }

    [Fact]
    public async Task Con_proxy_de_confianza_cada_visitante_tiene_su_contador_y_la_misma_IP_agota_el_suyo()
    {
        using var app = new AplicacionConProxy(proxies: "10.0.0.0/8", limitePublico: 3);
        using var c = app.Cliente();

        for (var i = 0; i < 3; i++)
            Assert.Equal(HttpStatusCode.OK, await Salud(c, "10.0.0.1", "203.0.113.5"));
        Assert.Equal(HttpStatusCode.TooManyRequests, await Salud(c, "10.0.0.1", "203.0.113.5"));

        // Otro visitante detras del MISMO proxy: contador propio, no el de toda la ciudad.
        Assert.Equal(HttpStatusCode.OK, await Salud(c, "10.0.0.1", "203.0.113.6"));
    }

    [Fact]
    public async Task Desde_un_origen_NO_confiable_rotar_X_Forwarded_For_no_evade_el_limite()
    {
        using var app = new AplicacionConProxy(proxies: "10.0.0.0/8", limitePublico: 3);
        using var c = app.Cliente();

        for (var i = 0; i < 3; i++)
            Assert.Equal(HttpStatusCode.OK, await Salud(c, "198.51.100.9", $"203.0.113.{i + 1}"));

        Assert.Equal(HttpStatusCode.TooManyRequests, await Salud(c, "198.51.100.9", "203.0.113.200"));
    }

    [Fact]
    public async Task Sin_configuracion_las_cabeceras_X_Forwarded_se_ignoran_incluso_desde_un_proxy()
    {
        using var app = new AplicacionConProxy(proxies: null, limitePublico: 3);
        using var c = app.Cliente();

        for (var i = 0; i < 3; i++)
            Assert.Equal(HttpStatusCode.OK, await Salud(c, "10.0.0.1", $"203.0.113.{i + 1}"));

        Assert.Equal(HttpStatusCode.TooManyRequests, await Salud(c, "10.0.0.1", "203.0.113.99"));

        var eco = await Eco(c, "10.0.0.1", "203.0.113.5", "http");
        Assert.Equal("10.0.0.1", eco.GetProperty("ip").GetString());
    }

    [Fact]
    public async Task La_IP_y_el_esquema_de_la_peticion_son_los_del_visitante_si_el_proxy_es_de_confianza()
    {
        using var app = new AplicacionConProxy(proxies: "10.0.0.0/8");
        using var c = app.Cliente(https: false);   // lo que ve la API tras Render: http plano

        var eco = await Eco(c, "10.0.0.1", "203.0.113.5", "https");

        Assert.Equal("203.0.113.5", eco.GetProperty("ip").GetString());
        Assert.Equal("https", eco.GetProperty("esquema").GetString());
        Assert.True(eco.GetProperty("https").GetBoolean());
    }

    [Fact]
    public async Task Un_origen_no_confiable_no_puede_declararse_https_ni_cambiar_su_IP()
    {
        using var app = new AplicacionConProxy(proxies: "10.0.0.0/8");
        using var c = app.Cliente(https: false);

        var eco = await Eco(c, "198.51.100.9", "1.2.3.4", "https");

        Assert.Equal("198.51.100.9", eco.GetProperty("ip").GetString());
        Assert.False(eco.GetProperty("https").GetBoolean());
    }

    [Fact]
    public async Task Lo_que_el_atacante_escribe_a_la_izquierda_de_la_cadena_no_se_lee()
    {
        // El atacante manda «1.2.3.4» y el proxy de confianza le añade la IP verdadera a la
        // derecha: se lee la verdadera, no la inventada.
        using var app = new AplicacionConProxy(proxies: "10.0.0.0/8");
        using var c = app.Cliente();

        var eco = await Eco(c, "10.0.0.1", "1.2.3.4, 203.0.113.5", null);

        Assert.Equal("203.0.113.5", eco.GetProperty("ip").GetString());
    }

    [Theory]
    [InlineData(1, "10.1.1.1")]   // un solo salto: se queda con el proxy interno
    [InlineData(2, "9.9.9.9")]    // dos saltos: llega al visitante
    public async Task El_limite_de_saltos_se_respeta(int saltos, string esperada)
    {
        // Cadena: visitante 9.9.9.9 -> proxy 10.1.1.1 (de confianza) -> conexion directa 10.0.0.1.
        using var app = new AplicacionConProxy(proxies: "10.0.0.0/8", saltos: saltos);
        using var c = app.Cliente();

        var eco = await Eco(c, "10.0.0.1", "9.9.9.9, 10.1.1.1", null);

        Assert.Equal(esperada, eco.GetProperty("ip").GetString());
    }

    [Fact]
    public async Task El_limite_de_saltos_por_omision_es_dos()
    {
        using var app = new AplicacionConProxy(proxies: "10.0.0.0/8");
        using var c = app.Cliente();

        var eco = await Eco(c, "10.0.0.1", "9.9.9.9, 10.1.1.1, 10.2.2.2", null);

        // Tres entradas de confianza a la derecha: con 2 saltos se llega a 10.1.1.1... no mas.
        Assert.Equal("10.1.1.1", eco.GetProperty("ip").GetString());
    }

    [Fact]
    public async Task Una_red_fuera_de_la_lista_no_es_de_confianza_aunque_se_parezca()
    {
        using var app = new AplicacionConProxy(proxies: "10.0.0.0/24");
        using var c = app.Cliente();

        var eco = await Eco(c, "10.0.1.1", "203.0.113.5", null);   // 10.0.1.x esta fuera del /24

        Assert.Equal("10.0.1.1", eco.GetProperty("ip").GetString());
    }

    [Fact]
    public async Task Loopback_no_es_de_confianza_salvo_que_se_liste()
    {
        // Los valores por omision de ASP.NET confian en 127.0.0.1; aqui no.
        using var app = new AplicacionConProxy(proxies: "10.0.0.0/8");
        using var c = app.Cliente();

        var eco = await Eco(c, "127.0.0.1", "203.0.113.5", null);

        Assert.Equal("127.0.0.1", eco.GetProperty("ip").GetString());
    }

    [Fact]
    public async Task La_lista_admite_texto_separado_por_comas_como_llega_de_una_variable_de_entorno()
    {
        using var app = new AplicacionConProxy(proxies: "173.245.48.0/20, 10.0.0.0/8 ,2400:cb00::/32");
        using var c = app.Cliente();

        var eco = await Eco(c, "10.0.0.1", "203.0.113.5", null);

        Assert.Equal("203.0.113.5", eco.GetProperty("ip").GetString());
    }

    [Fact]
    public async Task Un_origen_no_confiable_no_puede_falsear_la_IP_de_la_auditoria()
    {
        // Mismo recorrido que la prueba de abajo pero desde un origen que NO es proxy: la
        // cabecera inventada no puede acabar en la auditoria.
        using var app = new AplicacionConProxy(proxies: "10.0.0.0/8");
        using var c = app.Cliente();
        var falsa = $"198.19.{Random.Shared.Next(1, 255)}.{Random.Shared.Next(1, 255)}";
        var real = $"198.51.100.{Random.Shared.Next(1, 255)}";
        var correo = $"prod-{Guid.NewGuid():N}@zass.local";

        try
        {
            var peticion = new HttpRequestMessage(HttpMethod.Post, "/api/auth/registro")
            {
                Content = JsonContent.Create(new
                {
                    correo, contrasena = "contrasena-larga-1", nombre = "Prueba Falsa", rol = "buscador",
                }),
            };
            peticion.Headers.Add(AplicacionConProxy.CabeceraRemota, real);
            peticion.Headers.Add("X-Forwarded-For", falsa);
            Assert.Equal(HttpStatusCode.Created, (await c.SendAsync(peticion)).StatusCode);

            using var ctx = ContextoDePrueba.Crear();
            var usuario = await ctx.Usuarios.SingleAsync(u => u.Correo == correo);
            var filas = await ctx.Auditoria
                .Where(a => a.ActorId == usuario.Id || a.EntidadId == usuario.Id).ToListAsync();
            Assert.NotEmpty(filas);
            Assert.All(filas, a => Assert.Equal(real, a.IpOrigen));
        }
        finally
        {
            using var ctx = ContextoDePrueba.Crear();
            var usuario = await ctx.Usuarios.FirstOrDefaultAsync(u => u.Correo == correo);
            if (usuario is not null)
            {
                ctx.Auditoria.RemoveRange(
                    ctx.Auditoria.Where(a => a.ActorId == usuario.Id || a.EntidadId == usuario.Id));
                ctx.Usuarios.Remove(usuario);
                await ctx.SaveChangesAsync();
            }
        }
    }

    [Fact]
    public void La_cookie_antifalsificacion_exige_Secure_aunque_la_API_vea_http_tras_el_proxy()
    {
        // Render termina TLS y la API ve http. Con la politica por omision (SameAsRequest) la
        // cookie saldria sin Secure salvo que el proxy fuese de confianza. Se mira la opcion
        // y no la respuesta: por http el servidor de pruebas redirige antes de emitir nada.
        using var app = new AplicacionConProxy();
        using var cliente = app.Cliente();

        var opciones = app.Services
            .GetRequiredService<Microsoft.Extensions.Options.IOptions<Microsoft.AspNetCore.Antiforgery.AntiforgeryOptions>>()
            .Value;

        Assert.Equal(CookieSecurePolicy.Always, opciones.Cookie.SecurePolicy);
    }

    [Fact]
    public async Task La_IP_corregida_llega_a_la_auditoria_de_verdad()
    {
        // Ip(http) de EndpointsIdentidad: lo que se guarda para auditoria y sesiones.
        using var app = new AplicacionConProxy(proxies: "10.0.0.0/8");
        using var c = app.Cliente();
        var ip = $"198.18.{Random.Shared.Next(1, 255)}.{Random.Shared.Next(1, 255)}";
        var correo = $"prod-{Guid.NewGuid():N}@zass.local";

        try
        {
            var peticion = new HttpRequestMessage(HttpMethod.Post, "/api/auth/registro")
            {
                Content = JsonContent.Create(new
                {
                    correo, contrasena = "contrasena-larga-1", nombre = "Prueba Proxy", rol = "buscador",
                }),
            };
            peticion.Headers.Add(AplicacionConProxy.CabeceraRemota, "10.0.0.1");
            peticion.Headers.Add("X-Forwarded-For", ip);
            var r = await c.SendAsync(peticion);
            Assert.Equal(HttpStatusCode.Created, r.StatusCode);

            using var ctx = ContextoDePrueba.Crear();
            Assert.True(await ctx.Auditoria.AnyAsync(a => a.IpOrigen == ip),
                "La auditoria no guardo la IP del visitante.");
            var usuario = await ctx.Usuarios.SingleAsync(u => u.Correo == correo);
            var filas = await ctx.Auditoria
                .Where(a => a.ActorId == usuario.Id || a.EntidadId == usuario.Id).ToListAsync();
            Assert.NotEmpty(filas);
            Assert.All(filas, a => Assert.Equal(ip, a.IpOrigen));   // y ninguna con la IP del proxy
        }
        finally
        {
            using var ctx = ContextoDePrueba.Crear();
            var usuario = await ctx.Usuarios.FirstOrDefaultAsync(u => u.Correo == correo);
            if (usuario is not null)
            {
                ctx.Auditoria.RemoveRange(
                    ctx.Auditoria.Where(a => a.ActorId == usuario.Id || a.EntidadId == usuario.Id));
                ctx.Usuarios.Remove(usuario);
                await ctx.SaveChangesAsync();
            }
        }
    }
}

/// <summary>Configuracion peligrosa o incompleta: la API no arranca, y no dice secretos.</summary>
public class ValidacionDeArranquePruebas
{
    private const string Clave32 = "AAECAwQFBgcICQoLDA0ODxAREhMUFRYXGBkaGxwdHh8=";
    private const string CadenaSegura = "Host=db.ejemplo.neon.tech;Database=zass;Username=zass;Password=SECRETO-XYZ-123;SSL Mode=Require";

    private static IConfiguration Config(params (string, string?)[] valores) =>
        new ConfigurationBuilder().AddInMemoryCollection(
            valores.Select(v => new KeyValuePair<string, string?>(v.Item1, v.Item2))).Build();

    private static IConfiguration Buena(params (string, string?)[] cambios)
    {
        var d = new Dictionary<string, string?>
        {
            ["Zass:CadenaConexion"] = CadenaSegura,
            ["Zass:ClaveDireccionesBase64"] = Clave32,
        };
        foreach (var (k, v) in cambios) d[k] = v;
        return new ConfigurationBuilder().AddInMemoryCollection(d).Build();
    }

    [Fact]
    public void Una_configuracion_completa_de_produccion_no_da_problemas() =>
        Assert.Empty(ValidacionDeArranque.Validar(Buena(), esProductivo: true));

    [Fact]
    public void Falta_la_cadena_de_conexion() =>
        Assert.Contains(ValidacionDeArranque.Validar(Buena(("Zass:CadenaConexion", "")), true),
            p => p.Contains("CadenaConexion"));

    [Fact]
    public void Una_URL_postgresql_se_rechaza_con_una_pista_y_sin_copiar_la_clave()
    {
        var url = "postgresql://zass:SECRETO-XYZ-123@host/zass?sslmode=require";
        var problemas = ValidacionDeArranque.Validar(Buena(("Zass:CadenaConexion", url)), true);

        Assert.Contains(problemas, p => p.Contains("postgresql://"));
        Assert.DoesNotContain(problemas, p => p.Contains("SECRETO-XYZ-123"));
    }

    [Theory]
    [InlineData("Disable")]
    [InlineData("Prefer")]
    public void En_produccion_la_base_exige_SSL(string modo) =>
        Assert.Contains(ValidacionDeArranque.Validar(
            Buena(("Zass:CadenaConexion", $"Host=h;Database=d;Username=u;Password=p;SSL Mode={modo}")), true),
            p => p.Contains("SSL Mode"));

    [Fact]
    public void En_desarrollo_la_base_local_sin_SSL_es_valida() =>
        Assert.Empty(ValidacionDeArranque.Validar(
            Buena(("Zass:CadenaConexion", "Host=127.0.0.1;Database=zass;Username=u;Password=p")), esProductivo: false));

    [Theory]
    [InlineData("")]
    [InlineData("no-es-base64-!!")]
    [InlineData("YWJj")]
    public void La_clave_de_cifrado_debe_ser_Base64_de_32_bytes(string clave)
    {
        var problemas = ValidacionDeArranque.Validar(Buena(("Zass:ClaveDireccionesBase64", clave)), true);

        Assert.Contains(problemas, p => p.Contains("ClaveDireccionesBase64"));
        if (clave.Length > 0) Assert.DoesNotContain(problemas, p => p.Contains(clave));
    }

    [Fact]
    public void En_produccion_ExigirHttps_no_puede_apagarse() =>
        Assert.Contains(ValidacionDeArranque.Validar(Buena(("Zass:ExigirHttps", "false")), true),
            p => p.Contains("ExigirHttps"));

    [Fact]
    public void En_desarrollo_ExigirHttps_si_puede_apagarse() =>
        Assert.Empty(ValidacionDeArranque.Validar(Buena(("Zass:ExigirHttps", "false")), false));

    [Theory]
    [InlineData("0.0.0.0/0")]
    [InlineData("::/0")]
    [InlineData("no-es-una-ip")]
    [InlineData("10.0.0.0/33")]
    [InlineData("10.0.0.0/8/9")]
    public void Un_proxy_de_confianza_invalido_o_universal_se_rechaza(string red) =>
        Assert.Contains(ValidacionDeArranque.Validar(Buena((ProxiesDeConfianza.ClaveRedes, red)), true),
            p => p.Contains("ProxiesDeConfianza"));

    [Theory]
    [InlineData("0")]
    [InlineData("6")]
    [InlineData("dos")]
    public void Los_saltos_de_proxy_fuera_de_rango_se_rechazan(string saltos) =>
        Assert.Contains(ValidacionDeArranque.Validar(Buena((ProxiesDeConfianza.ClaveSaltos, saltos)), true),
            p => p.Contains("SaltosDeProxy"));

    [Fact]
    public void Se_informan_todos_los_problemas_juntos()
    {
        var problemas = ValidacionDeArranque.Validar(
            Config(("Zass:ExigirHttps", "false")), esProductivo: true);

        Assert.True(problemas.Count >= 3);
    }

    [Fact]
    public void En_produccion_una_configuracion_mala_impide_arrancar_y_el_mensaje_no_filtra_secretos()
    {
        using var app = new AplicacionConProxy(
            entorno: "Production",
            cadena: "Host=h;Database=d;Username=u;Password=SECRETO-XYZ-123;SSL Mode=Disable",
            extra: new() { ["Zass:ExigirHttps"] = "false" });

        var error = Assert.Throws<InvalidOperationException>(() => app.CreateClient());

        Assert.Contains("ExigirHttps", error.Message);
        Assert.DoesNotContain("SECRETO-XYZ-123", error.ToString());
    }

    [Fact]
    public async Task En_produccion_Swagger_no_existe()
    {
        using var app = new AplicacionConProxy(entorno: "Production", cadena: CadenaSegura);
        using var c = app.Cliente();

        Assert.Equal(HttpStatusCode.NotFound, (await c.GetAsync("/swagger/v1/swagger.json")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await c.GetAsync("/swagger/index.html")).StatusCode);
    }

    [Fact]
    public async Task En_desarrollo_Swagger_si_existe_lo_que_demuestra_que_la_prueba_anterior_mide_algo()
    {
        using var app = new AplicacionConProxy(entorno: "Development");
        using var c = app.Cliente();

        Assert.Equal(HttpStatusCode.OK, (await c.GetAsync("/swagger/v1/swagger.json")).StatusCode);
    }
}

/// <summary>/api/salud/lista: lista solo si la base responde, y sin contar nada interno.</summary>
public class SaludPruebas
{
    [Fact]
    public async Task Con_la_base_viva_la_sonda_de_lista_responde_200()
    {
        using var app = new AplicacionConProxy();
        using var c = app.Cliente();

        var r = await c.GetAsync("/api/salud/lista");

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Equal("lista", (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("estado").GetString());
    }

    [Fact]
    public async Task Con_la_base_caida_responde_503_y_no_filtra_detalles()
    {
        // Puerto 1: nadie escucha. La cadena lleva una clave reconocible para buscarla luego.
        using var app = new AplicacionConProxy(
            cadena: "Host=127.0.0.1;Port=1;Database=zass;Username=u;Password=SECRETO-XYZ-123;Timeout=2");
        using var c = app.Cliente();

        var r = await c.GetAsync("/api/salud/lista");
        var cuerpo = await r.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.ServiceUnavailable, r.StatusCode);
        Assert.Equal("{\"estado\":\"no-lista\"}", cuerpo);
        Assert.DoesNotContain("SECRETO-XYZ-123", cuerpo);
        Assert.DoesNotContain("127.0.0.1", cuerpo);
    }

    [Fact]
    public async Task Una_base_que_acepta_la_conexion_pero_no_contesta_da_503_en_unos_segundos()
    {
        // Un servidor que acepta TCP y calla: la base «colgada», el caso de verdad peligroso
        // para una sonda (una refusada falla al instante y no prueba nada). Sin el limite de
        // espera, la sonda esperaria los 30 s de Timeout de la cadena.
        var oyente = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        oyente.Start();
        var aceptadas = new List<System.Net.Sockets.TcpClient>();
        _ = Task.Run(async () =>
        {
            try { while (true) { var cl = await oyente.AcceptTcpClientAsync(); lock (aceptadas) aceptadas.Add(cl); } }
            catch { /* el oyente se paro */ }
        });
        var puerto = ((IPEndPoint)oyente.LocalEndpoint).Port;
        var app = new AplicacionConProxy(
            cadena: $"Host=127.0.0.1;Port={puerto};Database=zass;Username=u;Password=p;Timeout=30;Command Timeout=30;SSL Mode=Disable");
        try
        {
            using var c = app.Cliente();

            var reloj = System.Diagnostics.Stopwatch.StartNew();
            var r = await c.GetAsync("/api/salud/lista");
            reloj.Stop();

            Assert.Equal(HttpStatusCode.ServiceUnavailable, r.StatusCode);
            Assert.True(reloj.Elapsed < TimeSpan.FromSeconds(10),
                $"La sonda tardo {reloj.Elapsed.TotalSeconds:F1} s: el limite de espera no corta.");
        }
        finally
        {
            // Primero se cierran las conexiones colgadas y recien despues se libera la
            // aplicacion: liberar el origen de datos de Npgsql con una conexion a medio abrir
            // bloqueaba la prueba durante minutos (intermitente) y la dejaba sin terminar.
            oyente.Stop();
            lock (aceptadas) foreach (var cl in aceptadas) cl.Dispose();
            app.Dispose();
        }
    }

    [Fact]
    public async Task La_sonda_de_vida_sigue_respondiendo_aunque_la_base_este_caida()
    {
        using var app = new AplicacionConProxy(cadena: "Host=127.0.0.1;Port=1;Database=zass;Username=u;Password=p;Timeout=2");
        using var c = app.Cliente();

        Assert.Equal(HttpStatusCode.OK, (await c.GetAsync("/api/salud")).StatusCode);
    }

    [Fact]
    public async Task La_sonda_de_lista_tiene_limite_de_tasa_propio_y_no_gasta_el_cupo_publico()
    {
        using var app = new AplicacionConProxy(limitePublico: 2, limiteSalud: 3);
        using var c = app.Cliente();

        for (var i = 0; i < 3; i++)
            Assert.Equal(HttpStatusCode.OK, (await c.GetAsync("/api/salud/lista")).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await c.GetAsync("/api/salud/lista")).StatusCode);

        // El cupo publico (2) sigue intacto: la sonda no lo toco.
        Assert.Equal(HttpStatusCode.OK, (await c.GetAsync("/api/salud")).StatusCode);
    }
}

/// <summary>El modo --migrar: aplica, es idempotente y no deja la base a medias sin avisar.</summary>
public class MigradorPruebas
{
    private const string Base = "zass_pruebas_produccion_migrar";

    private static string CadenaDe(string baseDeDatos) =>
        new NpgsqlConnectionStringBuilder(Conexion.Cadena) { Database = baseDeDatos }.ConnectionString;

    private static void Recrear(string nombre, bool crear)
    {
        NpgsqlConnection.ClearAllPools();
        var mant = new NpgsqlConnectionStringBuilder(Conexion.Cadena) { Database = "postgres" };
        using var con = new NpgsqlConnection(mant.ConnectionString);
        con.Open();
        using (var d = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{nombre}\" WITH (FORCE)", con))
            d.ExecuteNonQuery();
        if (crear)
            using (var d = new NpgsqlCommand($"CREATE DATABASE \"{nombre}\"", con))
                d.ExecuteNonQuery();
    }

    [Fact]
    public async Task Migrar_aplica_todo_en_una_base_vacia_y_una_segunda_vez_no_hace_nada()
    {
        Recrear(Base, crear: true);
        try
        {
            using var app = new AplicacionConProxy(cadena: CadenaDe(Base));
            using var cliente = app.Cliente();   // levanta el host (y valida la configuracion)

            Assert.Equal(0, await Migrador.AplicarAsync(app.Services, NullLogger.Instance));

            using (var ambito = app.Services.CreateScope())
            {
                var db = ambito.ServiceProvider.GetRequiredService<ZassDbContext>();
                Assert.Empty(await db.Database.GetPendingMigrationsAsync());
                Assert.NotEmpty(await db.Database.GetAppliedMigrationsAsync());
                Assert.True(await db.Roles.CountAsync() >= 5, "Los datos de catalogo no se sembraron.");
            }

            Assert.Equal(0, await Migrador.AplicarAsync(app.Services, NullLogger.Instance));
        }
        finally { Recrear(Base, crear: false); }
    }

    [Fact]
    public async Task Migrar_contra_una_base_inalcanzable_sale_con_error()
    {
        using var app = new AplicacionConProxy(
            cadena: "Host=127.0.0.1;Port=1;Database=zass;Username=u;Password=p;Timeout=2");
        using var cliente = app.Cliente();

        Assert.Equal(1, await Migrador.AplicarAsync(app.Services, NullLogger.Instance));
    }

    [Theory]
    [InlineData("--migrar", true)]
    [InlineData("--MIGRAR", true)]
    [InlineData("--otra", false)]
    public void La_bandera_se_reconoce(string arg, bool esperado) =>
        Assert.Equal(esperado, Migrador.Solicitado(new[] { "x", arg }));
}
