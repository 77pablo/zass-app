using Microsoft.EntityFrameworkCore;
using Zass.Infraestructura.Datos;
using Npgsql;
using Zass.Aplicacion.Geografia;
using Zass.Dominio.Catalogo;
using Zass.Dominio.Comun;
using Zass.Dominio.Identidad;
using Zass.Infraestructura.Datos.Repositorios;
using Zass.Infraestructura.Seguridad;

namespace Zass.Integracion.Pruebas;

/// <summary>
/// MOD-04 contra PostgreSQL de verdad.
///
/// Lo que una lista en memoria no puede contestar: si las 16 zonas quedaron sembradas con
/// coordenadas que caen en Temuco, si la secuencia de identidad quedo donde debe, y si el
/// unico parcial ux_cobertura_principal —que hace cumplir R-2— acepta de verdad un cambio
/// de zona principal. Eso ultimo depende del orden en que EF Core emite los UPDATE, y es
/// justo lo que no se puede comprobar sin base.
/// </summary>
public class GeografiaIntegracionPruebas : IDisposable
{
    private readonly ContextoDePrueba _prueba = new();
    private readonly RepositorioGeografiaEfCore _repo;
    private readonly ServicioGeografia _servicio;

    // Plaza Anibal Pinto, el centro de Temuco.
    private static readonly Punto PlazaDeTemuco = new(-38.7359, -72.5904);

    public GeografiaIntegracionPruebas()
    {
        var ctx = _prueba.Ctx;
        _repo = new RepositorioGeografiaEfCore(ctx);
        _servicio = new ServicioGeografia(
            _repo, new GeocodificadorNoDisponible(), new UnidadDeTrabajoEfCore(ctx),
            new RelojUtc(), new AuditorEfCore(ctx));
    }

    public void Dispose() => _prueba.Dispose();

    private async Task<PerfilPrestador> UnPrestadorAsync()
    {
        var usuario = new Usuario
        {
            Correo = $"geo-{Guid.NewGuid():N}@zass.local",
            HashContrasena = "x",
            Nombre = "Prestador de prueba",
            CorreoVerificado = true,
        };
        _prueba.Ctx.Usuarios.Add(usuario);

        var perfil = new PerfilPrestador
        {
            UsuarioId = usuario.Id,
            NombrePublico = $"Estudio {Guid.NewGuid():N}",
            Slug = $"estudio-{Guid.NewGuid():N}",
        };
        _prueba.Ctx.PerfilesPrestador.Add(perfil);
        await _prueba.Ctx.SaveChangesAsync();
        return perfil;
    }

    private static ReemplazarCoberturaCmd Cmd(Guid perfilId, params ZonaCubiertaCmd[] zonas) =>
        new(perfilId, zonas);

    // ------------------------------------------------------- Las zonas sembradas

    /// <summary>
    /// Cuantas comunas tiene CADA region segun la division politico-administrativa oficial. Es el control con que se
    /// validaron los datos bajados de OpenStreetMap: si una region trae una de mas o de menos, algo se perdio o se colo.
    /// </summary>
    private static readonly Dictionary<string, int> ComunasPorRegion = new()
    {
        ["Arica y Parinacota"] = 4, ["Tarapacá"] = 7, ["Antofagasta"] = 9, ["Atacama"] = 9, ["Coquimbo"] = 15,
        ["Valparaíso"] = 38, ["Metropolitana de Santiago"] = 52, ["O'Higgins"] = 33, ["Maule"] = 30, ["Ñuble"] = 21,
        ["Biobío"] = 33, ["La Araucanía"] = 32, ["Los Ríos"] = 12, ["Los Lagos"] = 30, ["Aysén"] = 10,
        ["Magallanes y la Antártica Chilena"] = 11,
    };

    [Fact]
    public async Task Las_346_comunas_y_los_16_barrios_vinieron_con_el_esquema()
    {
        var zonas = await _repo.ZonasAsync();
        var comunas = zonas.Where(z => z.Tipo == TipoDeZona.Comuna).ToList();
        var barrios = zonas.Where(z => z.Tipo == TipoDeZona.Barrio).ToList();

        Assert.Equal(346, comunas.Count);
        Assert.Equal(16, barrios.Count);
        Assert.Equal(362, zonas.Count);
        Assert.Equal(ComunasPorRegion.Count, comunas.Select(c => c.Region).Distinct().Count());
        foreach (var (region, cuantas) in ComunasPorRegion)
            Assert.Equal(cuantas, comunas.Count(c => c.Region == region));
        Assert.All(zonas, z => Assert.False(string.IsNullOrWhiteSpace(z.Region)));
    }

    [Fact]
    public async Task Las_comunas_que_se_esperan_estan_y_con_el_nombre_que_la_gente_usa()
    {
        var comunas = (await _repo.ZonasAsync()).Where(z => z.Tipo == TipoDeZona.Comuna).Select(z => z.Nombre).ToHashSet();

        foreach (var esperada in new[] { "Arica", "Santiago", "Temuco", "Padre Las Casas", "Valdivia", "Punta Arenas", "Isla de Pascua", "Antártica", "Ñiquén", "Coyhaique" })
            Assert.Contains(esperada, comunas);
    }

    /// <summary>Los barrios de antes ahora son barrios de la comuna de Temuco, y la base lo exige (padre_id).</summary>
    [Fact]
    public async Task Los_16_barrios_cuelgan_de_la_comuna_de_Temuco()
    {
        var zonas = await _repo.ZonasAsync();
        var temuco = zonas.Single(z => z.Tipo == TipoDeZona.Comuna && z.Nombre == "Temuco");
        var barrios = zonas.Where(z => z.Tipo == TipoDeZona.Barrio).ToList();

        Assert.Equal(ZonasDeChile.IdTemuco, temuco.Id);
        Assert.Contains(barrios, z => z.Nombre == "Centro");
        Assert.Contains(barrios, z => z.Nombre == "Labranza");
        Assert.All(barrios, z =>
        {
            Assert.Equal(temuco.Id, z.PadreId);
            Assert.Equal("Temuco", z.Comuna);
            Assert.Equal("La Araucanía", z.Region);
        });
        Assert.All(zonas.Where(z => z.Tipo == TipoDeZona.Comuna), z => Assert.Null(z.PadreId));
    }

    /// <summary>
    /// Las coordenadas de los barrios vienen de OpenStreetMap, pero eso no las hace correctas por si solo: lo que se
    /// comprueba es que ninguna se haya colado invertida o en otra region. Temuco esta en torno a -38,7 / -72,6; una
    /// latitud positiva o una longitud de -38 serian latitud y longitud al reves, el error clasico y silencioso.
    /// </summary>
    [Fact]
    public async Task Ningun_barrio_quedo_fuera_de_Temuco_ni_con_las_coordenadas_al_reves()
    {
        var barrios = (await _repo.ZonasAsync()).Where(z => z.Tipo == TipoDeZona.Barrio);

        Assert.All(barrios, z =>
        {
            Assert.InRange(z.CentroLatitud, -39.1, -38.4);
            Assert.InRange(z.CentroLongitud, -73.0, -72.4);
        });
    }

    [Fact]
    public async Task Cada_barrio_esta_a_menos_de_30_km_del_centro_de_Temuco()
    {
        var barrios = (await _repo.ZonasAsync()).Where(z => z.Tipo == TipoDeZona.Barrio);

        Assert.All(barrios, z =>
        {
            var metros = Geo.DistanciaEnMetros(
                PlazaDeTemuco, new Punto(z.CentroLatitud, z.CentroLongitud));
            Assert.True(metros < 30_000, $"{z.Nombre} quedo a {metros / 1000:0.0} km del centro");
        });
    }

    /// <summary>
    /// Lo mismo para las 346 comunas: ninguna con latitud y longitud al reves ni fuera de Chile. Las continentales caben
    /// en la franja de -17,4 a -56,2 de latitud; las tres excepciones son las unicas que no son continentales.
    /// </summary>
    [Fact]
    public async Task Ninguna_comuna_quedo_fuera_de_Chile_ni_con_las_coordenadas_al_reves()
    {
        var comunas = (await _repo.ZonasAsync()).Where(z => z.Tipo == TipoDeZona.Comuna).ToList();
        var lejanas = new[] { "Isla de Pascua", "Juan Fernández", "Antártica" };

        Assert.All(comunas, z =>
        {
            Assert.InRange(z.CentroLatitud, -63.0, -17.0);
            Assert.InRange(z.CentroLongitud, -110.0, -58.0);
        });
        Assert.All(comunas.Where(z => !lejanas.Contains(z.Nombre)), z =>
        {
            Assert.InRange(z.CentroLatitud, -56.2, -17.4);
            Assert.InRange(z.CentroLongitud, -76.6, -65.5);
        });
        // Las ordenadas de norte a sur: el id de Arica es menor que el de Punta Arenas.
        Assert.True(comunas.Single(z => z.Nombre == "Arica").Id < comunas.Single(z => z.Nombre == "Punta Arenas").Id);
    }

    // ------------------------------------------------------- La jerarquia comuna / barrio

    private async Task<List<int>> RelacionadasAsync(int zonaId) =>
        await _prueba.Ctx.Database.SqlQuery<int>($"SELECT id AS \"Value\" FROM zonas_relacionadas({zonaId}) ORDER BY id").ToListAsync();

    [Fact]
    public async Task Una_comuna_se_relaciona_con_ella_misma_y_con_sus_barrios()
    {
        var r = await RelacionadasAsync(ZonasDeChile.IdTemuco);

        Assert.Equal(17, r.Count);
        Assert.Contains(ZonasDeChile.IdTemuco, r);
        Assert.Contains(1, r);       // Centro
        Assert.Contains(14, r);      // Labranza
    }

    [Fact]
    public async Task Un_barrio_se_relaciona_con_el_mismo_y_con_su_comuna_pero_no_con_sus_vecinos()
    {
        var r = await RelacionadasAsync(1);   // Centro

        Assert.Equal(new[] { 1, ZonasDeChile.IdTemuco }.OrderBy(x => x), r);
        Assert.DoesNotContain(14, r);         // Labranza es otro barrio
    }

    [Fact]
    public async Task Una_comuna_sin_barrios_solo_se_relaciona_con_ella_misma_y_una_zona_que_no_existe_con_nadie()
    {
        var arica = (await _repo.ZonasAsync()).Single(z => z.Nombre == "Arica");

        Assert.Equal(new[] { arica.Id }, await RelacionadasAsync(arica.Id));
        Assert.Empty(await RelacionadasAsync(99_999));
    }

    [Fact]
    public async Task La_base_rechaza_un_barrio_sin_comuna_y_una_comuna_con_padre()
    {
        _prueba.Ctx.Zonas.Add(new Zona { Id = 9001, Nombre = "Huérfano", Sector = "x", Comuna = "Temuco", Region = "La Araucanía", Tipo = TipoDeZona.Barrio, PadreId = null, CentroLatitud = -38.7, CentroLongitud = -72.6 });
        var e1 = await Assert.ThrowsAsync<DbUpdateException>(() => _prueba.Ctx.SaveChangesAsync());
        Assert.Equal(PostgresErrorCodes.CheckViolation, ((PostgresException)e1.InnerException!).SqlState);
        _prueba.Ctx.ChangeTracker.Clear();

        _prueba.Ctx.Zonas.Add(new Zona { Id = 9002, Nombre = "Comuna con padre", Sector = "x", Comuna = "Comuna con padre", Region = "La Araucanía", Tipo = TipoDeZona.Comuna, PadreId = ZonasDeChile.IdTemuco, CentroLatitud = -38.7, CentroLongitud = -72.6 });
        var e2 = await Assert.ThrowsAsync<DbUpdateException>(() => _prueba.Ctx.SaveChangesAsync());
        Assert.Equal(PostgresErrorCodes.CheckViolation, ((PostgresException)e2.InnerException!).SqlState);
    }

    [Fact]
    public async Task La_etiqueta_de_la_base_y_la_de_C_sharp_dicen_lo_mismo()
    {
        var zonas = await _repo.ZonasAsync();
        foreach (var z in zonas.Where(z => z.Id is 1 or 14 or 377).Concat(zonas.Take(3)))
        {
            var enSql = await _prueba.Ctx.Database
                .SqlQuery<string>($"SELECT etiqueta_de_zona({z.Nombre}, {z.Comuna}, {z.Tipo.ToString()}) AS \"Value\"").SingleAsync();

            Assert.Equal(z.Etiqueta(), enSql);
        }
    }

    [Fact]
    public async Task El_filtro_por_sector_funciona_contra_la_base()
    {
        var poniente = await _repo.ZonasAsync("poniente");

        Assert.NotEmpty(poniente);
        Assert.All(poniente, z => Assert.Equal("poniente", z.Sector));
    }

    /// <summary>Igual que en API-02: el texto del cliente no puede ser un patron.</summary>
    [Fact]
    public async Task El_sector_no_se_interpreta_como_patron()
    {
        var conComodin = await _repo.ZonasAsync("%");

        Assert.Empty(conComodin);
    }

    // ------------------------------------------------------- Lo que impone la base

    /// <summary>
    /// `zonas.id` es GENERATED BY DEFAULT AS IDENTITY y la siembra entro con
    /// identificadores explicitos, que no mueven la secuencia. El administrador (A-06)
    /// gestiona las zonas, asi que va a crear una: sin el reinicio pediria el id 1.
    /// </summary>
    [Fact]
    public async Task Una_zona_nueva_no_choca_con_las_sembradas()
    {
        var zona = new Zona
        {
            Nombre = $"Zona nueva {Guid.NewGuid():N}",
            Sector = "norte",
            CentroLatitud = -38.74,
            CentroLongitud = -72.59,
            Comuna = "Temuco",
        };
        _prueba.Ctx.Zonas.Add(zona);

        await _prueba.Ctx.SaveChangesAsync();

        Assert.True(zona.Id >= 100, $"el identificador fue {zona.Id}");
    }

    [Fact]
    public async Task La_base_rechaza_dos_zonas_con_el_mismo_nombre_en_la_comuna()
    {
        var centro = await _prueba.Ctx.Zonas.FirstAsync(z => z.Nombre == "Centro");

        _prueba.Ctx.Zonas.Add(new Zona
        {
            Nombre = centro.Nombre,
            Sector = "norte",
            CentroLatitud = -38.74,
            CentroLongitud = -72.59,
            Comuna = centro.Comuna,
        });

        var error = await Assert.ThrowsAsync<DbUpdateException>(
            () => _prueba.Ctx.SaveChangesAsync());
        Assert.Equal("23505", (error.InnerException as PostgresException)?.SqlState);
    }

    /// <summary>
    /// R-2 · ux_cobertura_principal. El servicio ya lo evita, pero dos peticiones a la
    /// vez pasarian las dos su comprobacion: quien tiene que rechazar es la base.
    /// </summary>
    [Fact]
    public async Task La_base_rechaza_dos_zonas_principales_del_mismo_prestador()
    {
        var perfil = await UnPrestadorAsync();

        _prueba.Ctx.Coberturas.Add(new CoberturaPrestador
        {
            PerfilPrestadorId = perfil.Id, ZonaId = 1, EsPrincipal = true,
        });
        _prueba.Ctx.Coberturas.Add(new CoberturaPrestador
        {
            PerfilPrestadorId = perfil.Id, ZonaId = 2, EsPrincipal = true,
        });

        var error = await Assert.ThrowsAsync<DbUpdateException>(
            () => _prueba.Ctx.SaveChangesAsync());
        Assert.Equal("23505", (error.InnerException as PostgresException)?.SqlState);
    }

    /// <summary>
    /// Dos prestadores SI pueden tener su principal en la misma zona: el unico es por
    /// prestador, no por zona. Si fuera por zona, el segundo corredor del Centro no
    /// podria publicar.
    /// </summary>
    [Fact]
    public async Task Dos_prestadores_pueden_tener_la_misma_zona_principal()
    {
        var uno = await UnPrestadorAsync();
        var otro = await UnPrestadorAsync();

        Assert.True((await _servicio.ReemplazarCoberturaAsync(
            Cmd(uno.Id, new ZonaCubiertaCmd(1, true)))).Exito);
        var r = await _servicio.ReemplazarCoberturaAsync(
            Cmd(otro.Id, new ZonaCubiertaCmd(1, true)));

        Assert.True(r.Exito, r.Mensaje);
    }

    // ------------------------------------------- el cambio de zona principal, de verdad

    /// <summary>
    /// La prueba que justifica los dos guardados de ReemplazarCobertura.
    ///
    /// ux_cobertura_principal es un unico parcial sobre (perfil_prestador_id) WHERE
    /// es_principal, y PostgreSQL lo comprueba despues de CADA sentencia. Si el servicio
    /// marcara la nueva principal antes de desmarcar la vieja, esto fallaria con un
    /// 23505 segun el orden en que EF Core decidiera emitir los UPDATE. En memoria pasa
    /// siempre; aqui es donde se sabe.
    /// </summary>
    [Fact]
    public async Task Cambiar_la_zona_principal_no_rompe_el_unico_de_la_base()
    {
        var perfil = await UnPrestadorAsync();
        await _servicio.ReemplazarCoberturaAsync(
            Cmd(perfil.Id, new ZonaCubiertaCmd(1, true), new ZonaCubiertaCmd(11, false)));

        var r = await _servicio.ReemplazarCoberturaAsync(
            Cmd(perfil.Id, new ZonaCubiertaCmd(1, false), new ZonaCubiertaCmd(11, true)));

        Assert.True(r.Exito, r.Mensaje);
        var cobertura = await _repo.CoberturaDePrestadorAsync(perfil.Id);
        Assert.Single(cobertura.Where(c => c.EsPrincipal));
        Assert.Equal(11, cobertura.Single(c => c.EsPrincipal).ZonaId);
    }

    /// <summary>
    /// El otro camino del mismo problema: la zona principal sale de la lista y entra una
    /// nueva. Aqui hay un DELETE y un UPDATE sobre el mismo indice.
    /// </summary>
    [Fact]
    public async Task Quitar_la_zona_principal_y_poner_otra_tampoco_rompe_el_unico()
    {
        var perfil = await UnPrestadorAsync();
        await _servicio.ReemplazarCoberturaAsync(
            Cmd(perfil.Id, new ZonaCubiertaCmd(1, true), new ZonaCubiertaCmd(11, false)));

        var r = await _servicio.ReemplazarCoberturaAsync(
            Cmd(perfil.Id, new ZonaCubiertaCmd(14, true)));

        Assert.True(r.Exito, r.Mensaje);
        var cobertura = await _repo.CoberturaDePrestadorAsync(perfil.Id);
        Assert.Single(cobertura);
        Assert.Equal(14, cobertura.Single().ZonaId);
        Assert.True(cobertura.Single().EsPrincipal);
    }

    // ------------------------------------------------- CS-10 con los datos de verdad

    [Fact]
    public async Task La_plaza_de_Temuco_resuelve_al_Centro()
    {
        var r = await _servicio.ResolverZonaAsync(PlazaDeTemuco);

        Assert.True(r.Exito, r.Mensaje);
        Assert.Equal("Centro", r.Valor!.Nombre);
    }

    /// <summary>
    /// Labranza esta a unos 13 km al poniente. Un punto de alla tiene que resolver a
    /// Labranza y no al Centro: es el caso que prueba que el centroide mas cercano hace
    /// algo util con las zonas reales, y no solo con tres inventadas.
    /// </summary>
    [Fact]
    public async Task Un_punto_en_Labranza_resuelve_a_Labranza()
    {
        var r = await _servicio.ResolverZonaAsync(new Punto(-38.7660, -72.7540));

        Assert.True(r.Exito, r.Mensaje);
        Assert.Equal("Labranza", r.Valor!.Nombre);
    }

    [Fact]
    public async Task Un_punto_en_Santiago_resuelve_a_la_comuna_de_Santiago()
    {
        // La Plaza de Armas. Se resuelve a la comuna cuya capital queda mas cerca: en una metropoli de comunas pegadas eso
        // es una aproximacion, y un punto en el limite puede caer en la vecina. Sirve para ubicar, no para fijar limites.
        var r = await _servicio.ResolverZonaAsync(new Punto(-33.4378, -70.6504));

        Assert.True(r.Exito, r.Mensaje);
        Assert.Equal("Santiago", r.Valor!.Nombre);
        Assert.Equal("Metropolitana de Santiago", r.Valor.Region);
        Assert.Equal("Comuna", r.Valor.Tipo);
    }

    /// <summary>
    /// Padre Las Casas esta pegada a Temuco, a pocos km de uno de sus barrios. Un punto ahi es de Padre Las Casas y no
    /// «Temuco»: se resuelve primero la comuna y solo despues, dentro de ella, su barrio.
    /// </summary>
    [Fact]
    public async Task Un_punto_en_Padre_Las_Casas_no_se_resuelve_a_un_barrio_de_Temuco()
    {
        var r = await _servicio.ResolverZonaAsync(new Punto(-38.7667, -72.6));

        Assert.True(r.Exito, r.Mensaje);
        Assert.Equal("Padre Las Casas", r.Valor!.Nombre);
        Assert.Equal("Comuna", r.Valor.Tipo);
    }

    [Fact]
    public async Task Un_punto_fuera_de_Chile_no_resuelve_a_ninguna_zona()
    {
        var r = await _servicio.ResolverZonaAsync(new Punto(40.4168, -3.7038));   // Madrid

        Assert.False(r.Exito);
        Assert.Equal(Fallas.NoEncontrado, r.Codigo);
    }

    /// <summary>
    /// Mientras N-08 no exista, geocodificar tiene que fallar con el mensaje que lleva al
    /// flujo 4B de CU-03. Esta prueba se cae el dia que haya proveedor, y esta bien.
    /// </summary>
    [Fact]
    public async Task Geocodificar_todavia_no_esta_disponible_y_lo_dice_asi()
    {
        var r = await _servicio.ZonaDeDireccionAsync("Arturo Prat 123, Temuco");

        Assert.False(r.Exito);
        Assert.Contains("zona", r.Mensaje!, StringComparison.OrdinalIgnoreCase);
    }

    // ------------------------------------------------- CU-10 contra la base

    [Fact]
    public async Task La_cobertura_se_guarda_y_se_lee_de_vuelta()
    {
        var perfil = await UnPrestadorAsync();

        var r = await _servicio.ReemplazarCoberturaAsync(Cmd(perfil.Id,
            new ZonaCubiertaCmd(1, true), new ZonaCubiertaCmd(11, false),
            new ZonaCubiertaCmd(14, false)));

        Assert.True(r.Exito, r.Mensaje);
        var cobertura = await _servicio.ZonasDeCoberturaAsync(perfil.Id);
        Assert.Equal(3, cobertura.Count);
        Assert.True(cobertura[0].EsPrincipal);
        Assert.Equal("Centro, Temuco", cobertura[0].Nombre);
    }

    [Fact]
    public async Task Una_cobertura_que_falla_no_deja_filas()
    {
        var perfil = await UnPrestadorAsync();

        var r = await _servicio.ReemplazarCoberturaAsync(
            Cmd(perfil.Id, new ZonaCubiertaCmd(1, true), new ZonaCubiertaCmd(9999, false)));

        Assert.False(r.Exito);
        Assert.Empty(await _repo.CoberturaDePrestadorAsync(perfil.Id));
    }
}
