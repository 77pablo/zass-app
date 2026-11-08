using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Zass.Aplicacion.Puertos;
using Zass.Aplicacion.Suscripciones;
using Zass.Dominio.Comun;

namespace Zass.Infraestructura.Fondo;

/// <summary>
/// A-09 Planificador. Mira cada tanto si algún trabajo programado está vencido y, si lo
/// consigue tomar (CC-08), lo corre.
///
/// Cada ciclo usa su propio ámbito, y por tanto su propio DbContext: un fallo en un ciclo no
/// deja un contexto roto para el siguiente. Lo mismo que hace <c>TrabajoDeCorreo</c>.
///
/// Hoy corre un solo trabajo. La forma está pensada para que agregar el siguiente —vencer
/// citas manuales sin respuesta (T5), pasar a <c>pendiente_asistencia</c> (T8), los
/// recordatorios de RN-S4-07— sea una entrada más, no otra clase como esta.
/// </summary>
public sealed class TrabajoDiario : BackgroundService
{
    private readonly IServiceScopeFactory _ambitos;
    private readonly IRelojSistema _reloj;
    private readonly OpcionesFondo _o;
    private readonly ILogger<TrabajoDiario> _log;

    public TrabajoDiario(
        IServiceScopeFactory ambitos, IRelojSistema reloj,
        IOptions<OpcionesFondo> o, ILogger<TrabajoDiario> log)
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
            try
            {
                await UnCicloAsync(ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // Solo el tipo: el mensaje de una excepción de base puede traer datos de la fila.
                _log.LogError("Falló un ciclo del planificador ({Tipo}); se reintenta.", ex.GetType().Name);
            }

            try { await Task.Delay(sondeo, ct); }
            catch (OperationCanceledException) { break; }
        }
    }

    /// <summary>
    /// Un ciclo: intenta tomar el trabajo diario y, si lo consigue, lo corre y lo suelta.
    /// Interno para poder probarlo sin esperar relojes de verdad.
    /// </summary>
    internal async Task UnCicloAsync(CancellationToken ct)
    {
        using var ambito = _ambitos.CreateScope();
        var coordinador = ambito.ServiceProvider.GetRequiredService<CoordinadorDeTrabajos>();
        var ahora = _reloj.Ahora;

        if (!await coordinador.TomarAsync(
                Trabajos.Suscripciones, ahora, TimeSpan.FromMinutes(_o.ReservaMinutos), ct))
            return;

        var resumen = "";
        var exito = false;
        try
        {
            var suscripciones = ambito.ServiceProvider.GetRequiredService<IServicioSuscripciones>();
            var r = await suscripciones.CorrerDiarioAsync(ct);
            resumen = r.ToString();
            exito = true;
            _log.LogInformation("Trabajo diario de suscripciones: {Resumen}.", resumen);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            resumen = $"error ({ex.GetType().Name})";
            _log.LogError("El trabajo diario de suscripciones falló ({Tipo}).", ex.GetType().Name);
        }
        finally
        {
            // Se suelta SIEMPRE, incluso al apagarse: si no, la reserva tendría que vencer
            // antes de que nadie pueda volver a intentarlo. Un fallo reintenta antes que un
            // éxito, y solo el éxito cuenta como corrida.
            var proxima = _reloj.Ahora + (exito
                ? TimeSpan.FromHours(_o.IntervaloHoras)
                : TimeSpan.FromMinutes(_o.EsperaTrasFalloMinutos));
            await coordinador.LiberarAsync(
                Trabajos.Suscripciones, _reloj.Ahora, proxima, resumen, exito, CancellationToken.None);
        }
    }
}
