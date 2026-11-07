using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Zass.Aplicacion.Identidad;
using Zass.Aplicacion.Puertos;
using Zass.Infraestructura.Configuracion;

namespace Zass.Integracion.Pruebas;

/// <summary>
/// El contenedor. Un registro que falta no se nota al compilar: revienta la primera vez
/// que alguien pide el endpoint, en ejecucion. Estas pruebas lo descubren antes.
/// </summary>
public class ContenedorPruebas
{
    private static readonly string ClaveValida =
        Convert.ToBase64String(Enumerable.Range(0, 32).Select(i => (byte)i).ToArray());

    private static ServiceProvider Construir(
        string? clave = null, string? cadena = null, bool validarAlResolver = true)
    {
        var configuracion = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Zass:CadenaConexion"] = cadena ?? Conexion.Cadena,
                ["Zass:ClaveDireccionesBase64"] = clave ?? ClaveValida,
            })
            .Build();

        return new ServiceCollection()
            .AgregarZass(configuracion)
            .BuildServiceProvider(new ServiceProviderOptions
            {
                ValidateOnBuild = validarAlResolver,
                ValidateScopes = true,
            });
    }

    [Theory]
    [InlineData(typeof(IServicioIdentidad))]
    [InlineData(typeof(ServicioRegistro))]
    [InlineData(typeof(ServicioSesion))]
    [InlineData(typeof(ServicioCuenta))]
    [InlineData(typeof(ServicioPerfiles))]
    [InlineData(typeof(IRepositorioUsuarios))]
    [InlineData(typeof(IRepositorioSesiones))]
    [InlineData(typeof(IRepositorioPerfiles))]
    [InlineData(typeof(IUnidadDeTrabajo))]
    [InlineData(typeof(INotificaciones))]
    [InlineData(typeof(IAuditor))]
    [InlineData(typeof(IRelojSistema))]
    [InlineData(typeof(IHashContrasenas))]
    [InlineData(typeof(IGeneradorSecretos))]
    [InlineData(typeof(ICifradoDeDatos))]
    public void El_contenedor_resuelve_cada_pieza_de_MOD_01(Type servicio)
    {
        using var proveedor = Construir();
        using var ambito = proveedor.CreateScope();

        Assert.NotNull(ambito.ServiceProvider.GetRequiredService(servicio));
    }

    [Fact]
    public void Los_servicios_de_aplicacion_y_sus_repositorios_comparten_la_misma_transaccion()
    {
        // Si el repositorio fuera singleton y la unidad de trabajo scoped, cada uno
        // tendria su contexto y guardar no escribiria lo que el otro cambio. Esto
        // comprueba que ambos salen del mismo ambito.
        using var proveedor = Construir();
        using var ambito = proveedor.CreateScope();

        var unaVez = ambito.ServiceProvider.GetRequiredService<IRepositorioUsuarios>();
        var otraVez = ambito.ServiceProvider.GetRequiredService<IRepositorioUsuarios>();

        Assert.Same(unaVez, otraVez);
    }

    [Fact]
    public void Sin_la_clave_de_cifrado_la_configuracion_se_rechaza()
    {
        // seguridad.md, seccion 2: la aplicacion no arranca si falta una variable.
        using var proveedor = Construir(clave: "");

        var error = Assert.Throws<OptionsValidationException>(
            () => proveedor.GetRequiredService<IOptions<OpcionesZass>>().Value);

        Assert.Contains("clave de cifrado", string.Join(" ", error.Failures));
    }

    [Fact]
    public void Sin_la_cadena_de_conexion_la_configuracion_se_rechaza()
    {
        using var proveedor = Construir(cadena: "", validarAlResolver: false);

        var error = Assert.Throws<OptionsValidationException>(
            () => proveedor.GetRequiredService<IOptions<OpcionesZass>>().Value);

        Assert.Contains("cadena de conexion", string.Join(" ", error.Failures));
    }

    [Theory]
    [InlineData("no-es-base64-!!")]
    [InlineData("YWJj")]
    public void Una_clave_de_cifrado_mal_formada_hace_fallar_el_arranque(string clave)
    {
        // El fallo tiene que ocurrir al construir el servicio, no la primera vez que un
        // prestador guarda su direccion real.
        using var proveedor = Construir(clave: clave, validarAlResolver: false);
        using var ambito = proveedor.CreateScope();

        Assert.Throws<OptionsValidationException>(
            () => ambito.ServiceProvider.GetRequiredService<ICifradoDeDatos>());
    }
}
