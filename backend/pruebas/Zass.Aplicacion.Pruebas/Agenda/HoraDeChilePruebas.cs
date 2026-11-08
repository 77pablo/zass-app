using Zass.Dominio.Agenda;
using Zass.Dominio.Comun;

namespace Zass.Aplicacion.Pruebas.Agenda;

/// <summary>
/// La hora de pared chilena, y el defecto que estas pruebas existen para cazar.
///
/// El generador de bloques trataba <c>hora_inicio</c> como UTC. Un prestador que publicaba
/// «lunes de 09:00 a 11:00» recibía horas a las 06:00 de Chile. **Las 36 pruebas de la agenda
/// seguían verdes**, porque todas comparaban cuántos bloques salían y ninguna a qué hora
/// empezaban. Esta clase mira justamente eso.
/// </summary>
public class HoraDeChilePruebas
{
    // Chile cambia la hora el primer sábado de abril y el primer sábado de septiembre, a las
    // 24:00 locales. En 2026: 4 de abril y 5 de septiembre.
    private static readonly DateOnly EnVerano = new(2026, 11, 9);   // UTC-3
    private static readonly DateOnly EnInvierno = new(2026, 7, 9);  // UTC-4

    [Fact]
    public void Las_nueve_de_la_manana_en_verano_son_las_doce_UTC()
    {
        var utc = HoraDeChile.AUtc(EnVerano, new TimeOnly(9, 0));

        Assert.Equal(new DateTime(2026, 11, 9, 12, 0, 0, DateTimeKind.Utc), utc);
    }

    [Fact]
    public void Las_nueve_de_la_manana_en_invierno_son_las_trece_UTC()
    {
        var utc = HoraDeChile.AUtc(EnInvierno, new TimeOnly(9, 0));

        Assert.Equal(new DateTime(2026, 7, 9, 13, 0, 0, DateTimeKind.Utc), utc);
    }

    /// <summary>
    /// El viaje de vuelta tiene que devolver lo mismo que se puso. Si esto falla, alguien ve
    /// en pantalla una hora distinta de la que reservó.
    /// </summary>
    [Theory]
    [InlineData(2026, 1, 15, 8, 30)]
    [InlineData(2026, 5, 20, 14, 0)]
    [InlineData(2026, 9, 30, 23, 45)]
    [InlineData(2026, 12, 24, 0, 15)]
    public void Ir_y_volver_devuelve_la_misma_hora_de_pared(
        int ano, int mes, int dia, int hora, int minuto)
    {
        var local = new TimeOnly(hora, minuto);

        var utc = HoraDeChile.AUtc(new DateOnly(ano, mes, dia), local);
        var vuelta = HoraDeChile.ALocal(utc!.Value);

        Assert.Equal(local, TimeOnly.FromDateTime(vuelta));
        Assert.Equal(new DateOnly(ano, mes, dia), DateOnly.FromDateTime(vuelta));
    }

    /// <summary>
    /// La noche en que entra el horario de verano el reloj salta: hay una hora de pared que no
    /// ocurre. Un bloque ahí no se puede reservar porque nunca llega.
    /// </summary>
    [Fact]
    public void La_hora_que_el_horario_de_verano_se_salta_no_existe()
    {
        // 5 de septiembre de 2026 a las 24:00 locales: el 6 pasa de 00:00 a 01:00.
        var saltada = HoraDeChile.AUtc(new DateOnly(2026, 9, 6), new TimeOnly(0, 30));

        Assert.Null(saltada);
    }

    [Fact]
    public void Un_dia_UTC_puede_ser_el_dia_anterior_en_Chile()
    {
        // 02:00 UTC del martes: en Chile (UTC-3) son las 23:00 del lunes.
        var dia = HoraDeChile.DiaLocal(new DateTime(2026, 11, 10, 2, 0, 0, DateTimeKind.Utc));

        Assert.Equal(new DateOnly(2026, 11, 9), dia);
    }

    // ------------------------------------------------------------- el generador

    [Fact]
    public void Un_bloque_de_las_nueve_empieza_a_las_nueve_de_Chile()
    {
        var regla = new Disponibilidad
        {
            PerfilPrestadorId = Guid.NewGuid(),
            PrestadorServicioId = Guid.NewGuid(),
            DiaSemana = 1,                     // lunes
            HoraInicio = new TimeOnly(9, 0),
            HoraFin = new TimeOnly(10, 0),
            DuracionBloqueMin = 30,
            VigenteDesde = new DateOnly(2026, 11, 1),
        };

        // Lunes 9 de noviembre de 2026.
        var bloques = regla.GenerarBloques(new DateOnly(2026, 11, 9), new DateOnly(2026, 11, 9));

        Assert.Equal(2, bloques.Count);
        Assert.Equal(new DateTime(2026, 11, 9, 12, 0, 0, DateTimeKind.Utc), bloques[0].InicioEn);
        Assert.Equal(new TimeOnly(9, 0), TimeOnly.FromDateTime(HoraDeChile.ALocal(bloques[0].InicioEn)));
        Assert.Equal(new TimeOnly(9, 30), TimeOnly.FromDateTime(HoraDeChile.ALocal(bloques[1].InicioEn)));
    }

    /// <summary>
    /// La misma regla en invierno cae en otro instante UTC. Es la prueba de que la conversión
    /// no es un desplazamiento fijo de tres horas escrito a mano.
    /// </summary>
    [Fact]
    public void La_misma_regla_en_invierno_cae_una_hora_despues_en_UTC()
    {
        var regla = new Disponibilidad
        {
            PerfilPrestadorId = Guid.NewGuid(),
            PrestadorServicioId = Guid.NewGuid(),
            DiaSemana = 4,                     // jueves
            HoraInicio = new TimeOnly(9, 0),
            HoraFin = new TimeOnly(9, 30),
            DuracionBloqueMin = 30,
            VigenteDesde = new DateOnly(2026, 1, 1),
        };

        // Jueves 9 de julio de 2026.
        var bloque = Assert.Single(
            regla.GenerarBloques(new DateOnly(2026, 7, 9), new DateOnly(2026, 7, 9)));

        Assert.Equal(new DateTime(2026, 7, 9, 13, 0, 0, DateTimeKind.Utc), bloque.InicioEn);
        Assert.Equal(new TimeOnly(9, 0), TimeOnly.FromDateTime(HoraDeChile.ALocal(bloque.InicioEn)));
    }

    /// <summary>
    /// Una franja nocturna cruza el día en UTC, y eso está bien: lo que el prestador publicó
    /// es «lunes a las 22:00», no «lunes en UTC».
    /// </summary>
    [Fact]
    public void Una_franja_nocturna_del_lunes_cae_en_martes_UTC_sin_dejar_de_ser_lunes_en_Chile()
    {
        var regla = new Disponibilidad
        {
            PerfilPrestadorId = Guid.NewGuid(),
            PrestadorServicioId = Guid.NewGuid(),
            DiaSemana = 1,
            HoraInicio = new TimeOnly(22, 0),
            HoraFin = new TimeOnly(22, 30),
            DuracionBloqueMin = 30,
            VigenteDesde = new DateOnly(2026, 11, 1),
        };

        var bloque = Assert.Single(
            regla.GenerarBloques(new DateOnly(2026, 11, 9), new DateOnly(2026, 11, 9)));

        Assert.Equal(DayOfWeek.Tuesday, bloque.InicioEn.DayOfWeek);
        Assert.Equal(DayOfWeek.Monday, HoraDeChile.ALocal(bloque.InicioEn).DayOfWeek);
        Assert.Equal(new TimeOnly(22, 0), TimeOnly.FromDateTime(HoraDeChile.ALocal(bloque.InicioEn)));
    }

    [Fact]
    public void El_dia_del_salto_el_bloque_que_no_existe_simplemente_no_se_genera()
    {
        var regla = new Disponibilidad
        {
            PerfilPrestadorId = Guid.NewGuid(),
            PrestadorServicioId = Guid.NewGuid(),
            DiaSemana = 7,                     // domingo
            HoraInicio = new TimeOnly(0, 0),
            HoraFin = new TimeOnly(2, 0),
            DuracionBloqueMin = 30,
            VigenteDesde = new DateOnly(2026, 1, 1),
        };

        // Domingo 6 de septiembre de 2026: 00:00 a 01:00 no ocurre.
        var bloques = regla.GenerarBloques(new DateOnly(2026, 9, 6), new DateOnly(2026, 9, 6));

        // De los cuatro de la franja quedan los dos de 01:00 y 01:30.
        Assert.Equal(2, bloques.Count);
        Assert.All(bloques, b =>
            Assert.True(TimeOnly.FromDateTime(HoraDeChile.ALocal(b.InicioEn)) >= new TimeOnly(1, 0)));
    }
}
