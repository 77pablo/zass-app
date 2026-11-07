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

        // Las entidades que escribieron las pruebas de credenciales no cuelgan de ningun usuario.
        using (var ctx = ContextoDePrueba.Crear())
        {
            ctx.EntidadesCertificadoras.RemoveRange(
                ctx.EntidadesCertificadoras.Where(e => e.Nombre.StartsWith(PrefijoDeEntidad)));
            await ctx.SaveChangesAsync();
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

    // ------------------------------------------------- MOD-05 Busqueda por HTTP (API-01)

    /// <summary>Un cliente SIN cookies: un visitante que nunca inicio sesion (P1 de CU-08).</summary>
    private HttpClient UnVisitante() => _app.CreateClient(new WebApplicationFactoryClientOptions
    {
        HandleCookies = false,
        AllowAutoRedirect = false,
        BaseAddress = new Uri("https://localhost"),
    });

    /// <summary>
    /// Deja a un prestador publicado y buscable, recorriendo el camino de verdad por HTTP:
    /// registro, correo, rubro, servicio, zona y publicar. Devuelve el id de su perfil.
    /// </summary>
    private async Task<Guid> UnPrestadorPublicadoAsync()
    {
        var correo = NuevoCorreo();
        await RegistrarAsync(correo, rol: "prestador");
        await VerificarAsync(correo);
        await IniciarSesionAsync(correo);
        await PonerRubroAsync(1);
        Assert.Equal(HttpStatusCode.NoContent,
            (await PonerServiciosAsync(new[] { UnServicio(101) })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent,
            (await PonerCoberturaAsync(new[] { UnaZona(1, true) })).StatusCode);

        var publicar = new HttpRequestMessage(
            HttpMethod.Post, "/api/mi/perfil-prestador/publicar");
        publicar.Headers.Add("X-CSRF-Token", await TokenCsrfAsync());
        Assert.Equal(HttpStatusCode.NoContent, (await _cliente.SendAsync(publicar)).StatusCode);

        var perfil = await _cliente.GetFromJsonAsync<JsonDocument>("/api/mi/perfil-prestador");
        return perfil!.RootElement.GetProperty("id").GetGuid();
    }

    [Fact]
    public async Task Buscar_sin_rubro_devuelve_400()
    {
        using var visitante = UnVisitante();

        var respuesta = await visitante.GetAsync("/api/busqueda?zona=1");

        Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);
    }

    [Fact]
    public async Task Buscar_en_un_rubro_que_no_existe_devuelve_400_y_lo_dice()
    {
        using var visitante = UnVisitante();

        var respuesta = await visitante.GetAsync("/api/busqueda?rubro=astrologia&zona=1");

        Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);
        Assert.Contains("rubro", await respuesta.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Buscar_sin_decir_donde_devuelve_400()
    {
        using var visitante = UnVisitante();

        var respuesta = await visitante.GetAsync("/api/busqueda?rubro=corretaje");

        Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);
        Assert.Contains("ubicacion", await respuesta.Content.ReadAsStringAsync(),
            StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Media coordenada no es una ubicacion. Sin la regla, mandar solo `lat` caeria en
    /// "dinos donde buscas", que no explica el error de verdad.
    /// </summary>
    [Theory]
    [InlineData("lat=-38.73", "longitud")]
    [InlineData("lon=-72.59", "latitud")]
    public async Task Buscar_con_media_coordenada_dice_cual_falta(string mitad, string faltante)
    {
        using var visitante = UnVisitante();

        var respuesta = await visitante.GetAsync($"/api/busqueda?rubro=corretaje&{mitad}");

        Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);
        // Solo con el codigo 400 la prueba pasaba tambien SIN la regla, porque sin ella
        // la peticion igual cae en "dinos donde buscas". Lo que importa es el motivo.
        Assert.Contains(faltante, await respuesta.Content.ReadAsStringAsync(),
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Buscar_con_un_cursor_basura_devuelve_400()
    {
        using var visitante = UnVisitante();

        var respuesta = await visitante.GetAsync(
            "/api/busqueda?rubro=corretaje&zona=1&cursor=esto-no-es-base64!!");

        Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);
    }

    /// <summary>
    /// E-400 de CU-08: un radio fuera de rango se CORRIGE y se informa, no se rechaza.
    /// Quien pide 500 km quiere buscar lejos, no leer un error.
    /// </summary>
    [Fact]
    public async Task Un_radio_imposible_se_corrige_y_la_respuesta_lo_dice()
    {
        using var visitante = UnVisitante();

        var respuesta = await visitante.GetAsync(
            "/api/busqueda?rubro=corretaje&lat=-38.7359&lon=-72.5904&radio=500");

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        var cuerpo = await respuesta.Content.ReadFromJsonAsync<JsonDocument>();
        Assert.Contains("radio", cuerpo!.RootElement.GetProperty("aviso").GetString()!,
            StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// La frase entera del producto, por HTTP y de punta a punta: un prestador se
    /// publica, y un visitante SIN sesion lo encuentra.
    /// </summary>
    [Fact]
    public async Task Un_visitante_sin_sesion_encuentra_al_prestador_publicado()
    {
        var perfilId = await UnPrestadorPublicadoAsync();
        using var visitante = UnVisitante();

        var respuesta = await visitante.GetAsync(
            "/api/busqueda?rubro=corretaje&lat=-38.7359&lon=-72.5904");

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        var cuerpo = await respuesta.Content.ReadFromJsonAsync<JsonDocument>();
        Assert.True(cuerpo!.RootElement.GetProperty("ordenadoPorDistancia").GetBoolean());
        Assert.Equal("Centro", cuerpo.RootElement.GetProperty("zonaBuscada").GetString());

        var encontrado = cuerpo.RootElement.GetProperty("resultados").EnumerateArray()
            .Single(r => r.GetProperty("perfilId").GetGuid() == perfilId);
        // RN-S3-03: lo que viaja es texto redondeado, nunca un numero.
        Assert.Equal(JsonValueKind.String, encontrado.GetProperty("distancia").ValueKind);
        Assert.StartsWith("a ", encontrado.GetProperty("distancia").GetString());
        Assert.False(encontrado.GetProperty("esTuPerfil").GetBoolean());
    }

    /// <summary>
    /// RN-S3-02, comprobada sobre los BYTES de la respuesta y no sobre el DTO: nada de
    /// ubicacion exacta, direccion, metros ni correo puede aparecer en el JSON, ni con
    /// otro nombre ni dentro de otro campo.
    /// </summary>
    [Fact]
    public async Task La_respuesta_publica_no_filtra_ubicacion_direccion_ni_correo()
    {
        await UnPrestadorPublicadoAsync();
        using var visitante = UnVisitante();

        var respuesta = await visitante.GetAsync(
            "/api/busqueda?rubro=corretaje&lat=-38.7359&lon=-72.5904");
        var texto = await respuesta.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        Assert.DoesNotContain("latitud", texto, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("longitud", texto, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("direccion", texto, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("metros", texto, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("correo", texto, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("@zass.local", texto, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("telefono", texto, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Buscar_por_zona_sin_punto_encuentra_y_avisa_que_no_hay_orden_por_distancia()
    {
        var perfilId = await UnPrestadorPublicadoAsync();
        using var visitante = UnVisitante();

        var respuesta = await visitante.GetAsync("/api/busqueda?rubro=corretaje&zona=1");

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        var cuerpo = await respuesta.Content.ReadFromJsonAsync<JsonDocument>();
        Assert.False(cuerpo!.RootElement.GetProperty("ordenadoPorDistancia").GetBoolean());
        Assert.Contains("distancia", cuerpo.RootElement.GetProperty("aviso").GetString()!,
            StringComparison.OrdinalIgnoreCase);

        var encontrado = cuerpo.RootElement.GetProperty("resultados").EnumerateArray()
            .Single(r => r.GetProperty("perfilId").GetGuid() == perfilId);
        Assert.Equal(JsonValueKind.Null, encontrado.GetProperty("distancia").ValueKind);
    }

    /// <summary>
    /// Flujo 4F de CU-08: un prestador autenticado que busca su rubro se ve marcado como
    /// "este eres tu". Es lo unico que cambia entre un visitante y un prestador.
    /// </summary>
    [Fact]
    public async Task Un_prestador_que_busca_su_rubro_se_ve_marcado_como_propio()
    {
        var perfilId = await UnPrestadorPublicadoAsync();   // _cliente queda con su sesion

        var respuesta = await _cliente.GetAsync("/api/busqueda?rubro=corretaje&zona=1");

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        var cuerpo = await respuesta.Content.ReadFromJsonAsync<JsonDocument>();
        var suyo = cuerpo!.RootElement.GetProperty("resultados").EnumerateArray()
            .Single(r => r.GetProperty("perfilId").GetGuid() == perfilId);
        Assert.True(suyo.GetProperty("esTuPerfil").GetBoolean());
    }

    /// <summary>
    /// Lo que RN-S3-09 promete, de punta a punta por HTTP: el prestador que se
    /// despublica desaparece de la busqueda en el acto.
    /// </summary>
    [Fact]
    public async Task Un_prestador_despublicado_desaparece_de_la_busqueda()
    {
        var perfilId = await UnPrestadorPublicadoAsync();
        using var visitante = UnVisitante();

        var antes = await visitante.GetFromJsonAsync<JsonDocument>(
            "/api/busqueda?rubro=corretaje&zona=1");
        Assert.Contains(antes!.RootElement.GetProperty("resultados").EnumerateArray(),
            r => r.GetProperty("perfilId").GetGuid() == perfilId);

        var despublicar = new HttpRequestMessage(
            HttpMethod.Post, "/api/mi/perfil-prestador/despublicar");
        despublicar.Headers.Add("X-CSRF-Token", await TokenCsrfAsync());
        Assert.Equal(HttpStatusCode.NoContent, (await _cliente.SendAsync(despublicar)).StatusCode);

        var despues = await visitante.GetFromJsonAsync<JsonDocument>(
            "/api/busqueda?rubro=corretaje&zona=1");
        Assert.DoesNotContain(despues!.RootElement.GetProperty("resultados").EnumerateArray(),
            r => r.GetProperty("perfilId").GetGuid() == perfilId);
    }

    // ------------------------------------------------- CU-09 · API-05, la ficha publica

    /// <summary>
    /// Deja a un prestador publicado con TODO lo que muestra una ficha: datos de contacto,
    /// varios servicios y dos zonas. Devuelve su slug.
    /// </summary>
    private async Task<string> UnaFichaCompletaAsync()
    {
        var correo = NuevoCorreo();
        await RegistrarAsync(correo, rol: "prestador");
        await VerificarAsync(correo);
        await IniciarSesionAsync(correo);

        var perfil = await _cliente.GetFromJsonAsync<JsonDocument>("/api/mi/perfil-prestador");
        var version = perfil!.RootElement.GetProperty("version").GetInt32();
        var slug = perfil.RootElement.GetProperty("slug").GetString()!;

        var datos = new HttpRequestMessage(HttpMethod.Put, "/api/mi/perfil-prestador")
        {
            Content = JsonContent.Create(new
            {
                nombrePublico = "Marcela Soto",
                descripcion = "Corredora de propiedades en Temuco.",
                rubroId = (short?)1,
                telefonoPublico = "+56 9 1234 5678",
                correoPublico = "contacto@soto.cl",
                whatsapp = "912345678",
                sitioWeb = "https://soto.cl",
                direccion = (string?)null, latitud = (double?)null, longitud = (double?)null,
                version,
            }),
        };
        datos.Headers.Add("X-CSRF-Token", await TokenCsrfAsync());
        Assert.Equal(HttpStatusCode.NoContent, (await _cliente.SendAsync(datos)).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent,
            (await PonerServiciosAsync(new[] { UnServicio(101, duracion: 45), UnServicio(102) })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent,
            (await PonerCoberturaAsync(new[] { UnaZona(11, false), UnaZona(1, true) })).StatusCode);

        var publicar = new HttpRequestMessage(HttpMethod.Post, "/api/mi/perfil-prestador/publicar");
        publicar.Headers.Add("X-CSRF-Token", await TokenCsrfAsync());
        Assert.Equal(HttpStatusCode.NoContent, (await _cliente.SendAsync(publicar)).StatusCode);
        return slug;
    }

    [Fact]
    public async Task La_ficha_publica_trae_el_rubro_los_servicios_las_zonas_y_el_contacto()
    {
        var slug = await UnaFichaCompletaAsync();
        using var visitante = UnVisitante();

        var respuesta = await visitante.GetAsync($"/api/prestadores/{slug}");

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        var ficha = (await respuesta.Content.ReadFromJsonAsync<JsonDocument>())!.RootElement;

        Assert.Equal("Marcela Soto", ficha.GetProperty("nombrePublico").GetString());
        Assert.Equal("Corredora de propiedades en Temuco.", ficha.GetProperty("descripcion").GetString());
        Assert.Equal("Corretaje de propiedades", ficha.GetProperty("rubro").GetProperty("nombre").GetString());

        var servicios = ficha.GetProperty("servicios").EnumerateArray().ToList();
        Assert.Equal(2, servicios.Count);
        var tasacion = servicios.Single(s => s.GetProperty("nombre").GetString() == "Tasación comercial");
        Assert.Equal(45, tasacion.GetProperty("duracionMinutos").GetInt32());

        // La zona principal va primero, aunque se mandara segunda.
        var zonas = ficha.GetProperty("zonas").EnumerateArray().ToList();
        Assert.Equal("Centro", zonas[0].GetProperty("nombre").GetString());
        Assert.True(zonas[0].GetProperty("esPrincipal").GetBoolean());
        Assert.Equal("Amanecer", zonas[1].GetProperty("nombre").GetString());
        Assert.False(zonas[1].GetProperty("esPrincipal").GetBoolean());

        var contacto = ficha.GetProperty("contacto");
        Assert.Equal("+56 9 1234 5678", contacto.GetProperty("telefono").GetString());
        Assert.Equal("912345678", contacto.GetProperty("whatsapp").GetString());
        Assert.Equal("contacto@soto.cl", contacto.GetProperty("correo").GetString());
        Assert.Equal("https://soto.cl", contacto.GetProperty("sitioWeb").GetString());
    }

    /// <summary>
    /// RN-S3-02, sobre los BYTES de la respuesta: la ficha es el DTO que mas expone de todo el
    /// sistema. Ni direccion, ni coordenadas, ni el identificador de la cuenta, ni el correo
    /// con el que se registro (que es otro que el correo publico).
    /// </summary>
    [Fact]
    public async Task La_ficha_publica_no_filtra_ubicacion_ni_datos_de_la_cuenta()
    {
        var slug = await UnaFichaCompletaAsync();
        using var visitante = UnVisitante();

        var texto = await (await visitante.GetAsync($"/api/prestadores/{slug}")).Content.ReadAsStringAsync();

        Assert.DoesNotContain("latitud", texto, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("longitud", texto, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("direccion", texto, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("usuarioId", texto, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("@zass.local", texto, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("hash", texto, StringComparison.OrdinalIgnoreCase);
        // Y el modo de agenda es de las citas, no de la ficha.
        Assert.DoesNotContain("modoAgenda", texto, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Lo que el prestador dejo de ofrecer se CONSERVA en la base pero no sale en la ficha.
    /// Esto recorre la desactivacion de verdad, contra PostgreSQL.
    /// </summary>
    [Fact]
    public async Task Un_servicio_que_deja_de_ofrecer_desaparece_de_la_ficha_publica()
    {
        var slug = await UnaFichaCompletaAsync();
        using var visitante = UnVisitante();

        Assert.Equal(HttpStatusCode.NoContent,
            (await PonerServiciosAsync(new[] { UnServicio(101) })).StatusCode);

        var ficha = (await visitante.GetFromJsonAsync<JsonDocument>($"/api/prestadores/{slug}"))!.RootElement;
        var nombres = ficha.GetProperty("servicios").EnumerateArray()
            .Select(s => s.GetProperty("nombre").GetString()).ToArray();
        Assert.Equal(new[] { "Tasación comercial" }, nombres);
    }

    /// <summary>
    /// "No esta publicado" y "no existe" tienen que ser INDISTINGUIBLES para quien pregunta: si
    /// no, probar slugs diria quien se dio de alta sin publicar. Misma respuesta, byte a byte.
    /// </summary>
    [Fact]
    public async Task Sin_publicar_y_no_existe_dan_exactamente_la_misma_respuesta()
    {
        var correo = NuevoCorreo();
        await RegistrarAsync(correo, rol: "prestador");
        await IniciarSesionAsync(correo);
        var slugBorrador = (await _cliente.GetFromJsonAsync<JsonDocument>("/api/mi/perfil-prestador"))!
            .RootElement.GetProperty("slug").GetString()!;
        using var visitante = UnVisitante();

        var sinPublicar = await visitante.GetAsync($"/api/prestadores/{slugBorrador}");
        var noExiste = await visitante.GetAsync("/api/prestadores/este-slug-no-existe-para-nadie");

        Assert.Equal(HttpStatusCode.NotFound, sinPublicar.StatusCode);
        Assert.Equal(noExiste.StatusCode, sinPublicar.StatusCode);
        Assert.Equal(
            await noExiste.Content.ReadAsStringAsync(), await sinPublicar.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Al_despublicarse_la_ficha_deja_de_existir_para_el_publico()
    {
        var slug = await UnaFichaCompletaAsync();
        using var visitante = UnVisitante();
        Assert.Equal(HttpStatusCode.OK, (await visitante.GetAsync($"/api/prestadores/{slug}")).StatusCode);

        var despublicar = new HttpRequestMessage(HttpMethod.Post, "/api/mi/perfil-prestador/despublicar");
        despublicar.Headers.Add("X-CSRF-Token", await TokenCsrfAsync());
        Assert.Equal(HttpStatusCode.NoContent, (await _cliente.SendAsync(despublicar)).StatusCode);

        Assert.Equal(HttpStatusCode.NotFound, (await visitante.GetAsync($"/api/prestadores/{slug}")).StatusCode);
    }

    // ------------------------------------------------- API-62, la comunidad por HTTP

    [Fact]
    public async Task La_comunidad_se_pide_sin_sesion_y_cuenta_a_quien_se_publico()
    {
        var slug = await UnaFichaCompletaAsync();      // un prestador publicado de verdad
        using var visitante = UnVisitante();

        var respuesta = await visitante.GetAsync("/api/comunidad");

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        var c = (await respuesta.Content.ReadFromJsonAsync<JsonDocument>())!.RootElement;

        Assert.True(c.GetProperty("total").GetInt32() >= 1);
        // El Centro (zona 1) lo cubre como principal, y Amanecer (11) como secundaria.
        var porZona = c.GetProperty("porZona").EnumerateArray().ToList();
        Assert.Contains(porZona, z => z.GetProperty("zonaId").GetInt32() == 1 && z.GetProperty("personas").GetInt32() >= 1);
        Assert.Contains(porZona, z => z.GetProperty("zonaId").GetInt32() == 11);
        var recientes = c.GetProperty("recienLlegados").EnumerateArray().ToList();
        Assert.Contains(recientes, r => r.GetProperty("slug").GetString() == slug);
        var mio = recientes.Single(r => r.GetProperty("slug").GetString() == slug);
        Assert.Equal("Marcela Soto", mio.GetProperty("nombrePublico").GetString());
        Assert.Equal("Corretaje de propiedades", mio.GetProperty("rubro").GetString());
        Assert.Equal("Centro", mio.GetProperty("zona").GetString());
    }

    /// <summary>Sobre los BYTES: la comunidad es publica, asi que nada de la cuenta ni de la ubicacion.</summary>
    [Fact]
    public async Task La_comunidad_no_filtra_ubicacion_ni_datos_de_la_cuenta()
    {
        await UnaFichaCompletaAsync();
        using var visitante = UnVisitante();

        var texto = await (await visitante.GetAsync("/api/comunidad")).Content.ReadAsStringAsync();

        foreach (var prohibido in new[] { "latitud", "longitud", "direccion", "usuarioId", "@zass.local", "telefono", "whatsapp", "hash" })
            Assert.DoesNotContain(prohibido, texto, StringComparison.OrdinalIgnoreCase);
    }

    // ------------------------------------------------- MOD-02 Credenciales por HTTP

    private const string PrefijoDeEntidad = "api-entidad-";

    private static object UnaCredencial(
        string nombre = "Abogado", string? entidadNombre = null, string tipo = "Titulo",
        string obtenidaEn = "2020-03-15", string? venceEn = null) => new
        {
            tipo, nombre, entidadId = (short?)null,
            entidadNombre = entidadNombre ?? $"{PrefijoDeEntidad}{Guid.NewGuid():N}",
            obtenidaEn, venceEn,
        };

    private async Task<HttpResponseMessage> CrearCredencialAsync(object cuerpo, bool conCsrf = true)
    {
        var peticion = new HttpRequestMessage(HttpMethod.Post, "/api/mi/credenciales")
        {
            Content = JsonContent.Create(cuerpo),
        };
        if (conCsrf) peticion.Headers.Add("X-CSRF-Token", await TokenCsrfAsync());
        return await _cliente.SendAsync(peticion);
    }

    private async Task<HttpResponseMessage> EliminarCredencialAsync(string id)
    {
        var peticion = new HttpRequestMessage(HttpMethod.Delete, $"/api/mi/credenciales/{id}");
        peticion.Headers.Add("X-CSRF-Token", await TokenCsrfAsync());
        return await _cliente.SendAsync(peticion);
    }

    private static async Task<string> IdDeAsync(HttpResponseMessage creada) =>
        (await creada.Content.ReadFromJsonAsync<JsonDocument>())!.RootElement.GetProperty("id").GetString()!;

    private async Task UnPrestadorConSesionAsync()
    {
        var correo = NuevoCorreo();
        await RegistrarAsync(correo, rol: "prestador");
        await VerificarAsync(correo);
        await IniciarSesionAsync(correo);
    }

    [Fact]
    public async Task Sin_sesion_las_credenciales_responden_401()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await _cliente.GetAsync("/api/mi/credenciales")).StatusCode);
    }

    [Fact]
    public async Task Crear_una_credencial_sin_el_token_csrf_devuelve_403()
    {
        await UnPrestadorConSesionAsync();

        var r = await CrearCredencialAsync(UnaCredencial(), conCsrf: false);

        Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
    }

    [Fact]
    public async Task Una_cuenta_de_buscador_no_puede_tener_credenciales()
    {
        var correo = NuevoCorreo();
        await RegistrarAsync(correo, rol: "buscador");
        await IniciarSesionAsync(correo);

        var lista = await _cliente.GetAsync("/api/mi/credenciales");
        var crear = await CrearCredencialAsync(UnaCredencial());

        Assert.False(lista.IsSuccessStatusCode);
        Assert.False(crear.IsSuccessStatusCode);
    }

    /// <summary>El camino de CU-05 sin archivos, por HTTP y contra la base real: crear, ver, eliminar.</summary>
    [Fact]
    public async Task Un_prestador_crea_ve_y_elimina_una_credencial()
    {
        await UnPrestadorConSesionAsync();

        var creada = await CrearCredencialAsync(UnaCredencial("Abogado"));
        Assert.Equal(HttpStatusCode.Created, creada.StatusCode);
        var id = await IdDeAsync(creada);

        var lista = (await _cliente.GetFromJsonAsync<JsonDocument>("/api/mi/credenciales"))!.RootElement;
        var mia = Assert.Single(lista.EnumerateArray().ToList());
        Assert.Equal(id, mia.GetProperty("id").GetString());
        Assert.Equal("Abogado", mia.GetProperty("nombre").GetString());
        Assert.Equal("Titulo", mia.GetProperty("tipo").GetString());
        // El dueño SI ve el estado interno (API-24), y que la entidad aun no esta reconocida.
        Assert.Equal("Borrador", mia.GetProperty("estado").GetString());
        Assert.False(mia.GetProperty("entidadReconocida").GetBoolean());
        Assert.False(mia.GetProperty("tieneRespaldo").GetBoolean());

        Assert.Equal(HttpStatusCode.NoContent, (await EliminarCredencialAsync(id)).StatusCode);
        var despues = (await _cliente.GetFromJsonAsync<JsonDocument>("/api/mi/credenciales"))!.RootElement;
        Assert.Equal(0, despues.GetArrayLength());
    }

    [Theory]
    [InlineData("ab", "2020-03-15", null, "nombre")]             // nombre demasiado corto
    [InlineData("Abogado", "2020-03-15", "2020-03-15", "vence")] // vence el mismo dia
    [InlineData("Abogado", "2999-01-01", null, "futura")]        // obtenida en el futuro
    public async Task Una_credencial_invalida_devuelve_400_y_no_se_guarda(
        string nombre, string obtenidaEn, string? venceEn, string pista)
    {
        await UnPrestadorConSesionAsync();

        var r = await CrearCredencialAsync(UnaCredencial(nombre, obtenidaEn: obtenidaEn, venceEn: venceEn));

        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        Assert.Contains(pista, (await r.Content.ReadAsStringAsync()).ToLowerInvariant());
        var lista = (await _cliente.GetFromJsonAsync<JsonDocument>("/api/mi/credenciales"))!.RootElement;
        Assert.Equal(0, lista.GetArrayLength());
    }

    /// <summary>
    /// Un cuerpo ilegible es un 400 en todos los entornos. En Development ASP.NET lanzaba la
    /// excepcion y el manejador global la volvia un 500, asi que las pruebas veian una API
    /// distinta de la que se despliega.
    /// </summary>
    [Fact]
    public async Task Un_cuerpo_que_no_es_json_devuelve_400_y_no_500()
    {
        var r = await _cliente.PostAsync("/api/auth/registro",
            new StringContent("{ esto no es json", System.Text.Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
    }

    [Fact]
    public async Task Un_tipo_de_credencial_que_no_existe_devuelve_400()
    {
        await UnPrestadorConSesionAsync();

        var r = await CrearCredencialAsync(UnaCredencial(tipo: "Hechicero"));

        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
    }

    /// <summary>
    /// E-403: eliminar la credencial de otra persona responde igual que una que no existe. Si
    /// respondiera distinto, quien prueba ids sabria cuales existen.
    /// </summary>
    [Fact]
    public async Task Eliminar_la_credencial_de_otro_responde_404_igual_que_una_inexistente()
    {
        await UnPrestadorConSesionAsync();
        var idAjeno = await IdDeAsync(await CrearCredencialAsync(UnaCredencial()));
        var primero = _cliente;

        // Otra persona, con su propia sesion (otro cliente: otra jarra de cookies).
        using var otroCliente = _app.CreateClient(new WebApplicationFactoryClientOptions
        {
            HandleCookies = true, AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost"),
        });
        _cliente = otroCliente;
        try
        {
            await UnPrestadorConSesionAsync();
            var ajena = await EliminarCredencialAsync(idAjeno);
            var inexistente = await EliminarCredencialAsync(Guid.NewGuid().ToString());

            Assert.Equal(HttpStatusCode.NotFound, ajena.StatusCode);
            Assert.Equal(inexistente.StatusCode, ajena.StatusCode);
            Assert.Equal(await inexistente.Content.ReadAsStringAsync(), await ajena.Content.ReadAsStringAsync());
        }
        finally { _cliente = primero; }

        // La de la primera persona sigue ahi.
        var lista = (await _cliente.GetFromJsonAsync<JsonDocument>("/api/mi/credenciales"))!.RootElement;
        Assert.Equal(1, lista.GetArrayLength());
    }

    /// <summary>
    /// La ficha publica muestra credenciales sin borradores y sin estado interno: "Declarada por
    /// el usuario" o "Verificada", y nada mas (RN-S2-03). El estado se mueve por la base porque
    /// enviar a revision y verificar esperan a MOD-08 y al Verificador.
    /// </summary>
    [Fact]
    public async Task La_ficha_publica_muestra_credenciales_sin_borradores_ni_estado_interno()
    {
        var slug = await UnaFichaCompletaAsync();
        Assert.Equal(HttpStatusCode.Created, (await CrearCredencialAsync(UnaCredencial("Borrador privado"))).StatusCode);
        var idRevision = Guid.Parse(await IdDeAsync(await CrearCredencialAsync(UnaCredencial("Magister en derecho"))));
        var idVerificada = Guid.Parse(await IdDeAsync(await CrearCredencialAsync(UnaCredencial("Abogado titulado"))));

        using (var ctx = ContextoDePrueba.Crear())
        {
            (await ctx.Credenciales.FindAsync(idRevision))!.Estado = EstadoCredencial.EnRevision;
            (await ctx.Credenciales.FindAsync(idVerificada))!.Verificar(Guid.NewGuid());
            await ctx.SaveChangesAsync();
        }

        using var visitante = UnVisitante();
        var texto = await (await visitante.GetAsync($"/api/prestadores/{slug}")).Content.ReadAsStringAsync();
        var credenciales = JsonDocument.Parse(texto).RootElement
            .GetProperty("credenciales").EnumerateArray().ToList();

        Assert.Equal(new[] { "Abogado titulado", "Magister en derecho" },
            credenciales.Select(c => c.GetProperty("nombre").GetString()).ToArray());
        Assert.Equal("Verificada", credenciales[0].GetProperty("etiqueta").GetString());
        Assert.Equal("Declarada por el usuario", credenciales[1].GetProperty("etiqueta").GetString());
        Assert.DoesNotContain("Borrador privado", texto);
        foreach (var interno in new[] { "EnRevision", "Rechazada", "selloPor", "documentos", "reservada" })
            Assert.DoesNotContain(interno, texto, StringComparison.OrdinalIgnoreCase);
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
