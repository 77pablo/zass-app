using Microsoft.EntityFrameworkCore;
using Zass.Aplicacion.Puertos;
using Zass.Dominio.Comun;

namespace Zass.Infraestructura.Datos.Repositorios;

/// <summary>JQ-8 · MI-METRICAS. Solo lectura y solo del perfil que se pide: el dueño lo pone la sesión.</summary>
public sealed class ConsultaMetricasPrestadorEfCore : IConsultaMetricasPrestador
{
    private readonly ZassDbContext _ctx;
    public ConsultaMetricasPrestadorEfCore(ZassDbContext ctx) => _ctx = ctx;

    public async Task<MetricasDePrestador> CalcularAsync(Guid perfilPrestadorId, DateTime ahora, CancellationToken ct = default)
    {
        var desde7 = ahora.AddDays(-7);
        var desde30 = ahora.AddDays(-30);

        var contactos = _ctx.Contactos.AsNoTracking()
            .Where(c => c.PerfilPrestadorId == perfilPrestadorId && c.OcurridoEn <= ahora);
        var c7 = await contactos.CountAsync(c => c.OcurridoEn >= desde7, ct);
        var c30 = await contactos.CountAsync(c => c.OcurridoEn >= desde30, ct);
        var cTotal = await contactos.CountAsync(ct);

        var porCanal = await contactos.Where(c => c.OcurridoEn >= desde30)
            .GroupBy(c => c.Canal)
            .Select(g => new { Canal = g.Key, Cuantos = g.Count() })
            .ToListAsync(ct);
        int Del(CanalContacto canal) => porCanal.SingleOrDefault(x => x.Canal == canal)?.Cuantos ?? 0;

        // Los escaneos de las tarjetas que HOY son de este perfil.
        var escaneos = _ctx.Escaneos.AsNoTracking()
            .Where(e => e.Tarjeta!.PerfilPrestadorId == perfilPrestadorId && e.OcurridoEn <= ahora);
        var e7 = await escaneos.CountAsync(e => e.OcurridoEn >= desde7, ct);
        var e30 = await escaneos.CountAsync(e => e.OcurridoEn >= desde30, ct);
        var eTotal = await escaneos.CountAsync(ct);

        return new MetricasDePrestador(
            new ConteoEnVentanas(c7, c30, cTotal),
            new ContactosPorCanal(Del(CanalContacto.Whatsapp), Del(CanalContacto.Telefono), Del(CanalContacto.Correo), Del(CanalContacto.Formulario)),
            new ConteoEnVentanas(e7, e30, eTotal),
            ahora);
    }
}
