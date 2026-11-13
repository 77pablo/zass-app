using System.Text.Json.Serialization;
using FluentValidation;
using Zass.Api;
using Zass.Api.Configuracion;
using Zass.Api.Endpoints;
using Zass.Infraestructura.Configuracion;

var constructor = WebApplication.CreateBuilder(args);

// Las cookies de sesion llevan la marca Secure SIEMPRE, tambien en desarrollo: el
// entorno local tambien va por HTTPS (https://localhost:7277), con el certificado de
// `dotnet dev-certs`. Antes se apagaba en desarrollo y eso dejaba un hueco feo: la
// unica forma de probar que la cookie no viaja en claro era desplegar.
//
// Se puede apagar con Zass:ExigirHttps=false, y solo para eso: levantar en HTTP
// pelado cuando no haya certificado. Con la marca puesta y sin HTTPS, el navegador
// descarta la cookie y el fallo se ve como "no me deja entrar".
var exigirHttps = constructor.Configuration.GetValue("Zass:ExigirHttps", true);

constructor.Services
    .AgregarZass(constructor.Configuration)           // puertos, repositorios, MOD-01
    .AgregarLimiteDeTasa(constructor.Configuration)                            // los 4 grupos P, A, G, S (+ Salud)
    .AgregarProxiesDeConfianza()                      // X-Forwarded-*: solo desde redes listadas
    .AgregarAutenticacion(exigirHttps)                // cookie de 15 min + CSRF
    .AddValidatorsFromAssemblyContaining<ValidadorVerificarCorreo>()
    .AddProblemDetails()                              // RFC 7807 en todos los errores
    .AddEndpointsApiExplorer()
    .AddSwaggerGen();

// Los enums viajan como TEXTO, no como numero.
//
// Con el serializador por omision, EstadoPerfil.Publicado sale como `"estado": 1`, y la
// pantalla tendria que mantener una tabla de numeros que se desalinea en silencio el dia
// que alguien reordene el enum o inserte un valor en medio. La base ya los guarda como
// texto (HasConversion<string> en el contexto), asi que esto hace coincidir las dos
// puntas. Lo encontro la prueba del camino completo de CU-03 al leer "estado".
constructor.Services.ConfigureHttpJsonOptions(opciones =>
    opciones.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

// Un cuerpo que no se puede leer (JSON roto, un valor de enum que no existe) es un 400.
//
// ASP.NET lo hace asi en produccion, pero en DESARROLLO lanza la excepcion en vez de
// responder, y el manejador global la vuelve un 500. Las pruebas corren en Development, asi
// que ahi un cuerpo ilegible daba 500 mientras en produccion daria 400: dos aplicaciones
// distintas. Lo encontro la prueba de "tipo de credencial que no existe". Se fija aqui para que
// el comportamiento sea el mismo en todos los entornos.
constructor.Services.Configure<Microsoft.AspNetCore.Routing.RouteHandlerOptions>(
    opciones => opciones.ThrowOnBadRequest = false);

var app = constructor.Build();

// Se valida DESPUES de Build y con app.Configuration: ahi ya estan todas las fuentes
// (variables de entorno, Key Vault, y los ajustes que inyectan las pruebas). Falla antes de
// abrir ningun puerto. Todo lo que no sea Development se trata como produccion.
ValidacionDeArranque.Exigir(app.Configuration, esProductivo: !app.Environment.IsDevelopment());

// `dotnet Zass.Api.dll --migrar`: aplica migraciones y termina (ver Migrador).
if (Migrador.Solicitado(args))
    return await Migrador.AplicarAsync(app.Services, app.Logger);

// Lo PRIMERO: el HSTS, la redireccion a HTTPS y el limitador de tasa tienen que ver ya la IP
// y el esquema reales del visitante. Sin proxies configurados no hace nada.
app.UsarProxiesDeConfianza(app.Configuration);

// El orden importa: las cabeceras primero, para que acompanen incluso a una respuesta
// de error o de limite de tasa.
app.UsarCabecerasDeSeguridad();
app.UseExceptionHandler();
app.UseStatusCodePages();

if (exigirHttps)
{
    // HSTS le dice al navegador que no vuelva a intentar en claro. En desarrollo
    // tambien: asi el comportamiento es el mismo que vera un usuario.
    app.UseHsts();
    app.UseHttpsRedirection();
}

app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.MapearIdentidad();
app.MapearCatalogo();
app.MapearGeografia();
app.MapearBusqueda();
app.MapearMapaDelSitio();
app.MapearCredenciales();
app.MapearSalud();                                    // /api/salud/lista (readiness)

app.MapearAgenda();                                    // ANCLAJE MOD-06 Agenda

app.MapearResenas();

app.MapearVerificacion();

app.MapearTarjetas();                                  // API-54 /t/{segmento} (CS-18)

// Sonda de vida. Sin sesion y con el limite publico: tambien es un endpoint.
app.MapGet("/api/salud", () => Results.Ok(new { estado = "vivo" }))
   .RequireRateLimiting(Tasa.Publico);

app.Run();
return 0;

/// <summary>
/// Publico para que las pruebas puedan levantar la aplicacion entera con
/// WebApplicationFactory y pedirle por HTTP de verdad.
/// </summary>
public partial class Program;
