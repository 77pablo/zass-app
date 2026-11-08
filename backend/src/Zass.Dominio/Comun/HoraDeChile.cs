namespace Zass.Dominio.Comun;

/// <summary>
/// La hora de pared de quien usa ZASS!.
///
/// <para>
/// **El defecto que esto corrige.** TBL-16 guarda <c>hora_inicio</c> y <c>hora_fin</c> como
/// <c>time</c>, sin zona, y el generador de bloques las trataba como UTC. Un prestador que
/// publicaba «lunes de 09:00 a 11:00» recibía horas a las 06:00 de Chile en verano y a las
/// 05:00 en invierno. No lo cazó ninguna prueba porque todas comparaban cuántos bloques
/// salían, no a qué hora.
/// </para>
///
/// ZASS! opera en Chile y la agenda es local por definición: una hora de atención es la hora
/// del reloj de la pared del prestador, no un instante en UTC. Por eso la zona es un dato del
/// NEGOCIO y vive en el dominio, no en la configuración. El día que ZASS! salga de Chile,
/// esto deja de ser una constante y pasa a ser una columna del perfil, y este archivo es el
/// único lugar que hay que mirar.
///
/// Sin paquetes: <c>TimeZoneInfo</c> es de la biblioteca base, y <c>Zass.Dominio</c> no
/// declara NuGet.
/// </summary>
public static class HoraDeChile
{
    /// <summary>
    /// Se intenta primero el identificador IANA, que es el que funciona en Linux y en el
    /// contenedor de producción, y después el de Windows. .NET 8 traduce entre los dos con
    /// ICU, pero una imagen recortada puede no traerlo: probar los dos evita que la agenda
    /// muera al desplegar por una diferencia de nombre.
    /// </summary>
    public static TimeZoneInfo Zona { get; } = Resolver();

    private static TimeZoneInfo Resolver()
    {
        foreach (var id in new[] { "America/Santiago", "Pacific SA Standard Time" })
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
            catch (TimeZoneNotFoundException) { }
            catch (InvalidTimeZoneException) { }
        }

        // Caerse aquí es mejor que generar una agenda corrida tres horas sin avisar.
        throw new InvalidOperationException(
            "No se encontró la zona horaria de Chile (America/Santiago ni Pacific SA Standard "
            + "Time). Sin ella la agenda quedaría desfasada, así que no se sigue.");
    }

    /// <summary>
    /// El instante UTC de una hora de pared chilena. Devuelve null cuando esa hora **no
    /// existe** ese día: al entrar el horario de verano el reloj salta y hay una hora que no
    /// ocurre. Un bloque ahí no se puede reservar porque no llega nunca.
    /// </summary>
    public static DateTime? AUtc(DateOnly dia, TimeOnly hora)
    {
        var local = dia.ToDateTime(hora, DateTimeKind.Unspecified);
        if (Zona.IsInvalidTime(local)) return null;

        // Una hora AMBIGUA (al salir del horario de verano ocurre dos veces) no se descarta:
        // ConvertTimeToUtc toma la primera, la de verano, y es la que la gente tiene en la
        // cabeza cuando llega el aviso. Descartarla borraría una hora de agenda al año.
        return TimeZoneInfo.ConvertTimeToUtc(local, Zona);
    }

    /// <summary>La hora de pared chilena de un instante UTC.</summary>
    public static DateTime ALocal(DateTime utc) =>
        TimeZoneInfo.ConvertTimeFromUtc(
            utc.Kind == DateTimeKind.Utc ? utc : DateTime.SpecifyKind(utc, DateTimeKind.Utc),
            Zona);

    /// <summary>
    /// El día del calendario chileno de un instante UTC. Importa en los bordes: a las 02:00
    /// UTC del martes en Chile todavía es lunes, y el generador recorre días locales.
    /// </summary>
    public static DateOnly DiaLocal(DateTime utc) => DateOnly.FromDateTime(ALocal(utc));
}
