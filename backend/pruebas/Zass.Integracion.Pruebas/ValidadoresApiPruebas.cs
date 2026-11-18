using FluentValidation;
using Zass.Api.Configuracion;
using Zass.Api.Endpoints;
using Zass.Aplicacion.Catalogo;
using Zass.Aplicacion.Geografia;
using Zass.Aplicacion.Identidad;

namespace Zass.Integracion.Pruebas;

/// <summary>
/// Pruebas de unidad para los validadores de la API (ValidadoresApi.cs).
/// Cada regla de cada validador tiene al menos: un caso valido, un caso invalido y el borde.
/// No usan base de datos: son pruebas puras de validacion de FluentValidation.
/// </summary>
public class ValidadoresApiPruebas
{
    private readonly ValidadorVerificarCorreo _verificarCorreo = new();
    private readonly ValidadorPedirRecuperacion _pedirRecuperacion = new();
    private readonly ValidadorReenviarVerificacion _reenviarVerificacion = new();
    private readonly ValidadorCambiarContrasena _cambiarContrasena = new();
    private readonly ValidadorReemplazarServicios _reemplazarServicios = new();
    private readonly ValidadorReemplazarCobertura _reemplazarCobertura = new();
    private readonly ValidadorBusqueda _busqueda = new();

    #region Helpers

    private static VerificarCorreoCmd VerificarCorreo(string token = "token-valido-123") => new(token);

    private static PedirRecuperacionCmd PedirRecuperacion(string correo = "pablo@correo.cl") => new(correo);

    private static ReenviarVerificacionCmd ReenviarVerificacion(string correo = "pablo@correo.cl") => new(correo);

    private static CambiarContrasenaCmd CambiarContrasena(
        string contrasenaActual = "contrasena-actual-1",
        string contrasenaNueva = "contrasena-nueva-1") => new(contrasenaActual, contrasenaNueva);

    private static ServicioOfrecidoCmd Servicio(
        int? servicioId = 1,
        string? nombreNuevo = null,
        short duracionMinutos = 60,
        string modoAgenda = "libre",
        int? precioReferencia = 10000) => new(servicioId, nombreNuevo, duracionMinutos, modoAgenda, precioReferencia);

    private static ReemplazarServiciosHttpCmd ReemplazarServicios(params ServicioOfrecidoCmd[] servicios) =>
        new(servicios.ToList().AsReadOnly());

    private static ZonaCubiertaCmd Zona(int zonaId = 1, bool esPrincipal = false) => new(zonaId, esPrincipal);

    private static ReemplazarCoberturaHttpCmd ReemplazarCobertura(params ZonaCubiertaCmd[] zonas) =>
        new(zonas.ToList().AsReadOnly());

    private static BusquedaHttpQuery Busqueda(
        string? rubro = "abogacia",
        int? servicio = null,
        double? lat = -38.74,
        double? lon = -72.6,
        int? zona = null,
        short? radio = 20,
        bool? verificados = null,
        decimal? calificacion = null,
        string? cursor = null) => new(rubro, servicio, lat, lon, zona, radio, verificados, calificacion, cursor);

    #endregion

    #region ValidadorVerificarCorreo

    [Fact]
    public void Token_vacio_se_rechaza()
    {
        var result = _verificarCorreo.Validate(VerificarCorreo(token: ""));
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == "Token");
    }

    [Fact]
    public void Token_de_200_caracteres_pasa()
    {
        var result = _verificarCorreo.Validate(VerificarCorreo(token: new string('a', 200)));
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Token_de_más_de_200_caracteres_se_rechaza()
    {
        var result = _verificarCorreo.Validate(VerificarCorreo(token: new string('a', 201)));
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == "Token" && e.ErrorCode == "MaximumLengthValidator");
    }

    [Fact]
    public void Token_valido_pasa()
    {
        var result = _verificarCorreo.Validate(VerificarCorreo());
        Assert.True(result.IsValid);
    }

    #endregion

    #region ValidadorPedirRecuperacion

    [Fact]
    public void Correo_vacio_se_rechaza_en_pedir_recuperacion()
    {
        var result = _pedirRecuperacion.Validate(PedirRecuperacion(correo: ""));
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == "Correo");
    }

    [Fact]
    public void Correo_sin_formato_se_rechaza_en_pedir_recuperacion()
    {
        var result = _pedirRecuperacion.Validate(PedirRecuperacion(correo: "sin-arroba"));
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == "Correo");
    }

    [Fact]
    public void Correo_de_254_caracteres_pasa_en_pedir_recuperacion()
    {
        // 254 es el máximo según RFC 5321
        var correoLargo = new string('a', 243) + "@correo.cl"; // 243 + 11 = 254
        var result = _pedirRecuperacion.Validate(PedirRecuperacion(correo: correoLargo));
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Correo_valido_pasa_en_pedir_recuperacion()
    {
        var result = _pedirRecuperacion.Validate(PedirRecuperacion());
        Assert.True(result.IsValid);
    }

    #endregion

    #region ValidadorReenviarVerificacion

    [Fact]
    public void Correo_vacio_se_rechaza_en_reenviar_verificacion()
    {
        var result = _reenviarVerificacion.Validate(ReenviarVerificacion(correo: ""));
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == "Correo");
    }

    // Un correo vacio tambien lo rechaza EmailAddress, asi que la prueba de arriba no vigila el
    // NotEmpty: quitarlo no hacia caer nada (mutacion M8 del vigilante, 18-nov). Esta mira la regla.
    [Fact]
    public void Correo_vacio_lo_rechaza_la_regla_de_obligatorio_en_reenviar_verificacion()
    {
        var result = _reenviarVerificacion.Validate(ReenviarVerificacion(correo: ""));
        Assert.Contains(result.Errors, e => e.PropertyName == "Correo" && e.ErrorCode == "NotEmptyValidator");
    }

    [Fact]
    public void Correo_sin_formato_se_rechaza_en_reenviar_verificacion()
    {
        var result = _reenviarVerificacion.Validate(ReenviarVerificacion(correo: "invalido"));
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == "Correo");
    }

    [Fact]
    public void Correo_valido_pasa_en_reenviar_verificacion()
    {
        var result = _reenviarVerificacion.Validate(ReenviarVerificacion());
        Assert.True(result.IsValid);
    }

    #endregion

    #region ValidadorCambiarContrasena

    [Fact]
    public void ContrasenaActual_vacia_se_rechaza()
    {
        var result = _cambiarContrasena.Validate(CambiarContrasena(contrasenaActual: ""));
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == "ContrasenaActual");
    }

    [Fact]
    public void ContrasenaNueva_vacia_se_rechaza()
    {
        var result = _cambiarContrasena.Validate(CambiarContrasena(contrasenaNueva: ""));
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == "ContrasenaNueva");
    }

    [Fact]
    public void ContrasenaNueva_de_menos_de_10_caracteres_se_rechaza()
    {
        var result = _cambiarContrasena.Validate(CambiarContrasena(contrasenaNueva: "corta"));
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == "ContrasenaNueva");
    }

    [Fact]
    public void ContrasenaNueva_de_10_caracteres_pasa()
    {
        var result = _cambiarContrasena.Validate(CambiarContrasena(contrasenaNueva: "1234567890"));
        Assert.True(result.IsValid);
    }

    [Fact]
    public void ContrasenaNueva_igual_que_la_actual_se_rechaza()
    {
        var result = _cambiarContrasena.Validate(CambiarContrasena(
            contrasenaActual: "misma-contrasena",
            contrasenaNueva: "misma-contrasena"));
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == "ContrasenaNueva");
    }

    [Fact]
    public void Cambiar_contrasena_valido_pasa()
    {
        var result = _cambiarContrasena.Validate(CambiarContrasena());
        Assert.True(result.IsValid);
    }

    #endregion

    #region ValidadorReemplazarServicios

    [Fact]
    public void Servicios_nulo_se_rechaza()
    {
        var cmd = new ReemplazarServiciosHttpCmd(null!);
        var result = _reemplazarServicios.Validate(cmd);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == "Servicios");
    }

    [Fact]
    public void Lista_vacia_de_servicios_pasa()
    {
        var result = _reemplazarServicios.Validate(ReemplazarServicios());
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Cincuenta_servicios_pasan_es_el_maximo()
    {
        var servicios = Enumerable.Range(1, 50).Select(i => Servicio()).ToList();
        var result = _reemplazarServicios.Validate(ReemplazarServicios(servicios.ToArray()));
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Cincuenta_y_un_servicios_se_rechazan()
    {
        var servicios = Enumerable.Range(1, 51).Select(i => Servicio()).ToList();
        var result = _reemplazarServicios.Validate(ReemplazarServicios(servicios.ToArray()));
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == "Servicios.Count");
    }

    [Fact]
    public void Servicio_con_ModoAgenda_vacio_se_rechaza()
    {
        var result = _reemplazarServicios.Validate(ReemplazarServicios(Servicio(modoAgenda: "")));
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == "Servicios[0].ModoAgenda" && e.ErrorCode == "NotEmptyValidator");
    }

    [Fact]
    public void Servicio_con_nombre_nuevo_de_200_caracteres_pasa()
    {
        var result = _reemplazarServicios.Validate(ReemplazarServicios(Servicio(nombreNuevo: new string('a', 200))));
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Servicio_con_nombre_nuevo_de_más_de_200_caracteres_se_rechaza()
    {
        var result = _reemplazarServicios.Validate(ReemplazarServicios(Servicio(nombreNuevo: new string('a', 201))));
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == "Servicios[0].NombreNuevo" && e.ErrorCode == "MaximumLengthValidator");
    }

    [Fact]
    public void Servicio_con_ModoAgenda_de_20_caracteres_pasa()
    {
        var result = _reemplazarServicios.Validate(ReemplazarServicios(Servicio(modoAgenda: new string('a', 20))));
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Servicio_con_ModoAgenda_de_más_de_20_caracteres_se_rechaza()
    {
        var result = _reemplazarServicios.Validate(ReemplazarServicios(Servicio(modoAgenda: new string('a', 21))));
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == "Servicios[0].ModoAgenda" && e.ErrorCode == "MaximumLengthValidator");
    }

    #endregion

    #region ValidadorReemplazarCobertura

    [Fact]
    public void Zonas_nulo_se_rechaza()
    {
        var cmd = new ReemplazarCoberturaHttpCmd(null!);
        var result = _reemplazarCobertura.Validate(cmd);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == "Zonas");
    }

    [Fact]
    public void Lista_vacia_de_zonas_pasa()
    {
        var result = _reemplazarCobertura.Validate(ReemplazarCobertura());
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Cincuenta_zonas_pasan_es_el_maximo()
    {
        var zonas = Enumerable.Range(1, 50).Select(i => Zona()).ToList();
        var result = _reemplazarCobertura.Validate(ReemplazarCobertura(zonas.ToArray()));
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Cincuenta_y_un_zonas_se_rechazan()
    {
        var zonas = Enumerable.Range(1, 51).Select(i => Zona()).ToList();
        var result = _reemplazarCobertura.Validate(ReemplazarCobertura(zonas.ToArray()));
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == "Zonas.Count");
    }

    #endregion

    #region ValidadorBusqueda

    [Fact]
    public void Rubro_vacio_se_rechaza()
    {
        var result = _busqueda.Validate(Busqueda(rubro: ""));
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == "Rubro");
    }

    [Fact]
    public void Rubro_de_40_caracteres_pasa()
    {
        var result = _busqueda.Validate(Busqueda(rubro: new string('a', 40)));
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Rubro_de_más_de_40_caracteres_se_rechaza()
    {
        var result = _busqueda.Validate(Busqueda(rubro: new string('a', 41)));
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == "Rubro" && e.ErrorCode == "MaximumLengthValidator");
    }

    [Fact]
    public void Latitud_fuera_de_rango_se_rechaza()
    {
        var result = _busqueda.Validate(Busqueda(lat: -91.0));
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == "Lat");
    }

    [Fact]
    public void Longitud_fuera_de_rango_se_rechaza()
    {
        var result = _busqueda.Validate(Busqueda(lon: -181.0));
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == "Lon");
    }

    [Fact]
    public void Latitud_sin_longitud_se_rechaza()
    {
        var result = _busqueda.Validate(Busqueda(lat: -38.74, lon: null));
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == "Lon");
    }

    [Fact]
    public void Longitud_sin_latitud_se_rechaza()
    {
        var result = _busqueda.Validate(Busqueda(lat: null, lon: -72.6));
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == "Lat");
    }

    [Fact]
    public void Coordenadas_validas_juntas_pasan()
    {
        var result = _busqueda.Validate(Busqueda(lat: -38.74, lon: -72.6));
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Coordenadas_nulas_pasan_es_opcional()
    {
        var result = _busqueda.Validate(Busqueda(lat: null, lon: null));
        Assert.True(result.IsValid);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(6)]
    public void Calificacion_fuera_de_1_a_5_se_rechaza(decimal calificacion)
    {
        var result = _busqueda.Validate(Busqueda(calificacion: calificacion));
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == "Calificacion");
    }

    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    public void Calificacion_de_1_a_5_pasa(decimal calificacion)
    {
        var result = _busqueda.Validate(Busqueda(calificacion: calificacion));
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Calificacion_nula_pasa_es_opcional()
    {
        var result = _busqueda.Validate(Busqueda(calificacion: null));
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Cursor_de_200_caracteres_pasa()
    {
        var result = _busqueda.Validate(Busqueda(cursor: new string('a', 200)));
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Cursor_de_más_de_200_caracteres_se_rechaza()
    {
        var result = _busqueda.Validate(Busqueda(cursor: new string('a', 201)));
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == "Cursor" && e.ErrorCode == "MaximumLengthValidator");
    }

    [Fact]
    public void Busqueda_valida_pasa()
    {
        var result = _busqueda.Validate(Busqueda());
        Assert.True(result.IsValid);
    }

    #endregion

    }
