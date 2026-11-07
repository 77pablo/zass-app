using System.Text.RegularExpressions;
using FluentValidation;

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
    public const int DescripcionMaxima = 2000;    // TBL-05
    public const int UrlMaxima = 300;

    /// <summary>
    /// Movil chileno, con o sin prefijo de pais y con o sin espacios. No se valida fijo
    /// a proposito: ZASS! contacta por WhatsApp y llamada, y el movil es lo util.
    /// </summary>
    public static readonly Regex MovilChileno = new(
        @"^(\+?56)?\s?9\s?\d{4}\s?\d{4}$", RegexOptions.Compiled);
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

        // RN-S1-08, otra vez y en la puerta: ni siquiera llega al caso de uso.
        RuleFor(x => x.Rol)
            .Must(r => ServicioRegistro.RolesPublicos.Contains(r))
            .WithMessage("Al registrarse se elige entre buscador y prestador.");
    }
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
    }
}
