using Zass.Aplicacion.Puertos;
using Zass.Dominio.Catalogo;
using Zass.Dominio.Credenciales;
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
    private readonly AlmacenQueRecuerda _almacen;
    private readonly RegistroQueAnota<ServicioBorradoDeDatos> _logBorrado = new();
    private readonly CifradoDeJuguete _cifrado = new();

    private const string Clave = "contrasena-larga-1";

    public CuentaPruebas()
    {
        var usuarios = new RepositorioUsuariosEnMemoria(_base);
        var perfiles = new RepositorioPerfilesEnMemoria(_base);
        var sesiones = new RepositorioSesionesEnMemoria(_base);
        var filtradas = new ContrasenasFiltradasNunca();

        _registro = new ServicioRegistro(
            usuarios, perfiles, _base, _hash, _secretos, _reloj, _cola, _auditoria, filtradas);
        _sesiones = new ServicioSesion(
            usuarios, sesiones, _base, _hash, _secretos, _reloj, _auditoria);
        _almacen = new AlmacenQueRecuerda(_base);
        _cuenta = ArmadoDeCuenta.Cuenta(
            _base, _hash, _secretos, _reloj, _cola, _auditoria, _sesiones, filtradas,
            _almacen, _logBorrado, _cifrado);
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

    // ------------------------------------------- CS-01 bis: reenviar el enlace

    /// <summary>
    /// Lo mismo exista o no la cuenta, y este o no verificada. Decir «ya estaba verificado»
    /// seria decir «esa persona tiene cuenta aqui», que es lo que no puede pasar.
    /// </summary>
    [Fact]
    public async Task Reenviar_responde_igual_exista_la_cuenta_o_no_y_este_verificada_o_no()
    {
        var (_, token) = await RegistrarAsync(correo: "pablo@correo.cl");
        await RegistrarAsync(correo: "ya@correo.cl", rol: "buscador");

        var sinVerificar = await _cuenta.ReenviarVerificacionAsync("pablo@correo.cl");
        await _cuenta.VerificarCorreoAsync(token);
        var yaVerificada = await _cuenta.ReenviarVerificacionAsync("pablo@correo.cl");
        var noExiste = await _cuenta.ReenviarVerificacionAsync("nadie@correo.cl");

        Assert.True(sinVerificar.Exito);
        Assert.True(yaVerificada.Exito);
        Assert.True(noExiste.Exito);
        Assert.Equal(sinVerificar.Mensaje, yaVerificada.Mensaje);
        Assert.Equal(sinVerificar.Mensaje, noExiste.Mensaje);
    }

    /// <summary>El enlace nuevo sirve, y lo que se guarda es el hash y no el token.</summary>
    [Fact]
    public async Task Reenviar_manda_un_enlace_NUEVO_que_de_verdad_verifica()
    {
        var (id, viejo) = await RegistrarAsync();
        _cola.Encoladas.Clear();
        _reloj.Avanzar(TimeSpan.FromMinutes(5));

        await _cuenta.ReenviarVerificacionAsync("pablo@correo.cl");

        var aviso = Assert.Single(_cola.Encoladas);
        Assert.Equal(TipoNotificacion.CorreoVerificacion, aviso.Tipo);

        var nuevo = _secretos.Emitidos.Last();
        Assert.NotEqual(viejo, nuevo);
        Assert.Contains(nuevo, aviso.Cuerpo);
        Assert.Equal(_secretos.HashToken(nuevo), _base.Usuarios.Single(u => u.Id == id).TokenVerificacion);

        var r = await _cuenta.VerificarCorreoAsync(nuevo);
        Assert.True(r.Exito, r.Mensaje);
        Assert.True(_base.Usuarios.Single(u => u.Id == id).CorreoVerificado);
    }

    /// <summary>
    /// El enlace viejo deja de servir. Si siguieran valiendo los dos, cada reenvio dejaria una
    /// puerta mas abierta, y una de ellas en un correo que quiza leyo alguien mas.
    /// </summary>
    [Fact]
    public async Task El_enlace_viejo_deja_de_servir_cuando_se_manda_uno_nuevo()
    {
        var (_, viejo) = await RegistrarAsync();
        _reloj.Avanzar(TimeSpan.FromMinutes(5));

        await _cuenta.ReenviarVerificacionAsync("pablo@correo.cl");
        var conElViejo = await _cuenta.VerificarCorreoAsync(viejo);

        Assert.False(conElViejo.Exito);
    }

    /// <summary>
    /// La espera entre reenvios. El limite de peticiones protege por IP y aqui no alcanza: el
    /// daño no lo recibe quien llama, lo recibe el dueño del correo. Sin esto, alguien que
    /// conoce un correo puede llenarle la bandeja desde varias IP.
    /// </summary>
    [Fact]
    public async Task Dos_reenvios_seguidos_mandan_UN_solo_correo()
    {
        await RegistrarAsync();
        _cola.Encoladas.Clear();
        _reloj.Avanzar(TimeSpan.FromMinutes(5));

        var primero = await _cuenta.ReenviarVerificacionAsync("pablo@correo.cl");
        var segundo = await _cuenta.ReenviarVerificacionAsync("pablo@correo.cl");

        Assert.Single(_cola.Encoladas);
        // Y la respuesta es la misma: a quien insiste no se le cuenta que se le freno.
        Assert.Equal(primero.Mensaje, segundo.Mensaje);
    }

    [Fact]
    public async Task Pasada_la_espera_si_se_puede_pedir_otro()
    {
        await RegistrarAsync();
        _cola.Encoladas.Clear();
        _reloj.Avanzar(TimeSpan.FromMinutes(5));

        await _cuenta.ReenviarVerificacionAsync("pablo@correo.cl");
        _reloj.Avanzar(ServicioCuenta.EsperaEntreReenvios + TimeSpan.FromSeconds(1));
        await _cuenta.ReenviarVerificacionAsync("pablo@correo.cl");

        Assert.Equal(2, _cola.Encoladas.Count);
    }

    [Fact]
    public async Task Reenviar_a_una_cuenta_que_no_existe_no_encola_ningun_correo()
    {
        _cola.Encoladas.Clear();

        await _cuenta.ReenviarVerificacionAsync("nadie@correo.cl");

        Assert.Empty(_cola.Encoladas);
    }

    /// <summary>Y a una cuenta ya verificada tampoco: no hay nada que verificar.</summary>
    [Fact]
    public async Task Reenviar_a_una_cuenta_ya_verificada_no_encola_ningun_correo()
    {
        var (_, token) = await RegistrarAsync();
        await _cuenta.VerificarCorreoAsync(token);
        _cola.Encoladas.Clear();
        _reloj.Avanzar(TimeSpan.FromMinutes(5));

        await _cuenta.ReenviarVerificacionAsync("pablo@correo.cl");

        Assert.Empty(_cola.Encoladas);
    }

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

        Assert.DoesNotContain(_base.Sesiones, s => s.EstaVigente(_reloj.Ahora));
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

        Assert.DoesNotContain(_base.Sesiones, s => s.EstaVigente(_reloj.Ahora));
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

    // CS-04: la contraseña se confirma antes de la baja (decisión de Pablo, 17-nov-2026).

    [Fact]
    public async Task Confirmar_la_contrasena_para_eliminar_acepta_la_correcta_y_no_cambia_nada()
    {
        var (id, _) = await RegistrarAsync();
        var antes = _base.Usuarios.Single(u => u.Id == id).HashContrasena;

        var r = await _cuenta.ConfirmarBajaAsync(id, Clave);

        Assert.True(r.Exito, r.Mensaje);
        var usuario = _base.Usuarios.Single(u => u.Id == id);
        // Confirmar no borra: eso lo hace EliminarAsync, despues de cancelar citas y suscripcion.
        Assert.NotEqual(EstadoCuenta.Eliminada, usuario.Estado);
        Assert.Null(usuario.AnonimizadoEn);
        Assert.Equal(antes, usuario.HashContrasena);
    }

    [Fact]
    public async Task Confirmar_la_contrasena_para_eliminar_rechaza_otra_con_422_y_no_con_401()
    {
        var (id, _) = await RegistrarAsync();

        var r = await _cuenta.ConfirmarBajaAsync(id, "no-era-esta");

        Assert.False(r.Exito);
        // 422: la sesion es valida, lo que falla es la confirmacion. Un 401 haria que el cliente
        // intente renovar la sesion como si hubiera vencido.
        Assert.Equal(Fallas.ReglaDeNegocio, r.Codigo);
        Assert.NotEqual(EstadoCuenta.Eliminada, _base.Usuarios.Single(u => u.Id == id).Estado);
    }

    [Fact]
    public async Task Confirmar_la_contrasena_de_una_cuenta_que_no_existe_no_revienta()
    {
        var r = await _cuenta.ConfirmarBajaAsync(Guid.NewGuid(), Clave);

        Assert.False(r.Exito);
        Assert.Equal(Fallas.NoEncontrado, r.Codigo);
    }

    // ----------------------------------------------------------- CS-04: borrar de verdad (Ley 21.719)

    private const string SecretoRfc = "GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQ";

    private static string ClaveNueva(string carpeta, TipoArchivo tipo = TipoArchivo.Webp) =>
        TiposDeArchivo.NuevaClave(carpeta, tipo);

    private sealed record PrestadorCompleto(
        Guid Id, PerfilPrestador Perfil, string FotoGrande, string FotoChica,
        string FotoTrabajo, string Documento, Credencial Credencial);

    /// <summary>Un prestador con TODO lo que la baja tiene que borrar: ficha, fotos, documentos y push.</summary>
    private async Task<PrestadorCompleto> UnPrestadorConTodoAsync()
    {
        var (id, token) = await RegistrarAsync(rol: "prestador");
        await _cuenta.VerificarCorreoAsync(token);

        var perfil = _base.PerfilesPrestador.Single();
        var fotoGrande = ClaveNueva(TiposDeArchivo.CarpetaFotos);
        perfil.Estado = EstadoPerfil.Publicado;
        perfil.NombrePublico = "Juan Pérez Gasfitería";
        perfil.Slug = "juan-perez-gasfiteria";
        perfil.Descripcion = "Gasfíter con 20 años de experiencia";
        perfil.UrlFoto = TiposDeArchivo.RutaPublica + fotoGrande[(TiposDeArchivo.CarpetaFotos.Length + 1)..];
        perfil.DireccionCifrada = new byte[] { 1, 2, 3 };
        perfil.Latitud = -38.73;
        perfil.Longitud = -72.59;
        perfil.TelefonoPublico = "+56911112222";
        perfil.CorreoPublico = "juan@gasfiteria.cl";
        perfil.Whatsapp = "+56911112222";
        perfil.SitioWeb = "https://gasfiteria.cl";
        perfil.Cobertura.Add(new CoberturaPrestador { PerfilPrestadorId = perfil.Id, ZonaId = 7, EsPrincipal = true });

        var fotoTrabajo = ClaveNueva(TiposDeArchivo.CarpetaFotos);
        _base.FotosDeTrabajo.Add(new FotoTrabajo { PerfilPrestadorId = perfil.Id, ClaveAlmacen = fotoTrabajo });

        var documento = ClaveNueva(TiposDeArchivo.CarpetaDocumentos, TipoArchivo.Pdf);
        var credencial = new Credencial
        {
            PerfilPrestadorId = perfil.Id, Tipo = TipoCredencial.Titulo, Nombre = "Gasfíter clase A N° 12345",
            EntidadCertificadoraId = 3, Estado = EstadoCredencial.Verificada,
        };
        credencial.Documentos.Add(new DocumentoCredencial
        {
            CredencialId = credencial.Id, ClaveAlmacenamiento = documento, NombreOriginal = "cedula-juan-perez.pdf",
            Estado = EstadoDocumento.Disponible,
        });
        credencial.Verificaciones.Add(new Verificacion
        {
            CredencialId = credencial.Id, VerificadorId = Guid.NewGuid(), Resultado = ResultadoVerificacion.Verificada,
            Detalle = "Juan Pérez, RUT 11.111.111-1, registro 12345",
        });
        _base.Credenciales.Add(credencial);

        _base.SuscripcionesPush.Add(new SuscripcionPush { UsuarioId = id, Endpoint = "https://push/uno" });
        _base.SuscripcionesPush.Add(new SuscripcionPush { UsuarioId = id, Endpoint = "https://push/dos" });
        _base.SuscripcionesPush.Add(new SuscripcionPush { UsuarioId = Guid.NewGuid(), Endpoint = "https://push/ajeno" });

        return new PrestadorCompleto(id, perfil, fotoGrande, TiposDeArchivo.ClaveChica(fotoGrande),
            fotoTrabajo, documento, credencial);
    }

    [Fact]
    public async Task Eliminar_la_cuenta_deja_la_ficha_sin_ningun_dato_personal()
    {
        var p = await UnPrestadorConTodoAsync();

        var r = await _cuenta.EliminarAsync(p.Id);

        Assert.True(r.Exito, r.Mensaje);
        var perfil = p.Perfil;
        Assert.Same(perfil, Assert.Single(_base.PerfilesPrestador));   // la fila sobrevive: la referencian citas y reseñas
        Assert.Equal(EstadoPerfil.Despublicado, perfil.Estado);
        Assert.Equal("Perfil eliminado", perfil.NombrePublico);
        Assert.DoesNotContain("juan", perfil.Slug, StringComparison.OrdinalIgnoreCase);
        Assert.Null(perfil.Descripcion);
        Assert.Null(perfil.UrlFoto);
        Assert.Null(perfil.DireccionCifrada);
        Assert.Null(perfil.Latitud);
        Assert.Null(perfil.Longitud);
        Assert.Null(perfil.TelefonoPublico);
        Assert.Null(perfil.CorreoPublico);
        Assert.Null(perfil.Whatsapp);
        Assert.Null(perfil.SitioWeb);
        Assert.Empty(perfil.Cobertura);
    }

    [Fact]
    public async Task Eliminar_la_cuenta_borra_las_fotos_de_trabajos_y_los_documentos_y_anonimiza_las_credenciales()
    {
        var p = await UnPrestadorConTodoAsync();

        await _cuenta.EliminarAsync(p.Id);

        Assert.Empty(_base.FotosDeTrabajo);
        Assert.Empty(p.Credencial.Documentos);
        // La credencial sigue (sirve a la auditoria de la verificacion), pero ya no liga a la persona.
        Assert.Equal(EstadoCredencial.Verificada, p.Credencial.Estado);
        Assert.Equal("Credencial eliminada", p.Credencial.Nombre);
        Assert.Null(p.Credencial.EntidadCertificadoraId);
        Assert.All(p.Credencial.Verificaciones, v => Assert.Null(v.Detalle));
    }

    [Fact]
    public async Task Eliminar_la_cuenta_pide_borrar_los_dos_archivos_de_la_foto_la_foto_de_trabajo_y_el_documento()
    {
        var p = await UnPrestadorConTodoAsync();

        await _cuenta.EliminarAsync(p.Id);

        Assert.Equivalent(
            new[] { p.FotoGrande, p.FotoChica, p.FotoTrabajo, p.Documento },
            _almacen.Borrados.Select(b => b.Clave));
    }

    [Fact]
    public async Task Los_archivos_se_borran_DESPUES_de_que_la_base_confirma()
    {
        var p = await UnPrestadorConTodoAsync();

        await _cuenta.EliminarAsync(p.Id);

        // Cuando se pidio borrar cada archivo, la base ya habia hecho su ULTIMO guardado.
        Assert.NotEmpty(_almacen.Borrados);
        Assert.All(_almacen.Borrados, b => Assert.Equal(_base.Guardados, b.GuardadosAlBorrar));
    }

    [Fact]
    public async Task Eliminar_la_cuenta_borra_todos_sus_navegadores_con_push_y_solo_los_suyos()
    {
        var p = await UnPrestadorConTodoAsync();

        await _cuenta.EliminarAsync(p.Id);

        var queda = Assert.Single(_base.SuscripcionesPush);
        Assert.Equal("https://push/ajeno", queda.Endpoint);
    }

    [Fact]
    public async Task Un_buscador_sin_ficha_tambien_pierde_sus_navegadores_con_push()
    {
        var (id, _) = await RegistrarAsync();
        _base.SuscripcionesPush.Add(new SuscripcionPush { UsuarioId = id, Endpoint = "https://push/uno" });

        var r = await _cuenta.EliminarAsync(id);

        Assert.True(r.Exito, r.Mensaje);
        Assert.Empty(_base.SuscripcionesPush);
    }

    [Fact]
    public async Task Si_falla_borrar_un_archivo_la_baja_queda_hecha_y_el_error_queda_en_el_log()
    {
        var p = await UnPrestadorConTodoAsync();
        _almacen.QueFallan.Add(p.Documento);

        var r = await _cuenta.EliminarAsync(p.Id);

        Assert.True(r.Exito, r.Mensaje);
        Assert.Equal(EstadoCuenta.Eliminada, _base.Usuarios.Single(u => u.Id == p.Id).Estado);
        // Siguio con los demas archivos, y dejo el error anotado con la clave (sin datos de la persona).
        Assert.Equal(4, _almacen.Borrados.Count);
        var error = Assert.Single(_logBorrado.Entradas, e => e.Nivel == Microsoft.Extensions.Logging.LogLevel.Error);
        Assert.Contains(p.Documento, error.Mensaje);
    }

    [Fact]
    public async Task Eliminar_la_cuenta_deja_constancia_en_auditoria_de_lo_borrado_sin_el_contenido()
    {
        var p = await UnPrestadorConTodoAsync();
        _auditoria.Registros.Clear();

        await _cuenta.EliminarAsync(p.Id);

        var e = Assert.Single(_auditoria.Registros, a => a.Accion == "baja_borrar_datos_perfil");
        Assert.Equal(p.Perfil.Id, e.EntidadId);
        Assert.DoesNotContain("Juan", e.Contexto ?? "");
    }

    [Fact]
    public async Task Despublicar_para_la_baja_saca_la_ficha_de_la_busqueda_y_guarda()
    {
        var p = await UnPrestadorConTodoAsync();
        var guardados = _base.Guardados;

        var r = await _cuenta.DespublicarParaBajaAsync(p.Id);

        Assert.True(r.Exito);
        Assert.Equal(EstadoPerfil.Despublicado, p.Perfil.Estado);
        Assert.Equal(guardados + 1, _base.Guardados);
        // Todavia no borra nada: solo cierra la puerta mientras se cancelan las citas.
        Assert.Equal("Juan Pérez Gasfitería", p.Perfil.NombrePublico);
        Assert.Empty(_almacen.Borrados);
    }

    // ----------------------------------------------------------- CS-04: MFA e intentos en la baja

    private Usuario ConMfaActivo(Guid id)
    {
        var u = _base.Usuarios.Single(x => x.Id == id);
        u.MfaSecretoCifrado = _cifrado.Cifrar(SecretoRfc);
        u.MfaActivadoEn = _reloj.Ahora;
        u.MfaCodigosRespaldo = Array.Empty<string>();
        return u;
    }

    private string CodigoVigente() => Totp.Codigo(SecretoRfc, Totp.PasoDe(_reloj.Ahora));

    [Fact]
    public async Task Con_mfa_la_baja_sin_codigo_falla_y_suma_al_contador_de_fallos()
    {
        var (id, _) = await RegistrarAsync();
        var u = ConMfaActivo(id);

        var r = await _cuenta.ConfirmarBajaAsync(id, Clave, codigo: null);

        Assert.False(r.Exito);
        Assert.Equal(Fallas.ReglaDeNegocio, r.Codigo);
        Assert.Equal(1, u.IntentosFallidos);
        Assert.NotEqual(EstadoCuenta.Eliminada, u.Estado);
    }

    [Fact]
    public async Task Con_mfa_la_baja_con_un_codigo_malo_falla_y_suma()
    {
        var (id, _) = await RegistrarAsync();
        var u = ConMfaActivo(id);

        var r = await _cuenta.ConfirmarBajaAsync(id, Clave, "000000");

        Assert.False(r.Exito);
        Assert.Equal(1, u.IntentosFallidos);
    }

    [Fact]
    public async Task Con_mfa_la_baja_con_contrasena_y_codigo_buenos_funciona_y_gasta_el_codigo()
    {
        var (id, _) = await RegistrarAsync();
        var u = ConMfaActivo(id);

        var r = await _cuenta.ConfirmarBajaAsync(id, Clave, CodigoVigente());

        Assert.True(r.Exito, r.Mensaje);
        Assert.Equal(0, u.IntentosFallidos);
        // El mismo codigo no sirve dos veces (se gasto de forma atomica).
        Assert.False((await _cuenta.ConfirmarBajaAsync(id, Clave, CodigoVigente())).Exito);
    }

    [Fact]
    public async Task Con_mfa_un_codigo_de_respaldo_tambien_confirma_la_baja_y_se_gasta()
    {
        var (id, _) = await RegistrarAsync();
        var u = _base.Usuarios.Single(x => x.Id == id);
        // Se activa de verdad, para que ServicioMfa arme los codigos de respaldo como los arma siempre.
        var mfa = new ServicioMfa(new RepositorioUsuariosEnMemoria(_base), _base, _hash, _secretos, _cifrado,
            _reloj, _auditoria, _sesiones, _cola);
        var alta = await mfa.IniciarAltaAsync(id);
        var codigos = (await mfa.ConfirmarAltaAsync(id, new ConfirmarMfaCmd(
            Clave, Totp.Codigo(alta.Valor!.Secreto, Totp.PasoDe(_reloj.Ahora))))).Valor!.Codigos;
        _reloj.Avanzar(TimeSpan.FromSeconds(60));

        var r = await _cuenta.ConfirmarBajaAsync(id, Clave, codigos[0]);

        Assert.True(r.Exito, r.Mensaje);
        Assert.Equal(9, u.MfaCodigosRespaldo!.Length);
    }

    [Fact]
    public async Task Sin_mfa_la_baja_sigue_pidiendo_solo_la_contrasena()
    {
        var (id, _) = await RegistrarAsync();

        var r = await _cuenta.ConfirmarBajaAsync(id, Clave);

        Assert.True(r.Exito, r.Mensaje);
    }

    [Fact]
    public async Task Una_contrasena_mala_en_la_baja_suma_al_contador_de_fallos()
    {
        var (id, _) = await RegistrarAsync();
        var u = _base.Usuarios.Single(x => x.Id == id);

        await _cuenta.ConfirmarBajaAsync(id, "no-era-esta");
        await _cuenta.ConfirmarBajaAsync(id, "tampoco-esta");

        Assert.Equal(2, u.IntentosFallidos);
    }

    [Fact]
    public async Task Diez_fallos_en_la_baja_bloquean_la_cuenta_y_el_bloqueo_rige_aunque_todo_sea_correcto()
    {
        var (id, _) = await RegistrarAsync();
        var u = ConMfaActivo(id);
        for (var i = 0; i < ServicioSesion.IntentosParaBloquear; i++)
            await _cuenta.ConfirmarBajaAsync(id, "no-era-esta");
        Assert.True(u.EstaBloqueada(_reloj.Ahora));

        var r = await _cuenta.ConfirmarBajaAsync(id, Clave, CodigoVigente());

        Assert.False(r.Exito);
        // Bloqueada no se mira el codigo, ni se gasta: sigue valido para cuando se levante el bloqueo.
        Assert.Null(u.MfaUltimoPaso);
    }

    [Fact]
    public async Task El_fallo_de_la_baja_no_dice_si_fallo_la_contrasena_o_el_codigo()
    {
        var (id, _) = await RegistrarAsync();
        ConMfaActivo(id);

        var contrasenaMala = await _cuenta.ConfirmarBajaAsync(id, "no-era-esta", CodigoVigente());
        var codigoMalo = await _cuenta.ConfirmarBajaAsync(id, Clave, "000000");
        var sinCodigo = await _cuenta.ConfirmarBajaAsync(id, Clave, null);

        Assert.Equal(contrasenaMala.Mensaje, codigoMalo.Mensaje);
        Assert.Equal(contrasenaMala.Mensaje, sinCodigo.Mensaje);
        Assert.Equal(contrasenaMala.Codigo, codigoMalo.Codigo);
    }

    [Fact]
    public async Task Una_contrasena_actual_mala_al_cambiar_la_contrasena_suma_al_contador_de_fallos()
    {
        var (id, _) = await RegistrarAsync();
        var u = _base.Usuarios.Single(x => x.Id == id);

        var r = await _cuenta.CambiarContrasenaAsync(id, "no-era-esta", "otra-contrasena-nueva-9");

        Assert.False(r.Exito);
        Assert.Equal(1, u.IntentosFallidos);
    }

    [Fact]
    public async Task Cambiar_la_contrasena_con_la_cuenta_bloqueada_no_funciona_ni_con_la_correcta()
    {
        var (id, _) = await RegistrarAsync();
        var u = _base.Usuarios.Single(x => x.Id == id);
        u.BloqueadaHasta = _reloj.Ahora.AddMinutes(5);
        var hashAntes = u.HashContrasena;

        var r = await _cuenta.CambiarContrasenaAsync(id, Clave, "otra-contrasena-nueva-9");

        Assert.False(r.Exito);
        Assert.Equal(hashAntes, u.HashContrasena);
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

    // ------------------------------------------------ sello de seguridad (22-nov-2026)

    [Fact]
    public async Task Cambiar_la_contrasena_renueva_el_sello_de_seguridad()
    {
        var (id, _) = await RegistrarAsync();
        var antes = _base.Usuarios.Single(u => u.Id == id).SelloSeguridad;

        var r = await _cuenta.CambiarContrasenaAsync(id, Clave, "clave-nueva-larga");

        Assert.True(r.Exito, r.Mensaje);
        Assert.NotEqual(antes, _base.Usuarios.Single(u => u.Id == id).SelloSeguridad);
    }

    [Fact]
    public async Task Cambiar_la_contrasena_con_la_actual_mala_no_renueva_el_sello()
    {
        var (id, _) = await RegistrarAsync();
        var antes = _base.Usuarios.Single(u => u.Id == id).SelloSeguridad;

        var r = await _cuenta.CambiarContrasenaAsync(id, "no-era-esta", "clave-nueva-larga");

        Assert.False(r.Exito);
        Assert.Equal(antes, _base.Usuarios.Single(u => u.Id == id).SelloSeguridad);
    }

    [Fact]
    public async Task Restablecer_la_contrasena_renueva_el_sello_de_seguridad()
    {
        var (id, _) = await RegistrarAsync();
        var antes = _base.Usuarios.Single(u => u.Id == id).SelloSeguridad;
        await _cuenta.PedirRecuperacionAsync("pablo@correo.cl");
        var token = _secretos.Emitidos.Last();

        var r = await _cuenta.RestablecerContrasenaAsync(token, "clave-nueva-larga");

        Assert.True(r.Exito, r.Mensaje);
        Assert.NotEqual(antes, _base.Usuarios.Single(u => u.Id == id).SelloSeguridad);
    }

    [Fact]
    public async Task Eliminar_la_cuenta_renueva_el_sello_de_seguridad()
    {
        var (id, _) = await RegistrarAsync();
        var antes = _base.Usuarios.Single(u => u.Id == id).SelloSeguridad;

        var r = await _cuenta.EliminarAsync(id);

        Assert.True(r.Exito, r.Mensaje);
        Assert.NotEqual(antes, _base.Usuarios.Single(u => u.Id == id).SelloSeguridad);
    }

    [Fact]
    public async Task Cerrar_todas_las_sesiones_con_corte_de_acceso_renueva_el_sello_y_sin_el_no()
    {
        var (id, _) = await RegistrarAsync();
        var antes = _base.Usuarios.Single(u => u.Id == id).SelloSeguridad;

        await _sesiones.RevocarTodasAsync(id);
        Assert.Equal(antes, _base.Usuarios.Single(u => u.Id == id).SelloSeguridad);

        await _sesiones.RevocarTodasAsync(id, cortarAcceso: true);
        Assert.NotEqual(antes, _base.Usuarios.Single(u => u.Id == id).SelloSeguridad);
    }

    [Fact]
    public async Task La_sesion_abierta_lleva_el_sello_vigente_de_la_cuenta()
    {
        var (id, _) = await RegistrarAsync();

        var sesion = (await _sesiones.IniciarAsync(new IniciarSesionCmd("pablo@correo.cl", Clave))).Valor!;

        Assert.Equal(_base.Usuarios.Single(u => u.Id == id).SelloSeguridad, sesion.SelloSeguridad);
    }
}
