using Microsoft.EntityFrameworkCore;
using Npgsql;
using Microsoft.EntityFrameworkCore.Storage;
using NpgsqlTypes;
using Zass.Aplicacion.Puertos;
using Zass.Dominio.Identidad;

namespace Zass.Infraestructura.Datos.Repositorios;

/// <summary>
/// La purga de cuentas sin verificar contra PostgreSQL. SQL a mano porque la condición recorre
/// tablas de casi todos los módulos y tiene que ser la MISMA en la lista, en el conteo y en el
/// DELETE (que la vuelve a evaluar, así que no hay ventana entre «comprobar» y «borrar»).
///
/// <para>
/// Las claves foráneas hacia <c>usuarios</c> borran en cascada roles, sesiones, avisos, push y los
/// dos perfiles; hacia <c>perfiles_prestador</c>, todo lo del prestador. Por eso la condición
/// excluye CUALQUIER fila hija de un perfil de prestador: lo que se borra en cascada es solo lo
/// que crea el registro. Si mañana aparece una tabla nueva que cuelga de usuarios o del perfil,
/// hay que sumarla aquí (la prueba de integración de la purga recorre las claves foráneas reales
/// y falla si alguna no está mirada).
/// </para>
/// </summary>
public sealed class RepositorioCuentasSinVerificarEfCore : IRepositorioCuentasSinVerificar
{
    /// <summary>
    /// Las tablas que la condición mira, por la columna con que apuntan a la cuenta o a su perfil.
    /// Pública para que la prueba de integración la compare con las claves foráneas de la base.
    /// </summary>
    public static readonly string[] TablasMiradas =
    {
        "citas", "contactos", "resenas", "reportes_resena", "verificaciones", "cambios_cita",
        "usuario_roles", "credenciales", "suscripciones", "intentos_de_pago", "tarjetas",
        "cupos_fundador", "canjes_de_cupon", "fotos_de_trabajo", "coberturas", "prestador_servicios",
        "disponibilidades", "bloques", "bloqueos_agenda",
        // Las que se borran con la cuenta a propósito: las crea el registro o no son actividad.
        "perfiles_prestador", "perfiles_buscador", "sesiones", "notificaciones", "suscripciones_push",
        "preferencias_aviso",
    };

    /// <summary>La condición «purgable», sobre <c>u</c> (usuarios). Parámetro: @limite.</summary>
    private const string Purgable = """
        u.estado = 'PendienteVerificacion'
        AND NOT u.correo_verificado
        AND u.creado_en <= @limite
        AND NOT EXISTS (SELECT 1 FROM usuario_roles ur JOIN roles r ON r.id = ur.rol_id
                        WHERE ur.usuario_id = u.id AND r.es_interno)
        AND NOT EXISTS (SELECT 1 FROM usuario_roles ur WHERE ur.asignado_por = u.id)
        AND NOT EXISTS (SELECT 1 FROM citas x WHERE x.buscador_id = u.id)
        AND NOT EXISTS (SELECT 1 FROM cambios_cita x WHERE x.realizado_por = u.id)
        AND NOT EXISTS (SELECT 1 FROM contactos x WHERE x.buscador_id = u.id)
        AND NOT EXISTS (SELECT 1 FROM resenas x WHERE x.autor_id = u.id)
        AND NOT EXISTS (SELECT 1 FROM reportes_resena x WHERE x.reportado_por = u.id OR x.resuelto_por = u.id)
        AND NOT EXISTS (SELECT 1 FROM verificaciones x WHERE x.verificador_id = u.id)
        AND NOT EXISTS (SELECT 1 FROM credenciales x WHERE x.sello_por = u.id OR x.reservada_por = u.id)
        AND NOT EXISTS (
            SELECT 1 FROM perfiles_prestador p
            WHERE p.usuario_id = u.id
              AND (p.estado <> 'Borrador'
                OR EXISTS (SELECT 1 FROM credenciales x WHERE x.perfil_prestador_id = p.id)
                OR EXISTS (SELECT 1 FROM suscripciones x WHERE x.perfil_prestador_id = p.id)
                OR EXISTS (SELECT 1 FROM intentos_de_pago x WHERE x.perfil_prestador_id = p.id)
                OR EXISTS (SELECT 1 FROM tarjetas x WHERE x.perfil_prestador_id = p.id)
                OR EXISTS (SELECT 1 FROM cupos_fundador x WHERE x.perfil_prestador_id = p.id)
                OR EXISTS (SELECT 1 FROM canjes_de_cupon x WHERE x.perfil_prestador_id = p.id)
                OR EXISTS (SELECT 1 FROM citas x WHERE x.perfil_prestador_id = p.id)
                OR EXISTS (SELECT 1 FROM contactos x WHERE x.perfil_prestador_id = p.id)
                OR EXISTS (SELECT 1 FROM resenas x WHERE x.perfil_prestador_id = p.id)
                OR EXISTS (SELECT 1 FROM fotos_de_trabajo x WHERE x.perfil_prestador_id = p.id)
                OR EXISTS (SELECT 1 FROM coberturas x WHERE x.perfil_prestador_id = p.id)
                OR EXISTS (SELECT 1 FROM prestador_servicios x WHERE x.perfil_prestador_id = p.id)
                OR EXISTS (SELECT 1 FROM disponibilidades x WHERE x.perfil_prestador_id = p.id)
                OR EXISTS (SELECT 1 FROM bloques x WHERE x.perfil_prestador_id = p.id)
                OR EXISTS (SELECT 1 FROM bloqueos_agenda x WHERE x.perfil_prestador_id = p.id)))
        """;

    /// <summary>Sin verificar y pasada de plazo, sin mirar la actividad.</summary>
    private const string Caducada = """
        u.estado = 'PendienteVerificacion' AND NOT u.correo_verificado AND u.creado_en <= @limite
        """;

    private readonly ZassDbContext _ctx;
    public RepositorioCuentasSinVerificarEfCore(ZassDbContext ctx) => _ctx = ctx;

    public async Task<IReadOnlyList<Guid>> PurgablesAsync(
        DateTime creadasAntesDe, int limite, CancellationToken ct = default)
    {
        var ids = new List<Guid>();
        await ConComandoAsync($"""
            SELECT u.id FROM usuarios u WHERE {Purgable}
            ORDER BY u.creado_en, u.id LIMIT @max
            """, creadasAntesDe, async cmd =>
        {
            cmd.Parameters.Add(new NpgsqlParameter("max", NpgsqlDbType.Integer) { Value = limite });
            await using var lector = await cmd.ExecuteReaderAsync(ct);
            while (await lector.ReadAsync(ct)) ids.Add(lector.GetGuid(0));
        }, ct);
        return ids;
    }

    public async Task<int> ContarRetenidasAsync(DateTime creadasAntesDe, CancellationToken ct = default)
    {
        var total = 0;
        await ConComandoAsync($"""
            SELECT count(*) FROM usuarios u WHERE {Caducada} AND NOT ({Purgable})
            """, creadasAntesDe, async cmd => total = Convert.ToInt32(await cmd.ExecuteScalarAsync(ct)), ct);
        return total;
    }

    public async Task<bool> PurgarAsync(Guid usuarioId, DateTime creadasAntesDe, CancellationToken ct = default)
    {
        var filas = await _ctx.Database.ExecuteSqlRawAsync(
            $"DELETE FROM usuarios u WHERE u.id = @id AND {Purgable}",
            new object[]
            {
                new NpgsqlParameter("id", NpgsqlDbType.Uuid) { Value = usuarioId },
                new NpgsqlParameter("limite", NpgsqlDbType.TimestampTz) { Value = creadasAntesDe },
            }, ct);
        if (filas == 1) Soltar(usuarioId);
        return filas == 1;
    }

    /// <summary>
    /// El DELETE va directo a la base. Si este contexto tenía cargada la cuenta (el registro la
    /// acaba de leer por correo o por RUT), se suelta del seguimiento para que nadie la guarde.
    /// </summary>
    private void Soltar(Guid usuarioId)
    {
        foreach (var e in _ctx.ChangeTracker.Entries<Usuario>().Where(e => e.Entity.Id == usuarioId).ToList())
            e.State = EntityState.Detached;
        foreach (var e in _ctx.ChangeTracker.Entries<UsuarioRol>().Where(e => e.Entity.UsuarioId == usuarioId).ToList())
            e.State = EntityState.Detached;
    }

    /// <summary>
    /// Usa la conexión (y la transacción, si hay una abierta) del contexto: las pruebas de
    /// integración envuelven todo en una transacción que después se deshace.
    /// </summary>
    private async Task ConComandoAsync(
        string sql, DateTime limite, Func<System.Data.Common.DbCommand, Task> usar, CancellationToken ct)
    {
        await _ctx.Database.OpenConnectionAsync(ct);
        try
        {
            await using var cmd = _ctx.Database.GetDbConnection().CreateCommand();
            cmd.CommandText = sql;
            cmd.Transaction = _ctx.Database.CurrentTransaction?.GetDbTransaction();
            cmd.Parameters.Add(new NpgsqlParameter("limite", NpgsqlDbType.TimestampTz) { Value = limite });
            await usar(cmd);
        }
        finally
        {
            await _ctx.Database.CloseConnectionAsync();
        }
    }
}
