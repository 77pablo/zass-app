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
            _base, _hash, _secretos, _reloj, _cola, _auditoria, new ContrasenasFiltradasNunca());
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

    // Un prestador llamado «Mapa» generaba el slug «mapa», que choca con la ruta literal
    // GET /api/prestadores/mapa (API-64): la ficha quedaba inalcanzable por la API.
    [Fact]
    public async Task Un_nombre_que_choca_con_una_ruta_reservada_no_se_queda_con_ese_slug()
    {
        var r = await _servicio.RegistrarAsync(Cmd(nombre: "Mapa", rol: "prestador"));

        Assert.True(r.Exito, r.Mensaje);
        var perfil = Assert.Single(_base.PerfilesPrestador);
        Assert.NotEqual("mapa", perfil.Slug);
        Assert.Equal("mapa-2", perfil.Slug);
    }

    // Las mayusculas y las tildes se normalizan ANTES de mirar la lista: «Mápa» tambien llega a «mapa».
    [Fact]
    public async Task Un_nombre_con_tildes_que_normaliza_a_una_palabra_reservada_tampoco_la_toma()
    {
        var r = await _servicio.RegistrarAsync(Cmd(nombre: "MÁPA", rol: "prestador"));

        Assert.True(r.Exito, r.Mensaje);
        Assert.Equal("mapa-2", Assert.Single(_base.PerfilesPrestador).Slug);
    }

    // El candado no puede morder a los demas: un nombre normal conserva su slug limpio.
    [Fact]
    public async Task Un_nombre_normal_conserva_su_slug_sin_sufijo()
    {
        var r = await _servicio.RegistrarAsync(Cmd(nombre: "Mapas del Sur", rol: "prestador"));

        Assert.True(r.Exito, r.Mensaje);
        Assert.Equal("mapas-del-sur", Assert.Single(_base.PerfilesPrestador).Slug);
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
    [Fact]
    public async Task ActivarPrestador_es_idempotente_si_la_cuenta_ya_tiene_el_rol_prestador()
    {
        var reg = await _servicio.RegistrarAsync(Cmd(rol: "buscador"));
        var id = reg.Valor!.UsuarioId;

        var primerIntento = await _servicio.ActivarPrestadorAsync(id);
        Assert.True(primerIntento.Exito, primerIntento.Mensaje);

        var segundoIntento = await _servicio.ActivarPrestadorAsync(id);
        Assert.True(segundoIntento.Exito, segundoIntento.Mensaje);

        var usuario = _base.Usuarios.Single(u => u.Id == id);
        Assert.True(usuario.TieneRol("prestador"));
        Assert.Equal(2, usuario.Roles.Count); // buscador + prestador, sin duplicar
    }

    [Fact]
    public async Task ActivarPrestador_no_lanza_nre_si_un_rol_no_tiene_su_entidad_cargada()
    {
        var reg = await _servicio.RegistrarAsync(Cmd(rol: "buscador"));
        var id = reg.Valor!.UsuarioId;

        // Simulamos un UsuarioRol que no cargo la navegacion Rol (Rol == null)
        var usuario = _base.Usuarios.Single(u => u.Id == id);
        usuario.Roles.Add(new Zass.Dominio.Identidad.UsuarioRol
        {
            UsuarioId = id,
            RolId = 99,
            Rol = null!
        });

        // Con r.Rol.Codigo esto lanza NullReferenceException; con TieneRol se evalua de forma segura
        var r = await _servicio.ActivarPrestadorAsync(id);
        Assert.True(r.Exito, r.Mensaje);
    }

    // ------------------------------------------------ Registro completo (20-nov-2026)

    private static RegistrarCuentaCmd Completo(
        string correo = "ana@correo.cl", string rut = "11.111.111-1", string tipo = "particular",
        string rol = "buscador") =>
        new(correo, "contrasena-larga-1", "Ana", rol, tipo,
            Apellido: tipo == "particular" ? "Rojas" : null, Rut: rut, Telefono: " +56 9 1234 5678 ",
            RazonSocial: tipo == "empresa" ? "Gasfiteria Rojas SpA" : null, MayorDeEdad: true);

    [Fact]
    public async Task Los_datos_de_la_cuenta_se_guardan_y_el_RUT_con_una_sola_forma()
    {
        var r = await _servicio.RegistrarAsync(Completo());

        Assert.True(r.Exito, r.Mensaje);
        var u = Assert.Single(_base.Usuarios);
        Assert.Equal(TipoCuenta.Particular, u.Tipo);
        Assert.Equal("Rojas", u.Apellido);
        Assert.Equal("11111111-1", u.Rut);
        Assert.Equal("+56 9 1234 5678", u.Telefono);
        Assert.Null(u.RazonSocial);
        Assert.Equal(_reloj.Ahora, u.MayorDeEdadConfirmadoEn);
    }

    [Fact]
    public async Task Un_RUT_ya_usado_se_rechaza_aunque_venga_escrito_distinto()
    {
        await _servicio.RegistrarAsync(Completo(correo: "ana@correo.cl", rut: "11.111.111-1"));

        var r = await _servicio.RegistrarAsync(Completo(correo: "otra@correo.cl", rut: "111111111"));

        Assert.False(r.Exito);
        Assert.Equal(Fallas.Conflicto, r.Codigo);
        Assert.Contains("RUT", r.Mensaje!);
        Assert.Single(_base.Usuarios);
    }

    [Fact]
    public async Task Una_empresa_guarda_su_razon_social_y_su_ficha_parte_con_ese_nombre()
    {
        var r = await _servicio.RegistrarAsync(Completo(tipo: "empresa", rut: "76.086.428-5", rol: "prestador"));

        Assert.True(r.Exito, r.Mensaje);
        var u = Assert.Single(_base.Usuarios);
        Assert.Equal(TipoCuenta.Empresa, u.Tipo);
        Assert.Equal("Gasfiteria Rojas SpA", u.RazonSocial);
        Assert.Null(u.Apellido);
        Assert.Equal("Ana", u.Nombre);   // la persona de contacto
        Assert.Equal("Gasfiteria Rojas SpA", Assert.Single(_base.PerfilesPrestador).NombrePublico);
    }

    [Fact]
    public async Task Un_particular_que_ofrece_parte_con_nombre_y_apellido_en_su_ficha()
    {
        await _servicio.RegistrarAsync(Completo(rol: "prestador"));

        Assert.Equal("Ana Rojas", Assert.Single(_base.PerfilesPrestador).NombrePublico);
    }

    [Fact]
    public void Borrar_la_cuenta_borra_tambien_RUT_telefono_apellido_y_razon_social()
    {
        var u = new Zass.Dominio.Identidad.Usuario
        {
            Nombre = "Ana", Apellido = "Rojas", Rut = "11111111-1", Telefono = "+56912345678",
            RazonSocial = "Gasfiteria Rojas SpA",
        };

        u.Anonimizar();

        Assert.Null(u.Apellido);
        Assert.Null(u.Rut);
        Assert.Null(u.Telefono);
        Assert.Null(u.RazonSocial);
    }
}