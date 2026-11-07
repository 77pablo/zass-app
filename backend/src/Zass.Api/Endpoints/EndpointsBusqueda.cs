using System.Security.Claims;
using Zass.Api.Configuracion;
using Zass.Aplicacion.Busqueda;
using Zass.Aplicacion.Catalogo;
using Zass.Aplicacion.Identidad;
using Zass.Dominio.Comun;

namespace Zass.Api.Endpoints;

/// <summary>
/// API-01, tal como la pide la secuencia PR-02:
/// <c>GET /api/busqueda?rubro=corretaje&amp;lat=&amp;lon=&amp;radio=10</c>.
///
/// El rubro va por <b>codigo</b> y no por identificador: un enlace a una busqueda se
/// comparte y se lee, y el numero ademas ataria las URLs publicas al orden en que se
/// sembro el catalogo.
/// </summary>
public sealed record BusquedaHttpQuery(
    string? Rubro,
    int? Servicio,
    double? Lat,
    double? Lon,
    int? Zona,
    short? Radio,
    bool? Verificados,
    decimal? Calificacion,
    string? Cursor);

public static class EndpointsBusqueda
{
    public static IEndpointRouteBuilder MapearBusqueda(this IEndpointRouteBuilder app)
    {
        var grupo = app.MapGroup("/api");

        // API-01. Publico y sin sesion: la precondicion P1 de CU-08 dice que un visitante
        // sin cuenta ejecuta el caso completo. Es el endpoint mas llamado del producto.
        grupo.MapGet("/busqueda", async (
                [AsParameters] BusquedaHttpQuery q,
                IServicioBusqueda busqueda,
                IServicioCatalogo catalogo,
                IServicioIdentidad identidad,
                ClaimsPrincipal quien) =>
            {
                // El codigo se resuelve a identificador aqui, en el borde. El servicio
                // sigue validando el rubro por su cuenta: no todos sus clientes van a
                // entrar por HTTP.
                var rubro = await catalogo.RubroPorCodigoAsync(q.Rubro ?? "");
                if (rubro is null)
                    return Respuestas.Problema(Resultado.Falla(
                        Fallas.Validacion, "Ese rubro no existe."));

                // Flujo 4F de CU-08: un prestador que busca su rubro se ve, y sabe que es
                // el. Sin sesion esto es nulo y nadie queda marcado.
                Guid? miPerfil = null;
                var usuarioId = quien.UsuarioId();
                if (usuarioId is not null)
                    miPerfil = await identidad.PerfilPrestadorIdAsync(usuarioId.Value);

                return Respuestas.DeResultado(await busqueda.BuscarAsync(
                    new CriteriosDeBusqueda(
                        RubroId: rubro.Id,
                        ServicioId: q.Servicio,
                        Latitud: q.Lat,
                        Longitud: q.Lon,
                        ZonaId: q.Zona,
                        RadioKm: q.Radio ?? LimitesDeBusqueda.RadioPorOmisionKm,
                        SoloVerificados: q.Verificados ?? false,
                        CalificacionMinima: q.Calificacion,
                        Cursor: q.Cursor,
                        QuienBuscaPerfilId: miPerfil)));
            })
            .ConValidacion<BusquedaHttpQuery>()
            .RequireRateLimiting(Tasa.Publico)
            .WithName("API-01");

        return app;
    }
}
