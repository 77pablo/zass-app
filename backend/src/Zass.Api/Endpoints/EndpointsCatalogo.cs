using System.Security.Claims;
using Zass.Api.Configuracion;
using Zass.Aplicacion.Catalogo;
using Zass.Aplicacion.Identidad;
using Zass.Aplicacion.Suscripciones;
using Zass.Dominio.Comun;

namespace Zass.Api.Endpoints;

/// <summary>API-20. La lista completa de lo que el prestador ofrece, no un parche.</summary>
public sealed record ReemplazarServiciosHttpCmd(IReadOnlyList<ServicioOfrecidoCmd> Servicios);

/// <summary>
/// API-02, API-03 y API-20. MOD-03 Catalogo.
///
/// Los dos primeros son publicos: el selector de rubros de la busqueda (CU-08) y el del
/// formulario de perfil (CU-03) tienen que funcionar sin sesion. El tercero exige sesion
/// y token CSRF, igual que todo lo que escribe.
/// </summary>
public static class EndpointsCatalogo
{
    public static IEndpointRouteBuilder MapearCatalogo(this IEndpointRouteBuilder app)
    {
        MapearPublico(app.MapGroup("/api"));
        MapearMiOferta(app.MapGroup("/api/mi"));
        return app;
    }

    private static void MapearPublico(RouteGroupBuilder grupo)
    {
        // API-02 - Rubros activos, con ?q= para autocompletar.
        grupo.MapGet("/rubros", async (IServicioCatalogo catalogo, string? q) =>
                Results.Ok(await catalogo.BuscarRubrosAsync(q)))
            .RequireRateLimiting(Tasa.Publico)
            .WithName("API-02");

        // API-03 - Servicios sugeridos de un rubro.
        //
        // El identificador se declara `int` y no `short` a proposito: con `short`, un
        // /rubros/99999/servicios no casaria con la ruta y devolveria 404 sin mensaje.
        // Asi se contesta lo mismo que para un rubro que no existe, que es lo que es.
        grupo.MapGet("/rubros/{rubroId:int}/servicios", async (
                int rubroId, IServicioCatalogo catalogo) =>
            {
                if (rubroId is <= 0 or > short.MaxValue)
                    return Respuestas.Problema(Resultado.Falla(
                        Fallas.NoEncontrado, "Ese rubro no existe o ya no esta activo."));

                return Respuestas.DeResultado(
                    await catalogo.ServiciosDeRubroAsync((short)rubroId));
            })
            .RequireRateLimiting(Tasa.Publico)
            .WithName("API-03");
    }

    private static void MapearMiOferta(RouteGroupBuilder grupo)
    {
        grupo.RequireAuthorization();
        grupo.AddEndpointFilter<FiltroCsrf>();

        // Leer lo que ya ofrece. No esta en api-rest.md porque la tabla solo preveia el
        // PUT; sin el GET, la pantalla de CU-03 paso 4 no puede pintar el formulario con
        // lo guardado, y la ficha publica no sirve: el perfil todavia es borrador.
        grupo.MapGet("/perfil-prestador/servicios", async (
                IServicioCatalogo catalogo, ServicioPerfiles perfiles, ClaimsPrincipal quien) =>
            {
                var usuarioId = quien.UsuarioId();
                if (usuarioId is null) return Results.Unauthorized();

                var mio = await perfiles.MiPerfilPrestadorAsync(usuarioId.Value);
                if (!mio.Exito) return Respuestas.Problema(mio);

                return Results.Ok(await catalogo.ServiciosDePrestadorAsync(mio.Valor!.Id));
            })
            .RequireRateLimiting(Tasa.General)
            .WithName("API-20b");

        // API-20 - Reemplaza los servicios ofrecidos (CU-03 paso 4).
        //
        // Aqui se compone, igual que API-22: el rubro del perfil lo sabe MOD-01, el tope
        // de servicios lo sabe MOD-13 y la oferta la guarda MOD-03. Ninguno de los tres
        // tiene que conocer a los otros dos.
        grupo.MapPut("/perfil-prestador/servicios", async (
                ReemplazarServiciosHttpCmd cmd,
                IServicioCatalogo catalogo,
                IServicioSuscripciones suscripciones,
                ServicioPerfiles perfiles,
                ClaimsPrincipal quien) =>
            {
                var usuarioId = quien.UsuarioId();
                if (usuarioId is null) return Results.Unauthorized();

                var mio = await perfiles.MiPerfilPrestadorAsync(usuarioId.Value);
                if (!mio.Exito) return Respuestas.Problema(mio);

                // El rubro se elige en el paso 3 y los servicios en el 4: llegar aqui sin
                // rubro es el formulario fuera de orden, no un dato invalido.
                var rubroId = mio.Valor!.RubroId;
                if (rubroId is null or <= 0)
                    return Respuestas.Problema(Resultado.Falla(
                        Fallas.ReglaDeNegocio,
                        "Elige primero tu rubro: los servicios pertenecen a un rubro."));

                var tope = await suscripciones.LimiteDeServiciosAsync(mio.Valor!.Id);

                return Respuestas.DeResultado(await catalogo.ReemplazarServiciosAsync(
                    new ReemplazarServiciosCmd(
                        mio.Valor!.Id, rubroId.Value, tope, cmd.Servicios)));
            })
            .ConValidacion<ReemplazarServiciosHttpCmd>()
            .RequireRateLimiting(Tasa.General)
            .WithName("API-20");
    }
}
