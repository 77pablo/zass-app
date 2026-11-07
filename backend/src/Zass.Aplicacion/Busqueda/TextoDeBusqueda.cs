using System.Globalization;
using System.Text;

namespace Zass.Aplicacion.Busqueda;

/// <summary>
/// Cómo se entiende lo que la persona escribe en la barra de búsqueda: "Abogados", "gasfiter",
/// "divorcio", "María Soto". Todo lo que sigue es texto sin tildes ni mayúsculas.
///
/// El resultado son GRUPOS: cada palabra escrita es un grupo, y a cada grupo le pueden servir
/// varias formas ("abogado" sirve como "abogado" y como "derecho", que es como se llama el rubro).
/// Una persona aparece si cumple TODOS los grupos, con cualquiera de sus formas, en su nombre,
/// su rubro o alguno de sus servicios.
/// </summary>
public static class TextoDeBusqueda
{
    public const int LargoMaximoConsulta = 80;
    public const int MaximoDePalabras = 6;
    public const int LargoMinimoDePalabra = 2;

    /// <summary>
    /// Las palabras con que la gente nombra un oficio y que no son el nombre del rubro.
    /// Quien escribe «abogado» busca el rubro «Derecho», y sin esta tabla no aparecería nadie.
    /// Es una lista a mano y corta A PROPÓSITO: cada entrada es una decisión sobre qué significa
    /// la palabra, y una lista enorme se llena de falsos positivos. Las claves van en singular.
    /// </summary>
    private static readonly Dictionary<string, string[]> Sinonimos = new()
    {
        // Derecho
        ["abogado"] = new[] { "derecho" }, ["abogada"] = new[] { "derecho" },
        ["legal"] = new[] { "derecho" }, ["juridico"] = new[] { "derecho" },
        ["juridica"] = new[] { "derecho" }, ["jurista"] = new[] { "derecho" },
        ["litigante"] = new[] { "derecho" }, ["leyes"] = new[] { "derecho" }, ["ley"] = new[] { "derecho" },
        // Contabilidad
        ["contador"] = new[] { "contabilidad" }, ["contadora"] = new[] { "contabilidad" },
        ["contable"] = new[] { "contabilidad" }, ["tributario"] = new[] { "contabilidad" },
        ["tributaria"] = new[] { "contabilidad" }, ["impuesto"] = new[] { "contabilidad" },
        ["iva"] = new[] { "contabilidad" }, ["renta"] = new[] { "contabilidad" },
        ["sii"] = new[] { "contabilidad" },
        // Corretaje de propiedades
        ["corredor"] = new[] { "corretaje" }, ["corredora"] = new[] { "corretaje" },
        ["inmobiliario"] = new[] { "corretaje" }, ["inmobiliaria"] = new[] { "corretaje" },
        ["propiedad"] = new[] { "corretaje" }, ["arriendo"] = new[] { "corretaje" },
        ["tasador"] = new[] { "corretaje" }, ["tasadora"] = new[] { "corretaje" },
        ["parcela"] = new[] { "corretaje" }, ["terreno"] = new[] { "corretaje" },
        // Arquitectura y construcción
        ["arquitecto"] = new[] { "arquitectura" }, ["arquitecta"] = new[] { "arquitectura" },
        ["constructor"] = new[] { "construccion" }, ["constructora"] = new[] { "construccion" },
        ["remodelacion"] = new[] { "arquitectura" }, ["plano"] = new[] { "arquitectura" },
        ["ingeniero"] = new[] { "arquitectura" }, ["ingeniera"] = new[] { "arquitectura" },
        // Técnicos y oficios
        ["gasfiter"] = new[] { "oficios" }, ["gasfitero"] = new[] { "oficios" },
        ["electricista"] = new[] { "oficios" }, ["pintor"] = new[] { "oficios" },
        ["pintora"] = new[] { "oficios" }, ["maestro"] = new[] { "oficios" },
        ["carpintero"] = new[] { "oficios" }, ["cerrajero"] = new[] { "oficios" },
        ["soldador"] = new[] { "oficios" }, ["tecnico"] = new[] { "oficios" },
        ["tecnica"] = new[] { "oficios" }, ["techador"] = new[] { "oficios" },
        ["ceramista"] = new[] { "oficios" }, ["instalador"] = new[] { "oficios" },
    };

    /// <summary>Quita tildes y diéresis, pasa a minúscula y deja los espacios simples.</summary>
    public static string Normalizar(string? texto)
    {
        if (string.IsNullOrWhiteSpace(texto)) return "";

        var descompuesto = texto.Trim().ToLower(CultureInfo.GetCultureInfo("es-CL")).Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(descompuesto.Length);
        foreach (var c in descompuesto)
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                sb.Append(c);
        return sb.ToString().Normalize(NormalizationForm.FormC);
    }

    /// <summary>
    /// "abogados" → "abogado", "contadores" → "contador". Es un singular tosco y a propósito: lo
    /// único que tiene que lograr es que el plural encuentre lo mismo que el singular, y para
    /// eso basta con quitar la «s» o la «es» del final.
    /// </summary>
    public static string Singular(string palabra)
    {
        if (palabra.Length > 4 && palabra.EndsWith("es", StringComparison.Ordinal)) return palabra[..^2];
        if (palabra.Length > 3 && palabra.EndsWith('s')) return palabra[..^1];
        return palabra;
    }

    /// <summary>Las palabras de la consulta, normalizadas y sin repetir. Solo letras y números.</summary>
    public static IReadOnlyList<string> Palabras(string? consulta)
    {
        var texto = Normalizar(consulta);
        if (texto.Length > LargoMaximoConsulta) texto = texto[..LargoMaximoConsulta];

        var palabras = new List<string>();
        var actual = new StringBuilder();
        void Cerrar()
        {
            if (actual.Length >= LargoMinimoDePalabra)
            {
                var p = Singular(actual.ToString());
                if (!palabras.Contains(p)) palabras.Add(p);
            }
            actual.Clear();
        }

        foreach (var c in texto)
        {
            if (char.IsLetterOrDigit(c)) actual.Append(c);
            else Cerrar();
        }
        Cerrar();

        return palabras.Take(MaximoDePalabras).ToList();
    }

    /// <summary>
    /// Las palabras convertidas en grupos: la palabra misma y, si la gente la usa para nombrar un
    /// rubro, el nombre de ese rubro. Devuelve dos listas del mismo largo (formas y grupo al que
    /// pertenece cada una), que es como se las entrega a la consulta.
    /// </summary>
    public static (string[] Formas, int[] Grupos) Grupos(string? consulta)
    {
        var formas = new List<string>();
        var grupos = new List<int>();
        var n = 0;
        foreach (var palabra in Palabras(consulta))
        {
            n++;
            formas.Add(palabra);
            grupos.Add(n);
            if (Sinonimos.TryGetValue(palabra, out var alias))
                foreach (var a in alias)
                {
                    formas.Add(a);
                    grupos.Add(n);
                }
        }
        return (formas.ToArray(), grupos.ToArray());
    }
}
