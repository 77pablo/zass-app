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
/// MOD-06 · S4. Las reglas de la agenda, sin base de datos.
///
/// Lo que NO se prueba aquí, a propósito: la doble reserva concurrente (CC-01) y el único
/// parcial que la impide (R-1). Con listas en memoria la pregunta «¿qué pasa si dos
/// transacciones llegan a la vez?» no existe, y una prueba que finja contestarla sería peor
/// que no tenerla. Está en las de integración, contra PostgreSQL.
/// </summary>
public class AgendaPruebas
{
    private static readonly DateTime Ahora = new(2026, 11, 9, 12, 0, 0, DateTimeKind.Utc);

    private readonly BaseEnMemoria _base = new();
    private readonly RelojFijo _reloj = new(Ahora);
    private readonly AuditoriaEnMemoria _auditor = new();
    private readonly ColaEnMemoria _cola = new();
    private readonly RepositorioAgendaEnMemoria _repo;
    private readonly ServicioAgenda _servicio;

    private readonly Usuario _buscador;
    private readonly PerfilPrestador _prestador;
    private readonly PrestadorServicio _automatico;
    private readonly PrestadorServicio _manual;

    public AgendaPruebas()
    {
        _base.Rubros.Add(new Rubro { Id = 1, Codigo = "derecho", Nombre = "Derecho", Activo = true });
        _base.Servicios.Add(new Servicio
        {
            Id = 201, RubroId = 1, Nombre = "Consulta inicial",
            NombreNormalizado = "consulta inicial", EsCatalogo = true,
        });
        _base.Servicios.Add(new Servicio
        {
            Id = 202, RubroId = 1, Nombre = "Tasación en terreno",
            NombreNormalizado = "tasacion en terreno", EsCatalogo = true,
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
        _base.Usuarios.Add(_buscador);

        var duenoPrestador = new Usuario
        {
            Correo = "prestador@zass.local", HashContrasena = "x",
            Nombre = "Pablo Espinoza", CorreoVerificado = true,
        };
        _base.Usuarios.Add(duenoPrestador);

        _prestador = new PerfilPrestador
        {
            UsuarioId = duenoPrestador.Id, RubroId = 1, Slug = "estudio-espinoza",
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
            PerfilPrestadorId = _prestador.Id, ServicioId = 202,
            DuracionMinutos = 60, ModoAgenda = ModoAgenda.Manual, Activo = true,
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
                new RepositorioSuscripcionesEnMemoria(_base), _base, _reloj, _auditor, _cola));
    }

    /// <summary>Un bloque libre, por omisión mañana a las 10:00 UTC (muy por sobre las 2 h).</summary>
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

    private Task<Dominio.Comun.Resultado<CitaDto>> AgendarAsync(
        BloqueHorario bloque, string? clave = null, string? nota = null) =>
        _servicio.AgendarAsync(new AgendarCmd(
            _buscador.Id, bloque.Id, clave ?? $"clave-{Guid.NewGuid():N}", nota));

    // ========================================================= CU-11 disponibilidad

    [Fact]
    public async Task Definir_disponibilidad_genera_los_bloques_de_la_ventana()
    {
        // Lunes de 09:00 a 11:00, bloques de 30 minutos: 4 por semana, 8 semanas.
        var r = await _servicio.DefinirDisponibilidadAsync(new DefinirDisponibilidadCmd(
            _prestador.Id, _automatico.Id,
            new[] { new FranjaCmd(1, new TimeOnly(9, 0), new TimeOnly(11, 0), 30) }));

        Assert.True(r.Exito, r.Mensaje);
        Assert.Equal(4 * ServicioAgenda.SemanasDeVentana, r.Valor);
        Assert.All(_base.Bloques, b => Assert.Equal(EstadoBloque.Libre, b.Estado));
        Assert.All(_base.Bloques, b => Assert.Equal(DayOfWeek.Monday, b.InicioEn.DayOfWeek));
    }

    [Fact]
    public async Task Definir_dos_veces_no_duplica_los_bloques()
    {
        var franja = new[] { new FranjaCmd(1, new TimeOnly(9, 0), new TimeOnly(11, 0), 30) };

        await _servicio.DefinirDisponibilidadAsync(
            new DefinirDisponibilidadCmd(_prestador.Id, _automatico.Id, franja));
        var cuantos = _base.Bloques.Count;
        await _servicio.DefinirDisponibilidadAsync(
            new DefinirDisponibilidadCmd(_prestador.Id, _automatico.Id, franja));

        // La regla vieja se desactiva y sus bloques libres se van; la nueva los vuelve a
        // generar. El total no crece.
        Assert.Equal(cuantos, _base.Bloques.Count);
        Assert.Single(_base.Bloques.Select(b => b.InicioEn).Distinct()
            .Where(i => i == _base.Bloques.Min(x => x.InicioEn)));
    }

    /// <summary>
    /// RN-S4-09, y es la regla que más daño haría si faltara: alguien cambia su horario y
    /// las citas que ya había dado se evaporan.
    /// </summary>
    [Fact]
    public async Task Cambiar_la_disponibilidad_no_toca_un_bloque_ya_reservado()
    {
        await _servicio.DefinirDisponibilidadAsync(new DefinirDisponibilidadCmd(
            _prestador.Id, _automatico.Id,
            new[] { new FranjaCmd(1, new TimeOnly(9, 0), new TimeOnly(11, 0), 30) }));

        var reservado = _base.Bloques.OrderBy(b => b.InicioEn).First();
        reservado.Reservar();

        await _servicio.DefinirDisponibilidadAsync(new DefinirDisponibilidadCmd(
            _prestador.Id, _automatico.Id,
            new[] { new FranjaCmd(3, new TimeOnly(15, 0), new TimeOnly(16, 0), 30) }));

        Assert.Contains(_base.Bloques, b => b.Id == reservado.Id);
        Assert.Equal(EstadoBloque.Reservado,
            _base.Bloques.Single(b => b.Id == reservado.Id).Estado);
    }

    [Fact]
    public async Task Dos_franjas_del_mismo_dia_que_se_pisan_se_rechazan_diciendo_cuales()
    {
        var r = await _servicio.DefinirDisponibilidadAsync(new DefinirDisponibilidadCmd(
            _prestador.Id, _automatico.Id,
            new[]
            {
                new FranjaCmd(1, new TimeOnly(9, 0), new TimeOnly(12, 0), 30),
                new FranjaCmd(1, new TimeOnly(11, 0), new TimeOnly(13, 0), 30),
            }));

        Assert.False(r.Exito);
        Assert.Equal(Fallas.Validacion, r.Codigo);
        Assert.Contains("09:00", r.Mensaje);
        Assert.Contains("11:00", r.Mensaje);
    }

    [Fact]
    public async Task El_horario_del_servicio_de_otro_prestador_no_existe()
    {
        var ajeno = new PerfilPrestador { UsuarioId = Guid.NewGuid(), Slug = "otro" };
        _base.PerfilesPrestador.Add(ajeno);

        var r = await _servicio.DefinirDisponibilidadAsync(new DefinirDisponibilidadCmd(
            ajeno.Id, _automatico.Id,
            new[] { new FranjaCmd(1, new TimeOnly(9, 0), new TimeOnly(10, 0), 30) }));

        // NoEncontrado y no NoAutorizado: distinguirlos diría que ese servicio existe.
        Assert.Equal(Fallas.NoEncontrado, r.Codigo);
    }

    // ========================================================= CU-12 el calendario

    [Fact]
    public async Task El_calendario_no_dice_con_quien_esta_ocupado_un_bloque()
    {
        UnBloque(estado: EstadoBloque.Reservado);

        var r = await _servicio.BloquesLibresAsync(
            _automatico.Id, Ahora, Ahora.AddDays(7));

        var bloque = Assert.Single(r.Valor!.Bloques);
        Assert.Equal("ocupado", bloque.Estado);

        // El DTO no tiene por dónde filtrarlo, y eso es la prueba: si alguien agregara un
        // campo con el buscador, esto seguiría verde pero el tipo tendría una propiedad más.
        Assert.Equal(4, typeof(BloqueDto).GetProperties().Length);
    }

    [Fact]
    public async Task Un_bloque_a_menos_de_dos_horas_se_muestra_ocupado_y_no_libre()
    {
        UnBloque(inicio: Ahora.AddHours(1));

        var r = await _servicio.BloquesLibresAsync(_automatico.Id, Ahora, Ahora.AddDays(1));

        Assert.Equal("ocupado", Assert.Single(r.Valor!.Bloques).Estado);
    }

    /// <summary>CS-11: el bloqueo no modifica los bloques, se cruza al consultar.</summary>
    [Fact]
    public async Task Un_feriado_tapa_los_bloques_de_ese_dia_sin_borrarlos()
    {
        var b = UnBloque();
        await _servicio.BloquearFechaAsync(new BloquearFechaCmd(
            _prestador.Id, b.InicioEn.Date, b.InicioEn.Date.AddDays(1), "Feriado"));

        var r = await _servicio.BloquesLibresAsync(_automatico.Id, Ahora, Ahora.AddDays(7));

        Assert.Equal("bloqueado", Assert.Single(r.Valor!.Bloques).Estado);
        Assert.Equal(EstadoBloque.Libre, _base.Bloques.Single(x => x.Id == b.Id).Estado);
    }

    [Fact]
    public async Task El_motivo_del_bloqueo_es_privado_y_no_llega_a_la_auditoria()
    {
        await _servicio.BloquearFechaAsync(new BloquearFechaCmd(
            _prestador.Id, Ahora.AddDays(1), Ahora.AddDays(2), "Operación de mi madre"));

        Assert.DoesNotContain(_auditor.Registros,
            a => a.Contexto is not null && a.Contexto.Contains("madre"));
    }

    [Fact]
    public async Task La_agenda_de_un_perfil_sin_publicar_no_existe()
    {
        _prestador.Estado = EstadoPerfil.Borrador;
        UnBloque();

        var r = await _servicio.BloquesLibresAsync(_automatico.Id, Ahora, Ahora.AddDays(7));

        Assert.Equal(Fallas.NoEncontrado, r.Codigo);
    }

    // ========================================================= CU-12 agendar automático

    [Fact]
    public async Task Tomar_un_bloque_libre_deja_la_cita_confirmada_al_instante()
    {
        var b = UnBloque();

        var r = await AgendarAsync(b);

        Assert.True(r.Exito, r.Mensaje);
        Assert.Equal(nameof(EstadoCita.Confirmada), r.Valor!.Estado);
        Assert.True(r.Valor.Nueva);
        Assert.StartsWith("ZS-", r.Valor.Codigo);
        Assert.Equal(EstadoBloque.Reservado, _base.Bloques.Single(x => x.Id == b.Id).Estado);
        Assert.Null(r.Valor.ExpiraEn);
    }

    /// <summary>
    /// El paso 11 de CU-12. Es la única puerta a la elegibilidad de reseña: si el contacto no
    /// queda, quien de verdad fue atendido no podrá opinar y nadie sabrá por qué.
    /// </summary>
    [Fact]
    public async Task Agendar_deja_el_contacto_que_habilitara_la_resena()
    {
        var b = UnBloque();

        var r = await AgendarAsync(b);

        var contacto = Assert.Single(_base.Contactos);
        Assert.Equal(_buscador.Id, contacto.BuscadorId);
        Assert.Equal(_prestador.Id, contacto.PerfilPrestadorId);
        Assert.Equal(r.Valor!.Id, contacto.CitaId);
        Assert.Equal(OrigenContacto.Cita, contacto.Origen);
    }

    [Fact]
    public async Task Agendar_deja_el_cambio_de_creacion_en_el_historial()
    {
        var r = await AgendarAsync(UnBloque());

        var cambio = Assert.Single(_base.CambiosCita);
        Assert.Equal(r.Valor!.Id, cambio.CitaId);
        Assert.Equal(TipoCambio.Creacion, cambio.Tipo);
        Assert.Null(cambio.EstadoAnterior);
        Assert.Equal(EstadoCita.Confirmada, cambio.EstadoNuevo);
        Assert.Equal(_buscador.Id, cambio.RealizadoPor);
    }

    /// <summary>
    /// CC-01 en la parte que una lista SÍ puede comprobar: que el servicio pida el bloqueo de
    /// fila y lo haga dentro de una transacción. Que el bloqueo funcione es cosa de integración.
    /// </summary>
    [Fact]
    public async Task Agendar_pide_el_bloqueo_de_fila_dentro_de_una_transaccion()
    {
        await AgendarAsync(UnBloque());

        Assert.Equal(1, _repo.BloqueosDeFila);
        Assert.Equal(1, _base.Transacciones);
    }

    // ========================================================= CU-13 manual

    [Fact]
    public async Task Un_servicio_manual_deja_la_cita_solicitada_y_con_plazo_de_24_horas()
    {
        var b = UnBloque(de: _manual);

        var r = await AgendarAsync(b);

        Assert.Equal(nameof(EstadoCita.Solicitada), r.Valor!.Estado);
        Assert.Equal(nameof(ModoAgenda.Manual), r.Valor.Modo);
        Assert.Equal(Ahora + ServicioAgenda.PlazoDeRespuestaManual, r.Valor.ExpiraEn);

        // El bloque se ocupa igual: lo libera la expiración, no el modo (RN-S4-03).
        Assert.Equal(EstadoBloque.Reservado, _base.Bloques.Single(x => x.Id == b.Id).Estado);
    }

    // ========================================================= lo que no se puede agendar

    [Fact]
    public async Task No_se_agenda_con_menos_de_dos_horas_de_anticipacion()
    {
        var r = await AgendarAsync(UnBloque(inicio: Ahora.AddMinutes(90)));

        Assert.False(r.Exito);
        Assert.Equal(Fallas.Validacion, r.Codigo);
        Assert.Contains("2 horas", r.Mensaje);
    }

    [Fact]
    public async Task No_se_agenda_en_el_pasado()
    {
        var r = await AgendarAsync(UnBloque(inicio: Ahora.AddDays(-1)));

        Assert.False(r.Exito);
    }

    [Fact]
    public async Task Un_bloque_que_otro_ya_tomo_responde_conflicto()
    {
        var r = await AgendarAsync(UnBloque(estado: EstadoBloque.Reservado));

        Assert.Equal(Fallas.Conflicto, r.Codigo);
        Assert.Contains("recién", r.Mensaje);
    }

    /// <summary>Flujo 4D: el prestador bloqueó el horario mientras el buscador elegía.</summary>
    [Fact]
    public async Task Un_bloque_bloqueado_lo_dice_con_su_propio_mensaje()
    {
        var r = await AgendarAsync(UnBloque(estado: EstadoBloque.Bloqueado));

        Assert.Equal(Fallas.Conflicto, r.Codigo);
        Assert.Contains("bloqueó", r.Mensaje);
    }

    [Fact]
    public async Task Un_feriado_impide_agendar_aunque_el_bloque_siga_libre()
    {
        var b = UnBloque();
        await _servicio.BloquearFechaAsync(new BloquearFechaCmd(
            _prestador.Id, b.InicioEn.AddHours(-1), b.FinEn.AddHours(1), null));

        var r = await AgendarAsync(b);

        Assert.Equal(Fallas.Conflicto, r.Codigo);
        Assert.Contains("bloqueó", r.Mensaje);
    }

    /// <summary>RN-S4-06, flujo 4E.</summary>
    [Fact]
    public async Task Mas_de_tres_citas_activas_con_el_mismo_prestador_se_rechaza()
    {
        for (var i = 0; i < ServicioAgenda.MaximoCitasActivasPorPrestador; i++)
            Assert.True((await AgendarAsync(UnBloque(inicio: Ahora.AddDays(2 + i)))).Exito);

        var r = await AgendarAsync(UnBloque(inicio: Ahora.AddDays(9)));

        Assert.Equal(Fallas.Conflicto, r.Codigo);
        Assert.Contains("Cancela una", r.Mensaje);
    }

    [Fact]
    public async Task Una_cita_cancelada_no_cuenta_para_el_tope()
    {
        for (var i = 0; i < ServicioAgenda.MaximoCitasActivasPorPrestador; i++)
            await AgendarAsync(UnBloque(inicio: Ahora.AddDays(2 + i)));
        _base.Citas[0].Cancelar();

        Assert.True((await AgendarAsync(UnBloque(inicio: Ahora.AddDays(9)))).Exito);
    }

    /// <summary>
    /// RN-S3-09. Es la misma regla que apaga la ficha de la tarjeta NFC (RN-S6-10): sin
    /// suscripción vigente el prestador no aparece y tampoco da horas.
    /// </summary>
    [Fact]
    public async Task Un_prestador_sin_suscripcion_vigente_no_da_horas()
    {
        _base.Suscripciones.Single().VigenteHasta = Ahora.AddDays(-1);

        var r = await AgendarAsync(UnBloque());

        Assert.Equal(Fallas.Conflicto, r.Codigo);
        Assert.Contains("no está recibiendo horas", r.Mensaje);
        Assert.Empty(_base.Citas);
    }

    [Fact]
    public async Task Un_servicio_desactivado_no_da_horas()
    {
        var b = UnBloque();
        _automatico.Activo = false;

        Assert.Equal(Fallas.Conflicto, (await AgendarAsync(b)).Codigo);
    }

    [Fact]
    public async Task Un_bloque_que_no_existe_responde_que_recargue()
    {
        var r = await _servicio.AgendarAsync(new AgendarCmd(
            _buscador.Id, Guid.NewGuid(), "clave-de-prueba-1", null));

        Assert.Equal(Fallas.NoEncontrado, r.Codigo);
    }

    // ========================================================= CC-04 idempotencia

    /// <summary>
    /// Flujo 4B de CU-12: el doble clic. Devuelve la MISMA cita, no una segunda, y
    /// <c>Nueva</c> en false es lo que hace que la API responda 200 en vez de 201.
    /// </summary>
    [Fact]
    public async Task El_mismo_envio_dos_veces_devuelve_la_misma_cita()
    {
        var b = UnBloque();
        const string clave = "intencion-unica-1";

        var primera = await AgendarAsync(b, clave);
        var segunda = await AgendarAsync(b, clave);

        Assert.True(primera.Valor!.Nueva);
        Assert.False(segunda.Valor!.Nueva);
        Assert.Equal(primera.Valor.Id, segunda.Valor.Id);
        Assert.Equal(primera.Valor.Codigo, segunda.Valor.Codigo);
        Assert.Single(_base.Citas);
        Assert.Single(_base.Contactos);
    }

    /// <summary>
    /// Dos claves distintas son dos intenciones: la segunda choca con el bloque ya reservado.
    /// Sin esto, «idempotente» sería «no se puede agendar dos veces nunca».
    /// </summary>
    [Fact]
    public async Task Dos_claves_distintas_sobre_el_mismo_bloque_no_son_el_mismo_envio()
    {
        var b = UnBloque();

        Assert.True((await AgendarAsync(b, "intencion-uno-1")).Exito);
        var segunda = await AgendarAsync(b, "intencion-dos-2");

        Assert.Equal(Fallas.Conflicto, segunda.Codigo);
    }

    [Fact]
    public async Task Sin_clave_de_idempotencia_no_se_agenda()
    {
        var r = await _servicio.AgendarAsync(new AgendarCmd(
            _buscador.Id, UnBloque().Id, "   ", null));

        Assert.Equal(Fallas.Validacion, r.Codigo);
        Assert.Empty(_base.Citas);
    }

    // ========================================================= API-37 y MOD-11

    [Fact]
    public async Task Mis_citas_trae_las_que_pedi_y_las_que_me_pidieron()
    {
        await AgendarAsync(UnBloque(inicio: Ahora.AddDays(3)));

        var comoBuscador = await _servicio.MisCitasAsync(_buscador.Id, null);
        var comoPrestador = await _servicio.MisCitasAsync(
            _base.Usuarios.Single(u => u.Correo == "prestador@zass.local").Id, _prestador.Id);

        Assert.Single(comoBuscador);
        Assert.Single(comoPrestador);
        Assert.Equal(comoBuscador[0].Id, comoPrestador[0].Id);
    }

    [Fact]
    public async Task Mis_citas_no_trae_las_de_otra_persona()
    {
        await AgendarAsync(UnBloque());

        Assert.Empty(await _servicio.MisCitasAsync(Guid.NewGuid(), null));
    }

    /// <summary>
    /// La consulta que usa MOD-11. Una cita confirmada NO habilita: tiene que haber pasado la
    /// hora y el prestador haber marcado asistencia. Es la defensa que cierra CU-16: sin ella
    /// bastaría agendar para poder reseñar.
    /// </summary>
    [Fact]
    public async Task Una_cita_confirmada_todavia_no_habilita_a_resenar()
    {
        await AgendarAsync(UnBloque());

        Assert.False(await _servicio.CitaRealizadaAsync(_buscador.Id, _prestador.Id));
    }

    [Theory]
    [InlineData(EstadoCita.Realizada, true)]
    [InlineData(EstadoCita.NoAsistio, true)]   // si faltó el prestador, se puede reseñar
    [InlineData(EstadoCita.Confirmada, false)]
    [InlineData(EstadoCita.Cancelada, false)]
    [InlineData(EstadoCita.PendienteAsistencia, false)]
    public async Task Solo_una_cita_cumplida_habilita_a_resenar(EstadoCita estado, bool habilita)
    {
        await AgendarAsync(UnBloque());
        _base.Citas.Single().Estado = estado;

        Assert.Equal(habilita, await _servicio.CitaRealizadaAsync(_buscador.Id, _prestador.Id));
    }
}
