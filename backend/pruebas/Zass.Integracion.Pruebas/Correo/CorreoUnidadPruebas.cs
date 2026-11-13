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
        var r = Plantillas.Renderizar(TipoNotificacion.CorreoVerificacion, "Ana", "https://zass.cl/", CargaDeAviso.ConToken("tok_en-1"))!;
        const string enlace = "https://zass.cl/verificar-correo?token=tok_en-1";

        Assert.Contains(enlace, r.Texto);
        Assert.Contains($"href=\"{enlace}\"", r.Html);
        Assert.DoesNotContain("zass.cl//", r.Texto);   // la barra final de la URL pública no se duplica
    }

    [Fact]
    public void La_recuperacion_apunta_a_restablecer_contrasena()
    {
        var r = Plantillas.Renderizar(TipoNotificacion.RecuperarContrasena, "Ana", "https://zass.cl", CargaDeAviso.ConToken("abc"))!;
        Assert.Contains("https://zass.cl/restablecer-contrasena?token=abc", r.Texto);
        Assert.Contains("https://zass.cl/restablecer-contrasena?token=abc", r.Html);
    }

    [Theory]
    [InlineData(TipoNotificacion.CorreoVerificacion)]
    [InlineData(TipoNotificacion.RecuperarContrasena)]
    public void El_asunto_no_lleva_el_token_ni_el_nombre_y_hay_tildes_y_marca(TipoNotificacion tipo)
    {
        var r = Plantillas.Renderizar(tipo, "Ana Pérez", "https://zass.cl", CargaDeAviso.ConToken("TOKEN-SECRETO"))!;

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
        var a = Plantillas.Renderizar(TipoNotificacion.CorreoVerificacion, "Ana Pérez", "https://zass.cl", CargaDeAviso.ConToken("tokenDeAna"))!;
        var b = Plantillas.Renderizar(TipoNotificacion.CorreoVerificacion, "Beto Soto", "https://zass.cl", CargaDeAviso.ConToken("tokenDeBeto"))!;

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
            "<script>alert(1)</script>", "https://zass.cl", CargaDeAviso.ConToken("a&b=c"))!;

        Assert.DoesNotContain("<script>", r.Html);
        Assert.Contains("&lt;script&gt;", r.Html);
        Assert.Contains("token=a%26b%3Dc", r.Texto);
    }

    // ------------------------------------------------------------ avisos de suscripcion

    [Theory]
    [InlineData(1, "mañana")]
    [InlineData(5, "en 5 días")]
    public void El_aviso_de_por_vencer_dice_cuanto_falta_y_lleva_al_panel(int dias, string esperado)
    {
        var r = Plantillas.Renderizar(TipoNotificacion.SuscripcionPorVencer, "Ana",
            "https://zass.cl/", CargaDeAviso.ConCampos(("dias", dias.ToString())))!;

        Assert.Contains(esperado, r.Asunto);
        Assert.Contains("https://zass.cl/mi-ficha", r.Texto);
        Assert.Contains("href=\"https://zass.cl/mi-ficha\"", r.Html);
        Assert.DoesNotContain("zass.cl//", r.Texto);
    }

    /// <summary>
    /// Lo que mas importa del texto: que no parezca que se pierde el trabajo hecho. Es lo
    /// unico que puede hacer que alguien no renueve creyendo que ya da igual.
    /// </summary>
    [Fact]
    public void Los_dos_avisos_dicen_que_no_se_borra_nada()
    {
        var porVencer = Plantillas.Renderizar(TipoNotificacion.SuscripcionPorVencer, "Ana",
            "https://zass.cl", CargaDeAviso.ConCampos(("dias", "3")))!;
        var vencida = Plantillas.Renderizar(TipoNotificacion.SuscripcionVencida, "Ana",
            "https://zass.cl", CargaDeAviso.ConCampos(("dias", "0")))!;

        foreach (var r in new[] { porVencer, vencida })
        {
            // «no se borra nada» en uno y «no se borró nada» en el otro: se compara la raiz.
            Assert.Contains("no se borr", r.Texto, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("vuelven", r.Texto);
            Assert.Contains("reseñas", r.Texto);
            Assert.Contains("credenciales", r.Texto);
            Assert.Contains("ZASS!", r.Asunto);
        }
    }

    [Fact]
    public void El_aviso_de_vencida_no_necesita_nada_en_la_carga()
    {
        Assert.NotNull(Plantillas.Renderizar(
            TipoNotificacion.SuscripcionVencida, "Ana", "https://zass.cl", CargaDeAviso.Purgada));
    }

    /// <summary>Sin los dias no hay nada que decir: no se envia, en vez de mandar «vence en 0».</summary>
    [Fact]
    public void El_aviso_de_por_vencer_sin_dias_en_la_carga_no_se_renderiza()
    {
        Assert.Null(Plantillas.Renderizar(
            TipoNotificacion.SuscripcionPorVencer, "Ana", "https://zass.cl", CargaDeAviso.Purgada));
    }

    [Fact]
    public void Los_avisos_de_suscripcion_tienen_plantilla_y_no_guardan_ningun_secreto()
    {
        foreach (var tipo in new[]
                 { TipoNotificacion.SuscripcionPorVencer, TipoNotificacion.SuscripcionVencida })
        {
            Assert.True(Plantillas.TienePlantilla(tipo));
            Assert.False(Plantillas.LlevaToken(tipo));
        }
    }

    [Fact]
    public void Un_tipo_sin_plantilla_no_se_renderiza()
    {
        // PerfilPublicado estuvo aqui, y despues CitaConfirmada: las dos tienen plantilla (la
        // segunda desde JQ-1). Recordatorio24h sigue abierto a conciencia: nadie lo encola todavia.
        Assert.Null(Plantillas.Renderizar(TipoNotificacion.Recordatorio24h, "Ana", "https://zass.cl", CargaDeAviso.Purgada));
        Assert.False(Plantillas.TienePlantilla(TipoNotificacion.Recordatorio24h));
    }

    // ------------------------------------------------------------ CU-03: la ficha publicada

    [Fact]
    public void El_aviso_de_ficha_publicada_enlaza_la_ficha_publica_para_compartirla()
    {
        var correo = Plantillas.Renderizar(
            TipoNotificacion.PerfilPublicado, "Ana", "https://zass.cl",
            CargaDeAviso.ConCampos(("slug", "ana-rojas")));

        Assert.NotNull(correo);
        Assert.Equal("Tu ficha ya está publicada en ZASS!", correo!.Asunto);
        Assert.Contains("https://zass.cl/prestadores/ana-rojas", correo.Texto);
        Assert.Contains("https://zass.cl/prestadores/ana-rojas", correo.Html);
        // Lo que de verdad importa que diga: que ya aparece, y que puede pausarla.
        Assert.Contains("ya está publicada", correo.Texto);
        Assert.Contains("pausar", correo.Texto);
    }

    /// <summary>
    /// Los avisos encolados ANTES de que existiera la plantilla llevan una frase suelta en el
    /// cuerpo, no una carga JSON. Tienen que salir igual: el aviso vale aunque el enlace sea el
    /// panel en vez de la ficha. Si esto devolviera null, esos avisos se quedarian en la cola
    /// para siempre.
    /// </summary>
    [Fact]
    public void Un_aviso_de_ficha_publicada_sin_slug_sale_igual_apuntando_al_panel()
    {
        foreach (var carga in new[] { CargaDeAviso.Purgada, "Tu perfil ya aparece en las búsquedas." })
        {
            var correo = Plantillas.Renderizar(TipoNotificacion.PerfilPublicado, "Ana", "https://zass.cl", carga);

            Assert.NotNull(correo);
            Assert.Contains("https://zass.cl/mi-ficha", correo!.Texto);
        }
    }

    [Fact]
    public void El_aviso_de_ficha_publicada_no_lleva_ningun_secreto()
    {
        Assert.True(Plantillas.TienePlantilla(TipoNotificacion.PerfilPublicado));
        Assert.False(Plantillas.LlevaToken(TipoNotificacion.PerfilPublicado));
    }

    /// <summary>Un slug con caracteres raros no puede romper el enlace ni el HTML.</summary>
    [Fact]
    public void El_slug_viaja_escapado_en_el_enlace()
    {
        var correo = Plantillas.Renderizar(
            TipoNotificacion.PerfilPublicado, "Ana", "https://zass.cl",
            CargaDeAviso.ConCampos(("slug", "ana\" onclick=x")));

        Assert.NotNull(correo);
        Assert.DoesNotContain("onclick=x\"", correo!.Html);
    }

    // ------------------------------------------------------------ avisos de resenas

    /// <summary>
    /// S5 promete «notificacion inmediata de cada resena nueva» (derecho a Saber del
    /// resenado) y CU-16.14 manda el enlace para responder. El aviso se encolaba desde el
    /// primer dia, pero sin plantilla el despacho ni lo miraba: no salia, y nadie se
    /// quejaba porque la fila quedaba «Programada» para siempre.
    /// </summary>
    [Theory]
    [InlineData(TipoNotificacion.ResenaNueva)]
    [InlineData(TipoNotificacion.ResenaRespondida)]
    public void Los_avisos_de_resena_tienen_plantilla_y_no_guardan_ningun_secreto(TipoNotificacion tipo)
    {
        Assert.True(Plantillas.TienePlantilla(tipo));
        Assert.False(Plantillas.LlevaToken(tipo));
    }

    /// <summary>
    /// La carga que encola S5 hoy es prosa, no JSON, porque la escribio antes de que
    /// existiera <see cref="CargaDeAviso"/>. Si la plantilla exigiera un campo, cada aviso
    /// ya encolado se volveria «Fallida» el dia del arreglo en vez de salir: estas dos
    /// plantillas no piden nada de la carga, a proposito.
    /// </summary>
    [Theory]
    [InlineData(TipoNotificacion.ResenaNueva)]
    [InlineData(TipoNotificacion.ResenaRespondida)]
    public void Los_avisos_de_resena_se_renderizan_con_cualquier_carga(TipoNotificacion tipo)
    {
        foreach (var carga in new[]
                 { "Alguien dejó una reseña en tu perfil.", CargaDeAviso.Purgada, "", "{}" })
            Assert.NotNull(Plantillas.Renderizar(tipo, "Ana", "https://zass.cl", carga));
    }

    /// <summary>
    /// Lo que NO lleva importa tanto como el enlace: el aviso no adelanta la calificacion
    /// ni el comentario ni quien lo escribio. Primero porque el asunto se ve en la pantalla
    /// bloqueada, y segundo porque una resena puede acabar oculta en moderacion (CS-17) y
    /// el correo ya no se puede retirar.
    /// </summary>
    [Fact]
    public void El_aviso_de_resena_nueva_lleva_al_panel_y_no_adelanta_la_resena()
    {
        var r = Plantillas.Renderizar(TipoNotificacion.ResenaNueva, "Luis Pérez",
            "https://zass.cl/", "Alguien dejó una reseña en tu perfil.")!;

        Assert.Contains("https://zass.cl/mi-ficha", r.Texto);
        Assert.Contains("href=\"https://zass.cl/mi-ficha\"", r.Html);
        Assert.DoesNotContain("zass.cl//", r.Texto);
        Assert.Contains("ZASS!", r.Asunto);
        Assert.DoesNotContain("Luis", r.Asunto);
        Assert.Contains("Luis Pérez", r.Texto);
    }

    [Fact]
    public void El_aviso_de_resena_respondida_lleva_a_la_ficha_publica_cuando_la_carga_trae_el_slug()
    {
        var r = Plantillas.Renderizar(TipoNotificacion.ResenaRespondida, "Ana", "https://zass.cl",
            CargaDeAviso.ConCampos(("slug", "luis-perez-gasfiter")))!;

        Assert.Contains("https://zass.cl/prestadores/luis-perez-gasfiter", r.Texto);
        Assert.Contains("href=\"https://zass.cl/prestadores/luis-perez-gasfiter\"", r.Html);
        Assert.Contains("ZASS!", r.Asunto);
    }

    /// <summary>
    /// Sin slug el correo sale igual, al inicio del sitio: un aviso que llega sin atajo es
    /// mejor que un aviso que no llega. Es el caso de los avisos ya encolados.
    /// </summary>
    [Fact]
    public void Sin_slug_el_aviso_de_resena_respondida_sale_igual_y_apunta_al_sitio()
    {
        var r = Plantillas.Renderizar(TipoNotificacion.ResenaRespondida, "Ana",
            "https://zass.cl/", "El prestador respondió la reseña que dejaste.")!;

        Assert.Contains("https://zass.cl", r.Texto);
        Assert.DoesNotContain("zass.cl//", r.Texto);
        Assert.DoesNotContain("prestadores/", r.Texto);
    }

    /// <summary>
    /// El slug viaja en la URL y en el HTML: si trajera algo raro (lo arma el servidor, pero
    /// aqui nadie confia en eso) no puede romper ni el enlace ni la pagina.
    /// </summary>
    [Fact]
    public void El_slug_se_escapa_en_la_url_y_en_el_html()
    {
        var r = Plantillas.Renderizar(TipoNotificacion.ResenaRespondida, "Ana", "https://zass.cl",
            CargaDeAviso.ConCampos(("slug", "<script>alert(1)</script>")))!;

        Assert.DoesNotContain("<script>", r.Html);
        Assert.DoesNotContain("<script>", r.Texto);
    }

    /// <summary>
    /// El agujero que dejo este defecto se cierra por el lado del catalogo, no por el de
    /// las dos plantillas: cada tipo que algun caso de uso encola como correo tiene que
    /// tener plantilla, o quedarse documentado aqui como pendiente a conciencia. Lo que no
    /// puede volver a pasar es que la lista se desincronice en silencio.
    /// </summary>
    [Fact]
    public void Todo_tipo_que_el_codigo_encola_por_correo_tiene_plantilla_o_esta_en_la_lista_de_pendientes()
    {
        // Encolados hoy por algun servicio de aplicacion (buscar: «Tipo = TipoNotificacion.»).
        var encolados = new[]
        {
            TipoNotificacion.CorreoVerificacion, TipoNotificacion.RecuperarContrasena,
            TipoNotificacion.PerfilPublicado, TipoNotificacion.ResenaNueva,
            TipoNotificacion.ResenaRespondida, TipoNotificacion.SuscripcionPorVencer,
            TipoNotificacion.SuscripcionVencida,
        };
        // Ya no queda ninguno pendiente a conciencia: PerfilPublicado era el ultimo y tiene
        // plantilla desde el 13-nov-2026. Si manana se encola un tipo nuevo sin plantilla, esta
        // prueba lo dice; anadirlo aqui tiene que ser una decision escrita, no un descuido.
        var pendientes = Array.Empty<TipoNotificacion>();

        var huecos = encolados.Except(pendientes).Where(t => !Plantillas.TienePlantilla(t)).ToArray();

        Assert.Empty(huecos);
    }

    // ------------------------------------------------------------ avisos de cita (JQ-1)

    /// <summary>
    /// El hueco de las resenas, cerrado para las citas por el lado del enum: si manana alguien
    /// agrega un tipo «Cita…» y lo encola sin plantilla, el despacho no lo mira nunca y la fila
    /// queda «Programada» para siempre. Esta prueba lo dice el mismo dia.
    /// </summary>
    [Fact]
    public void Todo_tipo_de_cita_del_enum_tiene_plantilla()
    {
        var huecos = Enum.GetValues<TipoNotificacion>()
            .Where(t => t.ToString().StartsWith("Cita", StringComparison.Ordinal))
            .Where(t => !Plantillas.TienePlantilla(t))
            .ToArray();

        Assert.Empty(huecos);
    }

    /// <summary>
    /// El candado con literal: la prueba de arriba recorre el enum, y si alguien renombrara los
    /// tipos o el prefijo, pasaria en verde sin mirar nada. Aqui van los cinco escritos a mano.
    /// </summary>
    [Theory]
    [InlineData(TipoNotificacion.CitaSolicitada)]
    [InlineData(TipoNotificacion.CitaConfirmada)]
    [InlineData(TipoNotificacion.CitaRechazada)]
    [InlineData(TipoNotificacion.CitaCancelada)]
    [InlineData(TipoNotificacion.CitaReprogramada)]
    public void Los_cinco_avisos_de_cita_tienen_plantilla_y_no_guardan_ningun_secreto(TipoNotificacion tipo)
    {
        Assert.True(Plantillas.TienePlantilla(tipo));
        Assert.False(Plantillas.LlevaToken(tipo));
    }

    /// <summary>
    /// Que el recorrido del enum encuentre los cinco, y no cero: un filtro que no calza con
    /// ningun nombre deja la prueba del recorrido vacia y verde.
    /// </summary>
    [Fact]
    public void El_recorrido_del_enum_encuentra_los_cinco_tipos_de_cita()
    {
        var deCita = Enum.GetValues<TipoNotificacion>()
            .Count(t => t.ToString().StartsWith("Cita", StringComparison.Ordinal));

        Assert.Equal(5, deCita);
    }

    /// <summary>
    /// Ninguna exige nada de la carga: con carga vacia, purgada, en prosa o con un rol raro, el
    /// correo sale igual. Un aviso que no se renderiza pasa a «Fallida» y no llega.
    /// </summary>
    [Theory]
    [InlineData(TipoNotificacion.CitaSolicitada)]
    [InlineData(TipoNotificacion.CitaConfirmada)]
    [InlineData(TipoNotificacion.CitaRechazada)]
    [InlineData(TipoNotificacion.CitaCancelada)]
    [InlineData(TipoNotificacion.CitaReprogramada)]
    public void Los_avisos_de_cita_se_renderizan_con_cualquier_carga(TipoNotificacion tipo)
    {
        foreach (var carga in new[]
                 { "", "{}", "Tu hora cambió.", CargaDeAviso.Purgada, "{\"rol\":\"otro\"}", "{\"rol\":\"prestador\"}" })
            Assert.NotNull(Plantillas.Renderizar(tipo, "Ana", "https://zass.cl", carga));
    }

    /// <summary>
    /// Los enlaces, escritos a mano. «Mi agenda» NO es destino de ningun aviso: ahi se definen
    /// horarios, y las citas se responden y se cambian en «Mis citas», para las dos partes.
    /// </summary>
    [Theory]
    [InlineData(TipoNotificacion.CitaSolicitada, "{\"rol\":\"prestador\"}", "https://zass.cl/mis-citas")]
    [InlineData(TipoNotificacion.CitaConfirmada, "{\"rol\":\"prestador\"}", "https://zass.cl/mis-citas")]
    [InlineData(TipoNotificacion.CitaConfirmada, "{\"rol\":\"buscador\"}", "https://zass.cl/mis-citas")]
    [InlineData(TipoNotificacion.CitaRechazada, "{\"rol\":\"buscador\"}", "https://zass.cl/buscar")]
    [InlineData(TipoNotificacion.CitaCancelada, "{\"rol\":\"prestador\"}", "https://zass.cl/mis-citas")]
    [InlineData(TipoNotificacion.CitaCancelada, "{\"rol\":\"buscador\"}", "https://zass.cl/buscar")]
    [InlineData(TipoNotificacion.CitaReprogramada, "{\"rol\":\"prestador\"}", "https://zass.cl/mis-citas")]
    [InlineData(TipoNotificacion.CitaReprogramada, "{\"rol\":\"buscador\"}", "https://zass.cl/mis-citas")]
    public void Cada_aviso_de_cita_enlaza_donde_la_persona_puede_hacer_algo(
        TipoNotificacion tipo, string carga, string enlace)
    {
        var r = Plantillas.Renderizar(tipo, "Ana", "https://zass.cl/", carga)!;

        Assert.Contains(enlace, r.Texto);
        Assert.Contains($"href=\"{enlace}\"", r.Html);
        Assert.DoesNotContain("zass.cl//", r.Texto);
        Assert.DoesNotContain("/mi-agenda", r.Texto);
    }

    /// <summary>
    /// El rol cambia el texto: a quien cancelo no le llega su propia cancelacion, le llega a la
    /// otra parte, y «la persona canceló» y «el prestador canceló» no son intercambiables. Los
    /// asuntos van escritos a mano, sin derivarlos de la plantilla.
    /// </summary>
    [Theory]
    [InlineData(TipoNotificacion.CitaConfirmada, "prestador", "Tienes una hora nueva en ZASS!", "Alguien agendó una hora contigo")]
    [InlineData(TipoNotificacion.CitaConfirmada, "buscador", "Tu hora quedó confirmada en ZASS!", "Tu hora quedó confirmada")]
    [InlineData(TipoNotificacion.CitaCancelada, "prestador", "Se canceló una hora en ZASS!", "La persona que tenía una hora contigo la canceló")]
    [InlineData(TipoNotificacion.CitaCancelada, "buscador", "Se canceló una hora en ZASS!", "El prestador canceló la hora que tenías")]
    [InlineData(TipoNotificacion.CitaReprogramada, "prestador", "Una cita cambió de hora en ZASS!", "La persona que tenía una hora contigo la cambió")]
    [InlineData(TipoNotificacion.CitaReprogramada, "buscador", "Tu hora cambió en ZASS!", "El prestador cambió tu hora")]
    public void El_rol_de_la_carga_elige_el_texto(TipoNotificacion tipo, string rol, string asunto, string frase)
    {
        var r = Plantillas.Renderizar(tipo, "Ana", "https://zass.cl", $"{{\"rol\":\"{rol}\"}}")!;

        Assert.Equal(asunto, r.Asunto);
        Assert.Contains(frase, r.Texto);
    }

    /// <summary>
    /// Sin rol, la de solicitud va al prestador (es el unico que la recibe) y las demas al
    /// buscador. Un rol desconocido cuenta como sin rol.
    /// </summary>
    [Theory]
    [InlineData(TipoNotificacion.CitaSolicitada, "Tienes una solicitud de hora en ZASS!")]
    [InlineData(TipoNotificacion.CitaConfirmada, "Tu hora quedó confirmada en ZASS!")]
    [InlineData(TipoNotificacion.CitaRechazada, "Tu solicitud de hora no se confirmó en ZASS!")]
    [InlineData(TipoNotificacion.CitaCancelada, "Se canceló una hora en ZASS!")]
    [InlineData(TipoNotificacion.CitaReprogramada, "Tu hora cambió en ZASS!")]
    public void Sin_rol_cada_aviso_de_cita_cae_en_su_destinatario_habitual(TipoNotificacion tipo, string asunto)
    {
        foreach (var carga in new[] { "{}", "{\"rol\":\"otro\"}" })
        {
            var r = Plantillas.Renderizar(tipo, "Ana", "https://zass.cl", carga)!;
            Assert.Equal(asunto, r.Asunto);
        }
        // Y el cuerpo sin rol es el del buscador, no el del prestador.
        if (tipo is TipoNotificacion.CitaCancelada)
            Assert.Contains("El prestador canceló",
                Plantillas.Renderizar(tipo, "Ana", "https://zass.cl", "{}")!.Texto);
    }

    /// <summary>
    /// La regla de este modulo: solo el nombre del propio destinatario. El asunto se ve en la
    /// pantalla bloqueada, asi que ni siquiera ese nombre va ahi; y el nombre se escapa en el HTML.
    /// </summary>
    [Theory]
    [InlineData(TipoNotificacion.CitaSolicitada)]
    [InlineData(TipoNotificacion.CitaConfirmada)]
    [InlineData(TipoNotificacion.CitaRechazada)]
    [InlineData(TipoNotificacion.CitaCancelada)]
    [InlineData(TipoNotificacion.CitaReprogramada)]
    public void El_aviso_de_cita_no_pone_el_nombre_en_el_asunto_y_lo_escapa_en_el_html(TipoNotificacion tipo)
    {
        var r = Plantillas.Renderizar(tipo, "Luis <b>Pérez</b>", "https://zass.cl", "{}")!;

        Assert.DoesNotContain("Luis", r.Asunto);
        Assert.DoesNotContain("!", r.Asunto.Replace("ZASS!", ""));
        Assert.Contains("Luis <b>Pérez</b>", r.Texto);
        Assert.DoesNotContain("<b>Pérez</b>", r.Html);
        Assert.Contains("ZASS!", r.Asunto);
    }

    /// <summary>
    /// La solicitud promete un plazo, y el plazo es el de la regla: si alguien cambia
    /// <c>PlazoDeRespuestaManual</c>, este texto miente. Va el 24 a mano, no la constante.
    /// </summary>
    [Fact]
    public void La_solicitud_dice_el_plazo_de_24_horas()
    {
        Assert.Equal(TimeSpan.FromHours(24), Zass.Aplicacion.Agenda.ServicioAgenda.PlazoDeRespuestaManual);
        Assert.Contains("24 horas",
            Plantillas.Renderizar(TipoNotificacion.CitaSolicitada, "Ana", "https://zass.cl", "{}")!.Texto);
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
