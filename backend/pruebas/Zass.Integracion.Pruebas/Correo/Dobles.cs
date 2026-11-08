using System.Net;
using Microsoft.Extensions.Logging;
using Zass.Aplicacion.Puertos;

namespace Zass.Integracion.Pruebas.Correo;

/// <summary>Una petición capturada. El cuerpo se lee en el momento: después el contenido ya se desechó.</summary>
public sealed record PeticionCapturada(HttpMethod Metodo, Uri Url, Dictionary<string, string> Cabeceras, string Cuerpo);

/// <summary>
/// El HttpMessageHandler falso: ninguna prueba de correo toca la red. Responde lo que le
/// diga el guion y guarda cada petición para poder inspeccionarla.
/// </summary>
public sealed class ManejadorFalso : HttpMessageHandler
{
    private readonly object _candado = new();
    private readonly Func<PeticionCapturada, int, Task<HttpResponseMessage>> _guion;
    private readonly List<PeticionCapturada> _peticiones = new();

    public ManejadorFalso(Func<PeticionCapturada, int, Task<HttpResponseMessage>> guion) => _guion = guion;

    public static ManejadorFalso Responde(HttpStatusCode codigo, int demoraMs = 0) =>
        new(async (_, _) =>
        {
            if (demoraMs > 0) await Task.Delay(demoraMs);
            return new HttpResponseMessage(codigo) { Content = new StringContent("{\"message\":\"x\"}") };
        });

    public List<PeticionCapturada> Peticiones { get { lock (_candado) return _peticiones.ToList(); } }
    public int Cuenta { get { lock (_candado) return _peticiones.Count; } }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage req, CancellationToken ct)
    {
        var cabeceras = req.Headers.ToDictionary(h => h.Key, h => string.Join(",", h.Value), StringComparer.OrdinalIgnoreCase);
        var cuerpo = req.Content is null ? "" : await req.Content.ReadAsStringAsync(ct);
        var captura = new PeticionCapturada(req.Method, req.RequestUri!, cabeceras, cuerpo);
        int n;
        lock (_candado) { _peticiones.Add(captura); n = _peticiones.Count; }
        return await _guion(captura, n);
    }
}

public sealed class RelojFalso : IRelojSistema
{
    public DateTime Ahora { get; set; } = DateTime.UtcNow;
    public void Avanzar(TimeSpan t) => Ahora += t;
}

/// <summary>Captura cada mensaje YA formateado, para comprobar que no se filtra nada.</summary>
public sealed class RegistroEnMemoria<T> : ILogger<T>
{
    private readonly object _candado = new();
    public List<string> Mensajes { get; } = new();

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel nivel, EventId id, TState estado, Exception? ex,
        Func<TState, Exception?, string> formateador)
    {
        var texto = formateador(estado, ex);
        if (ex is not null) texto += " | " + ex;   // también el mensaje de la excepción, si la hubiera
        lock (_candado) Mensajes.Add($"[{nivel}] {texto}");
    }

    public string Todo { get { lock (_candado) return string.Join("\n", Mensajes); } }
}
