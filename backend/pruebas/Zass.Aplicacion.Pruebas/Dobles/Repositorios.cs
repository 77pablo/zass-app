using Zass.Aplicacion.Puertos;
using Zass.Dominio.Agenda;
using Zass.Dominio.Catalogo;
using Zass.Dominio.Credenciales;
using Zass.Dominio.Reputacion;
using Zass.Dominio.Identidad;
using Zass.Dominio.Suscripciones;

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
    public List<Servicio> Servicios { get; } = new();
    public List<PrestadorServicio> PrestadorServicios { get; } = new();
    public List<Zona> Zonas { get; } = new();
    public List<CoberturaPrestador> Coberturas { get; } = new();
    public List<Plan> Planes { get; } = new();
    public List<Suscripcion> Suscripciones { get; } = new();
    public List<Pago> Pagos { get; } = new();
    /// <summary>Los 50 cupos fundador, todos libres al empezar (como los siembra la migracion).</summary>
    public List<CupoFundador> CuposFundador { get; } =
        Enumerable.Range(1, 50).Select(n => new CupoFundador { Numero = (short)n }).ToList();
    /// <summary>TBL-36. Los cobros empezados. Vacia al empezar, como la tabla.</summary>
    public List<IntentoDePago> IntentosDePago { get; } = new();
    /// <summary>TBL-34 y su canje. Ningun cupon existe hasta que una prueba lo crea.</summary>
    public List<Cupon> Cupones { get; } = new();
    public List<CanjeDeCupon> Canjes { get; } = new();
    public List<Credencial> Credenciales { get; } = new();
    public List<EntidadCertificadora> Entidades { get; } = new();
    public List<Disponibilidad> Disponibilidades { get; } = new();
    public List<BloqueHorario> Bloques { get; } = new();
    public List<BloqueoAgenda> BloqueosAgenda { get; } = new();
    public List<Cita> Citas { get; } = new();
    public List<CambioCita> CambiosCita { get; } = new();
    public List<Contacto> Contactos { get; } = new();

    public int Guardados { get; private set; }
    public Task<int> GuardarAsync(CancellationToken ct = default)
    {
        Guardados++;
        return Task.FromResult(0);
    }

    /// <summary>Cuantas transacciones se abrieron. Hay pruebas que comprueban que SI hubo una.</summary>
    public int Transacciones { get; private set; }

    /// <summary>
    /// Una lista en memoria no tiene transacciones: esto solo lleva la cuenta y deja pasar
    /// el cuerpo. Que la transaccion REAL exista y deshaga de verdad lo prueba integracion,
    /// que es el unico sitio donde esa pregunta tiene sentido.
    /// </summary>
    public async Task<T> EnTransaccionAsync<T>(
        Func<CancellationToken, Task<T>> cuerpo, CancellationToken ct = default)
    {
        Transacciones++;
        return await cuerpo(ct);
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
