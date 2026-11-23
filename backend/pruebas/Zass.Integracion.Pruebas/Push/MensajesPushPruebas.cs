using System.Text.Json;
using Microsoft.Extensions.Options;
using Zass.Aplicacion.Agenda;
using Zass.Aplicacion.Notificaciones;
using Zass.Aplicacion.Puertos;
using Zass.Dominio.Comun;
using Zass.Infraestructura.Push;

namespace Zass.Integracion.Pruebas.Push;

/// <summary>
/// ES-3. Los textos del push se leen en la pantalla bloqueada: utiles (quien y cuando) pero sin
/// datos sensibles. Sin base de datos.
/// </summary>
public class MensajesPushPruebas
{
    // Miercoles 25-nov-2026 10:00 en Chile (verano, UTC-3) = 13:00 UTC.
    private static readonly DateTime Inicio = new(2026, 11, 25, 13, 0, 0, DateTimeKind.Utc);

    private static string Rol(string rol) => CargaDeAviso.ConCampos((AvisosDeCita.CampoRol, rol));

    [Fact]
    public void Todo_tipo_que_admite_push_tiene_texto()
    {
        foreach (var tipo in Enum.GetValues<TipoNotificacion>().Where(AvisosPush.AdmitePush))
        {
            var m = MensajesPush.Para(tipo, "{}", null);
            Assert.True(m is not null, $"{tipo} admite push y no tiene texto");
            Assert.False(string.IsNullOrWhiteSpace(m!.Titulo));
            Assert.False(string.IsNullOrWhiteSpace(m.Cuerpo));
            Assert.StartsWith("/", m.Ruta);
        }
    }

    [Fact]
    public void Los_avisos_con_token_no_tienen_texto_de_push()
    {
        Assert.Null(MensajesPush.Para(TipoNotificacion.CorreoVerificacion, CargaDeAviso.ConToken("secreto"), null));
        Assert.Null(MensajesPush.Para(TipoNotificacion.RecuperarContrasena, CargaDeAviso.ConToken("secreto"), null));
    }

    [Fact]
    public void La_hora_va_en_hora_de_Chile()
    {
        Assert.Equal("miércoles 25 de noviembre a las 10:00", MensajesPush.Cuando(Inicio));
    }

    [Fact]
    public void Al_buscador_le_dice_con_quien_y_cuando_quedo_confirmada()
    {
        var m = MensajesPush.Para(TipoNotificacion.CitaConfirmada, Rol(AvisosDeCita.RolBuscador),
            new DatosDeCitaPush(Inicio, "Gasfitería Rojas"))!;

        Assert.Equal("Tu hora quedó confirmada", m.Titulo);
        Assert.Equal("Tu hora con Gasfitería Rojas quedó confirmada para el miércoles 25 de noviembre a las 10:00.", m.Cuerpo);
        Assert.Equal("/mis-citas", m.Ruta);
    }

    [Fact]
    public void Al_prestador_le_dice_quien_agendo()
    {
        var m = MensajesPush.Para(TipoNotificacion.CitaConfirmada, Rol(AvisosDeCita.RolPrestador),
            new DatosDeCitaPush(Inicio, "Ana"))!;

        Assert.Equal("Tienes una hora nueva", m.Titulo);
        Assert.StartsWith("Ana agendó una hora contigo", m.Cuerpo);
    }

    [Fact]
    public void Sin_datos_de_la_cita_sale_igual_sin_nombre_ni_hora()
    {
        var m = MensajesPush.Para(TipoNotificacion.CitaConfirmada, Rol(AvisosDeCita.RolBuscador), null)!;

        Assert.Equal("Tu hora quedó confirmada.", m.Cuerpo);
    }

    [Fact]
    public void El_recordatorio_de_2_horas_dice_solo_la_hora()
    {
        var m = MensajesPush.Para(TipoNotificacion.Recordatorio2h, Rol(AvisosDeCita.RolBuscador),
            new DatosDeCitaPush(Inicio, "Ana"))!;

        Assert.Equal("Recuerda tu hora con Ana hoy a las 10:00.", m.Cuerpo);
    }

    [Fact]
    public void Un_nombre_muy_largo_se_acorta()
    {
        Assert.Equal(41, MensajesPush.Nombre(new string('a', 300))!.Length);
    }

    [Fact]
    public void La_respuesta_a_una_resena_abre_la_ficha_solo_con_un_slug_limpio()
    {
        Assert.Equal("/prestadores/gasfiteria-rojas", MensajesPush.Para(TipoNotificacion.ResenaRespondida,
            CargaDeAviso.ConCampos(("slug", "gasfiteria-rojas")), null)!.Ruta);
        Assert.Equal("/", MensajesPush.Para(TipoNotificacion.ResenaRespondida,
            CargaDeAviso.ConCampos(("slug", "//atacante.cl")), null)!.Ruta);
    }

    [Fact]
    public void Ningun_texto_lleva_lo_que_traiga_la_carga_aparte_de_los_dias_y_el_slug()
    {
        // La carga puede traer cosas que no son para la pantalla bloqueada; ninguna se copia.
        var carga = CargaDeAviso.ConCampos(("token", "TOKEN-SECRETO"), ("telefono", "+56911112222"),
            ("rut", "11111111-1"), ("monto", "5990"), (AvisosDeCita.CampoRol, AvisosDeCita.RolPrestador));
        foreach (var tipo in Enum.GetValues<TipoNotificacion>().Where(AvisosPush.AdmitePush))
        {
            var m = MensajesPush.Para(tipo, carga, new DatosDeCitaPush(Inicio, "Ana"))!;
            var todo = m.Titulo + m.Cuerpo + m.Ruta;
            foreach (var prohibido in new[] { "TOKEN-SECRETO", "+5691111", "11111111", "5990", "$" })
                Assert.DoesNotContain(prohibido, todo);
        }
    }

    [Fact]
    public void El_json_que_viaja_trae_titulo_cuerpo_url_y_etiqueta()
    {
        var json = JsonDocument.Parse(MensajesPush.AJson(new MensajePush("T", "C", "/mis-citas"), "zass-x")).RootElement;

        Assert.Equal("T", json.GetProperty("titulo").GetString());
        Assert.Equal("C", json.GetProperty("cuerpo").GetString());
        Assert.Equal("/mis-citas", json.GetProperty("url").GetString());
        Assert.Equal("zass-x", json.GetProperty("etiqueta").GetString());
    }

    // ------------------------------------------------------------------ opciones

    [Fact]
    public void Sin_claves_el_push_queda_apagado_y_arranca()
    {
        Assert.False(new OpcionesPush().Activo);
        Assert.True(new ValidadorOpcionesPush().Validate(null, new OpcionesPush()).Succeeded);
    }

    [Theory]
    [InlineData("publica", "", "mailto:a@zass.cl")]
    [InlineData("", "privada", "mailto:a@zass.cl")]
    [InlineData("publica", "privada", "")]
    [InlineData("publica", "privada", "soporte@zass.cl")]
    public void Con_media_configuracion_no_arranca(string publica, string privada, string sujeto)
    {
        var o = new OpcionesPush { ClavePublica = publica, ClavePrivada = privada, Sujeto = sujeto };
        Assert.True(new ValidadorOpcionesPush().Validate(null, o).Failed);
    }

    [Fact]
    public void Completa_arranca_y_queda_activo()
    {
        var o = new OpcionesPush { ClavePublica = "p", ClavePrivada = "s", Sujeto = "mailto:soporte@zass.cl" };
        Assert.True(new ValidadorOpcionesPush().Validate(null, o).Succeeded);
        Assert.True(o.Activo);
    }

    // JQ-16 (decision de Pablo, 22-nov): el aviso del borrador tambien va por push. Dice HASTA
    // CUANDO, pero no el nombre de la credencial (lo escribio el prestador y se lee bloqueado).
    [Fact]
    public void El_borrador_por_eliminarse_dice_la_fecha_y_no_el_nombre()
    {
        var m = MensajesPush.Para(TipoNotificacion.BorradorPorEliminarse,
            CargaDeAviso.ConCampos(("credencial", "Título secreto"), ("borrarEl", "2026-12-07")), null)!;
        Assert.Contains("7 de diciembre", m.Cuerpo);
        Assert.DoesNotContain("secreto", m.Titulo + m.Cuerpo, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("/mi-ficha", m.Ruta);
        Assert.True(AvisosPush.AdmitePush(TipoNotificacion.BorradorPorEliminarse));
    }
}
