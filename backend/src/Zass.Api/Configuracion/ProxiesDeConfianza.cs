using System.Net;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Options;
using RedIp = Microsoft.AspNetCore.HttpOverrides.IPNetwork;

namespace Zass.Api.Configuracion;

/// <summary>
/// Confiar en X-Forwarded-For SOLO cuando la petición llega desde un proxy que nosotros
/// listamos.
///
/// El problema que resuelve: detrás de Cloudflare/Render, `RemoteIpAddress` es la del proxy, y
/// el limitador de tasa (por IP) metería a toda la ciudad en un solo contador. La solución
/// ingenua, aceptar X-Forwarded-For de cualquiera, es peor que el problema: el atacante
/// escribe una IP distinta en cada petición y evade el límite (y falsea la auditoría). Por eso:
///
///  1. Por omisión (lista vacía) NO se registra el middleware: ninguna cabecera X-Forwarded-*
///     se procesa. Es el comportamiento de siempre y el único seguro sin proxy.
///  2. Con lista, solo se leen las cabeceras si la conexión directa viene de una de esas
///     redes. El middleware recorre X-Forwarded-For de derecha a izquierda y se detiene en la
///     primera dirección que NO es de confianza: esa es la IP del cliente. Lo que el atacante
///     escriba a la izquierda no se lee nunca.
///  3. Nunca se aceptan «todas» (0.0.0.0/0 o ::/0): sería el agujero con otro nombre.
/// </summary>
public static class ProxiesDeConfianza
{
    public const string ClaveRedes = "Zass:ProxiesDeConfianza";
    public const string ClaveSaltos = "Zass:SaltosDeProxy";

    /// <summary>
    /// Saltos que se permiten recorrer. Con la cadena prevista (visitante, Pages Function de
    /// Cloudflare, balanceador de Render, contenedor) la API ve DOS proxies delante de ella:
    /// el balanceador de Render (conexión directa) y la salida de Cloudflare (que Render añade
    /// a X-Forwarded-For). Dos cubre esa cadena y nada más: un salto de más sería una IP más
    /// que el atacante podría hacer pasar por «de confianza» si alguna vez una red listada
    /// fuese demasiado ancha. El límite es una segunda barrera, no la primera: la primera es
    /// la lista de redes.
    /// </summary>
    public const int SaltosPorOmision = 2;

    /// <summary>Lee la lista (texto separado por comas/espacios, o elementos de una lista).</summary>
    public static IReadOnlyList<string> Entradas(IConfiguration configuracion)
    {
        var seccion = configuracion.GetSection(ClaveRedes);
        var textos = new List<string>();
        if (!string.IsNullOrWhiteSpace(seccion.Value)) textos.Add(seccion.Value);
        textos.AddRange(seccion.GetChildren()
            .Select(h => h.Value)
            .Where(v => !string.IsNullOrWhiteSpace(v))!);

        return textos
            .SelectMany(t => t.Split(new[] { ',', ';', ' ', '\n', '\r', '\t' },
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .ToList();
    }

    public static bool Activos(IConfiguration configuracion) => Entradas(configuracion).Count > 0;

    /// <summary>Interpreta «ip» o «ip/prefijo». Devuelve null con el motivo si no sirve.</summary>
    public static RedIp? Interpretar(string texto, out string? error)
    {
        error = null;
        var partes = texto.Split('/');
        if (partes.Length > 2 || !IPAddress.TryParse(partes[0], out var ip))
        {
            error = $"«{texto}» no es una IP ni una red CIDR válida.";
            return null;
        }

        var maximo = ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork ? 32 : 128;
        var prefijo = maximo;
        if (partes.Length == 2 && (!int.TryParse(partes[1], out prefijo) || prefijo < 0 || prefijo > maximo))
        {
            error = $"«{texto}» tiene un prefijo fuera de 0 a {maximo}.";
            return null;
        }

        if (prefijo == 0)
        {
            error = $"«{texto}» confiaría en cualquier origen, y eso deja falsear la IP del visitante.";
            return null;
        }

        return new RedIp(ip, prefijo);
    }

    public static int Saltos(IConfiguration configuracion) =>
        int.TryParse(configuracion[ClaveSaltos], out var n) ? n : SaltosPorOmision;

    /// <summary>
    /// Registra las opciones del middleware. Se leen al RESOLVER y no al registrar, igual que
    /// las demás opciones: la configuración de las pruebas y de Key Vault llega después.
    /// </summary>
    public static IServiceCollection AgregarProxiesDeConfianza(this IServiceCollection servicios) =>
        servicios.AddSingleton<IConfigureOptions<ForwardedHeadersOptions>>(sp =>
            new ConfigurarCabeceras(sp.GetRequiredService<IConfiguration>()));

    /// <summary>
    /// Activa el middleware solo si hay redes de confianza. Va PRIMERO en la tubería: el HSTS,
    /// la redirección a HTTPS y el limitador deben ver ya la IP y el esquema corregidos.
    /// </summary>
    public static IApplicationBuilder UsarProxiesDeConfianza(
        this IApplicationBuilder app, IConfiguration configuracion) =>
        Activos(configuracion) ? app.UseForwardedHeaders() : app;

    private sealed class ConfigurarCabeceras(IConfiguration configuracion)
        : IConfigureOptions<ForwardedHeadersOptions>
    {
        public void Configure(ForwardedHeadersOptions opciones)
        {
            // Solo For y Proto. X-Forwarded-Host no: el sitio no genera enlaces con el host
            // de la petición y aceptarlo ampliaría lo que un proxy comprometido puede decir.
            opciones.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;

            // Los valores por omisión de ASP.NET confían en loopback. Aquí la lista es
            // EXACTAMENTE la configurada: lo que no esté escrito, no es de confianza.
            opciones.KnownProxies.Clear();
            opciones.KnownNetworks.Clear();
            foreach (var entrada in Entradas(configuracion))
            {
                var red = Interpretar(entrada, out var error)
                    ?? throw new InvalidOperationException($"{ClaveRedes}: {error}");
                opciones.KnownNetworks.Add(red);
            }

            opciones.ForwardLimit = Saltos(configuracion);
        }
    }
}
