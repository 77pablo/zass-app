using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Zass.Aplicacion.Comun;

public static class Texto
{
    /// <summary>RN-S1-01. Un solo lugar normaliza el correo: minusculas y sin espacios.</summary>
    public static string NormalizarCorreo(string correo) => correo.Trim().ToLowerInvariant();

    /// <summary>
    /// Clave con la que se decide si dos nombres de servicio son el mismo: sin tildes,
    /// en minusculas y con los espacios comprimidos.
    ///
    /// Existe porque la comparacion NO puede quedar en manos de la base. El cluster
    /// local se creo con locale C, y ahi <c>lower()</c> solo plega ASCII: lower('Ó')
    /// sigue siendo 'Ó', asi que un indice unico sobre lower(nombre) dejaba entrar
    /// "TASACION COMERCIAL" junto a "Tasacion comercial". Con esta columna la igualdad
    /// es exacta y no depende del locale del servidor, que es distinto en Azure.
    ///
    /// De paso resuelve las tildes: "tasacion" escrito a mano encuentra "Tasación".
    /// </summary>
    public static string ClaveDeNombre(string texto)
    {
        var sinTildes = new string(texto.Normalize(NormalizationForm.FormD)
            .Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            .ToArray()).Normalize(NormalizationForm.FormC);

        return Regex.Replace(sinTildes, @"\s+", " ").Trim().ToLowerInvariant();
    }

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
