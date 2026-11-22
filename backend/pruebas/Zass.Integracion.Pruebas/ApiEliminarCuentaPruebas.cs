using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Zass.Api.Configuracion;
using Zass.Api.Endpoints;
using Zass.Aplicacion.Puertos;
using Zass.Dominio.Agenda;
using Zass.Dominio.Catalogo;
using Zass.Dominio.Comun;
using Zass.Dominio.Identidad;
using Zass.Dominio.Suscripciones;

namespace Zass.Integracion.Pruebas;

/// <summary>
/// CS-04 / API-17 de punta a punta, por HTTP y mirando la base (AC-7: «recorrido completo más
/// verificación en base de datos»). Hasta el 17-nov-2026 ninguna prueba llamaba a
/// <c>DELETE /api/mi/cuenta</c>: el endpoint existía, pero no tenía pantalla ni recorrido probado.
///
/// Lo que decidió Pablo el 17-nov y se comprueba aquí: se pide la contraseña; las citas futuras se
/// cancelan y la otra parte recibe el aviso; la suscripción vigente se cancela (sin reembolso); y
/// las citas pasadas quedan como estaban.
/// </summary>
public class ApiEliminarCuentaPruebas : IAsyncLifetime
{
    private const string Clave = "contrasena-larga-1";

    private readonly Aplicacion _app = new();
    private HttpClient _comoBuscador = null!;
    private HttpClient _comoPrestador = null!;

    private string _marca = "";
    private Guid _perfil;
    private Guid _prestadorUsuario;
    private Guid _servicioOfrecido;
    private int _servicioCatalogo;
    private Guid _regla;
    private Guid _bloqueManana;
    private Guid _suscripcion;
    // Las cuentas se anonimizan (el correo cambia): la limpieza va por id, no por correo.
    private readonly List<Guid> _usuarios = new();

    private HttpClient Cliente() => _app.CreateClient(new WebApplicationFactoryClientOptions
    {
        HandleCookies = true, AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost"),
    });

    public async Task InitializeAsync()
    {
        _comoBuscador = Cliente();
        _comoPrestador = Cliente();
        _marca = Guid.NewGuid().ToString("N")[..12];

        using var ctx = ContextoDePrueba.Crear();
        var ahora = DateTime.UtcNow;
        var hash = _app.Services.GetRequiredService<IHashContrasenas>();

        // El prestador con una contraseña de verdad: tiene que poder entrar y confirmar la baja.
        var prestador = new Usuario
        {
            Correo = $"baja-p-{_marca}@zass.local", HashContrasena = hash.Hash(Clave),
            Nombre = "Marcela Soto", CorreoVerificado = true, Estado = EstadoCuenta.Activa,
        };
        ctx.Usuarios.Add(prestador);

        var rubro = await ctx.Rubros.FirstAsync(r => r.Activo);
        var perfil = new PerfilPrestador
        {
            UsuarioId = prestador.Id, RubroId = rubro.Id, Slug = $"baja-{_marca}",
            NombrePublico = "Estudio de prueba", Estado = EstadoPerfil.Publicado,
            PublicadoEn = ahora.AddDays(-2),
        };
        ctx.PerfilesPrestador.Add(perfil);
        var servicio = new Servicio
        {
            RubroId = rubro.Id, Nombre = $"Consulta {_marca}",
            NombreNormalizado = $"consulta {_marca}", EsCatalogo = false,
        };
        ctx.Servicios.Add(servicio);
        await ctx.SaveChangesAsync();

        var ofrecido = new PrestadorServicio
        {
            PerfilPrestadorId = perfil.Id, ServicioId = servicio.Id,
            DuracionMinutos = 30, ModoAgenda = ModoAgenda.Automatico, Activo = true,
        };
        ctx.PrestadorServicios.Add(ofrecido);

        var plan = await ctx.Planes.FirstAsync(p => p.Codigo == "prueba");
        var suscripcion = new Suscripcion
        {
            PerfilPrestadorId = perfil.Id, PlanId = plan.Id, Estado = EstadoSuscripcion.Activa,
            IniciaEn = ahora.AddDays(-2), VigenteHasta = ahora.AddDays(28), RenovacionAutomatica = true,
        };
        ctx.Suscripciones.Add(suscripcion);

        var manana = ahora.AddDays(1);
        var regla = new Disponibilidad
        {
            PerfilPrestadorId = perfil.Id, PrestadorServicioId = ofrecido.Id,
            DiaSemana = (short)(((int)manana.DayOfWeek + 6) % 7 + 1),
            HoraInicio = TimeOnly.FromDateTime(manana),
            HoraFin = TimeOnly.FromDateTime(manana).AddMinutes(30),
            DuracionBloqueMin = 30, VigenteDesde = DateOnly.FromDateTime(ahora.AddDays(-10)),
        };
        ctx.Disponibilidades.Add(regla);
        await ctx.SaveChangesAsync();

        var bloque = new BloqueHorario
        {
            DisponibilidadId = regla.Id, PerfilPrestadorId = perfil.Id, PrestadorServicioId = ofrecido.Id,
            InicioEn = manana, FinEn = manana.AddMinutes(30), Estado = EstadoBloque.Libre,
        };
        ctx.Bloques.Add(bloque);
        await ctx.SaveChangesAsync();

        _perfil = perfil.Id;
        _prestadorUsuario = prestador.Id;
        _usuarios.Add(prestador.Id);
        _servicioOfrecido = ofrecido.Id;
        _servicioCatalogo = servicio.Id;
        _regla = regla.Id;
        _bloqueManana = bloque.Id;
        _suscripcion = suscripcion.Id;
    }

    public async Task DisposeAsync()
    {
        using (var ctx = ContextoDePrueba.Crear())
        {
            await ctx.Database.ExecuteSqlRawAsync(
                """
                DELETE FROM contactos    WHERE perfil_prestador_id = {0};
                DELETE FROM cambios_cita WHERE cita_id IN (SELECT id FROM citas WHERE perfil_prestador_id = {0});
                DELETE FROM citas        WHERE perfil_prestador_id = {0};
                DELETE FROM bloques      WHERE perfil_prestador_id = {0};
                DELETE FROM disponibilidades WHERE perfil_prestador_id = {0};
                DELETE FROM suscripciones WHERE perfil_prestador_id = {0};
                DELETE FROM prestador_servicios WHERE perfil_prestador_id = {0};
                DELETE FROM servicios    WHERE id = {1};
                DELETE FROM perfiles_prestador WHERE id = {0};
                """, _perfil, _servicioCatalogo);

            foreach (var id in _usuarios)
            {
                ctx.Auditoria.RemoveRange(ctx.Auditoria.Where(a => a.ActorId == id || a.EntidadId == id));
                ctx.Notificaciones.RemoveRange(ctx.Notificaciones.Where(n => n.DestinatarioId == id));
                ctx.Sesiones.RemoveRange(ctx.Sesiones.Where(s => s.UsuarioId == id));
            }
            ctx.Usuarios.RemoveRange(ctx.Usuarios.Where(u => _usuarios.Contains(u.Id)));
            await ctx.SaveChangesAsync();
        }
        _comoBuscador.Dispose();
        _comoPrestador.Dispose();
        _app.Dispose();
    }

    // ------------------------------------------------------------- ayudantes

    private async Task<Guid> UnBuscadorConSesionAsync()
    {
        var correo = $"baja-b-{Guid.NewGuid():N}-{_marca}@zass.local";
        Assert.Equal(HttpStatusCode.Created, (await _comoBuscador.PostAsJsonAsync("/api/auth/registro", new
        {
            // Registro completo (ES-2): sin RUT, teléfono y mayoría de edad la API responde 400.
            tipo = "particular", apellido = "Pérez", rut = DatosDeRegistro.Rut(), telefono = "+56 9 1234 5678",
            mayorDeEdad = true, correo, contrasena = Clave, nombre = "Ana", rol = "buscador",
        })).StatusCode);

        Guid id;
        using (var ctx = ContextoDePrueba.Crear())
        {
            var usuario = await ctx.Usuarios.SingleAsync(u => u.Correo == correo);
            id = usuario.Id;
            _usuarios.Add(id);
            var aviso = await ctx.Notificaciones
                .Where(n => n.DestinatarioId == id && n.Tipo == TipoNotificacion.CorreoVerificacion)
                .OrderByDescending(n => n.ProgramadaPara).FirstAsync();
            var token = CargaDeAviso.LeerToken(aviso.Cuerpo)!;
            Assert.Equal(HttpStatusCode.NoContent,
                (await _comoBuscador.PostAsJsonAsync("/api/auth/correo/verificar", new { token })).StatusCode);
        }

        Assert.Equal(HttpStatusCode.OK, (await _comoBuscador.PostAsJsonAsync("/api/auth/sesion", new
        {
            correo, contrasena = Clave,
        })).StatusCode);
        return id;
    }

    private async Task EntrarComoPrestadorAsync() =>
        Assert.Equal(HttpStatusCode.OK, (await _comoPrestador.PostAsJsonAsync("/api/auth/sesion", new
        {
            correo = $"baja-p-{_marca}@zass.local", contrasena = Clave,
        })).StatusCode);

    private static async Task<string> CsrfAsync(HttpClient cliente) =>
        (await (await cliente.GetAsync("/api/auth/csrf")).Content.ReadFromJsonAsync<JsonDocument>())!
            .RootElement.GetProperty("token").GetString()!;

    /// <summary>El cliente reserva la hora de mañana: una cita futura y activa.</summary>
    private async Task<Guid> AgendarMananaAsync()
    {
        var p = new HttpRequestMessage(HttpMethod.Post, "/api/citas")
        {
            Content = JsonContent.Create(new { bloqueId = _bloqueManana, notaBuscador = (string?)null }),
        };
        p.Headers.Add("X-CSRF-Token", await CsrfAsync(_comoBuscador));
        p.Headers.Add(FiltroIdempotencia.Cabecera, $"baja-{Guid.NewGuid():N}");
        var r = await _comoBuscador.SendAsync(p);
        Assert.True(r.IsSuccessStatusCode, await r.Content.ReadAsStringAsync());
        using var ctx = ContextoDePrueba.Crear();
        return (await ctx.Citas.SingleAsync(c => c.BloqueHorarioId == _bloqueManana)).Id;
    }

    /// <summary>
    /// Una cita PASADA y ya realizada. Si la baja no distinguiera pasado de futuro, intentaría
    /// cancelarla, la cita lo rechazaría («ya no se puede cancelar») y la baja fallaría entera.
    /// </summary>
    private async Task<Guid> UnaCitaPasadaRealizadaAsync(Guid buscador)
    {
        using var ctx = ContextoDePrueba.Crear();
        var hace = DateTime.UtcNow.AddDays(-3);
        var bloque = new BloqueHorario
        {
            DisponibilidadId = _regla, PerfilPrestadorId = _perfil, PrestadorServicioId = _servicioOfrecido,
            InicioEn = hace, FinEn = hace.AddMinutes(30), Estado = EstadoBloque.Reservado,
        };
        ctx.Bloques.Add(bloque);
        var cita = new Cita
        {
            Codigo = Cita.GenerarCodigo(), BloqueHorarioId = bloque.Id, BuscadorId = buscador,
            PrestadorServicioId = _servicioOfrecido, PerfilPrestadorId = _perfil,
            Estado = EstadoCita.Realizada, Modo = ModoAgenda.Automatico, CreadaEn = hace.AddDays(-1),
        };
        ctx.Citas.Add(cita);
        await ctx.SaveChangesAsync();
        return cita.Id;
    }

    private static async Task<HttpResponseMessage> EliminarAsync(HttpClient cliente, object cuerpo)
    {
        var p = new HttpRequestMessage(HttpMethod.Delete, "/api/mi/cuenta") { Content = JsonContent.Create(cuerpo) };
        p.Headers.Add("X-CSRF-Token", await CsrfAsync(cliente));
        return await cliente.SendAsync(p);
    }

    // ------------------------------------------------------------- el cliente se va

    [Fact]
    public async Task El_cliente_elimina_su_cuenta_su_cita_futura_se_cancela_y_el_prestador_recibe_el_aviso()
    {
        var buscador = await UnBuscadorConSesionAsync();
        var futura = await AgendarMananaAsync();
        var pasada = await UnaCitaPasadaRealizadaAsync(buscador);

        var r = await EliminarAsync(_comoBuscador, new { contrasena = Clave });

        Assert.Equal(HttpStatusCode.NoContent, r.StatusCode);
        using var ctx = ContextoDePrueba.Crear();
        Assert.Equal(EstadoCita.Cancelada, (await ctx.Citas.SingleAsync(c => c.Id == futura)).Estado);
        // La hora vuelve a estar disponible para cualquiera.
        Assert.Equal(EstadoBloque.Libre, (await ctx.Bloques.SingleAsync(b => b.Id == _bloqueManana)).Estado);
        // La otra parte se entera, con el aviso de siempre.
        Assert.True(await ctx.Notificaciones.AnyAsync(n =>
            n.DestinatarioId == _prestadorUsuario && n.Tipo == TipoNotificacion.CitaCancelada));
        // Lo pasado queda en el historial.
        Assert.Equal(EstadoCita.Realizada, (await ctx.Citas.SingleAsync(c => c.Id == pasada)).Estado);
        // Y la cuenta quedo anonimizada, con la sesion cerrada.
        Assert.Equal(EstadoCuenta.Eliminada, (await ctx.Usuarios.SingleAsync(u => u.Id == buscador)).Estado);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _comoBuscador.GetAsync("/api/mi/cuenta")).StatusCode);
        // El prestador no pierde su suscripcion porque un cliente se fue.
        Assert.Equal(EstadoSuscripcion.Activa, (await ctx.Suscripciones.SingleAsync(s => s.Id == _suscripcion)).Estado);
    }

    // ------------------------------------------------------------- el prestador se va

    [Fact]
    public async Task El_prestador_elimina_su_cuenta_se_cancelan_sus_citas_futuras_y_su_suscripcion()
    {
        var buscador = await UnBuscadorConSesionAsync();
        var futura = await AgendarMananaAsync();
        await EntrarComoPrestadorAsync();

        var r = await EliminarAsync(_comoPrestador, new { contrasena = Clave });

        Assert.Equal(HttpStatusCode.NoContent, r.StatusCode);
        using var ctx = ContextoDePrueba.Crear();
        Assert.Equal(EstadoCita.Cancelada, (await ctx.Citas.SingleAsync(c => c.Id == futura)).Estado);
        Assert.True(await ctx.Notificaciones.AnyAsync(n =>
            n.DestinatarioId == buscador && n.Tipo == TipoNotificacion.CitaCancelada));

        // Sin reembolso, pero nunca mas un cobro: cancelada y sin renovacion.
        var s = await ctx.Suscripciones.SingleAsync(x => x.Id == _suscripcion);
        Assert.Equal(EstadoSuscripcion.Cancelada, s.Estado);
        Assert.False(s.RenovacionAutomatica);

        Assert.Equal(EstadoPerfil.Despublicado, (await ctx.PerfilesPrestador.SingleAsync(p => p.Id == _perfil)).Estado);
        Assert.Equal(EstadoCuenta.Eliminada, (await ctx.Usuarios.SingleAsync(u => u.Id == _prestadorUsuario)).Estado);
    }

    // ------------------------------------------------------------- sin la contraseña no pasa nada

    [Fact]
    public async Task Con_otra_contrasena_responde_422_y_no_toca_nada()
    {
        await UnBuscadorConSesionAsync();
        var futura = await AgendarMananaAsync();
        await EntrarComoPrestadorAsync();

        var r = await EliminarAsync(_comoPrestador, new { contrasena = "no-era-esta-para-nada" });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, r.StatusCode);
        Assert.Contains("La contraseña no es correcta.", await r.Content.ReadAsStringAsync());
        using var ctx = ContextoDePrueba.Crear();
        Assert.NotEqual(EstadoCita.Cancelada, (await ctx.Citas.SingleAsync(c => c.Id == futura)).Estado);
        Assert.Equal(EstadoSuscripcion.Activa, (await ctx.Suscripciones.SingleAsync(s => s.Id == _suscripcion)).Estado);
        Assert.Equal(EstadoCuenta.Activa, (await ctx.Usuarios.SingleAsync(u => u.Id == _prestadorUsuario)).Estado);
        // La sesion sigue: equivocarse de contraseña no cierra nada.
        Assert.Equal(HttpStatusCode.OK, (await _comoPrestador.GetAsync("/api/mi/cuenta")).StatusCode);
    }

    [Fact]
    public async Task Sin_contrasena_responde_400_con_el_motivo_y_no_toca_nada()
    {
        await EntrarComoPrestadorAsync();

        var r = await EliminarAsync(_comoPrestador, new { contrasena = "" });

        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        Assert.Contains("Escribe tu contraseña para confirmar.", await r.Content.ReadAsStringAsync());
        using var ctx = ContextoDePrueba.Crear();
        Assert.Equal(EstadoCuenta.Activa, (await ctx.Usuarios.SingleAsync(u => u.Id == _prestadorUsuario)).Estado);
    }
}
