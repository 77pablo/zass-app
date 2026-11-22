using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Zass.Aplicacion.Identidad;
using Zass.Aplicacion.Agenda;
using Zass.Aplicacion.Catalogo;
using Zass.Aplicacion.Credenciales;
using Zass.Aplicacion.Busqueda;
using Zass.Aplicacion.Geografia;
using Zass.Aplicacion.Suscripciones;
using Zass.Aplicacion.Resenas;
using Zass.Aplicacion.Tarjetas;
using Zass.Aplicacion.Puertos;
using Zass.Infraestructura.Archivos;
using Zass.Infraestructura.Correo;
using Zass.Aplicacion.Notificaciones;
using Zass.Infraestructura.Push;
using Zass.Infraestructura.Fondo;
using Zass.Infraestructura.Pagos;
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
    /// produccion vive en las variables de entorno de Render; en desarrollo, en
    /// User Secrets. Nunca en el repositorio.
    /// *Diseño original: N-10 Key Vault con identidad administrada, porque no documentado.*
    /// </summary>
    [Required(ErrorMessage = "Falta la clave de cifrado de direcciones.")]
    public string ClaveDireccionesBase64 { get; set; } = "";

    /// <summary>
    /// Dirección pública del sitio (p. ej. https://zass.cl). Con ella se arman los enlaces de
    /// los correos. Opcional al arrancar: sin ella los correos no se envían (y se registra).
    /// </summary>
    public string? UrlPublica { get; set; }

    /// <summary>
    /// Secreto HMAC-SHA256 para firmar y verificar los avisos de pago (API-70). En produccion
    /// vive en las variables de entorno de Render; en desarrollo, se genera uno aleatorio por
    /// proceso. Minimo 32 caracteres. Config: Zass:SecretoAvisoPagos, env: Zass__SecretoAvisoPagos.
    /// </summary>
    public string? SecretoAvisoPagos { get; set; }

    /// <summary>
    /// Si es true, consulta Have I Been Pwned (rango k-anonimo) antes de aceptar una contrasena
    /// nueva. En produccion debe ser true; en desarrollo y pruebas false para no salir a internet.
    /// Por defecto true (se sobreescribe en appsettings.Development.json).
    /// </summary>
    public bool RevisarContrasenasFiltradas { get; set; } = true;

    /// <summary>
    /// Correo del administrador que recibe las alertas de seguridad (tarjetas bloqueadas por intentos fallidos).
    /// En produccion es obligatorio; en desarrollo/pruebas es opcional. Config: Zass:CorreoAlertasAdmin,
    /// env: Zass__CorreoAlertasAdmin.
    /// </summary>
    public string? CorreoAlertasAdmin { get; set; }

    /// <summary>
    /// Opciones de Flow.cl (la pasarela de pagos real). Se usan solo si Zass:Pasarela = "flow".
    /// </summary>
    public OpcionesFlow? Flow { get; set; }

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

/// <summary>
/// Opciones de Flow.cl. Se leen de Zass:Flow en appsettings.json o variables de entorno.
/// </summary>
public sealed class OpcionesFlow
{
    /// <summary>
    /// API Key de Flow (publica, se incluye en cada peticion).
    /// </summary>
    public string? ApiKey { get; set; }

    /// <summary>
    /// Secret Key para firmar las peticiones (HMAC-SHA256). Nunca se envia, solo firma.
    /// </summary>
    public string? SecretKey { get; set; }

    /// <summary>
    /// URL base de Flow. Por defecto https://sandbox.flow.cl/api; en produccion
    /// https://www.flow.cl/api.
    /// </summary>
    public string? UrlBase { get; set; }

    /// <summary>
    /// URL PUBLICA de la API que recibe los avisos de Flow (ej: https://api.zass.cl/api/pagos/aviso).
    /// </summary>
    public string? UrlAvisos { get; set; }

    /// <summary>
    /// A donde vuelve el navegador despues de pagar o inscribir la tarjeta (API-75). Opcional: si
    /// falta, sale de UrlAvisos cambiando «/aviso» por «/vuelta».
    /// </summary>
    public string? UrlVuelta { get; set; }
}

/// <summary>
/// Valida el secreto de firma de avisos de pago. En produccion exige 32+ caracteres;
/// en otros entornos genera uno aleatorio y avisa.
/// </summary>
public sealed class ValidadorOpcionesPagos : IValidateOptions<OpcionesZass>
{
    private readonly IHostEnvironment _entorno;
    private readonly ILogger<ValidadorOpcionesPagos> _registro;

    public ValidadorOpcionesPagos(IHostEnvironment entorno, ILogger<ValidadorOpcionesPagos> registro)
    {
        _entorno = entorno;
        _registro = registro;
    }

    public ValidateOptionsResult Validate(string? name, OpcionesZass o)
    {
        var fallos = new List<string>();

        if (string.IsNullOrWhiteSpace(o.SecretoAvisoPagos) || o.SecretoAvisoPagos.Length < 32)
        {
            if (_entorno.IsProduction())
            {
                fallos.Add("Falta Zass:SecretoAvisoPagos (32 caracteres o mas)");
            }
            else
            {
                // En desarrollo/Staging: generar uno aleatorio por proceso y avisar
                var bytes = new byte[32];
                RandomNumberGenerator.Fill(bytes);
                o.SecretoAvisoPagos = Convert.ToBase64String(bytes);
                _registro.LogWarning(
                    "Zass:SecretoAvisoPagos no configurado o muy corto; se genero uno aleatorio para este proceso. "
                    + "En produccion debe ponerse explicitamente (32+ caracteres).");
            }
        }

        // Validar CorreoAlertasAdmin: en produccion es obligatorio y debe tener forma de correo
        if (string.IsNullOrWhiteSpace(o.CorreoAlertasAdmin))
        {
            if (_entorno.IsProduction())
            {
                fallos.Add("Falta Zass:CorreoAlertasAdmin");
            }
            else
            {
                _registro.LogWarning("Zass:CorreoAlertasAdmin no configurado; las alertas de tarjeta bloqueada no se enviaran.");
            }
        }
        else if (!EsCorreoValido(o.CorreoAlertasAdmin))
        {
            if (_entorno.IsProduction())
            {
                fallos.Add("Zass:CorreoAlertasAdmin no tiene forma de correo valido.");
            }
            else
            {
                _registro.LogWarning("Zass:CorreoAlertasAdmin no tiene forma de correo valido; las alertas no se enviaran.");
            }
        }

        return fallos.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(fallos);
    }

    private static bool EsCorreoValido(string correo)
    {
        // Validacion simple pero efectiva: algo@algo.algo
        if (string.IsNullOrWhiteSpace(correo)) return false;
        var partes = correo.Split('@');
        if (partes.Length != 2) return false;
        if (string.IsNullOrWhiteSpace(partes[0]) || string.IsNullOrWhiteSpace(partes[1])) return false;
        if (!partes[1].Contains(".")) return false;
        return true;
    }
}

public static class RegistroDeServicios
{
    /// <summary>
    /// Que pasarela de pagos usar. Vive aqui y no en OpcionesZass porque no es un secreto
    /// ni algo sin lo que la aplicacion no pueda arrancar: tiene un valor por omision.
    /// </summary>
    public const string ClavePasarela = "Zass:Pasarela";

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

        // Las opciones de Flow NO se validan al arrancar: Zass:Flow solo existe donde se eligio
        // Zass:Pasarela=flow, y exigirlas siempre dejaria sin arrancar a produccion con la
        // simulada, a desarrollo y a todas las pruebas. Se exigen al resolver la pasarela
        // (el caso "flow" del switch de abajo). Se leen como parte de OpcionesZass.Flow.

        // Las opciones se leen al RESOLVER, no al registrar. Leerlas aqui mismo
        // congelaria la configuracion que existia en este instante, y se perderia
        // cualquier fuente agregada despues: las variables de entorno de Render en
        // produccion y los ajustes de las pruebas, que se inyectan luego de que
        // Program.cs llama a este metodo.
        servicios.AddDbContext<ZassDbContext>((sp, o) =>
            o.UseNpgsql(sp.GetRequiredService<IOptions<OpcionesZass>>().Value.CadenaConexion));

        // --- Puertos de plataforma ---
        servicios.AddSingleton<IRelojSistema, RelojUtc>();
        servicios.AddSingleton<IHashContrasenas, HashArgon2id>();
        servicios.AddSingleton<IGeneradorSecretos, GeneradorSecretos>();
        servicios.AddSingleton<ICifradoDeDatos>(sp => new CifradoAesGcm(
            sp.GetRequiredService<IOptions<OpcionesZass>>().Value.ClaveDirecciones()));

        // IContrasenasFiltradas: adaptador HIBP con HttpClient de 3s, o nulo segun opcion.
        // Se resuelve al final para leer la opcion ya vinculada.
        servicios.AddHttpClient<ContrasenasFiltradasHibp>(c =>
        {
            c.BaseAddress = new Uri("https://api.pwnedpasswords.com/");
            c.Timeout = TimeSpan.FromSeconds(3);
        });

        // Adaptador nulo para cuando RevisarContrasenasFiltradas = false (desarrollo, pruebas).
        servicios.AddSingleton<ContrasenasFiltradasNunca>();

        servicios.AddScoped<IContrasenasFiltradas>(sp =>
        {
            var opciones = sp.GetRequiredService<IOptions<OpcionesZass>>().Value;
            if (opciones.RevisarContrasenasFiltradas)
                return sp.GetRequiredService<ContrasenasFiltradasHibp>();
            return sp.GetRequiredService<ContrasenasFiltradasNunca>();
        });

        // FlowPasarela: HttpClient con timeout de 30s para llamadas a Flow.cl.
        servicios.AddHttpClient<FlowPasarela>(c =>
        {
            c.Timeout = TimeSpan.FromSeconds(30);
        });

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
        servicios.AddScoped<IRepositorioAuditoria, RepositorioAuditoriaEfCore>();

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
        // Auditoria 22-nov-2026: la purga de cuentas sin verificar (planificador y registro).
        servicios.AddScoped<IRepositorioCuentasSinVerificar, RepositorioCuentasSinVerificarEfCore>();
        servicios.AddScoped<ServicioCuentasSinVerificar>();
        servicios.AddScoped<ServicioSesion>();
        servicios.AddScoped<ServicioCuenta>();
        servicios.AddScoped<ServicioPerfiles>();

        // --- MOD-02 Credenciales ---
        servicios.AddScoped<IRepositorioCredenciales, RepositorioCredencialesEfCore>();
        servicios.AddScoped<IServicioCredenciales, ServicioCredenciales>();
        servicios.AddScoped<IServicioVerificacion, ServicioVerificacion>();
        // CS-06: el planificador vence las verificadas pasadas de fecha (auditoria 22-nov-2026).
        servicios.AddScoped<ServicioVencimientoCredenciales>();

        // DECISION DE PRODUCTO REVISABLE (etapa de lanzamiento, sin MOD-08): permite enviar a
        // revision sin documentos. Falso por omision. Se lee al RESOLVER, no al registrar, por la
        // misma razon que las opciones de arriba (fuentes agregadas despues).
        servicios.AddSingleton(sp => new OpcionesVerificacion
        {
            PermitirSinDocumentos = sp.GetRequiredService<IConfiguration>()
                .GetValue(OpcionesVerificacion.ClaveDeConfiguracion, false),
        });

        // --- MOD-08 Archivos (JQ-9): la foto del perfil, fotos de trabajos y los documentos de respaldo ---
        servicios.AddSingleton<IProcesadorDeImagenes, ProcesadorDeImagenesImageSharp>();
        servicios.AddSingleton<IAlmacenDeArchivos>(sp => ElegirAlmacen(sp));
        servicios.AddScoped<ServicioFotoDePerfil>();
        servicios.AddScoped<IServicioFotosDeTrabajo, ServicioFotosDeTrabajo>();

        // --- MOD-03 Catalogo ---
        servicios.AddScoped<IServicioCatalogo, ServicioCatalogo>();

        // --- MOD-04 Geolocalizacion ---
        servicios.AddScoped<IServicioGeografia, ServicioGeografia>();

        // --- MOD-06 Agenda ---
        servicios.AddScoped<IRepositorioAgenda, RepositorioAgendaEfCore>();
        servicios.AddScoped<IContactos, ContactosEfCore>();
        servicios.AddScoped<IServicioAgenda, ServicioAgenda>();

        // --- MOD-05 Busqueda ---
        servicios.AddScoped<IServicioBusqueda, ServicioBusqueda>();
        servicios.AddScoped<IRepositorioMapaDelSitio, RepositorioMapaDelSitioEfCore>();
        servicios.AddScoped<ServicioMapaDelSitio>();

        // --- MOD-06 Agenda ---
        // ANCLAJE: el agente de MOD-06 registra SOLO aqui. Existe vacio a proposito, para que
        // cuatro agentes a la vez inserten en regiones separadas y git fusione sin conflicto.

        // --- MOD-11 Resenas ---
        servicios.AddScoped<IRepositorioResenas, RepositorioResenasEfCore>();
        servicios.AddScoped<IServicioResenas, ServicioResenas>();

        // --- MOD-13 Suscripciones y cobros ---
        servicios.AddScoped<IServicioSuscripciones, ServicioSuscripciones>();
        servicios.AddScoped<IServicioCobros, ServicioCobros>();

        // La pasarela de pagos, elegida por configuracion. Hoy hay un solo adaptador; el
        // dia que Pablo decida entre Transbank, Flow y Mercado Pago, esto es un caso mas
        // en este switch y un archivo nuevo en Zass.Infraestructura/Pagos. Nada de la
        // aplicacion cambia, porque nada de la aplicacion sabe quien cobra.
        //
        // Un nombre desconocido NO cae en la simulada por omision: arrancar cobrando de
        // mentira por un error de escritura en una variable de entorno es la clase de fallo
        // que no se nota hasta que falta el dinero.
        //
        // Y en Produccion la simulada no se resuelve NUNCA, ni por omision ni pedida a
        // proposito. El caso que importa es el primero: la variable sin poner. El switch
        // solo se defendia del nombre desconocido, asi que un despliegue que olvidara
        // Zass:Pasarela arrancaba cobrando de mentira. Esto es N3 a proposito.
        servicios.AddSingleton<IPasarelaDePagos>(sp =>
        {
            var nombre = sp.GetRequiredService<IConfiguration>()
                .GetValue(ClavePasarela, PasarelaSimulada.NombreDeLaPasarela)!;
            var elegida = nombre.Trim().ToLowerInvariant();

            var entorno = sp.GetRequiredService<IHostEnvironment>();

            if (entorno.IsProduction() && elegida == PasarelaSimulada.NombreDeLaPasarela)
            {
                // La variable SIN PONER sigue fallando, que es el caso que de verdad importa:
                // un despliegue que la olvide no puede arrancar cobrando de mentira.
                var puestaAProposito = sp.GetRequiredService<IConfiguration>()[ClavePasarela];
                if (string.IsNullOrWhiteSpace(puestaAProposito))
                    throw new OptionsValidationException(
                        OpcionesZass.Seccion, typeof(OpcionesZass),
                        new[]
                        {
                            $"{ClavePasarela} no esta configurada y el entorno es Produccion. "
                            + $"La pasarela por omision ('{PasarelaSimulada.NombreDeLaPasarela}') no "
                            + "cobra: configura una real, o pon ese nombre a proposito para abrir "
                            + "en modo solo inscripcion.",
                        });

                // Puesta a proposito: se puede INSCRIBIR, nunca COBRAR. El invariante que
                // importa —una pasarela falsa jamas finge un cobro— se mantiene entero, y el
                // camino del cupo fundador (que no mueve un peso: tres montos en cero) funciona
                // mientras la pasarela real no este aprobada. Ver PasarelaSoloInscripcion.
                return new PasarelaSoloInscripcion(
                    new PasarelaSimulada(
                        sp.GetRequiredService<IOptions<OpcionesZass>>(),
                        sp.GetRequiredService<ILogger<PasarelaSimulada>>()),
                    sp.GetRequiredService<ILogger<PasarelaSoloInscripcion>>());
            }

            return elegida switch
            {
                PasarelaSimulada.NombreDeLaPasarela => new PasarelaSimulada(
                    sp.GetRequiredService<IOptions<OpcionesZass>>(),
                    sp.GetRequiredService<ILogger<PasarelaSimulada>>()),
                FlowPasarela.NombreDeLaPasarela => CrearFlow(sp),
                _ => throw new OptionsValidationException(
                    OpcionesZass.Seccion, typeof(OpcionesZass),
                    new[]
                    {
                        $"{ClavePasarela} dice '{nombre}' y no hay adaptador con ese nombre. "
                        + $"Los disponibles hoy son '{PasarelaSimulada.NombreDeLaPasarela}' y 'flow'.",
                    }),
            };
        });

        // --- MOD-12 Tarjetas NFC ---
        // Scoped como los demas: comparten el DbContext de la peticion.
        servicios.AddScoped<IRepositorioTarjetas, RepositorioTarjetasEfCore>();
        servicios.AddScoped<IServicioTarjetas, ServicioTarjetas>();

        // --- Trabajos de fondo ---
        // ANCLAJE: el agente de los trabajos de fondo registra SOLO aqui (vencer suscripciones,
        // avisar antes de vencer, despachar los avisos de TBL-27).

        // --- Correo transaccional ---
        // Con proveedor distinto de Ninguno y sin clave, la aplicacion se niega a arrancar
        // (ValidadorOpcionesCorreo + ValidateOnStart), igual que con la clave de cifrado.
        servicios.AddOptions<OpcionesCorreo>()
            .Bind(configuracion.GetSection(OpcionesCorreo.Seccion))
            .ValidateOnStart();
        servicios.AddSingleton<IValidateOptions<OpcionesCorreo>, ValidadorOpcionesCorreo>();

        // --- Validacion del secreto de avisos de pago ---
        // En produccion exige 32+ caracteres; en desarrollo/Staging genera uno aleatorio.
        servicios.AddSingleton<IValidateOptions<OpcionesZass>, ValidadorOpcionesPagos>();
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

        // --- ES-3 · Avisos push (Web Push con VAPID) ---
        // Sin claves queda apagado; con media configuracion la aplicacion no arranca.
        servicios.AddOptions<OpcionesPush>()
            .Bind(configuracion.GetSection(OpcionesPush.Seccion))
            .ValidateOnStart();
        servicios.AddSingleton<IValidateOptions<OpcionesPush>, ValidadorOpcionesPush>();
        servicios.AddSingleton<IEnviadorDePush>(sp =>
        {
            var o = sp.GetRequiredService<IOptions<OpcionesPush>>().Value;
            if (!o.Activo) return new EnviadorPushNulo();
            var http = new HttpClient(new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(5) })
            {
                Timeout = TimeSpan.FromSeconds(o.TiempoLimiteHttpSegundos),
            };
            return new EnviadorWebPush(http, Options.Create(o));
        });
        servicios.AddScoped<ProcesadorDePush>();
        servicios.AddHostedService<TrabajoDePush>();
        servicios.AddScoped<IRepositorioPush, RepositorioPushEfCore>();
        servicios.AddScoped<ServicioAvisosPush>();
        servicios.AddScoped<IRepositorioPreferenciasAviso, RepositorioPreferenciasAvisoEfCore>();
        servicios.AddScoped<ServicioPreferenciasAviso>();

        // --- Alertas al administrador ---
        servicios.AddScoped<IAlertasAdministrador, AlertasAdministrador>();

        // --- A-09 Planificador (trabajos de fondo) ---
        servicios.AddOptions<OpcionesFondo>().Bind(configuracion.GetSection(OpcionesFondo.Seccion));
        servicios.AddScoped<CoordinadorDeTrabajos>();
        servicios.AddHostedService<Planificador>();

        // --- Validadores ---
        // Se registran por ensamblado a proposito: agregar un comando sin su validador
        // no deja un hueco silencioso, porque el filtro de Api rechaza lo que no tiene
        // validador registrado (seguridad.md, sección 3).
        servicios.AddValidatorsFromAssemblyContaining<ValidadorRegistrarCuenta>();

        return servicios;
    }

    /// <summary>
    /// Flow, elegido con <c>Zass:Pasarela=flow</c>. Si falta la ApiKey, la SecretKey o la
    /// URL de avisos, la API NO arranca: un despliegue que pidio Flow y lo tiene mal escrito
    /// tiene que fallar al tiro, no al primer cobro. Las claves van en Render y en user-secrets,
    /// nunca en el repositorio (es publico).
    /// </summary>
    private static IPasarelaDePagos CrearFlow(IServiceProvider sp)
    {
        var flow = sp.GetRequiredService<IOptions<OpcionesZass>>().Value.Flow;
        var faltan = new List<string>();
        if (string.IsNullOrWhiteSpace(flow?.ApiKey)) faltan.Add("Zass:Flow:ApiKey");
        if (string.IsNullOrWhiteSpace(flow?.SecretKey)) faltan.Add("Zass:Flow:SecretKey");
        if (string.IsNullOrWhiteSpace(flow?.UrlAvisos)) faltan.Add("Zass:Flow:UrlAvisos");
        if (faltan.Count > 0)
            throw new OptionsValidationException(
                OpcionesZass.Seccion, typeof(OpcionesZass),
                new[] { $"{ClavePasarela} dice 'flow' y falta: {string.Join(", ", faltan)}." });

        return sp.GetRequiredService<FlowPasarela>();
    }

    /// <summary>
    /// MOD-08. Quien guarda los archivos, segun <c>Zass:Archivos:Proveedor</c>:
    /// <list type="bullet">
    /// <item>"r2": R2 de Cloudflare. Si falta alguna de sus cuatro claves, la API NO arranca: un
    /// despliegue que pidio R2 y lo tiene mal escrito tiene que fallar al tiro, no al primer subir.</item>
    /// <item>"local": una carpeta. En Produccion no se acepta (Render borra el disco en cada
    /// despliegue, y los archivos se perderian sin aviso).</item>
    /// <item>vacio: la carpeta local fuera de Produccion; en Produccion, deshabilitado (la API
    /// arranca y subir responde 503 hasta que Pablo ponga R2).</item>
    /// </list>
    /// </summary>
    private static IAlmacenDeArchivos ElegirAlmacen(IServiceProvider sp)
    {
        var opciones = sp.GetRequiredService<IConfiguration>()
            .GetSection(OpcionesArchivos.Seccion).Get<OpcionesArchivos>() ?? new OpcionesArchivos();
        var entorno = sp.GetRequiredService<IHostEnvironment>();
        var proveedor = opciones.Proveedor?.Trim().ToLowerInvariant() ?? "";

        static OptionsValidationException Falla(string mensaje) =>
            new(OpcionesArchivos.Seccion, typeof(OpcionesArchivos), new[] { mensaje });

        switch (proveedor)
        {
            case "r2":
                if (string.IsNullOrWhiteSpace(opciones.R2CuentaId) || string.IsNullOrWhiteSpace(opciones.R2ClaveAcceso)
                    || string.IsNullOrWhiteSpace(opciones.R2Secreto) || string.IsNullOrWhiteSpace(opciones.R2Balde))
                    throw Falla("Zass:Archivos:Proveedor dice 'r2' y falta R2CuentaId, R2ClaveAcceso, R2Secreto o R2Balde.");
                return new AlmacenR2(opciones, sp.GetRequiredService<ILogger<AlmacenR2>>());

            case "local":
                if (entorno.IsProduction())
                    throw Falla("Zass:Archivos:Proveedor 'local' no se permite en Produccion: el disco de Render se borra en cada despliegue.");
                return new AlmacenLocal(CarpetaLocal(opciones));

            case "":
                return entorno.IsProduction()
                    ? new AlmacenDeshabilitado()
                    : new AlmacenLocal(CarpetaLocal(opciones));

            default:
                throw Falla($"Zass:Archivos:Proveedor dice '{opciones.Proveedor}' y no hay almacen con ese nombre.");
        }
    }

    private static string CarpetaLocal(OpcionesArchivos o) =>
        string.IsNullOrWhiteSpace(o.CarpetaLocal)
            ? Path.Combine(Path.GetTempPath(), "zass-archivos")
            : o.CarpetaLocal;
}
