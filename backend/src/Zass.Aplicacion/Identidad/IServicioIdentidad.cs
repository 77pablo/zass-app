using Zass.Aplicacion.Puertos;
using Zass.Dominio.Comun;
using Zass.Dominio.Identidad;

namespace Zass.Aplicacion.Identidad;

public sealed record UsuarioDto(
    Guid Id,
    string Correo,
    string Nombre,
    bool CorreoVerificado,
    EstadoCuenta Estado,
    string[] Roles);

/// <summary>
/// API-05 - La ficha publica. Es el DTO que mas expone de todo el sistema, y por eso
/// lo que NO tiene importa mas que lo que tiene: ni direccion, ni latitud, ni longitud,
/// ni el correo de la cuenta. Solo los canales que el prestador marco como publicos.
/// </summary>
public sealed record PerfilPublicoDto(
    Guid Id,
    string Slug,
    string NombrePublico,
    string? Descripcion,
    string? UrlFoto,
    short? RubroId,
    string? ZonaPrincipal,
    string[] ZonasCubiertas,
    decimal? PromedioCalificacion,
    int TotalResenas,
    string? TelefonoPublico,
    string? CorreoPublico,
    string? Whatsapp,
    string? SitioWeb,
    DateTime? PublicadoEn);

/// <summary>
/// MOD-01 - Interfaz publica del modulo de Identidad.
///
/// Regla de propiedad de tabla-de-modulos.md: TBL-01 a TBL-06 son de MOD-01 y nadie mas
/// las consulta. Los otros modulos piden lo que necesitan por aqui. Esto es lo que
/// permitira, el dia que haga falta, separar un subsistema sin reescribirlo todo.
/// </summary>
public interface IServicioIdentidad
{
    Task<UsuarioDto?> ObtenerUsuarioAsync(Guid usuarioId, CancellationToken ct = default);

    Task<bool> TieneRolAsync(Guid usuarioId, string codigoRol, CancellationToken ct = default);

    Task<bool> PerfilPublicadoAsync(Guid perfilId, CancellationToken ct = default);

    /// <summary>
    /// RN-S2-09 y RN-S5-09: nadie verifica lo propio ni modera su propio reporte.
    /// </summary>
    Task<bool> EsMismaPersonaAsync(Guid usuarioA, Guid usuarioB, CancellationToken ct = default);

    Task<PerfilPublicoDto?> PerfilPublicoAsync(Guid perfilId, CancellationToken ct = default);

    Task<PerfilPublicoDto?> PerfilPublicoPorSlugAsync(string slug, CancellationToken ct = default);
}

public sealed class ServicioIdentidad : IServicioIdentidad
{
    private readonly IRepositorioUsuarios _usuarios;
    private readonly IRepositorioPerfiles _perfiles;

    public ServicioIdentidad(IRepositorioUsuarios usuarios, IRepositorioPerfiles perfiles)
    {
        _usuarios = usuarios;
        _perfiles = perfiles;
    }

    public async Task<UsuarioDto?> ObtenerUsuarioAsync(Guid usuarioId, CancellationToken ct = default)
    {
        var usuario = await _usuarios.PorIdAsync(usuarioId, ct);
        return usuario is null ? null : new UsuarioDto(
            usuario.Id,
            usuario.Correo,
            usuario.Nombre,
            usuario.CorreoVerificado,
            usuario.Estado,
            usuario.Roles.Where(r => r.Rol is not null).Select(r => r.Rol!.Codigo).ToArray());
    }

    public async Task<bool> TieneRolAsync(
        Guid usuarioId, string codigoRol, CancellationToken ct = default)
    {
        var usuario = await _usuarios.PorIdAsync(usuarioId, ct);
        return usuario?.TieneRol(codigoRol) ?? false;
    }

    public async Task<bool> PerfilPublicadoAsync(Guid perfilId, CancellationToken ct = default)
    {
        var perfil = await _perfiles.PrestadorPorIdAsync(perfilId, ct);
        return perfil?.Estado == EstadoPerfil.Publicado;
    }

    /// <summary>
    /// Hoy solo detecta la misma cuenta, porque el modelo de datos no guarda nada que
    /// ligue dos cuentas distintas a una persona (no se pide RUT, por minimizacion).
    /// Devolver falso para dos cuentas de la misma persona es un falso negativo conocido:
    /// deja pasar un conflicto de interes en vez de inventarse uno. Ampliarlo exige
    /// decidir primero QUE dato lo probaria, y esa decision no esta tomada.
    /// </summary>
    public async Task<bool> EsMismaPersonaAsync(
        Guid usuarioA, Guid usuarioB, CancellationToken ct = default)
    {
        if (usuarioA == usuarioB) return true;

        var a = await _usuarios.PorIdAsync(usuarioA, ct);
        var b = await _usuarios.PorIdAsync(usuarioB, ct);
        if (a is null || b is null) return false;

        return string.Equals(a.Correo, b.Correo, StringComparison.OrdinalIgnoreCase);
    }

    public async Task<PerfilPublicoDto?> PerfilPublicoAsync(
        Guid perfilId, CancellationToken ct = default) =>
        Publicar(await _perfiles.PrestadorPorIdAsync(perfilId, ct));

    public async Task<PerfilPublicoDto?> PerfilPublicoPorSlugAsync(
        string slug, CancellationToken ct = default) =>
        Publicar(await _perfiles.PrestadorPorSlugAsync(slug, ct));

    /// <summary>
    /// Un perfil que no esta publicado no existe para el publico: se devuelve nulo, igual
    /// que si el slug fuera inventado. Distinguir "existe pero esta en borrador" de "no
    /// existe" permitiria sondear quien se dio de alta sin publicar.
    /// </summary>
    private static PerfilPublicoDto? Publicar(PerfilPrestador? perfil)
    {
        if (perfil is null || perfil.Estado != EstadoPerfil.Publicado) return null;

        return new PerfilPublicoDto(
            perfil.Id,
            perfil.Slug,
            perfil.NombrePublico,
            perfil.Descripcion,
            perfil.UrlFoto,
            perfil.RubroId,
            perfil.ZonaPrincipal()?.Zona?.Nombre,
            perfil.Cobertura.Where(c => c.Zona is not null).Select(c => c.Zona!.Nombre).ToArray(),
            perfil.PromedioVisible() ? perfil.PromedioCalificacion : null,   // RN-S5-11
            perfil.TotalResenas,
            perfil.TelefonoPublico,
            perfil.CorreoPublico,
            perfil.Whatsapp,
            perfil.SitioWeb,
            perfil.PublicadoEn);
    }
}
