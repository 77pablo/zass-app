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
        MapearElCicloDeLaCita(app);
        return app;
    }

    /// <summary>
    /// Quién actúa, sacado de la SESIÓN y nunca del cuerpo. Es lo que impide que alguien
    /// mande el perfil de otro y confirme sus citas.
    /// </summary>
    private static async Task<AutorDeCambio> AutorAsync(
        ClaimsPrincipal quien, IServicioIdentidad identidad, Guid usuarioId) =>
        new(usuarioId, await identidad.PerfilPrestadorIdAsync(usuarioId));

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

        // CS-11 - Mis bloqueos vigentes.
        grupo.MapGet("/bloqueos", async (
                IServicioAgenda agenda, ServicioPerfiles perfiles, ClaimsPrincipal quien) =>
            {
                var usuarioId = quien.UsuarioId();
                if (usuarioId is null) return Results.Unauthorized();

                var mio = await perfiles.MiPerfilPrestadorAsync(usuarioId.Value);
                if (!mio.Exito) return Respuestas.Problema(mio);

                return Results.Ok(await agenda.MisBloqueosAsync(mio.Valor!.Id));
            })
            .RequireRateLimiting(Tasa.General)
            .WithName("API-34-listar");

        // CS-11 - Quita un bloqueo mío. Uno ajeno da 404, igual que uno que no existe.
        grupo.MapDelete("/bloqueos/{id:guid}", async (
                Guid id, IServicioAgenda agenda, ServicioPerfiles perfiles, ClaimsPrincipal quien) =>
            {
                var usuarioId = quien.UsuarioId();
                if (usuarioId is null) return Results.Unauthorized();

                var mio = await perfiles.MiPerfilPrestadorAsync(usuarioId.Value);
                if (!mio.Exito) return Respuestas.Problema(mio);

                var r = await agenda.QuitarBloqueoAsync(mio.Valor!.Id, id);
                return r.Exito ? Results.NoContent() : Respuestas.Problema(r);
            })
            .RequireRateLimiting(Tasa.General)
            .WithName("API-34-quitar");

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

                if (http.ClaveIdempotencia() is not { } clave) return FaltaLaClave();

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

    // ------------------------------------------------------------- CU-13, CU-14 y CS-12

    /// <summary>
    /// Lo que se le hace a una cita que ya existe. Las tres acciones que tocan bloques
    /// —cancelar y reprogramar— llevan `Idempotency-Key`, porque un reintento podría dejar un
    /// bloque libre y otro reservado, o mandar dos avisos de cancelación (api-rest.md).
    ///
    /// Confirmar, rechazar y marcar asistencia no la llevan: repetirlas es inofensivo, porque
    /// la segunda encuentra la cita en un estado desde el que la transición no existe.
    /// </summary>
    private static void MapearElCicloDeLaCita(IEndpointRouteBuilder app)
    {
        var grupo = app.MapGroup("/api/citas/{id:guid}");
        grupo.RequireAuthorization();
        grupo.AddEndpointFilter<FiltroCsrf>();

        // API-38 - El prestador confirma una solicitada (T3).
        grupo.MapPost("/confirmar", async (
                Guid id, IServicioAgenda agenda, IServicioIdentidad identidad, ClaimsPrincipal quien) =>
            {
                var usuarioId = quien.UsuarioId();
                if (usuarioId is null) return Results.Unauthorized();

                return Respuestas.DeResultado(await agenda.ConfirmarAsync(
                    id, await AutorAsync(quien, identidad, usuarioId.Value)));
            })
            .RequireRateLimiting(Tasa.General)
            .WithName("API-38");

        // API-39 - El prestador rechaza (T4). El motivo es opcional.
        grupo.MapPost("/rechazar", async (
                Guid id, MotivoHttpCmd cmd,
                IServicioAgenda agenda, IServicioIdentidad identidad, ClaimsPrincipal quien) =>
            {
                var usuarioId = quien.UsuarioId();
                if (usuarioId is null) return Results.Unauthorized();

                return Respuestas.DeResultado(await agenda.RechazarAsync(
                    id, await AutorAsync(quien, identidad, usuarioId.Value), cmd.Motivo));
            })
            .ConValidacion<MotivoHttpCmd>()
            .RequireRateLimiting(Tasa.General)
            .WithName("API-39");

        // API-40 - Cancela. Cualquiera de los dos, y con menos de 12 h queda marcada tardía.
        grupo.MapPost("/cancelar", async (
                Guid id, MotivoHttpCmd cmd, HttpContext http,
                IServicioAgenda agenda, IServicioIdentidad identidad, ClaimsPrincipal quien) =>
            {
                var usuarioId = quien.UsuarioId();
                if (usuarioId is null) return Results.Unauthorized();
                if (http.ClaveIdempotencia() is not { } clave) return FaltaLaClave();

                return Respuestas.DeResultado(await agenda.CancelarAsync(new CancelarCitaCmd(
                    id, await AutorAsync(quien, identidad, usuarioId.Value), clave, cmd.Motivo)));
            })
            .AddEndpointFilter<FiltroIdempotencia>()
            .ConValidacion<MotivoHttpCmd>()
            .RequireRateLimiting(Tasa.Sensible)
            .WithName("API-40");

        // API-41 - Reprograma: cancela y crea con vínculo a la primera de la cadena. CC-06.
        grupo.MapPost("/reprogramar", async (
                Guid id, ReprogramarHttpCmd cmd, HttpContext http,
                IServicioAgenda agenda, IServicioIdentidad identidad, ClaimsPrincipal quien) =>
            {
                var usuarioId = quien.UsuarioId();
                if (usuarioId is null) return Results.Unauthorized();
                if (http.ClaveIdempotencia() is not { } clave) return FaltaLaClave();

                var r = await agenda.ReprogramarAsync(new ReprogramarCitaCmd(
                    id, cmd.BloqueNuevoId, await AutorAsync(quien, identidad, usuarioId.Value),
                    clave, cmd.Motivo));
                if (!r.Exito) return Respuestas.Problema(r);

                // La cita nueva es un recurso nuevo: 201 la primera vez, 200 en el reintento.
                return r.Valor!.Nueva
                    ? Results.Created($"/api/citas/{r.Valor.Id}", r.Valor)
                    : Results.Ok(r.Valor);
            })
            .AddEndpointFilter<FiltroIdempotencia>()
            .ConValidacion<ReprogramarHttpCmd>()
            .RequireRateLimiting(Tasa.Sensible)
            .WithName("API-41");

        // API-42 - CS-12: solo el prestador, y solo despues de la hora.
        grupo.MapPost("/asistencia", async (
                Guid id, AsistenciaHttpCmd cmd,
                IServicioAgenda agenda, IServicioIdentidad identidad, ClaimsPrincipal quien) =>
            {
                var usuarioId = quien.UsuarioId();
                if (usuarioId is null) return Results.Unauthorized();

                return Respuestas.DeResultado(await agenda.RegistrarAsistenciaAsync(
                    id, await AutorAsync(quien, identidad, usuarioId.Value), cmd.Asistio));
            })
            .ConValidacion<AsistenciaHttpCmd>()
            .RequireRateLimiting(Tasa.General)
            .WithName("API-42");

        // La otra mitad de T10 (decision de Pablo, 11-nov-2026): el BUSCADOR dice que el
        // prestador no llego. Sin cuerpo a proposito: no hay nada que elegir. A quien se acusa
        // lo decide el papel de quien llama, nunca lo que mande.
        //
        // SIN NUMERO DE api-rest.md: ese documento no tiene un API-xx para esto, porque la
        // decision que lo crea es posterior. Hay que asignarle uno antes de fusionar.
        grupo.MapPost("/no-asistio-el-prestador", async (
                Guid id, IServicioAgenda agenda, IServicioIdentidad identidad, ClaimsPrincipal quien) =>
            {
                var usuarioId = quien.UsuarioId();
                if (usuarioId is null) return Results.Unauthorized();

                return Respuestas.DeResultado(await agenda.AcusarAusenciaDelPrestadorAsync(
                    id, await AutorAsync(quien, identidad, usuarioId.Value)));
            })
            .RequireRateLimiting(Tasa.General)
            .WithName("API-42b");
    }

    private static IResult FaltaLaClave() => Results.Problem(
        title: "Falta la clave de idempotencia",
        detail: $"Falta la cabecera {FiltroIdempotencia.Cabecera}.",
        statusCode: StatusCodes.Status400BadRequest);
}
