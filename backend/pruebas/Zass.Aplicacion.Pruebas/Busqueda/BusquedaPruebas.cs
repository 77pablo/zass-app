using Zass.Aplicacion.Busqueda;
using Zass.Aplicacion.Catalogo;
using Zass.Aplicacion.Credenciales;
using Zass.Aplicacion.Geografia;
using Zass.Aplicacion.Identidad;
using Zass.Aplicacion.Pruebas.Dobles;
using Zass.Aplicacion.Puertos;
using Zass.Aplicacion.Suscripciones;
using Zass.Dominio.Catalogo;
using Zass.Dominio.Comun;

namespace Zass.Aplicacion.Pruebas.Busqueda;

/// <summary>
/// MOD-05 - Busqueda (CU-08), sin base de datos.
///
/// MOD-03 y MOD-04 van con sus servicios DE VERDAD sobre listas en memoria, no con dobles
/// propios: asi "el punto se resuelve a zona" se comprueba con la misma logica que corre
/// en produccion. El unico doble es el repositorio de busqueda, que es puro SQL y se
/// prueba en integracion.
/// </summary>
public class BusquedaPruebas
{
    private const short Corretaje = 1;
    private const int Centro = 1;
    private const int Labranza = 2;

    private static readonly Punto PlazaDeTemuco = new(-38.7359, -72.5904);

    private readonly BaseEnMemoria _base = new();
    private readonly RepositorioBusquedaFalso _repo = new();
    private readonly RelojFijo _reloj = new(new DateTime(2026, 11, 7, 12, 0, 0, DateTimeKind.Utc));
    private readonly ServicioBusqueda _servicio;

    public BusquedaPruebas()
    {
        _base.Rubros.Add(new Rubro
        {
            Id = Corretaje, Codigo = "corretaje", Nombre = "Corretaje", Activo = true,
        });
        _base.Rubros.Add(new Rubro
        {
            Id = 9, Codigo = "apagado", Nombre = "Rubro apagado", Activo = false,
        });

        _base.Zonas.Add(new Zona
        {
            Id = Centro, Nombre = "Centro", Sector = "centro",
            CentroLatitud = -38.7400362, CentroLongitud = -72.5903905, Comuna = "Temuco",
        });
        _base.Zonas.Add(new Zona
        {
            Id = Labranza, Nombre = "Labranza", Sector = "poniente",
            CentroLatitud = -38.7665376, CentroLongitud = -72.7534177, Comuna = "Temuco",
        });

        var catalogo = new ServicioCatalogo(
            new RepositorioCatalogoEnMemoria(_base), _base, _reloj, new AuditoriaEnMemoria());
        var geografia = new ServicioGeografia(
            new RepositorioGeografiaEnMemoria(_base), new GeocodificadorApagado(),
            _base, _reloj, new AuditoriaEnMemoria());

        var identidad = new ServicioIdentidad(
            new RepositorioUsuariosEnMemoria(_base), new RepositorioPerfilesEnMemoria(_base));

        _servicio = new ServicioBusqueda(
            _repo, catalogo, geografia, identidad,
            new ServicioCredenciales(new RepositorioCredencialesEnMemoria(_base), _base, _reloj, new AuditoriaEnMemoria()),
            new ServicioSuscripciones(
                new RepositorioSuscripcionesEnMemoria(_base), _base, _reloj,
                new AuditoriaEnMemoria(), new ColaEnMemoria()),
            _reloj);
    }

    private FilaDeBusqueda UnaFila(
        string nombre = "Estudio Espinoza", double? metros = 3_400,
        decimal? promedio = null, int resenas = 0, bool verificada = false,
        Guid? id = null) =>
        new()
        {
            Id = id ?? Guid.NewGuid(),
            Slug = "estudio-espinoza",
            NombrePublico = nombre,
            Descripcion = "Corretaje en Temuco",
            RubroId = Corretaje,
            ZonaPrincipal = "Centro",
            Metros = metros,
            // Lo que calcula la base: el kilometro entero que se muestra, y la reputacion
            // PUBLICA, que vale -1 con menos de 3 resenas (RN-S5-11).
            Tramo = metros is null ? null : metros < 1000 ? 0 : (int)Math.Round(metros.Value / 1000.0, MidpointRounding.AwayFromZero),
            TieneVerificada = verificada,
            PromedioCalificacion = promedio,
            ReputacionOrden = resenas >= 3 && promedio is not null ? promedio.Value : -1m,
            TotalResenas = resenas,
        };

    private static CriteriosDeBusqueda DesdeElPunto(
        short radioKm = LimitesDeBusqueda.RadioPorOmisionKm, string? cursor = null,
        Guid? quienBusca = null) =>
        new(Corretaje, Latitud: PlazaDeTemuco.Latitud, Longitud: PlazaDeTemuco.Longitud,
            RadioKm: radioKm, Cursor: cursor, QuienBuscaPerfilId: quienBusca);

    private static CriteriosDeBusqueda DesdeLaZona(int zonaId = Centro, string? cursor = null) =>
        new(Corretaje, ZonaId: zonaId, Cursor: cursor);

    // ------------------------------------------------------------- validacion

    /// <summary>E-422 de CU-08. Un rubro que no existe es un error, no una pagina vacia.</summary>
    [Fact]
    public async Task Buscar_en_un_rubro_que_no_existe_se_rechaza()
    {
        var r = await _servicio.BuscarAsync(new CriteriosDeBusqueda(77, ZonaId: Centro));

        Assert.False(r.Exito);
        Assert.Equal(Fallas.Validacion, r.Codigo);
        Assert.Equal(0, _repo.Llamadas);
    }

    [Fact]
    public async Task Buscar_en_un_rubro_apagado_se_rechaza()
    {
        var r = await _servicio.BuscarAsync(new CriteriosDeBusqueda(9, ZonaId: Centro));

        Assert.False(r.Exito);
        Assert.Equal(0, _repo.Llamadas);
    }

    [Fact]
    public async Task Buscar_sin_punto_y_sin_zona_pide_que_se_diga_donde()
    {
        var r = await _servicio.BuscarAsync(new CriteriosDeBusqueda(Corretaje));

        Assert.False(r.Exito);
        Assert.Equal(Fallas.Validacion, r.Codigo);
        Assert.Contains("ubicacion", r.Mensaje!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Buscar_en_una_zona_que_no_existe_se_rechaza()
    {
        var r = await _servicio.BuscarAsync(DesdeLaZona(zonaId: 987));

        Assert.False(r.Exito);
        Assert.Equal(Fallas.NoEncontrado, r.Codigo);
    }

    // ------------------------------------------------------------- E-400, el radio

    /// <summary>
    /// CU-08 dice que el radio fuera de rango se CORRIGE al valor valido mas cercano y se
    /// informa. Quien pide 500 km quiere buscar lejos, no leer un error.
    /// </summary>
    [Theory]
    [InlineData((short)0, 1)]
    [InlineData((short)-5, 1)]
    [InlineData((short)500, 50)]
    public async Task Un_radio_imposible_se_corrige_y_se_avisa(short pedido, int esperadoKm)
    {
        var r = await _servicio.BuscarAsync(DesdeElPunto(radioKm: pedido));

        Assert.True(r.Exito, r.Mensaje);
        Assert.Equal(esperadoKm * 1000, _repo.UltimoRadioMetros);
        Assert.Contains("radio", r.Valor!.Aviso!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Un_radio_valido_no_genera_aviso_ni_se_toca()
    {
        var r = await _servicio.BuscarAsync(DesdeElPunto(radioKm: 25));

        Assert.True(r.Exito, r.Mensaje);
        Assert.Equal(25_000, _repo.UltimoRadioMetros);
        Assert.Null(r.Valor!.Aviso);
    }

    // ------------------------------------------------------------- donde se busca

    [Fact]
    public async Task Con_punto_se_resuelve_la_zona_y_se_ordena_por_distancia()
    {
        _repo.Filas.Add(UnaFila());

        var r = await _servicio.BuscarAsync(DesdeElPunto());

        Assert.True(r.Exito, r.Mensaje);
        Assert.True(r.Valor!.OrdenadoPorDistancia);
        Assert.Equal("Centro", r.Valor!.ZonaBuscada);
        // La zona resuelta es la que viaja al SQL como filtro duro de cobertura.
        Assert.Equal(Centro, _repo.UltimosCriterios!.ZonaId);
        Assert.True(_repo.PidioPorDistancia);
    }

    /// <summary>
    /// Un punto en Labranza tiene que buscar en Labranza. Si se resolviera siempre al
    /// Centro, la cobertura filtraria por la zona equivocada y los resultados serian de
    /// otro barrio sin que nada falle.
    /// </summary>
    [Fact]
    public async Task El_punto_decide_la_zona_que_se_filtra()
    {
        var r = await _servicio.BuscarAsync(new CriteriosDeBusqueda(
            Corretaje, Latitud: -38.7660, Longitud: -72.7540));

        Assert.True(r.Exito, r.Mensaje);
        Assert.Equal(Labranza, _repo.UltimosCriterios!.ZonaId);
        Assert.Equal("Labranza", r.Valor!.ZonaBuscada);
    }

    /// <summary>
    /// Buscar desde fuera de toda zona conocida no es un error: es que todavia no
    /// llegamos ahi. Una pagina vacia con el motivo deja al cliente ofrecer otra zona
    /// (flujo 4B); un 404 le dejaria una pantalla de error.
    /// </summary>
    [Fact]
    public async Task Buscar_desde_lejisimos_devuelve_pagina_vacia_con_el_motivo()
    {
        var r = await _servicio.BuscarAsync(new CriteriosDeBusqueda(
            Corretaje, Latitud: -33.4489, Longitud: -70.6693));   // Santiago

        Assert.True(r.Exito, r.Mensaje);
        Assert.Empty(r.Valor!.Resultados);
        Assert.NotNull(r.Valor!.Aviso);
        Assert.Equal(0, _repo.Llamadas);
    }

    /// <summary>RN-S3-07 y flujo 4C de CU-08: sin punto, no hay orden por distancia.</summary>
    [Fact]
    public async Task Sin_punto_se_busca_por_zona_y_la_respuesta_lo_dice()
    {
        _repo.Filas.Add(UnaFila());

        var r = await _servicio.BuscarAsync(DesdeLaZona());

        Assert.True(r.Exito, r.Mensaje);
        Assert.False(r.Valor!.OrdenadoPorDistancia);
        Assert.False(_repo.PidioPorDistancia);
        Assert.Contains("distancia", r.Valor!.Aviso!, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Centro", r.Valor!.Aviso!);
    }

    /// <summary>Y en ese caso NINGUN resultado lleva distancia: no hay desde donde medirla.</summary>
    [Fact]
    public async Task Sin_punto_ningun_resultado_lleva_distancia()
    {
        _repo.Filas.Add(UnaFila(metros: 3_400));

        var r = await _servicio.BuscarAsync(DesdeLaZona());

        Assert.Null(r.Valor!.Resultados.Single().Distancia);
    }

    // ------------------------------------------------------------- RN-S3-03

    [Theory]
    [InlineData(3_400.0, "a 3 km")]
    [InlineData(12_100.0, "a 12 km")]
    [InlineData(400.0, "a menos de 1 km")]
    public async Task La_distancia_viaja_redondeada_y_como_texto(double metros, string esperado)
    {
        _repo.Filas.Add(UnaFila(metros: metros));

        var r = await _servicio.BuscarAsync(DesdeElPunto());

        Assert.Equal(esperado, r.Valor!.Resultados.Single().Distancia);
    }

    /// <summary>
    /// La prueba estructural de RN-S3-02: el tipo que viaja al cliente NO TIENE donde
    /// guardar metros, latitud ni longitud. Si alguien agrega el campo, esto se cae.
    /// </summary>
    [Fact]
    public void El_resultado_publico_no_tiene_campos_de_ubicacion_ni_de_metros()
    {
        var campos = typeof(ResultadoDeBusqueda).GetProperties().Select(p => p.Name).ToArray();

        Assert.DoesNotContain(campos, n => n.Contains("Latitud", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(campos, n => n.Contains("Longitud", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(campos, n => n.Contains("Metros", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(campos, n => n.Contains("Direccion", StringComparison.OrdinalIgnoreCase));
        // Y la distancia que si viaja es texto, no un numero.
        Assert.Equal(typeof(string), typeof(ResultadoDeBusqueda).GetProperty("Distancia")!.PropertyType);
    }

    // ------------------------------------------------------------- RN-S5-11

    [Fact]
    public async Task Con_menos_de_tres_resenas_no_se_publica_el_promedio()
    {
        _repo.Filas.Add(UnaFila(promedio: 5.0m, resenas: 2));

        var r = await _servicio.BuscarAsync(DesdeElPunto());

        var uno = r.Valor!.Resultados.Single();
        Assert.Null(uno.PromedioCalificacion);
        Assert.Equal(2, uno.TotalResenas);        // el recuento si se muestra
    }

    [Fact]
    public async Task Con_tres_resenas_o_mas_el_promedio_si_viaja()
    {
        _repo.Filas.Add(UnaFila(promedio: 4.5m, resenas: 3));

        var r = await _servicio.BuscarAsync(DesdeElPunto());

        Assert.Equal(4.5m, r.Valor!.Resultados.Single().PromedioCalificacion);
    }

    /// <summary>
    /// La consulta por zona guarda -1 en vez de nulo para poder ordenar sin que el nulo
    /// se cuele al final. Ese -1 es un detalle del SQL y no puede llegar al cliente.
    /// </summary>
    [Fact]
    public async Task El_menos_uno_del_orden_por_reputacion_no_se_escapa_al_cliente()
    {
        _repo.Filas.Add(UnaFila(promedio: -1m, resenas: 0));

        var r = await _servicio.BuscarAsync(DesdeLaZona());

        Assert.Null(r.Valor!.Resultados.Single().PromedioCalificacion);
    }

    // ------------------------------------------------------------- flujo 4F

    [Fact]
    public async Task Un_prestador_que_busca_su_rubro_se_ve_marcado()
    {
        var suyo = Guid.NewGuid();
        _repo.Filas.Add(UnaFila(id: suyo));
        _repo.Filas.Add(UnaFila(id: Guid.NewGuid()));

        var r = await _servicio.BuscarAsync(DesdeElPunto(quienBusca: suyo));

        Assert.True(r.Valor!.Resultados.Single(x => x.PerfilId == suyo).EsTuPerfil);
        Assert.All(r.Valor!.Resultados.Where(x => x.PerfilId != suyo),
            x => Assert.False(x.EsTuPerfil));
    }

    [Fact]
    public async Task Sin_sesion_ningun_resultado_queda_marcado_como_propio()
    {
        _repo.Filas.Add(UnaFila());

        var r = await _servicio.BuscarAsync(DesdeElPunto());

        Assert.All(r.Valor!.Resultados, x => Assert.False(x.EsTuPerfil));
    }

    // ------------------------------------------------------------- paginacion

    [Fact]
    public async Task Una_pagina_llena_devuelve_veinte_resultados_y_cursor()
    {
        for (var i = 0; i < 25; i++) _repo.Filas.Add(UnaFila(metros: 1_000 + i));

        var r = await _servicio.BuscarAsync(DesdeElPunto());

        Assert.Equal(LimitesDeBusqueda.PorPagina, r.Valor!.Resultados.Count);
        Assert.NotNull(r.Valor!.CursorSiguiente);
    }

    /// <summary>
    /// La consulta pide UNA fila mas que la pagina. Es como se sabe si hay siguiente sin
    /// contar el total, que con cursor seria otra consulta sobre toda la tabla.
    /// </summary>
    [Fact]
    public async Task La_consulta_pide_una_fila_mas_que_la_pagina()
    {
        await _servicio.BuscarAsync(DesdeElPunto());

        Assert.Equal(LimitesDeBusqueda.PorPagina + 1, _repo.UltimosCriterios!.Limite);
    }

    [Fact]
    public async Task La_ultima_pagina_no_devuelve_cursor()
    {
        for (var i = 0; i < 20; i++) _repo.Filas.Add(UnaFila(metros: 1_000 + i));

        var r = await _servicio.BuscarAsync(DesdeElPunto());

        Assert.Equal(20, r.Valor!.Resultados.Count);
        Assert.Null(r.Valor!.CursorSiguiente);
    }

    [Fact]
    public async Task Sin_resultados_no_hay_cursor()
    {
        var r = await _servicio.BuscarAsync(DesdeElPunto());

        Assert.Empty(r.Valor!.Resultados);
        Assert.Null(r.Valor!.CursorSiguiente);
    }

    /// <summary>
    /// El cursor lleva la ultima fila SERVIDA, no la fila extra que se pidio para saber
    /// si habia mas. Si llevara la extra, esa fila se saltaria en la pagina siguiente.
    /// </summary>
    [Fact]
    public async Task El_cursor_apunta_a_la_ultima_fila_servida_y_vuelve_entero()
    {
        var ultimaServida = Guid.NewGuid();
        for (var i = 0; i < 19; i++) _repo.Filas.Add(UnaFila(metros: 1_000 + i));
        _repo.Filas.Add(UnaFila(metros: 5_500, promedio: 4.2m, resenas: 7, verificada: true, id: ultimaServida));   // la numero 20
        _repo.Filas.Add(UnaFila(metros: 9_999));                      // la extra

        var primera = await _servicio.BuscarAsync(DesdeElPunto());
        await _servicio.BuscarAsync(DesdeElPunto(cursor: primera.Valor!.CursorSiguiente));

        // La posicion es la TUPLA entera por la que se ordena: con menos, un empate haria
        // que la pagina siguiente repitiera o se saltara a alguien.
        Assert.Equal(
            new PosicionDeBusqueda(Tramo: 6, Verificada: true, Reputacion: 4.2m, Resenas: 7, Id: ultimaServida),
            _repo.UltimaPosicion);
    }

    [Fact]
    public async Task El_cursor_por_reputacion_tambien_vuelve_entero()
    {
        var ultima = Guid.NewGuid();
        for (var i = 0; i < 19; i++) _repo.Filas.Add(UnaFila(promedio: 5m, resenas: 10));
        _repo.Filas.Add(UnaFila(promedio: 4.2m, resenas: 7, id: ultima));
        _repo.Filas.Add(UnaFila(promedio: 4.1m, resenas: 3));

        var primera = await _servicio.BuscarAsync(DesdeLaZona());
        await _servicio.BuscarAsync(DesdeLaZona(cursor: primera.Valor!.CursorSiguiente));

        Assert.Equal(
            new PosicionDeBusqueda(Tramo: null, Verificada: false, Reputacion: 4.2m, Resenas: 7, Id: ultima),
            _repo.UltimaPosicion);
    }

    /// <summary>
    /// El cursor lleva lo que ORDENA, no lo que se muestra. Una fila con 2 resenas y un 5,0
    /// guardado muestra "2 reseñas" y se ordena como -1: si el cursor llevara el 5,0, la
    /// pagina siguiente se posicionaria donde la base no puso a esa fila y se saltaria a
    /// gente.
    /// </summary>
    [Fact]
    public async Task El_cursor_lleva_la_reputacion_que_ordena_y_no_el_promedio_oculto()
    {
        for (var i = 0; i < 20; i++) _repo.Filas.Add(UnaFila(promedio: 5m, resenas: 10));
        _repo.Filas[19] = UnaFila(promedio: 5.0m, resenas: 2);     // la ultima servida
        _repo.Filas.Add(UnaFila());                                // la extra

        var primera = await _servicio.BuscarAsync(DesdeLaZona());
        await _servicio.BuscarAsync(DesdeLaZona(cursor: primera.Valor!.CursorSiguiente));

        Assert.Equal(-1m, _repo.UltimaPosicion!.Reputacion);
        Assert.Equal(2, _repo.UltimaPosicion!.Resenas);
    }

    [Theory]
    [InlineData("esto-no-es-base64!!")]
    [InlineData("bm8gdGllbmUgZm9ybWF0bw==")]
    public async Task Un_cursor_invalido_se_rechaza_sin_ir_a_la_base(string cursor)
    {
        var r = await _servicio.BuscarAsync(DesdeElPunto(cursor: cursor));

        Assert.False(r.Exito);
        Assert.Equal(Fallas.Validacion, r.Codigo);
        Assert.Equal(0, _repo.Llamadas);
    }

    /// <summary>
    /// Un cursor de una busqueda por distancia no sirve en una por zona: el orden es otro
    /// y la fila a la que apunta no significa lo mismo. Aceptarlo devolveria una pagina
    /// que se salta resultados sin que nadie lo note.
    /// </summary>
    [Fact]
    public async Task Un_cursor_de_distancia_no_sirve_para_buscar_por_zona()
    {
        for (var i = 0; i < 21; i++) _repo.Filas.Add(UnaFila(metros: 1_000 + i));
        var porDistancia = await _servicio.BuscarAsync(DesdeElPunto());

        var r = await _servicio.BuscarAsync(DesdeLaZona(cursor: porDistancia.Valor!.CursorSiguiente));

        Assert.False(r.Exito);
        Assert.Equal(Fallas.Validacion, r.Codigo);
    }

    /// <summary>Al reves: un cursor de zona no sirve para buscar por distancia.</summary>
    [Fact]
    public async Task Un_cursor_de_zona_no_sirve_para_buscar_por_distancia()
    {
        for (var i = 0; i < 21; i++) _repo.Filas.Add(UnaFila(promedio: 4m, resenas: 5));
        var porZona = await _servicio.BuscarAsync(DesdeLaZona());

        var r = await _servicio.BuscarAsync(DesdeElPunto(cursor: porZona.Valor!.CursorSiguiente));

        Assert.False(r.Exito);
        Assert.Equal(Fallas.Validacion, r.Codigo);
    }

    /// <summary>
    /// El campo "verificado" del cursor solo puede ser 0 o 1. Un 7 es un cursor fabricado a
    /// mano, y aceptarlo seria comparar la tupla del keyset contra un valor que ninguna
    /// fila puede tener.
    /// </summary>
    [Theory]
    [InlineData("r|7|4|3|{id}")]                      // verificado solo puede ser 0 o 1
    [InlineData("r|1|no-es-un-numero|3|{id}")]
    [InlineData("r|1|4|tres|{id}")]
    [InlineData("r|1|4|3|no-es-un-guid")]
    [InlineData("d|1|1|4|3|{id}")]                    // letra de distancia en una busqueda por zona
    public async Task Un_cursor_fabricado_a_mano_se_rechaza(string plantilla)
    {
        var texto = plantilla.Replace("{id}", Guid.NewGuid().ToString());
        var cursor = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(texto));

        var r = await _servicio.BuscarAsync(DesdeLaZona(cursor: cursor));

        Assert.False(r.Exito);
        Assert.Equal(Fallas.Validacion, r.Codigo);
        Assert.Equal(0, _repo.Llamadas);
    }

    // ------------------------------------------------------------- API-62, la comunidad

    [Fact]
    public async Task El_resumen_traduce_lo_que_devuelve_el_repositorio()
    {
        _repo.Resumen = new ResumenCrudo(
            Total: 9,
            PorZona: new[] { new ConteoCrudo(1, 5), new ConteoCrudo(14, 4) },
            PorRubro: new[] { new ConteoCrudo(1, 7), new ConteoCrudo(2, 2) },
            Recientes: new[] { new RecienLlegadoFila("marcela-soto", "Marcela Soto", "Corretaje de propiedades", "Centro", true) });

        var r = await _servicio.ResumenDeComunidadAsync();

        Assert.Equal(9, r.Total);
        Assert.Equal(new[] { 5, 4 }, r.PorZona.Select(z => z.Personas).ToArray());
        Assert.Equal(new short[] { 1, 2 }, r.PorRubro.Select(x => x.RubroId).ToArray());
        var uno = Assert.Single(r.RecienLlegados);
        Assert.Equal(new RecienLlegadoDto("marcela-soto", "Marcela Soto", "Corretaje de propiedades", "Centro", true), uno);
    }

    /// <summary>
    /// La hora la pone el reloj del sistema y viaja a la consulta: es con lo que se decide si una
    /// suscripcion esta vigente. Si la pusiera la base con now(), la portada y la busqueda
    /// podrian discrepar por el instante entre una consulta y otra.
    /// </summary>
    [Fact]
    public async Task El_resumen_usa_la_hora_del_sistema_y_pide_seis_recientes()
    {
        await _servicio.ResumenDeComunidadAsync();

        Assert.Equal(_reloj.Ahora, _repo.ResumenAhora);
        Assert.Equal(ServicioBusqueda.RecienLlegadosMaximos, _repo.ResumenRecientes);
        Assert.Equal(6, ServicioBusqueda.RecienLlegadosMaximos);
    }

    [Fact]
    public async Task Sin_nadie_el_resumen_viene_vacio_y_no_nulo()
    {
        var r = await _servicio.ResumenDeComunidadAsync();

        Assert.Equal(0, r.Total);
        Assert.Empty(r.PorZona);
        Assert.Empty(r.PorRubro);
        Assert.Empty(r.RecienLlegados);
    }

    /// <summary>Estructural, como el resto de la busqueda: nada de ubicacion ni de la cuenta.</summary>
    [Fact]
    public void El_resumen_no_tiene_campos_de_ubicacion_ni_de_la_cuenta()
    {
        var tipos = new[]
        {
            typeof(ResumenDeComunidadDto), typeof(PersonasPorZonaDto),
            typeof(PersonasPorRubroDto), typeof(RecienLlegadoDto),
        };
        var campos = tipos.SelectMany(t => t.GetProperties()).Select(p => p.Name).ToArray();

        foreach (var prohibido in new[] { "Direccion", "Latitud", "Longitud", "Ubicacion", "Metros", "UsuarioId", "Correo", "Telefono", "Hash" })
            Assert.DoesNotContain(campos, c => c.Contains(prohibido, StringComparison.OrdinalIgnoreCase));
    }

    // ------------------------------------------------------------- los filtros de CS-09

    [Fact]
    public async Task Los_filtros_de_verificado_y_calificacion_llegan_a_la_consulta()
    {
        await _servicio.BuscarAsync(new CriteriosDeBusqueda(
            Corretaje, ZonaId: Centro, SoloVerificados: true, CalificacionMinima: 4m));

        Assert.True(_repo.UltimosCriterios!.SoloVerificados);
        Assert.Equal(4m, _repo.UltimosCriterios!.CalificacionMinima);
    }

    [Fact]
    public async Task El_servicio_elegido_llega_a_la_consulta()
    {
        await _servicio.BuscarAsync(new CriteriosDeBusqueda(
            Corretaje, ServicioId: 101, ZonaId: Centro));

        Assert.Equal(101, _repo.UltimosCriterios!.ServicioId);
    }

    /// <summary>
    /// La hora la pone el reloj del sistema y viaja a la consulta: es con lo que el SQL
    /// decide si una suscripcion esta vigente (RN-S3-09). Si la pusiera la base con now(),
    /// no habria forma de probar "manana este prestador ya no aparece".
    /// </summary>
    [Fact]
    public async Task La_hora_del_sistema_viaja_a_la_consulta()
    {
        await _servicio.BuscarAsync(DesdeLaZona());

        Assert.Equal(_reloj.Ahora, _repo.UltimosCriterios!.Ahora);
    }
}
