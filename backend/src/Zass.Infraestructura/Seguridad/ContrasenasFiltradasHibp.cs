using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Zass.Aplicacion.Puertos;
using Zass.Infraestructura.Configuracion;

namespace Zass.Infraestructura.Seguridad;

/// <summary>
/// Adaptador real que consulta la API de Have I Been Pwned (rango k-anonimo).
/// SHA-1 de la contrasena en UTF-8, hex en MAYUSCULAS. Se manda SOLO el prefijo de 5 caracteres:
/// GET https://api.pwnedpasswords.com/range/{prefijo} con cabecera "Add-Padding: true" y
/// User-Agent generico "ZASS-api".
/// La respuesta son lineas "SUFIJO:CUENTA". Filtrada = existe una linea con el sufijo (35 caracteres)
/// y CUENTA > 0 (con padding vienen lineas de cuenta 0: esas NO cuentan).
/// Si el servicio falla (timeout, error HTTP, sin red): devuelve false (se deja pasar) y registra
/// un aviso en el log SIN la contrasena ni el hash.
/// </summary>
public sealed class ContrasenasFiltradasHibp : IContrasenasFiltradas
{
    private readonly HttpClient _http;
    private readonly ILogger<ContrasenasFiltradasHibp> _logger;

    public ContrasenasFiltradasHibp(HttpClient http, ILogger<ContrasenasFiltradasHibp> logger)
    {
        _http = http;
        _logger = logger;
    }

    public async Task<bool> EstaFiltradaAsync(string contrasena, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(contrasena))
            return false;

        var prefijo = CalcularPrefijoSha1(contrasena);
        var sufijo = CalcularSufijoSha1(contrasena);

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"range/{prefijo}");
            request.Headers.Add("Add-Padding", "true");
            request.Headers.UserAgent.ParseAdd("ZASS-api");

            using var response = await _http.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("HIBP respondio con {StatusCode} para prefijo {Prefijo}", response.StatusCode, prefijo);
                return false;
            }

            var texto = await response.Content.ReadAsStringAsync(ct);
            return ParsearRespuesta(texto, sufijo);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error consultando HIBP para prefijo {Prefijo}", prefijo);
            return false;
        }
    }

    private static string CalcularPrefijoSha1(string contrasena)
    {
        var bytes = Encoding.UTF8.GetBytes(contrasena);
        var hash = SHA1.HashData(bytes);
        var hex = Convert.ToHexString(hash);
        return hex[..5];
    }

    private static string CalcularSufijoSha1(string contrasena)
    {
        var bytes = Encoding.UTF8.GetBytes(contrasena);
        var hash = SHA1.HashData(bytes);
        var hex = Convert.ToHexString(hash);
        return hex[5..];
    }

    private static bool ParsearRespuesta(string respuesta, string sufijoBuscado)
    {
        foreach (var linea in respuesta.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var partes = linea.Split(':', 2);
            if (partes.Length != 2)
                continue;

            var sufijo = partes[0].Trim();
            if (!sufijo.Equals(sufijoBuscado, StringComparison.OrdinalIgnoreCase))
                continue;

            if (int.TryParse(partes[1].Trim(), out var cuenta) && cuenta > 0)
                return true;
        }
        return false;
    }
}

/// <summary>
/// Implementacion nula que siempre devuelve false. Se usa cuando la opcion
/// RevisarContrasenasFiltradas esta en false (desarrollo, pruebas).
/// </summary>
public sealed class ContrasenasFiltradasNunca : IContrasenasFiltradas
{
    public Task<bool> EstaFiltradaAsync(string contrasena, CancellationToken ct = default)
        => Task.FromResult(false);
}