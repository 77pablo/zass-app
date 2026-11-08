using Zass.Aplicacion.Agenda;
using Zass.Aplicacion.Catalogo;
using Zass.Aplicacion.Identidad;
using Zass.Aplicacion.Pruebas.Dobles;
using Zass.Aplicacion.Suscripciones;
using Zass.Dominio.Agenda;
using Zass.Dominio.Catalogo;
using Zass.Dominio.Comun;
using Zass.Dominio.Identidad;
using Zass.Dominio.Suscripciones;

namespace Zass.Aplicacion.Pruebas.Agenda;

/// <summary>
/// MOD-06, el ciclo de vida de la cita: CU-13 responder, CU-14 cancelar y reprogramar, CS-12
/// asistencia. Las transiciones de PR-06 (estados-cita.md) y quién puede hacer qué en cada
/// estado.
///
/// Lo que NO está aquí: el abrazo mortal de CC-06 con dos reprogramaciones cruzadas de
/// verdad. Eso necesita dos transacciones compitiendo y vive en integración. Aquí sí se
/// comprueba que el servicio PIDA el bloqueo ordenado y fije la espera, que es la parte que
/// una lista puede contestar.
/// </summary>
public class CicloDeLaCitaPruebas
{
    private static readonly DateTime Ahora = new(2026, 11, 9, 12, 0, 0, DateTimeKind.Utc);

    private readonly BaseEnMemoria _base = new();
    private readonly RelojFijo _reloj = new(Ahora);
    private readonly AuditoriaEnMemoria _auditor = new();
    private readonly RepositorioAgendaEnMemoria _repo;
    private readonly ServicioAgenda _servicio;

    private readonly Usuario _buscador;
    private readonly Usuario _duenoPrestador;
    private readonly PerfilPrestador _prestador;
    private readonly PrestadorServicio _automatico;
    private readonly PrestadorServicio _manual;

    private AutorDeCambio ComoBuscador => new(_buscador.Id, null);
    private AutorDeCambio ComoPrestador => new(_duenoPrestador.Id, _prestador.Id);
    private AutorDeCambio ComoDesconocido => new(Guid.NewGuid(), Guid.NewGuid());

    public CicloDeLaCitaPruebas()
    {
        _base.Rubros.Add(new Rubro { Id = 1, Codigo = "derecho", Nombre = "Derecho", Activo = true });
        _base.Servicios.Add(new Servicio
        {
            Id = 201, RubroId = 1, Nombre = "Consulta inicial",
            NombreNormalizado = "consulta inicial", EsCatalogo = true,
        });
        _base.Planes.Add(new Plan
        {
            Id = 1, Codigo = "prueba", Nombre = "Prueba", PrecioMensual = 0,
            DiasPrueba = 30, Activo = true,
        });

        _buscador = new Usuario
        {
            Correo = "buscador@zass.local", HashContrasena = "x",
            Nombre = "Ana Pérez", CorreoVerificado = true,
        };
        _duenoPrestador = new Usuario
        {
            Correo = "prestador@zass.local", HashContrasena = "x",
            Nombre = "Pablo Espinoza", CorreoVerificado = true,
        };
        _base.Usuarios.Add(_buscador);
        _base.Usuarios.Add(_duenoPrestador);

        _prestador = new PerfilPrestador
        {
            UsuarioId = _duenoPrestador.Id, RubroId = 1, Slug = "estudio-espinoza",
            NombrePublico = "Estudio Espinoza", Estado = EstadoPerfil.Publicado,
            PublicadoEn = Ahora.AddDays(-5),
        };
        _base.PerfilesPrestador.Add(_prestador);

        _automatico = new PrestadorServicio
        {
            PerfilPrestadorId = _prestador.Id, ServicioId = 201,
            DuracionMinutos = 30, ModoAgenda = ModoAgenda.Automatico, Activo = true,
        };
        _manual = new PrestadorServicio
        {
            PerfilPrestadorId = _prestador.Id, ServicioId = 201,
            DuracionMinutos = 30, ModoAgenda = ModoAgenda.Manual, Activo = true,
        };
        _base.PrestadorServicios.Add(_automatico);
        _base.PrestadorServicios.Add(_manual);

        _base.Suscripciones.Add(new Suscripcion
        {
            PerfilPrestadorId = _prestador.Id, PlanId = 1,
            Estado = EstadoSuscripcion.Activa,
            IniciaEn = Ahora.AddDays(-5), VigenteHasta = Ahora.AddDays(25),
        });

        _repo = new RepositorioAgendaEnMemoria(_base);
        _servicio = new ServicioAgenda(
            _repo, _base, _reloj, _auditor,
            new ContactosEnMemoria(_base),
            new ServicioCatalogo(new RepositorioCatalogoEnMemoria(_base), _base, _reloj, _auditor),
            new ServicioIdentidad(
                new RepositorioUsuariosEnMemoria(_base), new RepositorioPerfilesEnMemoria(_base)),
            new ServicioSuscripciones(
                new RepositorioSuscripcionesEnMemoria(_base), _base, _reloj, _auditor,
                new ColaEnMemoria()));
    }

    private BloqueHorario UnBloque(
        DateTime? inicio = null, PrestadorServicio? de = null,
        EstadoBloque estado = EstadoBloque.Libre)
    {
        var servicio = de ?? _automatico;
        var desde = inicio ?? Ahora.AddDays(1).Date.AddHours(10);
        var b = new BloqueHorario
        {
            DisponibilidadId = Guid.NewGuid(),
            PerfilPrestadorId = servicio.PerfilPrestadorId,
            PrestadorServicioId = servicio.Id,
            InicioEn = desde,
            FinEn = desde.AddMinutes(servicio.DuracionMinutos),
            Estado = estado,
        };
        _base.Bloques.Add(b);
        return b;
    }

    /// <summary>Una cita ya creada por el camino normal, para no fabricarla a mano.</summary>
    private async Task<(Cita Cita, BloqueHorario Bloque)> UnaCitaAsync(
        DateTime? inicio = null, PrestadorServicio? de = null)
    {
        var b = UnBloque(inicio, de);
        var r = await _servicio.AgendarAsync(new AgendarCmd(
            _buscador.Id, b.Id, $"clave-{Guid.NewGuid():N}", "Consulta por arriendo"));
        Assert.True(r.Exito, r.Mensaje);
        return (_base.Citas.Single(c => c.Id == r.Valor!.Id), b);
    }

    // ========================================================= CU-13 responder

    [Fact]
    public async Task El_prestador_confirma_una_solicitada_y_el_bloque_sigue_reservado()
    {
        var (cita, bloque) = await UnaCitaAsync(de: _manual);
        Assert.Equal(EstadoCita.Solicitada, cita.Estado);

        var r = await _servicio.ConfirmarAsync(cita.Id, ComoPrestador);

        Assert.True(r.Exito, r.Mensaje);
        Assert.Equal(nameof(EstadoCita.Confirmada), r.Valor!.Estado);
        Assert.Equal(EstadoBloque.Reservado, bloque.Estado);
        Assert.Null(cita.ExpiraEn);   // ya no hay plazo que vencer
    }

    [Fact]
    public async Task El_rechazo_libera_la_hora_para_quien_sea()
    {
        var (cita, bloque) = await UnaCitaAsync(de: _manual);

        var r = await _servicio.RechazarAsync(cita.Id, ComoPrestador, "Ese día estoy en terreno");

        Assert.True(r.Exito, r.Mensaje);
        Assert.Equal(nameof(EstadoCita.Rechazada), r.Valor!.Estado);
        Assert.Equal(EstadoBloque.Libre, bloque.Estado);
        Assert.Equal("Ese día estoy en terreno",
            _base.CambiosCita.Single(c => c.Tipo == TipoCambio.Rechazo).Motivo);
    }

    /// <summary>RN-S4-03: pasado el plazo de 24 h ya no se confirma, se expiró.</summary>
    [Fact]
    public async Task Pasado_el_plazo_de_24_horas_ya_no_se_puede_confirmar()
    {
        var (cita, _) = await UnaCitaAsync(inicio: Ahora.AddDays(5), de: _manual);
        _reloj.Avanzar(ServicioAgenda.PlazoDeRespuestaManual + TimeSpan.FromMinutes(1));

        var r = await _servicio.ConfirmarAsync(cita.Id, ComoPrestador);

        Assert.Equal(Fallas.Conflicto, r.Codigo);
        Assert.Contains("24 horas", r.Mensaje);
    }

    [Fact]
    public async Task Una_cita_automatica_ya_viene_confirmada_y_no_se_vuelve_a_confirmar()
    {
        var (cita, _) = await UnaCitaAsync();

        var r = await _servicio.ConfirmarAsync(cita.Id, ComoPrestador);

        Assert.Equal(Fallas.Conflicto, r.Codigo);
    }

    /// <summary>
    /// El buscador NO confirma su propia cita: eso convertiría el modo manual en automático.
    /// Y recibe el mismo 404 que un desconocido, no un 403.
    /// </summary>
    [Fact]
    public async Task El_buscador_no_puede_confirmar_su_propia_cita()
    {
        var (cita, _) = await UnaCitaAsync(de: _manual);

        var delBuscador = await _servicio.ConfirmarAsync(cita.Id, ComoBuscador);
        var deUnExtrano = await _servicio.ConfirmarAsync(cita.Id, ComoDesconocido);

        Assert.Equal(Fallas.NoEncontrado, delBuscador.Codigo);
        Assert.Equal(deUnExtrano.Mensaje, delBuscador.Mensaje);
        Assert.Equal(EstadoCita.Solicitada, cita.Estado);
    }

    // ========================================================= CU-14 cancelar

    [Theory]
    [InlineData(true)]    // el buscador
    [InlineData(false)]   // el prestador
    public async Task Los_dos_lados_pueden_cancelar_con_tiempo(bool esElBuscador)
    {
        var (cita, bloque) = await UnaCitaAsync(inicio: Ahora.AddDays(3));

        var r = await _servicio.CancelarAsync(new CancelarCitaCmd(
            cita.Id, esElBuscador ? ComoBuscador : ComoPrestador, $"c-{Guid.NewGuid():N}", null));

        Assert.True(r.Exito, r.Mensaje);
        Assert.Equal(nameof(EstadoCita.Cancelada), r.Valor!.Estado);
        Assert.Equal(EstadoBloque.Libre, bloque.Estado);
        Assert.False(_base.CambiosCita.Single(c => c.Tipo == TipoCambio.Cancelacion).EsTardio);
    }

    /// <summary>RN-S4-04. Lo que distingue una cancelación tardía es que queda marcada.</summary>
    [Fact]
    public async Task Cancelar_con_menos_de_doce_horas_queda_marcado_como_tardio()
    {
        var (cita, _) = await UnaCitaAsync(inicio: Ahora.AddHours(6));

        var r = await _servicio.CancelarAsync(new CancelarCitaCmd(
            cita.Id, ComoBuscador, $"c-{Guid.NewGuid():N}", "Se me cruzó una reunión"));

        Assert.True(r.Exito, r.Mensaje);
        var cambio = _base.CambiosCita.Single(c => c.Tipo == TipoCambio.Cancelacion);
        Assert.True(cambio.EsTardio);
        Assert.Equal("Se me cruzó una reunión", cambio.Motivo);
    }

    /// <summary>
    /// Y en una tardía el motivo es OBLIGATORIO: quien se queda con la hora vacía a última
    /// hora merece saber por qué.
    /// </summary>
    [Fact]
    public async Task Una_cancelacion_tardia_sin_motivo_se_rechaza_y_no_cambia_nada()
    {
        var (cita, bloque) = await UnaCitaAsync(inicio: Ahora.AddHours(6));

        var r = await _servicio.CancelarAsync(new CancelarCitaCmd(
            cita.Id, ComoBuscador, $"c-{Guid.NewGuid():N}", null));

        Assert.Equal(Fallas.ReglaDeNegocio, r.Codigo);
        Assert.Contains("12 horas", r.Mensaje);
        Assert.Equal(EstadoCita.Confirmada, cita.Estado);
        Assert.Equal(EstadoBloque.Reservado, bloque.Estado);
    }

    /// <summary>
    /// RN-S4-08: un reintento de red no debe producir un segundo aviso de cancelación. La
    /// segunda llamada devuelve lo mismo en vez de un conflicto.
    /// </summary>
    [Fact]
    public async Task Cancelar_dos_veces_no_es_un_conflicto_ni_deja_dos_cambios()
    {
        var (cita, _) = await UnaCitaAsync(inicio: Ahora.AddDays(3));
        var clave = $"c-{Guid.NewGuid():N}";

        var primera = await _servicio.CancelarAsync(new CancelarCitaCmd(cita.Id, ComoBuscador, clave, null));
        var segunda = await _servicio.CancelarAsync(new CancelarCitaCmd(cita.Id, ComoBuscador, clave, null));

        Assert.True(primera.Exito);
        Assert.True(segunda.Exito, segunda.Mensaje);
        Assert.Equal(primera.Valor!.Id, segunda.Valor!.Id);
        Assert.Single(_base.CambiosCita.Where(c => c.Tipo == TipoCambio.Cancelacion));
    }

    [Fact]
    public async Task Un_extrano_no_puede_cancelar_la_cita_de_otro()
    {
        var (cita, _) = await UnaCitaAsync(inicio: Ahora.AddDays(3));

        var r = await _servicio.CancelarAsync(new CancelarCitaCmd(
            cita.Id, ComoDesconocido, $"c-{Guid.NewGuid():N}", null));

        Assert.Equal(Fallas.NoEncontrado, r.Codigo);
        Assert.Equal(EstadoCita.Confirmada, cita.Estado);
    }

    // ========================================================= CU-14 reprogramar

    /// <summary>
    /// RN-S4-05: reprogramar es cancelar y crear. Las dos cosas que importan son que el
    /// vínculo se conserve y que las dos horas queden en el estado correcto.
    /// </summary>
    [Fact]
    public async Task Reprogramar_cierra_la_vieja_crea_una_nueva_y_mueve_las_dos_horas()
    {
        var (cita, viejo) = await UnaCitaAsync(inicio: Ahora.AddDays(3));
        var nuevo = UnBloque(inicio: Ahora.AddDays(4));

        var r = await _servicio.ReprogramarAsync(new ReprogramarCitaCmd(
            cita.Id, nuevo.Id, ComoBuscador, $"r-{Guid.NewGuid():N}", "Me surgió un viaje"));

        Assert.True(r.Exito, r.Mensaje);
        Assert.True(r.Valor!.Nueva);
        Assert.NotEqual(cita.Id, r.Valor.Id);
        Assert.Equal(nameof(EstadoCita.Confirmada), r.Valor.Estado);

        Assert.Equal(EstadoCita.Reprogramada, cita.Estado);
        Assert.Equal(EstadoBloque.Libre, viejo.Estado);
        Assert.Equal(EstadoBloque.Reservado, nuevo.Estado);
    }

    /// <summary>
    /// `cita_origen_id` apunta SIEMPRE a la primera de la cadena, no a la anterior: así la
    /// elegibilidad de reseña está a un salto y no hay eslabones intermedios que romper
    /// (estados-cita.md, sección 4). Tres reprogramaciones seguidas lo demuestran.
    /// </summary>
    [Fact]
    public async Task La_cadena_de_reprogramaciones_apunta_siempre_a_la_primera()
    {
        var (primera, _) = await UnaCitaAsync(inicio: Ahora.AddDays(3));
        var actual = primera.Id;

        for (var i = 0; i < 3; i++)
        {
            var destino = UnBloque(inicio: Ahora.AddDays(5 + i));
            var r = await _servicio.ReprogramarAsync(new ReprogramarCitaCmd(
                actual, destino.Id, ComoBuscador, $"r{i}-{Guid.NewGuid():N}", null));
            Assert.True(r.Exito, r.Mensaje);
            actual = r.Valor!.Id;
        }

        var cadena = _base.Citas.Where(c => c.Id != primera.Id).ToList();
        Assert.Equal(3, cadena.Count);
        Assert.All(cadena, c => Assert.Equal(primera.Id, c.CitaOrigenId));
        Assert.Null(primera.CitaOrigenId);
    }

    [Fact]
    public async Task La_nueva_cita_conserva_la_nota_y_el_modo_de_la_vieja()
    {
        var (cita, _) = await UnaCitaAsync(inicio: Ahora.AddDays(3));
        var nuevo = UnBloque(inicio: Ahora.AddDays(4));

        var r = await _servicio.ReprogramarAsync(new ReprogramarCitaCmd(
            cita.Id, nuevo.Id, ComoBuscador, $"r-{Guid.NewGuid():N}", null));

        Assert.Equal("Consulta por arriendo", r.Valor!.NotaBuscador);
        Assert.Equal(cita.Modo.ToString(), r.Valor.Modo);
    }

    /// <summary>
    /// Sección 5 de estados-cita.md. El prestador puede mover su hora dentro de las 2 horas y
    /// el buscador no: es la agenda del prestador, y si le surge un imprevisto impedírselo
    /// solo produce una inasistencia.
    /// </summary>
    [Fact]
    public async Task Dentro_de_las_dos_horas_el_prestador_puede_mover_y_el_buscador_no()
    {
        var (cita, _) = await UnaCitaAsync(inicio: Ahora.AddHours(3));
        _reloj.Avanzar(TimeSpan.FromHours(2));   // falta 1 hora para la cita
        var destino = UnBloque(inicio: Ahora.AddDays(2));

        var delBuscador = await _servicio.ReprogramarAsync(new ReprogramarCitaCmd(
            cita.Id, destino.Id, ComoBuscador, $"r-{Guid.NewGuid():N}", null));
        Assert.Equal(Fallas.ReglaDeNegocio, delBuscador.Codigo);
        Assert.Contains("cancelarla", delBuscador.Mensaje);

        var delPrestador = await _servicio.ReprogramarAsync(new ReprogramarCitaCmd(
            cita.Id, destino.Id, ComoPrestador, $"r-{Guid.NewGuid():N}", null));
        Assert.True(delPrestador.Exito, delPrestador.Mensaje);
    }

    [Fact]
    public async Task No_se_reprograma_a_una_hora_de_otro_servicio()
    {
        var (cita, _) = await UnaCitaAsync(inicio: Ahora.AddDays(3));
        var deOtroServicio = UnBloque(inicio: Ahora.AddDays(4), de: _manual);

        var r = await _servicio.ReprogramarAsync(new ReprogramarCitaCmd(
            cita.Id, deOtroServicio.Id, ComoBuscador, $"r-{Guid.NewGuid():N}", null));

        Assert.Equal(Fallas.Validacion, r.Codigo);
        Assert.Contains("mismo servicio", r.Mensaje);
    }

    [Fact]
    public async Task No_se_reprograma_a_una_hora_que_otro_ya_tomo()
    {
        var (cita, _) = await UnaCitaAsync(inicio: Ahora.AddDays(3));
        var ocupado = UnBloque(inicio: Ahora.AddDays(4), estado: EstadoBloque.Reservado);

        var r = await _servicio.ReprogramarAsync(new ReprogramarCitaCmd(
            cita.Id, ocupado.Id, ComoBuscador, $"r-{Guid.NewGuid():N}", null));

        Assert.Equal(Fallas.Conflicto, r.Codigo);
    }

    [Fact]
    public async Task Reprogramar_a_la_misma_hora_no_hace_nada()
    {
        var (cita, viejo) = await UnaCitaAsync(inicio: Ahora.AddDays(3));

        var r = await _servicio.ReprogramarAsync(new ReprogramarCitaCmd(
            cita.Id, viejo.Id, ComoBuscador, $"r-{Guid.NewGuid():N}", null));

        Assert.Equal(Fallas.Validacion, r.Codigo);
        Assert.Equal(EstadoCita.Confirmada, cita.Estado);
    }

    [Fact]
    public async Task Una_cita_cancelada_ya_no_se_reprograma()
    {
        var (cita, _) = await UnaCitaAsync(inicio: Ahora.AddDays(3));
        await _servicio.CancelarAsync(new CancelarCitaCmd(cita.Id, ComoBuscador, $"c-{Guid.NewGuid():N}", null));
        var destino = UnBloque(inicio: Ahora.AddDays(4));

        var r = await _servicio.ReprogramarAsync(new ReprogramarCitaCmd(
            cita.Id, destino.Id, ComoBuscador, $"r-{Guid.NewGuid():N}", null));

        Assert.Equal(Fallas.Conflicto, r.Codigo);
    }

    /// <summary>
    /// El mismo envío dos veces devuelve la misma cita nueva, no dos: reprogramar toca dos
    /// bloques y un reintento podría dejar uno libre y otro reservado (api-rest.md).
    /// </summary>
    [Fact]
    public async Task Reprogramar_dos_veces_con_la_misma_clave_devuelve_la_misma_cita()
    {
        var (cita, _) = await UnaCitaAsync(inicio: Ahora.AddDays(3));
        var destino = UnBloque(inicio: Ahora.AddDays(4));
        var clave = $"r-{Guid.NewGuid():N}";

        var primera = await _servicio.ReprogramarAsync(new ReprogramarCitaCmd(
            cita.Id, destino.Id, ComoBuscador, clave, null));
        var segunda = await _servicio.ReprogramarAsync(new ReprogramarCitaCmd(
            cita.Id, destino.Id, ComoBuscador, clave, null));

        Assert.True(primera.Valor!.Nueva);
        Assert.False(segunda.Valor!.Nueva);
        Assert.Equal(primera.Valor.Id, segunda.Valor.Id);
        Assert.Equal(2, _base.Citas.Count);   // la vieja y la nueva, nada más
    }

    /// <summary>
    /// CC-06, la parte que una lista sí puede contestar: que se pida el bloqueo de los DOS
    /// bloques en una sola consulta ordenada, y que se fije la espera máxima. Que el orden
    /// evite el abrazo mortal se prueba en integración.
    /// </summary>
    [Fact]
    public async Task Reprogramar_bloquea_los_dos_bloques_juntos_y_fija_la_espera()
    {
        var (cita, _) = await UnaCitaAsync(inicio: Ahora.AddDays(3));
        var destino = UnBloque(inicio: Ahora.AddDays(4));

        await _servicio.ReprogramarAsync(new ReprogramarCitaCmd(
            cita.Id, destino.Id, ComoBuscador, $"r-{Guid.NewGuid():N}", null));

        Assert.Equal(1, _repo.BloqueosDeDosFilas);
        Assert.Equal(ServicioAgenda.EsperaDeBloqueo, _repo.EsperaFijada);
    }

    // ========================================================= CS-12 asistencia

    [Theory]
    [InlineData(true, EstadoCita.Realizada)]
    [InlineData(false, EstadoCita.NoAsistio)]
    public async Task El_prestador_marca_la_asistencia_y_el_bloque_queda_vencido(
        bool asistio, EstadoCita esperado)
    {
        var (cita, bloque) = await UnaCitaAsync(inicio: Ahora.AddHours(3));
        cita.Estado = EstadoCita.PendienteAsistencia;   // lo hará A-09 cuando pase la hora

        var r = await _servicio.RegistrarAsistenciaAsync(cita.Id, ComoPrestador, asistio);

        Assert.True(r.Exito, r.Mensaje);
        Assert.Equal(esperado.ToString(), r.Valor!.Estado);
        Assert.Equal(EstadoBloque.Vencido, bloque.Estado);
    }

    /// <summary>
    /// La defensa que cierra CU-16, y la más importante de este archivo: si el buscador
    /// pudiera marcar su propia asistencia, bastaría agendar y automarcar para fabricar
    /// elegibilidad de reseña.
    /// </summary>
    [Fact]
    public async Task El_buscador_no_puede_marcar_su_propia_asistencia()
    {
        var (cita, _) = await UnaCitaAsync(inicio: Ahora.AddHours(3));
        cita.Estado = EstadoCita.PendienteAsistencia;

        var r = await _servicio.RegistrarAsistenciaAsync(cita.Id, ComoBuscador, asistio: true);

        Assert.Equal(Fallas.NoEncontrado, r.Codigo);
        Assert.Equal(EstadoCita.PendienteAsistencia, cita.Estado);
        Assert.False(await _servicio.CitaRealizadaAsync(_buscador.Id, _prestador.Id));
    }

    /// <summary>
    /// Y tampoco antes de que pase la hora: sin esto se podría marcar realizada una cita
    /// futura y reseñar sin que el servicio hubiera ocurrido.
    /// </summary>
    [Fact]
    public async Task No_se_marca_asistencia_de_una_cita_que_todavia_no_ocurrio()
    {
        var (cita, _) = await UnaCitaAsync(inicio: Ahora.AddDays(3));

        var r = await _servicio.RegistrarAsistenciaAsync(cita.Id, ComoPrestador, asistio: true);

        Assert.Equal(Fallas.Conflicto, r.Codigo);
        Assert.Equal(EstadoCita.Confirmada, cita.Estado);
    }

    [Fact]
    public async Task Marcar_asistencia_habilita_la_resena_y_marcar_inasistencia_tambien()
    {
        var (cita, _) = await UnaCitaAsync(inicio: Ahora.AddHours(3));
        cita.Estado = EstadoCita.PendienteAsistencia;

        await _servicio.RegistrarAsistenciaAsync(cita.Id, ComoPrestador, asistio: true);

        Assert.True(await _servicio.CitaRealizadaAsync(_buscador.Id, _prestador.Id));
    }

    // ========================================================= el historial y la versión

    /// <summary>
    /// TBL-19 es un historial inmutable: cada transición deja su fila. Si una no la dejara,
    /// habría un cambio de estado del que no quedaría rastro.
    /// </summary>
    [Fact]
    public async Task Cada_transicion_deja_su_fila_en_el_historial()
    {
        var (cita, _) = await UnaCitaAsync(inicio: Ahora.AddDays(3), de: _manual);
        await _servicio.ConfirmarAsync(cita.Id, ComoPrestador);
        await _servicio.CancelarAsync(new CancelarCitaCmd(cita.Id, ComoBuscador, $"c-{Guid.NewGuid():N}", null));

        var tipos = _base.CambiosCita.Where(c => c.CitaId == cita.Id)
            .OrderBy(c => c.OcurridoEn).Select(c => c.Tipo).ToList();

        Assert.Equal(
            new[] { TipoCambio.Creacion, TipoCambio.Confirmacion, TipoCambio.Cancelacion },
            tipos);
        Assert.All(_base.CambiosCita, c => Assert.NotEqual(default, c.OcurridoEn));
    }

    /// <summary>
    /// CC-02. `version` es token de concurrencia en la base, y un token que nadie incrementa
    /// no detecta nada: la comparación siempre coincide.
    /// </summary>
    [Fact]
    public async Task Cada_cambio_sube_la_version_optimista()
    {
        var (cita, _) = await UnaCitaAsync(inicio: Ahora.AddDays(3), de: _manual);
        var inicial = cita.Version;

        await _servicio.ConfirmarAsync(cita.Id, ComoPrestador);
        var trasConfirmar = cita.Version;
        await _servicio.CancelarAsync(new CancelarCitaCmd(cita.Id, ComoBuscador, $"c-{Guid.NewGuid():N}", null));

        Assert.Equal(inicial + 1, trasConfirmar);
        Assert.Equal(inicial + 2, cita.Version);
    }

    /// <summary>
    /// El motivo puede ser privado («operación de mi madre»). Queda en el historial, que ven
    /// las dos partes, pero NO en el registro de auditoría.
    /// </summary>
    [Fact]
    public async Task El_motivo_de_una_cancelacion_no_llega_al_registro_de_auditoria()
    {
        var (cita, _) = await UnaCitaAsync(inicio: Ahora.AddHours(6));

        await _servicio.CancelarAsync(new CancelarCitaCmd(
            cita.Id, ComoBuscador, $"c-{Guid.NewGuid():N}", "Operación de mi madre"));

        Assert.DoesNotContain(_auditor.Registros,
            a => a.Contexto is not null && a.Contexto.Contains("madre"));
        Assert.Contains("Operación de mi madre",
            _base.CambiosCita.Single(c => c.Tipo == TipoCambio.Cancelacion).Motivo);
    }

    /// <summary>
    /// Las transiciones que NO existen (estados-cita.md, sección 6). Lo que se comprueba es
    /// que un estado terminal sea de verdad terminal.
    /// </summary>
    [Theory]
    [InlineData(EstadoCita.Realizada)]
    [InlineData(EstadoCita.NoAsistio)]
    [InlineData(EstadoCita.Rechazada)]
    [InlineData(EstadoCita.Reprogramada)]
    public async Task Desde_un_estado_terminal_no_se_cancela(EstadoCita terminal)
    {
        var (cita, _) = await UnaCitaAsync(inicio: Ahora.AddDays(3));
        cita.Estado = terminal;

        var r = await _servicio.CancelarAsync(new CancelarCitaCmd(
            cita.Id, ComoBuscador, $"c-{Guid.NewGuid():N}", null));

        Assert.Equal(Fallas.Conflicto, r.Codigo);
        Assert.Equal(terminal, cita.Estado);
    }

    // ========================================================= A-09: lo que mueve el tiempo

    /// <summary>RN-S4-03 y T5. La solicitada sin respuesta expira y la hora vuelve a estar libre.</summary>
    [Fact]
    public async Task Una_solicitada_sin_respuesta_expira_a_las_24_horas_y_libera_la_hora()
    {
        var (cita, bloque) = await UnaCitaAsync(inicio: Ahora.AddDays(5), de: _manual);
        _reloj.Avanzar(ServicioAgenda.PlazoDeRespuestaManual + TimeSpan.FromMinutes(1));

        var r = await _servicio.AvanzarPorElTiempoAsync();

        Assert.Equal(1, r.Expiradas);
        Assert.Equal(EstadoCita.Rechazada, cita.Estado);
        Assert.Equal(EstadoBloque.Libre, bloque.Estado);

        // TBL-19: «nulo si lo hizo el Planificador».
        var cambio = _base.CambiosCita.Single(c => c.Tipo == TipoCambio.Rechazo);
        Assert.Null(cambio.RealizadoPor);
        Assert.Contains("24 horas", cambio.Motivo);
    }

    [Fact]
    public async Task Una_solicitada_dentro_de_plazo_no_se_toca()
    {
        var (cita, bloque) = await UnaCitaAsync(inicio: Ahora.AddDays(5), de: _manual);
        _reloj.Avanzar(TimeSpan.FromHours(23));

        var r = await _servicio.AvanzarPorElTiempoAsync();

        Assert.Equal(0, r.Expiradas);
        Assert.Equal(EstadoCita.Solicitada, cita.Estado);
        Assert.Equal(EstadoBloque.Reservado, bloque.Estado);
    }

    /// <summary>
    /// Mientras el planificador decidía, el prestador pudo confirmar. Vencer algo que ya cambió
    /// sería pisarle la decisión: por eso el estado se vuelve a comprobar DENTRO de la
    /// transacción de cada cita.
    /// </summary>
    [Fact]
    public async Task Una_solicitada_que_el_prestador_confirmo_no_se_expira()
    {
        var (cita, _) = await UnaCitaAsync(inicio: Ahora.AddDays(5), de: _manual);
        await _servicio.ConfirmarAsync(cita.Id, ComoPrestador);
        _reloj.Avanzar(ServicioAgenda.PlazoDeRespuestaManual + TimeSpan.FromHours(1));

        var r = await _servicio.AvanzarPorElTiempoAsync();

        Assert.Equal(0, r.Expiradas);
        Assert.Equal(EstadoCita.Confirmada, cita.Estado);
    }

    /// <summary>
    /// La carrera que la releída de estado existe para cubrir: el planificador armó la lista de
    /// "por expirar" y, antes de llegar a esta cita, el buscador la canceló. Cancelar NO borra
    /// <c>ExpiraEn</c>, así que la cita sigue pareciendo vencida por fecha; lo único que la
    /// protege es volver a mirar el estado DENTRO de su transacción.
    ///
    /// Sin esa releída se escribía un cambio de «rechazo» sobre una cita cancelada: un
    /// historial inmutable que cuenta algo que no ocurrió.
    ///
    /// Se descubrió mutando la guarda y viendo que ninguna prueba secuencial se ponía roja.
    /// </summary>
    [Fact]
    public async Task Si_la_cita_cambio_despues_de_armar_la_lista_no_se_le_escribe_un_rechazo()
    {
        var (cita, bloque) = await UnaCitaAsync(inicio: Ahora.AddDays(5), de: _manual);
        await _servicio.CancelarAsync(new CancelarCitaCmd(cita.Id, ComoBuscador, $"c-{Guid.NewGuid():N}", null));
        Assert.NotNull(cita.ExpiraEn);   // el supuesto que hace posible la carrera

        _reloj.Avanzar(ServicioAgenda.PlazoDeRespuestaManual + TimeSpan.FromHours(1));
        _repo.IdsObsoletosPorExpirar = new[] { cita.Id };

        var r = await _servicio.AvanzarPorElTiempoAsync();

        Assert.Equal(0, r.Expiradas);
        Assert.Equal(EstadoCita.Cancelada, cita.Estado);
        Assert.DoesNotContain(_base.CambiosCita, c => c.CitaId == cita.Id && c.Tipo == TipoCambio.Rechazo);
        Assert.Equal(EstadoBloque.Libre, bloque.Estado);
    }

    /// <summary>T8. Es lo que abre la puerta de CS-12: sin ella ninguna cita puede marcarse.</summary>
    [Fact]
    public async Task Cuando_pasa_la_hora_la_cita_confirmada_espera_asistencia()
    {
        var (cita, bloque) = await UnaCitaAsync(inicio: Ahora.AddHours(3));
        _reloj.Avanzar(TimeSpan.FromHours(4));

        var r = await _servicio.AvanzarPorElTiempoAsync();

        Assert.Equal(1, r.PorAsistir);
        Assert.Equal(EstadoCita.PendienteAsistencia, cita.Estado);
        Assert.Equal(EstadoBloque.Reservado, bloque.Estado);   // T8: sigue reservado
    }

    [Fact]
    public async Task Una_cita_cuya_hora_no_ha_terminado_no_pasa_a_pendiente()
    {
        var (cita, _) = await UnaCitaAsync(inicio: Ahora.AddHours(3));
        _reloj.Avanzar(TimeSpan.FromMinutes(150));   // 30 min antes de que empiece... aún no termina

        await _servicio.AvanzarPorElTiempoAsync();

        Assert.Equal(EstadoCita.Confirmada, cita.Estado);
    }

    /// <summary>
    /// El circuito completo que hasta hoy no podía cerrarse: agendar, que pase la hora, que el
    /// planificador la pase a pendiente, y que el prestador marque asistencia. Cada eslabón
    /// estaba probado por separado; juntos faltaba el segundo, y sin él el tercero era
    /// inalcanzable.
    /// </summary>
    [Fact]
    public async Task Despues_de_la_hora_el_prestador_ya_puede_marcar_asistencia_y_el_buscador_resenar()
    {
        var (cita, _) = await UnaCitaAsync(inicio: Ahora.AddHours(3));
        Assert.False((await _servicio.RegistrarAsistenciaAsync(cita.Id, ComoPrestador, true)).Exito);

        _reloj.Avanzar(TimeSpan.FromHours(4));
        await _servicio.AvanzarPorElTiempoAsync();
        var r = await _servicio.RegistrarAsistenciaAsync(cita.Id, ComoPrestador, asistio: true);

        Assert.True(r.Exito, r.Mensaje);
        Assert.True(await _servicio.CitaRealizadaAsync(_buscador.Id, _prestador.Id));
    }

    /// <summary>
    /// T11. Si el prestador nunca marca, el buscador no puede quedarse sin reseñar por una
    /// omisión que no es suya.
    /// </summary>
    [Fact]
    public async Task A_las_72_horas_sin_marcar_se_asume_realizada_y_el_bloque_vence()
    {
        var (cita, bloque) = await UnaCitaAsync(inicio: Ahora.AddHours(3));
        _reloj.Avanzar(TimeSpan.FromHours(4));
        await _servicio.AvanzarPorElTiempoAsync();
        Assert.Equal(EstadoCita.PendienteAsistencia, cita.Estado);

        _reloj.Avanzar(ServicioAgenda.PlazoParaMarcarAsistencia + TimeSpan.FromMinutes(1));
        var r = await _servicio.AvanzarPorElTiempoAsync();

        Assert.Equal(1, r.Asumidas);
        Assert.Equal(EstadoCita.Realizada, cita.Estado);
        Assert.Equal(EstadoBloque.Vencido, bloque.Estado);
        Assert.True(await _servicio.CitaRealizadaAsync(_buscador.Id, _prestador.Id));
    }

    [Fact]
    public async Task Antes_de_las_72_horas_la_cita_sigue_esperando_asistencia()
    {
        var (cita, _) = await UnaCitaAsync(inicio: Ahora.AddHours(3));
        _reloj.Avanzar(TimeSpan.FromHours(4));
        await _servicio.AvanzarPorElTiempoAsync();

        _reloj.Avanzar(TimeSpan.FromHours(60));
        var r = await _servicio.AvanzarPorElTiempoAsync();

        Assert.Equal(0, r.Asumidas);
        Assert.Equal(EstadoCita.PendienteAsistencia, cita.Estado);
    }

    /// <summary>Correrlo dos veces seguidas no repite nada: es lo que hace seguro al planificador.</summary>
    [Fact]
    public async Task Correr_dos_veces_seguidas_no_repite_las_transiciones()
    {
        var (cita, _) = await UnaCitaAsync(inicio: Ahora.AddHours(3));
        _reloj.Avanzar(TimeSpan.FromHours(4));

        await _servicio.AvanzarPorElTiempoAsync();
        var segunda = await _servicio.AvanzarPorElTiempoAsync();

        Assert.Equal(new ResumenDeCitas(0, 0, 0), segunda);
        Assert.Single(_base.CambiosCita.Where(c => c.CitaId == cita.Id && c.Tipo == TipoCambio.Asistencia));
    }

    [Fact]
    public async Task Una_cita_cancelada_no_pasa_a_esperar_asistencia()
    {
        var (cita, _) = await UnaCitaAsync(inicio: Ahora.AddHours(3));
        await _servicio.CancelarAsync(new CancelarCitaCmd(cita.Id, ComoBuscador, $"c-{Guid.NewGuid():N}", "motivo"));
        _reloj.Avanzar(TimeSpan.FromHours(4));

        var r = await _servicio.AvanzarPorElTiempoAsync();

        Assert.Equal(0, r.PorAsistir);
        Assert.Equal(EstadoCita.Cancelada, cita.Estado);
    }
}
