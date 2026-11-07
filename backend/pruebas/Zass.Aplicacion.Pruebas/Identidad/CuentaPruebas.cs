using Zass.Aplicacion.Identidad;
using Zass.Aplicacion.Pruebas.Dobles;
using Zass.Dominio.Comun;
using Zass.Dominio.Identidad;

namespace Zass.Aplicacion.Pruebas.Identidad;

/// <summary>
/// CS-01 verificar correo, CS-02 recuperar contrasena, CS-04 eliminar cuenta, y API-16.
/// Reglas RN-S1-04 y RN-S1-07.
/// </summary>
public class CuentaPruebas
{
    private readonly BaseEnMemoria _base = new();
    private readonly RelojFijo _reloj = new();
    private readonly HashDeJuguete _hash = new();
    private readonly SecretosDeterministas _secretos = new();
    private readonly ColaEnMemoria _cola = new();
    private readonly AuditoriaEnMemoria _auditoria = new();
    private readonly ServicioRegistro _registro;
    private readonly ServicioSesion _sesiones;
    private readonly ServicioCuenta _cuenta;

    private const string Clave = "contrasena-larga-1";

    public CuentaPruebas()
    {
        var usuarios = new RepositorioUsuariosEnMemoria(_base);
        var perfiles = new RepositorioPerfilesEnMemoria(_base);
        var sesiones = new RepositorioSesionesEnMemoria(_base);

        _registro = new ServicioRegistro(
            usuarios, perfiles, _base, _hash, _secretos, _reloj, _cola, _auditoria);
        _sesiones = new ServicioSesion(
            usuarios, sesiones, _base, _hash, _secretos, _reloj, _auditoria);
        _cuenta = new ServicioCuenta(
            usuarios, perfiles, _base, _hash, _secretos, _reloj, _cola, _auditoria, _sesiones);
    }

    /// <summary>Registra de verdad, con el servicio real: asi el token es el que se emite.</summary>
    private async Task<(Guid Id, string Token)> RegistrarAsync(
        string correo = "pablo@correo.cl", string rol = "buscador")
    {
        var antes = _secretos.Emitidos.Count;
        var r = await _registro.RegistrarAsync(new RegistrarCuentaCmd(correo, Clave, "Pablo", rol));
        Assert.True(r.Exito, r.Mensaje);
        return (r.Valor!.UsuarioId, _secretos.Emitidos[antes]);
    }

    // ----------------------------------------------------------- CS-01

    [Fact]
    public async Task Un_token_valido_verifica_el_correo_y_activa_la_cuenta()
    {
        var (id, token) = await RegistrarAsync();

        var r = await _cuenta.VerificarCorreoAsync(token);

        Assert.True(r.Exito, r.Mensaje);
        var usuario = _base.Usuarios.Single(u => u.Id == id);
        Assert.True(usuario.CorreoVerificado);
        Assert.Equal(EstadoCuenta.Activa, usuario.Estado);
    }

    [Fact]
    public async Task El_token_de_verificacion_se_consume_y_no_sirve_dos_veces()
    {
        var (_, token) = await RegistrarAsync();
        await _cuenta.VerificarCorreoAsync(token);

        var r = await _cuenta.VerificarCorreoAsync(token);

        Assert.False(r.Exito);
        Assert.Equal(Fallas.NoEncontrado, r.Codigo);
    }

    [Fact]
    public async Task Un_token_de_verificacion_vencido_no_verifica_nada()
    {
        var (id, token) = await RegistrarAsync();

        _reloj.Avanzar(TimeSpan.FromHours(25));
        var r = await _cuenta.VerificarCorreoAsync(token);

        Assert.False(r.Exito);
        Assert.False(_base.Usuarios.Single(u => u.Id == id).CorreoVerificado);
    }

    [Fact]
    public async Task Un_token_de_verificacion_inventado_no_verifica_nada()
    {
        await RegistrarAsync();

        var r = await _cuenta.VerificarCorreoAsync("token-inventado");

        Assert.False(r.Exito);
    }

    // ----------------------------------------------------------- CS-02

    [Fact]
    public async Task Pedir_recuperacion_responde_igual_exista_o_no_la_cuenta()
    {
        await RegistrarAsync(correo: "pablo@correo.cl");

        var existe = await _cuenta.PedirRecuperacionAsync("pablo@correo.cl");
        var noExiste = await _cuenta.PedirRecuperacionAsync("nadie@correo.cl");

        Assert.True(existe.Exito);
        Assert.True(noExiste.Exito);
        Assert.Equal(existe.Mensaje, noExiste.Mensaje);
    }

    [Fact]
    public async Task Pedir_recuperacion_de_una_cuenta_real_encola_el_aviso_y_guarda_solo_el_hash()
    {
        var (id, _) = await RegistrarAsync();
        _cola.Encoladas.Clear();

        await _cuenta.PedirRecuperacionAsync("pablo@correo.cl");

        var aviso = Assert.Single(_cola.Encoladas);
        Assert.Equal(TipoNotificacion.RecuperarContrasena, aviso.Tipo);

        var token = _secretos.Emitidos.Last();
        Assert.Contains(token, aviso.Cuerpo);
        var usuario = _base.Usuarios.Single(u => u.Id == id);
        Assert.Equal(_secretos.HashToken(token), usuario.TokenRecuperacion);
    }

    [Fact]
    public async Task Pedir_recuperacion_de_una_cuenta_que_no_existe_no_encola_ningun_correo()
    {
        _cola.Encoladas.Clear();

        await _cuenta.PedirRecuperacionAsync("nadie@correo.cl");

        Assert.Empty(_cola.Encoladas);
    }

    [Fact]
    public async Task Restablecer_cambia_la_contrasena_y_consume_el_token()
    {
        var (id, _) = await RegistrarAsync();
        await _cuenta.PedirRecuperacionAsync("pablo@correo.cl");
        var token = _secretos.Emitidos.Last();

        var r = await _cuenta.RestablecerContrasenaAsync(token, "clave-nueva-larga");

        Assert.True(r.Exito, r.Mensaje);
        var usuario = _base.Usuarios.Single(u => u.Id == id);
        Assert.True(_hash.Verifica("clave-nueva-larga", usuario.HashContrasena));
        Assert.Null(usuario.TokenRecuperacion);

        var repetido = await _cuenta.RestablecerContrasenaAsync(token, "otra-clave-larga");
        Assert.False(repetido.Exito);
    }

    [Fact]
    public async Task Restablecer_revoca_todas_las_sesiones_porque_quien_la_pidio_perdio_el_control()
    {
        await RegistrarAsync();
        await _sesiones.IniciarAsync(new IniciarSesionCmd("pablo@correo.cl", Clave));
        await _sesiones.IniciarAsync(new IniciarSesionCmd("pablo@correo.cl", Clave));
        await _cuenta.PedirRecuperacionAsync("pablo@correo.cl");
        var token = _secretos.Emitidos.Last();

        await _cuenta.RestablecerContrasenaAsync(token, "clave-nueva-larga");

        Assert.Empty(_base.Sesiones.Where(s => s.EstaVigente(_reloj.Ahora)));
    }

    [Fact]
    public async Task Pedir_recuperacion_no_le_pisa_el_vencimiento_al_token_de_verificacion()
    {
        // Los dos tokens viven a la vez y duran distinto: 24 h el de correo, 1 h el de
        // recuperacion. Con una sola columna de vencimiento, pedir recuperacion acortaba
        // en silencio el token de verificacion de quien todavia no habia confirmado.
        var (_, tokenCorreo) = await RegistrarAsync();
        await _cuenta.PedirRecuperacionAsync("pablo@correo.cl");

        _reloj.Avanzar(TimeSpan.FromHours(2));
        var r = await _cuenta.VerificarCorreoAsync(tokenCorreo);

        Assert.True(r.Exito, r.Mensaje);
    }

    [Fact]
    public async Task Un_token_de_recuperacion_vencido_no_restablece_nada()
    {
        await RegistrarAsync();
        await _cuenta.PedirRecuperacionAsync("pablo@correo.cl");
        var token = _secretos.Emitidos.Last();

        _reloj.Avanzar(TimeSpan.FromHours(2));
        var r = await _cuenta.RestablecerContrasenaAsync(token, "clave-nueva-larga");

        Assert.False(r.Exito);
    }

    // --------------------------------------------- RN-S1-04 · cambio con sesion

    [Fact]
    public async Task Cambiar_la_contrasena_revoca_las_demas_sesiones_pero_no_la_actual()
    {
        var (id, _) = await RegistrarAsync();
        var actual = (await _sesiones.IniciarAsync(new IniciarSesionCmd("pablo@correo.cl", Clave))).Valor!;
        await _sesiones.IniciarAsync(new IniciarSesionCmd("pablo@correo.cl", Clave));

        var r = await _cuenta.CambiarContrasenaAsync(
            id, Clave, "clave-nueva-larga", sesionActual: actual.TokenRefresco);

        Assert.True(r.Exito, r.Mensaje);
        var vigentes = _base.Sesiones.Where(s => s.EstaVigente(_reloj.Ahora)).ToList();
        Assert.Equal(_secretos.HashToken(actual.TokenRefresco), Assert.Single(vigentes).HashToken);
    }

    [Fact]
    public async Task Cambiar_la_contrasena_exige_acertar_la_actual()
    {
        var (id, _) = await RegistrarAsync();

        var r = await _cuenta.CambiarContrasenaAsync(id, "no-era-esta", "clave-nueva-larga");

        Assert.False(r.Exito);
        Assert.Equal(Fallas.NoAutorizado, r.Codigo);
        Assert.True(_hash.Verifica(Clave, _base.Usuarios.Single(u => u.Id == id).HashContrasena));
    }

    // ----------------------------------------------------------- CS-04

    [Fact]
    public async Task Eliminar_la_cuenta_anonimiza_en_vez_de_borrar_la_fila()
    {
        var (id, _) = await RegistrarAsync();

        var r = await _cuenta.EliminarAsync(id);

        Assert.True(r.Exito, r.Mensaje);
        var usuario = Assert.Single(_base.Usuarios);
        Assert.Equal(EstadoCuenta.Eliminada, usuario.Estado);
        Assert.Equal("Usuario eliminado", usuario.Nombre);
        Assert.DoesNotContain("pablo@correo.cl", usuario.Correo);
        Assert.Equal("", usuario.HashContrasena);
        Assert.NotNull(usuario.AnonimizadoEn);
    }

    [Fact]
    public async Task Eliminar_la_cuenta_revoca_todas_las_sesiones()
    {
        var (id, _) = await RegistrarAsync();
        await _sesiones.IniciarAsync(new IniciarSesionCmd("pablo@correo.cl", Clave));

        await _cuenta.EliminarAsync(id);

        Assert.Empty(_base.Sesiones.Where(s => s.EstaVigente(_reloj.Ahora)));
    }

    [Fact]
    public async Task Eliminar_la_cuenta_saca_el_perfil_de_prestador_de_la_busqueda()
    {
        var (id, token) = await RegistrarAsync(rol: "prestador");
        await _cuenta.VerificarCorreoAsync(token);
        var perfil = _base.PerfilesPrestador.Single();
        perfil.Estado = EstadoPerfil.Publicado;

        await _cuenta.EliminarAsync(id);

        Assert.Equal(EstadoPerfil.Despublicado, perfil.Estado);
    }

    [Fact]
    public async Task Eliminar_la_cuenta_deja_el_correo_libre_para_volver_a_registrarse()
    {
        var (id, _) = await RegistrarAsync(correo: "pablo@correo.cl");
        await _cuenta.EliminarAsync(id);

        var r = await _registro.RegistrarAsync(
            new RegistrarCuentaCmd("pablo@correo.cl", Clave, "Pablo", "buscador"));

        Assert.True(r.Exito, r.Mensaje);
        Assert.Equal(2, _base.Usuarios.Count);
    }

    [Fact]
    public async Task Eliminar_la_cuenta_queda_en_auditoria()
    {
        var (id, _) = await RegistrarAsync();
        _auditoria.Registros.Clear();

        await _cuenta.EliminarAsync(id);

        Assert.Contains(_auditoria.Registros, e => e.Accion == "eliminar_cuenta" && e.EntidadId == id);
    }

    [Fact]
    public async Task Eliminar_una_cuenta_que_no_existe_no_revienta()
    {
        var r = await _cuenta.EliminarAsync(Guid.NewGuid());

        Assert.False(r.Exito);
        Assert.Equal(Fallas.NoEncontrado, r.Codigo);
    }

    // ----------------------------------------------------------- API-16

    [Fact]
    public async Task Mi_cuenta_devuelve_los_roles_y_nunca_el_hash_de_la_contrasena()
    {
        var (id, _) = await RegistrarAsync(rol: "prestador");

        var r = await _cuenta.MiCuentaAsync(id);

        Assert.True(r.Exito, r.Mensaje);
        var dto = r.Valor!;
        Assert.Equal("pablo@correo.cl", dto.Correo);
        Assert.Equal(new[] { "prestador" }, dto.Roles);
        Assert.False(dto.CorreoVerificado);
        Assert.DoesNotContain(
            "HashContrasena",
            dto.GetType().GetProperties().Select(p => p.Name));
    }
}
