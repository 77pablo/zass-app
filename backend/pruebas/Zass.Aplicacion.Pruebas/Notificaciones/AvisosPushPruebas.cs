using System.Security.Cryptography;
using Zass.Aplicacion.Notificaciones;
using Zass.Aplicacion.Pruebas.Dobles;
using Zass.Aplicacion.Puertos;
using Zass.Dominio.Comun;

namespace Zass.Aplicacion.Pruebas.Notificaciones;

/// <summary>ES-3. Las reglas del canal push y API-45, sin base de datos.</summary>
public class AvisosPushPruebas
{
    // ------------------------------------------------------------------ que tipos van por push

    [Theory]
    [InlineData(TipoNotificacion.CorreoVerificacion)]
    [InlineData(TipoNotificacion.RecuperarContrasena)]
    public void Los_avisos_con_token_no_van_por_push(TipoNotificacion tipo) =>
        Assert.False(AvisosPush.AdmitePush(tipo));

    [Fact]
    public void Todos_los_demas_tipos_van_por_push()
    {
        // Decision del 21-nov: citas, resenas, credencial, ficha, suscripcion y cobro.
        // CredencialVencida (22-nov, CS-06) va por push como CredencialResuelta: es un cambio de
        // estado de su credencial (CS-07: «por su canal preferido»), sin token ni nada que recuperar. Un tipo
        // nuevo que se agregue sin decidir su canal deja esta prueba roja.
        var sinPush = Enum.GetValues<TipoNotificacion>().Where(t => !AvisosPush.AdmitePush(t));
        // RegistroConRutExistente (#197, 22-nov) tampoco va por push: es un aviso de seguridad con
        // el enlace para recuperar la cuenta, y la pantalla del registro promete que llega «al
        // correo con que se creó esa cuenta». Lo que toca recuperar una cuenta no se lee en la
        // pantalla bloqueada, igual que los dos con token.
        Assert.Equal(new[]
        {
            TipoNotificacion.CorreoVerificacion, TipoNotificacion.RecuperarContrasena,
            TipoNotificacion.RegistroConRutExistente,
        }, sinPush);
    }

    [Fact]
    public void La_gemela_copia_el_aviso_con_canal_push_y_la_clave_con_sufijo()
    {
        var correo = new Notificacion
        {
            DestinatarioId = Guid.NewGuid(), CitaId = Guid.NewGuid(), Tipo = TipoNotificacion.Recordatorio2h,
            ClaveIdempotencia = "recordatorio:abc", Cuerpo = "{\"rol\":\"buscador\"}",
            ProgramadaPara = new DateTime(2026, 11, 22, 10, 0, 0, DateTimeKind.Utc),
        };

        var push = AvisosPush.Gemela(correo);

        Assert.NotEqual(correo.Id, push.Id);
        Assert.Equal(CanalNotificacion.Push, push.Canal);
        Assert.Equal("recordatorio:abc:push", push.ClaveIdempotencia);
        Assert.Equal((correo.DestinatarioId, correo.CitaId, correo.Tipo, correo.Cuerpo, correo.ProgramadaPara),
            (push.DestinatarioId, push.CitaId, push.Tipo, push.Cuerpo, push.ProgramadaPara));
        Assert.Equal(EstadoEnvio.Programada, push.Estado);
    }

    [Fact]
    public void Solo_lleva_gemela_un_correo_programado_de_un_tipo_con_push()
    {
        Notificacion A(Action<Notificacion> ajuste)
        {
            var n = new Notificacion { Tipo = TipoNotificacion.CitaConfirmada, ClaveIdempotencia = "k" };
            ajuste(n);
            return n;
        }

        Assert.True(AvisosPush.LlevaGemela(A(_ => { })));
        Assert.False(AvisosPush.LlevaGemela(A(n => n.Canal = CanalNotificacion.Push)));      // ya es push
        Assert.False(AvisosPush.LlevaGemela(A(n => n.Tipo = TipoNotificacion.RecuperarContrasena)));
        Assert.False(AvisosPush.LlevaGemela(A(n => n.Estado = EstadoEnvio.Anulada)));
        Assert.False(AvisosPush.LlevaGemela(A(n => n.ClaveIdempotencia = "")));
    }

    // ------------------------------------------------------------------ endpoints aceptados (SSRF)

    [Theory]
    [InlineData("https://fcm.googleapis.com/fcm/send/abc123")]
    [InlineData("https://updates.push.services.mozilla.com/wpush/v2/abc")]
    [InlineData("https://wns2-bl2p.notify.windows.com/w/?token=abc")]
    [InlineData("https://web.push.apple.com/QGx")]
    [InlineData("https://api.push.apple.com/3/device/abc")]
    public void Acepta_los_servicios_de_push_de_los_navegadores(string endpoint) =>
        Assert.True(AvisosPush.EndpointPermitido(endpoint));

    [Theory]
    [InlineData("http://fcm.googleapis.com/fcm/send/abc")]
    [InlineData("https://fcm.googleapis.com:8443/fcm/send/abc")]
    [InlineData("https://fcm.googleapis.com.atacante.cl/x")]
    [InlineData("https://atacantepush.services.mozilla.com/x")]
    [InlineData("https://usuario:clave@fcm.googleapis.com/x")]
    [InlineData("https://127.0.0.1/x")]
    [InlineData("https://localhost/x")]
    [InlineData("https://169.254.169.254/latest/meta-data")]
    [InlineData("ftp://fcm.googleapis.com/x")]
    [InlineData("/fcm/send/abc")]
    [InlineData("")]
    [InlineData(null)]
    public void Rechaza_cualquier_otra_direccion(string? endpoint) =>
        Assert.False(AvisosPush.EndpointPermitido(endpoint));

    // ------------------------------------------------------------------ validador de API-45

    private static string B64Url(byte[] b) => Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static RegistrarPushCmd Valido()
    {
        using var ecdh = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        var q = ecdh.ExportParameters(false).Q;
        var publica = new byte[65];
        publica[0] = 0x04;
        q.X!.CopyTo(publica, 1);
        q.Y!.CopyTo(publica, 33);
        return new RegistrarPushCmd("https://fcm.googleapis.com/fcm/send/abc", B64Url(publica),
            B64Url(RandomNumberGenerator.GetBytes(16)));
    }

    [Fact]
    public void El_validador_acepta_lo_que_entrega_un_navegador() =>
        Assert.True(new ValidadorRegistrarPush().Validate(Valido()).IsValid);

    [Fact]
    public void El_validador_rechaza_un_endpoint_no_permitido() =>
        Assert.False(new ValidadorRegistrarPush().Validate(Valido() with { Endpoint = "https://zass.cl/x" }).IsValid);

    [Fact]
    public void El_validador_exige_la_clave_P256_de_65_bytes_que_empieza_en_04()
    {
        var v = new ValidadorRegistrarPush();
        var bien = Valido();
        var bytes = Convert.FromBase64String(bien.P256dh!.Replace('-', '+').Replace('_', '/') + "=");
        bytes[0] = 0x02;   // comprimida: no sirve para RFC 8291

        Assert.False(v.Validate(bien with { P256dh = B64Url(bytes) }).IsValid);
        Assert.False(v.Validate(bien with { P256dh = B64Url(bytes[..64]) }).IsValid);
        Assert.False(v.Validate(bien with { P256dh = "no es base64 !!" }).IsValid);
    }

    [Fact]
    public void El_validador_exige_el_secreto_auth_de_16_bytes()
    {
        var v = new ValidadorRegistrarPush();
        Assert.False(v.Validate(Valido() with { Auth = B64Url(new byte[15]) }).IsValid);
        Assert.False(v.Validate(Valido() with { Auth = B64Url(new byte[17]) }).IsValid);
        Assert.False(v.Validate(Valido() with { Auth = null }).IsValid);
    }

    // ------------------------------------------------------------------ el servicio

    private sealed class RepoFalso : IRepositorioPush
    {
        public List<string> Llamadas { get; } = new();
        public bool HabiaAlgo { get; set; }

        public Task RegistrarAsync(Guid u, string e, string p, string a, DateTime ahora, CancellationToken ct = default)
        {
            Llamadas.Add($"registrar {e}");
            return Task.CompletedTask;
        }

        public Task<bool> BorrarAsync(Guid u, string e, CancellationToken ct = default)
        {
            Llamadas.Add($"borrar {e}");
            return Task.FromResult(HabiaAlgo);
        }

        public Task<int> EncolarGemelasPendientesAsync(Guid u, CancellationToken ct = default)
        {
            Llamadas.Add("gemelas");
            return Task.FromResult(0);
        }
    }

    [Fact]
    public async Task Registrar_guarda_y_despues_recoge_lo_que_esperaba_en_la_cola()
    {
        var repo = new RepoFalso();
        var servicio = new ServicioAvisosPush(repo, new UnidadQueAnota(repo.Llamadas), new RelojFijo());
        var cmd = Valido() with { Endpoint = "  https://fcm.googleapis.com/fcm/send/abc  " };

        var r = await servicio.RegistrarAsync(Guid.NewGuid(), cmd);

        Assert.True(r.Exito);
        Assert.Equal(new[] { "registrar https://fcm.googleapis.com/fcm/send/abc", "guardar", "gemelas" }, repo.Llamadas);
    }

    [Fact]
    public async Task Dar_de_baja_responde_bien_aunque_no_hubiera_nada_y_entonces_no_guarda()
    {
        var repo = new RepoFalso { HabiaAlgo = false };
        var servicio = new ServicioAvisosPush(repo, new UnidadQueAnota(repo.Llamadas), new RelojFijo());

        var r = await servicio.DarDeBajaAsync(Guid.NewGuid(), new BajaPushCmd("https://fcm.googleapis.com/x"));

        Assert.True(r.Exito);
        Assert.DoesNotContain("guardar", repo.Llamadas);
    }
}
