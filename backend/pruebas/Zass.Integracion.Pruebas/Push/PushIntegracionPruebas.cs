using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Zass.Aplicacion.Notificaciones;
using Zass.Aplicacion.Puertos;
using Zass.Dominio.Comun;
using Zass.Dominio.Identidad;
using Zass.Infraestructura.Datos;
using Zass.Infraestructura.Datos.Repositorios;
using Zass.Infraestructura.Push;
using Zass.Integracion.Pruebas.Correo;

namespace Zass.Integracion.Pruebas.Push;

/// <summary>
/// ES-3. El push contra PostgreSQL real: la gemela que crea la capa de datos al guardar, la que
/// recoge API-45 de lo ya encolado, el tope y el traspaso de navegadores, y el procesador con un
/// enviador falso. Al final, el enviador de verdad (cifrado y VAPID) contra un servicio de push falso.
/// </summary>
public class PushIntegracionPruebas : IAsyncLifetime
{
    private const string Prefijo = "push-prueba-";
    private readonly RelojFalso _reloj = new();

    public async Task InitializeAsync()
    {
        // El procesador barre TODA la cola de push: se parte sin restos de otras pruebas.
        using var ctx = ContextoDePrueba.Crear();
        await ctx.Database.ExecuteSqlRawAsync(
            "DELETE FROM notificaciones WHERE canal = 'Push' AND estado IN ('Programada', 'Enviando')");
    }

    public async Task DisposeAsync()
    {
        using var ctx = ContextoDePrueba.Crear();
        await ctx.Database.ExecuteSqlRawAsync("DELETE FROM usuarios WHERE correo LIKE {0}", Prefijo + "%");
    }

    // ------------------------------------------------------------------ ayudantes

    internal static (string P256dh, string Auth) ClavesDeNavegador()
    {
        using var ecdh = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        var p = ecdh.ExportParameters(false);
        var publica = new byte[65];
        publica[0] = 0x04;
        p.Q.X!.CopyTo(publica, 1);
        p.Q.Y!.CopyTo(publica, 33);
        return (B64Url(publica), B64Url(RandomNumberGenerator.GetBytes(16)));
    }

    internal static string B64Url(byte[] b) => Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    internal static string Endpoint() => $"https://fcm.googleapis.com/fcm/send/{Guid.NewGuid():N}";

    private static async Task<Usuario> UsuarioAsync(string nombre = "Ana")
    {
        using var ctx = ContextoDePrueba.Crear();
        var u = new Usuario
        {
            Correo = $"{Prefijo}{Guid.NewGuid():N}@zass.local", Nombre = nombre, HashContrasena = "x",
            CorreoVerificado = true, Estado = EstadoCuenta.Activa,
        };
        ctx.Usuarios.Add(u);
        await ctx.SaveChangesAsync();
        return u;
    }

    private static async Task<SuscripcionPush> NavegadorAsync(Guid usuarioId, DateTime? uso = null)
    {
        using var ctx = ContextoDePrueba.Crear();
        var (p, a) = ClavesDeNavegador();
        var s = new SuscripcionPush
        {
            UsuarioId = usuarioId, Endpoint = Endpoint(), ClaveP256dh = p, ClaveAuth = a,
            UltimoUsoEn = uso ?? DateTime.UtcNow,
        };
        ctx.SuscripcionesPush.Add(s);
        await ctx.SaveChangesAsync();
        return s;
    }

    private static Notificacion Aviso(Guid destinatario, TipoNotificacion tipo = TipoNotificacion.SuscripcionVencida,
        DateTime? programada = null) => new()
    {
        DestinatarioId = destinatario, Tipo = tipo, Canal = CanalNotificacion.Correo,
        ClaveIdempotencia = $"prueba-push:{Guid.NewGuid():N}", Cuerpo = "{}",
        ProgramadaPara = programada ?? DateTime.UtcNow,
    };

    private static async Task<List<Notificacion>> AvisosDeAsync(Guid usuarioId)
    {
        using var ctx = ContextoDePrueba.Crear();
        return await ctx.Notificaciones.AsNoTracking().Where(n => n.DestinatarioId == usuarioId).ToListAsync();
    }

    private static async Task EncolarAsync(params Notificacion[] avisos)
    {
        using var ctx = ContextoDePrueba.Crear();
        ctx.Notificaciones.AddRange(avisos);
        await ctx.SaveChangesAsync();
    }

    // ------------------------------------------------------------------ la gemela al guardar

    [Fact]
    public async Task Con_un_navegador_registrado_el_aviso_lleva_su_gemela_de_push()
    {
        var u = await UsuarioAsync();
        await NavegadorAsync(u.Id);
        var aviso = Aviso(u.Id);

        await EncolarAsync(aviso);

        var avisos = await AvisosDeAsync(u.Id);
        Assert.Equal(2, avisos.Count);
        var push = Assert.Single(avisos, n => n.Canal == CanalNotificacion.Push);
        Assert.Equal(aviso.ClaveIdempotencia + AvisosPush.SufijoClave, push.ClaveIdempotencia);
        Assert.Equal(EstadoEnvio.Programada, push.Estado);
        Assert.Equal(aviso.Tipo, push.Tipo);
    }

    [Fact]
    public async Task Sin_navegador_no_hay_gemela()
    {
        var u = await UsuarioAsync();

        await EncolarAsync(Aviso(u.Id));

        Assert.Equal(CanalNotificacion.Correo, Assert.Single(await AvisosDeAsync(u.Id)).Canal);
    }

    [Fact]
    public async Task Los_avisos_con_token_nunca_van_por_push()
    {
        var u = await UsuarioAsync();
        await NavegadorAsync(u.Id);

        await EncolarAsync(
            Aviso(u.Id, TipoNotificacion.CorreoVerificacion), Aviso(u.Id, TipoNotificacion.RecuperarContrasena));

        Assert.All(await AvisosDeAsync(u.Id), n => Assert.Equal(CanalNotificacion.Correo, n.Canal));
    }

    [Fact]
    public async Task La_gemela_es_solo_del_destinatario_con_navegador()
    {
        var conPush = await UsuarioAsync();
        var sinPush = await UsuarioAsync();
        await NavegadorAsync(conPush.Id);

        await EncolarAsync(Aviso(conPush.Id), Aviso(sinPush.Id));

        Assert.Equal(2, (await AvisosDeAsync(conPush.Id)).Count);
        Assert.Single(await AvisosDeAsync(sinPush.Id));
    }

    // ------------------------------------------------------------------ lo ya encolado (API-45)

    [Fact]
    public async Task Al_registrar_un_navegador_recoge_lo_que_esperaba_en_la_cola_y_no_lo_ya_enviado()
    {
        var u = await UsuarioAsync();
        var pendiente = Aviso(u.Id, TipoNotificacion.Recordatorio24h, DateTime.UtcNow.AddHours(5));
        var enviado = Aviso(u.Id);
        enviado.Estado = EstadoEnvio.Enviada;
        var conToken = Aviso(u.Id, TipoNotificacion.RecuperarContrasena);
        await EncolarAsync(pendiente, enviado, conToken);
        await NavegadorAsync(u.Id);

        using (var ctx = ContextoDePrueba.Crear())
        {
            var repo = new RepositorioPushEfCore(ctx);
            Assert.Equal(1, await repo.EncolarGemelasPendientesAsync(u.Id));
            Assert.Equal(0, await repo.EncolarGemelasPendientesAsync(u.Id));   // idempotente
        }

        var push = Assert.Single(await AvisosDeAsync(u.Id), n => n.Canal == CanalNotificacion.Push);
        Assert.Equal(pendiente.ClaveIdempotencia + AvisosPush.SufijoClave, push.ClaveIdempotencia);
        Assert.Equal(pendiente.ProgramadaPara, push.ProgramadaPara, TimeSpan.FromMilliseconds(1));
    }

    // ------------------------------------------------------------------ registrar y dar de baja

    [Fact]
    public async Task Un_navegador_que_ya_era_de_otra_cuenta_pasa_a_la_nueva()
    {
        var antes = await UsuarioAsync();
        var ahora = await UsuarioAsync();
        var s = await NavegadorAsync(antes.Id);
        var (p, a) = ClavesDeNavegador();

        using (var ctx = ContextoDePrueba.Crear())
        {
            await new RepositorioPushEfCore(ctx).RegistrarAsync(ahora.Id, s.Endpoint, p, a, DateTime.UtcNow);
            await ctx.SaveChangesAsync();
        }

        using var lee = ContextoDePrueba.Crear();
        var fila = await lee.SuscripcionesPush.SingleAsync(x => x.Endpoint == s.Endpoint);
        Assert.Equal(ahora.Id, fila.UsuarioId);
        Assert.Equal(p, fila.ClaveP256dh);
        Assert.False(await lee.SuscripcionesPush.AnyAsync(x => x.UsuarioId == antes.Id));
    }

    [Fact]
    public async Task Pasado_el_tope_se_borra_el_navegador_que_lleva_mas_sin_usarse()
    {
        var u = await UsuarioAsync();
        var masViejo = await NavegadorAsync(u.Id, DateTime.UtcNow.AddDays(-30));
        for (var i = 0; i < AvisosPush.MaximoPorUsuario - 1; i++) await NavegadorAsync(u.Id, DateTime.UtcNow.AddDays(-i));
        var (p, a) = ClavesDeNavegador();
        var nuevo = Endpoint();

        using (var ctx = ContextoDePrueba.Crear())
        {
            await new RepositorioPushEfCore(ctx).RegistrarAsync(u.Id, nuevo, p, a, DateTime.UtcNow);
            await ctx.SaveChangesAsync();
        }

        using var lee = ContextoDePrueba.Crear();
        var suyos = await lee.SuscripcionesPush.Where(x => x.UsuarioId == u.Id).Select(x => x.Endpoint).ToListAsync();
        Assert.Equal(AvisosPush.MaximoPorUsuario, suyos.Count);
        Assert.Contains(nuevo, suyos);
        Assert.DoesNotContain(masViejo.Endpoint, suyos);
    }

    [Fact]
    public async Task Dar_de_baja_solo_borra_un_navegador_propio()
    {
        var duena = await UsuarioAsync();
        var otra = await UsuarioAsync();
        var s = await NavegadorAsync(duena.Id);

        using (var ctx = ContextoDePrueba.Crear())
        {
            Assert.False(await new RepositorioPushEfCore(ctx).BorrarAsync(otra.Id, s.Endpoint));
            Assert.True(await new RepositorioPushEfCore(ctx).BorrarAsync(duena.Id, s.Endpoint));
            await ctx.SaveChangesAsync();
        }

        using var lee = ContextoDePrueba.Crear();
        Assert.False(await lee.SuscripcionesPush.AnyAsync(x => x.Endpoint == s.Endpoint));
    }

    // ------------------------------------------------------------------ el procesador

    private sealed class EnviadorFalso : IEnviadorDePush
    {
        public Func<SuscripcionPush, ResultadoPush> Responder { get; set; } = _ => ResultadoPush.Entregado;
        public List<(string Endpoint, MensajeSaliente Mensaje)> Enviados { get; } = new();

        public Task<ResultadoPush> EnviarAsync(SuscripcionPush destino, MensajeSaliente mensaje, CancellationToken ct)
        {
            Enviados.Add((destino.Endpoint, mensaje));
            return Task.FromResult(Responder(destino));
        }
    }

    private static readonly OpcionesPush Activas = new()
    {
        ClavePublica = "publica", ClavePrivada = "privada", Sujeto = "mailto:soporte@zass.cl",
        MaximoIntentos = 3, EsperaBaseSegundos = 30, EsperaMaximaSegundos = 900, TamanoDeLote = 50,
    };

    private ProcesadorDePush Procesador(ZassDbContext ctx, IEnviadorDePush enviador, OpcionesPush? o = null) =>
        new(ctx, enviador, _reloj, Options.Create(o ?? Activas), new RegistroEnMemoria<ProcesadorDePush>());

    private async Task<int> ProcesarAsync(IEnviadorDePush enviador, OpcionesPush? o = null)
    {
        using var ctx = ContextoDePrueba.Crear();
        return await Procesador(ctx, enviador, o).ProcesarLoteAsync();
    }

    private static async Task<Notificacion> PushDeAsync(Guid usuarioId) =>
        Assert.Single(await AvisosDeAsync(usuarioId), n => n.Canal == CanalNotificacion.Push);

    [Fact]
    public async Task Sale_a_todos_los_navegadores_y_queda_enviado()
    {
        var u = await UsuarioAsync();
        await NavegadorAsync(u.Id);
        await NavegadorAsync(u.Id);
        await EncolarAsync(Aviso(u.Id, programada: _reloj.Ahora.AddMinutes(-1)));
        var enviador = new EnviadorFalso();

        await ProcesarAsync(enviador);

        Assert.Equal(2, enviador.Enviados.Count);
        Assert.Equal(EstadoEnvio.Enviada, (await PushDeAsync(u.Id)).Estado);
        var json = JsonDocument.Parse(enviador.Enviados[0].Mensaje.Json).RootElement;
        Assert.Equal("Tu plan venció", json.GetProperty("titulo").GetString());
        Assert.Equal("/mi-ficha", json.GetProperty("url").GetString());
    }

    [Fact]
    public async Task El_correo_no_lo_toca_el_procesador_de_push()
    {
        var u = await UsuarioAsync();
        await NavegadorAsync(u.Id);
        await EncolarAsync(Aviso(u.Id, programada: _reloj.Ahora.AddMinutes(-1)));

        await ProcesarAsync(new EnviadorFalso());

        Assert.Equal(EstadoEnvio.Programada,
            Assert.Single(await AvisosDeAsync(u.Id), n => n.Canal == CanalNotificacion.Correo).Estado);
    }

    [Fact]
    public async Task Un_navegador_vencido_se_borra_y_si_era_el_unico_el_aviso_se_anula()
    {
        var u = await UsuarioAsync();
        var s = await NavegadorAsync(u.Id);
        await EncolarAsync(Aviso(u.Id, programada: _reloj.Ahora.AddMinutes(-1)));

        await ProcesarAsync(new EnviadorFalso { Responder = _ => ResultadoPush.Vencida });

        using var lee = ContextoDePrueba.Crear();
        Assert.False(await lee.SuscripcionesPush.AnyAsync(x => x.Id == s.Id));
        Assert.Equal(EstadoEnvio.Anulada, (await PushDeAsync(u.Id)).Estado);
    }

    [Fact]
    public async Task Si_llega_a_un_navegador_no_se_reintenta_para_el_que_fallo()
    {
        var u = await UsuarioAsync();
        var bueno = await NavegadorAsync(u.Id);
        await NavegadorAsync(u.Id);
        await EncolarAsync(Aviso(u.Id, programada: _reloj.Ahora.AddMinutes(-1)));

        await ProcesarAsync(new EnviadorFalso
        {
            Responder = d => d.Endpoint == bueno.Endpoint ? ResultadoPush.Entregado : ResultadoPush.Reintentable,
        });

        Assert.Equal(EstadoEnvio.Enviada, (await PushDeAsync(u.Id)).Estado);
    }

    [Fact]
    public async Task Con_el_servicio_caido_se_reintenta_mas_tarde_y_al_tope_falla()
    {
        var u = await UsuarioAsync();
        await NavegadorAsync(u.Id);
        await EncolarAsync(Aviso(u.Id, programada: _reloj.Ahora.AddMinutes(-1)));
        var caido = new EnviadorFalso { Responder = _ => ResultadoPush.Reintentable };

        await ProcesarAsync(caido);
        var tras1 = await PushDeAsync(u.Id);
        Assert.Equal(EstadoEnvio.Programada, tras1.Estado);
        Assert.True(tras1.ProgramadaPara > _reloj.Ahora);

        for (var i = 0; i < Activas.MaximoIntentos; i++)
        {
            _reloj.Avanzar(TimeSpan.FromHours(1));
            await ProcesarAsync(caido);
        }
        Assert.Equal(EstadoEnvio.Fallida, (await PushDeAsync(u.Id)).Estado);
        Assert.Equal(Activas.MaximoIntentos, caido.Enviados.Count);
    }

    [Fact]
    public async Task Si_la_persona_dio_de_baja_sus_navegadores_el_push_pendiente_se_anula_sin_intentar()
    {
        var u = await UsuarioAsync();
        var s = await NavegadorAsync(u.Id);
        await EncolarAsync(Aviso(u.Id, programada: _reloj.Ahora.AddMinutes(-1)));
        using (var ctx = ContextoDePrueba.Crear())
        {
            await ctx.SuscripcionesPush.Where(x => x.Id == s.Id).ExecuteDeleteAsync();
        }
        var enviador = new EnviadorFalso();

        await ProcesarAsync(enviador);

        Assert.Empty(enviador.Enviados);
        var push = await PushDeAsync(u.Id);
        Assert.Equal(EstadoEnvio.Anulada, push.Estado);
        Assert.Equal(0, push.Intentos);   // lo anulo el barrido, sin reservarlo ni gastar un intento
    }

    [Fact]
    public async Task Un_push_que_espero_demasiado_se_anula_en_vez_de_salir_tarde()
    {
        var u = await UsuarioAsync();
        await NavegadorAsync(u.Id);
        await EncolarAsync(Aviso(u.Id, programada: _reloj.Ahora.AddHours(-(Activas.CaducaHoras + 1))));
        var enviador = new EnviadorFalso();

        await ProcesarAsync(enviador);

        Assert.Empty(enviador.Enviados);
        var push = await PushDeAsync(u.Id);
        Assert.Equal(EstadoEnvio.Anulada, push.Estado);
        Assert.Equal(0, push.Intentos);
    }

    [Fact]
    public async Task Sin_claves_VAPID_no_toma_nada()
    {
        var u = await UsuarioAsync();
        await NavegadorAsync(u.Id);
        await EncolarAsync(Aviso(u.Id, programada: _reloj.Ahora.AddMinutes(-1)));
        var enviador = new EnviadorFalso();

        Assert.Equal(0, await ProcesarAsync(enviador, new OpcionesPush()));

        Assert.Empty(enviador.Enviados);
        Assert.Equal(EstadoEnvio.Programada, (await PushDeAsync(u.Id)).Estado);
    }

    // ------------------------------------------------------------------ el enviador de verdad

    private static OpcionesPush ClavesVapidDePrueba()
    {
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var p = ecdsa.ExportParameters(true);
        var publica = new byte[65];
        publica[0] = 0x04;
        p.Q.X!.CopyTo(publica, 1);
        p.Q.Y!.CopyTo(publica, 33);
        return new OpcionesPush
        {
            ClavePublica = B64Url(publica), ClavePrivada = B64Url(p.D!), Sujeto = "mailto:soporte@zass.cl",
        };
    }

    [Fact]
    public async Task El_enviador_cifra_el_aviso_y_firma_con_VAPID()
    {
        var o = ClavesVapidDePrueba();
        var manejador = new ServicioDePushFalso(HttpStatusCode.Created);
        using var enviador = new EnviadorWebPush(new HttpClient(manejador), Options.Create(o));
        var (p256, auth) = ClavesDeNavegador();
        var destino = new SuscripcionPush { Endpoint = Endpoint(), ClaveP256dh = p256, ClaveAuth = auth };
        const string json = "{\"titulo\":\"Tu plan venció\"}";

        var r = await enviador.EnviarAsync(destino, new MensajeSaliente(json, TimeSpan.FromHours(2), Urgente: true), default);

        Assert.Equal(ResultadoPush.Entregado, r);
        Assert.Equal(1, manejador.Cuenta);
        Assert.Equal(destino.Endpoint, manejador.Url);
        Assert.StartsWith("vapid ", manejador.Cabeceras["Authorization"], StringComparison.OrdinalIgnoreCase);
        Assert.Contains("k=" + o.ClavePublica, manejador.Cabeceras["Authorization"]);
        Assert.Equal("aes128gcm", manejador.Cabeceras["Content-Encoding"]);
        Assert.Equal("7200", manejador.Cabeceras["TTL"]);
        Assert.Equal("high", manejador.Cabeceras["Urgency"], ignoreCase: true);
        // Cifrado de verdad: el texto en claro no viaja.
        Assert.DoesNotContain("Tu plan", System.Text.Encoding.UTF8.GetString(manejador.Cuerpo));
        Assert.True(manejador.Cuerpo.Length > json.Length);
    }

    [Theory]
    [InlineData(HttpStatusCode.Gone, ResultadoPush.Vencida)]
    [InlineData(HttpStatusCode.NotFound, ResultadoPush.Vencida)]
    [InlineData(HttpStatusCode.TooManyRequests, ResultadoPush.Reintentable)]
    [InlineData(HttpStatusCode.ServiceUnavailable, ResultadoPush.Reintentable)]
    [InlineData(HttpStatusCode.BadRequest, ResultadoPush.Rechazado)]
    [InlineData(HttpStatusCode.Forbidden, ResultadoPush.Rechazado)]
    public async Task El_enviador_clasifica_la_respuesta_del_servicio_de_push(HttpStatusCode estado, ResultadoPush esperado)
    {
        var manejador = new ServicioDePushFalso(estado);
        using var enviador = new EnviadorWebPush(new HttpClient(manejador), Options.Create(ClavesVapidDePrueba()));
        var (p256, auth) = ClavesDeNavegador();

        var r = await enviador.EnviarAsync(
            new SuscripcionPush { Endpoint = Endpoint(), ClaveP256dh = p256, ClaveAuth = auth },
            new MensajeSaliente("{}", TimeSpan.FromDays(1), Urgente: false), default);

        Assert.Equal(esperado, r);
    }

    /// <summary>Un servicio de push falso: guarda la peticion entera, cabeceras del contenido incluidas.</summary>
    private sealed class ServicioDePushFalso(HttpStatusCode respuesta) : HttpMessageHandler
    {
        public int Cuenta { get; private set; }
        public string Url { get; private set; } = "";
        public Dictionary<string, string> Cabeceras { get; } = new(StringComparer.OrdinalIgnoreCase);
        public byte[] Cuerpo { get; private set; } = Array.Empty<byte>();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage req, CancellationToken ct)
        {
            Cuenta++;
            Url = req.RequestUri!.ToString();
            foreach (var h in req.Headers) Cabeceras[h.Key] = string.Join(",", h.Value);
            if (req.Content is not null)
            {
                foreach (var h in req.Content.Headers) Cabeceras[h.Key] = string.Join(",", h.Value);
                Cuerpo = await req.Content.ReadAsByteArrayAsync(ct);
            }
            return new HttpResponseMessage(respuesta);
        }
    }
}
