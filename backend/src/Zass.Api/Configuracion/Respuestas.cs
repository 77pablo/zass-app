using System.Security.Claims;
using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using Zass.Dominio.Comun;

namespace Zass.Api.Configuracion;

/// <summary>
/// Traduce un Resultado del dominio a HTTP. Un solo lugar, para que dos endpoints no
/// contesten distinto ante la misma falla.
/// </summary>
public static class Respuestas
{
    public static IResult Problema(Resultado resultado) => Results.Problem(
        title: Titulo(resultado.Codigo),
        detail: resultado.Mensaje,
        statusCode: Estado(resultado.Codigo));

    public static IResult DeResultado(Resultado resultado) =>
        resultado.Exito
            ? (resultado.Mensaje is null
                ? Results.NoContent()
                : Results.Ok(new { mensaje = resultado.Mensaje }))
            : Problema(resultado);

    public static IResult DeResultado<T>(Resultado<T> resultado) =>
        resultado.Exito ? Results.Ok(resultado.Valor) : Problema(resultado);

    /// <summary>
    /// 403 y 404 se mapean igual a proposito (seguridad.md, seccion 5): si editar la
    /// credencial de otro devolviera 403 y una inexistente 404, probar identificadores
    /// diria que existe y de quien es. Por eso los casos de uso devuelven NoEncontrado
    /// cuando el dueno no coincide, y aqui sale 404.
    /// </summary>
    private static int Estado(string? codigo) => codigo switch
    {
        Fallas.NoAutorizado => StatusCodes.Status401Unauthorized,
        Fallas.NoEncontrado => StatusCodes.Status404NotFound,
        Fallas.Conflicto => StatusCodes.Status409Conflict,
        Fallas.ReglaDeNegocio => StatusCodes.Status422UnprocessableEntity,
        Fallas.Validacion => StatusCodes.Status400BadRequest,
        Fallas.VersionDesactualizada => StatusCodes.Status412PreconditionFailed,
        _ => StatusCodes.Status400BadRequest,
    };

    private static string Titulo(string? codigo) => codigo switch
    {
        Fallas.NoAutorizado => "No autorizado",
        Fallas.NoEncontrado => "No encontrado",
        Fallas.Conflicto => "Conflicto",
        Fallas.ReglaDeNegocio => "Regla de negocio",
        Fallas.Validacion => "Datos invalidos",
        Fallas.VersionDesactualizada => "Version desactualizada",
        _ => "Peticion incorrecta",
    };
}

/// <summary>
/// seguridad.md, seccion 3: un filtro global corre el validador antes de llegar al caso
/// de uso, y **no existe la opcion de no validar**.
///
/// Si no hay validador registrado para el tipo del cuerpo, la peticion se rechaza con
/// 500 en vez de pasar sin revisar. Es deliberado: un endpoint nuevo sin validador es un
/// error del equipo, y tiene que doler en la primera prueba, no en produccion.
/// </summary>
public sealed class FiltroDeValidacion<T> : IEndpointFilter where T : class
{
    public async ValueTask<object?> InvokeAsync(
        EndpointFilterInvocationContext contexto, EndpointFilterDelegate siguiente)
    {
        var cuerpo = contexto.Arguments.OfType<T>().FirstOrDefault();
        if (cuerpo is null)
            return Results.Problem(
                title: "Datos invalidos", detail: "Falta el cuerpo de la peticion.",
                statusCode: StatusCodes.Status400BadRequest);

        var validador = contexto.HttpContext.RequestServices.GetService<IValidator<T>>()
            ?? throw new InvalidOperationException(
                $"No hay validador registrado para {typeof(T).Name}. "
                + "Todo endpoint tiene que declarar su esquema (seguridad.md, seccion 3).");

        var resultado = await validador.ValidateAsync(cuerpo);
        if (resultado.IsValid) return await siguiente(contexto);

        // Se devuelve el campo y el motivo, nunca el valor enviado: el registro de
        // validaciones rechazadas no debe convertirse en una copia de la entrada.
        return Results.ValidationProblem(
            resultado.Errors
                .GroupBy(e => e.PropertyName)
                .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray()),
            title: "Datos invalidos");
    }
}

public static class ExtensionesDeRuta
{
    /// <summary>Atajo para colgar el validador del tipo del cuerpo en una ruta.</summary>
    public static RouteHandlerBuilder ConValidacion<T>(this RouteHandlerBuilder ruta)
        where T : class => ruta.AddEndpointFilter<FiltroDeValidacion<T>>();

    /// <summary>El id del usuario de la sesion, o nulo si no hay sesion.</summary>
    public static Guid? UsuarioId(this ClaimsPrincipal principal)
    {
        var valor = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(valor, out var id) ? id : null;
    }
}

/// <summary>
/// seguridad.md: token CSRF por sesion en la cabecera X-CSRF-Token, obligatorio para
/// todo lo que no sea GET.
///
/// Se aplica a los endpoints con sesion. Registro, inicio de sesion y recuperacion
/// quedan fuera a proposito: todavia no hay sesion que falsificar, y lo que los protege
/// es SameSite=Lax mas el grupo A de limite de tasa.
/// </summary>
public sealed class FiltroCsrf : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(
        EndpointFilterInvocationContext contexto, EndpointFilterDelegate siguiente)
    {
        var http = contexto.HttpContext;

        if (HttpMethods.IsGet(http.Request.Method)
            || HttpMethods.IsHead(http.Request.Method)
            || HttpMethods.IsOptions(http.Request.Method))
            return await siguiente(contexto);

        var antifalsificacion = http.RequestServices
            .GetRequiredService<Microsoft.AspNetCore.Antiforgery.IAntiforgery>();
        try
        {
            await antifalsificacion.ValidateRequestAsync(http);
        }
        catch (Microsoft.AspNetCore.Antiforgery.AntiforgeryValidationException)
        {
            // 403 y no 400: la peticion esta bien formada, lo que falta es la prueba de
            // que la origino la propia aplicacion.
            return Results.Problem(
                title: "Peticion no verificada",
                detail: "Falta o no corresponde el token de la cabecera X-CSRF-Token.",
                statusCode: StatusCodes.Status403Forbidden);
        }

        return await siguiente(contexto);
    }
}
