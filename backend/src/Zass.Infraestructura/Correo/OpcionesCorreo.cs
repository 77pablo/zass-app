using Microsoft.Extensions.Options;

namespace Zass.Infraestructura.Correo;

/// <summary>Qué proveedor entrega el correo. Ninguno = desarrollo: los avisos quedan pendientes.</summary>
public enum ProveedorDeCorreo { Ninguno, Brevo, Resend }

/// <summary>
/// Sección <c>Zass:Correo</c>. La clave de API viene SOLO de la configuración del entorno
/// (variable <c>Zass__Correo__ApiKey</c> en Render), nunca del código ni del repositorio.
/// </summary>
public sealed class OpcionesCorreo
{
    public const string Seccion = "Zass:Correo";

    public ProveedorDeCorreo Proveedor { get; set; } = ProveedorDeCorreo.Ninguno;
    public string ApiKey { get; set; } = "";
    public string Remitente { get; set; } = "";
    public string NombreRemitente { get; set; } = "ZASS!";

    /// <summary>Intentos totales antes de dejar el aviso en «fallida» sin reintentar.</summary>
    public int MaximoIntentos { get; set; } = 5;
    /// <summary>Espera tras el primer fallo; se duplica en cada intento siguiente.</summary>
    public int EsperaBaseSegundos { get; set; } = 60;
    public int EsperaMaximaSegundos { get; set; } = 3600;
    public int TamanoDeLote { get; set; } = 10;
    public int SondeoSegundos { get; set; } = 15;
    public int TiempoLimiteHttpSegundos { get; set; } = 15;
    /// <summary>Cuánto se espera, tras fallar para siempre, antes de purgar la carga del aviso.</summary>
    public int PurgaDeFallidasHoras { get; set; } = 24;
    /// <summary>Plazo que un trabajador se reserva un aviso; si muere, otro lo retoma pasado este tiempo.</summary>
    public int ReservaSegundos { get; set; } = 120;
    /// <summary>
    /// Cuanto puede tardar un aviso en salir antes de que deje de tener sentido mandarlo. Pasado
    /// ese plazo se ANULA en vez de enviarse.
    ///
    /// No es una optimizacion: encolar y enviar estan separados a proposito, y un tipo sin
    /// plantilla se queda en la cola hasta que alguien la escriba. El dia que esa plantilla
    /// aparece, TODO lo acumulado se vuelve enviable de golpe, y la gente recibiria avisos de algo
    /// que hizo hace semanas. Paso aqui con PerfilPublicado: estuvo encolandose sin plantilla desde
    /// el primer dia. Siete dias es el plazo: un correo transaccional que llega mas tarde que eso
    /// ya no informa de nada, confunde.
    /// </summary>
    public int CaducaDias { get; set; } = 7;
}

/// <summary>
/// El arranque se niega si hay proveedor pero falta lo necesario para usarlo. Mejor caerse
/// al desplegar, con un mensaje claro, que descubrir semanas después que nadie recibió
/// su correo de verificación.
/// </summary>
public sealed class ValidadorOpcionesCorreo : IValidateOptions<OpcionesCorreo>
{
    public ValidateOptionsResult Validate(string? name, OpcionesCorreo o)
    {
        if (o.Proveedor == ProveedorDeCorreo.Ninguno) return ValidateOptionsResult.Success;

        var fallos = new List<string>();
        if (string.IsNullOrWhiteSpace(o.ApiKey))
            fallos.Add($"Falta la clave de API del correo: el proveedor es {o.Proveedor} y Zass:Correo:ApiKey "
                     + "está vacía. Defínela como variable de entorno Zass__Correo__ApiKey.");
        if (string.IsNullOrWhiteSpace(o.Remitente) || !o.Remitente.Contains('@'))
            fallos.Add("Falta el remitente del correo (Zass:Correo:Remitente), con una dirección válida "
                     + "de un dominio verificado en el proveedor.");
        if (o.MaximoIntentos < 1) fallos.Add("Zass:Correo:MaximoIntentos debe ser al menos 1.");
        if (o.TamanoDeLote < 1) fallos.Add("Zass:Correo:TamanoDeLote debe ser al menos 1.");
        return fallos.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(fallos);
    }
}
