using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit.Sdk;
using Zass.Aplicacion.Suscripciones;
using Zass.Dominio.Identidad;
using Zass.Dominio.Suscripciones;
using Zass.Infraestructura.Datos.Repositorios;
using Zass.Infraestructura.Seguridad;

namespace Zass.Integracion.Pruebas;

/// <summary>
/// Estas pruebas van solas: no se ejecutan en paralelo con ninguna otra clase. Los cupos
/// son filas compartidas y la reserva usa SKIP LOCKED, asi que una transaccion ajena con
/// un cupo bloqueado haria que esta recibiera menos de 20.
/// </summary>
[CollectionDefinition(Nombre, DisableParallelization = true)]
public sealed class ColeccionCuposFundador
{
    public const string Nombre = "CuposFundador";
}

/// <summary>
/// TBL-33 contra PostgreSQL de verdad.
///
/// Las pruebas de aplicacion ya cubrieron las reglas con listas en memoria. Aqui se prueba lo
/// que una lista no puede contestar: que el sembrado quedo en el esquema, que el indice unico
/// parcial rechaza, que los parametros de Npgsql funcionan, y sobre todo que treinta personas
/// pidiendo cupo a la vez se reparten exactamente veinte, sin repetir ninguno.
/// </summary>
[Collection(ColeccionCuposFundador.Nombre)]
public class CuposFundadorIntegracionPruebas : IDisposable
{
    private readonly ContextoDePrueba _prueba;
    private readonly RepositorioSuscripcionesEfCore _repo;
    private readonly ServicioSuscripciones _servicio;

    public CuposFundadorIntegracionPruebas()
    {
        // Por si una corrida anterior murio a medias y dejo cupos ocupados. La base de
        // pruebas es solo de pruebas: nadie mas la usa.
        LiberarTodos();

        _prueba = new ContextoDePrueba();
        var ctx = _prueba.Ctx;
        _repo = new RepositorioSuscripcionesEfCore(ctx);
        _servicio = new ServicioSuscripciones(
            _repo, new UnidadDeTrabajoEfCore(ctx), new RelojUtc(), new AuditorEfCore(ctx),
            new ColaNotificacionesEfCore(ctx));
    }

    public void Dispose() => _prueba.Dispose();

    // ------------------------------------------------------------ Ayudas

    private static void LiberarTodos()
    {
        using var ctx = ContextoDePrueba.Crear();
        ctx.Database.ExecuteSqlRaw(
            "UPDATE cupos_fundador SET perfil_prestador_id = NULL, reservado_hasta = NULL");
    }

    private static (Usuario, PerfilPrestador) UnoNuevo()
    {
        var usuario = new Usuario
        {
            Correo = $"cupo-{Guid.NewGuid():N}@zass.local",
            HashContrasena = "x",
            Nombre = "Prestador de prueba",
            CorreoVerificado = true,
        };
        var perfil = new PerfilPrestador
        {
            UsuarioId = usuario.Id,
            NombrePublico = $"Estudio {Guid.NewGuid():N}",
            Slug = $"estudio-{Guid.NewGuid():N}",
        };
        return (usuario, perfil);
    }

    /// <summary>Un prestador dentro de la transaccion de la prueba (se deshace al terminar).</summary>
    private async Task<PerfilPrestador> UnPrestadorAsync()
    {
        var (usuario, perfil) = UnoNuevo();
        _prueba.Ctx.Usuarios.Add(usuario);
        _prueba.Ctx.PerfilesPrestador.Add(perfil);
        await _prueba.Ctx.SaveChangesAsync();
        return perfil;
    }

    private Task<List<CupoFundador>> Cupos() =>
        _prueba.Ctx.CuposFundador.AsNoTracking().OrderBy(c => c.Numero).ToListAsync();

    private static PostgresException Postgres(Exception e)
    {
        for (Exception? x = e; x is not null; x = x.InnerException)
            if (x is PostgresException pg) return pg;
        throw new XunitException($"No habia PostgresException en la cadena: {e.GetType().Name}.");
    }

    // Para la concurrencia hacen falta filas COMMITEADAS: otras conexiones no ven las de una
    // transaccion abierta. Por eso estas pruebas limpian a mano, en un finally.
    private static async Task<(List<Guid> Perfiles, List<Guid> Usuarios)> CrearConCommitAsync(int cuantos)
    {
        using var ctx = ContextoDePrueba.Crear();
        var perfiles = new List<Guid>();
        var usuarios = new List<Guid>();
        for (var i = 0; i < cuantos; i++)
        {
            var (usuario, perfil) = UnoNuevo();
            ctx.Usuarios.Add(usuario);
            ctx.PerfilesPrestador.Add(perfil);
            usuarios.Add(usuario.Id);
            perfiles.Add(perfil.Id);
        }
        await ctx.SaveChangesAsync();
        return (perfiles, usuarios);
    }

    private static async Task LimpiarConCommitAsync(List<Guid> perfiles, List<Guid> usuarios)
    {
        using var ctx = ContextoDePrueba.Crear();
        await ctx.Database.ExecuteSqlRawAsync(
            "UPDATE cupos_fundador SET perfil_prestador_id = NULL, reservado_hasta = NULL");
        await ctx.PerfilesPrestador.IgnoreQueryFilters()
            .Where(p => perfiles.Contains(p.Id)).ExecuteDeleteAsync();
        await ctx.Usuarios.IgnoreQueryFilters()
            .Where(u => usuarios.Contains(u.Id)).ExecuteDeleteAsync();
    }

    private static async Task<List<CupoFundador>> LeerCuposAsync()
    {
        using var ctx = ContextoDePrueba.Crear();
        return await ctx.CuposFundador.AsNoTracking().OrderBy(c => c.Numero).ToListAsync();
    }

    /// <summary>
    /// Lanza una tarea por perfil, todas con su propio contexto y su propia conexion ya
    /// abierta, y las suelta a la vez. Devuelve lo que cada una obtuvo, en el mismo orden.
    /// </summary>
    private static async Task<short?[]> PedirTodosALaVezAsync(IReadOnlyList<Guid> perfiles)
    {
        var puerta = new TaskCompletionSource();
        var ahora = DateTime.UtcNow;

        var tareas = perfiles.Select(id => Task.Run(async () =>
        {
            using var ctx = ContextoDePrueba.Crear();
            await ctx.Database.OpenConnectionAsync();
            var repo = new RepositorioSuscripcionesEfCore(ctx);
            await puerta.Task;
            return await repo.IntentarReservarCupoFundadorAsync(id, ahora, ahora.AddMinutes(25));
        })).ToArray();

        await Task.Delay(300);
        puerta.SetResult();
        return await Task.WhenAll(tareas);
    }

    // ------------------------------------------------------------ Sembrado

    [Fact]
    public async Task Los_tres_planes_y_los_20_cupos_vinieron_con_el_esquema()
    {
        var planes = await _prueba.Ctx.Planes.AsNoTracking().ToListAsync();

        var prueba = planes.Single(p => p.Codigo == "prueba");
        var mensual = planes.Single(p => p.Codigo == "mensual");
        var fundador = planes.Single(p => p.Codigo == "fundador");
        Assert.Equal(0, prueba.PrecioMensual);
        Assert.Equal(30, prueba.DiasPrueba);
        Assert.Equal(7990, mensual.PrecioMensual);
        Assert.Equal(0, mensual.DiasPrueba);
        Assert.Equal(5990, fundador.PrecioMensual);
        Assert.Equal(0, fundador.DiasPrueba);

        var cupos = await Cupos();
        Assert.Equal(Enumerable.Range(1, 20).Select(n => (short)n), cupos.Select(c => c.Numero));
        Assert.All(cupos, c =>
        {
            Assert.Null(c.PerfilPrestadorId);
            Assert.Null(c.ReservadoHasta);
        });
    }

    // ------------------------------------------------------------ Lo que impone la base

    [Fact]
    public async Task La_base_rechaza_dos_cupos_para_el_mismo_perfil()
    {
        var perfil = await UnPrestadorAsync();

        var e = await Assert.ThrowsAnyAsync<Exception>(() =>
            _prueba.Ctx.Database.ExecuteSqlRawAsync(
                "UPDATE cupos_fundador SET perfil_prestador_id = {0} WHERE numero IN (1, 2)",
                perfil.Id));

        var pg = Postgres(e);
        Assert.Equal("23505", pg.SqlState);
        Assert.Equal("ux_cupo_fundador_perfil", pg.ConstraintName);
    }

    [Fact]
    public async Task Borrar_un_perfil_con_cupo_deja_el_cupo_libre()
    {
        var perfil = await UnPrestadorAsync();
        var ahora = DateTime.UtcNow;
        var numero = await _repo.IntentarReservarCupoFundadorAsync(perfil.Id, ahora, ahora.AddMinutes(25));
        Assert.Equal((short?)1, numero);

        await _prueba.Ctx.PerfilesPrestador.Where(p => p.Id == perfil.Id).ExecuteDeleteAsync();

        var cupo = (await Cupos()).Single(c => c.Numero == 1);
        Assert.Null(cupo.PerfilPrestadorId);
        Assert.Equal(20, await _repo.CuposFundadorDisponiblesAsync(DateTime.UtcNow));
    }

    // ------------------------------------------------------------ El servicio contra la base

    [Fact]
    public async Task Reservar_confirmar_y_liberar_funcionan_de_punta_a_punta()
    {
        var perfil = await UnPrestadorAsync();

        var reservado = await _servicio.ReservarCupoFundadorAsync(perfil.Id);
        Assert.True(reservado.Exito);
        Assert.Equal((short)1, reservado.Valor);
        Assert.Equal(19, await _servicio.CuposFundadorDisponiblesAsync());
        var tras = (await Cupos()).Single(c => c.Numero == 1);
        Assert.Equal(perfil.Id, tras.PerfilPrestadorId);
        Assert.NotNull(tras.ReservadoHasta);

        Assert.True((await _servicio.ConfirmarCupoFundadorAsync(perfil.Id)).Exito);
        var confirmado = (await Cupos()).Single(c => c.Numero == 1);
        Assert.Equal(perfil.Id, confirmado.PerfilPrestadorId);
        Assert.Null(confirmado.ReservadoHasta);

        Assert.True((await _servicio.LiberarCupoFundadorAsync(perfil.Id)).Exito);
        Assert.Equal(20, await _servicio.CuposFundadorDisponiblesAsync());
        Assert.Null((await Cupos()).Single(c => c.Numero == 1).PerfilPrestadorId);
    }

    [Fact]
    public async Task El_trabajo_diario_libera_de_verdad_una_reserva_vencida()
    {
        var perfil = await UnPrestadorAsync();
        var r = await _servicio.ReservarCupoFundadorAsync(perfil.Id, TimeSpan.FromMilliseconds(5));
        Assert.True(r.Exito);
        await Task.Delay(100);

        await _servicio.CorrerDiarioAsync();

        Assert.Null((await Cupos()).Single(c => c.Numero == r.Valor).PerfilPrestadorId);
    }

    // ------------------------------------------------------------ Vencimiento (horas explicitas)

    [Fact]
    public async Task Una_reserva_vencida_la_toma_otro_y_quien_la_perdio_recibe_el_siguiente()
    {
        var a = await UnPrestadorAsync();
        var b = await UnPrestadorAsync();
        var t = DateTime.UtcNow;

        var deA = await _repo.IntentarReservarCupoFundadorAsync(a.Id, t, t.AddMinutes(25));
        Assert.Equal(19, await _repo.CuposFundadorDisponiblesAsync(t.AddMinutes(24)));

        var tarde = t.AddMinutes(26);
        Assert.Equal(20, await _repo.CuposFundadorDisponiblesAsync(tarde));

        var deB = await _repo.IntentarReservarCupoFundadorAsync(b.Id, tarde, tarde.AddMinutes(25));
        Assert.Equal(deA, deB);
        Assert.Equal(b.Id, (await Cupos()).Single(c => c.Numero == deB).PerfilPrestadorId);

        var deANuevo = await _repo.IntentarReservarCupoFundadorAsync(a.Id, tarde, tarde.AddMinutes(25));
        Assert.Equal((short?)2, deANuevo);
    }

    [Fact]
    public async Task La_limpieza_libera_la_reserva_sin_confirmar_y_no_toca_la_confirmada()
    {
        var confirmada = await UnPrestadorAsync();
        var sinConfirmar = await UnPrestadorAsync();
        var t = DateTime.UtcNow;
        var deConfirmada = await _repo.IntentarReservarCupoFundadorAsync(confirmada.Id, t, t.AddMinutes(25));
        var deOtra = await _repo.IntentarReservarCupoFundadorAsync(sinConfirmar.Id, t, t.AddMinutes(25));
        Assert.True(await _repo.ConfirmarCupoFundadorAsync(confirmada.Id));

        var liberadas = await _repo.LiberarReservasFundadorVencidasAsync(t.AddDays(7));

        Assert.Equal(1, liberadas);
        var cupos = await Cupos();
        Assert.Equal(confirmada.Id, cupos.Single(c => c.Numero == deConfirmada).PerfilPrestadorId);
        Assert.Null(cupos.Single(c => c.Numero == deOtra).PerfilPrestadorId);
    }

    // ------------------------------------------------------------ Concurrencia real

    /// <summary>
    /// Lo que justifica todo el diseno: treinta personas piden cupo en el mismo instante y
    /// la base reparte exactamente veinte, cada uno a UNA sola persona. Se repite cinco
    /// veces porque una carrera que sale bien una vez no demuestra nada.
    /// </summary>
    [Fact]
    public async Task Treinta_pidiendo_a_la_vez_se_reparten_exactamente_veinte_cupos_sin_repetir()
    {
        var (perfiles, usuarios) = await CrearConCommitAsync(30);
        try
        {
            for (var ronda = 1; ronda <= 5; ronda++)
            {
                LiberarTodos();

                var resultados = await PedirTodosALaVezAsync(perfiles);

                var ganados = resultados.Where(x => x is not null).Select(x => x!.Value).ToList();
                Assert.True(ganados.Count == 20, $"Ronda {ronda}: ganaron {ganados.Count}, se esperaban 20.");
                Assert.Equal(
                    Enumerable.Range(1, 20).Select(n => (short)n),
                    ganados.OrderBy(n => n));
                Assert.Equal(10, resultados.Count(x => x is null));

                // Lo que cada tarea cree haber ganado coincide con lo que dice la base.
                var cupos = await LeerCuposAsync();
                Assert.Equal(20, cupos.Count(c => c.PerfilPrestadorId is not null));
                for (var i = 0; i < perfiles.Count; i++)
                {
                    if (resultados[i] is short n)
                        Assert.Equal(perfiles[i], cupos.Single(c => c.Numero == n).PerfilPrestadorId);
                }
            }
        }
        finally
        {
            await LimpiarConCommitAsync(perfiles, usuarios);
        }
    }

    /// <summary>
    /// El doble clic: la misma persona pide cupo diez veces a la vez. Todas reciben el mismo
    /// numero y el perfil ocupa UN cupo. Sin el indice unico parcial y la captura del 23505,
    /// aqui se llevaria varios.
    /// </summary>
    [Fact]
    public async Task El_mismo_perfil_pidiendo_diez_veces_a_la_vez_ocupa_un_solo_cupo()
    {
        var (perfiles, usuarios) = await CrearConCommitAsync(1);
        try
        {
            var mismo = Enumerable.Repeat(perfiles[0], 10).ToList();

            for (var ronda = 1; ronda <= 5; ronda++)
            {
                LiberarTodos();

                var resultados = await PedirTodosALaVezAsync(mismo);

                Assert.True(resultados.All(x => x is not null), $"Ronda {ronda}: alguna llamada no recibio cupo.");
                Assert.Single(resultados.Distinct());
                var cupos = await LeerCuposAsync();
                Assert.Equal(1, cupos.Count(c => c.PerfilPrestadorId == perfiles[0]));
                Assert.Equal(19, cupos.Count(c => c.PerfilPrestadorId is null));
            }
        }
        finally
        {
            await LimpiarConCommitAsync(perfiles, usuarios);
        }
    }
}