using Zass.Aplicacion.Busqueda;
using Zass.Aplicacion.Catalogo;
using Zass.Aplicacion.Credenciales;
using Zass.Aplicacion.Geografia;
using Zass.Aplicacion.Identidad;
using Zass.Aplicacion.Pruebas.Dobles;
using Zass.Aplicacion.Puertos;
using Zass.Dominio.Catalogo;
using Zass.Dominio.Comun;

namespace Zass.Aplicacion.Pruebas.Busqueda;

/// <summary>Cómo se entiende lo que se escribe en la barra de búsqueda (API-63), sin base de datos.</summary>
public class TextoDeBusquedaPruebas
{
    [Theory]
    [InlineData("Abogado", "abogado")]
    [InlineData("  GARCÍA  ", "garcia")]
    [InlineData("Ñielol", "nielol")]
    [InlineData("pingüino", "pinguino")]
    [InlineData("Técnicos y oficios", "tecnicos y oficios")]
    [InlineData(null, "")]
    [InlineData("   ", "")]
    public void Normalizar_quita_tildes_y_mayusculas(string? entrada, string esperado) =>
        Assert.Equal(esperado, TextoDeBusqueda.Normalizar(entrada));

    [Theory]
    [InlineData("abogados", "abogado")]
    [InlineData("contadores", "contador")]
    [InlineData("gasfiteres", "gasfiter")]
    [InlineData("oficios", "oficio")]
    [InlineData("abogado", "abogado")]
    [InlineData("iva", "iva")]      // corta: no se le quita nada
    [InlineData("gas", "gas")]      // 3 letras: la «s» es parte de la palabra
    public void El_plural_busca_lo_mismo_que_el_singular(string palabra, string esperado) =>
        Assert.Equal(esperado, TextoDeBusqueda.Singular(palabra));

    [Fact]
    public void Las_palabras_salen_normalizadas_sin_repetir_y_sin_signos()
    {
        var p = TextoDeBusqueda.Palabras("  Abogado,  ABOGADOS!  de   Temuco... ");

        Assert.Equal(new[] { "abogado", "de", "temuco" }, p);
    }

    /// <summary>
    /// «%» y «_» son comodines de LIKE. Si llegaran a la consulta, «%» encontraria a todo el mundo y
    /// «_» a cualquiera con una letra de por medio. Lo que llega son solo letras y numeros.
    /// </summary>
    [Fact]
    public void Los_comodines_de_like_nunca_pasan_a_las_palabras()
    {
        var p = TextoDeBusqueda.Palabras("100% _abogado_ a%b");

        Assert.All(p, palabra => Assert.True(palabra.All(char.IsLetterOrDigit)));
        Assert.DoesNotContain(p, palabra => palabra.Contains('%') || palabra.Contains('_'));
    }

    [Fact]
    public void Una_letra_sola_no_es_una_palabra()
    {
        Assert.Empty(TextoDeBusqueda.Palabras("a b c"));
    }

    [Fact]
    public void Hay_un_maximo_de_palabras_y_de_largo()
    {
        var muchas = string.Join(' ', Enumerable.Range(0, 20).Select(i => $"palabra{i}"));
        Assert.Equal(TextoDeBusqueda.MaximoDePalabras, TextoDeBusqueda.Palabras(muchas).Count);

        var larga = TextoDeBusqueda.Palabras(new string('a', 500));
        Assert.True(larga.Single().Length <= TextoDeBusqueda.LargoMaximoConsulta);
    }

    [Theory]
    [InlineData("abogado", "derecho")]
    [InlineData("Abogados", "derecho")]
    [InlineData("contadora", "contabilidad")]
    [InlineData("gasfiter", "oficios")]
    [InlineData("electricista", "oficios")]
    [InlineData("corredor", "corretaje")]
    [InlineData("arquitecto", "arquitectura")]
    public void Quien_escribe_un_oficio_busca_tambien_el_rubro_que_lo_contiene(string escrito, string rubro)
    {
        var (formas, grupos) = TextoDeBusqueda.Grupos(escrito);

        Assert.Contains(rubro, formas);
        Assert.Equal(grupos.Length, formas.Length);
        Assert.All(grupos, g => Assert.Equal(1, g));       // las dos formas son UN grupo: sirve cualquiera
    }

    [Fact]
    public void Cada_palabra_es_su_propio_grupo()
    {
        var (formas, grupos) = TextoDeBusqueda.Grupos("abogado garcia");

        Assert.Equal(new[] { "abogado", "derecho", "garcia" }, formas);
        Assert.Equal(new[] { 1, 1, 2 }, grupos);
    }

    [Fact]
    public void Sin_texto_no_hay_grupos_y_se_busca_a_todos()
    {
        var (formas, grupos) = TextoDeBusqueda.Grupos("   ");

        Assert.Empty(formas);
        Assert.Empty(grupos);
    }
}

public class BusquedaPorTextoPruebas
{
    private readonly BaseEnMemoria _base = new();
    private readonly RepositorioBusquedaFalso _repo = new();
    private readonly RelojFijo _reloj = new(new DateTime(2026, 11, 7, 12, 0, 0, DateTimeKind.Utc));
    private readonly ServicioBusqueda _servicio;

    public BusquedaPorTextoPruebas()
    {
        _base.Zonas.Add(new Zona { Id = 1, Nombre = "Centro", Sector = "centro", CentroLatitud = -38.74, CentroLongitud = -72.59, Comuna = "Temuco" });
        var auditor = new AuditoriaEnMemoria();
        var catalogo = new ServicioCatalogo(new RepositorioCatalogoEnMemoria(_base), _base, _reloj, auditor);
        var geografia = new ServicioGeografia(
            new RepositorioGeografiaEnMemoria(_base), new GeocodificadorApagado(), _base, _reloj, auditor);
        var identidad = new ServicioIdentidad(
            new RepositorioUsuariosEnMemoria(_base), new RepositorioPerfilesEnMemoria(_base));
        _servicio = new ServicioBusqueda(
            _repo, catalogo, geografia, identidad,
            new ServicioCredenciales(new RepositorioCredencialesEnMemoria(_base), _base, _reloj, auditor), _reloj);
    }

    private static CriteriosDeTextoDeBusqueda C(
        string? texto = "abogado", double? lat = null, double? lon = null, int? zona = null,
        OrdenDeTexto orden = OrdenDeTexto.Recomendados, int pagina = 1) =>
        new(texto, lat, lon, zona, orden, pagina);

    private static FilaDeTexto Fila(
        string nombre = "Marcela Soto", double? metros = 2_600, int resenas = 0, decimal? promedio = null,
        long total = 1, int? precio = null) => new()
    {
        Id = Guid.NewGuid(), Slug = nombre.ToLower().Replace(' ', '-'), NombrePublico = nombre,
        RubroNombre = "Derecho", ZonaPrincipal = "Centro", Metros = metros, Tramo = 3,
        PromedioCalificacion = promedio, TotalResenas = resenas, TotalFilas = total, PrecioDesde = precio,
    };

    // ------------------------------------------------------------- lo que se le pide a la base

    [Fact]
    public async Task El_texto_se_convierte_en_grupos_antes_de_llegar_a_la_base()
    {
        await _servicio.BuscarPorTextoAsync(C("Abogados García"));

        var c = _repo.UltimosCriteriosDeTexto!;
        Assert.Equal(new[] { "abogado", "derecho", "garcia" }, c.Formas);
        Assert.Equal(new[] { 1, 1, 2 }, c.Grupos);
        Assert.Equal(_reloj.Ahora, c.Ahora);
    }

    [Theory]
    [InlineData(OrdenDeTexto.Recomendados, "recomendados")]
    [InlineData(OrdenDeTexto.PrecioAsc, "precio_asc")]
    [InlineData(OrdenDeTexto.PrecioDesc, "precio_desc")]
    public async Task El_orden_pedido_viaja_a_la_base(OrdenDeTexto orden, string codigo)
    {
        var r = await _servicio.BuscarPorTextoAsync(C(orden: orden));

        Assert.Equal(codigo, _repo.UltimosCriteriosDeTexto!.Orden);
        Assert.Equal(orden, r.Valor!.OrdenAplicado);
    }

    [Fact]
    public async Task Con_ubicacion_y_cercania_se_ordena_por_cercania_y_se_mide_desde_la_persona()
    {
        var r = await _servicio.BuscarPorTextoAsync(C(lat: -38.70, lon: -72.60, orden: OrdenDeTexto.Cercania));

        Assert.Equal("cercania", _repo.UltimosCriteriosDeTexto!.Orden);
        Assert.Equal(-38.70, _repo.UltimosCriteriosDeTexto.Latitud);
        Assert.Equal(-72.60, _repo.UltimosCriteriosDeTexto.Longitud);
        Assert.True(r.Valor!.ConUbicacion);
        Assert.Null(r.Valor.Aviso);
    }

    /// <summary>No se ordena por cercania a ciegas: se avisa y se ordena por recomendados.</summary>
    [Fact]
    public async Task Cercania_sin_ubicacion_cae_a_recomendados_y_lo_dice()
    {
        var r = await _servicio.BuscarPorTextoAsync(C(orden: OrdenDeTexto.Cercania));

        Assert.Equal("recomendados", _repo.UltimosCriteriosDeTexto!.Orden);
        Assert.Equal(OrdenDeTexto.Recomendados, r.Valor!.OrdenAplicado);
        Assert.Contains("ubicacion", r.Valor.Aviso);
        Assert.False(r.Valor.ConUbicacion);
    }

    [Fact]
    public async Task Sin_ubicacion_se_mide_desde_el_centro_de_temuco_solo_para_ordenar()
    {
        await _servicio.BuscarPorTextoAsync(C());

        Assert.Equal(ServicioBusqueda.CentroDeTemuco.Latitud, _repo.UltimosCriteriosDeTexto!.Latitud);
        Assert.Equal(ServicioBusqueda.CentroDeTemuco.Longitud, _repo.UltimosCriteriosDeTexto.Longitud);
    }

    // ------------------------------------------------------------- RN-S3-03: la distancia

    [Fact]
    public async Task La_distancia_solo_se_dice_con_la_ubicacion_de_la_persona_y_siempre_redondeada()
    {
        _repo.FilasDeTexto.Add(Fila(metros: 2_600));

        var con = (await _servicio.BuscarPorTextoAsync(C(lat: -38.7, lon: -72.6))).Valor!;
        var sin = (await _servicio.BuscarPorTextoAsync(C())).Valor!;

        Assert.Equal("a 3 km", con.Resultados.Single().Distancia);
        Assert.Null(sin.Resultados.Single().Distancia);
    }

    [Fact]
    public async Task Por_debajo_de_un_kilometro_no_se_da_numero()
    {
        _repo.FilasDeTexto.Add(Fila(metros: 412));

        var r = (await _servicio.BuscarPorTextoAsync(C(lat: -38.7, lon: -72.6))).Valor!;

        Assert.Equal("a menos de 1 km", r.Resultados.Single().Distancia);
    }

    [Fact]
    public void El_resultado_no_tiene_donde_guardar_metros_ni_coordenadas_ni_direccion()
    {
        var campos = new[] { typeof(ResultadoDeTexto), typeof(PaginaDeTexto) }
            .SelectMany(t => t.GetProperties()).ToList();

        foreach (var prohibido in new[] { "Metros", "Latitud", "Longitud", "Direccion", "Ubicacion" })
            Assert.DoesNotContain(campos, p => p.Name.Contains(prohibido, StringComparison.OrdinalIgnoreCase)
                                               && p.Name != nameof(PaginaDeTexto.ConUbicacion));
        Assert.Equal(typeof(string), typeof(ResultadoDeTexto).GetProperty(nameof(ResultadoDeTexto.Distancia))!.PropertyType);
    }

    // ------------------------------------------------------------- RN-S5-11: la reputación

    [Theory]
    [InlineData(2, 5.0, null)]     // con menos de 3 reseñas no hay promedio publico
    [InlineData(3, 4.6, 4.6)]
    [InlineData(12, 4.8, 4.8)]
    public async Task El_promedio_solo_se_muestra_desde_la_tercera_resena(int resenas, double promedio, double? esperado)
    {
        _repo.FilasDeTexto.Add(Fila(resenas: resenas, promedio: (decimal)promedio));

        var r = (await _servicio.BuscarPorTextoAsync(C())).Valor!.Resultados.Single();

        Assert.Equal(esperado is null ? null : (decimal)esperado, r.PromedioCalificacion);
        Assert.Equal(resenas, r.TotalResenas);
    }

    // ------------------------------------------------------------- páginas

    [Fact]
    public async Task Pide_una_mas_que_la_pagina_para_saber_si_hay_siguiente()
    {
        await _servicio.BuscarPorTextoAsync(C(pagina: 3));

        var c = _repo.UltimosCriteriosDeTexto!;
        Assert.Equal(ServicioBusqueda.PorPaginaDeTexto + 1, c.Limite);
        Assert.Equal(2 * ServicioBusqueda.PorPaginaDeTexto, c.Desplazamiento);
    }

    [Fact]
    public async Task Hay_mas_cuando_la_base_devuelve_mas_de_una_pagina_y_no_se_muestra_la_de_sobra()
    {
        for (var i = 0; i < ServicioBusqueda.PorPaginaDeTexto + 1; i++)
            _repo.FilasDeTexto.Add(Fila($"Persona {i}", total: 40));

        var p = (await _servicio.BuscarPorTextoAsync(C())).Valor!;

        Assert.True(p.HayMas);
        Assert.Equal(ServicioBusqueda.PorPaginaDeTexto, p.Resultados.Count);
        Assert.Equal(40, p.Total);
    }

    /// <summary>Justo una pagina completa NO es «hay mas»: la fila de sobra es la que lo dice.</summary>
    [Fact]
    public async Task Una_pagina_exactamente_llena_no_dice_que_hay_mas()
    {
        for (var i = 0; i < ServicioBusqueda.PorPaginaDeTexto; i++)
            _repo.FilasDeTexto.Add(Fila($"Persona {i}", total: 24));

        var p = (await _servicio.BuscarPorTextoAsync(C())).Valor!;

        Assert.False(p.HayMas);
        Assert.Equal(ServicioBusqueda.PorPaginaDeTexto, p.Resultados.Count);
    }

    [Fact]
    public async Task Sin_filas_el_total_es_cero_y_no_hay_mas()
    {
        var p = (await _servicio.BuscarPorTextoAsync(C())).Valor!;

        Assert.Equal(0, p.Total);
        Assert.False(p.HayMas);
        Assert.Empty(p.Resultados);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(-5, 1)]
    [InlineData(999, ServicioBusqueda.PaginaMaximaDeTexto)]
    public async Task La_pagina_se_corrige_al_rango_valido(int pedida, int aplicada)
    {
        var p = (await _servicio.BuscarPorTextoAsync(C(pagina: pedida))).Valor!;

        Assert.Equal(aplicada, p.Pagina);
    }

    // ------------------------------------------------------------- los filtros

    [Fact]
    public async Task Los_filtros_viajan_a_la_base_y_por_omision_no_filtran_nada()
    {
        await _servicio.BuscarPorTextoAsync(C());
        Assert.False(_repo.UltimosCriteriosDeTexto!.SoloVerificados);
        Assert.Null(_repo.UltimosCriteriosDeTexto.CalificacionMinima);

        await _servicio.BuscarPorTextoAsync(new CriteriosDeTextoDeBusqueda(
            "abogado", null, null, null, OrdenDeTexto.Recomendados, 1, SoloVerificados: true, CalificacionMinima: 4m));
        Assert.True(_repo.UltimosCriteriosDeTexto.SoloVerificados);
        Assert.Equal(4m, _repo.UltimosCriteriosDeTexto.CalificacionMinima);
    }

    [Theory]
    [InlineData(0.5)]
    [InlineData(0)]
    [InlineData(5.1)]
    [InlineData(7)]
    [InlineData(-1)]
    public async Task Una_calificacion_minima_fuera_de_1_a_5_se_rechaza_y_no_se_busca(double nota)
    {
        var r = await _servicio.BuscarPorTextoAsync(new CriteriosDeTextoDeBusqueda(
            "abogado", null, null, null, OrdenDeTexto.Recomendados, 1, CalificacionMinima: (decimal)nota));

        Assert.False(r.Exito);
        Assert.Equal(Fallas.Validacion, r.Codigo);
        Assert.Null(_repo.UltimosCriteriosDeTexto);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(4)]
    [InlineData(5)]
    public async Task Las_notas_de_1_a_5_se_aceptan(double nota)
    {
        var r = await _servicio.BuscarPorTextoAsync(new CriteriosDeTextoDeBusqueda(
            "abogado", null, null, null, OrdenDeTexto.Recomendados, 1, CalificacionMinima: (decimal)nota));

        Assert.True(r.Exito);
    }

    // ------------------------------------------------------------- lo que se rechaza

    [Fact]
    public async Task Media_coordenada_se_rechaza_y_no_se_busca()
    {
        var r = await _servicio.BuscarPorTextoAsync(C(lat: -38.7));

        Assert.False(r.Exito);
        Assert.Equal(Fallas.Validacion, r.Codigo);
        Assert.Null(_repo.UltimosCriteriosDeTexto);
    }

    [Theory]
    [InlineData(91, 0)]
    [InlineData(-91, 0)]
    [InlineData(0, 181)]
    [InlineData(0, -181)]
    public async Task Una_ubicacion_fuera_de_rango_se_rechaza(double lat, double lon)
    {
        var r = await _servicio.BuscarPorTextoAsync(C(lat: lat, lon: lon));

        Assert.False(r.Exito);
        Assert.Null(_repo.UltimosCriteriosDeTexto);
    }

    [Fact]
    public async Task Un_orden_que_no_existe_se_rechaza()
    {
        var r = await _servicio.BuscarPorTextoAsync(C(orden: (OrdenDeTexto)42));

        Assert.False(r.Exito);
        Assert.Equal(Fallas.Validacion, r.Codigo);
    }

    [Fact]
    public async Task Una_zona_que_no_existe_se_rechaza_y_una_que_existe_filtra()
    {
        Assert.False((await _servicio.BuscarPorTextoAsync(C(zona: 999))).Exito);
        Assert.Null(_repo.UltimosCriteriosDeTexto);

        Assert.True((await _servicio.BuscarPorTextoAsync(C(zona: 1))).Exito);
        Assert.Equal(1, _repo.UltimosCriteriosDeTexto!.ZonaId);
    }

    [Fact]
    public async Task Sin_texto_se_busca_a_todos_y_no_es_un_error()
    {
        var r = await _servicio.BuscarPorTextoAsync(C(texto: null));

        Assert.True(r.Exito);
        Assert.Empty(_repo.UltimosCriteriosDeTexto!.Formas);
    }
}
