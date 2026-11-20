using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Zass.Aplicacion.Credenciales;
using Zass.Aplicacion.Identidad;
using Zass.Aplicacion.Puertos;
using Zass.Api.Configuracion;

namespace Zass.Api.Endpoints;

/// <summary>
/// MOD-08 (JQ-9). La foto del perfil: subirla, quitarla y servirla.
///
/// <para>
/// Ningun archivo se sirve directo desde el almacenamiento: todos pasan por aqui (o por los de
/// documentos). Asi el balde de R2 es privado entero, la CSP puede seguir diciendo
/// <c>img-src 'self'</c>, y no existe un enlace a R2 que alguien pueda copiar.
/// </para>
/// </summary>
public static class EndpointsArchivos
{
    /// <summary>
    /// Lo que se acepta en el cuerpo de una subida: el archivo de 10 MB y lo que agrega el formulario.
    /// Pasado esto, Kestrel corta con 413 antes de que llegue al caso de uso.
    /// </summary>
    public const long LimiteDeLaPeticion = 11 * 1024 * 1024;

    public static IEndpointRouteBuilder MapearArchivos(this IEndpointRouteBuilder app)
    {
        var mia = app.MapGroup("/api/mi");
        mia.RequireAuthorization();
        mia.AddEndpointFilter<FiltroCsrf>();

        // API-72 - Subir o reemplazar la foto del perfil (multipart, campo "archivo").
        mia.MapPost("/foto", async (
                IFormFile? archivo, ServicioFotoDePerfil fotos, ClaimsPrincipal quien) =>
            {
                var usuarioId = quien.UsuarioId();
                if (usuarioId is null) return Results.Unauthorized();
                if (archivo is null) return SinArchivo();

                await using var contenido = archivo.OpenReadStream();
                var r = await fotos.SubirAsync(usuarioId.Value, contenido, archivo.Length);
                return r.Exito ? Results.Ok(new { urlFoto = r.Valor }) : Respuestas.Problema(r);
            })
            .WithMetadata(new RequestSizeLimitAttribute(LimiteDeLaPeticion))
            .RequireRateLimiting(Tasa.Sensible)
            .WithName("API-72");

        // API-72-quitar - Quitar la foto.
        mia.MapDelete("/foto", async (ServicioFotoDePerfil fotos, ClaimsPrincipal quien) =>
            {
                var usuarioId = quien.UsuarioId();
                if (usuarioId is null) return Results.Unauthorized();
                return Respuestas.DeResultado(await fotos.QuitarAsync(usuarioId.Value));
            })
            .RequireRateLimiting(Tasa.General)
            .WithName("API-72-quitar");

        // API-73 - Servir una foto. Publica: es la misma que se ve en la ficha. El nombre es
        // aleatorio y no cambia nunca (una foto nueva es un nombre nuevo), asi que se puede
        // guardar en cache para siempre.
        app.MapGet("/api/archivos/fotos/{nombre}", async (
                string nombre, IAlmacenDeArchivos almacen, HttpContext http) =>
            {
                var clave = TiposDeArchivo.CarpetaFotos + "/" + nombre;
                if (!TiposDeArchivo.EsClaveValida(clave)) return Results.NotFound();

                var archivo = await almacen.AbrirAsync(clave);
                if (archivo is null) return Results.NotFound();

                http.Response.Headers.CacheControl = "public, max-age=31536000, immutable";
                http.Response.RegisterForDisposeAsync(archivo);
                return Results.Stream(archivo.Contenido, archivo.TipoMime);
            })
            .RequireRateLimiting(Tasa.Publico)
            .WithName("API-73");

        return app;
    }

    internal static IResult SinArchivo() => Results.Problem(
        title: "Datos inválidos",
        detail: "No llegó ningún archivo. Envíalo en el campo «archivo».",
        statusCode: StatusCodes.Status400BadRequest);

    /// <summary>
    /// Un documento de credencial hacia el navegador. Es dato personal sensible, asi que:
    /// sin cache en ningun lado, y con <c>CSP: sandbox</c> para que un PDF o una imagen abiertos
    /// en la pestaña no puedan ejecutar nada ni leer la sesion de ZASS!.
    /// </summary>
    internal static IResult ServirDocumento(DocumentoParaVer doc, HttpContext http)
    {
        var cabeceras = http.Response.Headers;
        cabeceras.CacheControl = "no-store";
        cabeceras.ContentSecurityPolicy = "sandbox; default-src 'none'; img-src 'self'; object-src 'self'";
        http.Response.RegisterForDisposeAsync(doc.Archivo);

        // inline: se abre en el visor del navegador, que es mas seguro que bajarlo y abrirlo
        // con un programa de escritorio. El nombre va solo como sugerencia al guardar.
        var disposicion = new Microsoft.Net.Http.Headers.ContentDispositionHeaderValue("inline");
        disposicion.SetHttpFileName(doc.Nombre);
        cabeceras.ContentDisposition = disposicion.ToString();

        return Results.Stream(doc.Archivo.Contenido, doc.Archivo.TipoMime);
    }
}
