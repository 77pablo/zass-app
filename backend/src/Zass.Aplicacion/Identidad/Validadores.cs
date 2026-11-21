using System.Text.RegularExpressions;
using FluentValidation;
using Zass.Aplicacion.Comun;

namespace Zass.Aplicacion.Identidad;

/// <summary>API-15 - Restablecer contrasena con el token que llego por correo.</summary>
public sealed record RestablecerContrasenaCmd(string Token, string ContrasenaNueva);

/// <summary>
/// Los limites comunes, en un solo lugar. Si el largo maximo de un campo vive en dos
/// archivos, tarde o temprano uno de los dos se queda atras y la base rechaza lo que el
/// validador acepto.
/// </summary>
public static class Limites
{
    public const int CorreoMaximo = 254;          // RFC 5321
    public const int ContrasenaMinima = 10;
    public const int ContrasenaMaxima = 128;      // hashear megabytes es una DoS gratis
    public const int NombreMaximo = 150;          // TBL-01
    public const int NombrePublicoMaximo = 150;   // TBL-05
    // 1500 y no 2000 (decision de Pablo): una presentacion mas larga ya no se lee en una
    // pantalla de celular. La COLUMNA de la base lee esta constante: el numero estuvo escrito
    // dos veces a mano, y cambiar una sin la otra dejaba un limite que mentia.
    public const int DescripcionMaxima = 1500;    // TBL-05
    public const int UrlMaxima = 300;

    /// <summary>
    /// Movil chileno, con o sin prefijo de pais y con o sin espacios. No se valida fijo
    /// a proposito: ZASS! contacta por WhatsApp y llamada, y el movil es lo util.
    /// </summary>
    public static readonly Regex MovilChileno = new(
        @"^(\+?56)?\s?9\s?\d{4}\s?\d{4}$", RegexOptions.Compiled);

    /// <summary>
    /// Telefono chileno de 9 digitos, movil o fijo (una empresa puede dar su fijo), con o sin
    /// +56 y con o sin espacios.
    /// </summary>
    public static readonly Regex TelefonoChileno = new(
        @"^(\+?56)?\s?\d(\s?\d){8}$", RegexOptions.Compiled);

    public const int ApellidoMaximo = 100;
    public const int RazonSocialMaxima = 200;
}

public sealed class ValidadorRegistrarCuenta : AbstractValidator<RegistrarCuentaCmd>
{
    public ValidadorRegistrarCuenta()
    {
        RuleFor(x => x.Correo)
            .NotEmpty().MaximumLength(Limites.CorreoMaximo)
            .EmailAddress().WithMessage("Ese correo no tiene forma de correo.");

        RuleFor(x => x.Contrasena)
            .NotEmpty()
            .MinimumLength(Limites.ContrasenaMinima)
                .WithMessage($"La contraseña necesita al menos {Limites.ContrasenaMinima} caracteres.")
            .MaximumLength(Limites.ContrasenaMaxima);

        RuleFor(x => x.Nombre)
            .Must(n => !string.IsNullOrWhiteSpace(n)).WithMessage("Falta el nombre.")
            .MaximumLength(Limites.NombreMaximo);

        // Registro completo (decision de Pablo, 20-nov-2026). Las reglas de los datos de la
        // cuenta viven en ValidadorDatosDeCuenta: las usa tambien ES-2 (completarlos en una
        // cuenta antigua) y tienen que ser LAS MISMAS, no una copia que se desincronice.
        Include(new ValidadorDatosDeCuenta());

        // RN-S1-08, otra vez y en la puerta: ni siquiera llega al caso de uso.
        RuleFor(x => x.Rol)
            .Must(r => ServicioRegistro.RolesPublicos.Contains(r))
            .WithMessage("Al registrarse se elige entre buscador y prestador.");
    }
}

/// <summary>
/// Los datos de la cuenta (decision de Pablo, 20-nov-2026): tipo, apellido o razon social,
/// RUT, telefono y la casilla «soy mayor de 18». Una sola definicion para el registro (API-08)
/// y para completar los datos de una cuenta antigua (ES-2).
/// </summary>
public sealed class ValidadorDatosDeCuenta : AbstractValidator<IDatosDeCuenta>
{
    public ValidadorDatosDeCuenta()
    {
        RuleFor(x => x.Tipo)
            .Must(t => t is "particular" or "empresa")
            .WithMessage("Elige si la cuenta es de un particular o de una empresa.");

        RuleFor(x => x.Apellido)
            .Must(a => !string.IsNullOrWhiteSpace(a)).WithMessage("Falta el apellido.")
            .MaximumLength(Limites.ApellidoMaximo)
            .When(x => x.Tipo == "particular");

        RuleFor(x => x.RazonSocial)
            .Must(r => !string.IsNullOrWhiteSpace(r)).WithMessage("Falta la razón social.")
            .MaximumLength(Limites.RazonSocialMaxima)
            .When(x => x.Tipo == "empresa");

        RuleFor(x => x.Rut)
            .Must(r => !string.IsNullOrWhiteSpace(r)).WithMessage("Falta el RUT.")
            .Must(Rut.EsValido).WithMessage("Ese RUT no es válido. Revisa el número y el dígito verificador.")
            .When(x => !string.IsNullOrWhiteSpace(x.Rut), ApplyConditionTo.CurrentValidator);

        RuleFor(x => x.Telefono)
            .Must(t => !string.IsNullOrWhiteSpace(t)).WithMessage("Falta el teléfono.")
            .Must(t => t is not null && Limites.TelefonoChileno.IsMatch(t.Trim()))
                .WithMessage("Escribe un teléfono chileno de 9 dígitos, por ejemplo +56 9 1234 5678.")
                .When(x => !string.IsNullOrWhiteSpace(x.Telefono), ApplyConditionTo.CurrentValidator);

        RuleFor(x => x.MayorDeEdad)
            .Equal(true).WithMessage("Para crear una cuenta tienes que ser mayor de 18 años.");
    }
}

/// <summary>ES-2. Completar los datos de una cuenta creada antes del registro completo.</summary>
public sealed class ValidadorCompletarDatosCuenta : AbstractValidator<CompletarDatosCuentaCmd>
{
    public ValidadorCompletarDatosCuenta() => Include(new ValidadorDatosDeCuenta());
}

public sealed class ValidadorIniciarSesion : AbstractValidator<IniciarSesionCmd>
{
    public ValidadorIniciarSesion()
    {
        RuleFor(x => x.Correo).NotEmpty().MaximumLength(Limites.CorreoMaximo);

        // Aqui NO se valida el largo de la contrasena. Si se validara, el formulario de
        // entrada revelaria el largo minimo que usa el sistema.
        RuleFor(x => x.Contrasena).NotEmpty().MaximumLength(Limites.ContrasenaMaxima);
    }
}

public sealed class ValidadorRestablecerContrasena : AbstractValidator<RestablecerContrasenaCmd>
{
    public ValidadorRestablecerContrasena()
    {
        RuleFor(x => x.Token).NotEmpty().MaximumLength(200);
        RuleFor(x => x.ContrasenaNueva)
            .NotEmpty()
            .MinimumLength(Limites.ContrasenaMinima)
            .MaximumLength(Limites.ContrasenaMaxima);
    }
}

public sealed class ValidadorEditarPerfilPrestador : AbstractValidator<EditarPerfilPrestadorCmd>
{
    public ValidadorEditarPerfilPrestador()
    {
        RuleFor(x => x.NombrePublico)
            .Must(n => !string.IsNullOrWhiteSpace(n)).WithMessage("Falta el nombre público.")
            .MaximumLength(Limites.NombrePublicoMaximo);

        RuleFor(x => x.Descripcion).MaximumLength(Limites.DescripcionMaxima);
        RuleFor(x => x.RubroId).GreaterThan((short)0).When(x => x.RubroId is not null);
        RuleFor(x => x.Version).GreaterThanOrEqualTo(0);

        RuleFor(x => x.TelefonoPublico)
            .Must(EsMovilChileno).WithMessage("Ese teléfono no parece un móvil chileno.")
            .When(x => !string.IsNullOrWhiteSpace(x.TelefonoPublico));

        RuleFor(x => x.Whatsapp)
            .Must(EsMovilChileno).WithMessage("Ese WhatsApp no parece un móvil chileno.")
            .When(x => !string.IsNullOrWhiteSpace(x.Whatsapp));

        RuleFor(x => x.CorreoPublico)
            .EmailAddress().MaximumLength(Limites.CorreoMaximo)
            .When(x => !string.IsNullOrWhiteSpace(x.CorreoPublico));

        RuleFor(x => x.SitioWeb)
            .Must(EsUrlWeb).WithMessage("El sitio web tiene que empezar con http o https.")
            .MaximumLength(Limites.UrlMaxima)
            .When(x => !string.IsNullOrWhiteSpace(x.SitioWeb));

        RuleFor(x => x.Latitud).InclusiveBetween(-90, 90).When(x => x.Latitud is not null);
        RuleFor(x => x.Longitud).InclusiveBetween(-180, 180).When(x => x.Longitud is not null);

        // Media coordenada no ubica nada, y dejarla pasar llenaria la columna generada
        // de nulos silenciosos.
        RuleFor(x => x.Longitud)
            .NotNull().WithMessage("Si viene la latitud, tiene que venir la longitud.")
            .When(x => x.Latitud is not null);
        RuleFor(x => x.Latitud)
            .NotNull().WithMessage("Si viene la longitud, tiene que venir la latitud.")
            .When(x => x.Longitud is not null);
    }

    private static bool EsMovilChileno(string? valor) =>
        valor is not null && Limites.MovilChileno.IsMatch(valor.Trim());

    /// <summary>
    /// Solo http y https. Dejar pasar cualquier esquema permitiria guardar
    /// `javascript:` en un campo que despues se pinta como enlace.
    /// </summary>
    private static bool EsUrlWeb(string? valor) =>
        Uri.TryCreate(valor, UriKind.Absolute, out var url)
        && (url.Scheme == Uri.UriSchemeHttp || url.Scheme == Uri.UriSchemeHttps);
}

public sealed class ValidadorEditarPerfilBuscador : AbstractValidator<EditarPerfilBuscadorCmd>
{
    public ValidadorEditarPerfilBuscador()
    {
        RuleFor(x => x.RadioPreferidoKm)
            .InclusiveBetween(ServicioPerfiles.RadioMinimoKm, ServicioPerfiles.RadioMaximoKm)
            .WithMessage($"El radio tiene que estar entre {ServicioPerfiles.RadioMinimoKm} "
                       + $"y {ServicioPerfiles.RadioMaximoKm} km.");

        RuleFor(x => x.ZonaInteresId).GreaterThan(0).When(x => x.ZonaInteresId is not null);

        // CU-04: hasta cinco oficios. Que EXISTAN lo mira el endpoint contra el catalogo (MOD-03):
        // un validador no consulta la base.
        RuleFor(x => x.RubrosInteres)
            .Must(r => r!.Distinct().Count() <= ServicioPerfiles.MaxRubrosDeInteres)
            .WithMessage($"Puedes elegir hasta {ServicioPerfiles.MaxRubrosDeInteres} oficios.")
            .When(x => x.RubrosInteres is not null);
        RuleForEach(x => x.RubrosInteres).GreaterThan((short)0).When(x => x.RubrosInteres is not null);
    }
}
