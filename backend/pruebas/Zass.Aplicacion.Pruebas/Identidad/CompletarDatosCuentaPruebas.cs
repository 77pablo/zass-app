using Zass.Aplicacion.Identidad;
using Zass.Aplicacion.Pruebas.Dobles;
using Zass.Dominio.Comun;
using Zass.Dominio.Identidad;

namespace Zass.Aplicacion.Pruebas.Identidad;

/// <summary>
/// ES-2 · Completar los datos de una cuenta creada antes del registro completo (20-nov-2026).
///
/// Los mensajes van escritos a mano: una prueba que compara contra la misma constante que
/// vigila no vigila nada.
/// </summary>
public class CompletarDatosCuentaPruebas
{
    private readonly BaseEnMemoria _base = new();
    private readonly RelojFijo _reloj = new();
    private readonly AuditoriaEnMemoria _auditoria = new();
    private readonly ServicioRegistro _registro;
    private readonly ServicioCuenta _cuenta;

    public CompletarDatosCuentaPruebas()
    {
        var usuarios = new RepositorioUsuariosEnMemoria(_base);
        var perfiles = new RepositorioPerfilesEnMemoria(_base);
        var hash = new HashDeJuguete();
        var secretos = new SecretosDeterministas();
        var cola = new ColaEnMemoria();
        var filtradas = new ContrasenasFiltradasNunca();

        _registro = new ServicioRegistro(
            usuarios, perfiles, _base, hash, secretos, _reloj, cola, _auditoria, filtradas);
        var sesiones = new ServicioSesion(
            usuarios, new RepositorioSesionesEnMemoria(_base), _base, hash, secretos, _reloj, _auditoria);
        _cuenta = new ServicioCuenta(
            usuarios, perfiles, _base, hash, secretos, _reloj, cola, _auditoria, sesiones, filtradas);
    }

    /// <summary>Una cuenta como las de antes del #174: sin RUT, sin telefono, sin casilla.</summary>
    private Usuario UnaCuentaAntigua(string rol = "buscador")
    {
        var u = new Usuario
        {
            Correo = $"antigua-{Guid.NewGuid():N}@correo.cl", Nombre = "Ana",
            Estado = EstadoCuenta.Activa, CorreoVerificado = true,
        };
        var r = _base.Roles.Single(x => x.Codigo == rol);
        u.Roles.Add(new UsuarioRol { UsuarioId = u.Id, RolId = r.Id, Rol = r });
        _base.Usuarios.Add(u);
        return u;
    }

    private static CompletarDatosCuentaCmd Datos(
        string tipo = "particular", string rut = "11.111.111-1", string? razonSocial = null) =>
        new(tipo, tipo == "particular" ? "Rojas" : null, rut, " +56 9 1234 5678 ", razonSocial, true);

    // ---------------------------------------------------------------- que falta

    [Fact]
    public void A_una_cuenta_antigua_le_faltan_los_tres_datos()
    {
        var u = UnaCuentaAntigua();

        Assert.Equal(new[] { "rut", "telefono", "mayorDeEdad" }, DatosDeCuenta.Pendientes(u));
    }

    [Fact]
    public async Task Mi_cuenta_le_dice_a_la_interfaz_que_datos_faltan()
    {
        var u = UnaCuentaAntigua();

        var r = await _cuenta.MiCuentaAsync(u.Id);

        Assert.Equal(new[] { "rut", "telefono", "mayorDeEdad" }, r.Valor!.DatosPendientes);
    }

    /// <summary>
    /// Las cuentas internas no se bloquean: las crea un administrador a mano y bloquearlas podria
    /// dejar al equipo fuera del panel el dia del despliegue.
    /// </summary>
    [Theory]
    [InlineData("verificador")]
    [InlineData("moderador")]
    [InlineData("administrador")]
    public void Una_cuenta_interna_no_tiene_datos_pendientes(string rol)
    {
        var u = UnaCuentaAntigua(rol);

        Assert.Empty(DatosDeCuenta.Pendientes(u));
    }

    // ---------------------------------------------------------------- completar

    [Fact]
    public async Task Completar_llena_los_datos_y_ya_no_falta_nada()
    {
        var u = UnaCuentaAntigua();

        var r = await _registro.CompletarDatosAsync(u.Id, Datos());

        Assert.True(r.Exito, r.Mensaje);
        Assert.Equal("11111111-1", u.Rut);            // normalizado, como en el registro
        Assert.Equal("+56 9 1234 5678", u.Telefono);  // recortado
        Assert.Equal("Rojas", u.Apellido);
        Assert.Equal(TipoCuenta.Particular, u.Tipo);
        Assert.Equal(_reloj.Ahora, u.MayorDeEdadConfirmadoEn);
        Assert.Empty(DatosDeCuenta.Pendientes(u));
        Assert.Contains(_auditoria.Registros, a => a.Accion == "datos_de_cuenta_completados");
    }

    [Fact]
    public async Task Una_empresa_guarda_su_razon_social_y_no_un_apellido()
    {
        var u = UnaCuentaAntigua("prestador");

        var r = await _registro.CompletarDatosAsync(
            u.Id, Datos(tipo: "empresa", rut: "76.086.428-5", razonSocial: " Gasfiteria Rojas SpA "));

        Assert.True(r.Exito, r.Mensaje);
        Assert.Equal(TipoCuenta.Empresa, u.Tipo);
        Assert.Equal("Gasfiteria Rojas SpA", u.RazonSocial);
        Assert.Null(u.Apellido);
    }

    /// <summary>
    /// El RUT de otra cuenta: el MISMO mensaje que el registro, escrito a mano. No dice de quien
    /// es ni deja distinguir un camino del otro.
    /// </summary>
    [Fact]
    public async Task Un_rut_que_usa_otra_cuenta_se_rechaza_con_el_mensaje_del_registro()
    {
        var duena = UnaCuentaAntigua();
        duena.Rut = "11111111-1";
        var u = UnaCuentaAntigua();

        var r = await _registro.CompletarDatosAsync(u.Id, Datos(rut: "11.111.111-1"));

        Assert.False(r.Exito);
        Assert.Equal(Fallas.Conflicto, r.Codigo);
        Assert.Equal("Ya existe una cuenta con ese RUT. Si es tuya, entra con tu correo.", r.Mensaje);
        Assert.Null(u.Rut);
    }

    [Fact]
    public async Task Una_cuenta_ya_completa_no_usa_este_formulario_para_editar()
    {
        var u = UnaCuentaAntigua();
        await _registro.CompletarDatosAsync(u.Id, Datos());

        var r = await _registro.CompletarDatosAsync(u.Id, Datos(rut: "76.086.428-5"));

        Assert.False(r.Exito);
        Assert.Equal(Fallas.Conflicto, r.Codigo);
        Assert.Equal("11111111-1", u.Rut);
    }

    /// <summary>
    /// Si la cuenta ya tenia RUT (y le falta otra cosa), su RUT no se cambia por aqui: cambiar
    /// el RUT de una cuenta seria cambiar de quien es.
    /// </summary>
    [Fact]
    public async Task Un_rut_que_la_cuenta_ya_tiene_no_se_cambia()
    {
        var u = UnaCuentaAntigua();
        u.Rut = "11111111-1";

        var otro = await _registro.CompletarDatosAsync(u.Id, Datos(rut: "76.086.428-5"));
        Assert.False(otro.Exito);
        Assert.Equal(Fallas.Validacion, otro.Codigo);
        Assert.Equal("11111111-1", u.Rut);

        var mismo = await _registro.CompletarDatosAsync(u.Id, Datos(rut: "11.111.111-1"));
        Assert.True(mismo.Exito, mismo.Mensaje);
        Assert.Empty(DatosDeCuenta.Pendientes(u));
    }

    [Fact]
    public async Task Una_cuenta_suspendida_no_completa_nada()
    {
        var u = UnaCuentaAntigua();
        u.Estado = EstadoCuenta.Suspendida;

        var r = await _registro.CompletarDatosAsync(u.Id, Datos());

        Assert.False(r.Exito);
        Assert.Equal(Fallas.NoAutorizado, r.Codigo);
        Assert.Null(u.Rut);
    }

    // ---------------------------------------------------------------- validacion

    /// <summary>
    /// «Las mismas validaciones del registro, sin duplicarlas»: con los mismos datos malos, los
    /// dos validadores dan los mismos errores en los mismos campos.
    /// </summary>
    [Theory]
    [InlineData("particular", "Rojas", "11.111.111-2", "+56 9 1234 5678", null, true)]   // digito malo
    [InlineData("particular", "Rojas", "11.111.111-1", "1234", null, true)]              // telefono corto
    [InlineData("particular", "Rojas", "11.111.111-1", "+56 9 1234 5678", null, false)]  // sin casilla
    [InlineData("empresa", null, "76.086.428-5", "+56 9 1234 5678", null, true)]         // sin razon social
    [InlineData("particular", null, "11.111.111-1", "+56 9 1234 5678", null, true)]      // sin apellido
    [InlineData("otro", "Rojas", "11.111.111-1", "+56 9 1234 5678", null, true)]         // tipo raro
    [InlineData("particular", "Rojas", "", "+56 9 1234 5678", null, true)]               // sin RUT
    public void Completar_y_registrar_rechazan_lo_mismo_con_el_mismo_mensaje(
        string tipo, string? apellido, string rut, string telefono, string? razon, bool mayor)
    {
        var registro = new ValidadorRegistrarCuenta().Validate(new RegistrarCuentaCmd(
            "ana@correo.cl", "contrasena-larga-1", "Ana", "buscador",
            tipo, apellido, rut, telefono, razon, mayor));
        var completar = new ValidadorCompletarDatosCuenta().Validate(
            new CompletarDatosCuentaCmd(tipo, apellido, rut, telefono, razon, mayor));

        Assert.False(completar.IsValid);
        Assert.Equal(
            registro.Errors.Select(e => (e.PropertyName, e.ErrorMessage)),
            completar.Errors.Select(e => (e.PropertyName, e.ErrorMessage)));
    }

    [Fact]
    public void Datos_buenos_pasan_la_validacion_de_completar()
    {
        var r = new ValidadorCompletarDatosCuenta().Validate(Datos());

        Assert.True(r.IsValid, string.Join("; ", r.Errors.Select(e => e.ErrorMessage)));
    }
}
