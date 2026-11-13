using Microsoft.EntityFrameworkCore;
using Zass.Aplicacion.Puertos;
using Zass.Dominio.Agenda;
using Zass.Dominio.Comun;
using Zass.Dominio.Reputacion;

namespace Zass.Infraestructura.Datos.Repositorios;

/// <summary>
/// MOD-11 contra PostgreSQL.
///
/// <b>Por qué hay SQL a mano en dos sitios.</b> El cerrojo (`FOR UPDATE`) y el recálculo de la
/// reputación tienen que ocurrir DENTRO del motor y en la misma transacción que la reseña
/// (CC-07); LINQ no expresa ninguna de las dos cosas.
///
/// <b>Por qué el recálculo cuenta en vez de sumar uno</b> (la spec de CC-07 muestra
/// `total + 1` y `(prom * n + c) / (n + 1)`). La columna `promedio_calificacion` es
/// `numeric(2,1)`: guarda el promedio YA redondeado a un decimal, y partir de un promedio
/// redondeado acumula error con cada reseña (5, 5 y 4 dan 4,7; sumarle un 5 da 4,775, y el
/// promedio real es 4,75). Además la fórmula relativa no sirve para editar ni retirar. Lo que sí
/// da el mismo resultado sin deriva es tomar el cerrojo de la fila del prestador y recalcular
/// desde TBL-22 con una instrucción nueva: tras esperar el cerrojo, esa instrucción ve todo lo
/// que confirmaron las anteriores. Es la misma garantía —nada se pierde— sin el error de redondeo.
/// </summary>
public sealed class RepositorioResenasEfCore : IRepositorioResenas
{
    private readonly ZassDbContext _ctx;
    public RepositorioResenasEfCore(ZassDbContext ctx) => _ctx = ctx;

    public async Task<T> EnTransaccionAsync<T>(
        Func<Task<T>> trabajo, Func<T, bool> confirmar, CancellationToken ct = default)
    {
        // Ya hay una (las pruebas de integración envuelven todo en una que siempre se deshace):
        // se corre dentro de ella. Abrir otra lanzaría, y confirmar la ajena sería peor.
        if (_ctx.Database.CurrentTransaction is not null) return await trabajo();

        await using var transaccion = await _ctx.Database.BeginTransactionAsync(ct);
        var resultado = await trabajo();
        if (confirmar(resultado))
        {
            await transaccion.CommitAsync(ct);
        }
        else
        {
            await transaccion.RollbackAsync(ct);
            // Lo que el contexto tenía pendiente nació de una transacción que ya no existe; si
            // se quedara, el siguiente SaveChanges (el de la auditoría del rechazo) lo insertaría.
            _ctx.ChangeTracker.Clear();
        }
        return resultado;
    }

    public Task BloquearPerfilAsync(Guid perfilPrestadorId, CancellationToken ct = default) =>
        _ctx.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT 1 FROM perfiles_prestador WHERE id = {perfilPrestadorId} FOR UPDATE", ct);

    public Task RecalcularReputacionAsync(Guid perfilPrestadorId, CancellationToken ct = default) =>
        _ctx.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE perfiles_prestador p
               SET total_resenas = r.total,
                   promedio_calificacion = r.promedio
              FROM (
                    SELECT count(*)::int AS total,
                           CASE WHEN count(*) = 0 THEN NULL
                                ELSE round(avg(calificacion)::numeric, 1) END AS promedio
                      FROM resenas
                     WHERE perfil_prestador_id = {perfilPrestadorId}
                       AND estado = 'Publicada'
                   ) r
             WHERE p.id = {perfilPrestadorId}
            """, ct);

    public async Task<DatosDePerfil?> PerfilAsync(Guid perfilPrestadorId, CancellationToken ct = default) =>
        await _ctx.PerfilesPrestador.AsNoTracking()
            .Where(p => p.Id == perfilPrestadorId)
            .Select(p => new DatosDePerfil(p.Id, p.UsuarioId, p.Estado, p.NombrePublico))
            .FirstOrDefaultAsync(ct);

    public async Task<Contacto?> ContactoAsync(Guid contactoId, CancellationToken ct = default)
    {
        var contacto = await _ctx.Contactos.AsNoTracking().Include(c => c.Cita)
            .FirstOrDefaultAsync(c => c.Id == contactoId, ct);
        if (contacto is not null) await SeguirLaCadenaAsync(new[] { contacto }, ct);
        return contacto;
    }

    public async Task<IReadOnlyList<Contacto>> ContactosSinResenaAsync(
        Guid buscadorId, Guid perfilPrestadorId, CancellationToken ct = default)
    {
        var contactos = await _ctx.Contactos.AsNoTracking().Include(c => c.Cita)
            .Where(c => c.BuscadorId == buscadorId && c.PerfilPrestadorId == perfilPrestadorId
                        && c.Resena == null)
            .OrderBy(c => c.OcurridoEn).ThenBy(c => c.Id)
            .ToListAsync(ct);
        await SeguirLaCadenaAsync(contactos, ct);
        return contactos;
    }

    /// <summary>
    /// RN-S4-05 y estados-cita.md, sección 4. El contacto de una cita apunta a la PRIMERA de la
    /// cadena de reprogramaciones, y esa queda <c>Reprogramada</c> en cuanto se mueve la hora.
    /// Si la elegibilidad mirara solo ese estado, quien reprograma y luego es atendido perdería
    /// el derecho a reseñar, que es justo lo que la regla existe para impedir.
    ///
    /// Por eso <c>cita_origen_id</c> apunta siempre a la primera y no a la anterior: la cita
    /// vigente de la cadena está a UN salto, sin recorrer eslabones. Aquí se sustituye la cita
    /// del contacto por esa, y la regla de <c>ReglasDeResenas</c> sigue siendo una función pura
    /// sobre un contacto cuya cita ya es la que importa.
    ///
    /// UNA consulta para todos los contactos, por la misma razón que el resto de este código:
    /// no hacer una ida a la base por contacto.
    /// </summary>
    private async Task SeguirLaCadenaAsync(IReadOnlyCollection<Contacto> contactos, CancellationToken ct)
    {
        var origenes = contactos
            .Where(c => c.Cita is { Estado: EstadoCita.Reprogramada })
            .Select(c => c.CitaId!.Value).Distinct().ToList();
        if (origenes.Count == 0) return;

        var sucesoras = await _ctx.Citas.AsNoTracking()
            .Where(c => c.CitaOrigenId != null && origenes.Contains(c.CitaOrigenId.Value))
            .ToListAsync(ct);

        foreach (var contacto in contactos.Where(c => c.Cita is { Estado: EstadoCita.Reprogramada }))
        {
            // La vigente es la ultima creada: las anteriores de la cadena estan todas cerradas
            // como Reprogramada, y a lo sumo una sigue abierta o llego a su desenlace.
            var vigente = sucesoras
                .Where(c => c.CitaOrigenId == contacto.CitaId)
                .OrderByDescending(c => c.CreadaEn).ThenByDescending(c => c.Id)
                .FirstOrDefault();
            if (vigente is not null) contacto.Cita = vigente;
        }
    }

    public async Task<DateTime?> UltimaResenaPorContactoAsync(
        Guid autorId, Guid perfilPrestadorId, DateTime desde, CancellationToken ct = default) =>
        await _ctx.Resenas.AsNoTracking()
            .Where(r => r.AutorId == autorId && r.PerfilPrestadorId == perfilPrestadorId
                        && r.Contacto!.CitaId == null && r.PublicadaEn > desde)
            .Select(r => (DateTime?)r.PublicadaEn)
            .OrderByDescending(f => f)
            .FirstOrDefaultAsync(ct);

    public Task<Resena?> ResenaDeContactoAsync(Guid contactoId, CancellationToken ct = default) =>
        _ctx.Resenas.AsNoTracking().FirstOrDefaultAsync(r => r.ContactoId == contactoId, ct);

    public Task<Resena?> ResenaPorIdAsync(Guid resenaId, CancellationToken ct = default) =>
        _ctx.Resenas.AsNoTracking().FirstOrDefaultAsync(r => r.Id == resenaId, ct);

    public Task<Resena?> ResenaParaActualizarAsync(Guid resenaId, CancellationToken ct = default) =>
        _ctx.Resenas.Include(r => r.Respuesta).FirstOrDefaultAsync(r => r.Id == resenaId, ct);

    public Task<Contacto?> ContactoDeCitaAsync(Guid citaId, CancellationToken ct = default) =>
        _ctx.Contactos.AsNoTracking().FirstOrDefaultAsync(c => c.CitaId == citaId, ct);

    public Task<Cita?> CitaAsync(Guid citaId, CancellationToken ct = default) =>
        _ctx.Citas.AsNoTracking().FirstOrDefaultAsync(c => c.Id == citaId, ct);

    public async Task<IReadOnlyList<FilaPublica>> PublicasAsync(
        Guid perfilPrestadorId, DateTime? antesDe, Guid? antesDeId, int limite,
        CancellationToken ct = default)
    {
        var consulta = _ctx.Resenas.AsNoTracking().Include(r => r.Respuesta)
            .Where(r => r.PerfilPrestadorId == perfilPrestadorId && r.Estado == EstadoResena.Publicada);

        if (antesDe is { } fecha && antesDeId is { } id)
            consulta = consulta.Where(r => r.PublicadaEn < fecha
                                           || (r.PublicadaEn == fecha && r.Id.CompareTo(id) < 0));

        // Del contacto solo se trae SI hubo cita, no el contacto: así el dato que sale hacia
        // fuera es el único que puede salir, y no una entidad entera de la que alguien podría
        // mapear un id sin darse cuenta (CU-16 §7).
        return await consulta
            .OrderByDescending(r => r.PublicadaEn).ThenByDescending(r => r.Id)
            .Take(limite)
            .Select(r => new FilaPublica(r, r.Contacto!.CitaId != null))
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<FilaDeMisResenas>> MiasAsync(
        Guid autorId, int limite, CancellationToken ct = default) =>
        // El filtro por autor es lo ÚNICO que separa mis reseñas de las de cualquier otro: no
        // hay un segundo control más abajo, así que no puede faltar aquí.
        await _ctx.Resenas.AsNoTracking()
            .Where(r => r.AutorId == autorId)
            .OrderByDescending(r => r.PublicadaEn).ThenByDescending(r => r.Id)
            .Take(limite)
            .Select(r => new FilaDeMisResenas(
                r, r.Contacto!.CitaId != null, r.PerfilPrestador!.NombrePublico, r.PerfilPrestador!.Slug))
            .ToListAsync(ct);

    public Task<bool> TieneReporteAbiertoAsync(Guid resenaId, Guid usuarioId, CancellationToken ct = default) =>
        _ctx.ReportesResena.AnyAsync(
            r => r.ResenaId == resenaId && r.ReportadoPor == usuarioId
                 && (r.Estado == EstadoReporte.Abierto || r.Estado == EstadoReporte.EnRevision), ct);

    public Task<ReporteResena?> ReportePorIdAsync(Guid reporteId, CancellationToken ct = default) =>
        _ctx.ReportesResena.AsNoTracking().FirstOrDefaultAsync(r => r.Id == reporteId, ct);

    public async Task<IReadOnlyList<ReporteResena>> ReportesAbiertosDeResenaAsync(
        Guid resenaId, CancellationToken ct = default) =>
        await _ctx.ReportesResena
            .Where(r => r.ResenaId == resenaId
                        && (r.Estado == EstadoReporte.Abierto || r.Estado == EstadoReporte.EnRevision))
            .OrderBy(r => r.CreadoEn).ThenBy(r => r.Id)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<FilaDeCola>> ColaAsync(
        Guid moderadorId, int limite, CancellationToken ct = default) =>
        await _ctx.ReportesResena.AsNoTracking()
            .Where(r => (r.Estado == EstadoReporte.Abierto || r.Estado == EstadoReporte.EnRevision)
                        && r.ReportadoPor != moderadorId
                        && r.Resena!.AutorId != moderadorId
                        && r.Resena.PerfilPrestador!.UsuarioId != moderadorId)
            .OrderByDescending(r => r.Motivo == MotivoReporte.DatosPersonales
                                    || r.Motivo == MotivoReporte.LenguajeDeOdio)
            .ThenBy(r => r.CreadoEn).ThenBy(r => r.Id)
            .Take(limite)
            .Select(r => new FilaDeCola(
                r.Id, r.ResenaId, r.Motivo, r.Detalle, r.Estado, r.CreadoEn,
                r.Resena!.Calificacion, r.Resena.Comentario, r.Resena.Estado,
                r.Resena.PerfilPrestador!.NombrePublico))
            .ToListAsync(ct);

    public void Agregar(Contacto contacto) => _ctx.Contactos.Add(contacto);
    public void Agregar(Resena resena) => _ctx.Resenas.Add(resena);
    public void Agregar(RespuestaResena respuesta) => _ctx.RespuestasResena.Add(respuesta);
    public void Agregar(ReporteResena reporte) => _ctx.ReportesResena.Add(reporte);
}
