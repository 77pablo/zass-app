using System.Security.Cryptography;
using System.Text;

namespace Zass.Api.Configuracion;

/// <summary>
/// P-11: la API solo atiende lo que llega por la Pages Function de Cloudflare.
///
/// <para>
/// El problema: la URL de Render está en el repositorio, que es público. Quien la conozca le pide
/// a la API directamente y se salta todo lo que hace la Function: el modo mantenimiento
/// (<c>frontend/functions/_middleware.js</c>) y la IP real del visitante. Y como Render también
/// está detrás de Cloudflare, cualquiera con su propia cuenta de Cloudflare sale desde rangos que
/// están en <see cref="ProxiesDeConfianza"/> y puede escribir el <c>X-Forwarded-For</c> que quiera.
/// </para>
///
/// <para>
/// La defensa: un secreto que solo conocen las dos puntas. Las Functions lo mandan en la cabecera
/// <see cref="Cabecera"/> (variable <c>SECRETO_ORIGEN</c> de Cloudflare Pages); la API, si tiene
/// <see cref="Clave"/> configurada, responde <b>403</b> a toda petición que no lo traiga exacto.
/// </para>
///
/// Reglas:
/// <list type="number">
/// <item>Sin la opción NO se registra nada: la API se comporta exactamente como antes. Así no se
/// rompe ni el desarrollo ni el CI, ni producción mientras Pablo no ponga las dos variables.</item>
/// <item>Exentas: <c>/api/salud</c> y lo que cuelga de ella (las sondas de Render y el flujo de
/// despertar) y las dos rutas que llama Flow: <c>/api/pagos/aviso</c> (API-70, servidor a
/// servidor) y <c>/api/pagos/vuelta</c> (API-75, el navegador que vuelve de flow.cl). Ver
/// <see cref="EstaExenta"/>.</item>
/// <item>Comparación en tiempo constante, sobre el SHA-256 de cada lado: ni el tiempo ni el largo
/// dicen nada del secreto.</item>
/// <item>El valor NUNCA se escribe en un registro ni en un mensaje de error.</item>
/// </list>
/// </summary>
public static class SecretoDeOrigen
{
    public const string Clave = "Zass:SecretoDeOrigen";
    public const string Cabecera = "X-Origen-Zass";

    /// <summary>32 caracteres: lo que da <c>openssl rand -hex 16</c>, como mínimo.</summary>
    public const int LargoMinimo = 32;

    /// <summary>El secreto configurado, o nulo si no hay (vacío o solo espacios cuenta como no hay).</summary>
    public static string? Leer(IConfiguration configuracion)
    {
        var valor = configuracion[Clave];
        return string.IsNullOrWhiteSpace(valor) ? null : valor;
    }

    /// <summary>
    /// Lo que no pasa por la Function y aun así tiene que llegar.
    ///
    /// <para>
    /// <b>Salud:</b> Render consulta <c>/api/salud/lista</c> directo al contenedor
    /// (<c>healthCheckPath</c> de render.yaml). Si se le exigiera el secreto, Render daría el
    /// servicio por caído y no terminaría ningún despliegue.
    /// </para>
    /// <para>
    /// <b>Flow:</b> la URL del aviso es una variable de Render (<c>Zass__Flow__UrlAvisos</c>), no
    /// está en el repositorio, así que no se puede saber desde aquí si apunta a zass.cl o a
    /// onrender.com. Y aunque apuntara a zass.cl, mientras el sitio esté en mantenimiento la
    /// Function le respondería 503 a Flow. Por eso se dejan pasar, y no es un agujero: ninguna de
    /// las dos cree lo que trae la petición; el adaptador le pregunta el estado a Flow con nuestra
    /// firma. Lo que exponen sin el secreto es exactamente lo que exponían antes.
    /// </para>
    /// Se compara por segmentos y sin distinguir mayúsculas, igual que el enrutador: lo exento no
    /// puede ser más amplio que la ruta a la que se llega.
    /// </summary>
    public static bool EstaExenta(PathString ruta) =>
        ruta.StartsWithSegments("/api/salud", StringComparison.OrdinalIgnoreCase)
        || ruta.Equals("/api/pagos/aviso", StringComparison.OrdinalIgnoreCase)
        || ruta.Equals("/api/pagos/aviso/", StringComparison.OrdinalIgnoreCase)
        || ruta.Equals("/api/pagos/vuelta", StringComparison.OrdinalIgnoreCase)
        || ruta.Equals("/api/pagos/vuelta/", StringComparison.OrdinalIgnoreCase);

    /// <summary>Problemas de forma del secreto (para la validación de arranque). Nunca cita el valor.</summary>
    public static IEnumerable<string> Problemas(IConfiguration configuracion)
    {
        var secreto = Leer(configuracion);
        if (secreto is null) yield break;

        if (secreto.Length < LargoMinimo)
            yield return $"{Clave} tiene menos de {LargoMinimo} caracteres (genéralo con «openssl rand -hex 32»).";

        // Va en una cabecera HTTP: solo ASCII visible, sin espacios. Uno con espacios o tildes
        // llegaría recortado o rechazado y la API daría 403 a todo el sitio.
        if (secreto.Any(c => c < '!' || c > '~'))
            yield return $"{Clave} solo puede tener caracteres ASCII visibles, sin espacios.";
    }

    /// <summary>
    /// En producción, sin el secreto, la API sigue arrancando pero lo dice en el registro.
    /// No se exige a propósito: exigirlo tumbaría producción el día que se despliegue este
    /// código, antes de que Cloudflare mande la cabecera. Cuándo exigirlo lo decide Pablo.
    /// </summary>
    public static string? Advertencia(IConfiguration configuracion, bool esProductivo) =>
        esProductivo && Leer(configuracion) is null
            ? $"{Clave} no está configurado: la API acepta peticiones que no pasan por Cloudflare "
              + "(cualquiera que conozca la URL de Render se salta el mantenimiento y puede falsear su IP). "
              + "Ver DESPLIEGUE-API.md, sección 4."
            : null;

    /// <summary>
    /// Registra el filtro solo si hay secreto. Va después de las cabeceras de seguridad y del
    /// HSTS (el 403 las lleva) y antes del limitador, la autenticación y los endpoints.
    /// </summary>
    public static IApplicationBuilder UsarSecretoDeOrigen(this IApplicationBuilder app, IConfiguration configuracion)
    {
        var secreto = Leer(configuracion);
        if (secreto is null) return app;

        var esperado = SHA256.HashData(Encoding.UTF8.GetBytes(secreto));

        return app.Use(async (contexto, siguiente) =>
        {
            if (EstaExenta(contexto.Request.Path) || Coincide(contexto.Request.Headers[Cabecera], esperado))
            {
                await siguiente();
                return;
            }

            // Sin cuerpo propio: UseStatusCodePages lo completa como problem+json, igual que
            // cualquier otro error de la API. No se dice qué faltó.
            contexto.Response.StatusCode = StatusCodes.Status403Forbidden;
        });
    }

    /// <summary>Exactamente UN valor, y que su SHA-256 sea el esperado (en tiempo constante).</summary>
    private static bool Coincide(Microsoft.Extensions.Primitives.StringValues valores, byte[] esperado)
    {
        if (valores.Count != 1 || valores[0] is not { Length: > 0 } valor) return false;
        var recibido = SHA256.HashData(Encoding.UTF8.GetBytes(valor));
        return CryptographicOperations.FixedTimeEquals(recibido, esperado);
    }
}
