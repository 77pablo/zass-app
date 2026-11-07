using Microsoft.AspNetCore.Hosting;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Zass.Dominio.Comun;

namespace Zass.Integracion.Pruebas;

/// <summary>
/// Levanta Zass.Api entera contra la base real y le pide por HTTP. Es la unica forma de
/// comprobar las cabeceras, las cookies, el CSRF, el limite de tasa y los codigos de
/// estado: nada de eso existe en la capa de aplicacion.
/// </summary>
public sealed class Aplicacion : WebApplicationFactory<Program>
{
    private readonly Dictionary<string, string?> _ajustes;

    public Aplicacion(int? limiteAutenticacion = null)
    {
        _ajustes = new Dictionary<string, string?>
        {
            ["Zass:CadenaConexion"] = Conexion.Cadena,
            ["Zass:ClaveDireccionesBase64"] =
                Convert.ToBase64String(Enumerable.Range(0, 32).Select(i => (byte)i).ToArray()),

            // Las pruebas salen todas de 127.0.0.1 y comparten cupo. Salvo la prueba que
            // mide el limite, las demas lo suben para no estorbarse entre si.
            ["Zass:Tasa:A"] = (limiteAutenticacion ?? 10_000).ToString(),
            ["Zass:Tasa:G"] = "10000",
            ["Zass:Tasa:P"] = "10000",
            ["Zass:Tasa:S"] = "10000",
        };
    }

    protected override void ConfigureWebHost(IWebHostBuilder constructor)
    {
        constructor.UseEnvironment(Environments.Development);
        constructor.ConfigureAppConfiguration(
            (_, config) => config.AddInMemoryCollection(_ajustes));
    }
}

public class ApiPruebas : IAsyncLifetime
{
    private readonly Aplicacion _app = new();
    private readonly List<string> _correosCreados = new();
    private HttpClient _cliente = null!;

    public Task InitializeAsync()
    {
        _cliente = _app.CreateClient(new WebApplicationFactoryClientOptions
        {
            HandleCookies = true,
            AllowAutoRedirect = false,
        });
        return Task.CompletedTask;
    }

    /// <summary>
    /// Por HTTP no hay transaccion que deshacer: las filas quedan. Se borran al final,
    /// una por una, por el correo que esta prueba creo.
    /// </summary>
    public async Task DisposeAsync()
    {
        if (_correosCreados.Count > 0)
        {
            using var ctx = ContextoDePrueba.Crear();
            foreach (var correo in _correosCreados)
            {
                var usuario = await ctx.Usuarios.FirstOrDefaultAsync(u => u.Correo == correo);
                if (usuario is null) continue;

                ctx.Auditoria.RemoveRange(
                    ctx.Auditoria.Where(a => a.ActorId == usuario.Id || a.EntidadId == usuario.Id));
                ctx.Usuarios.Remove(usuario);   // el resto cae en cascada
                await ctx.SaveChangesAsync();
            }
        }

        _cliente.Dispose();
        _app.Dispose();
    }

    // ------------------------------------------------------------ cabeceras

    [Theory]
    [InlineData("Content-Security-Policy")]
    [InlineData("Strict-Transport-Security")]
    [InlineData("X-Content-Type-Options")]
    [InlineData("X-Frame-Options")]
    [InlineData("Referrer-Policy")]
    [InlineData("Permissions-Policy")]
    [InlineData("Cross-Origin-Opener-Policy")]
    public async Task Toda_respuesta_trae_las_siete_cabeceras_de_seguridad(string cabecera)
    {
        var respuesta = await _cliente.GetAsync("/api/salud");

        Assert.True(respuesta.Headers.Contains(cabecera),
            $"Falta la cabecera {cabecera}.");
    }

    [Fact]
    public async Task La_politica_de_contenido_no_permite_guiones_ni_estilos_en_linea()
    {
        var respuesta = await _cliente.GetAsync("/api/salud");
        var csp = string.Join(" ", respuesta.Headers.GetValues("Content-Security-Policy"));

        Assert.DoesNotContain("unsafe-inline", csp);
        Assert.DoesNotContain("unsafe-eval", csp);
        Assert.Contains("frame-ancestors 'none'", csp);
    }

    [Fact]
    public async Task Las_cabeceras_acompanan_tambien_a_una_respuesta_de_error()
    {
        // Si el middleware estuviera despues del enrutado, un 404 saldria desnudo.
        var respuesta = await _cliente.GetAsync("/api/no-existe-esta-ruta");

        Assert.Equal(HttpStatusCode.NotFound, respuesta.StatusCode);
        Assert.True(respuesta.Headers.Contains("X-Frame-Options"));
    }

    // -------------------------------------------------------------- sesion

    [Fact]
    public async Task Sin_sesion_mi_cuenta_responde_401_y_no_404()
    {
        var respuesta = await _cliente.GetAsync("/api/mi/cuenta");

        Assert.Equal(HttpStatusCode.Unauthorized, respuesta.StatusCode);
    }

    [Fact]
    public async Task La_cadena_completa_funciona_por_HTTP()
    {
        var correo = NuevoCorreo();

        var alta = await _cliente.PostAsJsonAsync("/api/auth/registro", new
        {
            correo, contrasena = "contrasena-larga-1", nombre = "Pablo Espinoza", rol = "prestador",
        });
        Assert.Equal(HttpStatusCode.Created, alta.StatusCode);

        var token = await TokenDeVerificacionAsync(correo);
        var verificado = await _cliente.PostAsJsonAsync(
            "/api/auth/correo/verificar", new { token });
        Assert.Equal(HttpStatusCode.NoContent, verificado.StatusCode);

        var entrada = await _cliente.PostAsJsonAsync("/api/auth/sesion", new
        {
            correo, contrasena = "contrasena-larga-1",
        });
        Assert.Equal(HttpStatusCode.OK, entrada.StatusCode);

        // La cookie de sesion tiene que ser httpOnly: ningun guion de la pagina puede
        // leerla, ni siquiera uno inyectado.
        var cookies = entrada.Headers.GetValues("Set-Cookie").ToList();
        Assert.Contains(cookies, c => c.StartsWith("zass_sesion") && c.Contains("httponly", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(cookies, c => c.StartsWith("zass_refresco") && c.Contains("httponly", StringComparison.OrdinalIgnoreCase));
        Assert.All(cookies, c => Assert.Contains("samesite=lax", c.ToLowerInvariant()));

        var cuenta = await _cliente.GetAsync("/api/mi/cuenta");
        Assert.Equal(HttpStatusCode.OK, cuenta.StatusCode);

        var json = await cuenta.Content.ReadFromJsonAsync<JsonDocument>();
        var raiz = json!.RootElement;
        Assert.Equal(correo, raiz.GetProperty("correo").GetString());
        Assert.True(raiz.GetProperty("correoVerificado").GetBoolean());

        // Y lo que no tiene que estar: el hash nunca sale por la API.
        Assert.False(raiz.TryGetProperty("hashContrasena", out _));
    }

    [Fact]
    public async Task Una_contrasena_mala_y_un_correo_inexistente_dan_la_misma_respuesta()
    {
        var correo = NuevoCorreo();
        await RegistrarAsync(correo);

        var malaClave = await _cliente.PostAsJsonAsync("/api/auth/sesion", new
        {
            correo, contrasena = "otra-contrasena-larga",
        });
        var sinCuenta = await _cliente.PostAsJsonAsync("/api/auth/sesion", new
        {
            correo = "nadie-" + Guid.NewGuid().ToString("N") + "@zass.local",
            contrasena = "contrasena-larga-1",
        });

        Assert.Equal(HttpStatusCode.Unauthorized, malaClave.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, sinCuenta.StatusCode);
        Assert.Equal(
            await sinCuenta.Content.ReadAsStringAsync(),
            await malaClave.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Cerrar_sesion_deja_la_cuenta_inaccesible()
    {
        var correo = NuevoCorreo();
        await RegistrarAsync(correo);
        await IniciarSesionAsync(correo);

        var csrf = await TokenCsrfAsync();
        var peticion = new HttpRequestMessage(HttpMethod.Delete, "/api/auth/sesion");
        peticion.Headers.Add("X-CSRF-Token", csrf);
        var salida = await _cliente.SendAsync(peticion);

        Assert.Equal(HttpStatusCode.NoContent, salida.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await _cliente.GetAsync("/api/mi/cuenta")).StatusCode);
    }

    // ---------------------------------------------------------------- CSRF

    [Fact]
    public async Task Un_cambio_sin_token_CSRF_se_rechaza_con_403()
    {
        var correo = NuevoCorreo();
        await RegistrarAsync(correo);
        await IniciarSesionAsync(correo);

        var respuesta = await _cliente.PutAsJsonAsync("/api/mi/perfil-buscador", new
        {
            zonaInteresId = (int?)null, radioPreferidoKm = 25,
            aceptaAvisos = true, canalCorreo = true, canalPush = false,
        });

        Assert.Equal(HttpStatusCode.Forbidden, respuesta.StatusCode);
    }

    [Fact]
    public async Task El_mismo_cambio_con_token_CSRF_pasa()
    {
        var correo = NuevoCorreo();
        await RegistrarAsync(correo);
        await IniciarSesionAsync(correo);

        var peticion = new HttpRequestMessage(HttpMethod.Put, "/api/mi/perfil-buscador")
        {
            Content = JsonContent.Create(new
            {
                zonaInteresId = (int?)null, radioPreferidoKm = 25,
                aceptaAvisos = true, canalCorreo = true, canalPush = false,
            }),
        };
        peticion.Headers.Add("X-CSRF-Token", await TokenCsrfAsync());

        var respuesta = await _cliente.SendAsync(peticion);

        Assert.Equal(HttpStatusCode.NoContent, respuesta.StatusCode);
    }

    // ---------------------------------------------------------- validacion

    [Fact]
    public async Task Un_cuerpo_invalido_devuelve_400_con_el_campo_y_sin_el_valor_enviado()
    {
        var respuesta = await _cliente.PostAsJsonAsync("/api/auth/registro", new
        {
            correo = "no-es-un-correo", contrasena = "corta",
            nombre = "", rol = "administrador",
        });

        Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);
        var cuerpo = await respuesta.Content.ReadAsStringAsync();

        Assert.Contains("Correo", cuerpo);
        Assert.Contains("Rol", cuerpo);
        // El valor enviado no se devuelve: el error dice que campo, no que escribio.
        Assert.DoesNotContain("no-es-un-correo", cuerpo);
    }

    [Fact]
    public async Task Pedir_el_rol_administrador_al_registrarse_se_rechaza()
    {
        var respuesta = await _cliente.PostAsJsonAsync("/api/auth/registro", new
        {
            correo = NuevoCorreo(), contrasena = "contrasena-larga-1",
            nombre = "Colado", rol = "administrador",
        });

        Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);
    }

    // ------------------------------------------------- codigos de estado

    [Fact]
    public async Task Publicar_sin_el_minimo_publicable_devuelve_422()
    {
        var correo = NuevoCorreo();
        await RegistrarAsync(correo, rol: "prestador");
        await VerificarAsync(correo);
        await IniciarSesionAsync(correo);

        var peticion = new HttpRequestMessage(
            HttpMethod.Post, "/api/mi/perfil-prestador/publicar");
        peticion.Headers.Add("X-CSRF-Token", await TokenCsrfAsync());
        var respuesta = await _cliente.SendAsync(peticion);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, respuesta.StatusCode);
        Assert.Contains("rubro", await respuesta.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Editar_con_una_version_vieja_devuelve_412()
    {
        var correo = NuevoCorreo();
        await RegistrarAsync(correo, rol: "prestador");
        await IniciarSesionAsync(correo);
        var csrf = await TokenCsrfAsync();

        var peticion = new HttpRequestMessage(HttpMethod.Put, "/api/mi/perfil-prestador")
        {
            Content = JsonContent.Create(new
            {
                nombrePublico = "Estudio Espinoza", descripcion = (string?)null,
                rubroId = (short?)null, telefonoPublico = (string?)null,
                correoPublico = (string?)null, whatsapp = (string?)null,
                sitioWeb = (string?)null, direccion = (string?)null,
                latitud = (double?)null, longitud = (double?)null,
                version = 99,   // una version que nunca existio
            }),
        };
        peticion.Headers.Add("X-CSRF-Token", csrf);

        var respuesta = await _cliente.SendAsync(peticion);

        Assert.Equal(HttpStatusCode.PreconditionFailed, respuesta.StatusCode);
    }

    [Fact]
    public async Task La_ficha_de_un_prestador_sin_publicar_responde_404()
    {
        var correo = NuevoCorreo();
        await RegistrarAsync(correo, rol: "prestador");

        var respuesta = await _cliente.GetAsync("/api/prestadores/pablo-espinoza-que-no-existe");

        Assert.Equal(HttpStatusCode.NotFound, respuesta.StatusCode);
    }

    // ------------------------------------------------------------ auxiliares

    private string NuevoCorreo()
    {
        var correo = $"api-{Guid.NewGuid():N}@zass.local";
        _correosCreados.Add(correo);
        return correo;
    }

    private async Task RegistrarAsync(string correo, string rol = "buscador")
    {
        var respuesta = await _cliente.PostAsJsonAsync("/api/auth/registro", new
        {
            correo, contrasena = "contrasena-larga-1", nombre = "Pablo Espinoza", rol,
        });
        Assert.Equal(HttpStatusCode.Created, respuesta.StatusCode);
    }

    private async Task VerificarAsync(string correo)
    {
        var respuesta = await _cliente.PostAsJsonAsync("/api/auth/correo/verificar", new
        {
            token = await TokenDeVerificacionAsync(correo),
        });
        Assert.Equal(HttpStatusCode.NoContent, respuesta.StatusCode);
    }

    private async Task IniciarSesionAsync(string correo)
    {
        var respuesta = await _cliente.PostAsJsonAsync("/api/auth/sesion", new
        {
            correo, contrasena = "contrasena-larga-1",
        });
        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
    }

    private async Task<string> TokenCsrfAsync()
    {
        var respuesta = await _cliente.GetAsync("/api/auth/csrf");
        respuesta.EnsureSuccessStatusCode();
        var json = await respuesta.Content.ReadFromJsonAsync<JsonDocument>();
        return json!.RootElement.GetProperty("token").GetString()!;
    }

    /// <summary>
    /// Lee el token del aviso encolado, que es lo que la persona recibiria por correo.
    /// En la tabla usuario solo esta el hash, asi que no hay otra forma de obtenerlo
    /// y eso es exactamente lo que se queria comprobar.
    /// </summary>
    private static async Task<string> TokenDeVerificacionAsync(string correo)
    {
        using var ctx = ContextoDePrueba.Crear();
        var usuario = await ctx.Usuarios.SingleAsync(u => u.Correo == correo);
        var aviso = await ctx.Notificaciones
            .Where(n => n.DestinatarioId == usuario.Id
                     && n.Tipo == TipoNotificacion.CorreoVerificacion)
            .OrderByDescending(n => n.ProgramadaPara)
            .FirstAsync();

        return aviso.Cuerpo.Split(' ').Last().TrimEnd('.');
    }
}

/// <summary>
/// El limite de tasa, en su propia clase y con su propia aplicacion: es la unica prueba
/// que necesita el limite real del documento, y gastarlo aqui no afecta a las demas.
/// </summary>
public class LimiteDeTasaPruebas
{
    [Fact]
    public async Task El_sexto_intento_de_inicio_de_sesion_en_la_ventana_recibe_429()
    {
        // Grupo A: 5 por IP cada 15 min (seguridad.md, seccion 1).
        using var app = new Aplicacion(limiteAutenticacion: 5);
        using var cliente = app.CreateClient();

        var codigos = new List<HttpStatusCode>();
        for (var intento = 1; intento <= 6; intento++)
        {
            var respuesta = await cliente.PostAsJsonAsync("/api/auth/sesion", new
            {
                correo = "nadie@zass.local", contrasena = "contrasena-larga-1",
            });
            codigos.Add(respuesta.StatusCode);

            if (intento == 6)
            {
                Assert.Equal("900", respuesta.Headers.RetryAfter?.Delta?.TotalSeconds.ToString()
                    ?? respuesta.Headers.GetValues("Retry-After").First());

                var cuerpo = await respuesta.Content.ReadAsStringAsync();
                Assert.Contains("Demasiadas peticiones", cuerpo);
                // Nunca se dice cuantos intentos quedan.
                Assert.DoesNotContain("restantes", cuerpo);
            }
        }

        Assert.Equal(5, codigos.Count(c => c == HttpStatusCode.Unauthorized));
        Assert.Equal(HttpStatusCode.TooManyRequests, codigos[5]);
    }
}
