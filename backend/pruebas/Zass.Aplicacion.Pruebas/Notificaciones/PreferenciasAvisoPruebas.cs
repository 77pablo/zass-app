using Zass.Aplicacion.Notificaciones;
using Zass.Aplicacion.Pruebas.Dobles;
using Zass.Aplicacion.Puertos;
using Zass.Dominio.Comun;

namespace Zass.Aplicacion.Pruebas.Notificaciones;

/// <summary>ES-3b · API-43 (CS-14). Grupos, correo fijo, la regla de salida y el validador, sin base.</summary>
public class PreferenciasAvisoPruebas
{
    // ------------------------------------------------------------------ grupos y tipos

    [Fact]
    public void Cada_tipo_con_push_esta_en_un_solo_grupo_y_ningun_otro_tipo_aparece()
    {
        var enGrupos = PreferenciasAviso.Grupos.SelectMany(g => g.Tipos).ToList();
        Assert.Equal(enGrupos.Count, enGrupos.Distinct().Count());
        var conPush = Enum.GetValues<TipoNotificacion>().Where(AvisosPush.AdmitePush).OrderBy(t => t);
        Assert.Equal(conPush, enGrupos.OrderBy(t => t));
    }

    [Fact]
    public void Los_grupos_van_en_el_orden_de_la_pantalla() =>
        Assert.Equal(new[] { "citas", "recordatorios", "resenas", "perfil", "cobros" },
            PreferenciasAviso.Grupos.Select(g => g.Clave));

    [Theory]
    [InlineData(TipoNotificacion.CorreoVerificacion)]
    [InlineData(TipoNotificacion.RecuperarContrasena)]
    [InlineData(TipoNotificacion.RegistroConRutExistente)]
    public void Los_avisos_con_token_no_se_configuran(TipoNotificacion tipo) =>
        Assert.False(PreferenciasAviso.Configurable(tipo));

    [Fact]
    public void El_correo_es_fijo_en_cobro_plan_tarjeta_y_los_avisos_de_seguridad()
    {
        var fijos = Enum.GetValues<TipoNotificacion>().Where(PreferenciasAviso.CorreoFijo).OrderBy(t => t);
        Assert.Equal(new[]
        {
            TipoNotificacion.CorreoVerificacion, TipoNotificacion.RecuperarContrasena, TipoNotificacion.TarjetaActivada,
            TipoNotificacion.SuscripcionPorVencer, TipoNotificacion.SuscripcionVencida, TipoNotificacion.CobroRechazado,
            TipoNotificacion.RegistroConRutExistente,
        }.OrderBy(t => t), fijos);
    }

    [Fact]
    public void Todo_el_grupo_cobros_tiene_el_correo_fijo_y_ningun_otro_grupo_lo_tiene()
    {
        foreach (var (clave, tipos) in PreferenciasAviso.Grupos)
            Assert.All(tipos, t => Assert.Equal(clave == "cobros", PreferenciasAviso.CorreoFijo(t)));
    }

    // ------------------------------------------------------------------ la regla al enviar

    private static PreferenciaAviso Pref(TipoNotificacion t, bool correo, bool push) =>
        new() { Tipo = t, Correo = correo, Push = push };

    [Theory]
    [InlineData(CanalNotificacion.Correo)]
    [InlineData(CanalNotificacion.Push)]
    public void Sin_fila_sale_por_los_dos_canales(CanalNotificacion canal) =>
        Assert.True(PreferenciasAviso.Sale(TipoNotificacion.CitaConfirmada, canal, null));

    [Fact]
    public void Con_fila_cada_canal_sigue_lo_guardado()
    {
        var t = TipoNotificacion.ResenaNueva;
        Assert.False(PreferenciasAviso.Sale(t, CanalNotificacion.Correo, Pref(t, false, true)));
        Assert.True(PreferenciasAviso.Sale(t, CanalNotificacion.Push, Pref(t, false, true)));
        Assert.True(PreferenciasAviso.Sale(t, CanalNotificacion.Correo, Pref(t, true, false)));
        Assert.False(PreferenciasAviso.Sale(t, CanalNotificacion.Push, Pref(t, true, false)));
    }

    [Fact]
    public void Una_fila_vieja_no_apaga_el_correo_fijo_pero_si_el_push()
    {
        var t = TipoNotificacion.CobroRechazado;
        Assert.True(PreferenciasAviso.Sale(t, CanalNotificacion.Correo, Pref(t, false, false)));
        Assert.False(PreferenciasAviso.Sale(t, CanalNotificacion.Push, Pref(t, false, false)));
    }

    // ------------------------------------------------------------------ el validador

    private static GuardarPreferenciasCmd Cmd(params PreferenciaTipoCmd[] p) => new(p);
    private static PreferenciaTipoCmd P(TipoNotificacion? t, bool? correo, bool? push) => new(t, correo, push);
    private static readonly ValidadorGuardarPreferencias V = new();

    private static IEnumerable<string> Errores(GuardarPreferenciasCmd c) =>
        V.Validate(c).Errors.Select(e => e.ErrorMessage);

    [Fact]
    public void El_validador_acepta_apagar_el_push_y_el_correo_de_un_tipo_comun() =>
        Assert.True(V.Validate(Cmd(P(TipoNotificacion.CitaSolicitada, false, false))).IsValid);

    [Fact]
    public void El_validador_acepta_apagar_el_push_de_un_cobro() =>
        Assert.True(V.Validate(Cmd(P(TipoNotificacion.CobroRechazado, true, false))).IsValid);

    [Theory]
    [InlineData(TipoNotificacion.CobroRechazado)]
    [InlineData(TipoNotificacion.SuscripcionPorVencer)]
    [InlineData(TipoNotificacion.SuscripcionVencida)]
    [InlineData(TipoNotificacion.TarjetaActivada)]
    public void El_validador_no_deja_apagar_el_correo_fijo(TipoNotificacion tipo) =>
        Assert.Contains(ValidadorGuardarPreferencias.CorreoObligatorio, Errores(Cmd(P(tipo, false, true))));

    [Fact]
    public void El_validador_rechaza_la_lista_vacia_o_nula()
    {
        Assert.Contains(ValidadorGuardarPreferencias.SinCambios, Errores(Cmd()));
        Assert.Contains(ValidadorGuardarPreferencias.SinCambios, Errores(new GuardarPreferenciasCmd(null)));
    }

    [Theory]
    [InlineData(TipoNotificacion.CorreoVerificacion)]
    [InlineData(TipoNotificacion.RecuperarContrasena)]
    public void El_validador_rechaza_los_tipos_que_no_se_configuran(TipoNotificacion tipo) =>
        Assert.Contains(ValidadorGuardarPreferencias.TipoNoConfigurable, Errores(Cmd(P(tipo, true, true))));

    [Fact]
    public void El_validador_rechaza_tipo_nulo_o_fuera_del_enum()
    {
        Assert.Contains(ValidadorGuardarPreferencias.TipoNoConfigurable, Errores(Cmd(P(null, true, true))));
        Assert.Contains(ValidadorGuardarPreferencias.TipoNoConfigurable,
            Errores(Cmd(P((TipoNotificacion)999, true, true))));
    }

    [Fact]
    public void El_validador_rechaza_un_tipo_repetido() =>
        Assert.Contains(ValidadorGuardarPreferencias.TipoRepetido, Errores(Cmd(
            P(TipoNotificacion.CitaSolicitada, true, false), P(TipoNotificacion.CitaSolicitada, false, true))));

    [Fact]
    public void El_validador_rechaza_mas_filas_que_tipos()
    {
        var muchas = Enumerable.Range(0, AvisosPush.TiposConPush.Length + 1)
            .Select(_ => new PreferenciaTipoCmd(TipoNotificacion.CitaSolicitada, true, true)).ToArray();
        Assert.Contains(ValidadorGuardarPreferencias.TipoRepetido, Errores(Cmd(muchas)));
    }

    [Fact]
    public void El_validador_acepta_todos_los_tipos_configurables_de_una_vez()
    {
        var todos = Enum.GetValues<TipoNotificacion>().Where(PreferenciasAviso.Configurable)
            .Select(t => new PreferenciaTipoCmd(t, true, false)).ToArray();
        Assert.True(V.Validate(Cmd(todos)).IsValid);
    }

    [Fact]
    public void El_validador_rechaza_una_fila_nula_sin_lanzar()
    {
        // `"preferencias": [null]`: sin la regla NotNull llegaba al servicio y era un 500.
        var cmd = new GuardarPreferenciasCmd(new PreferenciaTipoCmd[] { null! });
        Assert.Contains(ValidadorGuardarPreferencias.TipoNoConfigurable, Errores(cmd));
        var mezcla = new GuardarPreferenciasCmd(new[] { P(TipoNotificacion.ResenaNueva, true, false), null! });
        Assert.False(V.Validate(mezcla).IsValid);
    }

    [Theory]
    [InlineData(TipoNotificacion.CitaConfirmada, CanalNotificacion.Correo, true)]
    [InlineData(TipoNotificacion.CitaConfirmada, CanalNotificacion.Push, true)]
    [InlineData(TipoNotificacion.CobroRechazado, CanalNotificacion.Correo, false)]
    [InlineData(TipoNotificacion.CobroRechazado, CanalNotificacion.Push, true)]
    [InlineData(TipoNotificacion.CorreoVerificacion, CanalNotificacion.Correo, false)]
    [InlineData(TipoNotificacion.RecuperarContrasena, CanalNotificacion.Correo, false)]
    [InlineData(TipoNotificacion.CorreoVerificacion, CanalNotificacion.Push, false)]
    public void Solo_se_consulta_la_base_cuando_la_preferencia_puede_cambiar_algo(
        TipoNotificacion tipo, CanalNotificacion canal, bool consulta) =>
        Assert.Equal(consulta, PreferenciasAviso.DependeDeLaPreferencia(tipo, canal));

    [Fact]
    public void El_validador_exige_los_dos_canales()
    {
        Assert.Contains(ValidadorGuardarPreferencias.FaltaCanal, Errores(Cmd(P(TipoNotificacion.ResenaNueva, null, true))));
        Assert.Contains(ValidadorGuardarPreferencias.FaltaCanal, Errores(Cmd(P(TipoNotificacion.ResenaNueva, true, null))));
    }

    // ------------------------------------------------------------------ el servicio

    private sealed class RepoFalso : IRepositorioPreferenciasAviso
    {
        public List<string> Llamadas { get; } = new();
        public List<PreferenciaAviso> Filas { get; } = new();

        public Task<List<PreferenciaAviso>> DeAsync(Guid u, CancellationToken ct = default)
        {
            Llamadas.Add("leer");
            return Task.FromResult(Filas.ToList());
        }

        public Task FijarAsync(Guid u, IReadOnlyCollection<CambioDePreferencia> cambios, DateTime ahora, CancellationToken ct = default)
        {
            foreach (var (t, correo, push) in cambios)
            {
                Llamadas.Add($"fijar {t} {correo} {push}");
                Filas.RemoveAll(f => f.Tipo == t);
                if (!(correo && push)) Filas.Add(new PreferenciaAviso { Tipo = t, Correo = correo, Push = push });
            }
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task Ver_sin_filas_lista_los_quince_tipos_encendidos_en_orden_de_grupo()
    {
        var repo = new RepoFalso();
        var dto = await new ServicioPreferenciasAviso(repo, new UnidadQueAnota(repo.Llamadas), new RelojFijo()).VerAsync(Guid.NewGuid());

        Assert.Equal(AvisosPush.TiposConPush.Length, dto.Preferencias.Count);
        Assert.All(dto.Preferencias, p => Assert.True(p.Correo && p.Push));
        Assert.Equal(PreferenciasAviso.Grupos.SelectMany(g => g.Tipos.Select(t => t.ToString())),
            dto.Preferencias.Select(p => p.Tipo));
        var cobro = dto.Preferencias.Single(p => p.Tipo == nameof(TipoNotificacion.CobroRechazado));
        Assert.Equal("cobros", cobro.Grupo);
        Assert.True(cobro.CorreoFijo);
        Assert.False(dto.Preferencias.Single(p => p.Tipo == nameof(TipoNotificacion.CitaSolicitada)).CorreoFijo);
    }

    [Fact]
    public async Task Ver_refleja_lo_guardado_y_una_fila_vieja_no_muestra_el_correo_fijo_apagado()
    {
        var repo = new RepoFalso();
        repo.Filas.Add(new PreferenciaAviso { Tipo = TipoNotificacion.ResenaNueva, Correo = false, Push = true });
        repo.Filas.Add(new PreferenciaAviso { Tipo = TipoNotificacion.CobroRechazado, Correo = false, Push = false });

        var dto = await new ServicioPreferenciasAviso(repo, new UnidadQueAnota(repo.Llamadas), new RelojFijo()).VerAsync(Guid.NewGuid());

        var resena = dto.Preferencias.Single(p => p.Tipo == nameof(TipoNotificacion.ResenaNueva));
        Assert.False(resena.Correo);
        Assert.True(resena.Push);
        var cobro = dto.Preferencias.Single(p => p.Tipo == nameof(TipoNotificacion.CobroRechazado));
        Assert.True(cobro.Correo);
        Assert.False(cobro.Push);
    }

    [Fact]
    public async Task Guardar_fija_cada_tipo_guarda_una_vez_y_devuelve_como_quedo()
    {
        var repo = new RepoFalso();
        var servicio = new ServicioPreferenciasAviso(repo, new UnidadQueAnota(repo.Llamadas), new RelojFijo());

        var r = await servicio.GuardarAsync(Guid.NewGuid(), Cmd(
            P(TipoNotificacion.CitaSolicitada, true, false), P(TipoNotificacion.CitaConfirmada, true, false)));

        Assert.True(r.Exito);
        // Los dos tipos en UNA transaccion (todo o nada), y se lee como quedo despues de cerrarla.
        Assert.Equal(new[] { "transaccion", "fijar CitaSolicitada True False", "fijar CitaConfirmada True False",
            "fin transaccion", "leer" }, repo.Llamadas);
        Assert.False(r.Valor!.Preferencias.Single(p => p.Tipo == "CitaSolicitada").Push);
        Assert.True(r.Valor.Preferencias.Single(p => p.Tipo == "CitaRechazada").Push);
    }
}
