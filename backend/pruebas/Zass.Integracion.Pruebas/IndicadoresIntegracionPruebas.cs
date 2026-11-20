using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Zass.Aplicacion.Puertos;
using Zass.Dominio.Comun;
using Zass.Dominio.Credenciales;
using Zass.Dominio.Identidad;
using Zass.Dominio.Reputacion;
using Zass.Dominio.Tarjetas;
using Zass.Infraestructura.Datos;
using Zass.Infraestructura.Datos.Repositorios;

namespace Zass.Integracion.Pruebas;

/// <summary>
/// API-60 · JQ-14. Los indicadores con datos armados a mano, donde cada cifra se calcula de cabeza.
///
/// Todo corre dentro de <see cref="ContextoDePrueba"/> (una transacción que se deshace). La base de
/// pruebas puede tener filas de otras pruebas, así que:
/// - las cifras con ventana de tiempo (30 y 7 días) se miden con un «ahora» en el año 2100, donde
///   solo existen las filas de esta prueba;
/// - las cifras de estado (en cola, reportes abiertos, tarjetas sin activar) se miden como la
///   diferencia entre antes y después de sembrar.
/// </summary>
public class IndicadoresIntegracionPruebas
{
    private static readonly DateTime Ahora = new(2100, 3, 1, 12, 0, 0, DateTimeKind.Utc);

    private static DateTime Hace(double dias, double horas = 0) => Ahora.AddDays(-dias).AddHours(-horas);

    [Fact]
    public async Task Cada_cifra_sale_de_cabeza()
    {
        using var prueba = new ContextoDePrueba();
        var ctx = prueba.Ctx;
        var consulta = new ConsultaIndicadoresEfCore(ctx);
        var antes = await consulta.CalcularAsync(Ahora);

        var (prestadora, perfil) = await UnPrestadorAsync(ctx);
        var verificador = await UnUsuarioAsync(ctx, "verif");

        // --- Verificación. Tres resueltas dentro de los 30 días, con 10, 20 y 30 horas entre la
        // creación y la resolución: promedio 20 h. Una sola es rechazo: tasa 1/3.
        Resolver(ctx, perfil.Id, verificador.Id, creada: Hace(10, 10), resuelta: Hace(10), ResultadoVerificacion.Verificada);
        Resolver(ctx, perfil.Id, verificador.Id, creada: Hace(11, 20), resuelta: Hace(11), ResultadoVerificacion.Rechazada);
        Resolver(ctx, perfil.Id, verificador.Id, creada: Hace(12, 30), resuelta: Hace(12), ResultadoVerificacion.AntecedentesSolicitados);
        // Fuera de la ventana: un rechazo de hace 40 días NO cuenta (ni en la tasa ni en el promedio).
        Resolver(ctx, perfil.Id, verificador.Id, creada: Hace(41), resuelta: Hace(40), ResultadoVerificacion.Rechazada);

        // Dos en cola; la más antigua, de 1990 (ninguna fila real de la base es anterior).
        var antigua = new DateTime(1990, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        ctx.Credenciales.Add(UnaCredencial(perfil.Id, EstadoCredencial.EnRevision, antigua));
        ctx.Credenciales.Add(UnaCredencial(perfil.Id, EstadoCredencial.EnRevision, Hace(1)));
        // Un borrador no está en cola.
        ctx.Credenciales.Add(UnaCredencial(perfil.Id, EstadoCredencial.Borrador, Hace(1)));

        // --- Avisos. Dos correos fallidos en los últimos 7 días.
        UnAviso(ctx, prestadora.Id, CanalNotificacion.Correo, EstadoEnvio.Fallida, Hace(1));
        UnAviso(ctx, prestadora.Id, CanalNotificacion.Correo, EstadoEnvio.Fallida, Hace(6));
        // No cuentan: uno de hace 8 días, uno enviado y uno que no es correo.
        UnAviso(ctx, prestadora.Id, CanalNotificacion.Correo, EstadoEnvio.Fallida, Hace(8));
        UnAviso(ctx, prestadora.Id, CanalNotificacion.Correo, EstadoEnvio.Enviada, Hace(1));
        UnAviso(ctx, prestadora.Id, CanalNotificacion.Push, EstadoEnvio.Fallida, Hace(1));

        // --- Reportes. Uno abierto y uno tomado por un moderador cuentan; uno resuelto, no.
        var autor = await UnUsuarioAsync(ctx, "autor");
        var resena = UnaResena(ctx, autor.Id, perfil.Id);
        UnReporte(ctx, resena.Id, verificador.Id, EstadoReporte.Abierto);
        UnReporte(ctx, resena.Id, verificador.Id, EstadoReporte.EnRevision);
        UnReporte(ctx, resena.Id, verificador.Id, EstadoReporte.ResueltoMantiene);

        // --- Tarjetas. Dos emitidas sin activar; una activa no cuenta.
        ctx.Tarjetas.Add(UnaTarjeta(EstadoTarjeta.Emitida, null));
        ctx.Tarjetas.Add(UnaTarjeta(EstadoTarjeta.Emitida, null));
        ctx.Tarjetas.Add(UnaTarjeta(EstadoTarjeta.Activa, perfil.Id));

        await ctx.SaveChangesAsync();
        var despues = await consulta.CalcularAsync(Ahora);

        var v = despues.Verificacion;
        Assert.Equal(3, v.Resueltas);
        Assert.Equal(1, v.Rechazadas);
        Assert.Equal(1.0 / 3, v.TasaDeRechazo!.Value, 6);
        Assert.Equal(20.0, v.HorasPromedioDesdeCreacion!.Value, 6);
        Assert.Equal(2, v.EnCola - antes.Verificacion.EnCola);
        Assert.Equal(antigua, v.MasAntiguaDesde);

        Assert.Equal(2, despues.Avisos.CorreosFallidos);
        Assert.Equal(2, despues.ReportesAbiertos - antes.ReportesAbiertos);
        Assert.Equal(2, despues.TarjetasEmitidasSinActivar - antes.TarjetasEmitidasSinActivar);
        Assert.Equal(Ahora, despues.CalculadoEn);
    }

    [Fact]
    public async Task Sin_resoluciones_en_la_ventana_la_tasa_y_el_promedio_son_nulos()
    {
        using var prueba = new ContextoDePrueba();
        var ctx = prueba.Ctx;
        var (_, perfil) = await UnPrestadorAsync(ctx);
        var verificador = await UnUsuarioAsync(ctx, "verif");
        // Solo una de hace 31 días: fuera.
        Resolver(ctx, perfil.Id, verificador.Id, creada: Hace(32), resuelta: Hace(31), ResultadoVerificacion.Rechazada);
        await ctx.SaveChangesAsync();

        var v = (await new ConsultaIndicadoresEfCore(ctx).CalcularAsync(Ahora)).Verificacion;
        Assert.Equal(0, v.Resueltas);
        Assert.Equal(0, v.Rechazadas);
        Assert.Null(v.TasaDeRechazo);
        Assert.Null(v.HorasPromedioDesdeCreacion);
    }

    // ------------------------------------------------------------- datos

    private static async Task<Usuario> UnUsuarioAsync(ZassDbContext ctx, string prefijo)
    {
        var u = new Usuario
        {
            Correo = $"ind-{prefijo}-{Guid.NewGuid():N}@zass.local", HashContrasena = "x",
            Nombre = "Persona de prueba", CorreoVerificado = true,
        };
        ctx.Usuarios.Add(u);
        await ctx.SaveChangesAsync();
        return u;
    }

    private static async Task<(Usuario, PerfilPrestador)> UnPrestadorAsync(ZassDbContext ctx)
    {
        var u = await UnUsuarioAsync(ctx, "prest");
        var perfil = new PerfilPrestador
        {
            UsuarioId = u.Id, NombrePublico = $"Estudio {Guid.NewGuid():N}",
            Slug = $"ind-{Guid.NewGuid():N}", RubroId = 1,
        };
        ctx.PerfilesPrestador.Add(perfil);
        await ctx.SaveChangesAsync();
        return (u, perfil);
    }

    private static Credencial UnaCredencial(Guid perfilId, EstadoCredencial estado, DateTime creada) => new()
    {
        PerfilPrestadorId = perfilId, Tipo = TipoCredencial.Curso, Nombre = "Curso de prueba",
        ObtenidaEn = new DateOnly(2020, 1, 1), Estado = estado, CreadaEn = creada,
    };

    private static void Resolver(ZassDbContext ctx, Guid perfilId, Guid verificadorId,
        DateTime creada, DateTime resuelta, ResultadoVerificacion resultado)
    {
        // La credencial queda en Rechazada o EnRevision: «Verificada» exige sello (ck_credencial_sello)
        // y el estado de la credencial no entra en estas cifras, que miran la verificación.
        var credencial = UnaCredencial(perfilId, resultado == ResultadoVerificacion.Rechazada
            ? EstadoCredencial.Rechazada : EstadoCredencial.Borrador, creada);
        ctx.Credenciales.Add(credencial);
        ctx.Verificaciones.Add(new Verificacion
        {
            CredencialId = credencial.Id, VerificadorId = verificadorId, Resultado = resultado,
            Motivo = resultado == ResultadoVerificacion.Rechazada ? MotivoRechazo.Ilegible : null,
            Detalle = "Detalle de prueba", ResueltaEn = resuelta,
        });
    }

    private static void UnAviso(ZassDbContext ctx, Guid destinatario, CanalNotificacion canal,
        EstadoEnvio estado, DateTime programada) => ctx.Notificaciones.Add(new Notificacion
    {
        DestinatarioId = destinatario, Tipo = default, Canal = canal, Estado = estado,
        ClaveIdempotencia = $"ind:{Guid.NewGuid():N}", Asunto = "Prueba", Cuerpo = "Prueba",
        ProgramadaPara = programada,
    });

    private static Resena UnaResena(ZassDbContext ctx, Guid autorId, Guid perfilId)
    {
        var contacto = new Contacto
        {
            BuscadorId = autorId, PerfilPrestadorId = perfilId,
            Origen = OrigenContacto.Ficha, Canal = CanalContacto.Whatsapp,
        };
        ctx.Contactos.Add(contacto);
        var resena = new Resena
        {
            ContactoId = contacto.Id, PerfilPrestadorId = perfilId, AutorId = autorId,
            Calificacion = 4, Comentario = "Una reseña de prueba con más de veinte letras.",
        };
        ctx.Resenas.Add(resena);
        return resena;
    }

    private static void UnReporte(ZassDbContext ctx, Guid resenaId, Guid reportadoPor, EstadoReporte estado) =>
        ctx.ReportesResena.Add(new ReporteResena
        {
            ResenaId = resenaId, ReportadoPor = reportadoPor, Motivo = MotivoReporte.FueraDeTema, Estado = estado,
        });

    private static Tarjeta UnaTarjeta(EstadoTarjeta estado, Guid? perfilId) => new()
    {
        UrlEstable = $"I{Guid.NewGuid():N}"[..9].ToUpperInvariant(), Estado = estado, PerfilPrestadorId = perfilId,
    };
}

/// <summary>API-60 por HTTP: quién puede pedirla y la forma de la respuesta.</summary>
public class IndicadoresApiPruebas : IAsyncLifetime
{
    private readonly AplicacionParaAuditoria _app = new();
    private HttpClient _cliente = null!;

    public Task InitializeAsync()
    {
        _cliente = _app.CreateClient(new WebApplicationFactoryClientOptions
        {
            HandleCookies = true, AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost"),
        });
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        _app.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task Sin_sesion_devuelve_401()
    {
        var r = await _cliente.GetAsync("/api/interno/indicadores");
        Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode);
    }

    [Theory]
    [InlineData("buscador")]
    [InlineData("verificador")]
    [InlineData("moderador")]
    public async Task Sin_el_rol_administrador_devuelve_403(string rol)
    {
        await EntrarConRolAsync(rol);
        var r = await _cliente.GetAsync("/api/interno/indicadores");
        Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
    }

    [Fact]
    public async Task El_administrador_recibe_las_cifras()
    {
        await EntrarConRolAsync("administrador");
        var r = await _cliente.GetAsync("/api/interno/indicadores");
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);

        using var json = JsonDocument.Parse(await r.Content.ReadAsStringAsync());
        var raiz = json.RootElement;
        var verificacion = raiz.GetProperty("verificacion");
        foreach (var campo in new[] { "enCola", "masAntiguaDesde", "resueltas", "rechazadas", "tasaDeRechazo", "horasPromedioDesdeCreacion" })
            Assert.True(verificacion.TryGetProperty(campo, out _), $"falta verificacion.{campo}");
        Assert.True(raiz.GetProperty("avisos").TryGetProperty("correosFallidos", out _));
        Assert.True(raiz.TryGetProperty("reportesAbiertos", out _));
        Assert.True(raiz.TryGetProperty("tarjetasEmitidasSinActivar", out _));
        Assert.True(raiz.TryGetProperty("calculadoEn", out _));
    }

    private async Task EntrarConRolAsync(string rol)
    {
        var correo = $"ind-api-{Guid.NewGuid():N}@zass.local";
        var registro = await _cliente.PostAsJsonAsync("/api/auth/registro", new
        {
            correo, contrasena = "contrasena-larga-1", nombre = "Usuario Test", rol = "buscador",
        });
        Assert.Equal(HttpStatusCode.Created, registro.StatusCode);

        if (rol != "buscador")
        {
            await using var ctx = new ZassDbContext(new DbContextOptionsBuilder<ZassDbContext>()
                .UseNpgsql(Conexion.Cadena).Options);
            var usuario = await ctx.Usuarios.SingleAsync(u => u.Correo == correo);
            var idRol = await ctx.Roles.Where(x => x.Codigo == rol).Select(x => x.Id).SingleAsync();
            ctx.UsuarioRoles.Add(new UsuarioRol { UsuarioId = usuario.Id, RolId = idRol });
            // Igual que AuditoriaApiPruebas: que la cuenta pueda entrar.
            if (usuario.Estado is not (EstadoCuenta.Activa or EstadoCuenta.PendienteVerificacion))
                usuario.Estado = EstadoCuenta.Activa;
            await ctx.SaveChangesAsync();
        }

        var sesion = await _cliente.PostAsJsonAsync("/api/auth/sesion", new { correo, contrasena = "contrasena-larga-1" });
        Assert.Equal(HttpStatusCode.OK, sesion.StatusCode);
    }
}
