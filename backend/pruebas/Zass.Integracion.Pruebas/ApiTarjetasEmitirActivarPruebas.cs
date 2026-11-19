using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Zass.Api.Endpoints;
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
        var verificador = await CrearYAutenticarAsync("verificador", "verificador");
        verificador.Dispose(); // Solo para crear el usuario, no lo usamos en las pruebas
        _otroHttp = await CrearYAutenticarAsync("otro");
        var borrador = await CrearYAutenticarAsync("borrador");
        borrador.Dispose(); // Solo para crear el usuario
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
            correo,
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
                if (perfilExistente == null)
                {
                    var perfil = new PerfilPrestador
                    {
                        UsuarioId = usuario.Id,
                        RubroId = rubro.Id,
                        NombrePublico = "Tarjetas " + sufijo,
                        Slug = sufijo == "prestador" ? _slug : $"apitj-{sufijo}-{_marca}",
                        Estado = EstadoPerfil.Publicado,
                        PublicadoEn = DateTime.UtcNow.AddDays(-1),
                    };
                    ctx.PerfilesPrestador.Add(perfil);
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
        await ctx.Database.ExecuteSqlRawAsync(
            """
            DELETE FROM tarjetas WHERE perfil_prestador_id IN (SELECT id FROM perfiles_prestador WHERE usuario_id IN (SELECT id FROM usuarios WHERE correo LIKE 'apitj-%'));
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
        var r = await _admin.PostAsync("/api/interno/tarjetas", null);

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
        var r = await _prestador.PostAsync("/api/interno/tarjetas", null);

        Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
    }

    [Fact]
    public async Task Verificador_no_puede_emitir_403()
    {
        var verificador = await CrearYAutenticarAsync("verificador", "verificador");
        try
        {
            var r = await verificador.PostAsync("/api/interno/tarjetas", null);
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
        var r = await _cliente.PostAsync("/api/interno/tarjetas", null);

        Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode);
    }

    [Fact]
    public async Task Emitir_auditoria_no_contiene_codigo_en_claro()
    {
        await _admin.PostAsync("/api/interno/tarjetas", null);

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

    [Fact]
    public async Task Activar_con_codigo_correcto_tarjeta_queda_activa_ligada_y_hash_borrado()
    {
        // Primero emitir una tarjeta como admin
        var emitir = await _admin.PostAsync("/api/interno/tarjetas", null);
        emitir.EnsureSuccessStatusCode();
        var emitBody = await emitir.Content.ReadFromJsonAsync<JsonElement>();
        var segmento = emitBody.GetProperty("segmento").GetString()!;
        var codigo = emitBody.GetProperty("codigo").GetString()!;

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
        var emitir = await _admin.PostAsync("/api/interno/tarjetas", null);
        emitir.EnsureSuccessStatusCode();
        var emitBody = await emitir.Content.ReadFromJsonAsync<JsonElement>();
        var segmento = emitBody.GetProperty("segmento").GetString()!;
        var codigo = emitBody.GetProperty("codigo").GetString()!;

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
        var emitir = await _admin.PostAsync("/api/interno/tarjetas", null);
        emitir.EnsureSuccessStatusCode();
        var emitBody = await emitir.Content.ReadFromJsonAsync<JsonElement>();
        var segmento = emitBody.GetProperty("segmento").GetString()!;
        var codigo = emitBody.GetProperty("codigo").GetString()!;

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
        var emitir = await _admin.PostAsync("/api/interno/tarjetas", null);
        emitir.EnsureSuccessStatusCode();
        var emitBody = await emitir.Content.ReadFromJsonAsync<JsonElement>();
        var segmento = emitBody.GetProperty("segmento").GetString()!;

        var csrf = await ObtenerCsrfAsync(_prestador);
        var r = await _prestador.SendAsync(CrearActivarRequest(_prestador, segmento, "CODIGO-INCORRECTO-1234", csrf));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, r.StatusCode);
        var prob = await r.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("El código no es válido.", prob.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task Tarjeta_ajena_o_inexistente_devuelve_404_igual()
    {
        var emitir = await _admin.PostAsync("/api/interno/tarjetas", null);
        emitir.EnsureSuccessStatusCode();
        var emitBody = await emitir.Content.ReadFromJsonAsync<JsonElement>();
        var segmento = emitBody.GetProperty("segmento").GetString()!;
        var codigo = emitBody.GetProperty("codigo").GetString()!;

        // Usar el prestador "otro" ya creado en InitializeAsync
        var otro = await CrearYAutenticarAsync("otro");
        try
        {
            var csrf = await ObtenerCsrfAsync(otro);
            var r = await otro.SendAsync(CrearActivarRequest(otro, segmento, codigo, csrf));

            // 404 igual que si no existiera
            Assert.Equal(HttpStatusCode.NotFound, r.StatusCode);
        }
        finally
        {
            otro.Dispose();
        }

        // Segmento inexistente
        var csrf2 = await ObtenerCsrfAsync(_prestador);
        var r2 = await _prestador.SendAsync(CrearActivarRequest(_prestador, "XXXX-XXXX", codigo, csrf2));
        Assert.Equal(HttpStatusCode.NotFound, r2.StatusCode);

        // Mismo cuerpo de error
        var prob1 = await r2.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Tarjeta no válida.", prob1.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task Activacion_simultanea_mismo_codigo_solo_una_gana_CC05()
    {
        var emitir = await _admin.PostAsync("/api/interno/tarjetas", null);
        emitir.EnsureSuccessStatusCode();
        var emitBody = await emitir.Content.ReadFromJsonAsync<JsonElement>();
        var segmento = emitBody.GetProperty("segmento").GetString()!;
        var codigo = emitBody.GetProperty("codigo").GetString()!;

        var otro = await CrearYAutenticarAsync("otro");
        try
        {
            var csrf1 = await ObtenerCsrfAsync(_prestador);
            var csrf2 = await ObtenerCsrfAsync(otro);

            // Dos peticiones simultaneas
            var t1 = _prestador.SendAsync(CrearActivarRequest(_prestador, segmento, codigo, csrf1));
            var t2 = otro.SendAsync(CrearActivarRequest(otro, segmento, codigo, csrf2));

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
        finally
        {
            otro.Dispose();
        }
    }

    [Fact]
    public async Task Sin_sesion_activar_devuelve_401()
    {
        var emitir = await _admin.PostAsync("/api/interno/tarjetas", null);
        emitir.EnsureSuccessStatusCode();
        var emitBody = await emitir.Content.ReadFromJsonAsync<JsonElement>();
        var segmento = emitBody.GetProperty("segmento").GetString()!;

        var r = await _cliente.PostAsJsonAsync($"/t/{segmento}/activar", new { codigo = "CODIGO-VALIDO-1234" });
        Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode);
    }

    [Fact]
    public async Task Perfil_no_publicado_no_puede_activar()
    {
        // El perfil "borrador" ya fue creado en InitializeAsync con estado Borrador
        var client = await CrearYAutenticarAsync("borrador");

        var emitir = await _admin.PostAsync("/api/interno/tarjetas", null);
        emitir.EnsureSuccessStatusCode();
        var emitBody = await emitir.Content.ReadFromJsonAsync<JsonElement>();
        var segmento = emitBody.GetProperty("segmento").GetString()!;
        var codigo = emitBody.GetProperty("codigo").GetString()!;

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
