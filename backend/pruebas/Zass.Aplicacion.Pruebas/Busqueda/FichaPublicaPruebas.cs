using Zass.Aplicacion.Busqueda;
using Zass.Aplicacion.Catalogo;
using Zass.Aplicacion.Credenciales;
using Zass.Aplicacion.Geografia;
using Zass.Aplicacion.Identidad;
using Zass.Aplicacion.Pruebas.Dobles;
using Zass.Dominio.Catalogo;
using Zass.Dominio.Comun;
using Zass.Dominio.Identidad;

namespace Zass.Aplicacion.Pruebas.Busqueda;

/// <summary>
/// CU-09 · API-05, la ficha publica completa. Es MOD-05 quien la compone, porque ningun
/// otro modulo puede: MOD-01 es dueno del perfil pero no de los servicios, y MOD-03 al reves.
///
/// Los tres modulos van con sus servicios DE VERDAD sobre listas en memoria, igual que en
/// las pruebas de la busqueda: asi "la composicion junta lo que debe" se comprueba con la
/// misma logica que corre en produccion.
/// </summary>
public class FichaPublicaPruebas
{
    private const short Corretaje = 1;

    private readonly BaseEnMemoria _base = new();
    private readonly RelojFijo _reloj = new(new DateTime(2026, 11, 7, 12, 0, 0, DateTimeKind.Utc));
    private readonly ServicioBusqueda _servicio;

    public FichaPublicaPruebas()
    {
        _base.Rubros.Add(new Rubro { Id = Corretaje, Codigo = "corretaje", Nombre = "Corretaje de propiedades", Icono = "llave", Activo = true });
        _base.Zonas.Add(new Zona { Id = 1, Nombre = "Centro", Sector = "centro", CentroLatitud = -38.74, CentroLongitud = -72.59, Comuna = "Temuco" });
        _base.Zonas.Add(new Zona { Id = 11, Nombre = "Amanecer", Sector = "poniente", CentroLatitud = -38.75, CentroLongitud = -72.62, Comuna = "Temuco" });
        _base.Zonas.Add(new Zona { Id = 14, Nombre = "Labranza", Sector = "poniente", CentroLatitud = -38.76, CentroLongitud = -72.75, Comuna = "Temuco" });

        var auditor = new AuditoriaEnMemoria();
        var catalogo = new ServicioCatalogo(new RepositorioCatalogoEnMemoria(_base), _base, _reloj, auditor);
        var geografia = new ServicioGeografia(
            new RepositorioGeografiaEnMemoria(_base), new GeocodificadorApagado(), _base, _reloj, auditor);
        var identidad = new ServicioIdentidad(
            new RepositorioUsuariosEnMemoria(_base), new RepositorioPerfilesEnMemoria(_base));

        _servicio = new ServicioBusqueda(
            new RepositorioBusquedaFalso(), catalogo, geografia, identidad,
            new ServicioCredenciales(new RepositorioCredencialesEnMemoria(_base), _base, _reloj, auditor),
            _reloj);
    }

    private PerfilPrestador UnPerfil(
        string slug = "marcela-soto", EstadoPerfil estado = EstadoPerfil.Publicado,
        short? rubroId = Corretaje, decimal? promedio = null, int resenas = 0)
    {
        var perfil = new PerfilPrestador
        {
            UsuarioId = Guid.NewGuid(),
            Slug = slug,
            NombrePublico = "Marcela Soto",
            Descripcion = "Corredora de propiedades en Temuco.",
            RubroId = rubroId,
            Estado = estado,
            PublicadoEn = estado == EstadoPerfil.Publicado ? _reloj.Ahora : null,
            TelefonoPublico = "+56 9 1234 5678",
            Whatsapp = "912345678",
            CorreoPublico = "contacto@soto.cl",
            SitioWeb = "https://soto.cl",
            PromedioCalificacion = promedio,
            TotalResenas = resenas,
        };
        _base.PerfilesPrestador.Add(perfil);
        return perfil;
    }

    private void Ofrece(PerfilPrestador p, int servicioId, string nombre, short duracion = 60, int? precio = null, bool activo = true)
    {
        if (_base.Servicios.All(s => s.Id != servicioId))
            _base.Servicios.Add(new Servicio { Id = servicioId, RubroId = Corretaje, Nombre = nombre, NombreNormalizado = nombre.ToLowerInvariant(), EsCatalogo = true });
        _base.PrestadorServicios.Add(new PrestadorServicio
        {
            PerfilPrestadorId = p.Id, ServicioId = servicioId, DuracionMinutos = duracion,
            PrecioReferencia = precio, Activo = activo,
        });
    }

    private void Cubre(PerfilPrestador p, int zonaId, bool principal) =>
        _base.Coberturas.Add(new CoberturaPrestador { PerfilPrestadorId = p.Id, ZonaId = zonaId, EsPrincipal = principal, DesdeEn = _reloj.Ahora });

    // ------------------------------------------------------------- lo que la compone

    [Fact]
    public async Task Compone_el_perfil_con_el_nombre_del_rubro_los_servicios_y_las_zonas()
    {
        var p = UnPerfil();
        Ofrece(p, 101, "Tasación comercial", duracion: 60, precio: 35_000);
        Cubre(p, 1, principal: true);

        var ficha = await _servicio.FichaPublicaAsync("marcela-soto");

        Assert.NotNull(ficha);
        Assert.Equal("Marcela Soto", ficha!.NombrePublico);
        // El nombre del rubro, no solo su id: la ficha lo muestra tal cual.
        Assert.Equal("Corretaje de propiedades", ficha.Rubro!.Nombre);
        Assert.Equal("corretaje", ficha.Rubro.Codigo);
        var servicio = Assert.Single(ficha.Servicios);
        Assert.Equal(new ServicioPublicoDto("Tasación comercial", 60, 35_000), servicio);
        var zona = Assert.Single(ficha.Zonas);
        Assert.Equal(new ZonaPublicaDto("Centro", "centro", true), zona);
    }

    [Fact]
    public async Task El_contacto_es_el_que_el_prestador_marco_como_publico()
    {
        UnPerfil();

        var ficha = await _servicio.FichaPublicaAsync("marcela-soto");

        Assert.Equal(
            new ContactoPublicoDto("+56 9 1234 5678", "912345678", "contacto@soto.cl", "https://soto.cl"),
            ficha!.Contacto);
    }

    [Fact]
    public async Task Un_contacto_que_no_se_puso_viaja_como_nulo()
    {
        var p = UnPerfil();
        p.TelefonoPublico = null; p.Whatsapp = null; p.CorreoPublico = null; p.SitioWeb = null;

        var ficha = await _servicio.FichaPublicaAsync("marcela-soto");

        Assert.Equal(new ContactoPublicoDto(null, null, null, null), ficha!.Contacto);
    }

    // ------------------------------------------------------------- servicios

    /// <summary>
    /// Lo que el prestador DEJO de ofrecer se conserva en la base (las citas y los horarios
    /// cuelgan de esas filas) pero no es publico.
    /// </summary>
    [Fact]
    public async Task Los_servicios_desactivados_no_salen()
    {
        var p = UnPerfil();
        Ofrece(p, 101, "Tasación comercial");
        Ofrece(p, 102, "Administración de arriendos", activo: false);

        var ficha = await _servicio.FichaPublicaAsync("marcela-soto");

        Assert.Equal(new[] { "Tasación comercial" }, ficha!.Servicios.Select(s => s.Nombre).ToArray());
    }

    /// <summary>
    /// "Ñ" y las tildes ordenan donde corresponde en espanol y no despues de la zeta, que es
    /// lo que hace una comparacion ordinal.
    /// </summary>
    [Fact]
    public async Task Los_servicios_van_ordenados_por_nombre_en_espanol()
    {
        var p = UnPerfil();
        Ofrece(p, 101, "Venta de casa");
        Ofrece(p, 102, "Árbol genealógico");
        Ofrece(p, 103, "Tasación");
        Ofrece(p, 104, "Administración");

        var ficha = await _servicio.FichaPublicaAsync("marcela-soto");

        Assert.Equal(
            new[] { "Administración", "Árbol genealógico", "Tasación", "Venta de casa" },
            ficha!.Servicios.Select(s => s.Nombre).ToArray());
    }

    [Fact]
    public async Task Sin_servicios_la_lista_va_vacia_y_no_nula()
    {
        UnPerfil();

        var ficha = await _servicio.FichaPublicaAsync("marcela-soto");

        Assert.NotNull(ficha!.Servicios);
        Assert.Empty(ficha.Servicios);
    }

    /// <summary>El mismo servicio de OTRO prestador no se mezcla.</summary>
    [Fact]
    public async Task No_trae_los_servicios_de_otro_prestador()
    {
        var suyo = UnPerfil("marcela-soto");
        var ajeno = UnPerfil("otro-prestador");
        Ofrece(suyo, 101, "Tasación comercial");
        Ofrece(ajeno, 102, "Administración de arriendos");

        var ficha = await _servicio.FichaPublicaAsync("marcela-soto");

        Assert.Equal(new[] { "Tasación comercial" }, ficha!.Servicios.Select(s => s.Nombre).ToArray());
    }

    // ------------------------------------------------------------- zonas

    /// <summary>RN-S3-08: la zona principal va primero, y la ficha la marca como tal.</summary>
    [Fact]
    public async Task La_zona_principal_va_primero_y_esta_marcada()
    {
        var p = UnPerfil();
        Cubre(p, 14, principal: false);
        Cubre(p, 11, principal: false);
        Cubre(p, 1, principal: true);

        var ficha = await _servicio.FichaPublicaAsync("marcela-soto");

        Assert.Equal("Centro", ficha!.Zonas[0].Nombre);
        Assert.True(ficha.Zonas[0].EsPrincipal);
        Assert.All(ficha.Zonas.Skip(1), z => Assert.False(z.EsPrincipal));
        Assert.Equal(3, ficha.Zonas.Count);
    }

    // ------------------------------------------------------------- el rubro

    /// <summary>
    /// Una ficha publicada tiene que poder decir en que rubro esta aunque el equipo apague
    /// despues ese rubro del selector: sin esto, un rubro desactivado dejaria fichas sin
    /// rubro de un dia para otro.
    /// </summary>
    [Fact]
    public async Task Un_rubro_apagado_sigue_apareciendo_en_la_ficha_de_quien_ya_lo_tiene()
    {
        UnPerfil();
        _base.Rubros.Single().Activo = false;

        var ficha = await _servicio.FichaPublicaAsync("marcela-soto");

        Assert.Equal("Corretaje de propiedades", ficha!.Rubro!.Nombre);
    }

    [Fact]
    public async Task Sin_rubro_la_ficha_sale_igual_con_el_rubro_en_nulo()
    {
        UnPerfil(rubroId: null);

        var ficha = await _servicio.FichaPublicaAsync("marcela-soto");

        Assert.NotNull(ficha);
        Assert.Null(ficha!.Rubro);
    }

    // ------------------------------------------------------------- RN-S5-11

    [Fact]
    public async Task Con_menos_de_tres_resenas_no_se_publica_el_promedio_pero_si_el_recuento()
    {
        UnPerfil(promedio: 5.0m, resenas: 2);

        var ficha = await _servicio.FichaPublicaAsync("marcela-soto");

        Assert.Null(ficha!.PromedioCalificacion);
        Assert.Equal(2, ficha.TotalResenas);
    }

    [Fact]
    public async Task Con_tres_resenas_o_mas_el_promedio_si_viaja()
    {
        UnPerfil(promedio: 4.6m, resenas: 12);

        var ficha = await _servicio.FichaPublicaAsync("marcela-soto");

        Assert.Equal(4.6m, ficha!.PromedioCalificacion);
    }

    // ------------------------------------------------------------- lo que no existe para el publico

    [Theory]
    [InlineData(EstadoPerfil.Borrador)]
    [InlineData(EstadoPerfil.Despublicado)]
    public async Task Un_perfil_sin_publicar_no_existe_para_el_publico(EstadoPerfil estado)
    {
        UnPerfil(estado: estado);

        Assert.Null(await _servicio.FichaPublicaAsync("marcela-soto"));
    }

    [Theory]
    [InlineData("no-existe")]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Un_slug_inexistente_o_vacio_devuelve_nulo(string slug)
    {
        UnPerfil();

        Assert.Null(await _servicio.FichaPublicaAsync(slug));
    }

    [Fact]
    public async Task Un_slug_con_espacios_alrededor_se_recorta()
    {
        UnPerfil();

        Assert.NotNull(await _servicio.FichaPublicaAsync("  marcela-soto  "));
    }

    // ------------------------------------------------------------- RN-S3-02

    /// <summary>
    /// La prueba estructural de RN-S3-02, sobre la ficha completa Y sus tipos anidados: ni
    /// direccion, ni coordenadas, ni identificadores de la cuenta. Si alguien agrega uno de
    /// esos campos a cualquiera de los DTO, esto se cae.
    /// </summary>
    [Fact]
    public void La_ficha_publica_no_tiene_campos_de_ubicacion_ni_de_la_cuenta()
    {
        var tipos = new[]
        {
            typeof(FichaPublicaDto), typeof(ZonaPublicaDto),
            typeof(ContactoPublicoDto), typeof(ServicioPublicoDto), typeof(RubroDto),
        };
        var campos = tipos.SelectMany(t => t.GetProperties()).Select(p => p.Name).ToArray();

        foreach (var prohibido in new[] { "Direccion", "Latitud", "Longitud", "Ubicacion", "Metros", "UsuarioId", "Hash", "Contrasena", "Rol" })
            Assert.DoesNotContain(campos, c => c.Contains(prohibido, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Y el servicio publico tampoco lleva lo interno: ni el id de la fila de oferta (que es
    /// de las citas y los horarios) ni el modo de agenda.
    /// </summary>
    [Fact]
    public void El_servicio_publico_no_expone_ids_internos_ni_el_modo_de_agenda()
    {
        var campos = typeof(ServicioPublicoDto).GetProperties().Select(p => p.Name).ToArray();

        Assert.Equal(new[] { "Nombre", "DuracionMinutos", "PrecioReferencia" }, campos);
    }
}
