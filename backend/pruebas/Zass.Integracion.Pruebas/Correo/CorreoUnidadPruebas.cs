using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Zass.Aplicacion.Puertos;
using Zass.Dominio.Comun;
using Zass.Infraestructura.Correo;

namespace Zass.Integracion.Pruebas.Correo;

/// <summary>Piezas del correo que no necesitan base de datos: plantillas, enmascarado, proveedores HTTP, opciones.</summary>
public class CorreoUnidadPruebas
{
    private const string ClaveSecreta = "clave-secreta-de-prueba-123";

    private static OpcionesCorreo Opciones(ProveedorDeCorreo p) => new()
    {
        Proveedor = p, ApiKey = ClaveSecreta, Remitente = "no-responder@zass.cl", NombreRemitente = "ZASS!",
    };

    private static CorreoSaliente Correo() =>
        new("ana@dominio.cl", "Ana Pérez", "Asunto", "texto plano", "<p>html</p>");

    private static EnviadorBrevo Brevo(ManejadorFalso m) =>
        new(new HttpClient(m), Options.Create(Opciones(ProveedorDeCorreo.Brevo)),
            new RegistroEnMemoria<EnviadorBrevo>());

    private static EnviadorResend Resend(ManejadorFalso m) =>
        new(new HttpClient(m), Options.Create(Opciones(ProveedorDeCorreo.Resend)),
            new RegistroEnMemoria<EnviadorResend>());

    // ------------------------------------------------------------ enmascarado

    [Theory]
    [InlineData("abel@dominio.cl", "a***@dominio.cl")]
    [InlineData("x@y.cl", "x***@y.cl")]
    [InlineData("", "***")]
    [InlineData(null, "***")]
    [InlineData("sin-arroba", "***")]
    [InlineData("@dominio.cl", "***")]
    [InlineData("a@", "***")]
    public void El_enmascarado_deja_solo_la_inicial_y_el_dominio(string? entrada, string esperado) =>
        Assert.Equal(esperado, Enmascarar.Correo(entrada));

    // ------------------------------------------------------------ plantillas

    [Fact]
    public void La_verificacion_lleva_el_enlace_correcto_en_texto_y_en_html()
    {
        var r = Plantillas.Renderizar(TipoNotificacion.CorreoVerificacion, "Ana", "https://zass.cl/", "tok_en-1")!;
        const string enlace = "https://zass.cl/verificar-correo?token=tok_en-1";

        Assert.Contains(enlace, r.Texto);
        Assert.Contains($"href=\"{enlace}\"", r.Html);
        Assert.DoesNotContain("zass.cl//", r.Texto);   // la barra final de la URL pública no se duplica
    }

    [Fact]
    public void La_recuperacion_apunta_a_restablecer_contrasena()
    {
        var r = Plantillas.Renderizar(TipoNotificacion.RecuperarContrasena, "Ana", "https://zass.cl", "abc")!;
        Assert.Contains("https://zass.cl/restablecer-contrasena?token=abc", r.Texto);
        Assert.Contains("https://zass.cl/restablecer-contrasena?token=abc", r.Html);
    }

    [Theory]
    [InlineData(TipoNotificacion.CorreoVerificacion)]
    [InlineData(TipoNotificacion.RecuperarContrasena)]
    public void El_asunto_no_lleva_el_token_ni_el_nombre_y_hay_tildes_y_marca(TipoNotificacion tipo)
    {
        var r = Plantillas.Renderizar(tipo, "Ana Pérez", "https://zass.cl", "TOKEN-SECRETO")!;

        Assert.DoesNotContain("TOKEN-SECRETO", r.Asunto);
        Assert.DoesNotContain("Ana", r.Asunto);
        Assert.Contains("ZASS!", r.Asunto);
        Assert.Contains("#F6C81E", r.Html);
        Assert.Contains("#1A1A1A", r.Html);
        Assert.Contains("lang=\"es\"", r.Html);
        Assert.Contains("Pérez", r.Texto);
    }

    [Fact]
    public void Las_plantillas_solo_contienen_lo_del_propio_destinatario()
    {
        // Dos usuarios distintos: el correo de uno no puede mencionar nada del otro.
        var a = Plantillas.Renderizar(TipoNotificacion.CorreoVerificacion, "Ana Pérez", "https://zass.cl", "tokenDeAna")!;
        var b = Plantillas.Renderizar(TipoNotificacion.CorreoVerificacion, "Beto Soto", "https://zass.cl", "tokenDeBeto")!;

        foreach (var parte in new[] { a.Texto, a.Html })
        {
            Assert.DoesNotContain("Beto", parte);
            Assert.DoesNotContain("tokenDeBeto", parte);
        }
        foreach (var parte in new[] { b.Texto, b.Html })
        {
            Assert.DoesNotContain("Ana", parte);
            Assert.DoesNotContain("tokenDeAna", parte);
        }
    }

    [Fact]
    public void El_nombre_y_el_token_se_escapan_en_el_html_y_en_la_url()
    {
        var r = Plantillas.Renderizar(TipoNotificacion.CorreoVerificacion,
            "<script>alert(1)</script>", "https://zass.cl", "a&b=c")!;

        Assert.DoesNotContain("<script>", r.Html);
        Assert.Contains("&lt;script&gt;", r.Html);
        Assert.Contains("token=a%26b%3Dc", r.Texto);
    }

    [Fact]
    public void Un_tipo_sin_plantilla_no_se_renderiza()
    {
        Assert.Null(Plantillas.Renderizar(TipoNotificacion.PerfilPublicado, "Ana", "https://zass.cl", "t"));
        Assert.False(Plantillas.TienePlantilla(TipoNotificacion.CitaConfirmada));
    }

    // ------------------------------------------------------------ carga con token

    [Fact]
    public void La_carga_guarda_el_token_y_la_purgada_ya_no()
    {
        Assert.Equal("tok", CargaDeAviso.LeerToken(CargaDeAviso.ConToken("tok")));
        Assert.Null(CargaDeAviso.LeerToken(CargaDeAviso.Purgada));
        Assert.Null(CargaDeAviso.LeerToken("texto libre que no es JSON"));
        Assert.DoesNotContain("tok", CargaDeAviso.Purgada);
    }

    // ------------------------------------------------------------ Brevo / Resend

    [Fact]
    public async Task Brevo_arma_la_peticion_de_la_API_v3_con_la_clave_solo_en_la_cabecera()
    {
        var m = ManejadorFalso.Responde(HttpStatusCode.Created);
        var r = await Brevo(m).EnviarAsync(Correo());

        Assert.Equal(ResultadoEnvio.Enviado, r.Resultado);
        var p = Assert.Single(m.Peticiones);
        Assert.Equal(HttpMethod.Post, p.Metodo);
        Assert.Equal("https://api.brevo.com/v3/smtp/email", p.Url.ToString());
        Assert.Equal(ClaveSecreta, p.Cabeceras["api-key"]);
        Assert.DoesNotContain(ClaveSecreta, p.Cuerpo);
        Assert.DoesNotContain(ClaveSecreta, p.Url.ToString());

        using var json = JsonDocument.Parse(p.Cuerpo);
        var raiz = json.RootElement;
        Assert.Equal("no-responder@zass.cl", raiz.GetProperty("sender").GetProperty("email").GetString());
        Assert.Equal("ana@dominio.cl", raiz.GetProperty("to")[0].GetProperty("email").GetString());
        Assert.Equal("<p>html</p>", raiz.GetProperty("htmlContent").GetString());
        Assert.Equal("texto plano", raiz.GetProperty("textContent").GetString());
    }

    [Fact]
    public async Task Resend_arma_la_peticion_con_Bearer_y_remitente_con_nombre()
    {
        var m = ManejadorFalso.Responde(HttpStatusCode.OK);
        var r = await Resend(m).EnviarAsync(Correo());

        Assert.Equal(ResultadoEnvio.Enviado, r.Resultado);
        var p = Assert.Single(m.Peticiones);
        Assert.Equal("https://api.resend.com/emails", p.Url.ToString());
        Assert.Equal("Bearer " + ClaveSecreta, p.Cabeceras["Authorization"]);
        Assert.DoesNotContain(ClaveSecreta, p.Cuerpo);

        using var json = JsonDocument.Parse(p.Cuerpo);
        Assert.Equal("ZASS! <no-responder@zass.cl>", json.RootElement.GetProperty("from").GetString());
        Assert.Equal("ana@dominio.cl", json.RootElement.GetProperty("to")[0].GetString());
    }

    [Theory]
    [InlineData(400, ResultadoEnvio.Rechazado)]
    [InlineData(401, ResultadoEnvio.Rechazado)]
    [InlineData(403, ResultadoEnvio.Rechazado)]
    [InlineData(422, ResultadoEnvio.Rechazado)]
    [InlineData(408, ResultadoEnvio.Reintentable)]
    [InlineData(429, ResultadoEnvio.Reintentable)]
    [InlineData(500, ResultadoEnvio.Reintentable)]
    [InlineData(502, ResultadoEnvio.Reintentable)]
    [InlineData(503, ResultadoEnvio.Reintentable)]
    [InlineData(201, ResultadoEnvio.Enviado)]
    public async Task Los_codigos_HTTP_se_clasifican_igual_en_los_dos_proveedores(int codigo, ResultadoEnvio esperado)
    {
        var rb = await Brevo(ManejadorFalso.Responde((HttpStatusCode)codigo)).EnviarAsync(Correo());
        var rr = await Resend(ManejadorFalso.Responde((HttpStatusCode)codigo)).EnviarAsync(Correo());

        Assert.Equal(esperado, rb.Resultado);
        Assert.Equal(esperado, rr.Resultado);
    }

    [Fact]
    public async Task El_detalle_de_un_rechazo_es_solo_el_codigo_y_nunca_el_cuerpo_del_proveedor()
    {
        var m = new ManejadorFalso((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StringContent("invalid recipient ana@dominio.cl"),
        }));
        var r = await Brevo(m).EnviarAsync(Correo());

        Assert.Equal("HTTP 400", r.Detalle);
    }

    [Fact]
    public async Task Un_tiempo_de_espera_agotado_es_reintentable()
    {
        // Lo que HttpClient.Timeout produce: una cancelación que nadie pidió.
        var m = new ManejadorFalso((_, _) => throw new TaskCanceledException("timeout"));
        var r = await Brevo(m).EnviarAsync(Correo());
        Assert.Equal(ResultadoEnvio.Reintentable, r.Resultado);
    }

    [Fact]
    public async Task Un_fallo_de_red_es_reintentable_y_no_filtra_el_mensaje_de_la_excepcion()
    {
        var log = new RegistroEnMemoria<EnviadorBrevo>();
        var m = new ManejadorFalso((_, _) => throw new HttpRequestException("no se pudo conectar a ana@dominio.cl"));
        var enviador = new EnviadorBrevo(new HttpClient(m), Options.Create(Opciones(ProveedorDeCorreo.Brevo)), log);

        var r = await enviador.EnviarAsync(Correo());

        Assert.Equal(ResultadoEnvio.Reintentable, r.Resultado);
        Assert.DoesNotContain("ana@dominio.cl", log.Todo);
    }

    [Fact]
    public async Task Cancelar_a_proposito_se_propaga_y_no_cuenta_como_fallo_del_proveedor()
    {
        using var cts = new CancellationTokenSource();
        var m = new ManejadorFalso((_, _) => { cts.Cancel(); throw new OperationCanceledException(cts.Token); });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Brevo(m).EnviarAsync(Correo(), cts.Token));
    }

    // ------------------------------------------------------------ opciones

    [Theory]
    [InlineData(ProveedorDeCorreo.Brevo)]
    [InlineData(ProveedorDeCorreo.Resend)]
    public void Con_proveedor_y_sin_clave_la_validacion_falla_con_un_mensaje_claro(ProveedorDeCorreo p)
    {
        var o = Opciones(p);
        o.ApiKey = "  ";
        var r = new ValidadorOpcionesCorreo().Validate(null, o);

        Assert.True(r.Failed);
        Assert.Contains("Zass__Correo__ApiKey", string.Join(" ", r.Failures!));
    }

    [Fact]
    public void Con_proveedor_y_sin_remitente_tambien_falla()
    {
        var o = Opciones(ProveedorDeCorreo.Brevo);
        o.Remitente = "";
        Assert.True(new ValidadorOpcionesCorreo().Validate(null, o).Failed);
    }

    [Fact]
    public void Con_Ninguno_no_se_exige_nada_y_es_el_valor_por_omision()
    {
        Assert.Equal(ProveedorDeCorreo.Ninguno, new OpcionesCorreo().Proveedor);
        Assert.True(new ValidadorOpcionesCorreo().Validate(null, new OpcionesCorreo()).Succeeded);
    }

    [Theory]
    [InlineData("https://zass.cl", "https://zass.cl")]
    [InlineData("https://zass.cl/", "https://zass.cl")]
    [InlineData("", null)]
    [InlineData(null, null)]
    [InlineData("zass.cl", null)]
    [InlineData("ftp://zass.cl", null)]
    public void La_url_publica_se_valida(string? entrada, string? esperado) =>
        Assert.Equal(esperado, ProcesadorDeCorreo.UrlPublicaValida(entrada));

    [Fact]
    public async Task Con_Ninguno_el_trabajo_de_fondo_termina_sin_tocar_nada()
    {
        var ambitos = new AmbitosQueExplotan();
        var trabajo = new TrabajoDeCorreo(ambitos, Options.Create(new OpcionesCorreo()),
            new RegistroEnMemoria<TrabajoDeCorreo>());

        await trabajo.StartAsync(CancellationToken.None);
        await (trabajo.ExecuteTask ?? Task.CompletedTask);
        await trabajo.StopAsync(CancellationToken.None);

        Assert.Equal(0, ambitos.Creados);
    }

    private sealed class AmbitosQueExplotan : IServiceScopeFactory
    {
        public int Creados;
        public IServiceScope CreateScope() { Creados++; throw new InvalidOperationException("no debía usarse"); }
    }
}
