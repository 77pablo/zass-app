using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Zass.Dominio.Comun;
using Zass.Dominio.Suscripciones;
using Zass.Dominio.Tarjetas;

namespace Zass.Integracion.Pruebas;

/// <summary>
/// API-56 a API-58 por HTTP y contra <c>zass_pruebas</c>: la cadena entera (ruta, sesion, CSRF,
/// servicio y SQL). Dos prestadores de verdad, A y B, cada uno con su cliente y su cookie.
///
/// Lo que solo aqui se ve: que la consulta de API-56 filtra por dueno, que una tarjeta ajena
/// da el MISMO 404 que una inexistente, que el JSON no lleva nada de la activacion, que los
/// bordes de 7 y 30 dias los respeta el SQL real, y que una tarjeta dada de baja deja de
/// abrir la ficha en API-54.
/// </summary>
public class ApiMisTarjetasPruebas : IAsyncLifetime
{
    private readonly Aplicacion _app = new();
    private string _marca = "";

    private Cuenta _a = null!;
    private Cuenta _b = null!;

    private Tarjeta _deA = null!;
    private Tarjeta _deAMuerta = null!;
    private Tarjeta _deB = null!;

    private sealed class Cuenta
    {
        public required HttpClient Http { get; init; }
        public required string Correo { get; init; }
        public Guid UsuarioId { get; set; }
        public Guid PerfilId { get; set; }

        public async Task<string> CsrfAsync() =>
            (await (await Http.GetAsync("/api/auth/csrf")).Content.ReadFromJsonAsync<JsonDocument>())!
                .RootElement.GetProperty("token").GetString()!;

        public async Task<HttpResponseMessage> DesactivarAsync(Guid tarjetaId, bool conCsrf = true)
        {
            var p = new HttpRequestMessage(HttpMethod.Post, $"/api/mi/tarjetas/{tarjetaId}/desactivar");
            if (conCsrf) p.Headers.Add("X-CSRF-Token", await CsrfAsync());
            return await Http.SendAsync(p);
        }
    }

    public async Task InitializeAsync()
    {
        _marca = Guid.NewGuid().ToString("N")[..12];
        _a = await UnPrestadorConSesionAsync("a");
        _b = await UnPrestadorConSesionAsync("b");

        using var ctx = ContextoDePrueba.Crear();
        _deA = NuevaTarjeta(ctx, _a, EstadoTarjeta.Activa);
        _deAMuerta = NuevaTarjeta(ctx, _a, EstadoTarjeta.Desactivada);
        _deB = NuevaTarjeta(ctx, _b, EstadoTarjeta.Activa);
        await ctx.SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        using (var ctx = ContextoDePrueba.Crear())
        {
            // Los escaneos se van en cascada con su tarjeta.
            await ctx.Database.ExecuteSqlRawAsync(
                """
                DELETE FROM tarjetas           WHERE perfil_prestador_id IN ({0}, {1});
                DELETE FROM suscripciones      WHERE perfil_prestador_id IN ({0}, {1});
                DELETE FROM perfiles_prestador WHERE id IN ({0}, {1});
                """,
                _a.PerfilId, _b.PerfilId);

            var usuarios = await ctx.Usuarios
                .Where(u => u.Correo.EndsWith($"-{_marca}@zass.local")).ToListAsync();
            foreach (var u in usuarios)
            {
                ctx.Auditoria.RemoveRange(
                    ctx.Auditoria.Where(a => a.ActorId == u.Id || a.EntidadId == u.Id));
                ctx.Notificaciones.RemoveRange(
                    ctx.Notificaciones.Where(n => n.DestinatarioId == u.Id));
                ctx.Sesiones.RemoveRange(ctx.Sesiones.Where(s => s.UsuarioId == u.Id));
                ctx.PerfilesBuscador.RemoveRange(
                    ctx.PerfilesBuscador.Where(p => p.UsuarioId == u.Id));
            }
            ctx.Usuarios.RemoveRange(usuarios);
            await ctx.SaveChangesAsync();
        }

        _a?.Http.Dispose();
        _b?.Http.Dispose();
        _app.Dispose();
    }

    // ------------------------------------------------------------- el montaje

    private async Task<Cuenta> UnPrestadorConSesionAsync(string sufijo)
    {
        var http = _app.CreateClient(new WebApplicationFactoryClientOptions
        {
            HandleCookies = true, AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost"),
        });
        var cuenta = new Cuenta { Http = http, Correo = $"mistj-{sufijo}-{_marca}@zass.local" };

        Assert.Equal(HttpStatusCode.Created, (await http.PostAsJsonAsync("/api/auth/registro", new
        {
            correo = cuenta.Correo, contrasena = "contrasena-larga-1",
            nombre = "Pablo Espinoza", rol = "prestador",
        })).StatusCode);

        using (var ctx = ContextoDePrueba.Crear())
        {
            var usuario = await ctx.Usuarios.SingleAsync(u => u.Correo == cuenta.Correo);
            cuenta.UsuarioId = usuario.Id;
            var aviso = await ctx.Notificaciones
                .Where(n => n.DestinatarioId == usuario.Id
                            && n.Tipo == TipoNotificacion.CorreoVerificacion)
                .OrderByDescending(n => n.ProgramadaPara).FirstAsync();
            var token = Zass.Aplicacion.Puertos.CargaDeAviso.LeerToken(aviso.Cuerpo)!;
            Assert.Equal(HttpStatusCode.NoContent,
                (await http.PostAsJsonAsync("/api/auth/correo/verificar", new { token })).StatusCode);

            // Publicado: asi API-54 abriria su ficha mientras la tarjeta este activa.
            var perfil = await ctx.PerfilesPrestador.SingleAsync(p => p.UsuarioId == usuario.Id);
            var rubro = await ctx.Rubros.FirstAsync(r => r.Activo);
            perfil.RubroId = rubro.Id;
            perfil.Slug = $"mistj-{sufijo}-{_marca}";
            perfil.NombrePublico = $"Tarjetas {sufijo.ToUpperInvariant()} {_marca}";
            perfil.Estado = EstadoPerfil.Publicado;
            perfil.PublicadoEn = DateTime.UtcNow.AddDays(-2);
            await ctx.SaveChangesAsync();
            cuenta.PerfilId = perfil.Id;
        }

        Assert.Equal(HttpStatusCode.OK, (await http.PostAsJsonAsync("/api/auth/sesion", new
        {
            correo = cuenta.Correo, contrasena = "contrasena-larga-1",
        })).StatusCode);

        return cuenta;
    }

    private static Tarjeta NuevaTarjeta(Infraestructura.Datos.ZassDbContext ctx, Cuenta dueno, EstadoTarjeta estado)
    {
        var t = new Tarjeta
        {
            UrlEstable = Tarjeta.GenerarUrlEstable(), Estado = estado, PerfilPrestadorId = dueno.PerfilId,
            ActivadaEn = DateTime.UtcNow.AddDays(-60),
            DesactivadaEn = estado == EstadoTarjeta.Desactivada ? DateTime.UtcNow.AddDays(-1) : null,
        };
        ctx.Tarjetas.Add(t);
        return t;
    }

    private static async Task<EstadoTarjeta> EstadoEnLaBaseAsync(Guid id)
    {
        using var ctx = ContextoDePrueba.Crear();
        return (await ctx.Tarjetas.AsNoTracking().SingleAsync(t => t.Id == id)).Estado;
    }

    private static async Task<(int estado, string? titulo, string? detalle)> ProblemaAsync(HttpResponseMessage r)
    {
        var json = await r.Content.ReadFromJsonAsync<JsonDocument>();
        string? Leer(string p) => json!.RootElement.TryGetProperty(p, out var v) ? v.GetString() : null;
        return ((int)r.StatusCode, Leer("title"), Leer("detail"));
    }

    // ------------------------------------------------------------- sesion y CSRF

    [Fact]
    public async Task Sin_sesion_los_tres_responden_401()
    {
        using var anonimo = _app.CreateClient(new WebApplicationFactoryClientOptions
        {
            HandleCookies = false, AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost"),
        });

        Assert.Equal(HttpStatusCode.Unauthorized, (await anonimo.GetAsync("/api/mi/tarjetas")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await anonimo.GetAsync($"/api/mi/tarjetas/{_deA.Id}/estadisticas")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await anonimo.PostAsync($"/api/mi/tarjetas/{_deA.Id}/desactivar", null)).StatusCode);
        Assert.Equal(EstadoTarjeta.Activa, await EstadoEnLaBaseAsync(_deA.Id));
    }

    [Fact]
    public async Task Desactivar_sin_token_CSRF_se_rechaza_y_la_tarjeta_sigue_activa()
    {
        var r = await _a.DesactivarAsync(_deA.Id, conCsrf: false);

        Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
        Assert.Equal(EstadoTarjeta.Activa, await EstadoEnLaBaseAsync(_deA.Id));
    }

    // ------------------------------------------------------------- API-56

    [Fact]
    public async Task Listar_trae_solo_las_mias_y_nada_del_codigo_de_activacion()
    {
        var r = await _a.Http.GetAsync("/api/mi/tarjetas");

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var crudo = await r.Content.ReadAsStringAsync();
        var lista = JsonDocument.Parse(crudo).RootElement.EnumerateArray().ToList();

        Assert.Equal(
            new[] { _deA.Id, _deAMuerta.Id }.OrderBy(x => x),
            lista.Select(t => t.GetProperty("id").GetGuid()).OrderBy(x => x));
        Assert.Equal(_deA.UrlEstable,
            lista.Single(t => t.GetProperty("id").GetGuid() == _deA.Id).GetProperty("urlEstable").GetString());
        Assert.DoesNotContain(_deB.UrlEstable, crudo);
        foreach (var prohibido in new[] { "hash", "codigo", "intentos", "bloqueada", "sucesora", "vence" })
            Assert.DoesNotContain(prohibido, crudo, StringComparison.OrdinalIgnoreCase);
    }

    // ------------------------------------------------------------- API-57

    [Fact]
    public async Task Desactivar_una_ajena_da_el_mismo_404_que_una_inexistente_y_no_la_toca()
    {
        var contraAjena = await _b.DesactivarAsync(_deA.Id);
        var contraNada = await _b.DesactivarAsync(Guid.NewGuid());

        Assert.Equal(HttpStatusCode.NotFound, contraAjena.StatusCode);
        Assert.Equal(await ProblemaAsync(contraNada), await ProblemaAsync(contraAjena));
        Assert.Equal(EstadoTarjeta.Activa, await EstadoEnLaBaseAsync(_deA.Id));
    }

    [Fact]
    public async Task Desactivar_la_propia_responde_204_y_la_tarjeta_deja_de_abrir_la_ficha()
    {
        Assert.Equal(HttpStatusCode.Found, (await _b.Http.GetAsync($"/t/{_deB.UrlEstable}")).StatusCode);

        var r = await _b.DesactivarAsync(_deB.Id);

        Assert.Equal(HttpStatusCode.NoContent, r.StatusCode);
        Assert.Equal(EstadoTarjeta.Desactivada, await EstadoEnLaBaseAsync(_deB.Id));
        var escaneo = await _b.Http.GetAsync($"/t/{_deB.UrlEstable}");
        Assert.Equal(HttpStatusCode.Found, escaneo.StatusCode);
        Assert.Equal("/tarjeta/no-valida", escaneo.Headers.Location!.OriginalString);
    }

    /// <summary>D4: perder la tarjeta no puede quedar sin salida porque el plan vencio.</summary>
    [Fact]
    public async Task Con_la_suscripcion_vencida_igual_se_lista_y_se_desactiva()
    {
        using (var ctx = ContextoDePrueba.Crear())
        {
            var plan = await ctx.Planes.FirstAsync();
            ctx.Suscripciones.Add(new Suscripcion
            {
                PerfilPrestadorId = _a.PerfilId, PlanId = plan.Id, Estado = EstadoSuscripcion.Vencida,
                IniciaEn = DateTime.UtcNow.AddDays(-60), VigenteHasta = DateTime.UtcNow.AddDays(-30),
            });
            await ctx.SaveChangesAsync();
        }

        Assert.Equal(HttpStatusCode.OK, (await _a.Http.GetAsync("/api/mi/tarjetas")).StatusCode);
        Assert.Equal(HttpStatusCode.OK,
            (await _a.Http.GetAsync($"/api/mi/tarjetas/{_deA.Id}/estadisticas")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await _a.DesactivarAsync(_deA.Id)).StatusCode);
        Assert.Equal(EstadoTarjeta.Desactivada, await EstadoEnLaBaseAsync(_deA.Id));
    }

    // ------------------------------------------------------------- API-58

    [Fact]
    public async Task Las_estadisticas_las_cuenta_el_SQL_con_los_bordes_de_7_y_30_dias()
    {
        // Margen de un minuto a cada lado del borde: el reloj del servidor corre mientras la
        // prueba siembra, y lo que se mide es de que lado cae, no el milisegundo.
        var ahora = DateTime.UtcNow;
        using (var ctx = ContextoDePrueba.Crear())
        {
            void Escaneo(MedioEscaneo m, DateTime cuando) =>
                ctx.Escaneos.Add(new EscaneoTarjeta { TarjetaId = _deA.Id, Medio = m, OcurridoEn = cuando });

            // Asimetrico a proposito: con cifras iguales por medio, intercambiar NFC y QR no se ve.
            Escaneo(MedioEscaneo.Nfc, ahora.AddMinutes(-5));                   // 7, 30, total
            Escaneo(MedioEscaneo.Nfc, ahora.AddMinutes(-10));                  // 7, 30, total
            Escaneo(MedioEscaneo.Nfc, ahora.AddDays(-2));                      // 7, 30, total
            Escaneo(MedioEscaneo.Qr, ahora.AddDays(-7).AddMinutes(1));         // 7, 30, total
            Escaneo(MedioEscaneo.Qr, ahora.AddDays(-7).AddMinutes(-1));        // 30, total
            Escaneo(MedioEscaneo.Nfc, ahora.AddDays(-30).AddMinutes(1));       // 30, total
            Escaneo(MedioEscaneo.Nfc, ahora.AddDays(-30).AddMinutes(-1));      // total
            Escaneo(MedioEscaneo.Qr, ahora.AddDays(-200));                     // total
            // De otra tarjeta: no cuenta.
            ctx.Escaneos.Add(new EscaneoTarjeta { TarjetaId = _deB.Id, Medio = MedioEscaneo.Nfc, OcurridoEn = ahora });
            await ctx.SaveChangesAsync();
        }

        var r = await _a.Http.GetAsync($"/api/mi/tarjetas/{_deA.Id}/estadisticas");

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var e = (await r.Content.ReadFromJsonAsync<JsonDocument>())!.RootElement;
        (int nfc, int qr) Par(string periodo) =>
            (e.GetProperty(periodo).GetProperty("nfc").GetInt32(), e.GetProperty(periodo).GetProperty("qr").GetInt32());

        Assert.Equal((3, 1), Par("ultimos7Dias"));
        Assert.Equal((4, 2), Par("ultimos30Dias"));
        Assert.Equal((5, 3), Par("total"));
        Assert.Equal(8, e.GetProperty("total").GetProperty("total").GetInt32());
    }

    /// <summary>
    /// El borde EXACTO solo se puede fijar sin el reloj de por medio: aqui se llama al
    /// repositorio con los limites puestos a mano, contra PostgreSQL. Un escaneo justo en el
    /// limite entra; uno un segundo antes, no.
    /// </summary>
    [Fact]
    public async Task El_SQL_cuenta_el_borde_exacto_de_cada_periodo_dentro()
    {
        var desde7 = new DateTime(2026, 11, 7, 12, 0, 0, DateTimeKind.Utc);
        var desde30 = new DateTime(2026, 10, 15, 12, 0, 0, DateTimeKind.Utc);
        using (var ctx = ContextoDePrueba.Crear())
        {
            void Escaneo(MedioEscaneo m, DateTime cuando) =>
                ctx.Escaneos.Add(new EscaneoTarjeta { TarjetaId = _deAMuerta.Id, Medio = m, OcurridoEn = cuando });

            Escaneo(MedioEscaneo.Qr, desde7);                     // 7, 30, total
            Escaneo(MedioEscaneo.Qr, desde7.AddSeconds(-1));      // 30, total
            Escaneo(MedioEscaneo.Nfc, desde30);                   // 30, total
            Escaneo(MedioEscaneo.Nfc, desde30.AddSeconds(-1));    // total
            await ctx.SaveChangesAsync();
        }

        using var lectura = ContextoDePrueba.Crear();
        var c = await new Infraestructura.Datos.Repositorios.RepositorioTarjetasEfCore(lectura)
            .EscaneosPorMedioAsync(_deAMuerta.Id, desde7, desde30);

        Assert.Equal(new Zass.Aplicacion.Puertos.EscaneosContados(
            Nfc7: 0, Qr7: 1, Nfc30: 1, Qr30: 2, NfcTotal: 2, QrTotal: 2), c);
    }

    [Fact]
    public async Task Las_estadisticas_de_una_ajena_dan_el_mismo_404_que_una_inexistente()
    {
        var contraAjena = await _b.Http.GetAsync($"/api/mi/tarjetas/{_deA.Id}/estadisticas");
        var contraNada = await _b.Http.GetAsync($"/api/mi/tarjetas/{Guid.NewGuid()}/estadisticas");

        Assert.Equal(HttpStatusCode.NotFound, contraAjena.StatusCode);
        Assert.Equal(await ProblemaAsync(contraNada), await ProblemaAsync(contraAjena));
    }
}
