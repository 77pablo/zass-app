using Zass.Aplicacion.Agenda;
using Zass.Dominio.Agenda;
using Zass.Dominio.Comun;

namespace Zass.Aplicacion.Pruebas.Agenda;

/// <summary>
/// JQ-3, la regla de las ventanas sola, en sus bordes. El planificador y el procesador de
/// correo la usan los dos, asi que un borde corrido aqui se nota en los dos lados.
/// </summary>
public class RecordatoriosDeCitaPruebas
{
    private static readonly DateTime Inicio = new(2026, 11, 20, 15, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime CreadaConTiempo = Inicio.AddDays(-5);
    private static readonly TimeSpan Seg = TimeSpan.FromSeconds(1);

    private static bool R24(TimeSpan falta, DateTime? creada = null, EstadoCita estado = EstadoCita.Confirmada) =>
        RecordatoriosDeCita.Corresponde(TipoNotificacion.Recordatorio24h, estado,
            Inicio, creada ?? CreadaConTiempo, Inicio - falta);

    private static bool R2(TimeSpan falta, DateTime? creada = null, EstadoCita estado = EstadoCita.Confirmada) =>
        RecordatoriosDeCita.Corresponde(TipoNotificacion.Recordatorio2h, estado,
            Inicio, creada ?? CreadaConTiempo, Inicio - falta);

    [Fact]
    public void El_de_24_horas_abre_justo_a_las_24_y_no_un_segundo_antes()
    {
        Assert.True(R24(TimeSpan.FromHours(24)));
        Assert.False(R24(TimeSpan.FromHours(24) + Seg));
    }

    [Fact]
    public void El_de_24_horas_cierra_a_las_12_porque_despues_le_toca_al_de_2()
    {
        Assert.True(R24(TimeSpan.FromHours(12)));
        Assert.False(R24(TimeSpan.FromHours(12) - Seg));
    }

    [Fact]
    public void El_de_2_horas_abre_a_las_2_y_cierra_a_los_30_minutos()
    {
        Assert.False(R2(TimeSpan.FromHours(2) + Seg));
        Assert.True(R2(TimeSpan.FromHours(2)));
        Assert.True(R2(TimeSpan.FromMinutes(30)));
        Assert.False(R2(TimeSpan.FromMinutes(30) - Seg));
    }

    [Fact]
    public void Las_dos_ventanas_no_se_pisan()
    {
        // Entre las 12 h y las 2 h no corresponde ninguno: hay un hueco a proposito.
        Assert.False(R24(TimeSpan.FromHours(6)));
        Assert.False(R2(TimeSpan.FromHours(6)));
        // Y nunca los dos a la vez.
        foreach (var minutos in Enumerable.Range(0, 25 * 60))
        {
            var falta = TimeSpan.FromMinutes(minutos);
            Assert.False(R24(falta) && R2(falta), $"Los dos a la vez con {falta} de anticipacion");
        }
    }

    [Fact]
    public void Agendada_con_menos_de_24_horas_no_lleva_el_de_24()
    {
        Assert.True(R24(TimeSpan.FromHours(20), creada: Inicio.AddHours(-24)));
        Assert.False(R24(TimeSpan.FromHours(20), creada: Inicio.AddHours(-24) + Seg));
        // Pero el de 2 h si, que con 23 h de anticipacion sobra.
        Assert.True(R2(TimeSpan.FromHours(1), creada: Inicio.AddHours(-23)));
    }

    [Fact]
    public void Agendada_con_menos_de_2_horas_no_lleva_ninguno()
    {
        var creada = Inicio.AddHours(-2) + Seg;
        Assert.False(R2(TimeSpan.FromHours(1), creada: creada));
        Assert.True(R2(TimeSpan.FromHours(1), creada: Inicio.AddHours(-2)));
    }

    [Theory]
    [InlineData(EstadoCita.Solicitada)]
    [InlineData(EstadoCita.Cancelada)]
    [InlineData(EstadoCita.Reprogramada)]
    [InlineData(EstadoCita.Rechazada)]
    [InlineData(EstadoCita.PendienteAsistencia)]
    public void Solo_una_cita_confirmada_lleva_recordatorio(EstadoCita estado)
    {
        Assert.False(R24(TimeSpan.FromHours(20), estado: estado));
        Assert.False(R2(TimeSpan.FromHours(1), estado: estado));
    }

    [Fact]
    public void Las_claves_son_fijas_y_distintas_por_tipo_y_rol()
    {
        var id = Guid.Parse("11111111-2222-3333-4444-555555555555");
        Assert.Equal("recordatorio-24h:11111111-2222-3333-4444-555555555555:buscador",
            RecordatoriosDeCita.Clave(TipoNotificacion.Recordatorio24h, id, AvisosDeCita.RolBuscador));
        Assert.Equal("recordatorio-2h:11111111-2222-3333-4444-555555555555:prestador",
            RecordatoriosDeCita.Clave(TipoNotificacion.Recordatorio2h, id, AvisosDeCita.RolPrestador));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            RecordatoriosDeCita.Clave(TipoNotificacion.CitaConfirmada, id, AvisosDeCita.RolBuscador));
    }
}
