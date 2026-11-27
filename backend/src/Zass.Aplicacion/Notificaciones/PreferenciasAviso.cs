using FluentValidation;
using Zass.Aplicacion.Puertos;
using Zass.Dominio.Comun;

namespace Zass.Aplicacion.Notificaciones;

/// <summary>
/// ES-3b · API-43 (CS-14). Por que canal sale cada tipo de aviso. La preferencia se guarda por
/// tipo, pero la pantalla la muestra por grupo (decision del 21-nov): elegir quince filas por
/// separado no se entiende.
///
/// Se aplica AL ENVIAR, no al encolar: cada procesador mira la preferencia justo antes de mandar
/// y, si ese canal esta apagado, deja el aviso Anulado. Asi un cambio alcanza tambien a los
/// recordatorios que ya estaban en la cola.
/// </summary>
public static class PreferenciasAviso
{
    /// <summary>Los grupos de la pantalla, en orden, con sus tipos. Cada tipo con push esta en uno solo.</summary>
    public static readonly IReadOnlyList<(string Clave, TipoNotificacion[] Tipos)> Grupos =
    [
        ("citas", [TipoNotificacion.CitaSolicitada, TipoNotificacion.CitaConfirmada, TipoNotificacion.CitaRechazada,
                   TipoNotificacion.CitaCancelada, TipoNotificacion.CitaReprogramada]),
        ("recordatorios", [TipoNotificacion.Recordatorio24h, TipoNotificacion.Recordatorio2h]),
        ("resenas", [TipoNotificacion.ResenaNueva, TipoNotificacion.ResenaRespondida]),
        ("perfil", [TipoNotificacion.CredencialResuelta, TipoNotificacion.CredencialVencida,
                    TipoNotificacion.BorradorPorEliminarse, TipoNotificacion.PerfilPublicado]),
        ("cobros", [TipoNotificacion.SuscripcionPorVencer, TipoNotificacion.SuscripcionVencida,
                    TipoNotificacion.CobroRechazado, TipoNotificacion.TarjetaActivada]),
    ];

    /// <summary>
    /// Los tipos que la persona puede configurar: los mismos que admiten push. Verificar el correo,
    /// recuperar la contrasena y el RUT ya usado van solo por correo y no se configuran.
    /// </summary>
    public static bool Configurable(TipoNotificacion tipo) => AvisosPush.AdmitePush(tipo);

    /// <summary>
    /// El correo no se puede apagar para los avisos de cobro ni de seguridad (PLAN-ESTEBAN, ES-3).
    /// La tarjeta activada cuenta como seguridad: avisa si alguien activo tu tarjeta (decision del
    /// 21-nov). Verificar el correo, recuperar la contrasena y el aviso de RUT ya usado (seguridad,
    /// solo por correo) tampoco, aunque igual no se configuran: queda escrito por si algun dia si.
    /// </summary>
    public static bool CorreoFijo(TipoNotificacion tipo) => tipo is
        TipoNotificacion.SuscripcionPorVencer or TipoNotificacion.SuscripcionVencida
        or TipoNotificacion.CobroRechazado or TipoNotificacion.TarjetaActivada
        or TipoNotificacion.CorreoVerificacion or TipoNotificacion.RecuperarContrasena
        or TipoNotificacion.RegistroConRutExistente or TipoNotificacion.MfaActivado
        or TipoNotificacion.RegistroConCorreoExistente
        or TipoNotificacion.MfaDesactivado or TipoNotificacion.MfaCodigosRegenerados
        or TipoNotificacion.MfaQuitadoPorAdministrador;

    /// <summary>
    /// Si un aviso de este tipo debe salir por este canal, segun la preferencia guardada (o null si
    /// no hay fila). Lo que la regla obliga gana a lo guardado, por si una fila vieja dice otra cosa.
    /// </summary>
    public static bool Sale(TipoNotificacion tipo, CanalNotificacion canal, PreferenciaAviso? preferencia)
    {
        if (canal == CanalNotificacion.Correo && CorreoFijo(tipo)) return true;
        if (preferencia is null || !Configurable(tipo)) return true;
        return canal == CanalNotificacion.Correo ? preferencia.Correo : preferencia.Push;
    }

    /// <summary>
    /// Si hace falta ir a la base a mirar la preferencia. Los tipos que no se configuran y el
    /// correo fijo salen siempre: verificar el correo, el mas frecuente, no cuesta una consulta.
    /// </summary>
    public static bool DependeDeLaPreferencia(TipoNotificacion tipo, CanalNotificacion canal) =>
        Configurable(tipo) && !(canal == CanalNotificacion.Correo && CorreoFijo(tipo));
}

/// <summary>Una fila de API-43: un tipo y sus dos canales.</summary>
public sealed record PreferenciaTipoCmd(TipoNotificacion? Tipo, bool? Correo, bool? Push);

/// <summary>API-43. Cambia solo los tipos que vienen; los demas quedan como estaban.</summary>
public sealed record GuardarPreferenciasCmd(IReadOnlyList<PreferenciaTipoCmd>? Preferencias);

/// <summary>Lo que ve la pantalla: cada tipo con su grupo, sus canales y si el correo es fijo.</summary>
public sealed record PreferenciaTipoDto(string Tipo, string Grupo, bool Correo, bool Push, bool CorreoFijo);

public sealed record PreferenciasDto(IReadOnlyList<PreferenciaTipoDto> Preferencias);

/// <summary>Un tipo con sus dos canales, ya validado.</summary>
public readonly record struct CambioDePreferencia(TipoNotificacion Tipo, bool Correo, bool Push);

/// <summary>ES-3b. Donde viven las preferencias (tabla <c>preferencias_aviso</c>).</summary>
public interface IRepositorioPreferenciasAviso
{
    Task<List<PreferenciaAviso>> DeAsync(Guid usuarioId, CancellationToken ct = default);

    /// <summary>
    /// Deja estos valores en la base, en el acto y sin leer antes (upsert): dos peticiones a la vez
    /// no chocan con la clave primaria. Una fila con los dos canales encendidos es lo mismo que no
    /// tenerla, asi que se borra en vez de guardarse. Va dentro de la transaccion del servicio.
    /// </summary>
    Task FijarAsync(Guid usuarioId, IReadOnlyCollection<CambioDePreferencia> cambios, DateTime ahora,
        CancellationToken ct = default);
}

public sealed class ServicioPreferenciasAviso
{
    private readonly IRepositorioPreferenciasAviso _repo;
    private readonly IUnidadDeTrabajo _unidad;
    private readonly IRelojSistema _reloj;

    public ServicioPreferenciasAviso(IRepositorioPreferenciasAviso repo, IUnidadDeTrabajo unidad, IRelojSistema reloj)
    {
        _repo = repo;
        _unidad = unidad;
        _reloj = reloj;
    }

    public async Task<PreferenciasDto> VerAsync(Guid usuarioId, CancellationToken ct = default)
    {
        var guardadas = (await _repo.DeAsync(usuarioId, ct)).ToDictionary(p => p.Tipo);
        var lista = PreferenciasAviso.Grupos
            .SelectMany(g => g.Tipos.Select(t =>
            {
                guardadas.TryGetValue(t, out var p);
                return new PreferenciaTipoDto(t.ToString(), g.Clave,
                    PreferenciasAviso.Sale(t, CanalNotificacion.Correo, p),
                    PreferenciasAviso.Sale(t, CanalNotificacion.Push, p),
                    PreferenciasAviso.CorreoFijo(t));
            }))
            .ToList();
        return new PreferenciasDto(lista);
    }

    /// <summary>API-43. La validacion ya paso (ValidadorGuardarPreferencias). Devuelve como quedo todo.</summary>
    public async Task<Resultado<PreferenciasDto>> GuardarAsync(Guid usuarioId, GuardarPreferenciasCmd cmd,
        CancellationToken ct = default)
    {
        var cambios = cmd.Preferencias!
            .Select(p => new CambioDePreferencia(p.Tipo!.Value, p.Correo!.Value, p.Push!.Value)).ToList();
        // Todo o nada: si un tipo fallara, no queda la mitad del grupo cambiada.
        await _unidad.EnTransaccionAsync(async c =>
        {
            await _repo.FijarAsync(usuarioId, cambios, _reloj.Ahora, c);
            return true;
        }, ct);
        return Resultado.Ok(await VerAsync(usuarioId, ct));
    }
}

public sealed class ValidadorGuardarPreferencias : AbstractValidator<GuardarPreferenciasCmd>
{
    // TEXTOS PROPUESTOS. La pantalla no deja llegar a estos casos; los ve quien llama a la API a mano.
    public const string SinCambios = "Elige al menos un aviso para cambiar.";
    public const string TipoNoConfigurable = "Ese aviso no se puede configurar.";
    public const string TipoRepetido = "Cada aviso puede venir una sola vez.";
    public const string FaltaCanal = "Indica si quieres el aviso por correo y por push.";
    public const string CorreoObligatorio =
        "Los avisos de tu plan, tus cobros y tu tarjeta siempre llegan por correo.";

    public ValidadorGuardarPreferencias()
    {
        RuleFor(x => x.Preferencias).Cascade(CascadeMode.Stop)
            .NotEmpty().WithMessage(SinCambios)
            .Must(l => l!.Count <= AvisosPush.TiposConPush.Length).WithMessage(TipoRepetido)
            .Must(l => l!.Where(p => p?.Tipo is not null).GroupBy(p => p!.Tipo).All(g => g.Count() == 1))
            .WithMessage(TipoRepetido);

        // Una fila null (`"preferencias": [null]`) no la mira ChildRules: sin esta regla llegaria
        // al servicio y seria un 500.
        RuleForEach(x => x.Preferencias).NotNull().WithMessage(TipoNoConfigurable).ChildRules(p =>
        {
            p.RuleFor(x => x.Tipo).Cascade(CascadeMode.Stop)
                .NotNull().WithMessage(TipoNoConfigurable)
                .Must(t => PreferenciasAviso.Configurable(t!.Value)).WithMessage(TipoNoConfigurable);
            p.RuleFor(x => x.Correo).NotNull().WithMessage(FaltaCanal);
            p.RuleFor(x => x.Push).NotNull().WithMessage(FaltaCanal);
            p.RuleFor(x => x.Correo)
                .Must(c => c != false).When(x => x.Tipo is { } t && PreferenciasAviso.CorreoFijo(t))
                .WithMessage(CorreoObligatorio);
        });
    }
}
