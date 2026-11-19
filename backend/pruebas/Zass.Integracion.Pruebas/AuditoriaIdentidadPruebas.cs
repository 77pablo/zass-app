using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Zass.Dominio.Comun;
using Zass.Dominio.Identidad;
using Zass.Aplicacion.Identidad;
using Zass.Infraestructura.Datos.Repositorios;
using Zass.Infraestructura.Seguridad;

namespace Zass.Integracion.Pruebas;

/// <summary>
/// T32: pruebas de integracion que verifican que los eventos de auditoria de identidad
/// queden registrados en TBL-28. Seguridad.md, seccion 6.
/// </summary>
public class AuditoriaIdentidadPruebas : IAsyncLifetime
{
    private readonly AplicacionParaAuditoria _app = new();
    private HttpClient _cliente = null!;
    private readonly List<string> _correosCreados = new();

    public Task InitializeAsync()
    {
        _cliente = _app.CreateClient(new WebApplicationFactoryClientOptions
        {
            HandleCookies = true,
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost"),
        });
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        if (_correosCreados.Count > 0)
        {
            using var ctx = ContextoDePrueba.Crear();
            foreach (var correo in _correosCreados)
            {
                var usuario = await ctx.Usuarios.FirstOrDefaultAsync(u => u.Correo == correo);
                if (usuario is null) continue;

                ctx.Auditoria.RemoveRange(
                    ctx.Auditoria.Where(a => a.ActorId == usuario.Id || a.EntidadId == usuario.Id));
                ctx.Usuarios.Remove(usuario);
            }
            await ctx.SaveChangesAsync();
        }

        await _app.DisposeAsync();
    }

    private async Task<string> TokenDeVerificacionAsync(string correo)
    {
        using var ctx = ContextoDePrueba.Crear();
        var usuario = await ctx.Usuarios.SingleAsync(u => u.Correo == correo);
        return usuario.TokenVerificacion!;
    }

    private async Task RegistrarAsync(string correo, string contrasena = "contrasena-larga-1", string rol = "buscador")
    {
        var alta = await _cliente.PostAsJsonAsync("/api/auth/registro", new
        {
            correo, contrasena, nombre = "Usuario de prueba", rol,
        });
        Assert.Equal(HttpStatusCode.Created, alta.StatusCode);
        _correosCreados.Add(correo);
    }

    // ------------------------------------------------------------- T32: auditoria de identidad

    /// <summary>
    /// Seguridad.md, seccion 6: "Inicio de sesion fallido" debe quedar registrado en TBL-28
    /// con correo con hash (nunca en claro), IP, momento y motivo.
    /// </summary>
    [Fact]
    public async Task Entrar_con_otra_contrasena_queda_registrado_sin_el_correo_en_claro()
    {
        var correo = $"auditoria-fallo-{Guid.NewGuid():N}@zass.local";
        await RegistrarAsync(correo);

        // Obtiene el ID del usuario para verificar la auditoria
        using var ctxPre = ContextoDePrueba.Crear();
        var usuario = await ctxPre.Usuarios.SingleAsync(u => u.Correo == correo);
        var usuarioId = usuario.Id;

        // Intento de login con contraseña incorrecta
        var respuesta = await _cliente.PostAsJsonAsync("/api/auth/sesion", new
        {
            correo, contrasena = "contrasena-incorrecta",
        });
        Assert.Equal(HttpStatusCode.Unauthorized, respuesta.StatusCode);

        // Verifica la auditoria: debe haber UNA fila con accion "inicio_sesion_fallido" para ESTE usuario
        using var ctx = ContextoDePrueba.Crear();
        ctx.ChangeTracker.Clear();

        var registros = await ctx.Auditoria
            .Where(a => a.Accion == "inicio_sesion_fallido" && a.ActorId == usuarioId)
            .ToListAsync();

        Assert.Single(registros);
        var registro = registros.Single();

        // 1) La IP de origen no debe estar vacia (la pasa el endpoint via HttpContext)
        Assert.False(string.IsNullOrWhiteSpace(registro.IpOrigen), "IpOrigen no debe ser nula ni vacia");

        // 2) Lo crucial: el correo NO aparece en claro en NINGUNA columna de texto de la fila
        var correoEnClaro = correo.ToLowerInvariant();
        var columnasTexto = new[]
        {
            registro.Accion,
            registro.Entidad,
            registro.Contexto ?? "",
            registro.IpOrigen ?? ""
        };
        foreach (var columna in columnasTexto)
        {
            Assert.DoesNotContain(correoEnClaro, columna, StringComparison.OrdinalIgnoreCase);
        }

        // Verifica que haya un correoHash en el contexto (con hash, no en claro)
        var contextoJson = registro.Contexto ?? "{}";
        Assert.Contains("correoHash", contextoJson, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Seguridad.md, seccion 6: "Eliminacion de cuenta" debe quedar registrado en TBL-28
    /// con usuario y momento.
    /// Nota: esta prueba usa el servicio directamente porque el endpoint HTTP de eliminacion
    /// (DELETE /api/mi/cuenta, API-17) requiere autenticacion por cookies (ClaimsPrincipal con
    /// UsuarioId) y CSRF; configurar eso en WebApplicationFactory para una sola prueba
    /// de auditoria añade complejidad desproporcionada sin probar nada distinto del servicio.
    /// </summary>
    [Fact]
    public async Task Eliminar_la_cuenta_queda_registrado()
    {
        // Crea un usuario directamente en la base
        var correo = $"auditoria-baja-{Guid.NewGuid():N}@zass.local";
        var hash = new HashArgon2id();
        var secretos = new GeneradorSecretos();
        
        using var ctx = ContextoDePrueba.Crear();
        var usuario = new Zass.Dominio.Identidad.Usuario
        {
            Correo = correo,
            HashContrasena = hash.Hash("contrasena-larga-1"),
            Nombre = "Usuario de prueba",
            CorreoVerificado = true,
            Estado = Zass.Dominio.Comun.EstadoCuenta.Activa,
        };
        ctx.Usuarios.Add(usuario);
        await ctx.SaveChangesAsync();
        var usuarioId = usuario.Id;

        // Llama al servicio directamente (como hace IdentidadIntegracionPruebas)
        var unidad = new UnidadDeTrabajoEfCore(ctx);
        var auditor = new AuditorEfCore(ctx);
        var reloj = new RelojUtc();
        var cuenta = new ServicioCuenta(
            new RepositorioUsuariosEfCore(ctx),
            new RepositorioPerfilesEfCore(ctx),
            unidad, hash, secretos, reloj, 
            new ColaNotificacionesEfCore(ctx),
            auditor,
            new ServicioSesion(
                new RepositorioUsuariosEfCore(ctx),
                new RepositorioSesionesEfCore(ctx),
                unidad, hash, secretos, reloj, auditor));

        var resultado = await cuenta.EliminarAsync(usuarioId);
        Assert.True(resultado.Exito, resultado.Mensaje);

        // Verifica la auditoria: UNA fila para ESTE usuario (ActorId == usuarioId)
        ctx.ChangeTracker.Clear();

        var registros = await ctx.Auditoria
            .Where(a => a.Accion == "eliminar_cuenta" && a.ActorId == usuarioId)
            .ToListAsync();

        Assert.Single(registros);
        var registro = registros.Single();

        Assert.Equal("eliminar_cuenta", registro.Accion);
        Assert.Equal("usuario", registro.Entidad);
        Assert.Equal(usuarioId, registro.ActorId);
        Assert.True(registro.OcurridoEn > DateTime.MinValue);
    }
}
