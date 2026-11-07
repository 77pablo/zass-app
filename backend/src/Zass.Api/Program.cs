using FluentValidation;
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
    .AgregarLimiteDeTasa(constructor.Configuration)                            // los 4 grupos P, A, G, S
    .AgregarAutenticacion(exigirHttps)                // cookie de 15 min + CSRF
    .AddValidatorsFromAssemblyContaining<ValidadorVerificarCorreo>()
    .AddProblemDetails()                              // RFC 7807 en todos los errores
    .AddEndpointsApiExplorer()
    .AddSwaggerGen();

var app = constructor.Build();

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

// Sonda de vida. Sin sesion y con el limite publico: tambien es un endpoint.
app.MapGet("/api/salud", () => Results.Ok(new { estado = "vivo" }))
   .RequireRateLimiting(Tasa.Publico);

app.Run();

/// <summary>
/// Publico para que las pruebas puedan levantar la aplicacion entera con
/// WebApplicationFactory y pedirle por HTTP de verdad.
/// </summary>
public partial class Program;
