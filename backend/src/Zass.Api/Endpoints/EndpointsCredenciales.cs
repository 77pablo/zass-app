using System.Security.Claims;
using Zass.Aplicacion.Credenciales;
using Zass.Aplicacion.Identidad;
using Zass.Api.Configuracion;
using Zass.Dominio.Comun;

namespace Zass.Api.Endpoints;

/// <summary>
/// MOD-02 - Credenciales (CU-05). Todo vive bajo /api/mi: son las credenciales de quien está
/// dentro, y la persona se decide con la sesión y nunca con un id que venga en la ruta.
///
/// Aquí se compone, igual que con MOD-03: MOD-01 sabe cuál es el perfil del usuario, MOD-02
/// guarda las credenciales de ese perfil, y ninguno conoce al otro.
/// </summary>
public static class EndpointsCredenciales
{
    public static IEndpointRouteBuilder MapearCredenciales(this IEndpointRouteBuilder app)
    {
        var grupo = app.MapGroup("/api/mi");
        grupo.RequireAuthorization();
        grupo.AddEndpointFilter<FiltroCsrf>();

        // API-24 - Mis credenciales, con el estado interno.
        grupo.MapGet("/credenciales", async (
                IServicioCredenciales credenciales, ServicioPerfiles perfiles, ClaimsPrincipal quien) =>
            {
                var usuarioId = quien.UsuarioId();
                if (usuarioId is null) return Results.Unauthorized();

                var mio = await perfiles.MiPerfilPrestadorAsync(usuarioId.Value);
                if (!mio.Exito) return Respuestas.Problema(mio);

                return Results.Ok(await credenciales.MisCredencialesAsync(mio.Valor!.Id));
            })
            .RequireRateLimiting(Tasa.General)
            .WithName("API-24");

        // API-25 - Crea una credencial en borrador.
        grupo.MapPost("/credenciales", async (
                CrearCredencialHttpCmd cmd,
                IServicioCredenciales credenciales, ServicioPerfiles perfiles, ClaimsPrincipal quien) =>
            {
                var usuarioId = quien.UsuarioId();
                if (usuarioId is null) return Results.Unauthorized();

                var mio = await perfiles.MiPerfilPrestadorAsync(usuarioId.Value);
                if (!mio.Exito) return Respuestas.Problema(mio);

                var r = await credenciales.CrearAsync(new CrearCredencialCmd(
                    mio.Valor!.Id, cmd.Tipo, cmd.Nombre, cmd.EntidadId, cmd.EntidadNombre,
                    cmd.ObtenidaEn, cmd.VenceEn));

                return r.Exito
                    ? Results.Created($"/api/mi/credenciales/{r.Valor}", new { id = r.Valor })
                    : Respuestas.Problema(r);
            })
            .ConValidacion<CrearCredencialHttpCmd>()
            .RequireRateLimiting(Tasa.General)
            .WithName("API-25");

        // API-28 - Elimina una credencial. La de otra persona responde igual que una inexistente.
        grupo.MapDelete("/credenciales/{id:guid}", async (
                Guid id,
                IServicioCredenciales credenciales, ServicioPerfiles perfiles, ClaimsPrincipal quien) =>
            {
                var usuarioId = quien.UsuarioId();
                if (usuarioId is null) return Results.Unauthorized();

                var mio = await perfiles.MiPerfilPrestadorAsync(usuarioId.Value);
                if (!mio.Exito) return Respuestas.Problema(mio);

                return Respuestas.DeResultado(await credenciales.EliminarAsync(mio.Valor!.Id, id));
            })
            .RequireRateLimiting(Tasa.General)
            .WithName("API-28");

        return app;
    }
}
