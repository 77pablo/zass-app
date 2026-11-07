using FluentValidation;

namespace Zass.Aplicacion.Credenciales;

/// <summary>
/// La validación del borde: forma y largos, para devolver un 400 con el campo señalado.
/// Las reglas que dependen del reloj o de la base (fecha no futura, tope de credenciales,
/// que la entidad exista) las aplica el servicio, que además repite las de forma: el servicio
/// no puede fiarse de que alguien lo haya llamado por HTTP.
/// </summary>
public sealed class ValidadorCrearCredencial : AbstractValidator<CrearCredencialHttpCmd>
{
    public ValidadorCrearCredencial()
    {
        RuleFor(x => x.Tipo).IsInEnum().WithMessage("Elige un tipo de credencial.");

        RuleFor(x => x.Nombre)
            .NotEmpty().WithMessage("Escribe el nombre de la credencial.")
            .MinimumLength(ServicioCredenciales.LargoMinimoNombre)
            .MaximumLength(ServicioCredenciales.LargoMaximoNombre);

        RuleFor(x => x.EntidadNombre)
            .MaximumLength(ServicioCredenciales.LargoMaximoEntidad);

        RuleFor(x => x)
            .Must(x => x.EntidadId is null || string.IsNullOrWhiteSpace(x.EntidadNombre))
            .WithName(nameof(CrearCredencialHttpCmd.EntidadNombre))
            .WithMessage("Elige la entidad de la lista o escribe su nombre, pero no las dos cosas.");

        RuleFor(x => x.VenceEn)
            .Must((x, vence) => vence is null || vence > x.ObtenidaEn)
            .WithMessage("El vencimiento tiene que ser posterior a la fecha de obtención.");
    }
}
