using Microsoft.EntityFrameworkCore;
using Zass.Aplicacion.Busqueda;
using Zass.Aplicacion.Catalogo;
using Zass.Aplicacion.Credenciales;
using Zass.Aplicacion.Geografia;
using Zass.Aplicacion.Identidad;
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
            new ServicioIdentidad(new RepositorioUsuariosEfCore(ctx), new RepositorioPerfilesEfCore(ctx)),
            new ServicioCredenciales(new RepositorioCredencialesEfCore(ctx), trabajo, _reloj, auditor),
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
        PosicionDeBusqueda? desde = null) =>
        _repo.PorDistanciaAsync(
            consulta ?? Consulta(_reloj.Ahora),
            PlazaDeTemuco.Latitud, PlazaDeTemuco.Longitud, radioMetros, desde);

    /// <summary>La posicion que el servicio pondria en el cursor despues de servir esta fila.</summary>
    private static PosicionDeBusqueda Posicion(FilaDeBusqueda f) =>
        new(f.Tramo, f.TieneVerificada, f.ReputacionOrden, f.TotalResenas, f.Id);

    /// <summary>
    /// Un punto al norte de la plaza, a esos kilometros. Un grado de latitud son unos
    /// 111,2 km, asi que la cuenta es directa y las pruebas pueden decir "este esta a 2,6
    /// km" sin inventarse coordenadas.
    /// </summary>
    private static (double Latitud, double Longitud) AlNorteDeLaPlaza(double km) =>
        (PlazaDeTemuco.Latitud + km / 111.195, PlazaDeTemuco.Longitud);

    private async Task<Guid[]> IdsEnOrdenAsync(bool porDistancia)
    {
        var filas = porDistancia
            ? await BuscarDesdeLaPlazaAsync()
            : await _repo.PorZonaAsync(Consulta(_reloj.Ahora), null);
        return filas.Select(f => f.Id).ToArray();
    }

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
        Assert.Equal("Labranza, Temuco", encontrado.ZonaPrincipal);
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
    /// La paginacion por cursor, de verdad: con limite 2 y tres prestadores en tramos
    /// distintos, la segunda pagina tiene que empezar exactamente donde termino la
    /// primera, sin repetir ni saltarse a nadie.
    /// </summary>
    [Fact]
    public async Task El_cursor_pagina_sin_repetir_ni_saltarse_a_nadie()
    {
        var (laA, loA) = AlNorteDeLaPlaza(0.2);    // tramo 0
        var (laB, loB) = AlNorteDeLaPlaza(1.1);    // tramo 1
        var (laC, loC) = AlNorteDeLaPlaza(2.2);    // tramo 2
        var a = await UnPrestadorBuscableAsync(latitud: laA, longitud: loA);
        var b = await UnPrestadorBuscableAsync(latitud: laB, longitud: loB);
        var c = await UnPrestadorBuscableAsync(latitud: laC, longitud: loC);

        var primera = await BuscarDesdeLaPlazaAsync(Consulta(_reloj.Ahora, limite: 2));
        var segunda = await BuscarDesdeLaPlazaAsync(
            Consulta(_reloj.Ahora, limite: 2), desde: Posicion(primera[^1]));

        Assert.Equal(new[] { a.Id, b.Id }, primera.Select(f => f.Id).ToArray());
        Assert.Equal(c.Id, Assert.Single(segunda).Id);
    }

    /// <summary>
    /// El caso que rompe un cursor hecho con menos que la tupla entera: dos prestadores
    /// EMPATADOS en todo. Sin el desempate por identificador, uno se repetiria en las dos
    /// paginas y el otro no apareceria nunca.
    /// </summary>
    [Fact]
    public async Task Dos_prestadores_empatados_en_todo_no_se_pisan_en_el_cursor()
    {
        var (la, lo) = AlNorteDeLaPlaza(0.4);
        var uno = await UnPrestadorBuscableAsync(latitud: la, longitud: lo);
        var otro = await UnPrestadorBuscableAsync(latitud: la, longitud: lo);

        var primera = await BuscarDesdeLaPlazaAsync(Consulta(_reloj.Ahora, limite: 1));
        var segunda = await BuscarDesdeLaPlazaAsync(
            Consulta(_reloj.Ahora, limite: 1), desde: Posicion(primera[^1]));

        Assert.Single(primera);
        Assert.Single(segunda);
        Assert.NotEqual(primera[0].Id, segunda[0].Id);
        Assert.Equal(
            new[] { uno.Id, otro.Id }.OrderBy(x => x).ToArray(),
            new[] { primera[0].Id, segunda[0].Id }.OrderBy(x => x).ToArray());
    }

    /// <summary>
    /// El cursor sobre la tupla ENTERA, con un orden que mezcla verificado, reputacion y
    /// resenas: paginar de a 2 tiene que dar exactamente la misma lista que pedirla
    /// entera, en las dos busquedas. Es la prueba que habria cazado un cursor que
    /// comparara menos columnas que el ORDER BY.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Paginar_de_a_dos_da_la_misma_lista_que_pedirla_entera(bool porDistancia)
    {
        await UnPrestadorBuscableAsync(conCredencialVerificada: true, promedio: 4.1m, resenas: 8);
        await UnPrestadorBuscableAsync(conCredencialVerificada: true, promedio: 4.1m, resenas: 8);
        await UnPrestadorBuscableAsync(conCredencialVerificada: true);
        await UnPrestadorBuscableAsync(promedio: 4.9m, resenas: 30);
        await UnPrestadorBuscableAsync(promedio: 4.9m, resenas: 12);
        await UnPrestadorBuscableAsync(promedio: 3.0m, resenas: 4);
        await UnPrestadorBuscableAsync(promedio: 5.0m, resenas: 2);   // oculto: cuenta como -1
        await UnPrestadorBuscableAsync();

        async Task<IReadOnlyList<FilaDeBusqueda>> Pagina(int limite, PosicionDeBusqueda? desde) =>
            porDistancia
                ? await BuscarDesdeLaPlazaAsync(Consulta(_reloj.Ahora, limite: limite), desde: desde)
                : await _repo.PorZonaAsync(Consulta(_reloj.Ahora, limite: limite), desde);

        var entera = (await Pagina(50, null)).Select(f => f.Id).ToArray();

        var porPaginas = new List<Guid>();
        PosicionDeBusqueda? posicion = null;
        for (var vuelta = 0; vuelta < 10; vuelta++)
        {
            var pagina = await Pagina(2, posicion);
            if (pagina.Count == 0) break;
            porPaginas.AddRange(pagina.Select(f => f.Id));
            posicion = Posicion(pagina[^1]);
        }

        Assert.Equal(8, entera.Length);
        Assert.Equal(entera, porPaginas.ToArray());
        Assert.Equal(porPaginas.Count, porPaginas.Distinct().Count());
    }

    // ------------------------------------------------- el orden (hallazgos 10 y 11)

    /// <summary>
    /// El fallo que se vio con los ojos en un navegador real. "Estudio Nuevo Temuco", con 2
    /// resenas y un 5,0 guardado, salia PRIMERO por encima de uno con 4,6 y 12 resenas,
    /// aunque la pantalla no muestra su promedio (RN-S5-11). Se ordenaba por un numero que
    /// se esconde: el orden lo delataba y a quien miraba le parecia arbitrario.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Un_promedio_que_la_pantalla_esconde_no_adelanta_a_uno_publico(bool porDistancia)
    {
        var oculto = await UnPrestadorBuscableAsync(promedio: 5.0m, resenas: 2);
        var publico = await UnPrestadorBuscableAsync(promedio: 4.6m, resenas: 12);

        var ids = await IdsEnOrdenAsync(porDistancia);

        Assert.Equal(new[] { publico.Id, oculto.Id }, ids);
    }

    /// <summary>
    /// El sello va antes que la reputacion: es la senal de confianza que ZASS! da por si
    /// misma, y un prestador nuevo todavia no tiene reputacion que mostrar. Es una decision
    /// de producto —CU-08 lista "credencial verificada, reputacion y completitud", y el
    /// flujo 4C.4 no fija cual va primero—; si se prefiere al reves, se cambian estas dos
    /// columnas de lugar en el SQL y en esta prueba.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Un_prestador_verificado_va_antes_que_uno_con_mejor_reputacion(bool porDistancia)
    {
        var sinSello = await UnPrestadorBuscableAsync(promedio: 4.9m, resenas: 50);
        var conSello = await UnPrestadorBuscableAsync(conCredencialVerificada: true);

        var ids = await IdsEnOrdenAsync(porDistancia);

        Assert.Equal(new[] { conSello.Id, sinSello.Id }, ids);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task A_igual_reputacion_gana_quien_tiene_mas_resenas(bool porDistancia)
    {
        var cinco = await UnPrestadorBuscableAsync(promedio: 4.0m, resenas: 5);
        var nueve = await UnPrestadorBuscableAsync(promedio: 4.0m, resenas: 9);

        var ids = await IdsEnOrdenAsync(porDistancia);

        Assert.Equal(new[] { nueve.Id, cinco.Id }, ids);
    }

    /// <summary>
    /// Dos prestadores en la misma zona estan a la MISMA distancia exacta: la de su
    /// centroide. Antes el desempate salia por `id`, que es un UUID: orden arbitrario y
    /// distinto en cada base. Ahora es determinista.
    /// </summary>
    [Fact]
    public async Task Los_empatados_en_todo_salen_siempre_en_el_mismo_orden()
    {
        await UnPrestadorBuscableAsync();
        await UnPrestadorBuscableAsync();
        await UnPrestadorBuscableAsync();

        var primera = await IdsEnOrdenAsync(porDistancia: true);
        var segunda = await IdsEnOrdenAsync(porDistancia: true);

        Assert.Equal(primera, segunda);
        Assert.Equal(primera.OrderBy(x => x).ToArray(), primera);   // y por id, ascendente
    }

    /// <summary>
    /// Dentro del MISMO tramo —los dos son "a 3 km"— manda la calidad y no los metros
    /// exactos. Ordenar por la distancia exacta filtra mas de lo que la pantalla muestra:
    /// si dos prestadores de "a 3 km" cambian de orden segun desde donde se busca, delatan
    /// de que lado de una linea estan.
    /// </summary>
    [Fact]
    public async Task Dentro_del_mismo_tramo_manda_la_calidad_y_no_la_distancia_exacta()
    {
        var (laCerca, loCerca) = AlNorteDeLaPlaza(2.61);    // "a 3 km"
        var (laLejos, loLejos) = AlNorteDeLaPlaza(3.45);    // tambien "a 3 km"
        var mas_cerca_sin_sello = await UnPrestadorBuscableAsync(latitud: laCerca, longitud: loCerca);
        var mas_lejos_con_sello = await UnPrestadorBuscableAsync(
            latitud: laLejos, longitud: loLejos, conCredencialVerificada: true);

        var filas = await BuscarDesdeLaPlazaAsync();

        Assert.Equal(3, filas[0].Tramo);
        Assert.Equal(3, filas[1].Tramo);
        Assert.True(filas[0].Metros < filas[1].Metros || filas[0].Id == mas_lejos_con_sello.Id);
        Assert.Equal(
            new[] { mas_lejos_con_sello.Id, mas_cerca_sin_sello.Id },
            filas.Select(f => f.Id).ToArray());
    }

    /// <summary>En OTRO tramo, en cambio, manda la distancia aunque el mas lejano tenga sello.</summary>
    [Fact]
    public async Task En_otro_tramo_manda_la_distancia_aunque_el_lejano_este_verificado()
    {
        var (laCerca, loCerca) = AlNorteDeLaPlaza(0.45);    // "a menos de 1 km"
        var (laLejos, loLejos) = AlNorteDeLaPlaza(3.45);    // "a 3 km"
        var cerca = await UnPrestadorBuscableAsync(latitud: laCerca, longitud: loCerca);
        var lejos = await UnPrestadorBuscableAsync(
            latitud: laLejos, longitud: loLejos, conCredencialVerificada: true);

        var filas = await BuscarDesdeLaPlazaAsync();

        Assert.Equal(new[] { cerca.Id, lejos.Id }, filas.Select(f => f.Id).ToArray());
    }

    /// <summary>
    /// El tramo por el que se ORDENA tiene que ser el que se MUESTRA. Si la base redondeara
    /// distinto que Geo.DistanciaPublica, el orden contradiria lo que dice la pantalla:
    /// "a 3 km" delante de "a 2 km". Aqui se compara fila por fila, sobre distancias que
    /// calculo PostGIS de verdad.
    /// </summary>
    [Fact]
    public async Task El_tramo_de_cada_fila_es_el_que_dice_su_texto_publico()
    {
        foreach (var km in new[] { 0.3, 0.9, 1.2, 1.8, 2.4, 3.3 })
        {
            var (la, lo) = AlNorteDeLaPlaza(km);
            await UnPrestadorBuscableAsync(latitud: la, longitud: lo);
        }

        var filas = await BuscarDesdeLaPlazaAsync();

        Assert.Equal(6, filas.Count);
        foreach (var f in filas)
            Assert.Equal(TramoDelTexto(Geo.DistanciaPublica(f.Metros!.Value)), f.Tramo);
    }

    /// <summary>
    /// La funcion de la base contra la regla del dominio, en los bordes: justo debajo,
    /// justo en y justo encima de cada corte. Incluye los empates (1,5 y 2,5 km), donde
    /// round(double precision) de PostgreSQL redondea al par y Geo.DistanciaPublica se
    /// aleja de cero: con `numeric` coinciden, y esto lo prueba.
    /// </summary>
    [Theory]
    [InlineData(0.0)]
    [InlineData(1.0)]
    [InlineData(999.99)]
    [InlineData(1000.0)]
    [InlineData(1000.01)]
    [InlineData(1499.99)]
    [InlineData(1500.0)]
    [InlineData(1500.01)]
    [InlineData(2499.99)]
    [InlineData(2500.0)]
    [InlineData(3500.0)]
    [InlineData(49_999.0)]
    [InlineData(50_000.0)]
    public async Task La_funcion_de_la_base_redondea_igual_que_el_dominio(double metros)
    {
        var enLaBase = await _prueba.Ctx.Database
            .SqlQuery<int>($"SELECT tramo_de_distancia({metros}::float8) AS \"Value\"")
            .SingleAsync();

        Assert.Equal(TramoDelTexto(Geo.DistanciaPublica(metros)), enLaBase);
    }

    /// <summary>"a menos de 1 km" es el tramo 0; "a N km" es el tramo N.</summary>
    private static int TramoDelTexto(string texto) =>
        texto.Contains("menos de 1 km")
            ? 0
            : int.Parse(System.Text.RegularExpressions.Regex.Match(texto, @"\d+").Value);

    // ------------------------------------------------- API-63, la barra de busqueda

    private const double CentroLat = -38.7359, CentroLon = -72.5904;
    private const double LabranzaLat = -38.7665376, LabranzaLon = -72.7534177;

    private async Task<IReadOnlyList<FilaDeTexto>> TextoAsync(
        string? q, string orden = "recomendados", int? zona = null,
        double lat = CentroLat, double lon = CentroLon, int limite = 50, int desplazamiento = 0,
        bool soloVerificados = false, decimal? notaMinima = null)
    {
        var (formas, grupos) = TextoDeBusqueda.Grupos(q);
        return await _repo.PorTextoAsync(new CriteriosDeTexto(
            formas, grupos, lat, lon, zona, orden, _reloj.Ahora, limite, desplazamiento,
            soloVerificados, notaMinima));
    }

    private static string[] Nombres(IEnumerable<FilaDeTexto> filas) => filas.Select(f => f.NombrePublico).ToArray();

    private async Task PonerPrecioAsync(Guid perfilId, int? precio)
    {
        var oferta = await _prueba.Ctx.PrestadorServicios.FirstAsync(x => x.PerfilPrestadorId == perfilId);
        oferta.PrecioReferencia = precio;
        await _prueba.Ctx.SaveChangesAsync();
    }

    /// <summary>La razon de ser de los sinonimos: quien escribe «abogado» busca el rubro «Derecho».</summary>
    [Fact]
    public async Task Abogado_encuentra_a_los_de_derecho_y_no_a_los_de_corretaje()
    {
        await UnPrestadorBuscableAsync(rubroId: Derecho, servicioId: 201, nombre: "Andrea Quilodran");
        await UnPrestadorBuscableAsync(nombre: "Marcela Soto");     // corretaje

        Assert.Equal(new[] { "Andrea Quilodran" }, Nombres(await TextoAsync("abogado")));
        Assert.Equal(new[] { "Andrea Quilodran" }, Nombres(await TextoAsync("Abogados")));    // plural y mayuscula
    }

    [Fact]
    public async Task La_busqueda_no_distingue_tildes_ni_mayusculas_en_ningun_sentido()
    {
        await UnPrestadorBuscableAsync(nombre: "José García Ñancupil");

        foreach (var q in new[] { "garcia", "GARCÍA", "jose", "ñancupil", "nancupil", "Josè" })
            Assert.Single(await TextoAsync(q));
    }

    [Fact]
    public async Task Se_encuentra_por_el_nombre_de_un_servicio_sin_nombrar_el_oficio()
    {
        await UnPrestadorBuscableAsync(servicioId: 101, nombre: "Tasadora Uno");                       // Tasacion comercial
        await UnPrestadorBuscableAsync(rubroId: Derecho, servicioId: 201, nombre: "Abogada Dos");       // Escritura de compraventa

        Assert.Equal(new[] { "Tasadora Uno" }, Nombres(await TextoAsync("tasacion")));
        Assert.Equal(new[] { "Abogada Dos" }, Nombres(await TextoAsync("compraventa")));
    }

    [Fact]
    public async Task Todas_las_palabras_tienen_que_cumplirse()
    {
        await UnPrestadorBuscableAsync(rubroId: Derecho, servicioId: 201, nombre: "Andrea García");
        await UnPrestadorBuscableAsync(rubroId: Derecho, servicioId: 201, nombre: "Rodrigo Cayul");
        await UnPrestadorBuscableAsync(nombre: "Pedro García");        // corretaje, tambien García

        Assert.Equal(new[] { "Andrea García" }, Nombres(await TextoAsync("abogado garcia")));
        Assert.Empty(await TextoAsync("abogado zzzz"));
    }

    /// <summary>Los comodines de LIKE no llegan a la consulta: «%» no encuentra a todo el mundo.</summary>
    [Fact]
    public async Task Un_porcentaje_o_un_guion_bajo_no_son_comodines()
    {
        await UnPrestadorBuscableAsync(nombre: "Marcela Soto");

        Assert.Empty(await TextoAsync("zz%"));
        Assert.Empty(await TextoAsync("m_rcela zzz"));
    }

    [Fact]
    public async Task Sin_texto_aparece_todo_el_que_es_visible_y_nadie_mas()
    {
        await UnPrestadorBuscableAsync(nombre: "Visible Uno");
        await UnPrestadorBuscableAsync(nombre: "Visible Dos", rubroId: Derecho, servicioId: 201);
        await UnPrestadorBuscableAsync(nombre: "Vencido", diasDeVigencia: -2);
        await UnPrestadorBuscableAsync(nombre: "Borrador", estado: EstadoPerfil.Borrador);
        await UnPrestadorBuscableAsync(nombre: "Cancelado", estadoSuscripcion: EstadoSuscripcion.Cancelada);

        var todos = await TextoAsync(null);

        Assert.Equal(new[] { "Visible Dos", "Visible Uno" }.OrderBy(x => x), Nombres(todos).OrderBy(x => x));
        Assert.All(todos, f => Assert.Equal(2, f.TotalFilas));
    }

    [Fact]
    public async Task Lo_que_no_es_visible_no_aparece_ni_buscandolo_por_su_nombre_exacto()
    {
        await UnPrestadorBuscableAsync(nombre: "Fantasma Vencido", diasDeVigencia: -1);

        Assert.Empty(await TextoAsync("fantasma"));
    }

    // ------------------------------------------------- los cuatro ordenes

    /// <summary>
    /// «Recomendados»: con sello primero, luego la reputacion PUBLICA. Dos reseñas de cinco estrellas
    /// no le ganan a doce de 4,6: el promedio de quien tiene menos de tres no cuenta (RN-S5-11).
    /// </summary>
    [Fact]
    public async Task Recomendados_pone_primero_al_verificado_y_despues_a_la_reputacion_publica()
    {
        await UnPrestadorBuscableAsync(nombre: "Pocas resenas", promedio: 5.0m, resenas: 2);
        await UnPrestadorBuscableAsync(nombre: "Muchas resenas", promedio: 4.6m, resenas: 12);
        await UnPrestadorBuscableAsync(nombre: "Con sello", conCredencialVerificada: true);

        var r = await TextoAsync(null, "recomendados");

        Assert.Equal(new[] { "Con sello", "Muchas resenas", "Pocas resenas" }, Nombres(r));
    }

    [Fact]
    public async Task Cercania_ordena_por_lo_mas_cerca_de_la_persona_y_cambia_si_la_persona_se_mueve()
    {
        await UnPrestadorBuscableAsync(nombre: "En el centro", zonaId: Centro);
        await UnPrestadorBuscableAsync(nombre: "En Labranza", zonaId: Labranza);

        var desdeElCentro = await TextoAsync(null, "cercania", lat: CentroLat, lon: CentroLon);
        var desdeLabranza = await TextoAsync(null, "cercania", lat: LabranzaLat, lon: LabranzaLon);

        Assert.Equal(new[] { "En el centro", "En Labranza" }, Nombres(desdeElCentro));
        Assert.Equal(new[] { "En Labranza", "En el centro" }, Nombres(desdeLabranza));
        Assert.True(desdeElCentro[0].Tramo < desdeElCentro[1].Tramo);
    }

    /// <summary>
    /// El orden pedido tiene que MANDAR sobre el de recomendados, y para probarlo hace falta que se
    /// contradigan: el mas lejano tiene sello. Si «cercania» no ordenara, ganaria el del sello.
    /// </summary>
    [Fact]
    public async Task Cercania_manda_sobre_recomendados_y_recomendados_sobre_cercania()
    {
        await UnPrestadorBuscableAsync(nombre: "Lejos con sello", zonaId: Labranza, conCredencialVerificada: true);
        await UnPrestadorBuscableAsync(nombre: "Cerca sin sello", zonaId: Centro);

        Assert.Equal("Cerca sin sello", (await TextoAsync(null, "cercania", lat: CentroLat, lon: CentroLon))[0].NombrePublico);
        Assert.Equal("Lejos con sello", (await TextoAsync(null, "recomendados", lat: CentroLat, lon: CentroLon))[0].NombrePublico);
    }

    [Fact]
    public async Task Precio_manda_sobre_recomendados_aunque_el_mas_barato_no_tenga_sello()
    {
        var baratoSinSello = await UnPrestadorBuscableAsync(nombre: "Barato sin sello");
        var caroConSello = await UnPrestadorBuscableAsync(nombre: "Caro con sello", conCredencialVerificada: true);
        await PonerPrecioAsync(baratoSinSello.Id, 10_000);
        await PonerPrecioAsync(caroConSello.Id, 90_000);

        Assert.Equal("Barato sin sello", (await TextoAsync(null, "precio_asc"))[0].NombrePublico);
        Assert.Equal("Caro con sello", (await TextoAsync(null, "precio_desc"))[0].NombrePublico);
    }

    /// <summary>
    /// La reputacion pesa por si misma, no solo como efecto de tener mas resenas: 5 resenas de 4,9
    /// van antes que 20 de 4,0.
    /// </summary>
    [Fact]
    public async Task Recomendados_prefiere_la_mejor_nota_publica_a_la_que_tiene_mas_resenas()
    {
        await UnPrestadorBuscableAsync(nombre: "Muchas pero regular", promedio: 4.0m, resenas: 20);
        await UnPrestadorBuscableAsync(nombre: "Pocas pero excelente", promedio: 4.9m, resenas: 5);

        Assert.Equal(new[] { "Pocas pero excelente", "Muchas pero regular" }, Nombres(await TextoAsync(null, "recomendados")));
    }

    [Fact]
    public async Task Un_servicio_apagado_no_hace_que_encuentren_a_la_persona()
    {
        var perfil = await UnPrestadorBuscableAsync(nombre: "Persona apagada", servicioId: 101);
        (await _prueba.Ctx.PrestadorServicios.FirstAsync(x => x.PerfilPrestadorId == perfil.Id)).Activo = false;
        await _prueba.Ctx.SaveChangesAsync();

        Assert.Empty(await TextoAsync("tasacion"));
    }

    [Fact]
    public async Task Entre_dos_a_la_misma_distancia_gana_el_mejor_recomendado()
    {
        await UnPrestadorBuscableAsync(nombre: "Cerca sin resenas", zonaId: Centro);
        await UnPrestadorBuscableAsync(nombre: "Cerca con sello", zonaId: Centro, conCredencialVerificada: true);

        var r = await TextoAsync(null, "cercania");

        Assert.Equal("Cerca con sello", r[0].NombrePublico);
    }

    [Fact]
    public async Task Precio_ordena_de_menor_a_mayor_y_de_mayor_a_menor_con_los_sin_precio_al_final()
    {
        var barato = await UnPrestadorBuscableAsync(nombre: "Barato");
        var caro = await UnPrestadorBuscableAsync(nombre: "Caro");
        var sinPrecio = await UnPrestadorBuscableAsync(nombre: "Sin precio");
        await PonerPrecioAsync(barato.Id, 20_000);
        await PonerPrecioAsync(caro.Id, 90_000);
        await PonerPrecioAsync(sinPrecio.Id, null);

        var ascendente = await TextoAsync(null, "precio_asc");
        var descendente = await TextoAsync(null, "precio_desc");

        Assert.Equal(new[] { "Barato", "Caro", "Sin precio" }, Nombres(ascendente));
        Assert.Equal(new[] { "Caro", "Barato", "Sin precio" }, Nombres(descendente));
        Assert.Equal(20_000, ascendente[0].PrecioDesde);
    }

    [Fact]
    public async Task El_precio_es_el_del_servicio_mas_barato_que_ofrece_y_ignora_los_apagados()
    {
        var perfil = await UnPrestadorBuscableAsync(nombre: "Dos servicios");
        var oferta = await _prueba.Ctx.PrestadorServicios.FirstAsync(x => x.PerfilPrestadorId == perfil.Id);
        oferta.PrecioReferencia = 50_000;
        _prueba.Ctx.PrestadorServicios.Add(new PrestadorServicio
        {
            PerfilPrestadorId = perfil.Id, ServicioId = 102, DuracionMinutos = 60,
            ModoAgenda = ModoAgenda.Automatico, Activo = true, PrecioReferencia = 30_000,
        });
        _prueba.Ctx.PrestadorServicios.Add(new PrestadorServicio
        {
            PerfilPrestadorId = perfil.Id, ServicioId = 103, DuracionMinutos = 60,
            ModoAgenda = ModoAgenda.Automatico, Activo = false, PrecioReferencia = 5_000,   // apagado
        });
        await _prueba.Ctx.SaveChangesAsync();

        Assert.Equal(30_000, (await TextoAsync("dos servicios")).Single().PrecioDesde);
    }

    // ------------------------------------------------- los filtros

    [Fact]
    public async Task Solo_verificados_deja_fuera_a_quien_no_tiene_sello()
    {
        await UnPrestadorBuscableAsync(nombre: "Con sello", conCredencialVerificada: true);
        await UnPrestadorBuscableAsync(nombre: "Sin sello");

        Assert.Equal(new[] { "Con sello" }, Nombres(await TextoAsync(null, soloVerificados: true)));
        Assert.Equal(2, (await TextoAsync(null, soloVerificados: false)).Count);
    }

    /// <summary>
    /// La nota minima mira la reputacion PUBLICA: con menos de 3 reseñas no hay nota (RN-S5-11), asi que
    /// quien tiene 2 reseñas de 5 estrellas no entra en «4 estrellas o mas».
    /// </summary>
    [Fact]
    public async Task La_nota_minima_solo_cuenta_a_quien_tiene_tres_resenas_o_mas()
    {
        await UnPrestadorBuscableAsync(nombre: "Cuatro con tres", promedio: 4.0m, resenas: 3);
        await UnPrestadorBuscableAsync(nombre: "Cinco con dos", promedio: 5.0m, resenas: 2);
        await UnPrestadorBuscableAsync(nombre: "Tres con diez", promedio: 3.9m, resenas: 10);
        await UnPrestadorBuscableAsync(nombre: "Sin resenas");

        var r = await TextoAsync(null, notaMinima: 4m);

        Assert.Equal(new[] { "Cuatro con tres" }, Nombres(r));
    }

    [Fact]
    public async Task La_nota_minima_incluye_justo_la_nota_pedida()
    {
        await UnPrestadorBuscableAsync(nombre: "Justo cuatro", promedio: 4.0m, resenas: 5);

        Assert.Single(await TextoAsync(null, notaMinima: 4m));
        Assert.Empty(await TextoAsync(null, notaMinima: 4.1m));
    }

    [Fact]
    public async Task Los_filtros_se_suman_y_el_total_cuenta_solo_a_quien_los_cumple()
    {
        await UnPrestadorBuscableAsync(nombre: "Cumple todo", conCredencialVerificada: true, promedio: 4.8m, resenas: 12);
        await UnPrestadorBuscableAsync(nombre: "Solo sello", conCredencialVerificada: true);
        await UnPrestadorBuscableAsync(nombre: "Solo nota", promedio: 4.9m, resenas: 20);

        var r = await TextoAsync(null, soloVerificados: true, notaMinima: 4m);

        Assert.Equal(new[] { "Cumple todo" }, Nombres(r));
        Assert.Equal(1, r.Single().TotalFilas);       // el total es de lo filtrado, no de todos
    }

    [Fact]
    public async Task Los_filtros_se_combinan_con_el_texto_y_el_orden()
    {
        await UnPrestadorBuscableAsync(rubroId: Derecho, servicioId: 201, nombre: "Abogada con sello", conCredencialVerificada: true);
        await UnPrestadorBuscableAsync(rubroId: Derecho, servicioId: 201, nombre: "Abogado sin sello");
        await UnPrestadorBuscableAsync(nombre: "Corredor con sello", conCredencialVerificada: true);

        Assert.Equal(new[] { "Abogada con sello" }, Nombres(await TextoAsync("abogado", "precio_asc", soloVerificados: true)));
    }

    // ------------------------------------------------- Chile: comunas y barrios

    private async Task<int> ComunaAsync(string nombre) =>
        (await _prueba.Ctx.Zonas.SingleAsync(z => z.Tipo == TipoDeZona.Comuna && z.Nombre == nombre)).Id;

    /// <summary>
    /// Quien cubre toda la comuna de Temuco atiende en sus barrios, aunque no haya marcado ninguno. Sin esto, buscar en
    /// «Centro, Temuco» no encontraria a quien dijo «atiendo en Temuco», que es lo que dice casi todo el mundo.
    /// </summary>
    [Fact]
    public async Task Quien_cubre_la_comuna_aparece_al_buscar_en_uno_de_sus_barrios()
    {
        var temuco = await ComunaAsync("Temuco");
        await UnPrestadorBuscableAsync(nombre: "Cubre la comuna entera", zonaId: temuco);
        await UnPrestadorBuscableAsync(nombre: "Cubre solo Labranza", zonaId: Labranza);

        var enCentro = Nombres(await TextoAsync(null, zona: Centro));

        Assert.Equal(new[] { "Cubre la comuna entera" }, enCentro);
    }

    /// <summary>Y al revés: quien solo marcó un barrio aparece al buscar en su comuna.</summary>
    [Fact]
    public async Task Quien_cubre_un_barrio_aparece_al_buscar_en_su_comuna()
    {
        var temuco = await ComunaAsync("Temuco");
        await UnPrestadorBuscableAsync(nombre: "Solo el Centro", zonaId: Centro);
        await UnPrestadorBuscableAsync(nombre: "Solo Labranza", zonaId: Labranza);

        var enTemuco = Nombres(await TextoAsync(null, zona: temuco));

        Assert.Equal(new[] { "Solo Labranza", "Solo el Centro" }.OrderBy(x => x), enTemuco.OrderBy(x => x));
    }

    /// <summary>Un barrio no es de la comuna de al lado: lo de Temuco no aparece al buscar en Padre Las Casas.</summary>
    [Fact]
    public async Task Buscar_en_otra_comuna_no_trae_a_la_gente_de_Temuco()
    {
        var plc = await ComunaAsync("Padre Las Casas");
        var arica = await ComunaAsync("Arica");
        await UnPrestadorBuscableAsync(nombre: "De Temuco", zonaId: Centro);
        await UnPrestadorBuscableAsync(nombre: "De Padre Las Casas", zonaId: plc);

        Assert.Equal(new[] { "De Padre Las Casas" }, Nombres(await TextoAsync(null, zona: plc)));
        Assert.Empty(await TextoAsync(null, zona: arica));
    }

    /// <summary>Dos barrios hermanos no se relacionan entre si: buscar en Labranza no trae a quien solo cubre el Centro.</summary>
    [Fact]
    public async Task Dos_barrios_de_la_misma_comuna_no_se_mezclan()
    {
        await UnPrestadorBuscableAsync(nombre: "Solo el Centro", zonaId: Centro);

        Assert.Empty(await TextoAsync(null, zona: Labranza));
    }

    [Fact]
    public async Task La_zona_principal_de_un_barrio_se_dice_con_su_comuna_y_la_de_una_comuna_va_sola()
    {
        var plc = await ComunaAsync("Padre Las Casas");
        await UnPrestadorBuscableAsync(nombre: "En un barrio", zonaId: Centro);
        await UnPrestadorBuscableAsync(nombre: "En una comuna", zonaId: plc);

        var filas = (await TextoAsync(null)).ToDictionary(f => f.NombrePublico);

        Assert.Equal("Centro, Temuco", filas["En un barrio"].ZonaPrincipal);
        Assert.Equal("Padre Las Casas", filas["En una comuna"].ZonaPrincipal);
    }

    /// <summary>
    /// A nivel nacional una comuna lejana tambien se mide: sin ubicacion de la persona la distancia no se calcula (no hay
    /// ningun «centro» de Chile), y con ella, la de Arica queda a miles de km de quien busca en Temuco.
    /// </summary>
    [Fact]
    public async Task Sin_ubicacion_no_hay_distancia_y_con_ella_una_comuna_lejana_queda_lejos()
    {
        var arica = await ComunaAsync("Arica");
        await UnPrestadorBuscableAsync(nombre: "En Arica", zonaId: arica);

        var con = (await TextoAsync(null, lat: CentroLat, lon: CentroLon)).Single();
        var sin = (await _repo.PorTextoAsync(new CriteriosDeTexto(
            Array.Empty<string>(), Array.Empty<int>(), null, null, null, "recomendados", _reloj.Ahora, 50, 0))).Single();

        Assert.True(con.Metros > 1_800_000, $"Arica a {con.Metros / 1000:0} km de Temuco");
        Assert.Null(sin.Metros);
        Assert.Null(sin.Tramo);
    }

    // ------------------------------------------------- el resumen por zona y por region

    /// <summary>
    /// La portada cuenta en LINQ y la busqueda filtra en SQL (zonas_relacionadas). Esta prueba exige que digan lo mismo para
    /// cada una de las 362 zonas: si no, la portada diria «9 personas en Temuco» y al entrar aparecerian 7.
    /// </summary>
    [Fact]
    public async Task El_conteo_de_la_portada_coincide_con_lo_que_la_busqueda_devuelve_en_cada_zona()
    {
        var temuco = await ComunaAsync("Temuco");
        var plc = await ComunaAsync("Padre Las Casas");
        var arica = await ComunaAsync("Arica");
        await UnPrestadorBuscableAsync(nombre: "A", zonaId: temuco);
        await UnPrestadorBuscableAsync(nombre: "B", zonaId: Centro);
        await UnPrestadorBuscableAsync(nombre: "C", zonaId: Labranza);
        await UnPrestadorBuscableAsync(nombre: "D", zonaId: plc);
        var e = await UnPrestadorBuscableAsync(nombre: "E", zonaId: arica);
        _prueba.Ctx.Coberturas.Add(new CoberturaPrestador { PerfilPrestadorId = e.Id, ZonaId = Centro, EsPrincipal = false, DesdeEn = _reloj.Ahora });
        await _prueba.Ctx.SaveChangesAsync();

        var resumen = await _repo.ResumenAsync(_reloj.Ahora, 6);
        var contadas = resumen.PorZona.ToDictionary(c => c.Id, c => c.Personas);

        foreach (var zona in await _prueba.Ctx.Zonas.ToListAsync())
        {
            var buscadas = (await TextoAsync(null, zona: zona.Id)).Count;
            Assert.Equal(buscadas, contadas.GetValueOrDefault(zona.Id));
        }
        Assert.Equal(4, contadas[temuco]);        // A, B, C y E (que tambien cubre el Centro)
        Assert.Equal(3, contadas[Centro]);        // A por su comuna, B, y E
    }

    [Fact]
    public async Task Quien_cubre_dos_barrios_de_una_comuna_cuenta_una_sola_vez_en_ella()
    {
        var temuco = await ComunaAsync("Temuco");
        var p = await UnPrestadorBuscableAsync(nombre: "En dos barrios", zonaId: Centro);
        _prueba.Ctx.Coberturas.Add(new CoberturaPrestador { PerfilPrestadorId = p.Id, ZonaId = Labranza, EsPrincipal = false, DesdeEn = _reloj.Ahora });
        await _prueba.Ctx.SaveChangesAsync();

        var resumen = await _repo.ResumenAsync(_reloj.Ahora, 6);

        Assert.Equal(1, resumen.PorZona.Single(c => c.Id == temuco).Personas);
        Assert.Equal(1, resumen.Total);
    }

    [Fact]
    public async Task El_conteo_por_region_cuenta_a_cada_persona_una_vez_aunque_cubra_varias_comunas_de_ella()
    {
        var plc = await ComunaAsync("Padre Las Casas");
        var arica = await ComunaAsync("Arica");
        var p = await UnPrestadorBuscableAsync(nombre: "Dos comunas de La Araucanía", zonaId: Centro);
        _prueba.Ctx.Coberturas.Add(new CoberturaPrestador { PerfilPrestadorId = p.Id, ZonaId = plc, EsPrincipal = false, DesdeEn = _reloj.Ahora });
        await UnPrestadorBuscableAsync(nombre: "De Arica", zonaId: arica);
        await _prueba.Ctx.SaveChangesAsync();

        var resumen = await _repo.ResumenAsync(_reloj.Ahora, 6);
        var porRegion = resumen.PorRegion!.ToDictionary(r => r.Region, r => r.Personas);

        Assert.Equal(1, porRegion["La Araucanía"]);
        Assert.Equal(1, porRegion["Arica y Parinacota"]);
        Assert.Equal(2, resumen.Total);
    }

    // ------------------------------------------------- zona y paginas

    [Fact]
    public async Task La_zona_filtra_por_cobertura_y_no_solo_por_zona_principal()
    {
        var soloCentro = await UnPrestadorBuscableAsync(nombre: "Solo Centro", zonaId: Centro);
        var ambas = await UnPrestadorBuscableAsync(nombre: "Centro y Labranza", zonaId: Centro);
        _prueba.Ctx.Coberturas.Add(new CoberturaPrestador
        {
            PerfilPrestadorId = ambas.Id, ZonaId = Labranza, EsPrincipal = false, DesdeEn = _reloj.Ahora,
        });
        await _prueba.Ctx.SaveChangesAsync();

        Assert.Equal(new[] { "Centro y Labranza" }, Nombres(await TextoAsync(null, zona: Labranza)));
        Assert.Equal(2, (await TextoAsync(null, zona: Centro)).Count);
        Assert.NotNull(soloCentro);
    }

    [Fact]
    public async Task Las_paginas_no_se_repiten_ni_se_saltan_a_nadie_y_el_total_es_de_todas()
    {
        for (var i = 0; i < 7; i++) await UnPrestadorBuscableAsync(nombre: $"Persona {i}");

        var primera = await TextoAsync(null, limite: 3, desplazamiento: 0);
        var segunda = await TextoAsync(null, limite: 3, desplazamiento: 3);
        var tercera = await TextoAsync(null, limite: 3, desplazamiento: 6);

        var ids = primera.Concat(segunda).Concat(tercera).Select(f => f.Id).ToList();
        Assert.Equal(7, ids.Count);
        Assert.Equal(7, ids.Distinct().Count());
        Assert.All(primera.Concat(segunda).Concat(tercera), f => Assert.Equal(7, f.TotalFilas));
    }

    [Fact]
    public async Task El_orden_es_estable_entre_peticiones_aunque_haya_empates()
    {
        for (var i = 0; i < 6; i++) await UnPrestadorBuscableAsync(nombre: $"Empate {i}");

        var a = Nombres(await TextoAsync(null));
        var b = Nombres(await TextoAsync(null));

        Assert.Equal(a, b);
    }

    // ------------------------------------------------- el SQL y C# dicen lo mismo

    /// <summary>
    /// «normalizar_texto» (SQL) y «TextoDeBusqueda.Normalizar» (C#) tienen que dar lo mismo, o la
    /// consulta compararia «garcia» contra algo que ya no se parece a lo que se escribio.
    /// </summary>
    [Theory]
    [InlineData("GARCÍA")]
    [InlineData("Ñielol")]
    [InlineData("Muñoz Ñandú")]
    [InlineData("Güemes")]
    [InlineData("ÁÉÍÓÚ áéíóú")]
    [InlineData("àèìòù")]
    [InlineData("Técnicos y oficios")]
    [InlineData("sin tildes")]
    public async Task La_normalizacion_de_la_base_y_la_de_C_sharp_coinciden(string texto)
    {
        var enSql = await _prueba.Ctx.Database
            .SqlQuery<string>($"SELECT normalizar_texto({texto}) AS \"Value\"").SingleAsync();

        Assert.Equal(TextoDeBusqueda.Normalizar(texto), enSql);
    }

    // ------------------------------------------------- API-62, la comunidad

    /// <summary>
    /// La prueba que vigila la duplicacion: "visible" vive en el SQL de la busqueda y otra vez
    /// en este LINQ. Si la portada contara a alguien que la busqueda no muestra, diria "14
    /// personas en el Centro" y al entrar aparecerian 12. Se compara el conteo con lo que la
    /// busqueda DEVUELVE, caso por caso.
    /// </summary>
    [Fact]
    public async Task El_resumen_cuenta_exactamente_a_quien_la_busqueda_muestra()
    {
        await UnPrestadorBuscableAsync();                                          // visible
        await UnPrestadorBuscableAsync(conCredencialVerificada: true);             // visible
        await UnPrestadorBuscableAsync(diasDeVigencia: -3);                        // suscripcion vencida
        await UnPrestadorBuscableAsync(estado: EstadoPerfil.Borrador);             // sin publicar
        await UnPrestadorBuscableAsync(estado: EstadoPerfil.Despublicado);         // despublicado
        await UnPrestadorBuscableAsync(estadoSuscripcion: EstadoSuscripcion.Cancelada); // cancelada
        await UnPrestadorBuscableAsync(rubroId: Derecho, servicioId: 201);         // visible, otro rubro

        var resumen = await _repo.ResumenAsync(_reloj.Ahora, 6);
        var buscados = await _repo.PorZonaAsync(Consulta(_reloj.Ahora), null);

        Assert.Equal(3, resumen.Total);
        // En el Centro y en corretaje: lo mismo que devuelve la busqueda.
        Assert.Equal(buscados.Count, resumen.PorZona.Single(z => z.Id == Centro).Personas - 1);   // el de derecho tambien cubre el Centro
        Assert.Equal(2, resumen.PorRubro.Single(r => r.Id == Corretaje).Personas);
        Assert.Equal(1, resumen.PorRubro.Single(r => r.Id == Derecho).Personas);
    }

    [Fact]
    public async Task Un_prestador_que_cubre_dos_zonas_cuenta_en_cada_una_y_una_sola_vez_en_el_total()
    {
        var perfil = await UnPrestadorBuscableAsync(zonaId: Centro);
        _prueba.Ctx.Coberturas.Add(new CoberturaPrestador
        {
            PerfilPrestadorId = perfil.Id, ZonaId = Labranza, EsPrincipal = false, DesdeEn = _reloj.Ahora,
        });
        await _prueba.Ctx.SaveChangesAsync();

        var resumen = await _repo.ResumenAsync(_reloj.Ahora, 6);

        Assert.Equal(1, resumen.Total);
        Assert.Equal(1, resumen.PorZona.Single(z => z.Id == Centro).Personas);
        Assert.Equal(1, resumen.PorZona.Single(z => z.Id == Labranza).Personas);
    }

    [Fact]
    public async Task Los_recien_llegados_van_del_mas_nuevo_al_mas_viejo_con_su_zona_y_su_sello()
    {
        var viejo = await UnPrestadorBuscableAsync(nombre: "El mas viejo");
        var nuevo = await UnPrestadorBuscableAsync(nombre: "El mas nuevo", conCredencialVerificada: true);
        viejo.PublicadoEn = _reloj.Ahora.AddDays(-10);
        nuevo.PublicadoEn = _reloj.Ahora.AddDays(-1);
        await _prueba.Ctx.SaveChangesAsync();

        var resumen = await _repo.ResumenAsync(_reloj.Ahora, 6);

        Assert.Equal(new[] { "El mas nuevo", "El mas viejo" }, resumen.Recientes.Select(r => r.NombrePublico).ToArray());
        var primero = resumen.Recientes[0];
        Assert.Equal("Centro, Temuco", primero.ZonaPrincipal);
        Assert.Equal("Corretaje de propiedades", primero.RubroNombre);
        Assert.True(primero.Verificada);
        Assert.False(resumen.Recientes[1].Verificada);
    }

    /// <summary>Si tambien cubre otra zona, la tarjeta dice la PRINCIPAL; la otra no puede ganarle.</summary>
    [Fact]
    public async Task El_recien_llegado_muestra_su_zona_principal_y_no_una_secundaria()
    {
        var perfil = await UnPrestadorBuscableAsync(zonaId: Labranza);
        _prueba.Ctx.Coberturas.Add(new CoberturaPrestador
        {
            PerfilPrestadorId = perfil.Id, ZonaId = Centro, EsPrincipal = false, DesdeEn = _reloj.Ahora,
        });
        await _prueba.Ctx.SaveChangesAsync();

        // Se toca la principal DESPUES: en PostgreSQL la fila actualizada pasa al final, asi que la
        // secundaria queda primera. Sin esto una consulta que olvide filtrar por principal acertaria
        // por casualidad, porque la principal se inserto primero.
        var principal = _prueba.Ctx.Coberturas.Single(c => c.PerfilPrestadorId == perfil.Id && c.EsPrincipal);
        principal.DesdeEn = principal.DesdeEn.AddMinutes(1);
        await _prueba.Ctx.SaveChangesAsync();

        var resumen = await _repo.ResumenAsync(_reloj.Ahora, 6);

        Assert.Equal("Labranza, Temuco", Assert.Single(resumen.Recientes).ZonaPrincipal);
    }

    /// <summary>El sello es solo de lo verificado: una credencial en revision, rechazada o vencida no lo da.</summary>
    [Theory]
    [InlineData(EstadoCredencial.Borrador)]
    [InlineData(EstadoCredencial.EnRevision)]
    [InlineData(EstadoCredencial.Rechazada)]
    [InlineData(EstadoCredencial.Vencida)]
    public async Task Una_credencial_que_no_esta_verificada_no_da_sello(EstadoCredencial estado)
    {
        var perfil = await UnPrestadorBuscableAsync();
        _prueba.Ctx.Credenciales.Add(new Credencial
        {
            PerfilPrestadorId = perfil.Id,
            Tipo = TipoCredencial.Titulo,
            Nombre = "Titulo profesional",
            ObtenidaEn = new DateOnly(2020, 1, 15),
            Estado = estado,
        });
        await _prueba.Ctx.SaveChangesAsync();

        var resumen = await _repo.ResumenAsync(_reloj.Ahora, 6);

        Assert.False(Assert.Single(resumen.Recientes).Verificada);
    }

    [Fact]
    public async Task Los_recien_llegados_se_cortan_en_el_maximo_pedido()
    {
        for (var i = 0; i < 8; i++) await UnPrestadorBuscableAsync();

        var resumen = await _repo.ResumenAsync(_reloj.Ahora, 6);

        Assert.Equal(6, resumen.Recientes.Count);
        Assert.Equal(8, resumen.Total);          // el corte es de la lista, no del conteo
    }

    [Fact]
    public async Task Un_recien_llegado_con_suscripcion_vencida_no_sale()
    {
        await UnPrestadorBuscableAsync(nombre: "Vencido", diasDeVigencia: -1);

        var resumen = await _repo.ResumenAsync(_reloj.Ahora, 6);

        Assert.Empty(resumen.Recientes);
        Assert.Equal(0, resumen.Total);
    }

    [Fact]
    public async Task Sin_prestadores_visibles_todo_viene_vacio()
    {
        var resumen = await _repo.ResumenAsync(_reloj.Ahora, 6);

        Assert.Equal(0, resumen.Total);
        Assert.Empty(resumen.PorZona);
        Assert.Empty(resumen.PorRubro);
        Assert.Empty(resumen.Recientes);
    }

    // ------------------------------------------------- la busqueda por zona

    [Fact]
    public async Task Por_zona_se_ordena_por_reputacion_y_no_lleva_distancia()
    {
        var mejor = await UnPrestadorBuscableAsync(promedio: 4.8m, resenas: 10);
        var peor = await UnPrestadorBuscableAsync(promedio: 3.2m, resenas: 10);
        var sinResenas = await UnPrestadorBuscableAsync();

        var filas = await _repo.PorZonaAsync(Consulta(_reloj.Ahora), null);

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

        var filas = await _repo.PorZonaAsync(Consulta(_reloj.Ahora), null);

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
