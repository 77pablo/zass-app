using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Zass.Dominio.Comun;
using Zass.Dominio.Identidad;
using Zass.Dominio.Reputacion;
using Zass.Dominio.Tarjetas;
using Zass.Infraestructura.Datos;
using Zass.Infraestructura.Datos.Repositorios;

namespace Zass.Integracion.Pruebas;

/// <summary>
/// JQ-8 · MI-METRICAS. Contactos y escaneos de UN prestador, con datos armados a mano dentro de una
/// transacción que se deshace. Las cifras se filtran por perfil, así que filas de otras pruebas no
/// cambian el resultado; igual se usa un «ahora» fijo para que los bordes de 7 y 30 días no dependan
/// del día en que corre.
/// </summary>
public class MetricasPrestadorIntegracionPruebas
{
    private static readonly DateTime Ahora = new(2100, 3, 1, 12, 0, 0, DateTimeKind.Utc);
    private static DateTime Hace(double dias) => Ahora.AddDays(-dias);

    [Fact]
    public async Task Cuenta_solo_lo_propio_en_cada_ventana()
    {
        using var prueba = new ContextoDePrueba();
        var ctx = prueba.Ctx;
        var buscador = await UnUsuarioAsync(ctx, "busc");
        var mio = await UnPerfilAsync(ctx);
        var ajeno = await UnPerfilAsync(ctx);

        // Contactos míos: hace 1 día (WhatsApp), 10 días (teléfono) y 40 días (correo) → 1 / 2 / 3.
        UnContacto(ctx, buscador.Id, mio.Id, CanalContacto.Whatsapp, Hace(1));
        UnContacto(ctx, buscador.Id, mio.Id, CanalContacto.Telefono, Hace(10));
        UnContacto(ctx, buscador.Id, mio.Id, CanalContacto.Correo, Hace(40));
        // Del otro prestador: no cuenta.
        UnContacto(ctx, buscador.Id, ajeno.Id, CanalContacto.Whatsapp, Hace(1));

        // Escaneos de mis DOS tarjetas: hace 2, 20 y 50 días → 1 / 2 / 3. Uno de una ajena, fuera.
        var t1 = UnaTarjeta(ctx, mio.Id);
        var t2 = UnaTarjeta(ctx, mio.Id);
        var tAjena = UnaTarjeta(ctx, ajeno.Id);
        UnEscaneo(ctx, t1.Id, Hace(2));
        UnEscaneo(ctx, t2.Id, Hace(20));
        UnEscaneo(ctx, t1.Id, Hace(50));
        UnEscaneo(ctx, tAjena.Id, Hace(1));

        await ctx.SaveChangesAsync();
        var m = await new ConsultaMetricasPrestadorEfCore(ctx).CalcularAsync(mio.Id, Ahora);

        Assert.Equal(new(1, 2, 3), m.Contactos);
        // Por canal, solo los 30 días: WhatsApp 1 y teléfono 1; el correo de hace 40 días no.
        Assert.Equal(new(1, 1, 0, 0), m.ContactosPorCanal30Dias);
        Assert.Equal(new(1, 2, 3), m.Escaneos);
        Assert.Equal(Ahora, m.CalculadoEn);
    }

    [Fact]
    public async Task Lo_que_ocurrio_despues_de_ahora_no_cuenta()
    {
        using var prueba = new ContextoDePrueba();
        var ctx = prueba.Ctx;
        var buscador = await UnUsuarioAsync(ctx, "busc");
        var mio = await UnPerfilAsync(ctx);
        UnContacto(ctx, buscador.Id, mio.Id, CanalContacto.Whatsapp, Ahora.AddHours(1));
        UnEscaneo(ctx, UnaTarjeta(ctx, mio.Id).Id, Ahora.AddHours(1));
        await ctx.SaveChangesAsync();

        var m = await new ConsultaMetricasPrestadorEfCore(ctx).CalcularAsync(mio.Id, Ahora);
        Assert.Equal(new(0, 0, 0), m.Contactos);
        Assert.Equal(new(0, 0, 0), m.Escaneos);
    }

    // ------------------------------------------------------------- datos

    private static async Task<Usuario> UnUsuarioAsync(ZassDbContext ctx, string prefijo)
    {
        var u = new Usuario
        {
            Correo = $"met-{prefijo}-{Guid.NewGuid():N}@zass.local", HashContrasena = "x",
            Nombre = "Persona de prueba", CorreoVerificado = true,
        };
        ctx.Usuarios.Add(u);
        await ctx.SaveChangesAsync();
        return u;
    }

    private static async Task<PerfilPrestador> UnPerfilAsync(ZassDbContext ctx)
    {
        var u = await UnUsuarioAsync(ctx, "prest");
        var perfil = new PerfilPrestador
        {
            UsuarioId = u.Id, NombrePublico = $"Estudio {Guid.NewGuid():N}",
            Slug = $"met-{Guid.NewGuid():N}", RubroId = 1,
        };
        ctx.PerfilesPrestador.Add(perfil);
        await ctx.SaveChangesAsync();
        return perfil;
    }

    private static void UnContacto(ZassDbContext ctx, Guid buscadorId, Guid perfilId, CanalContacto canal, DateTime cuando) =>
        ctx.Contactos.Add(new Contacto
        {
            BuscadorId = buscadorId, PerfilPrestadorId = perfilId,
            Origen = OrigenContacto.Ficha, Canal = canal, OcurridoEn = cuando,
        });

    private static Tarjeta UnaTarjeta(ZassDbContext ctx, Guid perfilId)
    {
        var t = new Tarjeta { UrlEstable = Tarjeta.GenerarUrlEstable(), Estado = EstadoTarjeta.Activa, PerfilPrestadorId = perfilId };
        ctx.Tarjetas.Add(t);
        return t;
    }

    private static void UnEscaneo(ZassDbContext ctx, Guid tarjetaId, DateTime cuando) =>
        ctx.Escaneos.Add(new EscaneoTarjeta { TarjetaId = tarjetaId, Medio = MedioEscaneo.Nfc, OcurridoEn = cuando });
}

/// <summary>MI-METRICAS por HTTP: sin sesión, 401; sin perfil de prestador, lo mismo que /api/mi/tarjetas.</summary>
public class MetricasPrestadorApiPruebas : IAsyncLifetime
{
    private readonly AplicacionParaAuditoria _app = new();
    private HttpClient _cliente = null!;

    public Task InitializeAsync()
    {
        _cliente = _app.CreateClient(new WebApplicationFactoryClientOptions
        {
            HandleCookies = true, AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost"),
        });
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        _app.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task Sin_sesion_devuelve_401()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await _cliente.GetAsync("/api/mi/metricas")).StatusCode);
    }

    [Fact]
    public async Task Sin_perfil_de_prestador_responde_igual_que_mis_tarjetas()
    {
        var correo = $"met-api-{Guid.NewGuid():N}@zass.local";
        Assert.Equal(HttpStatusCode.Created, (await _cliente.PostAsJsonAsync("/api/auth/registro", new
        {
            tipo = "particular", apellido = "Prueba", rut = DatosDeRegistro.Rut(), telefono = "+56 9 1234 5678",
            mayorDeEdad = true, correo, contrasena = "contrasena-larga-1", nombre = "Usuario Test", rol = "buscador",
        })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _cliente.PostAsJsonAsync("/api/auth/sesion",
            new { correo, contrasena = "contrasena-larga-1" })).StatusCode);

        var metricas = await _cliente.GetAsync("/api/mi/metricas");
        var tarjetas = await _cliente.GetAsync("/api/mi/tarjetas");
        Assert.NotEqual(HttpStatusCode.OK, metricas.StatusCode);
        Assert.Equal(tarjetas.StatusCode, metricas.StatusCode);
    }
}
