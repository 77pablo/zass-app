using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Zass.Aplicacion.Agenda;
using Zass.Aplicacion.Puertos;
using Zass.Aplicacion.Suscripciones;
using Zass.Dominio.Comun;

namespace Zass.Infraestructura.Fondo;

/// <summary>
/// A-09 Planificador. Mira cada tanto si algún trabajo programado está vencido y, si lo
/// consigue tomar (CC-08), lo corre.
///
/// Reemplaza a <c>TrabajoDiario</c>, que sabía correr UN trabajo. Ahora hay dos con ritmos
/// distintos —las suscripciones cada día, las citas cada 15 minutos— y cada uno es una fila en
/// <c>trabajos_de_fondo</c> con su propia reserva. Agregar el siguiente es una entrada en
/// <see cref="Tareas"/>, no otra clase.
///
/// Cada ciclo usa su propio ámbito, y por tanto su propio DbContext: un fallo en un trabajo no
/// deja un contexto roto para el siguiente, ni tumba a los demás.
/// </summary>
public sealed class Planificador : BackgroundService
{
    /// <summary>Qué corre, cada cuánto y qué devuelve. El resumen son números, nunca datos de nadie.</summary>
    internal sealed record Tarea(
        string Nombre, Func<OpcionesFondo, TimeSpan> Cada,
        Func<IServiceProvider, CancellationToken, Task<string>> Correr);

    internal static readonly Tarea[] Tareas =
    {
        // Cobrar va ANTES de vencer y avisar, y en la MISMA tarea, por tres razones:
        //
        // 1. A quien se le cobra bien ya no hay que avisarle de que se le acaba, y el aviso
        //    sale de PorVencerAsync, que lee la fecha que el cobro acaba de mover. Al
        //    reves, se le mandaria un «te queda poco» a quien acaba de pagar.
        // 2. Una tarea aparte seria otra fila de trabajos_de_fondo con su propia reserva y
        //    su propio ritmo, y el cobro tiene que correr exactamente una vez al dia, en el
        //    mismo ciclo que decide quien desaparece de la busqueda.
        // 3. Si el cobro lanza, no corre el resto y la tarea se reintenta entera. Es lo
        //    correcto: vencer suscripciones a las que no se les pudo ni intentar cobrar es
        //    justo el dano que C3 existe para evitar.
        //
        // No lo llama ServicioSuscripciones.CorrerDiarioAsync porque ServicioCobros ya
        // depende de IServicioSuscripciones, y al reves seria una dependencia circular.
        new(Trabajos.Suscripciones, o => TimeSpan.FromHours(o.IntervaloHoras),
            async (sp, ct) =>
            {
                var cobros = await sp.GetRequiredService<IServicioCobros>()
                    .CobrarVencimientosAsync(ct);
                var diario = await sp.GetRequiredService<IServicioSuscripciones>()
                    .CorrerDiarioAsync(ct);
                return $"{cobros}; {diario}";
            }),

        new(Trabajos.Citas, o => TimeSpan.FromMinutes(o.CitasCadaMinutos),
            async (sp, ct) =>
                (await sp.GetRequiredService<IServicioAgenda>().AvanzarPorElTiempoAsync(ct)).ToString()),
    };

    private readonly IServiceScopeFactory _ambitos;
    private readonly IRelojSistema _reloj;
    private readonly OpcionesFondo _o;
    private readonly ILogger<Planificador> _log;

    public Planificador(
        IServiceScopeFactory ambitos, IRelojSistema reloj,
        IOptions<OpcionesFondo> o, ILogger<Planificador> log)
    {
        _ambitos = ambitos;
        _reloj = reloj;
        _o = o.Value;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        if (!_o.Activo)
        {
            _log.LogInformation("Planificador apagado (Zass:Fondo:Activo = false).");
            return;
        }

        var sondeo = TimeSpan.FromSeconds(Math.Max(5, _o.SondeoSegundos));
        _log.LogInformation("Planificador activo: mira cada {Sondeo} si hay trabajo vencido.", sondeo);

        while (!ct.IsCancellationRequested)
        {
            foreach (var tarea in Tareas)
            {
                try
                {
                    await UnaTareaAsync(tarea, ct);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception ex)
                {
                    // Solo el tipo: el mensaje de una excepción de base puede traer datos de la fila.
                    _log.LogError("Falló el ciclo de {Tarea} ({Tipo}); se reintenta.", tarea.Nombre, ex.GetType().Name);
                }
            }

            try { await Task.Delay(sondeo, ct); }
            catch (OperationCanceledException) { return; }
        }
    }

    /// <summary>
    /// Intenta tomar UN trabajo y, si lo consigue, lo corre y lo suelta. Interno para poder
    /// probarlo sin esperar relojes de verdad.
    /// </summary>
    internal async Task UnaTareaAsync(Tarea tarea, CancellationToken ct)
    {
        using var ambito = _ambitos.CreateScope();
        var coordinador = ambito.ServiceProvider.GetRequiredService<CoordinadorDeTrabajos>();

        if (!await coordinador.TomarAsync(
                tarea.Nombre, _reloj.Ahora, TimeSpan.FromMinutes(_o.ReservaMinutos), ct))
            return;

        var resumen = "";
        var exito = false;
        try
        {
            resumen = await tarea.Correr(ambito.ServiceProvider, ct);
            exito = true;
            _log.LogInformation("{Tarea}: {Resumen}.", tarea.Nombre, resumen);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            resumen = $"error ({ex.GetType().Name})";
            _log.LogError("{Tarea} falló ({Tipo}).", tarea.Nombre, ex.GetType().Name);
        }
        finally
        {
            // Se suelta SIEMPRE, incluso al apagarse: si no, la reserva tendría que vencer
            // antes de que nadie pueda volver a intentarlo. Un fallo reintenta antes que un
            // éxito, y solo el éxito cuenta como corrida.
            var proxima = _reloj.Ahora + (exito
                ? tarea.Cada(_o)
                : TimeSpan.FromMinutes(_o.EsperaTrasFalloMinutos));
            await coordinador.LiberarAsync(
                tarea.Nombre, _reloj.Ahora, proxima, resumen, exito, CancellationToken.None);
        }
    }
}
