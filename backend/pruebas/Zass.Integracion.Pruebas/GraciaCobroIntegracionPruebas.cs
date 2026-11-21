using Microsoft.EntityFrameworkCore;
using Npgsql;
using Zass.Aplicacion.Suscripciones;
using Zass.Dominio.Comun;
using Zass.Dominio.Identidad;
using Zass.Dominio.Suscripciones;
using Zass.Infraestructura.Datos;
using Zass.Infraestructura.Datos.Repositorios;
using Zass.Infraestructura.Seguridad;

namespace Zass.Integracion.Pruebas;

/// <summary>
/// ES-1 · La gracia de 7 dias del cobro fundador, contra PostgreSQL de verdad.
///
/// <para>
/// Las pruebas de aplicacion (<c>CobroAutomaticoPruebas.ES1_*</c>) cubren el comportamiento:
/// un correo por periodo, un reintento por dia, nada si hay un Pendiente, el cupo perdido al
/// vencer. Lo que ninguna de ellas puede decir es lo que vive en la base:
/// </para>
/// <list type="bullet">
/// <item>que dos procesos reintentando el MISMO dia a la vez dejen UN intento: lo que lo
/// impide no es el <c>if</c>, es el indice unico <c>ux_intento_pago_clave</c>;</item>
/// <item>que el <c>StartsWith</c> de <c>IntentosPorPrefijoDeClaveAsync</c> se traduzca a un
/// LIKE que trae el cobro del periodo y sus reintentos, y nada de otro periodo ni de otra
/// suscripcion (en memoria es un <c>string.StartsWith</c> y no prueba la traduccion);</item>
/// <item>que el aviso <c>CobroRechazado</c> quepa en la columna y salga uno por periodo con
/// la cola de verdad (R-18).</item>
/// </list>
///
/// <para>
/// Igual que <see cref="CobroAutomaticoIntegracionPruebas"/>, NO llama a
/// <see cref="ServicioCobros.CobrarVencimientosAsync"/>: ese barrido cobra a todas las
/// suscripciones de la base compartida y le cambiaria el estado a los datos de otras pruebas.
/// </para>
/// </summary>
[Collection(ColeccionCobroConcurrencia.Nombre)]
public class GraciaCobroIntegracionPruebas : IDisposable
{
    private readonly List<Guid> _usuariosCreados = new();

    public void Dispose()
    {
        if (_usuariosCreados.Count == 0) return;

        using var ctx = ContextoDePrueba.Crear();
        foreach (var id in _usuariosCreados)
        {
            ctx.Database.ExecuteSqlRaw(
                """
                DELETE FROM notificaciones WHERE destinatario_id = {0};
                DELETE FROM intentos_de_pago WHERE perfil_prestador_id IN
                    (SELECT id FROM perfiles_prestador WHERE usuario_id = {0});
                DELETE FROM suscripciones WHERE perfil_prestador_id IN
                    (SELECT id FROM perfiles_prestador WHERE usuario_id = {0});
                DELETE FROM perfiles_prestador WHERE usuario_id = {0};
                DELETE FROM usuarios WHERE id = {0};
                """,
                id);
        }
    }

    /// <summary>Un fundador con tarjeta, a 5 dias de vencer: en plena gracia.</summary>
    private async Task<(Suscripcion S, Guid UsuarioId)> UnFundadorEnGraciaAsync()
    {
        using var ctx = ContextoDePrueba.Crear();

        var usuario = new Usuario
        {
            Correo = $"gracia-{Guid.NewGuid():N}@zass.local",
            HashContrasena = "x",
            Nombre = "Fundador en gracia",
            CorreoVerificado = true,
        };
        var perfil = new PerfilPrestador
        {
            UsuarioId = usuario.Id,
            NombrePublico = $"Estudio {Guid.NewGuid():N}",
            Slug = $"gracia-{Guid.NewGuid():N}",
        };
        var suscripcion = new Suscripcion
        {
            PerfilPrestadorId = perfil.Id,
            PlanId = 3,                                   // fundador
            Estado = EstadoSuscripcion.Prueba,
            IniciaEn = DateTime.UtcNow.AddDays(-25),
            VigenteHasta = DateTime.UtcNow.AddDays(5),
            ReferenciaPasarela = "medio-inscrito-de-prueba",
        };

        ctx.Usuarios.Add(usuario);
        ctx.PerfilesPrestador.Add(perfil);
        ctx.Suscripciones.Add(suscripcion);
        await ctx.SaveChangesAsync();

        _usuariosCreados.Add(usuario.Id);
        return (suscripcion, usuario.Id);
    }

    private static IntentoDePago Intento(
        Suscripcion s, string clave, EstadoIntentoPago estado, DateTime creadoEn) => new()
    {
        SuscripcionId = s.Id,
        PerfilPrestadorId = s.PerfilPrestadorId,
        PlanId = s.PlanId,
        Tipo = TipoDeIntento.Cobro,
        Meses = 1,
        MontoBruto = 5990,
        MontoDescuento = 0,
        Monto = 5990,
        Moneda = "CLP",
        Estado = estado,
        ClaveIdempotencia = clave,
        Pasarela = "fijada",
        CreadoEn = creadoEn,
    };

    // ------------------------------------------------------------------------------

    /// <summary>
    /// Dos procesos que reintentan el mismo dia a la vez: los dos leyeron «no hay reintento
    /// de hoy» y los dos insertan con la clave <c>…:reintento:yyyy-MM-dd</c>. Uno gana; el
    /// otro choca con el indice. Un solo intento, y por tanto un solo cobro a la tarjeta.
    /// </summary>
    [Fact]
    public async Task ES1_dos_procesos_reintentando_el_mismo_dia_a_la_vez_dejan_UN_intento()
    {
        var (s, _) = await UnFundadorEnGraciaAsync();
        var dia = new DateOnly(2026, 12, 17);
        var reintento = ServicioCobros.ClaveDeReintento(
            ServicioCobros.ClaveDeCobroAutomatico(s.Id, s.VigenteHasta), dia);

        // Cada proceso arma SU clave, como cada replica del planificador: si la clave dejara de
        // salir igual de los mismos datos, los dos insertarian y esta prueba lo diria.
        async Task<bool> Inserta()
        {
            var suClave = ServicioCobros.ClaveDeReintento(
                ServicioCobros.ClaveDeCobroAutomatico(s.Id, s.VigenteHasta), dia);
            using var ctx = ContextoDePrueba.Crear();
            ctx.IntentosDePago.Add(Intento(s, suClave, EstadoIntentoPago.Pendiente, DateTime.UtcNow));
            try
            {
                await ctx.SaveChangesAsync();
                return true;
            }
            catch (DbUpdateException e) when (e.InnerException is PostgresException
                                              { SqlState: "23505" } pg
                                              && pg.Message.Contains("ux_intento_pago_clave"))
            {
                return false;
            }
        }

        var resultados = await Task.WhenAll(Task.Run(Inserta), Task.Run(Inserta));

        Assert.Single(resultados, ganó => ganó);

        using var comprobacion = ContextoDePrueba.Crear();
        Assert.Equal(1, await comprobacion.IntentosDePago.CountAsync(i => i.SuscripcionId == s.Id));
        Assert.Equal(1, await comprobacion.IntentosDePago.CountAsync(i => i.ClaveIdempotencia == reintento));
    }

    /// <summary>
    /// La gracia se decide mirando juntos el cobro del periodo y sus reintentos. La consulta
    /// tiene que traer exactamente esos: ni el periodo siguiente (que es otro cobro) ni los
    /// de otra suscripcion. Y en orden de creacion.
    /// </summary>
    [Fact]
    public async Task ES1_el_prefijo_trae_el_cobro_del_periodo_y_sus_reintentos_y_nada_mas()
    {
        var (s, _) = await UnFundadorEnGraciaAsync();
        var (otra, _) = await UnFundadorEnGraciaAsync();
        var delPeriodo = ServicioCobros.ClaveDeCobroAutomatico(s.Id, s.VigenteHasta);
        var t0 = DateTime.UtcNow.AddDays(-2);

        using (var ctx = ContextoDePrueba.Crear())
        {
            ctx.IntentosDePago.AddRange(
                Intento(s, delPeriodo, EstadoIntentoPago.Fallido, t0),
                Intento(s, ServicioCobros.ClaveDeReintento(delPeriodo, new DateOnly(2026, 12, 17)),
                    EstadoIntentoPago.Fallido, t0.AddDays(1)),
                Intento(s, ServicioCobros.ClaveDeReintento(delPeriodo, new DateOnly(2026, 12, 18)),
                    EstadoIntentoPago.Pendiente, t0.AddDays(2)),
                // El periodo siguiente de la MISMA suscripcion: no es de esta gracia.
                Intento(s, ServicioCobros.ClaveDeCobroAutomatico(s.Id, s.VigenteHasta.AddMonths(1)),
                    EstadoIntentoPago.Pendiente, t0),
                // Otra suscripcion, mismo vencimiento: tampoco.
                Intento(otra, ServicioCobros.ClaveDeCobroAutomatico(otra.Id, s.VigenteHasta),
                    EstadoIntentoPago.Fallido, t0));
            await ctx.SaveChangesAsync();
        }

        using var lectura = ContextoDePrueba.Crear();
        var repo = new RepositorioSuscripcionesEfCore(lectura);
        var encontrados = await repo.IntentosPorPrefijoDeClaveAsync(delPeriodo);

        Assert.Equal(3, encontrados.Count);
        Assert.All(encontrados, i => Assert.StartsWith(delPeriodo, i.ClaveIdempotencia));
        Assert.Equal(
            new[] { EstadoIntentoPago.Fallido, EstadoIntentoPago.Fallido, EstadoIntentoPago.Pendiente },
            encontrados.Select(i => i.Estado));
    }

    /// <summary>
    /// El correo «actualiza tu tarjeta», con la cola y la base de verdad: dos rechazos del
    /// mismo periodo (el cobro y el reintento de manana) dejan UNA fila en notificaciones, y
    /// el tipo nuevo cabe en su columna.
    /// </summary>
    [Fact]
    public async Task ES1_el_aviso_de_cobro_rechazado_es_uno_por_periodo_en_la_base()
    {
        var (s, usuarioId) = await UnFundadorEnGraciaAsync();

        for (var vez = 0; vez < 2; vez++)
        {
            // Un contexto por corrida, como el planificador: cada ciclo tiene su propio ambito.
            using var ctx = ContextoDePrueba.Crear();
            var servicio = new ServicioSuscripciones(
                new RepositorioSuscripcionesEfCore(ctx), new UnidadDeTrabajoEfCore(ctx),
                new RelojUtc(), new AuditorEfCore(ctx), new ColaNotificacionesEfCore(ctx));
            await servicio.AvisarCobroRechazadoAsync(s.PerfilPrestadorId, s.Id, s.VigenteHasta);
        }

        using var comprobacion = ContextoDePrueba.Crear();
        var aviso = Assert.Single(
            await comprobacion.Notificaciones.Where(n => n.DestinatarioId == usuarioId).ToListAsync());
        Assert.Equal(TipoNotificacion.CobroRechazado, aviso.Tipo);
        Assert.Equal($"CobroRechazado:{s.Id:D}:{s.VigenteHasta:yyyy-MM-dd}", aviso.ClaveIdempotencia);
        Assert.Contains("fundador", aviso.Cuerpo);
    }
}
