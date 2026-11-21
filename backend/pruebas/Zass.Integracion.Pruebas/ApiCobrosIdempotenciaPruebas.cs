using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Zass.Api.Configuracion;
using Zass.Dominio.Comun;
using Zass.Dominio.Identidad;
using Zass.Dominio.Suscripciones;

namespace Zass.Integracion.Pruebas;

/// <summary>
/// CC-04 en API-68 (empezar a pagar un mes) por HTTP: la clave de idempotencia tiene DUEÑO.
///
/// La clave la inventa el cliente. Hasta el 21-nov-2026 se buscaba en toda la tabla
/// <c>intentos_de_pago</c>, y un prestador que presentaba la clave de otro recibía el intento
/// AJENO, con su <c>urlParaPagar</c> y su referencia de pasarela. Ahora recibe un 409 neutro,
/// sin un dato de ese intento, y no un 500 del índice único <c>ux_intento_pago_clave</c>.
/// </summary>
public class ApiCobrosIdempotenciaPruebas : IAsyncLifetime
{
    private readonly Aplicacion _app = new();
    private readonly string _marca = Guid.NewGuid().ToString("N")[..12];
    private readonly List<HttpClient> _clientes = new();

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        using (var ctx = ContextoDePrueba.Crear())
        {
            var usuarios = await ctx.Usuarios
                .Where(u => u.Correo.EndsWith($"-{_marca}@zass.local")).ToListAsync();
            foreach (var u in usuarios)
            {
                await ctx.Database.ExecuteSqlRawAsync(
                    """
                    DELETE FROM intentos_de_pago WHERE perfil_prestador_id IN
                        (SELECT id FROM perfiles_prestador WHERE usuario_id = {0});
                    DELETE FROM suscripciones WHERE perfil_prestador_id IN
                        (SELECT id FROM perfiles_prestador WHERE usuario_id = {0});
                    """, u.Id);
                ctx.Auditoria.RemoveRange(
                    ctx.Auditoria.Where(a => a.ActorId == u.Id || a.EntidadId == u.Id));
                ctx.Notificaciones.RemoveRange(
                    ctx.Notificaciones.Where(n => n.DestinatarioId == u.Id));
                ctx.Sesiones.RemoveRange(ctx.Sesiones.Where(s => s.UsuarioId == u.Id));
                ctx.PerfilesPrestador.RemoveRange(ctx.PerfilesPrestador.Where(p => p.UsuarioId == u.Id));
            }
            await ctx.SaveChangesAsync();
            ctx.Usuarios.RemoveRange(usuarios);
            await ctx.SaveChangesAsync();
        }
        foreach (var c in _clientes) c.Dispose();
        _app.Dispose();
    }

    /// <summary>
    /// Un prestador con el correo verificado, el perfil publicado y su suscripción de prueba
    /// vigente (lo que deja API-22), con la sesión abierta en un cliente propio.
    /// </summary>
    private async Task<(HttpClient cliente, Guid perfilId)> UnPrestadorConSesionAsync()
    {
        var cliente = _app.CreateClient(new WebApplicationFactoryClientOptions
        {
            HandleCookies = true, AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost"),
        });
        _clientes.Add(cliente);
        var correo = $"apicob-{Guid.NewGuid():N}-{_marca}@zass.local";

        Assert.Equal(HttpStatusCode.Created, (await cliente.PostAsJsonAsync("/api/auth/registro", new
        {
            tipo = "particular", apellido = "Prueba", rut = DatosDeRegistro.Rut(), telefono = "+56 9 1234 5678",
            mayorDeEdad = true, correo, contrasena = "contrasena-larga-1", nombre = "Pablo Espinoza", rol = "prestador",
        })).StatusCode);

        Guid perfilId;
        using (var ctx = ContextoDePrueba.Crear())
        {
            var usuario = await ctx.Usuarios.SingleAsync(u => u.Correo == correo);
            var aviso = await ctx.Notificaciones
                .Where(n => n.DestinatarioId == usuario.Id && n.Tipo == TipoNotificacion.CorreoVerificacion)
                .OrderByDescending(n => n.ProgramadaPara).FirstAsync();
            var token = Zass.Aplicacion.Puertos.CargaDeAviso.LeerToken(aviso.Cuerpo)!;
            Assert.Equal(HttpStatusCode.NoContent,
                (await cliente.PostAsJsonAsync("/api/auth/correo/verificar", new { token })).StatusCode);

            var ahora = DateTime.UtcNow;
            var perfil = await ctx.PerfilesPrestador.SingleAsync(p => p.UsuarioId == usuario.Id);
            perfil.RubroId = (await ctx.Rubros.FirstAsync(r => r.Activo)).Id;
            perfil.Slug = $"apicob-{Guid.NewGuid():N}"[..30];
            perfil.NombrePublico = "Estudio de cobros";
            perfil.Estado = EstadoPerfil.Publicado;
            perfil.PublicadoEn = ahora.AddDays(-2);
            var prueba = await ctx.Planes.FirstAsync(p => p.Codigo == "prueba");
            ctx.Suscripciones.Add(new Suscripcion
            {
                PerfilPrestadorId = perfil.Id, PlanId = prueba.Id,
                Estado = EstadoSuscripcion.Activa,
                IniciaEn = ahora.AddDays(-2), VigenteHasta = ahora.AddDays(28),
            });
            await ctx.SaveChangesAsync();
            perfilId = perfil.Id;
        }

        Assert.Equal(HttpStatusCode.OK, (await cliente.PostAsJsonAsync("/api/auth/sesion", new
        {
            correo, contrasena = "contrasena-larga-1",
        })).StatusCode);
        return (cliente, perfilId);
    }

    private static async Task<HttpResponseMessage> CobrarAsync(HttpClient cliente, string clave)
    {
        var csrf = (await (await cliente.GetAsync("/api/auth/csrf")).Content.ReadFromJsonAsync<JsonDocument>())!
            .RootElement.GetProperty("token").GetString()!;
        var p = new HttpRequestMessage(HttpMethod.Post, "/api/mi/cobros")
        {
            Content = JsonContent.Create(new { codigoPlan = "mensual", codigoCupon = (string?)null }),
        };
        p.Headers.Add("X-CSRF-Token", csrf);
        p.Headers.Add(FiltroIdempotencia.Cabecera, clave);
        return await cliente.SendAsync(p);
    }

    [Fact]
    public async Task La_clave_de_otro_prestador_no_devuelve_su_intento_ni_su_enlace_de_pago()
    {
        var clave = $"cobro-{Guid.NewGuid():N}";
        var (cliente1, perfil1) = await UnPrestadorConSesionAsync();
        var (cliente2, perfil2) = await UnPrestadorConSesionAsync();

        var primera = await CobrarAsync(cliente1, clave);
        Assert.Equal(HttpStatusCode.OK, primera.StatusCode);
        var suyo = (await primera.Content.ReadFromJsonAsync<JsonDocument>())!.RootElement;
        var idAjeno = suyo.GetProperty("id").GetString()!;
        var urlAjena = suyo.GetProperty("urlParaPagar").GetString();
        Assert.False(string.IsNullOrEmpty(urlAjena), "El primer intento debería traer adónde pagar.");

        var r = await CobrarAsync(cliente2, clave);
        var texto = await r.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
        Assert.DoesNotContain(idAjeno, texto);
        Assert.DoesNotContain(urlAjena!, texto);
        Assert.DoesNotContain("urlParaPagar", texto);
        Assert.DoesNotContain("referenciaPasarela", texto);

        using (var ctx = ContextoDePrueba.Crear())
        {
            Assert.Equal(1, await ctx.IntentosDePago.CountAsync(i => i.ClaveIdempotencia == clave));
            Assert.Equal(perfil1, (await ctx.IntentosDePago.SingleAsync(i => i.ClaveIdempotencia == clave)).PerfilPrestadorId);
            Assert.False(await ctx.IntentosDePago.AnyAsync(i => i.PerfilPrestadorId == perfil2));
        }

        // CC-04 sigue en pie para el dueño: la misma clave, el mismo intento.
        var reintento = await CobrarAsync(cliente1, clave);
        Assert.Equal(HttpStatusCode.OK, reintento.StatusCode);
        Assert.Equal(idAjeno, (await reintento.Content.ReadFromJsonAsync<JsonDocument>())!
            .RootElement.GetProperty("id").GetString());
    }
}
