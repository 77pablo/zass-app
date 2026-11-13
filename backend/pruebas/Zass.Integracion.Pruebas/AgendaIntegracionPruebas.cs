using Microsoft.EntityFrameworkCore;
using Npgsql;
using Zass.Aplicacion.Agenda;
using Zass.Aplicacion.Catalogo;
using Zass.Aplicacion.Identidad;
using Zass.Aplicacion.Suscripciones;
using Zass.Dominio.Agenda;
using Zass.Dominio.Catalogo;
using Zass.Dominio.Comun;
using Zass.Dominio.Identidad;
using Zass.Dominio.Suscripciones;
using Zass.Infraestructura.Datos;
using Zass.Infraestructura.Datos.Repositorios;
using Zass.Infraestructura.Seguridad;

namespace Zass.Integracion.Pruebas;

/// <summary>
/// MOD-06 contra PostgreSQL de verdad, y en particular **CC-01 y R-1**: lo único del módulo
/// que una lista en memoria no puede contestar.
///
/// Estas pruebas NO van dentro de una transacción que se deshace, porque lo que miden es qué
/// pasa cuando dos transacciones distintas compiten por la misma fila: con una transacción
/// envolvente la segunda se quedaría esperando el commit de la primera. Cada prueba limpia
/// lo que creó, en orden inverso a las claves ajenas.
/// </summary>
public class AgendaIntegracionPruebas : IAsyncLifetime
{
    private readonly List<ZassDbContext> _contextos = new();
    private readonly RelojUtc _reloj = new();

    private Guid _usuarioPrestador;
    private Guid _usuarioBuscador;
    private Guid _perfil;
    private Guid _servicioAutomatico;
    private int _servicioCatalogo;

    /// <summary>Un «proceso» con su propio contexto y conexión, como una réplica de la API.</summary>
    private (ServicioAgenda Agenda, ZassDbContext Ctx) UnaReplica()
    {
        var ctx = ContextoDePrueba.Crear();
        _contextos.Add(ctx);
        var trabajo = new UnidadDeTrabajoEfCore(ctx);
        var auditor = new AuditorEfCore(ctx);
        var agenda = new ServicioAgenda(
            new RepositorioAgendaEfCore(ctx), trabajo, _reloj, auditor,
            new ContactosEfCore(ctx),
            new ServicioCatalogo(new RepositorioCatalogoEfCore(ctx), trabajo, _reloj, auditor),
            new ServicioIdentidad(
                new RepositorioUsuariosEfCore(ctx), new RepositorioPerfilesEfCore(ctx)),
            new ServicioSuscripciones(
                new RepositorioSuscripcionesEfCore(ctx), trabajo, _reloj, auditor,
                new ColaNotificacionesEfCore(ctx)));
        return (agenda, ctx);
    }

    public async Task InitializeAsync()
    {
        using var ctx = ContextoDePrueba.Crear();
        var marca = Guid.NewGuid().ToString("N")[..12];

        var prestador = new Usuario
        {
            Correo = $"ag-p-{marca}@zass.local", HashContrasena = "x",
            Nombre = "Pablo Espinoza", CorreoVerificado = true,
        };
        var buscador = new Usuario
        {
            Correo = $"ag-b-{marca}@zass.local", HashContrasena = "x",
            Nombre = "Ana Pérez", CorreoVerificado = true, Estado = EstadoCuenta.Activa,
        };
        ctx.Usuarios.AddRange(prestador, buscador);
        // Reseñas exige cuenta de buscador, no solo un usuario: sin el rol, EsElegible responde
        // «necesitas una cuenta de buscador» aunque la cita esté realizada.
        ctx.UsuarioRoles.Add(new UsuarioRol
        {
            UsuarioId = buscador.Id,
            RolId = (await ctx.Roles.AsNoTracking().SingleAsync(r => r.Codigo == "buscador")).Id,
        });

        var rubro = await ctx.Rubros.FirstAsync(r => r.Activo);
        var perfil = new PerfilPrestador
        {
            UsuarioId = prestador.Id, RubroId = rubro.Id, Slug = $"ag-{marca}",
            NombrePublico = "Estudio de prueba", Estado = EstadoPerfil.Publicado,
            PublicadoEn = _reloj.Ahora.AddDays(-2),
        };
        ctx.PerfilesPrestador.Add(perfil);

        var servicio = new Servicio
        {
            RubroId = rubro.Id, Nombre = $"Consulta {marca}",
            NombreNormalizado = $"consulta {marca}", EsCatalogo = false,
        };
        ctx.Servicios.Add(servicio);
        await ctx.SaveChangesAsync();

        var ofrecido = new PrestadorServicio
        {
            PerfilPrestadorId = perfil.Id, ServicioId = servicio.Id,
            DuracionMinutos = 30, ModoAgenda = ModoAgenda.Automatico, Activo = true,
        };
        ctx.PrestadorServicios.Add(ofrecido);

        var plan = await ctx.Planes.FirstAsync(p => p.Codigo == "prueba");
        ctx.Suscripciones.Add(new Suscripcion
        {
            PerfilPrestadorId = perfil.Id, PlanId = plan.Id,
            Estado = EstadoSuscripcion.Activa,
            IniciaEn = _reloj.Ahora.AddDays(-2), VigenteHasta = _reloj.Ahora.AddDays(28),
        });
        await ctx.SaveChangesAsync();

        _usuarioPrestador = prestador.Id;
        _usuarioBuscador = buscador.Id;
        _perfil = perfil.Id;
        _servicioAutomatico = ofrecido.Id;
        _servicioCatalogo = servicio.Id;
    }

    public async Task DisposeAsync()
    {
        foreach (var c in _contextos) c.Dispose();

        using var ctx = ContextoDePrueba.Crear();
        // En orden inverso a las claves ajenas: contacto y cambio cuelgan de cita, cita del
        // bloque, el bloque del servicio ofrecido.
        await ctx.Database.ExecuteSqlRawAsync(
            """
            DELETE FROM contactos         WHERE perfil_prestador_id = {0};
            DELETE FROM cambios_cita      WHERE cita_id IN (SELECT id FROM citas WHERE perfil_prestador_id = {0});
            DELETE FROM citas             WHERE perfil_prestador_id = {0};
            DELETE FROM bloques           WHERE perfil_prestador_id = {0};
            DELETE FROM bloqueos_agenda   WHERE perfil_prestador_id = {0};
            DELETE FROM disponibilidades  WHERE perfil_prestador_id = {0};
            DELETE FROM suscripciones     WHERE perfil_prestador_id = {0};
            DELETE FROM prestador_servicios WHERE perfil_prestador_id = {0};
            DELETE FROM servicios         WHERE id = {3};
            DELETE FROM auditoria         WHERE actor_id IN ({1}, {2});
            DELETE FROM perfiles_prestador WHERE id = {0};
            DELETE FROM notificaciones    WHERE destinatario_id IN ({1}, {2});
            DELETE FROM usuario_roles     WHERE usuario_id IN ({1}, {2});
            DELETE FROM usuarios          WHERE id IN ({1}, {2});
            """,
            _perfil, _usuarioPrestador, _usuarioBuscador, _servicioCatalogo);
    }

    private async Task<Guid> UnBloqueLibreAsync(DateTime? inicio = null)
    {
        using var ctx = ContextoDePrueba.Crear();
        var desde = inicio ?? _reloj.Ahora.AddDays(1);
        var b = new BloqueHorario
        {
            DisponibilidadId = Guid.Empty,
            PerfilPrestadorId = _perfil,
            PrestadorServicioId = _servicioAutomatico,
            InicioEn = desde,
            FinEn = desde.AddMinutes(30),
            Estado = EstadoBloque.Libre,
        };

        // DisponibilidadId es obligatoria y apunta a TBL-16: se crea una regla mínima para
        // colgarle el bloque, igual que haría API-33.
        var regla = new Disponibilidad
        {
            PerfilPrestadorId = _perfil,
            PrestadorServicioId = _servicioAutomatico,
            DiaSemana = (short)(((int)desde.DayOfWeek + 6) % 7 + 1),
            HoraInicio = TimeOnly.FromDateTime(desde),
            HoraFin = TimeOnly.FromDateTime(desde).AddMinutes(30),
            DuracionBloqueMin = 30,
            VigenteDesde = DateOnly.FromDateTime(desde),
        };
        ctx.Disponibilidades.Add(regla);
        await ctx.SaveChangesAsync();

        b.DisponibilidadId = regla.Id;
        ctx.Bloques.Add(b);
        await ctx.SaveChangesAsync();
        return b.Id;
    }

    // ========================================================= CC-01

    /// <summary>
    /// El fallo más grave posible del producto: dos personas con la misma hora. Diez
    /// solicitudes a la vez sobre el mismo bloque, cada una con su propia conexión y su
    /// propia clave de idempotencia (son diez intenciones distintas, no un doble clic).
    ///
    /// Exactamente una tiene que ganar. Las otras nueve esperan en el <c>FOR UPDATE</c>, leen
    /// el estado ya actualizado y se retiran con 409.
    /// </summary>
    [Fact]
    public async Task Diez_personas_sobre_el_mismo_bloque_y_solo_una_se_queda_con_la_hora()
    {
        var bloque = await UnBloqueLibreAsync();
        var replicas = Enumerable.Range(0, 10).Select(_ => UnaReplica()).ToList();

        // Task.Run y una barrera, y las dos cosas hacen falta. Sin Task.Run las diez llamadas
        // se encadenan en el mismo hilo (cada una corre hasta su primer await de verdad y la
        // primera suele TERMINAR antes de que la segunda abra su conexion), y entonces esta
        // prueba pasaba IGUAL sin el FOR UPDATE: no media nada. Se descubrio quitandolo.
        using var partida = new Barrier(replicas.Count);
        var resultados = await Task.WhenAll(replicas.Select(r => Task.Run(() =>
        {
            partida.SignalAndWait();
            return r.Agenda.AgendarAsync(new AgendarCmd(
                _usuarioBuscador, bloque, $"carrera-{Guid.NewGuid():N}", null));
        })));

        Assert.Equal(1, resultados.Count(r => r.Exito));
        Assert.All(resultados.Where(r => !r.Exito), r => Assert.Equal(Fallas.Conflicto, r.Codigo));

        using var ctx = ContextoDePrueba.Crear();
        Assert.Equal(1, await ctx.Citas.CountAsync(c => c.BloqueHorarioId == bloque));
        Assert.Equal(EstadoBloque.Reservado,
            (await ctx.Bloques.SingleAsync(b => b.Id == bloque)).Estado);
    }

    /// <summary>
    /// R-1, la capa que no depende del código. Se salta el servicio entero y se insertan dos
    /// citas activas sobre el mismo bloque a mano: la base tiene que rechazar la segunda.
    ///
    /// Es la prueba que seguiría protegiendo el producto si alguien agregara mañana un camino
    /// sin <c>FOR UPDATE</c>.
    /// </summary>
    [Fact]
    public async Task La_base_rechaza_dos_citas_activas_sobre_el_mismo_bloque()
    {
        var bloque = await UnBloqueLibreAsync();
        using var ctx = ContextoDePrueba.Crear();

        ctx.Citas.Add(NuevaCitaCruda(bloque, EstadoCita.Confirmada));
        await ctx.SaveChangesAsync();

        ctx.Citas.Add(NuevaCitaCruda(bloque, EstadoCita.Solicitada));
        var choque = await Assert.ThrowsAsync<DbUpdateException>(() => ctx.SaveChangesAsync());

        var pg = Assert.IsType<PostgresException>(choque.InnerException);
        Assert.Equal("23505", pg.SqlState);
        Assert.Equal("ux_cita_bloque_activo", pg.ConstraintName);
    }

    /// <summary>
    /// El contrapunto de la anterior, y el que impide que R-1 sea demasiado estricta: una vez
    /// cancelada, el bloque se puede volver a dar. Sin el `WHERE estado IN (...)` del índice,
    /// una cancelación dejaría la hora muerta para siempre.
    /// </summary>
    [Fact]
    public async Task Una_cita_cancelada_deja_volver_a_agendar_ese_bloque()
    {
        var bloque = await UnBloqueLibreAsync();
        using var ctx = ContextoDePrueba.Crear();

        var primera = NuevaCitaCruda(bloque, EstadoCita.Confirmada);
        ctx.Citas.Add(primera);
        await ctx.SaveChangesAsync();

        primera.Estado = EstadoCita.Cancelada;
        await ctx.SaveChangesAsync();

        ctx.Citas.Add(NuevaCitaCruda(bloque, EstadoCita.Confirmada));
        await ctx.SaveChangesAsync();   // no debe lanzar

        Assert.Equal(2, await ctx.Citas.CountAsync(c => c.BloqueHorarioId == bloque));
    }

    private Cita NuevaCitaCruda(Guid bloqueId, EstadoCita estado) => new()
    {
        Codigo = Cita.GenerarCodigo(),
        BloqueHorarioId = bloqueId,
        BuscadorId = _usuarioBuscador,
        PrestadorServicioId = _servicioAutomatico,
        PerfilPrestadorId = _perfil,
        Estado = estado,
        Modo = ModoAgenda.Automatico,
        CreadaEn = _reloj.Ahora,
    };

    // ========================================================= T10 · quién faltó, contra la base

    /// <summary>
    /// `ck_cita_quien_falto` sobre la tabla de verdad: alguien marcado como ausente en una cita
    /// que no quedó en `no_asistio` es un estado imposible, y la base lo rechaza aunque el
    /// código lo intente.
    ///
    /// Es la capa que seguiría protegiendo la decisión del 11-nov-2026 si mañana alguien
    /// escribiera una ruta nueva que mueve el estado y se olvida de la columna: la elegibilidad
    /// de reseña empezaría a decir «sí» justo donde la decisión dice «no».
    /// </summary>
    [Theory]
    [InlineData(EstadoCita.Solicitada, FaltaEnCita.Cliente)]
    [InlineData(EstadoCita.Confirmada, FaltaEnCita.Prestador)]
    [InlineData(EstadoCita.PendienteAsistencia, FaltaEnCita.Cliente)]
    [InlineData(EstadoCita.Realizada, FaltaEnCita.Cliente)]
    [InlineData(EstadoCita.Realizada, FaltaEnCita.Prestador)]
    [InlineData(EstadoCita.Realizada, FaltaEnCita.Discrepancia)]
    [InlineData(EstadoCita.Cancelada, FaltaEnCita.Prestador)]
    [InlineData(EstadoCita.Rechazada, FaltaEnCita.Cliente)]
    public async Task La_base_rechaza_un_ausente_en_una_cita_que_no_quedo_en_no_asistio(
        EstadoCita estado, FaltaEnCita quienFalto)
    {
        var bloque = await UnBloqueLibreAsync();
        await using var ctx = ContextoDePrueba.Crear();

        var cita = NuevaCitaCruda(bloque, estado);
        cita.QuienFalto = quienFalto;
        ctx.Citas.Add(cita);

        var choque = await Assert.ThrowsAsync<DbUpdateException>(() => ctx.SaveChangesAsync());

        var pg = Assert.IsType<PostgresException>(choque.InnerException);
        Assert.Equal(PostgresErrorCodes.CheckViolation, pg.SqlState);
        Assert.Equal("ck_cita_quien_falto", pg.ConstraintName);
    }

    /// <summary>
    /// El contrapunto, y el que impide que el CHECK sea de más: con la cita en `no_asistio`
    /// caben los cuatro desenlaces, y `Nadie` cabe en cualquier estado. Sin esto, un CHECK con
    /// un <c>AND</c> donde va el <c>OR</c> pasaría la prueba de arriba y no dejaría guardar
    /// ninguna cita.
    /// </summary>
    [Theory]
    [InlineData(EstadoCita.NoAsistio, FaltaEnCita.Nadie)]
    [InlineData(EstadoCita.NoAsistio, FaltaEnCita.Cliente)]
    [InlineData(EstadoCita.NoAsistio, FaltaEnCita.Prestador)]
    [InlineData(EstadoCita.NoAsistio, FaltaEnCita.Discrepancia)]
    [InlineData(EstadoCita.Confirmada, FaltaEnCita.Nadie)]
    [InlineData(EstadoCita.Realizada, FaltaEnCita.Nadie)]
    public async Task La_base_acepta_las_combinaciones_que_si_pueden_existir(
        EstadoCita estado, FaltaEnCita quienFalto)
    {
        var bloque = await UnBloqueLibreAsync();
        await using var ctx = ContextoDePrueba.Crear();

        var cita = NuevaCitaCruda(bloque, estado);
        cita.QuienFalto = quienFalto;
        ctx.Citas.Add(cita);
        await ctx.SaveChangesAsync();                 // no debe lanzar

        await using var fin = ContextoDePrueba.Crear();
        Assert.Equal(quienFalto, (await fin.Citas.SingleAsync(c => c.Id == cita.Id)).QuienFalto);
    }

    /// <summary>
    /// El caso por el que el CHECK existe, y que no es el INSERT: una cita ya acusada que
    /// alguien mueve a otro estado sin limpiar la columna.
    /// </summary>
    [Fact]
    public async Task La_base_rechaza_mover_una_cita_acusada_sin_limpiar_el_ausente()
    {
        var bloque = await UnBloqueLibreAsync();
        await using var ctx = ContextoDePrueba.Crear();

        var cita = NuevaCitaCruda(bloque, EstadoCita.PendienteAsistencia);
        ctx.Citas.Add(cita);
        await ctx.SaveChangesAsync();

        Assert.True(cita.AcusarInasistencia(FaltaEnCita.Cliente).Exito);
        await ctx.SaveChangesAsync();

        cita.Estado = EstadoCita.Realizada;           // sin tocar QuienFalto
        var choque = await Assert.ThrowsAsync<DbUpdateException>(() => ctx.SaveChangesAsync());

        var pg = Assert.IsType<PostgresException>(choque.InnerException);
        Assert.Equal(PostgresErrorCodes.CheckViolation, pg.SqlState);
        Assert.Equal("ck_cita_quien_falto", pg.ConstraintName);
    }

    /// <summary>
    /// El SQL de <c>HuboCitaRealizadaAsync</c>, que es una COPIA de la condición que tiene el
    /// doble en memoria. Nada en las pruebas unitarias ve que las dos digan lo mismo: si
    /// alguien cambia esta de aquí, allá todo sigue verde. Esto es lo único que lo mira.
    /// </summary>
    [Theory]
    [InlineData(FaltaEnCita.Prestador, true)]     // plantón: el cliente sí reseña
    [InlineData(FaltaEnCita.Cliente, false)]      // no se presentó
    [InlineData(FaltaEnCita.Discrepancia, false)] // en disputa: bloqueada
    [InlineData(FaltaEnCita.Nadie, false)]        // fila anterior a la regla
    public async Task El_sql_de_elegibilidad_solo_cuenta_la_inasistencia_del_prestador(
        FaltaEnCita quienFalto, bool habilita)
    {
        var bloque = await UnBloqueLibreAsync();
        await using var ctx = ContextoDePrueba.Crear();

        var cita = NuevaCitaCruda(bloque, EstadoCita.NoAsistio);
        cita.QuienFalto = quienFalto;
        ctx.Citas.Add(cita);
        await ctx.SaveChangesAsync();

        var repo = new RepositorioAgendaEfCore(ctx);
        Assert.Equal(habilita, await repo.HuboCitaRealizadaAsync(_usuarioBuscador, _perfil));
    }

    /// <summary>
    /// La cola interna de revisión con el SQL real: solo las citas en disputa, que es la
    /// consulta para la que existe el índice parcial `ix_cita_en_disputa`.
    /// </summary>
    [Fact]
    public async Task La_cola_de_revision_trae_solo_las_citas_en_disputa()
    {
        await using var ctx = ContextoDePrueba.Crear();

        var enDisputa = NuevaCitaCruda(await UnBloqueLibreAsync(), EstadoCita.NoAsistio);
        enDisputa.QuienFalto = FaltaEnCita.Discrepancia;
        var soloElCliente = NuevaCitaCruda(
            await UnBloqueLibreAsync(_reloj.Ahora.AddDays(2)), EstadoCita.NoAsistio);
        soloElCliente.QuienFalto = FaltaEnCita.Cliente;
        var realizada = NuevaCitaCruda(
            await UnBloqueLibreAsync(_reloj.Ahora.AddDays(3)), EstadoCita.Realizada);

        ctx.Citas.AddRange(enDisputa, soloElCliente, realizada);
        await ctx.SaveChangesAsync();

        var cola = await new RepositorioAgendaEfCore(ctx).CitasEnDisputaAsync(200);

        Assert.Contains(cola, c => c.Id == enDisputa.Id);
        Assert.DoesNotContain(cola, c => c.Id == soloElCliente.Id);
        Assert.DoesNotContain(cola, c => c.Id == realizada.Id);
        // Y nada que no sea una disputa, venga de donde venga.
        Assert.All(cola, c =>
        {
            Assert.Equal(EstadoCita.NoAsistio, c.Estado);
            Assert.Equal(FaltaEnCita.Discrepancia, c.QuienFalto);
        });
    }

    // ========================================================= CC-04

    /// <summary>
    /// El doble envío de verdad: la misma clave, dos conexiones, a la vez. Las dos tienen que
    /// devolver la MISMA cita, y en la base tiene que quedar una.
    /// </summary>
    [Fact]
    public async Task El_mismo_envio_desde_dos_conexiones_a_la_vez_deja_una_sola_cita()
    {
        var bloque = await UnBloqueLibreAsync();
        var clave = $"doble-envio-{Guid.NewGuid():N}";
        var a = UnaReplica();
        var b = UnaReplica();

        var resultados = await Task.WhenAll(
            a.Agenda.AgendarAsync(new AgendarCmd(_usuarioBuscador, bloque, clave, null)),
            b.Agenda.AgendarAsync(new AgendarCmd(_usuarioBuscador, bloque, clave, null)));

        // Las dos responden bien, y con la misma cita: una la creó y la otra la encontró.
        Assert.All(resultados, r => Assert.True(r.Exito, r.Mensaje));
        Assert.Single(resultados.Select(r => r.Valor!.Id).Distinct());
        Assert.Single(resultados.Where(r => r.Valor!.Nueva));

        using var ctx = ContextoDePrueba.Crear();
        Assert.Equal(1, await ctx.Citas.CountAsync(c => c.ClaveIdempotencia == clave));
        Assert.Equal(1, await ctx.Contactos.CountAsync(c => c.PerfilPrestadorId == _perfil));
    }

    [Fact]
    public async Task La_clave_de_idempotencia_tiene_unico_en_la_base()
    {
        var clave = $"unico-{Guid.NewGuid():N}";
        using var ctx = ContextoDePrueba.Crear();

        var uno = NuevaCitaCruda(await UnBloqueLibreAsync(), EstadoCita.Confirmada);
        uno.ClaveIdempotencia = clave;
        ctx.Citas.Add(uno);
        await ctx.SaveChangesAsync();

        var otro = NuevaCitaCruda(
            await UnBloqueLibreAsync(_reloj.Ahora.AddDays(2)), EstadoCita.Confirmada);
        otro.ClaveIdempotencia = clave;
        ctx.Citas.Add(otro);

        var choque = await Assert.ThrowsAsync<DbUpdateException>(() => ctx.SaveChangesAsync());
        Assert.Equal("23505", (choque.InnerException as PostgresException)?.SqlState);
    }

    // ========================================================= la transacción completa

    /// <summary>
    /// Postcondición de fallo de CU-12: «no queda un bloque reservado sin cita, ni una cita
    /// sin bloque». Se provoca el fallo con el tope de RN-S4-06, que se comprueba DESPUÉS de
    /// haber tomado el bloqueo de fila: si la transacción no deshiciera, el bloque quedaría
    /// marcado reservado sin ninguna cita detrás y la hora moriría.
    /// </summary>
    [Fact]
    public async Task Un_fallo_despues_de_tomar_el_bloque_lo_deja_libre()
    {
        var replica = UnaReplica();
        for (var i = 0; i < ServicioAgenda.MaximoCitasActivasPorPrestador; i++)
        {
            var libre = await UnBloqueLibreAsync(_reloj.Ahora.AddDays(2 + i));
            var ok = await replica.Agenda.AgendarAsync(new AgendarCmd(
                _usuarioBuscador, libre, $"previa-{Guid.NewGuid():N}", null));
            Assert.True(ok.Exito, ok.Mensaje);
        }

        var elCuarto = await UnBloqueLibreAsync(_reloj.Ahora.AddDays(20));
        var r = await replica.Agenda.AgendarAsync(new AgendarCmd(
            _usuarioBuscador, elCuarto, $"tope-{Guid.NewGuid():N}", null));

        Assert.Equal(Fallas.Conflicto, r.Codigo);

        using var ctx = ContextoDePrueba.Crear();
        Assert.Equal(EstadoBloque.Libre,
            (await ctx.Bloques.SingleAsync(b => b.Id == elCuarto)).Estado);
        Assert.False(await ctx.Citas.AnyAsync(c => c.BloqueHorarioId == elCuarto));
    }

    // ========================================================= CC-06

    /// <summary>
    /// El abrazo mortal al reprogramar. Dos citas que intentan intercambiarse la hora a la
    /// vez: la A quiere pasar de X a Y y la B de Y a X.
    ///
    /// <para>
    /// Sin un orden de bloqueo, A toma X y pide Y mientras B toma Y y pide X: PostgreSQL
    /// detecta el ciclo y **mata una transacción** con <c>40P01</c>. Funciona —la base no se
    /// corrompe— pero quien reprogramó recibe un error 500 que no debería existir. Con los
    /// bloques pedidos ORDENADOS POR ID, el ciclo no se puede formar: las dos transacciones
    /// piden la misma fila primero, una espera a la otra y las dos terminan.
    /// </para>
    ///
    /// Lo que se afirma es eso: **ninguna muere**. Que las dos acaben en 409 es lo correcto
    /// de paso (no se pueden intercambiar dos horas ya reservadas), pero lo que esta prueba
    /// vigila es que el fallo sea el del negocio y no el del motor.
    /// </summary>
    [Fact]
    public async Task Dos_reprogramaciones_cruzadas_no_se_matan_entre_ellas()
    {
        var x = await UnBloqueLibreAsync(_reloj.Ahora.AddDays(3));
        var y = await UnBloqueLibreAsync(_reloj.Ahora.AddDays(4));

        var preparar = UnaReplica();
        var citaX = await preparar.Agenda.AgendarAsync(new AgendarCmd(
            _usuarioBuscador, x, $"cc06-x-{Guid.NewGuid():N}", null));
        var citaY = await preparar.Agenda.AgendarAsync(new AgendarCmd(
            _usuarioBuscador, y, $"cc06-y-{Guid.NewGuid():N}", null));
        Assert.True(citaX.Exito, citaX.Mensaje);
        Assert.True(citaY.Exito, citaY.Mensaje);

        var a = UnaReplica();
        var b = UnaReplica();
        var autor = new AutorDeCambio(_usuarioBuscador, null);

        // Las dos empiezan juntas, y cada una pide el bloque que la otra tiene.
        using var partida = new Barrier(2);
        var tareas = new[]
        {
            Task.Run(() =>
            {
                partida.SignalAndWait();
                return a.Agenda.ReprogramarAsync(new ReprogramarCitaCmd(
                    citaX.Valor!.Id, y, autor, $"cc06-ra-{Guid.NewGuid():N}", null));
            }),
            Task.Run(() =>
            {
                partida.SignalAndWait();
                return b.Agenda.ReprogramarAsync(new ReprogramarCitaCmd(
                    citaY.Valor!.Id, x, autor, $"cc06-rb-{Guid.NewGuid():N}", null));
            }),
        };

        // Si una muriera por el ciclo, esto lanzaría en vez de devolver un Resultado.
        var resultados = await Task.WhenAll(tareas);

        Assert.All(resultados, r =>
            Assert.True(r.Codigo is Fallas.Conflicto or Fallas.Validacion or Fallas.NoEncontrado
                        || r.Exito,
                $"terminó con {r.Codigo}: {r.Mensaje}"));

        // Y la base queda consistente: cada bloque con su cita activa, ninguno suelto.
        using var ctx = ContextoDePrueba.Crear();
        foreach (var bloque in new[] { x, y })
        {
            var estado = (await ctx.Bloques.SingleAsync(bl => bl.Id == bloque)).Estado;
            var activas = await ctx.Citas.CountAsync(
                c => c.BloqueHorarioId == bloque
                     && (c.Estado == EstadoCita.Solicitada || c.Estado == EstadoCita.Confirmada));
            Assert.Equal(estado == EstadoBloque.Reservado ? 1 : 0, activas);
        }
    }

    /// <summary>
    /// Reprogramar de verdad, contra la base: la vieja queda cerrada, la nueva nace ligada a
    /// la primera de la cadena y los dos bloques cambian de estado en la misma transacción.
    /// </summary>
    [Fact]
    public async Task Reprogramar_mueve_las_dos_horas_y_conserva_el_vinculo()
    {
        var viejo = await UnBloqueLibreAsync(_reloj.Ahora.AddDays(3));
        var nuevo = await UnBloqueLibreAsync(_reloj.Ahora.AddDays(5));
        var replica = UnaReplica();

        var original = await replica.Agenda.AgendarAsync(new AgendarCmd(
            _usuarioBuscador, viejo, $"rp-{Guid.NewGuid():N}", null));
        Assert.True(original.Exito, original.Mensaje);

        var r = await replica.Agenda.ReprogramarAsync(new ReprogramarCitaCmd(
            original.Valor!.Id, nuevo, new AutorDeCambio(_usuarioBuscador, null),
            $"rp2-{Guid.NewGuid():N}", "Me surgió un viaje"));

        Assert.True(r.Exito, r.Mensaje);

        using var ctx = ContextoDePrueba.Crear();
        Assert.Equal(EstadoBloque.Libre, (await ctx.Bloques.SingleAsync(b => b.Id == viejo)).Estado);
        Assert.Equal(EstadoBloque.Reservado, (await ctx.Bloques.SingleAsync(b => b.Id == nuevo)).Estado);

        var vieja = await ctx.Citas.SingleAsync(c => c.Id == original.Valor.Id);
        var nacida = await ctx.Citas.SingleAsync(c => c.Id == r.Valor!.Id);
        Assert.Equal(EstadoCita.Reprogramada, vieja.Estado);
        Assert.Equal(EstadoCita.Confirmada, nacida.Estado);
        Assert.Equal(vieja.Id, nacida.CitaOrigenId);

        // R-1 no se queja: la vieja ya no es activa, así que el bloque viejo queda libre de
        // verdad y la nueva es la única activa sobre el bloque nuevo.
        Assert.Equal(1, await ctx.Citas.CountAsync(
            c => c.BloqueHorarioId == nuevo && c.Estado == EstadoCita.Confirmada));
    }

    // ========================================================= A-09 contra la base

    /// <summary>
    /// Las tres transiciones del tiempo con el SQL real: las consultas con unión al bloque, la
    /// comparación de fechas en timestamptz y la transacción por cita. Una lista en memoria
    /// no contesta nada de eso.
    ///
    /// Se mueve el reloj de la API y no los datos: el bloque queda a 2 días y se avanza el
    /// reloj, que es lo que de verdad pasa en producción.
    /// </summary>
    [Fact]
    public async Task El_tiempo_lleva_una_cita_de_confirmada_a_realizada_con_el_sql_real()
    {
        var bloque = await UnBloqueLibreAsync(_reloj.Ahora.AddHours(3));
        var (agenda, _) = UnaReplica();
        var cita = await agenda.AgendarAsync(new AgendarCmd(
            _usuarioBuscador, bloque, $"t-{Guid.NewGuid():N}", null));
        Assert.True(cita.Exito, cita.Mensaje);

        var relojAdelantado = new RelojAdelantado(_reloj, TimeSpan.FromHours(4));
        var futura = AgendaConReloj(relojAdelantado);

        var t8 = await futura.AvanzarPorElTiempoAsync();
        Assert.Equal(1, t8.PorAsistir);
        await using (var ctx = ContextoDePrueba.Crear())
            Assert.Equal(EstadoCita.PendienteAsistencia,
                (await ctx.Citas.SingleAsync(c => c.Id == cita.Valor!.Id)).Estado);

        // Ahora el prestador sí puede marcar asistencia.
        var marca = await futura.RegistrarAsistenciaAsync(
            cita.Valor!.Id, new AutorDeCambio(_usuarioPrestador, _perfil), asistio: true);
        Assert.True(marca.Exito, marca.Mensaje);
        Assert.True(await futura.CitaRealizadaAsync(_usuarioBuscador, _perfil));

        await using var fin = ContextoDePrueba.Crear();
        Assert.Equal(EstadoBloque.Vencido, (await fin.Bloques.SingleAsync(b => b.Id == bloque)).Estado);
        Assert.Equal(2, await fin.CambiosCita.CountAsync(
            c => c.CitaId == cita.Valor.Id && c.Tipo == TipoCambio.Asistencia));
    }

    [Fact]
    public async Task Una_solicitada_sin_respuesta_expira_y_su_hora_vuelve_a_ofrecerse_en_la_base()
    {
        // Servicio manual: se cambia el modo del ofrecido de esta prueba.
        await using (var ctx = ContextoDePrueba.Crear())
        {
            (await ctx.PrestadorServicios.SingleAsync(ps => ps.Id == _servicioAutomatico))
                .ModoAgenda = ModoAgenda.Manual;
            await ctx.SaveChangesAsync();
        }
        var bloque = await UnBloqueLibreAsync(_reloj.Ahora.AddDays(5));
        var (agenda, _) = UnaReplica();
        var cita = await agenda.AgendarAsync(new AgendarCmd(
            _usuarioBuscador, bloque, $"t5-{Guid.NewGuid():N}", null));
        Assert.Equal("Solicitada", cita.Valor!.Estado);

        var futura = AgendaConReloj(new RelojAdelantado(_reloj, TimeSpan.FromHours(25)));
        var r = await futura.AvanzarPorElTiempoAsync();

        Assert.Equal(1, r.Expiradas);
        await using var fin = ContextoDePrueba.Crear();
        Assert.Equal(EstadoBloque.Libre, (await fin.Bloques.SingleAsync(b => b.Id == bloque)).Estado);

        // R-1 ya no estorba: la expirada deja de ser activa y la hora se puede volver a dar.
        Assert.False(await fin.Citas.AnyAsync(
            c => c.BloqueHorarioId == bloque
                 && (c.Estado == EstadoCita.Solicitada || c.Estado == EstadoCita.Confirmada)));
    }

    // ========================================================= agenda -> resenas (MOD-06 + MOD-11)

    private Zass.Aplicacion.Resenas.ServicioResenas ResenasConReloj(Zass.Aplicacion.Puertos.IRelojSistema reloj)
    {
        var ctx = ContextoDePrueba.Crear();
        _contextos.Add(ctx);
        return new Zass.Aplicacion.Resenas.ServicioResenas(
            new RepositorioResenasEfCore(ctx),
            new ServicioIdentidad(new RepositorioUsuariosEfCore(ctx), new RepositorioPerfilesEfCore(ctx)),
            new UnidadDeTrabajoEfCore(ctx), reloj, new AuditorEfCore(ctx), new ColaNotificacionesEfCore(ctx));
    }

    /// <summary>
    /// El circuito que ninguna rama podia probar sola: agendar, que pase la hora, que el
    /// prestador marque asistencia y que MOD-11 diga que se puede resenar.
    /// </summary>
    [Fact]
    public async Task Una_cita_realizada_habilita_la_resena_por_el_camino_completo()
    {
        var bloque = await UnBloqueLibreAsync(_reloj.Ahora.AddHours(3));
        var (agenda, _) = UnaReplica();
        var cita = await agenda.AgendarAsync(new AgendarCmd(_usuarioBuscador, bloque, $"e-{Guid.NewGuid():N}", null));
        Assert.True(cita.Exito, cita.Mensaje);

        var futuro = new RelojAdelantado(_reloj, TimeSpan.FromHours(4));
        var agendaFutura = AgendaConReloj(futuro);
        await agendaFutura.AvanzarPorElTiempoAsync();
        await agendaFutura.RegistrarAsistenciaAsync(cita.Valor!.Id, new AutorDeCambio(_usuarioPrestador, _perfil), true);

        var r = await ResenasConReloj(futuro).EsElegibleAsync(_usuarioBuscador, _perfil);

        Assert.True(r.Exito, r.Mensaje);
        Assert.True(r.Valor!.Elegible, r.Valor.Mensaje);
    }

    /// <summary>
    /// RN-S4-05, y es el defecto que solo aparecio al JUNTAR las dos ramas: el contacto de una
    /// cita apunta a la PRIMERA de la cadena, y esa queda `Reprogramada` en cuanto se mueve la
    /// hora. Si resenas mira solo ese estado, quien reprograma y luego es atendido pierde el
    /// derecho a opinar, que es justo lo que la regla existe para impedir.
    /// </summary>
    [Fact]
    public async Task Reprogramar_no_hace_perder_la_elegibilidad_de_resena()
    {
        var primero = await UnBloqueLibreAsync(_reloj.Ahora.AddDays(2));
        var segundo = await UnBloqueLibreAsync(_reloj.Ahora.AddDays(3));
        var (agenda, _) = UnaReplica();

        var original = await agenda.AgendarAsync(new AgendarCmd(_usuarioBuscador, primero, $"p-{Guid.NewGuid():N}", null));
        var nueva = await agenda.ReprogramarAsync(new ReprogramarCitaCmd(
            original.Valor!.Id, segundo, new AutorDeCambio(_usuarioBuscador, null), $"rp-{Guid.NewGuid():N}", null));
        Assert.True(nueva.Exito, nueva.Mensaje);

        var futuro = new RelojAdelantado(_reloj, TimeSpan.FromDays(3) + TimeSpan.FromHours(2));
        var agendaFutura = AgendaConReloj(futuro);
        await agendaFutura.AvanzarPorElTiempoAsync();
        var marca = await agendaFutura.RegistrarAsistenciaAsync(
            nueva.Valor!.Id, new AutorDeCambio(_usuarioPrestador, _perfil), true);
        Assert.True(marca.Exito, marca.Mensaje);

        var r = await ResenasConReloj(futuro).EsElegibleAsync(_usuarioBuscador, _perfil);

        Assert.True(r.Exito, r.Mensaje);
        Assert.True(r.Valor!.Elegible, $"{r.Valor.Motivo}: {r.Valor.Mensaje}");
    }

    private ServicioAgenda AgendaConReloj(Zass.Aplicacion.Puertos.IRelojSistema reloj)
    {
        var ctx = ContextoDePrueba.Crear();
        _contextos.Add(ctx);
        var trabajo = new UnidadDeTrabajoEfCore(ctx);
        var auditor = new AuditorEfCore(ctx);
        return new ServicioAgenda(
            new RepositorioAgendaEfCore(ctx), trabajo, reloj, auditor,
            new ContactosEfCore(ctx),
            new ServicioCatalogo(new RepositorioCatalogoEfCore(ctx), trabajo, reloj, auditor),
            new ServicioIdentidad(new RepositorioUsuariosEfCore(ctx), new RepositorioPerfilesEfCore(ctx)),
            new ServicioSuscripciones(
                new RepositorioSuscripcionesEfCore(ctx), trabajo, reloj, auditor,
                new ColaNotificacionesEfCore(ctx)));
    }

    /// <summary>El reloj real corrido hacia adelante. Así «pasaron 4 horas» sin esperarlas.</summary>
    private sealed class RelojAdelantado : Zass.Aplicacion.Puertos.IRelojSistema
    {
        private readonly Zass.Aplicacion.Puertos.IRelojSistema _base;
        private readonly TimeSpan _salto;
        public RelojAdelantado(Zass.Aplicacion.Puertos.IRelojSistema baseReloj, TimeSpan salto)
        {
            _base = baseReloj;
            _salto = salto;
        }
        public DateTime Ahora => _base.Ahora + _salto;
    }

    /// <summary>
    /// El camino completo de CU-11: definir el horario genera bloques de verdad, y el
    /// calendario público los muestra. Es lo que convierte la regla en horas reservables.
    /// </summary>
    [Fact]
    public async Task Definir_el_horario_genera_bloques_y_el_calendario_los_muestra()
    {
        var replica = UnaReplica();

        // El dia de la semana se toma del calendario CHILENO: con el UTC, una franja nocturna
        // caeria en el dia siguiente y esta prueba no encontraria sus propios bloques.
        var manana = HoraDeChile.DiaLocal(_reloj.Ahora).AddDays(1);
        var dia = (short)(((int)manana.DayOfWeek + 6) % 7 + 1);

        var r = await replica.Agenda.DefinirDisponibilidadAsync(new DefinirDisponibilidadCmd(
            _perfil, _servicioAutomatico,
            new[] { new FranjaCmd(dia, new TimeOnly(9, 0), new TimeOnly(11, 0), 30) }));

        Assert.True(r.Exito, r.Mensaje);
        Assert.True(r.Valor >= 4 * (ServicioAgenda.SemanasDeVentana - 1),
            $"se generaron {r.Valor} bloques, muy pocos para 8 semanas");

        var calendario = await replica.Agenda.BloquesLibresAsync(
            _servicioAutomatico, _reloj.Ahora, _reloj.Ahora.AddDays(14));

        Assert.True(calendario.Exito, calendario.Mensaje);
        Assert.NotEmpty(calendario.Valor!.Bloques);
        Assert.All(calendario.Valor.Bloques, b => Assert.Equal("libre", b.Estado));

        // Y a la hora que el prestador publico, no tres horas corrido.
        Assert.All(calendario.Valor.Bloques, b => Assert.InRange(
            TimeOnly.FromDateTime(HoraDeChile.ALocal(b.InicioEn)),
            new TimeOnly(9, 0), new TimeOnly(10, 30)));
    }
}
