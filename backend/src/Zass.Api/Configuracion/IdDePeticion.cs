using System.Security.Cryptography;
using Microsoft.AspNetCore.Http;

namespace Zass.Api.Configuracion;

/// <summary>
/// Un identificador por peticion, para unir lo que ve el usuario con lo que dice el registro.
///
/// Cuando alguien diga «me salio un error», el identificador de su respuesta (cabecera
/// <see cref="Cabecera"/>, o <c>idPeticion</c> en el cuerpo del error) encuentra en los registros
/// de Render TODAS las lineas de esa peticion: van dentro de un ambito de registro con
/// <see cref="ClaveDelRegistro"/>.
///
/// Lo genera el servidor SIEMPRE. El que mande el cliente se ignora: aceptarlo dejaria que
/// cualquiera escribiera en el registro lo que quisiera, o hiciera pasar sus lineas por las de
/// otra peticion.
/// </summary>
public static class IdDePeticion
{
    public const string Cabecera = "X-Id-Peticion";
    public const string ClaveDelRegistro = "IdPeticion";
    public const string ClaveDelProblema = "idPeticion";

    /// <summary>16 caracteres hexadecimales: corto para dictarlo, de sobra para no repetirse.</summary>
    public static string Nuevo() => Convert.ToHexString(RandomNumberGenerator.GetBytes(8)).ToLowerInvariant();

    /// <summary>
    /// Va ANTES que el manejador de excepciones, para que su linea de «error no controlado»
    /// tambien quede dentro del ambito y lleve el identificador.
    /// </summary>
    public static IApplicationBuilder UsarIdDePeticion(this IApplicationBuilder app) =>
        app.Use(async (contexto, siguiente) =>
        {
            var id = Nuevo();
            contexto.TraceIdentifier = id;

            // OnStarting y no la cabecera directa: ante un 500, UseExceptionHandler limpia la
            // respuesta (cabeceras incluidas) antes de escribir el error. OnStarting corre justo
            // antes de mandar la primera linea, despues de esa limpieza: la cabecera llega
            // tambien en el caso en que mas se necesita.
            contexto.Response.OnStarting(() =>
            {
                contexto.Response.Headers[Cabecera] = id;
                return Task.CompletedTask;
            });

            var registro = contexto.RequestServices
                .GetRequiredService<ILoggerFactory>().CreateLogger("Zass.Peticion");
            // Una PLANTILLA y no un diccionario: la consola simple escribe el ambito con su
            // ToString(), y un Dictionary imprime el nombre de su tipo
            // («System.Collections.Generic.Dictionary`2[...]»), no el identificador. Visto en una
            // linea de registro real el 17-nov-2026. La plantilla imprime «IdPeticion:abc...» y
            // ademas expone el par IdPeticion=abc... a quien lea los ambitos como clave y valor.
            using (registro.BeginScope(ClaveDelRegistro + ":{" + ClaveDelRegistro + "}", id))
                await siguiente();
        });

    /// <summary>
    /// El mismo identificador en el cuerpo de todo error RFC 7807.
    ///
    /// ASP.NET 10 agrega por su cuenta un <c>traceId</c> distinto en cada peticion a
    /// <c>ProblemDetails.Extensions</c>. Eso rompe las pruebas que comparan respuestas entre sí
    /// (ApiPruebas, ResenasApiPruebas) y las que esperan un solo identificador (IdDePeticionPruebas).
    /// Se quita antes de poner el nuestro para que el error tenga siempre un solo <c>idPeticion</c>,
    /// idéntico entre respuestas iguales.
    /// </summary>
    public static void Configurar(ProblemDetailsOptions opciones) =>
        opciones.CustomizeProblemDetails = c =>
        {
            c.ProblemDetails.Extensions.Remove("traceId");
            c.ProblemDetails.Extensions[ClaveDelProblema] = c.HttpContext.TraceIdentifier;
        };
}
