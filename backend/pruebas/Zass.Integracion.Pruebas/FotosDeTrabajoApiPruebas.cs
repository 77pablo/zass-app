using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SixLabors.ImageSharp;
using Zass.Dominio.Comun;
using Zass.Dominio.Suscripciones;

namespace Zass.Integracion.Pruebas;

/// <summary>
/// T69 por HTTP y contra la base real: las fotos de trabajos (API-77, 77-listar, 77-ordenar,
/// 77-quitar). Cada prueba mira lo que se vería si la regla se rompiera: que el Esencial NO puede
/// subir, que la foto 13 del Profesional NO entra, que una foto ajena NO se borra, que el archivo
/// se va del disco y que la foto servida no trae los metadatos.
/// </summary>
public partial class ApiPruebas
{
    /// <summary>Un prestador con sesión y, si se pide, con una suscripción activa en ese plan.</summary>
    private async Task<Guid> UnPrestadorConPlanAsync(string? codigoPlan)
    {
        var correo = NuevoCorreo();
        await RegistrarAsync(correo, rol: "prestador");
        await VerificarAsync(correo);
        await IniciarSesionAsync(correo);

        using var ctx = ContextoDePrueba.Crear();
        var perfilId = await ctx.PerfilesPrestador
            .Where(p => ctx.Usuarios.Any(u => u.Id == p.UsuarioId && u.Correo == correo))
            .Select(p => p.Id).SingleAsync();

        if (codigoPlan is not null)
        {
            var plan = await ctx.Planes.SingleAsync(p => p.Codigo == codigoPlan);
            ctx.Suscripciones.Add(new Suscripcion
            {
                PerfilPrestadorId = perfilId, PlanId = plan.Id, Estado = EstadoSuscripcion.Activa,
                IniciaEn = DateTime.UtcNow.AddDays(-1), VigenteHasta = DateTime.UtcNow.AddDays(29),
            });
            await ctx.SaveChangesAsync();
        }
        return perfilId;
    }

    private async Task<JsonElement> MisFotosDeTrabajoAsync()
    {
        var r = await _cliente.GetAsync("/api/mi/fotos-trabajo");
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        return (await r.Content.ReadFromJsonAsync<JsonDocument>())!.RootElement;
    }

    private async Task<(Guid Id, string Url)> SubirFotoDeTrabajoAsync(byte[]? imagen = null)
    {
        var r = await SubirAsync("/api/mi/fotos-trabajo", imagen ?? UnaImagen(1600, 900), "obra.jpg", "image/jpeg");
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var cuerpo = (await r.Content.ReadFromJsonAsync<JsonDocument>())!.RootElement;
        return (cuerpo.GetProperty("id").GetGuid(), cuerpo.GetProperty("url").GetString()!);
    }

    private async Task<HttpResponseMessage> OrdenarFotosAsync(params Guid[] ids)
    {
        var peticion = new HttpRequestMessage(HttpMethod.Put, "/api/mi/fotos-trabajo/orden")
        {
            Content = JsonContent.Create(new { ids }),
        };
        peticion.Headers.Add("X-CSRF-Token", await TokenCsrfAsync());
        return await _cliente.SendAsync(peticion);
    }

    [Fact]
    public async Task El_plan_Esencial_no_puede_subir_fotos_de_trabajos()
    {
        await UnPrestadorConPlanAsync("mensual");

        var r = await SubirAsync("/api/mi/fotos-trabajo", UnaImagen(800, 600), "obra.jpg", "image/jpeg");

        Assert.Equal(HttpStatusCode.UnprocessableEntity, r.StatusCode);
        Assert.Contains("Profesional", await r.Content.ReadAsStringAsync());
        Assert.Equal(0, (await MisFotosDeTrabajoAsync()).GetProperty("maximo").GetInt32());
    }

    [Fact]
    public async Task El_Profesional_sube_hasta_12_y_la_13_se_rechaza()
    {
        var perfilId = await UnPrestadorConPlanAsync("profesional");
        for (var i = 0; i < 12; i++) await SubirFotoDeTrabajoAsync(UnaImagen(120, 80));

        var decimotercera = await SubirAsync("/api/mi/fotos-trabajo", UnaImagen(120, 80), "obra.jpg", "image/jpeg");

        Assert.Equal(HttpStatusCode.UnprocessableEntity, decimotercera.StatusCode);
        Assert.Contains("máximo de tu plan", await decimotercera.Content.ReadAsStringAsync());
        using var ctx = ContextoDePrueba.Crear();
        Assert.Equal(12, await ctx.FotosDeTrabajo.CountAsync(f => f.PerfilPrestadorId == perfilId));
    }

    [Fact]
    public async Task La_foto_de_trabajo_se_guarda_webp_de_1200_sin_metadatos_y_en_orden()
    {
        const string marca = "GPS-ZASS-NO-DEBE-SALIR";
        await UnPrestadorConPlanAsync("profesional");

        var primera = await SubirFotoDeTrabajoAsync(UnaImagen(2400, 1200, marcaEnExif: marca));
        var segunda = await SubirFotoDeTrabajoAsync();

        var servida = await _cliente.GetAsync(primera.Url);
        Assert.Equal(HttpStatusCode.OK, servida.StatusCode);
        Assert.Equal("image/webp", servida.Content.Headers.ContentType!.MediaType);
        var bytes = await servida.Content.ReadAsByteArrayAsync();
        var info = Image.Identify(bytes);
        Assert.Equal(1200, info.Width);     // el lado mayor queda en 1200...
        Assert.Equal(600, info.Height);     // ...sin recortar ni aplastar
        Assert.DoesNotContain(marca, Encoding.ASCII.GetString(bytes));
        Assert.Null(info.Metadata.ExifProfile);

        var fotos = (await MisFotosDeTrabajoAsync()).GetProperty("fotos").EnumerateArray().ToList();
        Assert.Equal(new[] { primera.Id, segunda.Id }, fotos.Select(f => f.GetProperty("id").GetGuid()));
        Assert.Equal(new[] { 0, 1 }, fotos.Select(f => f.GetProperty("orden").GetInt32()));
    }

    [Fact]
    public async Task Ordenar_exige_todas_las_propias_y_cambia_el_orden()
    {
        await UnPrestadorConPlanAsync("profesional");
        var a = await SubirFotoDeTrabajoAsync();
        var b = await SubirFotoDeTrabajoAsync();
        var c = await SubirFotoDeTrabajoAsync();

        // Incompleta, repetida o con un id que no es suyo: no se toca nada.
        Assert.Equal(HttpStatusCode.BadRequest, (await OrdenarFotosAsync(c.Id, a.Id)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await OrdenarFotosAsync(c.Id, c.Id, a.Id)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await OrdenarFotosAsync(c.Id, Guid.NewGuid(), a.Id)).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await OrdenarFotosAsync(c.Id, a.Id, b.Id)).StatusCode);

        var ids = (await MisFotosDeTrabajoAsync()).GetProperty("fotos").EnumerateArray()
            .Select(f => f.GetProperty("id").GetGuid());
        Assert.Equal(new[] { c.Id, a.Id, b.Id }, ids);
    }

    [Fact]
    public async Task Una_foto_ajena_no_se_borra_y_la_propia_se_va_del_disco()
    {
        await UnPrestadorConPlanAsync("profesional");
        var deA = await SubirFotoDeTrabajoAsync();

        await UnPrestadorConPlanAsync("profesional");
        var deB = await SubirFotoDeTrabajoAsync();

        // B intenta borrar la de A: responde como si no existiera, y la de A sigue ahí.
        Assert.Equal(HttpStatusCode.NotFound, (await BorrarConCsrfAsync($"/api/mi/fotos-trabajo/{deA.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _cliente.GetAsync(deA.Url)).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await BorrarConCsrfAsync($"/api/mi/fotos-trabajo/{deB.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _cliente.GetAsync(deB.Url)).StatusCode);
        Assert.Empty((await MisFotosDeTrabajoAsync()).GetProperty("fotos").EnumerateArray());
    }

    [Fact]
    public async Task Sin_sesion_las_fotos_de_trabajos_responden_401()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await _cliente.GetAsync("/api/mi/fotos-trabajo")).StatusCode);
    }
}
