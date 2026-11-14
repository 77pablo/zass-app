using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Zass.Api.Configuracion;

namespace Zass.Arquitectura.Pruebas;

/// <summary>
/// La cabecera Content-Security-Policy que emite la API, mirada sin levantar la API: se arma la
/// tuberia con el unico middleware de cabeceras y se lee lo que deja en la respuesta.
///
/// Por que vive aqui y no en integracion: es una regla sobre el CODIGO (que dominios se nombran en
/// la politica), no sobre el comportamiento de un endpoint. Asi corre en cada compilacion, sin base
/// de datos, y nadie puede reintroducir un dominio muerto sin que algo se ponga rojo.
///
/// El dominio que se vigila, `*.blob.core.windows.net`, era almacenamiento de Azure: el plan de
/// despliegue original. La realidad medida es Cloudflare Pages + Render + Neon, y la API no sirve
/// ninguna imagen de Azure. Un dominio de mas en `img-src` no es decorativo: es un origen desde el
/// que el navegador aceptaria cargar imagenes.
/// </summary>
public class CabecerasDeSeguridadPruebas
{
    private static string Politica()
    {
        var servicios = new ServiceCollection().BuildServiceProvider();
        var constructor = new ApplicationBuilder(servicios);
        constructor.UsarCabecerasDeSeguridad();

        var contexto = new DefaultHttpContext { RequestServices = servicios };
        constructor.Build()(contexto).GetAwaiter().GetResult();

        return contexto.Response.Headers["Content-Security-Policy"].ToString();
    }

    /// <summary>La directiva pedida, sin el `;` final ni espacios sobrantes.</summary>
    private static string Directiva(string nombre) =>
        Politica()
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Single(d => d.StartsWith(nombre + " ", StringComparison.Ordinal))[(nombre.Length + 1)..]
            .Trim();

    [Fact]
    public void La_politica_de_contenido_no_nombra_el_almacenamiento_de_Azure()
    {
        var csp = Politica();

        Assert.False(string.IsNullOrWhiteSpace(csp), "La respuesta salio sin Content-Security-Policy.");
        Assert.DoesNotContain("blob.core.windows.net", csp, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("windows.net", csp, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("azure", csp, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Img_src_permite_solo_el_propio_origen_y_data()
    {
        // Lo minimo que funciona hoy: la API responde JSON y, en desarrollo, Swagger UI, que toma
        // sus imagenes del propio origen y alguna incrustada como `data:`. Nada mas carga imagenes.
        Assert.Equal("'self' data:", Directiva("img-src"));
    }

    [Fact]
    public void Ninguna_directiva_permite_un_origen_externo()
    {
        var externos = Politica()
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(d => d.Contains("://", StringComparison.Ordinal))
            .ToList();

        Assert.True(externos.Count == 0,
            "La politica nombra origenes externos: " + string.Join(" | ", externos));
    }
}
