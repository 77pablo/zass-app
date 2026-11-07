using Zass.Aplicacion.Identidad;
using Zass.Aplicacion.Pruebas.Dobles;
using Zass.Dominio.Comun;

namespace Zass.Aplicacion.Pruebas.Identidad;

/// <summary>CU-01 · Registrar cuenta (API-08). Reglas RN-S1-01, RN-S1-02, RN-S1-03, RN-S1-08.</summary>
public class RegistroPruebas
{
    private readonly BaseEnMemoria _base = new();
    private readonly RelojFijo _reloj = new();
    private readonly HashDeJuguete _hash = new();
    private readonly SecretosDeterministas _secretos = new();
    private readonly ColaEnMemoria _cola = new();
    private readonly AuditoriaEnMemoria _auditoria = new();
    private readonly ServicioRegistro _servicio;

    public RegistroPruebas()
    {
        _servicio = new ServicioRegistro(
            new RepositorioUsuariosEnMemoria(_base),
            new RepositorioPerfilesEnMemoria(_base),
            _base, _hash, _secretos, _reloj, _cola, _auditoria);
    }

    private static RegistrarCuentaCmd Cmd(
        string correo = "pablo@correo.cl",
        string contrasena = "contrasena-larga-1",
        string nombre = "Pablo",
        string rol = "buscador") => new(correo, contrasena, nombre, rol);

    [Fact]
    public async Task La_cuenta_nace_pendiente_de_verificar_el_correo()
    {
        var r = await _servicio.RegistrarAsync(Cmd());

        Assert.True(r.Exito, r.Mensaje);
        var usuario = Assert.Single(_base.Usuarios);
        Assert.Equal(EstadoCuenta.PendienteVerificacion, usuario.Estado);
        Assert.False(usuario.CorreoVerificado);
        Assert.Equal(1, _base.Guardados);
    }

    [Fact]
    public async Task El_correo_se_guarda_normalizado_en_minusculas()
    {
        var r = await _servicio.RegistrarAsync(Cmd(correo: "  Pablo@Correo.CL "));

        Assert.True(r.Exito, r.Mensaje);
        Assert.Equal("pablo@correo.cl", Assert.Single(_base.Usuarios).Correo);
    }

    [Fact]
    public async Task Un_correo_ya_usado_se_rechaza_aunque_cambie_de_mayusculas()
    {
        await _servicio.RegistrarAsync(Cmd(correo: "pablo@correo.cl"));

        var r = await _servicio.RegistrarAsync(Cmd(correo: "PABLO@CORREO.CL"));

        Assert.False(r.Exito);
        Assert.Equal(Fallas.Conflicto, r.Codigo);
        Assert.Single(_base.Usuarios);
    }

    [Fact]
    public async Task La_contrasena_nunca_queda_en_claro()
    {
        await _servicio.RegistrarAsync(Cmd(contrasena: "secreto-de-pablo"));

        var usuario = Assert.Single(_base.Usuarios);
        Assert.DoesNotContain("secreto-de-pablo", usuario.HashContrasena);
        Assert.True(_hash.Verifica("secreto-de-pablo", usuario.HashContrasena));
    }

    [Fact]
    public async Task Un_rol_interno_no_se_puede_pedir_al_registrarse()
    {
        var r = await _servicio.RegistrarAsync(Cmd(rol: "administrador"));

        Assert.False(r.Exito);
        Assert.Equal(Fallas.NoAutorizado, r.Codigo);
        Assert.Empty(_base.Usuarios);
    }

    [Fact]
    public async Task Un_rol_que_no_existe_se_rechaza()
    {
        var r = await _servicio.RegistrarAsync(Cmd(rol: "obispo"));

        Assert.False(r.Exito);
        Assert.Equal(Fallas.Validacion, r.Codigo);
        Assert.Empty(_base.Usuarios);
    }

    [Fact]
    public async Task Se_encola_el_correo_de_verificacion_y_del_token_solo_queda_el_hash()
    {
        var r = await _servicio.RegistrarAsync(Cmd());

        Assert.True(r.Exito, r.Mensaje);
        var aviso = Assert.Single(_cola.Encoladas);
        Assert.Equal(TipoNotificacion.CorreoVerificacion, aviso.Tipo);

        var tokenEnClaro = Assert.Single(_secretos.Emitidos);
        Assert.Contains(tokenEnClaro, aviso.Cuerpo);

        var usuario = Assert.Single(_base.Usuarios);
        Assert.NotNull(usuario.TokenVerificacion);
        Assert.NotEqual(tokenEnClaro, usuario.TokenVerificacion);
        Assert.Equal(_secretos.HashToken(tokenEnClaro), usuario.TokenVerificacion);
    }

    [Fact]
    public async Task El_token_de_verificacion_caduca_a_las_24_horas()
    {
        await _servicio.RegistrarAsync(Cmd());

        Assert.Equal(_reloj.Ahora.AddHours(24), Assert.Single(_base.Usuarios).TokenVerificacionExpiraEn);
    }

    [Fact]
    public async Task Quien_se_registra_como_prestador_queda_con_perfil_en_borrador()
    {
        var r = await _servicio.RegistrarAsync(Cmd(nombre: "Pablo Espinoza", rol: "prestador"));

        Assert.True(r.Exito, r.Mensaje);
        var perfil = Assert.Single(_base.PerfilesPrestador);
        Assert.Equal(EstadoPerfil.Borrador, perfil.Estado);
        Assert.Equal("Pablo Espinoza", perfil.NombrePublico);
        Assert.Equal("pablo-espinoza", perfil.Slug);
        Assert.Empty(_base.PerfilesBuscador);
    }

    [Fact]
    public async Task Dos_prestadores_con_el_mismo_nombre_no_comparten_slug()
    {
        await _servicio.RegistrarAsync(Cmd(correo: "uno@correo.cl", nombre: "Juan Perez", rol: "prestador"));
        await _servicio.RegistrarAsync(Cmd(correo: "dos@correo.cl", nombre: "Juan Perez", rol: "prestador"));

        var slugs = _base.PerfilesPrestador.Select(p => p.Slug).ToList();
        Assert.Equal(2, slugs.Count);
        Assert.Equal(slugs.Count, slugs.Distinct().Count());
    }

    [Fact]
    public async Task Quien_se_registra_como_buscador_queda_con_preferencias_por_omision()
    {
        var r = await _servicio.RegistrarAsync(Cmd(rol: "buscador"));

        Assert.True(r.Exito, r.Mensaje);
        var perfil = Assert.Single(_base.PerfilesBuscador);
        Assert.Equal((short)10, perfil.RadioPreferidoKm);
        Assert.Empty(_base.PerfilesPrestador);
    }

    [Fact]
    public async Task El_registro_queda_en_auditoria_sin_la_contrasena()
    {
        await _servicio.RegistrarAsync(Cmd(contrasena: "secreto-de-pablo"));

        var entrada = Assert.Single(_auditoria.Registros);
        Assert.Equal("registro_cuenta", entrada.Accion);
        Assert.DoesNotContain("secreto-de-pablo", entrada.Contexto ?? "");
    }
}
