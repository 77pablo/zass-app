using Zass.Aplicacion.Resenas;
using Zass.Dominio.Agenda;
using Zass.Dominio.Comun;
using Zass.Dominio.Reputacion;

namespace Zass.Aplicacion.Pruebas.Resenas;

/// <summary>
/// La regla VIVA de elegibilidad, para una cita que no ocurrió (decisión de Pablo del
/// 11-nov-2026 y cuadro de S5 §3).
///
/// Esta clase existe porque durante meses hubo CUATRO respuestas distintas a esta misma
/// pregunta repartidas por el código y solo una corría. Las otras tres se borraron o se
/// alinearon; lo que queda es esto, y es lo que hay que poder leer de una sentada.
/// </summary>
public class ElegibilidadPorInasistenciaPruebas
{
    private static readonly DateTime Ahora = new(2026, 11, 12, 15, 0, 0, DateTimeKind.Utc);

    private static Contacto ConCita(EstadoCita estado, FaltaEnCita quienFalto)
    {
        var cita = new Cita { Codigo = "ZS-TEST", Estado = estado, QuienFalto = quienFalto };
        return new Contacto
        {
            CitaId = cita.Id,
            Cita = cita,
            Origen = OrigenContacto.Cita,
            // Recién ocurrido a propósito: si la regla de la cita fallara y cayera a la rama
            // del contacto suelto, los 7 días no se cumplirían y el «no» vendría de otro sitio.
            OcurridoEn = Ahora.AddMinutes(-5),
        };
    }

    /// <summary>
    /// «Cita no_asistio del prestador: Sí, limitada» (S5 §3). Y limitada de verdad: la reseña
    /// nace de tipo inasistencia, que es lo que CU-16 4C.3 manda rotular.
    /// </summary>
    [Fact]
    public void Si_falto_el_prestador_el_cliente_si_puede_resenar_y_la_resena_es_limitada()
    {
        var contacto = ConCita(EstadoCita.NoAsistio, FaltaEnCita.Prestador);

        var r = ReglasDeResenas.EvaluarContacto(contacto, Ahora, null);

        Assert.True(r.Elegible);
        Assert.Equal(contacto.Id, r.ContactoId);
        Assert.Equal(MotivoDeNoElegibilidad.Ninguno, r.Motivo);
        Assert.Equal(TipoResena.Inasistencia, r.Tipo);
    }

    /// <summary>«Cita no_asistio del buscador: No. No se presentó» (S5 §3).</summary>
    [Fact]
    public void Si_falto_el_cliente_no_hay_resena()
    {
        var r = ReglasDeResenas.EvaluarContacto(
            ConCita(EstadoCita.NoAsistio, FaltaEnCita.Cliente), Ahora, null);

        Assert.False(r.Elegible);
        Assert.Null(r.ContactoId);
        Assert.Equal(MotivoDeNoElegibilidad.CitaNoAsistio, r.Motivo);
    }

    /// <summary>
    /// Se contradicen: bloqueada DE INMEDIATO, no «ya veremos». Y con su propio motivo, no
    /// reusando el del cliente ausente: a quien dice que lo dejaron plantado no se le puede
    /// responder que no se presentó.
    /// </summary>
    [Fact]
    public void Si_cada_uno_acusa_al_otro_la_resena_se_bloquea_con_su_propio_motivo()
    {
        var r = ReglasDeResenas.EvaluarContacto(
            ConCita(EstadoCita.NoAsistio, FaltaEnCita.Discrepancia), Ahora, null);

        Assert.False(r.Elegible);
        Assert.Null(r.ContactoId);
        Assert.Equal(MotivoDeNoElegibilidad.CitaEnDisputa, r.Motivo);
        Assert.NotEqual(MotivoDeNoElegibilidad.CitaNoAsistio, r.Motivo);
        // Y no se le promete una fecha a partir de la cual podrá: no depende del tiempo.
        Assert.Null(r.PuedeDesde);
    }

    /// <summary>
    /// Una cita en `no_asistio` sin nadie marcado no se puede producir hoy —solo se llega ahí
    /// acusando—, pero sí existe en filas anteriores a esta regla. Se cae del lado que no
    /// habilita: inventar un ausente para poder decir que sí sería peor.
    /// </summary>
    [Fact]
    public void Una_cita_no_asistio_sin_nadie_marcado_no_habilita()
    {
        var r = ReglasDeResenas.EvaluarContacto(
            ConCita(EstadoCita.NoAsistio, FaltaEnCita.Nadie), Ahora, null);

        Assert.False(r.Elegible);
        Assert.Equal(MotivoDeNoElegibilidad.CitaNoAsistio, r.Motivo);
    }

    /// <summary>El caso de siempre, para que se vea que no cambió: cita realizada, reseña de servicio.</summary>
    [Fact]
    public void Una_cita_realizada_sigue_habilitando_una_resena_de_servicio()
    {
        var r = ReglasDeResenas.EvaluarContacto(
            ConCita(EstadoCita.Realizada, FaltaEnCita.Nadie), Ahora, null);

        Assert.True(r.Elegible);
        Assert.Equal(TipoResena.Servicio, r.Tipo);
    }

    /// <summary>
    /// `QuienFalto` no se mira cuando la cita no está en `no_asistio`. Si se mirara, una cita
    /// aún por ocurrir con la columna sucia habilitaría reseña antes de tiempo.
    /// </summary>
    [Theory]
    [InlineData(EstadoCita.Confirmada)]
    [InlineData(EstadoCita.PendienteAsistencia)]
    public void Con_la_cita_aun_sin_ocurrir_no_habilita_aunque_alguien_figure_ausente(EstadoCita estado)
    {
        var r = ReglasDeResenas.EvaluarContacto(
            ConCita(estado, FaltaEnCita.Prestador), Ahora, null);

        Assert.False(r.Elegible);
        Assert.Equal(MotivoDeNoElegibilidad.CitaSinRealizar, r.Motivo);
    }
}
