using Zass.Aplicacion.Puertos;
using Zass.Infraestructura.Archivos;

namespace Zass.Integracion.Pruebas;

/// <summary>
/// Auditoría 22-nov-2026: <c>ListarAsync</c> de los almacenes, lo que el barrido de huérfanos lee.
/// Viven aquí y no en las pruebas de aplicación porque cargan Zass.Infraestructura, que Smart App
/// Control bloquea en este PC; en el CI corren.
/// </summary>
public class AlmacenesListarPruebas
{
    private static async Task<List<ArchivoListado>> Todos(IAsyncEnumerable<ArchivoListado> origen)
    {
        var r = new List<ArchivoListado>();
        await foreach (var a in origen) r.Add(a);
        return r;
    }

    [Fact]
    public async Task AlmacenLocal_lista_por_prefijo_con_la_fecha_del_archivo()
    {
        var carpeta = Path.Combine(Path.GetTempPath(), "zass-barrido-" + Guid.NewGuid().ToString("N"));
        try
        {
            var almacen = new AlmacenLocal(carpeta);
            var foto = $"fotos/{Guid.NewGuid():N}.webp";
            var documento = $"documentos/{Guid.NewGuid():N}.pdf";
            await almacen.GuardarAsync(foto, new MemoryStream(new byte[] { 1 }), "image/webp");
            await almacen.GuardarAsync(documento, new MemoryStream(new byte[] { 1 }), "application/pdf");

            var fecha = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);
            File.SetLastWriteTimeUtc(Path.Combine(carpeta, foto), fecha);

            var fotos = await Todos(almacen.ListarAsync("fotos/"));
            var documentos = await Todos(almacen.ListarAsync("documentos/"));

            var f = Assert.Single(fotos);
            Assert.Equal(foto, f.Clave);
            Assert.Equal(fecha, f.UltimaModificacionUtc);
            Assert.Equal(documento, Assert.Single(documentos).Clave);
        }
        finally
        {
            if (Directory.Exists(carpeta)) Directory.Delete(carpeta, recursive: true);
        }
    }

    [Fact]
    public async Task AlmacenDeshabilitado_no_lista_nada()
    {
        Assert.Empty(await Todos(new AlmacenDeshabilitado().ListarAsync("fotos/")));
    }
}
