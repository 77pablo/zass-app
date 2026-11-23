using Microsoft.Extensions.Logging.Abstractions;
using Zass.Aplicacion.Comun;
using Zass.Aplicacion.Credenciales;
using Zass.Aplicacion.Identidad;
using Zass.Aplicacion.Puertos;
using Zass.Infraestructura.Archivos;
using Zass.Infraestructura.Datos;
using Zass.Infraestructura.Datos.Repositorios;

namespace Zass.Integracion.Pruebas;

/// <summary>
/// Arma <see cref="ServicioCuenta"/> con piezas reales (EF Core y el almacén local), como lo hace
/// AgregarZass, para las pruebas que lo construyen a mano sin pasar por HTTP.
/// </summary>
public static class ArmadoDeCuentaReal
{
    public static ServicioCuenta Crear(
        ZassDbContext ctx, IHashContrasenas hash, IGeneradorSecretos secretos, IRelojSistema reloj,
        INotificaciones cola, IAuditor auditor, IUnidadDeTrabajo unidad, ServicioSesion sesion,
        IContrasenasFiltradas filtradas)
    {
        var usuarios = new RepositorioUsuariosEfCore(ctx);
        var perfiles = new RepositorioPerfilesEfCore(ctx);
        var mfa = new ServicioMfa(usuarios, unidad, hash, secretos, MfaDePrueba.Cifrado, reloj, auditor, sesion, cola);
        var almacen = new AlmacenLocal(Aplicacion.CarpetaDeArchivos);
        var borrado = new ServicioBorradoDeDatos(
            perfiles, new RepositorioPushEfCore(ctx),
            new ServicioCredenciales(new RepositorioCredencialesEfCore(ctx), unidad, reloj, auditor, null, almacen),
            almacen, auditor, reloj, NullLogger<ServicioBorradoDeDatos>.Instance);
        return new ServicioCuenta(
            usuarios, perfiles, unidad, hash, secretos, reloj, cola, auditor, sesion, filtradas, mfa, borrado);
    }
}
