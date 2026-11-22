using Microsoft.Extensions.Options;

namespace Zass.Infraestructura.Push;

/// <summary>
/// Seccion <c>Zass:Push</c> (ES-3). Las claves VAPID son SECRETOS: vienen solo del entorno
/// (<c>Zass__Push__ClavePrivada</c> en Render, User Secrets en desarrollo), nunca del repositorio.
/// Sin claves el push queda apagado: no se envia nada, API-45 sigue aceptando registros y la
/// clave publica responde 404 para que la web esconda el boton.
/// </summary>
public sealed class OpcionesPush
{
    public const string Seccion = "Zass:Push";

    /// <summary>Clave publica VAPID (P-256 sin comprimir, base64url). No es secreta: la pide el navegador.</summary>
    public string ClavePublica { get; set; } = "";
    /// <summary>Clave privada VAPID (base64url). SECRETO.</summary>
    public string ClavePrivada { get; set; } = "";
    /// <summary>Contacto para los servicios de push: «mailto:…» o una URL https.</summary>
    public string Sujeto { get; set; } = "";

    public int MaximoIntentos { get; set; } = 4;
    public int EsperaBaseSegundos { get; set; } = 30;
    public int EsperaMaximaSegundos { get; set; } = 900;
    public int TamanoDeLote { get; set; } = 20;
    public int SondeoSegundos { get; set; } = 10;
    public int TiempoLimiteHttpSegundos { get; set; } = 10;
    public int ReservaSegundos { get; set; } = 120;
    /// <summary>
    /// Un push que no salio en este plazo ya no avisa de nada: se anula. Mas corto que el del correo
    /// (7 dias) porque el push es para lo inmediato; el correo sigue saliendo por su lado.
    /// </summary>
    public int CaducaHoras { get; set; } = 24;

    public bool Activo => !string.IsNullOrWhiteSpace(ClavePublica) && !string.IsNullOrWhiteSpace(ClavePrivada);
}

/// <summary>
/// El arranque se niega con media configuracion: una clave sin la otra, o sin contacto. Mejor
/// caerse al desplegar que descubrir que ningun push salio.
/// </summary>
public sealed class ValidadorOpcionesPush : IValidateOptions<OpcionesPush>
{
    public ValidateOptionsResult Validate(string? name, OpcionesPush o)
    {
        var publica = !string.IsNullOrWhiteSpace(o.ClavePublica);
        var privada = !string.IsNullOrWhiteSpace(o.ClavePrivada);
        if (!publica && !privada) return ValidateOptionsResult.Success;

        var fallos = new List<string>();
        if (publica != privada)
            fallos.Add("Push a medias: Zass:Push necesita ClavePublica y ClavePrivada juntas (o ninguna).");
        if (!(o.Sujeto.StartsWith("mailto:", StringComparison.Ordinal)
              || o.Sujeto.StartsWith("https://", StringComparison.Ordinal)))
            fallos.Add("Falta Zass:Push:Sujeto, el contacto para los servicios de push (mailto:… o https://…).");
        if (o.MaximoIntentos < 1) fallos.Add("Zass:Push:MaximoIntentos debe ser al menos 1.");
        if (o.TamanoDeLote < 1) fallos.Add("Zass:Push:TamanoDeLote debe ser al menos 1.");
        return fallos.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(fallos);
    }
}
