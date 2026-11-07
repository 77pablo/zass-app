using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Zass.Api.Configuracion;
using Zass.Aplicacion.Identidad;

namespace Zass.Api.Endpoints;

public sealed record VerificarCorreoCmd(string Token);
public sealed record PedirRecuperacionCmd(string Correo);
public sealed record CambiarContrasenaCmd(string ContrasenaActual, string ContrasenaNueva);

/// <summary>
/// API-08 a API-23. Los identificadores del documento van en el nombre de cada ruta
/// para que la tabla de api-rest.md y este archivo se puedan comparar de un vistazo.
///
/// Todo endpoint declara su grupo de limite de tasa. Ninguno queda sin politica: el
/// valor por omision no es "sin limite", es un error que la prueba de arquitectura
/// convierte en fallo de compilacion.
/// </summary>
public static class EndpointsIdentidad
{
    public static IEndpointRouteBuilder MapearIdentidad(this IEndpointRouteBuilder app)
    {
        MapearAutenticacion(app.MapGroup("/api/auth"));
        MapearMiCuenta(app.MapGroup("/api/mi"));
        MapearPublico(app.MapGroup("/api"));
        return app;
    }

    // --------------------------------------------------- 2 - Identidad y sesion

    private static void MapearAutenticacion(RouteGroupBuilder grupo)
    {
        // API-08 - Crea cuenta con rol inicial y envia correo de verificacion.
        grupo.MapPost("/registro", async (
                RegistrarCuentaCmd cmd, ServicioRegistro servicio, HttpContext http) =>
            {
                var r = await servicio.RegistrarAsync(cmd, Ip(http));
                return r.Exito
                    ? Results.Created($"/api/mi/cuenta", r.Valor)
                    : Respuestas.Problema(r);
            })
            .ConValidacion<RegistrarCuentaCmd>()
            .RequireRateLimiting(Tasa.Autenticacion)
            .WithName("API-08");

        // API-09 - Inicia sesion. Registra el intento, acierte o falle.
        grupo.MapPost("/sesion", async (
                IniciarSesionCmd cmd, ServicioSesion servicio, HttpContext http) =>
            {
                var r = await servicio.IniciarAsync(cmd, Ip(http), Agente(http));
                if (!r.Exito) return Respuestas.Problema(r);

                await AbrirCookiesAsync(http, r.Valor!);
                return Results.Ok(new
                {
                    r.Valor!.UsuarioId,
                    r.Valor!.Roles,
                    r.Valor!.CorreoVerificado,
                });
            })
            .ConValidacion<IniciarSesionCmd>()
            .RequireRateLimiting(Tasa.Autenticacion)
            .WithName("API-09");

        // API-10 - Renueva el token de acceso con el de refresco.
        grupo.MapPost("/sesion/renovar", async (ServicioSesion servicio, HttpContext http) =>
            {
                var refresco = http.Request.Cookies[ConfiguracionDeSeguridad.CookieRefresco];
                if (string.IsNullOrEmpty(refresco))
                    return Results.Problem(
                        title: "No autorizado", detail: "No hay sesion que renovar.",
                        statusCode: StatusCodes.Status401Unauthorized);

                var r = await servicio.RenovarAsync(refresco, Ip(http), Agente(http));
                if (!r.Exito) return Respuestas.Problema(r);

                await AbrirCookiesAsync(http, r.Valor!);
                return Results.NoContent();
            })
            .RequireRateLimiting(Tasa.General)
            .WithName("API-10");

        // API-11 - Cierra la sesion actual.
        grupo.MapDelete("/sesion", async (ServicioSesion servicio, HttpContext http) =>
            {
                var refresco = http.Request.Cookies[ConfiguracionDeSeguridad.CookieRefresco];
                if (!string.IsNullOrEmpty(refresco)) await servicio.CerrarAsync(refresco);

                await CerrarCookiesAsync(http);
                return Results.NoContent();
            })
            .RequireAuthorization()
            .RequireRateLimiting(Tasa.General)
            .WithName("API-11");

        // API-12 - Revoca TODAS las sesiones (CS-03).
        grupo.MapDelete("/sesiones", async (
                ServicioSesion servicio, HttpContext http, ClaimsPrincipal quien) =>
            {
                var usuarioId = quien.UsuarioId();
                if (usuarioId is null) return Results.Unauthorized();

                await servicio.RevocarTodasAsync(usuarioId.Value);
                await CerrarCookiesAsync(http);
                return Results.NoContent();
            })
            .RequireAuthorization()
            .RequireRateLimiting(Tasa.General)
            .WithName("API-12");

        // API-13 - Consume el enlace de verificacion (CS-01).
        grupo.MapPost("/correo/verificar", async (
                VerificarCorreoCmd cmd, ServicioCuenta servicio) =>
                Respuestas.DeResultado(await servicio.VerificarCorreoAsync(cmd.Token)))
            .ConValidacion<VerificarCorreoCmd>()
            .RequireRateLimiting(Tasa.Autenticacion)
            .WithName("API-13");

        // API-14 - Envia enlace. Responde igual exista o no la cuenta.
        grupo.MapPost("/contrasena/recuperar", async (
                PedirRecuperacionCmd cmd, ServicioCuenta servicio, HttpContext http) =>
                Respuestas.DeResultado(
                    await servicio.PedirRecuperacionAsync(cmd.Correo, Ip(http))))
            .ConValidacion<PedirRecuperacionCmd>()
            .RequireRateLimiting(Tasa.Autenticacion)
            .WithName("API-14");

        // Entrega el token CSRF de la sesion. Es un GET a proposito: si exigiera el
        // token para obtener el token, no habria por donde empezar.
        grupo.MapGet("/csrf", (
                Microsoft.AspNetCore.Antiforgery.IAntiforgery antifalsificacion,
                HttpContext http) =>
            {
                var fichas = antifalsificacion.GetAndStoreTokens(http);
                return Results.Ok(new { token = fichas.RequestToken });
            })
            .RequireRateLimiting(Tasa.General)
            .WithName("CSRF");

        // API-15 - Cambia la contrasena y revoca sesiones (CS-02).
        grupo.MapPost("/contrasena/restablecer", async (
                RestablecerContrasenaCmd cmd, ServicioCuenta servicio, HttpContext http) =>
                Respuestas.DeResultado(await servicio.RestablecerContrasenaAsync(
                    cmd.Token, cmd.ContrasenaNueva, Ip(http))))
            .ConValidacion<RestablecerContrasenaCmd>()
            .RequireRateLimiting(Tasa.Autenticacion)
            .WithName("API-15");
    }

    // ------------------------------------------------ 3 - Mi cuenta y mi perfil

    private static void MapearMiCuenta(RouteGroupBuilder grupo)
    {
        grupo.RequireAuthorization();

        // Todo lo que no sea GET en este grupo exige X-CSRF-Token.
        grupo.AddEndpointFilter<FiltroCsrf>();

        // API-16 - Datos y roles de la sesion.
        grupo.MapGet("/cuenta", async (ServicioCuenta servicio, ClaimsPrincipal quien) =>
            {
                var usuarioId = quien.UsuarioId();
                return usuarioId is null
                    ? Results.Unauthorized()
                    : Respuestas.DeResultado(await servicio.MiCuentaAsync(usuarioId.Value));
            })
            .RequireRateLimiting(Tasa.General)
            .WithName("API-16");

        // API-17 - Elimina la cuenta y anonimiza (RN-S1-07). Grupo S.
        grupo.MapDelete("/cuenta", async (
                ServicioCuenta servicio, HttpContext http, ClaimsPrincipal quien) =>
            {
                var usuarioId = quien.UsuarioId();
                if (usuarioId is null) return Results.Unauthorized();

                var r = await servicio.EliminarAsync(usuarioId.Value, Ip(http));
                if (!r.Exito) return Respuestas.Problema(r);

                await CerrarCookiesAsync(http);
                return Results.NoContent();
            })
            .RequireRateLimiting(Tasa.Sensible)
            .WithName("API-17");

        // Cambio de contrasena desde la sesion (RN-S1-04).
        grupo.MapPost("/contrasena", async (
                CambiarContrasenaCmd cmd, ServicioCuenta servicio,
                HttpContext http, ClaimsPrincipal quien) =>
            {
                var usuarioId = quien.UsuarioId();
                if (usuarioId is null) return Results.Unauthorized();

                return Respuestas.DeResultado(await servicio.CambiarContrasenaAsync(
                    usuarioId.Value, cmd.ContrasenaActual, cmd.ContrasenaNueva,
                    http.Request.Cookies[ConfiguracionDeSeguridad.CookieRefresco]));
            })
            .ConValidacion<CambiarContrasenaCmd>()
            .RequireRateLimiting(Tasa.General)
            .WithName("CS-02b");

        // API-18 - Perfil completo, con zona y sin ubicacion exacta.
        grupo.MapGet("/perfil-prestador", async (
                ServicioPerfiles servicio, ClaimsPrincipal quien) =>
            {
                var usuarioId = quien.UsuarioId();
                if (usuarioId is null) return Results.Unauthorized();

                var r = await servicio.MiPerfilPrestadorAsync(usuarioId.Value);
                if (!r.Exito) return Respuestas.Problema(r);

                // La version viaja en ETag: es lo que el cliente devolvera en If-Match.
                return Results.Ok(r.Valor);
            })
            .RequireRateLimiting(Tasa.General)
            .WithName("API-18");

        // API-19 - Edita datos y direccion. Exige If-Match con la version.
        grupo.MapPut("/perfil-prestador", async (
                EditarPerfilPrestadorCmd cmd, ServicioPerfiles servicio, ClaimsPrincipal quien) =>
            {
                var usuarioId = quien.UsuarioId();
                return usuarioId is null
                    ? Results.Unauthorized()
                    : Respuestas.DeResultado(
                        await servicio.EditarPrestadorAsync(usuarioId.Value, cmd));
            })
            .ConValidacion<EditarPerfilPrestadorCmd>()
            .RequireRateLimiting(Tasa.General)
            .WithName("API-19");

        // API-22 - Publica o despublica. Valida el minimo publicable.
        grupo.MapPost("/perfil-prestador/publicar", async (
                ServicioPerfiles servicio, ClaimsPrincipal quien) =>
            {
                var usuarioId = quien.UsuarioId();
                return usuarioId is null
                    ? Results.Unauthorized()
                    : Respuestas.DeResultado(await servicio.PublicarAsync(usuarioId.Value));
            })
            .RequireRateLimiting(Tasa.General)
            .WithName("API-22");

        grupo.MapPost("/perfil-prestador/despublicar", async (
                ServicioPerfiles servicio, ClaimsPrincipal quien) =>
            {
                var usuarioId = quien.UsuarioId();
                return usuarioId is null
                    ? Results.Unauthorized()
                    : Respuestas.DeResultado(await servicio.DespublicarAsync(usuarioId.Value));
            })
            .RequireRateLimiting(Tasa.General)
            .WithName("API-22b");

        // API-23 - Preferencias de zona y radio (CU-04).
        grupo.MapPut("/perfil-buscador", async (
                EditarPerfilBuscadorCmd cmd, ServicioPerfiles servicio, ClaimsPrincipal quien) =>
            {
                var usuarioId = quien.UsuarioId();
                return usuarioId is null
                    ? Results.Unauthorized()
                    : Respuestas.DeResultado(
                        await servicio.EditarBuscadorAsync(usuarioId.Value, cmd));
            })
            .ConValidacion<EditarPerfilBuscadorCmd>()
            .RequireRateLimiting(Tasa.General)
            .WithName("API-23");
    }

    // ------------------------------------------------- 1 - Publico, sin sesion

    private static void MapearPublico(RouteGroupBuilder grupo)
    {
        // API-05 - Ficha publica. Ninguna respuesta de este grupo lleva direccion,
        // latitud, longitud, correo privado ni telefono no publico.
        grupo.MapGet("/prestadores/{slug}", async (
                string slug, IServicioIdentidad identidad) =>
            {
                var ficha = await identidad.PerfilPublicoPorSlugAsync(slug);
                return ficha is null ? Results.NotFound() : Results.Ok(ficha);
            })
            .RequireRateLimiting(Tasa.Publico)
            .WithName("API-05");
    }

    // ------------------------------------------------------------ auxiliares

    /// <summary>
    /// Dos cookies con trabajos distintos: la de acceso lleva la identidad y dura 15
    /// min; la de refresco no identifica a nadie, solo permite pedir otra de acceso, y
    /// dura 30 dias. Las dos httpOnly: ningun guion de la pagina puede leerlas.
    /// </summary>
    private static async Task AbrirCookiesAsync(HttpContext http, SesionAbiertaDto sesion)
    {
        var identidad = new ClaimsIdentity(
            CookieAuthenticationDefaults.AuthenticationScheme);
        identidad.AddClaim(new Claim(ClaimTypes.NameIdentifier, sesion.UsuarioId.ToString()));
        foreach (var rol in sesion.Roles) identidad.AddClaim(new Claim(ClaimTypes.Role, rol));

        await http.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(identidad),
            new AuthenticationProperties
            {
                IsPersistent = true,
                ExpiresUtc = DateTimeOffset.UtcNow.Add(ConfiguracionDeSeguridad.VidaDelAcceso),
            });

        http.Response.Cookies.Append(
            ConfiguracionDeSeguridad.CookieRefresco, sesion.TokenRefresco, new CookieOptions
            {
                HttpOnly = true,
                Secure = http.Request.IsHttps,
                SameSite = SameSiteMode.Lax,
                Expires = sesion.ExpiraEn,
                Path = "/api/auth",   // solo viaja a donde se usa
            });
    }

    private static async Task CerrarCookiesAsync(HttpContext http)
    {
        await http.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        http.Response.Cookies.Delete(
            ConfiguracionDeSeguridad.CookieRefresco, new CookieOptions { Path = "/api/auth" });
    }

    private static string? Ip(HttpContext http) =>
        http.Connection.RemoteIpAddress?.ToString();

    private static string? Agente(HttpContext http)
    {
        var agente = http.Request.Headers.UserAgent.ToString();
        return string.IsNullOrWhiteSpace(agente)
            ? null
            : agente[..Math.Min(agente.Length, 400)];
    }
}
