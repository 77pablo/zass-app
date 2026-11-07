using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Zass.Aplicacion.Comun;

public static class Texto
{
    /// <summary>RN-S1-01. Un solo lugar normaliza el correo: minusculas y sin espacios.</summary>
    public static string NormalizarCorreo(string correo) => correo.Trim().ToLowerInvariant();

    /// <summary>
    /// Slug para la ficha publica (API-05 /prestadores/{slug}). Sin tildes, sin enes, sin
    /// signos: lo que sobreviva a una URL y a un QR impreso en una tarjeta.
    /// </summary>
    public static string Slug(string texto)
    {
        var sinTildes = new string(texto.Normalize(NormalizationForm.FormD)
            .Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            .ToArray()).Normalize(NormalizationForm.FormC);

        var limpio = Regex.Replace(sinTildes.ToLowerInvariant(), "[^a-z0-9]+", "-").Trim('-');
        return limpio.Length == 0 ? "prestador" : limpio[..Math.Min(limpio.Length, 100)];
    }
}
