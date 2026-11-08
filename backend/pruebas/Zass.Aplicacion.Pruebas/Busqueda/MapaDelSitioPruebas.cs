using Zass.Aplicacion.Busqueda;
using Zass.Aplicacion.Pruebas.Dobles;

namespace Zass.Aplicacion.Pruebas.Busqueda;

/// <summary>
/// El listado de slugs para el sitemap. El filtro de "visible" vive en SQL y se prueba en integracion;
/// aqui se prueba lo que decide el servicio: el tamano de pagina y el cursor.
/// </summary>
public class MapaDelSitioPruebas
{
    private sealed class RepoFalso : IRepositorioMapaDelSitio
    {
        public List<FichaDelMapa> Filas { get; } = new();
        public string? UltimoCursor { get; private set; }
        public int UltimoLimite { get; private set; }
        public DateTime UltimoAhora { get; private set; }

        public Task<IReadOnlyList<FichaDelMapa>> VisiblesAsync(
            string? despuesDe, DateTime ahora, int limite, CancellationToken ct = default)
        {
            UltimoCursor = despuesDe; UltimoLimite = limite; UltimoAhora = ahora;
            IReadOnlyList<FichaDelMapa> r = Filas
                .Where(f => despuesDe is null || string.CompareOrdinal(f.Slug, despuesDe) > 0)
                .OrderBy(f => f.Slug, StringComparer.Ordinal).Take(limite).ToList();
            return Task.FromResult(r);
        }
    }

    private readonly RepoFalso _repo = new();
    private readonly RelojFijo _reloj = new(new DateTime(2026, 11, 7, 12, 0, 0, DateTimeKind.Utc));
    private readonly ServicioMapaDelSitio _servicio;

    public MapaDelSitioPruebas() => _servicio = new ServicioMapaDelSitio(_repo, _reloj);

    private void Fichas(int n)
    {
        for (var i = 0; i < n; i++) _repo.Filas.Add(new FichaDelMapa($"persona-{i:D4}", _reloj.Ahora));
    }

    [Fact]
    public async Task La_ultima_pagina_no_trae_cursor()
    {
        Fichas(3);
        var p = await _servicio.PaginaAsync(null, 10);
        Assert.Equal(3, p.Prestadores.Count);
        Assert.Null(p.CursorSiguiente);
    }

    [Fact]
    public async Task Con_mas_fichas_que_la_pagina_devuelve_el_ultimo_slug_como_cursor_y_la_siguiente_sigue_sin_repetir()
    {
        Fichas(5);
        var primera = await _servicio.PaginaAsync(null, 2);
        Assert.Equal(new[] { "persona-0000", "persona-0001" }, primera.Prestadores.Select(f => f.Slug).ToArray());
        Assert.Equal("persona-0001", primera.CursorSiguiente);

        var segunda = await _servicio.PaginaAsync(primera.CursorSiguiente, 2);
        Assert.Equal(new[] { "persona-0002", "persona-0003" }, segunda.Prestadores.Select(f => f.Slug).ToArray());

        var tercera = await _servicio.PaginaAsync(segunda.CursorSiguiente, 2);
        Assert.Equal(new[] { "persona-0004" }, tercera.Prestadores.Select(f => f.Slug).ToArray());
        Assert.Null(tercera.CursorSiguiente);
    }

    [Fact]
    public async Task Una_pagina_exacta_no_inventa_otra_vacia()
    {
        Fichas(4);
        var p = await _servicio.PaginaAsync(null, 4);
        Assert.Equal(4, p.Prestadores.Count);
        Assert.Null(p.CursorSiguiente);
    }

    [Theory]
    [InlineData(null, ServicioMapaDelSitio.PorPaginaPorOmision + 1)]
    [InlineData(0, 2)]
    [InlineData(-5, 2)]
    [InlineData(1, 2)]
    [InlineData(999999, ServicioMapaDelSitio.PorPaginaMaximo + 1)]
    public async Task El_tamano_de_pagina_se_corrige_a_un_rango_sensato(int? pedido, int seEsperaPedirAlRepo)
    {
        await _servicio.PaginaAsync(null, pedido);
        Assert.Equal(seEsperaPedirAlRepo, _repo.UltimoLimite);
    }

    [Fact]
    public async Task Un_cursor_en_blanco_cuenta_como_primera_pagina_y_se_usa_el_reloj_del_sistema()
    {
        await _servicio.PaginaAsync("   ", 10);
        Assert.Null(_repo.UltimoCursor);
        Assert.Equal(_reloj.Ahora, _repo.UltimoAhora);
    }
}
