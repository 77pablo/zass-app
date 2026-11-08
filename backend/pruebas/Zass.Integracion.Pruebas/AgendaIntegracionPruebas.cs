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
            Nombre = "Ana Pérez", CorreoVerificado = true,
        };
        ctx.Usuarios.AddRange(prestador, buscador);

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

    /// <summary>
    /// El camino completo de CU-11: definir el horario genera bloques de verdad, y el
    /// calendario público los muestra. Es lo que convierte la regla en horas reservables.
    /// </summary>
    [Fact]
    public async Task Definir_el_horario_genera_bloques_y_el_calendario_los_muestra()
    {
        var replica = UnaReplica();
        var manana = _reloj.Ahora.AddDays(1);
        var dia = (short)(((int)manana.DayOfWeek + 6) % 7 + 1);

        var r = await replica.Agenda.DefinirDisponibilidadAsync(new DefinirDisponibilidadCmd(
            _perfil, _servicioAutomatico,
            new[] { new FranjaCmd(dia, new TimeOnly(9, 0), new TimeOnly(11, 0), 30) }));

        Assert.True(r.Exito, r.Mensaje);
        Assert.Equal(4 * ServicioAgenda.SemanasDeVentana, r.Valor);

        var calendario = await replica.Agenda.BloquesLibresAsync(
            _servicioAutomatico, _reloj.Ahora, _reloj.Ahora.AddDays(14));

        Assert.True(calendario.Exito, calendario.Mensaje);
        Assert.NotEmpty(calendario.Valor!.Bloques);
        Assert.All(calendario.Valor.Bloques, b => Assert.Equal("libre", b.Estado));
    }
}
