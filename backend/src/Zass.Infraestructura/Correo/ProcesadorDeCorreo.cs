using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using NpgsqlTypes;
using Zass.Aplicacion.Puertos;
using Zass.Dominio.Comun;
using Zass.Infraestructura.Configuracion;
using Zass.Infraestructura.Datos;

namespace Zass.Infraestructura.Correo;

/// <summary>
/// Un ciclo de despacho: toma un lote de avisos de correo pendientes, los renderiza, los
/// envía y deja cada uno enviado, reprogramado o fallido. Es una clase aparte del
/// BackgroundService para poder probarla con la base real y un HttpMessageHandler falso,
/// sin esperar relojes de verdad.
///
/// Sin columnas nuevas: <c>programada_para</c> hace de «próximo intento» y de reserva
/// (lease) mientras un trabajador tiene el aviso; <c>intentos</c> y <c>ultimo_error</c>
/// ya existían en TBL-27.
/// </summary>
public sealed class ProcesadorDeCorreo
{
    /// <summary>
    /// Se deriva de <see cref="Plantillas.TienePlantilla"/> y no se escribe a mano: una lista
    /// copiada se desincroniza en cuanto alguien agrega una plantilla y no se entera de que
    /// ademas hay que apuntarla aqui (es lo que pasaba con los avisos de suscripcion).
    /// </summary>
    private static readonly string[] TiposConPlantilla =
        Enum.GetValues<TipoNotificacion>().Where(Plantillas.TienePlantilla)
            .Select(t => t.ToString()).ToArray();

    /// <summary>Solo de estos hay carga que purgar: los demas no guardan ningun secreto.</summary>
    private static readonly string[] TiposConToken =
        Enum.GetValues<TipoNotificacion>().Where(Plantillas.LlevaToken)
            .Select(t => t.ToString()).ToArray();

    private readonly ZassDbContext _ctx;
    private readonly IEnviadorDeCorreo _enviador;
    private readonly IRelojSistema _reloj;
    private readonly OpcionesCorreo _o;
    private readonly OpcionesZass _zass;
    private readonly ILogger<ProcesadorDeCorreo> _log;

    public ProcesadorDeCorreo(
        ZassDbContext ctx, IEnviadorDeCorreo enviador, IRelojSistema reloj,
        IOptions<OpcionesCorreo> o, IOptions<OpcionesZass> zass, ILogger<ProcesadorDeCorreo> log)
    {
        _ctx = ctx;
        _enviador = enviador;
        _reloj = reloj;
        _o = o.Value;
        _zass = zass.Value;
        _log = log;
    }

    /// <summary>La URL pública bien formada, o null si falta (entonces no se envía nada).</summary>
    public static string? UrlPublicaValida(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var u) && u.Scheme is "https" or "http"
            ? url!.TrimEnd('/')
            : null;

    /// <summary>Devuelve cuántos avisos tomó (no cuántos salieron bien).</summary>
    public async Task<int> ProcesarLoteAsync(CancellationToken ct = default)
    {
        var urlPublica = UrlPublicaValida(_zass.UrlPublica);
        if (urlPublica is null)
        {
            // No se toma nada: así los avisos siguen pendientes, sin gastar intentos, y salen
            // solos cuando alguien configure Zass:UrlPublica.
            _log.LogWarning(
                "No se envía correo: falta Zass:UrlPublica (p. ej. https://zass.cl) y sin ella los enlaces "
                + "de los correos no se pueden armar. Los avisos quedan pendientes.");
            return 0;
        }

        await PurgarAsync(ct);
        await AnularCaducadosAsync(ct);

        var ids = await TomarLoteAsync(ct);
        foreach (var id in ids)
        {
            ct.ThrowIfCancellationRequested();
            await ProcesarUnoAsync(id, urlPublica, ct);
            _ctx.ChangeTracker.Clear();
        }
        return ids.Count;
    }

    /// <summary>
    /// Reserva un lote con <c>FOR UPDATE SKIP LOCKED</c>: dos réplicas que consultan a la vez
    /// reciben filas distintas en vez de esperarse o repetirse. El bloqueo dura solo lo que
    /// tarda este UPDATE; la reserva que protege el envío es el estado «Enviando» con
    /// <c>programada_para</c> en el futuro. Si el trabajador muere a mitad, vencida la
    /// reserva otro retoma el aviso (y el intento ya quedó contado).
    ///
    /// La selección va en una CTE <c>MATERIALIZED</c> y no en <c>WHERE id IN (subconsulta)</c>:
    /// con estadísticas de tabla casi vacía PostgreSQL puede planificar el IN como un Nested Loop
    /// Semi Join que reejecuta la subconsulta por cada fila, y entonces el UPDATE toma más filas
    /// que el LIMIT (J1: 10 con lote 5, visto en CI con PG 17.5 y en local con PG 18).
    /// MATERIALIZED obliga a evaluarla una sola vez.
    /// </summary>
    internal async Task<List<Guid>> TomarLoteAsync(CancellationToken ct)
    {
        var ahora = _reloj.Ahora;
        const string sql = """
            WITH lote AS MATERIALIZED (
                SELECT id FROM notificaciones
                WHERE canal = 'Correo' AND tipo = ANY(@tipos)
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
                { Value = TiposConPlantilla });

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

    /// <summary>
    /// Anula los avisos que llevan demasiado tiempo esperando, en vez de mandarlos tarde.
    ///
    /// Encolar y enviar están separados a propósito, y un tipo sin plantilla se queda en la cola
    /// sin gastar intentos hasta que alguien la escriba. El problema es el día que esa plantilla
    /// llega: TODO lo acumulado se vuelve enviable de golpe y la gente recibe el aviso de algo que
    /// hizo hace semanas. Pasó con PerfilPublicado, encolándose sin plantilla desde el primer día.
    ///
    /// La condición es <c>intentos = 0</c>, y no es un detalle: en cuanto un aviso se reserva,
    /// <c>programada_para</c> pasa a valer la hora de la reserva y deja de decir cuándo se encoló.
    /// Con cero intentos sigue siendo la original, que es justo el caso que hay que cazar. Los que
    /// ya se intentaron están acotados por MaximoIntentos y no pueden quedarse años en la cola.
    ///
    /// Se anula sin purgar el cuerpo aquí: de eso se encarga PurgarAsync pasado su plazo, que es
    /// quien sabe qué tipos llevan secreto.
    /// </summary>
    private async Task AnularCaducadosAsync(CancellationToken ct)
    {
        var limite = _reloj.Ahora.AddDays(-_o.CaducaDias);
        var n = await _ctx.Database.ExecuteSqlRawAsync(
            "UPDATE notificaciones SET estado = 'Anulada' " +
            "WHERE canal = 'Correo' AND estado = 'Programada' AND intentos = 0 " +
            "AND programada_para < @limite",
            new object[] { new NpgsqlParameter("limite", NpgsqlDbType.TimestampTz) { Value = limite } },
            ct);

        if (n > 0)
        {
            _log.LogInformation(
                "{Cuantos} aviso(s) anulados por llevar mas de {Dias} dias esperando en la cola.",
                n, _o.CaducaDias);
        }
    }

    /// <summary>
    /// Un aviso que fracasó para siempre (o se anuló) conserva su carga un plazo, por si hay
    /// que investigar, y luego pierde el token en claro. El token ya habrá vencido: dura 1 h
    /// (recuperación) o 24 h (verificación).
    /// </summary>
    private async Task PurgarAsync(CancellationToken ct)
    {
        var limite = _reloj.Ahora.AddHours(-_o.PurgaDeFallidasHoras);
        var n = await _ctx.Database.ExecuteSqlRawAsync(
            "UPDATE notificaciones SET cuerpo = @purgada " +
            "WHERE estado IN ('Fallida', 'Anulada') AND tipo = ANY(@tipos) " +
            "AND programada_para < @limite AND cuerpo <> @purgada",
            new object[]
            {
                new NpgsqlParameter("purgada", CargaDeAviso.Purgada),
                new NpgsqlParameter("limite", NpgsqlDbType.TimestampTz) { Value = limite },
                new NpgsqlParameter("tipos", NpgsqlDbType.Array | NpgsqlDbType.Varchar) { Value = TiposConToken },
            }, ct);
        if (n > 0) _log.LogInformation("Se purgó la carga de {N} aviso(s) fallidos o anulados.", n);
    }

    private async Task ProcesarUnoAsync(Guid id, string urlPublica, CancellationToken ct)
    {
        var aviso = await _ctx.Notificaciones.Include(n => n.Destinatario)
            .SingleOrDefaultAsync(n => n.Id == id, ct);
        if (aviso is null) return;

        var ahora = _reloj.Ahora;
        var usuario = aviso.Destinatario;
        var destino = Enmascarar.Correo(usuario?.Correo);

        // Pasado el tope de intentos (p. ej. un trabajador murió varias veces con este aviso).
        if (aviso.Intentos > _o.MaximoIntentos)
        {
            await CerrarComoFallidaAsync(aviso, "máximo de intentos agotado", ahora, ct);
            _log.LogError("Aviso {Id} ({Tipo}) para {Destino} fallido: máximo de intentos.", aviso.Id, aviso.Tipo, destino);
            return;
        }

        // Ya no tiene sentido: la cuenta se eliminó, o ya verificó su correo por otro medio.
        // (Los avisos demasiado viejos NO se filtran aquí: ver AnularCaducadosAsync. Para cuando
        // un aviso llega a este método, `programada_para` ya vale la hora de la RESERVA, no la de
        // cuando se encoló, así que aquí no hay forma de saber su edad.)
        if (usuario is null || usuario.Estado == EstadoCuenta.Eliminada || usuario.AnonimizadoEn is not null
            || (aviso.Tipo == TipoNotificacion.CorreoVerificacion && usuario.CorreoVerificado))
        {
            aviso.Estado = EstadoEnvio.Anulada;
            aviso.Cuerpo = CargaDeAviso.Purgada;
            aviso.ProgramadaPara = ahora;
            await _ctx.SaveChangesAsync(ct);
            _log.LogInformation("Aviso {Id} ({Tipo}) anulado: ya no corresponde enviarlo.", aviso.Id, aviso.Tipo);
            return;
        }

        // La plantilla lee de la carga lo que necesita: el token, o los dias que quedan. Si no
        // esta (una carga ya purgada, por ejemplo) no hay correo que armar y no se reintenta.
        var correo = Plantillas.Renderizar(aviso.Tipo, usuario.Nombre, urlPublica, aviso.Cuerpo);
        if (correo is null)
        {
            await CerrarComoFallidaAsync(aviso, "carga incompleta para su plantilla", ahora, ct);
            _log.LogError("Aviso {Id} ({Tipo}) fallido: la carga no trae lo que pide su plantilla.",
                aviso.Id, aviso.Tipo);
            return;
        }

        ResultadoDeEnvio r;
        try
        {
            r = await _enviador.EnviarAsync(
                new CorreoSaliente(usuario.Correo, usuario.Nombre, correo.Asunto, correo.Texto, correo.Html), ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Apagado: el aviso queda «Enviando» y otro lo retoma al vencer la reserva.
            throw;
        }
        catch (Exception ex)
        {
            // Solo el tipo: el mensaje de una excepción puede traer URL, cabeceras o la dirección.
            r = new(ResultadoEnvio.Reintentable, $"error inesperado ({ex.GetType().Name})");
        }

        switch (r.Resultado)
        {
            case ResultadoEnvio.Enviado:
                aviso.Estado = EstadoEnvio.Enviada;
                aviso.EnviadaEn = _reloj.Ahora;
                aviso.UltimoError = null;
                // El token deja de existir en claro. Los avisos sin secreto conservan su carga:
                // purgarla solo borraria «cuantos dias quedaban», que sirve para entender el envio.
                if (Plantillas.LlevaToken(aviso.Tipo)) aviso.Cuerpo = CargaDeAviso.Purgada;
                await _ctx.SaveChangesAsync(ct);
                _log.LogInformation("Correo {Tipo} enviado a {Destino} (aviso {Id}, intento {N}).",
                    aviso.Tipo, destino, aviso.Id, aviso.Intentos);
                break;

            case ResultadoEnvio.Reintentable when aviso.Intentos < _o.MaximoIntentos:
                var espera = Espera(aviso.Intentos);
                aviso.Estado = EstadoEnvio.Programada;
                aviso.ProgramadaPara = _reloj.Ahora.Add(espera);
                aviso.UltimoError = r.Detalle;
                await _ctx.SaveChangesAsync(ct);
                _log.LogWarning("Correo {Tipo} a {Destino} no salió (aviso {Id}, intento {N}: {Detalle}); "
                    + "se reintenta en {Espera}.", aviso.Tipo, destino, aviso.Id, aviso.Intentos, r.Detalle, espera);
                break;

            default:
                await CerrarComoFallidaAsync(aviso, r.Detalle ?? "rechazado", _reloj.Ahora, ct);
                _log.LogError("Correo {Tipo} a {Destino} fallido para siempre (aviso {Id}, intento {N}: {Detalle}).",
                    aviso.Tipo, destino, aviso.Id, aviso.Intentos, r.Detalle);
                break;
        }
    }

    /// <summary>Espera creciente: base, 2×base, 4×base… con tope.</summary>
    internal TimeSpan Espera(int intentos)
    {
        var segundos = _o.EsperaBaseSegundos * Math.Pow(2, Math.Max(0, intentos - 1));
        return TimeSpan.FromSeconds(Math.Min(segundos, _o.EsperaMaximaSegundos));
    }

    private async Task CerrarComoFallidaAsync(Notificacion aviso, string motivo, DateTime ahora, CancellationToken ct)
    {
        aviso.Estado = EstadoEnvio.Fallida;
        aviso.UltimoError = motivo;
        // programada_para marca el momento del fallo: desde ahí corre el plazo de la purga.
        aviso.ProgramadaPara = ahora;
        await _ctx.SaveChangesAsync(ct);
    }
}
