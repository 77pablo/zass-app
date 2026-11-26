using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Zass.Aplicacion.Identidad;
using Zass.Dominio.Comun;

namespace Zass.Integracion.Pruebas;

/// <summary>
/// API-08 con un RUT que ya tiene cuenta (decision de Pablo, 21-nov-2026), por HTTP.
///
/// <para>
/// Lo que se vigila: que no se cree otra cuenta; que la respuesta no diga NADA de la cuenta
/// original (ni su correo, ni su telefono, ni su nombre); que salga UN correo, a la cuenta
/// original y no al correo del formulario; que dentro de la espera la respuesta sea identica y
/// no salga otro; y que varios intentos a la vez no terminen en un 500.
/// </para>
/// </summary>
public partial class ApiPruebas
{
    private const string TelefonoDeLaDuena = "+56 9 7777 1234";
    private const string NombreDeLaDuena = "Benedicta";

    /// <summary>
    /// Registra a la dueña del RUT por el camino de verdad y le da el correo por verificado (el
    /// aviso de RN-S1-13 solo va a un correo verificado: auditoría 22-nov-2026). Devuelve su correo.
    /// </summary>
    private async Task<string> UnaDuenaDeRutAsync(string rut, bool verificada = true)
    {
        var correo = NuevoCorreo();
        var alta = await _cliente.PostAsJsonAsync("/api/auth/registro", new
        {
            tipo = "particular", apellido = "Quintanilla", rut, telefono = TelefonoDeLaDuena,
            mayorDeEdad = true, correo, contrasena = "contrasena-larga-1", nombre = NombreDeLaDuena, rol = "buscador",
        });
        Assert.Equal(HttpStatusCode.Created, alta.StatusCode);
        if (verificada) await MarcarVerificadaAsync(correo);
        return correo;
    }

    /// <summary>Lo que deja API-09 al abrir el enlace del correo, sin pasar por el correo.</summary>
    private static async Task MarcarVerificadaAsync(string correo)
    {
        using var ctx = ContextoDePrueba.Crear();
        Assert.Equal(1, await ctx.Usuarios.Where(u => u.Correo == correo).ExecuteUpdateAsync(s => s
            .SetProperty(u => u.CorreoVerificado, true)
            .SetProperty(u => u.Estado, EstadoCuenta.Activa)));
    }

    /// <summary>Hace que la cuenta parezca creada hace <paramref name="hace"/>.</summary>
    private static async Task EnvejecerCuentaAsync(string correo, TimeSpan hace)
    {
        using var ctx = ContextoDePrueba.Crear();
        var cuando = DateTime.UtcNow - hace;
        Assert.Equal(1, await ctx.Usuarios.Where(u => u.Correo == correo)
            .ExecuteUpdateAsync(s => s.SetProperty(u => u.CreadoEn, cuando)));
    }

    /// <summary>Otro registro con ese RUT (escrito con puntos, como lo escribe la gente) y otro correo.</summary>
    private Task<HttpResponseMessage> RegistrarConRutAsync(string rut, string correo, HttpClient? cliente = null) =>
        (cliente ?? _cliente).PostAsJsonAsync("/api/auth/registro", new
        {
            tipo = "particular", apellido = "Intrusa", rut = ConPuntos(rut), telefono = "+56 9 1111 2222",
            mayorDeEdad = true, correo, contrasena = "contrasena-larga-1", nombre = "Eva", rol = "buscador",
        });

    private static string ConPuntos(string rut)
    {
        var partes = rut.Split('-');
        var c = partes[0];
        return $"{c[..^6]}.{c[^6..^3]}.{c[^3..]}-{partes[1]}";
    }

    private static async Task<int> AvisosDeRutAsync(string correoDuena)
    {
        using var ctx = ContextoDePrueba.Crear();
        return await ctx.Notificaciones.CountAsync(n =>
            n.Destinatario!.Correo == correoDuena && n.Tipo == TipoNotificacion.RegistroConRutExistente);
    }

    // ------------------------------------------------------------------------------

    [Fact]
    public async Task Rut_con_cuenta_no_crea_otra_cuenta_y_responde_un_aviso_neutro_sin_datos_de_la_original()
    {
        var rut = DatosDeRegistro.Rut();
        var duena = await UnaDuenaDeRutAsync(rut);
        var intrusa = NuevoCorreo();

        var r = await RegistrarConRutAsync(rut, intrusa);

        Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
        // Sin el idPeticion, que es aleatorio y podria traer «7777» por casualidad.
        var texto = await CuerpoComparable.De(r);
        var json = JsonDocument.Parse(texto).RootElement;
        Assert.Equal("rut_con_cuenta", json.GetProperty("codigo").GetString());
        Assert.Equal(ServicioRegistro.MensajeRutConCuenta, json.GetProperty("detail").GetString());

        // Nada de la cuenta original, ni entero ni a medias.
        Assert.DoesNotContain(duena, texto, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(duena.Split('@')[0], texto, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("@", texto);
        Assert.DoesNotContain("7777", texto);
        Assert.DoesNotContain(NombreDeLaDuena, texto);
        Assert.DoesNotContain("Quintanilla", texto);
        Assert.DoesNotContain("usuarioId", texto, StringComparison.OrdinalIgnoreCase);

        using var ctx = ContextoDePrueba.Crear();
        Assert.False(await ctx.Usuarios.AnyAsync(u => u.Correo == intrusa));
        Assert.Equal(1, await ctx.Usuarios.CountAsync(u => u.Rut == rut));
    }

    [Fact]
    public async Task Rut_con_cuenta_manda_un_correo_a_la_cuenta_original_y_ninguno_al_del_formulario()
    {
        var rut = DatosDeRegistro.Rut();
        var duena = await UnaDuenaDeRutAsync(rut);
        var intrusa = NuevoCorreo();
        using (var antes = ContextoDePrueba.Crear())
        {
            var totalAntes = await antes.Notificaciones.CountAsync();

            Assert.Equal(HttpStatusCode.Conflict, (await RegistrarConRutAsync(rut, intrusa)).StatusCode);

            using var despues = ContextoDePrueba.Crear();
            // Uno solo en toda la cola, y es para la dueña.
            Assert.Equal(totalAntes + 1, await despues.Notificaciones.CountAsync());
        }

        using var ctx = ContextoDePrueba.Crear();
        var aviso = await ctx.Notificaciones.Include(n => n.Destinatario)
            .SingleAsync(n => n.Destinatario!.Correo == duena && n.Tipo == TipoNotificacion.RegistroConRutExistente);
        Assert.Equal(CanalNotificacion.Correo, aviso.Canal);
        Assert.Equal(EstadoEnvio.Programada, aviso.Estado);
        Assert.DoesNotContain(intrusa, aviso.Cuerpo);
        Assert.DoesNotContain(intrusa, aviso.ClaveIdempotencia);
        Assert.False(await ctx.Notificaciones.AnyAsync(n => n.Destinatario!.Correo == intrusa));
    }

    /// <summary>
    /// La espera por cuenta: otro intento dentro de ella responde EXACTAMENTE igual (no se nota
    /// si salio correo o no) y no saca un segundo correo. Sin esto, cualquiera que sepa un RUT le
    /// llena la bandeja a su dueña.
    /// </summary>
    [Fact]
    public async Task Rut_con_cuenta_dentro_de_la_espera_responde_identico_y_no_manda_otro_correo()
    {
        var rut = DatosDeRegistro.Rut();
        var duena = await UnaDuenaDeRutAsync(rut);

        var primera = await RegistrarConRutAsync(rut, NuevoCorreo());
        var segunda = await RegistrarConRutAsync(rut, NuevoCorreo());

        Assert.Equal(primera.StatusCode, segunda.StatusCode);
        Assert.Equal(await CuerpoComparable.De(primera), await CuerpoComparable.De(segunda));
        Assert.Equal(1, await AvisosDeRutAsync(duena));
    }

    /// <summary>
    /// Y la espera se acaba: pasada, otro intento si avisa. Sin esta prueba, una espera infinita
    /// (o un aviso que sale una sola vez en la vida) pasaria en verde.
    /// </summary>
    [Fact]
    public async Task Rut_con_cuenta_pasada_la_espera_vuelve_a_avisar()
    {
        var rut = DatosDeRegistro.Rut();
        var duena = await UnaDuenaDeRutAsync(rut);
        Assert.Equal(HttpStatusCode.Conflict, (await RegistrarConRutAsync(rut, NuevoCorreo())).StatusCode);

        using (var ctx = ContextoDePrueba.Crear())
        {
            var aviso = await ctx.Notificaciones.SingleAsync(n =>
                n.Destinatario!.Correo == duena && n.Tipo == TipoNotificacion.RegistroConRutExistente);
            aviso.ProgramadaPara -= ServicioRegistro.EsperaEntreAvisosDeRut + TimeSpan.FromMinutes(1);
            await ctx.SaveChangesAsync();
        }

        Assert.Equal(HttpStatusCode.Conflict, (await RegistrarConRutAsync(rut, NuevoCorreo())).StatusCode);

        Assert.Equal(2, await AvisosDeRutAsync(duena));
    }

    /// <summary>
    /// Varios intentos a la vez: el indice unico de la clave decide cual encola, y los demas no
    /// pueden terminar en un 500 (un 23505 sin atrapar). Y sigue saliendo UN correo.
    /// </summary>
    [Fact]
    public async Task Rut_con_cuenta_varios_intentos_simultaneos_no_dan_500_y_sale_un_solo_correo()
    {
        var rut = DatosDeRegistro.Rut();
        var duena = await UnaDuenaDeRutAsync(rut);

        var clientes = Enumerable.Range(0, 8).Select(_ => UnVisitante()).ToList();
        try
        {
            var respuestas = await Task.WhenAll(clientes.Select(c => RegistrarConRutAsync(rut, NuevoCorreo(), c)));

            Assert.All(respuestas, r => Assert.Equal(HttpStatusCode.Conflict, r.StatusCode));
            var cuerpos = await Task.WhenAll(respuestas.Select(CuerpoComparable.De));
            Assert.Single(cuerpos.Distinct());
        }
        finally
        {
            foreach (var c in clientes) c.Dispose();
        }

        Assert.Equal(1, await AvisosDeRutAsync(duena));
    }

    // ------------------------------------------------------- auditoría 22-nov-2026, hallazgo 3

    /// <summary>
    /// La cuenta dueña del RUT NO verificó su correo: no se le escribe (ese correo puede ser el de
    /// quien registró un RUT ajeno), no se crea otra cuenta, y la respuesta dice la verdad sin datos.
    /// </summary>
    [Fact]
    public async Task Rut_de_una_cuenta_sin_verificar_no_avisa_a_ese_correo_y_dice_que_se_libera_a_las_72_horas()
    {
        var rut = DatosDeRegistro.Rut();
        var secuestro = await UnaDuenaDeRutAsync(rut, verificada: false);
        var victima = NuevoCorreo();

        var r = await RegistrarConRutAsync(rut, victima);

        Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
        var texto = await CuerpoComparable.De(r);
        var json = JsonDocument.Parse(texto).RootElement;
        Assert.Equal("rut_con_cuenta", json.GetProperty("codigo").GetString());
        Assert.Equal(ServicioRegistro.MensajeRutSinConfirmar, json.GetProperty("detail").GetString());
        Assert.DoesNotContain("@", texto);
        Assert.Equal(0, await AvisosDeRutAsync(secuestro));
        using var ctx = ContextoDePrueba.Crear();
        Assert.False(await ctx.Usuarios.AnyAsync(u => u.Correo == victima));
    }

    /// <summary>
    /// Pasadas las 72 h sin verificar, el registro de la víctima borra en el acto la cuenta que le
    /// retenía el RUT y crea la suya; queda la traza de la purga y ningún aviso al correo viejo.
    /// </summary>
    [Fact]
    public async Task Rut_de_una_cuenta_sin_verificar_pasadas_72_horas_queda_libre_al_registrarse()
    {
        var rut = DatosDeRegistro.Rut();
        var secuestro = await UnaDuenaDeRutAsync(rut, verificada: false);
        Guid idViejo;
        using (var ctx = ContextoDePrueba.Crear())
            idViejo = (await ctx.Usuarios.SingleAsync(u => u.Correo == secuestro)).Id;
        await EnvejecerCuentaAsync(secuestro, ServicioCuentasSinVerificar.PlazoParaVerificar + TimeSpan.FromMinutes(5));
        var victima = NuevoCorreo();

        var r = await RegistrarConRutAsync(rut, victima);

        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        using var verificar = ContextoDePrueba.Crear();
        Assert.False(await verificar.Usuarios.AnyAsync(u => u.Correo == secuestro));
        Assert.Equal(victima, (await verificar.Usuarios.SingleAsync(u => u.Rut == rut)).Correo);
        Assert.True(await verificar.Auditoria.AnyAsync(a =>
            a.Accion == "cuenta_sin_verificar_purgada" && a.EntidadId == idViejo));
        // La auditoría del registro original NO se borra con la cuenta.
        Assert.True(await verificar.Auditoria.AnyAsync(a => a.Accion == "registro_cuenta" && a.EntidadId == idViejo));
        await verificar.Auditoria.Where(a => a.EntidadId == idViejo || a.ActorId == idViejo).ExecuteDeleteAsync();
    }

    /// <summary>Lo mismo con el correo: una cuenta caducada sin verificar no retiene un correo ajeno.</summary>
    [Fact]
    public async Task Correo_de_una_cuenta_sin_verificar_pasadas_72_horas_queda_libre_al_registrarse()
    {
        var secuestro = await UnaDuenaDeRutAsync(DatosDeRegistro.Rut(), verificada: false);
        Guid idViejo;
        using (var antes = ContextoDePrueba.Crear())
            idViejo = (await antes.Usuarios.SingleAsync(u => u.Correo == secuestro)).Id;
        await EnvejecerCuentaAsync(secuestro, ServicioCuentasSinVerificar.PlazoParaVerificar + TimeSpan.FromMinutes(5));

        var r = await RegistrarConRutAsync(DatosDeRegistro.Rut(), secuestro);

        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        using var ctx = ContextoDePrueba.Crear();
        var nueva = await ctx.Usuarios.SingleAsync(u => u.Correo == secuestro);
        Assert.Equal("Intrusa", nueva.Apellido);
        Assert.NotEqual(idViejo, nueva.Id);
        await ctx.Auditoria.Where(a => a.EntidadId == idViejo || a.ActorId == idViejo).ExecuteDeleteAsync();
    }

    /// <summary>Control: antes de las 72 h el correo sigue retenido (la víctima lo verá liberarse después).</summary>
    [Fact]
    public async Task Correo_de_una_cuenta_sin_verificar_antes_de_72_horas_sigue_ocupado()
    {
        var secuestro = await UnaDuenaDeRutAsync(DatosDeRegistro.Rut(), verificada: false);
        await EnvejecerCuentaAsync(secuestro, ServicioCuentasSinVerificar.PlazoParaVerificar - TimeSpan.FromMinutes(5));

        Guid idOriginal;
        using (var ctx = ContextoDePrueba.Crear())
            idOriginal = await ctx.Usuarios.Where(u => u.Correo == secuestro).Select(u => u.Id).SingleAsync();

        var r = await RegistrarConRutAsync(DatosDeRegistro.Rut(), secuestro);

        // 25-nov-2026: ya no es 409 (delataba la cuenta); responde como un registro nuevo. Lo que
        // se vigila sigue igual: el correo NO se libera, la cuenta original sigue y no nace otra.
        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        using var ctx2 = ContextoDePrueba.Crear();
        Assert.Equal(idOriginal,
            await ctx2.Usuarios.Where(u => u.Correo == secuestro).Select(u => u.Id).SingleAsync());
    }

    /// <summary>El registro normal, con un RUT libre, sigue como antes: 201 y el correo de verificacion.</summary>
    [Fact]
    public async Task Con_un_rut_libre_el_registro_sigue_igual_y_no_sale_ningun_aviso_de_rut()
    {
        var correo = NuevoCorreo();

        var r = await RegistrarConRutAsync(DatosDeRegistro.Rut(), correo);

        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        using var ctx = ContextoDePrueba.Crear();
        var avisos = await ctx.Notificaciones.Where(n => n.Destinatario!.Correo == correo).ToListAsync();
        Assert.Equal(TipoNotificacion.CorreoVerificacion, Assert.Single(avisos).Tipo);
    }

    private static async Task<int> AvisosDeCorreoAsync(string correoDuena, TipoNotificacion tipo)
    {
        using var ctx = ContextoDePrueba.Crear();
        return await ctx.Notificaciones.CountAsync(n => n.Destinatario!.Correo == correoDuena && n.Tipo == tipo);
    }

    /// <summary>
    /// 25-nov-2026 (Pablo): el correo repetido responde IGUAL que un registro nuevo (mismo estado,
    /// misma ubicacion, mismos campos), no crea otra cuenta y le escribe a la dueña de ese correo,
    /// una vez por hora. Antes respondia 409 «Ya existe una cuenta con ese correo».
    /// </summary>
    [Fact]
    public async Task El_correo_repetido_responde_igual_que_un_registro_nuevo_y_avisa_a_su_duena()
    {
        var duena = await UnaDuenaDeRutAsync(DatosDeRegistro.Rut());

        var libre = await RegistrarConRutAsync(DatosDeRegistro.Rut(), NuevoCorreo());
        var repetido = await RegistrarConRutAsync(DatosDeRegistro.Rut(), duena);

        Assert.Equal(HttpStatusCode.Created, libre.StatusCode);
        Assert.Equal(libre.StatusCode, repetido.StatusCode);
        Assert.Equal(libre.Headers.Location, repetido.Headers.Location);
        var jsonLibre = JsonDocument.Parse(await libre.Content.ReadAsStringAsync()).RootElement;
        var json = JsonDocument.Parse(await repetido.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal(
            jsonLibre.EnumerateObject().Select(p => p.Name).OrderBy(n => n),
            json.EnumerateObject().Select(p => p.Name).OrderBy(n => n));

        using (var ctx = ContextoDePrueba.Crear())
        {
            var cuenta = await ctx.Usuarios.SingleAsync(u => u.Correo == duena);   // sigue siendo una
            Assert.NotEqual(cuenta.Id, json.GetProperty("usuarioId").GetGuid());
        }
        Assert.Equal(1, await AvisosDeCorreoAsync(duena, TipoNotificacion.RegistroConCorreoExistente));
        Assert.Equal(0, await AvisosDeRutAsync(duena));

        // Dentro de la hora: la misma respuesta y ningun correo mas.
        var otraVez = await RegistrarConRutAsync(DatosDeRegistro.Rut(), duena);
        Assert.Equal(HttpStatusCode.Created, otraVez.StatusCode);
        Assert.Equal(1, await AvisosDeCorreoAsync(duena, TipoNotificacion.RegistroConCorreoExistente));
    }

    /// <summary>
    /// Correo repetido de una cuenta que no se confirmo: casi siempre es la misma persona que perdio
    /// el enlace. Le llega uno NUEVO (pasada la espera entre reenvios) y el anterior deja de servir.
    /// </summary>
    [Fact]
    public async Task El_correo_repetido_sin_confirmar_recibe_un_enlace_nuevo()
    {
        var correo = await UnaDuenaDeRutAsync(DatosDeRegistro.Rut(), verificada: false);
        string? tokenViejo;
        using (var ctx = ContextoDePrueba.Crear())
        {
            // Como si el enlace del registro hubiera salido hace 10 minutos (la espera es de 2).
            var emitido = DateTime.UtcNow - TimeSpan.FromMinutes(10);
            await ctx.Usuarios.Where(u => u.Correo == correo).ExecuteUpdateAsync(s => s
                .SetProperty(u => u.TokenVerificacionExpiraEn, emitido + ServicioCuenta.VidaDelTokenDeVerificacion));
            tokenViejo = await ctx.Usuarios.Where(u => u.Correo == correo).Select(u => u.TokenVerificacion).SingleAsync();
        }

        var r = await RegistrarConRutAsync(DatosDeRegistro.Rut(), correo);

        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        Assert.Equal(2, await AvisosDeCorreoAsync(correo, TipoNotificacion.CorreoVerificacion));
        using var ctx2 = ContextoDePrueba.Crear();
        Assert.Equal(1, await ctx2.Usuarios.CountAsync(u => u.Correo == correo));
        Assert.NotEqual(tokenViejo,
            await ctx2.Usuarios.Where(u => u.Correo == correo).Select(u => u.TokenVerificacion).SingleAsync());
    }
}
