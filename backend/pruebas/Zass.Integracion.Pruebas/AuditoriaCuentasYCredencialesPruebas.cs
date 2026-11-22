using Microsoft.EntityFrameworkCore;
using Zass.Aplicacion.Credenciales;
using Zass.Aplicacion.Identidad;
using Zass.Dominio.Catalogo;
using Zass.Dominio.Comun;
using Zass.Dominio.Credenciales;
using Zass.Dominio.Identidad;
using Zass.Dominio.Reputacion;
using Zass.Dominio.Suscripciones;
using Zass.Infraestructura.Datos.Repositorios;
using Zass.Infraestructura.Seguridad;

namespace Zass.Integracion.Pruebas;

/// <summary>
/// Auditoría del 22-nov-2026, hallazgos 2 y 3, contra PostgreSQL: CS-06 (vencer credenciales) y
/// la purga de cuentas sin verificar. Todo dentro de una transacción que se deshace.
/// </summary>
public class AuditoriaCuentasYCredencialesPruebas : IDisposable
{
    private readonly ContextoDePrueba _prueba = new();
    private readonly RelojUtc _reloj = new();
    private Zass.Infraestructura.Datos.ZassDbContext Ctx => _prueba.Ctx;

    public void Dispose() => _prueba.Dispose();

    private ServicioCuentasSinVerificar Purga() => new(
        new RepositorioCuentasSinVerificarEfCore(Ctx), new UnidadDeTrabajoEfCore(Ctx), _reloj, new AuditorEfCore(Ctx));

    private ServicioVencimientoCredenciales Vencimiento() => new(
        new RepositorioCredencialesEfCore(Ctx), new UnidadDeTrabajoEfCore(Ctx), _reloj,
        new AuditorEfCore(Ctx), new ColaNotificacionesEfCore(Ctx));

    /// <summary>
    /// Una cuenta como la deja el registro (rol, perfil, aviso de verificación, sesión), creada hace
    /// <paramref name="hace"/>. Muy vieja por omisión: así sale primera en la lista (orden por fecha)
    /// aunque la base de pruebas tenga restos de otras corridas.
    /// </summary>
    private async Task<(Usuario Usuario, PerfilPrestador? Perfil)> UnaCuentaAsync(
        TimeSpan? hace = null, bool verificada = false, string rol = "prestador")
    {
        var creada = hace is { } h ? _reloj.Ahora - h : new DateTime(2001, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var u = new Usuario
        {
            Correo = $"purga-{Guid.NewGuid():N}@zass.local", HashContrasena = "x", Nombre = "Cuenta de prueba",
            Rut = DatosDeRegistro.Rut(), CorreoVerificado = verificada,
            Estado = verificada ? EstadoCuenta.Activa : EstadoCuenta.PendienteVerificacion,
            CreadoEn = creada, ActualizadoEn = creada,
        };
        var r = await Ctx.Roles.SingleAsync(x => x.Codigo == rol);
        u.Roles.Add(new UsuarioRol { UsuarioId = u.Id, RolId = r.Id, AsignadoEn = creada });
        Ctx.Usuarios.Add(u);
        PerfilPrestador? perfil = null;
        if (rol == "prestador")
        {
            perfil = new PerfilPrestador
            {
                UsuarioId = u.Id, NombrePublico = "Estudio", Slug = $"purga-{Guid.NewGuid():N}",
                Estado = EstadoPerfil.Borrador,
            };
            Ctx.PerfilesPrestador.Add(perfil);
        }
        else Ctx.PerfilesBuscador.Add(new PerfilBuscador { UsuarioId = u.Id });
        Ctx.Notificaciones.Add(new Notificacion
        {
            DestinatarioId = u.Id, Tipo = TipoNotificacion.CorreoVerificacion,
            ClaveIdempotencia = $"verificacion:{u.Id}", Asunto = "Confirma", Cuerpo = "{}", ProgramadaPara = creada,
        });
        Ctx.Auditoria.Add(new RegistroAuditoria
        {
            Accion = "registro_cuenta", Entidad = "usuario", EntidadId = u.Id, ActorId = u.Id, OcurridoEn = creada,
        });
        await Ctx.SaveChangesAsync();
        return (u, perfil);
    }

    private Task<bool> ExisteAsync(Guid id) => Ctx.Usuarios.AsNoTracking().AnyAsync(u => u.Id == id);

    // ------------------------------------------------------------------ hallazgo 3: la purga

    [Fact]
    public async Task La_purga_borra_la_cuenta_vieja_sin_verificar_con_lo_que_creo_el_registro_y_deja_la_auditoria()
    {
        var (u, perfil) = await UnaCuentaAsync();

        await Purga().PurgarVencidasAsync();
        Ctx.ChangeTracker.Clear();

        Assert.False(await ExisteAsync(u.Id));
        Assert.False(await Ctx.PerfilesPrestador.AnyAsync(p => p.Id == perfil!.Id));
        Assert.False(await Ctx.Notificaciones.AnyAsync(n => n.DestinatarioId == u.Id));
        Assert.False(await Ctx.UsuarioRoles.AnyAsync(r => r.UsuarioId == u.Id));
        // El RUT y el correo quedan libres.
        Assert.False(await Ctx.Usuarios.AnyAsync(x => x.Rut == u.Rut || x.Correo == u.Correo));
        // La auditoría no se borra: la del registro sigue, y la purga deja la suya.
        var trazas = await Ctx.Auditoria.Where(a => a.EntidadId == u.Id).Select(a => a.Accion).ToListAsync();
        Assert.Contains("registro_cuenta", trazas);
        Assert.Contains("cuenta_sin_verificar_purgada", trazas);
    }

    [Fact]
    public async Task La_purga_no_toca_una_cuenta_verificada_ni_una_que_aun_esta_en_plazo()
    {
        var (verificada, _) = await UnaCuentaAsync(verificada: true);
        var (enPlazo, _) = await UnaCuentaAsync(hace: ServicioCuentasSinVerificar.PlazoParaVerificar - TimeSpan.FromMinutes(10));
        var (pasada, _) = await UnaCuentaAsync(hace: ServicioCuentasSinVerificar.PlazoParaVerificar + TimeSpan.FromMinutes(10));

        await Purga().PurgarVencidasAsync();

        Assert.True(await ExisteAsync(verificada.Id));
        Assert.True(await ExisteAsync(enPlazo.Id));
        Assert.False(await ExisteAsync(pasada.Id));
    }

    public enum Actividad { Credencial, Suscripcion, PerfilPublicado, Contacto, Cobertura, Reporte, RolInterno }

    /// <summary>
    /// Con cualquier actividad la cuenta NO se borra aunque siga sin verificar: no se pierde nada
    /// que alguien haya hecho. Queda contada como «retenida» en el resumen para que alguien la mire.
    /// </summary>
    [Theory]
    [InlineData(Actividad.Credencial)]
    [InlineData(Actividad.Suscripcion)]
    [InlineData(Actividad.PerfilPublicado)]
    [InlineData(Actividad.Contacto)]
    [InlineData(Actividad.Cobertura)]
    [InlineData(Actividad.Reporte)]
    [InlineData(Actividad.RolInterno)]
    public async Task La_purga_no_borra_una_cuenta_con_actividad_y_la_cuenta_como_retenida(Actividad actividad)
    {
        var (u, perfil) = await UnaCuentaAsync();
        var repo = new RepositorioCuentasSinVerificarEfCore(Ctx);
        var limite = _reloj.Ahora - ServicioCuentasSinVerificar.PlazoParaVerificar;
        var retenidasAntes = await repo.ContarRetenidasAsync(limite);

        switch (actividad)
        {
            case Actividad.Credencial:
                Ctx.Credenciales.Add(new Credencial
                {
                    PerfilPrestadorId = perfil!.Id, Tipo = TipoCredencial.Titulo, Nombre = "Título",
                    ObtenidaEn = new DateOnly(2020, 1, 1),
                });
                break;
            case Actividad.Suscripcion:
                Ctx.Suscripciones.Add(new Suscripcion
                {
                    PerfilPrestadorId = perfil!.Id, PlanId = 1, Estado = EstadoSuscripcion.Prueba,
                    IniciaEn = _reloj.Ahora, VigenteHasta = _reloj.Ahora.AddDays(30),
                });
                break;
            case Actividad.PerfilPublicado:
                perfil!.Estado = EstadoPerfil.Publicado;
                perfil.RubroId = 1;
                perfil.PublicadoEn = _reloj.Ahora;
                break;
            case Actividad.Cobertura:
                Ctx.Coberturas.Add(new CoberturaPrestador
                {
                    PerfilPrestadorId = perfil!.Id, ZonaId = (await Ctx.Zonas.FirstAsync()).Id,
                    EsPrincipal = true, DesdeEn = _reloj.Ahora,
                });
                break;
            case Actividad.Contacto:
            case Actividad.Reporte:
            {
                var (otro, otroPerfil) = await UnaCuentaAsync(verificada: true);
                var contacto = new Contacto
                {
                    BuscadorId = actividad == Actividad.Contacto ? u.Id : otro.Id,
                    PerfilPrestadorId = otroPerfil!.Id, Origen = OrigenContacto.Ficha,
                    Canal = CanalContacto.Telefono, OcurridoEn = _reloj.Ahora,
                };
                Ctx.Contactos.Add(contacto);
                if (actividad == Actividad.Reporte)
                {
                    // Una reseña ajena que esta cuenta reportó.
                    var (autor, _) = await UnaCuentaAsync(verificada: true, rol: "buscador");
                    contacto.BuscadorId = autor.Id;
                    var resena = new Resena
                    {
                        ContactoId = contacto.Id, PerfilPrestadorId = otroPerfil.Id, AutorId = autor.Id,
                        Calificacion = 4, Comentario = "Un comentario de prueba suficientemente largo.",
                    };
                    Ctx.Resenas.Add(resena);
                    Ctx.ReportesResena.Add(new ReporteResena
                    {
                        ResenaId = resena.Id, ReportadoPor = u.Id, Motivo = MotivoReporte.Falsedad,
                    });
                }
                break;
            }
            case Actividad.RolInterno:
                var mod = await Ctx.Roles.SingleAsync(x => x.Codigo == "moderador");
                Ctx.UsuarioRoles.Add(new UsuarioRol { UsuarioId = u.Id, RolId = mod.Id, AsignadoEn = _reloj.Ahora });
                break;
        }
        await Ctx.SaveChangesAsync();

        var resumen = await Purga().PurgarVencidasAsync();

        Assert.True(await ExisteAsync(u.Id));
        Assert.Equal(retenidasAntes + 1, await repo.ContarRetenidasAsync(limite));
        Assert.True(resumen.RetenidasConActividad >= 1);
    }

    [Fact]
    public async Task Purgar_vuelve_a_comprobar_en_el_borrado_si_verifico_entre_medio_no_se_borra()
    {
        var (u, _) = await UnaCuentaAsync();
        var repo = new RepositorioCuentasSinVerificarEfCore(Ctx);
        var limite = _reloj.Ahora - ServicioCuentasSinVerificar.PlazoParaVerificar;
        Assert.Contains(u.Id, await repo.PurgablesAsync(limite, 10_000));

        await Ctx.Usuarios.Where(x => x.Id == u.Id).ExecuteUpdateAsync(s => s
            .SetProperty(x => x.CorreoVerificado, true).SetProperty(x => x.Estado, EstadoCuenta.Activa));

        Assert.False(await repo.PurgarAsync(u.Id, limite));
        Assert.True(await ExisteAsync(u.Id));
    }

    /// <summary>
    /// El candado de la purga: toda tabla que apunta (con clave foránea) a <c>usuarios</c> o a
    /// <c>perfiles_prestador</c> tiene que estar mirada por la condición de «purgable». Si mañana
    /// se agrega una tabla nueva que cuelga de la cuenta, esta prueba se pone roja hasta que
    /// alguien decida si es actividad (no purgar) o algo que se borra con la cuenta.
    /// </summary>
    [Fact]
    public async Task Toda_tabla_que_cuelga_de_la_cuenta_o_del_perfil_esta_mirada_por_la_purga()
    {
        var tablas = await Ctx.Database.SqlQueryRaw<string>("""
            SELECT DISTINCT conrelid::regclass::text AS "Value" FROM pg_constraint
            WHERE contype = 'f' AND confrelid::regclass::text IN ('usuarios', 'perfiles_prestador', 'suscripciones')
            """).ToListAsync();

        Assert.NotEmpty(tablas);
        var faltan = tablas.Where(t => !RepositorioCuentasSinVerificarEfCore.TablasMiradas.Contains(t)).ToList();
        // pagos cuelga de suscripciones, que ya excluye la cuenta.
        faltan.Remove("pagos");
        Assert.Empty(faltan);
    }

    // ------------------------------------------------------------------ hallazgo 2: CS-06

    private async Task<Credencial> UnaVerificadaAsync(PerfilPrestador perfil, DateOnly? vence, string nombre = "Certificado SEC")
    {
        var c = new Credencial
        {
            PerfilPrestadorId = perfil.Id, Tipo = TipoCredencial.Certificacion, Nombre = nombre,
            ObtenidaEn = new DateOnly(2020, 1, 1), VenceEn = vence,
            Estado = EstadoCredencial.Verificada, SelloEn = _reloj.Ahora, SelloPor = Guid.NewGuid(),
        };
        Ctx.Credenciales.Add(c);
        await Ctx.SaveChangesAsync();
        return c;
    }

    [Fact]
    public async Task CS06_contra_la_base_vence_avisa_al_dueño_y_deja_traza_una_sola_vez()
    {
        var (dueno, perfil) = await UnaCuentaAsync(verificada: true);
        var hoy = HoraDeChile.DiaLocal(_reloj.Ahora);
        var vencida = await UnaVerificadaAsync(perfil!, hoy.AddDays(-1), "Licencia clase A2");
        var deHoy = await UnaVerificadaAsync(perfil!, hoy);

        await Vencimiento().VencerAsync();
        await Vencimiento().VencerAsync();
        Ctx.ChangeTracker.Clear();

        Assert.Equal(EstadoCredencial.Vencida, (await Ctx.Credenciales.SingleAsync(c => c.Id == vencida.Id)).Estado);
        Assert.Equal(EstadoCredencial.Verificada, (await Ctx.Credenciales.SingleAsync(c => c.Id == deHoy.Id)).Estado);
        var aviso = await Ctx.Notificaciones.SingleAsync(n =>
            n.DestinatarioId == dueno.Id && n.Tipo == TipoNotificacion.CredencialVencida);
        Assert.Contains("Licencia clase A2", aviso.Cuerpo);
        Assert.Equal(1, await Ctx.Auditoria.CountAsync(a => a.Accion == "credencial.vencida" && a.EntidadId == vencida.Id));
    }

    [Fact]
    public async Task El_sello_publico_de_una_verificada_pasada_de_fecha_se_apaga_antes_de_que_corra_CS06()
    {
        var (_, perfil) = await UnaCuentaAsync(verificada: true);
        await UnaVerificadaAsync(perfil!, HoraDeChile.DiaLocal(_reloj.Ahora).AddDays(-2));
        var servicio = new ServicioCredenciales(
            new RepositorioCredencialesEfCore(Ctx), new UnidadDeTrabajoEfCore(Ctx), _reloj, new AuditorEfCore(Ctx));

        Assert.False(await servicio.TieneVerificadasAsync(perfil!.Id));
        Assert.Equal("Declarada por el usuario", Assert.Single(await servicio.CredencialesPublicasAsync(perfil.Id)).Etiqueta);
    }
}
