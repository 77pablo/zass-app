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
