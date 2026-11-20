namespace Zass.Aplicacion.Puertos;

/// <summary>
/// JQ-16 · CU-05 4D.3. Los borradores de credencial sin actividad. «Actividad» es la creación o el
/// último documento subido, lo que sea más nuevo (lo define PLAN-JOAQUIN; no hay otra fecha que lo
/// diga sin migración). Solo lectura: el borrado lo hace el servicio con IRepositorioCredenciales.
/// </summary>
public interface IRepositorioBorradores
{
    /// <summary>Los borradores cuya última actividad es igual o anterior a <paramref name="actividadHasta"/>.</summary>
    Task<IReadOnlyList<BorradorInactivo>> InactivosAsync(DateTime actividadHasta, CancellationToken ct = default);
}

/// <param name="UsuarioId">El dueño de la cuenta, a quien va el aviso.</param>
public sealed record BorradorInactivo(Guid CredencialId, Guid UsuarioId, string Nombre, DateTime UltimaActividad);
