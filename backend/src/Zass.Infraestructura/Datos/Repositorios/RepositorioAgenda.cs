using Microsoft.EntityFrameworkCore;
using Zass.Aplicacion.Puertos;
using Zass.Dominio.Agenda;
using Zass.Dominio.Comun;
using Zass.Dominio.Reputacion;

namespace Zass.Infraestructura.Datos.Repositorios;

/// <summary>
/// TBL-21. Mismo patrón que <see cref="ColaNotificacionesEfCore"/>: deja la fila y se va.
/// Quien la escribe (MOD-06) no sabe quién la lee (MOD-11), y así no hay ciclo.
/// </summary>
public sealed class ContactosEfCore : IContactos
{
    private readonly ZassDbContext _ctx;
    public ContactosEfCore(ZassDbContext ctx) => _ctx = ctx;

    public void Registrar(Contacto contacto) => _ctx.Contactos.Add(contacto);
}

/// <summary>
/// MOD-06 contra PostgreSQL. Lo que distingue a este repositorio de los demás son los tres
/// métodos que bloquean filas: son la única parte de CC-01 y CC-06 que no puede vivir en la
/// capa de aplicación, porque la da el motor y no el lenguaje.
/// </summary>
public sealed class RepositorioAgendaEfCore : IRepositorioAgenda
{
    private readonly ZassDbContext _ctx;
    public RepositorioAgendaEfCore(ZassDbContext ctx) => _ctx = ctx;

    // ------------------------------------------------------- TBL-16 disponibilidad

    public async Task<IReadOnlyList<Disponibilidad>> DisponibilidadesDeServicioAsync(
        Guid prestadorServicioId, CancellationToken ct = default) =>
        await _ctx.Disponibilidades
            .Where(d => d.PrestadorServicioId == prestadorServicioId)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<Disponibilidad>> DisponibilidadesDePrestadorAsync(
        Guid perfilPrestadorId, CancellationToken ct = default) =>
        await _ctx.Disponibilidades
            .Where(d => d.PerfilPrestadorId == perfilPrestadorId)
            .ToListAsync(ct);

    public void AgregarDisponibilidad(Disponibilidad disponibilidad) =>
        _ctx.Disponibilidades.Add(disponibilidad);

    // ------------------------------------------------------- TBL-17 bloque_horario

    public async Task<IReadOnlyList<BloqueHorario>> BloquesDeServicioAsync(
        Guid prestadorServicioId, DateTime desde, DateTime hasta, CancellationToken ct = default) =>
        await _ctx.Bloques
            .Where(b => b.PrestadorServicioId == prestadorServicioId
                        && b.InicioEn >= desde && b.InicioEn < hasta)
            .OrderBy(b => b.InicioEn)
            .ToListAsync(ct);

    public async Task<IReadOnlyCollection<DateTime>> IniciosYaGeneradosAsync(
        Guid prestadorServicioId, DateTime desde, DateTime hasta, CancellationToken ct = default) =>
        await _ctx.Bloques
            .Where(b => b.PrestadorServicioId == prestadorServicioId
                        && b.InicioEn >= desde && b.InicioEn < hasta)
            .Select(b => b.InicioEn)
            .ToListAsync(ct);

    public void AgregarBloques(IEnumerable<BloqueHorario> bloques) =>
        _ctx.Bloques.AddRange(bloques);

    /// <summary>
    /// RN-S4-09. Se borran por SQL y no trayéndolos a memoria: son hasta cientos de filas y
    /// el único criterio es `estado = 'libre'`, que la base evalúa mejor que esta capa. La
    /// condición de estado es la regla entera: un bloque reservado NUNCA se toca aquí.
    /// </summary>
    public Task<int> QuitarBloquesLibresDeAsync(
        Guid disponibilidadId, DateTime desde, CancellationToken ct = default) =>
        _ctx.Bloques
            .Where(b => b.DisponibilidadId == disponibilidadId
                        && b.InicioEn >= desde
                        && b.Estado == EstadoBloque.Libre)
            .ExecuteDeleteAsync(ct);

    // ------------------------------------------------------- TBL-20 bloqueo_agenda

    public async Task<IReadOnlyList<BloqueoAgenda>> BloqueosDePrestadorAsync(
        Guid perfilPrestadorId, DateTime desde, DateTime hasta, CancellationToken ct = default) =>
        await _ctx.BloqueosAgenda
            .Where(x => x.PerfilPrestadorId == perfilPrestadorId
                        && x.DesdeEn < hasta && desde < x.HastaEn)
            .OrderBy(x => x.DesdeEn)
            .ToListAsync(ct);

    public void AgregarBloqueo(BloqueoAgenda bloqueo) => _ctx.BloqueosAgenda.Add(bloqueo);

    // ------------------------------------------------------- CC-01

    /// <summary>
    /// El <c>FOR UPDATE</c> de CC-01, textual. Va por SQL porque EF Core no sabe expresarlo:
    /// no hay un `.ForUpdate()` en LINQ, y escribirlo con un `UPDATE ... RETURNING` cambiaría
    /// la semántica (bloquear para leer no es escribir).
    ///
    /// La entidad vuelve RASTREADA, así que el <c>Reservar()</c> que hace el servicio se
    /// convierte en el UPDATE del paso 10 al confirmar la transacción.
    /// </summary>
    public Task<BloqueHorario?> TomarBloqueAsync(Guid bloqueId, CancellationToken ct = default) =>
        _ctx.Bloques
            .FromSqlInterpolated($"SELECT * FROM bloques WHERE id = {bloqueId} FOR UPDATE")
            .AsTracking()
            .FirstOrDefaultAsync(ct);

    public Task<bool> TieneCitaActivaAsync(Guid bloqueId, CancellationToken ct = default) =>
        _ctx.Citas.AnyAsync(
            c => c.BloqueHorarioId == bloqueId
                 && (c.Estado == EstadoCita.Solicitada || c.Estado == EstadoCita.Confirmada), ct);

    // ------------------------------------------------------- TBL-18 cita, TBL-19 cambio_cita

    /// <summary>
    /// Con el bloque, el servicio y el perfil: son lo que la pantalla muestra de una cita, y
    /// pedirlos después serían tres consultas por fila.
    /// </summary>
    private IQueryable<Cita> Completa() =>
        _ctx.Citas
            .Include(c => c.BloqueHorario)
            .Include(c => c.PrestadorServicio).ThenInclude(ps => ps!.Servicio)
            .Include(c => c.PerfilPrestador);

    public Task<Cita?> CitaPorIdAsync(Guid id, CancellationToken ct = default) =>
        Completa().FirstOrDefaultAsync(c => c.Id == id, ct);

    public Task<Cita?> CitaPorClaveAsync(
        string claveIdempotencia, CancellationToken ct = default) =>
        Completa().FirstOrDefaultAsync(c => c.ClaveIdempotencia == claveIdempotencia, ct);

    public Task<int> CitasActivasEntreAsync(
        Guid buscadorId, Guid perfilPrestadorId, CancellationToken ct = default) =>
        _ctx.Citas.CountAsync(
            c => c.BuscadorId == buscadorId
                 && c.PerfilPrestadorId == perfilPrestadorId
                 && (c.Estado == EstadoCita.Solicitada || c.Estado == EstadoCita.Confirmada), ct);

    /// <summary>
    /// Incluye <c>NoAsistio</c>: si faltó el prestador, el buscador puede reseñar igual
    /// (nota de estados-cita.md). Quién faltó lo resuelve MOD-11 con el contacto.
    /// </summary>
    public Task<bool> HuboCitaRealizadaAsync(
        Guid buscadorId, Guid perfilPrestadorId, CancellationToken ct = default) =>
        _ctx.Citas.AnyAsync(
            c => c.BuscadorId == buscadorId
                 && c.PerfilPrestadorId == perfilPrestadorId
                 && (c.Estado == EstadoCita.Realizada || c.Estado == EstadoCita.NoAsistio), ct);

    public async Task<IReadOnlyList<Cita>> CitasDeBuscadorAsync(
        Guid buscadorId, CancellationToken ct = default) =>
        await Completa().Where(c => c.BuscadorId == buscadorId).ToListAsync(ct);

    public async Task<IReadOnlyList<Cita>> CitasDePrestadorAsync(
        Guid perfilPrestadorId, CancellationToken ct = default) =>
        await Completa().Where(c => c.PerfilPrestadorId == perfilPrestadorId).ToListAsync(ct);

    // ------------------------------------------------------- A-09

    public async Task<IReadOnlyList<Guid>> CitasPorExpirarAsync(
        DateTime ahora, int maximo, CancellationToken ct = default) =>
        await _ctx.Citas
            .Where(c => c.Estado == EstadoCita.Solicitada && c.ExpiraEn != null && c.ExpiraEn <= ahora)
            .OrderBy(c => c.ExpiraEn).Select(c => c.Id).Take(maximo).ToListAsync(ct);

    public async Task<IReadOnlyList<Guid>> CitasTerminadasAsync(
        DateTime ahora, int maximo, CancellationToken ct = default) =>
        await _ctx.Citas
            .Where(c => c.Estado == EstadoCita.Confirmada && c.BloqueHorario!.FinEn <= ahora)
            .OrderBy(c => c.BloqueHorario!.FinEn).Select(c => c.Id).Take(maximo).ToListAsync(ct);

    public async Task<IReadOnlyList<Guid>> CitasSinAsistenciaAsync(
        DateTime limite, int maximo, CancellationToken ct = default) =>
        await _ctx.Citas
            .Where(c => c.Estado == EstadoCita.PendienteAsistencia && c.BloqueHorario!.FinEn <= limite)
            .OrderBy(c => c.BloqueHorario!.FinEn).Select(c => c.Id).Take(maximo).ToListAsync(ct);

    /// <summary>
    /// Rastreada y con el bloque, para poder cambiarle el estado y liberar o vencer la hora
    /// en la misma transacción.
    /// </summary>
    public Task<Cita?> CitaParaCambioAsync(Guid id, CancellationToken ct = default) =>
        _ctx.Citas
            .Include(c => c.BloqueHorario)
            .Include(c => c.PrestadorServicio).ThenInclude(ps => ps!.Servicio)
            .Include(c => c.PerfilPrestador)
            .AsTracking()
            .FirstOrDefaultAsync(c => c.Id == id, ct);

    // ------------------------------------------------------- CC-06

    /// <summary>
    /// <c>ORDER BY id FOR UPDATE</c>, y el ORDER BY es la técnica entera: con un orden global
    /// dos reprogramaciones cruzadas piden el mismo bloque primero, una espera a la otra y
    /// las dos terminan. Sin él, PostgreSQL detecta el ciclo y mata una transacción, que
    /// funciona pero le entrega al usuario un error que no debería existir.
    ///
    /// Si los dos identificadores son el mismo, <c>IN</c> devuelve una fila: quien llama ya
    /// comprueba que origen y destino sean distintos.
    /// </summary>
    public async Task<IReadOnlyList<BloqueHorario>> TomarDosBloquesAsync(
        Guid unoId, Guid otroId, CancellationToken ct = default) =>
        await _ctx.Bloques
            .FromSqlInterpolated(
                $"SELECT * FROM bloques WHERE id IN ({unoId}, {otroId}) ORDER BY id FOR UPDATE")
            .AsTracking()
            .ToListAsync(ct);

    /// <summary>
    /// Local a la transacción, no a la conexión: la conexión vuelve al grupo y la reutiliza
    /// otra solicitud, que no tiene por qué heredar este límite.
    ///
    /// Va por <c>set_config(..., is_local =&gt; true)</c> y no por <c>SET LOCAL</c> porque
    /// <c>SET</c> no admite parámetros y habría que interpolar el valor en el SQL. Siendo una
    /// función, el valor viaja como parámetro y no hay nada que interpolar.
    /// </summary>
    public Task FijarEsperaDeBloqueoAsync(TimeSpan limite, CancellationToken ct = default)
    {
        var ms = $"{Math.Max(1, (int)limite.TotalMilliseconds)}ms";
        return _ctx.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT set_config('lock_timeout', {ms}, true)", ct);
    }

    public void AgregarCita(Cita cita) => _ctx.Citas.Add(cita);
    public void AgregarCambio(CambioCita cambio) => _ctx.CambiosCita.Add(cambio);
}
