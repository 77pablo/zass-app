using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Zass.Aplicacion.Identidad;
using Zass.Dominio.Comun;

namespace Zass.Api.Configuracion;

/// <summary>
/// El rol interno (verificador, moderador, administrador) se comprueba EN LA BASE en cada
/// peticion a <c>/api/interno</c>, no solo en la cookie.
///
/// <para>
/// Los roles viajan como claims del token de acceso, que dura 15 minutos (DT-08). Sin esto, a un
/// verificador o administrador al que se le quita el rol, o cuya cuenta se suspende, le quedaban
/// hasta 15 minutos de acceso al panel: documentos de credenciales, auditoria con IP, emision de
/// tarjetas. La moderacion ya lo revalidaba dentro de su servicio (<c>ServicioResenas</c>,
/// <c>TieneRolAsync</c>); verificacion y auditoria no.
/// </para>
///
/// <para>
/// Va como requisito de las politicas y no endpoint por endpoint: las tres politicas internas
/// (<see cref="Politicas"/>) lo llevan, y la prueba de arquitectura
/// <c>AutorizacionEnRutasPrivadasPruebas</c> exige que todo <c>/api/interno</c> lo tenga, asi
/// que un endpoint interno nuevo no puede olvidarlo. El claim de la cookie se sigue exigiendo
/// primero (<c>RequireRole</c>): quien ni siquiera lo trae recibe 403 sin ir a la base.
/// </para>
/// </summary>
public sealed record RolVigenteEnBase(string Rol) : IAuthorizationRequirement;

/// <summary>
/// Cumple <see cref="RolVigenteEnBase"/> si la cuenta de la sesion existe, no esta suspendida
/// ni eliminada y TODAVIA tiene el rol en la base. Si no, no lo cumple y la respuesta es 403,
/// la misma que para quien nunca tuvo el rol.
/// </summary>
public sealed class ManejadorRolVigente : AuthorizationHandler<RolVigenteEnBase>
{
    private readonly IServicioIdentidad _identidad;

    public ManejadorRolVigente(IServicioIdentidad identidad) => _identidad = identidad;

    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext contexto, RolVigenteEnBase requisito)
    {
        if (contexto.User.UsuarioId() is not { } usuarioId) return;

        var ct = (contexto.Resource as HttpContext)?.RequestAborted ?? CancellationToken.None;
        var usuario = await _identidad.ObtenerUsuarioAsync(usuarioId, ct);

        if (usuario is null
            || usuario.Estado is EstadoCuenta.Suspendida or EstadoCuenta.Eliminada
            || !usuario.Roles.Contains(requisito.Rol, StringComparer.Ordinal))
            return;

        contexto.Succeed(requisito);
    }
}
