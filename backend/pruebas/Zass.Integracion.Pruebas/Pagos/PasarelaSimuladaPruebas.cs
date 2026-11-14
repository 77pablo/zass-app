using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Zass.Aplicacion.Puertos;
using Zass.Infraestructura.Configuracion;
using Zass.Infraestructura.Pagos;

namespace Zass.Integracion.Pruebas.Pagos;

/// <summary>
/// El contrato de la pasarela de verdad, no el del doble.
///
/// <para>
/// **Por qué existe este archivo.** El 14 de noviembre de 2026, con 1.650 pruebas en verde,
/// el botón «Quiero mi cupo» devolvía 400 a todo el mundo: el camino del fundador le pedía
/// a la pasarela un COBRO de cero pesos, y <see cref="PasarelaSimulada"/> lo rechaza —igual
/// que lo rechazan Transbank, Flow y Mercado Pago—. Nadie podía tomar un cupo fundador, que
/// es la oferta entera del lanzamiento.
/// </para>
///
/// <para>
/// Lo que lo dejó pasar: <c>PasarelaFalsa</c>, el doble, aceptaba el monto cero sin chistar.
/// Un doble más permisivo que la cosa real no es un doble, es un tapón. Estas pruebas van
/// contra el adaptador de verdad para que el contrato se mida donde se cumple.
/// </para>
///
/// <para>
/// No tocan la base de datos ni salen a internet. Viven en esta suite por la misma razón de
/// máquina que <see cref="PasarelaEnProduccionPruebas"/>.
/// </para>
/// </summary>
public class PasarelaSimuladaPruebas
{
    private const string Raiz = "https://zass.cl";

    private static PasarelaSimulada LaPasarela() =>
        new(Options.Create(new OpcionesZass
        {
            CadenaConexion = "Host=no-se-conecta;Database=no-existe",
            ClaveDireccionesBase64 =
                Convert.ToBase64String(Enumerable.Range(0, 32).Select(i => (byte)i).ToArray()),
            UrlPublica = Raiz,
        }),
        NullLogger<PasarelaSimulada>.Instance);

    /// <summary>
    /// La guarda que destapó el defecto, fijada a propósito: un cobro de cero NO se puede
    /// pedir. Si alguien la quita para «arreglar» el cupo fundador, está tapando el agujero
    /// por el lado equivocado — lo que no cobra es una inscripción, y tiene su propio camino.
    /// </summary>
    [Fact]
    public async Task Pedirle_un_COBRO_de_cero_pesos_falla_y_eso_esta_bien()
    {
        var r = await LaPasarela().IniciarCobroAsync(
            new SolicitudDeCobro(Guid.NewGuid(), Monto: 0, "CLP", "lo que sea"));

        Assert.False(r.Exito);
        Assert.Contains("cero", r.Mensaje, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>La inscripción no tiene monto que rechazar: por eso no lo lleva en el tipo.</summary>
    [Fact]
    public async Task La_INSCRIPCION_no_cobra_nada_y_si_se_puede_pedir()
    {
        var intento = Guid.NewGuid();

        var r = await LaPasarela().IniciarInscripcionAsync(
            new SolicitudDeInscripcion(intento, "ZASS! Fundador - inscripcion"));

        Assert.True(r.Exito);
        Assert.NotNull(r.Valor);
        Assert.StartsWith("sim-", r.Valor!.ReferenciaPasarela);
        Assert.Contains(intento.ToString(), r.Valor.UrlParaPagar);
    }

    /// <summary>
    /// La otra mitad del defecto, y la que ninguna prueba de C# podía ver: la dirección a la
    /// que manda este adaptador tiene que ser una ruta que el sitio declare. Hasta hoy
    /// mandaba a <c>/pagar/simulado</c>, que no existe en <c>App.tsx</c>: quien llegaba veía
    /// «Esta página no existe». La ruta de verdad es <c>/pago/inscripcion</c>, y está en una
    /// constante para que las dos mitades no se puedan separar en silencio.
    /// </summary>
    [Fact]
    public async Task Manda_a_la_pantalla_que_el_sitio_declara_y_no_a_una_inventada()
    {
        var pasarela = LaPasarela();

        var inscripcion = await pasarela.IniciarInscripcionAsync(
            new SolicitudDeInscripcion(Guid.NewGuid(), "inscripcion"));
        var cobro = await pasarela.IniciarCobroAsync(
            new SolicitudDeCobro(Guid.NewGuid(), Monto: 5990, "CLP", "un mes"));

        Assert.Equal("/pago/inscripcion", PasarelaSimulada.RutaDeLaPantalla);
        Assert.StartsWith($"{Raiz}{PasarelaSimulada.RutaDeLaPantalla}?", inscripcion.Valor!.UrlParaPagar);
        Assert.StartsWith($"{Raiz}{PasarelaSimulada.RutaDeLaPantalla}?", cobro.Valor!.UrlParaPagar);
    }

    /// <summary>
    /// La pantalla lee <c>intento</c> para saber de qué cobro habla. Si el adaptador deja de
    /// mandarlo, la persona llega a una pasarela que no sabe qué confirmar.
    /// </summary>
    [Fact]
    public async Task La_direccion_lleva_el_identificador_del_intento_que_la_pantalla_espera()
    {
        var intento = Guid.NewGuid();

        var r = await LaPasarela().IniciarInscripcionAsync(
            new SolicitudDeInscripcion(intento, "inscripcion"));

        Assert.Contains($"{PasarelaSimulada.CampoIntento}={intento}", r.Valor!.UrlParaPagar);
    }
}
