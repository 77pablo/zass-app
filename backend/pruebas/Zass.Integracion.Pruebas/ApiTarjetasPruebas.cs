using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Zass.Api.Endpoints;
using Zass.Dominio.Comun;
using Zass.Dominio.Identidad;
using Zass.Dominio.Tarjetas;

namespace Zass.Integracion.Pruebas;

/// <summary>
/// API-54 · <c>GET /t/{segmento}</c> por HTTP y contra <c>zass_pruebas</c> (CS-18, incluye CU-09).
/// Lo que solo aqui se ve: el 302 y su Location, las cabeceras, que no salga ninguna cookie,
/// el limite de tasa, y la fila que queda (o no) en <c>escaneos</c>.
/// </summary>
public class ApiTarjetasPruebas : IAsyncLifetime
{
    private readonly Aplicacion _app = new();
    private HttpClient _cliente = null!;

    private string _marca = "";
    private string _slug = "";
    private readonly List<Guid> _perfiles = new();
    private readonly List<Guid> _tarjetas = new();

    private Tarjeta _activa = null!;
    private Tarjeta _desactivada = null!;
    private Tarjeta _reemplazada = null!;
    private Tarjeta _emitida = null!;
    private Tarjeta _activaDespublicada = null!;
    private string _inexistente = "";

    public async Task InitializeAsync()
    {
        // Sin manejo de cookies: asi cualquier Set-Cookie queda a la vista en la respuesta.
        _cliente = _app.CreateClient(new WebApplicationFactoryClientOptions
        {
            HandleCookies = false, AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost"),
        });

        _marca = Guid.NewGuid().ToString("N")[..12];
        _slug = $"apitj-{_marca}";

        using var ctx = ContextoDePrueba.Crear();
        var rubro = await ctx.Rubros.FirstAsync(r => r.Activo);

        PerfilPrestador NuevoPerfil(string sufijo, EstadoPerfil estado)
        {
            var u = new Usuario
            {
                Correo = $"apitj-{sufijo}-{_marca}@zass.local", HashContrasena = "x",
                Nombre = "Prestador de prueba", CorreoVerificado = true,
            };
            ctx.Usuarios.Add(u);
            var p = new PerfilPrestador
            {
                UsuarioId = u.Id, RubroId = rubro.Id, NombrePublico = "Tarjetas de prueba",
                Slug = sufijo == "pub" ? _slug : $"apitj-{sufijo}-{_marca}", Estado = estado,
                PublicadoEn = estado == EstadoPerfil.Publicado ? DateTime.UtcNow.AddDays(-1) : null,
            };
            ctx.PerfilesPrestador.Add(p);
            _perfiles.Add(p.Id);
            return p;
        }

        Tarjeta NuevaTarjeta(EstadoTarjeta estado, PerfilPrestador? dueno)
        {
            var t = new Tarjeta
            {
                UrlEstable = Tarjeta.GenerarUrlEstable(), Estado = estado,
                PerfilPrestadorId = dueno?.Id,
                ActivadaEn = estado == EstadoTarjeta.Emitida ? null : DateTime.UtcNow.AddDays(-1),
                DesactivadaEn = estado is EstadoTarjeta.Desactivada or EstadoTarjeta.Reemplazada
                    ? DateTime.UtcNow.AddHours(-1) : null,
            };
            ctx.Tarjetas.Add(t);
            _tarjetas.Add(t.Id);
            return t;
        }

        var publicado = NuevoPerfil("pub", EstadoPerfil.Publicado);
        var despublicado = NuevoPerfil("des", EstadoPerfil.Despublicado);

        _activa = NuevaTarjeta(EstadoTarjeta.Activa, publicado);
        // Las dos muertas siguen ligadas a un perfil PUBLICADO: es justo lo que no deben revelar.
        _desactivada = NuevaTarjeta(EstadoTarjeta.Desactivada, publicado);
        _reemplazada = NuevaTarjeta(EstadoTarjeta.Reemplazada, publicado);
        _reemplazada.TarjetaSucesoraId = _activa.Id;
        _emitida = NuevaTarjeta(EstadoTarjeta.Emitida, null);
        _activaDespublicada = NuevaTarjeta(EstadoTarjeta.Activa, despublicado);
        await ctx.SaveChangesAsync();

        // Un segmento con forma valida que no esta en la base.
        do _inexistente = Tarjeta.GenerarUrlEstable();
        while (await ctx.Tarjetas.AnyAsync(t => t.UrlEstable == _inexistente));
    }

    public async Task DisposeAsync()
    {
        using (var ctx = ContextoDePrueba.Crear())
        {
            await ctx.Escaneos.Where(e => _tarjetas.Contains(e.TarjetaId)).ExecuteDeleteAsync();
            await ctx.Tarjetas.Where(t => _tarjetas.Contains(t.Id)).ExecuteDeleteAsync();
            await ctx.PerfilesPrestador.Where(p => _perfiles.Contains(p.Id)).ExecuteDeleteAsync();
            await ctx.Usuarios.Where(u => u.Correo.EndsWith($"-{_marca}@zass.local")).ExecuteDeleteAsync();
        }
        _cliente.Dispose();
        await _app.DisposeAsync();
    }

    private Task<HttpResponseMessage> Escanear(string segmento, string consulta = "") =>
        _cliente.GetAsync($"/t/{segmento}{consulta}");

    private static async Task<List<EscaneoTarjeta>> EscaneosDe(params Tarjeta[] tarjetas)
    {
        var ids = tarjetas.Select(t => t.Id).ToList();
        using var ctx = ContextoDePrueba.Crear();
        return await ctx.Escaneos.AsNoTracking().Where(e => ids.Contains(e.TarjetaId)).ToListAsync();
    }

    // ------------------------------------------------------------- la ficha

    [Fact]
    public async Task Una_tarjeta_activa_redirige_302_a_la_ficha_y_deja_una_fila_en_escaneos()
    {
        var antes = DateTime.UtcNow;

        var r = await Escanear(_activa.UrlEstable);

        Assert.Equal(HttpStatusCode.Redirect, r.StatusCode);   // 302, nunca 301 (RN-S6-05)
        Assert.Equal($"/prestadores/{_slug}", r.Headers.Location?.OriginalString);
        var escaneo = Assert.Single(await EscaneosDe(_activa));
        Assert.Equal(MedioEscaneo.Nfc, escaneo.Medio);
        Assert.InRange(escaneo.OcurridoEn, antes.AddSeconds(-1), DateTime.UtcNow.AddSeconds(1));
        Assert.False(escaneo.DerivoEnContacto);
    }

    [Theory]
    [InlineData("?m=qr", MedioEscaneo.Qr)]
    [InlineData("?m=QR", MedioEscaneo.Qr)]
    [InlineData("", MedioEscaneo.Nfc)]
    [InlineData("?m=nfc", MedioEscaneo.Nfc)]
    [InlineData("?m=otra-cosa", MedioEscaneo.Nfc)]
    public async Task El_medio_sale_del_parametro_m_y_lo_raro_cuenta_como_Nfc(string consulta, MedioEscaneo esperado)
    {
        var r = await Escanear(_activa.UrlEstable, consulta);

        Assert.Equal(HttpStatusCode.Redirect, r.StatusCode);
        Assert.Equal(esperado, Assert.Single(await EscaneosDe(_activa)).Medio);
    }

    /// <summary>Lectores de QR que bajan o suben la URL entera: la ruta y el segmento aguantan.</summary>
    [Theory]
    [InlineData("/t/{0}", false)]
    [InlineData("/T/{0}", true)]
    public async Task La_ruta_y_el_segmento_no_distinguen_mayusculas(string plantilla, bool rutaEnMayusculas)
    {
        var segmento = rutaEnMayusculas ? _activa.UrlEstable : _activa.UrlEstable.ToLowerInvariant();

        var r = await _cliente.GetAsync(string.Format(plantilla, segmento));

        Assert.Equal($"/prestadores/{_slug}", r.Headers.Location?.OriginalString);
        Assert.Single(await EscaneosDe(_activa));
    }

    // ------------------------------------------------------------- lo que no es ficha

    /// <summary>
    /// RN-S6-06: desactivada, reemplazada, inexistente y malformada son la MISMA respuesta.
    /// Se comparan estado, Location, cada cabecera y el cuerpo, byte a byte.
    /// </summary>
    [Fact]
    public async Task Las_cuatro_tarjetas_no_validas_responden_exactamente_igual()
    {
        var respuestas = new[]
        {
            await Escanear(_desactivada.UrlEstable),
            await Escanear(_reemplazada.UrlEstable),
            await Escanear(_inexistente),
            await Escanear("no-es-una-tarjeta"),
        };

        var huellas = new List<string>();
        foreach (var r in respuestas)
        {
            Assert.Equal(HttpStatusCode.Redirect, r.StatusCode);
            Assert.Equal(EndpointsTarjetas.PaginaNoValida, r.Headers.Location?.OriginalString);
            huellas.Add(await Huella(r));
        }
        Assert.All(huellas, h => Assert.Equal(huellas[0], h));
        Assert.Empty(await EscaneosDe(_desactivada, _reemplazada));
    }

    [Fact]
    public async Task Una_tarjeta_emitida_lleva_a_sin_activar_y_no_cuenta()
    {
        var r = await Escanear(_emitida.UrlEstable);

        Assert.Equal(HttpStatusCode.Redirect, r.StatusCode);
        Assert.Equal(EndpointsTarjetas.PaginaSinActivar, r.Headers.Location?.OriginalString);
        Assert.Empty(await EscaneosDe(_emitida));
    }

    /// <summary>RN-S6-09: perfil despublicado → «perfil no disponible», sin slug y sin contar.</summary>
    [Fact]
    public async Task Una_tarjeta_activa_con_perfil_despublicado_lleva_a_no_disponible_y_no_cuenta()
    {
        var r = await Escanear(_activaDespublicada.UrlEstable);

        Assert.Equal(HttpStatusCode.Redirect, r.StatusCode);
        Assert.Equal(EndpointsTarjetas.PaginaNoDisponible, r.Headers.Location?.OriginalString);
        Assert.Empty(await EscaneosDe(_activaDespublicada));
    }

    // ------------------------------------------------------------- cabeceras

    /// <summary>RN-S6-07 y RN-S6-05: ninguna cookie, nada en cache, sin referer hacia la ficha.</summary>
    [Fact]
    public async Task Ninguna_respuesta_lleva_cookies_ni_se_guarda_en_cache()
    {
        var segmentos = new[]
        {
            _activa.UrlEstable, _desactivada.UrlEstable, _reemplazada.UrlEstable,
            _emitida.UrlEstable, _activaDespublicada.UrlEstable, _inexistente, "no-es-una-tarjeta",
        };

        foreach (var s in segmentos)
        {
            var r = await Escanear(s);
            Assert.False(r.Headers.Contains("Set-Cookie"), $"{s} mando Set-Cookie");
            Assert.True(r.Headers.CacheControl?.NoStore, $"{s} sin no-store");
            Assert.Equal("no-referrer", Assert.Single(r.Headers.GetValues("Referrer-Policy")));
        }
    }

    // ------------------------------------------------------------- la tabla

    /// <summary>
    /// R-20: la privacidad del escaneo vive en la estructura. Si alguien agrega una columna
    /// (ip, agente, usuario), esto se pone rojo antes de que llegue a guardarse nada.
    /// </summary>
    [Fact]
    public async Task La_tabla_escaneos_no_tiene_columnas_de_identidad()
    {
        using var ctx = ContextoDePrueba.Crear();
        var columnas = await ctx.Database.SqlQueryRaw<string>(
                "SELECT column_name AS \"Value\" FROM information_schema.columns WHERE table_name = 'escaneos'")
            .ToListAsync();

        Assert.Equal(
            new[] { "derivo_en_contacto", "id", "medio", "ocurrido_en", "tarjeta_id" },
            columnas.Order().ToArray());
    }

    /// <summary>Todo lo que viaja en la respuesta, en un texto comparable.</summary>
    private static async Task<string> Huella(HttpResponseMessage r)
    {
        var cabeceras = r.Headers.Concat(r.Content.Headers)
            .Where(h => h.Key != "Date")
            .OrderBy(h => h.Key, StringComparer.OrdinalIgnoreCase)
            .Select(h => $"{h.Key}: {string.Join(",", h.Value)}");
        var cuerpo = Convert.ToBase64String(await r.Content.ReadAsByteArrayAsync());
        return $"{(int)r.StatusCode}\n{string.Join("\n", cabeceras)}\n{cuerpo}";
    }
}

/// <summary>
/// El limite P en su propia aplicacion, como <see cref="LimiteDeTasaPruebas"/>: gastarlo aqui
/// no le quita cupo a nadie.
/// </summary>
public class ApiTarjetasLimitePruebas
{
    [Fact]
    public async Task Pasado_el_limite_P_la_cuarta_peticion_recibe_429()
    {
        using var app = new Aplicacion(limitePublico: 3);
        using var cliente = app.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost"),
        });

        // Inexistente a proposito: no hace falta sembrar nada, y el 429 tiene que salir igual.
        var codigos = new List<HttpStatusCode>();
        for (var i = 0; i < 4; i++)
            codigos.Add((await cliente.GetAsync($"/t/{Tarjeta.GenerarUrlEstable()}")).StatusCode);

        Assert.Equal(
            new[] { HttpStatusCode.Redirect, HttpStatusCode.Redirect, HttpStatusCode.Redirect,
                    HttpStatusCode.TooManyRequests },
            codigos);
    }
}
