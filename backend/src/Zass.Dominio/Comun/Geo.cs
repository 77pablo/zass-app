namespace Zass.Dominio.Comun;

/// <summary>
/// Un punto en la Tierra. Existe como tipo propio para que ninguna firma lleve dos
/// `double` sueltos que se puedan intercambiar sin que el compilador diga nada:
/// latitud y longitud al revés no es un error de redondeo, es otro continente.
/// </summary>
public readonly record struct Punto(double Latitud, double Longitud)
{
    public bool EsValido =>
        Latitud is >= -90 and <= 90 && Longitud is >= -180 and <= 180;
}

/// <summary>
/// Distancias, en el dominio y sin dependencias. MOD-04 las necesita en dos sabores muy
/// distintos, y la separación es cómo se hace cumplir RN-S3-03 en el código:
///
/// <list type="bullet">
/// <item><see cref="DistanciaEnMetros"/> es exacta y de uso INTERNO: ordena resultados.</item>
/// <item><see cref="DistanciaPublica"/> es texto redondeado y es la única que puede salir
/// en una respuesta.</item>
/// </list>
///
/// Por qué importa que sean dos: con la distancia exacta a tres prestadores se trilatera
/// la posición de cada uno, y RN-S3-02 prohíbe que una respuesta pública revele dónde
/// está alguien. Redondear a kilómetro entero deja ese cálculo sin precisión útil.
/// </summary>
public static class Geo
{
    /// <summary>Radio medio de la Tierra, en metros (esfera WGS-84).</summary>
    private const double RadioTierraMetros = 6_371_008.8;

    /// <summary>
    /// Haversine. No usa PostGIS a propósito: <c>Zass.Dominio</c> no lleva paquetes, y
    /// esta cuenta tiene que poder probarse sin base de datos.
    ///
    /// Sobre la esfera el error frente al elipsoide es de ~0,3 %: a 10 km son 30 metros,
    /// y lo que se publica está redondeado al kilómetro. Para ordenar por cercanía y
    /// para decir "a 3 km" sobra.
    /// </summary>
    public static double DistanciaEnMetros(Punto a, Punto b)
    {
        var latA = Grados(a.Latitud);
        var latB = Grados(b.Latitud);
        var dLat = latB - latA;
        var dLon = Grados(b.Longitud - a.Longitud);

        var h = Math.Sin(dLat / 2) * Math.Sin(dLat / 2)
                + Math.Cos(latA) * Math.Cos(latB) * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);

        return 2 * RadioTierraMetros * Math.Asin(Math.Min(1, Math.Sqrt(h)));
    }

    /// <summary>
    /// RN-S3-03. Lo único que puede viajar en una respuesta pública.
    ///
    /// Por debajo de un kilómetro no se da número: "a 400 m" en tres fichas distintas
    /// ubica una casa. Por encima, kilómetro entero y sin decimales.
    /// </summary>
    public static string DistanciaPublica(double metros)
    {
        if (double.IsNaN(metros) || metros < 0) return "distancia no disponible";
        if (metros < 1000) return "a menos de 1 km";

        var km = (int)Math.Round(metros / 1000.0, MidpointRounding.AwayFromZero);
        return $"a {km} km";
    }

    private static double Grados(double grados) => grados * Math.PI / 180.0;
}
