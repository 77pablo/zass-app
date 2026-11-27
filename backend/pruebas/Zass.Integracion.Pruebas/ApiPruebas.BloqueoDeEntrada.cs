using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Zass.Aplicacion.Identidad;

namespace Zass.Integracion.Pruebas;

/// <summary>
/// Bloqueo de la cuenta tras intentos fallidos de entrar (API-09, decision de Pablo del
/// 22-nov-2026; 30 desde el 25-nov): 30 fallos seguidos dejan la cuenta 15 minutos sin poder entrar. Antes solo
/// habia limite por IP, y desde muchas IP se podian probar contrasenas sin freno.
///
/// Lo que mas importa aqui es lo que NO se ve: bloqueada, la respuesta es identica a la de una
/// contrasena mala y a la de un correo que no existe, asi el bloqueo no sirve para averiguar
/// que cuentas existen ni si la contrasena probada era la buena.
/// </summary>
public partial class ApiPruebas
{
    private const string ClaveBuena = "contrasena-larga-1";
    private const string ClaveMala = "otra-contrasena-larga";

    private Task<HttpResponseMessage> EntrarAsync(string correo, string contrasena) =>
        _cliente.PostAsJsonAsync("/api/auth/sesion", new { correo, contrasena });

    private async Task FallarAsync(string correo, int veces)
    {
        for (var i = 0; i < veces; i++)
            Assert.Equal(HttpStatusCode.Unauthorized, (await EntrarAsync(correo, ClaveMala)).StatusCode);
    }

    private static async Task<Zass.Dominio.Identidad.Usuario> UsuarioAsync(string correo)
    {
        using var ctx = ContextoDePrueba.Crear();
        return await ctx.Usuarios.AsNoTracking().SingleAsync(u => u.Correo == correo);
    }

    private static async Task<int> BloqueosAuditadosAsync(Guid usuarioId)
    {
        using var ctx = ContextoDePrueba.Crear();
        return await ctx.Auditoria.CountAsync(
            a => a.Accion == "cuenta_bloqueada_por_intentos" && a.EntidadId == usuarioId);
    }

    /// <summary>
    /// Treinta fallos bloquean. Bloqueada, la contrasena correcta NO entra, y su respuesta es
    /// la misma (codigo y cuerpo) que la de una contrasena mala antes del bloqueo, una mala
    /// durante el bloqueo y un correo que no existe. El bloqueo dura 15 minutos y deja UNA
    /// entrada de auditoria.
    /// </summary>
    [Fact]
    public async Task Treinta_fallos_bloquean_y_bloqueada_la_contrasena_correcta_no_entra_ni_se_distingue()
    {
        var correo = NuevoCorreo();
        await RegistrarAsync(correo);

        var malaAntes = await EntrarAsync(correo, ClaveMala);
        var cuerpoMalaAntes = await CuerpoComparable.De(malaAntes);
        await FallarAsync(correo, ServicioSesion.IntentosParaBloquear - 1);

        var antesDeLaBuena = DateTime.UtcNow;
        var buenaBloqueada = await EntrarAsync(correo, ClaveBuena);
        var malaBloqueada = await EntrarAsync(correo, ClaveMala);
        var sinCuenta = await EntrarAsync("nadie-" + Guid.NewGuid().ToString("N") + "@zass.local", ClaveBuena);

        Assert.Equal(HttpStatusCode.Unauthorized, malaAntes.StatusCode);
        foreach (var r in new[] { buenaBloqueada, malaBloqueada, sinCuenta })
        {
            Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode);
            Assert.Equal(cuerpoMalaAntes, await CuerpoComparable.De(r));
            var cookies = r.Headers.TryGetValues("Set-Cookie", out var v) ? v.ToList() : new List<string>();
            Assert.DoesNotContain(cookies, c => c.StartsWith("zass_sesion") || c.StartsWith("zass_refresco"));
        }

        var usuario = await UsuarioAsync(correo);
        Assert.NotNull(usuario.BloqueadaHasta);
        // 15 minutos contados desde el trigesimo fallo (que fue justo antes de la buena).
        var plazo = usuario.BloqueadaHasta!.Value - antesDeLaBuena;
        Assert.InRange(plazo, ServicioSesion.DuracionDelBloqueo - TimeSpan.FromMinutes(1),
            ServicioSesion.DuracionDelBloqueo);
        Assert.Equal(TimeSpan.FromMinutes(15), ServicioSesion.DuracionDelBloqueo);
        Assert.Equal(30, ServicioSesion.IntentosParaBloquear);
        Assert.Equal(1, await BloqueosAuditadosAsync(usuario.Id));

        // Y a la dueña le llega un correo (25-nov-2026): con la cola de verdad, la que inyecta la API.
        using (var ctx = ContextoDePrueba.Crear())
            Assert.Equal(1, await ctx.Notificaciones.CountAsync(n =>
                n.DestinatarioId == usuario.Id && n.Tipo == Zass.Dominio.Comun.TipoNotificacion.CuentaBloqueadaPorIntentos));

        // Y tampoco hay sesion por la puerta de atras.
        Assert.Equal(HttpStatusCode.Unauthorized, (await _cliente.GetAsync("/api/mi/cuenta")).StatusCode);
    }

    /// <summary>Veintinueve fallos no bloquean: el trigesimo es el que cuenta.</summary>
    [Fact]
    public async Task Veintinueve_fallos_no_bloquean()
    {
        var correo = NuevoCorreo();
        await RegistrarAsync(correo);

        await FallarAsync(correo, ServicioSesion.IntentosParaBloquear - 1);

        Assert.Equal(HttpStatusCode.OK, (await EntrarAsync(correo, ClaveBuena)).StatusCode);
    }

    /// <summary>
    /// «Seguidos»: entrar bien pone el contador en cero. Nueve fallos, entrar, otros nueve
    /// fallos, y se puede entrar de nuevo (sin el reinicio serian 18 y estaria bloqueada).
    /// </summary>
    [Fact]
    public async Task Entrar_bien_reinicia_el_contador()
    {
        var correo = NuevoCorreo();
        await RegistrarAsync(correo);

        await FallarAsync(correo, ServicioSesion.IntentosParaBloquear - 1);
        Assert.Equal(HttpStatusCode.OK, (await EntrarAsync(correo, ClaveBuena)).StatusCode);
        Assert.Equal(0, (await UsuarioAsync(correo)).IntentosFallidos);

        await FallarAsync(correo, ServicioSesion.IntentosParaBloquear - 1);
        Assert.Equal(HttpStatusCode.OK, (await EntrarAsync(correo, ClaveBuena)).StatusCode);
    }

    /// <summary>
    /// Pasados los 15 minutos se puede entrar, y el contador empieza de nuevo: un fallo
    /// despues del bloqueo no vuelve a bloquear. (El paso del tiempo se simula corriendo
    /// bloqueada_hasta en la base.)
    /// </summary>
    [Fact]
    public async Task Pasado_el_bloqueo_se_puede_entrar_y_el_contador_empieza_de_nuevo()
    {
        var correo = NuevoCorreo();
        await RegistrarAsync(correo);
        await FallarAsync(correo, ServicioSesion.IntentosParaBloquear);
        Assert.Equal(HttpStatusCode.Unauthorized, (await EntrarAsync(correo, ClaveBuena)).StatusCode);

        using (var ctx = ContextoDePrueba.Crear())
        {
            var u = await ctx.Usuarios.SingleAsync(x => x.Correo == correo);
            u.BloqueadaHasta = DateTime.UtcNow - TimeSpan.FromSeconds(5);
            await ctx.SaveChangesAsync();
        }

        await FallarAsync(correo, 1);
        Assert.Equal(HttpStatusCode.OK, (await EntrarAsync(correo, ClaveBuena)).StatusCode);
    }

    /// <summary>
    /// Los fallos durante el bloqueo no cuentan. Si contaran, quien siga probando contrasenas
    /// mientras la cuenta esta bloqueada la volveria a bloquear sin fin (la duena no entraria
    /// nunca) y llenaria la auditoria. El plazo no se mueve y queda UNA entrada de bloqueo.
    /// </summary>
    [Fact]
    public async Task Los_fallos_durante_el_bloqueo_no_alargan_el_bloqueo_ni_lo_repiten()
    {
        var correo = NuevoCorreo();
        await RegistrarAsync(correo);
        await FallarAsync(correo, ServicioSesion.IntentosParaBloquear);
        var bloqueada = await UsuarioAsync(correo);
        Assert.NotNull(bloqueada.BloqueadaHasta);

        await FallarAsync(correo, ServicioSesion.IntentosParaBloquear);

        var despues = await UsuarioAsync(correo);
        Assert.Equal(bloqueada.BloqueadaHasta, despues.BloqueadaHasta);
        Assert.Equal(0, despues.IntentosFallidos);
        Assert.Equal(1, await BloqueosAuditadosAsync(despues.Id));
    }

    /// <summary>
    /// Recuperar la contrasena sigue funcionando con la cuenta bloqueada, y restablecerla la
    /// desbloquea: es la salida de la duena si alguien la dejo fuera a proposito.
    /// </summary>
    [Fact]
    public async Task Restablecer_la_contrasena_desbloquea_la_cuenta()
    {
        var correo = NuevoCorreo();
        await RegistrarAsync(correo);
        await FallarAsync(correo, ServicioSesion.IntentosParaBloquear);
        Assert.Equal(HttpStatusCode.Unauthorized, (await EntrarAsync(correo, ClaveBuena)).StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await PedirRecuperacionAsync(correo)).StatusCode);
        var token = Assert.Single(await TokensDeRecuperacionAsync(correo));
        const string nueva = "una-contrasena-nueva-larga-7";
        var restablecer = await _cliente.PostAsJsonAsync("/api/auth/contrasena/restablecer", new
        {
            token, contrasenaNueva = nueva,
        });
        Assert.Equal(HttpStatusCode.NoContent, restablecer.StatusCode);

        var usuario = await UsuarioAsync(correo);
        Assert.Null(usuario.BloqueadaHasta);
        Assert.Equal(0, usuario.IntentosFallidos);
        Assert.Equal(HttpStatusCode.OK, (await EntrarAsync(correo, nueva)).StatusCode);
    }

    /// <summary>
    /// Treinta fallos SIMULTANEOS cuentan treinta: el incremento es atomico en la base. Si cada
    /// peticion leyera el contador y escribiera el suyo, varias leerian el mismo valor y el
    /// conteo se quedaria corto, y la contrasena correcta entraria. Y el bloqueo se audita una
    /// sola vez aunque lo disparen peticiones en paralelo.
    /// </summary>
    [Fact]
    public async Task Intentos_simultaneos_no_se_saltan_el_conteo()
    {
        var correo = NuevoCorreo();
        await RegistrarAsync(correo);

        var simultaneos = await Task.WhenAll(Enumerable.Range(0, ServicioSesion.IntentosParaBloquear)
            .Select(_ => EntrarAsync(correo, ClaveMala)));
        Assert.All(simultaneos, r => Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode));

        Assert.Equal(HttpStatusCode.Unauthorized, (await EntrarAsync(correo, ClaveBuena)).StatusCode);
        var usuario = await UsuarioAsync(correo);
        Assert.NotNull(usuario.BloqueadaHasta);
        Assert.Equal(1, await BloqueosAuditadosAsync(usuario.Id));
    }
}
