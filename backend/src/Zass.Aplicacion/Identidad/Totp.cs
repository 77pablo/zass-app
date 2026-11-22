using System.Security.Cryptography;
using System.Text;

namespace Zass.Aplicacion.Identidad;

/// <summary>
/// TOTP de RFC 6238 (HMAC-SHA1, 6 digitos, pasos de 30 s), el que entienden Google
/// Authenticator, Microsoft Authenticator, 1Password y compania. Implementacion propia y corta
/// a proposito: son veinte lineas de RFC 4226 y no justifican un paquete mas en la cadena.
///
/// <para>
/// Se acepta el paso actual y uno a cada lado (±30 s), para perdonar un reloj de telefono un
/// poco corrido. Que un mismo codigo no sirva dos veces NO se decide aqui: esta clase solo dice
/// a que paso corresponde el codigo, y quien lo gasta (de forma atomica, en la base) es
/// <see cref="ServicioMfa"/>.
/// </para>
/// </summary>
public static class Totp
{
    public const int Digitos = 6;
    public const int SegundosPorPaso = 30;
    public const int Ventana = 1;
    public const int BytesDeSecreto = 20;   // 160 bits, lo que recomienda RFC 4226

    private const string AlfabetoBase32 = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

    public static string NuevoSecreto() => Base32(RandomNumberGenerator.GetBytes(BytesDeSecreto));

    public static long PasoDe(DateTime ahora) =>
        new DateTimeOffset(DateTime.SpecifyKind(ahora, DateTimeKind.Utc)).ToUnixTimeSeconds() / SegundosPorPaso;

    /// <summary>El codigo de ese paso. Lo usan las pruebas y la propia verificacion.</summary>
    public static string Codigo(string secretoBase32, long paso)
    {
        var clave = DesdeBase32(secretoBase32);
        Span<byte> contador = stackalloc byte[8];
        for (var i = 7; i >= 0; i--)
        {
            contador[i] = (byte)(paso & 0xFF);
            paso >>= 8;
        }

        var hash = HMACSHA1.HashData(clave, contador);
        var desplazamiento = hash[^1] & 0x0F;
        var binario = ((hash[desplazamiento] & 0x7F) << 24)
            | (hash[desplazamiento + 1] << 16)
            | (hash[desplazamiento + 2] << 8)
            | hash[desplazamiento + 3];

        return (binario % 1_000_000).ToString("D6");
    }

    /// <summary>
    /// El paso al que corresponde <paramref name="codigo"/> dentro de la ventana, o nulo si no
    /// corresponde a ninguno. Compara en tiempo constante.
    /// </summary>
    public static long? PasoDelCodigo(string secretoBase32, string codigo, DateTime ahora)
    {
        if (!EsCodigoTotp(codigo)) return null;

        var actual = PasoDe(ahora);
        long? encontrado = null;
        var esperado = Encoding.ASCII.GetBytes(codigo);
        for (var d = -Ventana; d <= Ventana; d++)
        {
            var candidato = Encoding.ASCII.GetBytes(Codigo(secretoBase32, actual + d));
            if (CryptographicOperations.FixedTimeEquals(candidato, esperado))
                encontrado ??= actual + d;
        }
        return encontrado;
    }

    public static bool EsCodigoTotp(string? codigo) =>
        codigo is { Length: Digitos } && codigo.All(char.IsAsciiDigit);

    /// <summary>
    /// La direccion otpauth:// que se convierte en QR. El emisor va en la etiqueta y en el
    /// parametro, como pide la especificacion de Google, para que todas las apps lo muestren.
    /// </summary>
    public static string Uri(string emisor, string cuenta, string secretoBase32)
    {
        var e = System.Uri.EscapeDataString(emisor);
        var c = System.Uri.EscapeDataString(cuenta);
        return $"otpauth://totp/{e}:{c}?secret={secretoBase32}&issuer={e}"
            + $"&algorithm=SHA1&digits={Digitos}&period={SegundosPorPaso}";
    }

    public static string Base32(ReadOnlySpan<byte> datos)
    {
        var salida = new StringBuilder((datos.Length * 8 + 4) / 5);
        int buffer = 0, bits = 0;
        foreach (var b in datos)
        {
            buffer = (buffer << 8) | b;
            bits += 8;
            while (bits >= 5)
            {
                salida.Append(AlfabetoBase32[(buffer >> (bits - 5)) & 31]);
                bits -= 5;
            }
        }
        if (bits > 0) salida.Append(AlfabetoBase32[(buffer << (5 - bits)) & 31]);
        return salida.ToString();
    }

    public static byte[] DesdeBase32(string texto)
    {
        var limpio = texto.Trim().TrimEnd('=').ToUpperInvariant();
        var salida = new List<byte>(limpio.Length * 5 / 8);
        int buffer = 0, bits = 0;
        foreach (var ch in limpio)
        {
            var valor = AlfabetoBase32.IndexOf(ch);
            if (valor < 0) throw new FormatException("El secreto TOTP no es Base32.");
            buffer = (buffer << 5) | valor;
            bits += 5;
            if (bits >= 8)
            {
                salida.Add((byte)((buffer >> (bits - 8)) & 0xFF));
                bits -= 8;
            }
        }
        return salida.ToArray();
    }
}
