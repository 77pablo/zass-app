using Microsoft.EntityFrameworkCore;
using Npgsql;
using Zass.Aplicacion.Puertos;
using Zass.Aplicacion.Suscripciones;
using Zass.Dominio.Comun;
using Zass.Dominio.Identidad;
using Zass.Dominio.Suscripciones;
using Zass.Infraestructura.Datos;
using Zass.Infraestructura.Datos.Repositorios;
using Zass.Infraestructura.Fondo;
using Zass.Infraestructura.Seguridad;

namespace Zass.Integracion.Pruebas;

/// <summary>
/// A-09 · CC-08 contra PostgreSQL de verdad.
///
/// Esto NO se puede probar con listas en memoria: lo que se comprueba es justamente que la
/// base serialice a dos réplicas que entran en el mismo instante. Por eso cada «réplica»
/// tiene su propio <see cref="ZassDbContext"/> y su propia conexión, y por eso estas pruebas
/// no van dentro de una transacción que se deshace: una transacción abierta esconde el
/// efecto que se quiere medir, porque la segunda réplica se quedaría esperando el commit.
///
/// Cada prueba usa un nombre de trabajo propio y borra su fila al terminar.
/// </summary>
public class FondoIntegracionPruebas : IAsyncLifetime
{
    private static readonly DateTime Ahora = new(2026, 11, 8, 9, 0, 0, DateTimeKind.Utc);
    private static readonly TimeSpan Reserva = TimeSpan.FromMinutes(10);

    private readonly string _trabajo = $"prueba-{Guid.NewGuid():N}";   // 39 caracteres, cabe en los 60
    private readonly List<ZassDbContext> _contextos = new();

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        using var ctx = ContextoDePrueba.Crear();
        await ctx.Database.ExecuteSqlRawAsync(
            "DELETE FROM trabajos_de_fondo WHERE nombre = @p0", _trabajo);
        foreach (var c in _contextos) c.Dispose();
    }

    /// <summary>Una «réplica»: contexto propio, conexión propia, como en producción.</summary>
    private CoordinadorDeTrabajos UnaReplica()
    {
        var ctx = ContextoDePrueba.Crear();
        _contextos.Add(ctx);
        return new CoordinadorDeTrabajos(ctx);
    }

    // ------------------------------------------------------------- CC-08

    [Fact]
    public async Task La_primera_vez_se_crea_la_fila_y_el_trabajo_corre_al_instante()
    {
        Assert.True(await UnaReplica().TomarAsync(_trabajo, Ahora, Reserva));

        using var ctx = ContextoDePrueba.Crear();
        var fila = await ctx.TrabajosDeFondo.SingleAsync(t => t.Nombre == _trabajo);
        Assert.Equal(0, fila.Corridas);
        Assert.Equal(Ahora + Reserva, fila.ReservadoHasta);
    }

    /// <summary>
    /// El fallo que esto impide: el trabajo diario corriendo una vez por réplica, y por tanto
    /// dos correos a la misma persona. Diez réplicas, un solo ganador.
    /// </summary>
    [Fact]
    public async Task Diez_replicas_a_la_vez_y_solo_una_se_lleva_el_trabajo()
    {
        var replicas = Enumerable.Range(0, 10).Select(_ => UnaReplica()).ToList();

        var resultados = await Task.WhenAll(
            replicas.Select(r => r.TomarAsync(_trabajo, Ahora, Reserva)));

        Assert.Equal(1, resultados.Count(x => x));
    }

    [Fact]
    public async Task Quien_no_lo_consigue_sigue_de_largo_en_vez_de_esperar()
    {
        Assert.True(await UnaReplica().TomarAsync(_trabajo, Ahora, Reserva));

        // Si esto esperara el bloqueo en vez de retirarse, aquí se colgaría hasta el timeout.
        var otra = UnaReplica();
        var reloj = System.Diagnostics.Stopwatch.StartNew();
        Assert.False(await otra.TomarAsync(_trabajo, Ahora, Reserva));
        Assert.True(reloj.Elapsed < TimeSpan.FromSeconds(3), $"tardó {reloj.Elapsed}");
    }

    /// <summary>
    /// La reserva vence a propósito: si la réplica muere a mitad del trabajo, otra lo retoma.
    /// Sin esto, el trabajo quedaría trabado para siempre y nadie recibiría su aviso.
    /// </summary>
    [Fact]
    public async Task Si_la_replica_muere_otra_lo_retoma_cuando_vence_la_reserva()
    {
        Assert.True(await UnaReplica().TomarAsync(_trabajo, Ahora, Reserva));

        var otra = UnaReplica();
        Assert.False(await otra.TomarAsync(_trabajo, Ahora + Reserva - TimeSpan.FromMinutes(1), Reserva));
        Assert.True(await otra.TomarAsync(_trabajo, Ahora + Reserva + TimeSpan.FromMinutes(1), Reserva));
    }

    [Fact]
    public async Task Tras_soltarlo_no_vuelve_a_tocarle_hasta_la_proxima_corrida()
    {
        var r = UnaReplica();
        Assert.True(await r.TomarAsync(_trabajo, Ahora, Reserva));
        await r.LiberarAsync(_trabajo, Ahora, Ahora.AddHours(24), "3 vencidas, 5 avisadas", true);

        Assert.False(await UnaReplica().TomarAsync(_trabajo, Ahora.AddHours(23), Reserva));
        Assert.True(await UnaReplica().TomarAsync(_trabajo, Ahora.AddHours(25), Reserva));

        using var ctx = ContextoDePrueba.Crear();
        var fila = await ctx.TrabajosDeFondo.SingleAsync(t => t.Nombre == _trabajo);
        Assert.Equal(1, fila.Corridas);
        Assert.Equal("3 vencidas, 5 avisadas", fila.UltimoResumen);
        Assert.Equal(Ahora, fila.UltimaCorridaEn);
    }

    /// <summary>
    /// Un fallo se reintenta antes que un éxito, y no cuenta como corrida: si contara, la
    /// cuenta de <c>corridas</c> dejaría de servir para ver que el trabajo de verdad funciona.
    /// </summary>
    [Fact]
    public async Task Un_fallo_se_reintenta_antes_y_no_cuenta_como_corrida()
    {
        var r = UnaReplica();
        Assert.True(await r.TomarAsync(_trabajo, Ahora, Reserva));
        await r.LiberarAsync(_trabajo, Ahora, Ahora.AddMinutes(30), "error (NpgsqlException)", false);

        Assert.True(await UnaReplica().TomarAsync(_trabajo, Ahora.AddMinutes(31), Reserva));

        using var ctx = ContextoDePrueba.Crear();
        var fila = await ctx.TrabajosDeFondo.SingleAsync(t => t.Nombre == _trabajo);
        Assert.Equal(0, fila.Corridas);
        Assert.Equal("error (NpgsqlException)", fila.UltimoResumen);
    }

    /// <summary>El resumen va a una columna de 300: uno más largo se recorta, no revienta.</summary>
    [Fact]
    public async Task Un_resumen_demasiado_largo_se_recorta_en_vez_de_fallar()
    {
        var r = UnaReplica();
        Assert.True(await r.TomarAsync(_trabajo, Ahora, Reserva));
        await r.LiberarAsync(_trabajo, Ahora, Ahora.AddHours(24), new string('x', 500), true);

        using var ctx = ContextoDePrueba.Crear();
        var fila = await ctx.TrabajosDeFondo.SingleAsync(t => t.Nombre == _trabajo);
        Assert.Equal(300, fila.UltimoResumen!.Length);
    }
}

/// <summary>
/// El trabajo diario de suscripciones contra la base: lo que una lista en memoria no puede
/// contestar es si el único de TBL-27 (R-18, CC-04) de verdad impide el segundo aviso.
/// </summary>
public class TrabajoDiarioIntegracionPruebas : IDisposable
{
    private readonly ContextoDePrueba _prueba = new();
    private readonly RelojUtc _reloj = new();
    private readonly ServicioSuscripciones _servicio;

    public TrabajoDiarioIntegracionPruebas()
    {
        var ctx = _prueba.Ctx;
        _servicio = new ServicioSuscripciones(
            new RepositorioSuscripcionesEfCore(ctx), new UnidadDeTrabajoEfCore(ctx),
            _reloj, new AuditorEfCore(ctx), new ColaNotificacionesEfCore(ctx));
    }

    public void Dispose() => _prueba.Dispose();

    private async Task<PerfilPrestador> UnPrestadorAsync()
    {
        var usuario = new Usuario
        {
            Correo = $"fondo-{Guid.NewGuid():N}@zass.local",
            HashContrasena = "x",
            Nombre = "Prestador de prueba",
            CorreoVerificado = true,
        };
        var perfil = new PerfilPrestador { UsuarioId = usuario.Id, Slug = $"fondo-{Guid.NewGuid():N}" };
        _prueba.Ctx.Usuarios.Add(usuario);
        _prueba.Ctx.PerfilesPrestador.Add(perfil);
        await _prueba.Ctx.SaveChangesAsync();
        return perfil;
    }

    private async Task<Suscripcion> ConSuscripcionAsync(Guid perfilId, DateTime hasta)
    {
        var plan = await _prueba.Ctx.Planes.FirstAsync(p => p.Codigo == "prueba");
        var s = new Suscripcion
        {
            PerfilPrestadorId = perfilId,
            PlanId = plan.Id,
            Estado = EstadoSuscripcion.Activa,
            IniciaEn = _reloj.Ahora.AddDays(-20),
            VigenteHasta = hasta,
        };
        _prueba.Ctx.Suscripciones.Add(s);
        await _prueba.Ctx.SaveChangesAsync();
        return s;
    }

    [Fact]
    public async Task El_aviso_de_por_vencer_queda_en_la_cola_listo_para_salir()
    {
        var p = await UnPrestadorAsync();
        var s = await ConSuscripcionAsync(p.Id, _reloj.Ahora.AddDays(3));

        var r = await _servicio.CorrerDiarioAsync();

        Assert.Equal(1, r.Avisadas);
        var aviso = await _prueba.Ctx.Notificaciones
            .SingleAsync(n => n.ClaveIdempotencia == $"SuscripcionPorVencer:{s.Id}:7");
        Assert.Equal(p.UsuarioId, aviso.DestinatarioId);
        Assert.Equal(EstadoEnvio.Programada, aviso.Estado);
        Assert.Equal("3", CargaDeAviso.LeerCampo(aviso.Cuerpo, "dias"));
    }

    /// <summary>
    /// La garantía que no depende del código: si el planificador corre dos veces —CC-08, o
    /// un despliegue a media mañana— el segundo aviso choca con el único y no sale.
    /// </summary>
    [Fact]
    public async Task Correr_dos_veces_no_deja_dos_avisos_porque_la_base_lo_impide()
    {
        var p = await UnPrestadorAsync();
        await ConSuscripcionAsync(p.Id, _reloj.Ahora.AddDays(3));

        await _servicio.CorrerDiarioAsync();
        _prueba.Ctx.ChangeTracker.Clear();

        var choque = await Assert.ThrowsAsync<DbUpdateException>(
            () => _servicio.CorrerDiarioAsync());
        Assert.Equal("23505", (choque.InnerException as PostgresException)?.SqlState);

        _prueba.Ctx.ChangeTracker.Clear();
        Assert.Equal(1, await _prueba.Ctx.Notificaciones
            .CountAsync(n => n.DestinatarioId == p.UsuarioId
                             && n.Tipo == TipoNotificacion.SuscripcionPorVencer));
    }

    [Fact]
    public async Task Vencer_marca_la_suscripcion_y_deja_su_aviso()
    {
        var p = await UnPrestadorAsync();
        var s = await ConSuscripcionAsync(p.Id, _reloj.Ahora.AddDays(-1));

        var r = await _servicio.CorrerDiarioAsync();

        Assert.Equal(1, r.Vencidas);
        _prueba.Ctx.ChangeTracker.Clear();
        Assert.Equal(EstadoSuscripcion.Vencida,
            (await _prueba.Ctx.Suscripciones.FindAsync(s.Id))!.Estado);
        Assert.True(await _prueba.Ctx.Notificaciones
            .AnyAsync(n => n.ClaveIdempotencia == $"SuscripcionVencida:{s.Id}"));
    }
}
