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

/// <summary>
/// API-21. Igual que en API-20, esto solo acota la forma del cuerpo: las reglas de verdad
/// —una sola zona principal, el tope de zonas, que existan— las aplica MOD-04.
/// </summary>
public sealed class ValidadorReemplazarCobertura : AbstractValidator<ReemplazarCoberturaHttpCmd>
{
    public const int MaximoPorPeticion = 50;

    public ValidadorReemplazarCobertura()
    {
        RuleFor(x => x.Zonas)
            .NotNull().WithMessage("Falta la lista de zonas.");

        When(x => x.Zonas is not null, () =>
        {
            RuleFor(x => x.Zonas.Count)
                .LessThanOrEqualTo(MaximoPorPeticion)
                .WithMessage($"No se pueden enviar mas de {MaximoPorPeticion} zonas de una vez.");
        });
    }
}

/// <summary>
/// API-01. Lo que se puede juzgar en el borde sin ir a la base.
///
/// El radio NO se valida aqui a proposito: CU-08 dice en E-400 que un radio fuera de
/// rango se CORRIGE al valor valido mas cercano y se informa, y eso lo hace MOD-05.
/// Rechazarlo aqui convertiria una correccion prevista en un error.
/// </summary>
public sealed class ValidadorBusqueda : AbstractValidator<BusquedaHttpQuery>
{
    public ValidadorBusqueda()
    {
        RuleFor(x => x.Rubro)
            .NotEmpty().WithMessage("Dinos que buscas: falta el rubro.")
            .MaximumLength(40);

        RuleFor(x => x.Lat)
            .InclusiveBetween(-90, 90).When(x => x.Lat is not null)
            .WithMessage("Esa latitud no es valida.");

        RuleFor(x => x.Lon)
            .InclusiveBetween(-180, 180).When(x => x.Lon is not null)
            .WithMessage("Esa longitud no es valida.");

        // Media coordenada no es una ubicacion. Sin esto, mandar solo `lat` caeria en
        // "dinos donde buscas", que es un mensaje que no explica el error de verdad.
        RuleFor(x => x.Lon)
            .NotNull().When(x => x.Lat is not null)
            .WithMessage("Falta la longitud.");
        RuleFor(x => x.Lat)
            .NotNull().When(x => x.Lon is not null)
            .WithMessage("Falta la latitud.");

        RuleFor(x => x.Calificacion)
            .InclusiveBetween(1m, 5m).When(x => x.Calificacion is not null)
            .WithMessage("La calificacion va de 1 a 5.");

        RuleFor(x => x.Cursor).MaximumLength(200);
    }
}
