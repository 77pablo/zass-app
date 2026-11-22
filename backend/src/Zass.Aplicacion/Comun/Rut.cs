namespace Zass.Aplicacion.Comun;

/// <summary>
/// El RUT chileno: cuerpo + digito verificador por modulo 11. Se guarda SIEMPRE con la misma
/// forma, sin puntos y con guion («12345678-K»), para que el indice unico de la base compare
/// lo mismo: «12.345.678-k» y «12345678K» son el mismo RUT.
/// </summary>
public static class Rut
{
    /// <summary>Desde 1.000.000: un cuerpo de menos digitos no es un RUT que se use hoy.</summary>
    private const int CuerpoMinimo = 1_000_000;

    /// <summary>Hasta 99.999.999: el cuerpo tiene a lo mas 8 digitos.</summary>
    private const int CuerpoMaximo = 99_999_999;

    /// <summary>
    /// La forma guardada, o null si no es un RUT valido (forma o digito verificador).
    /// Acepta puntos, espacios, guion o nada entre el cuerpo y el digito, y la k en minuscula.
    /// </summary>
    public static string? Normalizar(string? texto)
    {
        if (string.IsNullOrWhiteSpace(texto)) return null;

        var limpio = new string(texto.Where(c => !char.IsWhiteSpace(c) && c != '.' && c != '-').ToArray())
            .ToUpperInvariant();
        if (limpio.Length < 2 || limpio.Length > 9) return null;

        var cuerpoTexto = limpio[..^1];
        var dv = limpio[^1];
        if (!cuerpoTexto.All(char.IsAsciiDigit)) return null;
        if (dv != 'K' && !char.IsAsciiDigit(dv)) return null;

        var cuerpo = int.Parse(cuerpoTexto);
        if (cuerpo < CuerpoMinimo || cuerpo > CuerpoMaximo) return null;
        if (DigitoVerificador(cuerpo) != dv) return null;

        return $"{cuerpo}-{dv}";
    }

    public static bool EsValido(string? texto) => Normalizar(texto) is not null;

    /// <summary>Modulo 11: se multiplica de derecha a izquierda por 2..7 y se repite.</summary>
    public static char DigitoVerificador(int cuerpo)
    {
        var suma = 0;
        var factor = 2;
        for (var n = cuerpo; n > 0; n /= 10)
        {
            suma += n % 10 * factor;
            factor = factor == 7 ? 2 : factor + 1;
        }
        var resto = 11 - suma % 11;
        return resto switch { 11 => '0', 10 => 'K', _ => (char)('0' + resto) };
    }
}
