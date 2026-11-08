using System.Net;
using Zass.Dominio.Comun;

namespace Zass.Infraestructura.Correo;

public sealed record CorreoRenderizado(string Asunto, string Texto, string Html);

/// <summary>
/// Las plantillas de correo transaccional. Texto plano y HTML simple: tablas y estilos en
/// línea porque los clientes de correo ignoran casi todo lo demás, y contraste alto
/// (tinta #1A1A1A sobre blanco; el amarillo #F6C81E solo como acento de marca y como fondo
/// del botón con texto oscuro, nunca como color de texto).
///
/// Solo reciben el nombre y el enlace del PROPIO destinatario: no hay forma de que
/// filtren datos de otra persona.
/// </summary>
public static class Plantillas
{
    private const string Amarillo = "#F6C81E";
    private const string Tinta = "#1A1A1A";

    public static string EnlaceDeVerificacion(string urlPublica, string token) =>
        $"{urlPublica.TrimEnd('/')}/verificar-correo?token={Uri.EscapeDataString(token)}";

    public static string EnlaceDeRecuperacion(string urlPublica, string token) =>
        $"{urlPublica.TrimEnd('/')}/restablecer-contrasena?token={Uri.EscapeDataString(token)}";

    public static bool TienePlantilla(TipoNotificacion tipo) =>
        tipo is TipoNotificacion.CorreoVerificacion or TipoNotificacion.RecuperarContrasena;

    /// <summary>Null si el tipo no tiene plantilla (se queda pendiente, no se pierde).</summary>
    public static CorreoRenderizado? Renderizar(
        TipoNotificacion tipo, string nombre, string urlPublica, string token) => tipo switch
    {
        TipoNotificacion.CorreoVerificacion => Verificacion(nombre, EnlaceDeVerificacion(urlPublica, token)),
        TipoNotificacion.RecuperarContrasena => Recuperacion(nombre, EnlaceDeRecuperacion(urlPublica, token)),
        _ => null,
    };

    // El asunto es fijo y NUNCA lleva el token ni el nombre: los asuntos se ven en
    // notificaciones de pantalla bloqueada y en registros intermedios.
    public static CorreoRenderizado Verificacion(string nombre, string enlace)
    {
        var texto =
            $"Hola {nombre}:\n\n" +
            "Gracias por crear tu cuenta en ZASS!. Para confirmar tu correo abre este enlace " +
            "(sirve por 24 horas):\n\n" +
            $"{enlace}\n\n" +
            "Si no creaste una cuenta en ZASS!, ignora este mensaje: no pasará nada.\n\n" +
            "— El equipo de ZASS!";
        var html = Marco(
            "Confirma tu correo",
            $"Hola {H(nombre)}:",
            "Gracias por crear tu cuenta en ZASS!. Para confirmar tu correo, usa el botón. El enlace sirve por 24 horas.",
            "Confirmar mi correo", enlace,
            "Si no creaste una cuenta en ZASS!, ignora este mensaje: no pasará nada.");
        return new("Confirma tu correo en ZASS!", texto, html);
    }

    public static CorreoRenderizado Recuperacion(string nombre, string enlace)
    {
        var texto =
            $"Hola {nombre}:\n\n" +
            "Recibimos una solicitud para crear una contraseña nueva en ZASS!. Abre este enlace " +
            "(sirve por 1 hora):\n\n" +
            $"{enlace}\n\n" +
            "Si no fuiste tú, ignora este mensaje: tu contraseña no ha cambiado.\n\n" +
            "— El equipo de ZASS!";
        var html = Marco(
            "Crea una contraseña nueva",
            $"Hola {H(nombre)}:",
            "Recibimos una solicitud para crear una contraseña nueva en ZASS!. Usa el botón; el enlace sirve por 1 hora.",
            "Crear contraseña nueva", enlace,
            "Si no fuiste tú, ignora este mensaje: tu contraseña no ha cambiado.");
        return new("Crea una contraseña nueva en ZASS!", texto, html);
    }

    private static string H(string s) => WebUtility.HtmlEncode(s);

    private static string Marco(string titulo, string saludo, string parrafo,
        string textoBoton, string enlace, string aviso)
    {
        var e = H(enlace);
        return $"""
<!DOCTYPE html>
<html lang="es"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1"><title>{H(titulo)}</title></head>
<body style="margin:0;padding:0;background:#ffffff;color:{Tinta};font-family:Arial,Helvetica,sans-serif;font-size:16px;line-height:1.5;">
<table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="background:#ffffff;"><tr><td align="center" style="padding:24px 16px;">
<table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="max-width:560px;">
<tr><td style="background:{Amarillo};padding:16px 24px;font-size:24px;font-weight:bold;color:{Tinta};">ZASS!</td></tr>
<tr><td style="padding:24px;border:1px solid #e5e5e5;border-top:0;">
<h1 style="margin:0 0 16px;font-size:20px;color:{Tinta};">{H(titulo)}</h1>
<p style="margin:0 0 12px;">{saludo}</p>
<p style="margin:0 0 24px;">{H(parrafo)}</p>
<p style="margin:0 0 24px;"><a href="{e}" style="display:inline-block;background:{Amarillo};color:{Tinta};font-weight:bold;text-decoration:none;padding:12px 24px;border-radius:6px;">{H(textoBoton)}</a></p>
<p style="margin:0 0 12px;font-size:14px;">Si el botón no funciona, copia y pega este enlace en tu navegador:<br><a href="{e}" style="color:{Tinta};word-break:break-all;">{e}</a></p>
<p style="margin:0;font-size:14px;">{H(aviso)}</p>
</td></tr></table></td></tr></table></body></html>
""";
    }
}
