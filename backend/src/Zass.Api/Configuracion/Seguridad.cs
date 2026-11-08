using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.RateLimiting;

namespace Zass.Api.Configuracion;

/// <summary>
/// Los cuatro grupos de limite de tasa de seguridad.md, seccion 1.
///
/// Los nombres son los del documento para que el endpoint se lea igual que la tabla:
/// si api-rest.md dice que API-09 es del grupo A, el codigo dice Tasa.Autenticacion.
/// </summary>
public static class Tasa
{
    public const string Publico = "P";
    public const string Autenticacion = "A";
    public const string General = "G";
    public const string Sensible = "S";

    public static readonly string[] Todos = { Publico, Autenticacion, General, Sensible };
}

/// <summary>Politicas de autorizacion por rol interno (TBL-02).</summary>
public static class Politicas
{
    public const string Verificador = "verificador";
    public const string RolVerificador = "verificador";
}

public static class ConfiguracionDeSeguridad
{
    public const string CookieAcceso = "zass_sesion";
    public const string CookieRefresco = "zass_refresco";
    public const string CabeceraCsrf = "X-CSRF-Token";

    /// <summary>DT-08. El token de acceso dura 15 min; el de refresco, 30 dias.</summary>
    public static readonly TimeSpan VidaDelAcceso = TimeSpan.FromMinutes(15);

    /// <summary>
    /// Los limites vienen de configuracion, con los valores del documento por omision.
    /// No es para poder relajarlos en produccion: es para que una prueba pueda fijar el
    /// limite que quiere comprobar sin que las demas pruebas se queden sin cupo, porque
    /// todas salen de la misma IP.
    /// </summary>
    public static IServiceCollection AgregarLimiteDeTasa(
        this IServiceCollection servicios, IConfiguration? configuracion = null)
    {
        var seccion = configuracion?.GetSection("Zass:Tasa");
        int Limite(string grupo, int porOmision) =>
            int.TryParse(seccion?[grupo], out var valor) && valor > 0 ? valor : porOmision;

        servicios.AddRateLimiter(opciones =>
        {
            opciones.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            // Nunca se revela cuantos intentos quedan: seria informacion util para
            // quien ataca. Solo cuanto esperar.
            opciones.OnRejected = async (contexto, ct) =>
            {
                contexto.HttpContext.Response.Headers.RetryAfter = "900";
                contexto.HttpContext.Response.ContentType = "application/problem+json";
                await contexto.HttpContext.Response.WriteAsJsonAsync(new
                {
                    titulo = "Demasiadas peticiones",
                    detalle = "Espera unos minutos antes de volver a intentar.",
                    reintentarEn = 900,
                }, ct);
            };

            Ventana(opciones, Tasa.Publico, Limite(Tasa.Publico, 100));
            Ventana(opciones, Tasa.Autenticacion, Limite(Tasa.Autenticacion, 5));
            Ventana(opciones, Tasa.General, Limite(Tasa.General, 100));
            Ventana(opciones, Tasa.Sensible, Limite(Tasa.Sensible, 10));
        });

        return servicios;
    }

    /// <summary>
    /// Particionado por IP, 15 minutos de ventana. En produccion los contadores van en
    /// N-06 Redis, porque la API corre en varias replicas y un contador por proceso
    /// multiplicaria el limite por el numero de replicas.
    /// </summary>
    private static void Ventana(RateLimiterOptions opciones, string nombre, int permitidas) =>
        opciones.AddPolicy(nombre, contexto => RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: contexto.Connection.RemoteIpAddress?.ToString() ?? "desconocida",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = permitidas,
                Window = TimeSpan.FromMinutes(15),
                QueueLimit = 0,
            }));

    public static IServiceCollection AgregarAutenticacion(
        this IServiceCollection servicios, bool exigirHttps)
    {
        servicios
            .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
            .AddCookie(opciones =>
            {
                opciones.Cookie.Name = CookieAcceso;
                opciones.Cookie.HttpOnly = true;
                opciones.Cookie.SameSite = SameSiteMode.Lax;
                opciones.Cookie.SecurePolicy = exigirHttps
                    ? CookieSecurePolicy.Always
                    : CookieSecurePolicy.SameAsRequest;
                opciones.ExpireTimeSpan = VidaDelAcceso;
                opciones.SlidingExpiration = false;

                // Una API no redirige a una pagina de inicio de sesion: responde 401 y
                // 403 con cuerpo, y deja que el cliente decida.
                opciones.Events.OnRedirectToLogin = contexto =>
                {
                    contexto.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    return Task.CompletedTask;
                };
                opciones.Events.OnRedirectToAccessDenied = contexto =>
                {
                    contexto.Response.StatusCode = StatusCodes.Status403Forbidden;
                    return Task.CompletedTask;
                };
            });

        // El rol viaja en la cookie (ClaimTypes.Role, ver EndpointsIdentidad). La politica del
        // Verificador es lo unico que protege /api/interno: un buscador o un prestador autenticado
        // recibe 403 antes de que el endpoint ejecute nada, ni siquiera para listar la cola.
        servicios.AddAuthorization(opciones =>
            opciones.AddPolicy(Politicas.Verificador, p => p.RequireRole(Politicas.RolVerificador)));
        servicios.AddAntiforgery(opciones =>
        {
            opciones.HeaderName = CabeceraCsrf;
            opciones.Cookie.Name = "zass_csrf";
            opciones.Cookie.HttpOnly = false;   // el cliente tiene que poder leerla
            opciones.Cookie.SameSite = SameSiteMode.Lax;
        });

        return servicios;
    }

    /// <summary>
    /// Las siete cabeceras de seguridad.md, seccion 4. En produccion las repite N-01
    /// Front Door; aqui van igual, para que un despliegue sin Front Door no quede
    /// desnudo.
    /// </summary>
    public static IApplicationBuilder UsarCabecerasDeSeguridad(this IApplicationBuilder app) =>
        app.Use(async (contexto, siguiente) =>
        {
            var cabeceras = contexto.Response.Headers;

            // Sin unsafe-inline ni unsafe-eval: descarta de raiz la clase de XSS mas
            // comun, al costo de que ningun estilo ni guion pueda vivir en el HTML.
            cabeceras["Content-Security-Policy"] =
                "default-src 'self'; "
                + "img-src 'self' data: https://*.blob.core.windows.net; "
                + "connect-src 'self' https://api.zass.cl; "
                + "frame-ancestors 'none'; base-uri 'self'; form-action 'self'";
            cabeceras["Strict-Transport-Security"] = "max-age=31536000; includeSubDomains; preload";
            cabeceras["X-Content-Type-Options"] = "nosniff";
            cabeceras["X-Frame-Options"] = "DENY";
            cabeceras["Referrer-Policy"] = "strict-origin-when-cross-origin";
            cabeceras["Permissions-Policy"] = "geolocation=(self), camera=(), microphone=()";
            cabeceras["Cross-Origin-Opener-Policy"] = "same-origin";

            await siguiente();
        });
}
