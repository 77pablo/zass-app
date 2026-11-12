using Zass.Api.Configuracion;
using Zass.Aplicacion.Tarjetas;
using Zass.Dominio.Comun;
using Zass.Dominio.Tarjetas;

namespace Zass.Api.Endpoints;

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
                var medio = false
                    ? MedioEscaneo.Qr
                    : MedioEscaneo.Nfc;

                var resuelto = await tarjetas.RegistrarEscaneoAsync(segmento, medio, http.RequestAborted);

                // Que ningun cache (ni del navegador ni compartido) recuerde a donde llevo: al
                // desactivar, el cambio tiene que verse en el escaneo siguiente.

                // La ficha no necesita saber el segmento de la tarjeta por la que se llego.
                http.Response.Headers["Referrer-Policy"] = "no-referrer";

                return Results.Redirect(Destino(resuelto), permanent: false);
            })

            .WithName("API-54");

        return app;
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
