using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Zass.Dominio.Comun;

namespace Zass.Integracion.Pruebas;

/// <summary>
/// ES-3b · API-43 por HTTP: leer y cambiar por que canal llega cada aviso. Lo que se vigila:
/// sesion y CSRF, que el correo de cobro, plan y tarjeta no se pueda apagar, que solo se toque
/// lo de la propia cuenta, y que se pueda usar aunque falten datos de la cuenta (ES-2).
/// </summary>
public partial class ApiPruebas
{
    private const string RutaPreferencias = "/api/mi/notificaciones/preferencias";

    private async Task<HttpResponseMessage> PreferenciasAsync(object cuerpo, bool conCsrf = true)
    {
        using var peticion = new HttpRequestMessage(HttpMethod.Put, RutaPreferencias) { Content = JsonContent.Create(cuerpo) };
        if (conCsrf) peticion.Headers.Add("X-CSRF-Token", await TokenCsrfAsync());
        return await _cliente.SendAsync(peticion);
    }

    private static object Una(string tipo, bool correo, bool push) =>
        new { preferencias = new[] { new { tipo, correo, push } } };

    private static JsonElement Tipo(JsonDocument doc, string tipo) =>
        doc.RootElement.GetProperty("preferencias").EnumerateArray().Single(p => p.GetProperty("tipo").GetString() == tipo);

    private static async Task<List<PreferenciaAviso>> FilasDeAsync(string correo)
    {
        using var ctx = ContextoDePrueba.Crear();
        var id = await ctx.Usuarios.Where(u => u.Correo == correo).Select(u => u.Id).SingleAsync();
        return await ctx.PreferenciasAviso.AsNoTracking().Where(p => p.UsuarioId == id).ToListAsync();
    }

    // ------------------------------------------------------------------------------

    [Fact]
    public async Task ES3b_leer_las_preferencias_exige_sesion()
    {
        var r = await _cliente.GetAsync(RutaPreferencias);

        Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode);
    }

    [Fact]
    public async Task ES3b_sin_cambios_todo_esta_encendido_y_el_correo_de_cobros_es_fijo()
    {
        await UnaCuentaConSesionAsync();

        var r = await _cliente.GetAsync(RutaPreferencias);

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        using var doc = JsonDocument.Parse(await r.Content.ReadAsStringAsync());
        var lista = doc.RootElement.GetProperty("preferencias").EnumerateArray().ToList();
        Assert.Equal(Zass.Aplicacion.Notificaciones.AvisosPush.TiposConPush.Length, lista.Count);
        Assert.All(lista, p => Assert.True(p.GetProperty("correo").GetBoolean() && p.GetProperty("push").GetBoolean()));
        Assert.Equal("citas", lista[0].GetProperty("grupo").GetString());
        Assert.True(Tipo(doc, "CobroRechazado").GetProperty("correoFijo").GetBoolean());
        Assert.False(Tipo(doc, "CitaSolicitada").GetProperty("correoFijo").GetBoolean());
    }

    [Fact]
    public async Task ES3b_cambiar_las_preferencias_exige_sesion()
    {
        var r = await PreferenciasAsync(Una("CitaSolicitada", true, false), conCsrf: false);

        Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode);
    }

    [Fact]
    public async Task ES3b_cambiar_las_preferencias_exige_CSRF()
    {
        var correo = await UnaCuentaConSesionAsync();

        var r = await PreferenciasAsync(Una("CitaSolicitada", true, false), conCsrf: false);

        Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
        Assert.Empty(await FilasDeAsync(correo));
    }

    [Fact]
    public async Task ES3b_apagar_el_push_de_un_tipo_lo_guarda_y_devuelve_como_quedo_todo()
    {
        var correo = await UnaCuentaConSesionAsync();

        var r = await PreferenciasAsync(Una("CitaSolicitada", true, false));

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        using var doc = JsonDocument.Parse(await r.Content.ReadAsStringAsync());
        Assert.False(Tipo(doc, "CitaSolicitada").GetProperty("push").GetBoolean());
        Assert.True(Tipo(doc, "CitaConfirmada").GetProperty("push").GetBoolean());
        var fila = Assert.Single(await FilasDeAsync(correo));
        Assert.Equal(TipoNotificacion.CitaSolicitada, fila.Tipo);
        Assert.True(fila.Correo);
        Assert.False(fila.Push);
    }

    [Fact]
    public async Task ES3b_volver_a_encender_los_dos_canales_borra_la_fila()
    {
        var correo = await UnaCuentaConSesionAsync();
        await PreferenciasAsync(Una("ResenaNueva", false, false));
        Assert.Single(await FilasDeAsync(correo));

        var r = await PreferenciasAsync(Una("ResenaNueva", true, true));

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Empty(await FilasDeAsync(correo));
    }

    [Fact]
    public async Task ES3b_cambiar_un_tipo_ya_guardado_actualiza_la_misma_fila()
    {
        var correo = await UnaCuentaConSesionAsync();
        await PreferenciasAsync(Una("ResenaNueva", false, true));

        await PreferenciasAsync(Una("ResenaNueva", true, false));

        var fila = Assert.Single(await FilasDeAsync(correo));
        Assert.True(fila.Correo);
        Assert.False(fila.Push);
    }

    [Theory]
    [InlineData("CobroRechazado")]
    [InlineData("SuscripcionPorVencer")]
    [InlineData("SuscripcionVencida")]
    [InlineData("TarjetaActivada")]
    public async Task ES3b_el_correo_de_cobro_plan_y_tarjeta_no_se_puede_apagar(string tipo)
    {
        var correo = await UnaCuentaConSesionAsync();

        var r = await PreferenciasAsync(Una(tipo, false, true));

        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        Assert.Empty(await FilasDeAsync(correo));
    }

    [Fact]
    public async Task ES3b_el_push_de_un_cobro_si_se_puede_apagar()
    {
        var correo = await UnaCuentaConSesionAsync();

        var r = await PreferenciasAsync(Una("CobroRechazado", true, false));

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.False(Assert.Single(await FilasDeAsync(correo)).Push);
    }

    [Theory]
    [InlineData("CorreoVerificacion")]
    [InlineData("RecuperarContrasena")]
    [InlineData("NoExiste")]
    public async Task ES3b_un_tipo_que_no_se_configura_se_rechaza(string tipo)
    {
        var correo = await UnaCuentaConSesionAsync();

        var r = await PreferenciasAsync(Una(tipo, true, false));

        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        Assert.Empty(await FilasDeAsync(correo));
    }

    [Fact]
    public async Task ES3b_una_fila_nula_responde_400_y_no_500()
    {
        var correo = await UnaCuentaConSesionAsync();

        var r = await PreferenciasAsync(new { preferencias = new object?[] { null } });

        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        Assert.Empty(await FilasDeAsync(correo));
    }

    [Fact]
    public async Task ES3b_si_una_fila_es_invalida_no_se_guarda_ninguna()
    {
        var correo = await UnaCuentaConSesionAsync();

        var r = await PreferenciasAsync(new { preferencias = new[]
        {
            new { tipo = "CitaSolicitada", correo = true, push = false },
            new { tipo = "CobroRechazado", correo = false, push = false },
        } });

        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        Assert.Empty(await FilasDeAsync(correo));
    }

    [Fact]
    public async Task ES3b_solo_toca_y_muestra_las_preferencias_de_la_propia_cuenta()
    {
        // Otra cuenta con el correo de reseñas apagado, puesta directo en la base.
        var otra = NuevoCorreo();
        await RegistrarAsync(otra);
        using (var ctx = ContextoDePrueba.Crear())
        {
            var id = await ctx.Usuarios.Where(u => u.Correo == otra).Select(u => u.Id).SingleAsync();
            ctx.PreferenciasAviso.Add(new PreferenciaAviso { UsuarioId = id, Tipo = TipoNotificacion.ResenaNueva, Correo = false, Push = true });
            await ctx.SaveChangesAsync();
        }
        var correo = await UnaCuentaConSesionAsync();

        using var antes = JsonDocument.Parse(await _cliente.GetStringAsync(RutaPreferencias));
        await PreferenciasAsync(Una("ResenaNueva", true, false));

        Assert.True(Tipo(antes, "ResenaNueva").GetProperty("correo").GetBoolean());
        var deLaOtra = Assert.Single(await FilasDeAsync(otra));
        Assert.False(deLaOtra.Correo);
        Assert.True(deLaOtra.Push);
        Assert.False(Assert.Single(await FilasDeAsync(correo)).Push);
    }

    [Fact]
    public async Task ES3b_con_datos_de_cuenta_pendientes_igual_se_pueden_ver_y_cambiar_los_avisos()
    {
        var correo = await UnaCuentaAntiguaConSesionAsync();

        var ver = await _cliente.GetAsync(RutaPreferencias);
        var cambiar = await PreferenciasAsync(Una("CitaSolicitada", false, false));

        Assert.Equal(HttpStatusCode.OK, ver.StatusCode);
        Assert.Equal(HttpStatusCode.OK, cambiar.StatusCode);
        Assert.Single(await FilasDeAsync(correo));
    }
}
