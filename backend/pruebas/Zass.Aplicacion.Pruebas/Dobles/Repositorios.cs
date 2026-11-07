using Zass.Aplicacion.Puertos;
using Zass.Dominio.Catalogo;
using Zass.Dominio.Identidad;

namespace Zass.Aplicacion.Pruebas.Dobles;

/// <summary>
/// La base de datos de las pruebas de aplicacion: listas en memoria. No pretende imitar a
/// PostgreSQL; las restricciones reales (R-1, R-2, unicidad) se comprueban en integracion.
/// </summary>
public sealed class BaseEnMemoria : IUnidadDeTrabajo
{
    public List<Usuario> Usuarios { get; } = new();
    public List<Rol> Roles { get; } = new()
    {
        new Rol { Id = 1, Codigo = "buscador",      Nombre = "Buscador",      EsInterno = false },
        new Rol { Id = 2, Codigo = "prestador",     Nombre = "Prestador",     EsInterno = false },
        new Rol { Id = 3, Codigo = "verificador",   Nombre = "Verificador",   EsInterno = true  },
        new Rol { Id = 4, Codigo = "moderador",     Nombre = "Moderador",     EsInterno = true  },
        new Rol { Id = 5, Codigo = "administrador", Nombre = "Administrador", EsInterno = true  },
    };
    public List<Sesion> Sesiones { get; } = new();
    public List<PerfilPrestador> PerfilesPrestador { get; } = new();
    public List<PerfilBuscador> PerfilesBuscador { get; } = new();
    public List<Rubro> Rubros { get; } = new();
    public List<Zona> Zonas { get; } = new();

    public int Guardados { get; private set; }
    public Task<int> GuardarAsync(CancellationToken ct = default)
    {
        Guardados++;
        return Task.FromResult(0);
    }
}

public sealed class RepositorioUsuariosEnMemoria : IRepositorioUsuarios
{
    private readonly BaseEnMemoria _base;
    public RepositorioUsuariosEnMemoria(BaseEnMemoria baseEnMemoria) => _base = baseEnMemoria;

    private static string Normalizar(string correo) => correo.Trim().ToLowerInvariant();

    public Task<Usuario?> PorIdAsync(Guid id, CancellationToken ct = default) =>
        Task.FromResult(_base.Usuarios.FirstOrDefault(u => u.Id == id));

    public Task<Usuario?> PorCorreoAsync(string correo, CancellationToken ct = default) =>
        Task.FromResult(_base.Usuarios.FirstOrDefault(
            u => Normalizar(u.Correo) == Normalizar(correo)));

    public Task<Usuario?> PorTokenVerificacionAsync(string hashToken, CancellationToken ct = default) =>
        Task.FromResult(_base.Usuarios.FirstOrDefault(u => u.TokenVerificacion == hashToken));

    public Task<Usuario?> PorTokenRecuperacionAsync(string hashToken, CancellationToken ct = default) =>
        Task.FromResult(_base.Usuarios.FirstOrDefault(u => u.TokenRecuperacion == hashToken));

    public Task<bool> ExisteCorreoAsync(string correo, CancellationToken ct = default) =>
        Task.FromResult(_base.Usuarios.Any(u => Normalizar(u.Correo) == Normalizar(correo)));

    public Task<Rol?> RolPorCodigoAsync(string codigo, CancellationToken ct = default) =>
        Task.FromResult(_base.Roles.FirstOrDefault(r => r.Codigo == codigo));

    public void Agregar(Usuario usuario) => _base.Usuarios.Add(usuario);
}

public sealed class RepositorioSesionesEnMemoria : IRepositorioSesiones
{
    private readonly BaseEnMemoria _base;
    public RepositorioSesionesEnMemoria(BaseEnMemoria baseEnMemoria) => _base = baseEnMemoria;

    public Task<Sesion?> PorHashTokenAsync(string hashToken, CancellationToken ct = default) =>
        Task.FromResult(_base.Sesiones.FirstOrDefault(s => s.HashToken == hashToken));

    public Task<List<Sesion>> VigentesDeUsuarioAsync(
        Guid usuarioId, DateTime ahora, CancellationToken ct = default) =>
        Task.FromResult(_base.Sesiones.Where(s => s.UsuarioId == usuarioId && s.EstaVigente(ahora)).ToList());

    public void Agregar(Sesion sesion) => _base.Sesiones.Add(sesion);
}

public sealed class RepositorioPerfilesEnMemoria : IRepositorioPerfiles
{
    private readonly BaseEnMemoria _base;
    public RepositorioPerfilesEnMemoria(BaseEnMemoria baseEnMemoria) => _base = baseEnMemoria;

    public Task<PerfilPrestador?> PrestadorDeUsuarioAsync(Guid usuarioId, CancellationToken ct = default) =>
        Task.FromResult(_base.PerfilesPrestador.FirstOrDefault(p => p.UsuarioId == usuarioId));

    public Task<PerfilPrestador?> PrestadorPorIdAsync(Guid id, CancellationToken ct = default) =>
        Task.FromResult(_base.PerfilesPrestador.FirstOrDefault(p => p.Id == id));

    public Task<PerfilPrestador?> PrestadorPorSlugAsync(string slug, CancellationToken ct = default) =>
        Task.FromResult(_base.PerfilesPrestador.FirstOrDefault(p => p.Slug == slug));

    public Task<bool> ExisteSlugAsync(string slug, CancellationToken ct = default) =>
        Task.FromResult(_base.PerfilesPrestador.Any(p => p.Slug == slug));

    public Task<PerfilBuscador?> BuscadorDeUsuarioAsync(Guid usuarioId, CancellationToken ct = default) =>
        Task.FromResult(_base.PerfilesBuscador.FirstOrDefault(p => p.UsuarioId == usuarioId));

    public void Agregar(PerfilPrestador perfil) => _base.PerfilesPrestador.Add(perfil);
    public void Agregar(PerfilBuscador perfil) => _base.PerfilesBuscador.Add(perfil);
}
