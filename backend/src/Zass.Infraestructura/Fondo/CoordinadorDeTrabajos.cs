using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;
using Zass.Infraestructura.Datos;

namespace Zass.Infraestructura.Fondo;

/// <summary>
/// A-09 · CC-08. Quién corre el trabajo programado cuando la API tiene varias réplicas
/// (N-02: de 2 a 10 en producción).
///
/// <para>
/// <b>Se aparta del documento, a propósito.</b> CC-08 prescribe
/// <c>pg_try_advisory_lock</c> «sin tabla de bloqueos», y esto es una tabla (TBL-32
/// <c>trabajos_de_fondo</c>). El bloqueo consultivo resuelve <i>que no corran dos a la vez</i>,
/// pero no recuerda <i>que ya corrió hoy</i>: se suelta al terminar, así que el temporizador
/// de cada réplica volvería a disparar el trabajo, y un despliegue a media mañana lo
/// repetiría. Con eso, «avisar antes de que venza» descansaría entero en que la clave única
/// del aviso choque decenas de veces al día: funciona, pero por colisión, no por diseño.
/// La fila dice las dos cosas —cuándo toca y quién lo tiene— y encima deja ver que el
/// trabajo vive (<c>corridas</c>, <c>ultimo_resumen</c>), que con un bloqueo consultivo no
/// se puede saber. Ver la nota de CC-08 en el documento, que quedó corregida.
/// </para>
///
/// La reserva <b>vence</b>: si la réplica muere a mitad del trabajo, otra lo retoma pasado
/// el plazo en vez de que el trabajo quede trabado para siempre.
/// </summary>
public sealed class CoordinadorDeTrabajos
{
    private readonly ZassDbContext _ctx;

    public CoordinadorDeTrabajos(ZassDbContext ctx) => _ctx = ctx;

    /// <summary>
    /// Toma el trabajo si le toca y nadie lo tiene. Devuelve false sin esperar: la réplica
    /// que no lo consigue sigue de largo, no se queda bloqueada.
    ///
    /// Es UNA instrucción, y ahí está la garantía: dos réplicas que entren en el mismo
    /// milisegundo se serializan en la fila y solo una recibe resultado. No hay ventana
    /// entre «comprobar» y «tomar» porque no son dos pasos.
    ///
    /// El <c>INSERT</c> crea la fila la primera vez con <c>proxima_corrida_en</c> en el
    /// pasado, para que el trabajo corra al arrancar y no al día siguiente.
    /// </summary>
    public async Task<bool> TomarAsync(
        string nombre, DateTime ahora, TimeSpan reserva, CancellationToken ct = default)
    {
        const string sql = """
            INSERT INTO trabajos_de_fondo
                   (nombre, proxima_corrida_en, reservado_hasta, corridas)
            VALUES (@nombre, @ahora, @hasta, 0)
            ON CONFLICT (nombre) DO UPDATE
               SET reservado_hasta = @hasta
             WHERE trabajos_de_fondo.proxima_corrida_en <= @ahora
               AND trabajos_de_fondo.reservado_hasta <= @ahora
            RETURNING nombre
            """;

        await _ctx.Database.OpenConnectionAsync(ct);
        try
        {
            await using var cmd = _ctx.Database.GetDbConnection().CreateCommand();
            cmd.CommandText = sql;
            cmd.Parameters.Add(new NpgsqlParameter("nombre", NpgsqlDbType.Varchar) { Value = nombre });
            cmd.Parameters.Add(new NpgsqlParameter("ahora", NpgsqlDbType.TimestampTz) { Value = ahora });
            cmd.Parameters.Add(new NpgsqlParameter("hasta", NpgsqlDbType.TimestampTz)
                { Value = ahora + reserva });
            return await cmd.ExecuteScalarAsync(ct) is not null;
        }
        finally
        {
            await _ctx.Database.CloseConnectionAsync();
        }
    }

    /// <summary>
    /// Suelta el trabajo y fija cuándo vuelve a tocarle. Se llama siempre, haya salido bien
    /// o mal: dejar la reserva puesta solo retrasaría el próximo intento hasta que venza.
    ///
    /// El resumen son números y nunca datos de una persona: lo lee cualquiera que mire la tabla.
    /// </summary>
    public async Task LiberarAsync(
        string nombre, DateTime ahora, DateTime proxima, string resumen,
        bool conteoDeCorrida, CancellationToken ct = default)
    {
        const string sql = """
            UPDATE trabajos_de_fondo
               SET proxima_corrida_en = @proxima,
                   reservado_hasta    = @ahora,
                   ultima_corrida_en  = @ahora,
                   corridas           = corridas + @suma,
                   ultimo_resumen     = @resumen
             WHERE nombre = @nombre
            """;

        await _ctx.Database.ExecuteSqlRawAsync(sql, new object[]
        {
            new NpgsqlParameter("nombre", NpgsqlDbType.Varchar) { Value = nombre },
            new NpgsqlParameter("ahora", NpgsqlDbType.TimestampTz) { Value = ahora },
            new NpgsqlParameter("proxima", NpgsqlDbType.TimestampTz) { Value = proxima },
            new NpgsqlParameter("suma", NpgsqlDbType.Integer) { Value = conteoDeCorrida ? 1 : 0 },
            new NpgsqlParameter("resumen", NpgsqlDbType.Varchar)
            {
                Value = resumen.Length > 300 ? resumen[..300] : resumen,
            },
        }, ct);
    }
}
