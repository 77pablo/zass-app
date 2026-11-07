using System.Security.Cryptography;
using System.Text;
using Zass.Aplicacion.Puertos;
using Zass.Dominio.Comun;
using Zass.Dominio.Identidad;

namespace Zass.Aplicacion.Pruebas.Dobles;

/// <summary>El reloj que las pruebas mueven a mano: "pasaron 24 h" sin esperar 24 horas.</summary>
public sealed class RelojFijo : IRelojSistema
{
    public DateTime Ahora { get; private set; }
    public RelojFijo(DateTime? inicio = null) =>
        Ahora = inicio ?? new DateTime(2026, 11, 6, 12, 0, 0, DateTimeKind.Utc);
    public void Avanzar(TimeSpan cuanto) => Ahora = Ahora.Add(cuanto);
}

/// <summary>
/// Hash de juguete, legible a proposito: la prueba puede comprobar que lo guardado NO es
/// la contrasena en claro sin depender de Argon2id, que se verifica en integracion.
/// </summary>
public sealed class HashDeJuguete : IHashContrasenas
{
    public string Hash(string contrasena) => "hash:" + contrasena.Length + ":" +
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(contrasena)));
    public bool Verifica(string contrasena, string hash) => Hash(contrasena) == hash;
}

/// <summary>Secretos deterministas, para poder afirmar sobre el token emitido.</summary>
public sealed class SecretosDeterministas : IGeneradorSecretos
{
    private int _n;
    public List<string> Emitidos { get; } = new();

    public string NuevoToken()
    {
        var t = $"token-{++_n:000}";
        Emitidos.Add(t);
        return t;
    }

    public string HashToken(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}

public sealed class ColaEnMemoria : INotificaciones
{
    public List<Notificacion> Encoladas { get; } = new();
    public void Encolar(Notificacion aviso) => Encoladas.Add(aviso);
}

public sealed class AuditoriaEnMemoria : IAuditor
{
    public List<RegistroAuditoria> Registros { get; } = new();
    public void Registrar(RegistroAuditoria entrada) => Registros.Add(entrada);
}

/// <summary>Cifrado reversible de prueba. No es seguro; solo prueba que no se guarda en claro.</summary>
public sealed class CifradoDeJuguete : ICifradoDeDatos
{
    public byte[] Cifrar(string texto) =>
        Encoding.UTF8.GetBytes(texto).Select(b => (byte)(b ^ 0x5A)).ToArray();
    public string Descifrar(byte[] datos) =>
        Encoding.UTF8.GetString(datos.Select(b => (byte)(b ^ 0x5A)).ToArray());
}
