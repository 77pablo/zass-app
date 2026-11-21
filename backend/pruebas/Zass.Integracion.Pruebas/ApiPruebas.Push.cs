using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Zass.Dominio.Comun;
using Zass.Integracion.Pruebas.Push;

namespace Zass.Integracion.Pruebas;

/// <summary>
/// ES-3 · API-45 por HTTP: registrar este navegador para push, darlo de baja al cerrar sesion y
/// la clave publica VAPID. Lo que se vigila: sesion y CSRF, que solo se acepten servicios de
/// push conocidos (un endpoint cualquiera seria SSRF), y que la baja no toque lo ajeno.
/// </summary>
public partial class ApiPruebas
{
    private async Task<string> UnaCuentaConSesionAsync()
    {
        var correo = NuevoCorreo();
        await RegistrarAsync(correo);
        await VerificarAsync(correo);
        await IniciarSesionAsync(correo);
        return correo;
    }

    private static object NavegadorValido(string? endpoint = null)
    {
        var (p256, auth) = PushIntegracionPruebas.ClavesDeNavegador();
        return new { endpoint = endpoint ?? PushIntegracionPruebas.Endpoint(), p256dh = p256, auth };
    }

    private async Task<HttpResponseMessage> PushAsync(HttpMethod metodo, object cuerpo, bool conCsrf = true)
    {
        using var peticion = new HttpRequestMessage(metodo, "/api/mi/notificaciones/push")
        {
            Content = JsonContent.Create(cuerpo),
        };
        if (conCsrf) peticion.Headers.Add("X-CSRF-Token", await TokenCsrfAsync());
        return await _cliente.SendAsync(peticion);
    }

    private static async Task<Guid?> DuenoDelNavegadorAsync(string endpoint)
    {
        using var ctx = ContextoDePrueba.Crear();
        return await ctx.SuscripcionesPush.Where(s => s.Endpoint == endpoint)
            .Select(s => (Guid?)s.UsuarioId).SingleOrDefaultAsync();
    }

    // ------------------------------------------------------------------------------

    [Fact]
    public async Task ES3_sin_claves_VAPID_la_clave_publica_responde_404()
    {
        var r = await _cliente.GetAsync("/api/notificaciones/push/clave");

        Assert.Equal(HttpStatusCode.NotFound, r.StatusCode);
    }

    [Fact]
    public async Task ES3_registrar_un_navegador_exige_sesion()
    {
        var r = await PushAsync(HttpMethod.Post, NavegadorValido(), conCsrf: false);

        Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode);
    }

    [Fact]
    public async Task ES3_registrar_un_navegador_exige_CSRF()
    {
        await UnaCuentaConSesionAsync();

        var r = await PushAsync(HttpMethod.Post, NavegadorValido(), conCsrf: false);

        Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
    }

    [Fact]
    public async Task ES3_registra_el_navegador_a_nombre_de_la_sesion()
    {
        var correo = await UnaCuentaConSesionAsync();
        var endpoint = PushIntegracionPruebas.Endpoint();

        var r = await PushAsync(HttpMethod.Post, NavegadorValido(endpoint));

        Assert.Equal(HttpStatusCode.NoContent, r.StatusCode);
        using var ctx = ContextoDePrueba.Crear();
        var usuario = await ctx.Usuarios.SingleAsync(u => u.Correo == correo);
        Assert.Equal(usuario.Id, await DuenoDelNavegadorAsync(endpoint));
    }

    [Theory]
    [InlineData("http://fcm.googleapis.com/fcm/send/abc")]            // sin HTTPS
    [InlineData("https://127.0.0.1/interno")]                         // direccion interna
    [InlineData("https://fcm.googleapis.com.atacante.cl/x")]           // dominio parecido
    [InlineData("https://fcm.googleapis.com:8443/fcm/send/abc")]       // otro puerto
    [InlineData("https://zass.cl/api/admin")]                         // cualquier otro sitio
    public async Task ES3_solo_acepta_servicios_de_push_conocidos(string endpoint)
    {
        await UnaCuentaConSesionAsync();

        var r = await PushAsync(HttpMethod.Post, NavegadorValido(endpoint));

        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        Assert.Null(await DuenoDelNavegadorAsync(endpoint));
    }

    [Fact]
    public async Task ES3_rechaza_claves_del_navegador_mal_formadas()
    {
        await UnaCuentaConSesionAsync();

        var r = await PushAsync(HttpMethod.Post,
            new { endpoint = PushIntegracionPruebas.Endpoint(), p256dh = "corta", auth = "AAAA" });

        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
    }

    [Fact]
    public async Task ES3_dar_de_baja_borra_el_navegador_propio()
    {
        await UnaCuentaConSesionAsync();
        var endpoint = PushIntegracionPruebas.Endpoint();
        Assert.Equal(HttpStatusCode.NoContent, (await PushAsync(HttpMethod.Post, NavegadorValido(endpoint))).StatusCode);

        var r = await PushAsync(HttpMethod.Delete, new { endpoint });

        Assert.Equal(HttpStatusCode.NoContent, r.StatusCode);
        Assert.Null(await DuenoDelNavegadorAsync(endpoint));
    }

    [Fact]
    public async Task ES3_dar_de_baja_un_navegador_ajeno_responde_igual_y_no_lo_toca()
    {
        // El navegador es de otra cuenta. La respuesta no puede delatarlo (204 igual).
        var otra = await UnaCuentaConSesionAsync();
        var endpoint = PushIntegracionPruebas.Endpoint();
        await PushAsync(HttpMethod.Post, NavegadorValido(endpoint));
        Guid duenaId;
        using (var ctx = ContextoDePrueba.Crear())
            duenaId = (await ctx.Usuarios.SingleAsync(u => u.Correo == otra)).Id;

        await UnaCuentaConSesionAsync();   // ahora la sesion es de otra persona
        var r = await PushAsync(HttpMethod.Delete, new { endpoint });

        Assert.Equal(HttpStatusCode.NoContent, r.StatusCode);
        Assert.Equal(duenaId, await DuenoDelNavegadorAsync(endpoint));
    }

    [Fact]
    public async Task ES3_la_baja_queda_abierta_aunque_falten_datos_de_la_cuenta()
    {
        // ES-2 bloquea con 428 casi todo hasta completar los datos; dejar de recibir push no.
        await UnaCuentaAntiguaConSesionAsync();

        var baja = await PushAsync(HttpMethod.Delete, new { endpoint = PushIntegracionPruebas.Endpoint() });
        var alta = await PushAsync(HttpMethod.Post, NavegadorValido());

        Assert.Equal(HttpStatusCode.NoContent, baja.StatusCode);
        Assert.Equal((HttpStatusCode)428, alta.StatusCode);
    }

    [Fact]
    public async Task ES3_con_navegador_registrado_los_avisos_nuevos_llevan_su_push()
    {
        // De punta a punta: API-45 y despues un aviso real encolado por un caso de uso.
        var correo = await UnaCuentaConSesionAsync();
        await PushAsync(HttpMethod.Post, NavegadorValido());
        Guid id;
        using (var ctx = ContextoDePrueba.Crear())
        {
            id = (await ctx.Usuarios.SingleAsync(u => u.Correo == correo)).Id;
            ctx.Notificaciones.Add(new Notificacion
            {
                DestinatarioId = id, Tipo = TipoNotificacion.PerfilPublicado,
                ClaveIdempotencia = $"es3-api:{Guid.NewGuid():N}", Cuerpo = "{}",
            });
            await ctx.SaveChangesAsync();
        }

        using var lee = ContextoDePrueba.Crear();
        Assert.True(await lee.Notificaciones.AnyAsync(n =>
            n.DestinatarioId == id && n.Canal == CanalNotificacion.Push && n.Tipo == TipoNotificacion.PerfilPublicado));
    }
}
