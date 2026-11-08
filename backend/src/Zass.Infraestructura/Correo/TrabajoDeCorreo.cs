using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Zass.Infraestructura.Correo;

/// <summary>
/// El trabajo de fondo que despacha los avisos de correo encolados (TBL-27). Con el proveedor
/// «Ninguno» termina al arrancar y los avisos quedan pendientes, como hasta ahora.
///
/// Cada ciclo usa su propio ámbito (y por tanto su propio DbContext): un fallo en un ciclo no
/// deja un contexto roto para el siguiente. Es seguro con varias réplicas porque la
/// reserva de filas la hace PostgreSQL (SKIP LOCKED), no este proceso.
/// </summary>
public sealed class TrabajoDeCorreo : BackgroundService
{
    private readonly IServiceScopeFactory _ambitos;
    private readonly OpcionesCorreo _o;
    private readonly ILogger<TrabajoDeCorreo> _log;

    public TrabajoDeCorreo(
        IServiceScopeFactory ambitos, IOptions<OpcionesCorreo> o, ILogger<TrabajoDeCorreo> log)
    {
        _ambitos = ambitos;
        _o = o.Value;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        if (_o.Proveedor == ProveedorDeCorreo.Ninguno)
        {
            _log.LogInformation("Correo desactivado (Zass:Correo:Proveedor = Ninguno): los avisos quedan pendientes.");
            return;
        }

        _log.LogInformation("Envío de correo activo con {Proveedor}.", _o.Proveedor);
        var sondeo = TimeSpan.FromSeconds(Math.Max(1, _o.SondeoSegundos));

        while (!ct.IsCancellationRequested)
        {
            var tomados = 0;
            try
            {
                using var ambito = _ambitos.CreateScope();
                var procesador = ambito.ServiceProvider.GetRequiredService<ProcesadorDeCorreo>();
                tomados = await procesador.ProcesarLoteAsync(ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // Solo el tipo: una excepción de base de datos puede traer datos de la fila.
                _log.LogError("Falló un ciclo de envío de correo ({Tipo}); se reintenta.", ex.GetType().Name);
            }

            // Lote lleno: probablemente hay más esperando, no se duerme.
            if (tomados >= _o.TamanoDeLote) continue;
            try { await Task.Delay(sondeo, ct); }
            catch (OperationCanceledException) { break; }
        }
    }
}
