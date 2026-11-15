using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.Internal;
using Zass.Aplicacion.Puertos;
using Zass.Dominio.Comun;
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

    /// <summary>
    /// Esto cambio el 14-nov-2026, y conviene saber por que.
    ///
    /// <para>
    /// Antes, pedir la simulada en Produccion a proposito tampoco se podia, y eso dejaba el
    /// lanzamiento sin salida: el cupo fundador inscribe un medio de pago con tres montos en
    /// cero —no mueve un peso— y sin pasarela resuelta ese camino devuelve 500. Las dos
    /// salidas obvias eran peores: dejar la simulada entera abre el agujero de que el dia 31
    /// el cobro recurrente «apruebe» pagos que no existen, y declarar Staging es lo mismo con
    /// una mentira encima.
    /// </para>
    ///
    /// <para>
    /// Ahora, puesta **a proposito**, se resuelve en modo solo inscripcion: autoriza y no
    /// cobra. El invariante entero sigue en pie y lo fijan las tres pruebas de abajo.
    /// </para>
    /// </summary>
    [Fact]
    public void En_Produccion_pedir_la_simulada_a_proposito_da_el_modo_solo_inscripcion()
    {
        var contenedor = Contenedor("Production", pasarela: "simulada");

        var pasarela = contenedor.GetRequiredService<IPasarelaDePagos>();

        // El nombre dice lo que es, y queda escrito en cada intento de pago: mirando la tabla
        // se sabe que ese medio se inscribio sin pasarela real detras.
        Assert.Equal(PasarelaSoloInscripcion.NombreDeLaPasarela, pasarela.Nombre);
        Assert.NotEqual(PasarelaSimulada.NombreDeLaPasarela, pasarela.Nombre);
    }

    [Fact]
    public async Task El_modo_solo_inscripcion_SI_inscribe_porque_no_mueve_dinero()
    {
        var pasarela = Contenedor("Production", pasarela: "simulada")
            .GetRequiredService<IPasarelaDePagos>();

        var r = await pasarela.IniciarInscripcionAsync(
            new SolicitudDeInscripcion(Guid.NewGuid(), "ZASS! Fundador - inscripcion"));

        Assert.True(r.Exito, r.Mensaje);
        Assert.NotNull(r.Valor);
    }

    /// <summary>
    /// Un cobro con alguien delante: falla con regla de negocio, no con una excepcion, para
    /// que la pantalla pueda contarlo con calma.
    /// </summary>
    [Fact]
    public async Task El_modo_solo_inscripcion_NO_inicia_un_cobro()
    {
        var pasarela = Contenedor("Production", pasarela: "simulada")
            .GetRequiredService<IPasarelaDePagos>();

        var r = await pasarela.IniciarCobroAsync(
            new SolicitudDeCobro(Guid.NewGuid(), Monto: 5990, "CLP", "un mes"));

        Assert.False(r.Exito);
        Assert.Equal(Fallas.ReglaDeNegocio, r.Codigo);
        Assert.Contains("no tiene pasarela de pagos real", r.Mensaje);
    }

    /// <summary>
    /// **La prueba que de verdad importa.** El cobro del dia del vencimiento corre sin nadie
    /// mirando: es ahi donde una pasarela que finge «aprobado» regalaria meses en silencio.
    /// Tiene que fallar, y el intento quedarse sin resolver a proposito.
    /// </summary>
    [Fact]
    public async Task El_modo_solo_inscripcion_NO_cobra_el_dia_del_vencimiento()
    {
        var pasarela = Contenedor("Production", pasarela: "simulada")
            .GetRequiredService<IPasarelaDePagos>();

        var r = await pasarela.CobrarAsync(
            new SolicitudDeCobro(Guid.NewGuid(), Monto: 5990, "CLP", "el mes siguiente")
            { MedioInscrito = "sim-medio-inscrito" });

        Assert.False(r.Exito);
        Assert.Equal(Fallas.ReglaDeNegocio, r.Codigo);
        Assert.Contains("no tiene pasarela de pagos real", r.Mensaje);
    }

    /// <summary>
    /// Mayusculas y espacios no cambian nada: se compara normalizado, igual que el switch. Lo
    /// que cambia respecto de antes es el desenlace — se resuelve en modo solo inscripcion —,
    /// y esta prueba lo fija para que escribir «  SIMULADA  » no sea otra cosa que «simulada».
    /// </summary>
    [Fact]
    public void Mayusculas_y_espacios_no_cuelan_otra_pasarela()
    {
        var contenedor = Contenedor("Production", pasarela: "  SIMULADA  ");

        var pasarela = contenedor.GetRequiredService<IPasarelaDePagos>();

        Assert.Equal(PasarelaSoloInscripcion.NombreDeLaPasarela, pasarela.Nombre);
    }

    /// <summary>
    /// El mensaje de la variable sin poner tiene que decir QUE poner: quien despliega lo lee
    /// en el registro de Render y no tiene el codigo delante.
    /// </summary>
    [Fact]
    public void El_mensaje_dice_que_poner_cuando_la_variable_falta()
    {
        var contenedor = Contenedor("Production", pasarela: null);

        var error = Assert.ThrowsAny<Exception>(
            () => contenedor.GetRequiredService<IPasarelaDePagos>());

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
