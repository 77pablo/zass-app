using Microsoft.EntityFrameworkCore;
using Zass.Aplicacion.Puertos;
using Zass.Dominio.Comun;
using Zass.Dominio.Identidad;

namespace Zass.Infraestructura.Datos.Repositorios;

/// <summary>
/// Una transaccion por caso de uso. El contexto acumula los cambios y aqui se confirman.
/// </summary>
public sealed class UnidadDeTrabajoEfCore : IUnidadDeTrabajo
{
    private readonly ZassDbContext _ctx;
    public UnidadDeTrabajoEfCore(ZassDbContext ctx) => _ctx = ctx;

    public Task<int> GuardarAsync(CancellationToken ct = default) => _ctx.SaveChangesAsync(ct);

    /// <summary>
    /// La transaccion explicita que CC-01 necesita. Si ya hay una abierta se reutiliza, sin
    /// confirmarla ni deshacerla: quien la abrio decide. Eso es lo que permite que las
    /// pruebas de integracion envuelvan todo en una transaccion que luego se deshace.
    /// </summary>
    public async Task<T> EnTransaccionAsync<T>(
        Func<CancellationToken, Task<T>> cuerpo, CancellationToken ct = default)
    {
        if (_ctx.Database.CurrentTransaction is not null) return await cuerpo(ct);

        await using var tx = await _ctx.Database.BeginTransactionAsync(ct);
        var r = await cuerpo(ct);
        await tx.CommitAsync(ct);
        return r;
    }
}

public sealed class RepositorioUsuariosEfCore : IRepositorioUsuarios
{
    private readonly ZassDbContext _ctx;
    public RepositorioUsuariosEfCore(ZassDbContext ctx) => _ctx = ctx;

    /// <summary>
    /// Los roles van siempre incluidos: Usuario.TieneRol los recorre, y sin ellos
    /// devolveria falso en silencio en vez de fallar. Un bug de autorizacion que no
    /// avisa es peor que una consulta de mas.
    /// </summary>
    private IQueryable<Usuario> ConRoles() =>
        _ctx.Usuarios.Include(u => u.Roles).ThenInclude(r => r.Rol);

    public Task<Usuario?> PorIdAsync(Guid id, CancellationToken ct = default) =>
        ConRoles().FirstOrDefaultAsync(u => u.Id == id, ct);

    /// <summary>
    /// La columna es `citext`, asi que la igualdad ya es insensible a mayusculas en la
    /// base. Se normaliza igual antes de llegar aqui (RN-S1-01), para que el dato
    /// guardado y el consultado coincidan sin depender del tipo de la columna.
    /// </summary>
    public Task<Usuario?> PorCorreoAsync(string correo, CancellationToken ct = default) =>
        ConRoles().FirstOrDefaultAsync(u => u.Correo == correo, ct);

    public Task<Usuario?> PorTokenVerificacionAsync(string hashToken, CancellationToken ct = default) =>
        ConRoles().FirstOrDefaultAsync(u => u.TokenVerificacion == hashToken, ct);

    public Task<Usuario?> PorTokenRecuperacionAsync(string hashToken, CancellationToken ct = default) =>
        ConRoles().FirstOrDefaultAsync(u => u.TokenRecuperacion == hashToken, ct);

    public Task<bool> ExisteCorreoAsync(string correo, CancellationToken ct = default) =>
        _ctx.Usuarios.AnyAsync(u => u.Correo == correo, ct);

    public Task<Rol?> RolPorCodigoAsync(string codigo, CancellationToken ct = default) =>
        _ctx.Roles.FirstOrDefaultAsync(r => r.Codigo == codigo, ct);

    public void Agregar(Usuario usuario) => _ctx.Usuarios.Add(usuario);
}

public sealed class RepositorioSesionesEfCore : IRepositorioSesiones
{
    private readonly ZassDbContext _ctx;
    public RepositorioSesionesEfCore(ZassDbContext ctx) => _ctx = ctx;

    public Task<Sesion?> PorHashTokenAsync(string hashToken, CancellationToken ct = default) =>
        _ctx.Sesiones.FirstOrDefaultAsync(s => s.HashToken == hashToken, ct);

    /// <summary>
    /// El filtro de vigencia se escribe aqui en vez de llamar a Sesion.EstaVigente,
    /// porque ese metodo no se traduce a SQL y traeria la tabla entera a memoria.
    /// Si cambia la regla, cambian los dos: por eso la prueba de integracion compara
    /// este resultado con el del metodo del dominio.
    /// </summary>
    public Task<List<Sesion>> VigentesDeUsuarioAsync(
        Guid usuarioId, DateTime ahora, CancellationToken ct = default) =>
        _ctx.Sesiones
            .Where(s => s.UsuarioId == usuarioId && s.RevocadaEn == null && s.ExpiraEn > ahora)
            .ToListAsync(ct);

    public void Agregar(Sesion sesion) => _ctx.Sesiones.Add(sesion);
}

public sealed class RepositorioPerfilesEfCore : IRepositorioPerfiles
{
    private readonly ZassDbContext _ctx;
    public RepositorioPerfilesEfCore(ZassDbContext ctx) => _ctx = ctx;

    /// <summary>
    /// Servicios y cobertura vienen incluidos porque EstaCompleto() los necesita para
    /// decidir RN-S1-05. Sin ellos, publicar fallaria diciendo que falta un servicio
    /// que si existe.
    /// </summary>
    private IQueryable<PerfilPrestador> Completo() =>
        _ctx.PerfilesPrestador
            .Include(p => p.Servicios)
            .Include(p => p.Cobertura).ThenInclude(c => c.Zona);

    public Task<PerfilPrestador?> PrestadorDeUsuarioAsync(Guid usuarioId, CancellationToken ct = default) =>
        Completo().FirstOrDefaultAsync(p => p.UsuarioId == usuarioId, ct);

    public Task<PerfilPrestador?> PrestadorPorIdAsync(Guid id, CancellationToken ct = default) =>
        Completo().FirstOrDefaultAsync(p => p.Id == id, ct);

    public Task<PerfilPrestador?> PrestadorPorSlugAsync(string slug, CancellationToken ct = default) =>
        Completo().FirstOrDefaultAsync(p => p.Slug == slug, ct);

    public Task<bool> ExisteSlugAsync(string slug, CancellationToken ct = default) =>
        _ctx.PerfilesPrestador.AnyAsync(p => p.Slug == slug, ct);

    public Task<PerfilBuscador?> BuscadorDeUsuarioAsync(Guid usuarioId, CancellationToken ct = default) =>
        _ctx.PerfilesBuscador.FirstOrDefaultAsync(p => p.UsuarioId == usuarioId, ct);

    public void Agregar(PerfilPrestador perfil) => _ctx.PerfilesPrestador.Add(perfil);
    public void Agregar(PerfilBuscador perfil) => _ctx.PerfilesBuscador.Add(perfil);
}

/// <summary>
/// TBL-27. Encolar no envia: deja la fila programada y el trabajo de fondo la despacha.
/// Asi un fallo del correo no voltea el caso de uso que lo origino.
/// </summary>
public sealed class ColaNotificacionesEfCore : INotificaciones
{
    private readonly ZassDbContext _ctx;
    public ColaNotificacionesEfCore(ZassDbContext ctx) => _ctx = ctx;

    public void Encolar(Notificacion aviso) => _ctx.Notificaciones.Add(aviso);
}

/// <summary>TBL-28. Solo insercion.</summary>
public sealed class AuditorEfCore : IAuditor
{
    private readonly ZassDbContext _ctx;
    public AuditorEfCore(ZassDbContext ctx) => _ctx = ctx;

    public void Registrar(RegistroAuditoria entrada) => _ctx.Auditoria.Add(entrada);
}
