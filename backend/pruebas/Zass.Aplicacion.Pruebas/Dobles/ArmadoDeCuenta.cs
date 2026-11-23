using Microsoft.Extensions.Logging;
using Zass.Aplicacion.Identidad;
using Zass.Aplicacion.Credenciales;
using Zass.Aplicacion.Notificaciones;
using Zass.Aplicacion.Puertos;
using Zass.Dominio.Comun;

namespace Zass.Aplicacion.Pruebas.Dobles;

/// <summary>Los navegadores con push en memoria, sobre la lista de <see cref="BaseEnMemoria"/>.</summary>
public sealed class RepositorioPushEnMemoria(BaseEnMemoria baseEnMemoria) : IRepositorioPush
{
    public Task RegistrarAsync(Guid usuarioId, string endpoint, string p256dh, string auth, DateTime ahora,
        CancellationToken ct = default)
    {
        baseEnMemoria.SuscripcionesPush.Add(new SuscripcionPush
        {
            UsuarioId = usuarioId, Endpoint = endpoint, ClaveP256dh = p256dh, ClaveAuth = auth,
        });
        return Task.CompletedTask;
    }

    public Task<bool> BorrarAsync(Guid usuarioId, string endpoint, CancellationToken ct = default) =>
        Task.FromResult(baseEnMemoria.SuscripcionesPush
            .RemoveAll(s => s.UsuarioId == usuarioId && s.Endpoint == endpoint) > 0);

    public Task BorrarTodasDeUsuarioAsync(Guid usuarioId, CancellationToken ct = default)
    {
        baseEnMemoria.SuscripcionesPush.RemoveAll(s => s.UsuarioId == usuarioId);
        return Task.CompletedTask;
    }

    public Task<int> EncolarGemelasPendientesAsync(Guid usuarioId, CancellationToken ct = default) =>
        Task.FromResult(0);
}

/// <summary>
/// Un almacen que recuerda lo que se le pidio borrar y CUANTAS VECES se habia guardado la base en ese
/// momento: asi se comprueba que los archivos se borran DESPUES de que la base confirma.
/// </summary>
public sealed class AlmacenQueRecuerda(BaseEnMemoria baseEnMemoria) : IAlmacenDeArchivos
{
    public List<(string Clave, int GuardadosAlBorrar)> Borrados { get; } = new();
    public HashSet<string> QueFallan { get; } = new();

    public Task<Resultado> GuardarAsync(string clave, Stream contenido, string tipoMime, CancellationToken ct = default) =>
        Task.FromResult(Resultado.Ok());
    public Task<ArchivoAbierto?> AbrirAsync(string clave, CancellationToken ct = default) =>
        Task.FromResult<ArchivoAbierto?>(null);

    public Task<Resultado> BorrarAsync(string clave, CancellationToken ct = default)
    {
        Borrados.Add((clave, baseEnMemoria.Guardados));
        return Task.FromResult(QueFallan.Contains(clave)
            ? Resultado.Falla(Fallas.AlmacenamientoNoDisponible, "no contesto")
            : Resultado.Ok());
    }

    public async IAsyncEnumerable<ArchivoListado> ListarAsync(string prefijo, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        await Task.CompletedTask;
        yield break;
    }
}

/// <summary>Un registro que guarda lo que se le escribe, para comprobar que un error quedo anotado.</summary>
public sealed class RegistroQueAnota<T> : ILogger<T>
{
    public List<(LogLevel Nivel, string Mensaje)> Entradas { get; } = new();
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public bool IsEnabled(LogLevel logLevel) => true;
    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
        Func<TState, Exception?, string> formatter) =>
        Entradas.Add((logLevel, formatter(state, exception)));
}

/// <summary>Arma <see cref="ServicioCuenta"/> con todo lo que necesita, sobre la base en memoria.</summary>
public static class ArmadoDeCuenta
{
    public static ServicioCuenta Cuenta(
        BaseEnMemoria baseEnMemoria, IHashContrasenas hash, IGeneradorSecretos secretos, IRelojSistema reloj,
        INotificaciones cola, IAuditor auditoria, ServicioSesion sesiones, IContrasenasFiltradas filtradas,
        IAlmacenDeArchivos? almacen = null, ILogger<ServicioBorradoDeDatos>? registro = null,
        ICifradoMfa? cifrado = null)
    {
        var usuarios = new RepositorioUsuariosEnMemoria(baseEnMemoria);
        var perfiles = new RepositorioPerfilesEnMemoria(baseEnMemoria);
        var mfa = new ServicioMfa(usuarios, baseEnMemoria, hash, secretos, cifrado ?? new CifradoDeJuguete(),
            reloj, auditoria, sesiones, cola);
        var borrado = new ServicioBorradoDeDatos(
            perfiles, new RepositorioPushEnMemoria(baseEnMemoria),
            new ServicioCredenciales(new RepositorioCredencialesEnMemoria(baseEnMemoria), baseEnMemoria, reloj, auditoria),
            almacen ?? new AlmacenQueRecuerda(baseEnMemoria), auditoria, reloj,
            registro ?? new RegistroQueAnota<ServicioBorradoDeDatos>());
        return new ServicioCuenta(
            usuarios, perfiles, baseEnMemoria, hash, secretos, reloj, cola, auditoria, sesiones, filtradas,
            mfa, borrado);
    }
}
