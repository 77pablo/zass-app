using System.Text.Json;
using Zass.Aplicacion.Credenciales;
using Zass.Aplicacion.Pruebas.Dobles;
using Zass.Dominio.Comun;
using Zass.Dominio.Credenciales;
using Zass.Dominio.Identidad;

namespace Zass.Aplicacion.Pruebas.Credenciales;

/// <summary>
/// MOD-02 · CU-06: la cola, tomar y resolver, y API-27 (enviar a revisión) en sus dos modos:
/// con `PermitirSinDocumentos` apagado (la spec tal cual) y encendido (decisión de lanzamiento).
/// Sin base de datos: la carrera real de dos verificadores se prueba en integración.
/// </summary>
public class VerificacionPruebas
{
    private static readonly Guid Ana = Guid.Parse("00000000-0000-0000-0000-0000000000a1");   // verificadora
    private static readonly Guid Beto = Guid.Parse("00000000-0000-0000-0000-0000000000b1");  // otro verificador
    private static readonly Guid Duena = Guid.Parse("00000000-0000-0000-0000-0000000000d1"); // prestadora (usuario)

    private readonly BaseEnMemoria _base = new();
    private readonly AuditoriaEnMemoria _auditoria = new();
    private readonly RelojFijo _reloj = new(new DateTime(2026, 11, 12, 15, 0, 0, DateTimeKind.Utc));
    private readonly PerfilPrestador _perfil;
    private readonly PerfilPrestador _perfilDeAna;

    public VerificacionPruebas()
    {
        _perfil = new PerfilPrestador { UsuarioId = Duena, NombrePublico = "Estudio Duena", Slug = "estudio-duena" };
        // Ana tambien es prestadora: RN-S2-09 le prohibe revisar lo suyo.
        _perfilDeAna = new PerfilPrestador { UsuarioId = Ana, NombrePublico = "Ana Contadora", Slug = "ana-contadora" };
        _base.PerfilesPrestador.Add(_perfil);
        _base.PerfilesPrestador.Add(_perfilDeAna);
    }

    private ServicioVerificacion Servicio(bool sinDocumentos = false) => new(
        new RepositorioCredencialesEnMemoria(_base), _base, _reloj, _auditoria,
        new OpcionesVerificacion { PermitirSinDocumentos = sinDocumentos });

    private ServicioCredenciales Credenciales(bool sinDocumentos = false) => new(
        new RepositorioCredencialesEnMemoria(_base), _base, _reloj, _auditoria,
        new OpcionesVerificacion { PermitirSinDocumentos = sinDocumentos });

    private Credencial EnRevision(
        string nombre = "Abogado", bool conDocumento = true, PerfilPrestador? perfil = null,
        int minutosEnCola = 0, DateOnly? vence = null)
    {
        var c = new Credencial
        {
            PerfilPrestadorId = (perfil ?? _perfil).Id, Nombre = nombre, Tipo = TipoCredencial.Titulo,
            Estado = EstadoCredencial.EnRevision, ObtenidaEn = new DateOnly(2020, 3, 15), VenceEn = vence,
            CreadaEn = _reloj.Ahora.AddMinutes(-minutosEnCola),
        };
        if (conDocumento)
            c.Documentos.Add(new DocumentoCredencial { Estado = EstadoDocumento.Disponible, NombreOriginal = "t.pdf" });
        _base.Credenciales.Add(c);
        return c;
    }

    private async Task<Credencial> TomadaPorAsync(Guid verificador, Credencial c, bool sinDocumentos = false)
    {
        var r = await Servicio(sinDocumentos).TomarAsync(verificador, c.Id);
        Assert.True(r.Exito, r.Mensaje);
        return c;
    }

    private static ResolverCredencialCmd Verificar(Guid quien, Guid id, string? detalle = null) =>
        new(quien, id, ResultadoVerificacion.Verificada, null, detalle);

    private static ResolverCredencialCmd Rechazar(Guid quien, Guid id, MotivoRechazo? motivo, string? detalle) =>
        new(quien, id, ResultadoVerificacion.Rechazada, motivo, detalle);

    private static ResolverCredencialCmd Pedir(Guid quien, Guid id, string? detalle) =>
        new(quien, id, ResultadoVerificacion.AntecedentesSolicitados, null, detalle);

    // ============================================================= API-29 · la cola

    [Fact]
    public async Task La_cola_trae_solo_lo_que_esta_en_revision_la_mas_antigua_primero()
    {
        var nueva = EnRevision("Nueva", minutosEnCola: 5);
        var vieja = EnRevision("Vieja", minutosEnCola: 500);
        _base.Credenciales.Add(new Credencial { PerfilPrestadorId = _perfil.Id, Nombre = "Borrador", Estado = EstadoCredencial.Borrador });
        _base.Credenciales.Add(new Credencial { PerfilPrestadorId = _perfil.Id, Nombre = "Ya verificada", Estado = EstadoCredencial.Verificada, SelloPor = Beto });
        _base.Credenciales.Add(new Credencial { PerfilPrestadorId = _perfil.Id, Nombre = "Rechazada", Estado = EstadoCredencial.Rechazada });

        var cola = await Servicio().ColaAsync(Ana);

        Assert.Equal(new[] { vieja.Id, nueva.Id }, cola.Select(c => c.Id));
        Assert.Equal("Estudio Duena", cola[0].Prestador);
    }

    /// <summary>RN-S2-09: la cuenta del propio verificador no aparece en su cola.</summary>
    [Fact]
    public async Task La_cola_no_trae_las_credenciales_del_propio_verificador()
    {
        EnRevision("Ajena");
        var propia = EnRevision("Propia", perfil: _perfilDeAna);

        var cola = await Servicio().ColaAsync(Ana);

        Assert.DoesNotContain(cola, c => c.Id == propia.Id);
        Assert.Single(cola);
        // y Beto, que no es dueño de nada, si la ve
        Assert.Equal(2, (await Servicio().ColaAsync(Beto)).Count);
    }

    [Fact]
    public async Task La_cola_no_trae_las_que_otro_verificador_tiene_reservadas_y_vigentes()
    {
        var c = EnRevision();
        await TomadaPorAsync(Beto, c);

        Assert.Empty(await Servicio().ColaAsync(Ana));

        // La reserva de Beto sigue siendo suya en SU cola, marcada como tomada.
        var suya = Assert.Single(await Servicio().ColaAsync(Beto));
        Assert.True(suya.ReservadaPorMi);
        Assert.Equal(_reloj.Ahora.AddMinutes(30), suya.ReservadaHasta);
    }

    [Fact]
    public async Task Una_reserva_vencida_devuelve_la_credencial_a_la_cola()
    {
        var c = EnRevision();
        await TomadaPorAsync(Beto, c);

        _reloj.Avanzar(TimeSpan.FromMinutes(31));

        var ana = Assert.Single(await Servicio().ColaAsync(Ana));
        Assert.False(ana.ReservadaPorMi);
        Assert.Null(ana.ReservadaHasta);
    }

    [Fact]
    public async Task La_cola_no_trae_las_que_esperan_al_prestador()
    {
        var c = EnRevision();
        c.EsperandoPrestador = true;

        Assert.Empty(await Servicio().ColaAsync(Ana));
    }

    [Fact]
    public async Task La_cola_avisa_si_se_envio_sin_documentos_y_si_el_vencimiento_ya_paso()
    {
        EnRevision("Con respaldo", conDocumento: true);
        EnRevision("Sin respaldo", conDocumento: false, vence: new DateOnly(2026, 1, 1));

        var cola = await Servicio().ColaAsync(Ana);

        var con = cola.Single(c => c.Nombre == "Con respaldo");
        var sin = cola.Single(c => c.Nombre == "Sin respaldo");
        Assert.False(con.SinDocumentos);
        Assert.Equal(1, con.Documentos);
        Assert.False(con.VencimientoPasado);
        Assert.True(sin.SinDocumentos);
        Assert.Equal(0, sin.Documentos);
        Assert.True(sin.VencimientoPasado);
    }

    // ============================================================= API-30 · tomar

    [Fact]
    public async Task Tomar_reserva_por_30_minutos_y_lo_audita()
    {
        var c = EnRevision();

        var r = await Servicio().TomarAsync(Ana, c.Id);

        Assert.True(r.Exito);
        Assert.Equal(Ana, c.ReservadaPor);
        Assert.Equal(_reloj.Ahora.AddMinutes(30), c.ReservadaHasta);
        Assert.True(r.Valor!.ReservadaPorMi);
        Assert.Equal(_reloj.Ahora.AddMinutes(30), r.Valor.ReservadaHasta);
        var reg = Assert.Single(_auditoria.Registros);
        Assert.Equal("tomar_credencial", reg.Accion);
        Assert.Equal(Ana, reg.ActorId);
        Assert.Equal(c.Id, reg.EntidadId);
        Assert.Equal(1, _base.Guardados);
    }

    [Fact]
    public async Task Si_otro_ya_la_tomo_responde_conflicto_y_no_cambia_la_reserva()
    {
        var c = EnRevision();
        await TomadaPorAsync(Beto, c);

        var r = await Servicio().TomarAsync(Ana, c.Id);

        Assert.False(r.Exito);
        Assert.Equal(Fallas.Conflicto, r.Codigo);
        Assert.Equal("Otro verificador ya la tomó.", r.Mensaje);
        Assert.Equal(Beto, c.ReservadaPor);
    }

    [Fact]
    public async Task Pasados_los_30_minutos_otro_verificador_puede_tomarla()
    {
        var c = EnRevision();
        await TomadaPorAsync(Beto, c);
        _reloj.Avanzar(TimeSpan.FromMinutes(30).Add(TimeSpan.FromSeconds(1)));

        var r = await Servicio().TomarAsync(Ana, c.Id);

        Assert.True(r.Exito);
        Assert.Equal(Ana, c.ReservadaPor);
    }

    [Fact]
    public async Task Quien_ya_la_tiene_puede_volver_a_tomarla_y_se_renueva_la_reserva()
    {
        var c = EnRevision();
        await TomadaPorAsync(Ana, c);
        _reloj.Avanzar(TimeSpan.FromMinutes(20));

        var r = await Servicio().TomarAsync(Ana, c.Id);

        Assert.True(r.Exito);
        Assert.Equal(_reloj.Ahora.AddMinutes(30), c.ReservadaHasta);
    }

    /// <summary>RN-S2-09 + 4E.3: error genérico, igual que una inexistente, y queda el intento.</summary>
    [Fact]
    public async Task Tomar_la_propia_responde_como_si_no_existiera_y_registra_el_intento()
    {
        var propia = EnRevision(perfil: _perfilDeAna);

        var propiaR = await Servicio().TomarAsync(Ana, propia.Id);
        var inexistente = await Servicio().TomarAsync(Ana, Guid.NewGuid());

        Assert.False(propiaR.Exito);
        Assert.Equal(Fallas.NoEncontrado, propiaR.Codigo);
        Assert.Equal(inexistente.Codigo, propiaR.Codigo);
        Assert.Equal(inexistente.Mensaje, propiaR.Mensaje);
        Assert.Null(propia.ReservadaPor);
        var reg = Assert.Single(_auditoria.Registros);
        Assert.Equal("intento_revisar_propia", reg.Accion);
        Assert.Equal(Ana, reg.ActorId);
    }

    [Theory]
    [InlineData(EstadoCredencial.Borrador)]
    [InlineData(EstadoCredencial.Verificada)]
    [InlineData(EstadoCredencial.Rechazada)]
    [InlineData(EstadoCredencial.Vencida)]
    public async Task Solo_se_toma_lo_que_esta_en_revision(EstadoCredencial estado)
    {
        var c = EnRevision();
        c.Estado = estado;

        var r = await Servicio().TomarAsync(Ana, c.Id);

        Assert.False(r.Exito);
        Assert.Equal(Fallas.Conflicto, r.Codigo);
        Assert.Null(c.ReservadaPor);
    }

    [Fact]
    public async Task No_se_toma_una_que_espera_al_prestador()
    {
        var c = EnRevision();
        c.EsperandoPrestador = true;

        var r = await Servicio().TomarAsync(Ana, c.Id);

        Assert.Equal(Fallas.Conflicto, r.Codigo);
        Assert.Null(c.ReservadaPor);
    }

    // ============================================================= API-31 · resolver

    [Fact]
    public async Task Verificar_con_documentos_pone_el_sello_registra_la_decision_y_libera_la_reserva()
    {
        var c = EnRevision(vence: new DateOnly(2030, 1, 1));
        await TomadaPorAsync(Ana, c);
        _auditoria.Registros.Clear();

        var r = await Servicio().ResolverAsync(Verificar(Ana, c.Id, "Titulo coincide con el documento"));

        Assert.True(r.Exito);
        Assert.Equal(EstadoCredencial.Verificada, r.Valor!.Estado);
        Assert.Equal(EstadoCredencial.Verificada, c.Estado);
        Assert.Equal(Ana, c.SelloPor);
        Assert.Equal(_reloj.Ahora, c.SelloEn);
        Assert.Null(c.ReservadaPor);
        Assert.Null(c.ReservadaHasta);
        var v = Assert.Single(c.Verificaciones);
        Assert.Equal(Ana, v.VerificadorId);
        Assert.Equal(ResultadoVerificacion.Verificada, v.Resultado);
        Assert.Equal("Titulo coincide con el documento", v.Detalle);
        Assert.Equal(new DateOnly(2030, 1, 1), v.VigenciaHasta);
        Assert.Equal(_reloj.Ahora, v.ResueltaEn);
        Assert.Equal("Verificada", c.EtiquetaPublica());
        // RN-S2-08: quien, cuando y por que
        var reg = Assert.Single(_auditoria.Registros);
        Assert.Equal("verificar_credencial", reg.Accion);
        Assert.Equal(Ana, reg.ActorId);
        Assert.Equal(_reloj.Ahora, reg.OcurridoEn);
        Assert.Contains("Titulo coincide con el documento", reg.Contexto);
    }

    [Fact]
    public async Task Sin_haberla_tomado_no_se_puede_resolver()
    {
        var c = EnRevision();

        var r = await Servicio().ResolverAsync(Verificar(Ana, c.Id));

        Assert.False(r.Exito);
        Assert.Equal(Fallas.Conflicto, r.Codigo);
        Assert.Equal(EstadoCredencial.EnRevision, c.Estado);
        Assert.Empty(c.Verificaciones);
    }

    /// <summary>4C.3: si la reserva venció, aviso de que ya no la tiene.</summary>
    [Fact]
    public async Task Con_la_reserva_vencida_no_se_puede_resolver()
    {
        var c = EnRevision();
        await TomadaPorAsync(Ana, c);
        _reloj.Avanzar(TimeSpan.FromMinutes(31));

        var r = await Servicio().ResolverAsync(Verificar(Ana, c.Id));

        Assert.Equal(Fallas.Conflicto, r.Codigo);
        Assert.Contains("Ya no tienes reservada", r.Mensaje);
        Assert.Equal(EstadoCredencial.EnRevision, c.Estado);
    }

    [Fact]
    public async Task Justo_en_el_limite_de_los_30_minutos_todavia_se_puede_resolver()
    {
        var c = EnRevision();
        await TomadaPorAsync(Ana, c);
        _reloj.Avanzar(TimeSpan.FromMinutes(30));

        Assert.True((await Servicio().ResolverAsync(Verificar(Ana, c.Id))).Exito);
    }

    [Fact]
    public async Task No_se_resuelve_lo_que_tomo_otro_verificador()
    {
        var c = EnRevision();
        await TomadaPorAsync(Beto, c);

        var r = await Servicio().ResolverAsync(Verificar(Ana, c.Id));

        Assert.Equal(Fallas.Conflicto, r.Codigo);
        Assert.Equal(EstadoCredencial.EnRevision, c.Estado);
        Assert.Equal(Beto, c.ReservadaPor);
    }

    /// <summary>RN-S2-09: aunque tenga la reserva (¿un dato corrupto?), la propia no se resuelve.</summary>
    [Fact]
    public async Task No_se_puede_resolver_la_propia_ni_con_la_reserva_puesta()
    {
        var propia = EnRevision(perfil: _perfilDeAna);
        propia.Reservar(Ana, _reloj.Ahora.AddMinutes(30));

        var r = await Servicio().ResolverAsync(Verificar(Ana, propia.Id));

        Assert.Equal(Fallas.NoEncontrado, r.Codigo);
        Assert.Equal(EstadoCredencial.EnRevision, propia.Estado);
        Assert.Contains(_auditoria.Registros, a => a.Accion == "intento_revisar_propia");
    }

    [Fact]
    public async Task Resolver_una_inexistente_es_404()
    {
        var r = await Servicio().ResolverAsync(Verificar(Ana, Guid.NewGuid()));
        Assert.Equal(Fallas.NoEncontrado, r.Codigo);
    }

    [Fact]
    public async Task Resolver_una_ya_resuelta_responde_409_con_la_fecha()
    {
        var c = EnRevision();
        await TomadaPorAsync(Ana, c);
        await Servicio().ResolverAsync(Verificar(Ana, c.Id));

        var otra = await Servicio().ResolverAsync(Verificar(Beto, c.Id));

        Assert.Equal(Fallas.Conflicto, otra.Codigo);
        Assert.Contains("ya fue resuelta", otra.Mensaje);
        Assert.Contains("12-11-2026", otra.Mensaje);
        Assert.Single(c.Verificaciones);
    }

    // ---- RN-S2-06: un rechazo exige motivo escrito ----

    [Fact]
    public async Task Un_rechazo_sin_motivo_no_se_acepta()
    {
        var c = EnRevision();
        await TomadaPorAsync(Ana, c);

        var r = await Servicio().ResolverAsync(Rechazar(Ana, c.Id, null, "El documento no se lee nada"));

        Assert.Equal(Fallas.Validacion, r.Codigo);
        Assert.Equal(EstadoCredencial.EnRevision, c.Estado);
        Assert.Equal(Ana, c.ReservadaPor);   // sigue con ella para corregir y reintentar
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("      ")]
    [InlineData("corto")]
    [InlineData("123456789")]
    public async Task Un_rechazo_sin_texto_suficiente_no_se_acepta(string? detalle)
    {
        var c = EnRevision();
        await TomadaPorAsync(Ana, c);

        var r = await Servicio().ResolverAsync(Rechazar(Ana, c.Id, MotivoRechazo.Ilegible, detalle));

        Assert.Equal(Fallas.Validacion, r.Codigo);
        Assert.Equal(EstadoCredencial.EnRevision, c.Estado);
        Assert.Empty(c.Verificaciones);
    }

    [Fact]
    public async Task Un_rechazo_con_motivo_y_texto_deja_la_credencial_rechazada_y_sin_sello()
    {
        var c = EnRevision();
        await TomadaPorAsync(Ana, c);
        _auditoria.Registros.Clear();

        var r = await Servicio().ResolverAsync(
            Rechazar(Ana, c.Id, MotivoRechazo.NoCorresponde, "  El nombre del documento es otro  "));

        Assert.True(r.Exito);
        Assert.Equal(EstadoCredencial.Rechazada, c.Estado);
        Assert.Null(c.SelloPor);
        Assert.Null(c.SelloEn);
        Assert.Null(c.ReservadaPor);
        var v = Assert.Single(c.Verificaciones);
        Assert.Equal(MotivoRechazo.NoCorresponde, v.Motivo);
        Assert.Equal("El nombre del documento es otro", v.Detalle);
        Assert.True(v.EsRechazo());
        // 4A.6: el público la sigue viendo como declarada
        Assert.Equal("Declarada por el usuario", c.EtiquetaPublica());
        var reg = Assert.Single(_auditoria.Registros);
        Assert.Equal("rechazar_credencial", reg.Accion);
        Assert.Contains("NoCorresponde", reg.Contexto);
    }

    [Fact]
    public async Task Una_rechazada_puede_reenviarse_y_vuelve_a_la_cola()
    {
        var c = EnRevision();
        await TomadaPorAsync(Ana, c);
        await Servicio().ResolverAsync(Rechazar(Ana, c.Id, MotivoRechazo.Ilegible, "No se alcanza a leer"));

        var reenvio = await Credenciales().EnviarARevisionAsync(_perfil.Id, c.Id);

        Assert.True(reenvio.Exito);
        Assert.Equal(EstadoCredencial.EnRevision, c.Estado);
        Assert.Contains(await Servicio().ColaAsync(Beto), x => x.Id == c.Id);
    }

    // ---- 4B: pedir antecedentes ----

    [Fact]
    public async Task Pedir_antecedentes_exige_decir_que_falta()
    {
        var c = EnRevision();
        await TomadaPorAsync(Ana, c);

        var r = await Servicio().ResolverAsync(Pedir(Ana, c.Id, "  "));

        Assert.Equal(Fallas.Validacion, r.Codigo);
        Assert.False(c.EsperandoPrestador);
    }

    [Fact]
    public async Task Pedir_antecedentes_deja_en_revision_esperando_al_prestador_y_la_saca_de_la_cola()
    {
        var c = EnRevision();
        await TomadaPorAsync(Ana, c);
        _auditoria.Registros.Clear();

        var r = await Servicio().ResolverAsync(Pedir(Ana, c.Id, "Falta el certificado de la universidad"));

        Assert.True(r.Exito);
        Assert.True(r.Valor!.EsperandoPrestador);
        Assert.Equal(EstadoCredencial.EnRevision, c.Estado);
        Assert.True(c.EsperandoPrestador);
        Assert.Null(c.ReservadaPor);
        Assert.Null(c.SelloPor);
        Assert.Equal(ResultadoVerificacion.AntecedentesSolicitados, Assert.Single(c.Verificaciones).Resultado);
        Assert.Empty(await Servicio().ColaAsync(Beto));
        Assert.Equal("pedir_antecedentes", Assert.Single(_auditoria.Registros).Accion);
    }

    [Fact]
    public async Task Cuando_el_prestador_responde_vuelve_a_la_cola()
    {
        var c = EnRevision();
        await TomadaPorAsync(Ana, c);
        await Servicio().ResolverAsync(Pedir(Ana, c.Id, "Falta el certificado de la universidad"));

        var r = await Credenciales().EnviarARevisionAsync(_perfil.Id, c.Id);

        Assert.True(r.Exito);
        Assert.False(c.EsperandoPrestador);
        Assert.Contains(await Servicio().ColaAsync(Beto), x => x.Id == c.Id);
    }

    // ---- 4F ----

    [Fact]
    public async Task Una_credencial_con_el_vencimiento_ya_pasado_se_puede_verificar_igual()
    {
        var c = EnRevision(vence: new DateOnly(2026, 1, 1));
        await TomadaPorAsync(Ana, c);

        var r = await Servicio().ResolverAsync(Verificar(Ana, c.Id));

        Assert.True(r.Exito);
        Assert.Equal(EstadoCredencial.Verificada, c.Estado);
    }

    // ---- validacion de la forma ----

    [Fact]
    public async Task Un_resultado_que_no_existe_se_rechaza()
    {
        var c = EnRevision();
        await TomadaPorAsync(Ana, c);

        var r = await Servicio().ResolverAsync(new ResolverCredencialCmd(Ana, c.Id, (ResultadoVerificacion)99, null, null));

        Assert.Equal(Fallas.Validacion, r.Codigo);
    }

    [Fact]
    public async Task Un_detalle_de_mas_de_1000_caracteres_se_rechaza()
    {
        var c = EnRevision();
        await TomadaPorAsync(Ana, c);

        var r = await Servicio().ResolverAsync(Verificar(Ana, c.Id, new string('a', 1001)));
        Assert.Equal(Fallas.Validacion, r.Codigo);

        Assert.True((await Servicio().ResolverAsync(Verificar(Ana, c.Id, new string('a', 1000)))).Exito);
    }

    // ============================================================= sin documentos

    [Fact]
    public async Task Con_la_bandera_encendida_verificar_sin_documentos_exige_20_caracteres_de_como_se_comprobo()
    {
        var c = EnRevision(conDocumento: false);
        await TomadaPorAsync(Ana, c, sinDocumentos: true);

        foreach (var corto in new string?[] { null, "", "   ", "Lo vi en persona", new string('x', 19) })
        {
            var r = await Servicio(sinDocumentos: true).ResolverAsync(Verificar(Ana, c.Id, corto));
            Assert.Equal(Fallas.Validacion, r.Codigo);
            Assert.Equal(EstadoCredencial.EnRevision, c.Estado);
        }
        Assert.Empty(c.Verificaciones);
        Assert.Equal(Ana, c.ReservadaPor);
    }

    [Fact]
    public async Task Con_la_bandera_encendida_verificar_sin_documentos_usa_la_accion_de_auditoria_distinta()
    {
        var c = EnRevision(conDocumento: false);
        await TomadaPorAsync(Ana, c, sinDocumentos: true);
        _auditoria.Registros.Clear();

        var r = await Servicio(sinDocumentos: true).ResolverAsync(
            Verificar(Ana, c.Id, new string('x', 20)));

        Assert.True(r.Exito);
        Assert.Equal(EstadoCredencial.Verificada, c.Estado);
        Assert.Equal(Ana, c.SelloPor);
        var reg = Assert.Single(_auditoria.Registros);
        Assert.Equal("verificar_sin_documentos", reg.Accion);
        Assert.Equal(Ana, reg.ActorId);
        Assert.Equal(new string('x', 20), JsonDocument.Parse(reg.Contexto!).RootElement.GetProperty("detalle").GetString());
        Assert.True(JsonDocument.Parse(reg.Contexto!).RootElement.GetProperty("sinDocumentos").GetBoolean());
    }

    [Fact]
    public async Task Con_documentos_y_la_bandera_encendida_la_accion_sigue_siendo_la_normal_y_el_detalle_es_opcional()
    {
        var c = EnRevision(conDocumento: true);
        await TomadaPorAsync(Ana, c, sinDocumentos: true);
        _auditoria.Registros.Clear();

        var r = await Servicio(sinDocumentos: true).ResolverAsync(Verificar(Ana, c.Id));

        Assert.True(r.Exito);
        Assert.Equal("verificar_credencial", Assert.Single(_auditoria.Registros).Accion);
    }

    [Fact]
    public async Task Con_la_bandera_apagada_una_credencial_sin_respaldo_no_se_verifica_nunca()
    {
        var c = EnRevision(conDocumento: false);   // p. ej. quedo asi de cuando la bandera estaba encendida
        await TomadaPorAsync(Ana, c);

        var r = await Servicio(sinDocumentos: false).ResolverAsync(Verificar(Ana, c.Id, new string('x', 50)));

        Assert.False(r.Exito);
        Assert.Equal(Fallas.ReglaDeNegocio, r.Codigo);
        Assert.Equal(EstadoCredencial.EnRevision, c.Estado);
        Assert.Null(c.SelloPor);
        Assert.Empty(c.Verificaciones);
    }

    [Fact]
    public async Task Un_documento_que_no_esta_disponible_no_cuenta_como_respaldo()
    {
        var c = EnRevision(conDocumento: false);
        c.Documentos.Add(new DocumentoCredencial { Estado = EstadoDocumento.EnAnalisis });
        c.Documentos.Add(new DocumentoCredencial { Estado = EstadoDocumento.RechazadoSeguridad });
        await TomadaPorAsync(Ana, c);

        var r = await Servicio(sinDocumentos: false).ResolverAsync(Verificar(Ana, c.Id, new string('x', 50)));

        Assert.Equal(Fallas.ReglaDeNegocio, r.Codigo);
    }

    [Fact]
    public async Task Sin_documentos_si_se_puede_rechazar_y_pedir_antecedentes_con_la_bandera_apagada()
    {
        var a = EnRevision("A", conDocumento: false);
        var b = EnRevision("B", conDocumento: false);
        await TomadaPorAsync(Ana, a);
        await TomadaPorAsync(Ana, b);

        Assert.True((await Servicio().ResolverAsync(Rechazar(Ana, a.Id, MotivoRechazo.NoCorresponde, "No pudimos comprobarla"))).Exito);
        Assert.True((await Servicio().ResolverAsync(Pedir(Ana, b.Id, "Sube el documento original"))).Exito);
    }

    // ============================================================= API-27 · enviar a revision

    private Credencial Borrador(bool conDocumento = false, PerfilPrestador? perfil = null)
    {
        var c = new Credencial
        {
            PerfilPrestadorId = (perfil ?? _perfil).Id, Nombre = "Abogado", Tipo = TipoCredencial.Titulo,
            Estado = EstadoCredencial.Borrador, ObtenidaEn = new DateOnly(2020, 1, 1), CreadaEn = _reloj.Ahora,
        };
        if (conDocumento)
            c.Documentos.Add(new DocumentoCredencial { Estado = EstadoDocumento.Disponible });
        _base.Credenciales.Add(c);
        return c;
    }

    /// <summary>RN-S2-01 intacta: es el comportamiento por omision.</summary>
    [Fact]
    public async Task Con_la_bandera_apagada_enviar_sin_documentos_se_rechaza()
    {
        var c = Borrador();

        var r = await Credenciales(sinDocumentos: false).EnviarARevisionAsync(_perfil.Id, c.Id);

        Assert.False(r.Exito);
        Assert.Equal(Fallas.ReglaDeNegocio, r.Codigo);
        Assert.Equal(EstadoCredencial.Borrador, c.Estado);
        Assert.Empty(_auditoria.Registros);
        Assert.Equal(0, _base.Guardados);
    }

    [Fact]
    public async Task El_servicio_sin_opciones_se_comporta_como_bandera_apagada()
    {
        var c = Borrador();
        var servicio = new ServicioCredenciales(new RepositorioCredencialesEnMemoria(_base), _base, _reloj, _auditoria);

        Assert.False((await servicio.EnviarARevisionAsync(_perfil.Id, c.Id)).Exito);
    }

    [Fact]
    public async Task Con_la_bandera_apagada_un_documento_disponible_permite_enviar()
    {
        var c = Borrador(conDocumento: true);

        var r = await Credenciales(sinDocumentos: false).EnviarARevisionAsync(_perfil.Id, c.Id);

        Assert.True(r.Exito);
        Assert.Equal(EstadoCredencial.EnRevision, c.Estado);
    }

    [Fact]
    public async Task Con_la_bandera_apagada_un_documento_en_analisis_no_permite_enviar()
    {
        var c = Borrador();
        c.Documentos.Add(new DocumentoCredencial { Estado = EstadoDocumento.EnAnalisis });

        var r = await Credenciales(sinDocumentos: false).EnviarARevisionAsync(_perfil.Id, c.Id);

        Assert.Equal(Fallas.ReglaDeNegocio, r.Codigo);
    }

    [Fact]
    public async Task Con_la_bandera_encendida_se_envia_sin_documentos_y_queda_en_la_cola_marcada()
    {
        var c = Borrador();

        var r = await Credenciales(sinDocumentos: true).EnviarARevisionAsync(_perfil.Id, c.Id);

        Assert.True(r.Exito);
        Assert.Equal(EstadoCredencial.EnRevision, c.Estado);
        var reg = Assert.Single(_auditoria.Registros);
        Assert.Equal("enviar_credencial_a_revision", reg.Accion);
        Assert.True(JsonDocument.Parse(reg.Contexto!).RootElement.GetProperty("sinDocumentos").GetBoolean());
        var enCola = Assert.Single(await Servicio().ColaAsync(Ana));
        Assert.True(enCola.SinDocumentos);
        Assert.Equal(1, _base.Guardados);
    }

    [Fact]
    public async Task Enviar_la_de_otra_persona_responde_igual_que_una_inexistente()
    {
        var ajena = Borrador(perfil: _perfilDeAna);

        var deOtro = await Credenciales(true).EnviarARevisionAsync(_perfil.Id, ajena.Id);
        var inexistente = await Credenciales(true).EnviarARevisionAsync(_perfil.Id, Guid.NewGuid());

        Assert.Equal(Fallas.NoEncontrado, deOtro.Codigo);
        Assert.Equal(inexistente.Codigo, deOtro.Codigo);
        Assert.Equal(inexistente.Mensaje, deOtro.Mensaje);
        Assert.Equal(EstadoCredencial.Borrador, ajena.Estado);
    }

    [Theory]
    [InlineData(EstadoCredencial.EnRevision)]
    [InlineData(EstadoCredencial.Verificada)]
    public async Task No_se_envia_lo_que_ya_esta_en_revision_o_verificado(EstadoCredencial estado)
    {
        var c = Borrador(conDocumento: true);
        c.Estado = estado;

        var r = await Credenciales(true).EnviarARevisionAsync(_perfil.Id, c.Id);

        Assert.Equal(Fallas.Conflicto, r.Codigo);
        Assert.Equal(estado, c.Estado);
    }

    [Theory]
    [InlineData(EstadoCredencial.Rechazada)]
    [InlineData(EstadoCredencial.Vencida)]
    public async Task Una_rechazada_o_vencida_puede_reenviarse(EstadoCredencial estado)
    {
        var c = Borrador();
        c.Estado = estado;

        Assert.True((await Credenciales(true).EnviarARevisionAsync(_perfil.Id, c.Id)).Exito);
        Assert.Equal(EstadoCredencial.EnRevision, c.Estado);
    }

    [Fact]
    public async Task Responder_un_pedido_de_antecedentes_queda_auditado_con_su_propia_accion()
    {
        var c = Borrador();
        c.Estado = EstadoCredencial.EnRevision;
        c.EsperandoPrestador = true;

        await Credenciales(true).EnviarARevisionAsync(_perfil.Id, c.Id);

        Assert.Equal("responder_antecedentes", Assert.Single(_auditoria.Registros).Accion);
    }
}
