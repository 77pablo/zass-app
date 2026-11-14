using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.Internal;
using Zass.Aplicacion.Puertos;
using Zass.Infraestructura.Configuracion;
using Zass.Infraestructura.Pagos;

namespace Zass.Integracion.Pruebas.Pagos;

/// <summary>
/// Cobrar de mentira en produccion es la clase de error que se descubre por el lado de la
/// plata. No alcanza con un comentario: tiene que no arrancar.
///
/// <para>
/// Lo que habia antes de esta prueba: el comentario de <see cref="PasarelaSimulada"/>
/// prometia que «AgregarZass se niega a arrancar con este adaptador» fuera de desarrollo,
/// y no era cierto. El <c>switch</c> solo fallaba con un nombre DESCONOCIDO; si
/// <c>Zass:Pasarela</c> no estaba puesta, caia en la simulada por omision en cualquier
/// entorno, Produccion incluida.
/// </para>
///
/// <para>
/// Vive en esta suite y no en <c>Zass.Arquitectura.Pruebas</c> por una razon de maquina,
/// no de diseño: Windows bloquea el <c>Zass.Infraestructura.dll</c> recien compilado
/// cuando esa suite lo carga por reflexion (FileLoadException 0x800711C7). Aqui ya viven
/// pruebas de unidad que no necesitan base de datos (ver <c>Correo/CorreoUnidadPruebas</c>),
/// y esta tampoco la toca: no abre una sola conexion.
/// </para>
/// </summary>
public class PasarelaEnProduccionPruebas
{
    /// <summary>
    /// Los ajustes MINIMOS que <c>AgregarZass</c> exige de verdad: los dos [Required] de
    /// <see cref="OpcionesZass"/>. La cadena no apunta a ninguna base y no se usa: nada
    /// de esta prueba abre una conexion.
    /// </summary>
    private static IServiceProvider Contenedor(string entorno, string? pasarela)
    {
        var ajustes = new Dictionary<string, string?>
        {
            ["Zass:CadenaConexion"] = "Host=no-se-conecta;Database=no-existe",
            ["Zass:ClaveDireccionesBase64"] =
                Convert.ToBase64String(Enumerable.Range(0, 32).Select(i => (byte)i).ToArray()),
        };
        if (pasarela is not null) ajustes[RegistroDeServicios.ClavePasarela] = pasarela;

        var configuracion = new ConfigurationBuilder().AddInMemoryCollection(ajustes).Build();
        var servicios = new ServiceCollection();
        servicios.AddSingleton<IConfiguration>(configuracion);
        servicios.AddLogging();
        servicios.AddSingleton<IHostEnvironment>(
            new HostingEnvironment { EnvironmentName = entorno });
        servicios.AgregarZass(configuracion);
        return servicios.BuildServiceProvider();
    }

    [Fact]
    public void En_Produccion_y_sin_configurar_la_pasarela_NO_se_resuelve_la_simulada()
    {
        var contenedor = Contenedor("Production", pasarela: null);

        // El caso que de verdad pasa en un despliegue: nadie puso la variable.
        Assert.ThrowsAny<Exception>(() => contenedor.GetRequiredService<IPasarelaDePagos>());
    }

    [Fact]
    public void En_Produccion_pedir_la_simulada_a_proposito_tampoco_se_puede()
    {
        var contenedor = Contenedor("Production", pasarela: "simulada");

        Assert.ThrowsAny<Exception>(() => contenedor.GetRequiredService<IPasarelaDePagos>());
    }

    [Fact]
    public void El_nombre_va_en_el_mensaje_para_que_quien_despliega_sepa_que_poner()
    {
        var contenedor = Contenedor("Production", pasarela: "  SIMULADA  ");

        var error = Assert.ThrowsAny<Exception>(
            () => contenedor.GetRequiredService<IPasarelaDePagos>());

        // Mayusculas y espacios tampoco la cuelan: se compara normalizado, igual que el switch.
        Assert.Contains(RegistroDeServicios.ClavePasarela, error.Message);
        Assert.Contains(PasarelaSimulada.NombreDeLaPasarela, error.Message);
    }

    [Fact]
    public void En_Desarrollo_la_simulada_es_la_de_siempre()
    {
        var contenedor = Contenedor("Development", pasarela: null);

        var pasarela = contenedor.GetRequiredService<IPasarelaDePagos>();

        // La guarda no puede dejar sin pasarela a quien desarrolla: el flujo completo
        // tiene que poder recorrerse mientras Pablo no elija la de verdad.
        Assert.Equal(PasarelaSimulada.NombreDeLaPasarela, pasarela.Nombre);
    }

    [Fact]
    public void En_Staging_tampoco_arranca_cobrando_de_mentira()
    {
        var contenedor = Contenedor("Staging", pasarela: null);

        // Staging no es Produccion, y la simulada sigue siendo la de desarrollo: esta
        // prueba fija la decision tomada (la guarda es SOLO en Produccion) para que, si
        // alguien la cambia, lo haga a proposito y no por descuido.
        var pasarela = contenedor.GetRequiredService<IPasarelaDePagos>();

        Assert.Equal(PasarelaSimulada.NombreDeLaPasarela, pasarela.Nombre);
    }

    [Fact]
    public void Un_nombre_desconocido_no_cae_en_la_simulada_en_ningun_entorno()
    {
        var contenedor = Contenedor("Development", pasarela: "transbank");

        // Arrancar cobrando de mentira por un error de escritura en una variable de
        // entorno es la clase de fallo que no se nota hasta que falta el dinero.
        Assert.ThrowsAny<Exception>(() => contenedor.GetRequiredService<IPasarelaDePagos>());
    }
}
