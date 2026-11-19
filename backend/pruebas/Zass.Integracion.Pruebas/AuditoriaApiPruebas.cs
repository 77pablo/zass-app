using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Zass.Dominio.Comun;
using Zass.Infraestructura.Datos;
using Zass.Dominio.Identidad;

namespace Zass.Integracion.Pruebas;

/// <summary>
/// API-61 · GET /api/interno/auditoria.
/// Pruebas de integracion contra la base real.
/// </summary>
public class AuditoriaApiPruebas : IAsyncLifetime
{
    private readonly AplicacionParaAuditoria _app = new();
    private HttpClient _cliente = null!;

    public Task InitializeAsync()
    {
        _cliente = _app.CreateClient(new WebApplicationFactoryClientOptions
        {
            HandleCookies = true,
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost"),
        });

        // Limpiar la tabla de auditoria antes de cada prueba
        return LimpiarAuditoriaAsync();
    }

    public async Task DisposeAsync()
    {
        _app.Dispose();
        await Task.CompletedTask;
    }

    private async Task LimpiarAuditoriaAsync()
    {
        await using var ctx = new ZassDbContext(new DbContextOptionsBuilder<ZassDbContext>()
            .UseNpgsql(Conexion.Cadena)
            .Options);
        ctx.Auditoria.RemoveRange(ctx.Auditoria);
        await ctx.SaveChangesAsync();
    }

    // ------------------------------------------------------------- A) admin filtra por entidad

    [Fact]
    public async Task Filtra_por_entidad_mas_nuevo_primero()
    {
        // Sembrar datos
        await SembrarAuditoria(
            new RegistroAuditoria { Accion = "accion1", Entidad = "usuarios", EntidadId = Guid.Parse("11111111-1111-1111-1111-111111111111"), ActorId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"), OcurridoEn = DateTime.UtcNow.AddMinutes(-3) },
            new RegistroAuditoria { Accion = "accion2", Entidad = "usuarios", EntidadId = Guid.Parse("11111111-1111-1111-1111-111111111111"), ActorId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"), OcurridoEn = DateTime.UtcNow.AddMinutes(-2) },
            new RegistroAuditoria { Accion = "accion3", Entidad = "usuarios", EntidadId = Guid.Parse("11111111-1111-1111-1111-111111111111"), ActorId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"), OcurridoEn = DateTime.UtcNow.AddMinutes(-1) });

        // Crear sesión de administrador
        await IniciarSesionComoAdministrador();

        // Pedir por entidad
        var respuesta = await _cliente.GetAsync("/api/interno/auditoria?entidad=usuarios&entidadId=11111111-1111-1111-1111-111111111111");

        if (respuesta.StatusCode != HttpStatusCode.OK)
        {
            var cuerpo = await respuesta.Content.ReadAsStringAsync();
            throw new Exception($"Audit query falló: {respuesta.StatusCode} - {cuerpo}");
        }
        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        var resultado = await respuesta.Content.ReadFromJsonAsync<JsonAuditoria>();
        Assert.NotNull(resultado);
        Assert.Equal(3, resultado.items.Count);
        // El mas nuevo primero
        Assert.Equal("accion3", resultado.items[0].Accion);
        Assert.Equal("accion2", resultado.items[1].Accion);
        Assert.Equal("accion1", resultado.items[2].Accion);
    }

    // ------------------------------------------------------------- B) por actor

    [Fact]
    public async Task Filtra_por_actor()
    {
        var actor1 = Guid.NewGuid();
        var actor2 = Guid.NewGuid();

        await SembrarAuditoria(
            new RegistroAuditoria { Accion = "accion1", Entidad = "usuarios", EntidadId = Guid.NewGuid(), ActorId = actor1, OcurridoEn = DateTime.UtcNow.AddMinutes(-2) },
            new RegistroAuditoria { Accion = "accion2", Entidad = "sesiones", EntidadId = Guid.NewGuid(), ActorId = actor1, OcurridoEn = DateTime.UtcNow.AddMinutes(-1) },
            new RegistroAuditoria { Accion = "accion3", Entidad = "usuarios", EntidadId = Guid.NewGuid(), ActorId = actor2, OcurridoEn = DateTime.UtcNow });

        await IniciarSesionComoAdministrador();

        var respuesta = await _cliente.GetAsync($"/api/interno/auditoria?actorId={actor1}");

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        var resultado = await respuesta.Content.ReadFromJsonAsync<JsonAuditoria>();
        Assert.NotNull(resultado);
        Assert.Equal(2, resultado.items.Count);
        Assert.All(resultado.items, r => Assert.Equal(actor1, r.ActorId));
    }

    // ------------------------------------------------------------- C) paginacion

    [Fact]
    public async Task Paginacion_limite_2_sobre_3_registros()
    {
        var entidadId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        await SembrarAuditoria(
            new RegistroAuditoria { Accion = "accion1", Entidad = "usuarios", EntidadId = entidadId, ActorId = Guid.NewGuid(), OcurridoEn = DateTime.UtcNow.AddMinutes(-2) },
            new RegistroAuditoria { Accion = "accion2", Entidad = "usuarios", EntidadId = entidadId, ActorId = Guid.NewGuid(), OcurridoEn = DateTime.UtcNow.AddMinutes(-1) },
            new RegistroAuditoria { Accion = "accion3", Entidad = "usuarios", EntidadId = entidadId, ActorId = Guid.NewGuid(), OcurridoEn = DateTime.UtcNow });

        await IniciarSesionComoAdministrador();

        // Primera pagina: limite=2
        var respuesta1 = await _cliente.GetAsync($"/api/interno/auditoria?entidad=usuarios&entidadId={entidadId}&limite=2");
        Assert.Equal(HttpStatusCode.OK, respuesta1.StatusCode);
        var pagina1 = await respuesta1.Content.ReadFromJsonAsync<JsonAuditoria>();
        Assert.NotNull(pagina1);
        Assert.Equal(2, pagina1.items.Count);
        Assert.NotNull(pagina1.siguiente);

        // Segunda pagina con el cursor
        var respuesta2 = await _cliente.GetAsync($"/api/interno/auditoria?entidad=usuarios&entidadId={entidadId}&cursor={pagina1.siguiente}");
        Assert.Equal(HttpStatusCode.OK, respuesta2.StatusCode);
        var pagina2 = await respuesta2.Content.ReadFromJsonAsync<JsonAuditoria>();
        Assert.NotNull(pagina2);
        Assert.Single(pagina2.items);
        Assert.Equal("accion1", pagina2.items[0].Accion);
        Assert.Null(pagina2.siguiente);
    }

    // ------------------------------------------------------------- D) sin filtro -> 400

    [Fact]
    public async Task Sin_filtro_devuelve_400()
    {
        await IniciarSesionComoAdministrador();

        var respuesta = await _cliente.GetAsync("/api/interno/auditoria");

        Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);
    }

    // ------------------------------------------------------------- E) usuario normal -> 403

    [Fact]
    public async Task Usuario_normal_recibe_403()
    {
        await IniciarSesionComoBuscador();

        var respuesta = await _cliente.GetAsync("/api/interno/auditoria?entidad=usuarios&entidadId=11111111-1111-1111-1111-111111111111");

        Assert.Equal(HttpStatusCode.Forbidden, respuesta.StatusCode);
    }

    // ------------------------------------------------------------- F) verificador (no admin) -> 403

    [Fact]
    public async Task Verificador_sin_rol_admin_recibe_403()
    {
        await IniciarSesionComoVerificador();

        var respuesta = await _cliente.GetAsync("/api/interno/auditoria?entidad=usuarios&entidadId=11111111-1111-1111-1111-111111111111");

        Assert.Equal(HttpStatusCode.Forbidden, respuesta.StatusCode);
    }

    // ------------------------------------------------------------- G) sin sesion -> 401

    [Fact]
    public async Task Sin_sesion_devuelve_401()
    {
        var respuesta = await _cliente.GetAsync("/api/interno/auditoria?entidad=usuarios&entidadId=11111111-1111-1111-1111-111111111111");

        Assert.Equal(HttpStatusCode.Unauthorized, respuesta.StatusCode);
    }

    // ------------------------------------------------------------- H) limite=1000 -> se acota a 100

    [Fact]
    public async Task Limite_1000_se_acota_a_100()
    {
        // Sembrar 150 registros
        var ahora = DateTime.UtcNow;
        var entidadId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var registros = Enumerable.Range(0, 150)
            .Select(i => new RegistroAuditoria
            {
                Accion = $"accion{i}",
                Entidad = "usuarios",
                EntidadId = entidadId,
                ActorId = Guid.NewGuid(),
                OcurridoEn = ahora.AddSeconds(i)
            })
            .ToList();
        await SembrarAuditoria(registros.ToArray());

        await IniciarSesionComoAdministrador();

        var respuesta = await _cliente.GetAsync($"/api/interno/auditoria?entidad=usuarios&entidadId={entidadId}&limite=1000");

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        var resultado = await respuesta.Content.ReadFromJsonAsync<JsonAuditoria>();
        Assert.NotNull(resultado);
        Assert.Equal(100, resultado.items.Count);
    }

    // ------------------------------------------------------------- apoyo

    private async Task SembrarAuditoria(params RegistroAuditoria[] registros)
    {
        await using var ctx = new ZassDbContext(new DbContextOptionsBuilder<ZassDbContext>()
            .UseNpgsql(Conexion.Cadena)
            .Options);
        ctx.Auditoria.AddRange(registros);
        await ctx.SaveChangesAsync();
    }

    private async Task<string> RegistrarYObtenerCorreoAsync(string rol = "buscador")
    {
        var correo = $"api-{Guid.NewGuid():N}@zass.local";
        var respuesta = await _cliente.PostAsJsonAsync("/api/auth/registro", new
        {
            correo,
            contrasena = "contrasena-larga-1",
            nombre = "Usuario Test",
            rol
        });
        Assert.Equal(HttpStatusCode.Created, respuesta.StatusCode);
        return correo;
    }

    private async Task AgregarRolAsync(string correo, string rolCodigo)
    {
        await using var ctx = new ZassDbContext(new DbContextOptionsBuilder<ZassDbContext>()
            .UseNpgsql(Conexion.Cadena)
            .Options);

        var usuario = await ctx.Usuarios.SingleAsync(u => u.Correo == correo);
        var rol = await ctx.Roles.SingleAsync(r => r.Codigo == rolCodigo);

        // Evitar duplicados
        var existe = await ctx.UsuarioRoles.AnyAsync(ur => ur.UsuarioId == usuario.Id && ur.RolId == rol.Id);
        if (!existe)
        {
            ctx.UsuarioRoles.Add(new UsuarioRol { UsuarioId = usuario.Id, RolId = rol.Id });
            await ctx.SaveChangesAsync();
        }

        // Asegurar que el usuario pueda entrar (estado Activa o PendienteVerificacion)
        if (usuario.Estado == EstadoCuenta.PendienteVerificacion || usuario.Estado == EstadoCuenta.Activa)
        {
            // Ya está bien
        }
        else
        {
            usuario.Estado = EstadoCuenta.Activa;
            await ctx.SaveChangesAsync();
        }
    }

    private async Task IniciarSesionComoAdministrador()
    {
        var correo = await RegistrarYObtenerCorreoAsync("buscador");
        await AgregarRolAsync(correo, "administrador");
        await IniciarSesionAsync(correo);
    }

    private async Task IniciarSesionComoBuscador()
    {
        var correo = await RegistrarYObtenerCorreoAsync("buscador");
        // Ya tiene rol buscador por defecto
        await IniciarSesionAsync(correo);
    }

    private async Task IniciarSesionComoVerificador()
    {
        var correo = await RegistrarYObtenerCorreoAsync("buscador");
        await AgregarRolAsync(correo, "verificador");
        await IniciarSesionAsync(correo);
    }

    private async Task IniciarSesionAsync(string correo)
    {
        var respuesta = await _cliente.PostAsJsonAsync("/api/auth/sesion",
            new { correo, contrasena = "contrasena-larga-1" });
        if (respuesta.StatusCode != HttpStatusCode.OK)
        {
            var cuerpo = await respuesta.Content.ReadAsStringAsync();
            throw new Exception($"Login falló: {respuesta.StatusCode} - {cuerpo}");
        }
    }

    // ------------------------------------------------------------- DTOs para deserializar

    private sealed class JsonAuditoria
    {
        public List<JsonItemAuditoria> items { get; set; } = new();
        public string? siguiente { get; set; }
    }

    private sealed class JsonItemAuditoria
    {
        public long Id { get; set; }
        public string Accion { get; set; } = "";
        public string Entidad { get; set; } = "";
        public Guid? EntidadId { get; set; }
        public Guid? ActorId { get; set; }
        public string? Contexto { get; set; }
        public string? IpOrigen { get; set; }
        public DateTime OcurridoEn { get; set; }
    }
}