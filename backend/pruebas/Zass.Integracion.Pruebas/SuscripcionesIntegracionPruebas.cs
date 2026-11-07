using Microsoft.EntityFrameworkCore;
using Npgsql;
using Zass.Aplicacion.Suscripciones;
using Zass.Dominio.Comun;
using Zass.Dominio.Identidad;
using Zass.Dominio.Suscripciones;
using Zass.Infraestructura.Datos.Repositorios;
using Zass.Infraestructura.Seguridad;

namespace Zass.Integracion.Pruebas;

/// <summary>
/// MOD-13 contra PostgreSQL de verdad.
///
/// Las pruebas de aplicacion ya cubrieron las reglas con listas en memoria. Estas
/// cubren lo que una lista no puede contestar: si el indice unico parcial rechaza de
/// verdad, si el plan quedo sembrado en el esquema, y si el filtro de vigencia escrito
/// en SQL dice exactamente lo mismo que el metodo de C#.
/// </summary>
public class SuscripcionesIntegracionPruebas : IDisposable
{
    private readonly ContextoDePrueba _prueba = new();
    private readonly RelojUtc _reloj = new();
    private readonly RepositorioSuscripcionesEfCore _repo;
    private readonly ServicioSuscripciones _servicio;

    public SuscripcionesIntegracionPruebas()
    {
        var ctx = _prueba.Ctx;
        _repo = new RepositorioSuscripcionesEfCore(ctx);
        _servicio = new ServicioSuscripciones(
            _repo, new UnidadDeTrabajoEfCore(ctx), _reloj, new AuditorEfCore(ctx));
    }

    public void Dispose() => _prueba.Dispose();

    /// <summary>Un prestador minimo, suficiente para colgarle una suscripcion.</summary>
    private async Task<PerfilPrestador> UnPrestadorAsync()
    {
        var usuario = new Usuario
        {
            Correo = $"susc-{Guid.NewGuid():N}@zass.local",
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

    // ------------------------------------------------------- El plan sembrado

    // Sin el, IniciarPrueba no tiene con que arrancar y nadie llega nunca a estar
    // vigente: con RN-S3-09, eso seria una busqueda permanentemente vacia.
    [Fact]
    public async Task El_plan_de_prueba_vino_con_el_esquema()
    {
        var plan = await _repo.PlanPorCodigoAsync(ServicioSuscripciones.PlanInicial);

        Assert.NotNull(plan);
        Assert.Equal(30, plan!.DiasPrueba);
        Assert.Equal(0, plan.PrecioMensual);
        Assert.True(plan.Activo);
    }

    // ------------------------------------------------------- Lo que impone la base

    /// <summary>
    /// ux_suscripcion_vigente. El servicio ya evita el caso con una comprobacion previa,
    /// pero dos peticiones a la vez pasarian las dos esa comprobacion. Quien tiene que
    /// rechazar de verdad es la base, y eso se prueba saltandose el servicio.
    /// </summary>
    [Fact]
    public async Task La_base_rechaza_dos_suscripciones_vigentes_del_mismo_prestador()
    {
        var perfil = await UnPrestadorAsync();
        var ahora = _reloj.Ahora;

        _prueba.Ctx.Suscripciones.Add(new Suscripcion
        {
            PerfilPrestadorId = perfil.Id, PlanId = 1,
            Estado = EstadoSuscripcion.Activa,
            IniciaEn = ahora, VigenteHasta = ahora.AddDays(30),
        });
        await _prueba.Ctx.SaveChangesAsync();

        _prueba.Ctx.Suscripciones.Add(new Suscripcion
        {
            PerfilPrestadorId = perfil.Id, PlanId = 1,
            Estado = EstadoSuscripcion.Prueba,
            IniciaEn = ahora, VigenteHasta = ahora.AddDays(30),
        });

        var error = await Assert.ThrowsAsync<DbUpdateException>(
            () => _prueba.Ctx.SaveChangesAsync());
        var pg = Assert.IsType<PostgresException>(error.InnerException);
        Assert.Equal("23505", pg.SqlState);
        Assert.Contains("ux_suscripcion_vigente", pg.Message);
    }

    /// <summary>
    /// La otra mitad del indice parcial: el historico no puede estorbar. Si el filtro
    /// estuviera mal, un prestador no podria volver despues de una cancelacion.
    /// </summary>
    [Fact]
    public async Task El_historico_vencido_no_impide_una_suscripcion_nueva()
    {
        var perfil = await UnPrestadorAsync();
        var ahora = _reloj.Ahora;

        foreach (var estado in new[] { EstadoSuscripcion.Vencida, EstadoSuscripcion.Cancelada })
            _prueba.Ctx.Suscripciones.Add(new Suscripcion
            {
                PerfilPrestadorId = perfil.Id, PlanId = 1, Estado = estado,
                IniciaEn = ahora.AddDays(-90), VigenteHasta = ahora.AddDays(-60),
            });
        await _prueba.Ctx.SaveChangesAsync();

        var r = await _servicio.IniciarPruebaAsync(perfil.Id);

        Assert.True(r.Exito, r.Mensaje);
        Assert.True(await _servicio.EstaVigenteAsync(perfil.Id));
    }

    // ------------------------------------------------------- SQL contra C#

    /// <summary>
    /// El repositorio repite la condicion de vigencia en LINQ para que EF Core la pueda
    /// traducir a SQL; la entidad la tiene en C#. Son dos copias de la misma regla, y
    /// esta prueba existe para que no se separen en silencio.
    /// </summary>
    [Fact]
    public async Task El_filtro_en_SQL_dice_lo_mismo_que_EstaVigente_en_memoria()
    {
        var ahora = _reloj.Ahora;
        var casos = new[]
        {
            (EstadoSuscripcion.Activa,    ahora.AddDays(5)),
            (EstadoSuscripcion.Activa,    ahora.AddDays(-5)),
            (EstadoSuscripcion.Prueba,    ahora.AddDays(5)),
            (EstadoSuscripcion.Prueba,    ahora.AddDays(-5)),
            (EstadoSuscripcion.Vencida,   ahora.AddDays(5)),
            (EstadoSuscripcion.Cancelada, ahora.AddDays(5)),
        };

        foreach (var (estado, hasta) in casos)
        {
            var perfil = await UnPrestadorAsync();
            var s = new Suscripcion
            {
                PerfilPrestadorId = perfil.Id, PlanId = 1, Estado = estado,
                IniciaEn = ahora.AddDays(-1), VigenteHasta = hasta,
            };
            _prueba.Ctx.Suscripciones.Add(s);
            await _prueba.Ctx.SaveChangesAsync();

            var segunSql = await _servicio.EstaVigenteAsync(perfil.Id);
            var segunDominio = s.EstaVigente(ahora);

            Assert.True(segunSql == segunDominio,
                $"{estado} hasta {hasta:o}: SQL dijo {segunSql} y el dominio {segunDominio}");
        }
    }

    // ------------------------------------------------------- La consulta en lote

    [Fact]
    public async Task VigentesEntre_devuelve_el_subconjunto_correcto_desde_la_base()
    {
        var ahora = _reloj.Ahora;
        var vigente = await UnPrestadorAsync();
        var vencido = await UnPrestadorAsync();
        var sinNada = await UnPrestadorAsync();

        _prueba.Ctx.Suscripciones.AddRange(
            new Suscripcion
            {
                PerfilPrestadorId = vigente.Id, PlanId = 1, Estado = EstadoSuscripcion.Activa,
                IniciaEn = ahora, VigenteHasta = ahora.AddDays(10),
            },
            new Suscripcion
            {
                PerfilPrestadorId = vencido.Id, PlanId = 1, Estado = EstadoSuscripcion.Vencida,
                IniciaEn = ahora.AddDays(-60), VigenteHasta = ahora.AddDays(-30),
            });
        await _prueba.Ctx.SaveChangesAsync();

        var vigentes = await _servicio.VigentesEntreAsync(
            new[] { vigente.Id, vencido.Id, sinNada.Id });

        Assert.Equal(new[] { vigente.Id }, vigentes);
    }

    // ------------------------------------------------------- Ciclo completo

    [Fact]
    public async Task Prueba_vencimiento_y_pago_sobre_la_base_real()
    {
        var perfil = await UnPrestadorAsync();

        await _servicio.IniciarPruebaAsync(perfil.Id);
        Assert.True(await _servicio.EstaVigenteAsync(perfil.Id));

        // Se envejece la suscripcion para no esperar 30 dias.
        var s = await _repo.VigenteDePrestadorAsync(perfil.Id, _reloj.Ahora);
        s!.VigenteHasta = _reloj.Ahora.AddMinutes(-1);
        await _prueba.Ctx.SaveChangesAsync();

        // Ya no esta vigente aunque siga marcada 'Prueba': manda la fecha.
        Assert.False(await _servicio.EstaVigenteAsync(perfil.Id));

        await _servicio.VencerExpiradasAsync();
        Assert.Equal(EstadoSuscripcion.Vencida,
            (await _repo.UltimaDePrestadorAsync(perfil.Id))!.Estado);

        await _servicio.RegistrarPeriodoPagadoAsync(s.Id, 1, 9990, "trx-integracion");

        Assert.True(await _servicio.EstaVigenteAsync(perfil.Id));
        var pago = await _prueba.Ctx.Pagos.SingleAsync(p => p.SuscripcionId == s.Id);
        Assert.Equal(9990, pago.Monto);
        Assert.Equal(EstadoPago.Pagado, pago.Estado);
    }

    [Fact]
    public async Task La_prueba_dura_los_dias_que_dice_el_plan_sembrado()
    {
        var perfil = await UnPrestadorAsync();
        var antes = _reloj.Ahora;

        await _servicio.IniciarPruebaAsync(perfil.Id);

        var s = await _repo.VigenteDePrestadorAsync(perfil.Id, _reloj.Ahora);
        Assert.NotNull(s);
        var dias = (s!.VigenteHasta - antes).TotalDays;
        Assert.InRange(dias, 29.9, 30.1);
    }

    [Fact]
    public async Task La_consulta_del_panel_trae_el_nombre_del_plan_desde_la_base()
    {
        var perfil = await UnPrestadorAsync();
        await _servicio.IniciarPruebaAsync(perfil.Id);

        var r = await _servicio.DePrestadorAsync(perfil.Id);

        Assert.True(r.Exito);
        Assert.Equal("Prueba", r.Valor!.Plan);
        Assert.True(r.Valor.Vigente);
        Assert.True(r.Valor.EnPrueba);
        Assert.InRange(r.Valor.DiasRestantes, 29, 30);
    }

    [Fact]
    public async Task Iniciar_prueba_deja_rastro_en_la_auditoria()
    {
        var perfil = await UnPrestadorAsync();

        await _servicio.IniciarPruebaAsync(perfil.Id);

        var hay = await _prueba.Ctx.Auditoria
            .AnyAsync(r => r.Accion == "suscripcion_prueba_iniciada");
        Assert.True(hay);
    }
}
