using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Zass.Api.Configuracion;
using Zass.Aplicacion.Agenda;
using Zass.Aplicacion.Busqueda;
using Zass.Aplicacion.Catalogo;
using Zass.Aplicacion.Geografia;
using Zass.Aplicacion.Identidad;
using Zass.Aplicacion.Puertos;
using Zass.Aplicacion.Suscripciones;
using Zass.Dominio.Comun;
using Zass.Infraestructura.Configuracion;

namespace Zass.Api.Endpoints;

public sealed record VerificarCorreoCmd(string Token);
public sealed record PedirRecuperacionCmd(string Correo);
public sealed record ReenviarVerificacionCmd(string Correo);
public sealed record CambiarContrasenaCmd(string ContrasenaActual, string ContrasenaNueva);

/// <summary>
/// API-17. La contraseña, para confirmar que es el dueño quien pide la baja (CS-04), y el código de
/// la verificación en dos pasos (TOTP o de respaldo) si la cuenta la tiene activa.
/// </summary>
public sealed record EliminarCuentaCmd(string Contrasena, string? Codigo = null);

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
            // N3: captcha DESPUÉS de validar el cuerpo, para no gastar el token en un formulario mal llenado.
            .ConCaptcha(Turnstile.AccionRegistro)
            .RequireRateLimiting(Tasa.Autenticacion)
            .WithName("API-08");

        // API-09 - Inicia sesion. Registra el intento, acierte o falle.
        grupo.MapPost("/sesion", async (
                IniciarSesionCmd cmd, ServicioSesion servicio, HttpContext http) =>
            {
                var r = await servicio.EntrarAsync(cmd, Ip(http), Agente(http));
                if (!r.Exito) return Respuestas.Problema(r);

                // Cuenta con MFA: la contrasena era correcta, pero NO hay cookies todavia. Solo el
                // desafio, que API-79 canjea junto al codigo.
                if (r.Valor!.Desafio is { } desafio)
                    return Results.Ok(new { mfaRequerido = true, desafio = desafio.Desafio, expiraEn = desafio.ExpiraEn });

                return await SesionAbiertaAsync(http, r.Valor.Sesion!);
            })
            .ConValidacion<IniciarSesionCmd>()
            .RequireRateLimiting(Tasa.Autenticacion)
            .WithName("API-09");

        // API-79 - Segundo paso del inicio de sesion con MFA: el desafio de API-09 mas un codigo
        // TOTP o de respaldo. Sin CSRF, como API-09: todavia no hay sesion que proteger, y el
        // desafio (5 min, un solo uso) es la prueba de que la contrasena ya se dio.
        grupo.MapPost("/sesion/mfa", async (
                SegundoPasoCmd cmd, ServicioMfa servicio, HttpContext http) =>
            {
                var r = await servicio.SegundoPasoAsync(cmd, Ip(http), Agente(http));
                return r.Exito ? await SesionAbiertaAsync(http, r.Valor!) : Respuestas.Problema(r);
            })
            .ConValidacion<SegundoPasoCmd>()
            .RequireRateLimiting(Tasa.Autenticacion)
            .WithName("API-79");

        // API-10 - Renueva el token de acceso con el de refresco.
        grupo.MapPost("/sesion/renovar", async (ServicioSesion servicio, HttpContext http) =>
            {
                var refresco = http.Request.Cookies[ConfiguracionDeSeguridad.CookieRefresco];
                if (string.IsNullOrEmpty(refresco))
                    return Results.Problem(
                        title: "No autorizado", detail: "No hay sesión que renovar.",
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

                // Cierra TODAS las sesiones: tambien corta las cookies de acceso de los demas aparatos.
                await servicio.RevocarTodasAsync(usuarioId.Value, cortarAcceso: true);
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

        // Reenvia el enlace de verificacion. Responde igual exista o no la cuenta, y
        // tambien si ya estaba verificada: distinguir seria decir quien tiene cuenta aqui.
        // Grupo de autenticacion porque manda un correo, igual que recuperar la contrasena.
        grupo.MapPost("/correo/reenviar", async (
                ReenviarVerificacionCmd cmd, ServicioCuenta servicio, HttpContext http) =>
                Respuestas.DeResultado(
                    await servicio.ReenviarVerificacionAsync(cmd.Correo, Ip(http))))
            .ConValidacion<ReenviarVerificacionCmd>()
            .RequireRateLimiting(Tasa.Autenticacion)
            .WithName("API-71");

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
        // Pide la contraseña, cancela las citas futuras (avisando a la otra parte) y después
        // anonimiza, terminando la suscripción en la misma transacción: ver BajaDeCuenta. El cuerpo va con
        // [FromBody] porque en un DELETE la API mínima no lo infiere sola.
        grupo.MapDelete("/cuenta", async (
                [FromBody] EliminarCuentaCmd cmd,
                ServicioCuenta cuenta, ServicioPerfiles perfiles, IServicioAgenda agenda,
                IRelojSistema reloj,
                HttpContext http, ClaimsPrincipal quien) =>
            {
                var usuarioId = quien.UsuarioId();
                if (usuarioId is null) return Results.Unauthorized();

                var baja = new BajaDeCuenta(cuenta, perfiles, agenda, reloj);
                var r = await baja.EjecutarAsync(usuarioId.Value, cmd.Contrasena, cmd.Codigo, Ip(http));
                if (!r.Exito) return Respuestas.Problema(r);

                await CerrarCookiesAsync(http);
                return Results.NoContent();
            })
            .ConValidacion<EliminarCuentaCmd>()
            .RequireRateLimiting(Tasa.Sensible)
            .WithName("API-17");

        // ES-2 - Completa los datos del registro completo (RUT, telefono, mayoria de edad) en una
        // cuenta creada antes del 20-nov-2026. Mismas validaciones que API-08. Es uno de los pocos
        // endpoints que el filtro de datos pendientes deja pasar (FiltroDatosDeCuenta).
        // Limite de autenticacion y no el general: responde si un RUT ya esta usado, igual que
        // el registro, y no puede servir para recorrer RUTs.
        grupo.MapPut("/cuenta/datos", async (
                CompletarDatosCuentaCmd cmd, ServicioRegistro registro, HttpContext http, ClaimsPrincipal quien) =>
            {
                var usuarioId = quien.UsuarioId();
                if (usuarioId is null) return Results.Unauthorized();

                return Respuestas.DeResultado(
                    await registro.CompletarDatosAsync(usuarioId.Value, cmd, Ip(http)));
            })
            .ConValidacion<CompletarDatosCuentaCmd>()
            .RequireRateLimiting(Tasa.Autenticacion)
            .WithName(FiltroDatosDeCuenta.EndpointCompletar);

        // «Ofrecer mis servicios»: agrega el rol de prestador a la cuenta que ya existe. Los roles viajan en la
        // cookie de acceso, asi que se renueva la sesion para que la persona los vea de inmediato.
        grupo.MapPost("/activar-prestador", async (
                ServicioRegistro registro, ServicioSesion sesiones, HttpContext http, ClaimsPrincipal quien) =>
            {
                var usuarioId = quien.UsuarioId();
                if (usuarioId is null) return Results.Unauthorized();

                var r = await registro.ActivarPrestadorAsync(usuarioId.Value, Ip(http));
                if (!r.Exito) return Respuestas.Problema(r);

                var refresco = http.Request.Cookies[ConfiguracionDeSeguridad.CookieRefresco];
                if (!string.IsNullOrEmpty(refresco))
                {
                    var nueva = await sesiones.RenovarAsync(refresco, Ip(http), Agente(http));
                    if (nueva.Exito) await AbrirCookiesAsync(http, nueva.Valor!);
                }
                return Results.NoContent();
            })
            .RequireRateLimiting(Tasa.General)
            .WithName("CS-04");

        // Cambio de contrasena desde la sesion (RN-S1-04).
        grupo.MapPost("/contrasena", async (
                CambiarContrasenaCmd cmd, ServicioCuenta servicio,
                HttpContext http, ClaimsPrincipal quien) =>
            {
                var usuarioId = quien.UsuarioId();
                if (usuarioId is null) return Results.Unauthorized();

                return Respuestas.DeResultado(await servicio.CambiarContrasenaAsync(
                    usuarioId.Value, cmd.ContrasenaActual, cmd.ContrasenaNueva,
                    http.Request.Cookies[ConfiguracionDeSeguridad.CookieRefresco], Ip(http)));
            })
            .ConValidacion<CambiarContrasenaCmd>()
            .RequireRateLimiting(Tasa.General)
            .WithName("CS-02b");

        // API-80 - Estado de la verificacion en dos pasos de la propia cuenta.
        grupo.MapGet("/mfa", async (ServicioMfa servicio, ClaimsPrincipal quien) =>
            {
                var usuarioId = quien.UsuarioId();
                return usuarioId is null
                    ? Results.Unauthorized()
                    : Respuestas.DeResultado(await servicio.EstadoAsync(usuarioId.Value));
            })
            .RequireRateLimiting(Tasa.General)
            .WithName("API-80");

        // API-81 - Empieza el alta: secreto nuevo y la direccion otpauth:// para el QR. Queda
        // pendiente hasta API-82. Esto (con API-82) es lo que un rol del panel sin MFA SI puede
        // hacer para entrar al panel: vive en /api/mi, no en /api/interno.
        grupo.MapPost("/mfa/alta", async (ServicioMfa servicio, ClaimsPrincipal quien) =>
            {
                var usuarioId = quien.UsuarioId();
                return usuarioId is null
                    ? Results.Unauthorized()
                    : Respuestas.DeResultado(await servicio.IniciarAltaAsync(usuarioId.Value));
            })
            .RequireRateLimiting(Tasa.Autenticacion)
            .WithName("API-81");

        // API-82 - Confirma el alta con la contrasena y un codigo, y devuelve los 10 codigos de respaldo (una vez).
        grupo.MapPost("/mfa/confirmar", async (
                ConfirmarMfaCmd cmd, ServicioMfa servicio, HttpContext http, ClaimsPrincipal quien) =>
            {
                var usuarioId = quien.UsuarioId();
                return usuarioId is null
                    ? Results.Unauthorized()
                    : Respuestas.DeResultado(await servicio.ConfirmarAltaAsync(
                        usuarioId.Value, cmd, Ip(http), http.Request.Cookies[ConfiguracionDeSeguridad.CookieRefresco]));
            })
            .ConValidacion<ConfirmarMfaCmd>()
            .RequireRateLimiting(Tasa.Autenticacion)
            .WithName("API-82");

        // API-83 - Desactiva la verificacion en dos pasos. Pide contrasena Y codigo.
        grupo.MapPost("/mfa/desactivar", async (
                DesactivarMfaCmd cmd, ServicioMfa servicio, HttpContext http, ClaimsPrincipal quien) =>
            {
                var usuarioId = quien.UsuarioId();
                return usuarioId is null
                    ? Results.Unauthorized()
                    : Respuestas.DeResultado(await servicio.DesactivarAsync(
                        usuarioId.Value, cmd, Ip(http), http.Request.Cookies[ConfiguracionDeSeguridad.CookieRefresco]));
            })
            .ConValidacion<DesactivarMfaCmd>()
            .RequireRateLimiting(Tasa.Autenticacion)
            .WithName("API-83");

        // API-84 - Diez codigos de respaldo nuevos (los viejos dejan de servir). Pide contrasena Y codigo.
        grupo.MapPost("/mfa/codigos", async (
                RegenerarCodigosMfaCmd cmd, ServicioMfa servicio, HttpContext http, ClaimsPrincipal quien) =>
            {
                var usuarioId = quien.UsuarioId();
                return usuarioId is null
                    ? Results.Unauthorized()
                    : Respuestas.DeResultado(await servicio.RegenerarCodigosAsync(
                        usuarioId.Value, cmd, Ip(http), http.Request.Cookies[ConfiguracionDeSeguridad.CookieRefresco]));
            })
            .ConValidacion<RegenerarCodigosMfaCmd>()
            .RequireRateLimiting(Tasa.Autenticacion)
            .WithName("API-84");

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

        // API-22 - Publica el perfil y arranca la suscripcion de prueba.
        //
        // Los dos modulos se componen AQUI, no uno dentro de otro: MOD-13 ya depende de
        // MOD-01, asi que si MOD-01 llamara a MOD-13 habria un ciclo. Componer dos
        // modulos es justamente el trabajo de un endpoint.
        //
        // El orden importa. Primero publicar: si falla por falta de datos, no tiene
        // sentido haber empezado a contar los 30 dias de prueba.
        grupo.MapPost("/perfil-prestador/publicar", async (
                ServicioPerfiles servicio, IServicioSuscripciones suscripciones,
                ClaimsPrincipal quien) =>
            {
                var usuarioId = quien.UsuarioId();
                if (usuarioId is null) return Results.Unauthorized();

                var publicacion = await servicio.PublicarAsync(usuarioId.Value);
                if (!publicacion.Exito) return Respuestas.Problema(publicacion);

                // Sin suscripcion vigente el perfil no aparece en la busqueda
                // (RN-S3-09): publicar sin arrancar la prueba dejaria un perfil
                // "publicado" que no ve nadie, que es la peor forma de fallar.
                // IniciarPrueba es idempotente, asi que republicar no reinicia nada.
                var prueba = await suscripciones.IniciarPruebaAsync(publicacion.Valor);
                if (!prueba.Exito) return Respuestas.Problema(prueba);

                return Results.NoContent();
            })
            .RequireRateLimiting(Tasa.General)
            .WithName("API-22");

        // Lo que el prestador ve de su suscripcion: plan, estado y dias restantes.
        //
        // La traduccion de cuenta a perfil la hace MOD-01, que es el dueno de esa
        // tabla; MOD-13 solo entiende de perfiles. Son dos consultas en vez de una, y
        // es el precio de que cada modulo mande sobre sus propios datos.
        grupo.MapGet("/suscripcion", async (
                IServicioIdentidad identidad, IServicioSuscripciones suscripciones,
                ClaimsPrincipal quien) =>
            {
                var usuarioId = quien.UsuarioId();
                if (usuarioId is null) return Results.Unauthorized();

                var perfilId = await identidad.PerfilPrestadorIdAsync(usuarioId.Value);
                if (perfilId is null)
                    return Results.Problem(
                        title: "No encontrado",
                        detail: "Esta cuenta no tiene perfil de prestador.",
                        statusCode: StatusCodes.Status404NotFound);

                return Respuestas.DeResultado(
                    await suscripciones.DePrestadorAsync(perfilId.Value));
            })
            .RequireRateLimiting(Tasa.General)
            .WithName("MI-SUSCRIPCION");

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

        // API-23 - Preferencias de zona, radio y oficios de interes (CU-04).
        //
        // Que la zona y los oficios EXISTAN se mira aqui, en el borde: son de MOD-04 y MOD-03, y
        // MOD-01 no los conoce. Sin esto, una zona inventada llegaba a la base y rebotaba en la
        // clave foranea como un 500; y un oficio inventado se guardaba sin que nadie lo notara.
        grupo.MapPut("/perfil-buscador", async (
                EditarPerfilBuscadorCmd cmd, ServicioPerfiles servicio,
                IServicioCatalogo catalogo, IServicioGeografia geografia, ClaimsPrincipal quien) =>
            {
                var usuarioId = quien.UsuarioId();
                if (usuarioId is null) return Results.Unauthorized();

                if (cmd.ZonaInteresId is { } zonaId && !(await geografia.ZonaPorIdAsync(zonaId)).Exito)
                    return Respuestas.Problema(Resultado.Falla(Fallas.Validacion, "Esa comuna no existe."));

                foreach (var rubroId in (cmd.RubrosInteres ?? Array.Empty<short>()).Distinct())
                    if (await catalogo.RubroPorIdAsync(rubroId) is null)
                        return Respuestas.Problema(Resultado.Falla(Fallas.Validacion, "Uno de esos oficios no existe."));

                return Respuestas.DeResultado(await servicio.EditarBuscadorAsync(usuarioId.Value, cmd));
            })
            .ConValidacion<EditarPerfilBuscadorCmd>()
            .RequireRateLimiting(Tasa.General)
            .WithName("API-23");

        // API-23b - Las preferencias guardadas y los identificadores de los favoritos (CU-04,
        // CS-08). El mismo recurso que API-23, con el metodo que faltaba: sin esto la pantalla no
        // tenia como saber que habia guardado la persona.
        grupo.MapGet("/perfil-buscador", async (ServicioPerfiles servicio, ClaimsPrincipal quien) =>
            {
                var usuarioId = quien.UsuarioId();
                return usuarioId is null
                    ? Results.Unauthorized()
                    : Results.Ok(await servicio.MiPerfilBuscadorAsync(usuarioId.Value));
            })
            .RequireRateLimiting(Tasa.General)
            .WithName("API-23b");

        // CS-08 - Mis favoritos, como tarjetas. Los identificadores son de MOD-01 (el perfil de
        // buscador) y las tarjetas de MOD-05: ninguno de los dos puede armar la lista solo, igual
        // que la ficha. La tarjeta NO trae contacto (ver FavoritoDto).
        grupo.MapGet("/favoritos", async (
                ServicioPerfiles servicio, IServicioBusqueda busqueda, ClaimsPrincipal quien) =>
            {
                var usuarioId = quien.UsuarioId();
                if (usuarioId is null) return Results.Unauthorized();

                var mio = await servicio.MiPerfilBuscadorAsync(usuarioId.Value);
                return Results.Ok(await busqueda.TarjetasDeFavoritosAsync(mio.Favoritos));
            })
            .RequireRateLimiting(Tasa.General)
            .WithName("API-23-favoritos");

        // CS-08 - Guardar un favorito. PUT porque es idempotente: guardarlo dos veces deja lo mismo.
        grupo.MapPut("/favoritos/{perfilId:guid}", async (
                Guid perfilId, ServicioPerfiles servicio, ClaimsPrincipal quien) =>
            {
                var usuarioId = quien.UsuarioId();
                return usuarioId is null
                    ? Results.Unauthorized()
                    : Respuestas.DeResultado(await servicio.GuardarFavoritoAsync(usuarioId.Value, perfilId));
            })
            .RequireRateLimiting(Tasa.General)
            .WithName("API-23-favorito-guardar");

        // CS-08 - Quitar un favorito. Tambien idempotente: quitar uno que no estaba responde 204.
        grupo.MapDelete("/favoritos/{perfilId:guid}", async (
                Guid perfilId, ServicioPerfiles servicio, ClaimsPrincipal quien) =>
            {
                var usuarioId = quien.UsuarioId();
                return usuarioId is null
                    ? Results.Unauthorized()
                    : Respuestas.DeResultado(await servicio.QuitarFavoritoAsync(usuarioId.Value, perfilId));
            })
            .RequireRateLimiting(Tasa.General)
            .WithName("API-23-favorito-quitar");
    }

    // ------------------------------------------------- 1 - Publico, sin sesion

    private static void MapearPublico(RouteGroupBuilder grupo)
    {
        // API-05 - Ficha publica. Ninguna respuesta de este grupo lleva direccion,
        // latitud, longitud, correo privado ni telefono no publico.
        //
        // La arma MOD-05 (CU-09), que consume MOD-01, MOD-03 y MOD-04: el perfil es de MOD-01,
        // pero los servicios son de MOD-03 y las zonas de MOD-04, y ninguno puede armarla solo.
        //
        // Buscar y abrir una ficha NO exige cuenta; contactar si. Por eso el telefono, el WhatsApp, el correo y el
        // sitio solo salen con sesion: se quitan AQUI, en el servidor. Esconder los botones en la pantalla no
        // bastaria, porque el dato seguiria en la respuesta para quien abra las herramientas del navegador, y
        // seria la lista de contactos de todos los prestadores a un script de distancia.
        grupo.MapGet("/prestadores/{slug}", async (
                string slug, IServicioBusqueda busqueda, IServicioIdentidad identidad,
                HttpContext http) =>
            {
                var ficha = await busqueda.FichaPublicaAsync(slug);
                if (ficha is null) return Results.NotFound();

                var sinContacto = new ContactoPublicoDto(null, null, null, null);

                if (http.User.Identity?.IsAuthenticated != true)
                {
                    ficha = ficha with { Contacto = sinContacto, ContactoRequiereCuenta = true };
                }
                else
                {
                    // Tener sesion no basta: el correo tiene que estar verificado.
                    //
                    // Registrarse es gratis y toma treinta segundos, asi que un muro que solo
                    // pide «cuenta» cuesta un correo inventado: la lista de telefonos y
                    // WhatsApp de todos los prestadores queda a un script de distancia, que es
                    // exactamente lo que este muro existe para evitar. Verificar el correo es
                    // el mismo listo que ya se exige para dejar una resena (CU-16, precondicion
                    // P1) y para publicar perfil o agendar (RN-S1-02), asi que no es una regla
                    // nueva: es la misma, aplicada donde faltaba. Queda como RN-S1-09.
                    //
                    // La primera version de este comentario citaba RN-S5-04, que es otra cosa
                    // («el comentario se escapa al renderizar»): lo cazo la revision del
                    // documento. Una cita equivocada es peor que ninguna, porque el proximo que
                    // lea la regla citada no va a encontrar lo que el comentario promete.
                    //
                    // La consulta extra solo ocurre con sesion; a quien pasa de largo sin
                    // cuenta —la mayoria de las visitas— no le cuesta nada.
                    var usuarioId = http.User.UsuarioId();
                    var usuario = usuarioId is null
                        ? null
                        : await identidad.ObtenerUsuarioAsync(usuarioId.Value);

                    if (usuario is null || !usuario.CorreoVerificado)
                        ficha = ficha with
                        {
                            Contacto = sinContacto,
                            ContactoRequiereCorreoVerificado = true,
                        };
                }

                // La respuesta depende de la sesion: que ningun cache compartido guarde la version con contacto.
                http.Response.Headers.CacheControl = "private, no-store";
                http.Response.Headers.Vary = "Cookie";
                return Results.Ok(ficha);
            })
            .RequireRateLimiting(Tasa.Publico)
            .WithName("API-05");

        // API-07 - Descarga .vcf con los canales marcados publicos (CS-19).
        //
        // Usa la MISMA fuente que la ficha publica (API-05): IServicioBusqueda.FichaPublicaAsync.
        // Si la ficha no existe o no esta publicada -> 404 igual que la ficha.
        // Si la ficha existe pero NO expone contacto (sin suscripcion, RN-S6-10) -> 404 con el
        // mismo cuerpo que un inexistente (no filtrar que existe).
        //
        // vCard 3.0, lineas con CRLF, escapar coma, punto y coma, barra invertida y saltos
        // de linea en los valores (RFC 2426), UTF-8.
        // Content-Type "text/vcard; charset=utf-8" y Content-Disposition attachment
        // con filename "<slug>.vcf" (sanea el slug a [a-z0-9-]).
        //
        // La vCard ES el contacto: lleva el telefono, el WhatsApp y el correo. Por eso exige lo
        // mismo que API-05 para mostrarlos —sesion y correo verificado (RN-S1-09)—. La primera
        // version era anonima, y junto con API-64 (la lista de todos los slugs publicados)
        // dejaba la libreta de contactos de todos los prestadores a un script de distancia sin
        // crear ninguna cuenta: exactamente lo que el muro de API-05 existe para evitar.
        // Se comprueba ANTES de buscar la ficha, asi que el 401 y el 403 no dicen si existe.
        grupo.MapGet("/prestadores/{slug}/vcard", async (
                string slug,
                IServicioBusqueda busqueda,
                IServicioIdentidad identidad,
                IOptions<OpcionesZass> opciones,
                HttpContext http) =>
            {
                if (http.User.Identity?.IsAuthenticated != true) return Results.Unauthorized();

                var usuarioId = http.User.UsuarioId();
                var usuario = usuarioId is null ? null : await identidad.ObtenerUsuarioAsync(usuarioId.Value);
                if (usuario is null || !usuario.CorreoVerificado)
                    return Results.Problem(
                        statusCode: StatusCodes.Status403Forbidden,
                        title: "Verifica tu correo para guardar el contacto.");

                // La respuesta lleva datos de contacto que dependen de la sesion: ningun cache
                // compartido puede guardarla.
                http.Response.Headers.CacheControl = "private, no-store";
                http.Response.Headers.Vary = "Cookie";

                var ficha = await busqueda.FichaPublicaAsync(slug);
                if (ficha is null) return Results.NotFound();

                // RN-S6-10: sin suscripcion vigente, la ficha se ve pero sin contacto ni agendar.
                // Para la vCard, que ES contacto, devolvemos 404 indistinguible de un inexistente.
                if (!ficha.SuscripcionVigente) return Results.NotFound();

                var contacto = ficha.Contacto;
                var urlPublica = opciones.Value.UrlPublica?.TrimEnd('/');
                var urlFicha = urlPublica is null ? null : $"{urlPublica}/prestadores/{slug}";

                var vcard = ConstruirVCard(ficha.NombrePublico, contacto, urlFicha, slug);
                var filename = SanitizarSlug(slug) + ".vcf";

                return Results.File(
                    System.Text.Encoding.UTF8.GetBytes(vcard),
                    "text/vcard; charset=utf-8",
                    filename);
            })
            .RequireRateLimiting(Tasa.Publico)
            .WithName("API-07")
            .AllowAnonymous();
    }

    // ------------------------------------------------------------ auxiliares vCard

    /// <summary>
    /// Construye una vCard 3.0 (RFC 2426) con CRLF y escapado correcto.
    /// </summary>
    private static string ConstruirVCard(
        string nombre,
        ContactoPublicoDto contacto,
        string? urlFicha,
        string slug)
    {
        var sb = new System.Text.StringBuilder();
        sb.Append("BEGIN:VCARD\r\n");
        sb.Append("VERSION:3.0\r\n");
        sb.Append($"FN:{Escape(nombre)}\r\n");
        // N: Apellidos;Nombre;Prefijo;Sufijo - usamos el nombre completo como FN y N
        sb.Append($"N:{Escape(nombre)};;;\r\n");

        if (!string.IsNullOrWhiteSpace(contacto.Telefono))
            sb.Append($"TEL;TYPE=VOICE:{Escape(contacto.Telefono)}\r\n");

        if (!string.IsNullOrWhiteSpace(contacto.Whatsapp))
            sb.Append($"TEL;TYPE=CELL:{Escape(contacto.Whatsapp)}\r\n");

        if (!string.IsNullOrWhiteSpace(contacto.Correo))
            sb.Append($"EMAIL:{Escape(contacto.Correo)}\r\n");

        if (!string.IsNullOrWhiteSpace(contacto.SitioWeb))
            sb.Append($"URL:{Escape(contacto.SitioWeb)}\r\n");

        if (!string.IsNullOrWhiteSpace(urlFicha))
            sb.Append($"URL:{Escape(urlFicha)}\r\n");

        sb.Append("END:VCARD\r\n");
        return sb.ToString();
    }

    /// <summary>
    /// Escapa caracteres especiales segun RFC 2426: \ , ; \n \r
    /// </summary>
    private static string Escape(string valor)
    {
        if (string.IsNullOrEmpty(valor)) return valor;
        return valor
            .Replace("\\", "\\\\")
            .Replace(",", "\\,")
            .Replace(";", "\\;")
            .Replace("\n", "\\n")
            .Replace("\r", "\\r");
    }

    /// <summary>
    /// Sanea el slug para usarlo como nombre de archivo: solo a-z 0-9 y guion.
    /// </summary>
    private static string SanitizarSlug(string slug)
    {
        if (string.IsNullOrWhiteSpace(slug)) return "vcard";
        var sb = new System.Text.StringBuilder();
        foreach (var c in slug.ToLowerInvariant())
        {
            if ((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '-')
                sb.Append(c);
        }
        var resultado = sb.ToString();
        return string.IsNullOrWhiteSpace(resultado) ? "vcard" : resultado;
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
        // El sello de seguridad: en cada peticion se compara con el de la base (ver Seguridad.cs).
        identidad.AddClaim(new Claim(ConfiguracionDeSeguridad.ClaimSello, sesion.SelloSeguridad));

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

    /// <summary>Pone las cookies y responde lo mismo en API-09 (sin MFA) y en API-79.</summary>
    private static async Task<IResult> SesionAbiertaAsync(HttpContext http, SesionAbiertaDto sesion)
    {
        await AbrirCookiesAsync(http, sesion);
        return Results.Ok(new
        {
            sesion.UsuarioId,
            sesion.Roles,
            sesion.CorreoVerificado,
        });
    }

    private static async Task CerrarCookiesAsync(HttpContext http)
    {
        await http.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        http.Response.Cookies.Delete(
            ConfiguracionDeSeguridad.CookieRefresco, new CookieOptions { Path = "/api/auth" });
    }

    // La IP del visitante para auditoria y sesiones. Detras de un proxy de confianza ya viene
    // corregida por UseForwardedHeaders (ver ProxiesDeConfianza); por eso NO se lee
    // X-Forwarded-For aqui: una cabecera cruda la puede escribir cualquiera.
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
