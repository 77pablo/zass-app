using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Zass.Aplicacion.Suscripciones;
using Zass.Dominio.Comun;
using Zass.Dominio.Identidad;
using Zass.Dominio.Suscripciones;
using Zass.Infraestructura.Datos.Repositorios;
using Zass.Infraestructura.Fondo;
using Zass.Infraestructura.Seguridad;

namespace Zass.Integracion.Pruebas;

/// <summary>
/// N3 (auditoria del 22-nov-2026), por HTTP y contra PostgreSQL: cancelar la renovacion
/// (API-78) y eliminar la cuenta (API-17) dejan de cobrar. Antes no habia forma de cancelar,
/// y el cobro automatico le seguia cobrando a quien eliminaba su cuenta con la tarjeta
/// guardada en la pasarela.
///
/// <para>
/// En la coleccion del cobro, que no corre en paralelo con nada: corre el barrido del cobro
/// automatico con un reloj fijo en 2034 para no recoger suscripciones de otras pruebas.
/// </para>
/// </summary>
[Collection(ColeccionCobroConcurrencia.Nombre)]
public class ApiPagosCorrectosPruebas : IAsyncLifetime
{
    private static readonly DateTime Ahora = new(2034, 5, 10, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Vence = Ahora.AddDays(5);

    private readonly Aplicacion _app = new();
    private readonly List<HttpClient> _clientes = new();
    private readonly List<(Guid Usuario, Guid Perfil)> _creados = new();

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        using (var ctx = ContextoDePrueba.Crear())
        {
            foreach (var (u, p) in _creados)
            {
                await ctx.Database.ExecuteSqlRawAsync(
                    """
                    UPDATE cupos_fundador SET perfil_prestador_id = NULL, reservado_hasta = NULL
                     WHERE perfil_prestador_id = {1};
                    DELETE FROM auditoria WHERE entidad_id IN
                        (SELECT id FROM intentos_de_pago WHERE perfil_prestador_id = {1}
                         UNION SELECT id FROM suscripciones WHERE perfil_prestador_id = {1})
                       OR entidad_id = {1} OR entidad_id = {0} OR actor_id = {0};
                    DELETE FROM intentos_de_pago WHERE perfil_prestador_id = {1};
                    DELETE FROM pagos WHERE suscripcion_id IN (SELECT id FROM suscripciones WHERE perfil_prestador_id = {1});
                    DELETE FROM suscripciones WHERE perfil_prestador_id = {1};
                    DELETE FROM notificaciones WHERE destinatario_id = {0};
                    DELETE FROM sesiones WHERE usuario_id = {0};
                    """, u, p);
                ctx.PerfilesPrestador.RemoveRange(ctx.PerfilesPrestador.Where(x => x.Id == p));
                await ctx.SaveChangesAsync();
                ctx.Usuarios.RemoveRange(ctx.Usuarios.Where(x => x.Id == u));
                await ctx.SaveChangesAsync();
            }
        }
        foreach (var c in _clientes) c.Dispose();
        _app.Dispose();
    }

    /// <summary>
    /// Un prestador con sesion, perfil publicado y una suscripcion Esencial ACTIVA con medio de
    /// pago inscrito, a 5 dias de vencer segun el reloj fijo: justo lo que el cobro automatico
    /// cobra.
    /// </summary>
    private async Task<(HttpClient cliente, Guid perfilId, Guid suscripcionId)> UnPrestadorQueSeCobraAsync()
    {
        var cliente = _app.CreateClient(new WebApplicationFactoryClientOptions
        {
            HandleCookies = true, AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost"),
        });
        _clientes.Add(cliente);
        var correo = $"n3-{Guid.NewGuid():N}@zass.local";

        Assert.Equal(HttpStatusCode.Created, (await cliente.PostAsJsonAsync("/api/auth/registro", new
        {
            tipo = "particular", apellido = "Prueba", rut = DatosDeRegistro.Rut(), telefono = "+56 9 1234 5678",
            mayorDeEdad = true, correo, contrasena = "contrasena-larga-1", nombre = "Persona de prueba", rol = "prestador",
        })).StatusCode);

        Guid perfilId, suscripcionId;
        using (var ctx = ContextoDePrueba.Crear())
        {
            var usuario = await ctx.Usuarios.SingleAsync(u => u.Correo == correo);
            var aviso = await ctx.Notificaciones
                .Where(n => n.DestinatarioId == usuario.Id && n.Tipo == TipoNotificacion.CorreoVerificacion)
                .OrderByDescending(n => n.ProgramadaPara).FirstAsync();
            var token = Zass.Aplicacion.Puertos.CargaDeAviso.LeerToken(aviso.Cuerpo)!;
            Assert.Equal(HttpStatusCode.NoContent,
                (await cliente.PostAsJsonAsync("/api/auth/correo/verificar", new { token })).StatusCode);

            var perfil = await ctx.PerfilesPrestador.SingleAsync(p => p.UsuarioId == usuario.Id);
            _creados.Add((usuario.Id, perfil.Id));
            perfil.RubroId = (await ctx.Rubros.FirstAsync(r => r.Activo)).Id;
            perfil.Slug = $"n3-{Guid.NewGuid():N}"[..30];
            perfil.NombrePublico = "Estudio N3";
            perfil.Estado = EstadoPerfil.Publicado;
            perfil.PublicadoEn = DateTime.UtcNow.AddDays(-2);
            var mensual = await ctx.Planes.FirstAsync(p => p.Codigo == "mensual");
            var s = new Suscripcion
            {
                PerfilPrestadorId = perfil.Id, PlanId = mensual.Id,
                Estado = EstadoSuscripcion.Activa,
                IniciaEn = Vence.AddMonths(-1), VigenteHasta = Vence,
                ReferenciaPasarela = "medio-inscrito-n3",
            };
            ctx.Suscripciones.Add(s);
            await ctx.SaveChangesAsync();
            perfilId = perfil.Id;
            suscripcionId = s.Id;
        }

        Assert.Equal(HttpStatusCode.OK, (await cliente.PostAsJsonAsync("/api/auth/sesion", new
        {
            correo, contrasena = "contrasena-larga-1",
        })).StatusCode);
        return (cliente, perfilId, suscripcionId);
    }

    private static async Task<HttpResponseMessage> ConCsrfAsync(
        HttpClient cliente, HttpMethod metodo, string ruta, object? cuerpo = null)
    {
        var csrf = (await (await cliente.GetAsync("/api/auth/csrf")).Content.ReadFromJsonAsync<JsonDocument>())!
            .RootElement.GetProperty("token").GetString()!;
        var p = new HttpRequestMessage(metodo, ruta);
        if (cuerpo is not null) p.Content = JsonContent.Create(cuerpo);
        p.Headers.Add("X-CSRF-Token", csrf);
        return await cliente.SendAsync(p);
    }

    private static async Task CorrerCobroAutomaticoAsync()
    {
        var reloj = new RelojFijo(Ahora);
        using var ctx = ContextoDePrueba.Crear();
        var servicio = new ServicioCobros(
            new RepositorioSuscripcionesEfCore(ctx),
            new ServicioSuscripciones(
                new RepositorioSuscripcionesEfCore(ctx), new UnidadDeTrabajoEfCore(ctx), reloj,
                new AuditorEfCore(ctx), new ColaNotificacionesEfCore(ctx)),
            new PasarelaFalsa(), new UnidadDeTrabajoEfCore(ctx), reloj, new AuditorEfCore(ctx));
        await servicio.CobrarVencimientosAsync();
    }

    private static async Task<int> IntentosDeCobroAsync(Guid suscripcionId)
    {
        using var ctx = ContextoDePrueba.Crear();
        return await ctx.IntentosDePago.CountAsync(i => i.SuscripcionId == suscripcionId);
    }

    [Fact]
    public async Task API_78_sin_CSRF_no_cancela_nada()
    {
        var (cliente, _, suscripcionId) = await UnPrestadorQueSeCobraAsync();

        var r = await cliente.PostAsync("/api/mi/suscripcion/cancelar", null);

        Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
        using var ctx = ContextoDePrueba.Crear();
        Assert.Null((await ctx.Suscripciones.SingleAsync(s => s.Id == suscripcionId)).CanceladaEn);
    }

    [Fact]
    public async Task API_78_cancela_la_renovacion_la_suscripcion_sigue_vigente_y_el_cobro_automatico_no_cobra()
    {
        var (cliente, _, suscripcionId) = await UnPrestadorQueSeCobraAsync();

        var r = await ConCsrfAsync(cliente, HttpMethod.Post, "/api/mi/suscripcion/cancelar");

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var cuerpo = await r.Content.ReadFromJsonAsync<JsonDocument>();
        Assert.True(cuerpo!.RootElement.GetProperty("vigente").GetBoolean());
        Assert.True(cuerpo.RootElement.GetProperty("renovacionCancelada").GetBoolean());
        using (var ctx = ContextoDePrueba.Crear())
        {
            var s = await ctx.Suscripciones.SingleAsync(x => x.Id == suscripcionId);
            Assert.NotNull(s.CanceladaEn);
            Assert.Equal(EstadoSuscripcion.Activa, s.Estado);
            Assert.Equal(Vence, s.VigenteHasta);
        }

        await CorrerCobroAutomaticoAsync();

        Assert.Equal(0, await IntentosDeCobroAsync(suscripcionId));
    }

    /// <summary>
    /// El control de las dos de arriba y la de abajo: sin cancelar ni eliminar, este mismo
    /// montaje SI se cobra. Sin esto, un montaje que nunca se cobrara las dejaria verdes.
    /// </summary>
    [Fact]
    public async Task Sin_cancelar_el_mismo_montaje_si_se_cobra()
    {
        var (_, _, suscripcionId) = await UnPrestadorQueSeCobraAsync();

        await CorrerCobroAutomaticoAsync();

        Assert.Equal(1, await IntentosDeCobroAsync(suscripcionId));
    }

    [Fact]
    public async Task API_17_eliminar_la_cuenta_cancela_la_suscripcion_olvida_la_tarjeta_suelta_el_cupo_y_no_cobra()
    {
        var (cliente, perfilId, suscripcionId) = await UnPrestadorQueSeCobraAsync();
        using (var ctx = ContextoDePrueba.Crear())
        {
            var tomados = await ctx.Database.ExecuteSqlRawAsync(
                """
                UPDATE cupos_fundador SET perfil_prestador_id = {0}, reservado_hasta = NULL
                 WHERE numero = (SELECT min(numero) FROM cupos_fundador WHERE perfil_prestador_id IS NULL)
                """, perfilId);
            Assert.Equal(1, tomados);
        }

        // CS-04 (#76): eliminar la cuenta pide la contraseña.
        var r = await ConCsrfAsync(cliente, HttpMethod.Delete, "/api/mi/cuenta", new { contrasena = "contrasena-larga-1" });

        Assert.Equal(HttpStatusCode.NoContent, r.StatusCode);
        using (var ctx = ContextoDePrueba.Crear())
        {
            var s = await ctx.Suscripciones.SingleAsync(x => x.Id == suscripcionId);
            Assert.Equal(EstadoSuscripcion.Cancelada, s.Estado);
            Assert.Null(s.ReferenciaPasarela);
            Assert.NotNull(s.CanceladaEn);
            Assert.False(await ctx.CuposFundador.AnyAsync(c => c.PerfilPrestadorId == perfilId));
        }

        await CorrerCobroAutomaticoAsync();

        Assert.Equal(0, await IntentosDeCobroAsync(suscripcionId));
    }
}
