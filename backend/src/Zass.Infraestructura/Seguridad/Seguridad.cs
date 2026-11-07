using System.Security.Cryptography;
using System.Text;
using Konscious.Security.Cryptography;
using Zass.Aplicacion.Puertos;

namespace Zass.Infraestructura.Seguridad;

/// <summary>Implementacion de IRelojSistema. Siempre UTC: la zona se aplica al mostrar.</summary>
public sealed class RelojUtc : IRelojSistema
{
    public DateTime Ahora => DateTime.UtcNow;
}

/// <summary>
/// RN-S1-03 y DT-07. Argon2id con los parametros del documento: 19 MiB de memoria,
/// 2 iteraciones, paralelismo 1.
///
/// El formato guardado es `$argon2id$v=19$m=19456,t=2,p=1$sal$hash`, con la sal dentro.
/// Guardar los parametros junto al hash permite subirlos manana sin invalidar las
/// contrasenas de hoy: cada hash dice con que se calculo.
/// </summary>
public sealed class HashArgon2id : IHashContrasenas
{
    private const int MemoriaKiB = 19 * 1024;
    private const int Iteraciones = 2;
    private const int Paralelismo = 1;
    private const int LargoSal = 16;
    private const int LargoHash = 32;
    private const string Etiqueta = "argon2id";

    public string Hash(string contrasena)
    {
        var sal = RandomNumberGenerator.GetBytes(LargoSal);
        var hash = Derivar(contrasena, sal, MemoriaKiB, Iteraciones, Paralelismo);

        return string.Join('$', "",
            Etiqueta,
            "v=19",
            $"m={MemoriaKiB},t={Iteraciones},p={Paralelismo}",
            Convert.ToBase64String(sal),
            Convert.ToBase64String(hash));
    }

    public bool Verifica(string contrasena, string hash)
    {
        // Argon2 se niega a trabajar con una entrada vacia y lanza. Aqui una contrasena
        // vacia no es un error del programa: es un intento de entrar que no acierta, y
        // tiene que responder "no" como cualquier otro. Sin esta guarda, mandar la
        // contrasena vacia devolvia un 500 en vez de un 401.
        if (string.IsNullOrEmpty(contrasena) || string.IsNullOrEmpty(hash)) return false;

        var partes = hash.Split('$');
        if (partes.Length != 6 || partes[1] != Etiqueta) return false;

        var parametros = partes[3].Split(',')
            .Select(p => p.Split('='))
            .Where(p => p.Length == 2)
            .ToDictionary(p => p[0], p => p[1]);

        if (!Entero(parametros, "m", out var memoria)
            || !Entero(parametros, "t", out var iteraciones)
            || !Entero(parametros, "p", out var paralelismo))
            return false;

        byte[] sal, esperado;
        try
        {
            sal = Convert.FromBase64String(partes[4]);
            esperado = Convert.FromBase64String(partes[5]);
        }
        catch (FormatException)
        {
            return false;
        }

        var calculado = Derivar(contrasena, sal, memoria, iteraciones, paralelismo, esperado.Length);

        // Comparacion en tiempo constante: comparar con == filtraria cuantos bytes
        // coinciden a traves del tiempo de respuesta.
        return CryptographicOperations.FixedTimeEquals(calculado, esperado);
    }

    private static bool Entero(IDictionary<string, string> origen, string clave, out int valor)
    {
        valor = 0;
        return origen.TryGetValue(clave, out var texto) && int.TryParse(texto, out valor);
    }

    private static byte[] Derivar(
        string contrasena, byte[] sal, int memoriaKiB, int iteraciones, int paralelismo,
        int largo = LargoHash)
    {
        using var argon = new Argon2id(Encoding.UTF8.GetBytes(contrasena))
        {
            Salt = sal,
            MemorySize = memoriaKiB,
            Iterations = iteraciones,
            DegreeOfParallelism = paralelismo,
        };
        return argon.GetBytes(largo);
    }
}

/// <summary>
/// Tokens de un solo uso. El token viaja por correo; en la base solo queda su SHA-256,
/// igual que el token de refresco de TBL-04.
///
/// SHA-256 sin sal es correcto aqui y no lo seria para una contrasena: el token tiene
/// 256 bits de azar, asi que no hay diccionario que probar.
/// </summary>
public sealed class GeneradorSecretos : IGeneradorSecretos
{
    private const int BytesDeToken = 32;

    public string NuevoToken()
    {
        // Base64 de URL: entra en un enlace y en un QR sin escaparse.
        var bytes = RandomNumberGenerator.GetBytes(BytesDeToken);
        return Convert.ToBase64String(bytes)
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');
    }

    public string HashToken(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}

/// <summary>
/// RN-S1-06. La direccion literal se guarda cifrada con AES-256-GCM, que ademas autentica:
/// si alguien altera un byte en la base, el descifrado falla en vez de devolver basura.
///
/// El primer byte es la version de la clave, para poder rotarla sin volver a cifrar todo
/// de golpe (la clave de direcciones rota una vez al ano, segun seguridad.md).
/// </summary>
public sealed class CifradoAesGcm : ICifradoDeDatos
{
    private const int LargoNonce = 12;
    private const int LargoEtiqueta = 16;
    private const byte VersionDeClave = 1;

    private readonly byte[] _clave;

    public CifradoAesGcm(byte[] clave)
    {
        if (clave.Length != 32)
            throw new ArgumentException(
                "La clave de cifrado de direcciones tiene que ser de 32 bytes (AES-256).",
                nameof(clave));
        _clave = clave;
    }

    public byte[] Cifrar(string texto)
    {
        var claro = Encoding.UTF8.GetBytes(texto);
        var nonce = RandomNumberGenerator.GetBytes(LargoNonce);
        var cifrado = new byte[claro.Length];
        var etiqueta = new byte[LargoEtiqueta];

        using var aes = new AesGcm(_clave, LargoEtiqueta);
        aes.Encrypt(nonce, claro, cifrado, etiqueta);

        var salida = new byte[1 + LargoNonce + LargoEtiqueta + cifrado.Length];
        salida[0] = VersionDeClave;
        nonce.CopyTo(salida, 1);
        etiqueta.CopyTo(salida, 1 + LargoNonce);
        cifrado.CopyTo(salida, 1 + LargoNonce + LargoEtiqueta);
        return salida;
    }

    public string Descifrar(byte[] datos)
    {
        if (datos.Length < 1 + LargoNonce + LargoEtiqueta)
            throw new CryptographicException("El dato cifrado está truncado.");
        if (datos[0] != VersionDeClave)
            throw new CryptographicException(
                $"Ese dato se cifró con la versión de clave {datos[0]} y aquí está la {VersionDeClave}.");

        var nonce = datos.AsSpan(1, LargoNonce);
        var etiqueta = datos.AsSpan(1 + LargoNonce, LargoEtiqueta);
        var cifrado = datos.AsSpan(1 + LargoNonce + LargoEtiqueta);
        var claro = new byte[cifrado.Length];

        using var aes = new AesGcm(_clave, LargoEtiqueta);
        aes.Decrypt(nonce, cifrado, etiqueta, claro);
        return Encoding.UTF8.GetString(claro);
    }
}
