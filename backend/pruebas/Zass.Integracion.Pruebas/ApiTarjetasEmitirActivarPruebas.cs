using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Zass.Api.Configuracion;
using Zass.Api.Endpoints;
using Zass.Aplicacion.Puertos;
using Zass.Dominio.Comun;
using Zass.Dominio.Identidad;
using Zass.Dominio.Tarjetas;

namespace Zass.Integracion.Pruebas;

/// <summary>
/// API-59 POST /api/interno/tarjetas (Emitir) y API-55 POST /t/{segmento}/activar (Activar).
/// Pruebas de integración contra zass_pruebas.
/// </summary>
public class ApiTarjetasEmitirActivarPruebas : IAsyncLifetime
{
    private readonly Aplicacion _app = new();
    private HttpClient _cliente = null!;
    private HttpClient _admin = null!;
    private HttpClient _prestador = null!;
    private CuentaAutenticada _prestadorCuenta = null!;
    private HttpClient _otroHttp = null!;

    private string _marca = "";
    private string _slug = "";
    private readonly List<Guid> _perfiles = new();
    private readonly List<Guid> _tarjetas = new();
    private Guid _perfilPrestadorId;

        public async Task InitializeAsync()
    {
        _cliente = _app.CreateClient(new WebApplicationFactoryClientOptions
        {
            HandleCookies = false, AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost"),
        });

        // Limpiar TODOS los usuarios de prueba de corridas anteriores ANTES de generar marca nueva
        await LimpiarTodosDatosPruebaAsync();

        _marca = Guid.NewGuid().ToString("N")[..12];
        _slug = $"apitj-{_marca}";

        // Crear y autenticar usuarios a traves de la API (como ApiMisTarjetasPruebas)
        _admin = await CrearYAutenticarAsync("admin", "administrador");
        _prestador = await CrearYAutenticarAsync("prestador");
        _perfilPrestadorId = (await ObtenerPerfilIdAsync($"apitj-prestador-{_marca}@zass.local"))!.Value;
        _otroHttp = await CrearYAutenticarAsync("otro");
    }

    private async Task<HttpClient> CrearYAutenticarAsync(string sufijo, string rol = "prestador")
    {
        var client = _app.CreateClient(new WebApplicationFactoryClientOptions
        {
            HandleCookies = true, AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost"),
        });

        var correo = $"apitj-{sufijo}-{_marca}@zass.local";
        var contrasena = "contrasena-larga-1";

        // Registrar siempre como "prestador" (RolesPublicos solo permite buscador/prestador)
        var rolRegistro = "prestador";

        var reg = await client.PostAsJsonAsync("/api/auth/registro", new
        {
            tipo = "particular", apellido = "Prueba", rut = DatosDeRegistro.Rut(), telefono = "+56 9 1234 5678", mayorDeEdad = true, correo,
            contrasena,
            nombre = "Usuario " + sufijo,
            rol = rolRegistro
        });
        reg.EnsureSuccessStatusCode();

        // Verificar correo
        using (var ctx = ContextoDePrueba.Crear())
        {
            var usuario = await ctx.Usuarios.SingleAsync(u => u.Correo == correo);
            var aviso = await ctx.Notificaciones
                .Where(n => n.DestinatarioId == usuario.Id
                            && n.Tipo == TipoNotificacion.CorreoVerificacion)
                .OrderByDescending(n => n.ProgramadaPara).FirstAsync();
            var token = Zass.Aplicacion.Puertos.CargaDeAviso.LeerToken(aviso.Cuerpo)!;
            await client.PostAsJsonAsync("/api/auth/correo/verificar", new { token });
            usuario.CorreoVerificado = true; // Doble seguridad

            // Asignar rol interno si corresponde
            if (rol == "administrador")
            {
                var rolAdmin = await ctx.Roles.FirstAsync(r => r.Codigo == "administrador");
                usuario.Roles.Add(new UsuarioRol { UsuarioId = usuario.Id, RolId = rolAdmin.Id, AsignadoEn = DateTime.UtcNow });
            }
            else if (rol == "verificador")
            {
                var rolVerif = await ctx.Roles.FirstAsync(r => r.Codigo == "verificador");
                usuario.Roles.Add(new UsuarioRol { UsuarioId = usuario.Id, RolId = rolVerif.Id, AsignadoEn = DateTime.UtcNow });
            }

            if (rolRegistro == "prestador")
            {
                var rubro = await ctx.Rubros.FirstAsync(r => r.Activo);
                // Verificar si ya tiene perfil (de corrida anterior no limpiada)
                var perfilExistente = await ctx.PerfilesPrestador.SingleOrDefaultAsync(p => p.UsuarioId == usuario.Id);
                var slugEsperado = sufijo == "prestador" ? _slug : $"apitj-{sufijo}-{_marca}";
                if (perfilExistente == null)
                {
                    var perfil = new PerfilPrestador
                    {
                        UsuarioId = usuario.Id,
                        RubroId = rubro.Id,
                        NombrePublico = "Tarjetas " + sufijo,
                        Slug = slugEsperado,
                        Estado = EstadoPerfil.Publicado,
                        PublicadoEn = DateTime.UtcNow.AddDays(-1),
                    };
                    ctx.PerfilesPrestador.Add(perfil);
                }
                else
                {
                    // El registro crea el perfil en Borrador; aseguramos que quede Publicado y con el slug correcto
                    perfilExistente.Estado = EstadoPerfil.Publicado;
                    perfilExistente.PublicadoEn = DateTime.UtcNow.AddDays(-1);
                    perfilExistente.Slug = slugEsperado;
                }
                await ctx.SaveChangesAsync();
            }
            else
            {
                await ctx.SaveChangesAsync();
            }
        }

        // Iniciar sesion
        var login = await client.PostAsJsonAsync("/api/auth/sesion", new { correo, contrasena });
        login.EnsureSuccessStatusCode();

        return client;
    }

    private async Task<Guid?> ObtenerPerfilIdAsync(string correo)
    {
        using var ctx = ContextoDePrueba.Crear();
        var user = await ctx.Usuarios.SingleAsync(u => u.Correo == correo);
        var perfil = await ctx.PerfilesPrestador.SingleOrDefaultAsync(p => p.UsuarioId == user.Id);
        return perfil?.Id;
    }

    private async Task LimpiarTodosDatosPruebaAsync()
    {
        using var ctx = ContextoDePrueba.Crear();
        // Borrar TODOS los usuarios de prueba que empiezan con apitj- (de cualquier corrida anterior)
        // Tambien borrar tarjetas emitidas sin activar (perfil_prestador_id IS NULL) que sean de pruebas
        await ctx.Database.ExecuteSqlRawAsync(
            """
            DELETE FROM tarjetas WHERE perfil_prestador_id IN (SELECT id FROM perfiles_prestador WHERE usuario_id IN (SELECT id FROM usuarios WHERE correo LIKE 'apitj-%'));
            DELETE FROM tarjetas WHERE perfil_prestador_id IS NULL AND estado = 'Emitida';
            DELETE FROM perfiles_prestador WHERE usuario_id IN (SELECT id FROM usuarios WHERE correo LIKE 'apitj-%');
            DELETE FROM auditoria WHERE actor_id IN (SELECT id FROM usuarios WHERE correo LIKE 'apitj-%') OR entidad_id IN (SELECT id FROM usuarios WHERE correo LIKE 'apitj-%');
            DELETE FROM notificaciones WHERE destinatario_id IN (SELECT id FROM usuarios WHERE correo LIKE 'apitj-%');
            DELETE FROM sesiones WHERE usuario_id IN (SELECT id FROM usuarios WHERE correo LIKE 'apitj-%');
            DELETE FROM perfiles_buscador WHERE usuario_id IN (SELECT id FROM usuarios WHERE correo LIKE 'apitj-%');
            DELETE FROM usuarios WHERE correo LIKE 'apitj-%';
            """);
    }

    private static HttpRequestMessage CrearActivarRequest(HttpClient client, string segmento, string codigo, string csrf)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, $"/t/{segmento}/activar");
        req.Content = JsonContent.Create(new { codigo });
        req.Headers.Add("X-CSRF-Token", csrf);
        return req;
    }

    private static async Task<string> ObtenerCsrfAsync(HttpClient client)
    {
        var r = await client.GetAsync("/api/auth/csrf");
        r.EnsureSuccessStatusCode();
        var doc = await r.Content.ReadFromJsonAsync<JsonDocument>();
        return doc!.RootElement.GetProperty("token").GetString()!;
    }

    // ------------------------------------------------------------- API-59: Emitir tarjeta

    [Fact]
    public async Task Admin_emite_tarjeta_devuelve_201_con_codigo_en_claro_y_guarda_solo_hash()
    {
        var csrf = await ObtenerCsrfAsync(_admin);
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/interno/tarjetas");
        req.Headers.Add("X-CSRF-Token", csrf);
        var r = await _admin.SendAsync(req);

        Assert.Equal(HttpStatusCode.Created, r.StatusCode);

        var body = await r.Content.ReadFromJsonAsync<JsonElement>();
        var segmento = body.GetProperty("segmento").GetString()!;
        var codigo = body.GetProperty("codigo").GetString()!;
        var venceEn = body.GetProperty("venceEn").GetDateTime();

        // Formato del segmento: 4-4
        Assert.Matches("^[A-HJKMNP-Z2-9]{4}-[A-HJKMNP-Z2-9]{4}$", segmento);
        // Formato del codigo: 4-4-4
        Assert.Matches("^[A-HJKMNP-Z2-9]{4}-[A-HJKMNP-Z2-9]{4}-[A-HJKMNP-Z2-9]{4}$", codigo);
        // Vence en ~30 dias
        Assert.InRange(venceEn, DateTime.UtcNow.AddDays(29), DateTime.UtcNow.AddDays(31));

        // En la base solo esta el hash, NO el codigo en claro
        using var ctx = ContextoDePrueba.Crear();
        var tarjeta = await ctx.Tarjetas.FirstAsync(t => t.UrlEstable == segmento);
        Assert.Equal(EstadoTarjeta.Emitida, tarjeta.Estado);
        Assert.NotNull(tarjeta.HashCodigoActivacion);
        Assert.Equal(64, tarjeta.HashCodigoActivacion.Length); // SHA256 hex = 64 chars
        Assert.Null(tarjeta.PerfilPrestadorId);
        Assert.NotEqual(codigo, tarjeta.HashCodigoActivacion); // El codigo en claro NO esta guardado

        // Location header
        Assert.Equal($"/api/interno/tarjetas/{segmento}", r.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task Usuario_normal_no_puede_emitir_403()
    {
        var csrf = await ObtenerCsrfAsync(_prestador);
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/interno/tarjetas");
        req.Headers.Add("X-CSRF-Token", csrf);
        var r = await _prestador.SendAsync(req);

        Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
    }

    [Fact]
    public async Task Verificador_no_puede_emitir_403()
    {
        // Usar sufijo unico para evitar colision con el verificador creado en InitializeAsync
        var verificador = await CrearYAutenticarAsync($"verificador-{Guid.NewGuid():N}", "verificador");
        try
        {
            var csrf = await ObtenerCsrfAsync(verificador);
            var req = new HttpRequestMessage(HttpMethod.Post, "/api/interno/tarjetas");
            req.Headers.Add("X-CSRF-Token", csrf);
            var r = await verificador.SendAsync(req);
            Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
        }
        finally
        {
            verificador.Dispose();
        }
    }

    [Fact]
    public async Task Sin_sesion_emitir_devuelve_401()
    {
        // Sin sesion no hay CSRF, el endpoint rechazara antes por falta de autorizacion
        var r = await _cliente.PostAsync("/api/interno/tarjetas", null);

        Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode);
    }

    [Fact]
    public async Task Emitir_auditoria_no_contiene_codigo_en_claro()
    {
        var csrf = await ObtenerCsrfAsync(_admin);
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/interno/tarjetas");
        req.Headers.Add("X-CSRF-Token", csrf);
        await _admin.SendAsync(req);

        using var ctx = ContextoDePrueba.Crear();
        var auditoria = await ctx.Auditoria
            .Where(a => a.Accion == "TarjetaEmitida")
            .OrderByDescending(a => a.OcurridoEn)
            .FirstAsync();

        Assert.Equal("TarjetaEmitida", auditoria.Accion);
        Assert.Equal("Tarjeta", auditoria.Entidad);
        Assert.NotNull(auditoria.Contexto);
        Assert.DoesNotContain("codigo", auditoria.Contexto!, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("UrlEstable", auditoria.Contexto!);
        Assert.Contains("VenceEn", auditoria.Contexto!);
    }

    // ------------------------------------------------------------- API-55: Activar tarjeta

    private static async Task<(string segmento, string codigo)> EmitirTarjetaAsync(HttpClient admin)
    {
        var csrf = await ObtenerCsrfAsync(admin);
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/interno/tarjetas");
        req.Headers.Add("X-CSRF-Token", csrf);
        var r = await admin.SendAsync(req);
        r.EnsureSuccessStatusCode();
        var body = await r.Content.ReadFromJsonAsync<JsonElement>();
        return (body.GetProperty("segmento").GetString()!, body.GetProperty("codigo").GetString()!);
    }

    [Fact]
    public async Task Activar_con_codigo_correcto_tarjeta_queda_activa_ligada_y_hash_borrado()
    {
        // Primero emitir una tarjeta como admin
        var (segmento, codigo) = await EmitirTarjetaAsync(_admin);

        // Ahora activar como prestador
        var csrf = await ObtenerCsrfAsync(_prestador);
        var r = await _prestador.SendAsync(CrearActivarRequest(_prestador, segmento, codigo, csrf));

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);

        var body = await r.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Tarjeta activada", body.GetProperty("mensaje").GetString());
        Assert.Equal($"/prestadores/{_slug}", body.GetProperty("urlFicha").GetString());

        // Verificar en base: tarjeta activa, ligada al prestador, hash borrado
        using var ctx = ContextoDePrueba.Crear();
        var tarjeta = await ctx.Tarjetas.FirstAsync(t => t.UrlEstable == segmento);
        Assert.Equal(EstadoTarjeta.Activa, tarjeta.Estado);
        Assert.Equal(_perfilPrestadorId, tarjeta.PerfilPrestadorId);
        Assert.NotNull(tarjeta.ActivadaEn);
        Assert.Null(tarjeta.HashCodigoActivacion); // Consumo atomico - hash borrado
    }

    [Fact]
    public async Task Segundo_intento_con_mismo_codigo_rechazo_mismo_mensaje()
    {
        var (segmento, codigo) = await EmitirTarjetaAsync(_admin);

        var csrf = await ObtenerCsrfAsync(_prestador);

        // Primera activacion
        var r1 = await _prestador.SendAsync(CrearActivarRequest(_prestador, segmento, codigo, csrf));
        Assert.Equal(HttpStatusCode.OK, r1.StatusCode);

        // Segunda activacion con el mismo codigo
        var r2 = await _prestador.SendAsync(CrearActivarRequest(_prestador, segmento, codigo, csrf));

        // MISMA respuesta que codigo incorrecto (no revelar cual)
        Assert.Equal(HttpStatusCode.UnprocessableEntity, r2.StatusCode); // 422
        var prob = await r2.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("El código no es válido.", prob.GetProperty("detail").GetString());

        // La tarjeta sigue activa con el primer dueño
        using var ctx = ContextoDePrueba.Crear();
        var tarjeta = await ctx.Tarjetas.FirstAsync(t => t.UrlEstable == segmento);
        Assert.Equal(EstadoTarjeta.Activa, tarjeta.Estado);
        Assert.Equal(_perfilPrestadorId, tarjeta.PerfilPrestadorId);
    }

    [Fact]
    public async Task Codigo_vencido_rechazo_con_mismo_mensaje()
    {
        var (segmento, codigo) = await EmitirTarjetaAsync(_admin);

        // Cambiar la fecha de vencimiento a ayer
        using (var ctx = ContextoDePrueba.Crear())
        {
            var tarjeta = await ctx.Tarjetas.FirstAsync(t => t.UrlEstable == segmento);
            tarjeta.CodigoVenceEn = DateTime.UtcNow.AddDays(-1);
            await ctx.SaveChangesAsync();
        }

        var csrf = await ObtenerCsrfAsync(_prestador);
        var r = await _prestador.SendAsync(CrearActivarRequest(_prestador, segmento, codigo, csrf));

        // MISMA respuesta que codigo incorrecto
        Assert.Equal(HttpStatusCode.UnprocessableEntity, r.StatusCode);
        var prob = await r.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("El código no es válido.", prob.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task Codigo_incorrecto_rechazo_con_mismo_mensaje()
    {
        var (segmento, _) = await EmitirTarjetaAsync(_admin);

        var csrf = await ObtenerCsrfAsync(_prestador);
        // Usar formato valido (4-4-4) pero codigo inexistente
        var r = await _prestador.SendAsync(CrearActivarRequest(_prestador, segmento, "AAAA-AAAA-AAAA", csrf));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, r.StatusCode);
        var prob = await r.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("El código no es válido.", prob.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task Tarjeta_ya_activa_responde_igual_que_codigo_incorrecto_4A()
    {
        // 1. El admin emite la tarjeta T1
        var (segmentoT1, codigoT1) = await EmitirTarjetaAsync(_admin);

        // 2. _prestador la activa con su codigo -> 200
        var csrfPrestador = await ObtenerCsrfAsync(_prestador);
        var rActivarT1 = await _prestador.SendAsync(CrearActivarRequest(_prestador, segmentoT1, codigoT1, csrfPrestador));
        Assert.Equal(HttpStatusCode.OK, rActivarT1.StatusCode);

        // 3. _otroHttp intenta activar T1 con ese MISMO codigo -> guarda status y detail (A)
        var csrfOtro = await ObtenerCsrfAsync(_otroHttp);
        var rOtro = await _otroHttp.SendAsync(CrearActivarRequest(_otroHttp, segmentoT1, codigoT1, csrfOtro));
        var statusA = rOtro.StatusCode;
        var bodyA = await rOtro.Content.ReadFromJsonAsync<JsonElement>();
        var detailA = bodyA.GetProperty("detail").GetString();

        // 4. El admin emite OTRA tarjeta T2 (emitida, no activada)
        var (segmentoT2, _) = await EmitirTarjetaAsync(_admin);

        // 5. _otroHttp intenta activar T2 con un codigo INCORRECTO de 12 caracteres (formato valido) -> guarda status y detail (B)
        // Copiamos el formato de los codigos que devuelve EmitirTarjetaAsync (4-4-4 con alfabeto sin I, L, O, 0, 1)
        var codigoIncorrecto = "AAAA-AAAA-AAAA";
        var rIncorrecto = await _otroHttp.SendAsync(CrearActivarRequest(_otroHttp, segmentoT2, codigoIncorrecto, csrfOtro));
        var statusB = rIncorrecto.StatusCode;
        var bodyB = await rIncorrecto.Content.ReadFromJsonAsync<JsonElement>();
        var detailB = bodyB.GetProperty("detail").GetString();

        // 6. Assert: A y B son IGUALES entre si (tarjeta ya activada responde igual que codigo incorrecto)
        Assert.Equal(statusB, statusA);
        Assert.Equal(detailB, detailA);

        // 7. Assert: T1 sigue siendo de _prestador
        using var ctx = ContextoDePrueba.Crear();
        var tarjetaT1 = await ctx.Tarjetas.FirstAsync(t => t.UrlEstable == segmentoT1);
        Assert.Equal(EstadoTarjeta.Activa, tarjetaT1.Estado);
        Assert.Equal(_perfilPrestadorId, tarjetaT1.PerfilPrestadorId);

        // 8. Assert: T2 sigue Emitida (no se activó con el codigo incorrecto)
        var tarjetaT2 = await ctx.Tarjetas.FirstAsync(t => t.UrlEstable == segmentoT2);
        Assert.Equal(EstadoTarjeta.Emitida, tarjetaT2.Estado);
        Assert.Null(tarjetaT2.PerfilPrestadorId);
    }

    [Fact]
    public async Task Activacion_simultanea_mismo_codigo_solo_una_gana_CC05()
    {
        var (segmento, codigo) = await EmitirTarjetaAsync(_admin);

        // Usar el prestador "otro" ya creado en InitializeAsync
        var csrf1 = await ObtenerCsrfAsync(_prestador);
        var csrf2 = await ObtenerCsrfAsync(_otroHttp);

        // Dos peticiones simultaneas
        var t1 = _prestador.SendAsync(CrearActivarRequest(_prestador, segmento, codigo, csrf1));
        var t2 = _otroHttp.SendAsync(CrearActivarRequest(_otroHttp, segmento, codigo, csrf2));

        await Task.WhenAll(t1, t2);

        var r1 = t1.Result;
        var r2 = t2.Result;

        // Exactamente una OK, la otra 409 o 422 (ya activada)
        var okCount = new[] { r1, r2 }.Count(r => r.StatusCode == HttpStatusCode.OK);
        Assert.Equal(1, okCount);

        // La que gano tiene la tarjeta ligada a su perfil
        using var ctx = ContextoDePrueba.Crear();
        var tarjeta = await ctx.Tarjetas.FirstAsync(t => t.UrlEstable == segmento);
        Assert.Equal(EstadoTarjeta.Activa, tarjeta.Estado);

        var otroUser = await ctx.Usuarios.FirstAsync(u => u.Correo == $"apitj-otro-{_marca}@zass.local");
        var otroPerfil = await ctx.PerfilesPrestador.FirstAsync(p => p.UsuarioId == otroUser.Id);
        Assert.True(tarjeta.PerfilPrestadorId == _perfilPrestadorId ||
                    tarjeta.PerfilPrestadorId == otroPerfil.Id);
    }

    [Fact]
    public async Task Sin_sesion_activar_devuelve_401()
    {
        var (segmento, _) = await EmitirTarjetaAsync(_admin);

        // Usar formato valido pero codigo inexistente
        var r = await _cliente.PostAsJsonAsync($"/t/{segmento}/activar", new { codigo = "AAAA-AAAA-AAAA" });
        Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode);
    }

    [Fact]
    public async Task Perfil_no_publicado_no_puede_activar()
    {
        // Crear un usuario con perfil en estado Borrador (no Publicado)
        var sufijo = $"borrador-{Guid.NewGuid():N}";
        var client = await CrearYAutenticarAsync(sufijo);

        // Cambiar el perfil a estado Borrador (usar el correo correcto con marca)
        using (var ctx = ContextoDePrueba.Crear())
        {
            var usuario = await ctx.Usuarios.SingleAsync(u => u.Correo == $"apitj-{sufijo}-{_marca}@zass.local");
            var perfil = await ctx.PerfilesPrestador.SingleAsync(p => p.UsuarioId == usuario.Id);
            perfil.Estado = EstadoPerfil.Borrador;
            await ctx.SaveChangesAsync();
        }

        var (segmento, codigo) = await EmitirTarjetaAsync(_admin);

        try
        {
            var csrf = await ObtenerCsrfAsync(client);
            var r = await client.SendAsync(CrearActivarRequest(client, segmento, codigo, csrf));

            Assert.Equal(HttpStatusCode.UnprocessableEntity, r.StatusCode);
            var prob = await r.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Contains("publicado", prob.GetProperty("detail").GetString()!);
        }
        finally
        {
            client.Dispose();
        }
    }
    private sealed class CuentaAutenticada
    {
        public required HttpClient Http { get; init; }
        public required string Correo { get; init; }
        public required Guid UsuarioId { get; set; }
        public Guid PerfilId { get; set; }
    }

    public async Task DisposeAsync()
    {
        using (var ctx = ContextoDePrueba.Crear())
        {
            await ctx.Database.ExecuteSqlRawAsync(
                """
                DELETE FROM tarjetas WHERE perfil_prestador_id IN (SELECT id FROM perfiles_prestador WHERE usuario_id IN (SELECT id FROM usuarios WHERE correo LIKE {0}));
                DELETE FROM perfiles_prestador WHERE usuario_id IN (SELECT id FROM usuarios WHERE correo LIKE {0});
                DELETE FROM auditoria WHERE actor_id IN (SELECT id FROM usuarios WHERE correo LIKE {0}) OR entidad_id IN (SELECT id FROM usuarios WHERE correo LIKE {0});
                DELETE FROM notificaciones WHERE destinatario_id IN (SELECT id FROM usuarios WHERE correo LIKE {0});
                DELETE FROM sesiones WHERE usuario_id IN (SELECT id FROM usuarios WHERE correo LIKE {0});
                DELETE FROM perfiles_buscador WHERE usuario_id IN (SELECT id FROM usuarios WHERE correo LIKE {0});
                DELETE FROM usuarios WHERE correo LIKE {0};
                """,
                $"%{_marca}%");
        }
    
        _cliente?.Dispose();
        _admin?.Dispose();
        _prestador?.Dispose();
        if (_otroHttp != null) _otroHttp.Dispose();
        await _app.DisposeAsync();
    }

}

/// <summary>
/// Reloj fijo para pruebas de bloqueo por intentos: permite avanzar el tiempo.
/// </summary>
internal sealed class RelojAjustable : IRelojSistema
{
    private DateTime _ahora;
    public RelojAjustable(DateTime ahora) => _ahora = ahora;
    public DateTime Ahora => _ahora;
    public void Avanzar(TimeSpan cuanto) => _ahora = _ahora.Add(cuanto);
}

/// <summary>
/// Variante de AplicacionConProxy que permite inyectar un IRelojSistema personalizado.
/// Usado para pruebas que controlan el tiempo (bloqueo por intentos, ventanas temporales).
/// </summary>
public sealed class AplicacionConReloj : WebApplicationFactory<Program>
{
    private readonly Dictionary<string, string?> _ajustes;
    private readonly IRelojSistema _reloj;

    public AplicacionConReloj(
        IRelojSistema reloj,
        string? proxies = null, int limitePublico = 10_000, int? saltos = null,
        string? cadena = null, string entorno = "Development",
        Dictionary<string, string?>? extra = null, int? limiteSalud = null)
    {
        _reloj = reloj;
        _entorno = entorno;
        _ajustes = new Dictionary<string, string?>
        {
            ["Zass:CadenaConexion"] = cadena ?? Conexion.Cadena,
            ["Zass:ClaveDireccionesBase64"] =
                Convert.ToBase64String(Enumerable.Range(0, 32).Select(i => (byte)i).ToArray()),
            ["Zass:SecretoAvisoPagos"] =
                "secreto-de-prueba-del-aviso-0123456789ab", // 40 caracteres
            ["Zass:Tasa:A"] = "10000", ["Zass:Tasa:G"] = "10000", ["Zass:Tasa:S"] = "10000",
            ["Zass:Tasa:P"] = limitePublico.ToString(),
            ["Zass:RevisarContrasenasFiltradas"] = "false",
            ["Zass:Fondo:Activo"] = "false",
        };
        if (limiteSalud is not null) _ajustes["Zass:Tasa:Salud"] = limiteSalud.ToString();
        if (proxies is not null) _ajustes[ProxiesDeConfianza.ClaveRedes] = proxies;
        if (saltos is not null) _ajustes[ProxiesDeConfianza.ClaveSaltos] = saltos.ToString();
        if (extra is not null) foreach (var (k, v) in extra) _ajustes[k] = v;
    }

    private string _entorno;

    protected override void ConfigureWebHost(IWebHostBuilder constructor)
    {
        constructor.UseEnvironment(_entorno);
        constructor.ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(_ajustes));
        constructor.ConfigureServices(s =>
        {
            s.AddTransient<IStartupFilter, FiltroDeProxy>();
            // Reemplazar el reloj del sistema con el nuestro
            s.AddSingleton(_reloj);
        });
    }

    private sealed class FiltroDeProxy : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> siguiente) => app =>
        {
            app.Use(async (http, sig) =>
            {
                var remota = http.Request.Headers[AplicacionConProxy.CabeceraRemota].ToString();
                http.Connection.RemoteIpAddress = IPAddress.Parse(
                    string.IsNullOrEmpty(remota) ? "127.0.0.1" : remota);
                await sig();
            });
            siguiente(app);
        };
    }

    public HttpClient Cliente(bool https = true) => CreateClient(new WebApplicationFactoryClientOptions
    {
        AllowAutoRedirect = false,
        BaseAddress = new Uri(https ? "https://localhost" : "http://localhost"),
    });
}

/// <summary>
/// Pruebas del bloqueo por 5 intentos fallidos en 15 minutos (API-55 4B.5).
/// Usan un reloj fijo para controlar las ventanas temporales.
/// </summary>
public class ApiTarjetasBloqueoIntentosPruebas : IAsyncLifetime
{
    private readonly RelojAjustable _reloj;
    private readonly AplicacionConReloj _app;
    private HttpClient _admin = null!;
    private HttpClient _prestador = null!;
    private Guid _perfilPrestadorId;
    private string _marca = "";
    private string _slug = "";

    public ApiTarjetasBloqueoIntentosPruebas()
    {
        _reloj = new RelojAjustable(new DateTime(2026, 11, 15, 12, 0, 0, DateTimeKind.Utc));
        _app = new AplicacionConReloj(_reloj);
    }

    private HttpClient CrearCliente() => _app.CreateClient(new WebApplicationFactoryClientOptions
    {
        HandleCookies = true, AllowAutoRedirect = false,
        BaseAddress = new Uri("https://localhost"),
    });

    public async Task InitializeAsync()
    {
        // Limpiar datos previos
        await LimpiarTodosDatosPruebaAsync();

        _marca = Guid.NewGuid().ToString("N")[..12];
        _slug = $"apitj-bloqueo-{_marca}";

        // Crear y autenticar usuarios, cada uno con su propio cliente con cookies
        _admin = await CrearYAutenticarAsync("admin", "administrador");
        _prestador = await CrearYAutenticarAsync("prestador");
        _perfilPrestadorId = (await ObtenerPerfilIdAsync($"apitj-bloqueo-prestador-{_marca}@zass.local"))!.Value;
    }

    private async Task<HttpClient> CrearYAutenticarAsync(string sufijo, string rol = "prestador")
    {
        var client = CrearCliente();
        var correo = $"apitj-bloqueo-{sufijo}-{_marca}@zass.local";
        var contrasena = "contrasena-larga-1";

        // Registrar siempre como "prestador" (RolesPublicos solo permite buscador/prestador)
        var reg = await client.PostAsJsonAsync("/api/auth/registro", new
        {
            tipo = "particular", apellido = "Prueba", rut = DatosDeRegistro.Rut(), telefono = "+56 9 1234 5678", mayorDeEdad = true, correo,
            contrasena,
            nombre = "Usuario " + sufijo,
            rol = "prestador"
        });
        reg.EnsureSuccessStatusCode();

        // Verificar correo
        using (var ctx = ContextoDePrueba.Crear())
        {
            var usuario = await ctx.Usuarios.SingleAsync(u => u.Correo == correo);
            var aviso = await ctx.Notificaciones
                .Where(n => n.DestinatarioId == usuario.Id
                            && n.Tipo == TipoNotificacion.CorreoVerificacion)
                .OrderByDescending(n => n.ProgramadaPara).FirstAsync();
            var token = Zass.Aplicacion.Puertos.CargaDeAviso.LeerToken(aviso.Cuerpo)!;
            await client.PostAsJsonAsync("/api/auth/correo/verificar", new { token });
            usuario.CorreoVerificado = true;

            // Asignar rol interno si corresponde
            if (rol == "administrador")
            {
                var rolAdmin = await ctx.Roles.FirstAsync(r => r.Codigo == "administrador");
                usuario.Roles.Add(new UsuarioRol { UsuarioId = usuario.Id, RolId = rolAdmin.Id, AsignadoEn = DateTime.UtcNow });
            }

            var rubro = await ctx.Rubros.FirstAsync(r => r.Activo);
            var perfilExistente = await ctx.PerfilesPrestador.SingleOrDefaultAsync(p => p.UsuarioId == usuario.Id);
            var slugEsperado = sufijo == "prestador" ? _slug : $"apitj-bloqueo-{sufijo}-{_marca}";
            if (perfilExistente == null)
            {
                var perfil = new PerfilPrestador
                {
                    UsuarioId = usuario.Id,
                    RubroId = rubro.Id,
                    NombrePublico = "Tarjetas " + sufijo,
                    Slug = slugEsperado,
                    Estado = EstadoPerfil.Publicado,
                    PublicadoEn = _reloj.Ahora.AddDays(-1),
                };
                ctx.PerfilesPrestador.Add(perfil);
            }
            else
            {
                perfilExistente.Estado = EstadoPerfil.Publicado;
                perfilExistente.PublicadoEn = _reloj.Ahora.AddDays(-1);
                perfilExistente.Slug = slugEsperado;
            }
            await ctx.SaveChangesAsync();
        }

        // Iniciar sesión (guarda cookies en el cliente)
        var login = await client.PostAsJsonAsync("/api/auth/sesion", new { correo, contrasena });
        login.EnsureSuccessStatusCode();

        return client;
    }

    private async Task<Guid?> ObtenerPerfilIdAsync(string correo)
    {
        using var ctx = ContextoDePrueba.Crear();
        var user = await ctx.Usuarios.SingleAsync(u => u.Correo == correo);
        var perfil = await ctx.PerfilesPrestador.SingleOrDefaultAsync(p => p.UsuarioId == user.Id);
        return perfil?.Id;
    }

    private async Task LimpiarTodosDatosPruebaAsync()
    {
        using var ctx = ContextoDePrueba.Crear();
        await ctx.Database.ExecuteSqlRawAsync(
            """
            DELETE FROM tarjetas WHERE perfil_prestador_id IN (SELECT id FROM perfiles_prestador WHERE usuario_id IN (SELECT id FROM usuarios WHERE correo LIKE 'apitj-bloqueo-%'));
            DELETE FROM tarjetas WHERE perfil_prestador_id IS NULL AND estado = 'Emitida';
            DELETE FROM perfiles_prestador WHERE usuario_id IN (SELECT id FROM usuarios WHERE correo LIKE 'apitj-bloqueo-%');
            DELETE FROM auditoria WHERE actor_id IN (SELECT id FROM usuarios WHERE correo LIKE 'apitj-bloqueo-%') OR entidad_id IN (SELECT id FROM usuarios WHERE correo LIKE 'apitj-bloqueo-%');
            DELETE FROM notificaciones WHERE destinatario_id IN (SELECT id FROM usuarios WHERE correo LIKE 'apitj-bloqueo-%');
            DELETE FROM sesiones WHERE usuario_id IN (SELECT id FROM usuarios WHERE correo LIKE 'apitj-bloqueo-%');
            DELETE FROM perfiles_buscador WHERE usuario_id IN (SELECT id FROM usuarios WHERE correo LIKE 'apitj-bloqueo-%');
            DELETE FROM usuarios WHERE correo LIKE 'apitj-bloqueo-%';
            """);
    }

    private static async Task<(string segmento, string codigo)> EmitirTarjetaAsync(HttpClient admin, string csrf)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/interno/tarjetas");
        req.Headers.Add("X-CSRF-Token", csrf);
        var r = await admin.SendAsync(req);
        r.EnsureSuccessStatusCode();
        var body = await r.Content.ReadFromJsonAsync<JsonElement>();
        return (body.GetProperty("segmento").GetString()!, body.GetProperty("codigo").GetString()!);
    }

    private static HttpRequestMessage CrearActivarRequest(string segmento, string codigo, string csrf)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, $"/t/{segmento}/activar");
        req.Content = JsonContent.Create(new { codigo });
        req.Headers.Add("X-CSRF-Token", csrf);
        return req;
    }

    private static async Task<string> ObtenerCsrfAsync(HttpClient client)
    {
        var r = await client.GetAsync("/api/auth/csrf");
        r.EnsureSuccessStatusCode();
        var doc = await r.Content.ReadFromJsonAsync<JsonDocument>();
        return doc!.RootElement.GetProperty("token").GetString()!;
    }

    // P1: "5 fallos": 4 fallos y el 5.o intento con el codigo correcto ACTIVA (200, tarjeta Activa)
    [Fact]
    public async Task P1_Cinco_fallos_cuatro_fallos_y_quinto_correcto_activa()
    {
        var csrfAdmin = await ObtenerCsrfAsync(_admin);
        var (segmento, codigo) = await EmitirTarjetaAsync(_admin, csrfAdmin);

        var csrfPrestador = await ObtenerCsrfAsync(_prestador);

        // 4 fallos con codigo incorrecto
        for (int i = 0; i < 4; i++)
        {
            var r = await _prestador.SendAsync(CrearActivarRequest(segmento, "AAAA-AAAA-AAAA", csrfPrestador));
            Assert.Equal(HttpStatusCode.UnprocessableEntity, r.StatusCode);
        }

        // 5.o intento con codigo CORRECTO -> debe activar (200)
        var r5 = await _prestador.SendAsync(CrearActivarRequest(segmento, codigo, csrfPrestador));
        Assert.Equal(HttpStatusCode.OK, r5.StatusCode);

        // Verificar en base: tarjeta Activa
        using var ctx = ContextoDePrueba.Crear();
        var tarjeta = await ctx.Tarjetas.FirstAsync(t => t.UrlEstable == segmento);
        Assert.Equal(EstadoTarjeta.Activa, tarjeta.Estado);
        Assert.Equal(_perfilPrestadorId, tarjeta.PerfilPrestadorId);
        Assert.NotNull(tarjeta.ActivadaEn);
    }

    // P2: "5 fallos bloquean": 5 fallos y despues el codigo correcto -> 422 con el MISMO cuerpo que un codigo incorrecto
    [Fact]
    public async Task P2_Cinco_fallos_bloquean_codigo_correcto_despues_422()
    {
        var csrfAdmin = await ObtenerCsrfAsync(_admin);
        var (segmento, codigo) = await EmitirTarjetaAsync(_admin, csrfAdmin);

        var csrfPrestador = await ObtenerCsrfAsync(_prestador);

        // 5 fallos con codigo incorrecto
        for (int i = 0; i < 5; i++)
        {
            var r = await _prestador.SendAsync(CrearActivarRequest(segmento, "AAAA-AAAA-AAAA", csrfPrestador));
            Assert.Equal(HttpStatusCode.UnprocessableEntity, r.StatusCode);
        }

        // 6.o intento con codigo CORRECTO -> 422 (mismo mensaje que incorrecto)
        var r6 = await _prestador.SendAsync(CrearActivarRequest(segmento, codigo, csrfPrestador));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, r6.StatusCode);
        var prob = await r6.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("El código no es válido.", prob.GetProperty("detail").GetString());

        // Verificar en base: tarjeta sigue Emitida, hash no se consumio
        using var ctx = ContextoDePrueba.Crear();
        var tarjeta = await ctx.Tarjetas.FirstAsync(t => t.UrlEstable == segmento);
        Assert.Equal(EstadoTarjeta.Emitida, tarjeta.Estado);
        Assert.Null(tarjeta.PerfilPrestadorId);
        Assert.NotNull(tarjeta.HashCodigoActivacion);
    }

    // P3: "61 min": 5 fallos, avanzar el reloj 61 minutos -> activa; 59 minutos -> sigue bloqueada
    [Fact]
    public async Task P3_Sesenta_y_un_minutos_despues_del_quinto_fallo_desbloquea()
    {
        var csrfAdmin = await ObtenerCsrfAsync(_admin);
        var (segmento, codigo) = await EmitirTarjetaAsync(_admin, csrfAdmin);

        var csrfPrestador = await ObtenerCsrfAsync(_prestador);

        // 5 fallos con codigo incorrecto
        for (int i = 0; i < 5; i++)
        {
            var r = await _prestador.SendAsync(CrearActivarRequest(segmento, "AAAA-AAAA-AAAA", csrfPrestador));
            Assert.Equal(HttpStatusCode.UnprocessableEntity, r.StatusCode);
        }

        // Avanzar 59 minutos: sigue bloqueada
        _reloj.Avanzar(TimeSpan.FromMinutes(59));

        var r59 = await _prestador.SendAsync(CrearActivarRequest(segmento, codigo, csrfPrestador));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, r59.StatusCode);

        // Avanzar 2 minutos mas (total 61 min): debe desbloquear
        _reloj.Avanzar(TimeSpan.FromMinutes(2));

        var r61 = await _prestador.SendAsync(CrearActivarRequest(segmento, codigo, csrfPrestador));
        Assert.Equal(HttpStatusCode.OK, r61.StatusCode);

        // Verificar en base: tarjeta Activa
        using var ctx = ContextoDePrueba.Crear();
        var tarjeta = await ctx.Tarjetas.FirstAsync(t => t.UrlEstable == segmento);
        Assert.Equal(EstadoTarjeta.Activa, tarjeta.Estado);
    }

    // P4: "espaciados": 4 fallos, avanzar 20 minutos, 4 fallos, codigo correcto -> activa
    [Fact]
    public async Task P4_Espaciados_cuatro_fallos_20_min_cuatro_fallos_correcto_activa()
    {
        var csrfAdmin = await ObtenerCsrfAsync(_admin);
        var (segmento, codigo) = await EmitirTarjetaAsync(_admin, csrfAdmin);

        var csrfPrestador = await ObtenerCsrfAsync(_prestador);

        // 4 fallos
        for (int i = 0; i < 4; i++)
        {
            var r = await _prestador.SendAsync(CrearActivarRequest(segmento, "AAAA-AAAA-AAAA", csrfPrestador));
            Assert.Equal(HttpStatusCode.UnprocessableEntity, r.StatusCode);
        }

        // Avanzar 20 minutos (fuera de la ventana de 15 min)
        _reloj.Avanzar(TimeSpan.FromMinutes(20));

        // 4 fallos mas (estos NO se suman a los anteriores)
        for (int i = 0; i < 4; i++)
        {
            var r = await _prestador.SendAsync(CrearActivarRequest(segmento, "BBBB-BBBB-BBBB", csrfPrestador));
            Assert.Equal(HttpStatusCode.UnprocessableEntity, r.StatusCode);
        }

        // Codigo correcto -> debe activar (no hubo 5 en ninguna ventana de 15 min)
        var rCorrecto = await _prestador.SendAsync(CrearActivarRequest(segmento, codigo, csrfPrestador));
        Assert.Equal(HttpStatusCode.OK, rCorrecto.StatusCode);

        using var ctx = ContextoDePrueba.Crear();
        var tarjeta = await ctx.Tarjetas.FirstAsync(t => t.UrlEstable == segmento);
        Assert.Equal(EstadoTarjeta.Activa, tarjeta.Estado);
    }

    // P5: "otra tarjeta": 5 fallos en la tarjeta A no bloquean la tarjeta B
    [Fact]
    public async Task P5_Otra_tarjeta_cinco_fallos_en_A_no_bloquean_B()
    {
        var csrfAdmin = await ObtenerCsrfAsync(_admin);

        var (segmentoA, _) = await EmitirTarjetaAsync(_admin, csrfAdmin);
        var (segmentoB, codigoB) = await EmitirTarjetaAsync(_admin, csrfAdmin);

        var csrfPrestador = await ObtenerCsrfAsync(_prestador);

        // 5 fallos en tarjeta A
        for (int i = 0; i < 5; i++)
        {
            var r = await _prestador.SendAsync(CrearActivarRequest(segmentoA, "AAAA-AAAA-AAAA", csrfPrestador));
            Assert.Equal(HttpStatusCode.UnprocessableEntity, r.StatusCode);
        }

        // Codigo correcto de tarjeta B -> debe activar
        var rB = await _prestador.SendAsync(CrearActivarRequest(segmentoB, codigoB, csrfPrestador));
        Assert.Equal(HttpStatusCode.OK, rB.StatusCode);

        using var ctx = ContextoDePrueba.Crear();
        var tarjetaA = await ctx.Tarjetas.FirstAsync(t => t.UrlEstable == segmentoA);
        var tarjetaB = await ctx.Tarjetas.FirstAsync(t => t.UrlEstable == segmentoB);
        Assert.Equal(EstadoTarjeta.Emitida, tarjetaA.Estado);
        Assert.Equal(EstadoTarjeta.Activa, tarjetaB.Estado);
    }

    // P6: "auditoria sin codigo": ninguna fila contiene el codigo enviado ni su hash
    [Fact]
    public async Task P6_Auditoria_sin_codigo_ni_hash()
    {
        var csrfAdmin = await ObtenerCsrfAsync(_admin);
        var (segmento, _) = await EmitirTarjetaAsync(_admin, csrfAdmin);

        var csrfPrestador = await ObtenerCsrfAsync(_prestador);

        // 3 fallos
        for (int i = 0; i < 3; i++)
        {
            await _prestador.SendAsync(CrearActivarRequest(segmento, "CCCC-CCCC-CCCC", csrfPrestador));
        }

        // Verificar auditoria: ninguna fila contiene "codigo" ni "hash" en Contexto
        using var ctx = ContextoDePrueba.Crear();
        var tarjetaId = (await ctx.Tarjetas.FirstAsync(t => t.UrlEstable == segmento)).Id;
        var auditoria = await ctx.Auditoria
            .Where(a => a.Entidad == "Tarjeta" && a.EntidadId == tarjetaId)
            .ToListAsync();

        foreach (var a in auditoria)
        {
            if (!string.IsNullOrEmpty(a.Contexto))
            {
                Assert.DoesNotContain("codigo", a.Contexto, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("hash", a.Contexto, StringComparison.OrdinalIgnoreCase);
            }
        }
    }

    // P7: "aviso una vez": EXACTAMENTE 1 fila TarjetaBloqueadaPorIntentos por bloqueo
    [Fact]
    public async Task P7_Aviso_una_sola_vez_por_bloqueo()
    {
        var csrfAdmin = await ObtenerCsrfAsync(_admin);
        var (segmento, codigo) = await EmitirTarjetaAsync(_admin, csrfAdmin);

        var csrfPrestador = await ObtenerCsrfAsync(_prestador);

        // 5 fallos
        for (int i = 0; i < 5; i++)
        {
            await _prestador.SendAsync(CrearActivarRequest(segmento, "DDDD-DDDD-DDDD", csrfPrestador));
        }

        // Verificar: EXACTAMENTE 1 fila TarjetaBloqueadaPorIntentos
        using (var ctx = ContextoDePrueba.Crear())
        {
            var tarjetaId = (await ctx.Tarjetas.FirstAsync(t => t.UrlEstable == segmento)).Id;
            var bloqueos = await ctx.Auditoria
                .Where(a => a.Accion == "TarjetaBloqueadaPorIntentos" && a.Entidad == "Tarjeta" && a.EntidadId == tarjetaId)
                .CountAsync();
            Assert.Equal(1, bloqueos);
        }

        // 3 intentos mas durante el bloqueo
        for (int i = 0; i < 3; i++)
        {
            await _prestador.SendAsync(CrearActivarRequest(segmento, codigo, csrfPrestador));
        }

        // Sigue habiendo EXACTAMENTE 1 fila
        using (var ctx = ContextoDePrueba.Crear())
        {
            var tarjetaId = (await ctx.Tarjetas.FirstAsync(t => t.UrlEstable == segmento)).Id;
            var bloqueos = await ctx.Auditoria
                .Where(a => a.Accion == "TarjetaBloqueadaPorIntentos" && a.Entidad == "Tarjeta" && a.EntidadId == tarjetaId)
                .CountAsync();
            Assert.Equal(1, bloqueos);
        }
    }

    // P8: "simultaneos": 8 intentos simultaneos, luego el correcto -> 422
    [Fact]
    public async Task P8_Simultaneos_ocho_intentos_incorrectos_luego_correcto_bloquea()
    {
        var csrfAdmin = await ObtenerCsrfAsync(_admin);
        var (segmento, codigo) = await EmitirTarjetaAsync(_admin, csrfAdmin);

        var csrfPrestador = await ObtenerCsrfAsync(_prestador);

        // 8 intentos simultaneos usando el cliente autenticado del prestador.
        // IPs distintas via X-Prueba-Remota para no chocar con la tasa A (5/IP/15min).
        var tasks = new List<Task<HttpResponseMessage>>();
        for (int i = 0; i < 8; i++)
        {
            var remota = $"192.168.1.{i + 10}";
            var req = CrearActivarRequest(segmento, "EEEE-EEEE-EEEE", csrfPrestador);
            req.Headers.Add(AplicacionConProxy.CabeceraRemota, remota);
            tasks.Add(_prestador.SendAsync(req));
        }

        var results = await Task.WhenAll(tasks);
        foreach (var r in results)
        {
            Assert.Equal(HttpStatusCode.UnprocessableEntity, r.StatusCode);
        }

        // Intento con codigo correcto -> 422 (tarjeta bloqueada)
        var rCorrecto = await _prestador.SendAsync(CrearActivarRequest(segmento, codigo, csrfPrestador));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, rCorrecto.StatusCode);

        using var ctx = ContextoDePrueba.Crear();
        var tarjeta = await ctx.Tarjetas.FirstAsync(t => t.UrlEstable == segmento);
        Assert.Equal(EstadoTarjeta.Emitida, tarjeta.Estado);
        Assert.NotNull(tarjeta.HashCodigoActivacion);
    }

    public async Task DisposeAsync()
    {
        using (var ctx = ContextoDePrueba.Crear())
        {
            await ctx.Database.ExecuteSqlRawAsync(
                """
                DELETE FROM tarjetas WHERE perfil_prestador_id IN (SELECT id FROM perfiles_prestador WHERE usuario_id IN (SELECT id FROM usuarios WHERE correo LIKE {0}));
                DELETE FROM perfiles_prestador WHERE usuario_id IN (SELECT id FROM usuarios WHERE correo LIKE {0});
                DELETE FROM auditoria WHERE actor_id IN (SELECT id FROM usuarios WHERE correo LIKE {0}) OR entidad_id IN (SELECT id FROM usuarios WHERE correo LIKE {0});
                DELETE FROM notificaciones WHERE destinatario_id IN (SELECT id FROM usuarios WHERE correo LIKE {0});
                DELETE FROM sesiones WHERE usuario_id IN (SELECT id FROM usuarios WHERE correo LIKE {0});
                DELETE FROM perfiles_buscador WHERE usuario_id IN (SELECT id FROM usuarios WHERE correo LIKE {0});
                DELETE FROM usuarios WHERE correo LIKE {0};
                """,
                $"%apitj-bloqueo-{_marca}%");
        }

        _admin?.Dispose();
        _prestador?.Dispose();
        await _app.DisposeAsync();
    }
}
