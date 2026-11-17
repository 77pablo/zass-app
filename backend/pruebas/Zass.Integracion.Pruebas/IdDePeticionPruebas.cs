using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Zass.Api.Configuracion;

namespace Zass.Integracion.Pruebas;

/// <summary>
/// El identificador de peticion: que exista, que no se repita, que no lo dicte el cliente, y que
/// aparezca donde hace falta para buscar en el registro: la cabecera, el cuerpo del error y CADA
/// linea registrada durante la peticion.
///
/// El caso que importa es el 500: ahi UseExceptionHandler limpia la respuesta antes de escribir
/// el error, y una cabecera puesta al principio desaparece justo cuando mas se necesita.
/// </summary>
public class IdDePeticionPruebas
{
    // ------------------------------------------------------------- la tuberia sola, sin base

    private sealed class Tuberia : IAsyncDisposable
    {
        public required IHost Host { get; init; }
        public required RegistrosCapturados Registros { get; init; }
        public HttpClient Cliente() => Host.GetTestClient();
        public async ValueTask DisposeAsync() { await Host.StopAsync(); Host.Dispose(); }
    }

    /// <summary>La misma posicion que en Program.cs: antes del manejador de excepciones.</summary>
    private static async Task<Tuberia> Levantar(RequestDelegate final)
    {
        var registros = new RegistrosCapturados();
        var host = new HostBuilder()
            .ConfigureWebHost(w => w
                .UseTestServer()
                .ConfigureServices(s =>
                {
                    s.AddLogging(l => l.ClearProviders().AddProvider(registros));
                    s.AddProblemDetails(IdDePeticion.Configurar);
                })
                .Configure(app =>
                {
                    app.UsarIdDePeticion();
                    app.UseExceptionHandler();
                    app.Run(final);
                }))
            .Build();
        await host.StartAsync();
        return new Tuberia { Host = host, Registros = registros };
    }

    private static string IdDe(HttpResponseMessage r) =>
        Assert.Single(r.Headers.GetValues(IdDePeticion.Cabecera));

    [Fact]
    public async Task Cada_respuesta_lleva_un_identificador_y_no_se_repite()
    {
        await using var t = await Levantar(c => c.Response.WriteAsync("hola"));
        var cliente = t.Cliente();

        var uno = IdDe(await cliente.GetAsync("/"));
        var dos = IdDe(await cliente.GetAsync("/"));

        Assert.Matches("^[0-9a-f]{16}$", uno);
        Assert.Matches("^[0-9a-f]{16}$", dos);
        Assert.NotEqual(uno, dos);
    }

    [Fact]
    public async Task El_identificador_que_manda_el_cliente_se_ignora()
    {
        await using var t = await Levantar(c => c.Response.WriteAsync("hola"));
        var pedido = new HttpRequestMessage(HttpMethod.Get, "/");
        pedido.Headers.Add(IdDePeticion.Cabecera, "falsificado-por-el-cliente");

        var id = IdDe(await t.Cliente().SendAsync(pedido));

        Assert.NotEqual("falsificado-por-el-cliente", id);
        Assert.Matches("^[0-9a-f]{16}$", id);
    }

    [Fact]
    public async Task Lo_que_se_registra_durante_la_peticion_lleva_su_identificador()
    {
        await using var t = await Levantar(c =>
        {
            c.RequestServices.GetRequiredService<ILoggerFactory>()
                .CreateLogger("Prueba").LogInformation("linea de la prueba");
            return Task.CompletedTask;
        });

        var id = IdDe(await t.Cliente().GetAsync("/"));

        var linea = Assert.Single(t.Registros.Lineas, l => l.Mensaje == "linea de la prueba");
        Assert.Equal(id, linea.IdPeticion);
        // Y como lo escribe la consola de Render, que es donde se va a buscar.
        Assert.Contains($"{IdDePeticion.ClaveDelRegistro}:{id}", linea.AmbitosComoTexto);
    }

    [Fact]
    public async Task Un_500_lleva_el_identificador_en_cabecera_cuerpo_y_en_la_linea_del_error()
    {
        await using var t = await Levantar(_ => throw new InvalidOperationException("explota"));

        var r = await t.Cliente().GetAsync("/");

        Assert.Equal(HttpStatusCode.InternalServerError, r.StatusCode);

        // La cabecera sobrevivio a la limpieza que hace el manejador de excepciones.
        var id = IdDe(r);

        // El cuerpo dice el MISMO, y no trae el traceId de ASP.NET, que seria otro valor.
        using var cuerpo = JsonDocument.Parse(await r.Content.ReadAsStringAsync());
        Assert.Equal(id, cuerpo.RootElement.GetProperty(IdDePeticion.ClaveDelProblema).GetString());
        Assert.False(cuerpo.RootElement.TryGetProperty("traceId", out _),
            "El error trae dos identificadores distintos: idPeticion y traceId.");

        // Y la linea que escribe el manejador (la que se busca en Render) tambien lo lleva.
        var error = Assert.Single(t.Registros.Lineas, l => l.Excepcion is InvalidOperationException);
        Assert.Equal(id, error.IdPeticion);
        Assert.Contains($"{IdDePeticion.ClaveDelRegistro}:{id}", error.AmbitosComoTexto);
    }

    /// <summary>
    /// ASP.NET pone su propio <c>traceId</c> en el error solo cuando hay una ACTIVIDAD de
    /// diagnostico en curso, que es lo que pasa en cuanto alguien enciende telemetria. Sin el
    /// escucha de abajo no la hay, y quitar el <c>Remove("traceId")</c> no lo veria nadie.
    /// </summary>
    [Fact]
    public async Task Con_telemetria_encendida_el_error_sigue_trayendo_un_solo_identificador()
    {
        using var escucha = new System.Diagnostics.ActivityListener
        {
            ShouldListenTo = _ => true,
            Sample = (ref System.Diagnostics.ActivityCreationOptions<System.Diagnostics.ActivityContext> _) =>
                System.Diagnostics.ActivitySamplingResult.AllData,
        };
        System.Diagnostics.ActivitySource.AddActivityListener(escucha);
        await using var t = await Levantar(_ => throw new InvalidOperationException("explota"));

        var r = await t.Cliente().GetAsync("/");

        var id = IdDe(r);
        using var cuerpo = JsonDocument.Parse(await r.Content.ReadAsStringAsync());
        Assert.Equal(id, cuerpo.RootElement.GetProperty(IdDePeticion.ClaveDelProblema).GetString());
        Assert.False(cuerpo.RootElement.TryGetProperty("traceId", out _),
            "Con una actividad en curso, el error trae dos identificadores distintos: idPeticion y traceId.");
    }

    // ------------------------------------------------------------- la API entera

    [Fact]
    public async Task En_la_API_real_un_404_trae_el_mismo_identificador_en_cabecera_y_cuerpo()
    {
        await using var app = new AplicacionConProxy();
        var cliente = app.CreateClient();

        // Respuestas.Problema (Results.Problem): la ficha que no existe.
        var r = await cliente.GetAsync("/api/prestadores/este-slug-no-existe-para-nadie");

        Assert.Equal(HttpStatusCode.NotFound, r.StatusCode);
        var id = IdDe(r);
        using var cuerpo = JsonDocument.Parse(await r.Content.ReadAsStringAsync());
        Assert.Equal(id, cuerpo.RootElement.GetProperty(IdDePeticion.ClaveDelProblema).GetString());
        Assert.False(cuerpo.RootElement.TryGetProperty("traceId", out _));
    }

    /// <summary>
    /// El 429 lo escribe el limitador a mano, sin ProblemDetails: si nadie lo pone ahi, es el
    /// unico error sin identificador en el cuerpo.
    /// </summary>
    [Fact]
    public async Task El_429_del_limitador_tambien_trae_el_identificador_en_el_cuerpo()
    {
        await using var app = new AplicacionConProxy(limitePublico: 1);
        var cliente = app.CreateClient();

        await cliente.GetAsync("/api/salud");
        var r = await cliente.GetAsync("/api/salud");

        Assert.Equal(HttpStatusCode.TooManyRequests, r.StatusCode);
        var id = IdDe(r);
        using var cuerpo = JsonDocument.Parse(await r.Content.ReadAsStringAsync());
        Assert.Equal(id, cuerpo.RootElement.GetProperty(IdDePeticion.ClaveDelProblema).GetString());
    }

    // ------------------------------------------------------------- un registro que se puede leer

    /// <summary>
    /// Guarda cada linea con los ambitos activos al escribirla, de las DOS formas en que se leen:
    /// como pares clave/valor (lo que lee un formateador JSON) y como texto, que es lo que escribe
    /// la consola simple de Render: «=> » mas el ToString() de cada ambito. Se mira el texto porque
    /// fue ahi donde el identificador no aparecia: un diccionario se imprimia como el nombre de su
    /// tipo, y una prueba que solo leia los pares pasaba en verde igual.
    /// </summary>
    private sealed class RegistrosCapturados : ILoggerProvider, ISupportExternalScope
    {
        private IExternalScopeProvider _ambitos = new LoggerExternalScopeProvider();
        public ConcurrentQueue<Linea> Lineas { get; } = new();

        public void SetScopeProvider(IExternalScopeProvider ambitos) => _ambitos = ambitos;
        public ILogger CreateLogger(string categoria) => new Registrador(this);
        public void Dispose() { }

        public sealed record Linea(string Mensaje, Exception? Excepcion, string? IdPeticion, string AmbitosComoTexto);

        private sealed class Registrador(RegistrosCapturados destino) : ILogger
        {
            public IDisposable? BeginScope<TEstado>(TEstado estado) where TEstado : notnull =>
                destino._ambitos.Push(estado);

            public bool IsEnabled(LogLevel nivel) => true;

            public void Log<TEstado>(LogLevel nivel, EventId evento, TEstado estado,
                Exception? excepcion, Func<TEstado, Exception?, string> formato)
            {
                string? id = null;
                var texto = new System.Text.StringBuilder();
                destino._ambitos.ForEachScope((ambito, _) =>
                {
                    texto.Append(" => ").Append(ambito);
                    if (ambito is IEnumerable<KeyValuePair<string, object>> pares)
                        foreach (var (clave, valor) in pares)
                            if (clave == IdDePeticion.ClaveDelRegistro) id = valor?.ToString();
                }, (object?)null);
                destino.Lineas.Enqueue(new Linea(formato(estado, excepcion), excepcion, id, texto.ToString()));
            }
        }
    }
}
