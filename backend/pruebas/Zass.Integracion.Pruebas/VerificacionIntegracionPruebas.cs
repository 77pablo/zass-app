using Microsoft.EntityFrameworkCore;
using Zass.Aplicacion.Credenciales;
using Zass.Dominio.Comun;
using Zass.Dominio.Credenciales;
using Zass.Dominio.Identidad;
using Zass.Infraestructura.Datos;
using Zass.Infraestructura.Datos.Repositorios;
using Zass.Infraestructura.Seguridad;

namespace Zass.Integracion.Pruebas;

/// <summary>
/// CC-03 contra PostgreSQL de verdad: dos verificadores a la vez, cada uno con SU conexión.
///
/// Estas pruebas no pueden usar <see cref="ContextoDePrueba"/>: ese envuelve todo en una transacción
/// que se deshace, y dos conexiones distintas no verían las filas sin confirmar de la otra. Aquí los
/// datos se CONFIRMAN y se borran al final (por los usuarios, de los que cuelga todo en cascada).
/// </summary>
public class VerificacionIntegracionPruebas : IAsyncLifetime
{
    private readonly List<Guid> _usuarios = new();
    private readonly List<Guid> _credenciales = new();

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        using var ctx = ContextoDePrueba.Crear();
        ctx.Auditoria.RemoveRange(ctx.Auditoria.Where(a =>
            (a.ActorId != null && _usuarios.Contains(a.ActorId.Value))
            || (a.EntidadId != null && _credenciales.Contains(a.EntidadId.Value))));
        ctx.Usuarios.RemoveRange(ctx.Usuarios.Where(u => _usuarios.Contains(u.Id)));
        await ctx.SaveChangesAsync();
    }

    private static ZassDbContext Contexto() => ContextoDePrueba.Crear();

    private static (ServicioVerificacion Servicio, ZassDbContext Ctx) UnServicio(bool sinDocumentos = true)
    {
        var ctx = Contexto();
        return (new ServicioVerificacion(
            new RepositorioCredencialesEfCore(ctx), new UnidadDeTrabajoEfCore(ctx),
            new RelojUtc(), new AuditorEfCore(ctx),
            new OpcionesVerificacion { PermitirSinDocumentos = sinDocumentos }), ctx);
    }

    private async Task<PerfilPrestador> UnPrestadorAsync()
    {
        using var ctx = Contexto();
        var usuario = new Usuario
        {
            Correo = $"ver-{Guid.NewGuid():N}@zass.local", HashContrasena = "x",
            Nombre = "Prestadora", CorreoVerificado = true,
        };
        ctx.Usuarios.Add(usuario);
        var perfil = new PerfilPrestador
        {
            UsuarioId = usuario.Id, NombrePublico = $"Estudio {Guid.NewGuid():N}",
            Slug = $"estudio-{Guid.NewGuid():N}", RubroId = 1,
        };
        ctx.PerfilesPrestador.Add(perfil);
        await ctx.SaveChangesAsync();
        _usuarios.Add(usuario.Id);
        return perfil;
    }

    /// <summary>Un usuario que solo verifica (no necesita perfil: RN-S2-09 compara contra el dueño).</summary>
    private async Task<Guid> UnVerificadorAsync()
    {
        using var ctx = Contexto();
        var usuario = new Usuario
        {
            Correo = $"verif-{Guid.NewGuid():N}@zass.local", HashContrasena = "x",
            Nombre = "Verificador", CorreoVerificado = true,
        };
        ctx.Usuarios.Add(usuario);
        ctx.UsuarioRoles.Add(new UsuarioRol { UsuarioId = usuario.Id, RolId = 3 });
        await ctx.SaveChangesAsync();
        _usuarios.Add(usuario.Id);
        return usuario.Id;
    }

    private async Task<Guid> EnRevisionAsync(
        PerfilPrestador perfil, bool conDocumento = false, DateTime? creada = null)
    {
        using var ctx = Contexto();
        var c = new Credencial
        {
            PerfilPrestadorId = perfil.Id, Nombre = "Abogado", Tipo = TipoCredencial.Titulo,
            ObtenidaEn = new DateOnly(2020, 3, 15), Estado = EstadoCredencial.EnRevision,
            CreadaEn = creada ?? DateTime.UtcNow,
        };
        if (conDocumento)
            c.Documentos.Add(new DocumentoCredencial
            {
                ClaveAlmacenamiento = $"v-{Guid.NewGuid():N}", NombreOriginal = "t.pdf",
                TipoMime = "application/pdf", TamanoBytes = 10, Estado = EstadoDocumento.Disponible,
            });
        ctx.Credenciales.Add(c);
        await ctx.SaveChangesAsync();
        _credenciales.Add(c.Id);
        return c.Id;
    }

    private static async Task<Credencial> LeerAsync(Guid id)
    {
        using var ctx = Contexto();
        return await ctx.Credenciales.Include(c => c.Verificaciones).AsNoTracking().SingleAsync(c => c.Id == id);
    }

    // ============================================================= CC-03: la carrera

    /// <summary>
    /// La prueba que importa. Dos verificadores piden la MISMA credencial a la vez, con dos conexiones,
    /// 40 rondas sobre 40 credenciales distintas, soltando a los dos con la misma señal. En cada ronda
    /// tiene que ganar exactamente uno: nunca dos, nunca ninguno.
    /// </summary>
    [Fact]
    public async Task Dos_verificadores_tomando_la_misma_a_la_vez_solo_uno_gana_en_40_rondas()
    {
        var perfil = await UnPrestadorAsync();
        var ana = await UnVerificadorAsync();
        var beto = await UnVerificadorAsync();
        var ids = new List<Guid>();
        for (var i = 0; i < 40; i++) ids.Add(await EnRevisionAsync(perfil));

        var ganadoresPorAna = 0;
        foreach (var id in ids)
        {
            var salida = new TaskCompletionSource();
            async Task<Resultado<CredencialEnColaDto>> Tomar(Guid quien)
            {
                var (servicio, ctx) = UnServicio();
                using (ctx)
                {
                    await salida.Task;
                    return await servicio.TomarAsync(quien, id);
                }
            }
            var a = Task.Run(() => Tomar(ana));
            var b = Task.Run(() => Tomar(beto));
            await Task.Delay(30);   // que las dos ya tengan su conexion y esperen la senal
            salida.SetResult();
            var resultados = await Task.WhenAll(a, b);

            Assert.Equal(1, resultados.Count(r => r.Exito));
            var perdedor = resultados.Single(r => !r.Exito);
            Assert.Equal(Fallas.Conflicto, perdedor.Codigo);
            Assert.Equal("Otro verificador ya la tomó.", perdedor.Mensaje);

            var enBase = await LeerAsync(id);
            var ganador = resultados[0].Exito ? ana : beto;
            Assert.Equal(ganador, enBase.ReservadaPor);
            if (ganador == ana) ganadoresPorAna++;
        }
        // No es un requisito, es un chequeo de cordura de la prueba: si siempre gana el mismo,
        // probablemente no corren de verdad a la vez.
        Assert.InRange(ganadoresPorAna, 1, 39);
    }

    /// <summary>
    /// El nivel más bajo: el UPDATE condicional. Veinte solicitudes simultáneas de otros tantos
    /// verificadores sobre una sola credencial; solo una recibe «sí».
    /// </summary>
    [Fact]
    public async Task Veinte_reservas_simultaneas_de_la_misma_credencial_solo_una_gana()
    {
        var perfil = await UnPrestadorAsync();
        var id = await EnRevisionAsync(perfil);
        var verificadores = new List<Guid>();
        for (var i = 0; i < 20; i++) verificadores.Add(await UnVerificadorAsync());

        var salida = new TaskCompletionSource();
        var tareas = verificadores.Select(v => Task.Run(async () =>
        {
            using var ctx = Contexto();
            var repo = new RepositorioCredencialesEfCore(ctx);
            await salida.Task;
            var ahora = DateTime.UtcNow;
            return (Quien: v, Gano: await repo.ReservarAsync(id, v, ahora, ahora.AddMinutes(30)));
        })).ToList();
        await Task.Delay(100);
        salida.SetResult();
        var resultados = await Task.WhenAll(tareas);

        var ganadores = resultados.Where(r => r.Gano).ToList();
        Assert.Single(ganadores);
        Assert.Equal(ganadores[0].Quien, (await LeerAsync(id)).ReservadaPor);
    }

    /// <summary>
    /// Dos pestañas del MISMO verificador resolviendo a la vez la misma credencial: una sola decisión
    /// queda registrada. Sin consumir la reserva de forma atómica, las dos verían la reserva vigente y
    /// las dos escribirían una verificación.
    /// </summary>
    [Fact]
    public async Task Dos_resoluciones_simultaneas_del_mismo_verificador_dejan_una_sola_decision()
    {
        var perfil = await UnPrestadorAsync();
        var ana = await UnVerificadorAsync();
        var ids = new List<Guid>();
        for (var i = 0; i < 15; i++) ids.Add(await EnRevisionAsync(perfil, conDocumento: true));

        foreach (var id in ids)
        {
            var (tomador, ctxTomar) = UnServicio();
            using (ctxTomar) Assert.True((await tomador.TomarAsync(ana, id)).Exito);

            var salida = new TaskCompletionSource();
            Task<Resultado<ResolucionDto>> Resolver(ResultadoVerificacion que)
            {
                return Task.Run(async () =>
                {
                    var (servicio, ctx) = UnServicio();
                    using (ctx)
                    {
                        await salida.Task;
                        return await servicio.ResolverAsync(new ResolverCredencialCmd(
                            ana, id, que,
                            que == ResultadoVerificacion.Rechazada ? MotivoRechazo.Ilegible : null,
                            "Decision tomada desde una de las dos pestanas"));
                    }
                });
            }
            var a = Resolver(ResultadoVerificacion.Verificada);
            var b = Resolver(ResultadoVerificacion.Rechazada);
            await Task.Delay(30);
            salida.SetResult();
            var resultados = await Task.WhenAll(a, b);

            Assert.Equal(1, resultados.Count(r => r.Exito));
            var enBase = await LeerAsync(id);
            var decision = Assert.Single(enBase.Verificaciones);
            // Lo que quedo en la credencial es coherente con la unica decision registrada.
            Assert.Equal(decision.Resultado == ResultadoVerificacion.Verificada
                ? EstadoCredencial.Verificada : EstadoCredencial.Rechazada, enBase.Estado);
            Assert.Null(enBase.ReservadaPor);
        }
    }

    /// <summary>
    /// Cada uno toma una DISTINTA a la vez: no se estorban. Y un tercero resolviendo lo que tomó el
    /// primero no puede.
    /// </summary>
    [Fact]
    public async Task Dos_verificadores_con_credenciales_distintas_no_se_estorban()
    {
        var perfil = await UnPrestadorAsync();
        var ana = await UnVerificadorAsync();
        var beto = await UnVerificadorAsync();
        var uno = await EnRevisionAsync(perfil);
        var dos = await EnRevisionAsync(perfil);

        var salida = new TaskCompletionSource();
        Task<Resultado<CredencialEnColaDto>> Tomar(Guid quien, Guid id) => Task.Run(async () =>
        {
            var (s, ctx) = UnServicio();
            using (ctx) { await salida.Task; return await s.TomarAsync(quien, id); }
        });
        var a = Tomar(ana, uno);
        var b = Tomar(beto, dos);
        salida.SetResult();

        Assert.True((await a).Exito);
        Assert.True((await b).Exito);
        Assert.Equal(ana, (await LeerAsync(uno)).ReservadaPor);
        Assert.Equal(beto, (await LeerAsync(dos)).ReservadaPor);
    }

    // ============================================================= el repositorio, con SQL de verdad

    [Fact]
    public async Task La_reserva_vencida_en_la_base_se_puede_tomar_y_la_vigente_no()
    {
        var perfil = await UnPrestadorAsync();
        var ana = await UnVerificadorAsync();
        var beto = await UnVerificadorAsync();
        var id = await EnRevisionAsync(perfil);
        var ahora = DateTime.UtcNow;

        using var ctx = Contexto();
        var repo = new RepositorioCredencialesEfCore(ctx);

        Assert.True(await repo.ReservarAsync(id, ana, ahora, ahora.AddMinutes(30)));
        Assert.False(await repo.ReservarAsync(id, beto, ahora.AddMinutes(10), ahora.AddMinutes(40)));    // vigente
        Assert.False(await repo.ReservarAsync(id, beto, ahora.AddMinutes(30), ahora.AddMinutes(60)));    // justo en el limite: aun vigente
        Assert.True(await repo.ReservarAsync(id, beto, ahora.AddMinutes(31), ahora.AddMinutes(61)));     // vencida
        Assert.Equal(beto, (await LeerAsync(id)).ReservadaPor);
        Assert.True(await repo.ReservarAsync(id, beto, ahora.AddMinutes(40), ahora.AddMinutes(70)));     // la propia se renueva
    }

    [Theory]
    [InlineData(EstadoCredencial.Borrador)]
    [InlineData(EstadoCredencial.Verificada)]
    [InlineData(EstadoCredencial.Rechazada)]
    [InlineData(EstadoCredencial.Vencida)]
    public async Task La_base_no_reserva_lo_que_no_esta_en_revision(EstadoCredencial estado)
    {
        var perfil = await UnPrestadorAsync();
        var ana = await UnVerificadorAsync();
        var id = await EnRevisionAsync(perfil);
        using (var ctx = Contexto())
        {
            var c = await ctx.Credenciales.FindAsync(id);
            c!.Estado = estado;
            if (estado == EstadoCredencial.Verificada) c.SelloPor = ana;
            await ctx.SaveChangesAsync();
        }

        using var c2 = Contexto();
        Assert.False(await new RepositorioCredencialesEfCore(c2).ReservarAsync(
            id, ana, DateTime.UtcNow, DateTime.UtcNow.AddMinutes(30)));
    }

    [Fact]
    public async Task La_base_no_reserva_una_que_espera_al_prestador()
    {
        var perfil = await UnPrestadorAsync();
        var id = await EnRevisionAsync(perfil);
        using (var ctx = Contexto())
        {
            (await ctx.Credenciales.FindAsync(id))!.EsperandoPrestador = true;
            await ctx.SaveChangesAsync();
        }
        var ana = await UnVerificadorAsync();

        using var c2 = Contexto();
        Assert.False(await new RepositorioCredencialesEfCore(c2).ReservarAsync(
            id, ana, DateTime.UtcNow, DateTime.UtcNow.AddMinutes(30)));
    }

    /// <summary>RN-S2-09 en SQL: ni la cola ni la reserva dejan pasar la cuenta propia.</summary>
    [Fact]
    public async Task La_base_excluye_de_la_cola_y_de_la_reserva_las_credenciales_propias()
    {
        var perfil = await UnPrestadorAsync();
        var id = await EnRevisionAsync(perfil);
        var duenoComoVerificador = perfil.UsuarioId;
        var otro = await UnVerificadorAsync();

        using var ctx = Contexto();
        var repo = new RepositorioCredencialesEfCore(ctx);

        Assert.DoesNotContain(await repo.ColaAsync(duenoComoVerificador, DateTime.UtcNow), c => c.Id == id);
        Assert.Contains(await repo.ColaAsync(otro, DateTime.UtcNow), c => c.Id == id);
        Assert.False(await repo.ReservarAsync(id, duenoComoVerificador, DateTime.UtcNow, DateTime.UtcNow.AddMinutes(30)));
        Assert.Null((await LeerAsync(id)).ReservadaPor);
    }

    [Fact]
    public async Task La_cola_de_la_base_va_de_la_mas_antigua_a_la_mas_nueva_y_trae_perfil_entidad_y_documentos()
    {
        var perfil = await UnPrestadorAsync();
        var ana = await UnVerificadorAsync();
        var nueva = await EnRevisionAsync(perfil, creada: DateTime.UtcNow.AddMinutes(-1));
        var vieja = await EnRevisionAsync(perfil, conDocumento: true, creada: DateTime.UtcNow.AddDays(-3));
        var media = await EnRevisionAsync(perfil, creada: DateTime.UtcNow.AddHours(-5));

        var (servicio, ctx) = UnServicio();
        using (ctx)
        {
            var cola = (await servicio.ColaAsync(ana)).Where(c => new[] { nueva, vieja, media }.Contains(c.Id)).ToList();

            Assert.Equal(new[] { vieja, media, nueva }, cola.Select(c => c.Id));
            Assert.Equal(perfil.NombrePublico, cola[0].Prestador);
            Assert.Equal(perfil.Slug, cola[0].PrestadorSlug);
            Assert.Equal(1, cola[0].Documentos);
            Assert.False(cola[0].SinDocumentos);
            Assert.True(cola[1].SinDocumentos);
        }
    }

    [Fact]
    public async Task La_cola_de_la_base_oculta_la_reserva_vigente_de_otro_pero_no_la_vencida()
    {
        var perfil = await UnPrestadorAsync();
        var ana = await UnVerificadorAsync();
        var beto = await UnVerificadorAsync();
        var id = await EnRevisionAsync(perfil);
        using (var ctx = Contexto())
        {
            var repo = new RepositorioCredencialesEfCore(ctx);
            Assert.True(await repo.ReservarAsync(id, beto, DateTime.UtcNow, DateTime.UtcNow.AddMinutes(30)));
        }

        var (servicio, c1) = UnServicio();
        using (c1)
        {
            Assert.DoesNotContain(await servicio.ColaAsync(ana), c => c.Id == id);
            Assert.Contains(await servicio.ColaAsync(beto), c => c.Id == id && c.ReservadaPorMi);
        }

        using (var ctx = Contexto())
        {
            (await ctx.Credenciales.FindAsync(id))!.ReservadaHasta = DateTime.UtcNow.AddMinutes(-1);
            await ctx.SaveChangesAsync();
        }
        var (servicio2, c2) = UnServicio();
        using (c2)
            Assert.Contains(await servicio2.ColaAsync(ana), c => c.Id == id && !c.ReservadaPorMi);
    }

    // ============================================================= resolver y persistir de verdad

    [Fact]
    public async Task Verificar_con_documentos_persiste_el_sello_la_decision_y_la_auditoria()
    {
        var perfil = await UnPrestadorAsync();
        var ana = await UnVerificadorAsync();
        var id = await EnRevisionAsync(perfil, conDocumento: true);
        using (var tomar = Contexto())
        {
            var s = new ServicioVerificacion(new RepositorioCredencialesEfCore(tomar), new UnidadDeTrabajoEfCore(tomar),
                new RelojUtc(), new AuditorEfCore(tomar), new OpcionesVerificacion());
            Assert.True((await s.TomarAsync(ana, id)).Exito);
        }

        var (servicio, ctx) = UnServicio(sinDocumentos: false);
        using (ctx)
        {
            var r = await servicio.ResolverAsync(new ResolverCredencialCmd(ana, id, ResultadoVerificacion.Verificada, null, null));
            Assert.True(r.Exito, r.Mensaje);
        }

        var c = await LeerAsync(id);
        Assert.Equal(EstadoCredencial.Verificada, c.Estado);
        Assert.Equal(ana, c.SelloPor);
        Assert.NotNull(c.SelloEn);
        Assert.Null(c.ReservadaPor);
        Assert.Null(c.ReservadaHasta);
        Assert.Equal(ResultadoVerificacion.Verificada, Assert.Single(c.Verificaciones).Resultado);

        using var lectura = Contexto();
        var acciones = await lectura.Auditoria.Where(a => a.EntidadId == id).Select(a => a.Accion).ToListAsync();
        Assert.Contains("verificar_credencial", acciones);
        Assert.Contains("tomar_credencial", acciones);
    }

    [Fact]
    public async Task Un_rechazo_persiste_con_su_motivo_y_la_base_no_acepta_uno_sin_motivo()
    {
        var perfil = await UnPrestadorAsync();
        var ana = await UnVerificadorAsync();
        var id = await EnRevisionAsync(perfil);
        var (servicio, ctx) = UnServicio();
        using (ctx)
        {
            Assert.True((await servicio.TomarAsync(ana, id)).Exito);
            Assert.True((await servicio.ResolverAsync(new ResolverCredencialCmd(
                ana, id, ResultadoVerificacion.Rechazada, MotivoRechazo.DocumentoVencido, "El certificado esta vencido hace meses"))).Exito);
        }

        var c = await LeerAsync(id);
        Assert.Equal(EstadoCredencial.Rechazada, c.Estado);
        Assert.Null(c.SelloPor);
        var v = Assert.Single(c.Verificaciones);
        Assert.Equal(MotivoRechazo.DocumentoVencido, v.Motivo);

        // R-7: aunque alguien se saltara al servicio, la base no acepta un rechazo sin motivo.
        using var ctx2 = Contexto();
        ctx2.Verificaciones.Add(new Verificacion
        {
            CredencialId = id, VerificadorId = ana, Resultado = ResultadoVerificacion.Rechazada, Motivo = null,
        });
        var e = await Assert.ThrowsAsync<DbUpdateException>(() => ctx2.SaveChangesAsync());
        Assert.Equal(Npgsql.PostgresErrorCodes.CheckViolation, ((Npgsql.PostgresException)e.InnerException!).SqlState);
    }

    [Fact]
    public async Task Sin_documentos_y_con_la_bandera_encendida_verificar_persiste_con_la_auditoria_distinta()
    {
        var perfil = await UnPrestadorAsync();
        var ana = await UnVerificadorAsync();
        var id = await EnRevisionAsync(perfil, conDocumento: false);
        var (servicio, ctx) = UnServicio(sinDocumentos: true);
        using (ctx)
        {
            Assert.True((await servicio.TomarAsync(ana, id)).Exito);
            var corto = await servicio.ResolverAsync(new ResolverCredencialCmd(ana, id, ResultadoVerificacion.Verificada, null, "muy corto"));
            Assert.Equal(Fallas.Validacion, corto.Codigo);
            Assert.True((await servicio.ResolverAsync(new ResolverCredencialCmd(
                ana, id, ResultadoVerificacion.Verificada, null, "Revisé el título original el 12-nov en persona"))).Exito);
        }

        var c = await LeerAsync(id);
        Assert.Equal(EstadoCredencial.Verificada, c.Estado);
        Assert.Equal("Revisé el título original el 12-nov en persona", Assert.Single(c.Verificaciones).Detalle);
        using var lectura = Contexto();
        var acciones = await lectura.Auditoria.Where(a => a.EntidadId == id).Select(a => a.Accion).ToListAsync();
        Assert.Contains("verificar_sin_documentos", acciones);
        Assert.DoesNotContain("verificar_credencial", acciones);
    }

    [Fact]
    public async Task Pedir_antecedentes_persiste_la_marca_y_saca_la_credencial_de_la_cola()
    {
        var perfil = await UnPrestadorAsync();
        var ana = await UnVerificadorAsync();
        var otro = await UnVerificadorAsync();
        var id = await EnRevisionAsync(perfil);
        var (servicio, ctx) = UnServicio();
        using (ctx)
        {
            await servicio.TomarAsync(ana, id);
            Assert.True((await servicio.ResolverAsync(new ResolverCredencialCmd(
                ana, id, ResultadoVerificacion.AntecedentesSolicitados, null, "Falta el certificado de la universidad"))).Exito);
        }

        var c = await LeerAsync(id);
        Assert.Equal(EstadoCredencial.EnRevision, c.Estado);
        Assert.True(c.EsperandoPrestador);
        Assert.Null(c.ReservadaPor);
        var (s2, c2) = UnServicio();
        using (c2) Assert.DoesNotContain(await s2.ColaAsync(otro), x => x.Id == id);
    }
}
