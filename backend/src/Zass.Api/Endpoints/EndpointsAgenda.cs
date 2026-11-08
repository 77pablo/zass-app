using System.Security.Claims;
using Zass.Api.Configuracion;
using Zass.Aplicacion.Agenda;
using Zass.Aplicacion.Identidad;
using Zass.Dominio.Comun;

namespace Zass.Api.Endpoints;

/// <summary>
/// MOD-06 Agenda. API-32 a API-37 de la etapa de disponibilidad y agendamiento.
///
/// Tres grupos distintos y a propósito:
/// <list type="bullet">
/// <item><c>/api/mi/...</c> — lo del prestador. Exige sesión y el perfil sale de ella</item>
/// <item><c>/api/prestadores/{slug}/bloques</c> — público: elegir hora no exige cuenta
/// (flujo 4G de CU-12 guarda la intención y pide registrarse al confirmar)</item>
/// <item><c>/api/citas</c> — agendar. Exige sesión Y la cabecera <c>Idempotency-Key</c></item>
/// </list>
/// </summary>
public static class EndpointsAgenda
{
    public static IEndpointRouteBuilder MapearAgenda(this IEndpointRouteBuilder app)
    {
        MapearLoDelPrestador(app);
        MapearElCalendarioPublico(app);
        MapearCitas(app);
        return app;
    }

    // ------------------------------------------------------------- CU-11 y CS-11

    private static void MapearLoDelPrestador(IEndpointRouteBuilder app)
    {
        var grupo = app.MapGroup("/api/mi");
        grupo.RequireAuthorization();
        grupo.AddEndpointFilter<FiltroCsrf>();

        // API-32 - Mis reglas de atención, servicio por servicio.
        grupo.MapGet("/disponibilidad", async (
                IServicioAgenda agenda, ServicioPerfiles perfiles, ClaimsPrincipal quien) =>
            {
                var usuarioId = quien.UsuarioId();
                if (usuarioId is null) return Results.Unauthorized();

                var mio = await perfiles.MiPerfilPrestadorAsync(usuarioId.Value);
                if (!mio.Exito) return Respuestas.Problema(mio);

                return Results.Ok(await agenda.MiDisponibilidadAsync(mio.Valor!.Id));
            })
            .RequireRateLimiting(Tasa.General)
            .WithName("API-32");

        // API-33 - Define la disponibilidad de UN servicio y genera sus bloques.
        grupo.MapPut("/disponibilidad", async (
                DefinirDisponibilidadHttpCmd cmd,
                IServicioAgenda agenda, ServicioPerfiles perfiles, ClaimsPrincipal quien) =>
            {
                var usuarioId = quien.UsuarioId();
                if (usuarioId is null) return Results.Unauthorized();

                var mio = await perfiles.MiPerfilPrestadorAsync(usuarioId.Value);
                if (!mio.Exito) return Respuestas.Problema(mio);

                var r = await agenda.DefinirDisponibilidadAsync(new DefinirDisponibilidadCmd(
                    mio.Valor!.Id, cmd.PrestadorServicioId, cmd.Franjas));

                return r.Exito
                    ? Results.Ok(new { bloquesGenerados = r.Valor })
                    : Respuestas.Problema(r);
            })
            .ConValidacion<DefinirDisponibilidadHttpCmd>()
            .RequireRateLimiting(Tasa.General)
            .WithName("API-33");

        // API-34 - CS-11: bloquea una fecha o una franja.
        grupo.MapPost("/bloqueos", async (
                BloquearFechaHttpCmd cmd,
                IServicioAgenda agenda, ServicioPerfiles perfiles, ClaimsPrincipal quien) =>
            {
                var usuarioId = quien.UsuarioId();
                if (usuarioId is null) return Results.Unauthorized();

                var mio = await perfiles.MiPerfilPrestadorAsync(usuarioId.Value);
                if (!mio.Exito) return Respuestas.Problema(mio);

                var r = await agenda.BloquearFechaAsync(new BloquearFechaCmd(
                    mio.Valor!.Id, cmd.DesdeEn, cmd.HastaEn, cmd.Motivo));

                return r.Exito
                    ? Results.Created($"/api/mi/bloqueos/{r.Valor!.Id}", r.Valor)
                    : Respuestas.Problema(r);
            })
            .ConValidacion<BloquearFechaHttpCmd>()
            .RequireRateLimiting(Tasa.General)
            .WithName("API-34");

        // API-37 - Mis citas, de los dos lados. El perfil es opcional: un buscador sin
        // perfil de prestador ve solo las que pidió, y eso no es un error.
        grupo.MapGet("/citas", async (
                IServicioAgenda agenda, IServicioIdentidad identidad, ClaimsPrincipal quien) =>
            {
                var usuarioId = quien.UsuarioId();
                if (usuarioId is null) return Results.Unauthorized();

                var perfilId = await identidad.PerfilPrestadorIdAsync(usuarioId.Value);
                return Results.Ok(await agenda.MisCitasAsync(usuarioId.Value, perfilId));
            })
            .RequireRateLimiting(Tasa.General)
            .WithName("API-37");
    }

    // ------------------------------------------------------------- CU-12 pasos 2-4

    private static void MapearElCalendarioPublico(IEndpointRouteBuilder app)
    {
        // API-35 - Los bloques de un servicio. Público: quien mira horas todavía no tiene
        // por qué tener cuenta. El servicio va en la consulta porque la disponibilidad es
        // por servicio, no por prestador.
        app.MapGet("/api/prestadores/{slug}/bloques", async (
                string slug, Guid servicio, DateTime? desde, DateTime? hasta,
                IServicioAgenda agenda, IServicioIdentidad identidad) =>
            {
                var perfil = await identidad.PerfilPublicoPorSlugAsync(slug);
                if (perfil is null) return Results.NotFound();

                var d = desde ?? DateTime.UtcNow;
                var r = await agenda.BloquesLibresAsync(servicio, d, hasta ?? d);
                if (!r.Exito) return Respuestas.Problema(r);

                // El servicio existe, pero puede no ser de este prestador. Se comprueba aquí
                // porque la ruta promete «los bloques de ESTE prestador»: sin esto, cualquier
                // slug serviría para consultar la agenda de cualquiera, y el 404 de más
                // abajo no distingue «no existe» de «no es suyo», a propósito.
                if (r.Valor!.PerfilPrestadorId != perfil.Id) return Results.NotFound();

                return Results.Ok(r.Valor);
            })
            .RequireRateLimiting(Tasa.Publico)
            .WithName("API-35");
    }

    // ------------------------------------------------------------- CU-12 / CU-13

    private static void MapearCitas(IEndpointRouteBuilder app)
    {
        var grupo = app.MapGroup("/api");
        grupo.RequireAuthorization();
        grupo.AddEndpointFilter<FiltroCsrf>();

        // API-36 - Agenda automática o solicita manual. Encapsula CC-01 y CC-04.
        //
        // El filtro de idempotencia va ANTES del endpoint: sin la cabecera no se ejecuta
        // nada, que es lo que pide api-rest.md para los endpoints marcados con «I».
        grupo.MapPost("/citas", async (
                AgendarHttpCmd cmd, HttpContext http,
                IServicioAgenda agenda, ClaimsPrincipal quien) =>
            {
                var usuarioId = quien.UsuarioId();
                if (usuarioId is null) return Results.Unauthorized();

                var clave = http.ClaveIdempotencia();
                if (clave is null)
                    return Results.Problem(
                        title: "Falta la clave de idempotencia",
                        detail: $"Falta la cabecera {FiltroIdempotencia.Cabecera}.",
                        statusCode: StatusCodes.Status400BadRequest);

                var r = await agenda.AgendarAsync(new AgendarCmd(
                    usuarioId.Value, cmd.BloqueId, clave, cmd.NotaBuscador));
                if (!r.Exito) return Respuestas.Problema(r);

                // 201 la primera vez, 200 cuando la clave ya existía: así el cliente
                // distingue «se creó» de «ya estaba» sin que nada se duplique (flujo 4B).
                return r.Valor!.Nueva
                    ? Results.Created($"/api/citas/{r.Valor.Id}", r.Valor)
                    : Results.Ok(r.Valor);
            })
            .AddEndpointFilter<FiltroIdempotencia>()
            .ConValidacion<AgendarHttpCmd>()
            .RequireRateLimiting(Tasa.Sensible)
            .WithName("API-36");
    }
}
