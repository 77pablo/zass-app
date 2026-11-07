using Zass.Aplicacion.Identidad;

namespace Zass.Aplicacion.Pruebas.Identidad;

/// <summary>
/// seguridad.md, seccion 3: esquema estricto por endpoint, lo que no esta declarado se
/// rechaza. Estas pruebas cubren el borde, no el camino feliz: lo que hay que rechazar.
/// </summary>
public class ValidadoresPruebas
{
    private readonly ValidadorRegistrarCuenta _registro = new();
    private readonly ValidadorIniciarSesion _sesion = new();
    private readonly ValidadorEditarPerfilPrestador _perfil = new();
    private readonly ValidadorEditarPerfilBuscador _buscador = new();
    private readonly ValidadorRestablecerContrasena _restablecer = new();

    private static RegistrarCuentaCmd Registro(
        string correo = "pablo@correo.cl",
        string contrasena = "contrasena-larga-1",
        string nombre = "Pablo Espinoza",
        string rol = "buscador") => new(correo, contrasena, nombre, rol);

    [Theory]
    [InlineData("")]
    [InlineData("pablo")]
    [InlineData("pablo@")]
    [InlineData("pablo correo.cl")]
    public void Un_correo_que_no_es_correo_se_rechaza(string correo)
    {
        Assert.False(_registro.Validate(Registro(correo: correo)).IsValid);
    }

    [Theory]
    [InlineData("corta")]
    [InlineData("123456789")]
    public void Una_contrasena_de_menos_de_diez_caracteres_se_rechaza(string contrasena)
    {
        Assert.False(_registro.Validate(Registro(contrasena: contrasena)).IsValid);
    }

    [Fact]
    public void Una_contrasena_larguisima_se_rechaza_porque_hashearla_cuesta()
    {
        // Argon2id con una entrada de megabytes es una negacion de servicio gratis.
        Assert.False(_registro.Validate(Registro(contrasena: new string('a', 500))).IsValid);
    }

    [Theory]
    [InlineData("administrador")]
    [InlineData("verificador")]
    [InlineData("moderador")]
    [InlineData("obispo")]
    public void Al_registrarse_solo_valen_los_dos_roles_publicos(string rol)
    {
        Assert.False(_registro.Validate(Registro(rol: rol)).IsValid);
    }

    [Theory]
    [InlineData("buscador")]
    [InlineData("prestador")]
    public void Un_registro_normal_pasa(string rol)
    {
        Assert.True(_registro.Validate(Registro(rol: rol)).IsValid);
    }

    [Fact]
    public void Un_nombre_vacio_se_rechaza()
    {
        Assert.False(_registro.Validate(Registro(nombre: "   ")).IsValid);
    }

    [Fact]
    public void Iniciar_sesion_exige_correo_y_contrasena_pero_no_juzga_el_largo()
    {
        // A proposito: si el validador exigiera 10 caracteres aqui, cualquiera sabria el
        // largo minimo de las contrasenas del sistema probando el formulario de entrada.
        Assert.False(_sesion.Validate(new IniciarSesionCmd("", "x")).IsValid);
        Assert.True(_sesion.Validate(new IniciarSesionCmd("pablo@correo.cl", "x")).IsValid);
    }

    [Theory]
    [InlineData(-91.0, -72.6)]
    [InlineData(91.0, -72.6)]
    [InlineData(-38.7, -181.0)]
    [InlineData(-38.7, 181.0)]
    public void Unas_coordenadas_imposibles_se_rechazan(double latitud, double longitud)
    {
        Assert.False(_perfil.Validate(Perfil(latitud: latitud, longitud: longitud)).IsValid);
    }

    [Fact]
    public void Una_latitud_sin_longitud_se_rechaza_porque_media_coordenada_no_ubica_nada()
    {
        Assert.False(_perfil.Validate(Perfil(latitud: -38.74, longitud: null)).IsValid);
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("ftp://archivos.cl")]
    [InlineData("no es una url")]
    public void Un_sitio_web_que_no_es_http_se_rechaza(string sitio)
    {
        Assert.False(_perfil.Validate(Perfil(sitioWeb: sitio)).IsValid);
    }

    [Fact]
    public void Un_sitio_web_normal_pasa()
    {
        Assert.True(_perfil.Validate(Perfil(sitioWeb: "https://estudioespinoza.cl")).IsValid);
    }

    [Theory]
    [InlineData("+56912345678")]
    [InlineData("912345678")]
    [InlineData("+56 9 1234 5678")]
    public void Un_telefono_chileno_de_movil_pasa(string telefono)
    {
        Assert.True(_perfil.Validate(Perfil(telefono: telefono)).IsValid);
    }

    [Theory]
    [InlineData("12345")]
    [InlineData("no-es-telefono")]
    public void Un_telefono_que_no_lo_es_se_rechaza(string telefono)
    {
        Assert.False(_perfil.Validate(Perfil(telefono: telefono)).IsValid);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(51)]
    [InlineData(-5)]
    public void El_radio_fuera_de_1_a_50_se_rechaza(int radio)
    {
        var cmd = new EditarPerfilBuscadorCmd(null, (short)radio, true, true, false);
        Assert.False(_buscador.Validate(cmd).IsValid);
    }

    [Fact]
    public void Restablecer_exige_token_y_una_contrasena_que_valga()
    {
        Assert.False(_restablecer.Validate(new RestablecerContrasenaCmd("", "contrasena-larga-1")).IsValid);
        Assert.False(_restablecer.Validate(new RestablecerContrasenaCmd("token", "corta")).IsValid);
        Assert.True(_restablecer.Validate(new RestablecerContrasenaCmd("token", "contrasena-larga-1")).IsValid);
    }

    /// <summary>
    /// La descripcion del perfil admite hasta 1500 caracteres. Era 2000 y Pablo lo bajo: una
    /// presentacion de mas de 1500 ya no se lee en una pantalla de celular. Nadie probaba
    /// este limite, ni el viejo ni el nuevo.
    /// </summary>
    [Fact]
    public void La_descripcion_admite_1500_caracteres_y_no_1501()
    {
        Assert.True(_perfil.Validate(Perfil(descripcion: new string('a', 1500))).IsValid);
        Assert.False(_perfil.Validate(Perfil(descripcion: new string('a', 1501))).IsValid);
    }

    [Fact]
    public void Una_descripcion_de_2000_caracteres_ya_no_vale()
    {
        // El limite anterior. Si esto pasara, el cambio a 1500 no habria llegado al validador.
        Assert.False(_perfil.Validate(Perfil(descripcion: new string('a', 2000))).IsValid);
    }

    [Fact]
    public void La_descripcion_puede_estar_vacia()
    {
        Assert.True(_perfil.Validate(Perfil(descripcion: null)).IsValid);
        Assert.True(_perfil.Validate(Perfil(descripcion: "")).IsValid);
    }

    private static EditarPerfilPrestadorCmd Perfil(
        double? latitud = null,
        double? longitud = null,
        string? sitioWeb = null,
        string? telefono = null,
        string? descripcion = null) => new(
            NombrePublico: "Estudio Espinoza",
            Descripcion: descripcion,
            RubroId: 1,
            TelefonoPublico: telefono,
            CorreoPublico: null,
            Whatsapp: null,
            SitioWeb: sitioWeb,
            Direccion: latitud is null ? null : "Avenida Alemania 0231",
            Latitud: latitud,
            Longitud: longitud,
            Version: 0);
}
