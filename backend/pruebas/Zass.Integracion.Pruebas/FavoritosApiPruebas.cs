using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Zass.Dominio.Comun;
using Zass.Dominio.Identidad;
using Zass.Dominio.Suscripciones;

namespace Zass.Integracion.Pruebas;

/// <summary>
/// P-2 y P-3 por HTTP y contra la base real: los favoritos de CS-08 (API-23-favoritos, guardar y
/// quitar) y las preferencias de CU-04 (API-23 y API-23b).
///
/// Cada prueba mira lo que se veria si la regla se rompiera: que sin sesion no se lee nada, que
/// la lista no trae el contacto que la ficha esconde detras de la cuenta verificada, que un perfil
/// sin publicar no se puede guardar ni aparece, y que los favoritos de una cuenta no los ve ni los
/// toca otra.
/// </summary>
public partial class ApiPruebas
{
    private const string TelefonoDeFavorito = "+56 9 7777 1234";
    private const string WhatsappDeFavorito = "56977771234";
    private const string SitioDeFavorito = "https://favorito-de-prueba.test";

    /// <summary>
    /// Un prestador escrito directo en la base, CON todo el contacto publicado: si la lista de
    /// favoritos lo dejara pasar, aqui se veria. Su usuario se borra al final por el correo.
    /// </summary>
    private async Task<(Guid Id, string Slug)> UnPrestadorEnLaBaseAsync(
        EstadoPerfil estado, string nombre = "Favorito de prueba",
        int totalResenas = 0, decimal? promedio = null)
    {
        var correo = NuevoCorreo();
        var marca = Guid.NewGuid().ToString("N")[..12];
        using var ctx = ContextoDePrueba.Crear();
        var rubro = await ctx.Rubros.FirstAsync(r => r.Activo);

        var usuario = new Usuario
        {
            Correo = correo, HashContrasena = "x", Nombre = "Prestador", CorreoVerificado = true,
        };
        ctx.Usuarios.Add(usuario);
        var perfil = new PerfilPrestador
        {
            UsuarioId = usuario.Id, RubroId = rubro.Id, Slug = $"favorito-{marca}",
            NombrePublico = nombre, Estado = estado,
            PublicadoEn = estado == EstadoPerfil.Publicado ? DateTime.UtcNow.AddDays(-3) : null,
            TelefonoPublico = TelefonoDeFavorito, Whatsapp = WhatsappDeFavorito,
            CorreoPublico = $"publico-{marca}@zass.local", SitioWeb = SitioDeFavorito,
            TotalResenas = totalResenas, PromedioCalificacion = promedio,
        };
        ctx.PerfilesPrestador.Add(perfil);
        await ctx.SaveChangesAsync();

        // Al dia con su suscripcion: asi su FICHA si mostraria el contacto a quien tiene la cuenta
        // verificada, y «la lista no lo trae» prueba algo. Sin suscripcion la ficha lo vacia sola
        // (RN-S6-10), y la prueba del contacto saldria verde aunque la lista usara la ficha entera.
        if (estado == EstadoPerfil.Publicado)
        {
            var plan = await ctx.Planes.FirstAsync(p => p.Codigo == "prueba");
            ctx.Suscripciones.Add(new Suscripcion
            {
                PerfilPrestadorId = perfil.Id, PlanId = plan.Id, Estado = EstadoSuscripcion.Activa,
                IniciaEn = DateTime.UtcNow.AddDays(-1), VigenteHasta = DateTime.UtcNow.AddDays(29),
            });
            await ctx.SaveChangesAsync();
        }
        return (perfil.Id, perfil.Slug);
    }

    private async Task<HttpResponseMessage> GuardarFavoritoAsync(Guid perfilId, bool conCsrf = true)
    {
        var peticion = new HttpRequestMessage(HttpMethod.Put, $"/api/mi/favoritos/{perfilId}");
        if (conCsrf) peticion.Headers.Add("X-CSRF-Token", await TokenCsrfAsync());
        return await _cliente.SendAsync(peticion);
    }

    private async Task<HttpResponseMessage> QuitarFavoritoAsync(Guid perfilId)
    {
        var peticion = new HttpRequestMessage(HttpMethod.Delete, $"/api/mi/favoritos/{perfilId}");
        peticion.Headers.Add("X-CSRF-Token", await TokenCsrfAsync());
        return await _cliente.SendAsync(peticion);
    }

    private async Task<(string Texto, List<JsonElement> Lista)> MisFavoritosAsync()
    {
        var r = await _cliente.GetAsync("/api/mi/favoritos");
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var texto = await r.Content.ReadAsStringAsync();
        return (texto, JsonDocument.Parse(texto).RootElement.EnumerateArray().ToList());
    }

    private async Task<JsonElement> MiPerfilBuscadorAsync()
    {
        var r = await _cliente.GetAsync("/api/mi/perfil-buscador");
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        return (await r.Content.ReadFromJsonAsync<JsonDocument>())!.RootElement;
    }

    private async Task<HttpResponseMessage> GuardarPreferenciasAsync(object cuerpo)
    {
        var peticion = new HttpRequestMessage(HttpMethod.Put, "/api/mi/perfil-buscador")
        {
            Content = JsonContent.Create(cuerpo),
        };
        peticion.Headers.Add("X-CSRF-Token", await TokenCsrfAsync());
        return await _cliente.SendAsync(peticion);
    }

    private async Task UnBuscadorConSesionAsync(string rol = "buscador")
    {
        var correo = NuevoCorreo();
        await RegistrarAsync(correo, rol);
        await VerificarAsync(correo);
        await IniciarSesionAsync(correo);
    }

    /// <summary>Corre <paramref name="accion"/> con OTRA persona: otro cliente, otra jarra de cookies.</summary>
    private async Task ComoOtraPersonaAsync(Func<Task> accion)
    {
        var primero = _cliente;
        using var otro = _app.CreateClient(new WebApplicationFactoryClientOptions
        {
            HandleCookies = true, AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost"),
        });
        _cliente = otro;
        try { await accion(); }
        finally { _cliente = primero; }
    }

    private static List<Guid> Ids(IEnumerable<JsonElement> lista) =>
        lista.Select(f => f.GetProperty("perfilId").GetGuid()).ToList();

    // ---------------------------------------------------------------- sin sesion

    [Fact]
    public async Task Sin_sesion_favoritos_y_preferencias_responden_401()
    {
        var alguien = Guid.NewGuid();

        Assert.Equal(HttpStatusCode.Unauthorized, (await _cliente.GetAsync("/api/mi/favoritos")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _cliente.GetAsync("/api/mi/perfil-buscador")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await GuardarFavoritoAsync(alguien)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await QuitarFavoritoAsync(alguien)).StatusCode);
    }

    [Fact]
    public async Task Guardar_un_favorito_sin_token_CSRF_se_rechaza_con_403()
    {
        var (id, _) = await UnPrestadorEnLaBaseAsync(EstadoPerfil.Publicado);
        await UnBuscadorConSesionAsync();

        Assert.Equal(HttpStatusCode.Forbidden, (await GuardarFavoritoAsync(id, conCsrf: false)).StatusCode);
        Assert.Empty((await MisFavoritosAsync()).Lista);
    }

    // ---------------------------------------------------------------- la lista

    [Fact]
    public async Task La_lista_de_favoritos_trae_la_tarjeta_publica_y_nada_de_contacto()
    {
        var (id, slug) = await UnPrestadorEnLaBaseAsync(EstadoPerfil.Publicado, "Gasfiter de confianza");
        await UnBuscadorConSesionAsync();

        Assert.Equal(HttpStatusCode.NoContent, (await GuardarFavoritoAsync(id)).StatusCode);

        var (texto, lista) = await MisFavoritosAsync();
        var tarjeta = Assert.Single(lista);
        Assert.Equal(id, tarjeta.GetProperty("perfilId").GetGuid());
        Assert.Equal(slug, tarjeta.GetProperty("slug").GetString());
        Assert.Equal("Gasfiter de confianza", tarjeta.GetProperty("nombrePublico").GetString());

        // El contacto vive solo en la ficha, detras de la cuenta verificada (RN-S1-09). Se mira el
        // TEXTO de la respuesta, no solo las propiedades conocidas: un campo nuevo tambien se veria.
        Assert.DoesNotContain("7777", texto);
        Assert.DoesNotContain(WhatsappDeFavorito, texto);
        Assert.DoesNotContain("publico-", texto);
        Assert.DoesNotContain(SitioDeFavorito, texto);
        Assert.DoesNotContain("contacto", texto, StringComparison.OrdinalIgnoreCase);

        // Y API-23b trae el identificador, que es lo que pinta el boton en la busqueda.
        var mio = await MiPerfilBuscadorAsync();
        Assert.Equal(new[] { id }, mio.GetProperty("favoritos").EnumerateArray().Select(e => e.GetGuid()));
    }

    [Fact]
    public async Task Los_favoritos_van_del_mas_reciente_al_mas_antiguo_y_el_promedio_respeta_las_3_resenas()
    {
        var (pocas, _) = await UnPrestadorEnLaBaseAsync(EstadoPerfil.Publicado, "Dos resenas", 2, 5.0m);
        var (varias, _) = await UnPrestadorEnLaBaseAsync(EstadoPerfil.Publicado, "Cinco resenas", 5, 4.4m);
        await UnBuscadorConSesionAsync();

        await GuardarFavoritoAsync(pocas);
        await GuardarFavoritoAsync(varias);
        // Guardar otra vez el mismo no lo duplica ni lo mueve.
        Assert.Equal(HttpStatusCode.NoContent, (await GuardarFavoritoAsync(pocas)).StatusCode);

        var (_, lista) = await MisFavoritosAsync();
        Assert.Equal(new[] { varias, pocas }, Ids(lista));

        // RN-S5-11: con menos de 3 resenas el promedio no es publico, tampoco aqui.
        Assert.Equal(JsonValueKind.Null, lista[1].GetProperty("promedioCalificacion").ValueKind);
        Assert.Equal(2, lista[1].GetProperty("totalResenas").GetInt32());
        Assert.Equal(4.4m, lista[0].GetProperty("promedioCalificacion").GetDecimal());
    }

    // ---------------------------------------------------------------- lo que no existe

    [Fact]
    public async Task Un_perfil_inexistente_o_sin_publicar_no_se_guarda_y_responde_el_mismo_404()
    {
        var (borrador, _) = await UnPrestadorEnLaBaseAsync(EstadoPerfil.Borrador);
        await UnBuscadorConSesionAsync();

        var sinPublicar = await GuardarFavoritoAsync(borrador);
        var inexistente = await GuardarFavoritoAsync(Guid.NewGuid());

        Assert.Equal(HttpStatusCode.NotFound, sinPublicar.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, inexistente.StatusCode);
        // El mismo cuerpo: si no, esto serviria para sondear quien se dio de alta sin publicar.
        Assert.Equal(await CuerpoComparable.De(inexistente), await CuerpoComparable.De(sinPublicar));

        Assert.Empty((await MiPerfilBuscadorAsync()).GetProperty("favoritos").EnumerateArray());
    }

    [Fact]
    public async Task Un_favorito_que_se_despublica_desaparece_de_la_lista_y_se_puede_quitar()
    {
        var (id, _) = await UnPrestadorEnLaBaseAsync(EstadoPerfil.Publicado);
        await UnBuscadorConSesionAsync();
        await GuardarFavoritoAsync(id);

        using (var ctx = ContextoDePrueba.Crear())
        {
            var perfil = await ctx.PerfilesPrestador.SingleAsync(p => p.Id == id);
            perfil.Estado = EstadoPerfil.Despublicado;
            await ctx.SaveChangesAsync();
        }

        Assert.Empty((await MisFavoritosAsync()).Lista);

        Assert.Equal(HttpStatusCode.NoContent, (await QuitarFavoritoAsync(id)).StatusCode);
        Assert.Empty((await MiPerfilBuscadorAsync()).GetProperty("favoritos").EnumerateArray());
    }

    [Fact]
    public async Task Los_favoritos_no_tienen_tope_con_150_guardados_entra_otro()
    {
        var (id, _) = await UnPrestadorEnLaBaseAsync(EstadoPerfil.Publicado);
        var correo = NuevoCorreo();
        await RegistrarAsync(correo); await VerificarAsync(correo); await IniciarSesionAsync(correo);

        using (var ctx = ContextoDePrueba.Crear())
        {
            var perfil = await ctx.PerfilesBuscador
                .SingleAsync(p => ctx.Usuarios.Any(u => u.Id == p.UsuarioId && u.Correo == correo));
            perfil.Favoritos = Enumerable.Range(0, 150).Select(_ => Guid.NewGuid()).ToList();
            await ctx.SaveChangesAsync();
        }

        var r = await GuardarFavoritoAsync(id);

        // Sin tope (decision de Pablo, 22-nov): el 151 entra y queda primero.
        Assert.Equal(HttpStatusCode.NoContent, r.StatusCode);
        var favoritos = (await MiPerfilBuscadorAsync()).GetProperty("favoritos")
            .EnumerateArray().Select(e => e.GetGuid()).ToList();
        Assert.Equal(151, favoritos.Count);
        Assert.Equal(id, favoritos[0]);
    }

    // ---------------------------------------------------------------- de quien son

    [Fact]
    public async Task Los_favoritos_de_una_cuenta_no_los_ve_ni_los_quita_otra()
    {
        var (id, _) = await UnPrestadorEnLaBaseAsync(EstadoPerfil.Publicado);
        await UnBuscadorConSesionAsync();
        await GuardarFavoritoAsync(id);

        await ComoOtraPersonaAsync(async () =>
        {
            await UnBuscadorConSesionAsync();
            Assert.Empty((await MisFavoritosAsync()).Lista);
            Assert.Empty((await MiPerfilBuscadorAsync()).GetProperty("favoritos").EnumerateArray());

            // Quitar uno que no es tuyo no es un error (no estaba en TU lista), y no toca la ajena.
            Assert.Equal(HttpStatusCode.NoContent, (await QuitarFavoritoAsync(id)).StatusCode);
        });

        Assert.Equal(new[] { id }, Ids((await MisFavoritosAsync()).Lista));
    }

    [Fact]
    public async Task Un_prestador_tambien_puede_guardar_favoritos()
    {
        // El registro solo crea el perfil de buscador a quien entra como buscador; una misma cuenta
        // puede ser las dos cosas, y el prestador que guarda a un colega no deberia ver un 404.
        var (id, _) = await UnPrestadorEnLaBaseAsync(EstadoPerfil.Publicado);
        await UnBuscadorConSesionAsync(rol: "prestador");

        Assert.Equal(HttpStatusCode.NoContent, (await GuardarFavoritoAsync(id)).StatusCode);
        Assert.Equal(new[] { id }, Ids((await MisFavoritosAsync()).Lista));
    }

    // ---------------------------------------------------------------- preferencias (CU-04)

    [Fact]
    public async Task Las_preferencias_se_guardan_se_leen_y_los_oficios_no_se_borran_si_no_se_mandan()
    {
        int comuna;
        short[] rubros;
        using (var ctx = ContextoDePrueba.Crear())
        {
            comuna = await ctx.Zonas.Where(z => z.Tipo == Zass.Dominio.Catalogo.TipoDeZona.Comuna)
                .OrderBy(z => z.Id).Select(z => z.Id).FirstAsync();
            rubros = await ctx.Rubros.Where(r => r.Activo).OrderBy(r => r.Id).Select(r => r.Id).Take(2).ToArrayAsync();
        }
        await UnBuscadorConSesionAsync();

        var antes = await MiPerfilBuscadorAsync();
        Assert.Equal(JsonValueKind.Null, antes.GetProperty("zonaInteresId").ValueKind);
        Assert.Empty(antes.GetProperty("rubrosInteres").EnumerateArray());

        var r = await GuardarPreferenciasAsync(new
        {
            zonaInteresId = comuna, radioPreferidoKm = 20, aceptaAvisos = true,
            canalCorreo = true, canalPush = false, rubrosInteres = rubros,
        });
        Assert.Equal(HttpStatusCode.NoContent, r.StatusCode);

        var mio = await MiPerfilBuscadorAsync();
        Assert.Equal(comuna, mio.GetProperty("zonaInteresId").GetInt32());
        Assert.Equal(20, mio.GetProperty("radioPreferidoKm").GetInt32());
        Assert.Equal(rubros, mio.GetProperty("rubrosInteres").EnumerateArray().Select(e => e.GetInt16()));

        // Un cliente viejo, que no conoce los oficios, no los borra sin querer.
        await GuardarPreferenciasAsync(new
        {
            zonaInteresId = comuna, radioPreferidoKm = 10, aceptaAvisos = true,
            canalCorreo = true, canalPush = false,
        });
        Assert.Equal(rubros, (await MiPerfilBuscadorAsync()).GetProperty("rubrosInteres")
            .EnumerateArray().Select(e => e.GetInt16()));
    }

    [Fact]
    public async Task Una_comuna_o_un_oficio_que_no_existe_se_rechaza_con_400_y_no_se_guarda()
    {
        short rubro;
        using (var ctx = ContextoDePrueba.Crear())
            rubro = await ctx.Rubros.Where(r => r.Activo).Select(r => r.Id).FirstAsync();
        await UnBuscadorConSesionAsync();

        var zonaInventada = await GuardarPreferenciasAsync(new
        {
            zonaInteresId = 987654, radioPreferidoKm = 10, aceptaAvisos = true,
            canalCorreo = true, canalPush = false,
        });
        var oficioInventado = await GuardarPreferenciasAsync(new
        {
            zonaInteresId = (int?)null, radioPreferidoKm = 10, aceptaAvisos = true,
            canalCorreo = true, canalPush = false, rubrosInteres = new short[] { rubro, 32000 },
        });
        var demasiados = await GuardarPreferenciasAsync(new
        {
            zonaInteresId = (int?)null, radioPreferidoKm = 10, aceptaAvisos = true,
            canalCorreo = true, canalPush = false,
            rubrosInteres = new short[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11 },
        });
        // Que el rechazo sea POR EL TOPE y no porque entre esos once hay oficios que no existen:
        // sin esto la prueba pasaria igual aunque el tope desapareciera.
        var textoDemasiados = await demasiados.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.BadRequest, zonaInventada.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, oficioInventado.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, demasiados.StatusCode);
        Assert.Contains("hasta 10 oficios", textoDemasiados);

        var mio = await MiPerfilBuscadorAsync();
        Assert.Equal(JsonValueKind.Null, mio.GetProperty("zonaInteresId").ValueKind);
        Assert.Empty(mio.GetProperty("rubrosInteres").EnumerateArray());
    }
}
