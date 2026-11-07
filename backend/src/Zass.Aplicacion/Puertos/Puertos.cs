using Zass.Dominio.Catalogo;
using Zass.Dominio.Comun;
using Zass.Dominio.Identidad;

namespace Zass.Aplicacion.Puertos;

// Esta capa DECLARA lo que necesita; Zass.Infraestructura lo IMPLEMENTA.
// La flecha de dependencia apunta de infraestructura hacia aqui, nunca al revés.
// Consecuencia buscada: los casos de uso se prueban sin base de datos y sin red.

/// <summary>
/// El reloj, como puerto y no como DateTime.UtcNow. Permite probar "pasaron 24 h" sin
/// esperar 24 horas, y la mitad de las reglas de S4 dependen del tiempo.
/// </summary>
public interface IRelojSistema
{
    DateTime Ahora { get; }
}

/// <summary>RN-S1-03. La implementacion real es Argon2id: 19 MiB, 2 iteraciones, paralelismo 1 (DT-07).</summary>
public interface IHashContrasenas
{
    string Hash(string contrasena);
    bool Verifica(string contrasena, string hash);
}

/// <summary>
/// Secretos de un solo uso: verificacion de correo, recuperacion de contrasena, token de
/// refresco. Nunca se guarda el secreto, solo su hash, igual que TBL-04.
/// </summary>
public interface IGeneradorSecretos
{
    string NuevoToken();
    string HashToken(string token);
}

/// <summary>RN-S1-06. La direccion literal se guarda cifrada y no se indexa.</summary>
public interface ICifradoDeDatos
{
    byte[] Cifrar(string texto);
    string Descifrar(byte[] datos);
}

/// <summary>El buzon de avisos (TBL-27). Encolar no envia: el envio es trabajo de fondo.</summary>
public interface INotificaciones
{
    void Encolar(Notificacion aviso);
}

/// <summary>TBL-28. Solo insercion. Nunca contrasenas, tokens ni direcciones.</summary>
public interface IAuditor
{
    void Registrar(RegistroAuditoria entrada);
}

/// <summary>
/// Una transaccion por caso de uso. Quien orquesta decide cuando se confirma; los
/// repositorios solo acumulan cambios.
/// </summary>
public interface IUnidadDeTrabajo
{
    Task<int> GuardarAsync(CancellationToken ct = default);
}

public interface IRepositorioUsuarios
{
    Task<Usuario?> PorIdAsync(Guid id, CancellationToken ct = default);
    Task<Usuario?> PorCorreoAsync(string correo, CancellationToken ct = default);
    Task<Usuario?> PorTokenVerificacionAsync(string hashToken, CancellationToken ct = default);
    Task<Usuario?> PorTokenRecuperacionAsync(string hashToken, CancellationToken ct = default);
    Task<bool> ExisteCorreoAsync(string correo, CancellationToken ct = default);
    Task<Rol?> RolPorCodigoAsync(string codigo, CancellationToken ct = default);
    void Agregar(Usuario usuario);
}

public interface IRepositorioSesiones
{
    Task<Sesion?> PorHashTokenAsync(string hashToken, CancellationToken ct = default);
    Task<List<Sesion>> VigentesDeUsuarioAsync(Guid usuarioId, DateTime ahora, CancellationToken ct = default);
    void Agregar(Sesion sesion);
}

public interface IRepositorioPerfiles
{
    Task<PerfilPrestador?> PrestadorDeUsuarioAsync(Guid usuarioId, CancellationToken ct = default);
    Task<PerfilPrestador?> PrestadorPorIdAsync(Guid id, CancellationToken ct = default);
    Task<PerfilPrestador?> PrestadorPorSlugAsync(string slug, CancellationToken ct = default);
    Task<bool> ExisteSlugAsync(string slug, CancellationToken ct = default);
    Task<PerfilBuscador?> BuscadorDeUsuarioAsync(Guid usuarioId, CancellationToken ct = default);
    void Agregar(PerfilPrestador perfil);
    void Agregar(PerfilBuscador perfil);
}
