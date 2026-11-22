using Microsoft.EntityFrameworkCore;
using Zass.Aplicacion.Notificaciones;
using Zass.Dominio.Comun;

namespace Zass.Infraestructura.Datos;

/// <summary>
/// ES-3b · API-43. La pregunta que se hacen los dos procesadores justo antes de enviar: ¿esta
/// persona quiere este aviso por este canal? En un solo lugar, para que correo y push no puedan
/// responderla distinto.
/// </summary>
internal static class PreferenciasAlEnviar
{
    public static async Task<bool> SaleAsync(this ZassDbContext ctx, Notificacion aviso, CanalNotificacion canal,
        CancellationToken ct)
    {
        if (!PreferenciasAviso.DependeDeLaPreferencia(aviso.Tipo, canal)) return true;
        var preferencia = await ctx.PreferenciasAviso.AsNoTracking()
            .SingleOrDefaultAsync(p => p.UsuarioId == aviso.DestinatarioId && p.Tipo == aviso.Tipo, ct);
        return PreferenciasAviso.Sale(aviso.Tipo, canal, preferencia);
    }
}
