using Zass.Aplicacion.Identidad;

namespace Zass.Aplicacion.Pruebas.Identidad;

/// <summary>
/// Pruebas de unidad para los validadores de Identidad (Validadores.cs).
/// Cada regla de cada validador tiene al menos: un caso valido, un caso invalido y el borde.
/// </summary>
public class ValidadoresIdentidadPruebas
{
    private readonly ValidadorRegistrarCuenta _registro = new();
    private readonly ValidadorIniciarSesion _sesion = new();
    private readonly ValidadorRestablecerContrasena _restablecer = new();
    private readonly ValidadorEditarPerfilPrestador _perfil = new();
    private readonly ValidadorEditarPerfilBuscador _buscador = new();

    #region Helpers

    private static RegistrarCuentaCmd Registro(
        string correo = "pablo@correo.cl",
        string contrasena = "contrasena-larga-1",
        string nombre = "Pablo Espinoza",
        string rol = "buscador") => new(correo, contrasena, nombre, rol);

    private static IniciarSesionCmd Sesion(
        string correo = "pablo@correo.cl",
        string contrasena = "contrasena-larga-1") => new(correo, contrasena);

    private static RestablecerContrasenaCmd Restablecer(
        string token = "token-valido-123",
        string contrasena = "contrasena-larga-1") => new(token, contrasena);

    private static EditarPerfilPrestadorCmd Perfil(
        string nombrePublico = "Estudio Espinoza",
        string? descripcion = null,
        short? rubroId = 1,
        string? telefonoPublico = null,
        string? correoPublico = null,
        string? whatsapp = null,
        string? sitioWeb = null,
        string? direccion = null,
        double? latitud = null,
        double? longitud = null,
        int version = 0) => new(
            NombrePublico: nombrePublico,
            Descripcion: descripcion,
            RubroId: rubroId,
            TelefonoPublico: telefonoPublico,
            CorreoPublico: correoPublico,
            Whatsapp: whatsapp,
            SitioWeb: sitioWeb,
            Direccion: direccion,
            Latitud: latitud,
            Longitud: longitud,
            Version: version);

    private static EditarPerfilBuscadorCmd Buscador(
        short radioPreferidoKm = 20,
        int? zonaInteresId = null,
        bool aceptaAvisos = true,
        bool canalCorreo = true,
        bool canalPush = false) => new(
            ZonaInteresId: zonaInteresId,
            RadioPreferidoKm: radioPreferidoKm,
            AceptaAvisos: aceptaAvisos,
            CanalCorreo: canalCorreo,
            CanalPush: canalPush);

    #endregion

    #region ValidadorRegistrarCuenta

    [Fact]
    public void Correo_vacio_no_pasa()
    {
        var result = _registro.Validate(Registro(correo: ""));
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == "Correo");
    }

    [Theory]
    [InlineData("sin-arroba")]
    [InlineData("arroba-solo@")]
    [InlineData("dos@@correo.cl")]
    public void Correo_sin_formato_de_correo_se_rechaza(string correo)
    {
        var result = _registro.Validate(Registro(correo: correo));
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == "Correo");
    }

    [Fact]
    public void Correo_demasiado_largo_se_rechaza()
    {
        var correoLargo = new string('a', Limites.CorreoMaximo + 1) + "@correo.cl";
        var result = _registro.Validate(Registro(correo: correoLargo));
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == "Correo" && e.ErrorCode == "MaximumLengthValidator");
    }

    [Fact]
    public void Correo_valido_pasa()
    {
        var result = _registro.Validate(Registro(correo: "usuario.valido@ejemplo.cl"));
        Assert.True(result.IsValid);
    }

    [Theory]
    [InlineData("corta")]
    [InlineData("123456789")]
    public void Contrasena_de_menos_de_10_caracteres_se_rechaza(string contrasena)
    {
        var result = _registro.Validate(Registro(contrasena: contrasena));
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == "Contrasena");
    }

    [Fact]
    public void Contrasena_de_10_caracteres_pasa_es_el_minimo()
    {
        var result = _registro.Validate(Registro(contrasena: "1234567890"));
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Contrasena_de_128_caracteres_pasa_es_el_maximo()
    {
        var result = _registro.Validate(Registro(contrasena: new string('a', Limites.ContrasenaMaxima)));
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Contrasena_de_más_de_128_caracteres_se_rechaza()
    {
        var result = _registro.Validate(Registro(contrasena: new string('a', Limites.ContrasenaMaxima + 1)));
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == "Contrasena" && e.ErrorCode == "MaximumLengthValidator");
    }

    [Fact]
    public void Contrasena_vacia_se_rechaza()
    {
        var result = _registro.Validate(Registro(contrasena: ""));
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == "Contrasena");
    }

    [Fact]
    public void Nombre_vacio_se_rechaza()
    {
        var result = _registro.Validate(Registro(nombre: "   "));
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == "Nombre");
    }

    [Fact]
    public void Nombre_de_150_caracteres_pasa_es_el_maximo()
    {
        var result = _registro.Validate(Registro(nombre: new string('a', Limites.NombreMaximo)));
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Nombre_de_más_de_150_caracteres_se_rechaza()
    {
        var result = _registro.Validate(Registro(nombre: new string('a', Limites.NombreMaximo + 1)));
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == "Nombre" && e.ErrorCode == "MaximumLengthValidator");
    }

    [Theory]
    [InlineData("administrador")]
    [InlineData("verificador")]
    [InlineData("moderador")]
    public void Rol_que_no_es_buscador_o_prestador_se_rechaza(string rol)
    {
        var result = _registro.Validate(Registro(rol: rol));
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == "Rol");
    }

    [Theory]
    [InlineData("buscador")]
    [InlineData("prestador")]
    public void Rol_buscador_o_prestador_pasa(string rol)
    {
        var result = _registro.Validate(Registro(rol: rol));
        Assert.True(result.IsValid);
    }

    #endregion

    #region ValidadorIniciarSesion

    [Fact]
    public void Iniciar_sesion_con_correo_vacio_se_rechaza()
    {
        var result = _sesion.Validate(Sesion(correo: ""));
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == "Correo");
    }

    [Fact]
    public void Iniciar_sesion_con_contrasena_vacia_se_rechaza()
    {
        var result = _sesion.Validate(Sesion(contrasena: ""));
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == "Contrasena");
    }

    [Fact]
    public void Iniciar_sesion_con_contrasena_corta_pasa_no_se_valida_largo_aqui()
    {
        // A proposito: revelar el largo minimo seria un problema de seguridad
        var result = _sesion.Validate(Sesion(contrasena: "x"));
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Iniciar_sesion_valido_pasa()
    {
        var result = _sesion.Validate(Sesion());
        Assert.True(result.IsValid);
    }

    #endregion

    #region ValidadorRestablecerContrasena

    [Fact]
    public void Token_vacio_se_rechaza()
    {
        var result = _restablecer.Validate(Restablecer(token: ""));
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == "Token");
    }

    // El token del enlace tiene tope de 200 (Validadores.cs). Sin estas dos, aflojar ese tope
    // no hacia caer ninguna prueba (mutacion M3 del vigilante, 18-nov).
    [Fact]
    public void Token_de_200_caracteres_pasa_en_restablecer()
    {
        var result = _restablecer.Validate(Restablecer(token: new string('a', 200)));
        Assert.DoesNotContain(result.Errors, e => e.PropertyName == "Token");
    }

    [Fact]
    public void Token_de_201_caracteres_se_rechaza_en_restablecer()
    {
        var result = _restablecer.Validate(Restablecer(token: new string('a', 201)));
        Assert.Contains(result.Errors, e => e.PropertyName == "Token" && e.ErrorCode == "MaximumLengthValidator");
    }

    [Fact]
    public void ContrasenaNueva_vacia_se_rechaza()
    {
        var result = _restablecer.Validate(Restablecer(contrasena: ""));
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == "ContrasenaNueva");
    }

    [Fact]
    public void ContrasenaNueva_de_menos_de_10_caracteres_se_rechaza()
    {
        var result = _restablecer.Validate(Restablecer(contrasena: "corta"));
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == "ContrasenaNueva");
    }

    [Fact]
    public void ContrasenaNueva_de_10_caracteres_pasa()
    {
        var result = _restablecer.Validate(Restablecer(contrasena: "1234567890"));
        Assert.True(result.IsValid);
    }

    [Fact]
    public void ContrasenaNueva_de_128_caracteres_pasa()
    {
        var result = _restablecer.Validate(Restablecer(contrasena: new string('a', Limites.ContrasenaMaxima)));
        Assert.True(result.IsValid);
    }

    [Fact]
    public void ContrasenaNueva_de_más_de_128_caracteres_se_rechaza()
    {
        var result = _restablecer.Validate(Restablecer(contrasena: new string('a', Limites.ContrasenaMaxima + 1)));
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == "ContrasenaNueva" && e.ErrorCode == "MaximumLengthValidator");
    }

    [Fact]
    public void Restablecer_valido_pasa()
    {
        var result = _restablecer.Validate(Restablecer());
        Assert.True(result.IsValid);
    }

    #endregion

    #region ValidadorEditarPerfilPrestador

    [Fact]
    public void NombrePublico_vacio_se_rechaza()
    {
        var result = _perfil.Validate(Perfil(nombrePublico: "   "));
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == "NombrePublico");
    }

    [Fact]
    public void NombrePublico_de_150_caracteres_pasa()
    {
        var result = _perfil.Validate(Perfil(nombrePublico: new string('a', Limites.NombrePublicoMaximo)));
        Assert.True(result.IsValid);
    }

    [Fact]
    public void NombrePublico_de_más_de_150_caracteres_se_rechaza()
    {
        var result = _perfil.Validate(Perfil(nombrePublico: new string('a', Limites.NombrePublicoMaximo + 1)));
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == "NombrePublico" && e.ErrorCode == "MaximumLengthValidator");
    }

    [Fact]
    public void Descripcion_de_1500_caracteres_pasa()
    {
        var result = _perfil.Validate(Perfil(descripcion: new string('a', Limites.DescripcionMaxima)));
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Descripcion_de_1501_caracteres_se_rechaza()
    {
        var result = _perfil.Validate(Perfil(descripcion: new string('a', Limites.DescripcionMaxima + 1)));
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == "Descripcion" && e.ErrorCode == "MaximumLengthValidator");
    }

    [Fact]
    public void Descripcion_vacia_o_nulo_pasa()
    {
        Assert.True(_perfil.Validate(Perfil(descripcion: null)).IsValid);
        Assert.True(_perfil.Validate(Perfil(descripcion: "")).IsValid);
    }

    [Fact]
    public void RubroId_cero_se_rechaza()
    {
        var result = _perfil.Validate(Perfil(rubroId: 0));
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == "RubroId");
    }

    [Fact]
    public void RubroId_positivo_pasa()
    {
        var result = _perfil.Validate(Perfil(rubroId: 1));
        Assert.True(result.IsValid);
    }

    [Fact]
    public void RubroId_nulo_pasa_es_opcional()
    {
        var result = _perfil.Validate(Perfil(rubroId: null));
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Version_negativa_se_rechaza()
    {
        var result = _perfil.Validate(Perfil(version: -1));
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == "Version");
    }

    [Fact]
    public void Version_cero_pasa()
    {
        var result = _perfil.Validate(Perfil(version: 0));
        Assert.True(result.IsValid);
    }

    [Theory]
    [InlineData("12345")]
    [InlineData("no-es-telefono")]
    public void TelefonoPublico_que_no_es_movil_chileno_se_rechaza(string telefono)
    {
        var result = _perfil.Validate(Perfil(telefonoPublico: telefono));
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == "TelefonoPublico");
    }

    [Theory]
    [InlineData("+56912345678")]
    [InlineData("912345678")]
    [InlineData("+56 9 1234 5678")]
    public void TelefonoPublico_movil_chileno_pasa(string telefono)
    {
        var result = _perfil.Validate(Perfil(telefonoPublico: telefono));
        Assert.True(result.IsValid);
    }

    [Fact]
    public void TelefonoPublico_nulo_pasa_es_opcional()
    {
        var result = _perfil.Validate(Perfil(telefonoPublico: null));
        Assert.True(result.IsValid);
    }

    [Theory]
    [InlineData("12345")]
    [InlineData("no-es-telefono")]
    public void Whatsapp_que_no_es_movil_chileno_se_rechaza(string whatsapp)
    {
        var result = _perfil.Validate(Perfil(whatsapp: whatsapp));
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == "Whatsapp");
    }

    [Theory]
    [InlineData("+56912345678")]
    [InlineData("912345678")]
    public void Whatsapp_movil_chileno_pasa(string whatsapp)
    {
        var result = _perfil.Validate(Perfil(whatsapp: whatsapp));
        Assert.True(result.IsValid);
    }

    [Theory]
    [InlineData("sin-arroba")]
    [InlineData("dos@@correo.cl")]
    public void CorreoPublico_sin_formato_de_correo_se_rechaza(string correo)
    {
        var result = _perfil.Validate(Perfil(correoPublico: correo));
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == "CorreoPublico");
    }

    [Fact]
    public void CorreoPublico_valido_pasa()
    {
        var result = _perfil.Validate(Perfil(correoPublico: "contacto@estudio.cl"));
        Assert.True(result.IsValid);
    }

    [Fact]
    public void CorreoPublico_nulo_pasa_es_opcional()
    {
        var result = _perfil.Validate(Perfil(correoPublico: null));
        Assert.True(result.IsValid);
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("ftp://sitio.cl")]
    [InlineData("no-es-url")]
    public void SitioWeb_que_no_es_http_o_https_se_rechaza(string sitio)
    {
        var result = _perfil.Validate(Perfil(sitioWeb: sitio));
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == "SitioWeb");
    }

    [Theory]
    [InlineData("https://estudioespinoza.cl")]
    [InlineData("http://estudio.local")]
    public void SitioWeb_http_o_https_pasa(string sitio)
    {
        var result = _perfil.Validate(Perfil(sitioWeb: sitio));
        Assert.True(result.IsValid);
    }

    [Fact]
    public void SitioWeb_de_más_de_300_caracteres_se_rechaza()
    {
        var urlLarga = "https://" + new string('a', Limites.UrlMaxima - 8) + ".cl";
        var result = _perfil.Validate(Perfil(sitioWeb: urlLarga));
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == "SitioWeb" && e.ErrorCode == "MaximumLengthValidator");
    }

    [Fact]
    public void SitioWeb_nulo_pasa_es_opcional()
    {
        var result = _perfil.Validate(Perfil(sitioWeb: null));
        Assert.True(result.IsValid);
    }

    [Theory]
    [InlineData(-91.0, -72.6)]
    [InlineData(91.0, -72.6)]
    [InlineData(-38.7, -181.0)]
    [InlineData(-38.7, 181.0)]
    public void Coordenadas_imposibles_se_rechazan(double latitud, double longitud)
    {
        var result = _perfil.Validate(Perfil(latitud: latitud, longitud: longitud));
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == "Latitud" || e.PropertyName == "Longitud");
    }

    [Fact]
    public void Latitud_sin_longitud_se_rechaza()
    {
        var result = _perfil.Validate(Perfil(latitud: -38.74, longitud: null));
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == "Longitud");
    }

    [Fact]
    public void Longitud_sin_latitud_se_rechaza()
    {
        var result = _perfil.Validate(Perfil(latitud: null, longitud: -72.6));
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == "Latitud");
    }

    [Fact]
    public void Coordenadas_validas_juntas_pasan()
    {
        var result = _perfil.Validate(Perfil(latitud: -38.74, longitud: -72.6));
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Coordenadas_nulas_pasan_es_opcional()
    {
        var result = _perfil.Validate(Perfil(latitud: null, longitud: null));
        Assert.True(result.IsValid);
    }

    #endregion

    #region ValidadorEditarPerfilBuscador

    [Theory]
    [InlineData(0)]
    [InlineData(51)]
    [InlineData(-5)]
    public void Radio_fuera_de_1_a_50_se_rechaza(short radio)
    {
        var result = _buscador.Validate(Buscador(radioPreferidoKm: radio));
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == "RadioPreferidoKm");
    }

    [Fact]
    public void Radio_de_1_km_pasa_es_el_minimo()
    {
        var result = _buscador.Validate(Buscador(radioPreferidoKm: 1));
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Radio_de_50_km_pasa_es_el_maximo()
    {
        var result = _buscador.Validate(Buscador(radioPreferidoKm: 50));
        Assert.True(result.IsValid);
    }

    [Fact]
    public void ZonaInteresId_cero_se_rechaza()
    {
        var result = _buscador.Validate(Buscador(zonaInteresId: 0));
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == "ZonaInteresId");
    }

    [Fact]
    public void ZonaInteresId_positivo_pasa()
    {
        var result = _buscador.Validate(Buscador(zonaInteresId: 1));
        Assert.True(result.IsValid);
    }

    [Fact]
    public void ZonaInteresId_nulo_pasa_es_opcional()
    {
        var result = _buscador.Validate(Buscador(zonaInteresId: null));
        Assert.True(result.IsValid);
    }

    #endregion
}
