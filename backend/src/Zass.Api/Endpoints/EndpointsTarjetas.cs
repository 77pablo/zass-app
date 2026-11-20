using System.Security.Claims;
using Zass.Api.Configuracion;
using Zass.Aplicacion.Identidad;
using Zass.Aplicacion.Tarjetas;
using Zass.Dominio.Comun;
using Zass.Dominio.Tarjetas;

namespace Zass.Api.Endpoints;

/// <summary>Request para API-55: codigo de activacion de 12 caracteres.</summary>
public sealed record ActivarRequest(string Codigo);

/// <summary>
/// API-54 · <c>GET /t/{segmento}</c> (CS-18, incluye CU-09). La URL impresa en la tarjeta.
///
/// Fuera de <c>/api</c> a proposito: es la ruta que figura en api-rest.md y la que va impresa.
/// Siempre responde 302, nunca 301: una redireccion permanente la guarda el navegador, y una
/// tarjeta desactivada seguiria abriendo la ficha en ese telefono (RN-S6-05).
///
/// Malformado, inexistente, desactivada y reemplazada salen IDENTICAS: mismo estado, misma
/// Location, mismas cabeceras y cuerpo vacio (RN-S6-06). Sin sesion y sin cookies (RN-S6-07).
/// </summary>
public static class EndpointsTarjetas
{
    public const string PaginaNoValida = "/tarjeta/no-valida";
    public const string PaginaNoDisponible = "/tarjeta/no-disponible";
    public const string PaginaSinActivar = "/tarjeta/sin-activar";

    public static IEndpointRouteBuilder MapearTarjetas(this IEndpointRouteBuilder app)
    {
        app.MapGet("/t/{segmento}", async (
                string segmento, string? m, IServicioTarjetas tarjetas, HttpContext http) =>
            {
                // El QR lleva ?m=qr; la etiqueta NFC, nada. Cualquier otro valor cuenta como NFC.
                var medio = string.Equals(m, "qr", StringComparison.OrdinalIgnoreCase)
                    ? MedioEscaneo.Qr
                    : MedioEscaneo.Nfc;

                var resuelto = await tarjetas.RegistrarEscaneoAsync(segmento, medio, http.RequestAborted);

                // Que ningun cache (ni del navegador ni compartido) recuerde a donde llevo: al
                // desactivar, el cambio tiene que verse en el escaneo siguiente.
                http.Response.Headers.CacheControl = "no-store";
                // La ficha no necesita saber el segmento de la tarjeta por la que se llego.
                http.Response.Headers["Referrer-Policy"] = "no-referrer";

                return Results.Redirect(Destino(resuelto), permanent: false);
            })
            .RequireRateLimiting(Tasa.Publico)
            .WithName("API-54");

        // API-55 POST /t/{segmento}/activar - Activar con codigo de un solo uso (CU-19, CC-05)
        // Fuera de /api a proposito: la ruta que figura en api-rest.md y el QR impreso.
        // Tasa A (Autenticacion): 5 por IP cada 15 min. Requiere sesion prestador con perfil publicado.
        app.MapPost("/t/{segmento}/activar", async (
                string segmento,
                ActivarRequest request,
                IServicioTarjetas tarjetas,
                ServicioPerfiles perfiles,
                ClaimsPrincipal quien,
                HttpContext http) =>
            {
                var usuarioId = quien.UsuarioId();
                if (usuarioId is null) return Results.Unauthorized();

                var mio = await perfiles.MiPerfilPrestadorAsync(usuarioId.Value);
                if (!mio.Exito) return Respuestas.Problema(mio);

                // Verificar que el perfil esta publicado (CU-19 4E)
                if (mio.Valor!.Estado != EstadoPerfil.Publicado)
                    return Respuestas.Problema(Resultado.Falla(Fallas.ReglaDeNegocio, "El perfil debe estar publicado para activar la tarjeta."));

                var ip = http.Connection.RemoteIpAddress?.ToString();
                var r = await tarjetas.ActivarAsync(segmento, request.Codigo, mio.Valor.Id, ip, http.RequestAborted);
                return r.Exito ? Results.Ok(r.Valor) : Respuestas.Problema(r);
            })
            .RequireRateLimiting(Tasa.Autenticacion)
            .WithName("API-55");

        MapearMisTarjetas(app);
        MapearInternoTarjetas(app);

        return app;
    }

    /// <summary>
    /// API-59 POST /api/interno/tarjetas - Emite una tarjeta y su codigo (CU-19, CU-20).
    /// Tasa S (Sensible): 10 por IP cada 15 min. Requiere rol Administrador.
    /// </summary>
    private static void MapearInternoTarjetas(IEndpointRouteBuilder app)
    {
        var grupo = app.MapGroup("/api/interno");
        grupo.RequireAuthorization(Politicas.Administrador);
        grupo.AddEndpointFilter<FiltroCsrf>();

        grupo.MapPost("/tarjetas", async (
                IServicioTarjetas tarjetas, HttpContext http) =>
            {
                var r = await tarjetas.EmitirAsync(http.RequestAborted);
                return r.Exito ? Results.Created($"/api/interno/tarjetas/{r.Valor!.Segmento}", r.Valor) : Respuestas.Problema(r);
            })
            .RequireRateLimiting(Tasa.Sensible)
            .WithName("API-59");
    }

    /// <summary>
    /// API-56 a API-58 (CU-20, CS-18). Con sesion y CSRF, grupo G. Ninguno mira la
    /// suscripcion. Ajena e inexistente dan el mismo 404 (lo decide el servicio).
    /// </summary>
    private static void MapearMisTarjetas(IEndpointRouteBuilder app)
    {
        var grupo = app.MapGroup("/api/mi");
        grupo.RequireAuthorization();
        grupo.AddEndpointFilter<FiltroCsrf>();

        // API-56 - Mis tarjetas y su estado.
        grupo.MapGet("/tarjetas", async (
                IServicioTarjetas tarjetas, ServicioPerfiles perfiles, ClaimsPrincipal quien) =>
            {
                var usuarioId = quien.UsuarioId();
                if (usuarioId is null) return Results.Unauthorized();

                var mio = await perfiles.MiPerfilPrestadorAsync(usuarioId.Value);
                if (!mio.Exito) return Respuestas.Problema(mio);

                return Results.Ok(await tarjetas.MisTarjetasAsync(mio.Valor!.Id));
            })
            .RequireRateLimiting(Tasa.General)
            .WithName("API-56");

        // API-57 - Baja inmediata. No requiere tener la tarjeta (RN-S6-05).
        grupo.MapPost("/tarjetas/{id:guid}/desactivar", async (
                Guid id, IServicioTarjetas tarjetas, ServicioPerfiles perfiles, ClaimsPrincipal quien) =>
            {
                var usuarioId = quien.UsuarioId();
                if (usuarioId is null) return Results.Unauthorized();

                var mio = await perfiles.MiPerfilPrestadorAsync(usuarioId.Value);
                if (!mio.Exito) return Respuestas.Problema(mio);

                var r = await tarjetas.DesactivarAsync(mio.Valor!.Id, id);
                return r.Exito ? Results.NoContent() : Respuestas.Problema(r);
            })
            .RequireRateLimiting(Tasa.General)
            .WithName("API-57");

        // API-58 - Escaneos agregados por medio y periodo, sin conversion (JQ-8).
        grupo.MapGet("/tarjetas/{id:guid}/estadisticas", async (
                Guid id, IServicioTarjetas tarjetas, ServicioPerfiles perfiles, ClaimsPrincipal quien) =>
            {
                var usuarioId = quien.UsuarioId();
                if (usuarioId is null) return Results.Unauthorized();

                var mio = await perfiles.MiPerfilPrestadorAsync(usuarioId.Value);
                if (!mio.Exito) return Respuestas.Problema(mio);

                return Respuestas.DeResultado(await tarjetas.EstadisticasAsync(mio.Valor!.Id, id));
            })
            .RequireRateLimiting(Tasa.General)
            .WithName("API-58");
    }

    /// <summary>Location relativa: la resuelve el navegador contra zass.cl, por donde entro.</summary>
    internal static string Destino(EscaneoResuelto r) => r.Destino switch
    {
        DestinoTarjeta.Ficha => "/prestadores/" + Uri.EscapeDataString(r.Slug!),
        DestinoTarjeta.PerfilNoDisponible => PaginaNoDisponible,
        DestinoTarjeta.Activacion => PaginaSinActivar,
        _ => PaginaNoValida,
    };
}
