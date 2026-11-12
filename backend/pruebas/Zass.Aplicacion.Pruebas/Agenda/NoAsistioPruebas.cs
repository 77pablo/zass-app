using Zass.Dominio.Agenda;
using Zass.Dominio.Comun;

namespace Zass.Aplicacion.Pruebas.Agenda;

/// <summary>
/// T10 completa, en el dominio: quién faltó a una cita que no ocurrió (decisión de Pablo,
/// 11-nov-2026).
///
/// Lo que se prueba aquí no es «el método hace lo que dice»: es que las cuatro combinaciones
/// de quién habla y en qué orden terminen siempre en el mismo sitio, porque de ese valor
/// depende después si hay reseña o no.
/// </summary>
public class AcusarInasistenciaPruebas
{
    private static Cita UnaCita(EstadoCita estado = EstadoCita.PendienteAsistencia) => new()
    {
        Codigo = "ZS-TEST", Estado = estado,
    };

    [Fact]
    public void El_prestador_acusa_al_cliente_y_la_cita_queda_no_asistio()
    {
        var cita = UnaCita();

        var r = cita.AcusarInasistencia(FaltaEnCita.Cliente);

        Assert.True(r.Exito);
        Assert.Equal(EstadoCita.NoAsistio, cita.Estado);
        Assert.Equal(FaltaEnCita.Cliente, cita.QuienFalto);
    }

    [Fact]
    public void El_cliente_acusa_al_prestador_y_la_cita_queda_no_asistio()
    {
        var cita = UnaCita();

        var r = cita.AcusarInasistencia(FaltaEnCita.Prestador);

        Assert.True(r.Exito);
        Assert.Equal(EstadoCita.NoAsistio, cita.Estado);
        Assert.Equal(FaltaEnCita.Prestador, cita.QuienFalto);
    }

    /// <summary>
    /// El caso que obligó a que esto acepte una cita que ya está en `no_asistio`: si exigiera
    /// `pendiente_asistencia`, el primero en hablar dejaría al otro sin poder responder y la
    /// discrepancia no existiría nunca.
    /// </summary>
    [Theory]
    [InlineData(FaltaEnCita.Cliente, FaltaEnCita.Prestador)]
    [InlineData(FaltaEnCita.Prestador, FaltaEnCita.Cliente)]
    public void Si_cada_uno_acusa_al_otro_la_cita_queda_en_discrepancia(
        FaltaEnCita primero, FaltaEnCita segundo)
    {
        var cita = UnaCita();

        Assert.True(cita.AcusarInasistencia(primero).Exito);
        Assert.True(cita.AcusarInasistencia(segundo).Exito);

        Assert.Equal(FaltaEnCita.Discrepancia, cita.QuienFalto);
        Assert.Equal(EstadoCita.NoAsistio, cita.Estado);
    }

    /// <summary>De la discrepancia no se sale sola: nadie puede resolverla acusando más.</summary>
    [Theory]
    [InlineData(FaltaEnCita.Cliente)]
    [InlineData(FaltaEnCita.Prestador)]
    public void Desde_la_discrepancia_insistir_no_cambia_nada(FaltaEnCita deNuevo)
    {
        var cita = UnaCita();
        cita.AcusarInasistencia(FaltaEnCita.Cliente);
        cita.AcusarInasistencia(FaltaEnCita.Prestador);

        Assert.True(cita.AcusarInasistencia(deNuevo).Exito);

        Assert.Equal(FaltaEnCita.Discrepancia, cita.QuienFalto);
    }

    /// <summary>Repetir la propia acusación no la convierte en disputa consigo misma.</summary>
    [Theory]
    [InlineData(FaltaEnCita.Cliente)]
    [InlineData(FaltaEnCita.Prestador)]
    public void Repetir_la_misma_acusacion_es_idempotente(FaltaEnCita quien)
    {
        var cita = UnaCita();

        cita.AcusarInasistencia(quien);
        cita.AcusarInasistencia(quien);
        cita.AcusarInasistencia(quien);

        Assert.Equal(quien, cita.QuienFalto);
    }

    /// <summary>
    /// `Nadie` y `Discrepancia` no son acusaciones: son desenlaces. Nadie de fuera puede
    /// escribirlos a mano, y menos saltarse el paso de que alguien hable.
    /// </summary>
    [Theory]
    [InlineData(FaltaEnCita.Nadie)]
    [InlineData(FaltaEnCita.Discrepancia)]
    public void No_se_puede_acusar_con_un_valor_que_no_es_una_persona(FaltaEnCita valor)
    {
        var cita = UnaCita();

        var r = cita.AcusarInasistencia(valor);

        Assert.False(r.Exito);
        Assert.Equal(Fallas.Validacion, r.Codigo);
        Assert.Equal(EstadoCita.PendienteAsistencia, cita.Estado);
        Assert.Equal(FaltaEnCita.Nadie, cita.QuienFalto);
    }

    /// <summary>
    /// Antes de la hora no hay inasistencia que marcar, y después de asumirla realizada
    /// tampoco: las 72 h ya pasaron y la cita se cerró.
    /// </summary>
    [Theory]
    [InlineData(EstadoCita.Solicitada)]
    [InlineData(EstadoCita.Confirmada)]
    [InlineData(EstadoCita.Cancelada)]
    [InlineData(EstadoCita.Rechazada)]
    [InlineData(EstadoCita.Realizada)]
    public void No_se_marca_inasistencia_en_una_cita_que_no_esta_esperando_asistencia(EstadoCita estado)
    {
        var cita = UnaCita(estado);

        var r = cita.AcusarInasistencia(FaltaEnCita.Cliente);

        Assert.False(r.Exito);
        Assert.Equal(Fallas.Conflicto, r.Codigo);
        Assert.Equal(estado, cita.Estado);
        Assert.Equal(FaltaEnCita.Nadie, cita.QuienFalto);
    }

    /// <summary>
    /// T9. El prestador solo puede decir «sí ocurrió» mientras nadie haya dicho lo contrario.
    /// Quien se sienta acusado injustamente responde acusando, que lleva a la discrepancia;
    /// no hay un «yo sí fui» que borre lo que dijo el otro.
    /// </summary>
    [Fact]
    public void Marcar_asistencia_ya_no_es_posible_despues_de_una_acusacion()
    {
        var cita = UnaCita();
        cita.AcusarInasistencia(FaltaEnCita.Prestador);

        var r = cita.RegistrarAsistencia();

        Assert.False(r.Exito);
        Assert.Equal(EstadoCita.NoAsistio, cita.Estado);
        Assert.Equal(FaltaEnCita.Prestador, cita.QuienFalto);
    }

    [Fact]
    public void Marcar_asistencia_deja_la_cita_realizada_y_sin_nadie_ausente()
    {
        var cita = UnaCita();

        Assert.True(cita.RegistrarAsistencia().Exito);

        Assert.Equal(EstadoCita.Realizada, cita.Estado);
        Assert.Equal(FaltaEnCita.Nadie, cita.QuienFalto);
    }
}
