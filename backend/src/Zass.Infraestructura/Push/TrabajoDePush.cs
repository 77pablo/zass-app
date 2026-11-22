using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Zass.Infraestructura.Push;

/// <summary>
/// ES-3. El trabajo de fondo que despacha los push encolados. Sin claves VAPID termina al
/// arrancar (las filas de push no se crean igual si nadie registro un navegador, y las que
/// haya caducan solas). Seguro con varias replicas: la reserva la hace PostgreSQL.
/// </summary>
public sealed class TrabajoDePush : BackgroundService
{
    private readonly IServiceScopeFactory _ambitos;
    private readonly OpcionesPush _o;
    private readonly ILogger<TrabajoDePush> _log;

    public TrabajoDePush(IServiceScopeFactory ambitos, IOptions<OpcionesPush> o, ILogger<TrabajoDePush> log)
    {
        _ambitos = ambitos;
        _o = o.Value;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        if (!_o.Activo)
        {
            _log.LogInformation("Push desactivado (faltan las claves Zass:Push): no se envían avisos push.");
            return;
        }

        var sondeo = TimeSpan.FromSeconds(Math.Max(1, _o.SondeoSegundos));
        while (!ct.IsCancellationRequested)
        {
            var tomados = 0;
            try
            {
                using var ambito = _ambitos.CreateScope();
                tomados = await ambito.ServiceProvider.GetRequiredService<ProcesadorDePush>().ProcesarLoteAsync(ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _log.LogError("Falló un ciclo de envío de push ({Tipo}); se reintenta.", ex.GetType().Name);
            }

            if (tomados >= _o.TamanoDeLote) continue;
            try { await Task.Delay(sondeo, ct); }
            catch (OperationCanceledException) { break; }
        }
    }
}
