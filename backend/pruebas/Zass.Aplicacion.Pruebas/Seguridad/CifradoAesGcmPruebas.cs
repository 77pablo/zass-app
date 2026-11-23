using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Zass.Aplicacion.Puertos;
using Zass.Infraestructura.Configuracion;
using Zass.Infraestructura.Seguridad;

namespace Zass.Aplicacion.Pruebas.Seguridad;

/// <summary>
/// El cifrado AES-GCM con datos asociados (AAD) y la clave propia del secreto del MFA.
/// </summary>
public class CifradoAesGcmPruebas
{
    private static byte[] Clave(int desde) => Enumerable.Range(desde, 32).Select(i => (byte)i).ToArray();

    private readonly CifradoAesGcm _cifrado = new(Clave(0));

    [Fact]
    public void Sin_datos_asociados_la_ida_y_vuelta_sigue_andando_y_es_lo_mismo_que_con_vacio()
    {
        var bytes = _cifrado.Cifrar("Avenida Alemania 0231");
        Assert.Equal("Avenida Alemania 0231", _cifrado.Descifrar(bytes));
        Assert.Equal("Avenida Alemania 0231", _cifrado.Descifrar(bytes, ReadOnlySpan<byte>.Empty));

        var conVacio = _cifrado.Cifrar("Avenida Alemania 0231", ReadOnlySpan<byte>.Empty);
        Assert.Equal("Avenida Alemania 0231", _cifrado.Descifrar(conVacio));
    }

    [Fact]
    public void Lo_cifrado_antes_del_cambio_se_sigue_descifrando()
    {
        // Se arma el dato con el algoritmo de ANTES (Encrypt sin AAD, mismo formato) a mano.
        var claro = Encoding.UTF8.GetBytes("Pasaje Los Robles 12");
        var nonce = RandomNumberGenerator.GetBytes(12);
        var cifrado = new byte[claro.Length];
        var etiqueta = new byte[16];
        using (var aes = new AesGcm(Clave(0), 16)) aes.Encrypt(nonce, claro, cifrado, etiqueta);
        var viejo = new byte[] { 1 }.Concat(nonce).Concat(etiqueta).Concat(cifrado).ToArray();

        Assert.Equal("Pasaje Los Robles 12", _cifrado.Descifrar(viejo));
    }

    [Fact]
    public void Con_datos_asociados_otros_datos_no_descifran()
    {
        var ana = Encoding.UTF8.GetBytes("mfa-totp:ana");
        var beto = Encoding.UTF8.GetBytes("mfa-totp:beto");
        var bytes = _cifrado.Cifrar("SECRETO", ana);

        Assert.Equal("SECRETO", _cifrado.Descifrar(bytes, ana));
        Assert.ThrowsAny<CryptographicException>(() => _cifrado.Descifrar(bytes, beto));
        Assert.ThrowsAny<CryptographicException>(() => _cifrado.Descifrar(bytes));
        // Y al reves: lo cifrado sin datos asociados no se abre dando unos.
        Assert.ThrowsAny<CryptographicException>(() => _cifrado.Descifrar(_cifrado.Cifrar("X"), ana));
    }

    // ------------------------------------------------------ Clave propia del MFA

    private static ServiceProvider Proveedor(string? claveMfa)
    {
        var ajustes = new Dictionary<string, string?>
        {
            ["Zass:CadenaConexion"] = "Host=127.0.0.1;Database=zass;Username=u;Password=p",
            ["Zass:ClaveDireccionesBase64"] = Convert.ToBase64String(Clave(0)),
            ["Zass:SecretoAvisoPagos"] = "secreto-de-prueba-del-aviso-0123456789ab",
        };
        if (claveMfa is not null) ajustes["Zass:Mfa:ClaveSecreto"] = claveMfa;
        var configuracion = new ConfigurationBuilder().AddInMemoryCollection(ajustes).Build();
        return new ServiceCollection()
            .AddSingleton<IConfiguration>(configuracion)
            .AddLogging()
            .AddSingleton<IHostEnvironment>(new EntornoDePrueba())
            .AgregarZass(configuracion)
            .BuildServiceProvider();
    }

    private sealed class EntornoDePrueba : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Development";
        public string ApplicationName { get; set; } = "Pruebas";
        public string ContentRootPath { get; set; } = ".";
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = null!;
    }

    [Fact]
    public void Con_clave_propia_el_mfa_cifra_con_ella_y_las_direcciones_con_la_suya()
    {
        using var sp = Proveedor(Convert.ToBase64String(Clave(100)));
        var mfa = sp.GetRequiredService<ICifradoMfa>();
        var direcciones = sp.GetRequiredService<ICifradoDeDatos>();

        var delMfa = mfa.Cifrar("SECRETO");
        var deDirecciones = direcciones.Cifrar("Calle 1");

        Assert.Equal("SECRETO", new CifradoAesGcm(Clave(100)).Descifrar(delMfa));
        Assert.Equal("Calle 1", new CifradoAesGcm(Clave(0)).Descifrar(deDirecciones));
        Assert.ThrowsAny<CryptographicException>(() => direcciones.Descifrar(delMfa));
        Assert.ThrowsAny<CryptographicException>(() => mfa.Descifrar(deDirecciones));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Sin_clave_propia_el_mfa_usa_la_de_direcciones_y_todo_anda(string? claveMfa)
    {
        using var sp = Proveedor(claveMfa);
        var mfa = sp.GetRequiredService<ICifradoMfa>();
        var direcciones = sp.GetRequiredService<ICifradoDeDatos>();

        Assert.Equal("SECRETO", new CifradoAesGcm(Clave(0)).Descifrar(mfa.Cifrar("SECRETO")));
        Assert.Equal("SECRETO", direcciones.Descifrar(mfa.Cifrar("SECRETO")));
        Assert.Equal("Calle 1", mfa.Descifrar(direcciones.Cifrar("Calle 1")));
    }

    [Theory]
    [InlineData("no-es-base64-!!")]
    [InlineData("YWJj")]
    public void Una_clave_propia_mal_formada_se_rechaza_en_vez_de_caer_en_silencio_a_la_de_direcciones(string mala)
    {
        using var sp = Proveedor(mala);
        Assert.ThrowsAny<Exception>(() => sp.GetRequiredService<ICifradoMfa>());
    }
}
