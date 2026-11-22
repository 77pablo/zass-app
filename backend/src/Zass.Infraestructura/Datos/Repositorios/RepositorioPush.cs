using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;
using Zass.Aplicacion.Notificaciones;
using Zass.Dominio.Comun;

namespace Zass.Infraestructura.Datos.Repositorios;

/// <summary>ES-3 · API-45. Los navegadores con push, en <c>suscripciones_push</c>.</summary>
public sealed class RepositorioPushEfCore : IRepositorioPush
{
    private readonly ZassDbContext _ctx;
    public RepositorioPushEfCore(ZassDbContext ctx) => _ctx = ctx;

    public async Task RegistrarAsync(Guid usuarioId, string endpoint, string p256dh, string auth, DateTime ahora,
        CancellationToken ct = default)
    {
        var existente = await _ctx.SuscripcionesPush.SingleOrDefaultAsync(s => s.Endpoint == endpoint, ct);
        if (existente is null)
        {
            _ctx.SuscripcionesPush.Add(new SuscripcionPush
            {
                UsuarioId = usuarioId, Endpoint = endpoint, ClaveP256dh = p256dh, ClaveAuth = auth,
                CreadaEn = ahora, UltimoUsoEn = ahora,
            });
        }
        else
        {
            // El mismo navegador con otra cuenta (un computador compartido): pasa a la nueva, y la
            // anterior deja de recibir aqui. Las claves pueden haber cambiado si el navegador renovo.
            existente.UsuarioId = usuarioId;
            existente.ClaveP256dh = p256dh;
            existente.ClaveAuth = auth;
            existente.UltimoUsoEn = ahora;
        }

        // Tope por persona: este navegador cuenta como uno; de los demas quedan los mas usados.
        var sobran = await _ctx.SuscripcionesPush
            .Where(s => s.UsuarioId == usuarioId && s.Endpoint != endpoint)
            .OrderByDescending(s => s.UltimoUsoEn)
            .Skip(AvisosPush.MaximoPorUsuario - 1)
            .ToListAsync(ct);
        _ctx.SuscripcionesPush.RemoveRange(sobran);
    }

    public async Task<bool> BorrarAsync(Guid usuarioId, string endpoint, CancellationToken ct = default)
    {
        var suya = await _ctx.SuscripcionesPush
            .SingleOrDefaultAsync(s => s.Endpoint == endpoint && s.UsuarioId == usuarioId, ct);
        if (suya is null) return false;
        _ctx.SuscripcionesPush.Remove(suya);
        return true;
    }

    /// <summary>
    /// Un INSERT ... SELECT con ON CONFLICT: idempotente aunque dos registros lleguen a la vez.
    /// Solo los que siguen «Programada» (no los que se estan enviando ni los ya enviados).
    /// </summary>
    public Task<int> EncolarGemelasPendientesAsync(Guid usuarioId, CancellationToken ct = default) =>
        _ctx.Database.ExecuteSqlRawAsync("""
            INSERT INTO notificaciones
                (id, destinatario_id, cita_id, tipo, canal, clave_idempotencia, estado,
                 asunto, cuerpo, programada_para, intentos)
            SELECT gen_random_uuid(), n.destinatario_id, n.cita_id, n.tipo, 'Push',
                   n.clave_idempotencia || @sufijo, 'Programada',
                   n.asunto, n.cuerpo, n.programada_para, 0
              FROM notificaciones n
             WHERE n.destinatario_id = @usuario AND n.canal = 'Correo' AND n.estado = 'Programada'
               AND n.tipo = ANY(@tipos) AND n.clave_idempotencia <> ''
            ON CONFLICT (clave_idempotencia) DO NOTHING
            """,
            new object[]
            {
                new NpgsqlParameter("usuario", usuarioId),
                new NpgsqlParameter("sufijo", AvisosPush.SufijoClave),
                new NpgsqlParameter("tipos", NpgsqlDbType.Array | NpgsqlDbType.Varchar) { Value = AvisosPush.TiposConPush },
            }, ct);
}
