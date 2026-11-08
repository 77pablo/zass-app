using Npgsql;

namespace Zass.Api.Configuracion;

/// <summary>
/// Revisa la configuración al arrancar y falla con una lista clara de lo que está mal.
///
/// Es una función pura (configuración → lista de problemas) para poder probarla sin levantar
/// nada. NUNCA copia un valor de la configuración al mensaje: la cadena de conexión lleva la
/// contraseña y la clave de cifrado es un secreto, y los registros de Render los lee más gente
/// de la que debería leer secretos.
/// </summary>
public static class ValidacionDeArranque
{
    public static IReadOnlyList<string> Validar(IConfiguration configuracion, bool esProductivo)
    {
        var problemas = new List<string>();

        // --- Cadena de conexión ---
        var cadena = configuracion["Zass:CadenaConexion"];
        if (string.IsNullOrWhiteSpace(cadena))
        {
            problemas.Add("Falta Zass:CadenaConexion (variable Zass__CadenaConexion).");
        }
        else
        {
            try
            {
                var c = new NpgsqlConnectionStringBuilder(cadena);
                if (string.IsNullOrWhiteSpace(c.Host))
                    problemas.Add("Zass:CadenaConexion no indica Host.");

                // En producción la base está en otra máquina, a través de Internet: sin SSL
                // obligatorio, «Prefer» (el valor por omisión) acepta degradarse a texto plano.
                if (esProductivo && c.SslMode is not (SslMode.Require or SslMode.VerifyCA or SslMode.VerifyFull))
                    problemas.Add("En producción Zass:CadenaConexion debe llevar «SSL Mode=Require» (o VerifyFull).");
            }
            catch (Exception)
            {
                // Sin ex.Message: ese texto puede citar un trozo de la cadena. Npgsql NO
                // entiende la forma postgresql://usuario:clave@host/base que entrega Neon.
                problemas.Add("Zass:CadenaConexion no tiene el formato «Clave=valor;Clave=valor» de Npgsql "
                    + "(una URL postgresql:// no sirve: conviértela a Host=...;Database=...;Username=...;Password=...).");
            }
        }

        // --- Clave de cifrado de direcciones: 32 bytes en Base64 ---
        var clave = configuracion["Zass:ClaveDireccionesBase64"];
        if (string.IsNullOrWhiteSpace(clave))
        {
            problemas.Add("Falta Zass:ClaveDireccionesBase64 (32 bytes en Base64).");
        }
        else
        {
            try
            {
                var bytes = Convert.FromBase64String(clave);
                if (bytes.Length != 32)
                    problemas.Add($"Zass:ClaveDireccionesBase64 tiene {bytes.Length} bytes y necesita 32.");
            }
            catch (FormatException)
            {
                problemas.Add("Zass:ClaveDireccionesBase64 no es Base64 válido.");
            }
        }

        // --- HTTPS ---
        // Por omisión es true. Apagarlo en producción deja salir las cookies de sesión sin la
        // marca Secure; solo se permite apagarlo en desarrollo, y explícitamente.
        var https = configuracion["Zass:ExigirHttps"];
        if (https is not null)
        {
            if (!bool.TryParse(https, out var exigir))
                problemas.Add("Zass:ExigirHttps debe ser true o false.");
            else if (esProductivo && !exigir)
                problemas.Add("En producción Zass:ExigirHttps no puede ser false.");
        }

        // --- Proxies de confianza ---
        foreach (var entrada in ProxiesDeConfianza.Entradas(configuracion))
            if (ProxiesDeConfianza.Interpretar(entrada, out var error) is null)
                problemas.Add($"{ProxiesDeConfianza.ClaveRedes}: {error}");

        var saltos = configuracion[ProxiesDeConfianza.ClaveSaltos];
        if (saltos is not null && (!int.TryParse(saltos, out var n) || n < 1 || n > 5))
            problemas.Add($"{ProxiesDeConfianza.ClaveSaltos} debe ser un entero de 1 a 5.");

        return problemas;
    }

    /// <summary>Lanza con TODOS los problemas juntos: arreglarlos de a uno es una tarde perdida.</summary>
    public static void Exigir(IConfiguration configuracion, bool esProductivo)
    {
        var problemas = Validar(configuracion, esProductivo);
        if (problemas.Count == 0) return;

        throw new InvalidOperationException(
            "La API no arranca: configuración inválida.\n - " + string.Join("\n - ", problemas));
    }
}
