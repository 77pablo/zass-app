using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Zass.Api.Configuracion;
using Zass.Dominio.Agenda;
using Zass.Dominio.Catalogo;
using Zass.Dominio.Comun;
using Zass.Dominio.Credenciales;
using Zass.Dominio.Identidad;
using Zass.Dominio.Suscripciones;

namespace Zass.Integracion.Pruebas;

/// <summary>
/// La pregunta que nadie había contestado endpoint por endpoint: ¿puede una cuenta tocar los
/// datos de otra?
///
/// Todo lo de aquí usa DOS cuentas de verdad —cada una con su cliente HTTP y su cookie de
/// sesión— y pide por HTTP contra la base real. La capa de aplicación ya prueba varias de
/// estas reglas, pero con dobles, y un doble puede filtrar por dueño por su cuenta: entonces
/// la prueba unitaria sigue verde aunque el servicio deje de comprobarlo. Lo que se mide aquí
/// es la cadena entera (ruta, sesión, servicio, consulta SQL).
///
/// Lo que se espera en cada caso es <b>404</b> y nunca 200: `seguridad.md` §5 dice que 403 y
/// 404 responden igual, porque decir «existe pero no es tuyo» ya es información.
///
/// Lo que NO está aquí porque ya tenía prueba por HTTP, y se confirmó mutando su guarda:
/// cancelar una cita ajena, quitar un bloqueo ajeno, editar una reseña ajena, responder una
/// reseña de otro perfil, eliminar y enviar a revisión una credencial ajena, el verificador
/// con su propia credencial, el moderador con su propia reseña, y el contacto de la ficha
/// (sin sesión y sin suscripción vigente).
/// </summary>
public class AutorizacionIntegracionPruebas : IAsyncLifetime
{
    private readonly Aplicacion _app = new();

    private string _marca = "";

    // Dos prestadores publicados, cada uno con su servicio, su agenda y su sesión; y dos
    // buscadores. Cada cuenta tiene su propio cliente para que las cuatro sesiones convivan:
    // con un solo cliente, iniciar sesión como B cerraría la de A y no se podría comprobar
    // que A sigue sin poder tocar lo de B.
    private Cuenta _a = null!;
    private Cuenta _b = null!;
    private Cuenta _cliente1 = null!;
    private Cuenta _cliente2 = null!;

    /// <summary>Una cuenta con su cliente HTTP, su cookie de sesión y lo que se le sembró.</summary>
    private sealed class Cuenta
    {
        public required HttpClient Http { get; init; }
        public required string Correo { get; init; }
        public Guid UsuarioId { get; set; }
        public Guid PerfilId { get; set; }
        public string Slug { get; set; } = "";
        public Guid ServicioOfrecidoId { get; set; }
        public int ServicioCatalogoId { get; set; }
        public Guid BloqueUno { get; set; }
        public Guid BloqueDos { get; set; }
        public Guid CredencialId { get; set; }

        public async Task<string> CsrfAsync() =>
            (await (await Http.GetAsync("/api/auth/csrf")).Content.ReadFromJsonAsync<JsonDocument>())!
                .RootElement.GetProperty("token").GetString()!;

        /// <summary>Cualquier método con su token CSRF y, si hace falta, su clave de idempotencia.</summary>
        public async Task<HttpResponseMessage> EnviarAsync(
            HttpMethod metodo, string url, object? cuerpo = null, string? clave = null)
        {
            var p = new HttpRequestMessage(metodo, url)
            {
                Content = cuerpo is null ? null : JsonContent.Create(cuerpo),
            };
            p.Headers.Add("X-CSRF-Token", await CsrfAsync());
            if (clave is not null) p.Headers.Add(FiltroIdempotencia.Cabecera, clave);
            return await Http.SendAsync(p);
        }
    }

    public async Task InitializeAsync()
    {
        _marca = Guid.NewGuid().ToString("N")[..12];

        _a = await UnaCuentaAsync("prestador", "pa");
        _b = await UnaCuentaAsync("prestador", "pb");
        _cliente1 = await UnaCuentaAsync("buscador", "c1");
        _cliente2 = await UnaCuentaAsync("buscador", "c2");

        await SembrarPrestadorAsync(_a, "a");
        await SembrarPrestadorAsync(_b, "b");
    }

    public async Task DisposeAsync()
    {
        using (var ctx = ContextoDePrueba.Crear())
        {
            await ctx.Database.ExecuteSqlRawAsync(
                """
                DELETE FROM contactos    WHERE perfil_prestador_id IN ({0}, {1});
                DELETE FROM cambios_cita WHERE cita_id IN (
                    SELECT id FROM citas WHERE perfil_prestador_id IN ({0}, {1}));
                DELETE FROM citas            WHERE perfil_prestador_id IN ({0}, {1});
                DELETE FROM bloques          WHERE perfil_prestador_id IN ({0}, {1});
                DELETE FROM bloqueos_agenda  WHERE perfil_prestador_id IN ({0}, {1});
                DELETE FROM disponibilidades WHERE perfil_prestador_id IN ({0}, {1});
                DELETE FROM suscripciones    WHERE perfil_prestador_id IN ({0}, {1});
                DELETE FROM credenciales     WHERE perfil_prestador_id IN ({0}, {1});
                DELETE FROM prestador_servicios WHERE perfil_prestador_id IN ({0}, {1});
                DELETE FROM servicios        WHERE id IN ({2}, {3});
                DELETE FROM perfiles_prestador WHERE id IN ({0}, {1});
                """,
                _a.PerfilId, _b.PerfilId, _a.ServicioCatalogoId, _b.ServicioCatalogoId);

            var usuarios = await ctx.Usuarios
                .Where(u => u.Correo.EndsWith($"-{_marca}@zass.local")).ToListAsync();
            foreach (var u in usuarios)
            {
                ctx.Auditoria.RemoveRange(
                    ctx.Auditoria.Where(a => a.ActorId == u.Id || a.EntidadId == u.Id));
                ctx.Notificaciones.RemoveRange(
                    ctx.Notificaciones.Where(n => n.DestinatarioId == u.Id));
                ctx.Sesiones.RemoveRange(ctx.Sesiones.Where(s => s.UsuarioId == u.Id));
                ctx.PerfilesBuscador.RemoveRange(
                    ctx.PerfilesBuscador.Where(p => p.UsuarioId == u.Id));
            }
            ctx.Usuarios.RemoveRange(usuarios);
            await ctx.SaveChangesAsync();
        }

        foreach (var cuenta in new[] { _a, _b, _cliente1, _cliente2 })
            cuenta?.Http.Dispose();
        _app.Dispose();
    }

    // ------------------------------------------------------------- el montaje

    private async Task<Cuenta> UnaCuentaAsync(string rol, string prefijo)
    {
        var http = _app.CreateClient(new WebApplicationFactoryClientOptions
        {
            HandleCookies = true, AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost"),
        });
        var cuenta = new Cuenta { Http = http, Correo = $"authz-{prefijo}-{_marca}@zass.local" };

        Assert.Equal(HttpStatusCode.Created, (await http.PostAsJsonAsync("/api/auth/registro", new
        {
            correo = cuenta.Correo, contrasena = "contrasena-larga-1",
            nombre = rol == "prestador" ? "Pablo Espinoza" : "Ana Pérez", rol,
        })).StatusCode);

        using (var ctx = ContextoDePrueba.Crear())
        {
            var usuario = await ctx.Usuarios.SingleAsync(u => u.Correo == cuenta.Correo);
            cuenta.UsuarioId = usuario.Id;
            var aviso = await ctx.Notificaciones
                .Where(n => n.DestinatarioId == usuario.Id
                            && n.Tipo == TipoNotificacion.CorreoVerificacion)
                .OrderByDescending(n => n.ProgramadaPara).FirstAsync();
            var token = Zass.Aplicacion.Puertos.CargaDeAviso.LeerToken(aviso.Cuerpo)!;
            Assert.Equal(HttpStatusCode.NoContent,
                (await http.PostAsJsonAsync("/api/auth/correo/verificar", new { token })).StatusCode);
        }

        Assert.Equal(HttpStatusCode.OK, (await http.PostAsJsonAsync("/api/auth/sesion", new
        {
            correo = cuenta.Correo, contrasena = "contrasena-larga-1",
        })).StatusCode);

        return cuenta;
    }

    /// <summary>
    /// El perfil ya existe (lo crea el registro con rol `prestador`, en borrador): aquí se
    /// publica y se le cuelga lo que hace falta para tener agenda. Se escribe en la base y no
    /// por HTTP a propósito: lo que se prueba es la autorización, no el camino de alta.
    /// </summary>
    private async Task SembrarPrestadorAsync(Cuenta cuenta, string sufijo)
    {
        using var ctx = ContextoDePrueba.Crear();
        var ahora = DateTime.UtcNow;

        var perfil = await ctx.PerfilesPrestador.SingleAsync(p => p.UsuarioId == cuenta.UsuarioId);
        var rubro = await ctx.Rubros.FirstAsync(r => r.Activo);
        perfil.RubroId = rubro.Id;
        perfil.Slug = $"authz-{sufijo}-{_marca}";
        perfil.NombrePublico = $"Estudio {sufijo.ToUpperInvariant()} {_marca}";
        perfil.Estado = EstadoPerfil.Publicado;
        perfil.PublicadoEn = ahora.AddDays(-2);

        var servicio = new Servicio
        {
            RubroId = rubro.Id, Nombre = $"Consulta {sufijo} {_marca}",
            NombreNormalizado = $"consulta {sufijo} {_marca}", EsCatalogo = false,
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
        ctx.Suscripciones.Add(new Suscripcion
        {
            PerfilPrestadorId = perfil.Id, PlanId = plan.Id, Estado = EstadoSuscripcion.Activa,
            IniciaEn = ahora.AddDays(-2), VigenteHasta = ahora.AddDays(28),
        });

        var credencial = new Credencial
        {
            PerfilPrestadorId = perfil.Id, Tipo = TipoCredencial.Titulo,
            Nombre = $"Credencial {sufijo} {_marca}", ObtenidaEn = new DateOnly(2020, 3, 15),
            Estado = EstadoCredencial.Borrador, CreadaEn = ahora,
        };
        ctx.Credenciales.Add(credencial);

        var manana = ahora.AddDays(1);
        var regla = new Disponibilidad
        {
            PerfilPrestadorId = perfil.Id, PrestadorServicioId = ofrecido.Id,
            DiaSemana = (short)(((int)manana.DayOfWeek + 6) % 7 + 1),
            HoraInicio = TimeOnly.FromDateTime(manana),
            HoraFin = TimeOnly.FromDateTime(manana).AddMinutes(60),
            DuracionBloqueMin = 30, VigenteDesde = DateOnly.FromDateTime(manana),
        };
        ctx.Disponibilidades.Add(regla);
        await ctx.SaveChangesAsync();

        var uno = new BloqueHorario
        {
            DisponibilidadId = regla.Id, PerfilPrestadorId = perfil.Id,
            PrestadorServicioId = ofrecido.Id,
            InicioEn = manana, FinEn = manana.AddMinutes(30), Estado = EstadoBloque.Libre,
        };
        var dos = new BloqueHorario
        {
            DisponibilidadId = regla.Id, PerfilPrestadorId = perfil.Id,
            PrestadorServicioId = ofrecido.Id,
            InicioEn = manana.AddMinutes(30), FinEn = manana.AddMinutes(60),
            Estado = EstadoBloque.Libre,
        };
        ctx.Bloques.AddRange(uno, dos);
        await ctx.SaveChangesAsync();

        cuenta.PerfilId = perfil.Id;
        cuenta.Slug = perfil.Slug;
        cuenta.ServicioOfrecidoId = ofrecido.Id;
        cuenta.ServicioCatalogoId = servicio.Id;
        cuenta.BloqueUno = uno.Id;
        cuenta.BloqueDos = dos.Id;
        cuenta.CredencialId = credencial.Id;
    }

    /// <summary>Una cita confirmada del cliente 1 con el prestador A, por el camino real.</summary>
    private async Task<Guid> UnaCitaDeUnoConAAsync()
    {
        var r = await _cliente1.EnviarAsync(
            HttpMethod.Post, "/api/citas",
            new { bloqueId = _a.BloqueUno, notaBuscador = (string?)null },
            $"authz-{Guid.NewGuid():N}");
        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        return Guid.Parse((await r.Content.ReadFromJsonAsync<JsonDocument>())!
            .RootElement.GetProperty("id").GetString()!);
    }

    private static async Task<Cita> LaCitaAsync(Guid id)
    {
        using var ctx = ContextoDePrueba.Crear();
        return await ctx.Citas.AsNoTracking().SingleAsync(c => c.Id == id);
    }

    // ------------------------------------------------------- CU-14: reprogramar lo ajeno

    /// <summary>
    /// El hueco que no tenía prueba en ninguna capa: cancelar una cita ajena sí la tenía,
    /// reprogramarla no. Y reprogramar es peor, porque además de romper la cita ajena reserva
    /// una hora a nombre de otra persona.
    /// </summary>
    [Fact]
    public async Task Reprogramar_la_cita_de_otra_cuenta_responde_404_y_no_la_mueve()
    {
        var cita = await UnaCitaDeUnoConAAsync();

        var deOtroBuscador = await _cliente2.EnviarAsync(
            HttpMethod.Post, $"/api/citas/{cita}/reprogramar",
            new { bloqueNuevoId = _a.BloqueDos, motivo = (string?)null }, $"r-{Guid.NewGuid():N}");
        var deOtroPrestador = await _b.EnviarAsync(
            HttpMethod.Post, $"/api/citas/{cita}/reprogramar",
            new { bloqueNuevoId = _a.BloqueDos, motivo = (string?)null }, $"r-{Guid.NewGuid():N}");
        var inexistente = await _cliente2.EnviarAsync(
            HttpMethod.Post, $"/api/citas/{Guid.NewGuid()}/reprogramar",
            new { bloqueNuevoId = _a.BloqueDos, motivo = (string?)null }, $"r-{Guid.NewGuid():N}");

        Assert.Equal(HttpStatusCode.NotFound, deOtroBuscador.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, deOtroPrestador.StatusCode);
        // Misma respuesta que una cita que no existe: el 404 no dice de quién es.
        Assert.Equal(
            await CuerpoComparable.De(inexistente),
            await CuerpoComparable.De(deOtroBuscador));

        // Y nada se movió: ni la cita, ni la hora de destino, ni nació una cita nueva.
        var enLaBase = await LaCitaAsync(cita);
        Assert.Equal(_a.BloqueUno, enLaBase.BloqueHorarioId);
        Assert.Equal(EstadoCita.Confirmada, enLaBase.Estado);
        using var ctx = ContextoDePrueba.Crear();
        Assert.Equal(EstadoBloque.Libre,
            (await ctx.Bloques.SingleAsync(b => b.Id == _a.BloqueDos)).Estado);
        Assert.Equal(1, await ctx.Citas.CountAsync(c => c.PerfilPrestadorId == _a.PerfilId));
    }

    /// <summary>
    /// La otra mitad del mismo endpoint: el dueño de la cita sí es dueño, pero la hora de
    /// destino es de OTRO prestador. Sin la comprobación del servicio, reprogramar sería la
    /// forma de reservar una hora en la agenda de cualquiera.
    /// </summary>
    [Fact]
    public async Task Reprogramar_hacia_la_hora_de_otro_prestador_no_pasa_y_no_la_reserva()
    {
        var cita = await UnaCitaDeUnoConAAsync();

        var r = await _cliente1.EnviarAsync(
            HttpMethod.Post, $"/api/citas/{cita}/reprogramar",
            new { bloqueNuevoId = _b.BloqueUno, motivo = (string?)null }, $"r-{Guid.NewGuid():N}");

        Assert.False(r.IsSuccessStatusCode);
        using var ctx = ContextoDePrueba.Crear();
        Assert.Equal(EstadoBloque.Libre,
            (await ctx.Bloques.SingleAsync(b => b.Id == _b.BloqueUno)).Estado);
        Assert.Equal(0, await ctx.Citas.CountAsync(c => c.PerfilPrestadorId == _b.PerfilId));
        Assert.Equal(_a.BloqueUno, (await LaCitaAsync(cita)).BloqueHorarioId);
    }

    // ------------------------------------------------------- CU-13: responder lo ajeno

    /// <summary>
    /// Que el prestador ajeno reciba 404 y el dueño 409 es justo lo que prueba el orden: la
    /// guarda de dueño corre ANTES de mirar el estado. Si corriera después, el código de
    /// estado ya diría a un extraño en qué estado está una cita que no es suya.
    /// </summary>
    [Fact]
    public async Task Confirmar_o_rechazar_la_cita_de_otro_prestador_responde_404()
    {
        var cita = await UnaCitaDeUnoConAAsync();   // nace confirmada (modo automático)

        foreach (var accion in new[] { "confirmar", "rechazar" })
        {
            var deOtroPrestador = await _b.EnviarAsync(
                HttpMethod.Post, $"/api/citas/{cita}/{accion}", new { motivo = (string?)null });
            var delBuscadorAjeno = await _cliente2.EnviarAsync(
                HttpMethod.Post, $"/api/citas/{cita}/{accion}", new { motivo = (string?)null });
            var delDueno = await _a.EnviarAsync(
                HttpMethod.Post, $"/api/citas/{cita}/{accion}", new { motivo = (string?)null });

            Assert.Equal(HttpStatusCode.NotFound, deOtroPrestador.StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, delBuscadorAjeno.StatusCode);
            Assert.Equal(HttpStatusCode.Conflict, delDueno.StatusCode);
        }

        Assert.Equal(EstadoCita.Confirmada, (await LaCitaAsync(cita)).Estado);
    }

    /// <summary>
    /// CS-12 y su mitad nueva: marcar asistencia es del prestador de ESA cita y acusar al
    /// prestador es del buscador de ESA cita. Para todos los demás la cita no existe. Esto se
    /// probaba solo con dobles; por HTTP no había nada.
    /// </summary>
    [Fact]
    public async Task La_asistencia_y_la_ausencia_solo_las_marca_quien_es_parte_de_la_cita()
    {
        var cita = await UnaCitaDeUnoConAAsync();

        // «No asistió el prestador» es del buscador: ni el prestador de la cita ni un extraño.
        Assert.Equal(HttpStatusCode.NotFound, (await _a.EnviarAsync(
            HttpMethod.Post, $"/api/citas/{cita}/no-asistio-el-prestador")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _b.EnviarAsync(
            HttpMethod.Post, $"/api/citas/{cita}/no-asistio-el-prestador")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _cliente2.EnviarAsync(
            HttpMethod.Post, $"/api/citas/{cita}/no-asistio-el-prestador")).StatusCode);

        // Y la asistencia es del prestador de la cita, no de otro prestador cualquiera.
        Assert.Equal(HttpStatusCode.NotFound, (await _b.EnviarAsync(
            HttpMethod.Post, $"/api/citas/{cita}/asistencia", new { asistio = true })).StatusCode);

        var enLaBase = await LaCitaAsync(cita);
        Assert.Equal(EstadoCita.Confirmada, enLaBase.Estado);
        Assert.Equal(FaltaEnCita.Nadie, enLaBase.QuienFalto);
    }

    [Fact]
    public async Task Mis_citas_no_trae_las_de_otra_cuenta()
    {
        var cita = await UnaCitaDeUnoConAAsync();

        foreach (var ajeno in new[] { _cliente2, _b })
        {
            var r = await ajeno.Http.GetAsync("/api/mi/citas");
            Assert.Equal(HttpStatusCode.OK, r.StatusCode);
            var citas = (await r.Content.ReadFromJsonAsync<JsonDocument>())!.RootElement;
            Assert.DoesNotContain(
                citas.EnumerateArray(),
                c => c.GetProperty("id").GetString() == cita.ToString());
        }

        // Y las dos partes de la cita sí la ven: si la lista viniera vacía para todos, la
        // comprobación de arriba pasaría sin probar nada.
        foreach (var parte in new[] { _cliente1, _a })
        {
            var r = await parte.Http.GetAsync("/api/mi/citas");
            var citas = (await r.Content.ReadFromJsonAsync<JsonDocument>())!.RootElement;
            Assert.Contains(
                citas.EnumerateArray(),
                c => c.GetProperty("id").GetString() == cita.ToString());
        }
    }

    // ------------------------------------------------------- CU-11: la agenda ajena

    /// <summary>
    /// API-33 recibe el identificador del servicio en el CUERPO, y de ahí salen los bloques
    /// que se generan y los que se borran. Sin la comprobación de dueño, cualquier prestador
    /// podría reescribir la agenda de otro —borrándole los bloques que ya tiene— mandando un
    /// identificador que no es suyo.
    /// </summary>
    [Fact]
    public async Task Definir_la_disponibilidad_de_un_servicio_ajeno_responde_404_y_no_toca_su_agenda()
    {
        using (var antes = ContextoDePrueba.Crear())
        {
            Assert.Equal(2, await antes.Bloques.CountAsync(b => b.PerfilPrestadorId == _a.PerfilId));
        }

        var r = await _b.EnviarAsync(HttpMethod.Put, "/api/mi/disponibilidad", new
        {
            prestadorServicioId = _a.ServicioOfrecidoId,
            franjas = new[]
            {
                new { diaSemana = 3, horaInicio = "09:00:00", horaFin = "10:00:00", duracionBloqueMin = 30 },
            },
        });

        Assert.Equal(HttpStatusCode.NotFound, r.StatusCode);

        using var ctx = ContextoDePrueba.Crear();
        // Ni se generó nada nuevo ni desapareció lo que A ya tenía.
        Assert.Equal(2, await ctx.Bloques.CountAsync(b => b.PerfilPrestadorId == _a.PerfilId));
        Assert.Equal(1, await ctx.Disponibilidades.CountAsync(d => d.PerfilPrestadorId == _a.PerfilId));
        Assert.Equal(0, await ctx.Disponibilidades.CountAsync(d => d.PerfilPrestadorId == _b.PerfilId
                                                                   && d.PrestadorServicioId == _a.ServicioOfrecidoId));
    }

    [Fact]
    public async Task Mi_disponibilidad_no_muestra_las_reglas_de_otro_prestador()
    {
        var r = await _b.Http.GetAsync("/api/mi/disponibilidad");

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var json = (await r.Content.ReadFromJsonAsync<JsonDocument>())!.RootElement;
        Assert.DoesNotContain(
            json.EnumerateArray(),
            s => s.GetProperty("prestadorServicioId").GetString() == _a.ServicioOfrecidoId.ToString());
    }

    // ------------------------------------------------------- CU-05: la credencial ajena

    /// <summary>
    /// Eliminar y enviar a revisión una credencial ajena ya tenían prueba por HTTP; LEERLA no.
    /// La lista sale de `/api/mi/credenciales`, sin ningún identificador en la petición, así
    /// que lo único que la acota es el perfil que se saca de la sesión.
    /// </summary>
    [Fact]
    public async Task Mis_credenciales_son_solo_las_mias()
    {
        var mias = await _b.Http.GetAsync("/api/mi/credenciales");

        Assert.Equal(HttpStatusCode.OK, mias.StatusCode);
        var json = (await mias.Content.ReadFromJsonAsync<JsonDocument>())!.RootElement;
        var ids = json.EnumerateArray().Select(c => c.GetProperty("id").GetString()).ToList();
        Assert.Contains(_b.CredencialId.ToString(), ids);
        Assert.DoesNotContain(_a.CredencialId.ToString(), ids);
    }

    // ------------------------------------------------------- CU-03: publicar lo ajeno

    /// <summary>
    /// Publicar y despublicar no reciben identificador: el perfil sale de la sesión, así que no
    /// hay nada que cambiar en la petición para apagar la ficha de otro. Esta prueba fija eso:
    /// el día que alguien añada un `perfilId` al cuerpo «para el panel interno», cae.
    /// </summary>
    [Fact]
    public async Task Despublicar_solo_apaga_la_ficha_de_quien_llama()
    {
        Assert.Equal(HttpStatusCode.NoContent,
            (await _b.EnviarAsync(HttpMethod.Post, "/api/mi/perfil-prestador/despublicar")).StatusCode);

        Assert.Equal(HttpStatusCode.NotFound,
            (await _b.Http.GetAsync($"/api/prestadores/{_b.Slug}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK,
            (await _b.Http.GetAsync($"/api/prestadores/{_a.Slug}")).StatusCode);

        using var ctx = ContextoDePrueba.Crear();
        Assert.Equal(EstadoPerfil.Publicado,
            (await ctx.PerfilesPrestador.SingleAsync(p => p.Id == _a.PerfilId)).Estado);
    }

}
