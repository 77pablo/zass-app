using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Zass.Aplicacion.Identidad;

namespace Zass.Api.Configuracion;

/// <summary>
/// ES-2. Mientras a la cuenta le falten los datos del registro completo (RUT, telefono, mayoria
/// de edad; decision de Pablo, 20-nov-2026), la API no deja seguir: no basta con esconder
/// pantallas.
///
/// <para>
/// <b>Que se bloquea:</b> todo endpoint que EXIGE sesion. Las paginas publicas (busqueda,
/// fichas, catalogo) siguen abiertas aunque haya sesion: las veria igual cerrandola.
/// </para>
///
/// <para>
/// <b>Que queda abierto, a proposito:</b> todo <c>/api/auth</c> (entrar, salir, renovar,
/// recuperar la contrasena, verificar el correo), ver la propia cuenta (API-16, que es donde la
/// interfaz se entera de que falta), completar los datos y <b>eliminar la cuenta</b> (API-17):
/// irse no se condiciona a entregar mas datos personales (Ley 21.719).
/// </para>
///
/// <para>
/// <b>428 y no 403</b>: en este proyecto el 403 es «eso no es tuyo» y se responde igual que un
/// 404 (seguridad.md §5). Esto es otra cosa —una condicion previa de la propia cuenta— y la
/// interfaz tiene que poder distinguirlo para llevar a la pantalla de completar datos.
/// </para>
///
/// <para>
/// Las cuentas internas (verificador, moderador, administrador) no se bloquean: ver
/// <see cref="DatosDeCuenta"/>. Se mira primero el rol de la cookie para no ir a la base.
/// </para>
/// </summary>
public sealed class FiltroDatosDeCuenta
{
    public const string EndpointCompletar = "ES-2-COMPLETAR-DATOS";
    public const string CodigoDatosPendientes = "datos_de_cuenta_pendientes";

    /// <summary>Los endpoints con sesion que se pueden usar con datos pendientes.</summary>
    public static readonly IReadOnlySet<string> Permitidos = new HashSet<string>(StringComparer.Ordinal)
    {
        "API-16",            // ver la propia cuenta
        "API-17",            // eliminar la cuenta
        EndpointCompletar,   // completar los datos
        "API-45-baja",       // dejar de recibir push en este navegador (ES-3), al cerrar sesion
    };

    private readonly RequestDelegate _siguiente;

    public FiltroDatosDeCuenta(RequestDelegate siguiente) => _siguiente = siguiente;

    public async Task InvokeAsync(HttpContext http, ServicioCuenta cuentas)
    {
        if (!await DebeBloquearAsync(http, cuentas))
        {
            await _siguiente(http);
            return;
        }

        await Results.Problem(
                title: "Completa los datos de tu cuenta",
                detail: "Antes de seguir, completa tu RUT, tu teléfono y confirma que eres mayor de 18 años.",
                statusCode: StatusCodes.Status428PreconditionRequired,
                extensions: new Dictionary<string, object?> { ["codigo"] = CodigoDatosPendientes })
            .ExecuteAsync(http);
    }

    private static async Task<bool> DebeBloquearAsync(HttpContext http, ServicioCuenta cuentas)
    {
        var endpoint = http.GetEndpoint();
        if (endpoint is null) return false;

        // Solo lo que exige sesion. Un endpoint publico con sesion abierta pasa.
        if (endpoint.Metadata.GetMetadata<IAllowAnonymous>() is not null) return false;
        if (endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>().Count == 0) return false;

        if (http.User.Identity?.IsAuthenticated != true) return false;
        if (http.Request.Path.StartsWithSegments("/api/auth")) return false;

        var nombre = endpoint.Metadata.GetMetadata<IEndpointNameMetadata>()?.EndpointName;
        if (nombre is not null && Permitidos.Contains(nombre)) return false;

        // Cuenta interna: algun rol que no sea de los publicos. Sin ir a la base.
        if (http.User.FindAll(ClaimTypes.Role).Any(r => !ServicioRegistro.RolesPublicos.Contains(r.Value)))
            return false;

        var usuarioId = http.User.UsuarioId();
        if (usuarioId is null) return false;   // el propio endpoint respondera 401

        var pendientes = await cuentas.DatosPendientesAsync(usuarioId.Value, http.RequestAborted);
        return pendientes.Length > 0;
    }
}

public static class ExtensionesFiltroDatosDeCuenta
{
    /// <summary>Va DESPUES de UseAuthorization: necesita al usuario ya autenticado.</summary>
    public static IApplicationBuilder UsarFiltroDatosDeCuenta(this IApplicationBuilder app) =>
        app.UseMiddleware<FiltroDatosDeCuenta>();
}
