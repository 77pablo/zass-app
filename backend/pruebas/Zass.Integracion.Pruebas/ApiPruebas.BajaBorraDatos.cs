using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Zass.Dominio.Comun;
using Zass.Dominio.Identidad;

namespace Zass.Integracion.Pruebas;

/// <summary>
/// CS-04 / API-17 (Ley 21.719): eliminar la cuenta BORRA de verdad los datos personales, y con el MFA
/// activo pide también el código. Contra la base y el almacén reales de las pruebas: lo que se mira es
/// lo que se vería si fallara (la ficha con los datos, la foto sirviéndose por API-73, el PDF en disco).
/// </summary>
public partial class ApiPruebas
{
    private async Task<HttpResponseMessage> EliminarMiCuentaAsync(object cuerpo)
    {
        var peticion = new HttpRequestMessage(HttpMethod.Delete, "/api/mi/cuenta")
        {
            Content = JsonContent.Create(cuerpo),
        };
        peticion.Headers.Add("X-CSRF-Token", await TokenCsrfAsync());
        return await _cliente.SendAsync(peticion);
    }

    [Fact]
    public async Task Eliminar_la_cuenta_borra_la_ficha_las_fotos_los_documentos_y_los_push_y_las_fotos_ya_no_se_sirven()
    {
        // Un prestador Profesional (admite fotos de trabajos) con TODO lo que la baja tiene que borrar.
        var perfilId = await UnPrestadorConPlanAsync("profesional");

        var foto = await UrlFotoDeAsync(await SubirAsync("/api/mi/foto", UnaImagen(400, 400), "yo.jpg", "image/jpeg"));
        var (_, urlTrabajo) = await SubirFotoDeTrabajoAsync(UnaImagen(300, 200));
        var credencialId = await CredencialPropiaAsync();
        Assert.Equal(HttpStatusCode.Created,
            (await SubirAsync($"/api/mi/credenciales/{credencialId}/documentos", UnPdf, "cedula.pdf", "application/pdf")).StatusCode);

        string slug, claveDocumento;
        Guid usuarioId;
        using (var ctx = ContextoDePrueba.Crear())
        {
            var perfil = await ctx.PerfilesPrestador.SingleAsync(p => p.Id == perfilId);
            usuarioId = perfil.UsuarioId;
            slug = perfil.Slug;
            // Una ficha completa, con los datos que no se pueden quedar.
            perfil.Estado = EstadoPerfil.Publicado;
            perfil.PublicadoEn = DateTime.UtcNow.AddDays(-1);
            perfil.NombrePublico = "Marcela Soto Gasfitería";
            perfil.Descripcion = "Veinte años de oficio";
            perfil.TelefonoPublico = "+56911112222";
            perfil.CorreoPublico = "marcela@gasfiteria.cl";
            perfil.Latitud = -38.73;
            perfil.Longitud = -72.59;
            perfil.DireccionCifrada = new byte[] { 1, 2, 3 };
            ctx.SuscripcionesPush.Add(new SuscripcionPush
            {
                UsuarioId = usuarioId, Endpoint = $"https://push.ejemplo/{Guid.NewGuid():N}",
                ClaveP256dh = "p", ClaveAuth = "a",
            });
            await ctx.SaveChangesAsync();
            claveDocumento = await ctx.DocumentosCredencial
                .Where(d => d.Credencial!.PerfilPrestadorId == perfilId)
                .Select(d => d.ClaveAlmacenamiento).SingleAsync();
        }
        var grande = foto;
        Assert.Equal(HttpStatusCode.OK, (await _cliente.GetAsync(grande)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _cliente.GetAsync(Chica(grande))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _cliente.GetAsync(urlTrabajo)).StatusCode);
        Assert.True(File.Exists(RutaEnDisco(claveDocumento)));
        Assert.Equal(HttpStatusCode.OK, (await _cliente.GetAsync($"/api/prestadores/{slug}")).StatusCode);

        try
        {
            var r = await EliminarMiCuentaAsync(new { contrasena = "contrasena-larga-1" });
            Assert.Equal(HttpStatusCode.NoContent, r.StatusCode);

            // La ficha: la fila sigue (citas y reseñas la referencian) pero sin ningun dato personal.
            using var ctx = ContextoDePrueba.Crear();
            var perfil = await ctx.PerfilesPrestador.AsNoTracking().SingleAsync(p => p.Id == perfilId);
            Assert.Equal(EstadoPerfil.Despublicado, perfil.Estado);
            Assert.Equal("Perfil eliminado", perfil.NombrePublico);
            Assert.NotEqual(slug, perfil.Slug);
            Assert.Null(perfil.Descripcion);
            Assert.Null(perfil.UrlFoto);
            Assert.Null(perfil.TelefonoPublico);
            Assert.Null(perfil.CorreoPublico);
            Assert.Null(perfil.Latitud);
            Assert.Null(perfil.Longitud);
            Assert.Null(perfil.DireccionCifrada);

            // Las filas de fotos, documentos y push: borradas.
            Assert.False(await ctx.FotosDeTrabajo.AnyAsync(f => f.PerfilPrestadorId == perfilId));
            Assert.False(await ctx.DocumentosCredencial.AnyAsync(d => d.Credencial!.PerfilPrestadorId == perfilId));
            Assert.False(await ctx.SuscripcionesPush.AnyAsync(s => s.UsuarioId == usuarioId));
            // La credencial queda para la auditoria, sin lo que la liga a la persona.
            var credencial = await ctx.Credenciales.AsNoTracking().SingleAsync(c => c.PerfilPrestadorId == perfilId);
            Assert.Equal("Credencial eliminada", credencial.Nombre);

            // Los archivos: ya no se sirven por API-73 (publicas) y no estan en disco.
            Assert.Equal(HttpStatusCode.NotFound, (await _cliente.GetAsync(grande)).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await _cliente.GetAsync(Chica(grande))).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await _cliente.GetAsync(urlTrabajo)).StatusCode);
            Assert.False(File.Exists(RutaEnDisco(claveDocumento)));
            // Y la ficha, ni por el slug de antes ni por el nuevo: como si no existiera.
            Assert.Equal(HttpStatusCode.NotFound, (await _cliente.GetAsync($"/api/prestadores/{slug}")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await _cliente.GetAsync($"/api/prestadores/{perfil.Slug}")).StatusCode);
        }
        finally
        {
            // La cuenta quedo anonimizada (el correo ya no es el de la prueba): se limpia por id.
            using var ctx = ContextoDePrueba.Crear();
            ctx.Auditoria.RemoveRange(ctx.Auditoria.Where(a => a.ActorId == usuarioId || a.EntidadId == usuarioId));
            ctx.Usuarios.RemoveRange(ctx.Usuarios.Where(u => u.Id == usuarioId));
            await ctx.SaveChangesAsync();
        }
    }

    [Fact]
    public async Task Eliminar_la_cuenta_con_mfa_pide_el_codigo_y_cuenta_los_intentos_fallidos()
    {
        var correo = NuevoCorreo();
        await RegistrarAsync(correo);
        await VerificarAsync(correo);
        await IniciarSesionAsync(correo);
        // La sesion ya esta abierta; el MFA se activa despues, como si la persona lo hubiera activado.
        var secreto = await MfaDePrueba.ActivarAsync(correo);
        var usuarioId = (await CuentaEnBaseAsync(correo)).Id;

        try
        {
            // Solo la contraseña: no alcanza. Y la respuesta no dice cual de las dos cosas fallo.
            var sinCodigo = await EliminarMiCuentaAsync(new { contrasena = ClaveBuena });
            var codigoMalo = await EliminarMiCuentaAsync(new { contrasena = ClaveBuena, codigo = "000000" == CodigoTotp(secreto) ? "111111" : "000000" });
            var contrasenaMala = await EliminarMiCuentaAsync(new { contrasena = ClaveMala, codigo = CodigoTotp(secreto) });
            Assert.All(new[] { sinCodigo, codigoMalo, contrasenaMala },
                r => Assert.Equal(HttpStatusCode.UnprocessableEntity, r.StatusCode));
            var mensajes = new[] { sinCodigo, codigoMalo, contrasenaMala }
                .Select(r => r.Content.ReadAsStringAsync().Result).ToArray();
            Assert.All(mensajes, m => Assert.Contains("La contraseña o el código no corresponden.", m));

            var tras = await CuentaEnBaseAsync(correo);
            Assert.Equal(3, tras.IntentosFallidos);                    // sumaron al bloqueo por intentos
            Assert.Equal(EstadoCuenta.Activa, tras.Estado);            // y no se toco nada
            Assert.Equal(HttpStatusCode.OK, (await _cliente.GetAsync("/api/mi/cuenta")).StatusCode);

            // Contraseña y codigo buenos: ahora si.
            var bien = await EliminarMiCuentaAsync(new { contrasena = ClaveBuena, codigo = CodigoTotp(secreto) });
            Assert.Equal(HttpStatusCode.NoContent, bien.StatusCode);
            using var ctx = ContextoDePrueba.Crear();
            Assert.Equal(EstadoCuenta.Eliminada, (await ctx.Usuarios.AsNoTracking().SingleAsync(u => u.Id == usuarioId)).Estado);
        }
        finally
        {
            using var ctx = ContextoDePrueba.Crear();
            ctx.Auditoria.RemoveRange(ctx.Auditoria.Where(a => a.ActorId == usuarioId || a.EntidadId == usuarioId));
            ctx.Usuarios.RemoveRange(ctx.Usuarios.Where(u => u.Id == usuarioId));
            await ctx.SaveChangesAsync();
        }
    }
}
