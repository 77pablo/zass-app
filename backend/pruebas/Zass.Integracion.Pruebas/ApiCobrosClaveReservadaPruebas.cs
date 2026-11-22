using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Zass.Api.Configuracion;
using Zass.Aplicacion.Suscripciones;
using Zass.Dominio.Comun;
using Zass.Dominio.Identidad;
using Zass.Dominio.Suscripciones;
using Zass.Infraestructura.Datos.Repositorios;
using Zass.Infraestructura.Fondo;
using Zass.Infraestructura.Seguridad;

namespace Zass.Integracion.Pruebas;

/// <summary>
/// CC-04: el espacio de claves del servidor en <c>intentos_de_pago</c> está RESERVADO
/// (auditoría del 21-nov-2026).
///
/// <para>
/// El cobro automático arma su clave, <c>cobro-automatico:{suscripción}:{periodo}</c>, y se
/// salta el cobro si ya existe un intento con ella. Hasta este arreglo un prestador podía
/// crear por API-68 o API-76 un intento con la clave de la suscripción de OTRA persona, y el
/// cobro automático de esa persona no ocurría: la comprobación de «ya existe» y el índice
/// único <c>ux_intento_pago_clave</c> no miran dueño. Ahora esas claves se rechazan con 400
/// antes de tocar nada.
/// </para>
///
/// <para>
/// Va en la colección del cobro, que no corre en paralelo con nada: la prueba corre el
/// barrido del cobro automático, y lo hace con un reloj fijo en 2033 para que la ventana de
/// cobro no recoja suscripciones de otras pruebas.
/// </para>
/// </summary>
[Collection(ColeccionCobroConcurrencia.Nombre)]
public class ApiCobrosClaveReservadaPruebas : IAsyncLifetime
{
    private static readonly DateTime Ahora = new(2033, 3, 10, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime VenceLaVictima = Ahora.AddDays(5);

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
                    DELETE FROM auditoria WHERE entidad_id IN
                        (SELECT i.id FROM intentos_de_pago i JOIN perfiles_prestador p ON p.id = i.perfil_prestador_id
                          WHERE p.usuario_id = {0}
                         UNION SELECT s.id FROM suscripciones s JOIN perfiles_prestador p ON p.id = s.perfil_prestador_id
                          WHERE p.usuario_id = {0});
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
    /// La víctima: una suscripción mensual activa, con medio de pago inscrito, a 5 días de
    /// vencer según el reloj fijo. Exactamente lo que el cobro automático cobra.
    /// </summary>
    private async Task<Suscripcion> UnaVictimaPorVencerAsync()
    {
        using var ctx = ContextoDePrueba.Crear();
        var usuario = new Usuario
        {
            Correo = $"victima-{Guid.NewGuid():N}-{_marca}@zass.local",
            HashContrasena = "x",
            Nombre = "Prestador de prueba",
            CorreoVerificado = true,
        };
        var perfil = new PerfilPrestador
        {
            UsuarioId = usuario.Id,
            NombrePublico = $"Estudio {Guid.NewGuid():N}",
            Slug = $"victima-{Guid.NewGuid():N}",
        };
        var suscripcion = new Suscripcion
        {
            PerfilPrestadorId = perfil.Id,
            PlanId = 2,                                   // mensual
            Estado = EstadoSuscripcion.Activa,
            IniciaEn = VenceLaVictima.AddMonths(-1),
            VigenteHasta = VenceLaVictima,
            ReferenciaPasarela = "medio-inscrito-de-prueba",
        };
        ctx.Usuarios.Add(usuario);
        ctx.PerfilesPrestador.Add(perfil);
        ctx.Suscripciones.Add(suscripcion);
        await ctx.SaveChangesAsync();
        return suscripcion;
    }

    /// <summary>El atacante: un prestador real, con sesión, perfil publicado y su prueba vigente.</summary>
    private async Task<(HttpClient cliente, Guid perfilId)> UnPrestadorConSesionAsync()
    {
        var cliente = _app.CreateClient(new WebApplicationFactoryClientOptions
        {
            HandleCookies = true, AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost"),
        });
        _clientes.Add(cliente);
        var correo = $"apires-{Guid.NewGuid():N}-{_marca}@zass.local";

        Assert.Equal(HttpStatusCode.Created, (await cliente.PostAsJsonAsync("/api/auth/registro", new
        {
            tipo = "particular", apellido = "Prueba", rut = DatosDeRegistro.Rut(), telefono = "+56 9 1234 5678",
            mayorDeEdad = true, correo, contrasena = "contrasena-larga-1", nombre = "Persona de prueba", rol = "prestador",
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
            perfil.Slug = $"apires-{Guid.NewGuid():N}"[..30];
            perfil.NombrePublico = "Estudio que ataca";
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

    private static async Task<HttpResponseMessage> PostConClaveAsync(
        HttpClient cliente, string ruta, string clave, object? cuerpo)
    {
        var csrf = (await (await cliente.GetAsync("/api/auth/csrf")).Content.ReadFromJsonAsync<JsonDocument>())!
            .RootElement.GetProperty("token").GetString()!;
        var p = new HttpRequestMessage(HttpMethod.Post, ruta);
        if (cuerpo is not null) p.Content = JsonContent.Create(cuerpo);
        p.Headers.Add("X-CSRF-Token", csrf);
        p.Headers.Add(FiltroIdempotencia.Cabecera, clave);
        return await cliente.SendAsync(p);
    }

    /// <summary>El barrido del planificador, con el reloj fijo y una pasarela que rechaza.</summary>
    private static async Task<ResumenDeCobros> CorrerCobroAutomaticoAsync()
    {
        var reloj = new RelojFijo(Ahora);
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

    [Fact]
    public async Task API_68_rechaza_la_clave_del_cobro_automatico_ajeno_y_ese_cobro_igual_ocurre()
    {
        var victima = await UnaVictimaPorVencerAsync();
        var (atacante, perfilAtacante) = await UnPrestadorConSesionAsync();
        var clave = ServicioCobros.ClaveDeCobroAutomatico(victima.Id, victima.VigenteHasta);

        var r = await PostConClaveAsync(atacante, "/api/mi/cobros", clave,
            new { codigoPlan = "mensual", codigoCupon = (string?)null });

        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        Assert.Contains("reservado", await r.Content.ReadAsStringAsync());
        using (var ctx = ContextoDePrueba.Crear())
        {
            Assert.False(await ctx.IntentosDePago.AnyAsync(i => i.ClaveIdempotencia == clave));
            Assert.False(await ctx.IntentosDePago.AnyAsync(i => i.PerfilPrestadorId == perfilAtacante));
        }

        // Lo que de verdad importa: el cobro automático de la víctima no se salta.
        var resumen = await CorrerCobroAutomaticoAsync();

        Assert.Equal(0, resumen.YaIntentadas);
        using (var ctx = ContextoDePrueba.Crear())
        {
            var intento = await ctx.IntentosDePago.SingleAsync(i => i.ClaveIdempotencia == clave);
            Assert.Equal(victima.PerfilPrestadorId, intento.PerfilPrestadorId);
            Assert.Equal(victima.Id, intento.SuscripcionId);
        }
    }

    /// <summary>
    /// API-76 rechaza la clave ANTES de mirar si quien llama tiene cupo fundador: un atacante
    /// sin cupo recibe 400, no el 409 de «primero toma tu cupo». Con mayúsculas también: no
    /// chocaría con el índice, pero tampoco es una clave que genere un cliente legítimo.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task API_76_rechaza_la_clave_del_cobro_automatico_ajeno(bool enMayusculas)
    {
        var victima = await UnaVictimaPorVencerAsync();
        var (atacante, perfilAtacante) = await UnPrestadorConSesionAsync();
        var clave = ServicioCobros.ClaveDeCobroAutomatico(victima.Id, victima.VigenteHasta);
        if (enMayusculas) clave = clave.ToUpperInvariant();

        var r = await PostConClaveAsync(atacante, "/api/mi/suscripcion/tarjeta", clave, null);

        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        Assert.Contains("reservado", await r.Content.ReadAsStringAsync());
        using var ctx = ContextoDePrueba.Crear();
        Assert.False(await ctx.IntentosDePago.AnyAsync(i => i.PerfilPrestadorId == perfilAtacante));
    }
}
