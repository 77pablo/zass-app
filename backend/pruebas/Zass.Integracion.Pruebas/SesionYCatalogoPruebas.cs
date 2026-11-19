using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Zass.Dominio.Comun;

namespace Zass.Integracion.Pruebas;

/// <summary>
/// Pruebas de integracion para los endpoints de sesion y catalogo de la T52b:
/// API-10 POST /api/auth/sesion/renovar · API-12 DELETE /api/auth/sesiones ·
/// API-15 POST /api/auth/contrasena/restablecer · API-71 POST /api/auth/correo/reenviar ·
/// CS-02b POST /api/mi/contrasena · API-03 GET /api/rubros/{rubroId}/servicios
/// </summary>
public class SesionYCatalogoPruebas : IAsyncLifetime
{
    private readonly WebApplicationFactory<Program> _app = new();
    private readonly List<string> _correosCreados = new();
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
        if (_correosCreados.Count > 0)
        {
            using var ctx = ContextoDePrueba.Crear();
            foreach (var correo in _correosCreados)
            {
                var usuario = await ctx.Usuarios.FirstOrDefaultAsync(u => u.Correo == correo);
                if (usuario is null) continue;

                ctx.Auditoria.RemoveRange(
                    ctx.Auditoria.Where(a => a.ActorId == usuario.Id || a.EntidadId == usuario.Id));
                ctx.Usuarios.Remove(usuario);
                await ctx.SaveChangesAsync();
            }
        }

        _cliente.Dispose();
        _app.Dispose();
    }

    private string NuevoCorreo()
    {
        var correo = $"t52b-{Guid.NewGuid():N}@zass.local";
        _correosCreados.Add(correo);
        return correo;
    }

    private static async Task<string> TokenDeVerificacionAsync(string correo)
    {
        using var ctx = ContextoDePrueba.Crear();
        var usuario = await ctx.Usuarios.SingleAsync(u => u.Correo == correo);
        var aviso = await ctx.Notificaciones
            .Where(n => n.DestinatarioId == usuario.Id
                     && n.Tipo == TipoNotificacion.CorreoVerificacion)
            .OrderByDescending(n => n.ProgramadaPara)
            .FirstAsync();

        return Zass.Aplicacion.Puertos.CargaDeAviso.LeerToken(aviso.Cuerpo)!;
    }

    private async Task RegistrarAsync(string correo, string rol = "buscador")
    {
        var respuesta = await _cliente.PostAsJsonAsync("/api/auth/registro", new
        {
            correo, contrasena = "contrasena-larga-para-pruebas-123", nombre = "Usuario Prueba", rol,
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

    private async Task IniciarSesionAsync(string correo, string contrasena = "contrasena-larga-para-pruebas-123")
    {
        var respuesta = await _cliente.PostAsJsonAsync("/api/auth/sesion", new
        {
            correo, contrasena,
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

    // ============================================================ API-10: POST /api/auth/sesion/renovar

    /// <summary>
    /// API-10: Renueva el token de acceso con el de refresco.
    /// Camino feliz: con una cookie de refresco valida, renueva la sesion.
    /// </summary>
    [Fact]
    public async Task API_10_Renueva_la_sesion_con_el_token_de_refresco()
    {
        var correo = NuevoCorreo();
        await RegistrarAsync(correo);
        await VerificarAsync(correo);

        // Iniciar sesion para obtener las cookies
        var sesion = await _cliente.PostAsJsonAsync("/api/auth/sesion", new
        {
            correo, contrasena = "contrasena-larga-para-pruebas-123",
        });
        Assert.Equal(HttpStatusCode.OK, sesion.StatusCode);

        // La cookie de refresco debe estar presente
        var cookies = sesion.Headers.GetValues("Set-Cookie").ToList();
        Assert.Contains(cookies, c => c.StartsWith("zass_refresco"));

        // Simular que la cookie de acceso expira: esperar un poco o simplemente llamar a renovar
        // La API usa las cookies automaticamente porque HandleCookies = true
        var renovar = await _cliente.PostAsync("/api/auth/sesion/renovar", null);

        Assert.Equal(HttpStatusCode.NoContent, renovar.StatusCode);
    }

    /// <summary>
    /// API-10: Sin cookie de refresco, renovar falla con 401.
    /// </summary>
    [Fact]
    public async Task API_10_Sin_cookie_de_refresco_devuelve_401()
    {
        // Cliente sin cookies
        using var sinCookies = _app.CreateClient(new WebApplicationFactoryClientOptions
        {
            HandleCookies = false,
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost"),
        });

        var renovar = await sinCookies.PostAsync("/api/auth/sesion/renovar", null);

        Assert.Equal(HttpStatusCode.Unauthorized, renovar.StatusCode);
    }

    /// <summary>
    /// API-10: Despues de revocar todas las sesiones (API-12), el token viejo falla.
    /// </summary>
    [Fact]
    public async Task API_10_Despues_de_revocar_todas_las_sesiones_el_token_viejo_falla()
    {
        var correo = NuevoCorreo();
        await RegistrarAsync(correo);
        await VerificarAsync(correo);
        await IniciarSesionAsync(correo);

        // Revocar todas las sesiones
        var csrf = await TokenCsrfAsync();
        var peticion = new HttpRequestMessage(HttpMethod.Delete, "/api/auth/sesiones");
        peticion.Headers.Add("X-CSRF-Token", csrf);
        var revocar = await _cliente.SendAsync(peticion);
        Assert.Equal(HttpStatusCode.NoContent, revocar.StatusCode);

        // Intentar renovar con el token viejo debe fallar
        var renovar = await _cliente.PostAsync("/api/auth/sesion/renovar", null);

        // El token de refresco ya no es valido
        Assert.Equal(HttpStatusCode.Unauthorized, renovar.StatusCode);
    }

    // ============================================================ API-12: DELETE /api/auth/sesiones

    /// <summary>
    /// API-12: Revoca TODAS las sesiones del usuario (CS-03).
    /// Camino feliz: con sesion, revocar todas las sesiones.
    /// </summary>
    [Fact]
    public async Task API_12_Revoca_todas_las_sesiones_del_usuario()
    {
        var correo = NuevoCorreo();
        await RegistrarAsync(correo);
        await VerificarAsync(correo);
        await IniciarSesionAsync(correo);

        var csrf = await TokenCsrfAsync();
        var peticion = new HttpRequestMessage(HttpMethod.Delete, "/api/auth/sesiones");
        peticion.Headers.Add("X-CSRF-Token", csrf);
        var revocar = await _cliente.SendAsync(peticion);

        Assert.Equal(HttpStatusCode.NoContent, revocar.StatusCode);

        // Despues de revocar, mi cuenta debe responder 401
        var cuenta = await _cliente.GetAsync("/api/mi/cuenta");
        Assert.Equal(HttpStatusCode.Unauthorized, cuenta.StatusCode);
    }

    /// <summary>
    /// API-12: Sin sesion, revocar sesiones responde 401.
    /// </summary>
    [Fact]
    public async Task API_12_Sin_sesion_devuelve_401()
    {
        using var sinSesion = _app.CreateClient(new WebApplicationFactoryClientOptions
        {
            HandleCookies = false,
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost"),
        });

        var peticion = new HttpRequestMessage(HttpMethod.Delete, "/api/auth/sesiones");
        var revocar = await sinSesion.SendAsync(peticion);

        Assert.Equal(HttpStatusCode.Unauthorized, revocar.StatusCode);
    }

    // ============================================================ API-15: POST /api/auth/contrasena/restablecer

    /// <summary>
    /// API-15: Restablecer contrasena con token valido.
    /// El token es de un solo uso: despues de usarlo, falla.
    /// </summary>
    [Fact]
    public async Task API_15_Restablece_la_contrasena_con_token_valido()
    {
        var correo = NuevoCorreo();
        await RegistrarAsync(correo);
        await VerificarAsync(correo);

        // Pedir recuperacion de contrasena
        var pedir = await _cliente.PostAsJsonAsync("/api/auth/contrasena/recuperar", new
        {
            correo,
        });
        // Responde igual exista o no la cuenta (RN-S1-12)
        Assert.Equal(HttpStatusCode.NoContent, pedir.StatusCode);

        // Leer el token de la base
        using (var ctx = ContextoDePrueba.Crear())
        {
            var usuario = await ctx.Usuarios.SingleAsync(u => u.Correo == correo);
            var aviso = await ctx.Notificaciones
                .Where(n => n.DestinatarioId == usuario.Id
                         && n.Tipo == TipoNotificacion.RecuperarContrasena)
                .OrderByDescending(n => n.ProgramadaPara)
                .FirstAsync();

            var token = Zass.Aplicacion.Puertos.CargaDeAviso.LeerToken(aviso.Cuerpo)!;

            // Usar el token para restablecer
            var nuevaContrasena = "nueva-contrasena-larga-para-pruebas-456";
            var restablecer = await _cliente.PostAsJsonAsync("/api/auth/contrasena/restablecer", new
            {
                token,
                contrasenaNueva = nuevaContrasena,
            });

            Assert.Equal(HttpStatusCode.NoContent, restablecer.StatusCode);

            // Despues de restablecer, las sesiones se revocan: iniciar sesion con la nueva contrasena debe funcionar
            var iniciar = await _cliente.PostAsJsonAsync("/api/auth/sesion", new
            {
                correo, contrasena = nuevaContrasena,
            });
            Assert.Equal(HttpStatusCode.OK, iniciar.StatusCode);
        }
    }

    /// <summary>
    /// API-15: El token es de un solo uso: despues de usado, falla.
    /// </summary>
    [Fact]
    public async Task API_15_Token_usado_no_funciona_segunda_vez()
    {
        var correo = NuevoCorreo();
        await RegistrarAsync(correo);
        await VerificarAsync(correo);

        // Pedir recuperacion
        await _cliente.PostAsJsonAsync("/api/auth/contrasena/recuperar", new { correo });

        // Leer el token
        using var ctx = ContextoDePrueba.Crear();
        var usuario = await ctx.Usuarios.SingleAsync(u => u.Correo == correo);
        var aviso = await ctx.Notificaciones
            .Where(n => n.DestinatarioId == usuario.Id
                     && n.Tipo == TipoNotificacion.RecuperarContrasena)
            .OrderByDescending(n => n.ProgramadaPara)
            .FirstAsync();
        var token = Zass.Aplicacion.Puertos.CargaDeAviso.LeerToken(aviso.Cuerpo)!;

        // Primera vez: exito
        var primera = await _cliente.PostAsJsonAsync("/api/auth/contrasena/restablecer", new
        {
            token,
            contrasenaNueva = "primera-contrasena-larga-123",
        });
        Assert.Equal(HttpStatusCode.NoContent, primera.StatusCode);

        // Segunda vez con el mismo token: debe fallar
        var segunda = await _cliente.PostAsJsonAsync("/api/auth/contrasena/restablecer", new
        {
            token,
            contrasenaNueva = "segunda-contrasena-larga-456",
        });

        Assert.Equal(HttpStatusCode.Unauthorized, segunda.StatusCode);
    }

    /// <summary>
    /// API-15: Token invalido o mal formado devuelve 400.
    /// </summary>
    [Fact]
    public async Task API_15_Token_invalido_devuelve_400()
    {
        var correo = NuevoCorreo();
        await RegistrarAsync(correo);

        var restablecer = await _cliente.PostAsJsonAsync("/api/auth/contrasena/restablecer", new
        {
            token = "token-invalido-basura",
            contrasenaNueva = "nueva-contrasena-larga-789",
        });

        Assert.Equal(HttpStatusCode.BadRequest, restablecer.StatusCode);
    }

    // ============================================================ API-71: POST /api/auth/correo/reenviar

    /// <summary>
    /// API-71: Reenvia el enlace de verificacion.
    /// Responde LO MISMO exista o no la cuenta (RN-S1-12).
    /// </summary>
    [Fact]
    public async Task API_71_Responde_igual_exista_o_no_la_cuenta()
    {
        var correoExistente = NuevoCorreo();
        await RegistrarAsync(correoExistente);

        // Cuenta existente
        var existente = await _cliente.PostAsJsonAsync("/api/auth/correo/reenviar", new
        {
            correo = correoExistente,
        });

        // Cuenta inexistente
        var inexistente = await _cliente.PostAsJsonAsync("/api/auth/correo/reenviar", new
        {
            correo = "no-existe-" + Guid.NewGuid().ToString("N") + "@zass.local",
        });

        // Ambas deben responder 204 No Content
        Assert.Equal(HttpStatusCode.NoContent, existente.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, inexistente.StatusCode);

        // Y el cuerpo debe ser identico
        Assert.Equal(
            await CuerpoComparable.De(existente),
            await CuerpoComparable.De(inexistente));
    }

    /// <summary>
    /// API-71: Invalida el enlace anterior (RN-S1-11).
    /// Despues de reenviar, el token viejo no funciona.
    /// </summary>
    [Fact]
    public async Task API_71_Invalida_el_enlace_anterior()
    {
        var correo = NuevoCorreo();
        await RegistrarAsync(correo);

        // Leer el token original
        using (var ctx = ContextoDePrueba.Crear())
        {
            var usuario = await ctx.Usuarios.SingleAsync(u => u.Correo == correo);
            var avisoOriginal = await ctx.Notificaciones
                .Where(n => n.DestinatarioId == usuario.Id
                         && n.Tipo == TipoNotificacion.CorreoVerificacion)
                .OrderByDescending(n => n.ProgramadaPara)
                .FirstAsync();
            var tokenOriginal = Zass.Aplicacion.Puertos.CargaDeAviso.LeerToken(avisoOriginal.Cuerpo)!;

            // Reenviar el enlace
            var reenviar = await _cliente.PostAsJsonAsync("/api/auth/correo/reenviar", new
            {
                correo,
            });
            Assert.Equal(HttpStatusCode.NoContent, reenviar.StatusCode);

            // Intentar verificar con el token original debe fallar
            var verificar = await _cliente.PostAsJsonAsync("/api/auth/correo/verificar", new
            {
                token = tokenOriginal,
            });

            // El token viejo ya no es valido
            Assert.Equal(HttpStatusCode.Unauthorized, verificar.StatusCode);
        }
    }

    /// <summary>
    /// API-71: Tiene espera de 2 minutos por cuenta (RN-S1-10).
    /// Esta prueba verifica que el endpoint existe; el limite real se prueba en LimiteDeTasaPruebas.
    /// </summary>
    [Fact]
    public async Task API_71_Reenviar_verificacion_existe_y_responde_204()
    {
        var correo = NuevoCorreo();
        await RegistrarAsync(correo);

        var reenviar = await _cliente.PostAsJsonAsync("/api/auth/correo/reenviar", new
        {
            correo,
        });

        Assert.Equal(HttpStatusCode.NoContent, reenviar.StatusCode);
    }

    // ============================================================ CS-02b: POST /api/mi/contrasena

    /// <summary>
    /// CS-02b: Cambiar contrasena desde la sesion.
    /// Exige la contrasena actual.
    /// </summary>
    [Fact]
    public async Task CS_02b_Cambia_la_contrasena_con_la_contrasena_actual_correcta()
    {
        var correo = NuevoCorreo();
        await RegistrarAsync(correo);
        await VerificarAsync(correo);
        await IniciarSesionAsync(correo);

        var csrf = await TokenCsrfAsync();
        var peticion = new HttpRequestMessage(HttpMethod.Post, "/api/mi/contrasena")
        {
            Content = JsonContent.Create(new
            {
                contrasenaActual = "contrasena-larga-para-pruebas-123",
                contrasenaNueva = "nueva-contrasena-larga-para-pruebas-456",
            }),
        };
        peticion.Headers.Add("X-CSRF-Token", csrf);

        var respuesta = await _cliente.SendAsync(peticion);

        Assert.Equal(HttpStatusCode.NoContent, respuesta.StatusCode);

        // Verificar que la nueva contrasena funciona
        // Primero cerrar sesion
        var cerrar = new HttpRequestMessage(HttpMethod.Delete, "/api/auth/sesion");
        cerrar.Headers.Add("X-CSRF-Token", await TokenCsrfAsync());
        await _cliente.SendAsync(cerrar);

        // Iniciar con la nueva contrasena
        var iniciar = await _cliente.PostAsJsonAsync("/api/auth/sesion", new
        {
            correo, contrasena = "nueva-contrasena-larga-para-pruebas-456",
        });
        Assert.Equal(HttpStatusCode.OK, iniciar.StatusCode);
    }

    /// <summary>
    /// CS-02b: Con la contrasena actual incorrecta, falla.
    /// </summary>
    [Fact]
    public async Task CS_02b_Contrasena_actual_incorrecta_devuelve_400()
    {
        var correo = NuevoCorreo();
        await RegistrarAsync(correo);
        await VerificarAsync(correo);
        await IniciarSesionAsync(correo);

        var csrf = await TokenCsrfAsync();
        var peticion = new HttpRequestMessage(HttpMethod.Post, "/api/mi/contrasena")
        {
            Content = JsonContent.Create(new
            {
                contrasenaActual = "contrasena-equivocada",
                contrasenaNueva = "nueva-contrasena-larga-para-pruebas-789",
            }),
        };
        peticion.Headers.Add("X-CSRF-Token", csrf);

        var respuesta = await _cliente.SendAsync(peticion);

        Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);
    }

    /// <summary>
    /// CS-02b: Sin sesion, responde 401.
    /// </summary>
    [Fact]
    public async Task CS_02b_Sin_sesion_devuelve_401()
    {
        using var sinSesion = _app.CreateClient(new WebApplicationFactoryClientOptions
        {
            HandleCookies = false,
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost"),
        });

        var respuesta = await sinSesion.PostAsJsonAsync("/api/mi/contrasena", new
        {
            contrasenaActual = "algo",
            contrasenaNueva = "nueva-algo",
        });

        Assert.Equal(HttpStatusCode.Unauthorized, respuesta.StatusCode);
    }

    // ============================================================ API-03: GET /api/rubros/{rubroId}/servicios

    /// <summary>
    /// API-03: Servicios de un rubro existente.
    /// Publico: no requiere sesion.
    /// </summary>
    [Fact]
    public async Task API_03_Servicios_de_un_rubro_existente()
    {
        using var visitante = _app.CreateClient(new WebApplicationFactoryClientOptions
        {
            HandleCookies = false,
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost"),
        });

        // Rubro 1 = Corretaje (del seed)
        var respuesta = await visitante.GetAsync("/api/rubros/1/servicios");

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        var servicios = await respuesta.Content.ReadFromJsonAsync<JsonDocument>();
        Assert.NotNull(servicios);
        Assert.True(servicios.RootElement.GetArrayLength() > 0);
    }

    /// <summary>
    /// API-03: Rubro inexistente devuelve 404.
    /// </summary>
    [Fact]
    public async Task API_03_Rubro_inexistente_devuelve_404()
    {
        using var visitante = _app.CreateClient(new WebApplicationFactoryClientOptions
        {
            HandleCookies = false,
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost"),
        });

        var respuesta = await visitante.GetAsync("/api/rubros/99999/servicios");

        Assert.Equal(HttpStatusCode.NotFound, respuesta.StatusCode);
        var cuerpo = await respuesta.Content.ReadAsStringAsync();
        Assert.Contains("rubro", cuerpo, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// API-03: Rubro con identificador fuera de rango (no cabe en smallint) devuelve 404.
    /// </summary>
    [Theory]
    [InlineData("77777")]
    [InlineData("99999")]
    [InlineData("0")]
    public async Task API_03_Rubro_fuera_de_rango_devuelve_404(string rubroId)
    {
        using var visitante = _app.CreateClient(new WebApplicationFactoryClientOptions
        {
            HandleCookies = false,
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost"),
        });

        var respuesta = await visitante.GetAsync($"/api/rubros/{rubroId}/servicios");

        Assert.Equal(HttpStatusCode.NotFound, respuesta.StatusCode);
    }

    /// <summary>
    /// API-03: No hay "ajeno" para rubros: inexistente = 404.
    /// Esta prueba verifica que no hay tratamiento especial para "ajeno".
    /// </summary>
    [Fact]
    public async Task API_03_No_hay_tratamiento_especial_para_ajeno()
    {
        // API-03 es publico y solo devuelve servicios de un rubro.
        // No hay concepto de "ajeno" aqui: o existe el rubro o es 404.
        using var visitante = _app.CreateClient(new WebApplicationFactoryClientOptions
        {
            HandleCookies = false,
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost"),
        });

        // Un rubro que existe
        var existente = await visitante.GetAsync("/api/rubros/1/servicios");
        Assert.Equal(HttpStatusCode.OK, existente.StatusCode);

        // Un rubro que no existe
        var inexistente = await visitante.GetAsync("/api/rubros/99999/servicios");
        Assert.Equal(HttpStatusCode.NotFound, inexistente.StatusCode);
    }
}
