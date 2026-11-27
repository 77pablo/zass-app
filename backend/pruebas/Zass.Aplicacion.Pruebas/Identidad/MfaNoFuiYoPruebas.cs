using Zass.Aplicacion.Identidad;
using Zass.Aplicacion.Pruebas.Dobles;
using Zass.Aplicacion.Puertos;
using Zass.Dominio.Comun;
using Zass.Dominio.Identidad;

namespace Zass.Aplicacion.Pruebas.Identidad;

/// <summary>
/// Revision de seguridad del 25-nov-2026, punto 2. Quien tenia la contraseña podia activar la
/// verificacion en dos pasos con SU telefono y dejar fuera a la dueña: restablecer la contraseña no
/// la quitaba y nadie podia quitarla.
///
/// <para>
/// Lo que vigilan: el enlace «No fui yo» del correo de activacion la quita, cierra todo, deja la
/// contraseña vieja sin servir y lleva a crear otra; sirve una vez y 7 dias. Un administrador puede
/// quitarla con motivo (nunca la suya). Y la dueña se entera por correo de todo cambio.
/// </para>
/// </summary>
public class MfaNoFuiYoPruebas
{
    private const string Clave = "contrasena-larga-1";
    private const string ClaveDelIntruso = Clave;   // la conoce quien activo la verificacion

    private readonly BaseEnMemoria _base = new();
    private readonly RelojFijo _reloj = new();
    private readonly HashDeJuguete _hash = new();
    private readonly SecretosDeterministas _secretos = new();
    private readonly AuditoriaEnMemoria _auditoria = new();
    private readonly ColaEnMemoria _cola = new();
    private readonly ServicioSesion _sesion;
    private readonly ServicioMfa _mfa;
    private readonly ServicioCuenta _cuenta;

    public MfaNoFuiYoPruebas()
    {
        var usuarios = new RepositorioUsuariosEnMemoria(_base);
        _sesion = new ServicioSesion(usuarios, new RepositorioSesionesEnMemoria(_base),
            _base, _hash, _secretos, _reloj, _auditoria);
        _mfa = new ServicioMfa(usuarios, _base, _hash, _secretos, new CifradoDeJuguete(), _reloj, _auditoria, _sesion, _cola);
        _cuenta = ArmadoDeCuenta.Cuenta(_base, _hash, _secretos, _reloj, _cola, _auditoria, _sesion,
            new ContrasenasFiltradasNunca());
    }

    private Usuario UnaCuenta(string correo = "rosa@correo.cl")
    {
        var u = new Usuario
        {
            Correo = correo, Nombre = "Rosa", HashContrasena = _hash.Hash(Clave),
            Estado = EstadoCuenta.Activa, CorreoVerificado = true,
        };
        _base.Usuarios.Add(u);
        return u;
    }

    /// <summary>
    /// El intruso activa la verificacion con su telefono. Devuelve el token «No fui yo» que lleva el
    /// correo que le llega a la dueña, leido de la carga como lo leeria el enviador.
    /// </summary>
    private async Task<string> ElIntrusoActivaLaVerificacion(Usuario u)
    {
        var alta = await _mfa.IniciarAltaAsync(u.Id);
        var codigo = Totp.Codigo(alta.Valor!.Secreto, Totp.PasoDe(_reloj.Ahora));
        Assert.True((await _mfa.ConfirmarAltaAsync(u.Id, new ConfirmarMfaCmd(ClaveDelIntruso, codigo))).Exito);
        var aviso = Assert.Single(_cola.Encoladas, n => n.Tipo == TipoNotificacion.MfaActivado);
        return CargaDeAviso.LeerToken(aviso.Cuerpo)!;
    }

    private static Sesion SesionDe(Usuario u, string hashToken, DateTime ahora) => new()
    {
        UsuarioId = u.Id, HashToken = hashToken, CreadaEn = ahora, ExpiraEn = ahora.AddDays(7),
    };

    // ------------------------------------------------------------------- «No fui yo»

    [Fact]
    public void El_enlace_vive_siete_dias()
    {
        Assert.Equal(TimeSpan.FromDays(7), ServicioMfa.VidaDelEnlaceNoFuiYo);
    }

    [Fact]
    public async Task El_correo_de_activacion_lleva_un_token_que_solo_se_guarda_como_hash()
    {
        var u = UnaCuenta();
        var token = await ElIntrusoActivaLaVerificacion(u);

        Assert.False(string.IsNullOrEmpty(token));
        Assert.Equal(_secretos.HashToken(token), u.MfaRevocacionHash);
        Assert.NotEqual(token, u.MfaRevocacionHash);
        Assert.Equal(_reloj.Ahora.AddDays(7), u.MfaRevocacionExpiraEn);
    }

    [Fact]
    public async Task No_fui_yo_quita_la_verificacion_cierra_todo_y_la_contrasena_vieja_deja_de_servir()
    {
        var u = UnaCuenta();
        var token = await ElIntrusoActivaLaVerificacion(u);
        var delIntruso = SesionDe(u, _secretos.HashToken("sesion-del-intruso"), _reloj.Ahora);
        _base.Sesiones.Add(delIntruso);
        var selloDelIntruso = u.SelloSeguridad;

        var r = await _mfa.NoFuiYoAsync(token, "203.0.113.9");

        Assert.True(r.Exito, r.Mensaje);
        Assert.False(u.MfaActivo);
        Assert.Null(u.MfaRevocacionHash);
        Assert.NotNull(delIntruso.RevocadaEn);
        // Y su cookie de acceso (15 min) muere al instante: el sello cambio.
        Assert.NotEqual(selloDelIntruso, u.SelloSeguridad);
        Assert.Contains(_auditoria.Registros, a => a.Accion == "mfa_quitado_no_fui_yo" && a.EntidadId == u.Id);

        // El intruso ya no entra con la contraseña que conocia.
        Assert.False((await _sesion.EntrarAsync(new IniciarSesionCmd("rosa@correo.cl", ClaveDelIntruso))).Exito);

        // Y la dueña crea una nueva con el token que le devolvio la pantalla, y entra con ella.
        Assert.True((await _cuenta.RestablecerContrasenaAsync(r.Valor!.TokenRestablecer, "una-clave-nueva-77")).Exito);
        var entra = await _sesion.EntrarAsync(new IniciarSesionCmd("rosa@correo.cl", "una-clave-nueva-77"));
        Assert.True(entra.Exito, entra.Mensaje);
        Assert.NotNull(entra.Valor!.Sesion);   // sin segundo paso: la verificacion ya no esta
    }

    [Fact]
    public async Task El_enlace_sirve_una_sola_vez()
    {
        var u = UnaCuenta();
        var token = await ElIntrusoActivaLaVerificacion(u);

        Assert.True((await _mfa.NoFuiYoAsync(token)).Exito);
        var otra = await _mfa.NoFuiYoAsync(token);

        Assert.False(otra.Exito);
        Assert.Equal(Fallas.NoEncontrado, otra.Codigo);
        Assert.Equal(ServicioMfa.FalloNoFuiYo, otra.Mensaje);
    }

    [Fact]
    public async Task Pasados_siete_dias_el_enlace_no_sirve_y_no_toca_nada()
    {
        var u = UnaCuenta();
        var token = await ElIntrusoActivaLaVerificacion(u);
        var hashAntes = u.HashContrasena;

        _reloj.Avanzar(TimeSpan.FromDays(7));
        var r = await _mfa.NoFuiYoAsync(token);

        Assert.False(r.Exito);
        Assert.True(u.MfaActivo);
        Assert.Equal(hashAntes, u.HashContrasena);
    }

    [Fact]
    public async Task Un_token_inventado_o_vacio_no_sirve()
    {
        var u = UnaCuenta();
        await ElIntrusoActivaLaVerificacion(u);

        Assert.False((await _mfa.NoFuiYoAsync("token-que-no-existe")).Exito);
        Assert.False((await _mfa.NoFuiYoAsync("")).Exito);
        Assert.True(u.MfaActivo);
    }

    /// <summary>
    /// Si la dueña desactiva la verificacion y la vuelve a activar, el enlace del PRIMER correo ya no
    /// sirve: quitarla borra el token, y la segunda activacion trae uno nuevo.
    /// </summary>
    [Fact]
    public async Task Desactivar_la_verificacion_anula_el_enlace_anterior()
    {
        var u = UnaCuenta();
        var alta = await _mfa.IniciarAltaAsync(u.Id);
        var secreto = alta.Valor!.Secreto;
        await _mfa.ConfirmarAltaAsync(u.Id, new ConfirmarMfaCmd(Clave, Totp.Codigo(secreto, Totp.PasoDe(_reloj.Ahora))));
        var primero = CargaDeAviso.LeerToken(_cola.Encoladas.Single(n => n.Tipo == TipoNotificacion.MfaActivado).Cuerpo)!;

        _reloj.Avanzar(TimeSpan.FromSeconds(60));
        Assert.True((await _mfa.DesactivarAsync(u.Id,
            new DesactivarMfaCmd(Clave, Totp.Codigo(secreto, Totp.PasoDe(_reloj.Ahora))))).Exito);
        Assert.Null(u.MfaRevocacionHash);

        Assert.False((await _mfa.NoFuiYoAsync(primero)).Exito);
    }

    // ------------------------------------------------------------------- correos de aviso

    [Fact]
    public async Task Desactivar_y_regenerar_avisan_por_correo_a_la_duena()
    {
        var u = UnaCuenta();
        var alta = await _mfa.IniciarAltaAsync(u.Id);
        var secreto = alta.Valor!.Secreto;
        await _mfa.ConfirmarAltaAsync(u.Id, new ConfirmarMfaCmd(Clave, Totp.Codigo(secreto, Totp.PasoDe(_reloj.Ahora))));

        _reloj.Avanzar(TimeSpan.FromSeconds(60));
        Assert.True((await _mfa.RegenerarCodigosAsync(u.Id,
            new RegenerarCodigosMfaCmd(Clave, Totp.Codigo(secreto, Totp.PasoDe(_reloj.Ahora))))).Exito);
        _reloj.Avanzar(TimeSpan.FromSeconds(60));
        Assert.True((await _mfa.DesactivarAsync(u.Id,
            new DesactivarMfaCmd(Clave, Totp.Codigo(secreto, Totp.PasoDe(_reloj.Ahora))))).Exito);

        var regenerados = Assert.Single(_cola.Encoladas, n => n.Tipo == TipoNotificacion.MfaCodigosRegenerados);
        var desactivado = Assert.Single(_cola.Encoladas, n => n.Tipo == TipoNotificacion.MfaDesactivado);
        Assert.All(new[] { regenerados, desactivado }, n =>
        {
            Assert.Equal(u.Id, n.DestinatarioId);
            Assert.Equal(CanalNotificacion.Correo, n.Canal);
            Assert.Null(CargaDeAviso.LeerToken(n.Cuerpo));   // estos no llevan secreto
        });
    }

    [Fact]
    public async Task Un_intento_fallido_de_desactivar_o_regenerar_no_avisa()
    {
        var u = UnaCuenta();
        await ElIntrusoActivaLaVerificacion(u);

        await _mfa.DesactivarAsync(u.Id, new DesactivarMfaCmd(Clave, "000000"));
        await _mfa.RegenerarCodigosAsync(u.Id, new RegenerarCodigosMfaCmd(Clave, "000000"));

        Assert.DoesNotContain(_cola.Encoladas, n =>
            n.Tipo is TipoNotificacion.MfaDesactivado or TipoNotificacion.MfaCodigosRegenerados);
    }

    // ------------------------------------------------------------------- administrador

    [Fact]
    public async Task Un_administrador_la_quita_con_motivo_cierra_todo_y_la_duena_recibe_el_enlace()
    {
        var u = UnaCuenta();
        await ElIntrusoActivaLaVerificacion(u);
        var delIntruso = SesionDe(u, _secretos.HashToken("sesion-del-intruso"), _reloj.Ahora);
        _base.Sesiones.Add(delIntruso);
        var admin = UnaCuenta("admin@zass.cl");
        var selloDelIntruso = u.SelloSeguridad;

        var r = await _mfa.QuitarPorAdministradorAsync(admin.Id,
            new QuitarMfaCmd("  ROSA@correo.cl ", "Escribió a soporte: otra persona activó la verificación."), "198.51.100.4");

        Assert.True(r.Exito, r.Mensaje);
        Assert.False(u.MfaActivo);
        Assert.NotNull(delIntruso.RevocadaEn);
        Assert.NotEqual(selloDelIntruso, u.SelloSeguridad);
        Assert.False((await _sesion.EntrarAsync(new IniciarSesionCmd("rosa@correo.cl", ClaveDelIntruso))).Exito);

        var auditado = Assert.Single(_auditoria.Registros, a => a.Accion == "mfa_quitado_por_administrador");
        Assert.Equal(u.Id, auditado.EntidadId);
        Assert.Equal(admin.Id, auditado.ActorId);
        Assert.Contains("a soporte: otra persona", auditado.Contexto);   // el JSON escapa las tildes

        var aviso = Assert.Single(_cola.Encoladas, n => n.Tipo == TipoNotificacion.MfaQuitadoPorAdministrador);
        Assert.Equal(u.Id, aviso.DestinatarioId);
        var token = CargaDeAviso.LeerToken(aviso.Cuerpo)!;
        Assert.True((await _cuenta.RestablecerContrasenaAsync(token, "una-clave-nueva-77")).Exito);
    }

    [Fact]
    public async Task Un_administrador_no_puede_quitarse_la_suya()
    {
        var admin = UnaCuenta("admin@zass.cl");
        await ElIntrusoActivaLaVerificacion(admin);

        var r = await _mfa.QuitarPorAdministradorAsync(admin.Id, new QuitarMfaCmd("admin@zass.cl", "Perdí el teléfono, me la quito."));

        Assert.False(r.Exito);
        Assert.True(admin.MfaActivo);
        Assert.DoesNotContain(_auditoria.Registros, a => a.Accion == "mfa_quitado_por_administrador");
    }

    [Fact]
    public async Task Sin_cuenta_o_sin_verificacion_no_hace_nada()
    {
        var admin = UnaCuenta("admin@zass.cl");
        var sinMfa = UnaCuenta("sin@correo.cl");
        var hash = sinMfa.HashContrasena;

        Assert.Equal(Fallas.NoEncontrado,
            (await _mfa.QuitarPorAdministradorAsync(admin.Id, new QuitarMfaCmd("nadie@correo.cl", "Motivo suficientemente largo."))).Codigo);
        Assert.False((await _mfa.QuitarPorAdministradorAsync(admin.Id, new QuitarMfaCmd("sin@correo.cl", "Motivo suficientemente largo."))).Exito);
        Assert.Equal(hash, sinMfa.HashContrasena);
        Assert.Empty(_cola.Encoladas);
    }
}
