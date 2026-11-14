using Zass.Aplicacion.Puertos;
using Zass.Dominio.Agenda;
using Zass.Dominio.Comun;
using Zass.Dominio.Reputacion;

namespace Zass.Aplicacion.Pruebas.Dobles;

/// <summary>
/// MOD-11 en memoria. Tonto a propósito: no imita los índices únicos ni el cerrojo (eso se
/// comprueba contra PostgreSQL), y la "transacción" solo avisa si se confirmó o se deshizo, sin
/// deshacer nada: lo que importa aquí es que el servicio NO agregue nada antes de rechazar.
///
/// El recálculo sí está escrito, a mano y aparte del código real, para que las pruebas de
/// aplicación puedan afirmar sobre el promedio y el recuento sin base de datos.
/// </summary>
public sealed class RepositorioResenasEnMemoria : IRepositorioResenas
{
    private readonly BaseEnMemoria _base;
    public RepositorioResenasEnMemoria(BaseEnMemoria baseEnMemoria) => _base = baseEnMemoria;

    public List<Contacto> Contactos { get; } = new();
    public List<Resena> Resenas { get; } = new();
    public List<RespuestaResena> Respuestas { get; } = new();
    public List<ReporteResena> Reportes { get; } = new();
    public List<Cita> Citas { get; } = new();

    public int Cerrojos { get; private set; }
    public int Recalculos { get; private set; }
    public int Confirmadas { get; private set; }
    public int Deshechas { get; private set; }

    public async Task<T> EnTransaccionAsync<T>(
        Func<Task<T>> trabajo, Func<T, bool> confirmar, CancellationToken ct = default)
    {
        var r = await trabajo();
        if (confirmar(r)) Confirmadas++; else Deshechas++;
        return r;
    }

    public Task BloquearPerfilAsync(Guid perfilPrestadorId, CancellationToken ct = default)
    {
        Cerrojos++;
        return Task.CompletedTask;
    }

    public Task RecalcularReputacionAsync(Guid perfilPrestadorId, CancellationToken ct = default)
    {
        Recalculos++;
        var perfil = _base.PerfilesPrestador.First(p => p.Id == perfilPrestadorId);
        var visibles = Resenas
            .Where(r => r.PerfilPrestadorId == perfilPrestadorId && r.Estado == EstadoResena.Publicada)
            .ToList();
        perfil.TotalResenas = visibles.Count;
        perfil.PromedioCalificacion = visibles.Count == 0
            ? null
            : Math.Round((decimal)visibles.Average(r => r.Calificacion), 1, MidpointRounding.AwayFromZero);
        return Task.CompletedTask;
    }

    public Task<DatosDePerfil?> PerfilAsync(Guid perfilPrestadorId, CancellationToken ct = default) =>
        Task.FromResult(_base.PerfilesPrestador.Where(p => p.Id == perfilPrestadorId)
            .Select(p => new DatosDePerfil(p.Id, p.UsuarioId, p.Estado, p.NombrePublico, p.Slug))
            .FirstOrDefault());

    public Task<Contacto?> ContactoAsync(Guid contactoId, CancellationToken ct = default)
    {
        var c = Contactos.FirstOrDefault(x => x.Id == contactoId);
        if (c is not null && c.CitaId is { } cid) c.Cita = Citas.FirstOrDefault(x => x.Id == cid);
        return Task.FromResult(c);
    }

    public Task<IReadOnlyList<Contacto>> ContactosSinResenaAsync(
        Guid buscadorId, Guid perfilPrestadorId, CancellationToken ct = default)
    {
        IReadOnlyList<Contacto> r = Contactos
            .Where(c => c.BuscadorId == buscadorId && c.PerfilPrestadorId == perfilPrestadorId
                        && Resenas.All(x => x.ContactoId != c.Id))
            .OrderBy(c => c.OcurridoEn)
            .Select(c =>
            {
                if (c.CitaId is { } cid) c.Cita = Citas.FirstOrDefault(x => x.Id == cid);
                return c;
            })
            .ToList();
        return Task.FromResult(r);
    }

    public Task<DateTime?> UltimaResenaPorContactoAsync(
        Guid autorId, Guid perfilPrestadorId, DateTime desde, CancellationToken ct = default) =>
        Task.FromResult(Resenas
            .Where(r => r.AutorId == autorId && r.PerfilPrestadorId == perfilPrestadorId
                        && r.PublicadaEn > desde
                        && Contactos.First(c => c.Id == r.ContactoId).CitaId is null)
            .Select(r => (DateTime?)r.PublicadaEn)
            .OrderByDescending(f => f)
            .FirstOrDefault());

    public Task<Resena?> ResenaDeContactoAsync(Guid contactoId, CancellationToken ct = default) =>
        Task.FromResult(Resenas.FirstOrDefault(r => r.ContactoId == contactoId));

    private Resena? ConRespuesta(Guid id)
    {
        var r = Resenas.FirstOrDefault(x => x.Id == id);
        if (r is not null) r.Respuesta = Respuestas.FirstOrDefault(x => x.ResenaId == id);
        return r;
    }

    public Task<Resena?> ResenaPorIdAsync(Guid resenaId, CancellationToken ct = default) =>
        Task.FromResult(ConRespuesta(resenaId));

    public Task<Resena?> ResenaParaActualizarAsync(Guid resenaId, CancellationToken ct = default) =>
        Task.FromResult(ConRespuesta(resenaId));

    public Task<Contacto?> ContactoDeCitaAsync(Guid citaId, CancellationToken ct = default) =>
        Task.FromResult(Contactos.FirstOrDefault(c => c.CitaId == citaId));

    public Task<Cita?> CitaAsync(Guid citaId, CancellationToken ct = default) =>
        Task.FromResult(Citas.FirstOrDefault(c => c.Id == citaId));

    public Task<IReadOnlyList<FilaPublica>> PublicasAsync(
        Guid perfilPrestadorId, DateTime? antesDe, Guid? antesDeId, int limite,
        CancellationToken ct = default)
    {
        var q = Resenas.Where(r => r.PerfilPrestadorId == perfilPrestadorId && r.Estado == EstadoResena.Publicada);
        if (antesDe is { } f && antesDeId is { } id)
            q = q.Where(r => r.PublicadaEn < f || (r.PublicadaEn == f && r.Id.CompareTo(id) < 0));
        IReadOnlyList<FilaPublica> r2 = q.OrderByDescending(r => r.PublicadaEn).ThenByDescending(r => r.Id)
            .Take(limite).Select(r => new FilaPublica(ConRespuesta(r.Id)!, DeCita(r))).ToList();
        return Task.FromResult(r2);
    }

    public Task<IReadOnlyList<FilaDeMisResenas>> MiasAsync(
        Guid autorId, int limite, CancellationToken ct = default)
    {
        IReadOnlyList<FilaDeMisResenas> mias = Resenas
            .Where(r => r.AutorId == autorId)
            .OrderByDescending(r => r.PublicadaEn).ThenByDescending(r => r.Id)
            .Take(limite)
            .Select(r =>
            {
                var perfil = _base.PerfilesPrestador.FirstOrDefault(p => p.Id == r.PerfilPrestadorId);
                return new FilaDeMisResenas(
                    ConRespuesta(r.Id)!, DeCita(r), perfil?.NombrePublico ?? "", perfil?.Slug ?? "");
            })
            .ToList();
        return Task.FromResult(mias);
    }

    /// <summary>Lo mismo que hace la consulta real: mira la cita POR el contacto de la reseña.</summary>
    private bool DeCita(Resena r) =>
        Contactos.FirstOrDefault(c => c.Id == r.ContactoId)?.CitaId is not null;

    public Task<bool> TieneReporteAbiertoAsync(Guid resenaId, Guid usuarioId, CancellationToken ct = default) =>
        Task.FromResult(Reportes.Any(r => r.ResenaId == resenaId && r.ReportadoPor == usuarioId
            && r.Estado is EstadoReporte.Abierto or EstadoReporte.EnRevision));

    public Task<ReporteResena?> ReportePorIdAsync(Guid reporteId, CancellationToken ct = default) =>
        Task.FromResult(Reportes.FirstOrDefault(r => r.Id == reporteId));

    public Task<IReadOnlyList<ReporteResena>> ReportesAbiertosDeResenaAsync(Guid resenaId, CancellationToken ct = default)
    {
        IReadOnlyList<ReporteResena> r = Reportes
            .Where(x => x.ResenaId == resenaId && x.Estado is EstadoReporte.Abierto or EstadoReporte.EnRevision)
            .OrderBy(x => x.CreadoEn).ToList();
        return Task.FromResult(r);
    }

    public Task<IReadOnlyList<FilaDeCola>> ColaAsync(Guid moderadorId, int limite, CancellationToken ct = default)
    {
        IReadOnlyList<FilaDeCola> r = Reportes
            .Where(x => x.Estado is EstadoReporte.Abierto or EstadoReporte.EnRevision)
            .Select(x => (Reporte: x, Resena: Resenas.First(re => re.Id == x.ResenaId)))
            .Where(t => t.Reporte.ReportadoPor != moderadorId && t.Resena.AutorId != moderadorId
                        && _base.PerfilesPrestador.First(p => p.Id == t.Resena.PerfilPrestadorId).UsuarioId != moderadorId)
            .OrderByDescending(t => t.Reporte.EsGrave()).ThenBy(t => t.Reporte.CreadoEn)
            .Take(limite)
            .Select(t => new FilaDeCola(
                t.Reporte.Id, t.Resena.Id, t.Reporte.Motivo, t.Reporte.Detalle, t.Reporte.Estado,
                t.Reporte.CreadoEn, t.Resena.Calificacion, t.Resena.Comentario, t.Resena.Estado,
                _base.PerfilesPrestador.First(p => p.Id == t.Resena.PerfilPrestadorId).NombrePublico))
            .ToList();
        return Task.FromResult(r);
    }

    public void Agregar(Contacto contacto) => Contactos.Add(contacto);
    public void Agregar(Resena resena) => Resenas.Add(resena);
    public void Agregar(RespuestaResena respuesta) => Respuestas.Add(respuesta);
    public void Agregar(ReporteResena reporte) => Reportes.Add(reporte);
}
