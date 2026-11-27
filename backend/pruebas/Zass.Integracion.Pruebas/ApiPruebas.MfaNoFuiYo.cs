using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Zass.Dominio.Comun;
using Zass.Dominio.Identidad;

namespace Zass.Integracion.Pruebas;

/// <summary>
/// Revision de seguridad del 25-nov-2026, punto 2: «No fui yo» (API-85) y el administrador que quita
/// la verificacion (API-86), por HTTP y contra la base real.
/// </summary>
public partial class ApiPruebas
{
    /// <summary>Lo que llevaria el correo de activacion: el token «No fui yo» de la carga encolada.</summary>
    private static async Task<string> TokenNoFuiYoAsync(string correo)
    {
        using var ctx = ContextoDePrueba.Crear();
        var cuerpo = await ctx.Notificaciones
            .Where(n => n.Destinatario!.Correo == correo && n.Tipo == TipoNotificacion.MfaActivado)
            .Select(n => n.Cuerpo).SingleAsync();
        return Zass.Aplicacion.Puertos.CargaDeAviso.LeerToken(cuerpo)!;
    }

    /// <summary>Activa la verificacion por la API (asi sale el correo con el enlace) y cierra la sesion.</summary>
    private async Task<string> CuentaQueActivoPorLaApiAsync()
    {
        var correo = NuevoCorreo();
        await RegistrarAsync(correo);
        await VerificarAsync(correo);
        Assert.Equal(HttpStatusCode.OK, (await EntrarAsync(correo, ClaveBuena)).StatusCode);
        var alta = await PostConCsrfAsync("/api/mi/mfa/alta");
        var secreto = (await alta.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("secreto").GetString()!;
        var conf = await PostConCsrfAsync("/api/mi/mfa/confirmar", new { contrasena = ClaveBuena, codigo = CodigoTotp(secreto) });
        Assert.Equal(HttpStatusCode.OK, conf.StatusCode);
        return correo;
    }

    [Fact]
    public async Task No_fui_yo_por_la_api_quita_la_verificacion_y_lleva_a_crear_otra_contrasena()
    {
        var correo = await CuentaQueActivoPorLaApiAsync();
        var token = await TokenNoFuiYoAsync(correo);
        var antes = await CuentaEnBaseAsync(correo);
        Assert.Equal(64, antes.MfaRevocacionHash!.Length);   // solo el hash, nunca el token

        // Sin CSRF: quien lo usa no puede entrar a su cuenta, asi que tampoco tiene sesion.
        var r =await _cliente.PostAsJsonAsync("/api/auth/mfa/no-fui-yo", new { token });
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var tokenRestablecer = (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("tokenRestablecer").GetString()!;

        var despues = await CuentaEnBaseAsync(correo);
        Assert.False(despues.MfaActivo);
        Assert.Null(despues.MfaRevocacionHash);
        Assert.Equal(1, await AuditadasAsync(despues.Id, "mfa_quitado_no_fui_yo"));

        // La contraseña que conocia el intruso ya no sirve.
        Assert.Equal(HttpStatusCode.Unauthorized, (await EntrarAsync(correo, ClaveBuena)).StatusCode);

        var restablecer = await _cliente.PostAsJsonAsync("/api/auth/contrasena/restablecer",
            new { token = tokenRestablecer, contrasenaNueva = "otra-contrasena-nueva-9" });
        Assert.True(restablecer.IsSuccessStatusCode, restablecer.StatusCode.ToString());
        Assert.True(TraeCookieDeSesion(await EntrarAsync(correo, "otra-contrasena-nueva-9")));

        // Una segunda vez, el mismo enlace no sirve.
        var otra = await _cliente.PostAsJsonAsync("/api/auth/mfa/no-fui-yo", new { token });
        Assert.Equal(HttpStatusCode.NotFound, otra.StatusCode);
    }

    [Fact]
    public async Task No_fui_yo_con_un_token_inventado_no_toca_nada()
    {
        var correo = await CuentaQueActivoPorLaApiAsync();

        var r = await _cliente.PostAsJsonAsync("/api/auth/mfa/no-fui-yo", new { token = "inventado-" + Guid.NewGuid() });

        Assert.Equal(HttpStatusCode.NotFound, r.StatusCode);
        Assert.True((await CuentaEnBaseAsync(correo)).MfaActivo);
    }

    [Fact]
    public async Task Un_administrador_quita_la_verificacion_por_correo_con_motivo()
    {
        var correo = await CuentaQueActivoPorLaApiAsync();
        var (admin, adminId) = await UnaCuentaAsync("buscador");
        using (var ctx = ContextoDePrueba.Crear())
        {
            ctx.UsuarioRoles.Add(new UsuarioRol { UsuarioId = adminId, RolId = RolAdministradorId });
            await ctx.SaveChangesAsync();
        }
        await IniciarSesionAsync(admin);

        var r = await PostConCsrfAsync("/api/interno/cuentas/quitar-mfa",
            new { correo, motivo = "Escribió a soporte: otra persona activó la verificación." });

        Assert.True(r.IsSuccessStatusCode, r.StatusCode + " " + await r.Content.ReadAsStringAsync());
        var cuenta = await CuentaEnBaseAsync(correo);
        Assert.False(cuenta.MfaActivo);
        Assert.Equal(1, await AuditadasAsync(cuenta.Id, "mfa_quitado_por_administrador"));
        using var lectura = ContextoDePrueba.Crear();
        Assert.Equal(1, await lectura.Notificaciones.CountAsync(n =>
            n.DestinatarioId == cuenta.Id && n.Tipo == TipoNotificacion.MfaQuitadoPorAdministrador));
    }

    [Fact]
    public async Task Quien_no_es_administrador_no_puede_quitar_la_verificacion()
    {
        var correo = await CuentaQueActivoPorLaApiAsync();
        var (otro, _) = await UnaCuentaAsync("buscador");
        await IniciarSesionAsync(otro);

        var r = await PostConCsrfAsync("/api/interno/cuentas/quitar-mfa",
            new { correo, motivo = "Motivo suficientemente largo." });

        Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
        Assert.True((await CuentaEnBaseAsync(correo)).MfaActivo);
    }
}
