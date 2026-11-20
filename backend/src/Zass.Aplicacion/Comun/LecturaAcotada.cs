namespace Zass.Aplicacion.Comun;

/// <summary>
/// Lee un archivo subido a memoria SIN pasar del tope, sin creerle al largo que declaro quien
/// lo mando. Un Content-Length de 1 KB con 2 GB detras es justamente el ataque: si se le cree,
/// se termina leyendo todo.
/// </summary>
public static class LecturaAcotada
{
    /// <summary>Los bytes leidos, o nulo si el contenido pasa de <paramref name="tope"/>.</summary>
    public static async Task<byte[]?> LeerAsync(Stream origen, long tope, CancellationToken ct = default)
    {
        using var destino = new MemoryStream();
        var bufer = new byte[81920];
        long total = 0;
        int leidos;
        while ((leidos = await origen.ReadAsync(bufer, ct)) > 0)
        {
            total += leidos;
            if (total > tope) return null;
            destino.Write(bufer, 0, leidos);
        }
        return destino.ToArray();
    }

    /// <summary>"3,4 MB", para decirle a la persona cuanto pesaba lo que mando (E-413).</summary>
    public static string Megas(long bytes) =>
        (bytes / 1024d / 1024d).ToString("0.#", new System.Globalization.CultureInfo("es-CL")) + " MB";
}
