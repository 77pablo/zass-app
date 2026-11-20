using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Metadata.Profiles.Exif;
using SixLabors.ImageSharp.PixelFormats;

namespace Zass.Integracion.Pruebas;

/// <summary>
/// JQ-9 por HTTP y contra la base real: la foto del perfil (API-72, 72-quitar, 73) y los documentos
/// de respaldo de una credencial (API-26, 26-quitar, 26-ver, 74). Los archivos van a la carpeta de
/// <see cref="Aplicacion.CarpetaDeArchivos"/>.
///
/// <para>
/// Lo que cada prueba mira es lo que se veria si fallara de verdad: que la foto servida NO trae
/// los metadatos, que un documento ajeno responde igual que uno inexistente, que el archivo se va
/// del disco cuando se borra la credencial. No basta con que la subida responda 201.
/// </para>
/// </summary>
public partial class ApiPruebas
{
    private static readonly byte[] UnPdf =
        Encoding.ASCII.GetBytes("%PDF-1.4\n1 0 obj<<>>endobj\ntrailer<<>>\n%%EOF\n");

    private static byte[] UnaImagen(int ancho, int alto, bool png = false, string? marcaEnExif = null)
    {
        using var img = new Image<Rgba32>(ancho, alto, new Rgba32(100, 149, 237, 255));
        if (marcaEnExif is not null)
        {
            img.Metadata.ExifProfile = new ExifProfile();
            img.Metadata.ExifProfile.SetValue(ExifTag.Software, marcaEnExif);
            img.Metadata.ExifProfile.SetValue(ExifTag.ImageDescription, marcaEnExif);
        }
        using var ms = new MemoryStream();
        if (png) img.SaveAsPng(ms); else img.SaveAsJpeg(ms);
        return ms.ToArray();
    }

    private async Task<HttpResponseMessage> SubirAsync(
        string url, byte[] bytes, string nombre, string tipo, bool conCsrf = true)
    {
        var formulario = new MultipartFormDataContent();
        var parte = new ByteArrayContent(bytes);
        parte.Headers.ContentType = new MediaTypeHeaderValue(tipo);
        formulario.Add(parte, "archivo", nombre);

        var peticion = new HttpRequestMessage(HttpMethod.Post, url) { Content = formulario };
        if (conCsrf) peticion.Headers.Add("X-CSRF-Token", await TokenCsrfAsync());
        return await _cliente.SendAsync(peticion);
    }

    private async Task<HttpResponseMessage> BorrarConCsrfAsync(string url)
    {
        var peticion = new HttpRequestMessage(HttpMethod.Delete, url);
        peticion.Headers.Add("X-CSRF-Token", await TokenCsrfAsync());
        return await _cliente.SendAsync(peticion);
    }

    private static async Task<string> UrlFotoDeAsync(HttpResponseMessage r) =>
        (await r.Content.ReadFromJsonAsync<JsonDocument>())!.RootElement.GetProperty("urlFoto").GetString()!;

    private static string RutaEnDisco(string clave) => Path.Combine(Aplicacion.CarpetaDeArchivos, clave);

    // ================================================================ la foto

    [Fact]
    public async Task La_foto_se_reduce_en_el_servidor_y_se_sirve_con_cache_inmutable()
    {
        await UnPrestadorConSesionAsync();

        var r = await SubirAsync("/api/mi/foto", UnaImagen(2400, 1200), "grande.jpg", "image/jpeg");

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var url = await UrlFotoDeAsync(r);
        Assert.StartsWith("/api/archivos/fotos/", url);

        var servida = await _cliente.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, servida.StatusCode);
        Assert.Equal("image/jpeg", servida.Content.Headers.ContentType!.MediaType);
        Assert.Contains("immutable", servida.Headers.CacheControl!.ToString());

        var info = Image.Identify(await servida.Content.ReadAsByteArrayAsync());
        Assert.Equal(800, info.Width);    // el lado mayor quedó en 800
        Assert.Equal(400, info.Height);   // y la proporción se respeta
    }

    [Fact]
    public async Task La_foto_servida_no_trae_los_metadatos_que_venian_en_el_archivo()
    {
        await UnPrestadorConSesionAsync();
        const string marca = "GPS-ZASS-NO-DEBE-SALIR";

        var r = await SubirAsync("/api/mi/foto", UnaImagen(300, 300, marcaEnExif: marca), "con-exif.jpg", "image/jpeg");
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);

        var bytes = await (await _cliente.GetAsync(await UrlFotoDeAsync(r))).Content.ReadAsByteArrayAsync();

        Assert.DoesNotContain(marca, Encoding.ASCII.GetString(bytes));
        Assert.Null(Image.Identify(bytes).Metadata.ExifProfile);
    }

    [Fact]
    public async Task Un_archivo_que_no_es_imagen_con_nombre_y_tipo_de_imagen_responde_415()
    {
        await UnPrestadorConSesionAsync();
        var disfrazado = Encoding.ASCII.GetBytes("MZ esto es un ejecutable que dice ser una foto");

        var r = await SubirAsync("/api/mi/foto", disfrazado, "foto.jpg", "image/jpeg");

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, r.StatusCode);
        Assert.Contains("JPG, PNG o WebP", await r.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Una_foto_de_mas_de_10_MB_responde_413_con_el_tamano_real()
    {
        await UnPrestadorConSesionAsync();
        var enorme = new byte[10 * 1024 * 1024 + 1];
        enorme[0] = 0xFF; enorme[1] = 0xD8; enorme[2] = 0xFF;

        var r = await SubirAsync("/api/mi/foto", enorme, "enorme.jpg", "image/jpeg");

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, r.StatusCode);
        Assert.Contains("10 MB", await r.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Reemplazar_la_foto_borra_la_anterior_y_quitarla_borra_la_actual()
    {
        await UnPrestadorConSesionAsync();
        var primera = await UrlFotoDeAsync(await SubirAsync("/api/mi/foto", UnaImagen(200, 200), "a.jpg", "image/jpeg"));
        var segunda = await UrlFotoDeAsync(await SubirAsync("/api/mi/foto", UnaImagen(200, 200, png: true), "b.png", "image/png"));

        Assert.NotEqual(primera, segunda);
        Assert.Equal(HttpStatusCode.NotFound, (await _cliente.GetAsync(primera)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _cliente.GetAsync(segunda)).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await BorrarConCsrfAsync("/api/mi/foto")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _cliente.GetAsync(segunda)).StatusCode);
    }

    [Fact]
    public async Task Subir_la_foto_sin_sesion_responde_401_y_sin_csrf_no_guarda_nada()
    {
        var sinSesion = await SubirAsync("/api/mi/foto", UnaImagen(50, 50), "a.jpg", "image/jpeg", conCsrf: false);
        Assert.Equal(HttpStatusCode.Unauthorized, sinSesion.StatusCode);

        await UnPrestadorConSesionAsync();
        var sinCsrf = await SubirAsync("/api/mi/foto", UnaImagen(50, 50), "a.jpg", "image/jpeg", conCsrf: false);
        Assert.True(sinCsrf.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.BadRequest,
            $"Sin el token CSRF respondió {(int)sinCsrf.StatusCode}.");
    }

    [Theory]
    [InlineData("/api/archivos/fotos/..%2F..%2Fappsettings.json")]
    [InlineData("/api/archivos/fotos/no-es-una-clave.jpg")]
    [InlineData("/api/archivos/fotos/0123456789abcdef0123456789abcdef.pdf")]
    public async Task Servir_una_foto_con_un_nombre_que_no_armamos_nosotros_responde_404(string url)
    {
        Assert.Equal(HttpStatusCode.NotFound, (await _cliente.GetAsync(url)).StatusCode);
    }

    // ================================================================ los documentos

    private async Task<string> CredencialPropiaAsync()
    {
        var creada = await CrearCredencialAsync(UnaCredencial("Abogado con respaldo"));
        Assert.Equal(HttpStatusCode.Created, creada.StatusCode);
        return await IdDeAsync(creada);
    }

    [Fact]
    public async Task Un_pdf_adjunto_aparece_en_mis_credenciales_y_se_abre_aislado_y_sin_cache()
    {
        await UnPrestadorConSesionAsync();
        var id = await CredencialPropiaAsync();

        var subido = await SubirAsync($"/api/mi/credenciales/{id}/documentos", UnPdf, "titulo.pdf", "application/pdf");
        Assert.Equal(HttpStatusCode.Created, subido.StatusCode);
        var documento = (await subido.Content.ReadFromJsonAsync<JsonDocument>())!.RootElement;
        var documentoId = documento.GetProperty("id").GetString();
        Assert.False(documento.TryGetProperty("claveAlmacenamiento", out _));

        var mias = (await (await _cliente.GetAsync("/api/mi/credenciales")).Content.ReadFromJsonAsync<JsonDocument>())!.RootElement;
        var credencial = mias.EnumerateArray().Single(c => c.GetProperty("id").GetString() == id);
        Assert.True(credencial.GetProperty("tieneRespaldo").GetBoolean());
        Assert.Equal("titulo.pdf", credencial.GetProperty("respaldo")[0].GetProperty("nombre").GetString());

        var abierto = await _cliente.GetAsync($"/api/mi/credenciales/{id}/documentos/{documentoId}");
        Assert.Equal(HttpStatusCode.OK, abierto.StatusCode);
        Assert.Equal("application/pdf", abierto.Content.Headers.ContentType!.MediaType);
        Assert.Equal(UnPdf, await abierto.Content.ReadAsByteArrayAsync());
        Assert.StartsWith("sandbox", string.Join(" ", abierto.Headers.GetValues("Content-Security-Policy")));
        Assert.True(abierto.Headers.CacheControl!.NoStore);
    }

    [Fact]
    public async Task El_documento_de_otro_prestador_responde_igual_que_uno_que_no_existe()
    {
        await UnPrestadorConSesionAsync();
        var id = await CredencialPropiaAsync();
        var subido = await SubirAsync($"/api/mi/credenciales/{id}/documentos", UnPdf, "titulo.pdf", "application/pdf");
        var documentoId = (await subido.Content.ReadFromJsonAsync<JsonDocument>())!.RootElement.GetProperty("id").GetString();

        await UnPrestadorConSesionAsync();   // otra persona
        var ajeno = await _cliente.GetAsync($"/api/mi/credenciales/{id}/documentos/{documentoId}");
        var inexistente = await _cliente.GetAsync($"/api/mi/credenciales/{id}/documentos/{Guid.NewGuid()}");
        var subirAjeno = await SubirAsync($"/api/mi/credenciales/{id}/documentos", UnPdf, "x.pdf", "application/pdf");
        var quitarAjeno = await BorrarConCsrfAsync($"/api/mi/credenciales/{id}/documentos/{documentoId}");

        Assert.Equal(HttpStatusCode.NotFound, ajeno.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, inexistente.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, subirAjeno.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, quitarAjeno.StatusCode);
        Assert.Equal(
            (await inexistente.Content.ReadFromJsonAsync<JsonDocument>())!.RootElement.GetProperty("detail").GetString(),
            (await ajeno.Content.ReadFromJsonAsync<JsonDocument>())!.RootElement.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task El_sexto_documento_responde_409_y_un_tipo_no_aceptado_415()
    {
        await UnPrestadorConSesionAsync();
        var id = await CredencialPropiaAsync();
        for (var i = 0; i < 5; i++)
            Assert.Equal(HttpStatusCode.Created,
                (await SubirAsync($"/api/mi/credenciales/{id}/documentos", UnPdf, $"d{i}.pdf", "application/pdf")).StatusCode);

        var sexto = await SubirAsync($"/api/mi/credenciales/{id}/documentos", UnPdf, "d5.pdf", "application/pdf");
        Assert.Equal(HttpStatusCode.Conflict, sexto.StatusCode);
        Assert.Contains("Máximo 5", await sexto.Content.ReadAsStringAsync());

        var otra = await CredencialPropiaAsync();
        var webp = new byte[] { (byte)'R', (byte)'I', (byte)'F', (byte)'F', 0, 0, 0, 0, (byte)'W', (byte)'E', (byte)'B', (byte)'P', 1, 2 };
        var noAceptado = await SubirAsync($"/api/mi/credenciales/{otra}/documentos", webp, "titulo.pdf", "application/pdf");
        Assert.Equal(HttpStatusCode.UnsupportedMediaType, noAceptado.StatusCode);
        Assert.Contains("PDF, JPG o PNG", await noAceptado.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Una_imagen_de_respaldo_se_guarda_vuelta_a_codificar_y_sin_metadatos()
    {
        await UnPrestadorConSesionAsync();
        var id = await CredencialPropiaAsync();
        const string marca = "DATO-ESCONDIDO-EN-EL-TITULO";

        var subido = await SubirAsync($"/api/mi/credenciales/{id}/documentos",
            UnaImagen(400, 300, png: true, marcaEnExif: marca), "titulo.png", "image/png");
        Assert.Equal(HttpStatusCode.Created, subido.StatusCode);
        var documentoId = Guid.Parse((await subido.Content.ReadFromJsonAsync<JsonDocument>())!.RootElement.GetProperty("id").GetString()!);

        using var ctx = ContextoDePrueba.Crear();
        var clave = (await ctx.DocumentosCredencial.SingleAsync(d => d.Id == documentoId)).ClaveAlmacenamiento;
        var guardado = await File.ReadAllBytesAsync(RutaEnDisco(clave));

        Assert.EndsWith(".png", clave);
        Assert.DoesNotContain(marca, Encoding.ASCII.GetString(guardado));
        Assert.Equal(400, Image.Identify(guardado).Width);   // un documento no se achica
    }

    [Fact]
    public async Task Quitar_un_documento_y_eliminar_la_credencial_se_llevan_los_archivos_del_almacen()
    {
        await UnPrestadorConSesionAsync();
        var id = await CredencialPropiaAsync();
        var a = (await (await SubirAsync($"/api/mi/credenciales/{id}/documentos", UnPdf, "a.pdf", "application/pdf"))
            .Content.ReadFromJsonAsync<JsonDocument>())!.RootElement.GetProperty("id").GetString()!;
        await SubirAsync($"/api/mi/credenciales/{id}/documentos", UnPdf, "b.pdf", "application/pdf");

        string[] claves;
        using (var ctx = ContextoDePrueba.Crear())
            claves = await ctx.DocumentosCredencial.Where(d => d.CredencialId == Guid.Parse(id))
                .OrderBy(d => d.SubidoEn).Select(d => d.ClaveAlmacenamiento).ToArrayAsync();
        Assert.Equal(2, claves.Length);
        Assert.All(claves, c => Assert.True(File.Exists(RutaEnDisco(c))));

        Assert.Equal(HttpStatusCode.NoContent, (await BorrarConCsrfAsync($"/api/mi/credenciales/{id}/documentos/{a}")).StatusCode);
        Assert.False(File.Exists(RutaEnDisco(claves[0])));
        Assert.True(File.Exists(RutaEnDisco(claves[1])));

        Assert.True((await EliminarCredencialAsync(id)).IsSuccessStatusCode);
        Assert.False(File.Exists(RutaEnDisco(claves[1])));
    }

    [Fact]
    public async Task El_verificador_solo_abre_un_documento_de_la_credencial_que_tiene_tomada_y_queda_auditado()
    {
        var (correoPrestador, _) = await UnaCuentaAsync("prestador");
        await IniciarSesionAsync(correoPrestador);
        var id = await CredencialPropiaAsync();
        var documentoId = (await (await SubirAsync($"/api/mi/credenciales/{id}/documentos", UnPdf, "titulo.pdf", "application/pdf"))
            .Content.ReadFromJsonAsync<JsonDocument>())!.RootElement.GetProperty("id").GetString()!;
        Assert.Equal(HttpStatusCode.NoContent, (await PostConCsrfAsync($"/api/mi/credenciales/{id}/enviar")).StatusCode);
        var url = $"/api/interno/credenciales/{id}/documentos/{documentoId}";

        var (correoOtro, _) = await UnaCuentaAsync("buscador", verificador: true);
        var (correoVerificador, verificadorId) = await UnaCuentaAsync("buscador", verificador: true);

        // Ser verificador no basta: sin tomarla, 404.
        await IniciarSesionAsync(correoVerificador);
        Assert.Equal(HttpStatusCode.NotFound, (await _cliente.GetAsync(url)).StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await PostConCsrfAsync($"/api/interno/credenciales/{id}/tomar")).StatusCode);
        var abierto = await _cliente.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, abierto.StatusCode);
        Assert.Equal(UnPdf, await abierto.Content.ReadAsByteArrayAsync());

        // Otro verificador, mientras está tomada por el primero: 404.
        await IniciarSesionAsync(correoOtro);
        Assert.Equal(HttpStatusCode.NotFound, (await _cliente.GetAsync(url)).StatusCode);

        // Y el prestador dueño tampoco entra por la ruta interna.
        await IniciarSesionAsync(correoPrestador);
        Assert.Equal(HttpStatusCode.Forbidden, (await _cliente.GetAsync(url)).StatusCode);

        using var ctx = ContextoDePrueba.Crear();
        Assert.Equal(1, await ctx.Auditoria.CountAsync(
            a => a.Accion == "ver_documento" && a.ActorId == verificadorId && a.EntidadId == Guid.Parse(id)));
    }

    [Fact]
    public async Task La_ficha_publica_no_menciona_ningun_documento_de_respaldo()
    {
        var (correo, _) = await UnaCuentaAsync("prestador");
        await IniciarSesionAsync(correo);
        var id = await CredencialPropiaAsync();
        await SubirAsync($"/api/mi/credenciales/{id}/documentos", UnPdf, "titulo-secreto.pdf", "application/pdf");
        await PostConCsrfAsync($"/api/mi/credenciales/{id}/enviar");

        var slug = await SlugDeAsync(correo);
        var ficha = await (await _cliente.GetAsync($"/api/prestadores/{slug}")).Content.ReadAsStringAsync();

        // Que sea la ficha de verdad y con la credencial: si no, no mencionar nada no probaría nada.
        Assert.Contains("Abogado con respaldo", ficha);
        Assert.DoesNotContain("titulo-secreto", ficha);
        Assert.DoesNotContain("documentos/", ficha);
    }
}
