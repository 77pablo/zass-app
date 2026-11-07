using System.ComponentModel.DataAnnotations;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Zass.Aplicacion.Identidad;
using Zass.Aplicacion.Puertos;
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

    [Required(ErrorMessage = "Falta la cadena de conexion a PostgreSQL.")]
    public string CadenaConexion { get; set; } = "";

    /// <summary>
    /// Clave de cifrado de direcciones (RN-S1-06), en Base64 y de 32 bytes. En
    /// produccion vive en N-10 Key Vault; en desarrollo, en User Secrets. Nunca en el
    /// repositorio.
    /// </summary>
    [Required(ErrorMessage = "Falta la clave de cifrado de direcciones.")]
    public string ClaveDireccionesBase64 { get; set; } = "";

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
                new[] { "ClaveDireccionesBase64 no es Base64 valido." });
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
        servicios.AddScoped<INotificaciones, ColaNotificacionesEfCore>();
        servicios.AddScoped<IAuditor, AuditorEfCore>();

        // --- MOD-01 Identidad ---
        servicios.AddScoped<IServicioIdentidad, ServicioIdentidad>();
        servicios.AddScoped<ServicioRegistro>();
        servicios.AddScoped<ServicioSesion>();
        servicios.AddScoped<ServicioCuenta>();
        servicios.AddScoped<ServicioPerfiles>();

        // --- Validadores ---
        // Se registran por ensamblado a proposito: agregar un comando sin su validador
        // no deja un hueco silencioso, porque el filtro de Api rechaza lo que no tiene
        // validador registrado (seguridad.md, seccion 3).
        servicios.AddValidatorsFromAssemblyContaining<ValidadorRegistrarCuenta>();

        return servicios;
    }
}
