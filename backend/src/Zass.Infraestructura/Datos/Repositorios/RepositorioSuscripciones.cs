using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using NpgsqlTypes;
using Zass.Aplicacion.Puertos;
using Zass.Dominio.Comun;
using Zass.Dominio.Suscripciones;

namespace Zass.Infraestructura.Datos.Repositorios;

/// <summary>
/// MOD-13 contra PostgreSQL.
///
/// Todos los filtros de vigencia se escriben de forma que EF Core los pueda traducir a
/// SQL. Es deliberado: <c>Suscripcion.EstaVigente</c> es un metodo de C# y usarlo en un
/// <c>Where</c> obligaria a traer la tabla entera a memoria para filtrarla despues. La
/// condicion esta duplicada, y la prueba de integracion comprueba que las dos digan lo
/// mismo.
/// </summary>
public sealed class RepositorioSuscripcionesEfCore : IRepositorioSuscripciones
{
    private readonly ZassDbContext _ctx;
    public RepositorioSuscripcionesEfCore(ZassDbContext ctx) => _ctx = ctx;

    public Task<Suscripcion?> PorIdAsync(Guid id, CancellationToken ct = default) =>
        _ctx.Suscripciones.FirstOrDefaultAsync(s => s.Id == id, ct);

    public Task<Suscripcion?> VigenteDePrestadorAsync(
        Guid perfilPrestadorId, DateTime ahora, CancellationToken ct = default) =>
        _ctx.Suscripciones
            .Include(s => s.Plan)
            .FirstOrDefaultAsync(
                s => s.PerfilPrestadorId == perfilPrestadorId
                     && (s.Estado == EstadoSuscripcion.Prueba
                         || s.Estado == EstadoSuscripcion.Activa)
                     && s.VigenteHasta > ahora,
                ct);

    public Task<Suscripcion?> UltimaDePrestadorAsync(
        Guid perfilPrestadorId, CancellationToken ct = default) =>
        _ctx.Suscripciones
            .Include(s => s.Plan)
            .Where(s => s.PerfilPrestadorId == perfilPrestadorId)
            .OrderByDescending(s => s.IniciaEn)
            .FirstOrDefaultAsync(ct);

    /// <summary>
    /// La consulta que usa la busqueda. Una sola ida a la base, y devuelve solo la
    /// columna del identificador: no hace falta traer la suscripcion entera para
    /// responder "si" o "no".
    /// </summary>
    public async Task<IReadOnlyCollection<Guid>> VigentesEntreAsync(
        IReadOnlyCollection<Guid> perfilPrestadorIds, DateTime ahora,
        CancellationToken ct = default)
    {
        var ids = perfilPrestadorIds as Guid[] ?? perfilPrestadorIds.ToArray();

        return await _ctx.Suscripciones
            .Where(s => ids.Contains(s.PerfilPrestadorId)
                        && (s.Estado == EstadoSuscripcion.Prueba
                            || s.Estado == EstadoSuscripcion.Activa)
                        && s.VigenteHasta > ahora)
            .Select(s => s.PerfilPrestadorId)
            .Distinct()
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<Suscripcion>> ExpiradasSinMarcarAsync(
        DateTime ahora, CancellationToken ct = default) =>
        await _ctx.Suscripciones
            .Where(s => (s.Estado == EstadoSuscripcion.Prueba
                         || s.Estado == EstadoSuscripcion.Activa)
                        && s.VigenteHasta <= ahora)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<Suscripcion>> VigentesQueVencenAntesDeAsync(
        DateTime ahora, DateTime hasta, CancellationToken ct = default) =>
        await _ctx.Suscripciones
            .Where(s => (s.Estado == EstadoSuscripcion.Prueba
                         || s.Estado == EstadoSuscripcion.Activa)
                        && s.VigenteHasta > ahora
                        && s.VigenteHasta <= hasta)
            .OrderBy(s => s.VigenteHasta)
            .ToListAsync(ct);

    public Task<Plan?> PlanPorCodigoAsync(string codigo, CancellationToken ct = default) =>
        _ctx.Planes.FirstOrDefaultAsync(p => p.Codigo == codigo && p.Activo, ct);

    public async Task<IReadOnlyList<Plan>> PlanesActivosAsync(CancellationToken ct = default) =>
        await _ctx.Planes
            .Where(p => p.Activo)
            .OrderBy(p => p.PrecioMensual)
            .ToListAsync(ct);

    /// <summary>
    /// Pregunta si el perfil existe sin traerlo. Tampoco usa IRepositorioPerfiles: eso
    /// haria que MOD-13 dependiera de un puerto de MOD-01 para un EXISTS.
    /// </summary>
    public Task<bool> ExistePerfilPrestadorAsync(
        Guid perfilPrestadorId, CancellationToken ct = default) =>
        _ctx.PerfilesPrestador.AnyAsync(p => p.Id == perfilPrestadorId, ct);

    /// <summary>
    /// UNA consulta para todos los perfiles. Misma razon que ExistePerfilPrestadorAsync para
    /// no pasar por IRepositorioPerfiles: seria depender de un puerto de MOD-01 para leer una
    /// columna.
    /// </summary>
    public async Task<IReadOnlyDictionary<Guid, Guid>> UsuariosDePerfilesAsync(
        IReadOnlyCollection<Guid> perfilPrestadorIds, CancellationToken ct = default)
    {
        if (perfilPrestadorIds.Count == 0) return new Dictionary<Guid, Guid>();
        var filas = await _ctx.PerfilesPrestador
            .Where(p => perfilPrestadorIds.Contains(p.Id))
            .Select(p => new { p.Id, p.UsuarioId })
            .ToListAsync(ct);
        return filas.ToDictionary(f => f.Id, f => f.UsuarioId);
    }

    public void Agregar(Suscripcion suscripcion) => _ctx.Suscripciones.Add(suscripcion);
    public void AgregarPago(Pago pago) => _ctx.Pagos.Add(pago);
    // ------------------------------------------------------------ Cupos fundador
    //
    // TBL-33. Cada operacion es UNA sentencia SQL atomica que se ejecuta de inmediato,
    // sin pasar por GuardarAsync: dos personas pagando a la vez no pueden quedarse con
    // el mismo cupo, y eso solo lo garantiza la base, no una lectura previa en C#.
    //
    // Un cupo esta libre si no tiene perfil, o si su reserva ya vencio
    // (reservado_hasta <= ahora). Confirmado = perfil y reservado_hasta nulo: nunca vence.

    public Task<int> CuposFundadorDisponiblesAsync(
        DateTime ahora, CancellationToken ct = default) =>
        _ctx.CuposFundador.CountAsync(
            c => c.PerfilPrestadorId == null
                 || (c.ReservadoHasta != null && c.ReservadoHasta <= ahora),
            ct);

    public async Task<short?> IntentarReservarCupoFundadorAsync(
        Guid perfilPrestadorId, DateTime ahora, DateTime hasta, CancellationToken ct = default)
    {
        // 1) Si el perfil ya tiene cupo, se devuelve el mismo (idempotente). Si estaba
        //    solo reservado se renueva el plazo; si estaba confirmado no se toca el plazo.
        var propio = await RenovarCupoPropioAsync(perfilPrestadorId, hasta, ct);
        if (propio is not null) return propio;

        // 2) Si no, se toma el libre mas bajo. SKIP LOCKED: quien llega mientras otro esta
        //    tomando un cupo pasa al siguiente en vez de esperar o de llevarse el mismo.
        //
        //    Bajo mucha concurrencia la sentencia puede devolver 0 filas aunque queden
        //    cupos (otra solicitud se llevo la fila justo antes del bloqueo). Por eso un
        //    "null" no basta para decir "no hay": se cuenta y se reintenta, acotado.
        for (var intento = 0; intento < 10; intento++)
        {
            try
            {
                var tomado = await TomarCupoLibreAsync(perfilPrestadorId, ahora, hasta, ct);
                if (tomado is not null) return tomado;
            }
            catch (PostgresException e) when (e.SqlState == PostgresErrorCodes.UniqueViolation)
            {
                // Dos solicitudes del MISMO perfil a la vez: el indice unico parcial deja
                // pasar una. La que pierde devuelve el cupo que gano la otra.
                return await RenovarCupoPropioAsync(perfilPrestadorId, hasta, ct);
            }

            if (await CuposFundadorDisponiblesAsync(ahora, ct) == 0) return null;
        }

        return null;
    }

    public async Task<bool> ConfirmarCupoFundadorAsync(
        Guid perfilPrestadorId, CancellationToken ct = default) =>
        await EjecutarAsync(
            @"UPDATE cupos_fundador
                 SET reservado_hasta = NULL
               WHERE perfil_prestador_id = @perfil",
            ct, Uuid("perfil", perfilPrestadorId)) > 0;

    public async Task<bool> LiberarCupoFundadorAsync(
        Guid perfilPrestadorId, CancellationToken ct = default) =>
        await EjecutarAsync(
            @"UPDATE cupos_fundador
                 SET perfil_prestador_id = NULL, reservado_hasta = NULL
               WHERE perfil_prestador_id = @perfil",
            ct, Uuid("perfil", perfilPrestadorId)) > 0;

    public Task<int> LiberarReservasFundadorVencidasAsync(
        DateTime ahora, CancellationToken ct = default) =>
        EjecutarAsync(
            @"UPDATE cupos_fundador
                 SET perfil_prestador_id = NULL, reservado_hasta = NULL
               WHERE perfil_prestador_id IS NOT NULL
                 AND reservado_hasta IS NOT NULL
                 AND reservado_hasta <= @ahora",
            ct, Marca("ahora", ahora));

    private Task<short?> RenovarCupoPropioAsync(
        Guid perfilPrestadorId, DateTime hasta, CancellationToken ct) =>
        EscalarAsync(
            @"UPDATE cupos_fundador
                 SET reservado_hasta = CASE WHEN reservado_hasta IS NULL THEN NULL ELSE @hasta END
               WHERE perfil_prestador_id = @perfil
           RETURNING numero",
            ct, Uuid("perfil", perfilPrestadorId), Marca("hasta", hasta));

    private Task<short?> TomarCupoLibreAsync(
        Guid perfilPrestadorId, DateTime ahora, DateTime hasta, CancellationToken ct) =>
        EscalarAsync(
            @"UPDATE cupos_fundador
                 SET perfil_prestador_id = @perfil, reservado_hasta = @hasta
               WHERE numero = (SELECT numero
                                 FROM cupos_fundador
                                WHERE perfil_prestador_id IS NULL
                                   OR (reservado_hasta IS NOT NULL AND reservado_hasta <= @ahora)
                                ORDER BY numero
                                LIMIT 1
                                  FOR UPDATE SKIP LOCKED)
                 AND (perfil_prestador_id IS NULL
                      OR (reservado_hasta IS NOT NULL AND reservado_hasta <= @ahora))
           RETURNING numero",
            ct, Uuid("perfil", perfilPrestadorId), Marca("hasta", hasta), Marca("ahora", ahora));

    private async Task<short?> EscalarAsync(
        string sql, CancellationToken ct, params NpgsqlParameter[] parametros)
    {
        await _ctx.Database.OpenConnectionAsync(ct);
        try
        {
            await using var cmd = _ctx.Database.GetDbConnection().CreateCommand();
            cmd.CommandText = sql;
            cmd.Transaction = _ctx.Database.CurrentTransaction?.GetDbTransaction();
            foreach (var p in parametros) cmd.Parameters.Add(p);
            var r = await cmd.ExecuteScalarAsync(ct);
            return r is short n ? n : null;
        }
        finally
        {
            await _ctx.Database.CloseConnectionAsync();
        }
    }

    private async Task<int> EjecutarAsync(
        string sql, CancellationToken ct, params NpgsqlParameter[] parametros)
    {
        await _ctx.Database.OpenConnectionAsync(ct);
        try
        {
            await using var cmd = _ctx.Database.GetDbConnection().CreateCommand();
            cmd.CommandText = sql;
            cmd.Transaction = _ctx.Database.CurrentTransaction?.GetDbTransaction();
            foreach (var p in parametros) cmd.Parameters.Add(p);
            return await cmd.ExecuteNonQueryAsync(ct);
        }
        finally
        {
            await _ctx.Database.CloseConnectionAsync();
        }
    }

    private static NpgsqlParameter Uuid(string nombre, Guid valor) =>
        new(nombre, NpgsqlDbType.Uuid) { Value = valor };

    private static NpgsqlParameter Marca(string nombre, DateTime valor) =>
        new(nombre, NpgsqlDbType.TimestampTz) { Value = valor };
}
