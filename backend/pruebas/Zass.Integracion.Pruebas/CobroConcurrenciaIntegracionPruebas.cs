using Microsoft.EntityFrameworkCore;
using Npgsql;
using Zass.Aplicacion.Puertos;
using Zass.Aplicacion.Suscripciones;
using Zass.Dominio.Comun;
using Zass.Dominio.Identidad;
using Zass.Dominio.Suscripciones;
using Zass.Infraestructura.Datos;
using Zass.Infraestructura.Datos.Repositorios;
using Zass.Infraestructura.Seguridad;

namespace Zass.Integracion.Pruebas;

/// <summary>
/// Confirmar un cobro, contra PostgreSQL de verdad.
///
/// Las pruebas de aplicación ya cubrieron la idempotencia con listas en memoria, y ahí el
/// doble no tiene bloqueo de fila: devuelve el mismo objeto y la segunda confirmación nunca
/// espera a nadie. O sea que lo que de verdad protege el dinero —el <c>SELECT … FOR UPDATE</c>
/// de <c>TomarIntentoAsync</c> y el índice único <c>ux_pago_intento</c>— **no lo prueba
/// ninguna de ellas**. Eso es lo que se prueba aquí, y solo se puede probar aquí.
///
/// Va en su propia colección sin paralelizar: dos confirmaciones a la vez y una transacción
/// ajena bloqueando filas de pagos se estorbarían entre sí.
/// </summary>
[CollectionDefinition(Nombre, DisableParallelization = true)]
public sealed class ColeccionCobroConcurrencia
{
    public const string Nombre = "CobroConcurrencia";
}

[Collection(ColeccionCobroConcurrencia.Nombre)]
public class CobroConcurrenciaIntegracionPruebas : IDisposable
{
    private readonly List<Guid> _usuariosCreados = new();

    public void Dispose()
    {
        // Por HTTP no, pero aquí sí hay que limpiar: las filas quedan en la base de pruebas.
        if (_usuariosCreados.Count > 0)
        {
            using var ctx = ContextoDePrueba.Crear();
            foreach (var id in _usuariosCreados)
            {
                ctx.Database.ExecuteSqlRaw(
                    """
                    DELETE FROM canjes_de_cupon WHERE perfil_prestador_id IN
                        (SELECT id FROM perfiles_prestador WHERE usuario_id = {0});
                    DELETE FROM pagos WHERE suscripcion_id IN
                        (SELECT s.id FROM suscripciones s
                          JOIN perfiles_prestador p ON p.id = s.perfil_prestador_id
                         WHERE p.usuario_id = {0});
                    DELETE FROM intentos_de_pago WHERE perfil_prestador_id IN
                        (SELECT id FROM perfiles_prestador WHERE usuario_id = {0});
                    UPDATE cupos_fundador SET perfil_prestador_id = NULL, reservado_hasta = NULL
                     WHERE perfil_prestador_id IN
                        (SELECT id FROM perfiles_prestador WHERE usuario_id = {0});
                    DELETE FROM suscripciones WHERE perfil_prestador_id IN
                        (SELECT id FROM perfiles_prestador WHERE usuario_id = {0});
                    DELETE FROM perfiles_prestador WHERE usuario_id = {0};
                    DELETE FROM auditoria WHERE actor_id = {0};
                    DELETE FROM usuarios WHERE id = {0};
                    """,
                    id);
            }
        }
    }

    /// <summary>La pasarela, fijada por la prueba: devuelve el veredicto que se le dice.</summary>
    private sealed class PasarelaFijada : IPasarelaDePagos
    {
        private readonly CobroResuelto _veredicto;

        public PasarelaFijada(CobroResuelto veredicto) => _veredicto = veredicto;

        public string Nombre => "fijada";

        public Task<Resultado<CobroIniciado>> IniciarCobroAsync(
            SolicitudDeCobro s, CancellationToken ct = default) =>
            Task.FromResult(Resultado.Ok(new CobroIniciado("ref_fijada", "https://local.test/pagar")));

        public Task<Resultado<CobroResuelto>> LeerConfirmacionAsync(
            AvisoDePasarela a, CancellationToken ct = default) =>
            Task.FromResult(Resultado.Ok(_veredicto));
    }

    /// <summary>
    /// Sobre un contexto PLANO (`ContextoDePrueba.Crear()`), nunca sobre `new
    /// ContextoDePrueba()`: ese abre una transaccion en su constructor para aislar la prueba,
    /// y entonces `EnTransaccionAsync` ve que ya hay una, no abre la suya y **no commitea**.
    /// Resultado: nada persiste, el `FOR UPDATE` no serializa a nadie y las dos
    /// confirmaciones se creen primeras. Me paso, y el CI lo cazo.
    /// </summary>
    private static ServicioCobros Cobros(ZassDbContext ctx, CobroResuelto veredicto)
    {
        var repo = new RepositorioSuscripcionesEfCore(ctx);
        var trabajo = new UnidadDeTrabajoEfCore(ctx);
        var reloj = new RelojUtc();
        var auditor = new AuditorEfCore(ctx);
        var suscripciones = new ServicioSuscripciones(
            repo, trabajo, reloj, auditor, new ColaNotificacionesEfCore(ctx));

        return new ServicioCobros(
            repo, suscripciones, new PasarelaFijada(veredicto), trabajo, reloj, auditor);
    }

    /// <summary>Un prestador publicado con su suscripción de prueba, ya en la base.</summary>
    private async Task<(Guid Perfil, Guid Suscripcion)> UnPrestadorPublicadoAsync()
    {
        using var ctx = ContextoDePrueba.Crear();

        var usuario = new Usuario
        {
            Correo = $"cobro-{Guid.NewGuid():N}@zass.local",
            HashContrasena = "x",
            Nombre = "Prestador de prueba",
            CorreoVerificado = true,
        };
        var perfil = new PerfilPrestador
        {
            UsuarioId = usuario.Id,
            NombrePublico = $"Estudio {Guid.NewGuid():N}",
            Slug = $"cobro-{Guid.NewGuid():N}",
        };
        var suscripcion = new Suscripcion
        {
            PerfilPrestadorId = perfil.Id,
            PlanId = 1,
            Estado = EstadoSuscripcion.Prueba,
            IniciaEn = DateTime.UtcNow.AddDays(-5),
            VigenteHasta = DateTime.UtcNow.AddDays(10),
        };

        ctx.Usuarios.Add(usuario);
        ctx.PerfilesPrestador.Add(perfil);
        ctx.Suscripciones.Add(suscripcion);
        await ctx.SaveChangesAsync();

        _usuariosCreados.Add(usuario.Id);
        return (perfil.Id, suscripcion.Id);
    }

    /// <summary>Un intento de cobro ya creado y pendiente, con su monto fijado.</summary>
    private static async Task<Guid> UnIntentoPendienteAsync(
        Guid perfil, Guid suscripcion, int monto, short planId = 2)
    {
        using var ctx = ContextoDePrueba.Crear();
        var intento = new IntentoDePago
        {
            SuscripcionId = suscripcion,
            PerfilPrestadorId = perfil,
            PlanId = planId,
            Tipo = TipoDeIntento.Cobro,
            Meses = 1,
            MontoBruto = monto,
            MontoDescuento = 0,
            Monto = monto,
            Moneda = "CLP",
            Estado = EstadoIntentoPago.Pendiente,
            ClaveIdempotencia = $"clave-{Guid.NewGuid():N}",
            Pasarela = "fijada",
            CreadoEn = DateTime.UtcNow,
        };
        ctx.IntentosDePago.Add(intento);
        await ctx.SaveChangesAsync();
        return intento.Id;
    }

    private static async Task<int> PagosDeAsync(Guid intentoId)
    {
        using var ctx = ContextoDePrueba.Crear();
        return await ctx.Pagos.CountAsync(p => p.IntentoDePagoId == intentoId);
    }

    // ------------------------------------------------------------------------------

    [Fact]
    public async Task El_indice_que_todo_esto_vigila_EXISTE_en_la_base()
    {
        // La leccion de este proyecto: no confiar en que la migracion lo creo. Se mira.
        using var ctx = ContextoDePrueba.Crear();
        var conexion = ctx.Database.GetDbConnection();
        await ctx.Database.OpenConnectionAsync();
        using var cmd = conexion.CreateCommand();
        cmd.CommandText =
            "SELECT indexdef FROM pg_indexes WHERE tablename = 'pagos' AND indexname = 'ux_pago_intento'";
        var definicion = (string?)await cmd.ExecuteScalarAsync();

        Assert.NotNull(definicion);
        Assert.Contains("UNIQUE", definicion);
        Assert.Contains("intento_de_pago_id", definicion);
        Assert.Contains("IS NOT NULL", definicion);   // parcial, como se diseno
    }

    [Fact]
    public async Task Dos_confirmaciones_del_MISMO_cobro_a_la_vez_dejan_UN_pago_y_UN_mes()
    {
        var (perfil, suscripcion) = await UnPrestadorPublicadoAsync();
        var intento = await UnIntentoPendienteAsync(perfil, suscripcion, 7990);

        DateTime vigenteAntes;
        using (var ctx = ContextoDePrueba.Crear())
        {
            vigenteAntes = (await ctx.Suscripciones.SingleAsync(s => s.Id == suscripcion)).VigenteHasta;
        }

        var veredicto = new CobroResuelto(intento, "ref_1", Aprobado: true, MontoConfirmado: 7990, null);
        var puerta = new TaskCompletionSource();

        // Las dos salen al mismo instante. Una se queda esperando en el FOR UPDATE de
        // TomarIntentoAsync y, cuando entra, lee el estado ya cambiado.
        var tareas = Enumerable.Range(0, 2).Select(_ => Task.Run(async () =>
        {
            using var ctx = ContextoDePrueba.Crear();
            await ctx.Database.OpenConnectionAsync();
            var cobros = Cobros(ctx, veredicto);
            await puerta.Task;
            return await cobros.ConfirmarAsync(
                new AvisoDePasarela(new Dictionary<string, string>()));
        })).ToArray();

        await Task.Delay(300);
        puerta.SetResult();
        var resultados = await Task.WhenAll(tareas);

        // Las dos tienen que haber ido bien: la segunda no es un error, es un duplicado.
        Assert.All(resultados, r => Assert.True(r.Exito, r.Mensaje));

        // Y una sola de las dos puede haber sido la primera.
        Assert.Equal(1, resultados.Count(r => r.Valor!.Nueva));

        Assert.Equal(1, await PagosDeAsync(intento));

        using var comprobacion = ContextoDePrueba.Crear();
        var s = await comprobacion.Suscripciones.SingleAsync(x => x.Id == suscripcion);
        var i = await comprobacion.IntentosDePago.SingleAsync(x => x.Id == intento);

        Assert.Equal(EstadoIntentoPago.Pagado, i.Estado);
        Assert.Equal(EstadoSuscripcion.Activa, s.Estado);
        // Un mes, no dos: la fecha se movió una sola vez.
        Assert.Equal(vigenteAntes.AddMonths(1), s.VigenteHasta);
    }

    [Fact]
    public async Task El_indice_ux_pago_intento_rechaza_un_segundo_pago_del_mismo_intento()
    {
        var (perfil, suscripcion) = await UnPrestadorPublicadoAsync();
        var intento = await UnIntentoPendienteAsync(perfil, suscripcion, 7990);

        var veredicto = new CobroResuelto(intento, "ref_1", Aprobado: true, MontoConfirmado: 7990, null);
        using (var ctx = ContextoDePrueba.Crear())
        {
            var r = await Cobros(ctx, veredicto).ConfirmarAsync(
                new AvisoDePasarela(new Dictionary<string, string>()));
            Assert.True(r.Exito, r.Mensaje);
        }

        // Ahora se intenta a mano lo que una condición de carrera podría intentar: un
        // segundo pago para el mismo intento, saltándose la comprobación en C#.
        using var ctxDirecto = ContextoDePrueba.Crear();
        ctxDirecto.Pagos.Add(new Pago
        {
            SuscripcionId = suscripcion,
            IntentoDePagoId = intento,
            Monto = 7990,
            Moneda = "CLP",
            Estado = EstadoPago.Pagado,
            PeriodoDesde = DateTime.UtcNow,
            PeriodoHasta = DateTime.UtcNow.AddMonths(1),
            PagadoEn = DateTime.UtcNow,
        });

        var error = await Assert.ThrowsAsync<DbUpdateException>(() => ctxDirecto.SaveChangesAsync());

        // La garantía última no es el «if», es la base.
        var pg = Assert.IsType<PostgresException>(error.InnerException);
        Assert.Equal("23505", pg.SqlState);                      // violación de índice único
        Assert.Contains("ux_pago_intento", pg.Message);
        Assert.Equal(1, await PagosDeAsync(intento));
    }

    [Fact]
    public async Task Un_monto_que_no_coincide_no_extiende_nada_y_no_deja_pago()
    {
        var (perfil, suscripcion) = await UnPrestadorPublicadoAsync();
        var intento = await UnIntentoPendienteAsync(perfil, suscripcion, 7990);

        // La pasarela dice que cobró un peso. Es el aviso manipulado.
        var veredicto = new CobroResuelto(intento, "ref_1", Aprobado: true, MontoConfirmado: 1, null);

        using (var ctx = ContextoDePrueba.Crear())
        {
            var r = await Cobros(ctx, veredicto).ConfirmarAsync(
                new AvisoDePasarela(new Dictionary<string, string>()));
            Assert.False(r.Exito);
        }

        Assert.Equal(0, await PagosDeAsync(intento));

        using var comprobacion = ContextoDePrueba.Crear();
        var i = await comprobacion.IntentosDePago.SingleAsync(x => x.Id == intento);
        var s = await comprobacion.Suscripciones.SingleAsync(x => x.Id == suscripcion);

        Assert.Equal(EstadoIntentoPago.Pendiente, i.Estado);
        Assert.Equal(EstadoSuscripcion.Prueba, s.Estado);
    }
}
