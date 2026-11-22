using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using NpgsqlTypes;
using Zass.Aplicacion.Agenda;
using Zass.Aplicacion.Notificaciones;
using Zass.Aplicacion.Puertos;
using Zass.Dominio.Comun;
using Zass.Dominio.Identidad;
using Zass.Infraestructura.Datos;

namespace Zass.Infraestructura.Push;

/// <summary>
/// ES-3. Un ciclo de despacho de push: el mismo patron que <c>ProcesadorDeCorreo</c> (reserva con
/// SKIP LOCKED, <c>programada_para</c> como proximo intento y como reserva, espera creciente con
/// tope), sobre las filas con <c>canal = 'Push'</c>. Cada aviso va a TODOS los navegadores de la
/// persona; los que el servicio de push declara vencidos (404/410) se borran.
///
/// Un aviso cuenta como enviado si llego a al menos un navegador: reintentarlo para los que
/// fallaron repetiria el aviso en los que si llego.
/// </summary>
public sealed class ProcesadorDePush
{
    private readonly ZassDbContext _ctx;
    private readonly IEnviadorDePush _enviador;
    private readonly IRelojSistema _reloj;
    private readonly OpcionesPush _o;
    private readonly ILogger<ProcesadorDePush> _log;

    public ProcesadorDePush(ZassDbContext ctx, IEnviadorDePush enviador, IRelojSistema reloj,
        IOptions<OpcionesPush> o, ILogger<ProcesadorDePush> log)
    {
        _ctx = ctx;
        _enviador = enviador;
        _reloj = reloj;
        _o = o.Value;
        _log = log;
    }

    /// <summary>Devuelve cuantos avisos tomo (no cuantos llegaron).</summary>
    public async Task<int> ProcesarLoteAsync(CancellationToken ct = default)
    {
        if (!_o.Activo) return 0;

        await AnularSinDestinoOCaducadosAsync(ct);

        var ids = await TomarLoteAsync(ct);
        foreach (var id in ids)
        {
            ct.ThrowIfCancellationRequested();
            await ProcesarUnoAsync(id, ct);
            _ctx.ChangeTracker.Clear();
        }
        return ids.Count;
    }

    /// <summary>
    /// Anula, sin gastar intentos, los push que ya no pueden o no deben salir: la persona dio de
    /// baja todos sus navegadores, o el aviso lleva mas de <see cref="OpcionesPush.CaducaHoras"/>
    /// esperando (con <c>intentos = 0</c>, por la misma razon que en el correo: despues de la
    /// primera reserva <c>programada_para</c> deja de decir cuando se encolo).
    /// </summary>
    private async Task AnularSinDestinoOCaducadosAsync(CancellationToken ct)
    {
        var limite = _reloj.Ahora.AddHours(-_o.CaducaHoras);
        await _ctx.Database.ExecuteSqlRawAsync("""
            UPDATE notificaciones n SET estado = 'Anulada'
             WHERE n.canal = 'Push' AND n.estado = 'Programada'
               AND (NOT EXISTS (SELECT 1 FROM suscripciones_push s WHERE s.usuario_id = n.destinatario_id)
                    OR (n.intentos = 0 AND n.programada_para < @limite))
            """,
            new object[] { new NpgsqlParameter("limite", NpgsqlDbType.TimestampTz) { Value = limite } }, ct);
    }

    /// <summary>Igual que el correo: CTE MATERIALIZED + FOR UPDATE SKIP LOCKED (ver ProcesadorDeCorreo).</summary>
    internal async Task<List<Guid>> TomarLoteAsync(CancellationToken ct)
    {
        var ahora = _reloj.Ahora;
        const string sql = """
            WITH lote AS MATERIALIZED (
                SELECT id FROM notificaciones
                WHERE canal = 'Push' AND tipo = ANY(@tipos)
                  AND estado IN ('Programada', 'Enviando') AND programada_para <= @ahora
                ORDER BY programada_para
                LIMIT @lote
                FOR UPDATE SKIP LOCKED)
            UPDATE notificaciones n
               SET estado = 'Enviando', intentos = n.intentos + 1, programada_para = @reserva
              FROM lote
             WHERE n.id = lote.id
            RETURNING n.id
            """;

        await _ctx.Database.OpenConnectionAsync(ct);
        try
        {
            await using var cmd = _ctx.Database.GetDbConnection().CreateCommand();
            cmd.CommandText = sql;
            cmd.Parameters.Add(new NpgsqlParameter("reserva", NpgsqlDbType.TimestampTz)
                { Value = ahora.AddSeconds(_o.ReservaSegundos) });
            cmd.Parameters.Add(new NpgsqlParameter("ahora", NpgsqlDbType.TimestampTz) { Value = ahora });
            cmd.Parameters.Add(new NpgsqlParameter("lote", NpgsqlDbType.Integer) { Value = _o.TamanoDeLote });
            cmd.Parameters.Add(new NpgsqlParameter("tipos", NpgsqlDbType.Array | NpgsqlDbType.Varchar)
                { Value = AvisosPush.TiposConPush });

            var ids = new List<Guid>();
            await using var lector = await cmd.ExecuteReaderAsync(ct);
            while (await lector.ReadAsync(ct)) ids.Add(lector.GetGuid(0));
            return ids;
        }
        finally
        {
            await _ctx.Database.CloseConnectionAsync();
        }
    }

    private async Task ProcesarUnoAsync(Guid id, CancellationToken ct)
    {
        var aviso = await _ctx.Notificaciones.Include(n => n.Destinatario).SingleOrDefaultAsync(n => n.Id == id, ct);
        if (aviso is null) return;
        var ahora = _reloj.Ahora;
        var usuario = aviso.Destinatario;

        if (aviso.Intentos > _o.MaximoIntentos)
        {
            await CerrarAsync(aviso, EstadoEnvio.Fallida, "máximo de intentos agotado", ahora, ct);
            return;
        }

        if (usuario is null || usuario.Estado == EstadoCuenta.Eliminada || usuario.AnonimizadoEn is not null)
        {
            await CerrarAsync(aviso, EstadoEnvio.Anulada, null, ahora, ct);
            return;
        }

        var cita = await DatosDeCitaAsync(aviso, ct);

        // JQ-3, igual que el correo: un recordatorio de una cita que cambio o paso no sale.
        if (RecordatoriosDeCita.Tipos.Contains(aviso.Tipo) && !await RecordatorioVigenteAsync(aviso, ahora, ct))
        {
            await CerrarAsync(aviso, EstadoEnvio.Anulada, null, ahora, ct);
            return;
        }

        // ES-3b · API-43: la persona apago el push para este tipo (se mira al enviar, como el correo).
        if (!await _ctx.SaleAsync(aviso, CanalNotificacion.Push, ct))
        {
            await CerrarAsync(aviso, EstadoEnvio.Anulada, null, ahora, ct);
            return;
        }

        var mensaje = MensajesPush.Para(aviso.Tipo, aviso.Cuerpo, cita);
        if (mensaje is null)
        {
            await CerrarAsync(aviso, EstadoEnvio.Fallida, "tipo sin texto de push", ahora, ct);
            return;
        }

        var destinos = await _ctx.SuscripcionesPush.Where(s => s.UsuarioId == aviso.DestinatarioId).ToListAsync(ct);

        // Segunda capa contra SSRF (sugerencia de Pablo en el #202): el validador de API-45 ya
        // filtra los endpoints, pero una fila que entre por otro camino (un script, una migracion,
        // un cambio futuro) no paso por el. No se le hace POST: se borra. El endpoint no se registra
        // en el log (es un dato de capacidad: quien lo tiene puede mandarle avisos a esa persona).
        var noPermitidos = destinos.Where(s => !AvisosPush.EndpointPermitido(s.Endpoint)).ToList();
        if (noPermitidos.Count > 0)
        {
            _ctx.SuscripcionesPush.RemoveRange(noPermitidos);
            destinos = destinos.Except(noPermitidos).ToList();
            _log.LogWarning("Push: {N} navegador(es) con un endpoint fuera de la lista permitida, borrados sin enviar (aviso {Id}).",
                noPermitidos.Count, aviso.Id);
        }

        if (destinos.Count == 0)
        {
            await CerrarAsync(aviso, EstadoEnvio.Anulada, null, ahora, ct);
            return;
        }

        var saliente = new MensajeSaliente(
            MensajesPush.AJson(mensaje, Etiqueta(aviso)), Vigencia(aviso, cita, ahora),
            Urgente: RecordatoriosDeCita.Tipos.Contains(aviso.Tipo));

        int entregados = 0, vencidos = 0, reintentables = 0;
        foreach (var destino in destinos)
        {
            ResultadoPush r;
            try
            {
                r = await _enviador.EnviarAsync(destino, saliente, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;   // apagado: queda «Enviando» y otro lo retoma al vencer la reserva
            }
            catch (Exception)
            {
                r = ResultadoPush.Reintentable;
            }

            switch (r)
            {
                case ResultadoPush.Entregado:
                    entregados++;
                    destino.UltimoUsoEn = _reloj.Ahora;
                    break;
                case ResultadoPush.Vencida:
                    vencidos++;
                    _ctx.SuscripcionesPush.Remove(destino);
                    break;
                case ResultadoPush.Reintentable:
                    reintentables++;
                    break;
            }
        }

        if (vencidos > 0)
            _log.LogInformation("Push: {N} navegador(es) vencidos borrados (aviso {Id}).", vencidos, aviso.Id);

        if (entregados > 0)
        {
            aviso.Estado = EstadoEnvio.Enviada;
            aviso.EnviadaEn = _reloj.Ahora;
            aviso.UltimoError = null;
            await _ctx.SaveChangesAsync(ct);
            _log.LogInformation("Push {Tipo} enviado (aviso {Id}, {N} navegador(es)).", aviso.Tipo, aviso.Id, entregados);
        }
        else if (reintentables > 0 && aviso.Intentos < _o.MaximoIntentos)
        {
            var espera = Espera(aviso.Intentos);
            aviso.Estado = EstadoEnvio.Programada;
            aviso.ProgramadaPara = _reloj.Ahora.Add(espera);
            aviso.UltimoError = "servicio de push no disponible";
            await _ctx.SaveChangesAsync(ct);
            _log.LogWarning("Push {Tipo} no salió (aviso {Id}, intento {N}); se reintenta en {Espera}.",
                aviso.Tipo, aviso.Id, aviso.Intentos, espera);
        }
        else if (vencidos == destinos.Count)
        {
            await CerrarAsync(aviso, EstadoEnvio.Anulada, "sin navegadores vigentes", _reloj.Ahora, ct);
        }
        else
        {
            await CerrarAsync(aviso, EstadoEnvio.Fallida, "rechazado por el servicio de push", _reloj.Ahora, ct);
            _log.LogError("Push {Tipo} fallido para siempre (aviso {Id}).", aviso.Tipo, aviso.Id);
        }
    }

    /// <summary>La otra parte y la hora, si el aviso es de una cita. Del lado de quien lo recibe.</summary>
    private async Task<DatosDeCitaPush?> DatosDeCitaAsync(Notificacion aviso, CancellationToken ct)
    {
        if (aviso.CitaId is not { } citaId) return null;
        var c = await _ctx.Citas.AsNoTracking().Where(x => x.Id == citaId)
            .Select(x => new
            {
                Inicio = x.BloqueHorario!.InicioEn,
                x.BuscadorId,
                Buscador = x.Buscador!.Nombre,
                Prestador = x.PerfilPrestador!.NombrePublico,
            })
            .SingleOrDefaultAsync(ct);
        if (c is null) return null;
        var esBuscador = aviso.DestinatarioId == c.BuscadorId;
        return new DatosDeCitaPush(c.Inicio, esBuscador ? c.Prestador : c.Buscador);
    }

    private async Task<bool> RecordatorioVigenteAsync(Notificacion aviso, DateTime ahora, CancellationToken ct)
    {
        if (aviso.CitaId is not { } citaId) return false;
        var cita = await _ctx.Citas.AsNoTracking().Where(c => c.Id == citaId)
            .Select(c => new { c.Estado, c.CreadaEn, Inicio = c.BloqueHorario!.InicioEn })
            .SingleOrDefaultAsync(ct);
        return cita is not null
            && RecordatoriosDeCita.Corresponde(aviso.Tipo, cita.Estado, cita.Inicio, cita.CreadaEn, ahora);
    }

    /// <summary>Un aviso de la misma cita reemplaza al anterior en la bandeja, en vez de apilarse.</summary>
    private static string Etiqueta(Notificacion aviso) =>
        aviso.CitaId is { } c ? $"zass-cita-{c:N}" : $"zass-{aviso.Tipo.ToString().ToLowerInvariant()}";

    /// <summary>
    /// Cuanto puede esperar el servicio de push a que el navegador se conecte. Un recordatorio no
    /// sirve despues de la hora de la cita; lo demas, un dia.
    /// </summary>
    internal static TimeSpan Vigencia(Notificacion aviso, DatosDeCitaPush? cita, DateTime ahora) =>
        RecordatoriosDeCita.Tipos.Contains(aviso.Tipo) && cita is not null
            ? TimeSpan.FromSeconds(Math.Max(60, (cita.InicioUtc - ahora).TotalSeconds))
            : TimeSpan.FromDays(1);

    internal TimeSpan Espera(int intentos)
    {
        var segundos = _o.EsperaBaseSegundos * Math.Pow(2, Math.Max(0, intentos - 1));
        return TimeSpan.FromSeconds(Math.Min(segundos, _o.EsperaMaximaSegundos));
    }

    private async Task CerrarAsync(Notificacion aviso, EstadoEnvio estado, string? motivo, DateTime ahora, CancellationToken ct)
    {
        aviso.Estado = estado;
        aviso.UltimoError = motivo;
        aviso.ProgramadaPara = ahora;
        await _ctx.SaveChangesAsync(ct);
    }
}
