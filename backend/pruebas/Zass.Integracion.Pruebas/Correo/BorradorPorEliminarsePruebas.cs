using Zass.Aplicacion.Puertos;
using Zass.Dominio.Comun;
using Zass.Infraestructura.Correo;

namespace Zass.Integracion.Pruebas.Correo;

/// <summary>JQ-16. El correo de «tu borrador se eliminará». No toca la base.</summary>
public class BorradorPorEliminarsePruebas
{
    private static string Carga(string credencial, string fecha) =>
        CargaDeAviso.ConCampos(("credencial", credencial), ("borrarEl", fecha));

    [Fact]
    public void Dice_que_credencial_cuando_y_como_conservarla()
    {
        var correo = Plantillas.Renderizar(
            TipoNotificacion.BorradorPorEliminarse, "Rosa", "https://zass.cl", Carga("Curso de gasfitería", "2026-04-01"));

        Assert.NotNull(correo);
        Assert.Contains("«Curso de gasfitería»", correo!.Texto);
        Assert.Contains("1 de abril de 2026", correo.Texto);
        Assert.Contains("https://zass.cl/mi-ficha", correo.Texto);
        // La versión de texto dice lo mismo que la de HTML, también la nota final.
        Assert.Contains("Si ya no necesitas esa credencial, no tienes que hacer nada.", correo.Texto);
        Assert.Contains("Si ya no necesitas esa credencial, no tienes que hacer nada.", correo.Html);
        Assert.False(Plantillas.LlevaToken(TipoNotificacion.BorradorPorEliminarse));
    }

    /// <summary>El nombre lo escribió la persona: no puede inyectar HTML en el correo.</summary>
    [Fact]
    public void El_nombre_de_la_credencial_viaja_escapado()
    {
        var correo = Plantillas.Renderizar(
            TipoNotificacion.BorradorPorEliminarse, "Rosa", "https://zass.cl", Carga("<b>x</b>", "2026-04-01"));

        Assert.NotNull(correo);
        Assert.DoesNotContain("<b>x</b>", correo!.Html);
        Assert.Contains("&lt;b&gt;x&lt;/b&gt;", correo.Html);
    }

    [Fact]
    public void Sin_fecha_o_sin_credencial_no_se_arma_un_correo_a_medias()
    {
        Assert.Null(Plantillas.Renderizar(TipoNotificacion.BorradorPorEliminarse, "Rosa", "https://zass.cl",
            CargaDeAviso.ConCampos(("credencial", "Curso"))));
        Assert.Null(Plantillas.Renderizar(TipoNotificacion.BorradorPorEliminarse, "Rosa", "https://zass.cl",
            Carga("Curso", "no-es-fecha")));
    }
}
