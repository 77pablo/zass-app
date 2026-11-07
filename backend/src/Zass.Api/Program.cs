using FluentValidation;
using Zass.Api.Configuracion;
using Zass.Api.Endpoints;
using Zass.Infraestructura.Configuracion;

var constructor = WebApplication.CreateBuilder(args);

// En desarrollo las cookies pueden viajar por http; en cualquier otro ambiente, no.
var exigirHttps = !constructor.Environment.IsDevelopment();

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

if (exigirHttps) app.UseHsts();

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
