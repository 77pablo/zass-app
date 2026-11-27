using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Zass.Aplicacion.Identidad;
using Zass.Dominio.Identidad;

namespace Zass.Integracion.Pruebas;

/// <summary>
/// Verificacion en dos pasos (decision de Pablo, 22-nov-2026): obligatoria para los roles del
/// panel interno y opcional para el resto. API-79 a API-83, por HTTP y contra la base real.
/// </summary>
public partial class ApiPruebas
{
    private static string CodigoTotp(string secreto) => Totp.Codigo(secreto, Totp.PasoDe(DateTime.UtcNow));

    private static bool TraeCookieDeSesion(HttpResponseMessage r) =>
        r.Headers.TryGetValues("Set-Cookie", out var v)
        && v.Any(c => c.StartsWith("zass_sesion") || c.StartsWith("zass_refresco"));

    private static async Task<string> DesafioDeAsync(HttpResponseMessage r)
    {
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var cuerpo = await r.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(cuerpo.GetProperty("mfaRequerido").GetBoolean());
        return cuerpo.GetProperty("desafio").GetString()!;
    }

    private Task<HttpResponseMessage> SegundoPasoAsync(string desafio, string codigo) =>
        _cliente.PostAsJsonAsync("/api/auth/sesion/mfa", new { desafio, codigo });

    /// <summary>Una cuenta verificada con MFA activo (puesto en la base). Sin sesion abierta.</summary>
    private async Task<(string Correo, string Secreto)> CuentaConMfaAsync()
    {
        var correo = NuevoCorreo();
        await RegistrarAsync(correo);
        await VerificarAsync(correo);
        return (correo, await MfaDePrueba.ActivarAsync(correo));
    }

    private static async Task<Usuario> CuentaEnBaseAsync(string correo)
    {
        using var ctx = ContextoDePrueba.Crear();
        return await ctx.Usuarios.AsNoTracking().SingleAsync(u => u.Correo == correo);
    }

    private static async Task<int> AuditadasAsync(Guid usuarioId, string accion)
    {
        using var ctx = ContextoDePrueba.Crear();
        return await ctx.Auditoria.CountAsync(a => a.Accion == accion && a.EntidadId == usuarioId);
    }

    /// <summary>
    /// El alta completa desde «Mi cuenta»: secreto y otpauth con emisor ZASS!, un codigo malo no
    /// activa, el bueno activa y entrega 10 codigos de respaldo. El secreto queda cifrado.
    /// </summary>
    [Fact]
    public async Task Mfa_alta_completa_por_la_api_entrega_qr_y_diez_codigos_y_guarda_el_secreto_cifrado()
    {
        var correo = NuevoCorreo();
        await RegistrarAsync(correo);
        await VerificarAsync(correo);
        Assert.Equal(HttpStatusCode.OK, (await EntrarAsync(correo, ClaveBuena)).StatusCode);

        var alta = await PostConCsrfAsync("/api/mi/mfa/alta");
        Assert.Equal(HttpStatusCode.OK, alta.StatusCode);
        var datos = await alta.Content.ReadFromJsonAsync<JsonElement>();
        var secreto = datos.GetProperty("secreto").GetString()!;
        Assert.StartsWith("otpauth://totp/ZASS%21:", datos.GetProperty("uriOtpauth").GetString());
        Assert.Contains("issuer=ZASS%21", datos.GetProperty("uriOtpauth").GetString());

        var mala = await PostConCsrfAsync("/api/mi/mfa/confirmar", new { contrasena = ClaveBuena, codigo = "000000" == CodigoTotp(secreto) ? "111111" : "000000" });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, mala.StatusCode);
        Assert.False((await CuentaEnBaseAsync(correo)).MfaActivo);

        // Con la contrasena mala no activa, aunque el codigo sea el bueno.
        var sinClave = await PostConCsrfAsync("/api/mi/mfa/confirmar", new { contrasena = "otra-contrasena-9", codigo = CodigoTotp(secreto) });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, sinClave.StatusCode);
        Assert.False((await CuentaEnBaseAsync(correo)).MfaActivo);

        var buena = await PostConCsrfAsync("/api/mi/mfa/confirmar", new { contrasena = ClaveBuena, codigo = CodigoTotp(secreto) });
        Assert.Equal(HttpStatusCode.OK, buena.StatusCode);
        var codigos = (await buena.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("codigos");
        Assert.Equal(10, codigos.GetArrayLength());

        var usuario = await CuentaEnBaseAsync(correo);
        Assert.True(usuario.MfaActivo);
        Assert.Equal(10, usuario.MfaCodigosRespaldo!.Length);
        Assert.DoesNotContain(codigos[0].GetString()!.Replace("-", ""), string.Join(",", usuario.MfaCodigosRespaldo));
        Assert.Equal(secreto, MfaDePrueba.Cifrado.Descifrar(usuario.MfaSecretoCifrado!, ServicioMfa.DatosAsociados(usuario.Id)));
        Assert.DoesNotContain(secreto, System.Text.Encoding.ASCII.GetString(usuario.MfaSecretoCifrado!));
        Assert.Equal(1, await AuditadasAsync(usuario.Id, "mfa_activado"));

        var estado = (await _cliente.GetFromJsonAsync<JsonElement>("/api/mi/mfa"));
        Assert.True(estado.GetProperty("activo").GetBoolean());
        Assert.Equal(10, estado.GetProperty("codigosDeRespaldoRestantes").GetInt32());
    }

    /// <summary>
    /// Con MFA, la contrasena correcta NO abre sesion: ni cookies ni /api/mi. Solo el segundo paso
    /// con un codigo TOTP correcto la abre.
    /// </summary>
    [Fact]
    public async Task Mfa_con_mfa_la_contrasena_sola_no_abre_sesion_y_el_segundo_paso_si()
    {
        var (correo, secreto) = await CuentaConMfaAsync();

        var primero = await EntrarAsync(correo, ClaveBuena);
        Assert.False(TraeCookieDeSesion(primero));
        var desafio = await DesafioDeAsync(primero);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _cliente.GetAsync("/api/mi/cuenta")).StatusCode);

        var segundo = await SegundoPasoAsync(desafio, CodigoTotp(secreto));
        Assert.Equal(HttpStatusCode.OK, segundo.StatusCode);
        Assert.True(TraeCookieDeSesion(segundo));
        Assert.Equal(HttpStatusCode.OK, (await _cliente.GetAsync("/api/mi/cuenta")).StatusCode);
    }

    /// <summary>Un codigo TOTP ya usado no sirve otra vez, ni dentro de sus 30 segundos.</summary>
    [Fact]
    public async Task Mfa_un_codigo_totp_usado_no_sirve_dos_veces()
    {
        var (correo, secreto) = await CuentaConMfaAsync();
        var codigo = CodigoTotp(secreto);

        Assert.Equal(HttpStatusCode.OK,
            (await SegundoPasoAsync(await DesafioDeAsync(await EntrarAsync(correo, ClaveBuena)), codigo)).StatusCode);
        await _cliente.DeleteAsync("/api/auth/sesion");

        var otraVez = await SegundoPasoAsync(await DesafioDeAsync(await EntrarAsync(correo, ClaveBuena)), codigo);
        Assert.Equal(HttpStatusCode.Unauthorized, otraVez.StatusCode);
        Assert.False(TraeCookieDeSesion(otraVez));
    }

    /// <summary>Un codigo de respaldo sirve una sola vez, y su uso queda auditado.</summary>
    [Fact]
    public async Task Mfa_un_codigo_de_respaldo_sirve_una_sola_vez()
    {
        var (correo, _) = await CuentaConMfaAsync();
        var respaldo = await MfaDePrueba.NuevoCodigoDeRespaldoAsync(correo);
        var conGuion = respaldo[..5] + "-" + respaldo[5..].ToLowerInvariant();

        Assert.Equal(HttpStatusCode.OK,
            (await SegundoPasoAsync(await DesafioDeAsync(await EntrarAsync(correo, ClaveBuena)), conGuion)).StatusCode);

        var otraVez = await SegundoPasoAsync(await DesafioDeAsync(await EntrarAsync(correo, ClaveBuena)), respaldo);
        Assert.Equal(HttpStatusCode.Unauthorized, otraVez.StatusCode);

        var usuario = await CuentaEnBaseAsync(correo);
        Assert.Empty(usuario.MfaCodigosRespaldo!);
        Assert.Equal(1, await AuditadasAsync(usuario.Id, "mfa_codigo_respaldo_usado"));
        Assert.Equal(1, await AuditadasAsync(usuario.Id, "mfa_fallido"));
    }

    /// <summary>El desafio es de un solo uso y vence a los 5 minutos.</summary>
    [Fact]
    public async Task Mfa_el_desafio_es_de_un_solo_uso_y_vence()
    {
        var (correo, _) = await CuentaConMfaAsync();

        var desafio = await DesafioDeAsync(await EntrarAsync(correo, ClaveBuena));
        Assert.Equal(HttpStatusCode.OK,
            (await SegundoPasoAsync(desafio, await MfaDePrueba.NuevoCodigoDeRespaldoAsync(correo))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await SegundoPasoAsync(desafio, await MfaDePrueba.NuevoCodigoDeRespaldoAsync(correo))).StatusCode);

        var vencido = await DesafioDeAsync(await EntrarAsync(correo, ClaveBuena));
        using (var ctx = ContextoDePrueba.Crear())
        {
            var u = await ctx.Usuarios.SingleAsync(x => x.Correo == correo);
            Assert.Equal(DateTime.UtcNow.AddMinutes(5), u.MfaDesafioExpiraEn!.Value, TimeSpan.FromMinutes(1));
            u.MfaDesafioExpiraEn = DateTime.UtcNow.AddSeconds(-1);
            await ctx.SaveChangesAsync();
        }
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await SegundoPasoAsync(vencido, await MfaDePrueba.NuevoCodigoDeRespaldoAsync(correo))).StatusCode);
    }

    /// <summary>
    /// Los fallos del segundo paso cuentan para el MISMO bloqueo de 30 intentos que la contrasena:
    /// 5 contrasenas malas + 5 codigos malos bloquean, y bloqueada ni la contrasena ni un codigo
    /// bueno abren sesion.
    /// </summary>
    [Fact]
    public async Task Mfa_los_fallos_del_segundo_paso_cuentan_para_el_mismo_bloqueo()
    {
        var (correo, _) = await CuentaConMfaAsync();

        // Mitad con la contrasena, mitad con el codigo: juntos llegan justo al limite.
        var mitad = ServicioSesion.IntentosParaBloquear / 2;
        await FallarAsync(correo, mitad);
        var desafio = await DesafioDeAsync(await EntrarAsync(correo, ClaveBuena));
        for (var i = 0; i < ServicioSesion.IntentosParaBloquear - mitad; i++)
            Assert.Equal(HttpStatusCode.Unauthorized, (await SegundoPasoAsync(desafio, "ZZZZZ-ZZZZZ")).StatusCode);

        var usuario = await CuentaEnBaseAsync(correo);
        Assert.True(usuario.EstaBloqueada(DateTime.UtcNow));
        Assert.Equal(1, await AuditadasAsync(usuario.Id, "cuenta_bloqueada_por_intentos"));

        Assert.Equal(HttpStatusCode.Unauthorized,
            (await SegundoPasoAsync(desafio, await MfaDePrueba.NuevoCodigoDeRespaldoAsync(correo))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await EntrarAsync(correo, ClaveBuena)).StatusCode);
    }

    /// <summary>
    /// La contrasena correcta con MFA NO reinicia el contador (si lo hiciera, se podrian probar
    /// codigos sin fin alternando con la contrasena). Treinta codigos malos seguidos bloquean.
    /// </summary>
    [Fact]
    public async Task Mfa_la_contrasena_correcta_no_reinicia_el_contador_de_codigos_malos()
    {
        var (correo, _) = await CuentaConMfaAsync();

        for (var i = 0; i < ServicioSesion.IntentosParaBloquear; i++)
        {
            var desafio = await DesafioDeAsync(await EntrarAsync(correo, ClaveBuena));
            Assert.Equal(HttpStatusCode.Unauthorized, (await SegundoPasoAsync(desafio, "ZZZZZ-ZZZZZ")).StatusCode);
        }

        Assert.True((await CuentaEnBaseAsync(correo)).EstaBloqueada(DateTime.UtcNow));
    }

    /// <summary>
    /// Panel: un verificador con sesion pero sin MFA recibe 403 `mfa_requerido` en /api/interno,
    /// pero SI puede activarlo. Activado, el mismo panel responde.
    /// </summary>
    [Fact]
    public async Task Mfa_el_panel_exige_mfa_activo_y_quien_no_lo_tiene_puede_activarlo()
    {
        var (correo, _) = await UnaCuentaAsync("buscador", verificador: true);
        Assert.Equal(HttpStatusCode.OK, (await EntrarAsync(correo, ClaveBuena)).StatusCode);

        var sinMfa = await _cliente.GetAsync("/api/interno/credenciales/cola");
        Assert.Equal(HttpStatusCode.Forbidden, sinMfa.StatusCode);
        var cuerpo = await sinMfa.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("mfa_requerido", cuerpo.GetProperty("codigo").GetString());

        var estado = await _cliente.GetFromJsonAsync<JsonElement>("/api/mi/mfa");
        Assert.True(estado.GetProperty("obligatorio").GetBoolean());

        var alta = await PostConCsrfAsync("/api/mi/mfa/alta");
        Assert.Equal(HttpStatusCode.OK, alta.StatusCode);
        var secreto = (await alta.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("secreto").GetString()!;
        Assert.Equal(HttpStatusCode.OK,
            (await PostConCsrfAsync("/api/mi/mfa/confirmar", new { contrasena = ClaveBuena, codigo = CodigoTotp(secreto) })).StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await _cliente.GetAsync("/api/interno/credenciales/cola")).StatusCode);
    }

    /// <summary>Un 403 del panel por no tener el rol NO dice nada de MFA (sin cuerpo con codigo).</summary>
    [Fact]
    public async Task Mfa_sin_el_rol_el_panel_responde_403_sin_hablar_de_mfa()
    {
        var correo = NuevoCorreo();
        await RegistrarAsync(correo);
        await VerificarAsync(correo);
        Assert.Equal(HttpStatusCode.OK, (await EntrarAsync(correo, ClaveBuena)).StatusCode);

        var r = await _cliente.GetAsync("/api/interno/credenciales/cola");
        Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
        Assert.DoesNotContain("mfa_requerido", await r.Content.ReadAsStringAsync());
    }

    /// <summary>Desactivar pide contrasena Y codigo; sin la contrasena correcta no se apaga.</summary>
    [Fact]
    public async Task Mfa_desactivar_exige_contrasena_y_codigo()
    {
        var (correo, secreto) = await CuentaConMfaAsync();
        Assert.Equal(HttpStatusCode.OK, (await MfaDePrueba.EntrarAsync(_cliente, correo, ClaveBuena)).StatusCode);

        var sinClave = await PostConCsrfAsync("/api/mi/mfa/desactivar",
            new { contrasena = ClaveMala, codigo = CodigoTotp(secreto) });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, sinClave.StatusCode);
        Assert.True((await CuentaEnBaseAsync(correo)).MfaActivo);

        var ok = await PostConCsrfAsync("/api/mi/mfa/desactivar",
            new { contrasena = ClaveBuena, codigo = CodigoTotp(secreto) });
        Assert.Equal(HttpStatusCode.NoContent, ok.StatusCode);

        var usuario = await CuentaEnBaseAsync(correo);
        Assert.False(usuario.MfaActivo);
        Assert.Null(usuario.MfaSecretoCifrado);
        Assert.Equal(1, await AuditadasAsync(usuario.Id, "mfa_desactivado"));

        await _cliente.DeleteAsync("/api/auth/sesion");
        var sinSegundoPaso = await EntrarAsync(correo, ClaveBuena);
        Assert.True(TraeCookieDeSesion(sinSegundoPaso));
    }

    /// <summary>Restablecer la contrasena por correo NO desactiva el MFA.</summary>
    [Fact]
    public async Task Mfa_restablecer_la_contrasena_no_desactiva_el_mfa()
    {
        var (correo, _) = await CuentaConMfaAsync();
        await PedirRecuperacionAsync(correo);
        var token = (await TokensDeRecuperacionAsync(correo)).Single();

        var r = await _cliente.PostAsJsonAsync("/api/auth/contrasena/restablecer",
            new { token, contrasenaNueva = "otra-contrasena-nueva-9" });
        Assert.True(r.IsSuccessStatusCode, r.StatusCode.ToString());

        Assert.True((await CuentaEnBaseAsync(correo)).MfaActivo);
        await DesafioDeAsync(await EntrarAsync(correo, "otra-contrasena-nueva-9"));
    }
}
