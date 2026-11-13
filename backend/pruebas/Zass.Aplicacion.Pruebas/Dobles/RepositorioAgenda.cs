using Zass.Aplicacion.Puertos;
using Zass.Dominio.Agenda;
using Zass.Dominio.Comun;
using Zass.Dominio.Reputacion;

namespace Zass.Aplicacion.Pruebas.Dobles;

/// <summary>TBL-21 en memoria. Lo que MOD-06 deja para que MOD-11 lo lea.</summary>
public sealed class ContactosEnMemoria : IContactos
{
    private readonly BaseEnMemoria _base;
    public ContactosEnMemoria(BaseEnMemoria baseEnMemoria) => _base = baseEnMemoria;

    public void Registrar(Contacto contacto) => _base.Contactos.Add(contacto);
}

/// <summary>
/// MOD-06 en memoria.
///
/// **No imita el FOR UPDATE, y no puede.** <see cref="TomarBloqueAsync"/> devuelve el bloque
/// sin bloquear nada: con listas en memoria no existe la pregunta «¿qué pasa si dos
/// transacciones llegan a la vez?». Esa pregunta —CC-01 y R-1— se contesta en las pruebas de
/// integración, contra PostgreSQL. Aquí se comprueban las REGLAS: anticipación, tope de
/// citas, modo del servicio, suscripción vigente, idempotencia.
/// </summary>
public sealed class RepositorioAgendaEnMemoria : IRepositorioAgenda
{
    private readonly BaseEnMemoria _base;
    public RepositorioAgendaEnMemoria(BaseEnMemoria baseEnMemoria) => _base = baseEnMemoria;

    /// <summary>Cuántas veces se pidió el bloqueo de fila. Hay una prueba que lo cuenta.</summary>
    public int BloqueosDeFila { get; private set; }

    // ------------------------------------------------------- TBL-16

    public Task<IReadOnlyList<Disponibilidad>> DisponibilidadesDeServicioAsync(
        Guid prestadorServicioId, CancellationToken ct = default)
    {
        IReadOnlyList<Disponibilidad> r = _base.Disponibilidades
            .Where(d => d.PrestadorServicioId == prestadorServicioId).ToList();
        return Task.FromResult(r);
    }

    public Task<IReadOnlyList<Disponibilidad>> DisponibilidadesDePrestadorAsync(
        Guid perfilPrestadorId, CancellationToken ct = default)
    {
        IReadOnlyList<Disponibilidad> r = _base.Disponibilidades
            .Where(d => d.PerfilPrestadorId == perfilPrestadorId).ToList();
        return Task.FromResult(r);
    }

    public void AgregarDisponibilidad(Disponibilidad disponibilidad) =>
        _base.Disponibilidades.Add(disponibilidad);

    // ------------------------------------------------------- TBL-17

    public Task<IReadOnlyList<BloqueHorario>> BloquesDeServicioAsync(
        Guid prestadorServicioId, DateTime desde, DateTime hasta, CancellationToken ct = default)
    {
        IReadOnlyList<BloqueHorario> r = _base.Bloques
            .Where(b => b.PrestadorServicioId == prestadorServicioId
                        && b.InicioEn >= desde && b.InicioEn < hasta)
            .OrderBy(b => b.InicioEn).ToList();
        return Task.FromResult(r);
    }

    public Task<IReadOnlyCollection<DateTime>> IniciosYaGeneradosAsync(
        Guid prestadorServicioId, DateTime desde, DateTime hasta, CancellationToken ct = default)
    {
        IReadOnlyCollection<DateTime> r = _base.Bloques
            .Where(b => b.PrestadorServicioId == prestadorServicioId
                        && b.InicioEn >= desde && b.InicioEn < hasta)
            .Select(b => b.InicioEn).ToList();
        return Task.FromResult(r);
    }

    public void AgregarBloques(IEnumerable<BloqueHorario> bloques) =>
        _base.Bloques.AddRange(bloques);

    public Task<int> QuitarBloquesLibresDeAsync(
        Guid disponibilidadId, DateTime desde, CancellationToken ct = default)
    {
        var fuera = _base.Bloques
            .Where(b => b.DisponibilidadId == disponibilidadId
                        && b.InicioEn >= desde
                        && b.Estado == EstadoBloque.Libre)
            .ToList();
        foreach (var b in fuera) _base.Bloques.Remove(b);
        return Task.FromResult(fuera.Count);
    }

    // ------------------------------------------------------- TBL-20

    public Task<IReadOnlyList<BloqueoAgenda>> BloqueosDePrestadorAsync(
        Guid perfilPrestadorId, DateTime desde, DateTime hasta, CancellationToken ct = default)
    {
        IReadOnlyList<BloqueoAgenda> r = _base.BloqueosAgenda
            .Where(x => x.PerfilPrestadorId == perfilPrestadorId
                        && x.DesdeEn < hasta && desde < x.HastaEn)
            .OrderBy(x => x.DesdeEn).ToList();
        return Task.FromResult(r);
    }

    public void AgregarBloqueo(BloqueoAgenda bloqueo) => _base.BloqueosAgenda.Add(bloqueo);

    public Task<IReadOnlyList<BloqueoAgenda>> BloqueosVigentesDeAsync(
        Guid perfilPrestadorId, DateTime ahora, CancellationToken ct = default)
    {
        IReadOnlyList<BloqueoAgenda> r = _base.BloqueosAgenda
            .Where(x => x.PerfilPrestadorId == perfilPrestadorId && x.HastaEn > ahora)
            .OrderBy(x => x.DesdeEn).ThenBy(x => x.Id).ToList();
        return Task.FromResult(r);
    }

    public Task<BloqueoAgenda?> BloqueoDeAsync(
        Guid perfilPrestadorId, Guid bloqueoId, CancellationToken ct = default) =>
        Task.FromResult(_base.BloqueosAgenda
            .FirstOrDefault(x => x.Id == bloqueoId && x.PerfilPrestadorId == perfilPrestadorId));

    public void QuitarBloqueo(BloqueoAgenda bloqueo) => _base.BloqueosAgenda.Remove(bloqueo);

    // ------------------------------------------------------- CC-01 (sin bloquear nada)

    public Task<BloqueHorario?> TomarBloqueAsync(Guid bloqueId, CancellationToken ct = default)
    {
        BloqueosDeFila++;
        return Task.FromResult(_base.Bloques.FirstOrDefault(b => b.Id == bloqueId));
    }

    public Task<bool> TieneCitaActivaAsync(Guid bloqueId, CancellationToken ct = default) =>
        Task.FromResult(_base.Citas.Any(c => c.BloqueHorarioId == bloqueId && c.EstaActiva()));

    // ------------------------------------------------------- TBL-18, TBL-19

    /// <summary>
    /// El repositorio real trae el bloque, el servicio y el perfil con Include; aquí se
    /// enlazan a mano para que el servicio vea lo mismo en los dos lados.
    /// </summary>
    private Cita Enlazar(Cita c)
    {
        c.BloqueHorario ??= _base.Bloques.FirstOrDefault(b => b.Id == c.BloqueHorarioId);
        c.PrestadorServicio ??= _base.PrestadorServicios
            .FirstOrDefault(ps => ps.Id == c.PrestadorServicioId);
        if (c.PrestadorServicio is not null)
            c.PrestadorServicio.Servicio ??= _base.Servicios
                .FirstOrDefault(s => s.Id == c.PrestadorServicio.ServicioId);
        c.PerfilPrestador ??= _base.PerfilesPrestador
            .FirstOrDefault(p => p.Id == c.PerfilPrestadorId);
        return c;
    }

    public Task<Cita?> CitaPorIdAsync(Guid id, CancellationToken ct = default) =>
        Task.FromResult(_base.Citas.FirstOrDefault(c => c.Id == id) is { } c ? Enlazar(c) : null);

    public Task<Cita?> CitaPorClaveAsync(
        string claveIdempotencia, CancellationToken ct = default) =>
        Task.FromResult(
            _base.Citas.FirstOrDefault(c => c.ClaveIdempotencia == claveIdempotencia) is { } c
                ? Enlazar(c) : null);

    public Task<int> CitasActivasEntreAsync(
        Guid buscadorId, Guid perfilPrestadorId, CancellationToken ct = default) =>
        Task.FromResult(_base.Citas.Count(
            c => c.BuscadorId == buscadorId
                 && c.PerfilPrestadorId == perfilPrestadorId
                 && c.EstaActiva()));

    // OJO: esta condicion es una COPIA de la de RepositorioAgendaEfCore. Las dos tienen que
    // decir lo mismo, y nada en las pruebas unitarias lo comprueba: si alguien cambiara el SQL
    // de alla, esto seguiria verde. Lo unico que ve esa diferencia es la prueba de integracion.
    public Task<bool> HuboCitaRealizadaAsync(
        Guid buscadorId, Guid perfilPrestadorId, CancellationToken ct = default) =>
        Task.FromResult(_base.Citas.Any(
            c => c.BuscadorId == buscadorId
                 && c.PerfilPrestadorId == perfilPrestadorId
                 && (c.Estado == EstadoCita.Realizada
                     || (c.Estado == EstadoCita.NoAsistio
                         && c.QuienFalto == FaltaEnCita.Prestador))));

    public Task<IReadOnlyList<Cita>> CitasEnDisputaAsync(int maximo, CancellationToken ct = default)
    {
        IReadOnlyList<Cita> r = _base.Citas
            .Where(c => c.Estado == EstadoCita.NoAsistio && c.QuienFalto == FaltaEnCita.Discrepancia)
            .OrderBy(c => c.CreadaEn).Take(maximo).ToList();
        return Task.FromResult(r);
    }

    public Task<IReadOnlyList<Cita>> CitasDeBuscadorAsync(
        Guid buscadorId, CancellationToken ct = default)
    {
        IReadOnlyList<Cita> r = _base.Citas
            .Where(c => c.BuscadorId == buscadorId).Select(Enlazar).ToList();
        return Task.FromResult(r);
    }

    public Task<IReadOnlyList<Cita>> CitasDePrestadorAsync(
        Guid perfilPrestadorId, CancellationToken ct = default)
    {
        IReadOnlyList<Cita> r = _base.Citas
            .Where(c => c.PerfilPrestadorId == perfilPrestadorId).Select(Enlazar).ToList();
        return Task.FromResult(r);
    }

    private IReadOnlyList<Guid> Ids(IEnumerable<Cita> citas, int maximo)
    {
        IReadOnlyList<Guid> r = citas.Select(c => Enlazar(c).Id).Take(maximo).ToList();
        return r;
    }

    /// <summary>
    /// Para provocar la carrera «la lista se armó y la cita cambió antes de procesarla». Con
    /// pruebas secuenciales no ocurre nunca: el doble filtra por estado y la lista siempre
    /// está al día. Si esto está puesto, se devuelve tal cual, aunque ya no corresponda.
    /// </summary>
    public IReadOnlyList<Guid>? IdsObsoletosPorExpirar { get; set; }

    public Task<IReadOnlyList<Guid>> CitasPorExpirarAsync(DateTime ahora, int maximo, CancellationToken ct = default) =>
        IdsObsoletosPorExpirar is { } viejos ? Task.FromResult(viejos) :
        Task.FromResult(Ids(_base.Citas.Where(
            c => c.Estado == EstadoCita.Solicitada && c.ExpiraEn is { } l && l <= ahora), maximo));

    public Task<IReadOnlyList<Guid>> CitasTerminadasAsync(DateTime ahora, int maximo, CancellationToken ct = default) =>
        Task.FromResult(Ids(_base.Citas.Where(
            c => c.Estado == EstadoCita.Confirmada
                 && _base.Bloques.Any(b => b.Id == c.BloqueHorarioId && b.FinEn <= ahora)), maximo));

    public Task<IReadOnlyList<Guid>> CitasSinAsistenciaAsync(DateTime limite, int maximo, CancellationToken ct = default) =>
        Task.FromResult(Ids(_base.Citas.Where(
            c => c.Estado == EstadoCita.PendienteAsistencia
                 && _base.Bloques.Any(b => b.Id == c.BloqueHorarioId && b.FinEn <= limite)), maximo));

    public Task<Cita?> CitaParaCambioAsync(Guid id, CancellationToken ct = default) =>
        CitaPorIdAsync(id, ct);

    /// <summary>Cuántas veces se pidió el bloqueo ordenado de CC-06.</summary>
    public int BloqueosDeDosFilas { get; private set; }

    /// <summary>
    /// Devuelve los dos bloques ORDENADOS POR ID, como el real. El orden se respeta aquí
    /// aunque en memoria no sirva de nada: si el doble devolviera otro orden, una prueba
    /// podría depender del orden equivocado y pasar contra la base por casualidad.
    /// </summary>
    public Task<IReadOnlyList<BloqueHorario>> TomarDosBloquesAsync(
        Guid unoId, Guid otroId, CancellationToken ct = default)
    {
        BloqueosDeDosFilas++;
        IReadOnlyList<BloqueHorario> r = _base.Bloques
            .Where(b => b.Id == unoId || b.Id == otroId)
            .OrderBy(b => b.Id)
            .ToList();
        return Task.FromResult(r);
    }

    /// <summary>Cuánto se pidió esperar por un bloqueo. Null si nadie lo fijó.</summary>
    public TimeSpan? EsperaFijada { get; private set; }

    public Task FijarEsperaDeBloqueoAsync(TimeSpan limite, CancellationToken ct = default)
    {
        EsperaFijada = limite;
        return Task.CompletedTask;
    }

    public void AgregarCita(Cita cita) => _base.Citas.Add(cita);
    public void AgregarCambio(CambioCita cambio) => _base.CambiosCita.Add(cambio);
}
