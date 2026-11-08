using System.ComponentModel.DataAnnotations;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Zass.Aplicacion.Identidad;
using Zass.Aplicacion.Catalogo;
using Zass.Aplicacion.Credenciales;
using Zass.Aplicacion.Busqueda;
using Zass.Aplicacion.Geografia;
using Zass.Aplicacion.Suscripciones;
using Zass.Aplicacion.Puertos;
using Zass.Infraestructura.Correo;
using Zass.Infraestructura.Datos;
using Zass.Infraestructura.Datos.Repositorios;
using Zass.Infraestructura.Seguridad;

namespace Zass.Infraestructura.Configuracion;

/// <summary>
/// seguridad.md, seccion 2: la aplicacion NO arranca si falta una variable. Estas
/// anotaciones mas ValidateOnStart() son lo que lo hace cumplir: un despliegue sin la
/// clave de cifrado falla al levantar, no cuando el primer prestador guarda su direccion.
/// </summary>
public sealed class OpcionesZass
{
    public const string Seccion = "Zass";

    [Required(ErrorMessage = "Falta la cadena de conexión a PostgreSQL.")]
    public string CadenaConexion { get; set; } = "";

    /// <summary>
    /// Clave de cifrado de direcciones (RN-S1-06), en Base64 y de 32 bytes. En
    /// produccion vive en N-10 Key Vault; en desarrollo, en User Secrets. Nunca en el
    /// repositorio.
    /// </summary>
    [Required(ErrorMessage = "Falta la clave de cifrado de direcciones.")]
    public string ClaveDireccionesBase64 { get; set; } = "";

    /// <summary>
    /// Dirección pública del sitio (p. ej. https://zass.cl). Con ella se arman los enlaces de
    /// los correos. Opcional al arrancar: sin ella los correos no se envían (y se registra).
    /// </summary>
    public string? UrlPublica { get; set; }

    public byte[] ClaveDirecciones()
    {
        byte[] clave;
        try
        {
            clave = Convert.FromBase64String(ClaveDireccionesBase64);
        }
        catch (FormatException)
        {
            throw new OptionsValidationException(
                Seccion, typeof(OpcionesZass),
                new[] { "ClaveDireccionesBase64 no es Base64 válido." });
        }

        if (clave.Length != 32)
            throw new OptionsValidationException(
                Seccion, typeof(OpcionesZass),
                new[] { $"La clave de direcciones tiene {clave.Length} bytes y necesita 32." });

        return clave;
    }
}

public static class RegistroDeServicios
{
    /// <summary>
    /// Todo lo que Zass.Api necesita para funcionar, en un solo lugar.
    ///
    /// Que este metodo viva en Infraestructura y no en Api es la inversion de
    /// dependencias hecha concreta: Api registra implementaciones que no conoce, y
    /// Aplicacion sigue sin saber que existe EF Core.
    /// </summary>
    public static IServiceCollection AgregarZass(
        this IServiceCollection servicios, IConfiguration configuracion)
    {
        servicios.AddOptions<OpcionesZass>()
            .Bind(configuracion.GetSection(OpcionesZass.Seccion))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // Las opciones se leen al RESOLVER, no al registrar. Leerlas aqui mismo
        // congelaria la configuracion que existia en este instante, y se perderia
        // cualquier fuente agregada despues: Key Vault en produccion, y los ajustes de
        // las pruebas, que se inyectan luego de que Program.cs llama a este metodo.
        servicios.AddDbContext<ZassDbContext>((sp, o) =>
            o.UseNpgsql(sp.GetRequiredService<IOptions<OpcionesZass>>().Value.CadenaConexion));

        // --- Puertos de plataforma ---
        servicios.AddSingleton<IRelojSistema, RelojUtc>();
        servicios.AddSingleton<IHashContrasenas, HashArgon2id>();
        servicios.AddSingleton<IGeneradorSecretos, GeneradorSecretos>();
        servicios.AddSingleton<ICifradoDeDatos>(sp => new CifradoAesGcm(
            sp.GetRequiredService<IOptions<OpcionesZass>>().Value.ClaveDirecciones()));

        // --- Puertos con estado por peticion ---
        servicios.AddScoped<IUnidadDeTrabajo, UnidadDeTrabajoEfCore>();
        servicios.AddScoped<IRepositorioUsuarios, RepositorioUsuariosEfCore>();
        servicios.AddScoped<IRepositorioSesiones, RepositorioSesionesEfCore>();
        servicios.AddScoped<IRepositorioPerfiles, RepositorioPerfilesEfCore>();
        servicios.AddScoped<IRepositorioSuscripciones, RepositorioSuscripcionesEfCore>();
        servicios.AddScoped<IRepositorioCatalogo, RepositorioCatalogoEfCore>();
        servicios.AddScoped<IRepositorioGeografia, RepositorioGeografiaEfCore>();
        servicios.AddScoped<IRepositorioBusqueda, RepositorioBusquedaEfCore>();

        // N-08 todavia no existe. Esta implementacion contesta que no esta disponible, y
        // eso dispara el flujo 4B de CU-03 en vez de un 500.
        servicios.AddSingleton<IGeocodificador, GeocodificadorNoDisponible>();
        servicios.AddScoped<INotificaciones, ColaNotificacionesEfCore>();
        servicios.AddScoped<IAuditor, AuditorEfCore>();

        // AddLogging aqui y no en quien llama: varios servicios de este modulo escriben en
        // el registro (la sonda de abajo, y los enviadores y el procesador de correo). Sin
        // esto, AgregarZass declararia servicios cuya dependencia no provee y solo
        // funcionaria dentro de una aplicacion de ASP.NET, que trae el registro por su
        // cuenta; ContenedorPruebas construye un ServiceCollection DESNUDO a proposito,
        // justo para que eso no pase inadvertido. Es idempotente (usa TryAdd): no pisa la
        // configuracion de registro de la API.
        servicios.AddLogging();

        // La sonda de /api/salud/lista. Singleton: crea su propio ambito en cada consulta.
        servicios.AddSingleton<ISondaDeBase, SondaDeBase>();

        // --- MOD-01 Identidad ---
        servicios.AddScoped<IServicioIdentidad, ServicioIdentidad>();
        servicios.AddScoped<ServicioRegistro>();
        servicios.AddScoped<ServicioSesion>();
        servicios.AddScoped<ServicioCuenta>();
        servicios.AddScoped<ServicioPerfiles>();

        // --- MOD-02 Credenciales ---
        servicios.AddScoped<IRepositorioCredenciales, RepositorioCredencialesEfCore>();
        servicios.AddScoped<IServicioCredenciales, ServicioCredenciales>();

        // --- MOD-03 Catalogo ---
        servicios.AddScoped<IServicioCatalogo, ServicioCatalogo>();

        // --- MOD-04 Geolocalizacion ---
        servicios.AddScoped<IServicioGeografia, ServicioGeografia>();

        // --- MOD-05 Busqueda ---
        servicios.AddScoped<IServicioBusqueda, ServicioBusqueda>();
        servicios.AddScoped<IRepositorioMapaDelSitio, RepositorioMapaDelSitioEfCore>();
        servicios.AddScoped<ServicioMapaDelSitio>();

        // --- MOD-13 Suscripciones ---
        servicios.AddScoped<IServicioSuscripciones, ServicioSuscripciones>();

        // --- Correo transaccional ---
        // Con proveedor distinto de Ninguno y sin clave, la aplicacion se niega a arrancar
        // (ValidadorOpcionesCorreo + ValidateOnStart), igual que con la clave de cifrado.
        servicios.AddOptions<OpcionesCorreo>()
            .Bind(configuracion.GetSection(OpcionesCorreo.Seccion))
            .ValidateOnStart();
        servicios.AddSingleton<IValidateOptions<OpcionesCorreo>, ValidadorOpcionesCorreo>();
        servicios.AddSingleton<IEnviadorDeCorreo>(sp =>
        {
            var o = sp.GetRequiredService<IOptions<OpcionesCorreo>>().Value;
            if (o.Proveedor == ProveedorDeCorreo.Ninguno) return new EnviadorNulo();

            // Un solo HttpClient con vida de conexion acotada (para que el DNS se renueve).
            var http = new HttpClient(new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(5) })
            {
                Timeout = TimeSpan.FromSeconds(o.TiempoLimiteHttpSegundos),
            };
            return o.Proveedor == ProveedorDeCorreo.Brevo
                ? new EnviadorBrevo(http, Options.Create(o), sp.GetRequiredService<ILogger<EnviadorBrevo>>())
                : new EnviadorResend(http, Options.Create(o), sp.GetRequiredService<ILogger<EnviadorResend>>());
        });
        servicios.AddScoped<ProcesadorDeCorreo>();
        servicios.AddHostedService<TrabajoDeCorreo>();

        // --- Validadores ---
        // Se registran por ensamblado a proposito: agregar un comando sin su validador
        // no deja un hueco silencioso, porque el filtro de Api rechaza lo que no tiene
        // validador registrado (seguridad.md, sección 3).
        servicios.AddValidatorsFromAssemblyContaining<ValidadorRegistrarCuenta>();

        return servicios;
    }
}
