using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Zass.Api.Configuracion;
using Zass.Aplicacion.Notificaciones;
using Zass.Infraestructura.Push;

namespace Zass.Api.Endpoints;

/// <summary>ES-3 · MOD-07. Los avisos push (CU-15) y por que canal sale cada aviso (CS-14).</summary>
public static class EndpointsNotificaciones
{
    public const string EndpointRegistrar = "API-45";
    public const string EndpointBaja = "API-45-baja";
    public const string EndpointClave = "API-45-clave";
    public const string EndpointPreferencias = "API-43";
    public const string EndpointVerPreferencias = "API-43-ver";

    public static IEndpointRouteBuilder MapearNotificaciones(this IEndpointRouteBuilder app)
    {
        // La clave publica VAPID: el navegador la necesita para pedir el permiso. No es secreta.
        // Sin claves configuradas responde 404 y la web esconde el boton de activar.
        app.MapGet("/api/notificaciones/push/clave", (IOptions<OpcionesPush> opciones) =>
                opciones.Value.Activo
                    ? Results.Ok(new { clave = opciones.Value.ClavePublica })
                    : Results.NotFound())
            .AllowAnonymous()
            .RequireRateLimiting(Tasa.Publico)
            .WithName(EndpointClave);

        var mi = app.MapGroup("/api/mi/notificaciones");
        mi.RequireAuthorization();
        mi.AddEndpointFilter<FiltroCsrf>();

        // API-45. Registra ESTE navegador para recibir push. Varias por persona (tope 10); si el
        // navegador era de otra cuenta, pasa a esta.
        mi.MapPost("/push", async (RegistrarPushCmd cmd, ServicioAvisosPush servicio, ClaimsPrincipal quien,
                CancellationToken ct) =>
            {
                var usuarioId = quien.UsuarioId();
                if (usuarioId is null) return Results.Unauthorized();
                return Respuestas.DeResultado(await servicio.RegistrarAsync(usuarioId.Value, cmd, ct));
            })
            .ConValidacion<RegistrarPushCmd>()
            .RequireRateLimiting(Tasa.General)
            .WithName(EndpointRegistrar);

        // Baja de ESTE navegador, al cerrar sesion. Responde 204 exista o no (no dice si el
        // endpoint esta en otra cuenta). Se deja pasar aunque falten datos de la cuenta (ES-2):
        // dejar de recibir avisos no se condiciona a nada.
        mi.MapDelete("/push", async ([FromBody] BajaPushCmd cmd, ServicioAvisosPush servicio,
                ClaimsPrincipal quien, CancellationToken ct) =>
            {
                var usuarioId = quien.UsuarioId();
                if (usuarioId is null) return Results.Unauthorized();
                return Respuestas.DeResultado(await servicio.DarDeBajaAsync(usuarioId.Value, cmd, ct));
            })
            .ConValidacion<BajaPushCmd>()
            .RequireRateLimiting(Tasa.General)
            .WithName(EndpointBaja);

        // API-43 (lectura). La documentacion solo trae el PUT; la pantalla necesita leer como esta
        // todo antes de cambiarlo. Cada tipo con su grupo, sus canales y si el correo es fijo.
        mi.MapGet("/preferencias", async (ServicioPreferenciasAviso servicio, ClaimsPrincipal quien,
                CancellationToken ct) =>
            {
                var usuarioId = quien.UsuarioId();
                if (usuarioId is null) return Results.Unauthorized();
                return Results.Ok(await servicio.VerAsync(usuarioId.Value, ct));
            })
            .RequireRateLimiting(Tasa.General)
            .WithName(EndpointVerPreferencias);

        // API-43. Cambia los canales de los tipos que vienen y devuelve como quedo todo. El correo
        // de los avisos de cobro, plan y tarjeta no se puede apagar (400). Se deja pasar aunque
        // falten datos de la cuenta (ES-2): elegir que avisos recibir no se condiciona a nada.
        mi.MapPut("/preferencias", async (GuardarPreferenciasCmd cmd, ServicioPreferenciasAviso servicio,
                ClaimsPrincipal quien, CancellationToken ct) =>
            {
                var usuarioId = quien.UsuarioId();
                if (usuarioId is null) return Results.Unauthorized();
                return Respuestas.DeResultado(await servicio.GuardarAsync(usuarioId.Value, cmd, ct));
            })
            .ConValidacion<GuardarPreferenciasCmd>()
            .RequireRateLimiting(Tasa.General)
            .WithName(EndpointPreferencias);

        return app;
    }
}
