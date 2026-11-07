using Zass.Aplicacion.Credenciales;
using Zass.Aplicacion.Pruebas.Dobles;
using Zass.Dominio.Comun;
using Zass.Dominio.Credenciales;

namespace Zass.Aplicacion.Pruebas.Credenciales;

/// <summary>MOD-02 - Credenciales (CU-05 pasos 1 a 5, flujo 4F y lo que ve el público), sin base de datos.</summary>
public class CredencialesPruebas
{
    private static readonly Guid Yo = Guid.Parse("00000000-0000-0000-0000-00000000000a");
    private static readonly Guid Otra = Guid.Parse("00000000-0000-0000-0000-00000000000b");

    private readonly BaseEnMemoria _base = new();
    private readonly AuditoriaEnMemoria _auditoria = new();
    private readonly RelojFijo _reloj = new(new DateTime(2026, 11, 7, 12, 0, 0, DateTimeKind.Utc));
    private readonly ServicioCredenciales _servicio;

    public CredencialesPruebas()
    {
        _base.Entidades.Add(new EntidadCertificadora { Id = 1, Nombre = "Universidad de La Frontera", Catalogada = true, Activa = true });
        _base.Entidades.Add(new EntidadCertificadora { Id = 2, Nombre = "Entidad apagada", Catalogada = true, Activa = false });
        _servicio = new ServicioCredenciales(
            new RepositorioCredencialesEnMemoria(_base), _base, _reloj, _auditoria);
    }

    private static readonly DateOnly Hoy = new(2026, 11, 7);

    private CrearCredencialCmd Cmd(
        string nombre = "Abogado", Guid? perfil = null, short? entidadId = null, string? entidadNombre = null,
        DateOnly? obtenida = null, DateOnly? vence = null, TipoCredencial tipo = TipoCredencial.Titulo) =>
        new(perfil ?? Yo, tipo, nombre, entidadId, entidadNombre, obtenida ?? new DateOnly(2020, 3, 15), vence);

    private Credencial Existente(Guid perfil, EstadoCredencial estado, string nombre = "Algo", DateOnly? obtenida = null)
    {
        var c = new Credencial
        {
            PerfilPrestadorId = perfil, Nombre = nombre, Estado = estado,
            ObtenidaEn = obtenida ?? new DateOnly(2020, 1, 1), CreadaEn = _reloj.Ahora,
        };
        _base.Credenciales.Add(c);
        return c;
    }

    // ------------------------------------------------------------- crear

    [Fact]
    public async Task Crear_deja_una_credencial_en_borrador_con_el_nombre_recortado()
    {
        var r = await _servicio.CrearAsync(Cmd("  Abogado  ", entidadId: 1));

        Assert.True(r.Exito);
        var c = Assert.Single(_base.Credenciales);
        Assert.Equal(r.Valor, c.Id);
        Assert.Equal("Abogado", c.Nombre);
        Assert.Equal(EstadoCredencial.Borrador, c.Estado);
        Assert.Equal(Yo, c.PerfilPrestadorId);
        Assert.Equal("Universidad de La Frontera", c.EntidadCertificadora!.Nombre);
        Assert.Equal(_reloj.Ahora, c.CreadaEn);
        Assert.Equal(1, _base.Guardados);
    }

    [Theory]
    [InlineData(2, false)]
    [InlineData(3, true)]
    [InlineData(150, true)]
    [InlineData(151, false)]
    public async Task El_nombre_tiene_entre_3_y_150_caracteres(int largo, bool valido)
    {
        var r = await _servicio.CrearAsync(Cmd(new string('a', largo)));

        Assert.Equal(valido, r.Exito);
        if (!valido) Assert.Equal(Fallas.Validacion, r.Codigo);
    }

    [Fact]
    public async Task Un_nombre_de_solo_espacios_no_cuenta_como_nombre()
    {
        var r = await _servicio.CrearAsync(Cmd("      "));
        Assert.False(r.Exito);
    }

    [Fact]
    public async Task La_fecha_de_obtencion_no_puede_ser_futura_pero_hoy_si()
    {
        Assert.True((await _servicio.CrearAsync(Cmd(obtenida: Hoy))).Exito);

        var manana = await _servicio.CrearAsync(Cmd(obtenida: Hoy.AddDays(1)));
        Assert.False(manana.Exito);
        Assert.Equal(Fallas.Validacion, manana.Codigo);
        Assert.Contains("futura", manana.Mensaje);
    }

    [Fact]
    public async Task El_vencimiento_tiene_que_ser_posterior_a_la_obtencion()
    {
        var obtenida = new DateOnly(2020, 3, 15);

        Assert.False((await _servicio.CrearAsync(Cmd(obtenida: obtenida, vence: obtenida))).Exito);
        Assert.False((await _servicio.CrearAsync(Cmd(obtenida: obtenida, vence: obtenida.AddDays(-1)))).Exito);
        Assert.True((await _servicio.CrearAsync(Cmd(obtenida: obtenida, vence: obtenida.AddDays(1)))).Exito);
    }

    [Fact]
    public async Task Un_tipo_que_no_existe_se_rechaza()
    {
        var r = await _servicio.CrearAsync(Cmd(tipo: (TipoCredencial)99));
        Assert.False(r.Exito);
        Assert.Equal(Fallas.Validacion, r.Codigo);
    }

    [Fact]
    public async Task No_se_puede_elegir_una_entidad_y_ademas_escribir_otra()
    {
        var r = await _servicio.CrearAsync(Cmd(entidadId: 1, entidadNombre: "Otra universidad"));

        Assert.False(r.Exito);
        Assert.Empty(_base.Entidades.Where(e => e.Nombre == "Otra universidad"));
    }

    [Theory]
    [InlineData(99)]   // no existe
    [InlineData(2)]    // existe pero esta apagada
    public async Task Una_entidad_inexistente_o_apagada_se_rechaza(short id)
    {
        var r = await _servicio.CrearAsync(Cmd(entidadId: id));

        Assert.False(r.Exito);
        Assert.Equal(Fallas.Validacion, r.Codigo);
        Assert.Empty(_base.Credenciales);
    }

    [Fact]
    public async Task Una_entidad_escrita_a_mano_queda_como_no_catalogada()
    {
        var r = await _servicio.CrearAsync(Cmd(entidadNombre: "  Instituto del Sur  "));

        Assert.True(r.Exito);
        var entidad = _base.Entidades.Single(e => e.Nombre == "Instituto del Sur");
        Assert.False(entidad.Catalogada);
        Assert.Same(entidad, _base.Credenciales.Single().EntidadCertificadora);
    }

    [Fact]
    public async Task Una_entidad_escrita_que_ya_existe_se_reutiliza_sin_distinguir_mayusculas()
    {
        var antes = _base.Entidades.Count;

        var r = await _servicio.CrearAsync(Cmd(entidadNombre: "universidad de la frontera"));

        Assert.True(r.Exito);
        Assert.Equal(antes, _base.Entidades.Count);
        Assert.Equal(1, _base.Credenciales.Single().EntidadCertificadora!.Id);
    }

    [Fact]
    public async Task Una_entidad_escrita_que_coincide_con_una_apagada_se_rechaza()
    {
        var r = await _servicio.CrearAsync(Cmd(entidadNombre: "ENTIDAD APAGADA"));
        Assert.False(r.Exito);
    }

    [Theory]
    [InlineData("ab")]
    public async Task El_nombre_de_una_entidad_nueva_no_puede_ser_demasiado_corto(string nombre)
    {
        Assert.False((await _servicio.CrearAsync(Cmd(entidadNombre: nombre))).Exito);
        Assert.False((await _servicio.CrearAsync(Cmd(entidadNombre: new string('x', 201)))).Exito);
    }

    [Fact]
    public async Task Una_credencial_sin_entidad_es_valida()
    {
        Assert.True((await _servicio.CrearAsync(Cmd())).Exito);
        Assert.Null(_base.Credenciales.Single().EntidadCertificadora);
    }

    [Fact]
    public async Task Hay_un_tope_de_credenciales_por_prestador_y_no_cuenta_las_de_otro()
    {
        for (var i = 0; i < ServicioCredenciales.MaximoPorPrestador - 1; i++)
            Existente(Yo, EstadoCredencial.Borrador);
        for (var i = 0; i < 40; i++) Existente(Otra, EstadoCredencial.Borrador);

        Assert.True((await _servicio.CrearAsync(Cmd())).Exito);          // la 25
        var r = await _servicio.CrearAsync(Cmd());                        // la 26
        Assert.False(r.Exito);
        Assert.Equal(Fallas.ReglaDeNegocio, r.Codigo);
    }

    [Fact]
    public async Task Lo_rechazado_no_guarda_nada_ni_crea_la_entidad()
    {
        await _servicio.CrearAsync(Cmd(entidadNombre: "Nueva entidad", obtenida: Hoy.AddDays(5)));

        Assert.Empty(_base.Credenciales);
        Assert.DoesNotContain(_base.Entidades, e => e.Nombre == "Nueva entidad");
        Assert.Equal(0, _base.Guardados);
        Assert.Empty(_auditoria.Registros);
    }

    [Fact]
    public async Task Crear_queda_en_la_auditoria_sin_el_contenido_de_la_credencial()
    {
        var r = await _servicio.CrearAsync(Cmd("Título secreto de prueba", entidadNombre: "Entidad privada"));

        var registro = Assert.Single(_auditoria.Registros);
        Assert.Equal("crear_credencial", registro.Accion);
        Assert.Equal(r.Valor, registro.EntidadId);
        Assert.DoesNotContain("secreto", registro.Contexto ?? "");
        Assert.DoesNotContain("privada", registro.Contexto ?? "");
    }

    // ------------------------------------------------------------- eliminar

    [Fact]
    public async Task Eliminar_borra_la_propia_y_deja_constancia_de_si_tenia_sello()
    {
        var c = Existente(Yo, EstadoCredencial.Verificada);

        var r = await _servicio.EliminarAsync(Yo, c.Id);

        Assert.True(r.Exito);
        Assert.Empty(_base.Credenciales);
        var registro = Assert.Single(_auditoria.Registros);
        Assert.Equal("eliminar_credencial", registro.Accion);
        Assert.Contains("true", registro.Contexto!);
        Assert.Equal(1, _base.Guardados);
    }

    [Fact]
    public async Task Eliminar_la_de_otra_persona_responde_igual_que_una_que_no_existe_y_no_borra()
    {
        var ajena = Existente(Otra, EstadoCredencial.Borrador);

        var deAjena = await _servicio.EliminarAsync(Yo, ajena.Id);
        var inexistente = await _servicio.EliminarAsync(Yo, Guid.NewGuid());

        Assert.False(deAjena.Exito);
        Assert.Equal(Fallas.NoEncontrado, deAjena.Codigo);
        Assert.Equal(inexistente.Codigo, deAjena.Codigo);
        Assert.Equal(inexistente.Mensaje, deAjena.Mensaje);
        Assert.Single(_base.Credenciales);
        Assert.Equal(0, _base.Guardados);
    }

    // ------------------------------------------------------------- listar las propias

    [Fact]
    public async Task Mis_credenciales_son_solo_las_mias_con_su_estado_interno_y_su_respaldo()
    {
        var conArchivo = Existente(Yo, EstadoCredencial.EnRevision, "Con archivo");
        conArchivo.Documentos.Add(new DocumentoCredencial { Estado = EstadoDocumento.Disponible });
        Existente(Yo, EstadoCredencial.Rechazada, "Rechazada");
        Existente(Otra, EstadoCredencial.Verificada, "De otra");

        var mias = await _servicio.MisCredencialesAsync(Yo);

        Assert.Equal(2, mias.Count);
        Assert.DoesNotContain(mias, m => m.Nombre == "De otra");
        var primera = mias.Single(m => m.Nombre == "Con archivo");
        Assert.Equal(EstadoCredencial.EnRevision, primera.Estado);
        Assert.True(primera.TieneRespaldo);
        Assert.Equal(1, primera.Documentos);
        Assert.Equal(EstadoCredencial.Rechazada, mias.Single(m => m.Nombre == "Rechazada").Estado);
        Assert.False(mias.Single(m => m.Nombre == "Rechazada").TieneRespaldo);
    }

    [Fact]
    public async Task Mis_credenciales_dice_si_la_entidad_esta_reconocida()
    {
        await _servicio.CrearAsync(Cmd("Del catalogo", entidadId: 1));
        await _servicio.CrearAsync(Cmd("Escrita", entidadNombre: "Escuela nueva"));

        var mias = await _servicio.MisCredencialesAsync(Yo);

        Assert.True(mias.Single(m => m.Nombre == "Del catalogo").EntidadReconocida);
        Assert.False(mias.Single(m => m.Nombre == "Escrita").EntidadReconocida);
    }

    // ------------------------------------------------------------- lo que ve el publico

    [Fact]
    public async Task El_publico_no_ve_borradores()
    {
        Existente(Yo, EstadoCredencial.Borrador, "Borrador");
        Existente(Yo, EstadoCredencial.EnRevision, "En revision");

        var publicas = await _servicio.CredencialesPublicasAsync(Yo);

        Assert.Equal(new[] { "En revision" }, publicas.Select(p => p.Nombre).ToArray());
    }

    /// <summary>
    /// El público no distingue "en revisión", "rechazada" ni "vencida": las tres son "Declarada por
    /// el usuario". Que se vea "rechazada" le diría a cualquiera algo que el prestador no eligió contar.
    /// </summary>
    [Theory]
    [InlineData(EstadoCredencial.EnRevision)]
    [InlineData(EstadoCredencial.Rechazada)]
    [InlineData(EstadoCredencial.Vencida)]
    public async Task Todo_lo_que_no_tiene_sello_se_ve_como_declarada_por_el_usuario(EstadoCredencial estado)
    {
        Existente(Yo, estado);

        var p = Assert.Single(await _servicio.CredencialesPublicasAsync(Yo));

        Assert.Equal("Declarada por el usuario", p.Etiqueta);
        Assert.False(p.Verificada);
    }

    [Fact]
    public async Task Las_verificadas_van_primero_y_se_ven_como_verificada()
    {
        Existente(Yo, EstadoCredencial.EnRevision, "Declarada nueva", new DateOnly(2024, 1, 1));
        Existente(Yo, EstadoCredencial.Verificada, "Verificada vieja", new DateOnly(2010, 1, 1));

        var publicas = await _servicio.CredencialesPublicasAsync(Yo);

        Assert.Equal(new[] { "Verificada vieja", "Declarada nueva" }, publicas.Select(p => p.Nombre).ToArray());
        Assert.Equal("Verificada", publicas[0].Etiqueta);
        Assert.True(publicas[0].Verificada);
    }

    [Fact]
    public async Task El_publico_solo_ve_las_de_ese_prestador()
    {
        Existente(Otra, EstadoCredencial.Verificada, "De otra");

        Assert.Empty(await _servicio.CredencialesPublicasAsync(Yo));
    }

    [Fact]
    public void El_dato_publico_no_tiene_estado_interno_ni_archivos_ni_id()
    {
        var campos = typeof(CredencialPublicaDto).GetProperties().Select(p => p.Name).ToArray();

        foreach (var prohibido in new[] { "Estado", "Documentos", "Id", "SelloPor", "Reservada", "Perfil" })
            Assert.DoesNotContain(campos, c => c.Contains(prohibido, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Tiene_verificadas_solo_si_alguna_tiene_sello()
    {
        Existente(Yo, EstadoCredencial.EnRevision);
        Assert.False(await _servicio.TieneVerificadasAsync(Yo));

        Existente(Yo, EstadoCredencial.Verificada);
        Assert.True(await _servicio.TieneVerificadasAsync(Yo));
        Assert.False(await _servicio.TieneVerificadasAsync(Otra));
    }
}
