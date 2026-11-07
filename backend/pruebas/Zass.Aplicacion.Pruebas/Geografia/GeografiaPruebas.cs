using Zass.Aplicacion.Geografia;
using Zass.Aplicacion.Pruebas.Dobles;
using Zass.Dominio.Catalogo;
using Zass.Dominio.Comun;
using Zass.Dominio.Identidad;

namespace Zass.Aplicacion.Pruebas.Geografia;

/// <summary>
/// MOD-04 - Geolocalizacion. Zonas, cobertura y distancias.
///
/// Las coordenadas de las zonas son las de verdad de Temuco: si fueran inventadas, las
/// pruebas de "el centroide mas cercano" pasarian igual y no probarian nada del mundo.
/// </summary>
public class GeografiaPruebas
{
    // Centro de Temuco (Plaza Anibal Pinto) y dos barrios reales, para que las
    // distancias que se afirman aqui sean comprobables en un mapa.
    private static readonly Punto PlazaDeTemuco = new(-38.7359, -72.5904);

    private const int Centro = 1;
    private const int Amanecer = 2;
    private const int Labranza = 3;

    private readonly BaseEnMemoria _base = new();
    private readonly RepositorioGeografiaEnMemoria _repo;
    private readonly GeocodificadorApagado _geocodificador = new();
    private readonly AuditoriaEnMemoria _auditor = new();
    private readonly RelojFijo _reloj = new(new DateTime(2026, 11, 7, 12, 0, 0, DateTimeKind.Utc));
    private readonly ServicioGeografia _servicio;

    public GeografiaPruebas()
    {
        UnaZona(Centro, "Centro", "centro", -38.7359, -72.5904);
        UnaZona(Amanecer, "Amanecer", "poniente", -38.7490, -72.6360);
        UnaZona(Labranza, "Labranza", "poniente", -38.7330, -72.7320);

        _repo = new RepositorioGeografiaEnMemoria(_base);
        _servicio = new ServicioGeografia(_repo, _geocodificador, _base, _reloj, _auditor);
    }

    private Zona UnaZona(int id, string nombre, string sector, double lat, double lon)
    {
        var z = new Zona
        {
            Id = id, Nombre = nombre, Sector = sector,
            CentroLatitud = lat, CentroLongitud = lon, Comuna = "Temuco",
        };
        _base.Zonas.Add(z);
        return z;
    }

    private Guid UnPrestador()
    {
        var perfil = new PerfilPrestador { Id = Guid.NewGuid(), UsuarioId = Guid.NewGuid() };
        _base.PerfilesPrestador.Add(perfil);
        return perfil.Id;
    }

    private static ReemplazarCoberturaCmd Cmd(Guid perfilId, params ZonaCubiertaCmd[] zonas) =>
        new(perfilId, zonas);

    private static ZonaCubiertaCmd Zona(int id, bool principal = false) => new(id, principal);

    // ------------------------------------------------------------- API-04

    [Fact]
    public async Task Las_zonas_se_listan_ordenadas_por_nombre()
    {
        var zonas = await _servicio.ZonasAsync();

        Assert.Equal(3, zonas.Count);
        Assert.Equal("Amanecer", zonas[0].Nombre);
    }

    [Fact]
    public async Task Las_zonas_se_pueden_filtrar_por_sector()
    {
        var zonas = await _servicio.ZonasAsync("poniente");

        Assert.Equal(2, zonas.Count);
        Assert.All(zonas, z => Assert.Equal("poniente", z.Sector));
    }

    /// <summary>
    /// El centroide SI viaja al cliente, y es deliberado: es el centro de un barrio, no
    /// la casa de nadie, y el mapa de la busqueda necesita algo que dibujar cuando el
    /// buscador no da su posicion (RN-S3-07).
    /// </summary>
    [Fact]
    public async Task La_zona_que_viaja_al_cliente_lleva_su_centroide()
    {
        var zonas = await _servicio.ZonasAsync("centro");

        Assert.Equal(-38.7359, zonas.Single().CentroLatitud, precision: 4);
    }

    // ------------------------------------------------------------- CS-10

    [Fact]
    public async Task La_zona_de_un_punto_es_la_del_centroide_mas_cercano()
    {
        var r = await _servicio.ResolverZonaAsync(PlazaDeTemuco);

        Assert.True(r.Exito, r.Mensaje);
        Assert.Equal("Centro", r.Valor!.Nombre);
    }

    /// <summary>Un punto en pleno Amanecer resuelve a Amanecer, no al Centro.</summary>
    [Fact]
    public async Task Un_punto_de_otro_barrio_resuelve_a_ese_barrio()
    {
        var r = await _servicio.ResolverZonaAsync(new Punto(-38.7495, -72.6370));

        Assert.True(r.Exito, r.Mensaje);
        Assert.Equal("Amanecer", r.Valor!.Nombre);
    }

    /// <summary>
    /// Sin tope, Santiago resolveria a un barrio de Temuco y el prestador apareceria
    /// como si atendiera ahi. Es el fallo silencioso que tiene este diseno si no se pone
    /// un limite, porque "el mas cercano" siempre devuelve algo.
    /// </summary>
    [Fact]
    public async Task Un_punto_lejisimos_no_resuelve_a_ninguna_zona()
    {
        var santiago = new Punto(-33.4489, -70.6693);

        var r = await _servicio.ResolverZonaAsync(santiago);

        Assert.False(r.Exito);
        Assert.Equal(Fallas.NoEncontrado, r.Codigo);
        Assert.Contains("50 km", r.Mensaje!);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(-91, -72)]
    [InlineData(-38, 181)]
    public async Task Unas_coordenadas_imposibles_o_el_punto_cero_se_rechazan(double lat, double lon)
    {
        var r = await _servicio.ResolverZonaAsync(new Punto(lat, lon));

        Assert.False(r.Exito);
    }

    [Fact]
    public async Task Sin_zonas_cargadas_se_dice_eso_y_no_se_devuelve_nada()
    {
        _base.Zonas.Clear();

        var r = await _servicio.ResolverZonaAsync(PlazaDeTemuco);

        Assert.False(r.Exito);
        Assert.Equal(Fallas.ReglaDeNegocio, r.Codigo);
    }

    // ------------------------------------------- el geocodificador que todavia no existe

    /// <summary>
    /// Hoy siempre falla, y tiene que fallar con un mensaje que lleve al flujo 4B de
    /// CU-03: elegir la zona de una lista. Un "error interno" ahi dejaria al prestador
    /// sin saber que puede seguir.
    /// </summary>
    [Fact]
    public async Task Geocodificar_una_direccion_dice_que_no_esta_disponible_y_como_seguir()
    {
        var r = await _servicio.ZonaDeDireccionAsync("Arturo Prat 123, Temuco");

        Assert.False(r.Exito);
        Assert.Equal(1, _geocodificador.Llamadas);
        Assert.Contains("zona", r.Mensaje!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Geocodificar_sin_direccion_no_llega_a_molestar_al_servicio_externo()
    {
        var r = await _servicio.ZonaDeDireccionAsync("   ");

        Assert.False(r.Exito);
        Assert.Equal(Fallas.Validacion, r.Codigo);
        Assert.Equal(0, _geocodificador.Llamadas);
    }

    /// <summary>El dia que haya proveedor: la direccion resuelve a la zona del punto.</summary>
    [Fact]
    public async Task Con_un_geocodificador_que_responde_la_direccion_resuelve_a_su_zona()
    {
        var servicio = new ServicioGeografia(
            _repo, new GeocodificadorFalso(new Punto(-38.7330, -72.7325)),
            _base, _reloj, _auditor);

        var r = await servicio.ZonaDeDireccionAsync("Camino Labranza 500");

        Assert.True(r.Exito, r.Mensaje);
        Assert.Equal("Labranza", r.Valor!.Nombre);
    }

    // ------------------------------------------------------------- API-21 / CU-10

    [Fact]
    public async Task La_cobertura_se_guarda_con_su_zona_principal()
    {
        var perfilId = UnPrestador();

        var r = await _servicio.ReemplazarCoberturaAsync(
            Cmd(perfilId, Zona(Centro, principal: true), Zona(Amanecer)));

        Assert.True(r.Exito, r.Mensaje);
        var cobertura = await _servicio.ZonasDeCoberturaAsync(perfilId);
        Assert.Equal(2, cobertura.Count);
        Assert.True(cobertura[0].EsPrincipal);          // la principal va primero
        Assert.Equal("Centro", cobertura[0].Nombre);
    }

    [Fact]
    public async Task La_cobertura_vacia_se_rechaza()
    {
        var perfilId = UnPrestador();

        var r = await _servicio.ReemplazarCoberturaAsync(Cmd(perfilId));

        Assert.False(r.Exito);
        Assert.Equal(Fallas.ReglaDeNegocio, r.Codigo);
        Assert.Contains("despublica", r.Mensaje!, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>R-2 y RN-S3-08: ni cero principales ni dos.</summary>
    [Fact]
    public async Task Sin_zona_principal_se_rechaza()
    {
        var perfilId = UnPrestador();

        var r = await _servicio.ReemplazarCoberturaAsync(
            Cmd(perfilId, Zona(Centro), Zona(Amanecer)));

        Assert.False(r.Exito);
        Assert.Equal(Fallas.Validacion, r.Codigo);
        Assert.Contains("principal", r.Mensaje!);
    }

    [Fact]
    public async Task Con_dos_zonas_principales_se_rechaza()
    {
        var perfilId = UnPrestador();

        var r = await _servicio.ReemplazarCoberturaAsync(
            Cmd(perfilId, Zona(Centro, true), Zona(Amanecer, true)));

        Assert.False(r.Exito);
        Assert.Equal(Fallas.Validacion, r.Codigo);
    }

    [Fact]
    public async Task Una_zona_repetida_se_rechaza()
    {
        var perfilId = UnPrestador();

        var r = await _servicio.ReemplazarCoberturaAsync(
            Cmd(perfilId, Zona(Centro, true), Zona(Centro)));

        Assert.False(r.Exito);
        Assert.Equal(Fallas.Validacion, r.Codigo);
    }

    [Fact]
    public async Task Una_zona_que_no_existe_se_rechaza_diciendo_cual()
    {
        var perfilId = UnPrestador();

        var r = await _servicio.ReemplazarCoberturaAsync(
            Cmd(perfilId, Zona(Centro, true), Zona(987)));

        Assert.False(r.Exito);
        Assert.Equal(Fallas.Validacion, r.Codigo);
        Assert.Contains("987", r.Mensaje!);
    }

    [Fact]
    public async Task Mas_zonas_que_el_tope_se_rechazan()
    {
        var perfilId = UnPrestador();
        for (var i = 10; i < 10 + ServicioGeografia.MaxZonas + 1; i++)
            UnaZona(i, $"Zona {i}", "norte", -38.73 - i * 0.001, -72.59);

        var muchas = Enumerable.Range(10, ServicioGeografia.MaxZonas + 1)
            .Select((id, pos) => Zona(id, pos == 0))
            .ToArray();

        var r = await _servicio.ReemplazarCoberturaAsync(Cmd(perfilId, muchas));

        Assert.False(r.Exito);
        Assert.Equal(Fallas.ReglaDeNegocio, r.Codigo);
        Assert.Contains(ServicioGeografia.MaxZonas.ToString(), r.Mensaje!);
    }

    [Fact]
    public async Task Las_zonas_se_validan_en_una_sola_consulta()
    {
        var perfilId = UnPrestador();
        var antes = _repo.ConsultasEnLote;

        await _servicio.ReemplazarCoberturaAsync(
            Cmd(perfilId, Zona(Centro, true), Zona(Amanecer), Zona(Labranza)));

        Assert.Equal(1, _repo.ConsultasEnLote - antes);
    }

    /// <summary>
    /// Cambiar de zona principal es el caso que el unico parcial de R-2 puede romper si
    /// se marca la nueva antes de desmarcar la vieja. Aqui se comprueba el resultado; que
    /// la BASE lo acepte se comprueba en integracion.
    /// </summary>
    [Fact]
    public async Task Cambiar_la_zona_principal_deja_una_sola()
    {
        var perfilId = UnPrestador();
        await _servicio.ReemplazarCoberturaAsync(
            Cmd(perfilId, Zona(Centro, true), Zona(Amanecer)));

        var r = await _servicio.ReemplazarCoberturaAsync(
            Cmd(perfilId, Zona(Centro), Zona(Amanecer, true)));

        Assert.True(r.Exito, r.Mensaje);
        var cobertura = await _servicio.ZonasDeCoberturaAsync(perfilId);
        Assert.Single(cobertura.Where(z => z.EsPrincipal));
        Assert.Equal("Amanecer", cobertura.Single(z => z.EsPrincipal).Nombre);
    }

    [Fact]
    public async Task La_zona_que_sale_de_la_lista_se_quita()
    {
        var perfilId = UnPrestador();
        await _servicio.ReemplazarCoberturaAsync(
            Cmd(perfilId, Zona(Centro, true), Zona(Amanecer)));

        var r = await _servicio.ReemplazarCoberturaAsync(Cmd(perfilId, Zona(Centro, true)));

        Assert.True(r.Exito, r.Mensaje);
        var cobertura = await _servicio.ZonasDeCoberturaAsync(perfilId);
        Assert.Single(cobertura);
    }

    /// <summary>
    /// Quitar una zona SI borra la fila, al contrario que los servicios de MOD-03: nada
    /// cuelga de cobertura_prestador. Las citas apuntan a prestador_servicio, no aqui.
    /// </summary>
    [Fact]
    public async Task La_zona_que_se_mantiene_conserva_desde_cuando_se_cubre()
    {
        var perfilId = UnPrestador();
        await _servicio.ReemplazarCoberturaAsync(Cmd(perfilId, Zona(Centro, true)));
        var desde = (await _servicio.ZonasDeCoberturaAsync(perfilId)).Single().DesdeEn;

        _reloj.Avanzar(TimeSpan.FromDays(30));
        await _servicio.ReemplazarCoberturaAsync(
            Cmd(perfilId, Zona(Centro, true), Zona(Amanecer)));

        var cobertura = await _servicio.ZonasDeCoberturaAsync(perfilId);
        Assert.Equal(desde, cobertura.Single(z => z.ZonaId == Centro).DesdeEn);
    }

    [Fact]
    public async Task Reemplazar_la_cobertura_no_toca_la_de_otro_prestador()
    {
        var unoId = UnPrestador();
        var otroId = UnPrestador();
        await _servicio.ReemplazarCoberturaAsync(
            Cmd(otroId, Zona(Centro, true), Zona(Amanecer)));

        await _servicio.ReemplazarCoberturaAsync(Cmd(unoId, Zona(Labranza, true)));

        Assert.Equal(2, (await _servicio.ZonasDeCoberturaAsync(otroId)).Count);
    }

    [Fact]
    public async Task Una_cobertura_que_falla_no_deja_nada_a_medias()
    {
        var perfilId = UnPrestador();

        var r = await _servicio.ReemplazarCoberturaAsync(
            Cmd(perfilId, Zona(Centro, true), Zona(987)));

        Assert.False(r.Exito);
        Assert.Empty(_base.Coberturas);
    }

    [Fact]
    public async Task Reemplazar_la_cobertura_queda_en_la_auditoria()
    {
        var perfilId = UnPrestador();

        await _servicio.ReemplazarCoberturaAsync(Cmd(perfilId, Zona(Centro, true)));

        Assert.Contains(_auditor.Registros, a => a.Accion == "reemplazar_cobertura");
    }

    // ------------------------------------------------------------- RN-S3-03

    /// <summary>
    /// Plaza de Temuco a Labranza son unos 12 km en linea recta. Se comprueba contra una
    /// distancia del mundo real, no contra lo que devuelva la propia funcion.
    /// </summary>
    [Fact]
    public void La_distancia_interna_se_mide_en_metros_y_cuadra_con_el_mapa()
    {
        var metros = _servicio.DistanciaEnMetros(
            PlazaDeTemuco, new Punto(-38.7330, -72.7320));

        Assert.InRange(metros, 11_500, 13_000);
    }

    [Fact]
    public void La_distancia_de_un_punto_a_si_mismo_es_cero()
    {
        Assert.Equal(0, _servicio.DistanciaEnMetros(PlazaDeTemuco, PlazaDeTemuco), precision: 6);
    }

    /// <summary>
    /// RN-S3-02 y RN-S3-03. Bajo un kilometro NO se da numero: "a 400 m" en tres fichas
    /// distintas ubica una casa por trilateracion.
    /// </summary>
    [Fact]
    public void Por_debajo_de_un_kilometro_no_se_publica_un_numero()
    {
        var cerca = new Punto(PlazaDeTemuco.Latitud + 0.002, PlazaDeTemuco.Longitud);

        var texto = _servicio.DistanciaPublica(PlazaDeTemuco, cerca);

        Assert.Equal("a menos de 1 km", texto);
    }

    [Fact]
    public void La_distancia_publica_va_en_kilometros_enteros()
    {
        var texto = _servicio.DistanciaPublica(PlazaDeTemuco, new Punto(-38.7330, -72.7320));

        Assert.Equal("a 12 km", texto);
    }

    /// <summary>Ningun texto publico puede llevar metros: seria precision regalada.</summary>
    [Theory]
    [InlineData(0.0)]
    [InlineData(999.0)]
    [InlineData(1500.0)]
    [InlineData(49_000.0)]
    public void Ningun_texto_publico_menciona_metros(double metros)
    {
        var texto = Geo.DistanciaPublica(metros);

        Assert.DoesNotContain(" m", texto.Replace(" menos", ""));
    }
}
