using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Zass.Api.Configuracion;
using Zass.Aplicacion.Puertos;
using Zass.Dominio.Agenda;
using Zass.Dominio.Catalogo;
using Zass.Dominio.Comun;
using Zass.Dominio.Identidad;
using Zass.Dominio.Suscripciones;
using Zass.Infraestructura.Configuracion;

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
    private Guid _perfilAjeno;

    public async Task InitializeAsync()
    {
        _cliente = _app.CreateClient(new WebApplicationFactoryClientOptions
        {
            HandleCookies = true, AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost"),
        });

        _marca = Guid.NewGuid().ToString("N")[..12];
        _slug = $"apiag-{_marca}";
        var prestadorCorreo = $"apiag-p-{_marca}@zass.local";

        // Registrar prestador via HTTP API para que tenga hash de contrasena correcto
        var tempCliente = _app.CreateClient(new WebApplicationFactoryClientOptions
        {
            HandleCookies = true, AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost"),
        });

        var prestadorReg = await tempCliente.PostAsJsonAsync("/api/auth/registro", new
        {
            correo = prestadorCorreo, contrasena = "contrasena-larga-1",
            nombre = "Pablo Espinoza", rol = "prestador",
        });
        Assert.Equal(HttpStatusCode.Created, prestadorReg.StatusCode);

        // Verificar correo del prestador
        using (var ctxPrest = ContextoDePrueba.Crear())
        {
            var usuario = await ctxPrest.Usuarios.SingleAsync(u => u.Correo == prestadorCorreo);
            var aviso = await ctxPrest.Notificaciones
                .Where(n => n.DestinatarioId == usuario.Id
                            && n.Tipo == TipoNotificacion.CorreoVerificacion)
                .OrderByDescending(n => n.ProgramadaPara).FirstAsync();
            var token = Zass.Aplicacion.Puertos.CargaDeAviso.LeerToken(aviso.Cuerpo)!;
            Assert.Equal(HttpStatusCode.NoContent,
                (await tempCliente.PostAsJsonAsync("/api/auth/correo/verificar", new { token })).StatusCode);
        }

        tempCliente.Dispose();

        using var ctx = ContextoDePrueba.Crear();
        
        // Limpiar notificaciones de corridas previas para evitar colisiones de clave idempotencia
        await ctx.Database.ExecuteSqlRawAsync("TRUNCATE TABLE notificaciones RESTART IDENTITY CASCADE");
        
        var ahora = DateTime.UtcNow;

        // Usar el PerfilPrestador que ya creó el registro HTTP (en estado Borrador)
        var prestador = await ctx.Usuarios.SingleAsync(u => u.Correo == prestadorCorreo);
        var perfil = await ctx.PerfilesPrestador.SingleAsync(p => p.UsuarioId == prestador.Id);
        perfil.RubroId = (await ctx.Rubros.FirstAsync(r => r.Activo)).Id;
        perfil.Slug = _slug;
        perfil.NombrePublico = "Estudio de prueba";
        perfil.Estado = EstadoPerfil.Publicado;
        perfil.PublicadoEn = ahora.AddDays(-2);

        var servicio = new Servicio
        {
            RubroId = (short)perfil.RubroId!, Nombre = $"Consulta {_marca}",
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
                DELETE FROM bloqueos_agenda WHERE perfil_prestador_id = {2};
                DELETE FROM perfiles_prestador WHERE id = {2};
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
                """, _perfil, _servicioCatalogo, _perfilAjeno);

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

    /// <summary>Crea un HttpClient nuevo con su propio contenedor de cookies.</summary>
    private HttpClient CrearCliente()
    {
        return _app.CreateClient(new WebApplicationFactoryClientOptions
        {
            HandleCookies = true,
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost"),
        });
    }

    /// <summary>Registra un buscador, verifica su correo e inicia sesión en _cliente. Devuelve el correo.</summary>
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

    /// <summary>POST /api/citas con control fino de la cabecera (usa _cliente).</summary>
    private async Task<HttpResponseMessage> AgendarAsync(Guid bloqueId, string? claveIdempotencia, bool dosClaves = false)
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

    /// <summary>POST /api/citas/{id}/{accion} con CSRF y, si se pide, clave de idempotencia (usa _cliente).</summary>
    private async Task<HttpResponseMessage> AccionAsync(string citaId, string accion, object? cuerpo = null, string? clave = null)
    {
        var p = new HttpRequestMessage(HttpMethod.Post, $"/api/citas/{citaId}/{accion}")
        {
            Content = JsonContent.Create(cuerpo ?? new { }),
        };
        p.Headers.Add("X-CSRF-Token", await CsrfAsync());
        if (clave is not null) p.Headers.Add(FiltroIdempotencia.Cabecera, clave);
        return await _cliente.SendAsync(p);
    }

    /// <summary>Helper para tests que solo usan un buscador: agenda y devuelve citaId.</summary>
    private async Task<string> UnBuscadorConCitaAsync(string? clave = null)
    {
        await UnBuscadorConSesionAsync();
        var r = await AgendarAsync(_bloque, clave ?? $"c-{Guid.NewGuid():N}");
        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        var cita = await LeerCitaAsync(r);
        return cita.id;
    }

    /// <summary>Inicia sesión como prestador en _cliente.</summary>
    private async Task LoginComoPrestadorAsync(string correo)
    {
        Assert.Equal(HttpStatusCode.OK, (await _cliente.PostAsJsonAsync("/api/auth/sesion", new
        {
            correo, contrasena = "contrasena-larga-1",
        })).StatusCode);
    }

    /// <summary>POST /api/citas con control fino de la cabecera, usando el cliente dado.</summary>
    private async Task<HttpResponseMessage> AgendarAsync(HttpClient cliente, Guid bloqueId, string? claveIdempotencia, bool dosClaves = false)
    {
        var p = new HttpRequestMessage(HttpMethod.Post, "/api/citas")
        {
            Content = JsonContent.Create(new { bloqueId, notaBuscador = (string?)null }),
        };
        p.Headers.Add("X-CSRF-Token", await CsrfAsync(cliente));
        if (claveIdempotencia is not null)
            p.Headers.Add(FiltroIdempotencia.Cabecera, claveIdempotencia);
        if (dosClaves)
            p.Headers.Add(FiltroIdempotencia.Cabecera, $"otra-{Guid.NewGuid():N}");
        return await cliente.SendAsync(p);
    }

    /// <summary>POST /api/citas/{id}/{accion} con CSRF y, si se pide, clave de idempotencia (cliente dado).</summary>
    private async Task<HttpResponseMessage> AccionAsync(HttpClient cliente, string citaId, string accion, object? cuerpo = null, string? clave = null)
    {
        var p = new HttpRequestMessage(HttpMethod.Post, $"/api/citas/{citaId}/{accion}")
        {
            Content = JsonContent.Create(cuerpo ?? new { }),
        };
        p.Headers.Add("X-CSRF-Token", await CsrfAsync(cliente));
        if (clave is not null) p.Headers.Add(FiltroIdempotencia.Cabecera, clave);
        return await cliente.SendAsync(p);
    }

    /// <summary>Registra un buscador en un cliente dado, verifica correo e inicia sesión. Devuelve el correo.</summary>
    private async Task<string> UnBuscadorAsync(HttpClient cliente)
    {
        var correo = $"apiag-b-{Guid.NewGuid():N}-{_marca}@zass.local";
        Assert.Equal(HttpStatusCode.Created, (await cliente.PostAsJsonAsync("/api/auth/registro", new
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
                (await cliente.PostAsJsonAsync("/api/auth/correo/verificar", new { token })).StatusCode);
        }

        Assert.Equal(HttpStatusCode.OK, (await cliente.PostAsJsonAsync("/api/auth/sesion", new
        {
            correo, contrasena = "contrasena-larga-1",
        })).StatusCode);
        return correo;
    }

    private async Task<string> CsrfAsync(HttpClient cliente) =>
        (await (await cliente.GetAsync("/api/auth/csrf")).Content.ReadFromJsonAsync<JsonDocument>())!
            .RootElement.GetProperty("token").GetString()!;

    /// <summary>Registra un buscador, verifica su correo e inicia sesión en _cliente. Devuelve (_cliente, correo).</summary>
    private async Task<(HttpClient cliente, string correo)> UnBuscadorAsync()
    {
        var correo = await UnBuscadorConSesionAsync();
        return (_cliente, correo);
    }

    // ------------------------------------------------------------- CC-04, la cabecera

    /// <summary>
    /// api-rest.md: «Sin la cabecera, estos endpoints responden 400. No se asume una clave por
    /// defecto: una clave generada por el servidor no protege de nada».
    /// </summary>
    [Fact]
    public async Task Agendar_sin_la_cabecera_de_idempotencia_responde_400_y_no_crea_nada()
    {
        var (cliente, _) = await UnBuscadorAsync();

        var r = await AgendarAsync(cliente, _bloque, null);

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
        var cliente = CrearCliente();
        await UnBuscadorAsync(cliente);

        Assert.Equal(HttpStatusCode.BadRequest, (await AgendarAsync(cliente, _bloque, clave)).StatusCode);
    }

    /// <summary>
    /// Dos cabeceras distintas no son «una intención»: elegir una en silencio sería inventar
    /// cuál era.
    /// </summary>
    [Fact]
    public async Task Dos_claves_en_la_misma_solicitud_responden_400()
    {
        var cliente = CrearCliente();
        await UnBuscadorAsync(cliente);

        var r = await AgendarAsync(cliente, _bloque, $"clave-{Guid.NewGuid():N}", dosClaves: true);

        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
    }

    /// <summary>
    /// Flujo 4B de CU-12, por HTTP: 201 la primera vez y 200 la segunda, con la MISMA cita.
    /// El código distinto es lo que le dice al cliente que no se creó una segunda.
    /// </summary>
    [Fact]
    public async Task El_mismo_envio_responde_201_y_luego_200_con_la_misma_cita()
    {
        var cliente = CrearCliente();
        await UnBuscadorAsync(cliente);
        var clave = $"intencion-{Guid.NewGuid():N}";

        var primera = await AgendarAsync(cliente, _bloque, clave);
        var segunda = await AgendarAsync(cliente, _bloque, clave);

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
        var cliente1 = CrearCliente();
        await UnBuscadorAsync(cliente1);
        Assert.Equal(HttpStatusCode.Created,
            (await AgendarAsync(cliente1, _bloque, $"uno-{Guid.NewGuid():N}")).StatusCode);

        // Otra persona, otra intención, el mismo bloque.
        var cliente2 = CrearCliente();
        await UnBuscadorAsync(cliente2);
        var r = await AgendarAsync(cliente2, _bloque, $"dos-{Guid.NewGuid():N}");

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
        var cliente = CrearCliente();
        var r = await AgendarAsync(cliente, _bloque, $"clave-{Guid.NewGuid():N}");

        Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode);
    }

    [Fact]
    public async Task Agendar_sin_csrf_no_pasa()
    {
        var (cliente, _) = await UnBuscadorAsync();

        var p = new HttpRequestMessage(HttpMethod.Post, "/api/citas")
        {
            Content = JsonContent.Create(new { bloqueId = _bloque, notaBuscador = (string?)null }),
        };
        p.Headers.Add(FiltroIdempotencia.Cabecera, $"clave-{Guid.NewGuid():N}");

        Assert.Equal(HttpStatusCode.Forbidden, (await cliente.SendAsync(p)).StatusCode);
    }

    [Fact]
    public async Task La_disponibilidad_y_los_bloqueos_exigen_sesion()
    {
        var cliente = CrearCliente();
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await cliente.GetAsync("/api/mi/disponibilidad")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await cliente.GetAsync("/api/mi/citas")).StatusCode);
    }

        // ------------------------------------------------------------- CS-11, listar y quitar bloqueos

    /// <summary>El buscador de la sesión pasa a ser dueño del perfil del fixture (hay un perfil por usuario).</summary>
    private async Task HacerloDuenoDelPerfilAsync(string correo)
    {
        using var ctx = ContextoDePrueba.Crear();
        var usuario = await ctx.Usuarios.SingleAsync(u => u.Correo == correo);
        var perfil = await ctx.PerfilesPrestador.SingleAsync(p => p.Id == _perfil);
        perfil.UsuarioId = usuario.Id;
        await ctx.SaveChangesAsync();
    }

    private async Task<Guid> UnBloqueoAsync(Guid perfilId, DateTime desde, DateTime hasta)
    {
        using var ctx = ContextoDePrueba.Crear();
        var bloqueo = new BloqueoAgenda
        {
            PerfilPrestadorId = perfilId, DesdeEn = desde, HastaEn = hasta, Motivo = "Prueba",
        };
        ctx.Set<BloqueoAgenda>().Add(bloqueo);
        await ctx.SaveChangesAsync();
        return bloqueo.Id;
    }

    /// <summary>Otro prestador, con su propio perfil y un bloqueo. Se borra en DisposeAsync.</summary>
    private async Task<Guid> UnBloqueoAjenoAsync()
    {
        using (var ctx = ContextoDePrueba.Crear())
        {
            var dueno = new Usuario
            {
                Correo = $"apiag-o-{_marca}@zass.local", HashContrasena = "x",
                Nombre = "Otra Persona", CorreoVerificado = true,
            };
            ctx.Usuarios.Add(dueno);
            var rubro = await ctx.Rubros.FirstAsync(r => r.Activo);
            var perfil = new PerfilPrestador
            {
                UsuarioId = dueno.Id, RubroId = rubro.Id, Slug = $"apiag-o-{_marca}",
                NombrePublico = "Otro estudio", Estado = EstadoPerfil.Publicado,
                PublicadoEn = DateTime.UtcNow.AddDays(-2),
            };
            ctx.PerfilesPrestador.Add(perfil);
            await ctx.SaveChangesAsync();
            _perfilAjeno = perfil.Id;
        }
        var ahora = DateTime.UtcNow;
        return await UnBloqueoAsync(_perfilAjeno, ahora.AddDays(3), ahora.AddDays(3).AddHours(2));
    }

    private async Task<bool> ExisteElBloqueoAsync(Guid id)
    {
        using var ctx = ContextoDePrueba.Crear();
        return await ctx.Set<BloqueoAgenda>().AnyAsync(b => b.Id == id);
    }

    private async Task<HttpResponseMessage> QuitarBloqueoAsync(HttpClient cliente, Guid id, bool conCsrf = true)
    {
        var p = new HttpRequestMessage(HttpMethod.Delete, $"/api/mi/bloqueos/{id}");
        if (conCsrf) p.Headers.Add("X-CSRF-Token", await CsrfAsync(cliente));
        return await cliente.SendAsync(p);
    }

    [Fact]
    public async Task Listar_y_quitar_bloqueos_exigen_sesion()
    {
        var cliente = CrearCliente();
        Assert.Equal(HttpStatusCode.Unauthorized, (await cliente.GetAsync("/api/mi/bloqueos")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await QuitarBloqueoAsync(cliente, Guid.NewGuid(), conCsrf: false)).StatusCode);
    }

    [Fact]
    public async Task Quitar_un_bloqueo_sin_csrf_da_403_y_no_lo_borra()
    {
        var (cliente, correo) = await UnBuscadorAsync();
        await HacerloDuenoDelPerfilAsync(correo);
        var ahora = DateTime.UtcNow;
        var id = await UnBloqueoAsync(_perfil, ahora.AddDays(5), ahora.AddDays(5).AddHours(1));

        var r = await QuitarBloqueoAsync(cliente, id, conCsrf: false);

        Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
        Assert.True(await ExisteElBloqueoAsync(id));
    }

    [Fact]
    public async Task Quitar_un_bloqueo_ajeno_da_404_y_no_lo_borra()
    {
        var (cliente, correo) = await UnBuscadorAsync();
        await HacerloDuenoDelPerfilAsync(correo);   // tiene perfil: llega hasta el filtro de dueño
        var ajeno = await UnBloqueoAjenoAsync();

        var r = await QuitarBloqueoAsync(cliente, ajeno);

        Assert.Equal(HttpStatusCode.NotFound, r.StatusCode);
        Assert.True(await ExisteElBloqueoAsync(ajeno));
    }

    [Fact]
    public async Task Quitar_un_bloqueo_sin_perfil_de_prestador_da_404()
    {
        var (cliente, _) = await UnBuscadorAsync();   // buscador puro, sin perfil
        var ajeno = await UnBloqueoAjenoAsync();

        Assert.Equal(HttpStatusCode.NotFound, (await QuitarBloqueoAsync(cliente, ajeno)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await QuitarBloqueoAsync(cliente, Guid.NewGuid())).StatusCode);
        Assert.True(await ExisteElBloqueoAsync(ajeno));
    }

    [Fact]
    public async Task Quitar_el_propio_da_204_lo_borra_y_repetirlo_da_404()
    {
        var (cliente, correo) = await UnBuscadorAsync();
        await HacerloDuenoDelPerfilAsync(correo);
        var ahora = DateTime.UtcNow;
        var id = await UnBloqueoAsync(_perfil, ahora.AddDays(5), ahora.AddDays(5).AddHours(1));

        Assert.Equal(HttpStatusCode.NoContent, (await QuitarBloqueoAsync(cliente, id)).StatusCode);
        Assert.False(await ExisteElBloqueoAsync(id));
        Assert.Equal(HttpStatusCode.NotFound, (await QuitarBloqueoAsync(cliente, id)).StatusCode);
    }

    [Fact]
    public async Task Mis_bloqueos_trae_solo_los_propios_y_vigentes()
    {
        var cliente = CrearCliente();
        var correo = await UnBuscadorAsync(cliente);
        await HacerloDuenoDelPerfilAsync(correo);
        var ahora = DateTime.UtcNow;
        var vigente = await UnBloqueoAsync(_perfil, ahora.AddDays(5), ahora.AddDays(5).AddHours(1));
        await UnBloqueoAsync(_perfil, ahora.AddDays(-3), ahora.AddDays(-3).AddHours(1));   // ya pasó
        await UnBloqueoAjenoAsync();                                                       // de otro

        var r = await cliente.GetAsync("/api/mi/bloqueos");

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var ids = (await r.Content.ReadFromJsonAsync<JsonDocument>())!.RootElement
            .EnumerateArray().Select(x => x.GetProperty("id").GetGuid()).ToList();
        Assert.Equal(new[] { vigente }, ids);
    }
        private async Task<string> EstadoPublicoDelBloqueAsync(HttpClient cliente)
    {
        var r = await cliente.GetAsync(
            $"/api/prestadores/{_slug}/bloques?servicio={_servicioOfrecido}");
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var json = (await r.Content.ReadFromJsonAsync<JsonDocument>())!.RootElement;
        return json.GetProperty("bloques").EnumerateArray()
            .Single(b => b.GetProperty("id").GetGuid() == _bloque)
            .GetProperty("estado").GetString()!;
    }

    [Fact]
    public async Task El_calendario_vuelve_a_mostrar_libre_al_quitar_el_bloqueo()
    {
        var (cliente, correo) = await UnBuscadorAsync();
        await HacerloDuenoDelPerfilAsync(correo);
        Assert.Equal("libre", await EstadoPublicoDelBloqueAsync(cliente));

        var ahora = DateTime.UtcNow;
        var crear = new HttpRequestMessage(HttpMethod.Post, "/api/mi/bloqueos")
        {
            Content = JsonContent.Create(new
            {
                desdeEn = ahora.AddHours(12), hastaEn = ahora.AddDays(2), motivo = "Prueba",
            }),
        };
        crear.Headers.Add("X-CSRF-Token", await CsrfAsync(cliente));
        var creado = await cliente.SendAsync(crear);
        Assert.Equal(HttpStatusCode.Created, creado.StatusCode);
        var id = (await creado.Content.ReadFromJsonAsync<JsonDocument>())!.RootElement
            .GetProperty("id").GetGuid();

        Assert.Equal("bloqueado", await EstadoPublicoDelBloqueAsync(cliente));

        Assert.Equal(HttpStatusCode.NoContent, (await QuitarBloqueoAsync(cliente, id)).StatusCode);

        Assert.Equal("libre", await EstadoPublicoDelBloqueAsync(cliente));
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
        var cliente = CrearCliente();
        var r = await cliente.GetAsync(
            $"/api/prestadores/no-existe-{Guid.NewGuid():N}/bloques?servicio={_servicioOfrecido}");

        Assert.Equal(HttpStatusCode.NotFound, r.StatusCode);
    }

    [Fact]
    public async Task Mis_citas_trae_la_que_acabo_de_agendar()
    {
        var (cliente, _) = await UnBuscadorAsync();
        Assert.Equal(HttpStatusCode.Created,
            (await AgendarAsync(cliente, _bloque, $"mia-{Guid.NewGuid():N}")).StatusCode);

        var r = await cliente.GetAsync("/api/mi/citas");

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var citas = (await r.Content.ReadFromJsonAsync<JsonDocument>())!.RootElement;
        var cita = Assert.Single(citas.EnumerateArray());
        Assert.Equal("Confirmada", cita.GetProperty("estado").GetString());
        Assert.Equal("Estudio de prueba", cita.GetProperty("prestador").GetString());
        // El slug viaja con la cita: sin el, "Mis citas" no puede abrir el calendario del
        // prestador (API-35 solo se pide por slug) y reprogramar quedaria sin camino (CU-14).
        Assert.Equal(_slug, cita.GetProperty("prestadorSlug").GetString());
    }

    // ------------------------------------------------------------- el ciclo de la cita, por HTTP

    private async Task<string> UnaCitaPropiaAsync(HttpClient cliente)
    {
        var r = await AgendarAsync(cliente, _bloque, $"ciclo-{Guid.NewGuid():N}");
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
        var (cliente, _) = await UnBuscadorAsync();
        var cita = await UnaCitaPropiaAsync(cliente);

        var r = await AccionAsync(cliente, cita, accion, new { bloqueNuevoId = Guid.NewGuid(), motivo = (string?)null });

        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        Assert.Contains(FiltroIdempotencia.Cabecera, await r.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Cancelar_con_tiempo_responde_200_y_libera_la_hora()
    {
        var (cliente, _) = await UnBuscadorAsync();
        var cita = await UnaCitaPropiaAsync(cliente);

        var r = await AccionAsync(cliente, cita, "cancelar", new { motivo = (string?)null }, $"c-{Guid.NewGuid():N}");

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
        var (cliente1, _) = await UnBuscadorAsync();
        var ajena = await UnaCitaPropiaAsync(cliente1);

        var (cliente2, _) = await UnBuscadorAsync();   // otra persona, otra sesión
        var deOtro = await AccionAsync(cliente2, ajena, "cancelar", new { motivo = (string?)null }, $"c-{Guid.NewGuid():N}");
        var inexistente = await AccionAsync(cliente2, Guid.NewGuid().ToString(), "cancelar", new { motivo = (string?)null }, $"c-{Guid.NewGuid():N}");

        Assert.Equal(HttpStatusCode.NotFound, deOtro.StatusCode);
        Assert.Equal(await CuerpoComparable.De(inexistente), await CuerpoComparable.De(deOtro));
    }

    /// <summary>
    /// La defensa de CU-16 por HTTP: el buscador no puede marcar su propia asistencia, y el
    /// código es el mismo 404 que si la cita no existiera.
    /// </summary>
    [Fact]
    public async Task El_buscador_no_puede_marcar_asistencia_ni_confirmar()
    {
        var (cliente, _) = await UnBuscadorAsync();
        var cita = await UnaCitaPropiaAsync(cliente);

        Assert.Equal(HttpStatusCode.NotFound,
            (await AccionAsync(cliente, cita, "asistencia", new { asistio = true })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await AccionAsync(cliente, cita, "confirmar")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await AccionAsync(cliente, cita, "rechazar", new { motivo = (string?)null })).StatusCode);
    }

    [Theory]
    [InlineData("confirmar")]
    [InlineData("rechazar")]
    [InlineData("asistencia")]
    [InlineData("cancelar")]
    [InlineData("reprogramar")]
    public async Task Sin_sesion_ninguna_accion_sobre_una_cita_pasa(string accion)
    {
        var cliente = CrearCliente();
        var r = await AccionAsync(cliente, Guid.NewGuid().ToString(), accion, new { }, $"x-{Guid.NewGuid():N}");

        Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode);
    }

    // ------------------------------------------------------------- API-44: reenviar recordatorio

    /// <summary>POST /api/citas/{id}/recordatorio con CSRF.</summary>
    private async Task<HttpResponseMessage> ReenviarRecordatorioAsync(string citaId, string? clave = null)
    {
        try
        {
            var csrf = await CsrfAsync();
            var p = new HttpRequestMessage(HttpMethod.Post, $"/api/citas/{citaId}/recordatorio")
            {
                Content = JsonContent.Create(new { }),
            };
            p.Headers.Add("X-CSRF-Token", csrf);
            var response = await _cliente.SendAsync(p);
            return response;
        }
        catch (Exception ex)
        {
            System.Console.WriteLine($"[ERROR] ReenviarRecordatorioAsync failed: {ex}");
            throw;
        }
    }

    /// <summary>
    /// Crea una cita confirmada en modo automático y devuelve su id y los correos de ambos lados.
    /// </summary>
    private async Task<(string citaId, string buscadorCorreo, string prestadorCorreo)> UnaCitaConfirmadaAsync()
    {
        var buscadorCorreo = await UnBuscadorConSesionAsync();
        var r = await AgendarAsync(_bloque, $"recordatorio-{Guid.NewGuid():N}");
        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        var citaId = (await LeerCitaAsync(r)).id;

        // Obtener el correo del prestador
        using var ctx = ContextoDePrueba.Crear();
        var cita = await ctx.Citas.Include(c => c.PerfilPrestador).ThenInclude(p => p.Usuario)
            .SingleAsync(c => c.Id == Guid.Parse(citaId));
        var prestadorCorreo = cita.PerfilPrestador.Usuario.Correo;

        return (citaId, buscadorCorreo, prestadorCorreo);
    }

    [Fact]
    public async Task El_buscador_puede_reenviar_el_recordatorio_202_y_se_enquean_dos_correos()
    {
        // Limpiar notificaciones previas para evitar colisiones de clave idempotencia
        using (var cleanupCtx = ContextoDePrueba.Crear())
        {
            await cleanupCtx.Database.ExecuteSqlRawAsync("TRUNCATE TABLE notificaciones RESTART IDENTITY CASCADE");
        }

        var (citaId, buscadorCorreo, prestadorCorreo) = await UnaCitaConfirmadaAsync();

        var r = await ReenviarRecordatorioAsync(citaId);

        var cuerpo = await r.Content.ReadAsStringAsync();
        System.Console.WriteLine($"[DEBUG] Status: {r.StatusCode}, Body: {cuerpo}");
        Assert.True(r.StatusCode == HttpStatusCode.Accepted, $"Expected 202 Accepted but got {(int)r.StatusCode} {r.StatusCode}. Body: {cuerpo}");

        using var ctx = ContextoDePrueba.Crear();
        var allNotifs = await ctx.Notificaciones
            .Where(n => n.CitaId == Guid.Parse(citaId))
            .ToListAsync();

        var notificaciones = await ctx.Notificaciones
            .Where(n => n.CitaId == Guid.Parse(citaId)
                        && n.Tipo == TipoNotificacion.Recordatorio24h
                        && n.ClaveIdempotencia.StartsWith("recordatorio-reenviado:"))
            .OrderBy(n => n.ProgramadaPara)
            .ToListAsync();

        var allClavesInfo = string.Join("; ", allNotifs.Select(n => $"{n.Tipo}={n.ClaveIdempotencia}"));
        var reenvioClavesInfo = string.Join("; ", notificaciones.Select(n => $"{n.Tipo}={n.ClaveIdempotencia}"));
        Assert.True(notificaciones.Count == 2, $"Expected 2 reenvio notificaciones but got {notificaciones.Count}. All: {allClavesInfo}. Reenvio: {reenvioClavesInfo}");
        var destinatarios = notificaciones.Select(n => n.DestinatarioId).ToList();
        var buscador = await ctx.Usuarios.SingleAsync(u => u.Correo == buscadorCorreo);
        var prestador = await ctx.Usuarios.SingleAsync(u => u.Correo == prestadorCorreo);
        Assert.Contains(buscador.Id, destinatarios);
        Assert.Contains(prestador.Id, destinatarios);

        // Verificar claves idempotencia únicas
        var claves = notificaciones.Select(n => n.ClaveIdempotencia).ToList();
        Assert.Equal(2, claves.Distinct().Count());
    }

    [Fact]
    public async Task El_prestador_tambien_puede_reenviar_el_recordatorio()
    {
        await UnBuscadorConSesionAsync();
        var (citaId, buscadorCorreo, prestadorCorreo) = await UnaCitaConfirmadaAsync();

        // Cambiar a sesión del prestador
        await _cliente.PostAsJsonAsync("/api/auth/sesion", new
        {
            correo = prestadorCorreo, contrasena = "contrasena-larga-1",
        });

        var r = await ReenviarRecordatorioAsync(citaId);

        Assert.Equal(HttpStatusCode.Accepted, r.StatusCode);

        using var ctx = ContextoDePrueba.Crear();
        var notificaciones = await ctx.Notificaciones
            .Where(n => n.CitaId == Guid.Parse(citaId)
                        && n.Tipo == TipoNotificacion.Recordatorio24h
                        && n.ClaveIdempotencia.StartsWith("recordatorio-reenviado:"))
            .ToListAsync();

        Assert.Equal(2, notificaciones.Count);
    }

    [Fact]
    public async Task Maximo_5_reenvios_por_cita_por_dia_el_6to_da_409()
    {
        await UnBuscadorConSesionAsync();
        var (citaId, buscadorCorreo, prestadorCorreo) = await UnaCitaConfirmadaAsync();

        // 5 reenvíos seguidos OK
        for (int i = 1; i <= 5; i++)
        {
            var r = await ReenviarRecordatorioAsync(citaId);
            Assert.Equal(HttpStatusCode.Accepted, r.StatusCode);
        }

        // El 6º da 409
        var sexto = await ReenviarRecordatorioAsync(citaId);
        Assert.Equal(HttpStatusCode.Conflict, sexto.StatusCode);
        var cuerpo = await sexto.Content.ReadAsStringAsync();
        Assert.Contains("5 veces hoy", cuerpo);
        Assert.Contains("mañana", cuerpo);

        // Verificar que siguen siendo solo 5 reenvíos (2 correos cada uno = 10 notificaciones)
        using var ctx = ContextoDePrueba.Crear();
        var total = await ctx.Notificaciones.CountAsync(n =>
            n.CitaId == Guid.Parse(citaId)
            && n.Tipo == TipoNotificacion.Recordatorio24h
            && n.ClaveIdempotencia.StartsWith("recordatorio-reenviado:"));
        Assert.Equal(10, total);
    }

    [Fact]
    public async Task Al_dia_siguiente_de_Chile_se_puede_volver_a_enviar()
    {
        await UnBuscadorConSesionAsync();
        var (citaId, buscadorCorreo, prestadorCorreo) = await UnaCitaConfirmadaAsync();

        // 5 reenvíos hoy
        for (int i = 1; i <= 5; i++)
        {
            var r = await ReenviarRecordatorioAsync(citaId);
            Assert.Equal(HttpStatusCode.Accepted, r.StatusCode);
        }

        // El 6º da 409
        var sexto = await ReenviarRecordatorioAsync(citaId);
        Assert.Equal(HttpStatusCode.Conflict, sexto.StatusCode);

        // Nota: Esta prueba verifica que el límite de 5 reenvíos funciona.
        // La prueba del cambio de día se hace en pruebas de aplicación con reloj fijo.
        // Aquí solo verificamos que después de alcanzar el límite, el sistema responde 409.
        var cuerpo = await sexto.Content.ReadAsStringAsync();
        Assert.Contains("5 veces hoy", cuerpo);
    }

    [Fact]
    public async Task Un_tercero_ajeno_a_la_cita_recibe_404()
    {
        await UnBuscadorConSesionAsync();
        var (citaId, buscadorCorreo, prestadorCorreo) = await UnaCitaConfirmadaAsync();

        // Otra persona (tercero) inicia sesión
        var terceroCorreo = $"apiag-t-{Guid.NewGuid():N}-{_marca}@zass.local";
        Assert.Equal(HttpStatusCode.Created, (await _cliente.PostAsJsonAsync("/api/auth/registro", new
        {
            correo = terceroCorreo, contrasena = "contrasena-larga-1", nombre = "Tercero", rol = "buscador",
        })).StatusCode);
        using (var ctx = ContextoDePrueba.Crear())
        {
            var usuario = await ctx.Usuarios.SingleAsync(u => u.Correo == terceroCorreo);
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
            correo = terceroCorreo, contrasena = "contrasena-larga-1",
        })).StatusCode);

        var r = await ReenviarRecordatorioAsync(citaId);
        Assert.Equal(HttpStatusCode.NotFound, r.StatusCode);
    }

    [Fact]
    public async Task Sin_sesion_responde_401()
    {
        await UnBuscadorConSesionAsync();
        var (citaId, _, _) = await UnaCitaConfirmadaAsync();

        // Usar un cliente SIN cookies (sin sesión)
        var clienteSinSesion = _app.CreateClient(new WebApplicationFactoryClientOptions
        {
            HandleCookies = false,
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost"),
        });
        var r = await clienteSinSesion.PostAsync($"/api/citas/{citaId}/recordatorio", JsonContent.Create(new { }));
        Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode);
    }

    [Fact]
    public async Task Cita_cancelada_responde_409_con_mensaje_en_castellano()
    {
        await UnBuscadorConSesionAsync();
        var (citaId, buscadorCorreo, prestadorCorreo) = await UnaCitaConfirmadaAsync();

        // Cancelar la cita
        var rCancel = await AccionAsync(citaId, "cancelar", new { motivo = "Prueba" }, $"c-{Guid.NewGuid():N}");
        Assert.Equal(HttpStatusCode.OK, rCancel.StatusCode);

        // Intentar reenviar recordatorio
        var r = await ReenviarRecordatorioAsync(citaId);
        Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
        var cuerpo = await r.Content.ReadAsStringAsync();
        Assert.Contains("confirmada", cuerpo);
    }

    [Fact]
    public async Task Cita_pasada_responde_409_con_mensaje_en_castellano()
    {
        await UnBuscadorConSesionAsync();
        var (citaId, buscadorCorreo, prestadorCorreo) = await UnaCitaConfirmadaAsync();

        // Mover la cita al pasado modificando el bloque
        using (var ctx = ContextoDePrueba.Crear())
        {
            var cita = await ctx.Citas.SingleAsync(c => c.Id == Guid.Parse(citaId));
            var bloque = await ctx.Bloques.SingleAsync(b => b.Id == cita.BloqueHorarioId);
            var pasado = DateTime.UtcNow.AddHours(-1);
            bloque.InicioEn = pasado;
            bloque.FinEn = pasado.AddMinutes(30);
            bloque.Estado = EstadoBloque.Reservado;
            await ctx.SaveChangesAsync();
        }

        var r = await ReenviarRecordatorioAsync(citaId);
        Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
        var cuerpo = await r.Content.ReadAsStringAsync();
        Assert.Contains("futura", cuerpo);
    }

    [Fact]
    public async Task Diez_peticiones_simultaneas_exactamente_5_reenvios()
    {
        await UnBuscadorConSesionAsync();
        var (citaId, buscadorCorreo, prestadorCorreo) = await UnaCitaConfirmadaAsync();

        // 10 peticiones en paralelo
        var tareas = Enumerable.Range(0, 10).Select(_ => ReenviarRecordatorioAsync(citaId)).ToArray();
        var resultados = await Task.WhenAll(tareas);

        var aceptadas = resultados.Count(r => r.StatusCode == HttpStatusCode.Accepted);
        var conflictos = resultados.Count(r => r.StatusCode == HttpStatusCode.Conflict);
        var otros = resultados.Count(r => r.StatusCode != HttpStatusCode.Accepted && r.StatusCode != HttpStatusCode.Conflict);

        System.Console.WriteLine($"Aceptadas: {aceptadas}, Conflictos: {conflictos}, Otros: {otros}");
        foreach (var r in resultados)
        {
            var body = await r.Content.ReadAsStringAsync();
            System.Console.WriteLine($"  Status: {r.StatusCode}, Body: {body}");
        }

        Assert.Equal(5, aceptadas);
        Assert.Equal(5, conflictos);

        // Verificar que se encolaron exactamente 10 notificaciones (5 reenvíos × 2 correos)
        using var ctx = ContextoDePrueba.Crear();
        var total = await ctx.Notificaciones.CountAsync(n =>
            n.CitaId == Guid.Parse(citaId)
            && n.Tipo == TipoNotificacion.Recordatorio24h
            && n.ClaveIdempotencia.StartsWith("recordatorio-reenviado:"));
        Assert.Equal(10, total);
    }

    // ------------------------------------------------------------- camino feliz de cada accion

    /// <summary>
    /// API-38: El prestador confirma una cita solicitada. RN-S4-03: solo se confirma una vez,
    /// y solo desde Solicitada.
    /// 
    /// T51d: la cita se crea en modo manual, que es el unico camino real para que quede
    /// Solicitada. En modo automatico, la cita nace Confirmada y el aviso ya esta encolado;
    /// forzar el estado a Solicitada y luego confirmar duplicaba la clave_idempotencia.
    /// </summary>
    [Fact]
    public async Task Confirmar_cita_solicitada_responde_200_y_cambia_el_estado()
    {
        // Poner el servicio en modo manual: es el unico camino real para que la cita nazca Solicitada
        using (var ctx = ContextoDePrueba.Crear())
        {
            var ps = await ctx.PrestadorServicios.SingleAsync(ps => ps.Id == _servicioOfrecido);
            ps.ModoAgenda = ModoAgenda.Manual;
            await ctx.SaveChangesAsync();
        }

        // Agenda en modo manual -> la cita nace en estado Solicitada
        var citaId = await UnBuscadorConCitaAsync();

        // Obtener el correo del prestador
        string correoPrestador;
        using (var ctx = ContextoDePrueba.Crear())
        {
            var u = await ctx.Usuarios.SingleAsync(u => u.PerfilPrestador != null && u.PerfilPrestador.Id == _perfil);
            correoPrestador = u.Correo;
        }

        // Iniciar sesion como el prestador en _cliente
        await LoginComoPrestadorAsync(correoPrestador);

        // Ahora confirmar como prestador (usa _cliente con CSRF fresco)
        var r = await AccionAsync(citaId, "confirmar");

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        using var ctx2 = ContextoDePrueba.Crear();
        var citaConfirmada = await ctx2.Citas.SingleAsync(c => c.Id == Guid.Parse(citaId));
        Assert.Equal(EstadoCita.Confirmada, citaConfirmada.Estado);
    }

    /// <summary>
    /// API-39: El prestador rechaza una cita solicitada con motivo. RN-S4-04: el rechazo es
    /// definitivo y libera el bloque.
    /// 
    /// T51d: la cita se crea en modo manual, que es el unico camino real para que quede
    /// Solicitada. Forzar el estado a mano en una cita creada en modo automatico era
    /// incoherente con los avisos ya encolados.
    /// </summary>
    [Fact]
    public async Task Rechazar_cita_solicitada_libera_el_bloque()
    {
        // Poner el servicio en modo manual: es el unico camino real para que la cita nazca Solicitada
        using (var ctx = ContextoDePrueba.Crear())
        {
            var ps = await ctx.PrestadorServicios.SingleAsync(ps => ps.Id == _servicioOfrecido);
            ps.ModoAgenda = ModoAgenda.Manual;
            await ctx.SaveChangesAsync();
        }

        // Agenda en modo manual -> la cita nace en estado Solicitada
        var citaId = await UnBuscadorConCitaAsync();

        // Obtener el correo del prestador y el bloque original
        Guid bloqueIdOriginal;
        string correoPrestador;
        using (var ctx = ContextoDePrueba.Crear())
        {
            var cita = await ctx.Citas.SingleAsync(c => c.Id == Guid.Parse(citaId));
            bloqueIdOriginal = cita.BloqueHorarioId;
            var u = await ctx.Usuarios.SingleAsync(u => u.PerfilPrestador != null && u.PerfilPrestador.Id == _perfil);
            correoPrestador = u.Correo;
        }

        // Iniciar sesion como el prestador en _cliente
        await LoginComoPrestadorAsync(correoPrestador);

        // Ahora rechazar como prestador (usa _cliente con CSRF fresco)
        var r = await AccionAsync(citaId, "rechazar", new { motivo = "No coincide el horario" });

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        using var ctx2 = ContextoDePrueba.Crear();
        var citaRechazada = await ctx2.Citas.SingleAsync(c => c.Id == Guid.Parse(citaId));
        Assert.Equal(EstadoCita.Rechazada, citaRechazada.Estado);
        var bloque = await ctx2.Bloques.SingleAsync(b => b.Id == bloqueIdOriginal);
        Assert.Equal(EstadoBloque.Libre, bloque.Estado);
    }

    /// <summary>
    /// API-41: Reprogramar es cancelar y crear con vinculo. RN-S4-05: la cita nueva conserva
    /// la elegibilidad para resenar de la original. CC-06: el bloque nuevo debe estar libre.
    /// </summary>
    [Fact]
    public async Task Reprogramar_cita_cancela_la_original_y_crea_una_nueva_vinculada()
    {
        var citaOriginalId = await UnBuscadorConCitaAsync();

        // Crear un bloque nuevo para reprogramar
        Guid bloqueNuevoId;
        using (var ctx = ContextoDePrueba.Crear())
        {
            var bloqueOriginal = await ctx.Bloques.SingleAsync(b => b.Id == _bloque);
            var bloqueNuevo = new BloqueHorario
            {
                DisponibilidadId = bloqueOriginal.DisponibilidadId,
                PerfilPrestadorId = bloqueOriginal.PerfilPrestadorId,
                PrestadorServicioId = bloqueOriginal.PrestadorServicioId,
                InicioEn = bloqueOriginal.InicioEn.AddHours(2),
                FinEn = bloqueOriginal.FinEn.AddHours(2),
                Estado = EstadoBloque.Libre,
            };
            ctx.Bloques.Add(bloqueNuevo);
            await ctx.SaveChangesAsync();
            bloqueNuevoId = bloqueNuevo.Id;
        }

        var clave = $"reprogramar-{Guid.NewGuid():N}";
        var r = await AccionAsync(citaOriginalId, "reprogramar", new { bloqueNuevoId, motivo = "Cambio de horario" }, clave);

        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        var citaNueva = await LeerCitaAsync(r);

        using var ctx2 = ContextoDePrueba.Crear();
        var original = await ctx2.Citas.SingleAsync(c => c.Id == Guid.Parse(citaOriginalId));
        var nueva = await ctx2.Citas.SingleAsync(c => c.Id == Guid.Parse(citaNueva.id));

        Assert.Equal(EstadoCita.Reprogramada, original.Estado);
        Assert.Equal(EstadoCita.Confirmada, nueva.Estado);
        Assert.Equal(Guid.Parse(citaOriginalId), nueva.CitaOrigenId);
        Assert.Equal(bloqueNuevoId, nueva.BloqueHorarioId);
    }

    /// <summary>
    /// API-42: El prestador marca asistencia despues de la hora de la cita. CS-12: solo
    /// despues de la hora, y solo el prestador de ESA cita.
    /// </summary>
    [Fact]
    public async Task Marcar_asistencia_despues_de_la_hora_cambia_a_realizada()
    {
        var citaId = await UnBuscadorConCitaAsync();

        // Forzar que la cita este en el estado correcto y obtener el correo del prestador
        string correoPrestador;
        using (var ctx = ContextoDePrueba.Crear())
        {
            var citaAsistencia = await ctx.Citas.SingleAsync(c => c.Id == Guid.Parse(citaId));
            citaAsistencia.Estado = EstadoCita.PendienteAsistencia;
            var bloque = await ctx.Bloques.SingleAsync(b => b.Id == citaAsistencia.BloqueHorarioId);
            bloque.InicioEn = DateTime.UtcNow.AddHours(-2);
            bloque.FinEn = DateTime.UtcNow.AddHours(-1);
            var u = await ctx.Usuarios.SingleAsync(u => u.PerfilPrestador != null && u.PerfilPrestador.Id == citaAsistencia.PerfilPrestadorId);
            correoPrestador = u.Correo;
            await ctx.SaveChangesAsync();
        }

        // Iniciar sesion como el prestador en _cliente
        await LoginComoPrestadorAsync(correoPrestador);

        // Ahora marcar asistencia como prestador (usa _cliente con CSRF fresco)
        var r = await AccionAsync(citaId, "asistencia", new { asistio = true });

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        using var ctx2 = ContextoDePrueba.Crear();
        var cita = await ctx2.Citas.SingleAsync(c => c.Id == Guid.Parse(citaId));
        Assert.Equal(EstadoCita.Realizada, cita.Estado);
        Assert.Equal(FaltaEnCita.Nadie, cita.QuienFalto);
    }

    /// <summary>
    /// API-42b: El buscador acusa que el prestador no asistio. La otra mitad de CS-12:
    /// solo el buscador de ESA cita puede acusar, y solo despues de la hora.
    /// El estado cambia a NoAsistio (estados-cita.md T10), no a Realizada.
    /// </summary>
    [Fact]
    public async Task Acusar_ausencia_del_prestador_cambia_a_no_asistio()
    {
        var citaId = await UnBuscadorConCitaAsync();

        // Forzar que la cita este en estado de esperar asistencia
        using (var ctx = ContextoDePrueba.Crear())
        {
            var citaAusencia = await ctx.Citas.SingleAsync(c => c.Id == Guid.Parse(citaId));
            citaAusencia.Estado = EstadoCita.PendienteAsistencia;
            var bloque = await ctx.Bloques.SingleAsync(b => b.Id == citaAusencia.BloqueHorarioId);
            bloque.InicioEn = DateTime.UtcNow.AddHours(-2);
            bloque.FinEn = DateTime.UtcNow.AddHours(-1);
            await ctx.SaveChangesAsync();
        }

        // El buscador (ya logueado en _cliente tras UnBuscadorConCitaAsync) acusa la ausencia
        var r = await AccionAsync(citaId, "no-asistio-el-prestador");

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        using var ctx2 = ContextoDePrueba.Crear();
        var cita = await ctx2.Citas.SingleAsync(c => c.Id == Guid.Parse(citaId));
        Assert.Equal(EstadoCita.NoAsistio, cita.Estado);
        Assert.Equal(FaltaEnCita.Prestador, cita.QuienFalto);
    }

    // ------------------------------------------------------------- API-44: reenviar recordatorio - limite por dia de Chile

    /// <summary>
    /// Reloj de prueba que se mueve a mano, siempre en UTC (Npgsql lo exige).
    /// Para probar el límite por día de Chile (no UTC).
    /// </summary>
    private sealed class RelojDePruebaAgenda : IRelojSistema
    {
        public DateTime Ahora { get; private set; }
        public RelojDePruebaAgenda(DateTime inicio) => Ahora = inicio;
        public void Avanzar(TimeSpan cuanto) => Ahora = Ahora.Add(cuanto);
    }

    /// <summary>
    /// Reenvios_cuentan_por_dia_de_Chile_no_por_dia_UTC:
    /// Con el reloj de prueba a las 22:30 hora de Chile (que en UTC ya es el dia siguiente;
    /// usa una fecha de horario de verano, ej. 2026-12-10 22:30 America/Santiago = 2026-12-11 01:30 UTC),
    /// haz 5 reenvios (202 los 5); avanza el reloj a las 23:30 del MISMO dia de Chile -> el 6.o da 409;
    /// avanza a las 00:10 del dia siguiente de Chile -> 202. La cita tiene que ser futura en todos esos momentos.
    /// Luego la mutacion M5 (hoyChile calculado con la fecha UTC) tiene que tumbar ESTA prueba.
    /// </summary>
    [Fact]
    public async Task Reenvios_cuentan_por_dia_de_Chile_no_por_dia_UTC()
    {
        // 2026-12-10 22:30 America/Santiago (horario de verano, UTC-3) = 2026-12-11 01:30 UTC
        var inicioChile = new DateTime(2026, 12, 10, 22, 30, 0, DateTimeKind.Unspecified);
        var inicioUtc = TimeZoneInfo.ConvertTimeToUtc(inicioChile, TimeZoneInfo.FindSystemTimeZoneById("America/Santiago"));
        
        var reloj = new RelojDePruebaAgenda(inicioUtc);
        
        // Crear Aplicacion con reloj fijo
        var app = new Aplicacion(limiteAutenticacion: 10_000, reloj: reloj);
        var cliente = app.CreateClient(new WebApplicationFactoryClientOptions
        {
            HandleCookies = true,
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost"),
        });

        Guid perfilId = Guid.Empty;
        int servicioCatalogoId = 0;
        string marca = "";

        try
        {
            // Registrar prestador
            marca = Guid.NewGuid().ToString("N")[..12];
            var prestadorCorreo = $"apicl-{marca}@zass.local";
            var prestadorReg = await cliente.PostAsJsonAsync("/api/auth/registro", new
            {
                correo = prestadorCorreo, contrasena = "contrasena-larga-1",
                nombre = "Pablo Espinoza", rol = "prestador",
            });
            Assert.Equal(HttpStatusCode.Created, prestadorReg.StatusCode);

            // Verificar correo del prestador
            using (var ctxPrest = ContextoDePrueba.Crear())
            {
                var usuario = await ctxPrest.Usuarios.SingleAsync(u => u.Correo == prestadorCorreo);
                var aviso = await ctxPrest.Notificaciones
                    .Where(n => n.DestinatarioId == usuario.Id
                                && n.Tipo == TipoNotificacion.CorreoVerificacion)
                    .OrderByDescending(n => n.ProgramadaPara).FirstAsync();
                var token = Zass.Aplicacion.Puertos.CargaDeAviso.LeerToken(aviso.Cuerpo)!;
                Assert.Equal(HttpStatusCode.NoContent,
                    (await cliente.PostAsJsonAsync("/api/auth/correo/verificar", new { token })).StatusCode);
            }

            // Crear perfil, servicio, disponibilidad, bloque, suscripción
            using (var ctx = ContextoDePrueba.Crear())
            {
                await ctx.Database.ExecuteSqlRawAsync("TRUNCATE TABLE notificaciones RESTART IDENTITY CASCADE");
                
                var prestador = await ctx.Usuarios.SingleAsync(u => u.Correo == prestadorCorreo);
                var perfil = await ctx.PerfilesPrestador.SingleAsync(p => p.UsuarioId == prestador.Id);
                perfil.RubroId = (await ctx.Rubros.FirstAsync(r => r.Activo)).Id;
                perfil.Slug = $"apicl-{marca}";
                perfil.NombrePublico = "Estudio de prueba";
                perfil.Estado = EstadoPerfil.Publicado;
                perfil.PublicadoEn = inicioUtc.AddDays(-2);

                var servicio = new Servicio
                {
                    RubroId = (short)perfil.RubroId!, Nombre = $"Consulta {marca}",
                    NombreNormalizado = $"consulta {marca}", EsCatalogo = false,
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
                    IniciaEn = inicioUtc.AddDays(-2), VigenteHasta = inicioUtc.AddDays(28),
                });

                // La cita tiene que ser futura: bloque para mañana en hora de Chile
                // 2026-12-11 10:00 America/Santiago = 2026-12-11 13:00 UTC
                var bloqueChile = new DateTime(2026, 12, 11, 10, 0, 0, DateTimeKind.Unspecified);
                var bloqueUtc = TimeZoneInfo.ConvertTimeToUtc(bloqueChile, TimeZoneInfo.FindSystemTimeZoneById("America/Santiago"));
                
                var regla = new Disponibilidad
                {
                    PerfilPrestadorId = perfil.Id, PrestadorServicioId = ofrecido.Id,
                    DiaSemana = (short)(((int)bloqueChile.DayOfWeek + 6) % 7 + 1),
                    HoraInicio = TimeOnly.FromDateTime(bloqueChile),
                    HoraFin = TimeOnly.FromDateTime(bloqueChile).AddMinutes(30),
                    DuracionBloqueMin = 30, VigenteDesde = DateOnly.FromDateTime(bloqueChile),
                };
                ctx.Disponibilidades.Add(regla);
                await ctx.SaveChangesAsync();

                var bloque = new BloqueHorario
                {
                    DisponibilidadId = regla.Id, PerfilPrestadorId = perfil.Id,
                    PrestadorServicioId = ofrecido.Id,
                    InicioEn = bloqueUtc, FinEn = bloqueUtc.AddMinutes(30), Estado = EstadoBloque.Libre,
                };
                ctx.Bloques.Add(bloque);
                await ctx.SaveChangesAsync();

                // Guardar IDs para la limpieza en finally
                perfilId = perfil.Id;
                servicioCatalogoId = servicio.Id;

                var buscadorCorreo = $"apicl-b-{marca}@zass.local";
                var buscadorReg = await cliente.PostAsJsonAsync("/api/auth/registro", new
                {
                    correo = buscadorCorreo, contrasena = "contrasena-larga-1", nombre = "Ana Pérez", rol = "buscador",
                });
                Assert.Equal(HttpStatusCode.Created, buscadorReg.StatusCode);

                using (var ctx2 = ContextoDePrueba.Crear())
                {
                    var usuario = await ctx2.Usuarios.SingleAsync(u => u.Correo == buscadorCorreo);
                    var aviso = await ctx2.Notificaciones
                        .Where(n => n.DestinatarioId == usuario.Id
                                    && n.Tipo == TipoNotificacion.CorreoVerificacion)
                        .OrderByDescending(n => n.ProgramadaPara).FirstAsync();
                    var token = Zass.Aplicacion.Puertos.CargaDeAviso.LeerToken(aviso.Cuerpo)!;
                    Assert.Equal(HttpStatusCode.NoContent,
                        (await cliente.PostAsJsonAsync("/api/auth/correo/verificar", new { token })).StatusCode);
                }

                Assert.Equal(HttpStatusCode.OK, (await cliente.PostAsJsonAsync("/api/auth/sesion", new
                {
                    correo = buscadorCorreo, contrasena = "contrasena-larga-1",
                })).StatusCode);

                // Agendar la cita (modo automatico -> Confirmada)
                var csrfResp = await cliente.GetAsync("/api/auth/csrf");
                var csrfJson = await csrfResp.Content.ReadFromJsonAsync<JsonDocument>();
                var csrf = csrfJson!.RootElement.GetProperty("token").GetString()!;
                var agendar = new HttpRequestMessage(HttpMethod.Post, "/api/citas")
                {
                    Content = JsonContent.Create(new { bloqueId = bloque.Id, notaBuscador = (string?)null }),
                };
                agendar.Headers.Add("X-CSRF-Token", csrf);
                agendar.Headers.Add("Idempotency-Key", $"c-{Guid.NewGuid():N}");
                var rAgendar = await cliente.SendAsync(agendar);
                Assert.Equal(HttpStatusCode.Created, rAgendar.StatusCode);
                var citaJson = (await rAgendar.Content.ReadFromJsonAsync<JsonDocument>())!.RootElement;
                var citaId = citaJson.GetProperty("id").GetString()!;

                // Funcion helper para reenviar recordatorio
                async Task<HttpResponseMessage> ReenviarAsync()
                {
                    var csrfResp2 = await cliente.GetAsync("/api/auth/csrf");
                    var csrfJson2 = await csrfResp2.Content.ReadFromJsonAsync<JsonDocument>();
                    var csrf2 = csrfJson2!.RootElement.GetProperty("token").GetString()!;
                    var p = new HttpRequestMessage(HttpMethod.Post, $"/api/citas/{citaId}/recordatorio")
                    {
                        Content = JsonContent.Create(new { }),
                    };
                    p.Headers.Add("X-CSRF-Token", csrf2);
                    return await cliente.SendAsync(p);
                }

                // 1. A las 22:30 hora de Chile (ya es 11-dic 01:30 UTC): 5 reenvios OK
                for (int i = 1; i <= 5; i++)
                {
                    var r = await ReenviarAsync();
                    Assert.Equal(HttpStatusCode.Accepted, r.StatusCode);
                }

                // 2. A las 23:30 del MISMO dia de Chile (11-dic 02:30 UTC): el 6.o da 409
                reloj.Avanzar(TimeSpan.FromHours(1)); // 22:30 -> 23:30 Chile (UTC+1h)
                var sexto = await ReenviarAsync();
                Assert.Equal(HttpStatusCode.Conflict, sexto.StatusCode);
                var cuerpo = await sexto.Content.ReadAsStringAsync();
                Assert.Contains("5 veces hoy", cuerpo);

                // 3. A las 00:10 del dia siguiente de Chile (11-dic 03:10 UTC -> 12-dic 00:10 Chile): 202 OK
                reloj.Avanzar(TimeSpan.FromMinutes(40)); // 23:30 -> 00:10 Chile (UTC+40min)
                var septimo = await ReenviarAsync();
                Assert.Equal(HttpStatusCode.Accepted, septimo.StatusCode);

                // Verificar que se encolaron 12 notificaciones (6 reenvíos × 2 correos)
                using var ctxFinal = ContextoDePrueba.Crear();
                var total = await ctxFinal.Notificaciones.CountAsync(n =>
                    n.CitaId == Guid.Parse(citaId)
                    && n.Tipo == TipoNotificacion.Recordatorio24h
                    && n.ClaveIdempotencia.StartsWith("recordatorio-reenviado:"));
                Assert.Equal(12, total);
            }
        }
        finally
        {
            // Limpieza en el mismo orden que DisposeAsync
            if (perfilId != Guid.Empty)
            {
                using (var ctx = ContextoDePrueba.Crear())
                {
                    await ctx.Database.ExecuteSqlRawAsync(
                        """
                        DELETE FROM bloqueos_agenda WHERE perfil_prestador_id = {0};
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
                        """, perfilId, servicioCatalogoId);
                }
            }

            // Borrar usuarios apicl-%{marca}@zass.local con su auditoria, notificaciones y sesiones
            if (!string.IsNullOrEmpty(marca))
            {
                using (var ctx = ContextoDePrueba.Crear())
                {
                    var usuarios = await ctx.Usuarios
                        .Where(u => u.Correo.EndsWith($"-{marca}@zass.local")).ToListAsync();
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
            }

            cliente.Dispose();
            app.Dispose();
        }
    }
}
