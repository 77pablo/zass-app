using System.Security.Cryptography;
using Zass.Infraestructura.Seguridad;

namespace Zass.Integracion.Pruebas;

/// <summary>
/// RN-S1-03 y DT-07. Las pruebas de aplicacion usan un hash de juguete; estas usan el
/// Argon2id de verdad, que es el que va a correr en produccion.
/// </summary>
public class Argon2idPruebas
{
    private readonly HashArgon2id _hash = new();

    [Fact]
    public void La_contrasena_correcta_se_verifica_y_la_incorrecta_no()
    {
        var hash = _hash.Hash("contrasena-larga-1");

        Assert.True(_hash.Verifica("contrasena-larga-1", hash));
        Assert.False(_hash.Verifica("contrasena-larga-2", hash));
        Assert.False(_hash.Verifica("", hash));
    }

    [Fact]
    public void El_hash_no_contiene_la_contrasena()
    {
        var hash = _hash.Hash("secreto-de-pablo");

        Assert.DoesNotContain("secreto-de-pablo", hash);
    }

    [Fact]
    public void La_misma_contrasena_hasheada_dos_veces_da_resultados_distintos()
    {
        // Si dieran lo mismo, la sal no estaria funcionando y dos personas con la misma
        // contrasena serian visibles como tales en un volcado de la tabla.
        var primero = _hash.Hash("contrasena-larga-1");
        var segundo = _hash.Hash("contrasena-larga-1");

        Assert.NotEqual(primero, segundo);
        Assert.True(_hash.Verifica("contrasena-larga-1", primero));
        Assert.True(_hash.Verifica("contrasena-larga-1", segundo));
    }

    [Fact]
    public void El_hash_declara_los_parametros_del_documento()
    {
        // 19 MiB, 2 iteraciones, paralelismo 1. Guardarlos junto al hash permite subirlos
        // manana sin invalidar las contrasenas de hoy.
        var hash = _hash.Hash("contrasena-larga-1");

        Assert.StartsWith("$argon2id$v=19$m=19456,t=2,p=1$", hash);
    }

    [Theory]
    [InlineData("")]
    [InlineData("no-es-un-hash")]
    [InlineData("$argon2id$v=19$m=19456,t=2,p=1$sal-que-no-es-base64$hash")]
    [InlineData("$bcrypt$v=19$m=19456,t=2,p=1$c2Fs$aGFzaA==")]
    public void Un_hash_corrupto_devuelve_falso_en_vez_de_reventar(string hash)
    {
        // Importa que no lance: un registro viejo o alterado tiene que producir un
        // "no entras", no un error 500 que delate el formato interno.
        Assert.False(_hash.Verifica("contrasena-larga-1", hash));
    }
}

/// <summary>RN-S1-06. La direccion literal, cifrada con AES-256-GCM.</summary>
public class CifradoPruebas
{
    private static readonly byte[] Clave = Enumerable.Range(0, 32).Select(i => (byte)i).ToArray();
    private readonly CifradoAesGcm _cifrado = new(Clave);

    [Fact]
    public void El_viaje_de_ida_y_vuelta_devuelve_el_mismo_texto()
    {
        var cifrado = _cifrado.Cifrar("Avenida Alemania 0231, Temuco");

        Assert.Equal("Avenida Alemania 0231, Temuco", _cifrado.Descifrar(cifrado));
    }

    [Fact]
    public void El_texto_cifrado_no_contiene_la_direccion()
    {
        var cifrado = _cifrado.Cifrar("Avenida Alemania 0231, Temuco");

        Assert.DoesNotContain("Alemania", System.Text.Encoding.UTF8.GetString(cifrado));
    }

    [Fact]
    public void La_misma_direccion_cifrada_dos_veces_da_bytes_distintos()
    {
        // El nonce es nuevo cada vez. Si no lo fuera, dos prestadores de la misma calle
        // tendrian el mismo texto cifrado y eso ya es una filtracion.
        var primero = _cifrado.Cifrar("Avenida Alemania 0231");
        var segundo = _cifrado.Cifrar("Avenida Alemania 0231");

        Assert.NotEqual(primero, segundo);
    }

    [Fact]
    public void Alterar_un_byte_hace_fallar_el_descifrado_en_vez_de_devolver_basura()
    {
        // Esto es lo que aporta GCM sobre CBC: el dato esta autenticado, no solo oculto.
        var cifrado = _cifrado.Cifrar("Avenida Alemania 0231");
        cifrado[^1] ^= 0xFF;

        Assert.Throws<AuthenticationTagMismatchException>(() => _cifrado.Descifrar(cifrado));
    }

    [Fact]
    public void Una_clave_distinta_no_puede_leer_lo_que_cifro_otra()
    {
        var cifrado = _cifrado.Cifrar("Avenida Alemania 0231");
        var otra = new CifradoAesGcm(Enumerable.Repeat((byte)7, 32).ToArray());

        Assert.Throws<AuthenticationTagMismatchException>(() => otra.Descifrar(cifrado));
    }

    [Fact]
    public void Un_dato_truncado_falla_con_un_mensaje_claro()
    {
        Assert.Throws<CryptographicException>(() => _cifrado.Descifrar(new byte[] { 1, 2, 3 }));
    }

    [Theory]
    [InlineData(16)]
    [InlineData(31)]
    [InlineData(64)]
    public void Una_clave_que_no_sea_de_32_bytes_se_rechaza_al_construir(int largo)
    {
        // Falla al construirse, no al cifrar: asi la aplicacion no arranca con una clave
        // mala en vez de descubrirlo con la primera direccion real.
        Assert.Throws<ArgumentException>(() => new CifradoAesGcm(new byte[largo]));
    }
}

/// <summary>Los tokens de un solo uso de CS-01 y CS-02, y el de refresco de TBL-04.</summary>
public class GeneradorSecretosPruebas
{
    private readonly GeneradorSecretos _secretos = new();

    [Fact]
    public void Dos_tokens_seguidos_nunca_son_iguales()
    {
        var tokens = Enumerable.Range(0, 200).Select(_ => _secretos.NuevoToken()).ToList();

        Assert.Equal(tokens.Count, tokens.Distinct().Count());
    }

    [Fact]
    public void El_token_entra_en_una_url_sin_escaparse()
    {
        // Viaja en un enlace de correo y, en S6, dentro de un QR impreso.
        var token = _secretos.NuevoToken();

        Assert.Equal(token, Uri.EscapeDataString(token));
        Assert.DoesNotContain('+', token);
        Assert.DoesNotContain('/', token);
        Assert.DoesNotContain('=', token);
    }

    [Fact]
    public void El_hash_del_token_es_estable_y_no_contiene_el_token()
    {
        var token = _secretos.NuevoToken();

        Assert.Equal(_secretos.HashToken(token), _secretos.HashToken(token));
        Assert.DoesNotContain(token, _secretos.HashToken(token));
        Assert.NotEqual(_secretos.HashToken(token), _secretos.HashToken(_secretos.NuevoToken()));
    }
}
