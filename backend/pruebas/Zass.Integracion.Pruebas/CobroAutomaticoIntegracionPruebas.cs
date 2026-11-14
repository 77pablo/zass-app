using Microsoft.EntityFrameworkCore;
using Npgsql;
using Zass.Aplicacion.Suscripciones;
using Zass.Dominio.Comun;
using Zass.Dominio.Identidad;
using Zass.Dominio.Suscripciones;

namespace Zass.Integracion.Pruebas;

/// <summary>
/// El cobro automático (C3), contra PostgreSQL de verdad.
///
/// <para>
/// Las 13 pruebas de aplicación ya cubren su comportamiento con listas en memoria: que cobre
/// 7 días antes, que dos corridas el mismo día cobren una vez, que un rechazo no mueva la
/// vigencia. Lo que NINGUNA de ellas puede decir es si la garantía última existe: la clave del
/// cobro automático es determinista a propósito, y lo que impide que dos réplicas del
/// planificador corriendo a la vez creen dos cobros del mismo mes **no es el <c>if</c> que las
/// comprueba** —entre la lectura y el INSERT cabe la otra corrida—, es el índice único
/// <c>ux_intento_pago_clave</c>. Un índice que nadie mira es un índice que puede no estar: en
/// este proyecto ya pasó que una migración dijo «Done» y dos diagramas estaban rotos.
/// </para>
///
/// <para>
/// <b>Lo que esta clase NO hace, y es deliberado:</b> no llama a
/// <see cref="ServicioCobros.CobrarVencimientosAsync"/>. Ese barrido cobra a **todas** las
/// suscripciones de la base que vencen dentro de 7 días, y la base de pruebas es compartida por
/// las demás suites: cobrarle a los datos de otra prueba le cambiaría el estado por debajo. El
/// barrido se prueba en memoria, donde la base es de la prueba y de nadie más; lo que se prueba
/// aquí es lo único que el doble en memoria no puede tener.
/// </para>
///
/// Va en la colección del cobro, sin paralelizar, por lo mismo que la de confirmar.
/// </summary>
[Collection(ColeccionCobroConcurrencia.Nombre)]
public class CobroAutomaticoIntegracionPruebas : IDisposable
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

    /// <summary>
    /// Un prestador con su suscripción a punto de vencer y su medio de pago inscrito: el
    /// escenario exacto del 16 de diciembre, siete días antes de que se le acabe el mes gratis.
    /// </summary>
    private async Task<Suscripcion> UnaSuscripcionPorVencerAsync()
    {
        using var ctx = ContextoDePrueba.Crear();

        var usuario = new Usuario
        {
            Correo = $"auto-{Guid.NewGuid():N}@zass.local",
            HashContrasena = "x",
            Nombre = "Fundador de prueba",
            CorreoVerificado = true,
        };
        var perfil = new PerfilPrestador
        {
            UsuarioId = usuario.Id,
            NombrePublico = $"Estudio {Guid.NewGuid():N}",
            Slug = $"auto-{Guid.NewGuid():N}",
        };
        var suscripcion = new Suscripcion
        {
            PerfilPrestadorId = perfil.Id,
            PlanId = 3,                                   // fundador
            Estado = EstadoSuscripcion.Prueba,
            IniciaEn = DateTime.UtcNow.AddDays(-23),
            VigenteHasta = DateTime.UtcNow.AddDays(7),
            ReferenciaPasarela = "medio-inscrito-de-prueba",
        };

        ctx.Usuarios.Add(usuario);
        ctx.PerfilesPrestador.Add(perfil);
        ctx.Suscripciones.Add(suscripcion);
        await ctx.SaveChangesAsync();

        _usuariosCreados.Add(usuario.Id);
        return suscripcion;
    }

    private static IntentoDePago IntentoConClave(Suscripcion s, string clave) => new()
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
        Estado = EstadoIntentoPago.Pendiente,
        ClaveIdempotencia = clave,
        Pasarela = "fijada",
        CreadoEn = DateTime.UtcNow,
    };

    // ------------------------------------------------------------------------------

    [Fact]
    public async Task El_indice_de_la_clave_de_idempotencia_EXISTE_y_es_UNICO_en_la_base()
    {
        using var ctx = ContextoDePrueba.Crear();
        var conexion = ctx.Database.GetDbConnection();
        await ctx.Database.OpenConnectionAsync();
        using var cmd = conexion.CreateCommand();
        cmd.CommandText =
            """
            SELECT indexdef FROM pg_indexes
             WHERE tablename = 'intentos_de_pago' AND indexname = 'ux_intento_pago_clave'
            """;
        var definicion = (string?)await cmd.ExecuteScalarAsync();

        Assert.NotNull(definicion);
        Assert.Contains("UNIQUE", definicion);
        Assert.Contains("clave_idempotencia", definicion);
    }

    /// <summary>
    /// La carrera que el <c>if</c> no cubre, hecha a mano: dos corridas del trabajo que leen
    /// «no hay intento con esta clave» y las dos insertan. En memoria las dos ganan; aquí la
    /// segunda se estrella contra el índice, que es el comportamiento que RN-S7-12 promete.
    /// </summary>
    [Fact]
    public async Task Dos_cobros_automaticos_del_MISMO_periodo_no_pueden_coexistir()
    {
        var s = await UnaSuscripcionPorVencerAsync();
        var clave = ServicioCobros.ClaveDeCobroAutomatico(s.Id, s.VigenteHasta);

        using (var primera = ContextoDePrueba.Crear())
        {
            primera.IntentosDePago.Add(IntentoConClave(s, clave));
            await primera.SaveChangesAsync();
        }

        using var segunda = ContextoDePrueba.Crear();
        segunda.IntentosDePago.Add(IntentoConClave(s, clave));

        var error = await Assert.ThrowsAsync<DbUpdateException>(() => segunda.SaveChangesAsync());
        var pg = Assert.IsType<PostgresException>(error.InnerException);

        Assert.Equal("23505", pg.SqlState);                  // violación de índice único
        Assert.Contains("ux_intento_pago_clave", pg.Message);

        using var comprobacion = ContextoDePrueba.Crear();
        Assert.Equal(
            1,
            await comprobacion.IntentosDePago.CountAsync(i => i.ClaveIdempotencia == clave));
    }

    /// <summary>
    /// Y el otro lado, que es lo que hace útil al índice: el mes SIGUIENTE sí se puede cobrar.
    /// Una clave que no distinguiera el período dejaría al prestador pagando un solo mes para
    /// siempre, y eso también es un error de cobro, solo que a favor de quien no paga.
    /// </summary>
    [Fact]
    public async Task El_periodo_SIGUIENTE_si_se_puede_cobrar_con_la_misma_suscripcion()
    {
        var s = await UnaSuscripcionPorVencerAsync();
        var esteMes = ServicioCobros.ClaveDeCobroAutomatico(s.Id, s.VigenteHasta);
        var elSiguiente = ServicioCobros.ClaveDeCobroAutomatico(s.Id, s.VigenteHasta.AddMonths(1));

        Assert.NotEqual(esteMes, elSiguiente);

        using (var ctx = ContextoDePrueba.Crear())
        {
            ctx.IntentosDePago.Add(IntentoConClave(s, esteMes));
            ctx.IntentosDePago.Add(IntentoConClave(s, elSiguiente));
            await ctx.SaveChangesAsync();                    // los dos caben, y tienen que caber
        }

        using var comprobacion = ContextoDePrueba.Crear();
        Assert.Equal(
            2,
            await comprobacion.IntentosDePago.CountAsync(i => i.SuscripcionId == s.Id));
    }

    /// <summary>
    /// El candado con literal. Las pruebas de arriba usan la función para construir la clave,
    /// así que seguirían verdes si la función empezara a devolver la fecha de hoy en vez del
    /// período: las dos corridas del mismo día coincidirían igual. Esto no: el formato está
    /// escrito a mano, y lo que vigila es que la clave NO dependa de cuándo se corre.
    /// </summary>
    [Fact]
    public void La_clave_del_cobro_automatico_sale_del_periodo_y_no_del_dia_en_que_se_corre()
    {
        var suscripcion = new Guid("11111111-2222-3333-4444-555555555555");
        var vence = new DateTime(2026, 12, 23, 12, 0, 0, DateTimeKind.Utc);

        Assert.Equal(
            "cobro-automatico:11111111-2222-3333-4444-555555555555:2026-12-23",
            ServicioCobros.ClaveDeCobroAutomatico(suscripcion, vence));

        // Y lo que de verdad se vigila: que la fecha que lleva sea la del PERÍODO y no la de
        // hoy. Se pide un vencimiento a 37 días para que las dos fechas no puedan coincidir
        // por casualidad el día que corra la prueba.
        var lejano = DateTime.UtcNow.AddDays(37);
        var clave = ServicioCobros.ClaveDeCobroAutomatico(suscripcion, lejano);

        Assert.Contains(lejano.ToString("yyyy-MM-dd"), clave);
        Assert.DoesNotContain(DateTime.UtcNow.ToString("yyyy-MM-dd"), clave);
    }
}
