using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Zass.Dominio.Comun;
using Zass.Dominio.Credenciales;
using Zass.Dominio.Identidad;

namespace Zass.Integracion.Pruebas;

/// <summary>
/// MOD-02 · CU-06 por HTTP y contra la base real: la cola, tomar y resolver (API-27, 29, 30, 31) con
/// la decisión de lanzamiento ENCENDIDA (`PermitirSinDocumentos`, que es como arranca esta clase).
/// El modo apagado vive en <see cref="VerificacionSinDocumentosApiPruebas"/>.
///
/// Parcial a propósito: reutiliza los ayudantes de ApiPruebas (registro, sesión, CSRF, ficha pública)
/// en lugar de copiarlos. Varios usuarios comparten el cliente: iniciar sesión de nuevo reemplaza la
/// cookie, y cada petición pide su propio token CSRF.
/// </summary>
public partial class ApiPruebas
{
    private const short RolVerificador = 3;

    /// <summary>Una cuenta verificada (correo y todo). Con `rolInterno` se le añade el rol, por base: no hay API para eso.</summary>
    private async Task<(string Correo, Guid Id)> UnaCuentaAsync(string rol, bool verificador = false)
    {
        var correo = NuevoCorreo();
        await RegistrarAsync(correo, rol);
        await VerificarAsync(correo);

        using var ctx = ContextoDePrueba.Crear();
        var id = (await ctx.Usuarios.SingleAsync(u => u.Correo == correo)).Id;
        if (verificador)
        {
            ctx.UsuarioRoles.Add(new UsuarioRol { UsuarioId = id, RolId = RolVerificador });
            await ctx.SaveChangesAsync();
        }
        return (correo, id);
    }

    private async Task<HttpResponseMessage> PostConCsrfAsync(string url, object? cuerpo = null)
    {
        var peticion = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = cuerpo is null ? null : JsonContent.Create(cuerpo),
        };
        peticion.Headers.Add("X-CSRF-Token", await TokenCsrfAsync());
        return await _cliente.SendAsync(peticion);
    }

    private static object Resolucion(string resultado, string? motivo = null, string? detalle = null) =>
        new { resultado, motivo, detalle };

    /// <summary>El prestador (sesión iniciada) crea una credencial y la envía a revisión.</summary>
    private async Task<Guid> CredencialEnRevisionDeAsync(string correoPrestador, string nombre = "Abogado titulado")
    {
        await IniciarSesionAsync(correoPrestador);
        var id = await IdDeAsync(await CrearCredencialAsync(UnaCredencial(nombre)));
        var enviada = await PostConCsrfAsync($"/api/mi/credenciales/{id}/enviar");
        Assert.Equal(HttpStatusCode.NoContent, enviada.StatusCode);
        return Guid.Parse(id);
    }

    private async Task<JsonElement> ColaAsync()
    {
        var r = await _cliente.GetAsync("/api/interno/credenciales/cola");
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        return (await r.Content.ReadFromJsonAsync<JsonDocument>())!.RootElement.Clone();
    }

    private static bool EstaEnCola(JsonElement cola, Guid id) =>
        cola.EnumerateArray().Any(x => x.GetProperty("id").GetString() == id.ToString());

    private static async Task<Credencial> DeLaBaseAsync(Guid id)
    {
        using var ctx = ContextoDePrueba.Crear();
        return await ctx.Credenciales.Include(c => c.Verificaciones).SingleAsync(c => c.Id == id);
    }

    // ------------------------------------------------------------- quién puede entrar

    [Theory]
    [InlineData("GET", "/api/interno/credenciales/cola")]
    [InlineData("POST", "/api/interno/credenciales/00000000-0000-0000-0000-000000000001/tomar")]
    [InlineData("POST", "/api/interno/credenciales/00000000-0000-0000-0000-000000000001/resolver")]
    public async Task Sin_sesion_la_verificacion_responde_401(string metodo, string url)
    {
        var r = metodo == "GET" ? await _cliente.GetAsync(url) : await _cliente.PostAsJsonAsync(url, Resolucion("Verificada"));

        Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode);
    }

    /// <summary>Un buscador ni un prestador pueden ni listar la cola, y el 403 no trae nada de ella.</summary>
    [Theory]
    [InlineData("buscador")]
    [InlineData("prestador")]
    public async Task Una_cuenta_sin_rol_de_verificador_recibe_403_en_los_tres_endpoints(string rol)
    {
        var (correoDelPrestador, _) = await UnaCuentaAsync("prestador");
        var enCola = await CredencialEnRevisionDeAsync(correoDelPrestador, "Magister confidencial");
        var (correo, _) = await UnaCuentaAsync(rol);
        await IniciarSesionAsync(correo);

        var cola = await _cliente.GetAsync("/api/interno/credenciales/cola");
        var tomar = await PostConCsrfAsync($"/api/interno/credenciales/{enCola}/tomar");
        var resolver = await PostConCsrfAsync($"/api/interno/credenciales/{enCola}/resolver",
            Resolucion("Verificada", detalle: "Intento de colarme como verificador"));

        foreach (var r in new[] { cola, tomar, resolver })
        {
            Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
            var texto = await r.Content.ReadAsStringAsync();
            Assert.DoesNotContain("Magister confidencial", texto);
            Assert.DoesNotContain(enCola.ToString(), texto);
        }
        // y no cambió nada
        var c = await DeLaBaseAsync(enCola);
        Assert.Equal(EstadoCredencial.EnRevision, c.Estado);
        Assert.Null(c.ReservadaPor);
        Assert.Empty(c.Verificaciones);
    }

    [Fact]
    public async Task Tomar_y_resolver_sin_el_token_csrf_devuelven_403()
    {
        var (dueno, _) = await UnaCuentaAsync("prestador");
        var id = await CredencialEnRevisionDeAsync(dueno);
        var (verificador, _) = await UnaCuentaAsync("buscador", verificador: true);
        await IniciarSesionAsync(verificador);

        var tomar = await _cliente.PostAsync($"/api/interno/credenciales/{id}/tomar", null);
        var resolver = await _cliente.PostAsJsonAsync($"/api/interno/credenciales/{id}/resolver", Resolucion("Verificada"));

        Assert.Equal(HttpStatusCode.Forbidden, tomar.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, resolver.StatusCode);
        Assert.Null((await DeLaBaseAsync(id)).ReservadaPor);
    }

    // ------------------------------------------------------------- el camino completo

    /// <summary>
    /// CU-05 → CU-06 de punta a punta, sin archivos: enviar sin documentos, que la cola lo diga,
    /// tomar, que otro no pueda, y verificar exigiendo el detalle. Y la ficha pública, por bytes.
    /// </summary>
    [Fact]
    public async Task Del_envio_sin_documentos_al_sello_verificada_con_la_auditoria_distinta()
    {
        var (dueno, duenoId) = await UnaCuentaAsync("prestador");
        var (ana, anaId) = await UnaCuentaAsync("buscador", verificador: true);
        var (beto, _) = await UnaCuentaAsync("buscador", verificador: true);
        var id = await CredencialEnRevisionDeAsync(dueno);
        var slug = await SlugDeAsync(dueno);        // deja la sesion del dueño; despues entran los verificadores

        // El dueño no puede enviarla dos veces.
        Assert.Equal(HttpStatusCode.Conflict, (await PostConCsrfAsync($"/api/mi/credenciales/{id}/enviar")).StatusCode);

        // (b) la cola le dice a Ana que fue sin documentos.
        await IniciarSesionAsync(ana);
        var cola = await ColaAsync();
        var item = cola.EnumerateArray().Single(x => x.GetProperty("id").GetString() == id.ToString());
        Assert.True(item.GetProperty("sinDocumentos").GetBoolean());
        Assert.Equal("Abogado titulado", item.GetProperty("nombre").GetString());
        Assert.False(item.GetProperty("reservadaPorMi").GetBoolean());

        var tomada = await PostConCsrfAsync($"/api/interno/credenciales/{id}/tomar");
        Assert.Equal(HttpStatusCode.OK, tomada.StatusCode);
        var cuerpo = (await tomada.Content.ReadFromJsonAsync<JsonDocument>())!.RootElement;
        Assert.True(cuerpo.GetProperty("reservadaPorMi").GetBoolean());

        // Beto no la ve en su cola ni puede tomarla (4D.3).
        await IniciarSesionAsync(beto);
        Assert.False(EstaEnCola(await ColaAsync(), id));
        var segunda = await PostConCsrfAsync($"/api/interno/credenciales/{id}/tomar");
        Assert.Equal(HttpStatusCode.Conflict, segunda.StatusCode);
        Assert.Contains("Otro verificador ya la tomó.", await segunda.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.Conflict,
            (await PostConCsrfAsync($"/api/interno/credenciales/{id}/resolver", Resolucion("Verificada", detalle: "Lo comprobé yo mismo en persona"))).StatusCode);

        // (c) Ana: sin detalle, o con uno corto, no puede verificar.
        await IniciarSesionAsync(ana);
        foreach (var corto in new string?[] { null, "Lo vi en persona" })
        {
            var r = await PostConCsrfAsync($"/api/interno/credenciales/{id}/resolver", Resolucion("Verificada", detalle: corto));
            Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        }
        Assert.Equal(EstadoCredencial.EnRevision, (await DeLaBaseAsync(id)).Estado);

        // Antes de resolver, el público la ve como declarada.
        using (var anonimo = UnVisitante())
        {
            var antes = await (await anonimo.GetAsync($"/api/prestadores/{slug}")).Content.ReadAsStringAsync();
            Assert.Equal("Declarada por el usuario",
                JsonDocument.Parse(antes).RootElement.GetProperty("credenciales").EnumerateArray().Single().GetProperty("etiqueta").GetString());
            NadaInternoEn(antes, anaId, "Revisé el título original");
        }

        var verificada = await PostConCsrfAsync($"/api/interno/credenciales/{id}/resolver",
            Resolucion("Verificada", detalle: "Revisé el título original el 12-nov en persona"));
        Assert.Equal(HttpStatusCode.OK, verificada.StatusCode);

        var c = await DeLaBaseAsync(id);
        Assert.Equal(EstadoCredencial.Verificada, c.Estado);
        Assert.Equal(anaId, c.SelloPor);
        Assert.NotNull(c.SelloEn);
        Assert.Null(c.ReservadaPor);
        var v = Assert.Single(c.Verificaciones);
        Assert.Equal(anaId, v.VerificadorId);
        Assert.Equal("Revisé el título original el 12-nov en persona", v.Detalle);

        using (var ctx = ContextoDePrueba.Crear())
        {
            var acciones = await ctx.Auditoria.Where(a => a.EntidadId == id).OrderBy(a => a.Id).ToListAsync();
            var resolucion = Assert.Single(acciones, a => a.Accion == "verificar_sin_documentos");
            Assert.Equal(anaId, resolucion.ActorId);
            Assert.Contains(acciones, a => a.Accion == "tomar_credencial" && a.ActorId == anaId);
            Assert.Contains(acciones, a => a.Accion == "enviar_credencial_a_revision");
            Assert.DoesNotContain(acciones, a => a.Accion == "verificar_credencial");
        }

        // Ya no está en ninguna cola.
        Assert.False(EstaEnCola(await ColaAsync(), id));

        // El público: «Verificada», y NADA interno por los bytes.
        using var visitante = UnVisitante();
        var texto = await (await visitante.GetAsync($"/api/prestadores/{slug}")).Content.ReadAsStringAsync();
        var publica = JsonDocument.Parse(texto).RootElement.GetProperty("credenciales").EnumerateArray().Single();
        Assert.Equal("Verificada", publica.GetProperty("etiqueta").GetString());
        NadaInternoEn(texto, anaId, "Revisé el título original");
        _ = duenoId;
    }

    /// <summary>
    /// Lo que no debe salir NUNCA por una superficie que no es la cola: ni el estado interno, ni el
    /// detalle del verificador, ni quién la verificó, ni marcas de la reserva o de «sin documentos».
    /// </summary>
    private static void NadaInternoEn(string texto, Guid verificador, string detalle)
    {
        Assert.DoesNotContain(detalle, texto);
        Assert.DoesNotContain(verificador.ToString(), texto, StringComparison.OrdinalIgnoreCase);
        foreach (var interno in new[]
        {
            "EnRevision", "Rechazada", "AntecedentesSolicitados", "NoCorresponde", "Ilegible",
            "selloPor", "selloEn", "reservada", "esperandoPrestador", "sinDocumentos", "verificador",
            "motivo", "detalle",
        })
            Assert.DoesNotContain(interno, texto, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>El slug lo asigna el servidor al crear el perfil; se lee con la sesión del dueño.</summary>
    private async Task<string> SlugDeAsync(string correoPrestador)
    {
        await IniciarSesionAsync(correoPrestador);
        var perfil = await _cliente.GetFromJsonAsync<JsonDocument>("/api/mi/perfil-prestador");
        var slug = perfil!.RootElement.GetProperty("slug").GetString()!;

        // La ficha solo existe publicada; se publica por base: este test es de credenciales.
        using var ctx = ContextoDePrueba.Crear();
        var p = await ctx.PerfilesPrestador.SingleAsync(x => x.Slug == slug);
        p.Estado = EstadoPerfil.Publicado;
        await ctx.SaveChangesAsync();
        return slug;
    }

    // ------------------------------------------------------------- rechazar y lo que ve cada quien

    [Fact]
    public async Task Rechazar_exige_motivo_y_texto_y_el_publico_la_sigue_viendo_como_declarada()
    {
        var (dueno, _) = await UnaCuentaAsync("prestador");
        var (ana, anaId) = await UnaCuentaAsync("buscador", verificador: true);
        var id = await CredencialEnRevisionDeAsync(dueno);
        var slug = await SlugDeAsync(dueno);
        const string explicacion = "La entidad que figura no la reconocemos ni tiene registro";

        await IniciarSesionAsync(ana);
        Assert.Equal(HttpStatusCode.OK, (await PostConCsrfAsync($"/api/interno/credenciales/{id}/tomar")).StatusCode);

        // RN-S2-06: sin motivo, sin texto, o con un enum que no existe, no pasa.
        var url = $"/api/interno/credenciales/{id}/resolver";
        Assert.Equal(HttpStatusCode.BadRequest, (await PostConCsrfAsync(url, Resolucion("Rechazada", null, explicacion))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await PostConCsrfAsync(url, Resolucion("Rechazada", "EntidadNoReconocida", null))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await PostConCsrfAsync(url, Resolucion("Rechazada", "EntidadNoReconocida", "corto"))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await PostConCsrfAsync(url, Resolucion("Rechazada", "Hechicero", explicacion))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await PostConCsrfAsync(url, Resolucion("Aprobada", null, explicacion))).StatusCode);
        Assert.Equal(EstadoCredencial.EnRevision, (await DeLaBaseAsync(id)).Estado);

        Assert.Equal(HttpStatusCode.OK,
            (await PostConCsrfAsync(url, Resolucion("Rechazada", "EntidadNoReconocida", explicacion))).StatusCode);

        var c = await DeLaBaseAsync(id);
        Assert.Equal(EstadoCredencial.Rechazada, c.Estado);
        Assert.Equal(MotivoRechazo.EntidadNoReconocida, Assert.Single(c.Verificaciones).Motivo);

        // El dueño ve su estado interno (como ya mostraba API-24)... pero ni el motivo ni el texto ni quién.
        await IniciarSesionAsync(dueno);
        var propias = await (await _cliente.GetAsync("/api/mi/credenciales")).Content.ReadAsStringAsync();
        Assert.Equal("Rechazada", JsonDocument.Parse(propias).RootElement.EnumerateArray().Single().GetProperty("estado").GetString());
        Assert.DoesNotContain("EntidadNoReconocida", propias);
        Assert.DoesNotContain(explicacion, propias);
        Assert.DoesNotContain(anaId.ToString(), propias, StringComparison.OrdinalIgnoreCase);
        foreach (var interno in new[] { "motivo", "detalle", "verificador", "selloPor", "reservada", "esperandoPrestador", "sinDocumentos" })
            Assert.DoesNotContain(interno, propias, StringComparison.OrdinalIgnoreCase);

        // 4A.6: el público sigue viéndola como «declarada», nunca como rechazada.
        using var visitante = UnVisitante();
        var texto = await (await visitante.GetAsync($"/api/prestadores/{slug}")).Content.ReadAsStringAsync();
        Assert.Equal("Declarada por el usuario",
            JsonDocument.Parse(texto).RootElement.GetProperty("credenciales").EnumerateArray().Single().GetProperty("etiqueta").GetString());
        NadaInternoEn(texto, anaId, explicacion);
        Assert.DoesNotContain("EntidadNoReconocida", texto);

        // 4A.7: puede corregir y reenviar.
        Assert.Equal(HttpStatusCode.NoContent, (await PostConCsrfAsync($"/api/mi/credenciales/{id}/enviar")).StatusCode);
    }

    // ------------------------------------------------------------- pedir antecedentes

    [Fact]
    public async Task Pedir_antecedentes_la_saca_de_la_cola_hasta_que_el_prestador_responde()
    {
        var (dueno, _) = await UnaCuentaAsync("prestador");
        var (ana, _) = await UnaCuentaAsync("buscador", verificador: true);
        var id = await CredencialEnRevisionDeAsync(dueno);

        await IniciarSesionAsync(ana);
        await PostConCsrfAsync($"/api/interno/credenciales/{id}/tomar");
        var url = $"/api/interno/credenciales/{id}/resolver";
        Assert.Equal(HttpStatusCode.BadRequest, (await PostConCsrfAsync(url, Resolucion("AntecedentesSolicitados"))).StatusCode);

        var r = await PostConCsrfAsync(url, Resolucion("AntecedentesSolicitados", detalle: "Falta el certificado de la universidad"));
        var cuerpo = (await r.Content.ReadFromJsonAsync<JsonDocument>())!.RootElement;
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.True(cuerpo.GetProperty("esperandoPrestador").GetBoolean());
        Assert.False(EstaEnCola(await ColaAsync(), id));
        Assert.Equal(HttpStatusCode.Conflict, (await PostConCsrfAsync($"/api/interno/credenciales/{id}/tomar")).StatusCode);

        await IniciarSesionAsync(dueno);
        Assert.Equal(HttpStatusCode.NoContent, (await PostConCsrfAsync($"/api/mi/credenciales/{id}/enviar")).StatusCode);

        await IniciarSesionAsync(ana);
        Assert.True(EstaEnCola(await ColaAsync(), id));
    }

    // ------------------------------------------------------------- RN-S2-09

    [Fact]
    public async Task Un_verificador_no_ve_ni_toma_ni_resuelve_su_propia_credencial()
    {
        var (ana, anaId) = await UnaCuentaAsync("prestador", verificador: true);   // verificadora Y prestadora
        var propia = await CredencialEnRevisionDeAsync(ana, "Mi propio titulo");
        var (otra, _) = await UnaCuentaAsync("prestador");
        var ajena = await CredencialEnRevisionDeAsync(otra);

        await IniciarSesionAsync(ana);
        var cola = await ColaAsync();
        Assert.False(EstaEnCola(cola, propia));
        Assert.True(EstaEnCola(cola, ajena));

        // Por enlace directo: el mismo 404 que una inexistente, sin pista de que existe.
        var tomar = await PostConCsrfAsync($"/api/interno/credenciales/{propia}/tomar");
        var inexistente = await PostConCsrfAsync($"/api/interno/credenciales/{Guid.NewGuid()}/tomar");
        var resolver = await PostConCsrfAsync($"/api/interno/credenciales/{propia}/resolver",
            Resolucion("Verificada", detalle: "Me la verifico yo misma, que listo"));

        Assert.Equal(HttpStatusCode.NotFound, tomar.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, inexistente.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, resolver.StatusCode);
        Assert.Equal(await inexistente.Content.ReadAsStringAsync(), await tomar.Content.ReadAsStringAsync());

        var c = await DeLaBaseAsync(propia);
        Assert.Equal(EstadoCredencial.EnRevision, c.Estado);
        Assert.Null(c.ReservadaPor);

        // 4E.3: queda registrado el intento.
        using var ctx = ContextoDePrueba.Crear();
        Assert.True(await ctx.Auditoria.CountAsync(
            a => a.Accion == "intento_revisar_propia" && a.ActorId == anaId && a.EntidadId == propia) >= 2);
    }

    // ------------------------------------------------------------- reserva de 30 minutos

    [Fact]
    public async Task Con_la_reserva_vencida_no_se_resuelve_y_otro_verificador_puede_tomarla()
    {
        var (dueno, _) = await UnaCuentaAsync("prestador");
        var (ana, _) = await UnaCuentaAsync("buscador", verificador: true);
        var (beto, betoId) = await UnaCuentaAsync("buscador", verificador: true);
        var id = await CredencialEnRevisionDeAsync(dueno);

        await IniciarSesionAsync(ana);
        await PostConCsrfAsync($"/api/interno/credenciales/{id}/tomar");

        // Pasan 31 minutos (se mueve la fecha de la reserva, no el reloj del proceso).
        using (var ctx = ContextoDePrueba.Crear())
        {
            (await ctx.Credenciales.FindAsync(id))!.ReservadaHasta = DateTime.UtcNow.AddMinutes(-1);
            await ctx.SaveChangesAsync();
        }

        var tarde = await PostConCsrfAsync($"/api/interno/credenciales/{id}/resolver",
            Resolucion("Verificada", detalle: "Lo comprobé yo mismo en persona"));
        Assert.Equal(HttpStatusCode.Conflict, tarde.StatusCode);
        Assert.Contains("Ya no tienes reservada", await tarde.Content.ReadAsStringAsync());

        await IniciarSesionAsync(beto);
        Assert.True(EstaEnCola(await ColaAsync(), id));
        Assert.Equal(HttpStatusCode.OK, (await PostConCsrfAsync($"/api/interno/credenciales/{id}/tomar")).StatusCode);
        Assert.Equal(betoId, (await DeLaBaseAsync(id)).ReservadaPor);
    }

    // ------------------------------------------------------------- grupo de tasa S

    [Fact]
    public async Task Los_endpoints_de_verificacion_son_del_grupo_de_tasa_sensible()
    {
        using var app = new Aplicacion(limiteSensible: 3);
        using var cliente = app.CreateClient(new WebApplicationFactoryClientOptions
        {
            HandleCookies = true, AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost"),
        });

        // Sin sesion el limitador corre antes que la autorizacion: 3 pasan (401) y la cuarta es 429.
        var codigos = new List<HttpStatusCode>();
        for (var i = 0; i < 4; i++)
            codigos.Add((await cliente.GetAsync("/api/interno/credenciales/cola")).StatusCode);

        Assert.Equal(3, codigos.Count(c => c == HttpStatusCode.Unauthorized));
        Assert.Equal(HttpStatusCode.TooManyRequests, codigos[3]);
    }

    // ------------------------------------------------------------- API-27 por HTTP

    [Fact]
    public async Task Enviar_la_credencial_de_otra_persona_responde_404_igual_que_una_inexistente()
    {
        var (duena, _) = await UnaCuentaAsync("prestador");
        await IniciarSesionAsync(duena);
        var ajena = await IdDeAsync(await CrearCredencialAsync(UnaCredencial()));

        var (intruso, _) = await UnaCuentaAsync("prestador");
        await IniciarSesionAsync(intruso);
        var deOtra = await PostConCsrfAsync($"/api/mi/credenciales/{ajena}/enviar");
        var inexistente = await PostConCsrfAsync($"/api/mi/credenciales/{Guid.NewGuid()}/enviar");

        Assert.Equal(HttpStatusCode.NotFound, deOtra.StatusCode);
        Assert.Equal(await inexistente.Content.ReadAsStringAsync(), await deOtra.Content.ReadAsStringAsync());
        Assert.Equal(EstadoCredencial.Borrador, (await DeLaBaseAsync(Guid.Parse(ajena))).Estado);
    }

    [Fact]
    public async Task Enviar_sin_sesion_ni_csrf_no_pasa_y_un_buscador_tampoco()
    {
        var id = Guid.NewGuid();
        Assert.Equal(HttpStatusCode.Unauthorized, (await _cliente.PostAsync($"/api/mi/credenciales/{id}/enviar", null)).StatusCode);

        var (duena, _) = await UnaCuentaAsync("prestador");
        await IniciarSesionAsync(duena);
        var propia = await IdDeAsync(await CrearCredencialAsync(UnaCredencial()));
        Assert.Equal(HttpStatusCode.Forbidden, (await _cliente.PostAsync($"/api/mi/credenciales/{propia}/enviar", null)).StatusCode);

        var (buscador, _) = await UnaCuentaAsync("buscador");
        await IniciarSesionAsync(buscador);
        Assert.False((await PostConCsrfAsync($"/api/mi/credenciales/{propia}/enviar")).IsSuccessStatusCode);
    }
}

/// <summary>
/// La decisión de lanzamiento APAGADA, que es el comportamiento por omisión en producción: la
/// especificación tal cual (RN-S2-01), sin atajos. Misma regla, probada por HTTP y con la base real.
/// </summary>
public class VerificacionSinDocumentosApiPruebas : IAsyncLifetime
{
    private readonly Aplicacion _app = new(permitirSinDocumentos: false);
    private readonly List<string> _correos = new();
    private HttpClient _cliente = null!;

    public Task InitializeAsync()
    {
        _cliente = _app.CreateClient(new WebApplicationFactoryClientOptions
        {
            HandleCookies = true, AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost"),
        });
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        using (var ctx = ContextoDePrueba.Crear())
        {
            foreach (var correo in _correos)
            {
                var usuario = await ctx.Usuarios.FirstOrDefaultAsync(u => u.Correo == correo);
                if (usuario is null) continue;
                ctx.Auditoria.RemoveRange(ctx.Auditoria.Where(a => a.ActorId == usuario.Id || a.EntidadId == usuario.Id));
                ctx.Usuarios.Remove(usuario);
                await ctx.SaveChangesAsync();
            }
            ctx.EntidadesCertificadoras.RemoveRange(ctx.EntidadesCertificadoras.Where(e => e.Nombre.StartsWith("api-sd-")));
            await ctx.SaveChangesAsync();
        }
        _cliente.Dispose();
        _app.Dispose();
    }

    private async Task<string> PrestadorConSesionAsync()
    {
        var correo = $"api-sd-{Guid.NewGuid():N}@zass.local";
        _correos.Add(correo);
        Assert.Equal(HttpStatusCode.Created, (await _cliente.PostAsJsonAsync("/api/auth/registro", new
        {
            correo, contrasena = "contrasena-larga-1", nombre = "Pablo Espinoza", rol = "prestador",
        })).StatusCode);

        // El token de verificacion llega por el aviso encolado, como en ApiPruebas.
        using (var ctx = ContextoDePrueba.Crear())
        {
            var usuario = await ctx.Usuarios.SingleAsync(u => u.Correo == correo);
            var aviso = await ctx.Notificaciones
                .Where(n => n.DestinatarioId == usuario.Id && n.Tipo == TipoNotificacion.CorreoVerificacion)
                .OrderByDescending(n => n.ProgramadaPara).FirstAsync();
            var token = aviso.Cuerpo.Split(' ').Last().TrimEnd('.');
            Assert.Equal(HttpStatusCode.NoContent,
                (await _cliente.PostAsJsonAsync("/api/auth/correo/verificar", new { token })).StatusCode);
        }
        await IniciarSesionAsync(correo);
        return correo;
    }

    private async Task IniciarSesionAsync(string correo) =>
        Assert.Equal(HttpStatusCode.OK, (await _cliente.PostAsJsonAsync("/api/auth/sesion",
            new { correo, contrasena = "contrasena-larga-1" })).StatusCode);

    private async Task<HttpResponseMessage> PostAsync(string url, object? cuerpo = null)
    {
        var token = (await (await _cliente.GetAsync("/api/auth/csrf")).Content.ReadFromJsonAsync<JsonDocument>())!
            .RootElement.GetProperty("token").GetString()!;
        var p = new HttpRequestMessage(HttpMethod.Post, url) { Content = cuerpo is null ? null : JsonContent.Create(cuerpo) };
        p.Headers.Add("X-CSRF-Token", token);
        return await _cliente.SendAsync(p);
    }

    private async Task<Guid> UnaCredencialAsync()
    {
        var r = await PostAsync("/api/mi/credenciales", new
        {
            tipo = "Titulo", nombre = "Abogado", entidadId = (short?)null,
            entidadNombre = $"api-sd-{Guid.NewGuid():N}", obtenidaEn = "2020-03-15", venceEn = (string?)null,
        });
        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        return Guid.Parse((await r.Content.ReadFromJsonAsync<JsonDocument>())!.RootElement.GetProperty("id").GetString()!);
    }

    [Fact]
    public async Task Con_la_bandera_apagada_enviar_a_revision_sin_documentos_devuelve_422_y_no_cambia_nada()
    {
        await PrestadorConSesionAsync();
        var id = await UnaCredencialAsync();

        var r = await PostAsync($"/api/mi/credenciales/{id}/enviar");

        Assert.Equal(HttpStatusCode.UnprocessableEntity, r.StatusCode);
        using var ctx = ContextoDePrueba.Crear();
        Assert.Equal(EstadoCredencial.Borrador, (await ctx.Credenciales.FindAsync(id))!.Estado);
        Assert.False(await ctx.Auditoria.AnyAsync(a => a.EntidadId == id && a.Accion == "enviar_credencial_a_revision"));
    }

    [Fact]
    public async Task Con_la_bandera_apagada_enviar_con_un_documento_disponible_si_pasa()
    {
        await PrestadorConSesionAsync();
        var id = await UnaCredencialAsync();
        using (var ctx = ContextoDePrueba.Crear())
        {
            ctx.DocumentosCredencial.Add(new DocumentoCredencial
            {
                CredencialId = id, ClaveAlmacenamiento = $"sd-{Guid.NewGuid():N}", NombreOriginal = "titulo.pdf",
                TipoMime = "application/pdf", TamanoBytes = 10, Estado = EstadoDocumento.Disponible,
            });
            await ctx.SaveChangesAsync();
        }

        Assert.Equal(HttpStatusCode.NoContent, (await PostAsync($"/api/mi/credenciales/{id}/enviar")).StatusCode);
    }

    /// <summary>Una que quedó en revisión sin respaldo (de cuando la bandera estaba encendida) no se verifica.</summary>
    [Fact]
    public async Task Con_la_bandera_apagada_una_credencial_en_revision_sin_respaldo_no_se_puede_verificar()
    {
        var duena = await PrestadorConSesionAsync();
        var id = await UnaCredencialAsync();
        using (var ctx = ContextoDePrueba.Crear())
        {
            (await ctx.Credenciales.FindAsync(id))!.Estado = EstadoCredencial.EnRevision;
            await ctx.SaveChangesAsync();
        }

        var verificador = $"api-sd-{Guid.NewGuid():N}@zass.local";
        _correos.Add(verificador);
        await _cliente.PostAsJsonAsync("/api/auth/registro", new
        {
            correo = verificador, contrasena = "contrasena-larga-1", nombre = "Vera Ficadora", rol = "buscador",
        });
        Guid verificadorId;
        using (var ctx = ContextoDePrueba.Crear())
        {
            var usuario = await ctx.Usuarios.SingleAsync(u => u.Correo == verificador);
            var aviso = await ctx.Notificaciones
                .Where(n => n.DestinatarioId == usuario.Id && n.Tipo == TipoNotificacion.CorreoVerificacion)
                .OrderByDescending(n => n.ProgramadaPara).FirstAsync();
            await _cliente.PostAsJsonAsync("/api/auth/correo/verificar", new { token = aviso.Cuerpo.Split(' ').Last().TrimEnd('.') });
            ctx.UsuarioRoles.Add(new UsuarioRol { UsuarioId = usuario.Id, RolId = 3 });
            await ctx.SaveChangesAsync();
            verificadorId = usuario.Id;
        }
        await IniciarSesionAsync(verificador);
        Assert.Equal(HttpStatusCode.OK, (await PostAsync($"/api/interno/credenciales/{id}/tomar")).StatusCode);

        var r = await PostAsync($"/api/interno/credenciales/{id}/resolver", new
        {
            resultado = "Verificada", motivo = (string?)null, detalle = "Revisé el título original en persona, de verdad",
        });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, r.StatusCode);
        using var c = ContextoDePrueba.Crear();
        var cred = await c.Credenciales.FindAsync(id);
        Assert.Equal(EstadoCredencial.EnRevision, cred!.Estado);
        Assert.Null(cred.SelloPor);
        _ = duena; _ = verificadorId;
    }
}
