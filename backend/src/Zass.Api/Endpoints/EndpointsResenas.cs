using System.Security.Claims;
using FluentValidation;
using Zass.Api.Configuracion;
using Zass.Aplicacion.Identidad;
using Zass.Aplicacion.Resenas;
using Zass.Dominio.Comun;

namespace Zass.Api.Endpoints;

// Lo que llega por HTTP. La persona NUNCA viaja en el cuerpo: la pone la sesión.
public sealed record RegistrarContactoHttpCmd(Guid PerfilId, OrigenContacto Origen, CanalContacto Canal);

public sealed record PublicarResenaHttpCmd(
    Guid ContactoId, int Calificacion, string Comentario, string[]? Aspectos, bool? EnviarIgual);

public sealed record EditarResenaHttpCmd(
    int Calificacion, string Comentario, string[]? Aspectos, bool? EnviarIgual);

public sealed record ResponderResenaHttpCmd(string Texto);

public sealed record ReportarResenaHttpCmd(MotivoReporte Motivo, string? Detalle);

/// <summary>`Decision` es "mantener" o "retirar": un texto y no un booleano, para que el cuerpo se lea solo.</summary>
public sealed record ResolverReporteHttpCmd(string Decision, string Motivo);

// Aquí solo se acota la FORMA (largos máximos, que la lista no sea enorme); las reglas de verdad
// —20 a 1.500 caracteres, aspectos permitidos, plazo— las aplica MOD-11.
public sealed class ValidadorRegistrarContacto : AbstractValidator<RegistrarContactoHttpCmd>
{
    public ValidadorRegistrarContacto()
    {
        RuleFor(x => x.PerfilId).NotEmpty().WithMessage("Falta el prestador.");
        RuleFor(x => x.Origen).IsInEnum().WithMessage("El origen del contacto no es válido.");
        RuleFor(x => x.Canal).IsInEnum().WithMessage("El canal del contacto no es válido.");
    }
}

public sealed class ValidadorPublicarResena : AbstractValidator<PublicarResenaHttpCmd>
{
    public ValidadorPublicarResena()
    {
        RuleFor(x => x.ContactoId).NotEmpty().WithMessage("Falta el contacto.");
        RuleFor(x => x.Comentario).NotNull().MaximumLength(ReglasDeResenas.ComentarioMaximo * 4);
        RuleFor(x => x.Aspectos!.Length).LessThanOrEqualTo(10).When(x => x.Aspectos is not null);
    }
}

public sealed class ValidadorEditarResena : AbstractValidator<EditarResenaHttpCmd>
{
    public ValidadorEditarResena()
    {
        RuleFor(x => x.Comentario).NotNull().MaximumLength(ReglasDeResenas.ComentarioMaximo * 4);
        RuleFor(x => x.Aspectos!.Length).LessThanOrEqualTo(10).When(x => x.Aspectos is not null);
    }
}

public sealed class ValidadorResponderResena : AbstractValidator<ResponderResenaHttpCmd>
{
    public ValidadorResponderResena() =>
        RuleFor(x => x.Texto).NotNull().MaximumLength(ReglasDeResenas.RespuestaMaxima * 4);
}

public sealed class ValidadorReportarResena : AbstractValidator<ReportarResenaHttpCmd>
{
    public ValidadorReportarResena()
    {
        RuleFor(x => x.Motivo).IsInEnum().WithMessage("El motivo del reporte no es válido.");
        RuleFor(x => x.Detalle).MaximumLength(ReglasDeResenas.DetalleMaximo * 4);
    }
}

public sealed class ValidadorResolverReporte : AbstractValidator<ResolverReporteHttpCmd>
{
    public ValidadorResolverReporte()
    {
        RuleFor(x => x.Decision).NotEmpty()
            .Must(d => d is "mantener" or "retirar")
            .WithMessage("La decisión es «mantener» o «retirar».");
        RuleFor(x => x.Motivo).NotNull().MaximumLength(ReglasDeResenas.MotivoResolucionMaximo * 4);
    }
}

/// <summary>
/// MOD-11 · S5 (API-46 a API-53). Los ocho son del grupo G salvo los dos internos, que son del S.
///
/// Aquí no se compone con otros módulos: MOD-11 consume a MOD-01 por su cuenta (rol, correo
/// verificado, misma persona), así que cada endpoint solo pasa la persona de la sesión.
/// </summary>
public static class EndpointsResenas
{
    public const string CabeceraIdempotencia = "Idempotency-Key";

    public static IEndpointRouteBuilder MapearResenas(this IEndpointRouteBuilder app)
    {
        var grupo = app.MapGroup("/api");
        grupo.RequireAuthorization();
        grupo.AddEndpointFilter<FiltroCsrf>();

        // API-46 · registra el contacto desde la ficha. Arranca los 7 días.
        grupo.MapPost("/contactos", async (
                RegistrarContactoHttpCmd cmd, IServicioResenas resenas, ClaimsPrincipal quien) =>
            {
                var usuarioId = quien.UsuarioId();
                if (usuarioId is null) return Results.Unauthorized();

                // El origen "cita" es del módulo de agenda y no se puede pedir desde fuera: la cita
                // no viaja en este cuerpo, y un contacto "por cita" sin cita no existe.
                if (cmd.Origen == OrigenContacto.Cita)
                    return Respuestas.Problema(Resultado.Falla(
                        Fallas.Validacion, "Ese origen de contacto no se puede registrar desde aquí."));

                var r = await resenas.RegistrarContactoAsync(
                    usuarioId.Value, cmd.PerfilId, cmd.Origen, cmd.Canal);
                return r.Exito ? Results.Ok(new { id = r.Valor }) : Problema(r);
            })
            .ConValidacion<RegistrarContactoHttpCmd>()
            .RequireRateLimiting(Tasa.General)
            .WithName("API-46");

        // API-47 · ¿puedo reseñar a este prestador? Siempre 200: "no" es una respuesta, no un error.
        grupo.MapGet("/mi/elegibilidad", async (
                Guid? perfilId, IServicioResenas resenas, ClaimsPrincipal quien) =>
            {
                var usuarioId = quien.UsuarioId();
                if (usuarioId is null) return Results.Unauthorized();
                if (perfilId is null)
                    return Respuestas.Problema(Resultado.Falla(Fallas.Validacion, "Falta el prestador."));

                var r = await resenas.EsElegibleAsync(usuarioId.Value, perfilId.Value);
                return r.Exito ? Results.Ok(r.Valor) : Problema(r);
            })
            .RequireRateLimiting(Tasa.General)
            .WithName("API-47");

        // API-48 · publica. Idempotency-Key obligatoria: sin ella, 400 (api-rest.md).
        grupo.MapPost("/resenas", async (
                PublicarResenaHttpCmd cmd, HttpRequest peticion,
                IServicioResenas resenas, ClaimsPrincipal quien) =>
            {
                var usuarioId = quien.UsuarioId();
                if (usuarioId is null) return Results.Unauthorized();

                var clave = peticion.Headers[CabeceraIdempotencia].ToString();
                if (string.IsNullOrWhiteSpace(clave))
                    return Respuestas.Problema(Resultado.Falla(
                        Fallas.Validacion, $"Falta la cabecera {CabeceraIdempotencia}."));

                var r = await resenas.PublicarAsync(new PublicarResenaCmd(
                    usuarioId.Value, cmd.ContactoId, cmd.Calificacion, cmd.Comentario,
                    cmd.Aspectos, cmd.EnviarIgual ?? false, clave));
                if (!r.Exito) return Problema(r);

                if (r.Valor!.RequiereConfirmacion) return Advertencia(r.Valor);
                // 201 la primera vez, 200 el reintento idempotente (api-rest.md).
                return r.Valor.YaExistia
                    ? Results.Ok(r.Valor.Resena)
                    : Results.Created($"/api/resenas/{r.Valor.Resena!.Id}", r.Valor.Resena);
            })
            .ConValidacion<PublicarResenaHttpCmd>()
            .RequireRateLimiting(Tasa.General)
            .WithName("API-48");

        // API-49 · edita dentro de las 72 h.
        grupo.MapPut("/resenas/{id:guid}", async (
                Guid id, EditarResenaHttpCmd cmd, IServicioResenas resenas, ClaimsPrincipal quien) =>
            {
                var usuarioId = quien.UsuarioId();
                if (usuarioId is null) return Results.Unauthorized();

                var r = await resenas.EditarAsync(new EditarResenaCmd(
                    id, usuarioId.Value, cmd.Calificacion, cmd.Comentario, cmd.Aspectos,
                    cmd.EnviarIgual ?? false));
                if (!r.Exito) return Problema(r);
                return r.Valor!.RequiereConfirmacion ? Advertencia(r.Valor) : Results.Ok(r.Valor.Resena);
            })
            .ConValidacion<EditarResenaHttpCmd>()
            .RequireRateLimiting(Tasa.General)
            .WithName("API-49");

        // API-50 · el prestador responde. Una por reseña; volver a enviar la edita (RN-S5-06).
        grupo.MapPost("/resenas/{id:guid}/respuesta", async (
                Guid id, ResponderResenaHttpCmd cmd, IServicioResenas resenas, ClaimsPrincipal quien) =>
            {
                var usuarioId = quien.UsuarioId();
                if (usuarioId is null) return Results.Unauthorized();

                var r = await resenas.ResponderAsync(id, usuarioId.Value, cmd.Texto);
                if (!r.Exito) return Problema(r);
                return r.Valor
                    ? Results.Created($"/api/resenas/{id}/respuesta", new { creada = true })
                    : Results.Ok(new { creada = false });
            })
            .ConValidacion<ResponderResenaHttpCmd>()
            .RequireRateLimiting(Tasa.General)
            .WithName("API-50");

        // API-51 · reporta con motivo tipificado.
        grupo.MapPost("/resenas/{id:guid}/reporte", async (
                Guid id, ReportarResenaHttpCmd cmd, IServicioResenas resenas, ClaimsPrincipal quien) =>
            {
                var usuarioId = quien.UsuarioId();
                if (usuarioId is null) return Results.Unauthorized();

                var r = await resenas.ReportarAsync(id, usuarioId.Value, cmd.Motivo, cmd.Detalle);
                return r.Exito ? Results.NoContent() : Problema(r);
            })
            .ConValidacion<ReportarResenaHttpCmd>()
            .RequireRateLimiting(Tasa.General)
            .WithName("API-51");

        // ---------------------------------------------------------------- internos (grupo S)

        var interno = app.MapGroup("/api/interno");
        interno.RequireAuthorization(p => p.RequireRole(Roles.Moderador));
        interno.AddEndpointFilter<FiltroCsrf>();

        // API-52 · la cola de reportes abiertos, sin los propios.
        interno.MapGet("/reportes/cola", async (IServicioResenas resenas, ClaimsPrincipal quien) =>
            {
                var usuarioId = quien.UsuarioId();
                if (usuarioId is null) return Results.Unauthorized();

                var r = await resenas.ColaDeReportesAsync(usuarioId.Value);
                return r.Exito ? Results.Ok(r.Valor) : Problema(r);
            })
            .RequireRateLimiting(Tasa.Sensible)
            .WithName("API-52");

        // API-53 · mantiene o retira, con motivo.
        interno.MapPost("/reportes/{id:guid}/resolver", async (
                Guid id, ResolverReporteHttpCmd cmd, IServicioResenas resenas, ClaimsPrincipal quien) =>
            {
                var usuarioId = quien.UsuarioId();
                if (usuarioId is null) return Results.Unauthorized();

                var r = await resenas.ResolverReporteAsync(
                    id, usuarioId.Value, cmd.Decision == "retirar", cmd.Motivo);
                return r.Exito ? Results.NoContent() : Problema(r);
            })
            .ConValidacion<ResolverReporteHttpCmd>()
            .RequireRateLimiting(Tasa.Sensible)
            .WithName("API-53");

        // ---------------------------------------------------------------- público

        // Lectura pública de las reseñas de una ficha. NO está en la lista API-46..53 de la
        // especificación (MOD-11 sí declara `ResenasPublicas`); sin una ruta así las reseñas se
        // publicarían y nadie podría leerlas. Sin sesión, igual que la ficha.
        app.MapGet("/api/prestadores/{slug}/resenas", async (
                string slug, string? cursor, IServicioIdentidad identidad, IServicioResenas resenas) =>
            {
                var perfil = await identidad.PerfilPublicoPorSlugAsync(slug);
                if (perfil is null)
                    return Respuestas.Problema(Resultado.Falla(Fallas.NoEncontrado, "No encontramos a ese prestador."));

                var r = await resenas.ResenasPublicasAsync(perfil.Id, cursor);
                return r.Exito ? Results.Ok(r.Valor) : Problema(r);
            })
            .RequireRateLimiting(Tasa.Publico)
            .WithName("API-resenas-publicas");

        return app;
    }

    /// <summary>
    /// E-403 de CU-16: la elegibilidad no se cumple. 403 CON el motivo en el cuerpo, porque el
    /// flujo 4A.3 pide explicar cuál condición falta; el resto de las fallas va por el traductor
    /// común.
    /// </summary>
    private static IResult Problema(Resultado r) => r.Codigo == FallasResenas.NoElegible
        ? Results.Problem(title: "No elegible", detail: r.Mensaje, statusCode: StatusCodes.Status403Forbidden)
        : Respuestas.Problema(r);

    /// <summary>CU-16 4E: el texto parece traer datos personales. 422 con los fragmentos y la forma de seguir.</summary>
    private static IResult Advertencia(ResultadoPublicacion r) => Results.Json(new
    {
        titulo = "Revisa tu comentario",
        detalle = "Tu comentario parece incluir datos personales. Puedes corregirlo o enviarlo igual (se revisará).",
        requiereConfirmacion = true,
        fragmentos = r.DatosPersonalesDetectados,
    }, statusCode: StatusCodes.Status422UnprocessableEntity);
}
