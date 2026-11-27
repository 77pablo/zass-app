namespace Zass.Aplicacion.Puertos;

/// <summary>
/// Barrido de archivos huérfanos. Todo lo que la base todavía referencia en el almacenamiento.
/// Solo lectura. Es un puerto aparte (y no un método más de los repositorios de perfiles y de
/// credenciales) porque el barrido necesita las tres fuentes de una sola vez, y porque así los
/// repositorios que ya usan decenas de pruebas no cambian.
/// </summary>
public interface IRepositorioClavesDeArchivos
{
    /// <summary>
    /// Las claves en uso. Si no se puede leer la base LANZA: quien llama no debe borrar nada,
    /// porque sin esta lista todo archivo parecería huérfano.
    /// </summary>
    Task<ClavesDeArchivosEnUso> EnUsoAsync(CancellationToken ct = default);
}

/// <param name="UrlsDeFotoDePerfil">El campo <c>UrlFoto</c> de los perfiles (la URL pública, no la clave).</param>
/// <param name="ClavesDeFotosDeTrabajo">El campo <c>ClaveAlmacen</c> de las fotos de trabajos.</param>
/// <param name="ClavesDeDocumentos">El campo <c>ClaveAlmacenamiento</c> de los documentos de credenciales.</param>
public sealed record ClavesDeArchivosEnUso(
    IReadOnlyList<string> UrlsDeFotoDePerfil,
    IReadOnlyList<string> ClavesDeFotosDeTrabajo,
    IReadOnlyList<string> ClavesDeDocumentos);
