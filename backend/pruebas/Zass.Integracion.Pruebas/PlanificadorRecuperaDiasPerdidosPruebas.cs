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
/// Prueba de integración para verificar si el Planificador recupera los días perdidos
/// cuando la API duerme en el plan gratis de Render.
///
/// El Planificador corre dentro del proceso de la API. En el plan gratis de Render,
/// la API se duerme cuando no hay tráfico, y `despertar-api.yml` solo la despierta
/// entre el 22 y el 24 de noviembre. Fuera de esas fechas, **la API no corre** y por tanto
/// **el Planificador no corre**.
///
/// Esta prueba verifica: si el planificador no corrió durante 3 días, la siguiente
/// corrida cobra lo que estaba en la ventana y no cobra dos veces.
///
/// Si el código no recupera los días perdidos, la prueba queda en
/// `Skip = "defecto: ..."` y va primero en el PR.
/// </summary>
[Collection(ColeccionCobroConcurrencia.Nombre)]
public class PlanificadorRecuperaDiasPerdidosPruebas : IAsyncLifetime
{
    private readonly List<Guid> _usuariosCreados = new();

    // Fechas fijas para toda la prueba (evita flakiness por DateTime.UtcNow)
    private static readonly DateTime BaseUtc = new(2026, 11, 18, 12, 0, 0, DateTimeKind.Utc); // 18-nov-2026 12:00 UTC
    private static readonly DateTime VigenteHasta = BaseUtc.AddDays(7);                        // 25-nov-2026 12:00 UTC (vence en 7 días)
    private static readonly DateTime Dia0 = BaseUtc;                                           // Día 0: primera corrida
    private static readonly DateTime Dia4 = BaseUtc.AddDays(4);                                // Día 4: después de 3 días de sueño

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        if (_usuariosCreados.Count == 0) return;

        using var ctx = ContextoDePrueba.Crear();
        foreach (var id in _usuariosCreados)
        {
            await ctx.Database.ExecuteSqlRawAsync(
                """
                DELETE FROM intentos_de_pago WHERE perfil_prestador_id IN
                    (SELECT id FROM perfiles_prestador WHERE usuario_id = {0});
                DELETE FROM suscripciones WHERE perfil_prestador_id IN
                    (SELECT id FROM perfiles_prestador WHERE usuario_id = {0});
                DELETE FROM perfiles_prestador WHERE usuario_id = {0};
                DELETE FROM usuarios WHERE id = {0};
                """,
                id);
        }
    }

    /// <summary>
    /// Crea una suscripción a punto de vencer (dentro de la ventana de 7 días)
    /// con medio de pago inscrito.
    /// </summary>
    private async Task<Suscripcion> UnaSuscripcionPorVencerAsync(DateTime? vigenteHasta = null)
    {
        using var ctx = ContextoDePrueba.Crear();

        var usuario = new Usuario
        {
            Correo = $"planif-{Guid.NewGuid():N}@zass.local",
            HashContrasena = "x",
            Nombre = "Fundador de prueba",
            CorreoVerificado = true,
        };
        var perfil = new PerfilPrestador
        {
            UsuarioId = usuario.Id,
            NombrePublico = $"Estudio {Guid.NewGuid():N}",
            Slug = $"planif-{Guid.NewGuid():N}",
        };
        var suscripcion = new Suscripcion
        {
            PerfilPrestadorId = perfil.Id,
            PlanId = 2,                                   // mensual (no requiere cupo fundador)
            Estado = EstadoSuscripcion.Activa,            // Activa, no Prueba (la prueba es solo para el plan gratis)
            IniciaEn = BaseUtc.AddDays(-23),              // 26-oct-2026
            VigenteHasta = vigenteHasta ?? VigenteHasta,  // permite pasar VigenteHasta personalizado
            ReferenciaPasarela = "medio-inscrito-de-prueba",
        };

        ctx.Usuarios.Add(usuario);
        ctx.PerfilesPrestador.Add(perfil);
        ctx.Suscripciones.Add(suscripcion);
        await ctx.SaveChangesAsync();

        _usuariosCreados.Add(usuario.Id);
        return suscripcion;
    }

    /// <summary>
    /// Simula el trabajo diario corriendo con un reloj fijo.
    /// Devuelve el resumen del cobro automático.
    /// </summary>
    private async Task<ResumenDeCobros> CorrerTrabajoDiarioAsync(DateTime ahora)
    {
        var reloj = new RelojFijo(ahora);
        using var ctx = ContextoDePrueba.Crear();
        var servicio = new ServicioCobros(
            new RepositorioSuscripcionesEfCore(ctx),
            new ServicioSuscripciones(
                new RepositorioSuscripcionesEfCore(ctx),
                new UnidadDeTrabajoEfCore(ctx),
                reloj,
                new AuditorEfCore(ctx),
                new ColaNotificacionesEfCore(ctx)),
            new PasarelaFalsa(),
            new UnidadDeTrabajoEfCore(ctx),
            reloj,
            new AuditorEfCore(ctx));

        return await servicio.CobrarVencimientosAsync();
    }

    /// <summary>
    /// PRUEBA PRINCIPAL: Si el planificador no corrió durante 3 días, la siguiente
    /// corrida cobra lo que estaba en la ventana y no cobra dos veces.
    ///
    /// Escenario:
    /// - Día 0 (BaseUtc): Suscripción vence en 7 días (está en la ventana de cobro)
    /// - Día 1, 2, 3: API duerme (el planificador NO corre)
    /// - Día 4 (BaseUtc + 4 días): API despierta, el planificador corre
    ///
    /// Esperado: Se crea UN solo intento de cobro para ese período.
    /// Si se crean dos o cero, el planificador no recupera los días perdidos.
    /// </summary>
    [Fact]
    public async Task Si_el_planificador_no_corrio_3_dias_la_siguiente_corrida_cobra_lo_que_estaba_en_la_ventana_y_no_cobra_dos_veces()
    {
        var suscripcion = await UnaSuscripcionPorVencerAsync();
        var claveEsperada = ServicioCobros.ClaveDeCobroAutomatico(suscripcion.Id, suscripcion.VigenteHasta);

        // PRIMERA CORRIDA (Día 0 - antes de que la API duerma)
        var resumen1 = await CorrerTrabajoDiarioAsync(Dia0);

        // Verificar que se creó el intento en la primera corrida
        using (var ctx = ContextoDePrueba.Crear())
        {
            var intento1 = await ctx.IntentosDePago
                .FirstOrDefaultAsync(i => i.ClaveIdempotencia == claveEsperada);
            Assert.NotNull(intento1); // Debería haberse creado en la primera corrida
        }

        // SEGUNDA CORRIDA (Día 4 - después de 3 días de sueño)
        var resumen2 = await CorrerTrabajoDiarioAsync(Dia4);

        // Verificar que NO se creó un segundo intento (idempotencia por clave)
        using (var ctx = ContextoDePrueba.Crear())
        {
            var intentos = await ctx.IntentosDePago
                .Where(i => i.ClaveIdempotencia == claveEsperada)
                .CountAsync();
            Assert.Equal(1, intentos); // Solo UN intento, no dos
        }

        // El resumen de la segunda corrida debería mostrar 0 cobradas, 1 ya intentada
        Assert.Equal(0, resumen2.Cobradas);
        Assert.Equal(1, resumen2.YaIntentadas);
        // Rechazadas puede ser 0 o 1 según si el intento pendiente se rechaza en la segunda corrida
        // Lo importante es YaIntentadas = 1 (idempotencia por clave)
    }

    /// <summary>
    /// Verifica que el CoordinadorDeTrabajos usa `proxima_corrida_en` para decidir
    /// si el trabajo toca, y no "hoy" en UTC. Si la API duerme, `proxima_corrida_en`
    /// queda en el pasado y la siguiente corrida debería ejecutar el trabajo.
    /// </summary>
    [Fact]
    public async Task Coordinador_usa_proxima_corrida_en_y_no_hoy_UTC()
    {
        var nombreTrabajo = $"prueba-coordinador-{Guid.NewGuid():N}";
        var ahora = BaseUtc;
        var reserva = TimeSpan.FromMinutes(10);

        using var ctx = ContextoDePrueba.Crear();
        var coordinador = new CoordinadorDeTrabajos(ctx);

        // Primera toma: crea la fila con proxima_corrida_en = ahora
        Assert.True(await coordinador.TomarAsync(nombreTrabajo, ahora, reserva));

        // Liberar: fija proxima_corrida_en = ahora + 24 horas
        await coordinador.LiberarAsync(nombreTrabajo, ahora, ahora.AddHours(24), "ok", true);

        // Antes de la próxima corrida: NO debe tomar
        Assert.False(await coordinador.TomarAsync(nombreTrabajo, ahora.AddHours(23), reserva));

        // DESPUÉS de la próxima corrida (simula que la API despertó tarde):
        // proxima_corrida_en está en el pasado, así que DEBE tomar
        Assert.True(await coordinador.TomarAsync(nombreTrabajo, ahora.AddHours(25), reserva));

        // Liberar la segunda corrida para que cuente
        await coordinador.LiberarAsync(nombreTrabajo, ahora.AddHours(25), ahora.AddHours(49), "ok", true);

        // Verificar que la fila tiene corridas = 2
        using var ctx2 = ContextoDePrueba.Crear();
        var fila = await ctx2.TrabajosDeFondo.SingleAsync(t => t.Nombre == nombreTrabajo);
        Assert.Equal(2, fila.Corridas);
    }

    /// <summary>
    /// Verifica el comportamiento cuando el trabajo falla: se reintenta antes
    /// (EsperaTrasFalloMinutos = 30 por defecto) y no cuenta como corrida exitosa.
    /// </summary>
    [Fact]
    public async Task Un_fallo_se_reintenta_antes_y_no_cuenta_como_corrida()
    {
        var nombreTrabajo = $"prueba-fallo-{Guid.NewGuid():N}";
        var ahora = BaseUtc;
        var reserva = TimeSpan.FromMinutes(10);

        using var ctx = ContextoDePrueba.Crear();
        var coordinador = new CoordinadorDeTrabajos(ctx);

        Assert.True(await coordinador.TomarAsync(nombreTrabajo, ahora, reserva));
        await coordinador.LiberarAsync(nombreTrabajo, ahora, ahora.AddMinutes(30), "error (NpgsqlException)", false);

        // Debe poder tomarse de nuevo después de 31 minutos (EsperaTrasFalloMinutos)
        Assert.True(await coordinador.TomarAsync(nombreTrabajo, ahora.AddMinutes(31), reserva));

        using var ctx2 = ContextoDePrueba.Crear();
        var fila = await ctx2.TrabajosDeFondo.SingleAsync(t => t.Nombre == nombreTrabajo);
        Assert.Equal(0, fila.Corridas); // El fallo no cuenta
        Assert.Equal("error (NpgsqlException)", fila.UltimoResumen);
    }

    /// <summary>
    /// PRUEBA A: Si la ventana de 7 días se abrió mientras la API dormía,
    /// la primera corrida al despertar debe cobrar una vez.
    ///
    /// Escenario:
    /// - VigenteHasta = BaseUtc + 9 días (la ventana de 7 días se abre en BaseUtc + 2)
    /// - Día 0 (BaseUtc): Primera corrida — aún FUERA de la ventana (0 intentos)
    /// - Días 1, 2, 3: API duerme (sin corridas)
    /// - Día 4 (BaseUtc + 4): Segunda corrida — DENTRO de la ventana, debe cobrar 1 vez
    /// - Día 5 (BaseUtc + 5): Tercera corrida — misma suscripción, debe ser idempotente
    ///
    /// Esperado:
    /// - Día 0: 0 intentos con la clave
    /// - Día 4: exactamente 1 intento; resumen.YaIntentadas == 0 y (Cobradas + Rechazadas) == 1
    /// - Día 5: sigue 1 intento; resumen.YaIntentadas == 1
    /// </summary>
    [Fact]
    public async Task Si_la_ventana_se_abrio_mientras_la_API_dormia_la_primera_corrida_cobra_una_vez()
    {
        // VigenteHasta = BaseUtc + 9 días
        // La ventana de 7 días se abre en BaseUtc + 2 (9 - 7 = 2)
        var vigenteHasta = BaseUtc.AddDays(9);
        var suscripcion = await UnaSuscripcionPorVencerAsync(vigenteHasta);
        var claveEsperada = ServicioCobros.ClaveDeCobroAutomatico(suscripcion.Id, suscripcion.VigenteHasta);

        // DÍA 0: Primera corrida (fuera de la ventana de 7 días)
        var resumen0 = await CorrerTrabajoDiarioAsync(BaseUtc);

        // Verificar: 0 intentos en BD
        using (var ctx = ContextoDePrueba.Crear())
        {
            var intentosDia0 = await ctx.IntentosDePago
                .Where(i => i.ClaveIdempotencia == claveEsperada)
                .CountAsync();
            Assert.Equal(0, intentosDia0); // Aún fuera de la ventana
        }
        Assert.Equal(0, resumen0.Cobradas + resumen0.Rechazadas);
        Assert.Equal(0, resumen0.YaIntentadas);

        // DÍA 4: Segunda corrida (después de 3 días de sueño, DENTRO de la ventana)
        var resumen4 = await CorrerTrabajoDiarioAsync(BaseUtc.AddDays(4));

        // Verificar: EXACTAMENTE 1 intento en BD
        using (var ctx = ContextoDePrueba.Crear())
        {
            var intentosDia4 = await ctx.IntentosDePago
                .Where(i => i.ClaveIdempotencia == claveEsperada)
                .CountAsync();
            Assert.Equal(1, intentosDia4);
        }

        // Resumen: YaIntentadas == 0 (era la primera vez), Cobradas + Rechazadas == 1
        Assert.Equal(0, resumen4.YaIntentadas);
        Assert.Equal(1, resumen4.Cobradas + resumen4.Rechazadas);

        // DÍA 5: Tercera corrida (mismo día, debe ser idempotente)
        var resumen5 = await CorrerTrabajoDiarioAsync(BaseUtc.AddDays(5));

        // Verificar: sigue habiendo 1 intento
        using (var ctx = ContextoDePrueba.Crear())
        {
            var intentosDia5 = await ctx.IntentosDePago
                .Where(i => i.ClaveIdempotencia == claveEsperada)
                .CountAsync();
            Assert.Equal(1, intentosDia5);
        }

        // Resumen: YaIntentadas == 1 (ya se intentó en la corrida anterior)
        Assert.Equal(1, resumen5.YaIntentadas);
    }

    /// <summary>
    /// PRUEBA B: Si la API durmió más allá del vencimiento (VigenteHasta),
    /// la suscripción igual se debe cobrar.
    ///
    /// Escenario:
    /// - VigenteHasta = BaseUtc + 3 días
    /// - NO hay corridas antes
    /// - Primera corrida en BaseUtc + 4 días (1 día DESPUÉS del vencimiento)
    ///
    /// Esperado con el código ACTUAL: 0 intentos (PorVencerAsync solo mira ahora..ahora+7 días)
    /// Defecto conocido: si la API duerme más allá de VigenteHasta, la suscripción vence
    /// sin intentar cobro (ServicioSuscripciones.cs PorVencerAsync).
    ///
    /// Esta prueba se marca con Skip hasta que Pablo decida si se cobra tarde o no.
    /// </summary>
    [Fact(Skip = "defecto: si la API duerme mas alla de VigenteHasta, la suscripcion vence sin intentar cobro (ServicioSuscripciones.cs PorVencerAsync). Decision de Pablo.")]
    public async Task Si_la_API_durmio_mas_alla_del_vencimiento_la_suscripcion_igual_se_cobra()
    {
        // VigenteHasta = BaseUtc + 3 días (ventana de 7 días = BaseUtc - 4 hasta BaseUtc + 3)
        // Primera corrida en BaseUtc + 4 (1 día DESPUÉS del vencimiento)
        var vigenteHasta = BaseUtc.AddDays(3);
        var suscripcion = await UnaSuscripcionPorVencerAsync(vigenteHasta);
        var claveEsperada = ServicioCobros.ClaveDeCobroAutomatico(suscripcion.Id, suscripcion.VigenteHasta);

        // Primera (y única) corrida: BaseUtc + 4 (ya pasó VigenteHasta)
        var resumen = await CorrerTrabajoDiarioAsync(BaseUtc.AddDays(4));

        // Verificar: debería haber 1 intento (pero con código actual da 0)
        using (var ctx = ContextoDePrueba.Crear())
        {
            var intentos = await ctx.IntentosDePago
                .Where(i => i.ClaveIdempotencia == claveEsperada)
                .CountAsync();
            Assert.Equal(1, intentos);
        }

        // Resumen: debería haber 1 cobro/rechazo
        Assert.Equal(1, resumen.Cobradas + resumen.Rechazadas);
        Assert.Equal(0, resumen.YaIntentadas);
    }
}

/// <summary>
/// Reloj fijo para pruebas: siempre devuelve la misma hora.
/// </summary>
internal sealed class RelojFijo : IRelojSistema
{
    private readonly DateTime _ahora;
    public RelojFijo(DateTime ahora) => _ahora = ahora;
    public DateTime Ahora => _ahora;
}

/// <summary>
/// Pasarela falsa que RECHAZA el cobro (para que la suscripción no se extienda
/// y la prueba pueda verificar la idempotencia por clave de período).
/// </summary>
internal sealed class PasarelaFalsa : IPasarelaDePagos
{
    public string Nombre => "falsa";

    public Task<Resultado<CobroIniciado>> IniciarCobroAsync(SolicitudDeCobro solicitud, CancellationToken ct = default)
        => Task.FromResult(Resultado.Ok(new CobroIniciado("ref-falsa", "https://pagar.falsa")));

    public Task<Resultado<CobroIniciado>> IniciarInscripcionAsync(SolicitudDeInscripcion solicitud, CancellationToken ct = default)
        => Task.FromResult(Resultado.Ok(new CobroIniciado("ref-falsa", "https://inscribir.falsa")));

    public Task<Resultado<CobroResuelto>> CobrarAsync(SolicitudDeCobro solicitud, CancellationToken ct = default)
        => Task.FromResult(Resultado.Ok(new CobroResuelto(solicitud.IntentoDePagoId, "ref-falsa", false, solicitud.Monto, "rechazada por prueba")));

    public Task<Resultado<CobroResuelto>> LeerConfirmacionAsync(AvisoDePasarela aviso, CancellationToken ct = default)
        => Task.FromResult(Resultado.Ok(new CobroResuelto(Guid.Empty, null, false, null, "rechazada por prueba")));
}