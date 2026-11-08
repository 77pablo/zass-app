using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Zass.Dominio.Comun;

namespace Zass.Integracion.Pruebas.Correo;

/// <summary>La API entera con ajustes de correo a medida.</summary>
public sealed class AplicacionConCorreo : WebApplicationFactory<Program>
{
    private readonly Dictionary<string, string?> _ajustes;

    public AplicacionConCorreo(params (string Clave, string? Valor)[] extra)
    {
        _ajustes = new Dictionary<string, string?>
        {
            ["Zass:CadenaConexion"] = Conexion.Cadena,
            ["Zass:ClaveDireccionesBase64"] =
                Convert.ToBase64String(Enumerable.Range(0, 32).Select(i => (byte)i).ToArray()),
            ["Zass:Tasa:A"] = "10000", ["Zass:Tasa:G"] = "10000",
            ["Zass:Tasa:P"] = "10000", ["Zass:Tasa:S"] = "10000",
        };
        foreach (var (clave, valor) in extra) _ajustes[clave] = valor;
    }

    protected override void ConfigureWebHost(IWebHostBuilder constructor)
    {
        constructor.UseEnvironment(Environments.Development);
        constructor.ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(_ajustes));
    }
}

public class CorreoApiPruebas : IAsyncLifetime
{
    private const string Prefijo = "correo-api-";
    private readonly AplicacionConCorreo _app = new();
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
        using var ctx = ContextoDePrueba.Crear();
        await ctx.Database.ExecuteSqlRawAsync("DELETE FROM usuarios WHERE correo LIKE {0}", Prefijo + "%");
        _cliente.Dispose();
        _app.Dispose();
    }

    private static string Correo() => $"{Prefijo}{Guid.NewGuid():N}@zass.local";

    private async Task<int> AvisosAsync(string correo)
    {
        using var ctx = ContextoDePrueba.Crear();
        return await ctx.Notificaciones.CountAsync(n => n.Destinatario!.Correo == correo);
    }

    [Fact]
    public async Task Pedir_recuperacion_de_un_correo_inexistente_no_encola_nada_y_responde_igual()
    {
        var existente = Correo();
        var alta = await _cliente.PostAsJsonAsync("/api/auth/registro", new
        {
            correo = existente, contrasena = "contrasena-larga-1", nombre = "Ana Pérez", rol = "buscador",
        });
        Assert.Equal(HttpStatusCode.Created, alta.StatusCode);

        // El registro encoló UN aviso (la verificación). Recuperar sumará uno más.
        Assert.Equal(1, await AvisosAsync(existente));
        var inexistente = Correo();
        using var antes = ContextoDePrueba.Crear();
        var totalAntes = await antes.Notificaciones.CountAsync();

        var rExiste = await _cliente.PostAsJsonAsync("/api/auth/contrasena/recuperar", new { correo = existente });
        var rNoExiste = await _cliente.PostAsJsonAsync("/api/auth/contrasena/recuperar", new { correo = inexistente });

        // Misma respuesta por fuera (estado y cuerpo): nadie puede averiguar qué correos tienen cuenta...
        Assert.Equal(rExiste.StatusCode, rNoExiste.StatusCode);
        Assert.Equal(await rExiste.Content.ReadAsStringAsync(), await rNoExiste.Content.ReadAsStringAsync());

        // ...pero por dentro solo el existente generó un aviso: el inexistente no encola ni enviaría nada.
        Assert.Equal(2, await AvisosAsync(existente));
        Assert.Equal(0, await AvisosAsync(inexistente));
        using var despues = ContextoDePrueba.Crear();
        Assert.Equal(totalAntes + 1, await despues.Notificaciones.CountAsync());
    }

    [Fact]
    public async Task El_aviso_encolado_por_el_registro_lleva_el_token_en_una_carga_que_se_puede_purgar()
    {
        var correo = Correo();
        await _cliente.PostAsJsonAsync("/api/auth/registro", new
        {
            correo, contrasena = "contrasena-larga-1", nombre = "Ana Pérez", rol = "buscador",
        });

        using var ctx = ContextoDePrueba.Crear();
        var aviso = await ctx.Notificaciones.SingleAsync(n => n.Destinatario!.Correo == correo);

        Assert.Equal(TipoNotificacion.CorreoVerificacion, aviso.Tipo);
        Assert.NotNull(Zass.Aplicacion.Puertos.CargaDeAviso.LeerToken(aviso.Cuerpo));
        Assert.DoesNotContain("Ana", aviso.Cuerpo);        // ya no viaja el nombre en la carga
        Assert.DoesNotContain(aviso.Cuerpo, aviso.Asunto); // y el asunto nunca lleva el token
    }

    // ------------------------------------------------------------ arranque

    [Theory]
    [InlineData("Brevo")]
    [InlineData("Resend")]
    public void Con_proveedor_y_sin_clave_la_aplicacion_se_niega_a_arrancar_con_un_mensaje_claro(string proveedor)
    {
        using var app = new AplicacionConCorreo(
            ("Zass:Correo:Proveedor", proveedor), ("Zass:Correo:Remitente", "no-responder@zass.cl"));

        var ex = Assert.ThrowsAny<Exception>(() => app.CreateClient());

        var validacion = Buscar(ex);
        Assert.NotNull(validacion);
        Assert.Contains("Zass:Correo:ApiKey", validacion!.Message);
    }

    [Fact]
    public void Con_proveedor_clave_y_remitente_la_aplicacion_arranca()
    {
        using var app = new AplicacionConCorreo(
            ("Zass:Correo:Proveedor", "Brevo"), ("Zass:Correo:ApiKey", "clave-de-prueba"),
            ("Zass:Correo:Remitente", "no-responder@zass.cl"),
            // sondeo largo y sin UrlPublica: el trabajo no envia nada (y no toca la red) durante la prueba
            ("Zass:Correo:SondeoSegundos", "3600"));
        using var cliente = app.CreateClient();
        Assert.NotNull(cliente);
    }

    [Fact]
    public async Task Sin_configurar_el_correo_el_proveedor_es_Ninguno_y_los_avisos_quedan_pendientes()
    {
        var correo = Correo();
        await _cliente.PostAsJsonAsync("/api/auth/registro", new
        {
            correo, contrasena = "contrasena-larga-1", nombre = "Ana Pérez", rol = "buscador",
        });
        await Task.Delay(300);   // si hubiera un trabajo enviando, ya habría tocado la fila

        using var ctx = ContextoDePrueba.Crear();
        var aviso = await ctx.Notificaciones.SingleAsync(n => n.Destinatario!.Correo == correo);
        Assert.Equal(EstadoEnvio.Programada, aviso.Estado);
        Assert.Equal(0, aviso.Intentos);
    }

    private static OptionsValidationException? Buscar(Exception? ex)
    {
        for (; ex is not null; ex = ex.InnerException)
        {
            if (ex is OptionsValidationException o) return o;
            if (ex is AggregateException a)
                foreach (var interna in a.InnerExceptions)
                    if (Buscar(interna) is { } hallada) return hallada;
        }
        return null;
    }
}
