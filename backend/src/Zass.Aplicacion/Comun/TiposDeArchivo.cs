using System.Text.RegularExpressions;

namespace Zass.Aplicacion.Puertos;

/// <summary>Los unicos tipos de archivo que ZASS! acepta (CU-05 paso 6: PDF, JPG y PNG; WebP solo para la foto).</summary>
public enum TipoArchivo { Pdf, Jpeg, Png, Webp }

/// <summary>
/// El tipo REAL de un archivo, leido de sus primeros bytes. Ni la extension ni el Content-Type que
/// manda el navegador cuentan: los dos los escribe quien sube, y un .exe renombrado a .pdf trae
/// "application/pdf" si el atacante quiere.
/// </summary>
public static partial class TiposDeArchivo
{
    /// <summary>Cuantos bytes hacen falta para reconocer cualquiera de los tipos aceptados.</summary>
    public const int BytesParaDetectar = 12;

    private static readonly byte[] FirmaPdf = "%PDF-"u8.ToArray();
    private static readonly byte[] FirmaJpeg = { 0xFF, 0xD8, 0xFF };
    private static readonly byte[] FirmaPng = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };

    public static TipoArchivo? Detectar(ReadOnlySpan<byte> inicio)
    {
        if (inicio.StartsWith(FirmaPdf)) return TipoArchivo.Pdf;
        if (inicio.StartsWith(FirmaJpeg)) return TipoArchivo.Jpeg;
        if (inicio.StartsWith(FirmaPng)) return TipoArchivo.Png;

        // WebP: "RIFF" + 4 bytes de largo + "WEBP".
        if (inicio.Length >= 12
            && inicio[..4].SequenceEqual("RIFF"u8)
            && inicio.Slice(8, 4).SequenceEqual("WEBP"u8))
            return TipoArchivo.Webp;

        return null;
    }

    public static string Mime(TipoArchivo tipo) => tipo switch
    {
        TipoArchivo.Pdf => "application/pdf",
        TipoArchivo.Jpeg => "image/jpeg",
        TipoArchivo.Png => "image/png",
        TipoArchivo.Webp => "image/webp",
        _ => throw new ArgumentOutOfRangeException(nameof(tipo)),
    };

    public static string Extension(TipoArchivo tipo) => tipo switch
    {
        TipoArchivo.Pdf => "pdf",
        TipoArchivo.Jpeg => "jpg",
        TipoArchivo.Png => "png",
        TipoArchivo.Webp => "webp",
        _ => throw new ArgumentOutOfRangeException(nameof(tipo)),
    };

    /// <summary>Las dos carpetas que existen. La foto es publica; el documento, jamas.</summary>
    public const string CarpetaFotos = "fotos";
    public const string CarpetaDocumentos = "documentos";

    /// <summary>Ruta base pública desde donde se sirven las fotos (perfil y trabajos).</summary>
    public const string RutaPublica = "/api/archivos/fotos/";

    /// <summary>
    /// Una clave nueva: carpeta, 32 caracteres hexadecimales al azar y la extension del tipo real.
    /// Nada de lo que escribio quien sube entra en ella.
    /// </summary>
    public static string NuevaClave(string carpeta, TipoArchivo tipo) =>
        $"{carpeta}/{Guid.NewGuid():N}.{Extension(tipo)}";

    /// <summary>
    /// Si una clave tiene la forma que arma <see cref="NuevaClave"/>. Los almacenes lo comprueban
    /// antes de tocar el disco o el balde: una clave con ".." o una barra de mas no llega nunca a
    /// ser una ruta.
    /// </summary>
    public static bool EsClaveValida(string? clave) => clave is not null && FormaDeClave().IsMatch(clave);

    /// <summary>
    /// Fotos: WebP, en su tamaño grande o en el chico (sufijo <see cref="SufijoChica"/>).
    /// Documentos: PDF, JPG o PNG. Nada más tiene forma de clave.
    /// </summary>
    [GeneratedRegex("^(fotos/[0-9a-f]{32}(-96)?\\.webp|documentos/[0-9a-f]{32}\\.(pdf|jpg|png))\\z",
        RegexOptions.CultureInvariant)]
    private static partial Regex FormaDeClave();

    /// <summary>Lo que distingue la foto chica de la grande en su clave: «fotos/abc…-96.webp».</summary>
    public const string SufijoChica = "-96";

    /// <summary>La clave de la foto chica que acompaña a una grande.</summary>
    public static string ClaveChica(string claveGrande) =>
        claveGrande[..claveGrande.LastIndexOf('.')] + SufijoChica + claveGrande[claveGrande.LastIndexOf('.')..];

    /// <summary>
    /// El nombre original, tal como se le muestra a su dueño y al Verificador: sin carpetas, sin
    /// caracteres de control y con un largo razonable. Solo se muestra; nunca forma una ruta.
    /// </summary>
    public static string NombreParaMostrar(string? nombre, TipoArchivo tipo)
    {
        var limpio = Path.GetFileName((nombre ?? "").Replace('\\', '/'));
        limpio = new string(limpio.Where(c => !char.IsControl(c)).ToArray()).Trim();
        if (limpio.Length == 0) limpio = $"documento.{Extension(tipo)}";
        return limpio.Length <= 120 ? limpio : limpio[..120];
    }
}
