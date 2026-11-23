using Zass.Aplicacion.Identidad;
using Zass.Aplicacion.Pruebas.Dobles;
using Zass.Dominio.Comun;

namespace Zass.Aplicacion.Pruebas.Identidad;

/// <summary>
/// P19 (T49): rechazar contrasenas que aparecen en filtraciones conocidas (HIBP).
/// Pruebas A y B del diseño: servicio con dobles.
/// </summary>
public class ContrasenasFiltradasPruebas
{
    private readonly BaseEnMemoria _base = new();
    private readonly RelojFijo _reloj = new();
    private readonly HashDeJuguete _hash = new();
    private readonly SecretosDeterministas _secretos = new();
    private readonly ColaEnMemoria _cola = new();
    private readonly AuditoriaEnMemoria _auditoria = new();

    // ----------------------------------------------------------- A) Doble que dice "filtrada"

    [Fact]
    public async Task Registro_rechaza_contrasena_filtrada_y_no_guarda_nada()
    {
        var filtradas = new ContrasenasFiltradasFiltrada("password");
        var servicio = new ServicioRegistro(
            new RepositorioUsuariosEnMemoria(_base),
            new RepositorioPerfilesEnMemoria(_base),
            _base, _hash, _secretos, _reloj, _cola, _auditoria, filtradas);

        var r = await servicio.RegistrarAsync(new RegistrarCuentaCmd("pablo@correo.cl", "password", "Pablo", "buscador"));

        Assert.False(r.Exito);
        Assert.Equal(Fallas.Validacion, r.Codigo);
        Assert.Equal("Esa contraseña aparece en filtraciones de datos conocidas. Elige otra.", r.Mensaje);
        Assert.Empty(_base.Usuarios);
        Assert.Equal(0, _hash.LlamadasHash);
    }

    [Fact]
    public async Task Restablecer_rechaza_contrasena_filtrada_y_no_cambia_el_hash()
    {
        var filtradas = new ContrasenasFiltradasFiltrada("password");
        var usuarios = new RepositorioUsuariosEnMemoria(_base);
        var perfiles = new RepositorioPerfilesEnMemoria(_base);
        var sesiones = new RepositorioSesionesEnMemoria(_base);

        var registro = new ServicioRegistro(usuarios, perfiles, _base, _hash, _secretos, _reloj, _cola, _auditoria, filtradas);
        var cuenta = ArmadoDeCuenta.Cuenta(_base, _hash, _secretos, _reloj, _cola, _auditoria, new ServicioSesion(usuarios, sesiones, _base, _hash, _secretos, _reloj, _auditoria), filtradas);

        // Registrar con contrasena NO filtrada
        await registro.RegistrarAsync(new RegistrarCuentaCmd("pablo@correo.cl", "contrasena-larga-1", "Pablo", "buscador"));
        var usuario = _base.Usuarios.Single();
        var hashOriginal = usuario.HashContrasena;

        // Pedir recuperacion
        await cuenta.PedirRecuperacionAsync("pablo@correo.cl");
        var token = _secretos.Emitidos.Last();

        // Intentar restablecer con contrasena filtrada
        var r = await cuenta.RestablecerContrasenaAsync(token, "password");

        Assert.False(r.Exito);
        Assert.Equal(Fallas.Validacion, r.Codigo);
        Assert.Equal("Esa contraseña aparece en filtraciones de datos conocidas. Elige otra.", r.Mensaje);
        // El hash NO debe haber cambiado
        Assert.Equal(hashOriginal, _base.Usuarios.Single().HashContrasena);
    }

    [Fact]
    public async Task Cambiar_rechaza_contrasena_filtrada_y_no_cambia_el_hash()
    {
        var filtradas = new ContrasenasFiltradasFiltrada("password");
        var usuarios = new RepositorioUsuariosEnMemoria(_base);
        var perfiles = new RepositorioPerfilesEnMemoria(_base);
        var sesiones = new RepositorioSesionesEnMemoria(_base);

        var registro = new ServicioRegistro(usuarios, perfiles, _base, _hash, _secretos, _reloj, _cola, _auditoria, filtradas);
        var sesionSvc = new ServicioSesion(usuarios, sesiones, _base, _hash, _secretos, _reloj, _auditoria);
        var cuenta = ArmadoDeCuenta.Cuenta(_base, _hash, _secretos, _reloj, _cola, _auditoria, sesionSvc, filtradas);

        // Registrar e iniciar sesion con contrasena NO filtrada
        await registro.RegistrarAsync(new RegistrarCuentaCmd("pablo@correo.cl", "contrasena-larga-1", "Pablo", "buscador"));
        var id = _base.Usuarios.Single().Id;
        var hashOriginal = _base.Usuarios.Single().HashContrasena;
        var inicio = await sesionSvc.IniciarAsync(new IniciarSesionCmd("pablo@correo.cl", "contrasena-larga-1"));

        // Intentar cambiar a contrasena filtrada
        var r = await cuenta.CambiarContrasenaAsync(id, "contrasena-larga-1", "password", inicio.Valor!.TokenRefresco);

        Assert.False(r.Exito);
        Assert.Equal(Fallas.Validacion, r.Codigo);
        Assert.Equal("Esa contraseña aparece en filtraciones de datos conocidas. Elige otra.", r.Mensaje);
        // El hash NO debe haber cambiado
        Assert.Equal(hashOriginal, _base.Usuarios.Single().HashContrasena);
    }

    // ----------------------------------------------------------- B) Doble que dice "no filtrada" -> todo funciona igual

    [Fact]
    public async Task Registro_con_doble_no_filtrada_funciona_igual_que_hoy()
    {
        var filtradas = new ContrasenasFiltradasNunca();
        var servicio = new ServicioRegistro(
            new RepositorioUsuariosEnMemoria(_base),
            new RepositorioPerfilesEnMemoria(_base),
            _base, _hash, _secretos, _reloj, _cola, _auditoria, filtradas);

        var r = await servicio.RegistrarAsync(new RegistrarCuentaCmd("pablo@correo.cl", "contrasena-larga-1", "Pablo", "buscador"));

        Assert.True(r.Exito, r.Mensaje);
        Assert.Single(_base.Usuarios);
        Assert.Equal(1, _hash.LlamadasHash);
    }

    [Fact]
    public async Task Restablecer_con_doble_no_filtrada_funciona_igual_que_hoy()
    {
        var filtradas = new ContrasenasFiltradasNunca();
        var usuarios = new RepositorioUsuariosEnMemoria(_base);
        var perfiles = new RepositorioPerfilesEnMemoria(_base);
        var sesiones = new RepositorioSesionesEnMemoria(_base);

        var registro = new ServicioRegistro(usuarios, perfiles, _base, _hash, _secretos, _reloj, _cola, _auditoria, filtradas);
        var cuenta = ArmadoDeCuenta.Cuenta(_base, _hash, _secretos, _reloj, _cola, _auditoria, new ServicioSesion(usuarios, sesiones, _base, _hash, _secretos, _reloj, _auditoria), filtradas);

        await registro.RegistrarAsync(new RegistrarCuentaCmd("pablo@correo.cl", "contrasena-larga-1", "Pablo", "buscador"));
        await cuenta.PedirRecuperacionAsync("pablo@correo.cl");
        var token = _secretos.Emitidos.Last();

        var r = await cuenta.RestablecerContrasenaAsync(token, "clave-nueva-larga");

        Assert.True(r.Exito, r.Mensaje);
        Assert.True(_hash.Verifica("clave-nueva-larga", _base.Usuarios.Single().HashContrasena));
    }

    [Fact]
    public async Task Cambiar_con_doble_no_filtrada_funciona_igual_que_hoy()
    {
        var filtradas = new ContrasenasFiltradasNunca();
        var usuarios = new RepositorioUsuariosEnMemoria(_base);
        var perfiles = new RepositorioPerfilesEnMemoria(_base);
        var sesiones = new RepositorioSesionesEnMemoria(_base);

        var registro = new ServicioRegistro(usuarios, perfiles, _base, _hash, _secretos, _reloj, _cola, _auditoria, filtradas);
        var sesionSvc = new ServicioSesion(usuarios, sesiones, _base, _hash, _secretos, _reloj, _auditoria);
        var cuenta = ArmadoDeCuenta.Cuenta(_base, _hash, _secretos, _reloj, _cola, _auditoria, sesionSvc, filtradas);

        await registro.RegistrarAsync(new RegistrarCuentaCmd("pablo@correo.cl", "contrasena-larga-1", "Pablo", "buscador"));
        var id = _base.Usuarios.Single().Id;
        var inicio = await sesionSvc.IniciarAsync(new IniciarSesionCmd("pablo@correo.cl", "contrasena-larga-1"));

        var r = await cuenta.CambiarContrasenaAsync(id, "contrasena-larga-1", "clave-nueva-larga", inicio.Valor!.TokenRefresco);

        Assert.True(r.Exito, r.Mensaje);
        Assert.True(_hash.Verifica("clave-nueva-larga", _base.Usuarios.Single().HashContrasena));
    }
}