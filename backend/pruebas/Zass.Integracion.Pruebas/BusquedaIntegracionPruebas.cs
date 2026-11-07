using Microsoft.EntityFrameworkCore;
using Zass.Aplicacion.Busqueda;
using Zass.Aplicacion.Catalogo;
using Zass.Aplicacion.Geografia;
using Zass.Aplicacion.Puertos;
using Zass.Aplicacion.Suscripciones;
using Zass.Dominio.Catalogo;
using Zass.Dominio.Comun;
using Zass.Dominio.Credenciales;
using Zass.Dominio.Identidad;
using Zass.Dominio.Suscripciones;
using Zass.Infraestructura.Datos.Repositorios;
using Zass.Infraestructura.Seguridad;

namespace Zass.Integracion.Pruebas;

/// <summary>
/// MOD-05 contra PostgreSQL de verdad. Es donde se prueba la consulta de CU-08, porque la
/// consulta ES el modulo: el servicio solo la rodea.
///
/// Las pruebas de aplicacion usan un doble tonto a proposito. Aqui no hay imitacion de
/// nada: los cuatro filtros duros, el orden, el cursor y el centroide de respaldo los
/// resuelve el motor.
/// </summary>
public class BusquedaIntegracionPruebas : IDisposable
{
    private readonly ContextoDePrueba _prueba = new();
    private readonly RepositorioBusquedaEfCore _repo;
    private readonly ServicioSuscripciones _suscripciones;
    private readonly ServicioBusqueda _servicio;
    private readonly RelojUtc _reloj = new();

    private const short Corretaje = 1;
    private const short Derecho = 2;
    private const int Centro = 1;
    private const int Labranza = 14;

    // Plaza Anibal Pinto. El centroide sembrado del Centro esta a unos 450 m de aqui.
    private static readonly Punto PlazaDeTemuco = new(-38.7359, -72.5904);

    public BusquedaIntegracionPruebas()
    {
        var ctx = _prueba.Ctx;
        var trabajo = new UnidadDeTrabajoEfCore(ctx);
        var auditor = new AuditorEfCore(ctx);

        _repo = new RepositorioBusquedaEfCore(ctx);
        _suscripciones = new ServicioSuscripciones(
            new RepositorioSuscripcionesEfCore(ctx), trabajo, _reloj, auditor);

        _servicio = new ServicioBusqueda(
            _repo,
            new ServicioCatalogo(new RepositorioCatalogoEfCore(ctx), trabajo, _reloj, auditor),
            new ServicioGeografia(
                new RepositorioGeografiaEfCore(ctx), new GeocodificadorNoDisponible(),
                trabajo, _reloj, auditor),
            _reloj);
    }

    public void Dispose() => _prueba.Dispose();

    /// <summary>
    /// Un prestador publicado y buscable: cuenta, perfil, suscripcion vigente, cobertura
    /// con zona principal y un servicio activo. Es el minimo para aparecer, y escribirlo
    /// una vez evita que cada prueba invente su propia version a medias.
    /// </summary>
    private async Task<PerfilPrestador> UnPrestadorBuscableAsync(
        short rubroId = Corretaje,
        int zonaId = Centro,
        int servicioId = 101,
        EstadoPerfil estado = EstadoPerfil.Publicado,
        EstadoSuscripcion estadoSuscripcion = EstadoSuscripcion.Prueba,
        int diasDeVigencia = 30,
        bool conCredencialVerificada = false,
        decimal? promedio = null,
        int resenas = 0,
        double? latitud = null,
        double? longitud = null,
        string? nombre = null)
    {
        var ctx = _prueba.Ctx;

        var usuario = new Usuario
        {
            Correo = $"busq-{Guid.NewGuid():N}@zass.local",
            HashContrasena = "x",
            Nombre = "Prestador de prueba",
            CorreoVerificado = true,
        };
        ctx.Usuarios.Add(usuario);

        var perfil = new PerfilPrestador
        {
            UsuarioId = usuario.Id,
            RubroId = rubroId,
            NombrePublico = nombre ?? $"Estudio {Guid.NewGuid():N}"[..20],
            Slug = $"estudio-{Guid.NewGuid():N}",
            Descripcion = "Corretaje en Temuco",
            Estado = estado,
            PublicadoEn = estado == EstadoPerfil.Publicado ? _reloj.Ahora : null,
            PromedioCalificacion = promedio,
            TotalResenas = resenas,
            Latitud = latitud,
            Longitud = longitud,
        };
        ctx.PerfilesPrestador.Add(perfil);

        ctx.Suscripciones.Add(new Suscripcion
        {
            PerfilPrestadorId = perfil.Id,
            PlanId = 1,
            Estado = estadoSuscripcion,
            IniciaEn = _reloj.Ahora.AddDays(-1),
            VigenteHasta = _reloj.Ahora.AddDays(diasDeVigencia),
        });

        ctx.Coberturas.Add(new CoberturaPrestador
        {
            PerfilPrestadorId = perfil.Id,
            ZonaId = zonaId,
            EsPrincipal = true,
            DesdeEn = _reloj.Ahora,
        });

        ctx.PrestadorServicios.Add(new PrestadorServicio
        {
            PerfilPrestadorId = perfil.Id,
            ServicioId = servicioId,
            DuracionMinutos = 60,
            ModoAgenda = ModoAgenda.Automatico,
            Activo = true,
        });

        if (conCredencialVerificada)
            ctx.Credenciales.Add(new Credencial
            {
                PerfilPrestadorId = perfil.Id,
                Tipo = TipoCredencial.Titulo,
                Nombre = "Titulo profesional",
                ObtenidaEn = new DateOnly(2020, 1, 15),
                Estado = EstadoCredencial.Verificada,
                SelloEn = _reloj.Ahora,
                SelloPor = Guid.NewGuid(),      // ck_credencial_sello lo exige
            });

        await ctx.SaveChangesAsync();
        return perfil;
    }

    private static CriteriosDeConsulta Consulta(
        DateTime ahora, int zonaId = Centro, short rubroId = Corretaje,
        int? servicioId = null, bool soloVerificados = false,
        decimal? calificacionMinima = null, int limite = 21) =>
        new(rubroId, zonaId, servicioId, soloVerificados, calificacionMinima, ahora, limite);

    private Task<IReadOnlyList<FilaDeBusqueda>> BuscarDesdeLaPlazaAsync(
        CriteriosDeConsulta? consulta = null, int radioMetros = 50_000,
        double? desdeMetros = null, Guid? desdeId = null) =>
        _repo.PorDistanciaAsync(
            consulta ?? Consulta(_reloj.Ahora),
            PlazaDeTemuco.Latitud, PlazaDeTemuco.Longitud, radioMetros,
            desdeMetros, desdeId);

    // ------------------------------------------------- RN-S3-09, las dos puntas

    /// <summary>
    /// La prueba que vigila la duplicacion aceptada a sabiendas.
    ///
    /// La condicion de vigencia vive dos veces: en C# dentro de MOD-13 y en el SQL de la
    /// busqueda. No se puede evitar sin renunciar al filtro en la base, que es lo que
    /// mantiene sana la paginacion. Lo que SI se puede es comprobar que las dos puntas
    /// dicen lo mismo, caso por caso. El dia que alguien cambie una y no la otra, esto
    /// se cae.
    /// </summary>
    [Theory]
    [InlineData(EstadoSuscripcion.Prueba, 30, true)]
    [InlineData(EstadoSuscripcion.Activa, 30, true)]
    [InlineData(EstadoSuscripcion.Prueba, -1, false)]
    [InlineData(EstadoSuscripcion.Activa, -1, false)]
    [InlineData(EstadoSuscripcion.Vencida, 30, false)]
    [InlineData(EstadoSuscripcion.Cancelada, 30, false)]
    public async Task El_SQL_y_MOD_13_dicen_lo_mismo_sobre_quien_esta_vigente(
        EstadoSuscripcion estado, int dias, bool deberiaAparecer)
    {
        var perfil = await UnPrestadorBuscableAsync(
            estadoSuscripcion: estado, diasDeVigencia: dias);

        var enLaBusqueda = (await BuscarDesdeLaPlazaAsync()).Any(f => f.Id == perfil.Id);
        var segunMod13 = await _suscripciones.EstaVigenteAsync(perfil.Id);

        Assert.Equal(deberiaAparecer, enLaBusqueda);
        Assert.Equal(enLaBusqueda, segunMod13);
    }

    /// <summary>
    /// RN-S3-09 dicho del todo: el impago DESAPARECE, no baja de posicion. Un prestador
    /// con suscripcion vencida no sale ni siendo el unico de su rubro.
    /// </summary>
    [Fact]
    public async Task El_prestador_sin_suscripcion_vigente_desaparece_aunque_sea_el_unico()
    {
        await UnPrestadorBuscableAsync(diasDeVigencia: -1);

        var filas = await BuscarDesdeLaPlazaAsync();

        Assert.Empty(filas);
    }

    /// <summary>
    /// Y manana deja de aparecer sin que nadie corra ningun trabajo de fondo: el filtro
    /// compara con la hora que se le pasa, no con un estado que alguien tenga que marcar.
    /// </summary>
    [Fact]
    public async Task Al_vencer_el_plazo_el_prestador_deja_de_aparecer_solo()
    {
        var perfil = await UnPrestadorBuscableAsync(diasDeVigencia: 2);

        var hoy = await BuscarDesdeLaPlazaAsync(Consulta(_reloj.Ahora));
        var enTresDias = await BuscarDesdeLaPlazaAsync(Consulta(_reloj.Ahora.AddDays(3)));

        Assert.Contains(hoy, f => f.Id == perfil.Id);
        Assert.DoesNotContain(enTresDias, f => f.Id == perfil.Id);
    }

    // ------------------------------------------------- los otros filtros duros

    [Theory]
    [InlineData(EstadoPerfil.Borrador)]
    [InlineData(EstadoPerfil.Despublicado)]
    public async Task Solo_aparecen_los_perfiles_publicados(EstadoPerfil estado)
    {
        await UnPrestadorBuscableAsync(estado: estado);

        Assert.Empty(await BuscarDesdeLaPlazaAsync());
    }

    [Fact]
    public async Task Un_prestador_de_otro_rubro_no_aparece()
    {
        await UnPrestadorBuscableAsync(rubroId: Derecho, servicioId: 201);

        Assert.Empty(await BuscarDesdeLaPlazaAsync());
    }

    /// <summary>
    /// El paso 5 de CU-08 pide cobertura de la zona, no solo cercania: quien atiende solo
    /// en Labranza no puede salirle a alguien que busca en el Centro, aunque Labranza
    /// este dentro de los 50 km.
    /// </summary>
    [Fact]
    public async Task Quien_no_cubre_la_zona_buscada_no_aparece_aunque_este_cerca()
    {
        await UnPrestadorBuscableAsync(zonaId: Labranza);

        Assert.Empty(await BuscarDesdeLaPlazaAsync());
    }

    [Fact]
    public async Task Quien_cubre_la_zona_sin_tenerla_como_principal_si_aparece()
    {
        var perfil = await UnPrestadorBuscableAsync(zonaId: Labranza);
        _prueba.Ctx.Coberturas.Add(new CoberturaPrestador
        {
            PerfilPrestadorId = perfil.Id, ZonaId = Centro,
            EsPrincipal = false, DesdeEn = _reloj.Ahora,
        });
        await _prueba.Ctx.SaveChangesAsync();

        var filas = await BuscarDesdeLaPlazaAsync();

        var encontrado = Assert.Single(filas);
        Assert.Equal(perfil.Id, encontrado.Id);
        // La ficha muestra su zona PRINCIPAL, que es otra: Labranza.
        Assert.Equal("Labranza", encontrado.ZonaPrincipal);
    }

    [Fact]
    public async Task El_filtro_por_servicio_deja_fuera_a_quien_no_lo_ofrece()
    {
        var conTasacion = await UnPrestadorBuscableAsync(servicioId: 101);
        await UnPrestadorBuscableAsync(servicioId: 102);

        var filas = await BuscarDesdeLaPlazaAsync(Consulta(_reloj.Ahora, servicioId: 101));

        Assert.Equal(conTasacion.Id, Assert.Single(filas).Id);
    }

    [Fact]
    public async Task Un_servicio_desactivado_no_cuenta_para_el_filtro()
    {
        var perfil = await UnPrestadorBuscableAsync(servicioId: 101);
        var oferta = await _prueba.Ctx.PrestadorServicios
            .SingleAsync(o => o.PerfilPrestadorId == perfil.Id);
        oferta.Activo = false;
        await _prueba.Ctx.SaveChangesAsync();

        var filas = await BuscarDesdeLaPlazaAsync(Consulta(_reloj.Ahora, servicioId: 101));

        Assert.Empty(filas);
    }

    // ------------------------------------------------- CS-09, los filtros blandos

    [Fact]
    public async Task Solo_verificados_deja_fuera_a_quien_no_tiene_credencial()
    {
        var conSello = await UnPrestadorBuscableAsync(conCredencialVerificada: true);
        await UnPrestadorBuscableAsync(conCredencialVerificada: false);

        var filas = await BuscarDesdeLaPlazaAsync(Consulta(_reloj.Ahora, soloVerificados: true));

        var encontrado = Assert.Single(filas);
        Assert.Equal(conSello.Id, encontrado.Id);
        Assert.True(encontrado.TieneVerificada);
    }

    /// <summary>
    /// Una credencial que no esta verificada —en revision, rechazada— no da sello. Si
    /// diera, subir un papel cualquiera bastaria para aparecer como verificado.
    /// </summary>
    [Fact]
    public async Task Una_credencial_en_revision_no_da_sello()
    {
        var perfil = await UnPrestadorBuscableAsync();
        _prueba.Ctx.Credenciales.Add(new Credencial
        {
            PerfilPrestadorId = perfil.Id,
            Tipo = TipoCredencial.Titulo,
            Nombre = "Titulo en revision",
            ObtenidaEn = new DateOnly(2020, 1, 15),
            Estado = EstadoCredencial.EnRevision,
        });
        await _prueba.Ctx.SaveChangesAsync();

        var filas = await BuscarDesdeLaPlazaAsync();

        Assert.False(Assert.Single(filas).TieneVerificada);
        Assert.Empty(await BuscarDesdeLaPlazaAsync(
            Consulta(_reloj.Ahora, soloVerificados: true)));
    }

    /// <summary>
    /// RN-S5-11 tambien manda en el filtro: el promedio solo es publico con 3 resenas o
    /// mas, asi que filtrar por el fuera de ese caso ocultaria perfiles por un numero que
    /// nadie puede ver.
    /// </summary>
    [Fact]
    public async Task El_filtro_por_calificacion_solo_mira_a_quien_ya_tiene_tres_resenas()
    {
        var conTres = await UnPrestadorBuscableAsync(promedio: 4.6m, resenas: 3);
        await UnPrestadorBuscableAsync(promedio: 5.0m, resenas: 2);   // promedio no publico

        var filas = await BuscarDesdeLaPlazaAsync(
            Consulta(_reloj.Ahora, calificacionMinima: 4m));

        Assert.Equal(conTres.Id, Assert.Single(filas).Id);
    }

    // ------------------------------------------------- el orden y la distancia

    /// <summary>
    /// Hoy ningun perfil tiene punto exacto —el geocodificador no existe—, asi que la
    /// distancia se mide al CENTRO de su zona principal (flujo 4B.4 de CU-03). El
    /// centroide sembrado del Centro esta a unos 450 m de la Plaza Anibal Pinto.
    /// </summary>
    [Fact]
    public async Task Sin_punto_exacto_la_distancia_se_mide_al_centro_de_la_zona()
    {
        var perfil = await UnPrestadorBuscableAsync();

        var fila = Assert.Single(await BuscarDesdeLaPlazaAsync());

        Assert.Equal(perfil.Id, fila.Id);
        Assert.NotNull(fila.Metros);
        Assert.InRange(fila.Metros!.Value, 100, 1_500);
    }

    /// <summary>
    /// Y cuando SI hay punto exacto, manda el punto y no el centroide. Es la mitad que
    /// queda lista para el dia que exista N-08.
    /// </summary>
    [Fact]
    public async Task Con_punto_exacto_la_distancia_se_mide_al_punto()
    {
        // A dos cuadras de la plaza, pero con la zona principal igual de siempre.
        await UnPrestadorBuscableAsync(latitud: -38.7362, longitud: -72.5907);

        var fila = Assert.Single(await BuscarDesdeLaPlazaAsync());

        Assert.NotNull(fila.Metros);
        Assert.InRange(fila.Metros!.Value, 0, 150);
    }

    [Fact]
    public async Task Los_resultados_vienen_ordenados_del_mas_cercano_al_mas_lejano()
    {
        var lejos = await UnPrestadorBuscableAsync(latitud: -38.7500, longitud: -72.6100);
        var cerca = await UnPrestadorBuscableAsync(latitud: -38.7360, longitud: -72.5905);

        var filas = await BuscarDesdeLaPlazaAsync();

        Assert.Equal(2, filas.Count);
        Assert.Equal(cerca.Id, filas[0].Id);
        Assert.Equal(lejos.Id, filas[1].Id);
        Assert.True(filas[0].Metros < filas[1].Metros);
    }

    [Fact]
    public async Task El_radio_deja_fuera_lo_que_esta_mas_lejos()
    {
        // En el Centro como zona, pero con un punto exacto a unos 4 km.
        await UnPrestadorBuscableAsync(latitud: -38.7700, longitud: -72.5904);

        var dentro = await BuscarDesdeLaPlazaAsync(radioMetros: 10_000);
        var fuera = await BuscarDesdeLaPlazaAsync(radioMetros: 1_000);

        Assert.Single(dentro);
        Assert.Empty(fuera);
    }

    // ------------------------------------------------- el cursor, contra la base

    /// <summary>
    /// La paginacion por cursor, de verdad: con limite 2 y tres prestadores a distintas
    /// distancias, la segunda pagina tiene que empezar exactamente donde termino la
    /// primera, sin repetir ni saltarse a nadie.
    /// </summary>
    [Fact]
    public async Task El_cursor_pagina_sin_repetir_ni_saltarse_a_nadie()
    {
        var a = await UnPrestadorBuscableAsync(latitud: -38.7360, longitud: -72.5905);
        var b = await UnPrestadorBuscableAsync(latitud: -38.7380, longitud: -72.5905);
        var c = await UnPrestadorBuscableAsync(latitud: -38.7420, longitud: -72.5905);

        var primera = await BuscarDesdeLaPlazaAsync(Consulta(_reloj.Ahora, limite: 2));
        var segunda = await BuscarDesdeLaPlazaAsync(
            Consulta(_reloj.Ahora, limite: 2),
            desdeMetros: primera[^1].Metros, desdeId: primera[^1].Id);

        Assert.Equal(new[] { a.Id, b.Id }, primera.Select(f => f.Id).ToArray());
        Assert.Equal(c.Id, Assert.Single(segunda).Id);
    }

    /// <summary>
    /// El caso que rompe un cursor hecho solo con la distancia: dos prestadores a la
    /// MISMA distancia. Sin el desempate por identificador, uno de los dos se repetiria
    /// en las dos paginas y el otro no aparecerian nunca.
    /// </summary>
    [Fact]
    public async Task Dos_prestadores_a_la_misma_distancia_no_se_pisan_en_el_cursor()
    {
        var uno = await UnPrestadorBuscableAsync(latitud: -38.7370, longitud: -72.5905);
        var otro = await UnPrestadorBuscableAsync(latitud: -38.7370, longitud: -72.5905);

        var primera = await BuscarDesdeLaPlazaAsync(Consulta(_reloj.Ahora, limite: 1));
        var segunda = await BuscarDesdeLaPlazaAsync(
            Consulta(_reloj.Ahora, limite: 1),
            desdeMetros: primera[^1].Metros, desdeId: primera[^1].Id);

        Assert.Single(primera);
        Assert.Single(segunda);
        Assert.NotEqual(primera[0].Id, segunda[0].Id);
        Assert.Equal(
            new[] { uno.Id, otro.Id }.OrderBy(x => x).ToArray(),
            new[] { primera[0].Id, segunda[0].Id }.OrderBy(x => x).ToArray());
    }

    // ------------------------------------------------- la busqueda por zona

    [Fact]
    public async Task Por_zona_se_ordena_por_reputacion_y_no_lleva_distancia()
    {
        var mejor = await UnPrestadorBuscableAsync(promedio: 4.8m, resenas: 10);
        var peor = await UnPrestadorBuscableAsync(promedio: 3.2m, resenas: 10);
        var sinResenas = await UnPrestadorBuscableAsync();

        var filas = await _repo.PorZonaAsync(Consulta(_reloj.Ahora), null, null, null);

        Assert.Equal(
            new[] { mejor.Id, peor.Id, sinResenas.Id },
            filas.Select(f => f.Id).ToArray());
        Assert.All(filas, f => Assert.Null(f.Metros));
    }

    [Fact]
    public async Task Por_zona_tambien_se_aplican_los_filtros_duros()
    {
        await UnPrestadorBuscableAsync(diasDeVigencia: -1);
        await UnPrestadorBuscableAsync(estado: EstadoPerfil.Borrador);
        await UnPrestadorBuscableAsync(zonaId: Labranza);
        var buscable = await UnPrestadorBuscableAsync();

        var filas = await _repo.PorZonaAsync(Consulta(_reloj.Ahora), null, null, null);

        Assert.Equal(buscable.Id, Assert.Single(filas).Id);
    }

    // ------------------------------------------------- el camino completo del servicio

    /// <summary>
    /// Y ahora la frase entera: publicar, prueba, y VISIBLE. Esto es lo que el producto
    /// promete, recorrido por el servicio y contra la base.
    /// </summary>
    [Fact]
    public async Task Un_prestador_publicado_con_prueba_vigente_aparece_en_la_busqueda()
    {
        var perfil = await UnPrestadorBuscableAsync(nombre: "Corretaje Araucania");

        var r = await _servicio.BuscarAsync(new CriteriosDeBusqueda(
            Corretaje, Latitud: PlazaDeTemuco.Latitud, Longitud: PlazaDeTemuco.Longitud));

        Assert.True(r.Exito, r.Mensaje);
        var encontrado = Assert.Single(r.Valor!.Resultados);
        Assert.Equal(perfil.Id, encontrado.PerfilId);
        Assert.Equal("Centro", r.Valor!.ZonaBuscada);
        Assert.True(r.Valor!.OrdenadoPorDistancia);
        // RN-S3-03: lo que viaja es texto redondeado.
        Assert.Equal("a menos de 1 km", encontrado.Distancia);
    }

    [Fact]
    public async Task Buscar_por_zona_sin_punto_tambien_encuentra_y_avisa()
    {
        await UnPrestadorBuscableAsync();

        var r = await _servicio.BuscarAsync(new CriteriosDeBusqueda(Corretaje, ZonaId: Centro));

        Assert.True(r.Exito, r.Mensaje);
        Assert.Single(r.Valor!.Resultados);
        Assert.False(r.Valor!.OrdenadoPorDistancia);
        Assert.Contains("distancia", r.Valor!.Aviso!, StringComparison.OrdinalIgnoreCase);
    }
}
