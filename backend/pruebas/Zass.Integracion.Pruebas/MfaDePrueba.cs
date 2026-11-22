using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Zass.Aplicacion.Identidad;
using Zass.Infraestructura.Seguridad;

namespace Zass.Integracion.Pruebas;

/// <summary>
/// MFA obligatorio en el panel (22-nov-2026): las pruebas que usan /api/interno necesitan una
/// cuenta interna CON la verificacion en dos pasos activa. Esto la deja activa en la base, con el
/// mismo cifrado que la API de pruebas (la clave de 32 bytes 0..31 de <see cref="Aplicacion"/>).
///
/// <para>
/// Lo que el MFA mismo hace (alta, segundo paso, bloqueo, reuso) NO se prueba con esto: lo prueban
/// ApiPruebas.Mfa y las pruebas de aplicacion, por la API. Esto es solo andamiaje.
/// </para>
/// </summary>
public static class MfaDePrueba
{
    public static readonly CifradoAesGcm Cifrado =
        new(Enumerable.Range(0, 32).Select(i => (byte)i).ToArray());

    private const string AlfabetoRespaldo = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

    /// <summary>Deja MFA activo con un secreto nuevo y devuelve el secreto (Base32).</summary>
    public static async Task<string> ActivarAsync(string correo)
    {
        var secreto = Totp.NuevoSecreto();
        using var ctx = ContextoDePrueba.Crear();
        var u = await ctx.Usuarios.SingleAsync(x => x.Correo == correo);
        u.MfaSecretoCifrado = Cifrado.Cifrar(secreto);
        u.MfaActivadoEn = DateTime.UtcNow;
        u.MfaCodigosRespaldo = Array.Empty<string>();
        await ctx.SaveChangesAsync();
        return secreto;
    }

    /// <summary>Agrega un codigo de respaldo nuevo a la cuenta y lo devuelve (para entrar sin esperar 30 s).</summary>
    public static async Task<string> NuevoCodigoDeRespaldoAsync(string correo)
    {
        var codigo = new string(Enumerable.Range(0, 10)
            .Select(_ => AlfabetoRespaldo[RandomNumberGenerator.GetInt32(AlfabetoRespaldo.Length)]).ToArray());
        using var ctx = ContextoDePrueba.Crear();
        var u = await ctx.Usuarios.SingleAsync(x => x.Correo == correo);
        var hash = ServicioMfa.HashDeCodigoDeRespaldo(new GeneradorSecretos(), u.Id, codigo);
        u.MfaCodigosRespaldo = (u.MfaCodigosRespaldo ?? Array.Empty<string>()).Append(hash).ToArray();
        await ctx.SaveChangesAsync();
        return codigo;
    }

    /// <summary>
    /// Inicia sesion como lo haria la pantalla: si API-09 pide el segundo paso, lo da con un codigo
    /// de respaldo. Y si la cuenta tiene un rol del panel y aun no tiene MFA, se lo deja activo
    /// DESPUES de entrar (la sesion ya abierta sigue sirviendo, y /api/interno deja pasar).
    /// </summary>
    public static async Task<HttpResponseMessage> EntrarAsync(HttpClient cliente, string correo, string contrasena)
    {
        var r = await cliente.PostAsJsonAsync("/api/auth/sesion", new { correo, contrasena });
        if (!r.IsSuccessStatusCode) return r;

        var cuerpo = await r.Content.ReadFromJsonAsync<JsonElement>();
        if (cuerpo.TryGetProperty("mfaRequerido", out var pide) && pide.GetBoolean())
        {
            var codigo = await NuevoCodigoDeRespaldoAsync(correo);
            r = await cliente.PostAsJsonAsync("/api/auth/sesion/mfa",
                new { desafio = cuerpo.GetProperty("desafio").GetString(), codigo });
            return r;
        }

        using var ctx = ContextoDePrueba.Crear();
        var u = await ctx.Usuarios.AsNoTracking().Include(x => x.Roles).ThenInclude(x => x.Rol)
            .SingleAsync(x => x.Correo == correo);
        if (u.TieneRolInterno && !u.MfaActivo) await ActivarAsync(correo);
        return r;
    }
}
