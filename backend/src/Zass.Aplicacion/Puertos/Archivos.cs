using Zass.Dominio.Comun;

namespace Zass.Aplicacion.Puertos;

/// <summary>
/// MOD-08 (JQ-9). Donde viven los archivos que sube la gente: la foto del perfil y los documentos
/// de respaldo de una credencial. Igual que con la pasarela, la aplicacion dice QUE necesita y la
/// infraestructura decide DONDE: una carpeta local en desarrollo y pruebas, R2 de Cloudflare
/// (compatible con S3) en produccion.
///
/// <para>
/// No hay "URL publica" en este puerto a proposito. Todo archivo sale por la API: la foto por un
/// endpoint publico con cache, y el documento por uno que mira la sesion antes de abrirlo. Asi el
/// almacenamiento puede ser un balde PRIVADO entero, y un documento no tiene ningun enlace que se
/// pueda copiar y compartir: quien no tiene permiso no llega ni a saber que existe.
/// </para>
///
/// <para>
/// La clave la arma siempre ZASS! (ver <see cref="TiposDeArchivo.NuevaClave"/>), nunca con el
/// nombre que traia el archivo: un nombre elegido por quien sube es una ruta elegida por quien sube.
/// </para>
/// </summary>
public interface IAlmacenDeArchivos
{
    /// <summary>Guarda el contenido bajo esa clave. Falla con AlmacenamientoNoDisponible si no pudo.</summary>
    Task<Resultado> GuardarAsync(
        string clave, Stream contenido, string tipoMime, CancellationToken ct = default);

    /// <summary>Abre el archivo para leerlo, o nulo si no existe. Quien lo abre lo cierra.</summary>
    Task<ArchivoAbierto?> AbrirAsync(string clave, CancellationToken ct = default);

    /// <summary>
    /// Borra el archivo. Que no exista no es un error: borrar dos veces deja lo mismo. Devuelve
    /// falla solo si el almacenamiento no contesto, y entonces el archivo puede quedar huerfano.
    /// </summary>
    Task<Resultado> BorrarAsync(string clave, CancellationToken ct = default);
}

/// <summary>Los dos tamaños de una foto de perfil, ya en WebP.</summary>
public sealed record FotoPreparada(byte[] Grande, byte[] Chica);

/// <summary>Un archivo abierto para leer. El stream es de quien lo pidio.</summary>
public sealed record ArchivoAbierto(Stream Contenido, string TipoMime, long Largo) : IAsyncDisposable
{
    public ValueTask DisposeAsync() => Contenido.DisposeAsync();
}

/// <summary>
/// Redimensiona y vuelve a codificar imagenes. Es un puerto y no codigo de la aplicacion porque
/// necesita una biblioteca de imagenes, y Zass.Aplicacion no carga con eso.
///
/// <para>
/// Volver a codificar no es cosmetico: descarta los metadatos (el EXIF de una foto de celular trae
/// las coordenadas GPS de donde se saco) y cualquier cosa escondida dentro del archivo que no sea
/// la imagen. Lo que se guarda son pixeles que dibujamos nosotros.
/// </para>
/// </summary>
public interface IProcesadorDeImagenes
{
    /// <summary>
    /// La foto de perfil, en los DOS tamaños que se sirven: endereza segun la orientacion, la
    /// recorta en un cuadrado centrado (en ZASS! la foto siempre se ve en un circulo) y la devuelve
    /// en WebP, sin metadatos, a <paramref name="ladoGrande"/> y a <paramref name="ladoChico"/> pixeles.
    /// Falla con TipoDeArchivoNoAceptado si no se puede leer como imagen.
    /// </summary>
    Task<Resultado<FotoPreparada>> PrepararFotoAsync(
        Stream imagen, int ladoGrande, int ladoChico, CancellationToken ct = default);

    /// <summary>
    /// Una imagen que respalda una credencial: se conserva el tamaño (es un documento y se tiene
    /// que poder leer), pero se vuelve a codificar en su mismo formato y sin metadatos.
    /// </summary>
    Task<Resultado<byte[]>> LimpiarImagenAsync(
        Stream imagen, TipoArchivo tipo, CancellationToken ct = default);
}
