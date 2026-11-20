using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Zass.Infraestructura.Configuracion;

namespace Zass.Integracion.Pruebas;

/// <summary>
/// Q6 - Pruebas del validador de OpcionesZass para CorreoAlertasAdmin.
/// En Production, si falta o es invalida -> la API NO arranca.
/// Fuera de Production -> LogWarning y el aviso simplemente no se envia.
/// </summary>
public class ValidadorOpcionesZassPruebas
{
    [Fact]
    public void Q6_Production_sin_CorreoAlertasAdmin_Fail_con_mensaje_Falta_Zass_CorreoAlertasAdmin()
    {
        var entorno = new EntornoFalso("Production");
        var registro = new RegistroFalso<ValidadorOpcionesPagos>();
        var validador = new ValidadorOpcionesPagos(entorno, registro);

        var opciones = new OpcionesZass
        {
            CadenaConexion = "Host=127.0.0.1;Database=zass;Username=zass;Password=x",
            ClaveDireccionesBase64 = Convert.ToBase64String(Enumerable.Range(0, 32).Select(i => (byte)i).ToArray()),
            SecretoAvisoPagos = new string('x', 32),
            // CorreoAlertasAdmin = null (falta)
        };

        var resultado = validador.Validate(null, opciones);

        Assert.False(resultado.Succeeded);
        Assert.Contains(resultado.Failures, f => f.Contains("Falta Zass:CorreoAlertasAdmin"));
    }

    [Fact]
    public void Q6_Production_con_CorreoAlertasAdmin_invalido_Fail()
    {
        var entorno = new EntornoFalso("Production");
        var registro = new RegistroFalso<ValidadorOpcionesPagos>();
        var validador = new ValidadorOpcionesPagos(entorno, registro);

        var opciones = new OpcionesZass
        {
            CadenaConexion = "Host=127.0.0.1;Database=zass;Username=zass;Password=x",
            ClaveDireccionesBase64 = Convert.ToBase64String(Enumerable.Range(0, 32).Select(i => (byte)i).ToArray()),
            SecretoAvisoPagos = new string('x', 32),
            CorreoAlertasAdmin = "no-es-un-correo", // invalido
        };

        var resultado = validador.Validate(null, opciones);

        Assert.False(resultado.Succeeded);
        Assert.Contains(resultado.Failures, f => f.Contains("CorreoAlertasAdmin") && f.Contains("valido"));
    }

    [Fact]
    public void Q6_Production_con_CorreoAlertasAdmin_valido_Success()
    {
        var entorno = new EntornoFalso("Production");
        var registro = new RegistroFalso<ValidadorOpcionesPagos>();
        var validador = new ValidadorOpcionesPagos(entorno, registro);

        var opciones = new OpcionesZass
        {
            CadenaConexion = "Host=127.0.0.1;Database=zass;Username=zass;Password=x",
            ClaveDireccionesBase64 = Convert.ToBase64String(Enumerable.Range(0, 32).Select(i => (byte)i).ToArray()),
            SecretoAvisoPagos = new string('x', 32),
            CorreoAlertasAdmin = "alertas@ejemplo.test", // valido
        };

        var resultado = validador.Validate(null, opciones);

        Assert.True(resultado.Succeeded);
    }

    [Fact]
    public void Q6_Development_sin_CorreoAlertasAdmin_Success_con_LogWarning()
    {
        var entorno = new EntornoFalso("Development");
        var registro = new RegistroFalso<ValidadorOpcionesPagos>();
        var validador = new ValidadorOpcionesPagos(entorno, registro);

        var opciones = new OpcionesZass
        {
            CadenaConexion = "Host=127.0.0.1;Database=zass;Username=zass;Password=x",
            ClaveDireccionesBase64 = Convert.ToBase64String(Enumerable.Range(0, 32).Select(i => (byte)i).ToArray()),
            SecretoAvisoPagos = new string('x', 32),
            // CorreoAlertasAdmin = null (falta)
        };

        var resultado = validador.Validate(null, opciones);

        Assert.True(resultado.Succeeded);
        // Debe haber un LogWarning
        Assert.Contains(registro.Mensajes, m => m.Contains("CorreoAlertasAdmin") && m.Contains("Warning"));
    }

    [Fact]
    public void Q6_Development_con_CorreoAlertasAdmin_valido_Success_sin_LogWarning()
    {
        var entorno = new EntornoFalso("Development");
        var registro = new RegistroFalso<ValidadorOpcionesPagos>();
        var validador = new ValidadorOpcionesPagos(entorno, registro);

        var opciones = new OpcionesZass
        {
            CadenaConexion = "Host=127.0.0.1;Database=zass;Username=zass;Password=x",
            ClaveDireccionesBase64 = Convert.ToBase64String(Enumerable.Range(0, 32).Select(i => (byte)i).ToArray()),
            SecretoAvisoPagos = new string('x', 32),
            CorreoAlertasAdmin = "alertas@ejemplo.test",
        };

        var resultado = validador.Validate(null, opciones);

        Assert.True(resultado.Succeeded);
        // No debe haber LogWarning sobre CorreoAlertasAdmin
        Assert.DoesNotContain(registro.Mensajes, m => m.Contains("CorreoAlertasAdmin") && m.Contains("Warning"));
    }

    /// <summary>Doble de IHostEnvironment para las pruebas.</summary>
    private sealed class EntornoFalso : IHostEnvironment
    {
        private readonly string _entorno;
        public EntornoFalso(string entorno) => _entorno = entorno;
        public string ApplicationName { get; set; } = "Zass.Api";
        public string EnvironmentName => _entorno;
        public string ContentRootPath { get; set; } = "";
        public bool IsDevelopment() => _entorno == Environments.Development;
        public bool IsProduction() => _entorno == Environments.Production;
        public bool IsEnvironment(string environmentName) => _entorno == environmentName;
        public bool IsStaging() => _entorno == Environments.Staging;
    }

    /// <summary>Doble de ILogger para capturar mensajes.</summary>
    private sealed class RegistroFalso<T> : ILogger<T>
    {
        private readonly object _candado = new();
        public List<string> Mensajes { get; } = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel nivel, EventId id, TState estado, Exception? ex,
            Func<TState, Exception?, string> formateador)
        {
            var texto = formateador(estado, ex);
            if (ex is not null) texto += " | " + ex;
            lock (_candado) Mensajes.Add($"[{nivel}] {texto}");
        }
    }
}
