using FluentValidation;
using Zass.Aplicacion.Puertos;
using Zass.Dominio.Comun;

namespace Zass.Aplicacion.Notificaciones;

/// <summary>API-45. Lo que entrega <c>PushSubscription.toJSON()</c> del navegador, aplanado.</summary>
public sealed record RegistrarPushCmd(string? Endpoint, string? P256dh, string? Auth);

/// <summary>Baja de ESTE navegador (al cerrar sesion). Solo el endpoint: las claves no hacen falta.</summary>
public sealed record BajaPushCmd(string? Endpoint);

/// <summary>ES-3. Donde viven los navegadores registrados (TBL nueva <c>suscripciones_push</c>).</summary>
public interface IRepositorioPush
{
    /// <summary>
    /// Deja el navegador a nombre de esta persona: lo crea, o lo mueve si era de otra cuenta
    /// (un computador compartido), o le renueva las claves. Si con esto pasa del tope, borra el
    /// que lleva mas sin usarse. No guarda: lo hace la unidad de trabajo.
    /// </summary>
    Task RegistrarAsync(Guid usuarioId, string endpoint, string p256dh, string auth, DateTime ahora,
        CancellationToken ct = default);

    /// <summary>Borra el navegador solo si es de esta persona. Devuelve si habia algo que borrar.</summary>
    Task<bool> BorrarAsync(Guid usuarioId, string endpoint, CancellationToken ct = default);

    /// <summary>
    /// Crea la gemela de push de los avisos de esta persona que AUN esperan en la cola de correo
    /// (por ejemplo, un recordatorio ya programado). Asi activar el push no deja fuera lo que se
    /// encolo justo antes. Si la gemela ya existe no hace nada (la clave es unica). Los avisos que
    /// ya salieron por correo no se repiten por push.
    /// </summary>
    Task<int> EncolarGemelasPendientesAsync(Guid usuarioId, CancellationToken ct = default);
}

public sealed class ServicioAvisosPush
{
    private readonly IRepositorioPush _repo;
    private readonly IUnidadDeTrabajo _unidad;
    private readonly IRelojSistema _reloj;

    public ServicioAvisosPush(IRepositorioPush repo, IUnidadDeTrabajo unidad, IRelojSistema reloj)
    {
        _repo = repo;
        _unidad = unidad;
        _reloj = reloj;
    }

    /// <summary>API-45. La validacion de la entrada ya paso (ValidadorRegistrarPush).</summary>
    public async Task<Resultado> RegistrarAsync(Guid usuarioId, RegistrarPushCmd cmd, CancellationToken ct = default)
    {
        await _repo.RegistrarAsync(usuarioId, cmd.Endpoint!.Trim(), cmd.P256dh!.Trim(), cmd.Auth!.Trim(),
            _reloj.Ahora, ct);
        await _unidad.GuardarAsync(ct);
        await _repo.EncolarGemelasPendientesAsync(usuarioId, ct);
        return Resultado.Ok();
    }

    /// <summary>
    /// Baja de este navegador. Responde lo mismo exista o no, y sea o no de esta persona: asi no
    /// sirve para averiguar si un endpoint esta registrado en otra cuenta.
    /// </summary>
    public async Task<Resultado> DarDeBajaAsync(Guid usuarioId, BajaPushCmd cmd, CancellationToken ct = default)
    {
        if (await _repo.BorrarAsync(usuarioId, cmd.Endpoint!.Trim(), ct)) await _unidad.GuardarAsync(ct);
        return Resultado.Ok();
    }
}

/// <summary>Una clave en base64url que, decodificada, mide exactamente lo esperado.</summary>
internal static class Base64Url
{
    public static bool MideBytes(string? valor, int bytes, byte? primerByte = null)
    {
        if (string.IsNullOrWhiteSpace(valor) || valor.Length > 200) return false;
        var s = valor.Trim().Replace('-', '+').Replace('_', '/').TrimEnd('=');
        s = s.PadRight(s.Length + (4 - s.Length % 4) % 4, '=');
        var buffer = new byte[s.Length];
        if (!Convert.TryFromBase64String(s, buffer, out var escritos)) return false;
        return escritos == bytes && (primerByte is null || buffer[0] == primerByte);
    }
}

public sealed class ValidadorRegistrarPush : AbstractValidator<RegistrarPushCmd>
{
    // TEXTOS PROPUESTOS. La persona casi nunca los ve: el navegador arma estos datos solo.
    public const string NavegadorNoCompatible =
        "Este navegador no es compatible con los avisos de ZASS!. Prueba con otro navegador.";

    public ValidadorRegistrarPush()
    {
        RuleFor(x => x.Endpoint).Cascade(CascadeMode.Stop)
            .NotEmpty().WithMessage(NavegadorNoCompatible)
            .MaximumLength(2048).WithMessage(NavegadorNoCompatible)
            .Must(AvisosPush.EndpointPermitido).WithMessage(NavegadorNoCompatible);
        // P-256 sin comprimir: 65 bytes que empiezan con 0x04 (RFC 8291).
        RuleFor(x => x.P256dh).Must(v => Base64Url.MideBytes(v, 65, 0x04)).WithMessage(NavegadorNoCompatible);
        RuleFor(x => x.Auth).Must(v => Base64Url.MideBytes(v, 16)).WithMessage(NavegadorNoCompatible);
    }
}

public sealed class ValidadorBajaPush : AbstractValidator<BajaPushCmd>
{
    public ValidadorBajaPush()
    {
        RuleFor(x => x.Endpoint).NotEmpty().MaximumLength(2048)
            .WithMessage(ValidadorRegistrarPush.NavegadorNoCompatible);
    }
}
