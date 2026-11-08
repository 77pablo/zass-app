using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Zass.Aplicacion.Identidad;
using Zass.Aplicacion.Puertos;
using Zass.Aplicacion.Resenas;
using Zass.Dominio.Agenda;
using Zass.Dominio.Catalogo;
using Zass.Dominio.Comun;
using Zass.Dominio.Identidad;
using Zass.Dominio.Reputacion;
using Zass.Infraestructura.Configuracion;
using Zass.Infraestructura.Datos;
using Zass.Infraestructura.Datos.Repositorios;
using Zass.Infraestructura.Seguridad;

namespace Zass.Integracion.Pruebas;

/// <summary>El reloj de las pruebas de integración: se mueve a mano, siempre en UTC (Npgsql lo exige).</summary>
public sealed class RelojDePruebaResenas : IRelojSistema
{
    public DateTime Ahora { get; private set; } = DateTime.UtcNow;
    public void Avanzar(TimeSpan cuanto) => Ahora = Ahora.Add(cuanto);
}

/// <summary>Lo que comparten las pruebas de MOD-11 contra la base real.</summary>
internal static class AyudasResenas
{
    public const string Comentario = "Excelente atención, llegaron puntuales y explicaron todo con claridad.";

    public static ServicioResenas Servicio(
        ZassDbContext ctx, IRelojSistema reloj, IUnidadDeTrabajo? trabajo = null)
    {
        var uow = trabajo ?? new UnidadDeTrabajoEfCore(ctx);
        var identidad = new ServicioIdentidad(
            new RepositorioUsuariosEfCore(ctx), new RepositorioPerfilesEfCore(ctx));
        return new ServicioResenas(
            new RepositorioResenasEfCore(ctx), identidad, uow, reloj,
            new AuditorEfCore(ctx), new ColaNotificacionesEfCore(ctx));
    }

    public static Usuario NuevoUsuario(ZassDbContext ctx, string rol = "buscador", bool verificado = true)
    {
        var u = new Usuario
        {
            Correo = $"res-{Guid.NewGuid():N}@zass.local", HashContrasena = "x", Nombre = "Persona de prueba",
            CorreoVerificado = verificado,
            Estado = verificado ? EstadoCuenta.Activa : EstadoCuenta.PendienteVerificacion,
        };
        ctx.Usuarios.Add(u);
        ctx.UsuarioRoles.Add(new UsuarioRol
        {
            UsuarioId = u.Id, RolId = ctx.Roles.AsNoTracking().Single(r => r.Codigo == rol).Id,
        });
        return u;
    }

    public static void DarRol(ZassDbContext ctx, Usuario u, string rol) =>
        ctx.UsuarioRoles.Add(new UsuarioRol
        {
            UsuarioId = u.Id, RolId = ctx.Roles.AsNoTracking().Single(r => r.Codigo == rol).Id,
        });

    public static PerfilPrestador NuevoPerfil(
        ZassDbContext ctx, Usuario dueno, EstadoPerfil estado = EstadoPerfil.Publicado)
    {
        var p = new PerfilPrestador
        {
            UsuarioId = dueno.Id, NombrePublico = $"Estudio {Guid.NewGuid():N}"[..20],
            Slug = $"estudio-{Guid.NewGuid():N}", RubroId = 1, Estado = estado,
            PublicadoEn = estado == EstadoPerfil.Publicado ? DateTime.UtcNow : null,
        };
        ctx.PerfilesPrestador.Add(p);
        return p;
    }

    public static Contacto NuevoContacto(
        ZassDbContext ctx, Usuario buscador, PerfilPrestador perfil, TimeSpan? hace = null, Guid? citaId = null)
    {
        var c = new Contacto
        {
            BuscadorId = buscador.Id, PerfilPrestadorId = perfil.Id, CitaId = citaId,
            Origen = citaId is null ? OrigenContacto.Ficha : OrigenContacto.Cita,
            Canal = CanalContacto.Whatsapp, OcurridoEn = DateTime.UtcNow - (hace ?? TimeSpan.FromDays(8)),
        };
        ctx.Contactos.Add(c);
        return c;
    }

    public static PublicarResenaCmd Pub(
        Usuario autor, Contacto c, int cal = 5, string? texto = null, string? clave = null,
        bool enviarIgual = false) =>
        new(autor.Id, c.Id, cal, texto ?? Comentario, null, enviarIgual, clave ?? Guid.NewGuid().ToString());

    /// <summary>
    /// El control nocturno de CC-07, hecho a mano: recalcula desde TBL-22 y compara con lo guardado en
    /// TBL-05. Devuelve cuántas cosas no coinciden (0 = sincronizado).
    /// </summary>
    public static async Task<int> DesvioAsync(ZassDbContext ctx, Guid perfilId)
    {
        ctx.ChangeTracker.Clear();
        var perfil = await ctx.PerfilesPrestador.AsNoTracking().SingleAsync(p => p.Id == perfilId);
        var visibles = await ctx.Resenas.AsNoTracking()
            .Where(r => r.PerfilPrestadorId == perfilId && r.Estado == EstadoResena.Publicada)
            .Select(r => (int)r.Calificacion).ToListAsync();

        var desvio = 0;
        if (perfil.TotalResenas != visibles.Count) desvio++;
        var esperado = visibles.Count == 0
            ? (decimal?)null
            : Math.Round((decimal)visibles.Average(), 1, MidpointRounding.AwayFromZero);
        if (perfil.PromedioCalificacion != esperado) desvio++;
        return desvio;
    }
}

/// <summary>
/// MOD-11 contra PostgreSQL de verdad, dentro de una transacción que siempre se deshace: lo que una
/// lista en memoria no puede contestar. Las restricciones (R-11, R-12, R-13, R-14, R-19), el SQL del
/// recálculo, la paginación por keyset con empates y la forma de las consultas.
/// </summary>
public class ResenasIntegracionPruebas : IDisposable
{
    private readonly ContextoDePrueba _prueba = new();
    private readonly RelojDePruebaResenas _reloj = new();
    private readonly ServicioResenas _servicio;
    private ZassDbContext Ctx => _prueba.Ctx;

    public ResenasIntegracionPruebas() => _servicio = AyudasResenas.Servicio(Ctx, _reloj);

    public void Dispose() => _prueba.Dispose();

    private (Usuario Autor, PerfilPrestador Perfil, Usuario Dueno) Escena()
    {
        var dueno = AyudasResenas.NuevoUsuario(Ctx, "prestador");
        var autor = AyudasResenas.NuevoUsuario(Ctx);
        var perfil = AyudasResenas.NuevoPerfil(Ctx, dueno);
        return (autor, perfil, dueno);
    }

    private async Task<Resena> Publicada(PerfilPrestador perfil, int cal = 5, Usuario? autor = null)
    {
        var a = autor ?? AyudasResenas.NuevoUsuario(Ctx);
        var c = AyudasResenas.NuevoContacto(Ctx, a, perfil);
        await Ctx.SaveChangesAsync();
        var r = await _servicio.PublicarAsync(AyudasResenas.Pub(a, c, cal));
        Assert.True(r.Exito, r.Mensaje);
        return await Ctx.Resenas.AsNoTracking().SingleAsync(x => x.Id == r.Valor!.Resena!.Id);
    }

    private async Task<PerfilPrestador> PerfilDeNuevo(Guid id)
    {
        Ctx.ChangeTracker.Clear();
        return await Ctx.PerfilesPrestador.AsNoTracking().SingleAsync(p => p.Id == id);
    }

    // ------------------------------------------------------------- publicar y leer de vuelta

    [Fact]
    public async Task Una_reseña_publicada_se_lee_de_vuelta_con_todo_y_deja_aviso_y_auditoria()
    {
        var (autor, perfil, dueno) = Escena();
        var c = AyudasResenas.NuevoContacto(Ctx, autor, perfil);
        await Ctx.SaveChangesAsync();

        var r = await _servicio.PublicarAsync(new PublicarResenaCmd(
            autor.Id, c.Id, 4, "  Muy buen trabajo, rápido y ordenado en todo momento.  ",
            new[] { "puntualidad", "precio_acordado" }, false, "k-1"));
        Assert.True(r.Exito, r.Mensaje);
        Ctx.ChangeTracker.Clear();

        var resena = await Ctx.Resenas.AsNoTracking().SingleAsync(x => x.ContactoId == c.Id);
        Assert.Equal(4, resena.Calificacion);
        Assert.Equal("Muy buen trabajo, rápido y ordenado en todo momento.", resena.Comentario);
        Assert.Equal(new[] { "puntualidad", "precio_acordado" }, resena.Aspectos);
        Assert.Equal(EstadoResena.Publicada, resena.Estado);
        Assert.Equal(TipoResena.Servicio, resena.Tipo);
        Assert.Equal(resena.PublicadaEn.AddHours(72), resena.EditableHasta);
        var aviso = await Ctx.Notificaciones.AsNoTracking()
            .SingleAsync(n => n.DestinatarioId == dueno.Id && n.Tipo == TipoNotificacion.ResenaNueva);
        Assert.Equal($"resena-nueva:{resena.Id}", aviso.ClaveIdempotencia);
        Assert.Contains(await Ctx.Auditoria.AsNoTracking().Where(a => a.EntidadId == resena.Id).ToListAsync(),
            a => a.Accion == "resena.publicada" && a.ActorId == autor.Id);

        var p = await PerfilDeNuevo(perfil.Id);
        Assert.Equal(1, p.TotalResenas);
        Assert.Equal(4.0m, p.PromedioCalificacion);
    }

    [Fact]
    public async Task El_comentario_se_guarda_crudo_con_html_y_comillas_sin_alterarse()
    {
        var (autor, perfil, _) = Escena();
        var c = AyudasResenas.NuevoContacto(Ctx, autor, perfil);
        await Ctx.SaveChangesAsync();
        var crudo = "<script>alert('x')</script> \"Todo bien\" & más; ñandú — 😀 sin problema alguno";

        var r = await _servicio.PublicarAsync(AyudasResenas.Pub(autor, c, texto: crudo));
        Assert.True(r.Exito, r.Mensaje);
        Ctx.ChangeTracker.Clear();

        Assert.Equal(crudo, (await Ctx.Resenas.AsNoTracking().SingleAsync(x => x.ContactoId == c.Id)).Comentario);
    }

    [Fact]
    public async Task El_promedio_guardado_es_el_simple_redondeado_a_un_decimal()
    {
        var (_, perfil, _) = Escena();
        await Publicada(perfil, 5);
        await Publicada(perfil, 5);
        await Publicada(perfil, 4);

        var p = await PerfilDeNuevo(perfil.Id);

        Assert.Equal(3, p.TotalResenas);
        Assert.Equal(4.7m, p.PromedioCalificacion);       // 14/3 = 4,666... y NO 4,775 (la deriva de la fórmula relativa)
    }

    [Fact]
    public async Task Sin_deriva_aunque_se_acumulen_muchas_reseñas()
    {
        // Con una fórmula relativa sobre un promedio ya redondeado, 40 reseñas se desvían del real.
        var (_, perfil, _) = Escena();
        var notas = new[] { 5, 4, 5, 3, 4, 5, 2, 5, 4, 4, 3, 5, 5, 1, 4, 5, 5, 3, 4, 5 };
        foreach (var n in notas) await Publicada(perfil, n);

        Assert.Equal(0, await AyudasResenas.DesvioAsync(Ctx, perfil.Id));
        Assert.Equal(Math.Round((decimal)notas.Average(), 1, MidpointRounding.AwayFromZero),
            (await PerfilDeNuevo(perfil.Id)).PromedioCalificacion);
    }

    [Fact]
    public async Task Ocultar_retirar_y_restaurar_actualizan_el_recuento_y_sin_reseñas_el_promedio_es_nulo()
    {
        var (_, perfil, _) = Escena();
        var a = await Publicada(perfil, 5);
        var reportante = AyudasResenas.NuevoUsuario(Ctx);
        var moderador = AyudasResenas.NuevoUsuario(Ctx, "moderador");
        await Ctx.SaveChangesAsync();

        var rep = await _servicio.ReportarAsync(a.Id, reportante.Id, MotivoReporte.LenguajeDeOdio, null);
        Assert.True(rep.Exito, rep.Mensaje);
        var oculta = await PerfilDeNuevo(perfil.Id);
        Assert.Equal(0, oculta.TotalResenas);
        Assert.Null(oculta.PromedioCalificacion);

        var reporteId = (await Ctx.ReportesResena.AsNoTracking().SingleAsync(r => r.ResenaId == a.Id)).Id;
        Assert.True((await _servicio.ResolverReporteAsync(reporteId, moderador.Id, false, "Se revisó: no hay odio en el texto.")).Exito);
        Assert.Equal(1, (await PerfilDeNuevo(perfil.Id)).TotalResenas);

        var otro = AyudasResenas.NuevoUsuario(Ctx);
        await Ctx.SaveChangesAsync();
        await _servicio.ReportarAsync(a.Id, otro.Id, MotivoReporte.Falsedad, "No fue cliente");
        var segundo = (await Ctx.ReportesResena.AsNoTracking().Where(r => r.ResenaId == a.Id && r.Estado == EstadoReporte.Abierto).SingleAsync()).Id;
        Assert.True((await _servicio.ResolverReporteAsync(segundo, moderador.Id, true, "Falsa: no hubo servicio alguno.")).Exito);

        var retirada = await PerfilDeNuevo(perfil.Id);
        Assert.Equal(0, retirada.TotalResenas);
        Assert.Null(retirada.PromedioCalificacion);
        Assert.Equal(EstadoResena.Retirada, (await Ctx.Resenas.AsNoTracking().SingleAsync(r => r.Id == a.Id)).Estado);
        Assert.Contains(await Ctx.Auditoria.AsNoTracking().Where(x => x.EntidadId == a.Id).ToListAsync(),
            x => x.Accion == "resena.retirada" && x.ActorId == moderador.Id);
    }

    [Fact]
    public async Task Editar_cambia_el_promedio_guarda_la_version_anterior_en_jsonb_y_marca_editada()
    {
        var (autor, perfil, _) = Escena();
        var c = AyudasResenas.NuevoContacto(Ctx, autor, perfil);
        await Ctx.SaveChangesAsync();
        var r = await _servicio.PublicarAsync(AyudasResenas.Pub(autor, c, 5));
        var id = r.Valor!.Resena!.Id;

        var e = await _servicio.EditarAsync(new EditarResenaCmd(
            id, autor.Id, 2, "Con el tiempo vi varios defectos que antes no noté.", null, false));
        Assert.True(e.Exito, e.Mensaje);
        Ctx.ChangeTracker.Clear();

        var resena = await Ctx.Resenas.AsNoTracking().SingleAsync(x => x.Id == id);
        Assert.True(resena.Editada);
        Assert.Equal(2, resena.Calificacion);
        Assert.Equal(2.0m, (await PerfilDeNuevo(perfil.Id)).PromedioCalificacion);
        var traza = await Ctx.Auditoria.AsNoTracking().SingleAsync(a => a.EntidadId == id && a.Accion == "resena.editada");
        using var json = JsonDocument.Parse(traza.Contexto!);
        Assert.Equal(5, json.RootElement.GetProperty("calificacionAnterior").GetInt32());
        Assert.Equal(AyudasResenas.Comentario, json.RootElement.GetProperty("comentarioAnterior").GetString());
    }

    [Fact]
    public async Task Editar_pasadas_las_72_horas_se_rechaza_con_la_base_real()
    {
        var (autor, perfil, _) = Escena();
        var c = AyudasResenas.NuevoContacto(Ctx, autor, perfil);
        await Ctx.SaveChangesAsync();
        var id = (await _servicio.PublicarAsync(AyudasResenas.Pub(autor, c, 5))).Valor!.Resena!.Id;
        _reloj.Avanzar(TimeSpan.FromHours(72) + TimeSpan.FromSeconds(1));

        var e = await _servicio.EditarAsync(new EditarResenaCmd(
            id, autor.Id, 1, "Tarde para opinar distinto, pero lo intento igualmente.", null, false));

        Assert.Equal(Fallas.ReglaDeNegocio, e.Codigo);
        Ctx.ChangeTracker.Clear();
        Assert.Equal(5, (await Ctx.Resenas.AsNoTracking().SingleAsync(x => x.Id == id)).Calificacion);
    }

    [Fact]
    public async Task Responder_crea_y_despues_edita_una_sola_fila_y_avisa_al_autor_una_vez()
    {
        var (autor, perfil, dueno) = Escena();
        var resena = await Publicada(perfil, 5, autor);

        Assert.True((await _servicio.ResponderAsync(resena.Id, dueno.Id, "Gracias por tu reseña.")).Valor);
        var edicion = await _servicio.ResponderAsync(resena.Id, dueno.Id, "Gracias, vuelve cuando quieras.");
        Assert.False(edicion.Valor);
        Ctx.ChangeTracker.Clear();

        var respuesta = await Ctx.RespuestasResena.AsNoTracking().SingleAsync(x => x.ResenaId == resena.Id);
        Assert.Equal("Gracias, vuelve cuando quieras.", respuesta.Texto);
        Assert.NotNull(respuesta.EditadaEn);
        Assert.Equal(1, await Ctx.Notificaciones.CountAsync(
            n => n.DestinatarioId == autor.Id && n.Tipo == TipoNotificacion.ResenaRespondida));
    }

    // ------------------------------------------------------------- restricciones de la base

    [Fact]
    public async Task La_base_rechaza_una_segunda_reseña_para_el_mismo_contacto_R11()
    {
        var (autor, perfil, _) = Escena();
        var c = AyudasResenas.NuevoContacto(Ctx, autor, perfil);
        Ctx.Resenas.Add(new Resena { ContactoId = c.Id, PerfilPrestadorId = perfil.Id, AutorId = autor.Id, Calificacion = 5, Comentario = AyudasResenas.Comentario });
        await Ctx.SaveChangesAsync();
        // Sin entidades seguidas: con el contacto en el contexto, EF "arregla" la relación uno a uno
        // reemplazando la reseña vieja, y la restricción de la base ni se llegaría a probar.
        Ctx.ChangeTracker.Clear();
        Ctx.Resenas.Add(new Resena { ContactoId = c.Id, PerfilPrestadorId = perfil.Id, AutorId = autor.Id, Calificacion = 4, Comentario = AyudasResenas.Comentario });

        var e = await Assert.ThrowsAsync<DbUpdateException>(() => Ctx.SaveChangesAsync());
        Assert.Equal(PostgresErrorCodes.UniqueViolation, ((PostgresException)e.InnerException!).SqlState);
    }

    [Theory]
    [InlineData(0, 20, PostgresErrorCodes.CheckViolation)]
    [InlineData(6, 20, PostgresErrorCodes.CheckViolation)]
    [InlineData(5, 19, PostgresErrorCodes.CheckViolation)]
    [InlineData(5, 1501, PostgresErrorCodes.StringDataRightTruncation)]   // la columna es varchar(1500)
    public async Task La_base_rechaza_calificacion_y_comentario_fuera_de_rango_R12_R13(
        int calificacion, int largo, string sqlState)
    {
        var (autor, perfil, _) = Escena();
        var c = AyudasResenas.NuevoContacto(Ctx, autor, perfil);
        Ctx.Resenas.Add(new Resena
        {
            ContactoId = c.Id, PerfilPrestadorId = perfil.Id, AutorId = autor.Id,
            Calificacion = (short)calificacion, Comentario = new string('a', largo),
        });

        var e = await Assert.ThrowsAsync<DbUpdateException>(() => Ctx.SaveChangesAsync());
        Assert.Equal(sqlState, ((PostgresException)e.InnerException!).SqlState);
    }

    [Fact]
    public async Task La_base_rechaza_una_segunda_respuesta_a_la_misma_reseña_R14()
    {
        var (_, perfil, _) = Escena();
        var resena = await Publicada(perfil);
        Ctx.RespuestasResena.Add(new RespuestaResena { ResenaId = resena.Id, Texto = "Una" });
        await Ctx.SaveChangesAsync();
        Ctx.ChangeTracker.Clear();      // ver la nota de R-11
        Ctx.RespuestasResena.Add(new RespuestaResena { ResenaId = resena.Id, Texto = "Otra" });

        var e = await Assert.ThrowsAsync<DbUpdateException>(() => Ctx.SaveChangesAsync());
        Assert.Equal(PostgresErrorCodes.UniqueViolation, ((PostgresException)e.InnerException!).SqlState);
    }

    [Fact]
    public async Task La_base_rechaza_que_el_moderador_resuelva_su_propio_reporte_R19()
    {
        var (_, perfil, _) = Escena();
        var resena = await Publicada(perfil);
        var yo = AyudasResenas.NuevoUsuario(Ctx, "moderador");
        Ctx.ReportesResena.Add(new ReporteResena
        {
            ResenaId = resena.Id, ReportadoPor = yo.Id, ResueltoPor = yo.Id,
            Motivo = MotivoReporte.Falsedad, Estado = EstadoReporte.ResueltoMantiene,
        });

        var e = await Assert.ThrowsAsync<DbUpdateException>(() => Ctx.SaveChangesAsync());
        Assert.Equal(PostgresErrorCodes.CheckViolation, ((PostgresException)e.InnerException!).SqlState);
    }

    // ------------------------------------------------------------- elegibilidad con citas reales

    private async Task<Cita> UnaCita(Usuario buscador, PerfilPrestador perfil, EstadoCita estado)
    {
        var oferta = new PrestadorServicio { PerfilPrestadorId = perfil.Id, ServicioId = 101 };
        var disp = new Disponibilidad
        {
            PerfilPrestadorId = perfil.Id, PrestadorServicioId = oferta.Id, DiaSemana = 1,
            HoraInicio = new TimeOnly(9, 0), HoraFin = new TimeOnly(12, 0),
            VigenteDesde = DateOnly.FromDateTime(DateTime.UtcNow),
        };
        var inicio = DateTime.UtcNow.AddDays(-2);
        var bloque = new BloqueHorario
        {
            DisponibilidadId = disp.Id, PerfilPrestadorId = perfil.Id, PrestadorServicioId = oferta.Id,
            InicioEn = inicio, FinEn = inicio.AddMinutes(30), Estado = EstadoBloque.Reservado,
        };
        var cita = new Cita
        {
            BloqueHorarioId = bloque.Id, BuscadorId = buscador.Id, PrestadorServicioId = oferta.Id,
            PerfilPrestadorId = perfil.Id, Estado = estado, Codigo = Cita.GenerarCodigo(),
        };
        Ctx.AddRange(oferta, disp, bloque, cita);
        await Ctx.SaveChangesAsync();
        return cita;
    }

    [Theory]
    [InlineData(EstadoCita.Realizada, true)]
    [InlineData(EstadoCita.Confirmada, false)]
    [InlineData(EstadoCita.NoAsistio, false)]
    [InlineData(EstadoCita.Cancelada, false)]
    public async Task Una_cita_real_habilita_solo_si_quedo_realizada(EstadoCita estado, bool elegible)
    {
        var (autor, perfil, _) = Escena();
        await Ctx.SaveChangesAsync();
        var cita = await UnaCita(autor, perfil, estado);

        var registrado = await _servicio.RegistrarContactoAsync(
            autor.Id, perfil.Id, OrigenContacto.Cita, CanalContacto.Formulario, cita.Id);
        Assert.True(registrado.Exito, registrado.Mensaje);
        Ctx.ChangeTracker.Clear();

        var e = (await _servicio.EsElegibleAsync(autor.Id, perfil.Id)).Valor!;
        Assert.Equal(elegible, e.Elegible);
        if (elegible)
        {
            var publicada = await _servicio.PublicarAsync(AyudasResenas.Pub(
                autor, await Ctx.Contactos.AsNoTracking().SingleAsync(c => c.Id == registrado.Valor)));
            Assert.True(publicada.Exito, publicada.Mensaje);
        }
    }

    [Fact]
    public async Task Una_cita_ajena_no_se_puede_ligar_a_un_contacto_con_la_base_real()
    {
        var (autor, perfil, _) = Escena();
        var otra = AyudasResenas.NuevoUsuario(Ctx);
        await Ctx.SaveChangesAsync();
        var cita = await UnaCita(otra, perfil, EstadoCita.Realizada);

        var r = await _servicio.RegistrarContactoAsync(
            autor.Id, perfil.Id, OrigenContacto.Cita, CanalContacto.Formulario, cita.Id);

        Assert.Equal(Fallas.NoEncontrado, r.Codigo);
    }

    [Fact]
    public async Task El_tope_de_90_dias_cuenta_solo_las_reseñas_por_contacto_sin_cita_con_la_base_real()
    {
        var (autor, perfil, _) = Escena();
        await Ctx.SaveChangesAsync();
        var cita = await UnaCita(autor, perfil, EstadoCita.Realizada);
        var porCita = AyudasResenas.NuevoContacto(Ctx, autor, perfil, TimeSpan.FromHours(1), cita.Id);
        await Ctx.SaveChangesAsync();
        Assert.True((await _servicio.PublicarAsync(AyudasResenas.Pub(autor, porCita))).Exito);

        // Con solo la reseña por cita, un contacto suelto de hace 8 días sigue habilitado.
        AyudasResenas.NuevoContacto(Ctx, autor, perfil);
        await Ctx.SaveChangesAsync();
        Assert.True((await _servicio.EsElegibleAsync(autor.Id, perfil.Id)).Valor!.Elegible);

        var suelto = (await Ctx.Contactos.AsNoTracking().Where(c => c.CitaId == null && c.BuscadorId == autor.Id).SingleAsync());
        Assert.True((await _servicio.PublicarAsync(AyudasResenas.Pub(autor, suelto))).Exito);

        // Ahora sí: ya hay una por contacto, y otro contacto suelto queda bajo el tope.
        AyudasResenas.NuevoContacto(Ctx, autor, perfil);
        await Ctx.SaveChangesAsync();
        var e = (await _servicio.EsElegibleAsync(autor.Id, perfil.Id)).Valor!;
        Assert.False(e.Elegible);
        Assert.Equal(MotivoDeNoElegibilidad.TopeDe90Dias, e.Motivo);
    }

    [Fact]
    public async Task Contactarse_dos_veces_deja_una_sola_fila_con_la_base_real()
    {
        var (autor, perfil, _) = Escena();
        await Ctx.SaveChangesAsync();

        var a = await _servicio.RegistrarContactoAsync(autor.Id, perfil.Id, OrigenContacto.Ficha, CanalContacto.Telefono);
        var b = await _servicio.RegistrarContactoAsync(autor.Id, perfil.Id, OrigenContacto.Busqueda, CanalContacto.Correo);

        Assert.Equal(a.Valor, b.Valor);
        Assert.Equal(1, await Ctx.Contactos.CountAsync(c => c.BuscadorId == autor.Id));
    }

    // ------------------------------------------------------------- lectura pública

    [Fact]
    public async Task La_paginacion_publica_no_repite_ni_salta_con_empates_de_fecha_en_la_base_real()
    {
        var (_, perfil, _) = Escena();
        var fecha = DateTime.UtcNow.AddDays(-1);
        for (var i = 0; i < 45; i++)
        {
            var autor = AyudasResenas.NuevoUsuario(Ctx);
            var c = AyudasResenas.NuevoContacto(Ctx, autor, perfil);
            Ctx.Resenas.Add(new Resena
            {
                ContactoId = c.Id, PerfilPrestadorId = perfil.Id, AutorId = autor.Id, Calificacion = 4,
                Comentario = $"Reseña número {i} con comentario suficiente para pasar.",
                PublicadaEn = fecha.AddMinutes(i % 6),      // seis fechas, ~8 reseñas por fecha
            });
        }
        await Ctx.SaveChangesAsync();
        Ctx.ChangeTracker.Clear();

        var vistas = new List<Guid>();
        string? cursor = null;
        var paginas = 0;
        do
        {
            var p = (await _servicio.ResenasPublicasAsync(perfil.Id, cursor)).Valor!;
            vistas.AddRange(p.Resenas.Select(r => r.Id));
            cursor = p.SiguienteCursor;
            paginas++;
        } while (cursor is not null && paginas < 10);

        Assert.Equal(3, paginas);
        Assert.Equal(45, vistas.Distinct().Count());
        Assert.Equal(45, vistas.Count);
    }

    [Fact]
    public async Task Lo_publico_lleva_la_respuesta_y_deja_fuera_ocultas_retiradas_y_toda_identidad()
    {
        var (autor, perfil, dueno) = Escena();
        var visible = await Publicada(perfil, 5, autor);
        var oculta = await Publicada(perfil, 1);
        await Ctx.Resenas.Where(r => r.Id == oculta.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.Estado, EstadoResena.OcultaEnModeracion));
        await _servicio.ResponderAsync(visible.Id, dueno.Id, "Gracias por tu reseña.");
        Ctx.ChangeTracker.Clear();

        var pagina = (await _servicio.ResenasPublicasAsync(perfil.Id, null)).Valor!;

        var unica = Assert.Single(pagina.Resenas);
        Assert.Equal(visible.Id, unica.Id);
        Assert.Equal("Gracias por tu reseña.", unica.Respuesta!.Texto);
        var json = JsonSerializer.Serialize(pagina);
        Assert.DoesNotContain(autor.Id.ToString(), json);
        Assert.DoesNotContain(autor.Correo, json);
        Assert.DoesNotContain(visible.ContactoId.ToString(), json);
    }

    // ------------------------------------------------------------- la cola de moderación

    [Fact]
    public async Task La_cola_real_excluye_lo_propio_y_pone_los_graves_primero()
    {
        var (autorA, perfil, dueno) = Escena();
        var mod = AyudasResenas.NuevoUsuario(Ctx, "moderador");
        AyudasResenas.DarRol(Ctx, mod, "buscador");      // para poder escribir una reseña propia
        await Ctx.SaveChangesAsync();
        var ajena1 = await Publicada(perfil, 5, autorA);
        var ajena2 = await Publicada(perfil, 3);
        var deMod = await Publicada(perfil, 2, mod);
        var reportante = AyudasResenas.NuevoUsuario(Ctx);
        await Ctx.SaveChangesAsync();

        await _servicio.ReportarAsync(ajena1.Id, reportante.Id, MotivoReporte.Falsedad, null);
        await _servicio.ReportarAsync(ajena2.Id, reportante.Id, MotivoReporte.DatosPersonales, "Trae un teléfono");
        await _servicio.ReportarAsync(deMod.Id, reportante.Id, MotivoReporte.FueraDeTema, null);
        var propio = await Publicada(perfil, 4);
        await _servicio.ReportarAsync(propio.Id, mod.Id, MotivoReporte.FueraDeTema, null);

        var cola = (await _servicio.ColaDeReportesAsync(mod.Id)).Valor!;

        Assert.Equal(new[] { ajena2.Id, ajena1.Id }, cola.Select(f => f.ResenaId));
        Assert.Equal(MotivoReporte.DatosPersonales, cola[0].Motivo);
        Assert.Equal(perfil.NombrePublico, cola[0].PerfilNombre);
    }

    [Fact]
    public async Task La_cola_excluye_los_reclamos_sobre_el_perfil_del_propio_moderador()
    {
        var (_, perfilNormal, _) = Escena();
        var mod = AyudasResenas.NuevoUsuario(Ctx, "moderador");
        var perfilDelMod = AyudasResenas.NuevoPerfil(Ctx, mod);
        await Ctx.SaveChangesAsync();
        var enMiPerfil = await Publicada(perfilDelMod, 5);
        var enOtro = await Publicada(perfilNormal, 5);
        var reportante = AyudasResenas.NuevoUsuario(Ctx);
        await Ctx.SaveChangesAsync();
        await _servicio.ReportarAsync(enMiPerfil.Id, reportante.Id, MotivoReporte.Falsedad, null);
        await _servicio.ReportarAsync(enOtro.Id, reportante.Id, MotivoReporte.Falsedad, null);

        var cola = (await _servicio.ColaDeReportesAsync(mod.Id)).Valor!;

        Assert.Equal(enOtro.Id, Assert.Single(cola).ResenaId);
    }

    // ------------------------------------------------------------- el contenedor

    [Fact]
    public void El_contenedor_resuelve_el_servicio_de_reseñas_y_su_repositorio()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Zass:CadenaConexion"] = Conexion.Cadena,
            ["Zass:ClaveDireccionesBase64"] =
                Convert.ToBase64String(Enumerable.Range(0, 32).Select(i => (byte)i).ToArray()),
        }).Build();
        using var proveedor = new ServiceCollection().AgregarZass(config)
            .BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
        using var ambito = proveedor.CreateScope();

        Assert.NotNull(ambito.ServiceProvider.GetRequiredService<IServicioResenas>());
        Assert.NotNull(ambito.ServiceProvider.GetRequiredService<IRepositorioResenas>());
    }
}

/// <summary>
/// La concurrencia de CC-07 y de las reglas de elegibilidad, con VARIAS conexiones reales que
/// confirman de verdad. No pueden vivir dentro de la transacción que siempre se deshace de la otra
/// clase: lo que se mide aquí es justamente lo que ocurre entre transacciones. Por eso cada prueba
/// limpia lo suyo al terminar.
/// </summary>
public class ResenasConcurrenciaPruebas : IAsyncLifetime
{
    private readonly List<Guid> _usuarios = new();

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        if (_usuarios.Count == 0) return;
        using var ctx = ContextoDePrueba.Crear();
        // Todo lo demás (perfiles, contactos, reseñas, avisos) cae en cascada desde el usuario.
        await ctx.Auditoria.Where(a => a.ActorId != null && _usuarios.Contains(a.ActorId.Value)).ExecuteDeleteAsync();
        await ctx.Usuarios.Where(u => _usuarios.Contains(u.Id)).ExecuteDeleteAsync();
    }

    private async Task<(PerfilPrestador Perfil, Usuario Dueno)> UnPrestador()
    {
        using var ctx = ContextoDePrueba.Crear();
        var dueno = AyudasResenas.NuevoUsuario(ctx, "prestador");
        var perfil = AyudasResenas.NuevoPerfil(ctx, dueno);
        await ctx.SaveChangesAsync();
        _usuarios.Add(dueno.Id);
        return (perfil, dueno);
    }

    private async Task<(Usuario Autor, Contacto Contacto)> UnBuscadorConContacto(PerfilPrestador perfil)
    {
        using var ctx = ContextoDePrueba.Crear();
        var autor = AyudasResenas.NuevoUsuario(ctx);
        var contacto = AyudasResenas.NuevoContacto(ctx, autor, perfil);
        await ctx.SaveChangesAsync();
        _usuarios.Add(autor.Id);
        return (autor, contacto);
    }

    private static async Task<Resultado<ResultadoPublicacion>> Publicar(PublicarResenaCmd cmd)
    {
        // Un contexto por operación: así se parecen a dos peticiones HTTP distintas.
        using var ctx = ContextoDePrueba.Crear();
        return await AyudasResenas.Servicio(ctx, new RelojUtc()).PublicarAsync(cmd);
    }

    [Fact]
    public async Task Doce_publicaciones_simultaneas_al_mismo_prestador_no_pierden_ninguna_CC07()
    {
        var (perfil, _) = await UnPrestador();
        var gente = new List<(Usuario Autor, Contacto Contacto)>();
        for (var i = 0; i < 12; i++) gente.Add(await UnBuscadorConContacto(perfil));
        var notas = new[] { 5, 4, 3, 5, 5, 2, 4, 5, 1, 4, 5, 3 };

        var resultados = await Task.WhenAll(gente.Select((g, i) => Task.Run(() =>
            Publicar(AyudasResenas.Pub(g.Autor, g.Contacto, notas[i])))));

        Assert.All(resultados, r => Assert.True(r.Exito, r.Mensaje));
        using var ctx = ContextoDePrueba.Crear();
        var p = await ctx.PerfilesPrestador.AsNoTracking().SingleAsync(x => x.Id == perfil.Id);
        Assert.Equal(12, p.TotalResenas);
        Assert.Equal(Math.Round((decimal)notas.Average(), 1, MidpointRounding.AwayFromZero), p.PromedioCalificacion);
        Assert.Equal(0, await AyudasResenas.DesvioAsync(ctx, perfil.Id));
    }

    [Fact]
    public async Task El_mismo_envio_cinco_veces_a_la_vez_deja_una_reseña_un_aviso_y_un_recuento_CC04()
    {
        var (perfil, dueno) = await UnPrestador();
        var (autor, contacto) = await UnBuscadorConContacto(perfil);
        var cmd = AyudasResenas.Pub(autor, contacto, 4, clave: "misma-clave");

        var resultados = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => Task.Run(() => Publicar(cmd))));

        Assert.All(resultados, r => Assert.True(r.Exito, r.Mensaje));
        Assert.Equal(1, resultados.Count(r => !r.Valor!.YaExistia));      // exactamente una creó
        Assert.Equal(4, resultados.Count(r => r.Valor!.YaExistia));
        Assert.Single(resultados.Select(r => r.Valor!.Resena!.Id).Distinct());
        using var ctx = ContextoDePrueba.Crear();
        Assert.Equal(1, await ctx.Resenas.CountAsync(r => r.ContactoId == contacto.Id));
        Assert.Equal(1, await ctx.Notificaciones.CountAsync(
            n => n.DestinatarioId == dueno.Id && n.Tipo == TipoNotificacion.ResenaNueva));
        Assert.Equal(1, (await ctx.PerfilesPrestador.AsNoTracking().SingleAsync(p => p.Id == perfil.Id)).TotalResenas);
    }

    [Fact]
    public async Task Dos_contactos_a_la_vez_no_esquivan_el_tope_de_una_reseña_cada_90_dias_RN_S5_02()
    {
        var (perfil, _) = await UnPrestador();
        Usuario autor;
        Contacto c1, c2;
        using (var ctx = ContextoDePrueba.Crear())
        {
            autor = AyudasResenas.NuevoUsuario(ctx);
            c1 = AyudasResenas.NuevoContacto(ctx, autor, perfil, TimeSpan.FromDays(10));
            c2 = AyudasResenas.NuevoContacto(ctx, autor, perfil, TimeSpan.FromDays(9));
            await ctx.SaveChangesAsync();
            _usuarios.Add(autor.Id);
        }

        var resultados = await Task.WhenAll(
            Task.Run(() => Publicar(AyudasResenas.Pub(autor, c1))),
            Task.Run(() => Publicar(AyudasResenas.Pub(autor, c2))));

        Assert.Equal(1, resultados.Count(r => r.Exito));
        Assert.Equal(1, resultados.Count(r => !r.Exito && r.Codigo == FallasResenas.NoElegible));
        using var verificar = ContextoDePrueba.Crear();
        Assert.Equal(1, await verificar.Resenas.CountAsync(r => r.AutorId == autor.Id));
    }

    [Fact]
    public async Task Un_rechazo_deja_registrado_el_intento_y_ninguna_reseña_con_transaccion_real()
    {
        var (perfil, _) = await UnPrestador();
        Usuario autor;
        Contacto reciente;
        using (var ctx = ContextoDePrueba.Crear())
        {
            autor = AyudasResenas.NuevoUsuario(ctx);
            reciente = AyudasResenas.NuevoContacto(ctx, autor, perfil, TimeSpan.FromDays(2));
            await ctx.SaveChangesAsync();
            _usuarios.Add(autor.Id);
        }

        var r = await Publicar(AyudasResenas.Pub(autor, reciente));

        Assert.Equal(FallasResenas.NoElegible, r.Codigo);
        using var verificar = ContextoDePrueba.Crear();
        Assert.Equal(0, await verificar.Resenas.CountAsync(x => x.ContactoId == reciente.Id));
        Assert.Contains(await verificar.Auditoria.AsNoTracking().Where(a => a.EntidadId == reciente.Id).ToListAsync(),
            a => a.Accion == "resena.intento_rechazado" && a.ActorId == autor.Id);
        Assert.Equal(0, (await verificar.PerfilesPrestador.AsNoTracking().SingleAsync(p => p.Id == perfil.Id)).TotalResenas);
    }

    /// <summary>Guarda de verdad y después falla, como lo haría una caída entre el guardado y el recálculo.</summary>
    private sealed class UnidadQueFallaDespuesDeGuardar : IUnidadDeTrabajo
    {
        private readonly IUnidadDeTrabajo _real;
        public UnidadQueFallaDespuesDeGuardar(IUnidadDeTrabajo real) => _real = real;
        public async Task<int> GuardarAsync(CancellationToken ct = default)
        {
            await _real.GuardarAsync(ct);
            throw new InvalidOperationException("caída simulada tras guardar la reseña");
        }

        // Lo que añadió MOD-06 (CC-01) a la interfaz. Reseñas lleva su propia transacción por el
        // repositorio, así que aquí solo se delega.
        public Task<T> EnTransaccionAsync<T>(
            Func<CancellationToken, Task<T>> cuerpo, CancellationToken ct = default) =>
            _real.EnTransaccionAsync(cuerpo, ct);
    }

    [Fact]
    public async Task Si_algo_falla_despues_de_guardar_la_reseña_la_transaccion_se_deshace_entera()
    {
        var (perfil, dueno) = await UnPrestador();
        var (autor, contacto) = await UnBuscadorConContacto(perfil);

        using (var ctx = ContextoDePrueba.Crear())
        {
            var servicio = AyudasResenas.Servicio(
                ctx, new RelojUtc(), new UnidadQueFallaDespuesDeGuardar(new UnidadDeTrabajoEfCore(ctx)));
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => servicio.PublicarAsync(AyudasResenas.Pub(autor, contacto)));
        }

        // CU-16, postcondición de fallo: transacción revertida y reputación intacta.
        using var verificar = ContextoDePrueba.Crear();
        Assert.Equal(0, await verificar.Resenas.CountAsync(r => r.ContactoId == contacto.Id));
        Assert.Equal(0, await verificar.Notificaciones.CountAsync(
            n => n.DestinatarioId == dueno.Id && n.Tipo == TipoNotificacion.ResenaNueva));
        Assert.Equal(0, (await verificar.PerfilesPrestador.AsNoTracking().SingleAsync(p => p.Id == perfil.Id)).TotalResenas);
        Assert.Equal(0, await verificar.Auditoria.CountAsync(a => a.EntidadId != null && a.ActorId == autor.Id));
    }

    [Fact]
    public async Task Dos_respuestas_simultaneas_terminan_en_una_sola_fila_sin_error()
    {
        var (perfil, dueno) = await UnPrestador();
        var (autor, contacto) = await UnBuscadorConContacto(perfil);
        var resenaId = (await Publicar(AyudasResenas.Pub(autor, contacto))).Valor!.Resena!.Id;

        var r = await Task.WhenAll(Enumerable.Range(0, 4).Select(i => Task.Run(async () =>
        {
            using var ctx = ContextoDePrueba.Crear();
            return await AyudasResenas.Servicio(ctx, new RelojUtc())
                .ResponderAsync(resenaId, dueno.Id, $"Respuesta número {i}");
        })));

        Assert.All(r, x => Assert.True(x.Exito, x.Mensaje));
        Assert.Equal(1, r.Count(x => x.Valor));       // una creó, las demás editaron
        using var verificar = ContextoDePrueba.Crear();
        Assert.Equal(1, await verificar.RespuestasResena.CountAsync(x => x.ResenaId == resenaId));
        Assert.Equal(1, await verificar.Notificaciones.CountAsync(n => n.DestinatarioId == autor.Id
            && n.Tipo == TipoNotificacion.ResenaRespondida));
    }

    [Fact]
    public async Task Publicar_editar_y_reportar_a_la_vez_dejan_el_recuento_sincronizado_con_el_control_nocturno()
    {
        var (perfil, _) = await UnPrestador();
        var gente = new List<(Usuario Autor, Contacto Contacto)>();
        for (var i = 0; i < 6; i++) gente.Add(await UnBuscadorConContacto(perfil));
        Usuario reportante;
        using (var ctx = ContextoDePrueba.Crear())
        {
            reportante = AyudasResenas.NuevoUsuario(ctx);
            await ctx.SaveChangesAsync();
            _usuarios.Add(reportante.Id);
        }

        // Primero 3 ya publicadas; después, a la vez: 3 publicaciones nuevas, 3 ediciones y 3 reportes graves.
        var previas = new List<Guid>();
        foreach (var g in gente.Take(3))
            previas.Add((await Publicar(AyudasResenas.Pub(g.Autor, g.Contacto, 5))).Valor!.Resena!.Id);

        var tareas = new List<Task>();
        foreach (var g in gente.Skip(3))
            tareas.Add(Task.Run(() => Publicar(AyudasResenas.Pub(g.Autor, g.Contacto, 2))));
        for (var i = 0; i < 3; i++)
        {
            var (autor, id) = (gente[i].Autor, previas[i]);
            tareas.Add(Task.Run(async () =>
            {
                using var ctx = ContextoDePrueba.Crear();
                await AyudasResenas.Servicio(ctx, new RelojUtc()).EditarAsync(new EditarResenaCmd(
                    id, autor.Id, 1, "Cambié de idea: el trabajo tuvo varios problemas serios.", null, false));
            }));
            if (i < 2)
                tareas.Add(Task.Run(async () =>
                {
                    using var ctx = ContextoDePrueba.Crear();
                    await AyudasResenas.Servicio(ctx, new RelojUtc())
                        .ReportarAsync(id, reportante.Id, MotivoReporte.LenguajeDeOdio, null);
                }));
        }
        await Task.WhenAll(tareas);

        using var verificar = ContextoDePrueba.Crear();
        Assert.Equal(0, await AyudasResenas.DesvioAsync(verificar, perfil.Id));
        var p = await verificar.PerfilesPrestador.AsNoTracking().SingleAsync(x => x.Id == perfil.Id);
        Assert.Equal(4, p.TotalResenas);       // 6 reseñas, menos las 2 que el reporte grave ocultó
    }
}
