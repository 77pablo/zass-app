using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Zass.Dominio.Comun;
using Zass.Dominio.Identidad;

namespace Zass.Integracion.Pruebas;

/// <summary>
/// MOD-11 por HTTP, contra la API entera y la base real: sesión, CSRF, Idempotency-Key, los códigos de
/// estado, el rol de moderador y, sobre todo, los BYTES de lo que sale hacia fuera. Lo público no puede
/// filtrar quién escribió una reseña, y eso solo se comprueba leyendo la respuesta cruda.
/// </summary>
public class ResenasApiPruebas : IAsyncLifetime
{
    private const string Contrasena = "contrasena-larga-1";
    private const string Texto = "Excelente atención, llegaron puntuales y explicaron todo con claridad.";

    private readonly Aplicacion _app = new();
    private readonly List<string> _correos = new();

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        using (var ctx = ContextoDePrueba.Crear())
        {
            foreach (var correo in _correos)
            {
                var u = await ctx.Usuarios.FirstOrDefaultAsync(x => x.Correo == correo);
                if (u is null) continue;
                await ctx.Auditoria.Where(a => a.ActorId == u.Id || a.EntidadId == u.Id).ExecuteDeleteAsync();
                await ctx.Usuarios.Where(x => x.Id == u.Id).ExecuteDeleteAsync();    // el resto cae en cascada
            }
        }
        _app.Dispose();
    }

    /// <summary>Una persona con su propio cliente y sus propias cookies.</summary>
    private sealed class Persona
    {
        public required HttpClient Http { get; init; }
        public required string Correo { get; init; }
        public Guid UsuarioId { get; set; }
        public Guid? PerfilId { get; set; }
        public string? Slug { get; set; }

        public async Task<string> CsrfAsync()
        {
            var r = await Http.GetAsync("/api/auth/csrf");
            r.EnsureSuccessStatusCode();
            return (await r.Content.ReadFromJsonAsync<JsonDocument>())!.RootElement.GetProperty("token").GetString()!;
        }

        public async Task<HttpResponseMessage> EnviarAsync(
            HttpMethod metodo, string ruta, object? cuerpo = null, string? clave = null, bool conCsrf = true)
        {
            var p = new HttpRequestMessage(metodo, ruta);
            if (conCsrf) p.Headers.Add("X-CSRF-Token", await CsrfAsync());
            if (clave is not null) p.Headers.Add("Idempotency-Key", clave);
            if (cuerpo is not null)
                p.Content = new StringContent(JsonSerializer.Serialize(cuerpo), Encoding.UTF8, "application/json");
            return await Http.SendAsync(p);
        }
    }

    private HttpClient NuevoCliente() => _app.CreateClient(new WebApplicationFactoryClientOptions
    {
        HandleCookies = true, AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost"),
    });

    private Persona Visitante() => new() { Http = NuevoCliente(), Correo = "" };

    private async Task<Persona> UnaPersonaAsync(string rol, bool tambienModerador = false, bool publicado = false)
    {
        var correo = $"api-res-{Guid.NewGuid():N}@zass.local";
        _correos.Add(correo);
        var p = new Persona { Http = NuevoCliente(), Correo = correo };

        var alta = await p.Http.PostAsJsonAsync("/api/auth/registro", new
        {
            correo, contrasena = Contrasena, nombre = "Persona de prueba", rol,
        });
        Assert.Equal(HttpStatusCode.Created, alta.StatusCode);

        using (var ctx = ContextoDePrueba.Crear())
        {
            var usuario = await ctx.Usuarios.SingleAsync(u => u.Correo == correo);
            p.UsuarioId = usuario.Id;
            var aviso = await ctx.Notificaciones
                .Where(n => n.DestinatarioId == usuario.Id && n.Tipo == TipoNotificacion.CorreoVerificacion)
                .OrderByDescending(n => n.ProgramadaPara).FirstAsync();
            var token = aviso.Cuerpo.Split(' ').Last().TrimEnd('.');
            var v = await p.Http.PostAsJsonAsync("/api/auth/correo/verificar", new { token });
            Assert.Equal(HttpStatusCode.NoContent, v.StatusCode);

            if (tambienModerador)
            {
                // El rol interno no se pide al registrarse (API-08 lo rechaza): se asigna en la base.
                ctx.UsuarioRoles.Add(new UsuarioRol
                {
                    UsuarioId = usuario.Id,
                    RolId = (await ctx.Roles.SingleAsync(r => r.Codigo == "moderador")).Id,
                });
                await ctx.SaveChangesAsync();
            }

            if (publicado)
            {
                var perfil = await ctx.PerfilesPrestador.SingleAsync(x => x.UsuarioId == usuario.Id);
                perfil.Estado = EstadoPerfil.Publicado;
                perfil.PublicadoEn = DateTime.UtcNow;
                perfil.RubroId = 1;
                await ctx.SaveChangesAsync();
                p.PerfilId = perfil.Id;
                p.Slug = perfil.Slug;
            }
        }

        // La sesión se abre DESPUÉS de asignar roles: los roles viajan en la cookie.
        var entrada = await p.Http.PostAsJsonAsync("/api/auth/sesion", new { correo, contrasena = Contrasena });
        Assert.Equal(HttpStatusCode.OK, entrada.StatusCode);
        return p;
    }

    private static async Task EnvejecerContactoAsync(Guid contactoId, TimeSpan hace)
    {
        using var ctx = ContextoDePrueba.Crear();
        await ctx.Contactos.Where(c => c.Id == contactoId)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.OcurridoEn, DateTime.UtcNow - hace));
    }

    private static async Task<Guid> IdAsync(HttpResponseMessage r) =>
        (await r.Content.ReadFromJsonAsync<JsonDocument>())!.RootElement.GetProperty("id").GetGuid();

    private async Task<(Persona Autor, Persona Prestador, Guid ContactoId)> ConContactoMaduroAsync()
    {
        var prestador = await UnaPersonaAsync("prestador", publicado: true);
        var autor = await UnaPersonaAsync("buscador");
        var c = await autor.EnviarAsync(HttpMethod.Post, "/api/contactos",
            new { perfilId = prestador.PerfilId, origen = "Ficha", canal = "Whatsapp" });
        Assert.Equal(HttpStatusCode.OK, c.StatusCode);
        var id = await IdAsync(c);
        await EnvejecerContactoAsync(id, TimeSpan.FromDays(8));
        return (autor, prestador, id);
    }

    private static object CuerpoResena(Guid contactoId, int cal = 5, string? texto = null, bool? enviarIgual = null) =>
        new { contactoId, calificacion = cal, comentario = texto ?? Texto, aspectos = new[] { "puntualidad" }, enviarIgual };

    private async Task<Guid> PublicadaAsync(Persona autor, Guid contactoId, int cal = 5)
    {
        var r = await autor.EnviarAsync(HttpMethod.Post, "/api/resenas", CuerpoResena(contactoId, cal), Guid.NewGuid().ToString());
        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        return await IdAsync(r);
    }

    // ------------------------------------------------------------- sesión, CSRF, clave

    [Theory]
    [InlineData("POST", "/api/contactos")]
    [InlineData("GET", "/api/mi/elegibilidad?perfilId=00000000-0000-0000-0000-000000000001")]
    [InlineData("POST", "/api/resenas")]
    [InlineData("PUT", "/api/resenas/00000000-0000-0000-0000-000000000001")]
    [InlineData("POST", "/api/resenas/00000000-0000-0000-0000-000000000001/respuesta")]
    [InlineData("POST", "/api/resenas/00000000-0000-0000-0000-000000000001/reporte")]
    [InlineData("GET", "/api/interno/reportes/cola")]
    [InlineData("POST", "/api/interno/reportes/00000000-0000-0000-0000-000000000001/resolver")]
    public async Task Sin_sesion_todo_responde_401(string metodo, string ruta)
    {
        var v = Visitante();

        var r = await v.EnviarAsync(new HttpMethod(metodo), ruta, metodo == "GET" ? null : new { }, conCsrf: false);

        Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode);
    }

    [Fact]
    public async Task Publicar_sin_token_csrf_responde_403()
    {
        var (autor, _, contactoId) = await ConContactoMaduroAsync();

        var r = await autor.EnviarAsync(HttpMethod.Post, "/api/resenas", CuerpoResena(contactoId), "k", conCsrf: false);

        Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
    }

    [Fact]
    public async Task Publicar_sin_la_cabecera_Idempotency_Key_responde_400_y_no_crea_nada()
    {
        var (autor, _, contactoId) = await ConContactoMaduroAsync();

        var r = await autor.EnviarAsync(HttpMethod.Post, "/api/resenas", CuerpoResena(contactoId), clave: null);

        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        using var ctx = ContextoDePrueba.Crear();
        Assert.False(await ctx.Resenas.AnyAsync(x => x.ContactoId == contactoId));
    }

    [Fact]
    public async Task Un_prestador_sin_cuenta_de_buscador_no_puede_registrar_contactos_ni_reseñar()
    {
        var prestador = await UnaPersonaAsync("prestador", publicado: true);
        var otro = await UnaPersonaAsync("prestador", publicado: true);

        var r = await prestador.EnviarAsync(HttpMethod.Post, "/api/contactos",
            new { perfilId = otro.PerfilId, origen = "Ficha", canal = "Telefono" });

        Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
    }

    // ------------------------------------------------------------- el camino completo

    [Fact]
    public async Task Contactar_esperar_reseñar_responder_y_leer_el_camino_completo_de_CU_16_y_CU_17()
    {
        var prestador = await UnaPersonaAsync("prestador", publicado: true);
        var autor = await UnaPersonaAsync("buscador");

        // API-46: contacto desde la ficha.
        var contacto = await autor.EnviarAsync(HttpMethod.Post, "/api/contactos",
            new { perfilId = prestador.PerfilId, origen = "Ficha", canal = "Whatsapp" });
        Assert.Equal(HttpStatusCode.OK, contacto.StatusCode);
        var contactoId = await IdAsync(contacto);

        // API-47: todavía no. Dice por qué y desde cuándo.
        var antes = (await autor.Http.GetFromJsonAsync<JsonDocument>(
            $"/api/mi/elegibilidad?perfilId={prestador.PerfilId}"))!.RootElement;
        Assert.False(antes.GetProperty("elegible").GetBoolean());
        Assert.Equal("VentanaDeContacto", antes.GetProperty("motivo").GetString());
        Assert.True(antes.GetProperty("puedeDesde").GetDateTime() > DateTime.UtcNow.AddDays(6));

        // API-48 antes de tiempo: 403 con el motivo, y no se crea nada.
        var temprano = await autor.EnviarAsync(HttpMethod.Post, "/api/resenas", CuerpoResena(contactoId), "k-temprano");
        Assert.Equal(HttpStatusCode.Forbidden, temprano.StatusCode);
        Assert.Contains("7 días", await temprano.Content.ReadAsStringAsync());

        // Pasan los 7 días.
        await EnvejecerContactoAsync(contactoId, TimeSpan.FromDays(8));
        var despues = (await autor.Http.GetFromJsonAsync<JsonDocument>(
            $"/api/mi/elegibilidad?perfilId={prestador.PerfilId}"))!.RootElement;
        Assert.True(despues.GetProperty("elegible").GetBoolean());
        Assert.Equal(contactoId, despues.GetProperty("contactoId").GetGuid());

        // API-48: publica. 201, Location y la reseña con su plazo de edición.
        var clave = Guid.NewGuid().ToString();
        var creada = await autor.EnviarAsync(HttpMethod.Post, "/api/resenas", CuerpoResena(contactoId, 4), clave);
        Assert.Equal(HttpStatusCode.Created, creada.StatusCode);
        var cuerpo = (await creada.Content.ReadFromJsonAsync<JsonDocument>())!.RootElement;
        var resenaId = cuerpo.GetProperty("id").GetGuid();
        Assert.Equal($"/api/resenas/{resenaId}", creada.Headers.Location!.OriginalString);
        Assert.Equal(4, cuerpo.GetProperty("calificacion").GetInt32());
        Assert.Equal("Publicada", cuerpo.GetProperty("estado").GetString());
        Assert.True(cuerpo.GetProperty("editableHasta").GetDateTime() > DateTime.UtcNow.AddHours(71));

        // Doble envío: 200 con la MISMA reseña, sin duplicar.
        var repetida = await autor.EnviarAsync(HttpMethod.Post, "/api/resenas", CuerpoResena(contactoId, 4), clave);
        Assert.Equal(HttpStatusCode.OK, repetida.StatusCode);
        Assert.Equal(resenaId, (await repetida.Content.ReadFromJsonAsync<JsonDocument>())!.RootElement.GetProperty("id").GetGuid());

        using (var ctx = ContextoDePrueba.Crear())
        {
            Assert.Equal(1, await ctx.Resenas.CountAsync(x => x.ContactoId == contactoId));
            var perfil = await ctx.PerfilesPrestador.AsNoTracking().SingleAsync(p => p.Id == prestador.PerfilId);
            Assert.Equal(1, perfil.TotalResenas);
            Assert.Equal(4.0m, perfil.PromedioCalificacion);
            Assert.Equal(1, await ctx.Notificaciones.CountAsync(n => n.DestinatarioId == prestador.UsuarioId
                && n.Tipo == TipoNotificacion.ResenaNueva));
        }

        // API-50: el prestador responde (201) y vuelve a responder (200, edita).
        var respuesta = await prestador.EnviarAsync(HttpMethod.Post, $"/api/resenas/{resenaId}/respuesta",
            new { texto = "Muchas gracias por tu comentario." });
        Assert.Equal(HttpStatusCode.Created, respuesta.StatusCode);
        var edicion = await prestador.EnviarAsync(HttpMethod.Post, $"/api/resenas/{resenaId}/respuesta",
            new { texto = "Muchas gracias, quedamos atentos." });
        Assert.Equal(HttpStatusCode.OK, edicion.StatusCode);

        // Lectura pública SIN sesión: la reseña y la respuesta, y NADA de quién la escribió.
        var anonimo = Visitante();
        var publica = await anonimo.Http.GetAsync($"/api/prestadores/{prestador.Slug}/resenas");
        Assert.Equal(HttpStatusCode.OK, publica.StatusCode);
        var crudo = await publica.Content.ReadAsStringAsync();
        Assert.Contains("Excelente atención", crudo);
        Assert.Contains("Muchas gracias, quedamos atentos.", crudo);
        SinIdentidad(crudo, autor, contactoId);
    }

    /// <summary>Lo que no puede aparecer en NINGUNA respuesta pública, buscado en el texto crudo.</summary>
    private static void SinIdentidad(string crudo, Persona autor, Guid contactoId)
    {
        Assert.DoesNotContain(autor.UsuarioId.ToString(), crudo, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(autor.Correo, crudo, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(contactoId.ToString(), crudo, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("autor", crudo, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("correo", crudo, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("estado", crudo, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("editableHasta", crudo, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("contacto", crudo, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("usuario", crudo, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Las_reseñas_de_una_ficha_que_no_existe_o_no_esta_publicada_responden_404()
    {
        var v = Visitante();
        var borrador = await UnaPersonaAsync("prestador");      // sin publicar

        var inexistente = await v.Http.GetAsync("/api/prestadores/no-existe-este-slug/resenas");
        Assert.Equal(HttpStatusCode.NotFound, inexistente.StatusCode);

        using var ctx = ContextoDePrueba.Crear();
        var slug = (await ctx.PerfilesPrestador.AsNoTracking().SingleAsync(p => p.UsuarioId == borrador.UsuarioId)).Slug;
        var sinPublicar = await v.Http.GetAsync($"/api/prestadores/{slug}/resenas");
        Assert.Equal(HttpStatusCode.NotFound, sinPublicar.StatusCode);
    }

    [Fact]
    public async Task Un_cursor_invalido_en_la_lectura_publica_responde_400()
    {
        var prestador = await UnaPersonaAsync("prestador", publicado: true);

        var r = await Visitante().Http.GetAsync($"/api/prestadores/{prestador.Slug}/resenas?cursor=%25%25no");

        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
    }

    // ------------------------------------------------------------- validación y datos personales

    [Theory]
    [InlineData(0, Texto)]
    [InlineData(6, Texto)]
    [InlineData(5, "muy corto")]
    public async Task Calificacion_o_comentario_invalidos_responden_400_sin_crear_nada(int cal, string texto)
    {
        var (autor, _, contactoId) = await ConContactoMaduroAsync();

        var r = await autor.EnviarAsync(HttpMethod.Post, "/api/resenas", CuerpoResena(contactoId, cal, texto), "k");

        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        using var ctx = ContextoDePrueba.Crear();
        Assert.False(await ctx.Resenas.AnyAsync(x => x.ContactoId == contactoId));
    }

    [Fact]
    public async Task Un_comentario_enorme_se_rechaza_en_el_borde_sin_llegar_a_la_base()
    {
        var (autor, _, contactoId) = await ConContactoMaduroAsync();

        var r = await autor.EnviarAsync(HttpMethod.Post, "/api/resenas",
            CuerpoResena(contactoId, 5, new string('a', 100_000)), "k");

        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
    }

    [Fact]
    public async Task Datos_personales_en_el_texto_avisan_con_422_y_enviar_igual_publica()
    {
        var (autor, _, contactoId) = await ConContactoMaduroAsync();
        var texto = "Muy bien todo, pueden llamarlo al +56 9 8765 4321 si necesitan referencias.";

        var aviso = await autor.EnviarAsync(HttpMethod.Post, "/api/resenas", CuerpoResena(contactoId, 5, texto), "k1");
        Assert.Equal(HttpStatusCode.UnprocessableEntity, aviso.StatusCode);
        var cuerpo = (await aviso.Content.ReadFromJsonAsync<JsonDocument>())!.RootElement;
        Assert.True(cuerpo.GetProperty("requiereConfirmacion").GetBoolean());
        Assert.NotEmpty(cuerpo.GetProperty("fragmentos").EnumerateArray());
        using (var ctx = ContextoDePrueba.Crear())
            Assert.False(await ctx.Resenas.AnyAsync(x => x.ContactoId == contactoId));

        var igual = await autor.EnviarAsync(HttpMethod.Post, "/api/resenas", CuerpoResena(contactoId, 5, texto, true), "k1");
        Assert.Equal(HttpStatusCode.Created, igual.StatusCode);
    }

    [Fact]
    public async Task El_html_del_comentario_viaja_crudo_y_nunca_como_marcado()
    {
        var (autor, prestador, contactoId) = await ConContactoMaduroAsync();
        var crudo = "<img src=x onerror=alert(1)> Muy buen servicio, todo claro y a tiempo";

        Assert.Equal(HttpStatusCode.Created,
            (await autor.EnviarAsync(HttpMethod.Post, "/api/resenas", CuerpoResena(contactoId, 5, crudo), "k")).StatusCode);

        var r = await Visitante().Http.GetAsync($"/api/prestadores/{prestador.Slug}/resenas");
        Assert.StartsWith("application/json", r.Content.Headers.ContentType!.MediaType);   // JSON, no HTML
        var texto = (await r.Content.ReadFromJsonAsync<JsonDocument>())!.RootElement
            .GetProperty("resenas")[0].GetProperty("comentario").GetString();
        Assert.Equal(crudo, texto);      // sin alterar: escapar es cosa de quien lo pinta (RN-S5-04)
        Assert.Equal("nosniff", r.Headers.GetValues("X-Content-Type-Options").Single());
    }

    // ------------------------------------------------------------- editar

    [Fact]
    public async Task Editar_funciona_para_el_autor_y_responde_404_a_cualquier_otra_persona()
    {
        var (autor, prestador, contactoId) = await ConContactoMaduroAsync();
        var id = await PublicadaAsync(autor, contactoId, 5);
        var otra = await UnaPersonaAsync("buscador");

        var ajena = await otra.EnviarAsync(HttpMethod.Put, $"/api/resenas/{id}",
            new { calificacion = 1, comentario = "Intento editar una reseña que no escribí yo." });
        Assert.Equal(HttpStatusCode.NotFound, ajena.StatusCode);

        var propia = await autor.EnviarAsync(HttpMethod.Put, $"/api/resenas/{id}",
            new { calificacion = 3, comentario = "Lo pensé mejor y fue un servicio normal, sin más." });
        Assert.Equal(HttpStatusCode.OK, propia.StatusCode);
        var cuerpo = (await propia.Content.ReadFromJsonAsync<JsonDocument>())!.RootElement;
        Assert.True(cuerpo.GetProperty("editada").GetBoolean());
        Assert.Equal(3, cuerpo.GetProperty("calificacion").GetInt32());

        using var ctx = ContextoDePrueba.Crear();
        Assert.Equal(3.0m, (await ctx.PerfilesPrestador.AsNoTracking().SingleAsync(p => p.Id == prestador.PerfilId)).PromedioCalificacion);
    }

    [Fact]
    public async Task Editar_pasadas_las_72_horas_responde_422()
    {
        var (autor, _, contactoId) = await ConContactoMaduroAsync();
        var id = await PublicadaAsync(autor, contactoId, 5);
        using (var ctx = ContextoDePrueba.Crear())
            await ctx.Resenas.Where(r => r.Id == id)
                .ExecuteUpdateAsync(s => s.SetProperty(r => r.EditableHasta, DateTime.UtcNow.AddMinutes(-1)));

        var r = await autor.EnviarAsync(HttpMethod.Put, $"/api/resenas/{id}",
            new { calificacion = 1, comentario = "Ya pasó el plazo pero quiero cambiarla igual." });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, r.StatusCode);
    }

    // ------------------------------------------------------------- responder

    [Fact]
    public async Task Solo_el_dueño_del_perfil_responde_y_los_demas_reciben_404()
    {
        var (autor, _, contactoId) = await ConContactoMaduroAsync();
        var id = await PublicadaAsync(autor, contactoId);
        var otroPrestador = await UnaPersonaAsync("prestador", publicado: true);

        var r1 = await otroPrestador.EnviarAsync(HttpMethod.Post, $"/api/resenas/{id}/respuesta", new { texto = "No es mi reseña." });
        var r2 = await autor.EnviarAsync(HttpMethod.Post, $"/api/resenas/{id}/respuesta", new { texto = "Me respondo a mí mismo." });
        var r3 = await otroPrestador.EnviarAsync(HttpMethod.Post, $"/api/resenas/{Guid.NewGuid()}/respuesta", new { texto = "Inexistente." });

        Assert.Equal(HttpStatusCode.NotFound, r1.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, r2.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, r3.StatusCode);
        // Y el 404 de "no es tuya" es indistinguible del de "no existe".
        Assert.Equal(await r1.Content.ReadAsStringAsync(), await r3.Content.ReadAsStringAsync());
    }

    // ------------------------------------------------------------- reportar y moderar

    [Fact]
    public async Task Un_reporte_grave_oculta_la_reseña_de_la_lectura_publica_y_del_recuento_al_instante()
    {
        var (autor, prestador, contactoId) = await ConContactoMaduroAsync();
        var id = await PublicadaAsync(autor, contactoId);

        var rep = await prestador.EnviarAsync(HttpMethod.Post, $"/api/resenas/{id}/reporte",
            new { motivo = "LenguajeDeOdio", detalle = "Insultos en el texto." });
        Assert.Equal(HttpStatusCode.NoContent, rep.StatusCode);

        var publica = (await Visitante().Http.GetFromJsonAsync<JsonDocument>($"/api/prestadores/{prestador.Slug}/resenas"))!.RootElement;
        Assert.Empty(publica.GetProperty("resenas").EnumerateArray());
        using var ctx = ContextoDePrueba.Crear();
        Assert.Equal(0, (await ctx.PerfilesPrestador.AsNoTracking().SingleAsync(p => p.Id == prestador.PerfilId)).TotalResenas);

        // El mismo reclamo dos veces es un 409, y el autor no puede reportar la suya.
        var otra = await prestador.EnviarAsync(HttpMethod.Post, $"/api/resenas/{id}/reporte", new { motivo = "Falsedad" });
        Assert.Equal(HttpStatusCode.Conflict, otra.StatusCode);
        var propia = await autor.EnviarAsync(HttpMethod.Post, $"/api/resenas/{id}/reporte", new { motivo = "Falsedad" });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, propia.StatusCode);
    }

    [Fact]
    public async Task Un_motivo_de_reporte_que_no_existe_responde_400()
    {
        var (autor, prestador, contactoId) = await ConContactoMaduroAsync();
        var id = await PublicadaAsync(autor, contactoId);

        var r = await prestador.EnviarAsync(HttpMethod.Post, $"/api/resenas/{id}/reporte", new { motivo = "Aburrida" });

        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
    }

    [Fact]
    public async Task La_cola_y_resolver_son_solo_del_moderador()
    {
        var comun = await UnaPersonaAsync("buscador");
        var reporteInventado = Guid.NewGuid();

        var cola = await comun.Http.GetAsync("/api/interno/reportes/cola");
        var resolver = await comun.EnviarAsync(HttpMethod.Post, $"/api/interno/reportes/{reporteInventado}/resolver",
            new { decision = "retirar", motivo = "Quiero borrar una reseña ajena." });

        Assert.Equal(HttpStatusCode.Forbidden, cola.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, resolver.StatusCode);
    }

    [Fact]
    public async Task El_moderador_ve_la_cola_sin_identidades_resuelve_y_la_reseña_se_retira()
    {
        var (autor, prestador, contactoId) = await ConContactoMaduroAsync();
        var id = await PublicadaAsync(autor, contactoId, 1);
        await prestador.EnviarAsync(HttpMethod.Post, $"/api/resenas/{id}/reporte", new { motivo = "Falsedad", detalle = "No fue cliente." });
        var mod = await UnaPersonaAsync("buscador", tambienModerador: true);

        var colaRespuesta = await mod.Http.GetAsync("/api/interno/reportes/cola");
        Assert.Equal(HttpStatusCode.OK, colaRespuesta.StatusCode);
        var crudo = await colaRespuesta.Content.ReadAsStringAsync();
        var fila = JsonDocument.Parse(crudo).RootElement.EnumerateArray()
            .Single(f => f.GetProperty("resenaId").GetGuid() == id);
        Assert.Equal("Falsedad", fila.GetProperty("motivo").GetString());
        Assert.Contains("Excelente atención", crudo);
        // El moderador ve el reclamo y la reseña, no quién es cada parte.
        Assert.DoesNotContain(autor.UsuarioId.ToString(), crudo, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(autor.Correo, crudo, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(prestador.Correo, crudo, StringComparison.OrdinalIgnoreCase);

        var reporteId = fila.GetProperty("reporteId").GetGuid();
        var sinMotivo = await mod.EnviarAsync(HttpMethod.Post, $"/api/interno/reportes/{reporteId}/resolver",
            new { decision = "retirar", motivo = "no" });
        var decisionRara = await mod.EnviarAsync(HttpMethod.Post, $"/api/interno/reportes/{reporteId}/resolver",
            new { decision = "borrar", motivo = "Un motivo suficientemente largo." });
        Assert.Equal(HttpStatusCode.BadRequest, sinMotivo.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, decisionRara.StatusCode);

        var ok = await mod.EnviarAsync(HttpMethod.Post, $"/api/interno/reportes/{reporteId}/resolver",
            new { decision = "retirar", motivo = "Reseña falsa: la persona nunca contrató el servicio." });
        Assert.Equal(HttpStatusCode.NoContent, ok.StatusCode);

        var otraVez = await mod.EnviarAsync(HttpMethod.Post, $"/api/interno/reportes/{reporteId}/resolver",
            new { decision = "mantener", motivo = "Segunda decisión sobre el mismo reclamo." });
        Assert.Equal(HttpStatusCode.Conflict, otraVez.StatusCode);

        using var ctx = ContextoDePrueba.Crear();
        Assert.Equal(EstadoResena.Retirada, (await ctx.Resenas.AsNoTracking().SingleAsync(r => r.Id == id)).Estado);
        Assert.Equal(0, (await ctx.PerfilesPrestador.AsNoTracking().SingleAsync(p => p.Id == prestador.PerfilId)).TotalResenas);
        Assert.Contains(await ctx.Auditoria.AsNoTracking().Where(a => a.EntidadId == id).ToListAsync(),
            a => a.Accion == "resena.retirada" && a.ActorId == mod.UsuarioId);
    }

    [Fact]
    public async Task El_moderador_no_resuelve_un_reclamo_sobre_su_propia_reseña_y_no_lo_ve_en_la_cola()
    {
        var prestador = await UnaPersonaAsync("prestador", publicado: true);
        var mod = await UnaPersonaAsync("buscador", tambienModerador: true);
        var c = await mod.EnviarAsync(HttpMethod.Post, "/api/contactos",
            new { perfilId = prestador.PerfilId, origen = "Ficha", canal = "Correo" });
        var contactoId = await IdAsync(c);
        await EnvejecerContactoAsync(contactoId, TimeSpan.FromDays(8));
        var id = await PublicadaAsync(mod, contactoId);
        await prestador.EnviarAsync(HttpMethod.Post, $"/api/resenas/{id}/reporte", new { motivo = "Falsedad" });
        Guid reporteId;
        using (var ctx = ContextoDePrueba.Crear())
            reporteId = (await ctx.ReportesResena.AsNoTracking().SingleAsync(r => r.ResenaId == id)).Id;

        var cola = (await mod.Http.GetFromJsonAsync<JsonDocument>("/api/interno/reportes/cola"))!.RootElement;
        var resolver = await mod.EnviarAsync(HttpMethod.Post, $"/api/interno/reportes/{reporteId}/resolver",
            new { decision = "retirar", motivo = "Intento retirar mi propia reseña." });

        Assert.DoesNotContain(cola.EnumerateArray(), f => f.GetProperty("reporteId").GetGuid() == reporteId);
        Assert.Equal(HttpStatusCode.NotFound, resolver.StatusCode);
    }

    // ------------------------------------------------------------- contacto

    [Fact]
    public async Task Registrar_un_contacto_valida_el_cuerpo_y_no_permite_el_origen_cita_desde_fuera()
    {
        var prestador = await UnaPersonaAsync("prestador", publicado: true);
        var autor = await UnaPersonaAsync("buscador");

        var origenRaro = await autor.EnviarAsync(HttpMethod.Post, "/api/contactos",
            new { perfilId = prestador.PerfilId, origen = "Paloma", canal = "Telefono" });
        var porCita = await autor.EnviarAsync(HttpMethod.Post, "/api/contactos",
            new { perfilId = prestador.PerfilId, origen = "Cita", canal = "Telefono" });
        var inexistente = await autor.EnviarAsync(HttpMethod.Post, "/api/contactos",
            new { perfilId = Guid.NewGuid(), origen = "Ficha", canal = "Telefono" });

        Assert.Equal(HttpStatusCode.BadRequest, origenRaro.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, porCita.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, inexistente.StatusCode);
    }

    [Fact]
    public async Task La_elegibilidad_sin_contacto_dice_que_falta_y_sin_perfil_responde_400()
    {
        var prestador = await UnaPersonaAsync("prestador", publicado: true);
        var autor = await UnaPersonaAsync("buscador");

        var sin = (await autor.Http.GetFromJsonAsync<JsonDocument>(
            $"/api/mi/elegibilidad?perfilId={prestador.PerfilId}"))!.RootElement;
        var sinParametro = await autor.Http.GetAsync("/api/mi/elegibilidad");

        Assert.False(sin.GetProperty("elegible").GetBoolean());
        Assert.Equal("SinContacto", sin.GetProperty("motivo").GetString());
        Assert.Equal(HttpStatusCode.BadRequest, sinParametro.StatusCode);
    }
}
