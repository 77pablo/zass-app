using Microsoft.Extensions.DependencyInjection;
using Zass.Aplicacion.Puertos;
using Zass.Aplicacion.Suscripciones;
using Zass.Dominio.Comun;
using Zass.Infraestructura.Fondo;

namespace Zass.Integracion.Pruebas;

/// <summary>
/// C3. Que el cobro automatico tenga QUIEN lo dispare.
///
/// <para>
/// Esta prueba existe porque el agujero que C3 viene a tapar no era de logica: el camino
/// de cobro estaba completo y probado, y nadie lo llamaba. Un <c>ServicioCobros</c>
/// perfecto que no esta enganchado al trabajo diario deja a los fundadores sin cobrar
/// exactamente igual que si no existiera.
/// </para>
///
/// <para>
/// No abre ninguna conexion a PostgreSQL —son dos dobles y un contenedor de servicios— y
/// vive aqui, y no en las pruebas de aplicacion, solo porque <c>Planificador.Tareas</c> es
/// interno y es este proyecto el que lo ve (<c>InternalsVisibleTo</c>).
/// </para>
/// </summary>
public class TrabajoDiarioCobraPruebas
{
    [Fact]
    public async Task El_trabajo_diario_cobra_los_vencimientos_ANTES_de_vencer_y_avisar()
    {
        // El orden de las llamadas se anota en una lista de la PRUEBA, no en un estatico:
        // dos pruebas de esta clase corriendo a la vez no se pisan.
        var llamadas = new List<string>();
        var cobros = new CobrosEspia(llamadas);
        var suscripciones = new SuscripcionesEspia(llamadas);

        var servicios = new ServiceCollection();
        servicios.AddSingleton<IServicioCobros>(cobros);
        servicios.AddSingleton<IServicioSuscripciones>(suscripciones);
        using var proveedor = servicios.BuildServiceProvider();

        var tarea = Planificador.Tareas.Single(t => t.Nombre == Trabajos.Suscripciones);
        var resumen = await tarea.Correr(proveedor, CancellationToken.None);

        // Que se llamo, que es lo que no pasaba antes de C3.
        Assert.Equal(1, cobros.Corridas);
        Assert.Equal(1, suscripciones.Corridas);

        // Y en ESE orden: a quien se le acaba de cobrar no hay que avisarle de que se le
        // acaba, y el aviso lo decide PorVencerAsync leyendo la fecha que el cobro movio.
        Assert.Equal(new[] { "cobrar", "diario" }, llamadas.ToArray());

        // El resumen que queda en trabajos_de_fondo lleva las dos partes, y son numeros.
        Assert.Contains("cobradas", resumen);
        Assert.Contains("vencidas", resumen);
    }

    private sealed class CobrosEspia : IServicioCobros
    {
        private readonly List<string> _llamadas;
        public CobrosEspia(List<string> llamadas) => _llamadas = llamadas;

        public int Corridas { get; private set; }

        public Task<ResumenDeCobros> CobrarVencimientosAsync(CancellationToken ct = default)
        {
            Corridas++;
            _llamadas.Add("cobrar");
            return Task.FromResult(new ResumenDeCobros(1, 0, 0, 0, 0));
        }

        public Task<Resultado<CotizacionDto>> CotizarAsync(
            Guid perfilPrestadorId, string codigoPlan, string? codigoCupon,
            CancellationToken ct = default) => throw new NotSupportedException();

        public Task<Resultado<IntentoDePagoDto>> IniciarAsync(
            IniciarCobroCmd cmd, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<Resultado<IntentoDePagoDto>> IniciarInscripcionFundadorAsync(
            Guid perfilPrestadorId, string claveIdempotencia, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<Resultado<ConfirmacionDto>> ConfirmarAsync(
            AvisoDePasarela aviso, CancellationToken ct = default) =>
            throw new NotSupportedException();
    }

    /// <summary>
    /// Solo contesta <see cref="CorrerDiarioAsync"/>. Lo demas lanza a proposito: si el
    /// enganche del trabajo diario empieza a llamar a otra cosa, esta prueba lo dice en vez
    /// de pasar en verde con un doble que contesta a todo.
    /// </summary>
    private sealed class SuscripcionesEspia : IServicioSuscripciones
    {
        private readonly List<string> _llamadas;
        public SuscripcionesEspia(List<string> llamadas) => _llamadas = llamadas;

        public int Corridas { get; private set; }

        public Task<ResumenDelDiario> CorrerDiarioAsync(CancellationToken ct = default)
        {
            Corridas++;
            _llamadas.Add("diario");
            return Task.FromResult(new ResumenDelDiario(0, 0));
        }

        public Task<bool> EstaVigenteAsync(Guid perfilPrestadorId, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyCollection<Guid>> VigentesEntreAsync(
            IReadOnlyCollection<Guid> perfilPrestadorIds, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<Resultado<SuscripcionDto>> DePrestadorAsync(
            Guid perfilPrestadorId, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<Resultado> IniciarPruebaAsync(
            Guid perfilPrestadorId, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<Resultado<Guid>> RegistrarPeriodoPagadoAsync(
            Guid suscripcionId, int meses = 1, int montoPagado = 0, string? referencia = null,
            Guid? intentoDePagoId = null, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<Resultado> CancelarAsync(Guid suscripcionId, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<int> VencerExpiradasAsync(CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<SuscripcionPorVencer>> PorVencerAsync(
            int dentroDeDias, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<PlanDto>> PlanesActivosAsync(CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<int> LimiteDeServiciosAsync(
            Guid perfilPrestadorId, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<int> CuposFundadorDisponiblesAsync(CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<Resultado<PlanFundadorDto>> EstadoDelPlanFundadorAsync(
            CancellationToken ct = default) => throw new NotSupportedException();

        public Task<Resultado<short>> ReservarCupoFundadorAsync(
            Guid perfilPrestadorId, TimeSpan? duracion = null, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<Resultado> ConfirmarCupoFundadorAsync(
            Guid perfilPrestadorId, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<Resultado> LiberarCupoFundadorAsync(
            Guid perfilPrestadorId, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<Resultado<DateTime>> PasarAFundadorConMesGratisAsync(
            Guid suscripcionId, string? medioInscrito = null, CancellationToken ct = default) =>
            throw new NotSupportedException();
    }
}
