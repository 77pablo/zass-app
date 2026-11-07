using Microsoft.EntityFrameworkCore;
using Zass.Aplicacion.Puertos;
using Zass.Dominio.Catalogo;

namespace Zass.Infraestructura.Datos.Repositorios;

/// <summary>
/// MOD-03 contra PostgreSQL.
///
/// Las comparaciones de texto van con <c>ToLower()</c> y no con <c>EF.Functions.ILike</c>
/// a proposito: ILIKE interpreta <c>%</c> y <c>_</c> como comodines, asi que un
/// <c>?q=%</c> en API-02 devolveria el catalogo entero y un nombre de servicio con un
/// guion bajo dejaria de encontrarse a si mismo. El texto que escribe una persona no
/// puede llegar a un patron sin escapar.
/// </summary>
public sealed class RepositorioCatalogoEfCore : IRepositorioCatalogo
{
    private readonly ZassDbContext _ctx;
    public RepositorioCatalogoEfCore(ZassDbContext ctx) => _ctx = ctx;

    public async Task<IReadOnlyList<Rubro>> RubrosActivosAsync(
        string? texto, int limite, CancellationToken ct = default)
    {
        var consulta = _ctx.Rubros.Where(r => r.Activo);

        if (!string.IsNullOrWhiteSpace(texto))
        {
            var buscado = texto.ToLower();
            consulta = consulta.Where(r =>
                r.Nombre.ToLower().Contains(buscado) || r.Codigo.Contains(buscado));
        }

        return await consulta.OrderBy(r => r.Nombre).Take(limite).ToListAsync(ct);
    }

    public Task<Rubro?> RubroActivoPorIdAsync(short rubroId, CancellationToken ct = default) =>
        _ctx.Rubros.FirstOrDefaultAsync(r => r.Id == rubroId && r.Activo, ct);

    public Task<Rubro?> RubroActivoPorCodigoAsync(
        string codigo, CancellationToken ct = default)
    {
        var buscado = codigo.Trim().ToLower();
        return _ctx.Rubros.FirstOrDefaultAsync(r => r.Activo && r.Codigo == buscado, ct);
    }

    public async Task<IReadOnlyList<Servicio>> ServiciosDeCatalogoAsync(
        short rubroId, CancellationToken ct = default) =>
        await _ctx.Servicios
            .Where(s => s.RubroId == rubroId && s.EsCatalogo)
            .OrderBy(s => s.Nombre)
            .ToListAsync(ct);

    /// <summary>Un solo <c>WHERE id = ANY(...)</c>, no una consulta por servicio.</summary>
    public async Task<IReadOnlyList<Servicio>> ServiciosPorIdsAsync(
        IReadOnlyCollection<int> ids, CancellationToken ct = default) =>
        await _ctx.Servicios.Where(s => ids.Contains(s.Id)).ToListAsync(ct);

    /// <summary>
    /// Igualdad exacta contra la columna normalizada, sin <c>lower()</c> por medio.
    /// Quien normaliza es Texto.ClaveDeNombre, en C#: en este cluster (locale C)
    /// lower() solo plega ASCII, asi que apoyar la regla en la base la dejaba atada al
    /// locale del servidor.
    /// </summary>
    public Task<Servicio?> ServicioPorNombreAsync(
        short rubroId, string nombreNormalizado, CancellationToken ct = default) =>
        _ctx.Servicios.FirstOrDefaultAsync(
            s => s.RubroId == rubroId && s.NombreNormalizado == nombreNormalizado, ct);

    public async Task<IReadOnlyList<PrestadorServicio>> OfertaDePrestadorAsync(
        Guid perfilPrestadorId, CancellationToken ct = default) =>
        await _ctx.PrestadorServicios
            .Include(o => o.Servicio)
            .Where(o => o.PerfilPrestadorId == perfilPrestadorId)
            .ToListAsync(ct);

    public Task<PrestadorServicio?> OfertaPorIdAsync(Guid id, CancellationToken ct = default) =>
        _ctx.PrestadorServicios
            .Include(o => o.Servicio)
            .FirstOrDefaultAsync(o => o.Id == id, ct);

    public void AgregarServicio(Servicio servicio) => _ctx.Servicios.Add(servicio);

    public void AgregarOferta(PrestadorServicio oferta) => _ctx.PrestadorServicios.Add(oferta);
}
