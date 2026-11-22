using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace Zass.Integracion.Pruebas;

/// <summary>
/// ES-2 · Las cuentas creadas antes del registro completo (20-nov-2026), por HTTP.
///
/// <para>
/// Una cuenta «antigua» se fabrica registrandola por el camino de verdad y borrandole despues
/// en la base el RUT, el telefono y la casilla de mayoria de edad: asi quedan exactamente como
/// las que existian antes del #174.
/// </para>
///
/// <para>
/// Lo que se vigila: que la API (no solo la pantalla) no deje seguir mientras falten, que deje
/// abierto lo que tiene que quedar abierto (ver la cuenta, salir, eliminarla, las paginas
/// publicas), que completar los datos desbloquee, y que un RUT ajeno se rechace con el MISMO
/// mensaje que en el registro.
/// </para>
/// </summary>
public partial class ApiPruebas
{
    private async Task<string> UnaCuentaAntiguaConSesionAsync()
    {
        var correo = NuevoCorreo();
        await RegistrarAsync(correo);
        await VerificarAsync(correo);

        using (var ctx = ContextoDePrueba.Crear())
        {
            var u = await ctx.Usuarios.SingleAsync(x => x.Correo == correo);
            u.Rut = null;
            u.Telefono = null;
            u.Apellido = null;
            u.MayorDeEdadConfirmadoEn = null;
            await ctx.SaveChangesAsync();
        }

        await IniciarSesionAsync(correo);
        return correo;
    }

    private async Task<HttpResponseMessage> CompletarDatosAsync(object cuerpo, bool conCsrf = true)
    {
        using var peticion = new HttpRequestMessage(HttpMethod.Put, "/api/mi/cuenta/datos")
        {
            Content = JsonContent.Create(cuerpo),
        };
        if (conCsrf) peticion.Headers.Add("X-CSRF-Token", await TokenCsrfAsync());
        return await _cliente.SendAsync(peticion);
    }

    private static object DatosValidos(string? rut = null) => new
    {
        tipo = "particular", apellido = "Rojas", rut = rut ?? DatosDeRegistro.Rut(),
        telefono = "+56 9 8765 4321", mayorDeEdad = true,
    };

    private async Task<string[]> DatosPendientesAsync()
    {
        var cuenta = await _cliente.GetAsync("/api/mi/cuenta");
        Assert.Equal(HttpStatusCode.OK, cuenta.StatusCode);
        var json = await cuenta.Content.ReadFromJsonAsync<JsonDocument>();
        return json!.RootElement.GetProperty("datosPendientes").EnumerateArray()
            .Select(e => e.GetString()!).ToArray();
    }

    // ------------------------------------------------------------------------------

    [Fact]
    public async Task ES2_la_cuenta_antigua_ve_en_mi_cuenta_que_datos_le_faltan()
    {
        await UnaCuentaAntiguaConSesionAsync();

        Assert.Equal(new[] { "rut", "telefono", "mayorDeEdad" }, await DatosPendientesAsync());
    }

    [Fact]
    public async Task ES2_con_datos_pendientes_un_endpoint_con_sesion_responde_428()
    {
        await UnaCuentaAntiguaConSesionAsync();

        var r = await _cliente.GetAsync("/api/mi/suscripcion");

        Assert.Equal((HttpStatusCode)428, r.StatusCode);
        var json = await r.Content.ReadFromJsonAsync<JsonDocument>();
        Assert.Equal("datos_de_cuenta_pendientes", json!.RootElement.GetProperty("codigo").GetString());
    }

    /// <summary>Una cuenta con todo no se bloquea: el filtro solo mira a quien le falta algo.</summary>
    [Fact]
    public async Task ES2_una_cuenta_con_sus_datos_completos_no_recibe_428()
    {
        var correo = NuevoCorreo();
        await RegistrarAsync(correo); await VerificarAsync(correo); await IniciarSesionAsync(correo);

        Assert.Empty(await DatosPendientesAsync());
        Assert.NotEqual((HttpStatusCode)428, (await _cliente.GetAsync("/api/mi/suscripcion")).StatusCode);
    }

    /// <summary>
    /// Las paginas publicas no exigen sesion, y no se cierran por tener una: la persona las
    /// veria igual cerrandola.
    /// </summary>
    [Fact]
    public async Task ES2_con_datos_pendientes_las_paginas_publicas_siguen_abiertas()
    {
        await UnaCuentaAntiguaConSesionAsync();

        var r = await _cliente.GetAsync($"/api/prestadores/no-existe-{Guid.NewGuid():N}");

        Assert.Equal(HttpStatusCode.NotFound, r.StatusCode);
    }

    /// <summary>
    /// Irse no se condiciona a entregar mas datos (Ley 21.719): con datos pendientes se puede
    /// eliminar la cuenta.
    /// </summary>
    [Fact]
    public async Task ES2_con_datos_pendientes_se_puede_eliminar_la_cuenta()
    {
        await UnaCuentaAntiguaConSesionAsync();

        // CS-04 (#76): eliminar la cuenta pide la contraseña.
        using var peticion = new HttpRequestMessage(HttpMethod.Delete, "/api/mi/cuenta")
        {
            Content = JsonContent.Create(new { contrasena = "contrasena-larga-1" }),
        };
        peticion.Headers.Add("X-CSRF-Token", await TokenCsrfAsync());
        var r = await _cliente.SendAsync(peticion);

        Assert.Equal(HttpStatusCode.NoContent, r.StatusCode);
    }

    [Fact]
    public async Task ES2_con_datos_pendientes_se_puede_cerrar_la_sesion()
    {
        await UnaCuentaAntiguaConSesionAsync();

        var r = await _cliente.DeleteAsync("/api/auth/sesion");

        Assert.True(r.IsSuccessStatusCode, $"Cerrar sesion respondio {(int)r.StatusCode}.");
    }

    [Fact]
    public async Task ES2_completar_los_datos_desbloquea_la_cuenta()
    {
        var correo = await UnaCuentaAntiguaConSesionAsync();
        var rut = DatosDeRegistro.Rut();

        var r = await CompletarDatosAsync(DatosValidos(rut));

        Assert.Equal(HttpStatusCode.NoContent, r.StatusCode);
        Assert.Empty(await DatosPendientesAsync());
        Assert.NotEqual((HttpStatusCode)428, (await _cliente.GetAsync("/api/mi/suscripcion")).StatusCode);

        using var ctx = ContextoDePrueba.Crear();
        var u = await ctx.Usuarios.SingleAsync(x => x.Correo == correo);
        Assert.Equal(rut, u.Rut);
        Assert.NotNull(u.MayorDeEdadConfirmadoEn);
    }

    [Fact]
    public async Task ES2_completar_los_datos_exige_el_token_csrf()
    {
        await UnaCuentaAntiguaConSesionAsync();

        var r = await CompletarDatosAsync(DatosValidos(), conCsrf: false);

        Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
        Assert.Equal(3, (await DatosPendientesAsync()).Length);
    }

    /// <summary>Las validaciones del registro, por la puerta: un RUT con el digito malo no entra.</summary>
    [Fact]
    public async Task ES2_completar_valida_igual_que_el_registro()
    {
        await UnaCuentaAntiguaConSesionAsync();

        var r = await CompletarDatosAsync(new
        {
            tipo = "particular", apellido = "Rojas", rut = "11.111.111-2",
            telefono = "+56 9 8765 4321", mayorDeEdad = true,
        });

        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        Assert.Contains("RUT no es válido", await r.Content.ReadAsStringAsync());
    }

    /// <summary>
    /// Un RUT que ya usa otra cuenta se rechaza sin decir de quien es. Desde el 21-nov-2026 el
    /// registro (API-08) responde otra cosa —un aviso y un correo a la dueña—, pero ES-2 NO
    /// cambio: aqui la persona ya tiene sesion en otra cuenta, y no se le escribe a nadie (ver la
    /// recomendacion en el PR). Esta prueba fija las dos cosas para que nadie las mezcle sin querer.
    /// </summary>
    [Fact]
    public async Task ES2_un_rut_de_otra_cuenta_se_rechaza_sin_decir_de_quien_es_y_sin_correo()
    {
        var rutAjeno = DatosDeRegistro.Rut();
        var duena = NuevoCorreo();
        var alta = await _cliente.PostAsJsonAsync("/api/auth/registro", new
        {
            tipo = "particular", apellido = "Prueba", rut = rutAjeno, telefono = "+56 9 1234 5678",
            mayorDeEdad = true, correo = duena, contrasena = "contrasena-larga-1", nombre = "Ana", rol = "buscador",
        });
        Assert.Equal(HttpStatusCode.Created, alta.StatusCode);
        await MarcarVerificadaAsync(duena);   // el aviso de RUT solo va a un correo verificado (22-nov)

        var otroRegistro = await _cliente.PostAsJsonAsync("/api/auth/registro", new
        {
            tipo = "particular", apellido = "Prueba", rut = rutAjeno, telefono = "+56 9 1234 5678",
            mayorDeEdad = true, correo = NuevoCorreo(), contrasena = "contrasena-larga-1", nombre = "Eva", rol = "buscador",
        });

        await UnaCuentaAntiguaConSesionAsync();
        var completar = await CompletarDatosAsync(DatosValidos(rutAjeno));

        Assert.Equal(HttpStatusCode.Conflict, otroRegistro.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, completar.StatusCode);
        var cuerpo = await CuerpoComparable.De(completar);
        Assert.Equal(Zass.Aplicacion.Identidad.ServicioRegistro.MensajeRutOcupado,
            JsonDocument.Parse(cuerpo).RootElement.GetProperty("detail").GetString());
        Assert.DoesNotContain(duena, cuerpo);
        Assert.Equal(3, (await DatosPendientesAsync()).Length);

        // El unico aviso de RUT a la dueña es el del registro; completar no saca otro.
        using var ctx = ContextoDePrueba.Crear();
        Assert.Equal(1, await ctx.Notificaciones.CountAsync(n => n.Destinatario!.Correo == duena
            && n.Tipo == Zass.Dominio.Comun.TipoNotificacion.RegistroConRutExistente));
    }
}
