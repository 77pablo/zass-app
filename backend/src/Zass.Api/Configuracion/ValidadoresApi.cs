using FluentValidation;
using Zass.Api.Endpoints;
using Zass.Aplicacion.Identidad;

namespace Zass.Api.Configuracion;

// Los comandos que solo existen en el borde (un token suelto, un correo suelto) tienen
// su validador aqui. Sin validador registrado, el filtro rechaza la peticion: no hay
// forma de dejar un endpoint sin esquema por descuido.

public sealed class ValidadorVerificarCorreo : AbstractValidator<VerificarCorreoCmd>
{
    public ValidadorVerificarCorreo() =>
        RuleFor(x => x.Token).NotEmpty().MaximumLength(200);
}

public sealed class ValidadorPedirRecuperacion : AbstractValidator<PedirRecuperacionCmd>
{
    public ValidadorPedirRecuperacion() =>
        RuleFor(x => x.Correo)
            .NotEmpty()
            .MaximumLength(Limites.CorreoMaximo)
            .EmailAddress();
}

public sealed class ValidadorCambiarContrasena : AbstractValidator<CambiarContrasenaCmd>
{
    public ValidadorCambiarContrasena()
    {
        // La actual no se juzga por largo: puede venir de antes de que existiera el
        // minimo. La nueva si.
        RuleFor(x => x.ContrasenaActual).NotEmpty().MaximumLength(Limites.ContrasenaMaxima);
        RuleFor(x => x.ContrasenaNueva)
            .NotEmpty()
            .MinimumLength(Limites.ContrasenaMinima)
            .MaximumLength(Limites.ContrasenaMaxima)
            .NotEqual(x => x.ContrasenaActual)
                .WithMessage("La contraseña nueva tiene que ser distinta de la actual.");
    }
}

/// <summary>
/// API-20. Solo acota la forma del cuerpo: el tope de verdad es el del plan y lo aplica
/// MOD-03, que es quien sabe cual es. Esto evita que una lista de diez mil servicios
/// llegue a recorrerse entera.
/// </summary>
public sealed class ValidadorReemplazarServicios : AbstractValidator<ReemplazarServiciosHttpCmd>
{
    public const int MaximoPorPeticion = 50;

    public ValidadorReemplazarServicios()
    {
        RuleFor(x => x.Servicios)
            .NotNull().WithMessage("Falta la lista de servicios.");

        When(x => x.Servicios is not null, () =>
        {
            RuleFor(x => x.Servicios.Count)
                .LessThanOrEqualTo(MaximoPorPeticion)
                .WithMessage($"No se pueden enviar mas de {MaximoPorPeticion} servicios de una vez.");

            RuleForEach(x => x.Servicios).ChildRules(s =>
            {
                s.RuleFor(i => i.NombreNuevo).MaximumLength(200);
                s.RuleFor(i => i.ModoAgenda).NotEmpty().MaximumLength(20);
            });
        });
    }
}
