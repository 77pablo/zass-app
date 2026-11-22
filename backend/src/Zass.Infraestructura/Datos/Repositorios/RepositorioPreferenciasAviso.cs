using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;
using Zass.Aplicacion.Notificaciones;
using Zass.Dominio.Comun;

namespace Zass.Infraestructura.Datos.Repositorios;

/// <summary>ES-3b · API-43. Las preferencias de aviso, en <c>preferencias_aviso</c> (solo excepciones).</summary>
public sealed class RepositorioPreferenciasAvisoEfCore : IRepositorioPreferenciasAviso
{
    private readonly ZassDbContext _ctx;
    public RepositorioPreferenciasAvisoEfCore(ZassDbContext ctx) => _ctx = ctx;

    public Task<List<PreferenciaAviso>> DeAsync(Guid usuarioId, CancellationToken ct = default) =>
        _ctx.PreferenciasAviso.AsNoTracking().Where(p => p.UsuarioId == usuarioId).ToListAsync(ct);

    /// <summary>
    /// Dos sentencias y ninguna lectura previa: borra los tipos que quedan con los dos canales
    /// encendidos y hace upsert del resto. Con leer-y-despues-insertar, dos peticiones a la vez
    /// (un doble clic) insertarian las dos y la segunda seria un 500 por la clave primaria.
    /// </summary>
    public async Task FijarAsync(Guid usuarioId, IReadOnlyCollection<CambioDePreferencia> cambios, DateTime ahora,
        CancellationToken ct = default)
    {
        var porOmision = cambios.Where(c => c.Correo && c.Push).Select(c => c.Tipo.ToString()).ToArray();
        var excepciones = cambios.Where(c => !(c.Correo && c.Push)).ToArray();

        if (porOmision.Length > 0)
            await _ctx.Database.ExecuteSqlRawAsync(
                "DELETE FROM preferencias_aviso WHERE usuario_id = @usuario AND tipo = ANY(@tipos)",
                new object[] { new NpgsqlParameter("usuario", usuarioId), Texto("tipos", porOmision) }, ct);

        if (excepciones.Length > 0)
            await _ctx.Database.ExecuteSqlRawAsync("""
                INSERT INTO preferencias_aviso (usuario_id, tipo, correo, push, actualizada_en)
                SELECT @usuario, x.tipo, x.correo, x.push, @ahora
                  FROM unnest(@tipos, @correos, @pushes) AS x(tipo, correo, push)
                ON CONFLICT (usuario_id, tipo)
                DO UPDATE SET correo = EXCLUDED.correo, push = EXCLUDED.push, actualizada_en = EXCLUDED.actualizada_en
                """,
                new object[]
                {
                    new NpgsqlParameter("usuario", usuarioId),
                    new NpgsqlParameter("ahora", NpgsqlDbType.TimestampTz) { Value = ahora },
                    Texto("tipos", excepciones.Select(c => c.Tipo.ToString()).ToArray()),
                    new NpgsqlParameter("correos", NpgsqlDbType.Array | NpgsqlDbType.Boolean)
                        { Value = excepciones.Select(c => c.Correo).ToArray() },
                    new NpgsqlParameter("pushes", NpgsqlDbType.Array | NpgsqlDbType.Boolean)
                        { Value = excepciones.Select(c => c.Push).ToArray() },
                }, ct);
    }

    private static NpgsqlParameter Texto(string nombre, string[] valores) =>
        new(nombre, NpgsqlDbType.Array | NpgsqlDbType.Varchar) { Value = valores };
}
