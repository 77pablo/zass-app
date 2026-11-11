using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;
using Zass.Aplicacion.Puertos;
using Zass.Dominio.Comun;
using Zass.Dominio.Identidad;
using Zass.Infraestructura.Configuracion;
using Zass.Infraestructura.Correo;
using Zass.Infraestructura.Datos;

namespace Zass.Integracion.Pruebas.Correo;

/// <summary>
/// El despacho de avisos contra PostgreSQL real y un HttpMessageHandler falso. Aquí NO se
/// usa la transacción que se deshace de ContextoDePrueba: la reserva con SKIP LOCKED y la
/// concurrencia solo se prueban con filas confirmadas y conexiones distintas. Cada prueba
/// borra al final los usuarios que creó (los avisos caen en cascada).
/// </summary>
public class ProcesadorDeCorreoPruebas : IAsyncLifetime
{
    private const string Url = "https://zass.test";
    private const string Prefijo = "correo-prueba-";

    private readonly RelojFalso _reloj = new();
    private readonly RegistroEnMemoria<ProcesadorDeCorreo> _log = new();

    private static readonly OpcionesCorreo BaseOpciones = new();

    private OpcionesCorreo _o = new()
    {
        Proveedor = ProveedorDeCorreo.Brevo, ApiKey = "clave-secreta-de-prueba-123",
        Remitente = "no-responder@zass.cl", MaximoIntentos = 5, EsperaBaseSegundos = 60,
        EsperaMaximaSegundos = 3600, TamanoDeLote = 10, PurgaDeFallidasHoras = 24, ReservaSegundos = 120,
    };

    private string _urlPublica = Url;

    public async Task InitializeAsync()
    {
        // Esta base es solo de las pruebas. Avisos pendientes que dejara otra prueba
        // serían tomados por este procesador y falsearían los recuentos.
        using var ctx = ContextoDePrueba.Crear();
        await ctx.Database.ExecuteSqlRawAsync(
            "DELETE FROM notificaciones WHERE estado IN ('Programada', 'Enviando')");
    }

    public async Task DisposeAsync()
    {
        using var ctx = ContextoDePrueba.Crear();
        await ctx.Database.ExecuteSqlRawAsync(
            "DELETE FROM usuarios WHERE correo LIKE {0}", Prefijo + "%");
    }

    // ------------------------------------------------------------ ayudas

    private ProcesadorDeCorreo Procesador(ZassDbContext ctx, ManejadorFalso manejador, RegistroEnMemoria<ProcesadorDeCorreo>? log = null)
    {
        var enviador = new EnviadorBrevo(new HttpClient(manejador), Options.Create(_o),
            new RegistroEnMemoria<EnviadorBrevo>());
        return new ProcesadorDeCorreo(ctx, enviador, _reloj, Options.Create(_o),
            Options.Create(new OpcionesZass { UrlPublica = _urlPublica }), log ?? _log);
    }

    private static async Task<Usuario> CrearUsuarioAsync(string nombre = "Ana Pérez", bool verificado = false)
    {
        using var ctx = ContextoDePrueba.Crear();
        var u = new Usuario
        {
            Correo = $"{Prefijo}{Guid.NewGuid():N}@zass.local",
            Nombre = nombre,
            HashContrasena = "x",
            CorreoVerificado = verificado,
            Estado = verificado ? EstadoCuenta.Activa : EstadoCuenta.PendienteVerificacion,
        };
        ctx.Usuarios.Add(u);
        await ctx.SaveChangesAsync();
        return u;
    }

    private async Task<Guid> EncolarAsync(Guid usuarioId, string token,
        TipoNotificacion tipo = TipoNotificacion.CorreoVerificacion, string? cuerpo = null)
    {
        using var ctx = ContextoDePrueba.Crear();
        var n = new Notificacion
        {
            DestinatarioId = usuarioId,
            Tipo = tipo,
            Canal = CanalNotificacion.Correo,
            ClaveIdempotencia = $"prueba:{Guid.NewGuid():N}",
            Asunto = "x",
            Cuerpo = cuerpo ?? CargaDeAviso.ConToken(token),
            ProgramadaPara = _reloj.Ahora.AddSeconds(-1),
        };
        ctx.Notificaciones.Add(n);
        await ctx.SaveChangesAsync();
        return n.Id;
    }

    private static async Task<Notificacion> LeerAsync(Guid id)
    {
        using var ctx = ContextoDePrueba.Crear();
        return await ctx.Notificaciones.AsNoTracking().SingleAsync(n => n.Id == id);
    }

    private async Task<int> CicloAsync(ManejadorFalso m, RegistroEnMemoria<ProcesadorDeCorreo>? log = null)
    {
        using var ctx = ContextoDePrueba.Crear();
        return await Procesador(ctx, m, log).ProcesarLoteAsync();
    }

    /// <summary>La base guarda microsegundos y el reloj falso, décimas de microsegundo.</summary>
    private static void Cerca(TimeSpan esperado, TimeSpan real) =>
        Assert.True(Math.Abs((esperado - real).TotalMilliseconds) < 5, $"esperado {esperado}, real {real}");

    // ------------------------------------------------------------ éxito

    [Fact]
    public async Task Un_aviso_de_verificacion_se_envia_con_el_enlace_correcto_y_su_token_se_purga()
    {
        var u = await CrearUsuarioAsync("Ana Pérez");
        var id = await EncolarAsync(u.Id, "TOKEN_ABC-123");
        var m = ManejadorFalso.Responde(HttpStatusCode.Created);

        Assert.Equal(1, await CicloAsync(m));

        var p = Assert.Single(m.Peticiones);
        Assert.Contains("https://zass.test/verificar-correo?token=TOKEN_ABC-123", p.Cuerpo);
        Assert.Contains(u.Correo, p.Cuerpo);
        var n = await LeerAsync(id);
        Assert.Equal(EstadoEnvio.Enviada, n.Estado);
        Assert.NotNull(n.EnviadaEn);
        Assert.Equal(1, n.Intentos);
        Assert.Null(n.UltimoError);
        // Higiene: el token en claro ya no existe en la base.
        Assert.Equal(CargaDeAviso.Purgada, n.Cuerpo);
        Assert.DoesNotContain("TOKEN_ABC-123", n.Cuerpo);
        Assert.DoesNotContain("TOKEN_ABC-123", n.Asunto);
    }

    [Fact]
    public async Task Un_aviso_de_recuperacion_apunta_a_restablecer_contrasena()
    {
        var u = await CrearUsuarioAsync(verificado: true);
        await EncolarAsync(u.Id, "REC-999", TipoNotificacion.RecuperarContrasena);
        var m = ManejadorFalso.Responde(HttpStatusCode.OK);

        await CicloAsync(m);

        Assert.Contains("https://zass.test/restablecer-contrasena?token=REC-999", Assert.Single(m.Peticiones).Cuerpo);
    }

    [Fact]
    public async Task Lo_enviado_no_se_vuelve_a_enviar()
    {
        var u = await CrearUsuarioAsync();
        await EncolarAsync(u.Id, "T1");
        var m = ManejadorFalso.Responde(HttpStatusCode.Created);

        await CicloAsync(m);
        _reloj.Avanzar(TimeSpan.FromHours(1));
        Assert.Equal(0, await CicloAsync(m));
        Assert.Equal(1, m.Cuenta);
    }

    // ------------------------------------------------------------ 4xx, 5xx, timeout

    [Fact]
    public async Task Un_4xx_deja_el_aviso_fallido_sin_reintentar()
    {
        var u = await CrearUsuarioAsync();
        var id = await EncolarAsync(u.Id, "T1");
        var m = ManejadorFalso.Responde(HttpStatusCode.Unauthorized);

        await CicloAsync(m);
        _reloj.Avanzar(TimeSpan.FromHours(2));
        await CicloAsync(m);

        Assert.Equal(1, m.Cuenta);
        var n = await LeerAsync(id);
        Assert.Equal(EstadoEnvio.Fallida, n.Estado);
        Assert.Equal("HTTP 401", n.UltimoError);
        Assert.Equal(1, n.Intentos);
    }

    [Fact]
    public async Task Un_aviso_fallido_conserva_su_carga_un_plazo_y_pasado_el_plazo_se_purga()
    {
        var u = await CrearUsuarioAsync();
        var id = await EncolarAsync(u.Id, "TOKEN-FALLIDO");
        await CicloAsync(ManejadorFalso.Responde(HttpStatusCode.BadRequest));

        // Dentro del plazo todavía está (para poder investigar)...
        _reloj.Avanzar(TimeSpan.FromHours(23));
        await CicloAsync(ManejadorFalso.Responde(HttpStatusCode.BadRequest));
        Assert.Contains("TOKEN-FALLIDO", (await LeerAsync(id)).Cuerpo);

        // ...y pasadas 24 h desde el fallo desaparece, aunque nadie vuelva a enviar nada.
        _reloj.Avanzar(TimeSpan.FromHours(2));
        await CicloAsync(ManejadorFalso.Responde(HttpStatusCode.BadRequest));
        var n = await LeerAsync(id);
        Assert.Equal(CargaDeAviso.Purgada, n.Cuerpo);
        Assert.Equal(EstadoEnvio.Fallida, n.Estado);
    }

    [Fact]
    public async Task Un_5xx_se_reintenta_con_espera_creciente_y_al_final_sale()
    {
        var u = await CrearUsuarioAsync();
        var id = await EncolarAsync(u.Id, "T1");
        var m = new ManejadorFalso((_, n) => Task.FromResult(new HttpResponseMessage(
            n < 3 ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.Created)));

        // intento 1: falla, espera 60 s
        var t0 = _reloj.Ahora;
        await CicloAsync(m);
        var n1 = await LeerAsync(id);
        Assert.Equal(EstadoEnvio.Programada, n1.Estado);
        Assert.Equal(1, n1.Intentos);
        Cerca(TimeSpan.FromSeconds(60), n1.ProgramadaPara - t0);
        Assert.Equal("HTTP 503", n1.UltimoError);
        Assert.Contains("T1", n1.Cuerpo);   // sigue pendiente: el token aún hace falta

        // antes de la espera no se toca
        _reloj.Avanzar(TimeSpan.FromSeconds(59));
        Assert.Equal(0, await CicloAsync(m));

        // intento 2: falla, la espera se duplica (120 s)
        _reloj.Avanzar(TimeSpan.FromSeconds(2));
        var t1 = _reloj.Ahora;
        await CicloAsync(m);
        var n2 = await LeerAsync(id);
        Assert.Equal(2, n2.Intentos);
        Cerca(TimeSpan.FromSeconds(120), n2.ProgramadaPara - t1);

        // intento 3: sale
        _reloj.Avanzar(TimeSpan.FromSeconds(121));
        await CicloAsync(m);
        var n3 = await LeerAsync(id);
        Assert.Equal(EstadoEnvio.Enviada, n3.Estado);
        Assert.Equal(3, n3.Intentos);
        Assert.Equal(CargaDeAviso.Purgada, n3.Cuerpo);
        Assert.Equal(3, m.Cuenta);
    }

    [Fact]
    public async Task La_espera_tiene_tope()
    {
        _o.EsperaBaseSegundos = 1000;
        _o.EsperaMaximaSegundos = 1500;
        var u = await CrearUsuarioAsync();
        var id = await EncolarAsync(u.Id, "T1");
        var m = ManejadorFalso.Responde(HttpStatusCode.InternalServerError);

        await CicloAsync(m);                                   // 1000 s
        _reloj.Avanzar(TimeSpan.FromSeconds(1001));
        var t = _reloj.Ahora;
        await CicloAsync(m);                                   // 2000 s pedidos, tope 1500
        Cerca(TimeSpan.FromSeconds(1500), (await LeerAsync(id)).ProgramadaPara - t);
    }

    [Fact]
    public async Task Un_timeout_se_reintenta()
    {
        var u = await CrearUsuarioAsync();
        var id = await EncolarAsync(u.Id, "T1");
        var m = new ManejadorFalso((_, _) => throw new TaskCanceledException("timeout"));

        await CicloAsync(m);

        var n = await LeerAsync(id);
        Assert.Equal(EstadoEnvio.Programada, n.Estado);
        Assert.Equal("tiempo de espera agotado", n.UltimoError);
        Assert.True(n.ProgramadaPara > _reloj.Ahora);
    }

    [Fact]
    public async Task Tras_el_maximo_de_intentos_queda_fallida_y_no_se_reintenta_mas()
    {
        _o.MaximoIntentos = 3;
        var u = await CrearUsuarioAsync();
        var id = await EncolarAsync(u.Id, "T1");
        var m = ManejadorFalso.Responde(HttpStatusCode.ServiceUnavailable);

        for (var i = 0; i < 6; i++)
        {
            await CicloAsync(m);
            _reloj.Avanzar(TimeSpan.FromHours(2));   // más que cualquier espera
        }

        Assert.Equal(3, m.Cuenta);
        var n = await LeerAsync(id);
        Assert.Equal(EstadoEnvio.Fallida, n.Estado);
        Assert.Equal(3, n.Intentos);
    }

    // ------------------------------------------------------------ registros sin datos sensibles

    [Fact]
    public async Task Los_registros_enmascaran_el_correo_y_nunca_muestran_el_token_ni_la_clave()
    {
        var u = await CrearUsuarioAsync("Ana Pérez");
        await EncolarAsync(u.Id, "TOKEN-QUE-NO-DEBE-SALIR");
        var u2 = await CrearUsuarioAsync();
        await EncolarAsync(u2.Id, "OTRO-TOKEN-SECRETO");
        var u3 = await CrearUsuarioAsync();
        await EncolarAsync(u3.Id, "TERCER-TOKEN-SECRETO");

        var guion = new Queue<HttpStatusCode>(new[]
            { HttpStatusCode.Created, HttpStatusCode.ServiceUnavailable, HttpStatusCode.BadRequest });
        var m = new ManejadorFalso((_, _) => Task.FromResult(new HttpResponseMessage(guion.Dequeue())));
        var log = new RegistroEnMemoria<ProcesadorDeCorreo>();

        await CicloAsync(m, log);

        var todo = log.Todo;
        Assert.NotEmpty(log.Mensajes);
        foreach (var usuario in new[] { u, u2, u3 })
            Assert.DoesNotContain(usuario.Correo, todo);
        Assert.Contains("c***@zass.local", todo);
        foreach (var secreto in new[] { "TOKEN-QUE-NO-DEBE-SALIR", "OTRO-TOKEN-SECRETO", "TERCER-TOKEN-SECRETO", _o.ApiKey })
            Assert.DoesNotContain(secreto, todo);
    }

    // ------------------------------------------------------------ UrlPublica, usuarios, tipos

    [Fact]
    public async Task Sin_url_publica_no_se_envia_nada_se_registra_y_el_aviso_sigue_pendiente_sin_gastar_intentos()
    {
        _urlPublica = "";
        var u = await CrearUsuarioAsync();
        var id = await EncolarAsync(u.Id, "T1");
        var m = ManejadorFalso.Responde(HttpStatusCode.Created);
        var log = new RegistroEnMemoria<ProcesadorDeCorreo>();

        Assert.Equal(0, await CicloAsync(m, log));

        Assert.Equal(0, m.Cuenta);
        var n = await LeerAsync(id);
        Assert.Equal(EstadoEnvio.Programada, n.Estado);
        Assert.Equal(0, n.Intentos);
        Assert.Contains("Zass:UrlPublica", log.Todo);

        // Al configurarla, sale solo.
        _urlPublica = Url;
        await CicloAsync(m);
        Assert.Equal(1, m.Cuenta);
    }

    [Fact]
    public async Task Si_el_usuario_ya_verifico_su_correo_el_aviso_se_anula_sin_enviar_y_sin_token()
    {
        var u = await CrearUsuarioAsync(verificado: true);
        var id = await EncolarAsync(u.Id, "T1");
        var m = ManejadorFalso.Responde(HttpStatusCode.Created);

        await CicloAsync(m);

        Assert.Equal(0, m.Cuenta);
        var n = await LeerAsync(id);
        Assert.Equal(EstadoEnvio.Anulada, n.Estado);
        Assert.Equal(CargaDeAviso.Purgada, n.Cuerpo);
    }

    [Fact]
    public async Task Una_cuenta_eliminada_no_recibe_correo()
    {
        var u = await CrearUsuarioAsync(verificado: true);
        using (var ctx = ContextoDePrueba.Crear())
            await ctx.Database.ExecuteSqlRawAsync("UPDATE usuarios SET estado = 'Eliminada' WHERE id = {0}", u.Id);
        var id = await EncolarAsync(u.Id, "T1", TipoNotificacion.RecuperarContrasena);
        var m = ManejadorFalso.Responde(HttpStatusCode.Created);

        await CicloAsync(m);

        Assert.Equal(0, m.Cuenta);
        Assert.Equal(EstadoEnvio.Anulada, (await LeerAsync(id)).Estado);
    }

    [Fact]
    public async Task Un_aviso_sin_plantilla_ni_de_otro_canal_no_se_toca()
    {
        var u = await CrearUsuarioAsync();
        var otroTipo = await EncolarAsync(u.Id, "x", TipoNotificacion.PerfilPublicado, cuerpo: "Tu perfil ya es visible");
        var m = ManejadorFalso.Responde(HttpStatusCode.Created);

        Assert.Equal(0, await CicloAsync(m));

        Assert.Equal(0, m.Cuenta);
        var n = await LeerAsync(otroTipo);
        Assert.Equal(EstadoEnvio.Programada, n.Estado);
        Assert.Equal(0, n.Intentos);
        Assert.Equal("Tu perfil ya es visible", n.Cuerpo);
    }

    [Fact]
    public async Task Una_carga_sin_token_legible_queda_fallida_sin_llamar_al_proveedor()
    {
        var u = await CrearUsuarioAsync();
        var id = await EncolarAsync(u.Id, "", cuerpo: CargaDeAviso.Purgada);
        var m = ManejadorFalso.Responde(HttpStatusCode.Created);

        await CicloAsync(m);

        Assert.Equal(0, m.Cuenta);
        Assert.Equal(EstadoEnvio.Fallida, (await LeerAsync(id)).Estado);
    }

    // ------------------------------------------------------------ reserva (lease)

    [Fact]
    public async Task Un_aviso_reservado_por_otro_trabajador_no_se_toca_hasta_que_vence_la_reserva()
    {
        var u = await CrearUsuarioAsync();
        var id = await EncolarAsync(u.Id, "T1");
        var m = ManejadorFalso.Responde(HttpStatusCode.Created);

        // Un trabajador lo reservó y murió: queda «Enviando» con la reserva en el futuro.
        using (var ctx = ContextoDePrueba.Crear())
            await ctx.Database.ExecuteSqlRawAsync(
                "UPDATE notificaciones SET estado = 'Enviando', intentos = 1, programada_para = {0} WHERE id = {1}",
                _reloj.Ahora.AddSeconds(100), id);

        Assert.Equal(0, await CicloAsync(m));
        Assert.Equal(0, m.Cuenta);

        // Vencida la reserva, otro trabajador lo retoma (y cuenta como segundo intento).
        _reloj.Avanzar(TimeSpan.FromSeconds(101));
        Assert.Equal(1, await CicloAsync(m));
        var n = await LeerAsync(id);
        Assert.Equal(EstadoEnvio.Enviada, n.Estado);
        Assert.Equal(2, n.Intentos);
    }

    // ------------------------------------------------------------ concurrencia

    [Fact]
    public async Task Dos_trabajadores_a_la_vez_envian_cada_aviso_una_sola_vez()
    {
        _o.TamanoDeLote = 4;
        const int total = 40;
        var tokens = new List<string>();
        for (var i = 0; i < total; i++)
        {
            var u = await CrearUsuarioAsync($"Persona {i}");
            var token = $"TOK-{i:000}-{Guid.NewGuid():N}";
            tokens.Add(token);
            await EncolarAsync(u.Id, token);
        }

        // Un proveedor lento, para que los dos trabajadores se solapen de verdad.
        var m = ManejadorFalso.Responde(HttpStatusCode.Created, demoraMs: 15);

        async Task Trabajador()
        {
            while (true)
            {
                using var ctx = ContextoDePrueba.Crear();   // conexión propia: dos procesos distintos
                var tomados = await Procesador(ctx, m).ProcesarLoteAsync();
                if (tomados == 0) break;
            }
        }

        await Task.WhenAll(Trabajador(), Trabajador());

        // Un solo envío por aviso: ni uno perdido ni uno repetido.
        Assert.Equal(total, m.Cuenta);
        foreach (var token in tokens)
            Assert.Single(m.Peticiones, p => p.Cuerpo.Contains(token));

        using var verif = ContextoDePrueba.Crear();
        var filas = await verif.Notificaciones.AsNoTracking()
            .Where(n => n.Destinatario!.Correo.StartsWith(Prefijo)).ToListAsync();
        Assert.Equal(total, filas.Count);
        Assert.All(filas, n =>
        {
            Assert.Equal(EstadoEnvio.Enviada, n.Estado);
            Assert.Equal(1, n.Intentos);
            Assert.Equal(CargaDeAviso.Purgada, n.Cuerpo);
        });
    }

    [Fact]
    public async Task Dos_reservas_simultaneas_reciben_filas_distintas()
    {
        _o.TamanoDeLote = 5;
        var ids = new List<Guid>();
        for (var i = 0; i < 10; i++)
            ids.Add(await EncolarAsync((await CrearUsuarioAsync()).Id, $"T{i}"));

        using var ctxA = ContextoDePrueba.Crear();
        using var ctxB = ContextoDePrueba.Crear();
        var m = ManejadorFalso.Responde(HttpStatusCode.Created);

        var lotes = await Task.WhenAll(
            Procesador(ctxA, m).TomarLoteAsync(default), Procesador(ctxB, m).TomarLoteAsync(default));
        var (a, b) = (lotes[0], lotes[1]);

        Assert.Equal(5, a.Count);
        Assert.Equal(5, b.Count);
        Assert.Empty(a.Intersect(b));
    }

    // ------------------------------------------------------------ el lote respeta su tamaño

    /// <summary>
    /// La forma de reserva que falló en CI (J1), tal cual estaba. NO es la consulta del
    /// procesador: es la referencia con la que se comprueba que las estadísticas de la prueba
    /// empujan al planificador al plan malo. Por eso la precondición sigue valiendo después de
    /// cambiar la consulta de producción.
    /// </summary>
    private const string FormaQueFalloEnCi = """
        UPDATE notificaciones SET estado = 'Enviando', intentos = intentos + 1, programada_para = @reserva
        WHERE id IN (
            SELECT id FROM notificaciones
            WHERE canal = 'Correo' AND tipo = ANY(@tipos)
              AND estado IN ('Programada', 'Enviando') AND programada_para <= @ahora
            ORDER BY programada_para
            LIMIT @lote
            FOR UPDATE SKIP LOCKED)
        RETURNING id
        """;

    private Notificacion AvisoAislado(int i, EstadoEnvio estado, DateTime programadaPara) => new()
    {
        DestinatarioId = Guid.NewGuid(),   // la copia aislada no lleva la FK a usuarios
        Tipo = TipoNotificacion.CorreoVerificacion,
        Canal = CanalNotificacion.Correo,
        ClaveIdempotencia = $"prueba:{Guid.NewGuid():N}",
        Asunto = "x",
        Cuerpo = CargaDeAviso.ConToken($"T{i}"),
        Estado = estado,
        ProgramadaPara = programadaPara,
    };

    /// <summary>
    /// Copia de notificaciones en un esquema propio y desechable, con las estadísticas que había
    /// en CI cuando falló: analizada casi vacía, así que el planificador estima ~1 fila en la
    /// tabla y 1 elegible. Después llegan los avisos de verdad.
    /// El ANALYZE es de la copia: las estadísticas de public.notificaciones no se tocan.
    /// Devuelve el esquema y la cadena de conexión que lo pone primero en search_path.
    /// </summary>
    private async Task<(string Esquema, string Cadena)> CopiaConEstadisticasDeCiAsync(int elegibles)
    {
        var esquema = "lote_aislado_" + Guid.NewGuid().ToString("N")[..12];
        using (var ctx = ContextoDePrueba.Crear())
            await ctx.Database.ExecuteSqlRawAsync(
                "CREATE SCHEMA " + esquema + "; "
                + "CREATE TABLE " + esquema + ".notificaciones (LIKE public.notificaciones INCLUDING ALL) "
                + "WITH (autovacuum_enabled = false);");

        // Sin esquema por defecto en el modelo, «notificaciones» se resuelve por search_path.
        var cadena = new NpgsqlConnectionStringBuilder(Conexion.Cadena) { SearchPath = esquema + ",public" }
            .ConnectionString;

        using (var ctx = Aislado(cadena))
        {
            // UNA fila ya enviada: el ANALYZE deja «1 fila en 1 página», y como los avisos nuevos
            // caben en esa misma página el planificador sigue creyendo que la tabla tiene ~1 fila.
            // Así estaba en CI (Index Scan … rows=1 en el lado externo). Con 40 elige Hash Semi Join.
            ctx.Notificaciones.Add(AvisoAislado(0, EstadoEnvio.Enviada, _reloj.Ahora.AddDays(-1)));
            await ctx.SaveChangesAsync();
            await ctx.Database.ExecuteSqlRawAsync("ANALYZE notificaciones");

            for (var i = 0; i < elegibles; i++)
                ctx.Notificaciones.Add(AvisoAislado(i, EstadoEnvio.Programada, _reloj.Ahora.AddSeconds(-1)));
            await ctx.SaveChangesAsync();
        }
        return (esquema, cadena);
    }

    private static ZassDbContext Aislado(string cadena) =>
        new(new DbContextOptionsBuilder<ZassDbContext>().UseNpgsql(cadena).Options);

    private static async Task BorrarEsquemaAsync(string esquema)
    {
        using var ctx = ContextoDePrueba.Crear();
        await ctx.Database.ExecuteSqlRawAsync("DROP SCHEMA " + esquema + " CASCADE");
    }

    /// <summary>
    /// Precondición: con estas estadísticas, la forma que falló en CI se planifica con la
    /// subconsulta del lado interno de un Nested Loop Semi Join (la que se reejecuta). Si este
    /// PostgreSQL elige otro plan, la prueba no estaría probando nada y se dice en vez de dar verde.
    /// </summary>
    private async Task ExigirPlanQueReejecutaLaSubconsultaAsync(string cadena, int lote)
    {
        using var ctx = Aislado(cadena);
        await ctx.Database.OpenConnectionAsync();
        await using var cmd = ctx.Database.GetDbConnection().CreateCommand();
        cmd.CommandText = "EXPLAIN " + FormaQueFalloEnCi;
        cmd.Parameters.Add(new NpgsqlParameter("reserva", NpgsqlTypes.NpgsqlDbType.TimestampTz)
            { Value = _reloj.Ahora.AddSeconds(120) });
        cmd.Parameters.Add(new NpgsqlParameter("ahora", NpgsqlTypes.NpgsqlDbType.TimestampTz) { Value = _reloj.Ahora });
        cmd.Parameters.Add(new NpgsqlParameter("lote", NpgsqlTypes.NpgsqlDbType.Integer) { Value = lote });
        cmd.Parameters.Add(new NpgsqlParameter("tipos", NpgsqlTypes.NpgsqlDbType.Array | NpgsqlTypes.NpgsqlDbType.Varchar)
            { Value = new[] { nameof(TipoNotificacion.CorreoVerificacion) } });

        var plan = new System.Text.StringBuilder();
        await using (var r = await cmd.ExecuteReaderAsync())
            while (await r.ReadAsync()) plan.AppendLine(r.GetString(0));

        Assert.True(plan.ToString().Contains("Nested Loop Semi Join"),
            "Precondición: con estas estadísticas el plan no es el que reproduce J1 (Nested Loop Semi Join):\n" + plan);
    }

    [Fact]
    public async Task Un_lote_nunca_trae_mas_avisos_que_TamanoDeLote_aunque_el_plan_reejecute_la_subconsulta()
    {
        const int lote = 5;
        var (esquema, cadena) = await CopiaConEstadisticasDeCiAsync(elegibles: 10);
        try
        {
            await ExigirPlanQueReejecutaLaSubconsultaAsync(cadena, lote);

            _o.TamanoDeLote = lote;
            using var ctx = Aislado(cadena);
            var tomados = await Procesador(ctx, ManejadorFalso.Responde(HttpStatusCode.Created))
                .TomarLoteAsync(default);

            Assert.Equal(lote, tomados.Count);
        }
        finally
        {
            await BorrarEsquemaAsync(esquema);
        }
    }
}
