using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Zass.Dominio.Agenda;
using Zass.Dominio.Catalogo;
using Zass.Dominio.Comun;
using Zass.Dominio.Identidad;
using Zass.Dominio.Suscripciones;

namespace Zass.Integracion.Pruebas;

/// <summary>
/// RN-S6-10 por HTTP y contra <c>zass_pruebas</c>: el prestador que dejo de pagar conserva su
/// ficha (API-05 responde 200), pero sin contacto y sin calendario (API-35 ya no da horas).
///
/// Lo que SOLO se ve aqui: que el JSON que sale por el cable no lleva el telefono. Esconder
/// los botones en la pantalla no bastaria —el dato seguiria ahi para quien abra las
/// herramientas del navegador—, y eso es justo lo que una prueba de componente no puede ver.
///
/// El mismo prestador se prueba con la suscripcion ACTIVA y sin ella, cambiando una sola
/// columna: asi la diferencia no puede venir de otra cosa.
/// </summary>
public class ApiFichaSinSuscripcionPruebas : IAsyncLifetime
{
    private const string Telefono = "+56 9 5550 0101";

    private readonly Aplicacion _app = new();
    private HttpClient _cliente = null!;

    private string _marca = "";
    private string _slug = "";
    private Guid _perfil;
    private Guid _suscripcion;
    private Guid _servicioOfrecido;
    private int _servicioCatalogo;

    public async Task InitializeAsync()
    {
        _cliente = _app.CreateClient(new WebApplicationFactoryClientOptions
        {
            // Con cookies: la ficha esconde el contacto a quien no tiene sesion, asi que sin
            // sesion esta prueba no distinguiria RN-S6-10 de «no has entrado».
            HandleCookies = true, AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost"),
        });

        _marca = Guid.NewGuid().ToString("N")[..12];
        _slug = $"apisus-{_marca}";

        using var ctx = ContextoDePrueba.Crear();
        var ahora = DateTime.UtcNow;
        var rubro = await ctx.Rubros.FirstAsync(r => r.Activo);

        var usuario = new Usuario
        {
            Correo = $"apisus-p-{_marca}@zass.local", HashContrasena = "x",
            Nombre = "Prestador de prueba", CorreoVerificado = true,
        };
        ctx.Usuarios.Add(usuario);

        var perfil = new PerfilPrestador
        {
            UsuarioId = usuario.Id, RubroId = rubro.Id, Slug = _slug,
            NombrePublico = "Estudio de prueba", Descripcion = "Corretaje en Temuco",
            Estado = EstadoPerfil.Publicado, PublicadoEn = ahora.AddDays(-30),
            TelefonoPublico = Telefono, Whatsapp = "912345678",
            CorreoPublico = $"publico-{_marca}@zass.local", SitioWeb = "https://ejemplo.test",
        };
        ctx.PerfilesPrestador.Add(perfil);

        var servicio = new Servicio
        {
            RubroId = rubro.Id, Nombre = $"Consulta {_marca}",
            NombreNormalizado = $"consulta {_marca}", EsCatalogo = false,
        };
        ctx.Servicios.Add(servicio);
        await ctx.SaveChangesAsync();

        var ofrecido = new PrestadorServicio
        {
            PerfilPrestadorId = perfil.Id, ServicioId = servicio.Id,
            DuracionMinutos = 30, ModoAgenda = ModoAgenda.Automatico, Activo = true,
        };
        ctx.PrestadorServicios.Add(ofrecido);

        var plan = await ctx.Planes.FirstAsync(p => p.Codigo == "prueba");
        var suscripcion = new Suscripcion
        {
            PerfilPrestadorId = perfil.Id, PlanId = plan.Id, Estado = EstadoSuscripcion.Activa,
            IniciaEn = ahora.AddDays(-30), VigenteHasta = ahora.AddDays(28),
        };
        ctx.Suscripciones.Add(suscripcion);
        await ctx.SaveChangesAsync();

        // Una hora libre de verdad: sin esto, «el calendario no da horas» saldria verde
        // aunque la guarda no existiera, porque no habria horas que dar.
        var manana = ahora.AddDays(1);
        var regla = new Disponibilidad
        {
            PerfilPrestadorId = perfil.Id, PrestadorServicioId = ofrecido.Id,
            DiaSemana = (short)(((int)manana.DayOfWeek + 6) % 7 + 1),
            HoraInicio = TimeOnly.FromDateTime(manana),
            HoraFin = TimeOnly.FromDateTime(manana).AddMinutes(30),
            DuracionBloqueMin = 30, VigenteDesde = DateOnly.FromDateTime(manana),
        };
        ctx.Disponibilidades.Add(regla);
        await ctx.SaveChangesAsync();

        ctx.Bloques.Add(new BloqueHorario
        {
            DisponibilidadId = regla.Id, PerfilPrestadorId = perfil.Id,
            PrestadorServicioId = ofrecido.Id,
            InicioEn = manana, FinEn = manana.AddMinutes(30), Estado = EstadoBloque.Libre,
        });
        await ctx.SaveChangesAsync();

        _perfil = perfil.Id;
        _suscripcion = suscripcion.Id;
        _servicioOfrecido = ofrecido.Id;
        _servicioCatalogo = servicio.Id;
    }

    public async Task DisposeAsync()
    {
        using (var ctx = ContextoDePrueba.Crear())
        {
            await ctx.Database.ExecuteSqlRawAsync(
                """
                DELETE FROM bloques              WHERE perfil_prestador_id = {0};
                DELETE FROM disponibilidades     WHERE perfil_prestador_id = {0};
                DELETE FROM suscripciones        WHERE perfil_prestador_id = {0};
                DELETE FROM prestador_servicios  WHERE perfil_prestador_id = {0};
                DELETE FROM servicios            WHERE id = {1};
                DELETE FROM perfiles_prestador   WHERE id = {0};
                """, _perfil, _servicioCatalogo);

            var usuarios = await ctx.Usuarios
                .Where(u => u.Correo.EndsWith($"-{_marca}@zass.local")).ToListAsync();
            foreach (var u in usuarios)
            {
                ctx.Auditoria.RemoveRange(
                    ctx.Auditoria.Where(a => a.ActorId == u.Id || a.EntidadId == u.Id));
                ctx.Notificaciones.RemoveRange(
                    ctx.Notificaciones.Where(n => n.DestinatarioId == u.Id));
            }
            ctx.Usuarios.RemoveRange(usuarios);
            await ctx.SaveChangesAsync();
        }
        _cliente.Dispose();
        await _app.DisposeAsync();
    }

    /// <summary>La unica columna que cambia entre los dos escenarios.</summary>
    private static async Task CaerLaSuscripcionAsync(Guid suscripcionId)
    {
        using var ctx = ContextoDePrueba.Crear();
        await ctx.Suscripciones.Where(s => s.Id == suscripcionId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(x => x.Estado, EstadoSuscripcion.Vencida)
                .SetProperty(x => x.VigenteHasta, DateTime.UtcNow.AddDays(-1)));
    }

    /// <summary>
    /// Buscador con correo verificado y sesion abierta. Sin esto, el contacto llegaria vacio
    /// por falta de cuenta y la prueba de RN-S6-10 estaria verde por el motivo equivocado.
    /// </summary>
    private async Task UnBuscadorConSesionAsync()
    {
        var correo = $"apisus-b-{Guid.NewGuid():N}-{_marca}@zass.local";
        Assert.Equal(HttpStatusCode.Created, (await _cliente.PostAsJsonAsync("/api/auth/registro", new
        {
            correo, contrasena = "contrasena-larga-1", nombre = "Ana Pérez", rol = "buscador",
        })).StatusCode);

        using (var ctx = ContextoDePrueba.Crear())
        {
            var usuario = await ctx.Usuarios.SingleAsync(u => u.Correo == correo);
            var aviso = await ctx.Notificaciones
                .Where(n => n.DestinatarioId == usuario.Id
                            && n.Tipo == TipoNotificacion.CorreoVerificacion)
                .OrderByDescending(n => n.ProgramadaPara).FirstAsync();
            var token = Zass.Aplicacion.Puertos.CargaDeAviso.LeerToken(aviso.Cuerpo)!;
            Assert.Equal(HttpStatusCode.NoContent,
                (await _cliente.PostAsJsonAsync("/api/auth/correo/verificar", new { token })).StatusCode);
        }

        Assert.Equal(HttpStatusCode.OK, (await _cliente.PostAsJsonAsync("/api/auth/sesion", new
        {
            correo, contrasena = "contrasena-larga-1",
        })).StatusCode);
    }

    private async Task<JsonElement> FichaAsync()
    {
        var r = await _cliente.GetAsync($"/api/prestadores/{_slug}");
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        return await r.Content.ReadFromJsonAsync<JsonElement>();
    }

    private Task<HttpResponseMessage> CalendarioAsync() =>
        _cliente.GetAsync($"/api/prestadores/{_slug}/bloques?servicio={_servicioOfrecido}");

    // ------------------------------------------------------------- al dia (el control)

    /// <summary>
    /// El control de la prueba: con la suscripcion al dia la ficha trae el telefono y el
    /// calendario trae la hora. Sin esto, lo de abajo podria estar verde por accidente.
    /// </summary>
    [Fact]
    public async Task Al_dia_la_ficha_trae_el_contacto_y_el_calendario_trae_la_hora()
    {
        await UnBuscadorConSesionAsync();

        var ficha = await FichaAsync();

        Assert.True(ficha.GetProperty("suscripcionVigente").GetBoolean());
        Assert.Equal(Telefono, ficha.GetProperty("contacto").GetProperty("telefono").GetString());

        var calendario = await CalendarioAsync();
        Assert.Equal(HttpStatusCode.OK, calendario.StatusCode);
        var cuerpo = await calendario.Content.ReadFromJsonAsync<JsonElement>();
        Assert.NotEmpty(cuerpo.GetProperty("bloques").EnumerateArray().ToList());
    }

    // ------------------------------------------------------------- RN-S6-10

    /// <summary>
    /// La ficha NO desaparece: 200, con nombre, oficio y servicios. Lo que desaparece es el
    /// contacto, y desaparece del JSON, no solo de la pantalla.
    /// </summary>
    [Fact]
    public async Task Sin_suscripcion_la_ficha_responde_200_pero_el_contacto_no_viaja()
    {
        await UnBuscadorConSesionAsync();
        await CaerLaSuscripcionAsync(_suscripcion);

        var ficha = await FichaAsync();

        Assert.False(ficha.GetProperty("suscripcionVigente").GetBoolean());
        Assert.Equal("Estudio de prueba", ficha.GetProperty("nombrePublico").GetString());
        Assert.NotEmpty(ficha.GetProperty("servicios").EnumerateArray().ToList());

        var contacto = ficha.GetProperty("contacto");
        foreach (var canal in new[] { "telefono", "whatsapp", "correo", "sitioWeb" })
            Assert.Null(contacto.GetProperty(canal).GetString());

        // Lo que de verdad protege: el numero no esta en NINGUNA parte de la respuesta.
        Assert.DoesNotContain("5550", ficha.GetRawText());
    }

    /// <summary>
    /// El hueco que esto cierra: antes el calendario se pintaba con horas que parecian
    /// libres y el «no» llegaba recien al confirmar. Ahora no hay horas que elegir.
    /// </summary>
    [Fact]
    public async Task Sin_suscripcion_el_calendario_ya_no_ofrece_horas()
    {
        await CaerLaSuscripcionAsync(_suscripcion);

        var r = await CalendarioAsync();

        Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
        var problema = await r.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains("no está recibiendo horas", problema.GetProperty("detail").GetString());
        // Y no se cuela ninguna hora por el cuerpo del error.
        Assert.False(problema.TryGetProperty("bloques", out _));
    }
}
