using Zass.Aplicacion.Identidad;
using Zass.Aplicacion.Pruebas.Dobles;
using Zass.Dominio.Catalogo;
using Zass.Dominio.Comun;

namespace Zass.Aplicacion.Pruebas.Identidad;

/// <summary>
/// La interfaz publica de MOD-01: lo unico que los otros modulos pueden pedirle.
/// Si alguna de estas pruebas se cae, se cae media plataforma: 6 de los 12 modulos
/// dependen de este.
/// </summary>
public class ServicioIdentidadPruebas
{
    private readonly BaseEnMemoria _base = new();
    private readonly RelojFijo _reloj = new();
    private readonly HashDeJuguete _hash = new();
    private readonly SecretosDeterministas _secretos = new();
    private readonly ColaEnMemoria _cola = new();
    private readonly AuditoriaEnMemoria _auditoria = new();
    private readonly ServicioRegistro _registro;
    private readonly IServicioIdentidad _identidad;

    public ServicioIdentidadPruebas()
    {
        var usuarios = new RepositorioUsuariosEnMemoria(_base);
        var perfiles = new RepositorioPerfilesEnMemoria(_base);

        _registro = new ServicioRegistro(
            usuarios, perfiles, _base, _hash, _secretos, _reloj, _cola, _auditoria);
        _identidad = new ServicioIdentidad(usuarios, perfiles);

        _base.Zonas.Add(new Zona { Id = 7, Nombre = "Amanecer", Sector = "Norte" });
    }

    private async Task<Guid> RegistrarAsync(string correo, string rol) =>
        (await _registro.RegistrarAsync(
            new RegistrarCuentaCmd(correo, "contrasena-larga-1", "Pablo Espinoza", rol))).Valor!.UsuarioId;

    [Fact]
    public async Task ObtenerUsuario_devuelve_el_usuario_o_nulo_si_no_existe()
    {
        var id = await RegistrarAsync("pablo@correo.cl", "buscador");

        Assert.Equal("pablo@correo.cl", (await _identidad.ObtenerUsuarioAsync(id))!.Correo);
        Assert.Null(await _identidad.ObtenerUsuarioAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task TieneRol_responde_por_el_codigo_del_rol()
    {
        var id = await RegistrarAsync("pablo@correo.cl", "prestador");

        Assert.True(await _identidad.TieneRolAsync(id, "prestador"));
        Assert.False(await _identidad.TieneRolAsync(id, "administrador"));
        Assert.False(await _identidad.TieneRolAsync(Guid.NewGuid(), "prestador"));
    }

    [Fact]
    public async Task PerfilPublicado_solo_es_verdadero_cuando_el_perfil_esta_publicado()
    {
        await RegistrarAsync("pablo@correo.cl", "prestador");
        var perfil = _base.PerfilesPrestador.Single();

        Assert.False(await _identidad.PerfilPublicadoAsync(perfil.Id));

        perfil.Estado = EstadoPerfil.Publicado;
        Assert.True(await _identidad.PerfilPublicadoAsync(perfil.Id));

        perfil.Estado = EstadoPerfil.Despublicado;
        Assert.False(await _identidad.PerfilPublicadoAsync(perfil.Id));
    }

    [Fact]
    public async Task EsMismaPersona_distingue_dos_cuentas_distintas_de_la_misma_cuenta()
    {
        var pablo = await RegistrarAsync("pablo@correo.cl", "buscador");
        var ana = await RegistrarAsync("ana@correo.cl", "prestador");

        Assert.True(await _identidad.EsMismaPersonaAsync(pablo, pablo));
        Assert.False(await _identidad.EsMismaPersonaAsync(pablo, ana));
    }

    [Fact]
    public async Task La_ficha_publica_no_lleva_coordenadas_ni_el_correo_de_la_cuenta()
    {
        // RN-S3-02 y el aviso del grupo P de api-rest.md: este es el DTO que mas expone.
        await RegistrarAsync("pablo@correo.cl", "prestador");
        var perfil = _base.PerfilesPrestador.Single();
        perfil.Estado = EstadoPerfil.Publicado;
        perfil.Latitud = -38.74;
        perfil.Longitud = -72.60;
        perfil.DireccionCifrada = new byte[] { 1, 2, 3 };
        perfil.CorreoPublico = "contacto@estudio.cl";
        perfil.Cobertura.Add(new CoberturaPrestador
        {
            PerfilPrestadorId = perfil.Id, ZonaId = 7, EsPrincipal = true,
            Zona = _base.Zonas.Single(z => z.Id == 7),
        });

        var ficha = await _identidad.PerfilPublicoAsync(perfil.Id);

        Assert.NotNull(ficha);
        var propiedades = ficha!.GetType().GetProperties().Select(p => p.Name).ToList();
        Assert.DoesNotContain("Latitud", propiedades);
        Assert.DoesNotContain("Longitud", propiedades);
        Assert.DoesNotContain("Direccion", propiedades);
        Assert.DoesNotContain("DireccionCifrada", propiedades);
        Assert.Equal("Amanecer", ficha.ZonaPrincipal);
        Assert.Equal("contacto@estudio.cl", ficha.CorreoPublico);
        Assert.Equal("pablo-espinoza", ficha.Slug);
    }

    [Fact]
    public async Task La_ficha_publica_de_un_perfil_sin_publicar_no_existe_para_nadie()
    {
        await RegistrarAsync("pablo@correo.cl", "prestador");
        var perfil = _base.PerfilesPrestador.Single();

        Assert.Null(await _identidad.PerfilPublicoAsync(perfil.Id));
        Assert.Null(await _identidad.PerfilPublicoPorSlugAsync(perfil.Slug));
    }

    [Fact]
    public async Task Con_menos_de_tres_resenas_la_ficha_muestra_el_recuento_y_no_el_promedio()
    {
        // RN-S5-11. Un solo 5 de un amigo no puede verse igual que 40 calificaciones.
        await RegistrarAsync("pablo@correo.cl", "prestador");
        var perfil = _base.PerfilesPrestador.Single();
        perfil.Estado = EstadoPerfil.Publicado;
        perfil.TotalResenas = 2;
        perfil.PromedioCalificacion = 5.0m;

        var ficha = await _identidad.PerfilPublicoPorSlugAsync(perfil.Slug);

        Assert.Equal(2, ficha!.TotalResenas);
        Assert.Null(ficha.PromedioCalificacion);

        perfil.TotalResenas = 3;
        var conTres = await _identidad.PerfilPublicoPorSlugAsync(perfil.Slug);
        Assert.Equal(5.0m, conTres!.PromedioCalificacion);
    }

    // Lo pide MOD-13: la suscripcion cuelga del PERFIL, no del usuario, y traducir de
    // uno a otro es trabajo de MOD-01. Si no lo expusiera aqui, el modulo de
    // suscripciones acabaria leyendo la tabla de perfiles, que no es suya.
    [Fact]
    public async Task Da_el_id_del_perfil_de_prestador_de_una_cuenta()
    {
        await RegistrarAsync("pablo@correo.cl", "prestador");
        var perfil = _base.PerfilesPrestador.Single();

        var id = await _identidad.PerfilPrestadorIdAsync(perfil.UsuarioId);

        Assert.Equal(perfil.Id, id);
    }

    [Fact]
    public async Task Una_cuenta_de_buscador_no_tiene_perfil_de_prestador()
    {
        await RegistrarAsync("ana@correo.cl", "buscador");
        var usuarioId = _base.Usuarios.Single().Id;

        Assert.Null(await _identidad.PerfilPrestadorIdAsync(usuarioId));
    }
}
