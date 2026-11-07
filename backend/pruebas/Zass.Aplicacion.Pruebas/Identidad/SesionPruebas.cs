using Zass.Aplicacion.Identidad;
using Zass.Aplicacion.Pruebas.Dobles;
using Zass.Dominio.Comun;
using Zass.Dominio.Identidad;

namespace Zass.Aplicacion.Pruebas.Identidad;

/// <summary>CU-02 - Iniciar sesion, y CS-03 - Revocar sesiones. API-09 a API-12.</summary>
public class SesionPruebas
{
    private readonly BaseEnMemoria _base = new();
    private readonly RelojFijo _reloj = new();
    private readonly HashDeJuguete _hash = new();
    private readonly SecretosDeterministas _secretos = new();
    private readonly AuditoriaEnMemoria _auditoria = new();
    private readonly ServicioSesion _servicio;

    private const string Clave = "contrasena-larga-1";

    public SesionPruebas()
    {
        _servicio = new ServicioSesion(
            new RepositorioUsuariosEnMemoria(_base), new RepositorioSesionesEnMemoria(_base),
            _base, _hash, _secretos, _reloj, _auditoria);
    }

    private Usuario SembrarUsuario(
        string correo = "pablo@correo.cl",
        EstadoCuenta estado = EstadoCuenta.Activa,
        bool correoVerificado = true)
    {
        var usuario = new Usuario
        {
            Correo = correo,
            Nombre = "Pablo",
            HashContrasena = _hash.Hash(Clave),
            Estado = estado,
            CorreoVerificado = correoVerificado,
        };
        usuario.Roles.Add(new UsuarioRol
        {
            UsuarioId = usuario.Id,
            RolId = 1,
            Rol = _base.Roles.First(r => r.Codigo == "buscador"),
        });
        _base.Usuarios.Add(usuario);
        return usuario;
    }

    [Fact]
    public async Task Un_inicio_correcto_abre_sesion_y_de_la_sesion_solo_queda_el_hash_del_token()
    {
        var usuario = SembrarUsuario();

        var r = await _servicio.IniciarAsync(new IniciarSesionCmd("pablo@correo.cl", Clave));

        Assert.True(r.Exito, r.Mensaje);
        var token = r.Valor!.TokenRefresco;
        var sesion = Assert.Single(_base.Sesiones);
        Assert.Equal(usuario.Id, sesion.UsuarioId);
        Assert.NotEqual(token, sesion.HashToken);
        Assert.Equal(_secretos.HashToken(token), sesion.HashToken);
        Assert.Equal(new[] { "buscador" }, r.Valor!.Roles);
    }

    [Fact]
    public async Task El_token_de_refresco_vive_treinta_dias()
    {
        SembrarUsuario();

        await _servicio.IniciarAsync(new IniciarSesionCmd("pablo@correo.cl", Clave));

        Assert.Equal(_reloj.Ahora.AddDays(30), Assert.Single(_base.Sesiones).ExpiraEn);
    }

    [Fact]
    public async Task El_correo_se_normaliza_tambien_al_iniciar_sesion()
    {
        SembrarUsuario(correo: "pablo@correo.cl");

        var r = await _servicio.IniciarAsync(new IniciarSesionCmd("  PABLO@Correo.cl ", Clave));

        Assert.True(r.Exito, r.Mensaje);
    }

    [Fact]
    public async Task Una_contrasena_mala_y_un_correo_inexistente_responden_exactamente_lo_mismo()
    {
        SembrarUsuario();

        var malaClave = await _servicio.IniciarAsync(new IniciarSesionCmd("pablo@correo.cl", "otra-clave"));
        var sinCuenta = await _servicio.IniciarAsync(new IniciarSesionCmd("nadie@correo.cl", Clave));

        Assert.False(malaClave.Exito);
        Assert.False(sinCuenta.Exito);
        Assert.Equal(Fallas.NoAutorizado, malaClave.Codigo);
        Assert.Equal(sinCuenta.Codigo, malaClave.Codigo);
        Assert.Equal(sinCuenta.Mensaje, malaClave.Mensaje);
        Assert.Empty(_base.Sesiones);
    }

    [Fact]
    public async Task Una_cuenta_suspendida_no_inicia_sesion()
    {
        SembrarUsuario(estado: EstadoCuenta.Suspendida);

        var r = await _servicio.IniciarAsync(new IniciarSesionCmd("pablo@correo.cl", Clave));

        Assert.False(r.Exito);
        Assert.Empty(_base.Sesiones);
    }

    [Fact]
    public async Task Una_cuenta_eliminada_no_inicia_sesion()
    {
        SembrarUsuario(estado: EstadoCuenta.Eliminada);

        var r = await _servicio.IniciarAsync(new IniciarSesionCmd("pablo@correo.cl", Clave));

        Assert.False(r.Exito);
        Assert.Empty(_base.Sesiones);
    }

    [Fact]
    public async Task Sin_verificar_el_correo_igual_se_inicia_sesion_porque_RN_S1_02_solo_frena_publicar_y_agendar()
    {
        SembrarUsuario(estado: EstadoCuenta.PendienteVerificacion, correoVerificado: false);

        var r = await _servicio.IniciarAsync(new IniciarSesionCmd("pablo@correo.cl", Clave));

        Assert.True(r.Exito, r.Mensaje);
        Assert.False(r.Valor!.CorreoVerificado);
    }

    [Fact]
    public async Task El_intento_fallido_queda_en_auditoria_con_el_correo_en_hash_y_nunca_en_claro()
    {
        SembrarUsuario();

        await _servicio.IniciarAsync(new IniciarSesionCmd("pablo@correo.cl", "otra-clave"), ip: "1.2.3.4");

        var entrada = Assert.Single(_auditoria.Registros);
        Assert.Equal("inicio_sesion_fallido", entrada.Accion);
        Assert.Equal("1.2.3.4", entrada.IpOrigen);
        Assert.DoesNotContain("pablo@correo.cl", entrada.Contexto ?? "");
        Assert.DoesNotContain("otra-clave", entrada.Contexto ?? "");
        Assert.Contains(_secretos.HashToken("pablo@correo.cl"), entrada.Contexto ?? "");
    }

    [Fact]
    public async Task El_inicio_exitoso_tambien_queda_en_auditoria()
    {
        SembrarUsuario();

        await _servicio.IniciarAsync(new IniciarSesionCmd("pablo@correo.cl", Clave));

        Assert.Equal("inicio_sesion", Assert.Single(_auditoria.Registros).Accion);
    }

    [Fact]
    public async Task Renovar_entrega_un_token_nuevo_y_revoca_el_anterior()
    {
        SembrarUsuario();
        var primero = (await _servicio.IniciarAsync(new IniciarSesionCmd("pablo@correo.cl", Clave))).Valor!;

        var r = await _servicio.RenovarAsync(primero.TokenRefresco);

        Assert.True(r.Exito, r.Mensaje);
        Assert.NotEqual(primero.TokenRefresco, r.Valor!.TokenRefresco);

        var vieja = _base.Sesiones.Single(s => s.HashToken == _secretos.HashToken(primero.TokenRefresco));
        Assert.NotNull(vieja.RevocadaEn);
        Assert.Single(_base.Sesiones.Where(s => s.EstaVigente(_reloj.Ahora)));
    }

    [Fact]
    public async Task Un_token_ya_renovado_no_sirve_dos_veces()
    {
        SembrarUsuario();
        var primero = (await _servicio.IniciarAsync(new IniciarSesionCmd("pablo@correo.cl", Clave))).Valor!;
        await _servicio.RenovarAsync(primero.TokenRefresco);

        var r = await _servicio.RenovarAsync(primero.TokenRefresco);

        Assert.False(r.Exito);
        Assert.Equal(Fallas.NoAutorizado, r.Codigo);
    }

    [Fact]
    public async Task Un_token_vencido_no_renueva()
    {
        SembrarUsuario();
        var sesion = (await _servicio.IniciarAsync(new IniciarSesionCmd("pablo@correo.cl", Clave))).Valor!;

        _reloj.Avanzar(TimeSpan.FromDays(31));
        var r = await _servicio.RenovarAsync(sesion.TokenRefresco);

        Assert.False(r.Exito);
    }

    [Fact]
    public async Task Un_token_inventado_no_renueva()
    {
        SembrarUsuario();

        var r = await _servicio.RenovarAsync("token-inventado");

        Assert.False(r.Exito);
        Assert.Equal(Fallas.NoAutorizado, r.Codigo);
    }

    [Fact]
    public async Task Cerrar_sesion_revoca_solo_la_actual()
    {
        SembrarUsuario();
        var movil = (await _servicio.IniciarAsync(new IniciarSesionCmd("pablo@correo.cl", Clave))).Valor!;
        var pc = (await _servicio.IniciarAsync(new IniciarSesionCmd("pablo@correo.cl", Clave))).Valor!;

        var r = await _servicio.CerrarAsync(movil.TokenRefresco);

        Assert.True(r.Exito, r.Mensaje);
        var vigentes = _base.Sesiones.Where(s => s.EstaVigente(_reloj.Ahora)).ToList();
        Assert.Equal(_secretos.HashToken(pc.TokenRefresco), Assert.Single(vigentes).HashToken);
    }

    [Fact]
    public async Task Revocar_todas_no_deja_ninguna_sesion_vigente()
    {
        var usuario = SembrarUsuario();
        await _servicio.IniciarAsync(new IniciarSesionCmd("pablo@correo.cl", Clave));
        await _servicio.IniciarAsync(new IniciarSesionCmd("pablo@correo.cl", Clave));

        var r = await _servicio.RevocarTodasAsync(usuario.Id);

        Assert.True(r.Exito, r.Mensaje);
        Assert.Empty(_base.Sesiones.Where(s => s.EstaVigente(_reloj.Ahora)));
        Assert.Equal(2, _base.Sesiones.Count);
    }

    [Fact]
    public async Task Revocar_todas_puede_perdonar_la_sesion_de_la_que_viene_la_peticion()
    {
        var usuario = SembrarUsuario();
        var actual = (await _servicio.IniciarAsync(new IniciarSesionCmd("pablo@correo.cl", Clave))).Valor!;
        await _servicio.IniciarAsync(new IniciarSesionCmd("pablo@correo.cl", Clave));

        await _servicio.RevocarTodasAsync(usuario.Id, exceptoToken: actual.TokenRefresco);

        var vigentes = _base.Sesiones.Where(s => s.EstaVigente(_reloj.Ahora)).ToList();
        Assert.Equal(_secretos.HashToken(actual.TokenRefresco), Assert.Single(vigentes).HashToken);
    }
}
