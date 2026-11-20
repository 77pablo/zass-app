using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Processing;
using Zass.Aplicacion.Puertos;
using Zass.Dominio.Comun;

namespace Zass.Infraestructura.Archivos;

/// <summary>
/// <see cref="IProcesadorDeImagenes"/> con ImageSharp: puro .NET, sin bibliotecas nativas que
/// instalar en la imagen de Docker de Render.
///
/// <para>
/// Licencia (para Pablo): ImageSharp usa la "Six Labors Split License". Es gratis mientras la
/// empresa facture menos de 1 millón de dólares al año; por encima, pide una licencia comercial.
/// </para>
/// </summary>
public sealed class ProcesadorDeImagenesImageSharp : IProcesadorDeImagenes
{
    /// <summary>
    /// Tope de pixeles ANTES de decodificar. Una imagen de pocos KB puede declarar 50.000 x 50.000
    /// y pedir gigas al descomprimirse ("bomba de descompresion"). Se mira la cabecera primero.
    /// 40 MP sobra para cualquier celular actual.
    /// </summary>
    public const long PixelesMaximos = 40_000_000;

    private static readonly DecoderOptions Opciones = new() { MaxFrames = 1 };

    public async Task<Resultado<byte[]>> PrepararFotoAsync(
        Stream imagen, int ladoMaximo, CancellationToken ct = default)
    {
        var cargada = await CargarAsync(imagen, ct);
        if (!cargada.Exito) return Resultado.Falla<byte[]>(cargada.Codigo!, cargada.Mensaje!);

        using var img = cargada.Valor!;
        img.Mutate(x =>
        {
            x.AutoOrient();
            if (img.Width > ladoMaximo || img.Height > ladoMaximo)
                x.Resize(new ResizeOptions { Mode = ResizeMode.Max, Size = new Size(ladoMaximo, ladoMaximo) });
        });
        QuitarMetadatos(img);

        using var salida = new MemoryStream();
        await img.SaveAsJpegAsync(salida, new JpegEncoder { Quality = 82 }, ct);
        return Resultado.Ok(salida.ToArray());
    }

    public async Task<Resultado<byte[]>> LimpiarImagenAsync(
        Stream imagen, TipoArchivo tipo, CancellationToken ct = default)
    {
        var cargada = await CargarAsync(imagen, ct);
        if (!cargada.Exito) return Resultado.Falla<byte[]>(cargada.Codigo!, cargada.Mensaje!);

        using var img = cargada.Valor!;
        img.Mutate(x => x.AutoOrient());
        QuitarMetadatos(img);

        using var salida = new MemoryStream();
        if (tipo == TipoArchivo.Png)
            await img.SaveAsPngAsync(salida, new PngEncoder(), ct);
        else
            await img.SaveAsJpegAsync(salida, new JpegEncoder { Quality = 90 }, ct);
        return Resultado.Ok(salida.ToArray());
    }

    private static async Task<Resultado<Image>> CargarAsync(Stream imagen, CancellationToken ct)
    {
        try
        {
            var info = await Image.IdentifyAsync(Opciones, imagen, ct);
            if ((long)info.Width * info.Height > PixelesMaximos)
                return Resultado.Falla<Image>(Fallas.ArchivoDemasiadoGrande,
                    "La imagen tiene demasiados píxeles. Usa una de menos de 40 megapíxeles.");

            imagen.Position = 0;
            return Resultado.Ok(await Image.LoadAsync(Opciones, imagen, ct));
        }
        catch (Exception e) when (e is ImageFormatException or UnknownImageFormatException
                                      or InvalidImageContentException or NotSupportedException)
        {
            return Resultado.Falla<Image>(Fallas.TipoDeArchivoNoAceptado,
                "No pudimos leer esa imagen. Prueba con otra.");
        }
    }

    /// <summary>
    /// Fuera todo lo que no son pixeles: EXIF (trae el GPS de la foto), IPTC, XMP y el perfil de
    /// color. Despues de AutoOrient la orientacion ya esta aplicada a los pixeles, asi que el EXIF
    /// no hace falta ni para eso.
    /// </summary>
    private static void QuitarMetadatos(Image img)
    {
        img.Metadata.ExifProfile = null;
        img.Metadata.IptcProfile = null;
        img.Metadata.XmpProfile = null;
        img.Metadata.IccProfile = null;
        foreach (var cuadro in img.Frames)
        {
            cuadro.Metadata.ExifProfile = null;
            cuadro.Metadata.IptcProfile = null;
            cuadro.Metadata.XmpProfile = null;
            cuadro.Metadata.IccProfile = null;
        }
    }
}
