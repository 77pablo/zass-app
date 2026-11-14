using System.Text.Json;
using Zass.Aplicacion.Identidad;
using Zass.Aplicacion.Pruebas.Dobles;
using Zass.Aplicacion.Puertos;
using Zass.Aplicacion.Resenas;
using Zass.Dominio.Agenda;
using Zass.Dominio.Comun;
using Zass.Dominio.Identidad;
using Zass.Dominio.Reputacion;

namespace Zass.Aplicacion.Pruebas.Resenas;

/// <summary>
/// MOD-11 · S5 sin base de datos: elegibilidad, publicar, editar, responder, reportar y moderar.
/// Lo que estas pruebas NO pueden decir (cerrojo, índices únicos, el SQL del recálculo) se
/// comprueba contra PostgreSQL en la capa de integración.
/// </summary>
public class ResenasPruebas
{
    private static readonly DateTime Ahora = new(2026, 11, 7, 12, 0, 0, DateTimeKind.Utc);
    private const string Comentario = "Excelente atención, llegaron puntuales y explicaron todo con claridad.";

    private readonly BaseEnMemoria _base = new();
    private readonly RelojFijo _reloj = new(Ahora);
    private readonly AuditoriaEnMemoria _auditoria = new();
    private readonly ColaEnMemoria _avisos = new();
    private readonly RepositorioResenasEnMemoria _repo;
    private readonly ServicioResenas _servicio;

    private readonly Usuario _ana;          // buscadora, la que opina
    private readonly Usuario _luis;         // dueño del perfil reseñado
    private readonly Usuario _mod;          // moderadora
    private readonly PerfilPrestador _perfil;

    public ResenasPruebas()
    {
        _repo = new RepositorioResenasEnMemoria(_base);
        var identidad = new ServicioIdentidad(
            new RepositorioUsuariosEnMemoria(_base), new RepositorioPerfilesEnMemoria(_base));
        _servicio = new ServicioResenas(_repo, identidad, _base, _reloj, _auditoria, _avisos);

        _ana = UnUsuario("buscador");
        _luis = UnUsuario("prestador");
        _mod = UnUsuario("moderador");
        _perfil = UnPerfil(_luis);
    }

    // ------------------------------------------------------------- ayudas

    private Usuario UnUsuario(string rol, bool verificado = true)
    {
        var u = new Usuario
        {
            Id = Guid.NewGuid(), Correo = $"{Guid.NewGuid():N}@zass.local", Nombre = "Persona",
            CorreoVerificado = verificado, Estado = verificado ? EstadoCuenta.Activa : EstadoCuenta.PendienteVerificacion,
        };
        u.Roles.Add(new UsuarioRol { UsuarioId = u.Id, Rol = _base.Roles.First(r => r.Codigo == rol) });
        _base.Usuarios.Add(u);
        return u;
    }

    private PerfilPrestador UnPerfil(Usuario dueno, EstadoPerfil estado = EstadoPerfil.Publicado)
    {
        var p = new PerfilPrestador
        {
            Id = Guid.NewGuid(), UsuarioId = dueno.Id, Estado = estado,
            NombrePublico = "Estudio " + Guid.NewGuid().ToString("N")[..6], Slug = Guid.NewGuid().ToString("N"),
        };
        _base.PerfilesPrestador.Add(p);
        return p;
    }

    private Contacto UnContacto(Usuario? buscador = null, PerfilPrestador? perfil = null, TimeSpan? hace = null, Cita? cita = null)
    {
        var c = new Contacto
        {
            BuscadorId = (buscador ?? _ana).Id, PerfilPrestadorId = (perfil ?? _perfil).Id,
            Origen = cita is null ? OrigenContacto.Ficha : OrigenContacto.Cita, Canal = CanalContacto.Whatsapp,
            OcurridoEn = Ahora - (hace ?? TimeSpan.FromDays(8)), CitaId = cita?.Id,
        };
        _repo.Contactos.Add(c);
        return c;
    }

    private Cita UnaCita(EstadoCita estado, Usuario? buscador = null, PerfilPrestador? perfil = null)
    {
        var c = new Cita
        {
            Id = Guid.NewGuid(), BuscadorId = (buscador ?? _ana).Id,
            PerfilPrestadorId = (perfil ?? _perfil).Id, Estado = estado,
        };
        _repo.Citas.Add(c);
        return c;
    }

    private PublicarResenaCmd Pub(Contacto c, int cal = 5, string? texto = null, string[]? aspectos = null,
        bool enviarIgual = false, string clave = "clave-1", Usuario? autor = null) =>
        new((autor ?? _ana).Id, c.Id, cal, texto ?? Comentario, aspectos, enviarIgual, clave);

    private async Task<Resena> UnaPublicada(Usuario? autor = null, int cal = 5, PerfilPrestador? perfil = null, DateTime? cuando = null)
    {
        var a = autor ?? _ana;
        var c = UnContacto(a, perfil);
        var r = await _servicio.PublicarAsync(Pub(c, cal, autor: a, clave: Guid.NewGuid().ToString()));
        Assert.True(r.Exito, r.Mensaje);
        var resena = _repo.Resenas.Single(x => x.Id == r.Valor!.Resena!.Id);
        if (cuando is not null) resena.PublicadaEn = cuando.Value;
        return resena;
    }

    private static string Texto(int largo) => new string('a', largo);

    private PerfilPrestador Recargado() => _base.PerfilesPrestador.First(p => p.Id == _perfil.Id);

    // ------------------------------------------------------------- API-46 · contacto

    [Fact]
    public async Task Registrar_un_contacto_lo_guarda_con_la_hora_del_reloj()
    {
        var r = await _servicio.RegistrarContactoAsync(_ana.Id, _perfil.Id, OrigenContacto.Ficha, CanalContacto.Telefono);

        Assert.True(r.Exito);
        var c = Assert.Single(_repo.Contactos);
        Assert.Equal(r.Valor, c.Id);
        Assert.Equal(Ahora, c.OcurridoEn);
        Assert.Equal(_ana.Id, c.BuscadorId);
        Assert.Equal(CanalContacto.Telefono, c.Canal);
        Assert.Null(c.CitaId);
    }

    [Fact]
    public async Task Un_segundo_contacto_devuelve_el_primero_y_no_reinicia_el_reloj_de_7_dias()
    {
        var primero = await _servicio.RegistrarContactoAsync(_ana.Id, _perfil.Id, OrigenContacto.Ficha, CanalContacto.Telefono);
        _reloj.Avanzar(TimeSpan.FromDays(3));

        var segundo = await _servicio.RegistrarContactoAsync(_ana.Id, _perfil.Id, OrigenContacto.Busqueda, CanalContacto.Correo);

        Assert.Equal(primero.Valor, segundo.Valor);
        Assert.Equal(Ahora, Assert.Single(_repo.Contactos).OcurridoEn);
    }

    [Fact]
    public async Task Despues_de_reseñar_un_contacto_nuevo_abre_otra_interaccion()
    {
        var c = UnContacto();
        await _servicio.PublicarAsync(Pub(c));

        var nuevo = await _servicio.RegistrarContactoAsync(_ana.Id, _perfil.Id, OrigenContacto.Ficha, CanalContacto.Telefono);

        Assert.NotEqual(c.Id, nuevo.Valor);
        Assert.Equal(2, _repo.Contactos.Count);
    }

    [Fact]
    public async Task Contactarse_con_uno_mismo_se_rechaza()
    {
        var r = await _servicio.RegistrarContactoAsync(_luis.Id, _perfil.Id, OrigenContacto.Ficha, CanalContacto.Telefono);

        // Luis no es buscador, así que cae antes por la cuenta; con rol de buscador cae por mismo-perfil.
        Assert.False(r.Exito);
        Assert.Empty(_repo.Contactos);
    }

    [Fact]
    public async Task Un_buscador_dueño_de_ese_perfil_no_puede_contactarse_consigo_mismo()
    {
        _luis.Roles.Add(new UsuarioRol { UsuarioId = _luis.Id, Rol = _base.Roles.First(r => r.Codigo == "buscador") });

        var r = await _servicio.RegistrarContactoAsync(_luis.Id, _perfil.Id, OrigenContacto.Ficha, CanalContacto.Telefono);

        Assert.False(r.Exito);
        Assert.Equal(Fallas.ReglaDeNegocio, r.Codigo);
        Assert.Empty(_repo.Contactos);
    }

    [Fact]
    public async Task Un_perfil_sin_publicar_o_inexistente_no_se_puede_contactar()
    {
        var borrador = UnPerfil(UnUsuario("prestador"), EstadoPerfil.Borrador);

        var a = await _servicio.RegistrarContactoAsync(_ana.Id, borrador.Id, OrigenContacto.Ficha, CanalContacto.Telefono);
        var b = await _servicio.RegistrarContactoAsync(_ana.Id, Guid.NewGuid(), OrigenContacto.Ficha, CanalContacto.Telefono);

        Assert.Equal(Fallas.NoEncontrado, a.Codigo);
        Assert.Equal(Fallas.NoEncontrado, b.Codigo);
        Assert.Empty(_repo.Contactos);
    }

    [Fact]
    public async Task Sin_correo_verificado_o_sin_rol_de_buscador_no_se_registra_contacto()
    {
        var sinVerificar = UnUsuario("buscador", verificado: false);

        var a = await _servicio.RegistrarContactoAsync(sinVerificar.Id, _perfil.Id, OrigenContacto.Ficha, CanalContacto.Telefono);
        var b = await _servicio.RegistrarContactoAsync(_mod.Id, _perfil.Id, OrigenContacto.Ficha, CanalContacto.Telefono);

        Assert.Equal(FallasResenas.NoElegible, a.Codigo);
        Assert.Equal(FallasResenas.NoElegible, b.Codigo);
        Assert.Empty(_repo.Contactos);
    }

    [Fact]
    public async Task El_origen_cita_exige_cita_y_la_cita_exige_el_origen_cita()
    {
        var cita = UnaCita(EstadoCita.Realizada);

        var sinCita = await _servicio.RegistrarContactoAsync(_ana.Id, _perfil.Id, OrigenContacto.Cita, CanalContacto.Formulario);
        var citaSinOrigen = await _servicio.RegistrarContactoAsync(_ana.Id, _perfil.Id, OrigenContacto.Ficha, CanalContacto.Formulario, cita.Id);

        Assert.Equal(Fallas.Validacion, sinCita.Codigo);
        Assert.Equal(Fallas.Validacion, citaSinOrigen.Codigo);
        Assert.Empty(_repo.Contactos);
    }

    [Fact]
    public async Task Una_cita_ajena_no_sirve_para_un_contacto()
    {
        var deOtra = UnaCita(EstadoCita.Realizada, buscador: UnUsuario("buscador"));
        var deOtroPerfil = UnaCita(EstadoCita.Realizada, perfil: UnPerfil(UnUsuario("prestador")));

        var a = await _servicio.RegistrarContactoAsync(_ana.Id, _perfil.Id, OrigenContacto.Cita, CanalContacto.Formulario, deOtra.Id);
        var b = await _servicio.RegistrarContactoAsync(_ana.Id, _perfil.Id, OrigenContacto.Cita, CanalContacto.Formulario, deOtroPerfil.Id);

        Assert.Equal(Fallas.NoEncontrado, a.Codigo);
        Assert.Equal(Fallas.NoEncontrado, b.Codigo);
        Assert.Empty(_repo.Contactos);
    }

    [Fact]
    public async Task Una_cita_tiene_un_solo_contacto_aunque_ya_se_haya_reseñado()
    {
        var cita = UnaCita(EstadoCita.Realizada);
        var primero = await _servicio.RegistrarContactoAsync(_ana.Id, _perfil.Id, OrigenContacto.Cita, CanalContacto.Formulario, cita.Id);
        await _servicio.PublicarAsync(Pub(_repo.Contactos.Single()));

        var otra = await _servicio.RegistrarContactoAsync(_ana.Id, _perfil.Id, OrigenContacto.Cita, CanalContacto.Formulario, cita.Id);

        Assert.Equal(primero.Valor, otra.Valor);
        Assert.Single(_repo.Contactos);
    }

    // ------------------------------------------------------------- API-47 · elegibilidad

    private async Task<ResultadoElegibilidad> Elegibilidad(Usuario? quien = null, PerfilPrestador? perfil = null)
    {
        var r = await _servicio.EsElegibleAsync((quien ?? _ana).Id, (perfil ?? _perfil).Id);
        Assert.True(r.Exito, r.Mensaje);
        return r.Valor!;
    }

    [Fact]
    public async Task Sin_contacto_ni_cita_no_puede_reseñar()
    {
        var e = await Elegibilidad();

        Assert.False(e.Elegible);
        Assert.Equal(MotivoDeNoElegibilidad.SinContacto, e.Motivo);
        Assert.NotNull(e.Mensaje);
    }

    [Theory]
    [InlineData(7 * 24 * 60 * 60 - 1, false)]    // 1 segundo antes de cumplir 7 días
    [InlineData(7 * 24 * 60 * 60, true)]         // justo los 7 días
    [InlineData(7 * 24 * 60 * 60 + 1, true)]
    public async Task Un_contacto_sin_cita_habilita_exactamente_a_los_7_dias(int segundos, bool elegible)
    {
        var c = UnContacto(hace: TimeSpan.FromSeconds(segundos));

        var e = await Elegibilidad();

        Assert.Equal(elegible, e.Elegible);
        if (elegible) Assert.Equal(c.Id, e.ContactoId);
        else
        {
            Assert.Equal(MotivoDeNoElegibilidad.VentanaDeContacto, e.Motivo);
            Assert.Equal(c.OcurridoEn.AddDays(7), e.PuedeDesde);
        }
    }

    [Fact]
    public async Task La_fecha_exacta_desde_la_que_podra_reseñar_va_en_el_mensaje()
    {
        UnContacto(hace: TimeSpan.FromDays(2));

        var e = await Elegibilidad();

        Assert.Equal(Ahora.AddDays(5), e.PuedeDesde);
        Assert.Contains("12 de noviembre de 2026", e.Mensaje);
    }

    [Theory]
    [InlineData(EstadoCita.Realizada, true)]
    [InlineData(EstadoCita.Confirmada, false)]
    [InlineData(EstadoCita.Solicitada, false)]
    [InlineData(EstadoCita.PendienteAsistencia, false)]
    [InlineData(EstadoCita.Cancelada, false)]
    [InlineData(EstadoCita.Rechazada, false)]
    [InlineData(EstadoCita.Reprogramada, false)]
    [InlineData(EstadoCita.NoAsistio, false)]
    public async Task La_cita_solo_habilita_cuando_quedo_realizada(EstadoCita estado, bool elegible)
    {
        // El contacto de una cita habilita al instante, sin esperar los 7 días.
        UnContacto(hace: TimeSpan.FromMinutes(5), cita: UnaCita(estado));

        var e = await Elegibilidad();

        Assert.Equal(elegible, e.Elegible);
    }

    [Fact]
    public async Task Una_cita_con_inasistencia_se_informa_como_tal_y_no_habilita()
    {
        // Ya NO es una decisión pendiente (se tomó el 11-nov-2026). Esta cita queda en
        // `no_asistio` sin que nadie haya acusado a nadie, que es el caso de las filas
        // anteriores a la regla: se cae del lado que no habilita. Quién faltó y qué pasa en
        // cada caso está en ElegibilidadPorInasistenciaPruebas.
        UnContacto(cita: UnaCita(EstadoCita.NoAsistio));

        var e = await Elegibilidad();

        Assert.Equal(MotivoDeNoElegibilidad.CitaNoAsistio, e.Motivo);
    }

    [Fact]
    public async Task Una_cita_realizada_no_depende_de_los_7_dias_pero_un_contacto_si()
    {
        UnContacto(hace: TimeSpan.FromDays(1));                                              // solo contacto: espera
        var porCita = UnContacto(hace: TimeSpan.FromMinutes(1), cita: UnaCita(EstadoCita.Realizada));

        var e = await Elegibilidad();

        Assert.True(e.Elegible);
        Assert.Equal(porCita.Id, e.ContactoId);
    }

    [Theory]
    [InlineData(89, false)]
    [InlineData(90, true)]
    [InlineData(91, true)]
    public async Task Por_contacto_solo_una_reseña_por_prestador_cada_90_dias(int diasDesdeLaUltima, bool elegible)
    {
        var anterior = await UnaPublicada();
        anterior.PublicadaEn = Ahora.AddDays(-diasDesdeLaUltima);
        var nuevo = UnContacto();

        var e = await Elegibilidad();

        Assert.Equal(elegible, e.Elegible);
        if (!elegible)
        {
            Assert.Equal(MotivoDeNoElegibilidad.TopeDe90Dias, e.Motivo);
            Assert.Equal(anterior.PublicadaEn.AddDays(90), e.PuedeDesde);
            Assert.Equal(nuevo.Id, e.ContactoId);
        }
    }

    [Fact]
    public async Task El_tope_de_90_dias_sigue_corriendo_aunque_la_reseña_anterior_se_retire()
    {
        var anterior = await UnaPublicada();
        anterior.Retirar();
        UnContacto();

        var e = await Elegibilidad();

        Assert.Equal(MotivoDeNoElegibilidad.TopeDe90Dias, e.Motivo);
    }

    [Fact]
    public async Task El_tope_de_90_dias_no_aplica_a_una_cita_realizada_ni_cuenta_las_reseñas_por_cita()
    {
        var porCita = UnContacto(cita: UnaCita(EstadoCita.Realizada), hace: TimeSpan.FromDays(1));
        var primera = await _servicio.PublicarAsync(Pub(porCita));
        Assert.True(primera.Exito);

        // La reseña por cita no cierra la puerta al contacto suelto de después...
        UnContacto();
        var sinCita = await Elegibilidad();
        Assert.True(sinCita.Elegible);

        // ...y otra cita realizada habilita de inmediato aunque acabe de reseñar por contacto.
        await _servicio.PublicarAsync(Pub(_repo.Contactos.Last(), clave: "k2"));
        var otraCita = UnContacto(cita: UnaCita(EstadoCita.Realizada), hace: TimeSpan.FromMinutes(5));
        Assert.True((await Elegibilidad()).Elegible);
        Assert.Equal(otraCita.Id, (await Elegibilidad()).ContactoId);
    }

    [Fact]
    public async Task El_prestador_no_puede_reseñarse_a_si_mismo_y_el_mensaje_no_dice_por_que()
    {
        _luis.Roles.Add(new UsuarioRol { UsuarioId = _luis.Id, Rol = _base.Roles.First(r => r.Codigo == "buscador") });
        UnContacto(buscador: _luis);

        var e = await Elegibilidad(_luis);

        Assert.False(e.Elegible);
        Assert.Equal(MotivoDeNoElegibilidad.Autoresena, e.Motivo);
        Assert.DoesNotContain("propio", e.Mensaje!, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("mism", e.Mensaje!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Una_cuenta_sin_verificar_o_que_no_es_de_buscador_no_es_elegible()
    {
        var sinVerificar = UnUsuario("buscador", verificado: false);
        UnContacto(buscador: sinVerificar);

        var a = await Elegibilidad(sinVerificar);
        var b = await Elegibilidad(_mod);

        Assert.Equal(MotivoDeNoElegibilidad.CuentaNoHabilitada, a.Motivo);
        Assert.Equal(MotivoDeNoElegibilidad.CuentaNoHabilitada, b.Motivo);
    }

    [Fact]
    public async Task Un_prestador_inexistente_es_404()
    {
        var r = await _servicio.EsElegibleAsync(_ana.Id, Guid.NewGuid());

        Assert.Equal(Fallas.NoEncontrado, r.Codigo);
    }

    [Fact]
    public async Task Con_varios_contactos_ofrece_el_elegible_y_si_ninguno_lo_es_el_que_antes_se_cumple()
    {
        var masNuevo = UnContacto(hace: TimeSpan.FromDays(1));
        var masViejo = UnContacto(hace: TimeSpan.FromDays(4));
        var e = await Elegibilidad();
        Assert.Equal(masViejo.OcurridoEn.AddDays(7), e.PuedeDesde);
        Assert.NotEqual(masNuevo.OcurridoEn.AddDays(7), e.PuedeDesde);

        var listo = UnContacto(hace: TimeSpan.FromDays(30));
        Assert.Equal(listo.Id, (await Elegibilidad()).ContactoId);
    }

    [Fact]
    public async Task Lo_que_ya_tiene_reseña_no_cuenta_como_contacto_pendiente()
    {
        var c = UnContacto();
        await _servicio.PublicarAsync(Pub(c));

        var e = await Elegibilidad();

        Assert.Equal(MotivoDeNoElegibilidad.SinContacto, e.Motivo);
    }

    // ------------------------------------------------------------- API-48 · publicar

    [Fact]
    public async Task Publicar_crea_la_reseña_cruda_con_72_horas_de_edicion()
    {
        var c = UnContacto();
        var texto = "  <b>Muy bien</b> & recomendado: puntuales y claros en todo.  ";

        var r = await _servicio.PublicarAsync(Pub(c, 4, texto, new[] { "Puntualidad", "claridad", "puntualidad" }));

        Assert.True(r.Exito, r.Mensaje);
        Assert.False(r.Valor!.YaExistia);
        var resena = Assert.Single(_repo.Resenas);
        Assert.Equal(r.Valor.Resena!.Id, resena.Id);
        Assert.Equal(c.Id, resena.ContactoId);
        Assert.Equal(_ana.Id, resena.AutorId);
        Assert.Equal(_perfil.Id, resena.PerfilPrestadorId);
        Assert.Equal(4, resena.Calificacion);
        // RN-S5-04: recortada y NADA más. Ni se quita el HTML ni se escapa: eso lo hace quien renderiza.
        Assert.Equal("<b>Muy bien</b> & recomendado: puntuales y claros en todo.", resena.Comentario);
        Assert.Equal(new[] { "puntualidad", "claridad" }, resena.Aspectos);
        Assert.Equal(EstadoResena.Publicada, resena.Estado);
        Assert.Equal(TipoResena.Servicio, resena.Tipo);
        Assert.False(resena.Editada);
        Assert.Equal(Ahora, resena.PublicadaEn);
        Assert.Equal(Ahora.AddHours(72), resena.EditableHasta);
    }

    /// <summary>
    /// CU-16 4C.3: «El comentario se marca visiblemente como "reseña por inasistencia"». Ese
    /// rótulo sale de <c>Tipo</c> en la FILA, así que lo que hay que mirar es la reseña
    /// publicada, no lo que contestó la elegibilidad.
    ///
    /// <para>
    /// Esta prueba nació de una mutación, el 12-nov-2026: devolver `Tipo = elegibilidad.Tipo` al
    /// `TipoResena.Servicio` fijo que había antes dejaba las 709 unitarias EN VERDE. Era el único
    /// sitio del sistema donde se escribe `Inasistencia` y no lo vigilaba nada: la elegibilidad
    /// tenía prueba, el valor que de verdad se guarda no.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Con_el_prestador_ausente_la_resena_publicada_nace_marcada_por_inasistencia()
    {
        var cita = UnaCita(EstadoCita.NoAsistio);
        cita.QuienFalto = FaltaEnCita.Prestador;
        var c = UnContacto(hace: TimeSpan.FromMinutes(5), cita: cita);

        var r = await _servicio.PublicarAsync(Pub(c));

        Assert.True(r.Exito, r.Mensaje);
        Assert.Equal(TipoResena.Inasistencia, Assert.Single(_repo.Resenas).Tipo);
    }

    /// <summary>
    /// El contraste, en la misma forma: una cita realizada publica una reseña de SERVICIO. Sin
    /// el par, poner `Inasistencia` fijo en vez de `Servicio` fijo volvería a pasar inadvertido.
    /// </summary>
    [Fact]
    public async Task Con_la_cita_realizada_la_resena_publicada_nace_de_servicio()
    {
        var c = UnContacto(hace: TimeSpan.FromMinutes(5), cita: UnaCita(EstadoCita.Realizada));

        var r = await _servicio.PublicarAsync(Pub(c));

        Assert.True(r.Exito, r.Mensaje);
        Assert.Equal(TipoResena.Servicio, Assert.Single(_repo.Resenas).Tipo);
    }

    [Fact]
    public async Task Publicar_recalcula_la_reputacion_toma_el_cerrojo_y_confirma_una_sola_transaccion()
    {
        var c = UnContacto();

        await _servicio.PublicarAsync(Pub(c, 4));

        Assert.Equal(1, _repo.Cerrojos);
        Assert.Equal(1, _repo.Recalculos);
        Assert.Equal(1, _repo.Confirmadas);
        Assert.Equal(0, _repo.Deshechas);
        Assert.Equal(1, Recargado().TotalResenas);
        Assert.Equal(4.0m, Recargado().PromedioCalificacion);
    }

    [Fact]
    public async Task El_promedio_es_simple_con_un_decimal_sin_ponderacion()
    {
        // RN-S5-10. 5 + 5 + 4 = 14 / 3 = 4,666... -> 4,7
        await UnaPublicada(cal: 5);
        await UnaPublicada(autor: UnUsuario("buscador"), cal: 5);
        await UnaPublicada(autor: UnUsuario("buscador"), cal: 4);

        Assert.Equal(3, Recargado().TotalResenas);
        Assert.Equal(4.7m, Recargado().PromedioCalificacion);
    }

    [Fact]
    public async Task Publicar_avisa_al_prestador_una_vez_con_clave_determinista_y_sin_nombrar_al_autor()
    {
        var c = UnContacto();

        await _servicio.PublicarAsync(Pub(c));

        var aviso = Assert.Single(_avisos.Encoladas);
        Assert.Equal(TipoNotificacion.ResenaNueva, aviso.Tipo);
        Assert.Equal(_luis.Id, aviso.DestinatarioId);
        Assert.Equal($"resena-nueva:{_repo.Resenas.Single().Id}", aviso.ClaveIdempotencia);
        Assert.Equal("Tienes una reseña nueva", aviso.Asunto);
        Assert.DoesNotContain(Comentario, aviso.Cuerpo);
    }

    [Fact]
    public async Task Publicar_deja_traza_de_auditoria()
    {
        var c = UnContacto();

        await _servicio.PublicarAsync(Pub(c, 3));

        var a = Assert.Single(_auditoria.Registros);
        Assert.Equal("resena.publicada", a.Accion);
        Assert.Equal(_ana.Id, a.ActorId);
        Assert.DoesNotContain(Comentario, a.Contexto);     // la auditoría no copia el comentario al publicar
    }

    [Theory]
    [InlineData(0)]
    [InlineData(6)]
    [InlineData(-1)]
    public async Task La_calificacion_fuera_de_1_a_5_se_rechaza_sin_tocar_nada(int calificacion)
    {
        var c = UnContacto();

        var r = await _servicio.PublicarAsync(Pub(c, calificacion));

        Assert.Equal(Fallas.Validacion, r.Codigo);
        Assert.Empty(_repo.Resenas);
        Assert.Equal(0, _repo.Cerrojos);
    }

    [Theory]
    [InlineData(19, false)]
    [InlineData(20, true)]
    [InlineData(1500, true)]
    [InlineData(1501, false)]
    public async Task El_comentario_tiene_entre_20_y_1500_caracteres(int largo, bool valido)
    {
        var c = UnContacto();

        var r = await _servicio.PublicarAsync(Pub(c, texto: Texto(largo)));

        Assert.Equal(valido, r.Exito);
        if (!valido) Assert.Equal(Fallas.Validacion, r.Codigo);
    }

    [Fact]
    public async Task El_largo_se_cuenta_en_caracteres_y_no_en_unidades_utf16()
    {
        var c = UnContacto();
        // 20 emojis = 40 unidades UTF-16 pero 20 caracteres, que es lo que cuenta la base.
        var r1 = await _servicio.PublicarAsync(Pub(c, texto: string.Concat(Enumerable.Repeat("😀", 20))));
        Assert.True(r1.Exito, r1.Mensaje);

        // 1.000 emojis son 1.000 caracteres (válidos) aunque ocupen 2.000 unidades.
        var c2 = UnContacto(buscador: UnUsuario("buscador"));
        var r2 = await _servicio.PublicarAsync(Pub(c2, texto: string.Concat(Enumerable.Repeat("😀", 1000)), autor: _base.Usuarios.Last()));
        Assert.True(r2.Exito, r2.Mensaje);
    }

    [Fact]
    public async Task Un_comentario_de_solo_espacios_no_cuenta()
    {
        var c = UnContacto();

        var r = await _servicio.PublicarAsync(Pub(c, texto: new string(' ', 40)));

        Assert.Equal(Fallas.Validacion, r.Codigo);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Sin_clave_de_idempotencia_no_se_publica_y_no_se_inventa_una(string clave)
    {
        var c = UnContacto();

        var r = await _servicio.PublicarAsync(Pub(c, clave: clave));

        Assert.Equal(Fallas.Validacion, r.Codigo);
        Assert.Empty(_repo.Resenas);
    }

    [Fact]
    public async Task Un_aspecto_que_no_existe_se_rechaza()
    {
        var c = UnContacto();

        var r = await _servicio.PublicarAsync(Pub(c, aspectos: new[] { "puntualidad", "simpatia" }));

        Assert.Equal(Fallas.Validacion, r.Codigo);
        Assert.Empty(_repo.Resenas);
    }

    [Fact]
    public async Task El_contacto_de_otra_persona_o_inexistente_es_404()
    {
        var deOtra = UnContacto(buscador: UnUsuario("buscador"));

        var a = await _servicio.PublicarAsync(Pub(deOtra));
        var b = await _servicio.PublicarAsync(new PublicarResenaCmd(_ana.Id, Guid.NewGuid(), 5, Comentario, null, false, "k"));

        Assert.Equal(Fallas.NoEncontrado, a.Codigo);
        Assert.Equal(Fallas.NoEncontrado, b.Codigo);
        Assert.Empty(_repo.Resenas);
    }

    [Fact]
    public async Task Sin_elegibilidad_el_servidor_rechaza_aunque_el_cliente_lo_intente()
    {
        var c = UnContacto(hace: TimeSpan.FromDays(2));     // paso 9: se revalida en el servidor

        var r = await _servicio.PublicarAsync(Pub(c));

        Assert.False(r.Exito);
        Assert.Equal(FallasResenas.NoElegible, r.Codigo);
        Assert.Contains("12 de noviembre de 2026", r.Mensaje);
        Assert.Empty(_repo.Resenas);
        Assert.Empty(_avisos.Encoladas);
        Assert.Equal(1, _repo.Deshechas);
        Assert.Equal(0, _repo.Recalculos);
        Assert.Null(Recargado().PromedioCalificacion);
    }

    [Fact]
    public async Task Un_intento_rechazado_por_elegibilidad_queda_registrado()
    {
        var c = UnContacto(hace: TimeSpan.FromDays(2));

        await _servicio.PublicarAsync(Pub(c));

        var a = Assert.Single(_auditoria.Registros);
        Assert.Equal("resena.intento_rechazado", a.Accion);
        Assert.Equal(c.Id, a.EntidadId);
        Assert.Contains("VentanaDeContacto", a.Contexto);
    }

    [Fact]
    public async Task La_autoresena_se_bloquea_se_registra_como_incidente_y_el_mensaje_es_neutro()
    {
        _luis.Roles.Add(new UsuarioRol { UsuarioId = _luis.Id, Rol = _base.Roles.First(r => r.Codigo == "buscador") });
        var c = UnContacto(buscador: _luis);

        var r = await _servicio.PublicarAsync(Pub(c, autor: _luis));

        Assert.Equal(FallasResenas.NoElegible, r.Codigo);
        Assert.Empty(_repo.Resenas);
        Assert.Equal("resena.autoresena_bloqueada", Assert.Single(_auditoria.Registros).Accion);
        Assert.DoesNotContain("mism", r.Mensaje!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task El_tope_de_90_dias_se_revalida_al_publicar()
    {
        await UnaPublicada();
        var segundo = UnContacto();

        var r = await _servicio.PublicarAsync(Pub(segundo, clave: "otra"));

        Assert.Equal(FallasResenas.NoElegible, r.Codigo);
        Assert.Single(_repo.Resenas);
    }

    [Fact]
    public async Task Un_reintento_con_el_mismo_contenido_devuelve_la_reseña_sin_duplicar_ni_avisar()
    {
        var c = UnContacto();
        var primero = await _servicio.PublicarAsync(Pub(c, 5, clave: "k"));

        var reintento = await _servicio.PublicarAsync(Pub(c, 5, clave: "k"));

        Assert.True(reintento.Exito);
        Assert.True(reintento.Valor!.YaExistia);
        Assert.Equal(primero.Valor!.Resena!.Id, reintento.Valor.Resena!.Id);
        Assert.Single(_repo.Resenas);
        Assert.Single(_avisos.Encoladas);
        Assert.Equal(1, _repo.Recalculos);
        Assert.Equal(1, Recargado().TotalResenas);
    }

    [Fact]
    public async Task Otro_contenido_sobre_la_misma_interaccion_es_un_conflicto()
    {
        var c = UnContacto();
        await _servicio.PublicarAsync(Pub(c, 5, clave: "k"));

        var r = await _servicio.PublicarAsync(Pub(c, 1, "Cambio de opinión totalmente distinto al anterior.", clave: "k2"));

        Assert.Equal(Fallas.Conflicto, r.Codigo);
        Assert.Equal(5, Assert.Single(_repo.Resenas).Calificacion);
    }

    [Fact]
    public async Task Una_cuenta_sin_correo_verificado_no_puede_publicar()
    {
        var sin = UnUsuario("buscador", verificado: false);
        var c = UnContacto(buscador: sin);

        var r = await _servicio.PublicarAsync(Pub(c, autor: sin));

        Assert.Equal(FallasResenas.NoElegible, r.Codigo);
        Assert.Empty(_repo.Resenas);
    }

    [Theory]
    [InlineData("Me atendió el señor con RUT 12.345.678-5 y todo muy bien")]
    [InlineData("Su RUT es 12345678-K, excelente trabajo en general")]
    [InlineData("Llámenlo al +56 9 8765 4321, muy buen servicio de verdad")]
    [InlineData("Mi teléfono 987654321 por si quieren referencias reales")]
    [InlineData("Quedó en Avenida Alemania 1234 y trabajan muy bien todos")]
    [InlineData("Está en calle Manuel Montt 450 y atienden de maravilla")]
    public void Se_detectan_rut_telefono_y_direccion(string texto)
    {
        Assert.NotEmpty(ReglasDeResenas.DatosPersonales(texto));
    }

    [Theory]
    [InlineData("Llegaron a las 10 y terminaron en 45 minutos, todo por 35000 pesos")]
    [InlineData("Excelente atención, muy puntuales y explicaron cada paso con calma")]
    [InlineData("Me cobraron $120.000 por el trabajo, precio acordado desde el inicio")]
    [InlineData("Fue en 2024, el 15 de marzo, y volvería a contratarlo sin dudar")]
    public void Un_comentario_normal_no_dispara_el_aviso(string texto)
    {
        Assert.Empty(ReglasDeResenas.DatosPersonales(texto));
    }

    [Fact]
    public async Task Con_datos_personales_avisa_antes_de_publicar_y_no_crea_nada()
    {
        var c = UnContacto();
        var texto = "Muy bien, su RUT es 12.345.678-5 por si lo necesitan";

        var r = await _servicio.PublicarAsync(Pub(c, texto: texto));

        Assert.True(r.Exito);
        Assert.True(r.Valor!.RequiereConfirmacion);
        Assert.Contains("12.345.678-5", r.Valor.DatosPersonalesDetectados);
        Assert.Empty(_repo.Resenas);
        Assert.Empty(_avisos.Encoladas);
        Assert.Equal(0, _repo.Cerrojos);
    }

    [Fact]
    public async Task Enviar_igual_publica_y_deja_la_reseña_marcada_para_revision_preventiva()
    {
        var c = UnContacto();
        var texto = "Muy bien, su RUT es 12.345.678-5 por si lo necesitan";

        var r = await _servicio.PublicarAsync(Pub(c, texto: texto, enviarIgual: true));

        Assert.False(r.Valor!.RequiereConfirmacion);
        var resena = Assert.Single(_repo.Resenas);
        Assert.Equal(EstadoResena.Publicada, resena.Estado);
        Assert.Contains(_auditoria.Registros, a => a.Accion == "resena.revision_preventiva" && a.EntidadId == resena.Id);
    }

    // ------------------------------------------------------------- API-49 · editar

    [Fact]
    public async Task Editar_dentro_del_plazo_cambia_la_reseña_la_marca_editada_y_recalcula()
    {
        var resena = await UnaPublicada(cal: 5);
        _reloj.Avanzar(TimeSpan.FromHours(10));

        var r = await _servicio.EditarAsync(new EditarResenaCmd(
            resena.Id, _ana.Id, 2, "Pensándolo mejor, tuvieron varios retrasos en la obra.", new[] { "puntualidad" }, false));

        Assert.True(r.Exito, r.Mensaje);
        Assert.True(resena.Editada);
        Assert.Equal(2, resena.Calificacion);
        Assert.Equal("Pensándolo mejor, tuvieron varios retrasos en la obra.", resena.Comentario);
        Assert.Equal(Ahora.AddHours(72), resena.EditableHasta);     // editar no estira el plazo
        Assert.Equal(2.0m, Recargado().PromedioCalificacion);
        Assert.Equal(1, Recargado().TotalResenas);
    }

    [Fact]
    public async Task Editar_guarda_la_version_anterior_en_la_auditoria()
    {
        var resena = await UnaPublicada(cal: 5);
        var anterior = resena.Comentario;

        await _servicio.EditarAsync(new EditarResenaCmd(
            resena.Id, _ana.Id, 3, "Ahora creo que fue un servicio apenas aceptable, la verdad.", null, false));

        var a = _auditoria.Registros.Single(x => x.Accion == "resena.editada");
        Assert.Equal(_ana.Id, a.ActorId);
        using var json = JsonDocument.Parse(a.Contexto!);
        Assert.Equal(5, json.RootElement.GetProperty("calificacionAnterior").GetInt32());
        Assert.Equal(anterior, json.RootElement.GetProperty("comentarioAnterior").GetString());
    }

    /// <summary>
    /// CS-16, por las DOS vías de la Tanda B: el contacto directo y la cita realizada. El plazo
    /// no depende de cómo nació la reseña, y una regla que lo hiciera depender pasaría
    /// inadvertida si solo se probara una.
    /// </summary>
    [Theory]
    [InlineData(false, 72 * 3600, true)]          // justo a las 72 h todavía se puede
    [InlineData(false, 72 * 3600 + 1, false)]
    [InlineData(true, 72 * 3600, true)]
    [InlineData(true, 72 * 3600 + 1, false)]
    public async Task El_plazo_de_edicion_es_de_72_horas_exactas(bool porCita, int segundos, bool puede)
    {
        var resena = await UnaPublicadaPor(porCita);
        _reloj.Avanzar(TimeSpan.FromSeconds(segundos));

        var r = await _servicio.EditarAsync(new EditarResenaCmd(
            resena.Id, _ana.Id, 1, "Otra opinión distinta, escrita con tiempo de sobra.", null, false));

        Assert.Equal(puede, r.Exito);
        if (!puede)
        {
            Assert.Equal(Fallas.ReglaDeNegocio, r.Codigo);
            Assert.False(resena.Editada);
            Assert.Equal(5, resena.Calificacion);
        }
    }

    /// <summary>El plazo que se guarda, con el número escrito a mano y no sacado de `PlazoDeEdicion`.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Publicar_fija_el_plazo_de_edicion_en_72_horas_por_las_dos_vias(bool porCita)
    {
        var resena = await UnaPublicadaPor(porCita);

        Assert.Equal(Ahora, resena.PublicadaEn);
        Assert.Equal(Ahora.AddHours(72), resena.EditableHasta);
    }

    /// <summary>
    /// Una reseña publicada por contacto directo (8 días atrás) o por una cita realizada hace un
    /// rato, que no espera los 7 días.
    /// </summary>
    private async Task<Resena> UnaPublicadaPor(bool porCita)
    {
        var c = porCita
            ? UnContacto(hace: TimeSpan.FromMinutes(5), cita: UnaCita(EstadoCita.Realizada))
            : UnContacto();
        var r = await _servicio.PublicarAsync(Pub(c));
        Assert.True(r.Exito, r.Mensaje);
        Assert.Equal(c.CitaId is not null, porCita);
        return _repo.Resenas.Single(x => x.Id == r.Valor!.Resena!.Id);
    }

    [Fact]
    public async Task Editar_la_reseña_de_otra_persona_es_404()
    {
        var resena = await UnaPublicada();

        var r = await _servicio.EditarAsync(new EditarResenaCmd(
            resena.Id, UnUsuario("buscador").Id, 1, "Intento de editar una reseña que no es mía.", null, false));

        Assert.Equal(Fallas.NoEncontrado, r.Codigo);
        Assert.False(resena.Editada);
    }

    [Fact]
    public async Task Guardar_lo_mismo_no_la_marca_como_editada()
    {
        var resena = await UnaPublicada(cal: 4);

        var r = await _servicio.EditarAsync(new EditarResenaCmd(resena.Id, _ana.Id, 4, resena.Comentario, null, false));

        Assert.True(r.Exito);
        Assert.False(resena.Editada);
        Assert.DoesNotContain(_auditoria.Registros, a => a.Accion == "resena.editada");
    }

    [Fact]
    public async Task Una_reseña_oculta_no_se_puede_editar_aunque_este_a_tiempo()
    {
        var resena = await UnaPublicada();
        resena.Ocultar();

        var r = await _servicio.EditarAsync(new EditarResenaCmd(
            resena.Id, _ana.Id, 1, "Quiero cambiar mi reseña que está oculta ahora mismo.", null, false));

        Assert.Equal(Fallas.ReglaDeNegocio, r.Codigo);
    }

    [Fact]
    public async Task Editar_valida_igual_que_publicar()
    {
        var resena = await UnaPublicada();

        var a = await _servicio.EditarAsync(new EditarResenaCmd(resena.Id, _ana.Id, 9, Comentario, null, false));
        var b = await _servicio.EditarAsync(new EditarResenaCmd(resena.Id, _ana.Id, 3, "corto", null, false));

        Assert.Equal(Fallas.Validacion, a.Codigo);
        Assert.Equal(Fallas.Validacion, b.Codigo);
    }

    // ------------------------------------------------------------- API-50 · responder

    [Fact]
    public async Task El_prestador_responde_y_la_respuesta_queda_bajo_la_reseña()
    {
        var resena = await UnaPublicada();
        _avisos.Encoladas.Clear();

        var r = await _servicio.ResponderAsync(resena.Id, _luis.Id, "  Gracias por confiar en nosotros.  ");

        Assert.True(r.Exito);
        Assert.True(r.Valor);       // creó
        var respuesta = Assert.Single(_repo.Respuestas);
        Assert.Equal("Gracias por confiar en nosotros.", respuesta.Texto);
        Assert.Equal(resena.Id, respuesta.ResenaId);
        Assert.Equal(Ahora, respuesta.PublicadaEn);
        var aviso = Assert.Single(_avisos.Encoladas);
        Assert.Equal(_ana.Id, aviso.DestinatarioId);
        Assert.Equal(TipoNotificacion.ResenaRespondida, aviso.Tipo);
    }

    /// <summary>
    /// El cuerpo del aviso es su CARGA, no el texto del correo (eso lo arma Plantillas), y de
    /// esta depende el enlace: sin el slug el correo sale apuntando al inicio del sitio en vez
    /// de a la ficha donde esta la respuesta.
    /// </summary>
    [Fact]
    public async Task El_aviso_de_respuesta_lleva_el_slug_del_perfil_en_la_carga()
    {
        var resena = await UnaPublicada();
        _avisos.Encoladas.Clear();

        await _servicio.ResponderAsync(resena.Id, _luis.Id, "Gracias por confiar en nosotros.");

        var aviso = Assert.Single(_avisos.Encoladas);
        Assert.Equal(_perfil.Slug, CargaDeAviso.LeerCampo(aviso.Cuerpo, "slug"));
    }

    /// <summary>
    /// Lo que el aviso de reseña nueva NO lleva: ni la calificación ni el comentario. La
    /// reseña puede acabar oculta en moderación (CS-17) y un correo enviado no se retira.
    /// </summary>
    [Fact]
    public async Task El_aviso_de_resena_nueva_no_guarda_la_calificacion_ni_el_comentario()
    {
        var c = UnContacto();

        await _servicio.PublicarAsync(Pub(c));

        var aviso = Assert.Single(_avisos.Encoladas);
        Assert.DoesNotContain(Comentario, aviso.Cuerpo);
        Assert.Null(CargaDeAviso.LeerCampo(aviso.Cuerpo, "calificacion"));
        Assert.Null(CargaDeAviso.LeerCampo(aviso.Cuerpo, "comentario"));
    }

    [Fact]
    public async Task Volver_a_responder_edita_la_misma_respuesta_y_no_avisa_otra_vez()
    {
        var resena = await UnaPublicada();
        await _servicio.ResponderAsync(resena.Id, _luis.Id, "Primera versión de mi respuesta.");
        _avisos.Encoladas.Clear();
        _reloj.Avanzar(TimeSpan.FromDays(400));       // RN-S5-06: sin límite de plazo

        var r = await _servicio.ResponderAsync(resena.Id, _luis.Id, "Versión corregida de mi respuesta.");

        Assert.True(r.Exito);
        Assert.False(r.Valor);      // editó
        var respuesta = Assert.Single(_repo.Respuestas);
        Assert.Equal("Versión corregida de mi respuesta.", respuesta.Texto);
        Assert.Equal(_reloj.Ahora, respuesta.EditadaEn);
        Assert.Empty(_avisos.Encoladas);
    }

    [Fact]
    public async Task Solo_el_dueño_del_perfil_responde_y_para_los_demas_la_reseña_no_existe()
    {
        var resena = await UnaPublicada();
        var otroPrestador = UnUsuario("prestador");
        UnPerfil(otroPrestador);

        var a = await _servicio.ResponderAsync(resena.Id, otroPrestador.Id, "Intento ajeno de responder.");
        var b = await _servicio.ResponderAsync(resena.Id, _ana.Id, "La autora tampoco responde.");

        Assert.Equal(Fallas.NoEncontrado, a.Codigo);
        Assert.Equal(Fallas.NoEncontrado, b.Codigo);
        Assert.Empty(_repo.Respuestas);
    }

    [Fact]
    public async Task No_se_responde_una_reseña_oculta_o_retirada()
    {
        var oculta = await UnaPublicada();
        oculta.Ocultar();
        var retirada = await UnaPublicada(autor: UnUsuario("buscador"));
        retirada.Retirar();

        Assert.Equal(Fallas.NoEncontrado, (await _servicio.ResponderAsync(oculta.Id, _luis.Id, "Respuesta")).Codigo);
        Assert.Equal(Fallas.NoEncontrado, (await _servicio.ResponderAsync(retirada.Id, _luis.Id, "Respuesta")).Codigo);
    }

    [Theory]
    [InlineData("", false)]
    [InlineData("   ", false)]
    [InlineData("a", true)]
    [InlineData(1000, true)]
    [InlineData(1001, false)]
    public async Task La_respuesta_no_puede_estar_vacia_ni_pasar_de_1000_caracteres(object entrada, bool valido)
    {
        var resena = await UnaPublicada();
        var texto = entrada is int n ? Texto(n) : (string)entrada;

        var r = await _servicio.ResponderAsync(resena.Id, _luis.Id, texto);

        Assert.Equal(valido, r.Exito);
    }

    // ------------------------------------------------------------- API-51 · reportar

    [Fact]
    public async Task Un_reporte_no_grave_deja_la_reseña_visible_y_abierto_para_el_moderador()
    {
        var resena = await UnaPublicada();

        var r = await _servicio.ReportarAsync(resena.Id, _luis.Id, MotivoReporte.Falsedad, "  No fue mi cliente  ");

        Assert.True(r.Exito, r.Mensaje);
        Assert.Equal(EstadoResena.Publicada, resena.Estado);
        var reporte = Assert.Single(_repo.Reportes);
        Assert.Equal(EstadoReporte.Abierto, reporte.Estado);
        Assert.Equal("No fue mi cliente", reporte.Detalle);
        Assert.Equal(_luis.Id, reporte.ReportadoPor);
        Assert.Equal(1, Recargado().TotalResenas);
    }

    [Theory]
    [InlineData(MotivoReporte.DatosPersonales)]
    [InlineData(MotivoReporte.LenguajeDeOdio)]
    public async Task Un_motivo_grave_oculta_la_reseña_en_el_acto_y_la_saca_del_recuento(MotivoReporte motivo)
    {
        var resena = await UnaPublicada(cal: 5);
        await UnaPublicada(autor: UnUsuario("buscador"), cal: 3);
        Assert.Equal(4.0m, Recargado().PromedioCalificacion);

        var r = await _servicio.ReportarAsync(resena.Id, _luis.Id, motivo, null);

        Assert.True(r.Exito);
        Assert.Equal(EstadoResena.OcultaEnModeracion, resena.Estado);
        Assert.Equal(1, Recargado().TotalResenas);
        Assert.Equal(3.0m, Recargado().PromedioCalificacion);       // 4F.3: sin ella mientras dura
        Assert.Contains(_auditoria.Registros, a => a.Accion == "resena.reportada" && a.Contexto!.Contains("\"ocultada\":true"));
    }

    [Theory]
    [InlineData(MotivoReporte.Falsedad)]
    [InlineData(MotivoReporte.ConflictoDeInteres)]
    [InlineData(MotivoReporte.FueraDeTema)]
    public async Task Los_motivos_no_graves_no_ocultan(MotivoReporte motivo)
    {
        var resena = await UnaPublicada();

        await _servicio.ReportarAsync(resena.Id, _luis.Id, motivo, null);

        Assert.Equal(EstadoResena.Publicada, resena.Estado);
    }

    [Fact]
    public async Task Nadie_reporta_su_propia_reseña_ni_dos_veces_la_misma()
    {
        var resena = await UnaPublicada();

        var propia = await _servicio.ReportarAsync(resena.Id, _ana.Id, MotivoReporte.FueraDeTema, null);
        await _servicio.ReportarAsync(resena.Id, _luis.Id, MotivoReporte.FueraDeTema, null);
        var repetido = await _servicio.ReportarAsync(resena.Id, _luis.Id, MotivoReporte.Falsedad, null);

        Assert.Equal(Fallas.ReglaDeNegocio, propia.Codigo);
        Assert.Equal(Fallas.Conflicto, repetido.Codigo);
        Assert.Single(_repo.Reportes);
    }

    [Fact]
    public async Task No_se_reporta_una_reseña_retirada_ni_con_la_cuenta_sin_verificar()
    {
        var retirada = await UnaPublicada();
        retirada.Retirar();
        var viva = await UnaPublicada(autor: UnUsuario("buscador"));
        var sin = UnUsuario("buscador", verificado: false);

        Assert.Equal(Fallas.NoEncontrado, (await _servicio.ReportarAsync(retirada.Id, _luis.Id, MotivoReporte.Falsedad, null)).Codigo);
        Assert.Equal(Fallas.ReglaDeNegocio, (await _servicio.ReportarAsync(viva.Id, sin.Id, MotivoReporte.Falsedad, null)).Codigo);
        Assert.Empty(_repo.Reportes);
    }

    [Fact]
    public async Task El_detalle_no_puede_pasar_de_1000_caracteres()
    {
        var resena = await UnaPublicada();

        var r = await _servicio.ReportarAsync(resena.Id, _luis.Id, MotivoReporte.Falsedad, Texto(1001));

        Assert.Equal(Fallas.Validacion, r.Codigo);
    }

    // ------------------------------------------------------------- API-52 · cola

    [Fact]
    public async Task La_cola_es_solo_del_moderador()
    {
        var r1 = await _servicio.ColaDeReportesAsync(_ana.Id);
        var r2 = await _servicio.ColaDeReportesAsync(_luis.Id);

        Assert.Equal(Fallas.NoEncontrado, r1.Codigo);
        Assert.Equal(Fallas.NoEncontrado, r2.Codigo);
    }

    [Fact]
    public async Task La_cola_trae_los_graves_primero_y_despues_los_mas_viejos()
    {
        var a = await UnaPublicada();
        var b = await UnaPublicada(autor: UnUsuario("buscador"));
        await _servicio.ReportarAsync(a.Id, _luis.Id, MotivoReporte.Falsedad, null);
        _reloj.Avanzar(TimeSpan.FromMinutes(1));
        await _servicio.ReportarAsync(b.Id, _luis.Id, MotivoReporte.LenguajeDeOdio, null);

        var cola = (await _servicio.ColaDeReportesAsync(_mod.Id)).Valor!;

        Assert.Equal(2, cola.Count);
        Assert.Equal(MotivoReporte.LenguajeDeOdio, cola[0].Motivo);
        Assert.Equal(MotivoReporte.Falsedad, cola[1].Motivo);
    }

    [Fact]
    public async Task La_cola_no_incluye_los_reclamos_en_que_el_moderador_es_parte()
    {
        // Es autor de una reseña reportada, es dueño de un perfil reseñado, y es quien reportó otra.
        _mod.Roles.Add(new UsuarioRol { UsuarioId = _mod.Id, Rol = _base.Roles.First(r => r.Codigo == "buscador") });
        var propiaDelMod = await UnaPublicada(autor: _mod);
        var perfilDelMod = UnPerfil(_mod);
        var enSuPerfil = await UnaPublicada(autor: UnUsuario("buscador"), perfil: perfilDelMod);
        var reportadaPorEl = await UnaPublicada(autor: UnUsuario("buscador"));
        var ajena = await UnaPublicada(autor: UnUsuario("buscador"));

        await _servicio.ReportarAsync(propiaDelMod.Id, _luis.Id, MotivoReporte.Falsedad, null);
        await _servicio.ReportarAsync(enSuPerfil.Id, _ana.Id, MotivoReporte.Falsedad, null);
        await _servicio.ReportarAsync(reportadaPorEl.Id, _mod.Id, MotivoReporte.Falsedad, null);
        await _servicio.ReportarAsync(ajena.Id, _luis.Id, MotivoReporte.Falsedad, null);

        var cola = (await _servicio.ColaDeReportesAsync(_mod.Id)).Valor!;

        var unica = Assert.Single(cola);
        Assert.Equal(ajena.Id, unica.ResenaId);
    }

    [Fact]
    public async Task Un_reporte_resuelto_sale_de_la_cola()
    {
        var a = await UnaPublicada();
        await _servicio.ReportarAsync(a.Id, _luis.Id, MotivoReporte.Falsedad, null);
        var id = _repo.Reportes.Single().Id;

        await _servicio.ResolverReporteAsync(id, _mod.Id, false, "Se revisó y la reseña es legítima.");

        Assert.Empty((await _servicio.ColaDeReportesAsync(_mod.Id)).Valor!);
    }

    // ------------------------------------------------------------- API-53 · resolver

    private async Task<(Resena Resena, ReporteResena Reporte)> UnReclamo(MotivoReporte motivo = MotivoReporte.Falsedad, int cal = 5)
    {
        var resena = await UnaPublicada(autor: UnUsuario("buscador"), cal: cal);
        await _servicio.ReportarAsync(resena.Id, _luis.Id, motivo, null);
        return (resena, _repo.Reportes.Last());
    }

    [Fact]
    public async Task Retirar_marca_la_reseña_retirada_cierra_el_reclamo_recalcula_y_deja_traza()
    {
        var (resena, reporte) = await UnReclamo(cal: 1);
        await UnaPublicada(autor: UnUsuario("buscador"), cal: 5);
        Assert.Equal(3.0m, Recargado().PromedioCalificacion);

        var r = await _servicio.ResolverReporteAsync(reporte.Id, _mod.Id, true, "  Reseña falsa: no hubo servicio.  ");

        Assert.True(r.Exito, r.Mensaje);
        Assert.Equal(EstadoResena.Retirada, resena.Estado);
        Assert.Equal(EstadoReporte.ResueltoRetira, reporte.Estado);
        Assert.Equal(_mod.Id, reporte.ResueltoPor);
        Assert.Equal("Reseña falsa: no hubo servicio.", reporte.Resolucion);
        Assert.Equal(Ahora, reporte.ResueltoEn);
        Assert.Equal(1, Recargado().TotalResenas);
        Assert.Equal(5.0m, Recargado().PromedioCalificacion);
        var traza = _auditoria.Registros.Single(a => a.Accion == "resena.retirada");      // RN-S5-12
        Assert.Equal(_mod.Id, traza.ActorId);
        using var contexto = JsonDocument.Parse(traza.Contexto!);
        Assert.Equal("Reseña falsa: no hubo servicio.", contexto.RootElement.GetProperty("motivo").GetString());
    }

    [Fact]
    public async Task Mantener_devuelve_a_la_vista_una_reseña_que_estaba_oculta_y_vuelve_a_contarla()
    {
        var (resena, reporte) = await UnReclamo(MotivoReporte.LenguajeDeOdio, cal: 2);
        Assert.Equal(0, Recargado().TotalResenas);

        var r = await _servicio.ResolverReporteAsync(reporte.Id, _mod.Id, false, "No hay lenguaje de odio en el texto.");

        Assert.True(r.Exito);
        Assert.Equal(EstadoResena.Publicada, resena.Estado);
        Assert.Equal(EstadoReporte.ResueltoMantiene, reporte.Estado);
        Assert.Equal(1, Recargado().TotalResenas);
        Assert.Equal(2.0m, Recargado().PromedioCalificacion);
    }

    [Fact]
    public async Task Una_decision_cierra_todos_los_reclamos_abiertos_de_esa_reseña()
    {
        var (resena, primero) = await UnReclamo(MotivoReporte.Falsedad);
        var otro = UnUsuario("buscador");
        await _servicio.ReportarAsync(resena.Id, otro.Id, MotivoReporte.DatosPersonales, null);     // este la oculta

        await _servicio.ResolverReporteAsync(primero.Id, _mod.Id, false, "Revisada completa: se mantiene.");

        Assert.All(_repo.Reportes, r => Assert.Equal(EstadoReporte.ResueltoMantiene, r.Estado));
        Assert.Equal(EstadoResena.Publicada, resena.Estado);
    }

    [Fact]
    public async Task El_moderador_no_resuelve_un_reclamo_en_el_que_es_parte()
    {
        _mod.Roles.Add(new UsuarioRol { UsuarioId = _mod.Id, Rol = _base.Roles.First(r => r.Codigo == "buscador") });
        var propia = await UnaPublicada(autor: _mod);
        await _servicio.ReportarAsync(propia.Id, _luis.Id, MotivoReporte.Falsedad, null);
        var reporte = _repo.Reportes.Single();

        var r = await _servicio.ResolverReporteAsync(reporte.Id, _mod.Id, true, "Intento retirar la mía propia.");

        Assert.Equal(Fallas.NoEncontrado, r.Codigo);
        Assert.Equal(EstadoReporte.Abierto, reporte.Estado);
        Assert.Equal(EstadoResena.Publicada, propia.Estado);
        Assert.Contains(_auditoria.Registros, a => a.Accion == "reporte.resolucion_rechazada");
    }

    [Fact]
    public async Task El_moderador_que_reporto_otro_de_los_reclamos_tampoco_resuelve()
    {
        _mod.Roles.Add(new UsuarioRol { UsuarioId = _mod.Id, Rol = _base.Roles.First(r => r.Codigo == "buscador") });
        var (resena, reporte) = await UnReclamo();
        await _servicio.ReportarAsync(resena.Id, _mod.Id, MotivoReporte.Falsedad, null);

        var r = await _servicio.ResolverReporteAsync(reporte.Id, _mod.Id, true, "Debería abstenerme de resolver esto.");

        Assert.Equal(Fallas.NoEncontrado, r.Codigo);
        Assert.Equal(EstadoResena.Publicada, resena.Estado);
    }

    [Fact]
    public async Task Resolver_dos_veces_es_un_conflicto_y_no_cambia_nada()
    {
        var (resena, reporte) = await UnReclamo();
        await _servicio.ResolverReporteAsync(reporte.Id, _mod.Id, false, "Primera decisión: se mantiene.");

        var otra = await _servicio.ResolverReporteAsync(reporte.Id, _mod.Id, true, "Segunda decisión: se retira.");

        Assert.Equal(Fallas.Conflicto, otra.Codigo);
        Assert.Equal(EstadoResena.Publicada, resena.Estado);
    }

    [Fact]
    public async Task Solo_un_moderador_resuelve_y_siempre_con_el_motivo_escrito()
    {
        var (_, reporte) = await UnReclamo();

        var noMod = await _servicio.ResolverReporteAsync(reporte.Id, _luis.Id, true, "Soy el prestador y quiero borrarla.");
        var sinMotivo = await _servicio.ResolverReporteAsync(reporte.Id, _mod.Id, true, "corto");
        var inexistente = await _servicio.ResolverReporteAsync(Guid.NewGuid(), _mod.Id, true, "Un motivo suficientemente largo.");

        Assert.Equal(Fallas.NoEncontrado, noMod.Codigo);
        Assert.Equal(Fallas.Validacion, sinMotivo.Codigo);
        Assert.Equal(Fallas.NoEncontrado, inexistente.Codigo);
        Assert.Equal(EstadoReporte.Abierto, reporte.Estado);
    }

    // ------------------------------------------------------------- lectura pública

    [Fact]
    public async Task Las_reseñas_publicas_solo_incluyen_las_publicadas_de_la_mas_nueva_a_la_mas_vieja()
    {
        var vieja = await UnaPublicada(autor: UnUsuario("buscador"), cuando: Ahora.AddDays(-5));
        var nueva = await UnaPublicada(autor: UnUsuario("buscador"), cuando: Ahora.AddDays(-1));
        var oculta = await UnaPublicada(autor: UnUsuario("buscador"));
        oculta.Ocultar();
        var retirada = await UnaPublicada(autor: UnUsuario("buscador"));
        retirada.Retirar();

        var pagina = (await _servicio.ResenasPublicasAsync(_perfil.Id, null)).Valor!;

        Assert.Equal(new[] { nueva.Id, vieja.Id }, pagina.Resenas.Select(r => r.Id));
        Assert.Null(pagina.SiguienteCursor);
    }

    [Fact]
    public async Task Lo_publico_no_filtra_autor_ni_estado_interno_ni_contacto()
    {
        var resena = await UnaPublicada();
        await _servicio.ResponderAsync(resena.Id, _luis.Id, "Gracias por tu opinión.");

        var pagina = (await _servicio.ResenasPublicasAsync(_perfil.Id, null)).Valor!;
        // Sin escapar los acentos, para poder buscar el texto tal cual en lo que viajaría por HTTP.
        var json = JsonSerializer.Serialize(pagina, new JsonSerializerOptions
        {
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        });

        Assert.DoesNotContain(_ana.Id.ToString(), json);
        Assert.DoesNotContain(_ana.Correo, json);
        Assert.DoesNotContain(resena.ContactoId.ToString(), json);
        Assert.DoesNotContain("autor", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("estado", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("editableHasta", json, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Gracias por tu opinión.", json);
    }

    [Fact]
    public async Task La_paginacion_trae_20_por_pagina_sin_repetir_ni_saltarse_ninguna()
    {
        for (var i = 0; i < 45; i++)
            await UnaPublicada(autor: UnUsuario("buscador"), cuando: Ahora.AddMinutes(-(i % 7)));    // empates de fecha a propósito

        var vistas = new List<Guid>();
        string? cursor = null;
        var paginas = 0;
        do
        {
            var p = (await _servicio.ResenasPublicasAsync(_perfil.Id, cursor)).Valor!;
            vistas.AddRange(p.Resenas.Select(r => r.Id));
            cursor = p.SiguienteCursor;
            paginas++;
        } while (cursor is not null && paginas < 10);

        Assert.Equal(3, paginas);
        Assert.Equal(45, vistas.Count);
        Assert.Equal(45, vistas.Distinct().Count());
    }

    [Theory]
    [InlineData("no-es-base64!!")]
    [InlineData("YWJj")]                // base64 válido pero sin la forma ticks|id
    public async Task Un_cursor_invalido_es_400(string cursor)
    {
        var r = await _servicio.ResenasPublicasAsync(_perfil.Id, cursor);

        Assert.Equal(Fallas.Validacion, r.Codigo);
    }

    [Fact]
    public async Task Las_reseñas_de_un_perfil_sin_publicar_no_se_leen()
    {
        var borrador = UnPerfil(UnUsuario("prestador"), EstadoPerfil.Borrador);

        var r = await _servicio.ResenasPublicasAsync(borrador.Id, null);

        Assert.Equal(Fallas.NoEncontrado, r.Codigo);
    }

    // ------------------------------------------------------------- API-65 · mis reseñas

    [Fact]
    public async Task Mis_resenas_traen_solo_las_mias()
    {
        var mia = await UnaPublicada();
        var otroAutor = UnUsuario("buscador");
        var ajena = await UnaPublicada(autor: otroAutor);

        var mias = (await _servicio.MisResenasAsync(_ana.Id)).Valor!;

        Assert.Equal(new[] { mia.Id }, mias.Select(r => r.Id));
        Assert.DoesNotContain(mias, r => r.Id == ajena.Id);
    }

    [Fact]
    public async Task Mis_resenas_dicen_a_quien_se_la_deje_y_hasta_cuando_puedo_cambiarla()
    {
        var resena = await UnaPublicada();

        var mia = Assert.Single((await _servicio.MisResenasAsync(_ana.Id)).Valor!);

        Assert.Equal(_perfil.NombrePublico, mia.PerfilNombre);
        Assert.Equal(_perfil.Slug, mia.PerfilSlug);
        Assert.Equal(resena.PublicadaEn + ReglasDeResenas.PlazoDeEdicion, mia.EditableHasta);
        Assert.Equal(EstadoResena.Publicada, mia.Estado);
    }

    /// <summary>
    /// Una reseña retirada en moderación sigue siendo de quien la escribió. Esconderla de su
    /// autor haría que desapareciera sin explicación, y la pantalla no podría decir qué pasó.
    /// </summary>
    [Fact]
    public async Task Una_resena_retirada_sigue_apareciendo_entre_las_mias_con_su_estado()
    {
        var resena = await UnaPublicada();
        resena.Retirar();

        var mia = Assert.Single((await _servicio.MisResenasAsync(_ana.Id)).Valor!);

        Assert.Equal(EstadoResena.Retirada, mia.Estado);
    }

    // ------------------------------------------------------------- el origen (11-nov-2026)

    [Fact]
    public async Task Mis_resenas_distinguen_la_cita_del_contacto_directo()
    {
        var cita = UnaCita(EstadoCita.Realizada);
        var porCita = await _servicio.PublicarAsync(
            Pub(UnContacto(cita: cita), clave: "con-cita"));
        var otroPerfil = UnPerfil(UnUsuario("prestador"));
        var directa = await _servicio.PublicarAsync(
            Pub(UnContacto(perfil: otroPerfil), clave: "sin-cita"));
        Assert.True(porCita.Exito && directa.Exito);

        var mias = (await _servicio.MisResenasAsync(_ana.Id)).Valor!;

        Assert.Equal(OrigenDeResena.Cita, mias.Single(r => r.Id == porCita.Valor!.Resena!.Id).Origen);
        Assert.Equal(OrigenDeResena.Directo, mias.Single(r => r.Id == directa.Valor!.Resena!.Id).Origen);
    }

    /// <summary>
    /// El orden de "mis reseñas" es CONTRATO, no casualidad: la pantalla pinta la lista tal como
    /// llega y lo que la persona viene a buscar es la última que escribió, la que todavía puede
    /// cambiar. Las tres nacen en instantes distintos a propósito: con `PublicadaEn` empatado el
    /// desempate queda en el id, que es un Guid al azar, y la prueba saldría cara o cruz.
    /// </summary>
    [Fact]
    public async Task Mis_resenas_llegan_de_la_mas_nueva_a_la_mas_vieja()
    {
        var primera = await UnaPublicada(perfil: UnPerfil(UnUsuario("prestador")));
        _reloj.Avanzar(TimeSpan.FromDays(1));
        var segunda = await UnaPublicada(perfil: UnPerfil(UnUsuario("prestador")));
        _reloj.Avanzar(TimeSpan.FromDays(1));
        var tercera = await UnaPublicada(perfil: UnPerfil(UnUsuario("prestador")));

        var mias = (await _servicio.MisResenasAsync(_ana.Id)).Valor!;

        Assert.Equal(new[] { tercera.Id, segunda.Id, primera.Id }, mias.Select(r => r.Id));
    }

    /// <summary>
    /// Lo mismo que usa la API de verdad (Program.cs): nombres en camelCase y los enum como
    /// TEXTO. Sin el convertidor, `Origen` se serializa como número y la prueba de abajo leería
    /// unos bytes que nadie recibe nunca.
    /// </summary>
    private static readonly JsonSerializerOptions ComoLaApi =
        new(JsonSerializerDefaults.Web)
        {
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
        };

    /// <summary>
    /// Decisión de Pablo del 11-nov-2026: quien reseña tras marcar "ya me atendió" queda
    /// rotulado «contacto directo, sin cita», y eso tiene que viajar en lo PÚBLICO, que es
    /// donde se lee. Lo que no viaja es con qué contacto ni con qué cita (CU-16 §7).
    ///
    /// El orden (la más nueva primero) también es contrato: el cursor keyset de la lista pública
    /// va sobre esa misma tupla. Por eso las dos reseñas se publican en instantes distintos.
    /// </summary>
    [Fact]
    public async Task Lo_publico_dice_si_hubo_cita_o_fue_contacto_directo_y_nada_mas()
    {
        var cita = UnaCita(EstadoCita.Realizada);
        var conCita = UnContacto(cita: cita);
        await _servicio.PublicarAsync(Pub(conCita, clave: "con-cita"));
        _reloj.Avanzar(TimeSpan.FromMinutes(1));
        var otroAutor = UnUsuario("buscador");
        var sinCita = UnContacto(otroAutor);
        await _servicio.PublicarAsync(Pub(sinCita, autor: otroAutor, clave: "sin-cita"));

        var pagina = (await _servicio.ResenasPublicasAsync(_perfil.Id, null)).Valor!;
        var json = JsonSerializer.Serialize(pagina, ComoLaApi);

        Assert.Equal(
            new[] { OrigenDeResena.Directo, OrigenDeResena.Cita },
            pagina.Resenas.Select(r => r.Origen));

        // Lo que NO sale: ningún identificador con el que cruzar a nadie con nada.
        Assert.DoesNotContain(cita.Id.ToString(), json);
        Assert.DoesNotContain(conCita.Id.ToString(), json);
        Assert.DoesNotContain(sinCita.Id.ToString(), json);
        Assert.DoesNotContain(_ana.Id.ToString(), json);
        Assert.DoesNotContain(otroAutor.Id.ToString(), json);

        // Y los campos son EXACTAMENTE estos. `origen` dice "Cita" o "Directo" y ahí se acaba lo
        // que se sabe del contacto: un campo nuevo que hable de él rompe aquí aunque venga vacío.
        var campos = JsonDocument.Parse(json).RootElement.GetProperty("resenas").EnumerateArray()
            .SelectMany(r => r.EnumerateObject().Select(c => c.Name))
            .Distinct().OrderBy(n => n, StringComparer.Ordinal).ToArray();
        Assert.Equal(
            new[]
            {
                "aspectos", "calificacion", "comentario", "editada", "id", "origen",
                "publicadaEn", "respuesta", "tipo",
            },
            campos);
    }
}
