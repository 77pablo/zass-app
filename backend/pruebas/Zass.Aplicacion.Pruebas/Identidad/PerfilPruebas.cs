using Zass.Aplicacion.Identidad;
using Zass.Aplicacion.Pruebas.Dobles;
using Zass.Dominio.Catalogo;
using Zass.Dominio.Comun;
using Zass.Dominio.Identidad;

namespace Zass.Aplicacion.Pruebas.Identidad;

/// <summary>
/// CU-03 publicar perfil de prestador y CU-04 preferencias del buscador.
/// Reglas RN-S1-02, RN-S1-05, RN-S1-06, RN-S3-02 y la version optimista CC-02.
/// </summary>
public class PerfilPruebas
{
    private readonly BaseEnMemoria _base = new();
    private readonly RelojFijo _reloj = new();
    private readonly HashDeJuguete _hash = new();
    private readonly SecretosDeterministas _secretos = new();
    private readonly ColaEnMemoria _cola = new();
    private readonly AuditoriaEnMemoria _auditoria = new();
    private readonly CifradoDeJuguete _cifrado = new();
    private readonly ServicioRegistro _registro;
    private readonly ServicioPerfiles _perfiles;

    private const string Clave = "contrasena-larga-1";

    public PerfilPruebas()
    {
        var usuarios = new RepositorioUsuariosEnMemoria(_base);
        var repoPerfiles = new RepositorioPerfilesEnMemoria(_base);

        _registro = new ServicioRegistro(
            usuarios, repoPerfiles, _base, _hash, _secretos, _reloj, _cola, _auditoria);
        _perfiles = new ServicioPerfiles(
            usuarios, repoPerfiles, _base, _reloj, _cifrado, _cola, _auditoria);

        _base.Rubros.Add(new Rubro { Id = 1, Codigo = "legal", Nombre = "Legal" });
        _base.Zonas.Add(new Zona { Id = 7, Nombre = "Amanecer", Sector = "Norte" });
    }

    /// <summary>Un prestador con el correo ya verificado, que es la precondicion P2 de CU-03.</summary>
    private async Task<(Guid UsuarioId, PerfilPrestador Perfil)> PrestadorAsync(
        bool correoVerificado = true)
    {
        var r = await _registro.RegistrarAsync(
            new RegistrarCuentaCmd("pablo@correo.cl", Clave, "Pablo Espinoza", "prestador"));
        Assert.True(r.Exito, r.Mensaje);

        var usuario = _base.Usuarios.Single();
        if (correoVerificado) usuario.VerificarCorreo();

        return (usuario.Id, _base.PerfilesPrestador.Single());
    }

    /// <summary>
    /// Lo que MOD-03 y MOD-04 pondran cuando existan: un servicio activo y una zona
    /// principal. Son el minimo publicable de RN-S1-05, junto con nombre y rubro.
    /// </summary>
    private void CompletarMinimoPublicable(PerfilPrestador perfil)
    {
        perfil.RubroId = 1;
        perfil.Servicios.Add(new PrestadorServicio
        {
            PerfilPrestadorId = perfil.Id, ServicioId = 1, Activo = true,
        });
        perfil.Cobertura.Add(new CoberturaPrestador
        {
            PerfilPrestadorId = perfil.Id, ZonaId = 7, EsPrincipal = true,
            Zona = _base.Zonas.Single(z => z.Id == 7),
        });
    }

    // ----------------------------------------------------- CU-03 - editar

    [Fact]
    public async Task Editar_guarda_el_nombre_la_descripcion_y_los_canales_publicos()
    {
        var (usuarioId, perfil) = await PrestadorAsync();

        var r = await _perfiles.EditarPrestadorAsync(usuarioId, new EditarPerfilPrestadorCmd(
            NombrePublico: "Estudio Espinoza",
            Descripcion: "Derecho de familia en Temuco",
            RubroId: 1,
            TelefonoPublico: "+56912345678",
            CorreoPublico: "contacto@estudio.cl",
            Whatsapp: null,
            SitioWeb: null,
            Direccion: null,
            Latitud: null,
            Longitud: null,
            Version: perfil.Version));

        Assert.True(r.Exito, r.Mensaje);
        Assert.Equal("Estudio Espinoza", perfil.NombrePublico);
        Assert.Equal("Derecho de familia en Temuco", perfil.Descripcion);
        Assert.Equal("+56912345678", perfil.TelefonoPublico);
        Assert.Equal((short)1, perfil.RubroId);
    }

    [Fact]
    public async Task Editar_sube_la_version_del_perfil()
    {
        var (usuarioId, perfil) = await PrestadorAsync();
        var antes = perfil.Version;

        await _perfiles.EditarPrestadorAsync(usuarioId, Edicion(perfil.Version));

        Assert.Equal(antes + 1, perfil.Version);
    }

    [Fact]
    public async Task Dos_aparatos_editando_a_la_vez_no_se_pisan_en_silencio()
    {
        // CC-02. El segundo trae la version que leyo antes del primer guardado.
        var (usuarioId, perfil) = await PrestadorAsync();
        var versionQueLeyeronLosDos = perfil.Version;

        var primero = await _perfiles.EditarPrestadorAsync(
            usuarioId, Edicion(versionQueLeyeronLosDos, nombre: "Del movil"));
        var segundo = await _perfiles.EditarPrestadorAsync(
            usuarioId, Edicion(versionQueLeyeronLosDos, nombre: "Del computador"));

        Assert.True(primero.Exito, primero.Mensaje);
        Assert.False(segundo.Exito);
        Assert.Equal(Fallas.VersionDesactualizada, segundo.Codigo);
        Assert.Equal("Del movil", perfil.NombrePublico);
    }

    [Fact]
    public async Task La_direccion_se_guarda_cifrada_y_nunca_en_claro()
    {
        var (usuarioId, perfil) = await PrestadorAsync();

        await _perfiles.EditarPrestadorAsync(usuarioId, Edicion(
            perfil.Version, direccion: "Avenida Alemania 0231", latitud: -38.74, longitud: -72.60));

        Assert.NotNull(perfil.DireccionCifrada);
        var comoTexto = System.Text.Encoding.UTF8.GetString(perfil.DireccionCifrada!);
        Assert.DoesNotContain("Alemania", comoTexto);
        Assert.Equal("Avenida Alemania 0231", _cifrado.Descifrar(perfil.DireccionCifrada!));
        Assert.Equal(-38.74, perfil.Latitud);
    }

    [Fact]
    public async Task El_perfil_propio_trae_la_zona_pero_nunca_las_coordenadas()
    {
        // RN-S3-02. Ni siquiera en /mi/perfil-prestador, que es el endpoint del dueno:
        // si el DTO no las tiene, ninguna vista puede filtrarlas por descuido.
        var (usuarioId, perfil) = await PrestadorAsync();
        CompletarMinimoPublicable(perfil);
        await _perfiles.EditarPrestadorAsync(usuarioId, Edicion(
            perfil.Version, direccion: "Avenida Alemania 0231", latitud: -38.74, longitud: -72.60));

        var r = await _perfiles.MiPerfilPrestadorAsync(usuarioId);

        Assert.True(r.Exito, r.Mensaje);
        var propiedades = r.Valor!.GetType().GetProperties().Select(p => p.Name).ToList();
        Assert.DoesNotContain("Latitud", propiedades);
        Assert.DoesNotContain("Longitud", propiedades);
        Assert.DoesNotContain("DireccionCifrada", propiedades);
        Assert.Equal("Amanecer", r.Valor!.ZonaPrincipal);
        Assert.True(r.Valor!.TieneDireccion);
    }

    [Fact]
    public async Task Nadie_puede_editar_el_perfil_de_otro()
    {
        var (_, perfil) = await PrestadorAsync();

        var r = await _perfiles.EditarPrestadorAsync(Guid.NewGuid(), Edicion(perfil.Version));

        Assert.False(r.Exito);
        Assert.Equal(Fallas.NoEncontrado, r.Codigo);
    }

    // --------------------------------------------------- CU-03 - publicar

    [Fact]
    public async Task Publicar_sin_rubro_servicio_ni_zona_falla_y_dice_que_falta()
    {
        var (usuarioId, _) = await PrestadorAsync();

        var r = await _perfiles.PublicarAsync(usuarioId);

        Assert.False(r.Exito);
        Assert.Equal(Fallas.ReglaDeNegocio, r.Codigo);
        Assert.Contains("rubro", r.Mensaje!);
        Assert.Equal(EstadoPerfil.Borrador, _base.PerfilesPrestador.Single().Estado);
    }

    // Lo necesita quien compone: al publicar hay que arrancar la suscripcion de prueba
    // (MOD-13), y para eso hace falta saber de que perfil se trata. Sin esto, el
    // endpoint tendria que volver a buscar el perfil que este metodo ya tiene en la mano.
    [Fact]
    public async Task Publicar_devuelve_el_id_del_perfil_publicado()
    {
        var (usuarioId, perfil) = await PrestadorAsync();
        CompletarMinimoPublicable(perfil);

        var r = await _perfiles.PublicarAsync(usuarioId);

        Assert.True(r.Exito, r.Mensaje);
        Assert.Equal(perfil.Id, r.Valor);
    }

    [Fact]
    public async Task Publicar_con_el_minimo_publicable_deja_el_perfil_visible()
    {
        var (usuarioId, perfil) = await PrestadorAsync();
        CompletarMinimoPublicable(perfil);

        var r = await _perfiles.PublicarAsync(usuarioId);

        Assert.True(r.Exito, r.Mensaje);
        Assert.Equal(EstadoPerfil.Publicado, perfil.Estado);
        Assert.NotNull(perfil.PublicadoEn);
    }

    [Fact]
    public async Task Publicar_sin_el_correo_verificado_no_se_permite()
    {
        // RN-S1-02 y precondicion P2 de CU-03.
        var (usuarioId, perfil) = await PrestadorAsync(correoVerificado: false);
        CompletarMinimoPublicable(perfil);

        var r = await _perfiles.PublicarAsync(usuarioId);

        Assert.False(r.Exito);
        Assert.Equal(Fallas.ReglaDeNegocio, r.Codigo);
        Assert.Contains("correo", r.Mensaje!);
        Assert.Equal(EstadoPerfil.Borrador, perfil.Estado);
    }

    [Fact]
    public async Task Publicar_avisa_que_el_perfil_ya_aparece_en_las_busquedas()
    {
        var (usuarioId, perfil) = await PrestadorAsync();
        CompletarMinimoPublicable(perfil);
        _cola.Encoladas.Clear();

        await _perfiles.PublicarAsync(usuarioId);

        var aviso = Assert.Single(_cola.Encoladas);
        Assert.Equal(usuarioId, aviso.DestinatarioId);
        Assert.Equal(TipoNotificacion.PerfilPublicado, aviso.Tipo);
        Assert.Contains("búsquedas", aviso.Cuerpo);
    }

    [Fact]
    public async Task Despublicar_saca_el_perfil_de_la_busqueda_sin_borrar_nada()
    {
        var (usuarioId, perfil) = await PrestadorAsync();
        CompletarMinimoPublicable(perfil);
        await _perfiles.PublicarAsync(usuarioId);

        var r = await _perfiles.DespublicarAsync(usuarioId);

        Assert.True(r.Exito, r.Mensaje);
        Assert.Equal(EstadoPerfil.Despublicado, perfil.Estado);
        Assert.NotNull(perfil.PublicadoEn);
        Assert.Single(_base.PerfilesPrestador);
    }

    [Fact]
    public async Task Volver_a_publicar_conserva_la_fecha_de_la_primera_publicacion()
    {
        var (usuarioId, perfil) = await PrestadorAsync();
        CompletarMinimoPublicable(perfil);
        await _perfiles.PublicarAsync(usuarioId);
        var primeraVez = perfil.PublicadoEn;

        await _perfiles.DespublicarAsync(usuarioId);
        _reloj.Avanzar(TimeSpan.FromDays(10));
        await _perfiles.PublicarAsync(usuarioId);

        Assert.Equal(primeraVez, perfil.PublicadoEn);
    }

    // ------------------------------------------------------------- CU-04

    [Fact]
    public async Task El_buscador_ajusta_zona_radio_y_canales()
    {
        var r = await _registro.RegistrarAsync(
            new RegistrarCuentaCmd("ana@correo.cl", Clave, "Ana", "buscador"));
        var usuarioId = r.Valor!.UsuarioId;

        var cambio = await _perfiles.EditarBuscadorAsync(usuarioId,
            new EditarPerfilBuscadorCmd(ZonaInteresId: 7, RadioPreferidoKm: 25,
                AceptaAvisos: false, CanalCorreo: true, CanalPush: true));

        Assert.True(cambio.Exito, cambio.Mensaje);
        var perfil = _base.PerfilesBuscador.Single();
        Assert.Equal(7, perfil.ZonaInteresId);
        Assert.Equal((short)25, perfil.RadioPreferidoKm);
        Assert.False(perfil.AceptaAvisos);
        Assert.True(perfil.CanalPush);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(51)]
    public async Task El_radio_del_buscador_vive_entre_1_y_50_kilometros(int radio)
    {
        // Invariante de CL-04. Fuera de ese rango el resultado no tiene sentido:
        // 0 km no devuelve a nadie y 200 km deja de ser "cerca".
        var r = await _registro.RegistrarAsync(
            new RegistrarCuentaCmd("ana@correo.cl", Clave, "Ana", "buscador"));

        var cambio = await _perfiles.EditarBuscadorAsync(r.Valor!.UsuarioId,
            new EditarPerfilBuscadorCmd(null, (short)radio, true, true, false));

        Assert.False(cambio.Exito);
        Assert.Equal(Fallas.Validacion, cambio.Codigo);
        Assert.Equal((short)10, _base.PerfilesBuscador.Single().RadioPreferidoKm);
    }

    private static EditarPerfilPrestadorCmd Edicion(
        int version,
        string nombre = "Estudio Espinoza",
        string? direccion = null,
        double? latitud = null,
        double? longitud = null) => new(
            NombrePublico: nombre,
            Descripcion: null,
            RubroId: 1,
            TelefonoPublico: null,
            CorreoPublico: null,
            Whatsapp: null,
            SitioWeb: null,
            Direccion: direccion,
            Latitud: latitud,
            Longitud: longitud,
            Version: version);
}
