using System.Net;
using System.Linq;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Zass.Aplicacion.Identidad;

namespace Zass.Integracion.Pruebas;

/// <summary>
/// Sello de seguridad (decision de Pablo, 22-nov-2026): cerrar todas las sesiones, cambiar la
/// contrasena, tocar el MFA o eliminar la cuenta invalidan AL INSTANTE la cookie de acceso, que
/// antes seguia valiendo hasta 15 minutos. Se prueba con la cookie de verdad: se copia la que
/// entrega el inicio de sesion y se vuelve a presentar, desde otro cliente, despues del cambio.
/// </summary>
public partial class ApiPruebas
{
    private const string ClaveNuevaDelSello = "una-contrasena-nueva-larga-7";

    /// <summary>El valor de la cookie de acceso que entrego esa respuesta de inicio de sesion.</summary>
    private static string CookieDeAcceso(HttpResponseMessage entrada)
    {
        // Puede venir mas de un Set-Cookie "zass_sesion=": si el cliente trae una cookie con un
        // sello ya invalido, OnValidatePrincipal la rechaza y emite una de BORRADO (valor vacio)
        // ademas de la nueva. Nos quedamos con la ultima que trae un valor de verdad.
        var linea = entrada.Headers.GetValues("Set-Cookie")
            .Where(c => c.StartsWith("zass_sesion=") && c.Split(';')[0]["zass_sesion=".Length..].Length > 0)
            .Last();
        return linea.Split(';')[0]["zass_sesion=".Length..];
    }

    /// <summary>Presenta una cookie de acceso copiada, desde un cliente sin cookies propias.</summary>
    private async Task<HttpStatusCode> MiCuentaConCookieCopiadaAsync(string valor)
    {
        using var otro = _app.CreateClient(new WebApplicationFactoryClientOptions
        {
            HandleCookies = false, AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost"),
        });
        using var peticion = new HttpRequestMessage(HttpMethod.Get, "/api/mi/cuenta");
        peticion.Headers.Add("Cookie", $"zass_sesion={valor}");
        return (await otro.SendAsync(peticion)).StatusCode;
    }

    private async Task<string> EntrarYCopiarCookieAsync(string correo)
    {
        var entrada = await EntrarAsync(correo, ClaveBuena);
        Assert.Equal(HttpStatusCode.OK, entrada.StatusCode);
        return CookieDeAcceso(entrada);
    }

    [Fact]
    public async Task Cambiar_la_contrasena_invalida_la_cookie_de_acceso_anterior_en_la_peticion_siguiente()
    {
        var correo = NuevoCorreo();
        await RegistrarAsync(correo);
        await VerificarAsync(correo);
        var cookie = await EntrarYCopiarCookieAsync(correo);

        // El camino normal: entrar y usar la cookie sigue andando, tambien copiada a otro cliente.
        Assert.Equal(HttpStatusCode.OK, (await _cliente.GetAsync("/api/mi/cuenta")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, await MiCuentaConCookieCopiadaAsync(cookie));

        var cambio = new HttpRequestMessage(HttpMethod.Post, "/api/mi/contrasena")
        {
            Content = JsonContent.Create(new { contrasenaActual = ClaveBuena, contrasenaNueva = ClaveNuevaDelSello }),
        };
        cambio.Headers.Add("X-CSRF-Token", await TokenCsrfAsync());
        Assert.Equal(HttpStatusCode.NoContent, (await _cliente.SendAsync(cambio)).StatusCode);

        // La cookie anterior ya no sirve, ni la copiada ni la de este mismo aparato...
        Assert.Equal(HttpStatusCode.Unauthorized, await MiCuentaConCookieCopiadaAsync(cookie));
        Assert.Equal(HttpStatusCode.Unauthorized, (await _cliente.GetAsync("/api/mi/cuenta")).StatusCode);

        // ...y entrando con la contrasena nueva se vuelve a la normalidad, con un sello nuevo.
        var otraEntrada = await EntrarAsync(correo, ClaveNuevaDelSello);
        Assert.Equal(HttpStatusCode.OK, otraEntrada.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _cliente.GetAsync("/api/mi/cuenta")).StatusCode);
        Assert.NotEqual(cookie, CookieDeAcceso(otraEntrada));
    }

    [Fact]
    public async Task Cerrar_todas_las_sesiones_invalida_la_cookie_de_acceso_copiada_en_otro_aparato()
    {
        var correo = NuevoCorreo();
        await RegistrarAsync(correo);
        await VerificarAsync(correo);
        var cookie = await EntrarYCopiarCookieAsync(correo);
        Assert.Equal(HttpStatusCode.OK, await MiCuentaConCookieCopiadaAsync(cookie));

        var revocar = new HttpRequestMessage(HttpMethod.Delete, "/api/auth/sesiones");
        revocar.Headers.Add("X-CSRF-Token", await TokenCsrfAsync());
        Assert.Equal(HttpStatusCode.NoContent, (await _cliente.SendAsync(revocar)).StatusCode);

        Assert.Equal(HttpStatusCode.Unauthorized, await MiCuentaConCookieCopiadaAsync(cookie));
    }

    [Fact]
    public async Task Cerrar_solo_la_sesion_actual_no_toca_el_sello_de_los_demas_aparatos()
    {
        var correo = NuevoCorreo();
        await RegistrarAsync(correo);
        await VerificarAsync(correo);
        var cookie = await EntrarYCopiarCookieAsync(correo);

        var salir = new HttpRequestMessage(HttpMethod.Delete, "/api/auth/sesion");
        salir.Headers.Add("X-CSRF-Token", await TokenCsrfAsync());
        Assert.Equal(HttpStatusCode.NoContent, (await _cliente.SendAsync(salir)).StatusCode);

        // API-11 solo borra las cookies de este navegador y revoca ESTE refresco: otro aparato con su
        // propia cookie de acceso sigue (no se renueva el sello, o se cerraria a todos).
        Assert.Equal(HttpStatusCode.OK, await MiCuentaConCookieCopiadaAsync(cookie));
    }

    [Fact]
    public async Task Un_sello_distinto_en_la_base_corta_la_cookie_y_una_cuenta_eliminada_tambien()
    {
        var correo = NuevoCorreo();
        await RegistrarAsync(correo);
        await VerificarAsync(correo);
        var cookie = await EntrarYCopiarCookieAsync(correo);
        Assert.Equal(HttpStatusCode.OK, await MiCuentaConCookieCopiadaAsync(cookie));

        using (var ctx = ContextoDePrueba.Crear())
        {
            var u = await ctx.Usuarios.SingleAsync(x => x.Correo == correo);
            u.RenovarSelloSeguridad();
            await ctx.SaveChangesAsync();
        }
        Assert.Equal(HttpStatusCode.Unauthorized, await MiCuentaConCookieCopiadaAsync(cookie));

        // Con el sello de vuelta pero la cuenta eliminada, tampoco pasa.
        var nueva = await EntrarYCopiarCookieAsync(correo);
        Assert.Equal(HttpStatusCode.OK, await MiCuentaConCookieCopiadaAsync(nueva));
        using (var ctx = ContextoDePrueba.Crear())
        {
            var u = await ctx.Usuarios.SingleAsync(x => x.Correo == correo);
            u.Estado = Zass.Dominio.Comun.EstadoCuenta.Eliminada;
            await ctx.SaveChangesAsync();
        }
        Assert.Equal(HttpStatusCode.Unauthorized, await MiCuentaConCookieCopiadaAsync(nueva));
    }
}
