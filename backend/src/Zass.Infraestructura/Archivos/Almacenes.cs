using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Logging;
using Zass.Aplicacion.Puertos;
using Zass.Dominio.Comun;

namespace Zass.Infraestructura.Archivos;

/// <summary>
/// Configuracion de MOD-08 (seccion <c>Zass:Archivos</c>). Las credenciales de R2 van en Render
/// y en user-secrets, NUNCA en el repositorio (es publico).
/// </summary>
public sealed class OpcionesArchivos
{
    public const string Seccion = "Zass:Archivos";

    /// <summary>"local", "r2" o vacio. Vacio en Produccion = subir archivos deshabilitado.</summary>
    public string? Proveedor { get; set; }

    /// <summary>Carpeta para el proveedor local. Si falta, una dentro de la carpeta temporal.</summary>
    public string? CarpetaLocal { get; set; }

    public string? R2CuentaId { get; set; }
    public string? R2ClaveAcceso { get; set; }
    public string? R2Secreto { get; set; }
    public string? R2Balde { get; set; }
}

/// <summary>
/// Para desarrollo y pruebas: una carpeta del disco. NO sirve en Render (su disco se borra en cada
/// despliegue), y por eso en Produccion no se elige nunca por omision.
/// </summary>
public sealed class AlmacenLocal : IAlmacenDeArchivos
{
    private readonly string _raiz;

    public AlmacenLocal(string carpeta)
    {
        _raiz = Path.GetFullPath(carpeta);
        Directory.CreateDirectory(Path.Combine(_raiz, TiposDeArchivo.CarpetaFotos));
        Directory.CreateDirectory(Path.Combine(_raiz, TiposDeArchivo.CarpetaDocumentos));
    }

    /// <summary>
    /// La ruta de una clave. Dos candados: la forma de la clave (sin "..", sin barras de mas) y,
    /// por si acaso, que la ruta final siga dentro de la raiz.
    /// </summary>
    private string? Ruta(string clave)
    {
        if (!TiposDeArchivo.EsClaveValida(clave)) return null;
        var ruta = Path.GetFullPath(Path.Combine(_raiz, clave));
        return ruta.StartsWith(_raiz + Path.DirectorySeparatorChar, StringComparison.Ordinal) ? ruta : null;
    }

    public async Task<Resultado> GuardarAsync(
        string clave, Stream contenido, string tipoMime, CancellationToken ct = default)
    {
        var ruta = Ruta(clave);
        if (ruta is null) return Resultado.Falla(Fallas.Validacion, "Clave de archivo inválida.");

        // CreateNew: una clave al azar repetida seria un error nuestro, y pisar un archivo ajeno
        // es peor que fallar.
        await using var archivo = new FileStream(ruta, FileMode.CreateNew, FileAccess.Write);
        await contenido.CopyToAsync(archivo, ct);
        return Resultado.Ok();
    }

    public Task<ArchivoAbierto?> AbrirAsync(string clave, CancellationToken ct = default)
    {
        var ruta = Ruta(clave);
        if (ruta is null || !File.Exists(ruta)) return Task.FromResult<ArchivoAbierto?>(null);

        var flujo = new FileStream(ruta, FileMode.Open, FileAccess.Read, FileShare.Read);
        return Task.FromResult<ArchivoAbierto?>(new ArchivoAbierto(flujo, MimeDe(clave), flujo.Length));
    }

    public Task<Resultado> BorrarAsync(string clave, CancellationToken ct = default)
    {
        var ruta = Ruta(clave);
        if (ruta is not null && File.Exists(ruta)) File.Delete(ruta);
        return Task.FromResult(Resultado.Ok());
    }

    internal static string MimeDe(string clave) => Path.GetExtension(clave) switch
    {
        ".pdf" => TiposDeArchivo.Mime(TipoArchivo.Pdf),
        ".png" => TiposDeArchivo.Mime(TipoArchivo.Png),
        ".webp" => TiposDeArchivo.Mime(TipoArchivo.Webp),
        _ => TiposDeArchivo.Mime(TipoArchivo.Jpeg),
    };
}

/// <summary>
/// R2 de Cloudflare por su API compatible con S3. Un solo balde PRIVADO: nada se sirve directo
/// desde R2, todo pasa por la API (ver <see cref="IAlmacenDeArchivos"/>).
/// </summary>
public sealed class AlmacenR2 : IAlmacenDeArchivos, IDisposable
{
    private readonly AmazonS3Client _cliente;
    private readonly string _balde;
    private readonly ILogger<AlmacenR2> _registro;

    public AlmacenR2(OpcionesArchivos opciones, ILogger<AlmacenR2> registro)
    {
        _registro = registro;
        _balde = opciones.R2Balde!;
        var config = new AmazonS3Config
        {
            ServiceURL = $"https://{opciones.R2CuentaId}.r2.cloudflarestorage.com",
            ForcePathStyle = true,
            AuthenticationRegion = "auto",
            // R2 no acepta las sumas de verificacion que el SDK agrega por omision desde 2025.
            RequestChecksumCalculation = RequestChecksumCalculation.WHEN_REQUIRED,
            ResponseChecksumValidation = ResponseChecksumValidation.WHEN_REQUIRED,
        };
        _cliente = new AmazonS3Client(new BasicAWSCredentials(opciones.R2ClaveAcceso, opciones.R2Secreto), config);
    }

    public async Task<Resultado> GuardarAsync(
        string clave, Stream contenido, string tipoMime, CancellationToken ct = default)
    {
        if (!TiposDeArchivo.EsClaveValida(clave)) return Resultado.Falla(Fallas.Validacion, "Clave de archivo inválida.");
        try
        {
            await _cliente.PutObjectAsync(new PutObjectRequest
            {
                BucketName = _balde,
                Key = clave,
                InputStream = contenido,
                ContentType = tipoMime,
                AutoCloseStream = false,
            }, ct);
            return Resultado.Ok();
        }
        catch (Exception e) when (e is AmazonServiceException or HttpRequestException or TaskCanceledException)
        {
            // Sin la clave en el registro: dice de que carpeta y nada mas.
            _registro.LogError(e, "R2 no guardó un archivo de {Carpeta}.", clave.Split('/')[0]);
            return Resultado.Falla(Fallas.AlmacenamientoNoDisponible, "No pudimos guardar el archivo. Intenta de nuevo.");
        }
    }

    public async Task<ArchivoAbierto?> AbrirAsync(string clave, CancellationToken ct = default)
    {
        if (!TiposDeArchivo.EsClaveValida(clave)) return null;
        try
        {
            var r = await _cliente.GetObjectAsync(_balde, clave, ct);
            return new ArchivoAbierto(r.ResponseStream, AlmacenLocal.MimeDe(clave), r.ContentLength);
        }
        catch (AmazonS3Exception e) when (e.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task<Resultado> BorrarAsync(string clave, CancellationToken ct = default)
    {
        if (!TiposDeArchivo.EsClaveValida(clave)) return Resultado.Ok();
        try
        {
            await _cliente.DeleteObjectAsync(_balde, clave, ct);
            return Resultado.Ok();
        }
        catch (Exception e) when (e is AmazonServiceException or HttpRequestException or TaskCanceledException)
        {
            _registro.LogWarning(e, "R2 no borró un archivo de {Carpeta}: puede quedar huérfano.", clave.Split('/')[0]);
            return Resultado.Falla(Fallas.AlmacenamientoNoDisponible, "No se pudo borrar el archivo.");
        }
    }

    public void Dispose() => _cliente.Dispose();
}

/// <summary>
/// En Produccion, mientras R2 no este configurado: nadie puede subir, y el resto de la aplicacion
/// arranca igual. Elegir la carpeta local por omision en Render seria peor que no tener nada: los
/// archivos se perderian en el siguiente despliegue sin que nadie se entere.
/// </summary>
public sealed class AlmacenDeshabilitado : IAlmacenDeArchivos
{
    public Task<Resultado> GuardarAsync(string clave, Stream contenido, string tipoMime, CancellationToken ct = default) =>
        Task.FromResult(Resultado.Falla(Fallas.AlmacenamientoNoDisponible,
            "Subir archivos todavía no está disponible."));

    public Task<ArchivoAbierto?> AbrirAsync(string clave, CancellationToken ct = default) =>
        Task.FromResult<ArchivoAbierto?>(null);

    public Task<Resultado> BorrarAsync(string clave, CancellationToken ct = default) =>
        Task.FromResult(Resultado.Ok());
}
