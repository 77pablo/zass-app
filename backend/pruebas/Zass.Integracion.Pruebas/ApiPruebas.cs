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

            // https, no http. El servidor de pruebas no cifra nada de verdad, pero con
            // esta direccion Request.IsHttps vale true, y entonces la aplicacion se
            // comporta como en produccion: emite la cookie con Secure y no redirige.
            // Con http las pruebas verian 307 en todas partes y una cookie sin Secure,
            // es decir, probarian una aplicacion distinta de la que se despliega.
            BaseAddress = new Uri("https://localhost"),
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

        // Secure: la cookie no sale por una conexion en claro. Esto solo se puede
        // comprobar porque el cliente de pruebas habla https.
        Assert.All(cookies, c => Assert.Contains("secure", c.ToLowerInvariant()));

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

    // ------------------------------------------------------------ MOD-13 por HTTPS

    [Fact]
    public async Task Sin_sesion_la_suscripcion_responde_401()
    {
        var r = await _cliente.GetAsync("/api/mi/suscripcion");

        Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode);
    }

    [Fact]
    public async Task Una_cuenta_de_buscador_no_tiene_suscripcion_que_mirar()
    {
        var correo = NuevoCorreo();
        await RegistrarAsync(correo);
        await IniciarSesionAsync(correo);

        var r = await _cliente.GetAsync("/api/mi/suscripcion");

        Assert.Equal(HttpStatusCode.NotFound, r.StatusCode);
    }

    // No tener suscripcion todavia es normal, no un error: el perfil recien creado
    // aun no publico. Si esto devolviera 404, la pantalla tendria que tratar el caso
    // corriente como una excepcion.
    [Fact]
    public async Task Un_prestador_sin_publicar_ve_que_no_tiene_suscripcion()
    {
        var correo = NuevoCorreo();
        await RegistrarAsync(correo, "prestador");
        await IniciarSesionAsync(correo);

        var r = await _cliente.GetAsync("/api/mi/suscripcion");

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var json = (await r.Content.ReadFromJsonAsync<JsonDocument>())!.RootElement;
        Assert.False(json.GetProperty("vigente").GetBoolean());
        Assert.Equal(0, json.GetProperty("diasRestantes").GetInt32());
    }

    // El orden de la composicion de API-22: primero publicar, despues arrancar la
    // prueba. Si se hiciera al reves, un perfil incompleto gastaria dias de prueba
    // sin haber llegado nunca a aparecer en la busqueda.
    [Fact]
    public async Task Publicar_sin_el_minimo_no_arranca_la_prueba()
    {
        var correo = NuevoCorreo();
        await RegistrarAsync(correo, "prestador");
        await VerificarAsync(correo);
        await IniciarSesionAsync(correo);

        var peticion = new HttpRequestMessage(
            HttpMethod.Post, "/api/mi/perfil-prestador/publicar");
        peticion.Headers.Add("X-CSRF-Token", await TokenCsrfAsync());
        var publicar = await _cliente.SendAsync(peticion);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, publicar.StatusCode);

        var suscripcion = await _cliente.GetAsync("/api/mi/suscripcion");
        var json = (await suscripcion.Content.ReadFromJsonAsync<JsonDocument>())!.RootElement;
        Assert.False(json.GetProperty("vigente").GetBoolean());
    }

    // ------------------------------------------------------------

    // ------------------------------------------------- MOD-03 Catalogo por HTTP

    /// <summary>
    /// API-02 es publico: el selector de rubros de la busqueda (CU-08) y el del
    /// formulario de perfil (CU-03) tienen que funcionar sin sesion.
    /// </summary>
    [Fact]
    public async Task Los_rubros_se_piden_sin_sesion()
    {
        var respuesta = await _cliente.GetAsync("/api/rubros");

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        var rubros = await respuesta.Content.ReadFromJsonAsync<JsonDocument>();
        Assert.Equal(5, rubros!.RootElement.GetArrayLength());
    }

    [Fact]
    public async Task Los_rubros_se_filtran_con_q()
    {
        var respuesta = await _cliente.GetAsync("/api/rubros?q=contab");

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        var rubros = await respuesta.Content.ReadFromJsonAsync<JsonDocument>();
        Assert.Equal(1, rubros!.RootElement.GetArrayLength());
        Assert.Equal("contabilidad", rubros.RootElement[0].GetProperty("codigo").GetString());
    }

    [Fact]
    public async Task Los_servicios_de_un_rubro_se_piden_sin_sesion()
    {
        var respuesta = await _cliente.GetAsync("/api/rubros/1/servicios");

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        var servicios = await respuesta.Content.ReadFromJsonAsync<JsonDocument>();
        Assert.True(servicios!.RootElement.GetArrayLength() > 0);
    }

    /// <summary>
    /// Un identificador que no cabe en smallint no puede terminar en un 404 vacio por no
    /// casar con la ruta: contesta lo mismo que un rubro que no existe, que es lo que es.
    /// </summary>
    [Theory]
    [InlineData("77")]
    [InlineData("99999")]
    [InlineData("0")]
    public async Task Los_servicios_de_un_rubro_que_no_existe_devuelven_404(string rubroId)
    {
        var respuesta = await _cliente.GetAsync($"/api/rubros/{rubroId}/servicios");

        Assert.Equal(HttpStatusCode.NotFound, respuesta.StatusCode);
        Assert.Contains("rubro", await respuesta.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Guardar_los_servicios_sin_sesion_devuelve_401()
    {
        var peticion = new HttpRequestMessage(
            HttpMethod.Put, "/api/mi/perfil-prestador/servicios")
        {
            Content = JsonContent.Create(new { servicios = new[] { UnServicio(101) } }),
        };

        var respuesta = await _cliente.SendAsync(peticion);

        Assert.Equal(HttpStatusCode.Unauthorized, respuesta.StatusCode);
    }

    [Fact]
    public async Task Guardar_los_servicios_sin_el_token_csrf_devuelve_403()
    {
        var correo = NuevoCorreo();
        await RegistrarAsync(correo, rol: "prestador");
        await IniciarSesionAsync(correo);

        var peticion = new HttpRequestMessage(
            HttpMethod.Put, "/api/mi/perfil-prestador/servicios")
        {
            Content = JsonContent.Create(new { servicios = new[] { UnServicio(101) } }),
        };

        var respuesta = await _cliente.SendAsync(peticion);

        Assert.Equal(HttpStatusCode.Forbidden, respuesta.StatusCode);
    }

    /// <summary>
    /// El rubro se elige en el paso 3 de CU-03 y los servicios en el 4. Llegar al 4 sin
    /// el 3 es el formulario fuera de orden, no un dato invalido: 422 y se dice cual es.
    /// </summary>
    [Fact]
    public async Task Guardar_los_servicios_sin_rubro_en_el_perfil_devuelve_422()
    {
        var correo = NuevoCorreo();
        await RegistrarAsync(correo, rol: "prestador");
        await IniciarSesionAsync(correo);

        var respuesta = await PonerServiciosAsync(new[] { UnServicio(101) });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, respuesta.StatusCode);
        Assert.Contains("rubro", await respuesta.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Guardar_un_servicio_de_otro_rubro_devuelve_400()
    {
        var correo = NuevoCorreo();
        await RegistrarAsync(correo, rol: "prestador");
        await IniciarSesionAsync(correo);
        await PonerRubroAsync(1);           // corretaje

        var respuesta = await PonerServiciosAsync(new[] { UnServicio(201) });   // derecho

        Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);
    }

    /// <summary>El camino completo de CU-03 pasos 3 y 4, por HTTP y contra la base real.</summary>
    [Fact]
    public async Task Un_prestador_elige_su_rubro_y_guarda_sus_servicios()
    {
        var correo = NuevoCorreo();
        await RegistrarAsync(correo, rol: "prestador");
        await IniciarSesionAsync(correo);
        await PonerRubroAsync(1);

        var guardado = await PonerServiciosAsync(new object[]
        {
            UnServicio(101, duracion: 45),
            UnServicio(102),
            // Uno escrito a mano, que es lo que permite CU-03 paso 4.
            new { servicioId = (int?)null, nombreNuevo = "Visita a terreno",
                  duracionMinutos = 60, modoAgenda = "manual", precioReferencia = 25000 },
        });
        Assert.Equal(HttpStatusCode.NoContent, guardado.StatusCode);

        var leido = await _cliente.GetAsync("/api/mi/perfil-prestador/servicios");
        Assert.Equal(HttpStatusCode.OK, leido.StatusCode);
        var servicios = await leido.Content.ReadFromJsonAsync<JsonDocument>();
        Assert.Equal(3, servicios!.RootElement.GetArrayLength());

        var escrito = servicios.RootElement.EnumerateArray()
            .Single(s => s.GetProperty("nombre").GetString() == "Visita a terreno");
        Assert.False(escrito.GetProperty("esCatalogo").GetBoolean());
        Assert.Equal(25000, escrito.GetProperty("precioReferencia").GetInt32());
    }

    /// <summary>
    /// Lo que MOD-03 desbloquea, y lo que todavia no: con rubro y servicio puestos,
    /// publicar ya no se queja de ellos y solo falta la zona, que es MOD-04. Esta prueba
    /// cambia el dia que MOD-04 exista, y eso es justamente lo que tiene que pasar.
    /// </summary>
    [Fact]
    public async Task Con_rubro_y_servicios_publicar_ya_solo_pide_la_zona()
    {
        var correo = NuevoCorreo();
        await RegistrarAsync(correo, rol: "prestador");
        await VerificarAsync(correo);
        await IniciarSesionAsync(correo);
        await PonerRubroAsync(1);
        Assert.Equal(HttpStatusCode.NoContent,
            (await PonerServiciosAsync(new[] { UnServicio(101) })).StatusCode);

        var peticion = new HttpRequestMessage(
            HttpMethod.Post, "/api/mi/perfil-prestador/publicar");
        peticion.Headers.Add("X-CSRF-Token", await TokenCsrfAsync());
        var respuesta = await _cliente.SendAsync(peticion);

        var cuerpo = await respuesta.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.UnprocessableEntity, respuesta.StatusCode);
        Assert.Contains("zona", cuerpo);
        Assert.DoesNotContain("el rubro", cuerpo);
        Assert.DoesNotContain("al menos un servicio", cuerpo);
    }

    // ------------------------------------------------- MOD-04 Geografia por HTTP

    /// <summary>
    /// API-04 es publico: el selector de zonas lo necesitan la busqueda (CU-08) y el
    /// formulario de perfil (CU-03) antes de que haya sesion.
    /// </summary>
    [Fact]
    public async Task Las_zonas_se_piden_sin_sesion()
    {
        var respuesta = await _cliente.GetAsync("/api/zonas");

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        var zonas = await respuesta.Content.ReadFromJsonAsync<JsonDocument>();
        Assert.Equal(16, zonas!.RootElement.GetArrayLength());
    }

    [Fact]
    public async Task Las_zonas_se_filtran_por_sector()
    {
        var respuesta = await _cliente.GetAsync("/api/zonas?sector=poniente");

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        var zonas = await respuesta.Content.ReadFromJsonAsync<JsonDocument>();
        Assert.True(zonas!.RootElement.GetArrayLength() > 0);
        Assert.All(
            zonas.RootElement.EnumerateArray(),
            z => Assert.Equal("poniente", z.GetProperty("sector").GetString()));
    }

    /// <summary>
    /// El centroide de la zona SI viaja: es el centro de un barrio, no la casa de nadie.
    /// Lo que no puede viajar nunca es la ubicacion exacta del prestador (RN-S3-02), y de
    /// eso se encarga el DTO del perfil, que no tiene esos campos.
    /// </summary>
    [Fact]
    public async Task La_zona_publica_lleva_su_centroide_y_su_comuna()
    {
        var respuesta = await _cliente.GetAsync("/api/zonas?sector=centro");
        var zonas = await respuesta.Content.ReadFromJsonAsync<JsonDocument>();

        var centro = zonas!.RootElement[0];
        Assert.Equal("Temuco", centro.GetProperty("comuna").GetString());
        Assert.InRange(centro.GetProperty("centroLatitud").GetDouble(), -39.1, -38.4);
    }

    [Fact]
    public async Task Guardar_la_cobertura_sin_sesion_devuelve_401()
    {
        var peticion = new HttpRequestMessage(
            HttpMethod.Put, "/api/mi/perfil-prestador/cobertura")
        {
            Content = JsonContent.Create(new { zonas = new[] { UnaZona(1, true) } }),
        };

        var respuesta = await _cliente.SendAsync(peticion);

        Assert.Equal(HttpStatusCode.Unauthorized, respuesta.StatusCode);
    }

    [Fact]
    public async Task Guardar_la_cobertura_sin_el_token_csrf_devuelve_403()
    {
        var correo = NuevoCorreo();
        await RegistrarAsync(correo, rol: "prestador");
        await IniciarSesionAsync(correo);

        var peticion = new HttpRequestMessage(
            HttpMethod.Put, "/api/mi/perfil-prestador/cobertura")
        {
            Content = JsonContent.Create(new { zonas = new[] { UnaZona(1, true) } }),
        };

        var respuesta = await _cliente.SendAsync(peticion);

        Assert.Equal(HttpStatusCode.Forbidden, respuesta.StatusCode);
    }

    /// <summary>R-2 y RN-S3-08, dichos en el borde: sin principal no se guarda.</summary>
    [Fact]
    public async Task Guardar_la_cobertura_sin_zona_principal_devuelve_400()
    {
        var correo = NuevoCorreo();
        await RegistrarAsync(correo, rol: "prestador");
        await IniciarSesionAsync(correo);

        var respuesta = await PonerCoberturaAsync(new[] { UnaZona(1, false) });

        Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);
        Assert.Contains("principal", await respuesta.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Guardar_una_zona_que_no_existe_devuelve_400()
    {
        var correo = NuevoCorreo();
        await RegistrarAsync(correo, rol: "prestador");
        await IniciarSesionAsync(correo);

        var respuesta = await PonerCoberturaAsync(new[] { UnaZona(9999, true) });

        Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);
    }

    [Fact]
    public async Task Un_prestador_guarda_su_cobertura_y_la_lee_de_vuelta()
    {
        var correo = NuevoCorreo();
        await RegistrarAsync(correo, rol: "prestador");
        await IniciarSesionAsync(correo);

        var guardado = await PonerCoberturaAsync(new[]
        {
            UnaZona(11, false),      // Amanecer
            UnaZona(1, true),        // Centro, la principal
        });
        Assert.Equal(HttpStatusCode.NoContent, guardado.StatusCode);

        var leido = await _cliente.GetAsync("/api/mi/perfil-prestador/cobertura");
        Assert.Equal(HttpStatusCode.OK, leido.StatusCode);
        var cobertura = await leido.Content.ReadFromJsonAsync<JsonDocument>();

        Assert.Equal(2, cobertura!.RootElement.GetArrayLength());
        // La principal va primero: es la que se muestra en la ficha (RN-S3-08).
        Assert.True(cobertura.RootElement[0].GetProperty("esPrincipal").GetBoolean());
        Assert.Equal("Centro", cobertura.RootElement[0].GetProperty("nombre").GetString());
    }

    // --------------------------------------- el camino completo de CU-03, por HTTP

    /// <summary>
    /// La prueba que ESTADO.md daba por imposible durante dos sesiones: publicar y que
    /// arranque la suscripcion de prueba, de punta a punta y por HTTP.
    ///
    /// Hasta que existieron MOD-03 y MOD-04 no habia forma de llegar al minimo publicable
    /// (RN-S1-05: nombre, rubro, un servicio y una zona), asi que la mitad de API-22 —la
    /// que compone publicar con arrancar la prueba— solo se podia comprobar por el lado
    /// del fallo. Esto recorre el camino bueno.
    /// </summary>
    [Fact]
    public async Task Un_prestador_completa_su_perfil_lo_publica_y_le_arranca_la_prueba()
    {
        var correo = NuevoCorreo();
        await RegistrarAsync(correo, rol: "prestador");
        await VerificarAsync(correo);       // RN-S1-02: sin correo verificado no publica
        await IniciarSesionAsync(correo);

        // Paso 3 de CU-03: el rubro
        await PonerRubroAsync(1);

        // Paso 4: los servicios
        Assert.Equal(HttpStatusCode.NoContent,
            (await PonerServiciosAsync(new[] { UnServicio(101) })).StatusCode);

        // Pasos 6 y 7: la zona de cobertura
        Assert.Equal(HttpStatusCode.NoContent,
            (await PonerCoberturaAsync(new[] { UnaZona(1, true) })).StatusCode);

        // Paso 9: publicar
        var publicar = new HttpRequestMessage(
            HttpMethod.Post, "/api/mi/perfil-prestador/publicar");
        publicar.Headers.Add("X-CSRF-Token", await TokenCsrfAsync());
        var publicado = await _cliente.SendAsync(publicar);

        Assert.Equal(HttpStatusCode.NoContent, publicado.StatusCode);

        // Y lo que importa de verdad: sin suscripcion vigente el perfil no apareceria en
        // la busqueda (RN-S3-09), asi que publicar TIENE que haber arrancado la prueba.
        var suscripcion = await _cliente.GetFromJsonAsync<JsonDocument>("/api/mi/suscripcion");
        Assert.True(suscripcion!.RootElement.GetProperty("vigente").GetBoolean());
        Assert.True(suscripcion.RootElement.GetProperty("enPrueba").GetBoolean());
        Assert.Equal(30, suscripcion.RootElement.GetProperty("diasRestantes").GetInt32());

        // El perfil quedo publicado y ya no le falta nada.
        var perfil = await _cliente.GetFromJsonAsync<JsonDocument>("/api/mi/perfil-prestador");
        Assert.Equal("Publicado", perfil!.RootElement.GetProperty("estado").GetString());
        Assert.Empty(perfil.RootElement.GetProperty("faltaParaPublicar").EnumerateArray());
        Assert.Equal("Centro", perfil.RootElement.GetProperty("zonaPrincipal").GetString());
    }

    /// <summary>
    /// Publicar dos veces no reinicia la prueba: IniciarPrueba es idempotente, y si no lo
    /// fuera, despublicar y volver a publicar regalaria 30 dias cada vez.
    /// </summary>
    [Fact]
    public async Task Volver_a_publicar_no_regala_otros_30_dias()
    {
        var correo = NuevoCorreo();
        await RegistrarAsync(correo, rol: "prestador");
        await VerificarAsync(correo);
        await IniciarSesionAsync(correo);
        await PonerRubroAsync(1);
        await PonerServiciosAsync(new[] { UnServicio(101) });
        await PonerCoberturaAsync(new[] { UnaZona(1, true) });

        var csrf = await TokenCsrfAsync();
        for (var vez = 0; vez < 2; vez++)
        {
            var peticion = new HttpRequestMessage(
                HttpMethod.Post, "/api/mi/perfil-prestador/publicar");
            peticion.Headers.Add("X-CSRF-Token", csrf);
            var r = await _cliente.SendAsync(peticion);
            Assert.Equal(HttpStatusCode.NoContent, r.StatusCode);
        }

        var suscripcion = await _cliente.GetFromJsonAsync<JsonDocument>("/api/mi/suscripcion");
        Assert.Equal(30, suscripcion!.RootElement.GetProperty("diasRestantes").GetInt32());

        using var ctx = ContextoDePrueba.Crear();
        var usuario = await ctx.Usuarios.SingleAsync(u => u.Correo == correo);
        var perfil = await ctx.PerfilesPrestador.SingleAsync(p => p.UsuarioId == usuario.Id);
        Assert.Single(await ctx.Suscripciones.Where(s => s.PerfilPrestadorId == perfil.Id).ToListAsync());
    }

    private static object UnaZona(int zonaId, bool principal) => new
    {
        zonaId,
        esPrincipal = principal,
    };

    private async Task<HttpResponseMessage> PonerCoberturaAsync(object[] zonas)
    {
        var peticion = new HttpRequestMessage(
            HttpMethod.Put, "/api/mi/perfil-prestador/cobertura")
        {
            Content = JsonContent.Create(new { zonas }),
        };
        peticion.Headers.Add("X-CSRF-Token", await TokenCsrfAsync());
        return await _cliente.SendAsync(peticion);
    }

    private static object UnServicio(int servicioId, short duracion = 60) => new
    {
        servicioId,
        nombreNuevo = (string?)null,
        duracionMinutos = duracion,
        modoAgenda = "automatico",
        precioReferencia = (int?)null,
    };

    /// <summary>API-19 con lo minimo: el rubro y la version que exige CC-02.</summary>
    private async Task PonerRubroAsync(short rubroId)
    {
        var perfil = await _cliente.GetFromJsonAsync<JsonDocument>("/api/mi/perfil-prestador");
        var version = perfil!.RootElement.GetProperty("version").GetInt32();

        var peticion = new HttpRequestMessage(HttpMethod.Put, "/api/mi/perfil-prestador")
        {
            Content = JsonContent.Create(new
            {
                nombrePublico = "Estudio de prueba", descripcion = (string?)null,
                rubroId, telefonoPublico = (string?)null, correoPublico = (string?)null,
                whatsapp = (string?)null, sitioWeb = (string?)null,
                direccion = (string?)null, latitud = (double?)null, longitud = (double?)null,
                version,
            }),
        };
        peticion.Headers.Add("X-CSRF-Token", await TokenCsrfAsync());

        var respuesta = await _cliente.SendAsync(peticion);
        Assert.Equal(HttpStatusCode.NoContent, respuesta.StatusCode);
    }

    private async Task<HttpResponseMessage> PonerServiciosAsync(object[] servicios)
    {
        var peticion = new HttpRequestMessage(
            HttpMethod.Put, "/api/mi/perfil-prestador/servicios")
        {
            Content = JsonContent.Create(new { servicios }),
        };
        peticion.Headers.Add("X-CSRF-Token", await TokenCsrfAsync());
        return await _cliente.SendAsync(peticion);
    }

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
