using Zass.Dominio.Comun;
using Zass.Dominio.Identidad;
using Zass.Dominio.Suscripciones;
using Zass.Infraestructura.Datos.Repositorios;
using Zass.Infraestructura.Seguridad;

namespace Zass.Integracion.Pruebas;

/// <summary>
/// El listado de slugs del sitemap contra PostgreSQL de verdad. Lo que hay que demostrar es que "visible"
/// significa lo mismo que en la busqueda: si el sitemap listara una ficha que la API responde 404, Google
/// la marcaria como error, y si callara una visible no se indexaria.
/// </summary>
public class MapaDelSitioIntegracionPruebas : IDisposable
{
    private readonly ContextoDePrueba _prueba = new();
    private readonly RepositorioMapaDelSitioEfCore _repo;
    private readonly RelojUtc _reloj = new();
    private readonly string _marca = $"mapa-{Guid.NewGuid():N}"[..14];

    public MapaDelSitioIntegracionPruebas() => _repo = new RepositorioMapaDelSitioEfCore(_prueba.Ctx);

    public void Dispose() => _prueba.Dispose();

    private async Task<string> UnoAsync(
        string sufijo,
        EstadoPerfil estado = EstadoPerfil.Publicado,
        EstadoSuscripcion suscripcion = EstadoSuscripcion.Prueba,
        int diasDeVigencia = 30)
    {
        var ctx = _prueba.Ctx;
        var usuario = new Usuario
        {
            Correo = $"{_marca}-{sufijo}@zass.local", HashContrasena = "x",
            Nombre = "Prueba", CorreoVerificado = true,
        };
        ctx.Usuarios.Add(usuario);
        var perfil = new PerfilPrestador
        {
            UsuarioId = usuario.Id, RubroId = 1, NombrePublico = "Prueba",
            Slug = $"{_marca}-{sufijo}", Estado = estado,
            PublicadoEn = estado == EstadoPerfil.Publicado ? _reloj.Ahora : null,
        };
        ctx.PerfilesPrestador.Add(perfil);
        ctx.Suscripciones.Add(new Suscripcion
        {
            PerfilPrestadorId = perfil.Id, PlanId = 1, Estado = suscripcion,
            IniciaEn = _reloj.Ahora.AddDays(-1), VigenteHasta = _reloj.Ahora.AddDays(diasDeVigencia),
        });
        await ctx.SaveChangesAsync();
        return perfil.Slug;
    }

    /// <summary>Recorre TODO el listado con paginas de 100 y se queda con lo que esta prueba creo.</summary>
    private async Task<List<string>> MisSlugsAsync()
    {
        var todos = new List<string>();
        string? cursor = null;
        while (true)
        {
            var filas = await _repo.VisiblesAsync(cursor, _reloj.Ahora, 100);
            todos.AddRange(filas.Select(f => f.Slug));
            if (filas.Count < 100) break;
            cursor = filas[^1].Slug;
        }
        return todos.Where(s => s.StartsWith(_marca)).ToList();
    }

    [Fact]
    public async Task Lista_solo_lo_que_la_busqueda_mostraria()
    {
        var visible1 = await UnoAsync("a");
        var activa = await UnoAsync("b", suscripcion: EstadoSuscripcion.Activa);
        await UnoAsync("c", diasDeVigencia: -3);                              // suscripcion vencida
        await UnoAsync("d", estado: EstadoPerfil.Borrador);                   // sin publicar
        await UnoAsync("e", estado: EstadoPerfil.Despublicado);               // despublicado
        await UnoAsync("f", suscripcion: EstadoSuscripcion.Cancelada);        // cancelada

        Assert.Equal(new[] { visible1, activa }, await MisSlugsAsync());
    }

    [Fact]
    public async Task El_cursor_pagina_sin_repetir_ni_saltarse_a_nadie()
    {
        var esperados = new List<string>();
        foreach (var s in new[] { "1", "2", "3", "4", "5" }) esperados.Add(await UnoAsync(s));

        // De una en una, partiendo justo antes de MI prefijo.
        var vistos = new List<string>();
        string? cursor = _marca[..^1];
        while (true)
        {
            var filas = await _repo.VisiblesAsync(cursor, _reloj.Ahora, 1);
            if (filas.Count == 0 || !filas[0].Slug.StartsWith(_marca)) break;
            vistos.Add(filas[0].Slug);
            cursor = filas[0].Slug;
        }

        Assert.Equal(esperados.OrderBy(s => s, StringComparer.Ordinal), vistos);
    }

    [Fact]
    public async Task Respeta_el_limite_pedido()
    {
        await UnoAsync("x"); await UnoAsync("y"); await UnoAsync("z");
        Assert.Equal(2, (await _repo.VisiblesAsync(null, _reloj.Ahora, 2)).Count);
    }
}
