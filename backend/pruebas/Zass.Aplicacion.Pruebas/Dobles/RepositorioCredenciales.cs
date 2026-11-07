using Zass.Aplicacion.Puertos;
using Zass.Dominio.Comun;
using Zass.Dominio.Credenciales;

namespace Zass.Aplicacion.Pruebas.Dobles;

/// <summary>
/// MOD-02 en memoria. Tonto a propósito: no imita el índice único del nombre de la entidad ni
/// el borrado en cascada de los documentos; eso se comprueba contra PostgreSQL en integración.
/// </summary>
public sealed class RepositorioCredencialesEnMemoria : IRepositorioCredenciales
{
    private readonly BaseEnMemoria _base;
    public RepositorioCredencialesEnMemoria(BaseEnMemoria baseEnMemoria) => _base = baseEnMemoria;

    public Task<IReadOnlyList<Credencial>> DePrestadorAsync(Guid perfilPrestadorId, CancellationToken ct = default)
    {
        IReadOnlyList<Credencial> r = _base.Credenciales
            .Where(c => c.PerfilPrestadorId == perfilPrestadorId)
            .OrderByDescending(c => c.CreadaEn).ToList();
        return Task.FromResult(r);
    }

    public Task<IReadOnlyList<Credencial>> PublicasDePrestadorAsync(Guid perfilPrestadorId, CancellationToken ct = default)
    {
        IReadOnlyList<Credencial> r = _base.Credenciales
            .Where(c => c.PerfilPrestadorId == perfilPrestadorId && c.Estado != EstadoCredencial.Borrador).ToList();
        return Task.FromResult(r);
    }

    public Task<Credencial?> PorIdAsync(Guid id, CancellationToken ct = default) =>
        Task.FromResult(_base.Credenciales.FirstOrDefault(c => c.Id == id));

    public Task<int> ContarDePrestadorAsync(Guid perfilPrestadorId, CancellationToken ct = default) =>
        Task.FromResult(_base.Credenciales.Count(c => c.PerfilPrestadorId == perfilPrestadorId));

    public Task<bool> TieneVerificadaAsync(Guid perfilPrestadorId, CancellationToken ct = default) =>
        Task.FromResult(_base.Credenciales.Any(
            c => c.PerfilPrestadorId == perfilPrestadorId && c.Estado == EstadoCredencial.Verificada));

    public void Agregar(Credencial credencial) => _base.Credenciales.Add(credencial);
    public void Eliminar(Credencial credencial) => _base.Credenciales.Remove(credencial);

    public Task<EntidadCertificadora?> EntidadActivaPorIdAsync(short id, CancellationToken ct = default) =>
        Task.FromResult(_base.Entidades.FirstOrDefault(e => e.Id == id && e.Activa));

    public Task<EntidadCertificadora?> EntidadPorNombreAsync(string nombre, CancellationToken ct = default) =>
        Task.FromResult(_base.Entidades.FirstOrDefault(
            e => string.Equals(e.Nombre, nombre, StringComparison.OrdinalIgnoreCase)));

    public void AgregarEntidad(EntidadCertificadora entidad) => _base.Entidades.Add(entidad);
}
