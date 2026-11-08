using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Zass.Api.Configuracion;
using Zass.Dominio.Agenda;
using Zass.Dominio.Catalogo;
using Zass.Dominio.Comun;
using Zass.Dominio.Identidad;
using Zass.Dominio.Suscripciones;

namespace Zass.Integracion.Pruebas;

/// <summary>
/// MOD-06 por HTTP. Lo que solo aquí se puede comprobar: la cabecera `Idempotency-Key` (un
/// filtro de ASP.NET, invisible desde la capa de aplicación), los códigos 201 contra 200 del
/// flujo 4B, y que el calendario público no sirva para espiar la agenda de otro prestador.
/// </summary>
public class ApiAgendaPruebas : IAsyncLifetime
{
    private readonly Aplicacion _app = new();
    private HttpClient _cliente = null!;

    private string _marca = "";
    private string _slug = "";
    private Guid _perfil;
    private Guid _servicioOfrecido;
    private int _servicioCatalogo;
    private Guid _bloque;

    public async Task InitializeAsync()
    {
        _cliente = _app.CreateClient(new WebApplicationFactoryClientOptions
        {
            HandleCookies = true, AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost"),
        });

        _marca = Guid.NewGuid().ToString("N")[..12];
        _slug = $"apiag-{_marca}";

        using var ctx = ContextoDePrueba.Crear();
        var ahora = DateTime.UtcNow;

        var prestador = new Usuario
        {
            Correo = $"apiag-p-{_marca}@zass.local", HashContrasena = "x",
            Nombre = "Pablo Espinoza", CorreoVerificado = true,
        };
        ctx.Usuarios.Add(prestador);

        var rubro = await ctx.Rubros.FirstAsync(r => r.Activo);
        var perfil = new PerfilPrestador
        {
            UsuarioId = prestador.Id, RubroId = rubro.Id, Slug = _slug,
            NombrePublico = "Estudio de prueba", Estado = EstadoPerfil.Publicado,
            PublicadoEn = ahora.AddDays(-2),
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
        ctx.Suscripciones.Add(new Suscripcion
        {
            PerfilPrestadorId = perfil.Id, PlanId = plan.Id,
            Estado = EstadoSuscripcion.Activa,
            IniciaEn = ahora.AddDays(-2), VigenteHasta = ahora.AddDays(28),
        });

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

        var bloque = new BloqueHorario
        {
            DisponibilidadId = regla.Id, PerfilPrestadorId = perfil.Id,
            PrestadorServicioId = ofrecido.Id,
            InicioEn = manana, FinEn = manana.AddMinutes(30), Estado = EstadoBloque.Libre,
        };
        ctx.Bloques.Add(bloque);
        await ctx.SaveChangesAsync();

        _perfil = perfil.Id;
        _servicioOfrecido = ofrecido.Id;
        _servicioCatalogo = servicio.Id;
        _bloque = bloque.Id;
    }

    public async Task DisposeAsync()
    {
        using (var ctx = ContextoDePrueba.Crear())
        {
            await ctx.Database.ExecuteSqlRawAsync(
                """
                DELETE FROM contactos    WHERE perfil_prestador_id = {0};
                DELETE FROM cambios_cita WHERE cita_id IN (SELECT id FROM citas WHERE perfil_prestador_id = {0});
                DELETE FROM citas        WHERE perfil_prestador_id = {0};
                DELETE FROM bloques      WHERE perfil_prestador_id = {0};
                DELETE FROM bloqueos_agenda WHERE perfil_prestador_id = {0};
                DELETE FROM disponibilidades WHERE perfil_prestador_id = {0};
                DELETE FROM suscripciones WHERE perfil_prestador_id = {0};
                DELETE FROM prestador_servicios WHERE perfil_prestador_id = {0};
                DELETE FROM servicios    WHERE id = {1};
                DELETE FROM perfiles_prestador WHERE id = {0};
                """, _perfil, _servicioCatalogo);

            var usuarios = await ctx.Usuarios
                .Where(u => u.Correo.EndsWith($"-{_marca}@zass.local")).ToListAsync();
            foreach (var u in usuarios)
            {
                ctx.Auditoria.RemoveRange(
                    ctx.Auditoria.Where(a => a.ActorId == u.Id || a.EntidadId == u.Id));
                ctx.Notificaciones.RemoveRange(
                    ctx.Notificaciones.Where(n => n.DestinatarioId == u.Id));
                ctx.Sesiones.RemoveRange(ctx.Sesiones.Where(s => s.UsuarioId == u.Id));
            }
            ctx.Usuarios.RemoveRange(usuarios);
            await ctx.SaveChangesAsync();
        }
        _cliente.Dispose();
        _app.Dispose();
    }

    // ------------------------------------------------------------- ayudantes

    private async Task<string> UnBuscadorConSesionAsync()
    {
        var correo = $"apiag-b-{Guid.NewGuid():N}-{_marca}@zass.local";
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
        return correo;
    }

    private async Task<string> CsrfAsync() =>
        (await (await _cliente.GetAsync("/api/auth/csrf")).Content.ReadFromJsonAsync<JsonDocument>())!
            .RootElement.GetProperty("token").GetString()!;

    /// <summary>POST /api/citas con control fino de la cabecera, que es lo que se prueba.</summary>
    private async Task<HttpResponseMessage> AgendarAsync(
        Guid bloqueId, string? claveIdempotencia, bool dosClaves = false)
    {
        var p = new HttpRequestMessage(HttpMethod.Post, "/api/citas")
        {
            Content = JsonContent.Create(new { bloqueId, notaBuscador = (string?)null }),
        };
        p.Headers.Add("X-CSRF-Token", await CsrfAsync());
        if (claveIdempotencia is not null)
            p.Headers.Add(FiltroIdempotencia.Cabecera, claveIdempotencia);
        if (dosClaves)
            p.Headers.Add(FiltroIdempotencia.Cabecera, $"otra-{Guid.NewGuid():N}");
        return await _cliente.SendAsync(p);
    }

    // ------------------------------------------------------------- CC-04, la cabecera

    /// <summary>
    /// api-rest.md: «Sin la cabecera, estos endpoints responden 400. No se asume una clave por
    /// defecto: una clave generada por el servidor no protege de nada».
    /// </summary>
    [Fact]
    public async Task Agendar_sin_la_cabecera_de_idempotencia_responde_400_y_no_crea_nada()
    {
        await UnBuscadorConSesionAsync();

        var r = await AgendarAsync(_bloque, null);

        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        Assert.Contains(FiltroIdempotencia.Cabecera, await r.Content.ReadAsStringAsync());

        using var ctx = ContextoDePrueba.Crear();
        Assert.False(await ctx.Citas.AnyAsync(c => c.BloqueHorarioId == _bloque));
        Assert.Equal(EstadoBloque.Libre,
            (await ctx.Bloques.SingleAsync(b => b.Id == _bloque)).Estado);
    }

    [Theory]
    [InlineData("corta")]                                  // menos del mínimo
    [InlineData("con espacio en medio")]
    [InlineData("tiene/barra/y/eso/no")]
    public async Task Una_clave_mal_formada_responde_400(string clave)
    {
        await UnBuscadorConSesionAsync();

        Assert.Equal(HttpStatusCode.BadRequest, (await AgendarAsync(_bloque, clave)).StatusCode);
    }

    /// <summary>
    /// Dos cabeceras distintas no son «una intención»: elegir una en silencio sería inventar
    /// cuál era.
    /// </summary>
    [Fact]
    public async Task Dos_claves_en_la_misma_solicitud_responden_400()
    {
        await UnBuscadorConSesionAsync();

        var r = await AgendarAsync(_bloque, $"clave-{Guid.NewGuid():N}", dosClaves: true);

        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
    }

    /// <summary>
    /// Flujo 4B de CU-12, por HTTP: 201 la primera vez y 200 la segunda, con la MISMA cita.
    /// El código distinto es lo que le dice al cliente que no se creó una segunda.
    /// </summary>
    [Fact]
    public async Task El_mismo_envio_responde_201_y_luego_200_con_la_misma_cita()
    {
        await UnBuscadorConSesionAsync();
        var clave = $"intencion-{Guid.NewGuid():N}";

        var primera = await AgendarAsync(_bloque, clave);
        var segunda = await AgendarAsync(_bloque, clave);

        Assert.Equal(HttpStatusCode.Created, primera.StatusCode);
        Assert.Equal(HttpStatusCode.OK, segunda.StatusCode);

        var a = await LeerCitaAsync(primera);
        var b = await LeerCitaAsync(segunda);
        Assert.Equal(a.id, b.id);
        Assert.Equal(a.codigo, b.codigo);

        using var ctx = ContextoDePrueba.Crear();
        Assert.Equal(1, await ctx.Citas.CountAsync(c => c.ClaveIdempotencia == clave));
    }

    [Fact]
    public async Task Un_bloque_ya_tomado_por_otra_persona_responde_409()
    {
        await UnBuscadorConSesionAsync();
        Assert.Equal(HttpStatusCode.Created,
            (await AgendarAsync(_bloque, $"uno-{Guid.NewGuid():N}")).StatusCode);

        // Otra persona, otra intención, el mismo bloque.
        await UnBuscadorConSesionAsync();
        var r = await AgendarAsync(_bloque, $"dos-{Guid.NewGuid():N}");

        Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
    }

    private static async Task<(string id, string codigo)> LeerCitaAsync(HttpResponseMessage r)
    {
        var json = (await r.Content.ReadFromJsonAsync<JsonDocument>())!.RootElement;
        return (json.GetProperty("id").GetString()!, json.GetProperty("codigo").GetString()!);
    }

    // ------------------------------------------------------------- sesión y CSRF

    [Fact]
    public async Task Agendar_sin_sesion_no_pasa_ni_con_clave_bien_puesta()
    {
        var r = await AgendarAsync(_bloque, $"clave-{Guid.NewGuid():N}");

        Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode);
    }

    [Fact]
    public async Task Agendar_sin_csrf_no_pasa()
    {
        await UnBuscadorConSesionAsync();

        var p = new HttpRequestMessage(HttpMethod.Post, "/api/citas")
        {
            Content = JsonContent.Create(new { bloqueId = _bloque, notaBuscador = (string?)null }),
        };
        p.Headers.Add(FiltroIdempotencia.Cabecera, $"clave-{Guid.NewGuid():N}");

        Assert.Equal(HttpStatusCode.Forbidden, (await _cliente.SendAsync(p)).StatusCode);
    }

    [Fact]
    public async Task La_disponibilidad_y_los_bloqueos_exigen_sesion()
    {
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await _cliente.GetAsync("/api/mi/disponibilidad")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await _cliente.GetAsync("/api/mi/citas")).StatusCode);
    }

    // ------------------------------------------------------------- API-35, el calendario público

    [Fact]
    public async Task El_calendario_es_publico_y_no_exige_cuenta()
    {
        var r = await _cliente.GetAsync(
            $"/api/prestadores/{_slug}/bloques?servicio={_servicioOfrecido}");

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var json = (await r.Content.ReadFromJsonAsync<JsonDocument>())!.RootElement;
        Assert.Equal("Automatico", json.GetProperty("modoAgenda").GetString());
        Assert.NotEmpty(json.GetProperty("bloques").EnumerateArray());
    }

    /// <summary>
    /// Sin esto, cualquier slug válido serviría para consultar la agenda de cualquier otro
    /// prestador: bastaría con conocer un identificador de servicio.
    /// </summary>
    [Fact]
    public async Task El_servicio_de_otro_prestador_no_se_puede_consultar_por_mi_slug()
    {
        using var ctx = ContextoDePrueba.Crear();
        var ajeno = await ctx.PrestadorServicios
            .FirstOrDefaultAsync(ps => ps.PerfilPrestadorId != _perfil);
        if (ajeno is null) return;   // sin otro prestador en la base no hay nada que probar

        var r = await _cliente.GetAsync(
            $"/api/prestadores/{_slug}/bloques?servicio={ajeno.Id}");

        Assert.Equal(HttpStatusCode.NotFound, r.StatusCode);
    }

    [Fact]
    public async Task Un_slug_que_no_existe_responde_404()
    {
        var r = await _cliente.GetAsync(
            $"/api/prestadores/no-existe-{Guid.NewGuid():N}/bloques?servicio={_servicioOfrecido}");

        Assert.Equal(HttpStatusCode.NotFound, r.StatusCode);
    }

    [Fact]
    public async Task Mis_citas_trae_la_que_acabo_de_agendar()
    {
        await UnBuscadorConSesionAsync();
        Assert.Equal(HttpStatusCode.Created,
            (await AgendarAsync(_bloque, $"mia-{Guid.NewGuid():N}")).StatusCode);

        var r = await _cliente.GetAsync("/api/mi/citas");

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var citas = (await r.Content.ReadFromJsonAsync<JsonDocument>())!.RootElement;
        var cita = Assert.Single(citas.EnumerateArray());
        Assert.Equal("Confirmada", cita.GetProperty("estado").GetString());
        Assert.Equal("Estudio de prueba", cita.GetProperty("prestador").GetString());
    }

    // ------------------------------------------------------------- el ciclo de la cita, por HTTP

    /// <summary>POST a /api/citas/{id}/{accion} con CSRF y, si se pide, la clave de idempotencia.</summary>
    private async Task<HttpResponseMessage> AccionAsync(
        string citaId, string accion, object? cuerpo = null, string? clave = null)
    {
        var p = new HttpRequestMessage(HttpMethod.Post, $"/api/citas/{citaId}/{accion}")
        {
            Content = JsonContent.Create(cuerpo ?? new { }),
        };
        p.Headers.Add("X-CSRF-Token", await CsrfAsync());
        if (clave is not null) p.Headers.Add(FiltroIdempotencia.Cabecera, clave);
        return await _cliente.SendAsync(p);
    }

    private async Task<string> UnaCitaPropiaAsync()
    {
        var r = await AgendarAsync(_bloque, $"ciclo-{Guid.NewGuid():N}");
        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        return (await LeerCitaAsync(r)).id;
    }

    /// <summary>
    /// api-rest.md marca cancelar y reprogramar con «I». Confirmar, rechazar y asistencia no:
    /// repetirlas es inofensivo, porque la segunda encuentra la cita en un estado desde el que
    /// la transición no existe.
    /// </summary>
    [Theory]
    [InlineData("cancelar")]
    [InlineData("reprogramar")]
    public async Task Cancelar_y_reprogramar_exigen_la_clave_de_idempotencia(string accion)
    {
        await UnBuscadorConSesionAsync();
        var cita = await UnaCitaPropiaAsync();

        var r = await AccionAsync(cita, accion, new { bloqueNuevoId = Guid.NewGuid(), motivo = (string?)null });

        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        Assert.Contains(FiltroIdempotencia.Cabecera, await r.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Cancelar_con_tiempo_responde_200_y_libera_la_hora()
    {
        await UnBuscadorConSesionAsync();
        var cita = await UnaCitaPropiaAsync();

        var r = await AccionAsync(cita, "cancelar", new { motivo = (string?)null }, $"c-{Guid.NewGuid():N}");

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        using var ctx = ContextoDePrueba.Crear();
        Assert.Equal(EstadoBloque.Libre, (await ctx.Bloques.SingleAsync(b => b.Id == _bloque)).Estado);
        Assert.Equal(EstadoCita.Cancelada,
            (await ctx.Citas.SingleAsync(c => c.Id == Guid.Parse(cita))).Estado);
    }

    /// <summary>La cita de otra persona responde 404, igual que una que no existe.</summary>
    [Fact]
    public async Task Cancelar_la_cita_de_otro_responde_404_igual_que_una_inexistente()
    {
        await UnBuscadorConSesionAsync();
        var ajena = await UnaCitaPropiaAsync();

        await UnBuscadorConSesionAsync();   // otra persona, otra sesión
        var deOtro = await AccionAsync(ajena, "cancelar", new { motivo = (string?)null }, $"c-{Guid.NewGuid():N}");
        var inexistente = await AccionAsync(
            Guid.NewGuid().ToString(), "cancelar", new { motivo = (string?)null }, $"c-{Guid.NewGuid():N}");

        Assert.Equal(HttpStatusCode.NotFound, deOtro.StatusCode);
        Assert.Equal(await inexistente.Content.ReadAsStringAsync(), await deOtro.Content.ReadAsStringAsync());
    }

    /// <summary>
    /// La defensa de CU-16 por HTTP: el buscador no puede marcar su propia asistencia, y el
    /// código es el mismo 404 que si la cita no existiera.
    /// </summary>
    [Fact]
    public async Task El_buscador_no_puede_marcar_asistencia_ni_confirmar()
    {
        await UnBuscadorConSesionAsync();
        var cita = await UnaCitaPropiaAsync();

        Assert.Equal(HttpStatusCode.NotFound,
            (await AccionAsync(cita, "asistencia", new { asistio = true })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await AccionAsync(cita, "confirmar")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await AccionAsync(cita, "rechazar", new { motivo = (string?)null })).StatusCode);
    }

    [Theory]
    [InlineData("confirmar")]
    [InlineData("rechazar")]
    [InlineData("asistencia")]
    [InlineData("cancelar")]
    [InlineData("reprogramar")]
    public async Task Sin_sesion_ninguna_accion_sobre_una_cita_pasa(string accion)
    {
        var r = await AccionAsync(Guid.NewGuid().ToString(), accion, new { }, $"x-{Guid.NewGuid():N}");

        Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode);
    }
}
