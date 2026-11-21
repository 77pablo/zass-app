using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Zass.Aplicacion.Identidad;
using Zass.Dominio.Comun;
using Zass.Dominio.Identidad;

namespace Zass.Integracion.Pruebas;

/// <summary>
/// Cuatro hallazgos de la auditoria de seguridad (21-nov-2026), por HTTP y contra la base real:
/// la espera de «olvide mi contraseña» (API-14), el Cache-Control por omision de la API y el rol
/// del panel interno revalidado en la base. API-55 (activar tarjeta) vive en
/// <see cref="ApiTarjetasEmitirActivarPruebas"/> y en <see cref="AutorizacionEnRutasPrivadasPruebas"/>.
/// </summary>
public partial class ApiPruebas
{
    private const short RolAdministradorId = 5;

    private Task<HttpResponseMessage> PedirRecuperacionAsync(string correo) =>
        _cliente.PostAsJsonAsync("/api/auth/contrasena/recuperar", new { correo });

    private static async Task<List<string>> TokensDeRecuperacionAsync(string correo)
    {
        using var ctx = ContextoDePrueba.Crear();
        return (await ctx.Notificaciones
                .Where(n => n.Destinatario!.Correo == correo && n.Tipo == TipoNotificacion.RecuperarContrasena)
                .Select(n => n.Cuerpo)
                .ToListAsync())
            .Select(c => Zass.Aplicacion.Puertos.CargaDeAviso.LeerToken(c)!)
            .ToList();
    }

    // ====================================================== API-14 · olvide mi contraseña

    /// <summary>
    /// Dos pedidos seguidos para la misma cuenta encolan UN solo correo y NO le invalidan el
    /// enlace que ya tiene; y la respuesta es identica para la cuenta existente, la que esta en
    /// la espera y la que no existe. Antes cada pedido pisaba el token y encolaba otro correo:
    /// desde varias IP se le llenaba la bandeja a la victima y se le rompia el enlace vigente.
    /// </summary>
    [Fact]
    public async Task Recuperar_dos_veces_seguidas_encola_un_solo_correo_y_no_rompe_el_enlace()
    {
        var correo = NuevoCorreo();
        await RegistrarAsync(correo);
        var inexistente = NuevoCorreo();

        var primero = await PedirRecuperacionAsync(correo);
        var enEspera = await PedirRecuperacionAsync(correo);
        var noExiste = await PedirRecuperacionAsync(inexistente);

        // Por fuera: lo mismo en los tres casos (estado y cuerpo).
        Assert.Equal(HttpStatusCode.OK, primero.StatusCode);
        var cuerpo = await primero.Content.ReadAsStringAsync();
        foreach (var r in new[] { enEspera, noExiste })
        {
            Assert.Equal(primero.StatusCode, r.StatusCode);
            Assert.Equal(cuerpo, await r.Content.ReadAsStringAsync());
        }

        // Por dentro: un solo correo, y su enlace sigue sirviendo.
        var token = Assert.Single(await TokensDeRecuperacionAsync(correo));
        var restablecer = await _cliente.PostAsJsonAsync("/api/auth/contrasena/restablecer", new
        {
            token, contrasenaNueva = "otra-contrasena-larga-para-pruebas-9",
        });
        Assert.Equal(HttpStatusCode.NoContent, restablecer.StatusCode);
    }

    /// <summary>
    /// Pasada la espera, se puede pedir otro enlace: la espera frena la inundacion, no deja a
    /// nadie sin poder recuperar su cuenta. (Se simula el paso del tiempo corriendo la fecha
    /// del token en la base.)
    /// </summary>
    [Fact]
    public async Task Recuperar_pasada_la_espera_vuelve_a_mandar_un_enlace()
    {
        var correo = NuevoCorreo();
        await RegistrarAsync(correo);
        await PedirRecuperacionAsync(correo);

        using (var ctx = ContextoDePrueba.Crear())
        {
            var u = await ctx.Usuarios.SingleAsync(x => x.Correo == correo);
            u.TokenRecuperacionExpiraEn = u.TokenRecuperacionExpiraEn!.Value
                - ServicioCuenta.EsperaEntreReenvios - TimeSpan.FromSeconds(5);
            await ctx.SaveChangesAsync();
        }

        var r = await PedirRecuperacionAsync(correo);

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Equal(2, (await TokensDeRecuperacionAsync(correo)).Count);
    }

    /// <summary>
    /// Pedidos SIMULTANEOS para la misma cuenta (API-14 y API-71): ninguno sale 500 y todos
    /// responden lo mismo que una cuenta inexistente. Antes la clave del aviso era
    /// «tipo:usuario:segundo» sobre un indice unico: dos en el mismo segundo chocaban y salian
    /// 500 solo si la cuenta existia, un oraculo de existencia.
    /// </summary>
    [Fact]
    public async Task Recuperar_y_reenviar_simultaneos_nunca_dan_500_ni_se_distinguen_de_una_cuenta_inexistente()
    {
        var correo = NuevoCorreo();
        await RegistrarAsync(correo);

        // El registro acaba de mandar el enlace de verificacion: se le corre la fecha para que el
        // reenvio no este en la espera y los pedidos simultaneos de verdad intenten emitir.
        using (var ctx = ContextoDePrueba.Crear())
        {
            var u = await ctx.Usuarios.SingleAsync(x => x.Correo == correo);
            u.TokenVerificacionExpiraEn = u.TokenVerificacionExpiraEn!.Value
                - ServicioCuenta.EsperaEntreReenvios - TimeSpan.FromSeconds(5);
            await ctx.SaveChangesAsync();
        }

        var referencia = new Dictionary<string, string>();
        foreach (var url in new[] { "/api/auth/contrasena/recuperar", "/api/auth/correo/reenviar" })
        {
            var r = await _cliente.PostAsJsonAsync(url, new { correo = NuevoCorreo() });
            Assert.Equal(HttpStatusCode.OK, r.StatusCode);
            referencia[url] = await r.Content.ReadAsStringAsync();
        }

        var pedidos = Enumerable.Range(0, 8)
            .Select(i => i % 2 == 0 ? "/api/auth/contrasena/recuperar" : "/api/auth/correo/reenviar")
            .Select(async url => (Url: url, Respuesta: await _cliente.PostAsJsonAsync(url, new { correo })))
            .ToArray();
        var respuestas = await Task.WhenAll(pedidos);

        foreach (var (url, r) in respuestas)
        {
            Assert.Equal(HttpStatusCode.OK, r.StatusCode);
            Assert.Equal(referencia[url], await r.Content.ReadAsStringAsync());
        }
        Assert.NotEmpty(await TokensDeRecuperacionAsync(correo));
    }

    // ====================================================== Cache-Control por omision

    /// <summary>
    /// Lo que la sesion ve no lo guarda ningun cache: un endpoint de /api/mi que no fija su
    /// propio Cache-Control sale con «private, no-store» (lo pone el middleware). Y la foto
    /// publica (API-73), que SI fija el suyo, lo conserva: cache inmutable, sin no-store.
    /// </summary>
    [Fact]
    public async Task Api_mi_sale_private_no_store_y_la_foto_publica_conserva_su_cache_inmutable()
    {
        await UnPrestadorConSesionAsync();

        var cuenta = await _cliente.GetAsync("/api/mi/cuenta");
        Assert.Equal(HttpStatusCode.OK, cuenta.StatusCode);
        Assert.True(cuenta.Headers.CacheControl?.Private, $"Cache-Control: {cuenta.Headers.CacheControl}");
        Assert.True(cuenta.Headers.CacheControl?.NoStore, $"Cache-Control: {cuenta.Headers.CacheControl}");

        var subida = await SubirAsync("/api/mi/foto", UnaImagen(400, 400), "cara.jpg", "image/jpeg");
        Assert.Equal(HttpStatusCode.OK, subida.StatusCode);
        var foto = await _cliente.GetAsync(await UrlFotoDeAsync(subida));

        Assert.Equal(HttpStatusCode.OK, foto.StatusCode);
        var cache = foto.Headers.CacheControl!;
        Assert.True(cache.Public, $"Cache-Control: {cache}");
        Assert.False(cache.NoStore, $"Cache-Control: {cache}");
        Assert.False(cache.Private, $"Cache-Control: {cache}");
        Assert.Contains("immutable", cache.ToString());
        Assert.Equal(TimeSpan.FromDays(365), cache.MaxAge);
    }

    // ====================================================== rol del panel, revalidado en base

    /// <summary>
    /// A un verificador con la sesion vigente se le quita el rol en la base: la peticion
    /// siguiente a /api/interno ya es 403. Antes el rol vivia en la cookie y le quedaban hasta
    /// 15 minutos de acceso a la cola y a los documentos de credenciales.
    /// </summary>
    [Fact]
    public async Task Quitarle_el_rol_a_un_verificador_con_sesion_le_corta_el_panel_de_inmediato()
    {
        var (correo, id) = await UnaCuentaAsync("buscador", verificador: true);
        await IniciarSesionAsync(correo);
        Assert.Equal(HttpStatusCode.OK, (await _cliente.GetAsync("/api/interno/credenciales/cola")).StatusCode);

        using (var ctx = ContextoDePrueba.Crear())
        {
            ctx.UsuarioRoles.RemoveRange(
                ctx.UsuarioRoles.Where(r => r.UsuarioId == id && r.RolId == RolVerificador));
            await ctx.SaveChangesAsync();
        }

        var despues = await _cliente.GetAsync("/api/interno/credenciales/cola");
        Assert.Equal(HttpStatusCode.Forbidden, despues.StatusCode);
    }

    /// <summary>Lo mismo si la cuenta se suspende: el rol sigue en la base, pero la cuenta no.</summary>
    [Fact]
    public async Task Suspender_a_un_verificador_con_sesion_le_corta_el_panel_de_inmediato()
    {
        var (correo, id) = await UnaCuentaAsync("buscador", verificador: true);
        await IniciarSesionAsync(correo);
        Assert.Equal(HttpStatusCode.OK, (await _cliente.GetAsync("/api/interno/credenciales/cola")).StatusCode);

        using (var ctx = ContextoDePrueba.Crear())
        {
            var u = await ctx.Usuarios.SingleAsync(x => x.Id == id);
            u.Estado = EstadoCuenta.Suspendida;
            await ctx.SaveChangesAsync();
        }

        Assert.Equal(HttpStatusCode.Forbidden,
            (await _cliente.GetAsync("/api/interno/credenciales/cola")).StatusCode);
    }

    /// <summary>
    /// Y para el administrador: la auditoria (con IPs) deja de responderle en cuanto pierde el rol.
    /// </summary>
    [Fact]
    public async Task Quitarle_el_rol_a_un_administrador_con_sesion_le_corta_la_auditoria_de_inmediato()
    {
        var (correo, id) = await UnaCuentaAsync("buscador");
        using (var ctx = ContextoDePrueba.Crear())
        {
            ctx.UsuarioRoles.Add(new UsuarioRol { UsuarioId = id, RolId = RolAdministradorId });
            await ctx.SaveChangesAsync();
        }
        await IniciarSesionAsync(correo);
        var url = $"/api/interno/auditoria?actorId={id}";
        Assert.Equal(HttpStatusCode.OK, (await _cliente.GetAsync(url)).StatusCode);

        using (var ctx = ContextoDePrueba.Crear())
        {
            ctx.UsuarioRoles.RemoveRange(
                ctx.UsuarioRoles.Where(r => r.UsuarioId == id && r.RolId == RolAdministradorId));
            await ctx.SaveChangesAsync();
        }

        Assert.Equal(HttpStatusCode.Forbidden, (await _cliente.GetAsync(url)).StatusCode);
    }
}
