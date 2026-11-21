using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Zass.Aplicacion.Puertos;
using Zass.Dominio.Comun;
using Zass.Infraestructura.Configuracion;

namespace Zass.Infraestructura.Correo;

/// <summary>
/// Implementacion de IAlertasAdministrador que envia un correo al administrador.
/// Usa la zona horaria de Chile para formatear las horas.
/// </summary>
public sealed class AlertasAdministrador : IAlertasAdministrador
{
    private readonly IEnviadorDeCorreo _enviador;
    private readonly IOptions<OpcionesZass> _opciones;
    private readonly ILogger<AlertasAdministrador> _log;

    public AlertasAdministrador(
        IEnviadorDeCorreo enviador,
        IOptions<OpcionesZass> opciones,
        ILogger<AlertasAdministrador> log)
    {
        _enviador = enviador;
        _opciones = opciones;
        _log = log;
    }

    public async Task AvisarTarjetaBloqueadaAsync(AlertaTarjetaBloqueada alerta, CancellationToken ct = default)
    {
        var correoAdmin = _opciones.Value.CorreoAlertasAdmin;
        if (string.IsNullOrWhiteSpace(correoAdmin))
        {
            _log.LogWarning("Zass:CorreoAlertasAdmin no configurado; no se envio la alerta de tarjeta bloqueada.");
            return;
        }

        // Formatear horas en hora de Chile
        var quintoFalloChile = HoraDeChile.ALocal(alerta.QuintoFalloUtc);
        var finBloqueoChile = HoraDeChile.ALocal(alerta.BloqueadaHastaUtc);

        var asunto = "ZASS!: tarjeta bloqueada por intentos fallidos";
        var texto = $"""
            Hola:

            Una tarjeta de prestador fue bloqueada automaticamente tras 5 intentos fallidos de activacion en 15 minutos.

            URL estable de la tarjeta: {alerta.UrlEstable}
            Hora del 5.o fallo (hora de Chile): {quintoFalloChile:dd'/'MM'/'yyyy HH:mm:ss}
            Hora de fin del bloqueo (hora de Chile): {finBloqueoChile:dd'/'MM'/'yyyy HH:mm:ss}

            El bloqueo dura 1 hora desde el 5.o fallo.

            — El equipo de ZASS!
            """;

        var html = $"""
            <!DOCTYPE html>
            <html lang="es"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1"><title>{asunto}</title></head>
            <body style="margin:0;padding:0;background:#ffffff;color:#1A1A1A;font-family:Arial,Helvetica,sans-serif;font-size:16px;line-height:1.5;">
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="background:#ffffff;"><tr><td align="center" style="padding:24px 16px;">
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="max-width:560px;">
            <tr><td style="background:#F6C81E;padding:16px 24px;font-size:24px;font-weight:bold;color:#1A1A1A;">ZASS!</td></tr>
            <tr><td style="padding:24px;border:1px solid #e5e5e5;border-top:0;">
            <h1 style="margin:0 0 16px;font-size:20px;color:#1A1A1A;">Tarjeta bloqueada por intentos fallidos</h1>
            <p style="margin:0 0 12px;">Hola:</p>
            <p style="margin:0 0 12px;">Una tarjeta de prestador fue bloqueada automaticamente tras 5 intentos fallidos de activacion en 15 minutos.</p>
            <ul style="margin:0 0 24px;padding-left:20px;">
                <li><strong>URL estable:</strong> {HtmlEncode(alerta.UrlEstable)}</li>
                <li><strong>Hora del 5.o fallo (hora de Chile):</strong> {quintoFalloChile:dd'/'MM'/'yyyy HH:mm:ss}</li>
                <li><strong>Hora de fin del bloqueo (hora de Chile):</strong> {finBloqueoChile:dd'/'MM'/'yyyy HH:mm:ss}</li>
            </ul>
            <p style="margin:0 0 12px;">El bloqueo dura 1 hora desde el 5.o fallo.</p>
            <p style="margin:0;font-size:14px;">— El equipo de ZASS!</p>
            </td></tr></table></td></tr></table></body></html>
            """;

        var correo = new CorreoSaliente(correoAdmin, "Administrador ZASS!", asunto, texto, html);

        try
        {
            var resultado = await _enviador.EnviarAsync(correo, ct);
            if (resultado.Resultado == ResultadoEnvio.Reintentable || resultado.Resultado == ResultadoEnvio.Rechazado)
            {
                _log.LogWarning("No se pudo enviar la alerta de tarjeta bloqueada ({Detalle}).", resultado.Detalle ?? "sin detalle");
            }
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Excepcion al enviar la alerta de tarjeta bloqueada.");
        }
    }

    private static string HtmlEncode(string s) => System.Net.WebUtility.HtmlEncode(s);
}
