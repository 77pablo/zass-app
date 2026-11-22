using Microsoft.EntityFrameworkCore;
using Zass.Aplicacion.Puertos;
using Zass.Dominio.Comun;

namespace Zass.Infraestructura.Datos.Repositorios;

/// <summary>
/// API-60 · JQ-14. Cuenta sobre las tablas que ya existen; ninguna escritura y ninguna migración.
/// Cada cifra es una consulta corta: con el volumen de lanzamiento no hace falta precalcular nada.
/// </summary>
public sealed class ConsultaIndicadoresEfCore : IConsultaIndicadores
{
    private readonly ZassDbContext _ctx;
    public ConsultaIndicadoresEfCore(ZassDbContext ctx) => _ctx = ctx;

    public async Task<IndicadoresDeOperacion> CalcularAsync(DateTime ahora, CancellationToken ct = default)
    {
        var desdeVerificacion = ahora.AddDays(-VentanasDeIndicadores.DiasVerificacion);
        var desdeAvisos = ahora.AddDays(-VentanasDeIndicadores.DiasAvisos);

        // --- Verificación
        var enCola = _ctx.Credenciales.AsNoTracking().Where(c => c.Estado == EstadoCredencial.EnRevision);
        var cuantasEnCola = await enCola.CountAsync(ct);
        var masAntigua = cuantasEnCola == 0
            ? (DateTime?)null
            : await enCola.MinAsync(c => c.CreadaEn, ct);

        // Las resueltas dentro de la ventana, con la creación de su credencial para el tiempo.
        var resueltas = await _ctx.Verificaciones.AsNoTracking()
            .Where(v => v.ResueltaEn >= desdeVerificacion && v.ResueltaEn <= ahora)
            .Select(v => new { v.Resultado, v.ResueltaEn, CreadaEn = v.Credencial!.CreadaEn })
            .ToListAsync(ct);

        var rechazadas = resueltas.Count(v => v.Resultado == ResultadoVerificacion.Rechazada);
        double? tasa = resueltas.Count == 0 ? null : (double)rechazadas / resueltas.Count;
        double? horas = resueltas.Count == 0
            ? null
            : resueltas.Average(v => (v.ResueltaEn - v.CreadaEn).TotalHours);

        // --- Avisos: correos fallidos programados en la ventana.
        var fallidos = await _ctx.Notificaciones.AsNoTracking().CountAsync(n =>
            n.Canal == CanalNotificacion.Correo
            && n.Estado == EstadoEnvio.Fallida
            && n.ProgramadaPara >= desdeAvisos && n.ProgramadaPara <= ahora, ct);

        // --- Reportes sin resolver (abiertos o tomados por un moderador).
        var reportes = await _ctx.ReportesResena.AsNoTracking().CountAsync(r =>
            r.Estado == EstadoReporte.Abierto || r.Estado == EstadoReporte.EnRevision, ct);

        // --- Tarjetas emitidas que nadie activó todavía.
        var sinActivar = await _ctx.Tarjetas.AsNoTracking().CountAsync(t => t.Estado == EstadoTarjeta.Emitida, ct);

        return new IndicadoresDeOperacion(
            new IndicadoresDeVerificacion(cuantasEnCola, masAntigua, resueltas.Count, rechazadas, tasa, horas),
            new IndicadoresDeAvisos(fallidos),
            reportes,
            sinActivar,
            ahora);
    }
}
