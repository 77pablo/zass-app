using FluentValidation;

namespace Zass.Aplicacion.Agenda;

/// <summary>
/// La validación del borde de API-33: forma, largos y rangos, para devolver un 400 con el
/// campo señalado. Lo que depende del reloj o de la base —que el servicio exista, que sea de
/// quien dice, el solape entre franjas— lo aplica <see cref="ServicioAgenda"/>, que además
/// repite estas: el servicio no puede fiarse de que alguien lo haya llamado por HTTP.
/// </summary>
public sealed class ValidadorDefinirDisponibilidad
    : AbstractValidator<DefinirDisponibilidadHttpCmd>
{
    /// <summary>
    /// Tope de franjas por servicio. Siete días con tres tramos cada uno (mañana, tarde,
    /// noche) son 21: el tope deja holgura y evita que una sola solicitud genere cientos de
    /// miles de bloques en la ventana de 8 semanas.
    /// </summary>
    public const int MaximoFranjas = 30;

    public ValidadorDefinirDisponibilidad()
    {
        RuleFor(x => x.PrestadorServicioId)
            .NotEmpty().WithMessage("Dinos a qué servicio aplica el horario.");

        // Una lista vacía SÍ es válida: es como se quitan todas las horas de un servicio.
        RuleFor(x => x.Franjas)
            .NotNull().WithMessage("Manda la lista de franjas, aunque vaya vacía.")
            .Must(f => f.Count <= MaximoFranjas)
            .WithMessage($"No más de {MaximoFranjas} franjas por servicio.");

        RuleForEach(x => x.Franjas).ChildRules(f =>
        {
            f.RuleFor(x => x.DiaSemana)
                .InclusiveBetween((short)1, (short)7)
                .WithMessage("El día va de 1 (lunes) a 7 (domingo).");

            f.RuleFor(x => x.HoraFin)
                .Must((franja, fin) => fin > franja.HoraInicio)
                .WithMessage("La hora de término tiene que ser posterior a la de inicio.");

            f.RuleFor(x => x.DuracionBloqueMin)
                .InclusiveBetween((short)5, (short)480)
                .WithMessage("Un bloque dura entre 5 minutos y 8 horas.");

            f.RuleFor(x => x)
                .Must(x => (x.HoraFin - x.HoraInicio).TotalMinutes >= x.DuracionBloqueMin)
                .WithName(nameof(FranjaCmd.DuracionBloqueMin))
                .WithMessage("La franja es más corta que un bloque.");
        });
    }
}

/// <summary>API-34. CS-11: un feriado o una ausencia.</summary>
public sealed class ValidadorBloquearFecha : AbstractValidator<BloquearFechaHttpCmd>
{
    public ValidadorBloquearFecha()
    {
        RuleFor(x => x.DesdeEn).NotEmpty().WithMessage("Dinos desde cuándo.");

        RuleFor(x => x.HastaEn)
            .Must((cmd, hasta) => hasta > cmd.DesdeEn)
            .WithMessage("El fin del bloqueo tiene que ser posterior al inicio.");

        RuleFor(x => x.Motivo).MaximumLength(ServicioAgenda.LargoMaximoMotivo);
    }
}

/// <summary>
/// API-36. La clave de idempotencia NO se valida aquí: viaja en la cabecera y la comprueba
/// <c>FiltroIdempotencia</c>, que corre antes y sin ella no deja entrar.
/// </summary>
public sealed class ValidadorAgendar : AbstractValidator<AgendarHttpCmd>
{
    public ValidadorAgendar()
    {
        RuleFor(x => x.BloqueId).NotEmpty().WithMessage("Elige una hora.");
        RuleFor(x => x.NotaBuscador).MaximumLength(ServicioAgenda.LargoMaximoNota);
    }
}
