using System.Net;
using Zass.Aplicacion.Puertos;
using Zass.Dominio.Comun;

namespace Zass.Infraestructura.Correo;

public sealed record CorreoRenderizado(string Asunto, string Texto, string Html);

/// <summary>
/// Las plantillas de correo transaccional. Texto plano y HTML simple: tablas y estilos en
/// línea porque los clientes de correo ignoran casi todo lo demás, y contraste alto
/// (tinta #1A1A1A sobre blanco; el amarillo #F6C81E solo como acento de marca y como fondo
/// del botón con texto oscuro, nunca como color de texto).
///
/// Solo reciben el nombre del PROPIO destinatario y un enlace: ninguna plantilla pone en el
/// correo datos de otra persona. El aviso de resena respondida es el unico que enlaza a la
/// ficha publica de un tercero, y lo hace con el slug, que ya es publico y que el
/// destinatario conoce porque es a quien reseno.
/// </summary>
public static class Plantillas
{
    private const string Amarillo = "#F6C81E";
    private const string Tinta = "#1A1A1A";

    public static string EnlaceDeVerificacion(string urlPublica, string token) =>
        $"{urlPublica.TrimEnd('/')}/verificar-correo?token={Uri.EscapeDataString(token)}";

    public static string EnlaceDeRecuperacion(string urlPublica, string token) =>
        $"{urlPublica.TrimEnd('/')}/restablecer-contrasena?token={Uri.EscapeDataString(token)}";

    /// <summary>El panel del prestador, donde se ve y se renueva la suscripcion.</summary>
    public static string EnlaceDelPanel(string urlPublica) => $"{urlPublica.TrimEnd('/')}/mi-ficha";

    /// <summary>
    /// La ficha publica de un prestador: la que el prestador comparte y donde se leen sus resenas.
    /// Sin slug devuelve el inicio del sitio: el aviso sale igual, solo sin atajo.
    ///
    /// (Dos ramas escribieron este ayudante a la vez, una exigiendo el slug y otra admitiendolo
    /// nulo. Se queda la tolerante: quien necesite otra cosa cuando falta el slug lo decide en su
    /// propio caso, como hace PerfilPublicado, que prefiere mandar al panel.)
    /// </summary>
    public static string EnlaceDeLaFicha(string urlPublica, string? slug)
    {
        var raiz = urlPublica.TrimEnd('/');
        return string.IsNullOrWhiteSpace(slug) ? raiz : $"{raiz}/prestadores/{Uri.EscapeDataString(slug)}";
    }

    /// <summary>
    /// Los tipos que este modulo sabe entregar. Los demas se quedan encolados sin gastar
    /// intentos: encolar no es enviar, y el dia que tengan plantilla saldran solos.
    /// </summary>
    public static bool TienePlantilla(TipoNotificacion tipo) =>
        tipo is TipoNotificacion.CorreoVerificacion
             or TipoNotificacion.RecuperarContrasena
             or TipoNotificacion.ResenaNueva
             or TipoNotificacion.ResenaRespondida
             or TipoNotificacion.SuscripcionPorVencer
             or TipoNotificacion.SuscripcionVencida
             or TipoNotificacion.PerfilPublicado;

    /// <summary>
    /// Los que llevan un secreto en la carga. Importa porque de esos el cuerpo se purga en
    /// cuanto el correo sale, y porque sin token no hay nada que enviar.
    /// </summary>
    public static bool LlevaToken(TipoNotificacion tipo) =>
        tipo is TipoNotificacion.CorreoVerificacion or TipoNotificacion.RecuperarContrasena;

    /// <summary>
    /// Null si el tipo no tiene plantilla, o si su carga no trae lo que la plantilla necesita
    /// (un token purgado, por ejemplo). El cuarto parametro es la CARGA del aviso, no el token
    /// suelto: hay un solo formato de cuerpo.
    /// </summary>
    public static CorreoRenderizado? Renderizar(
        TipoNotificacion tipo, string nombre, string urlPublica, string carga) => tipo switch
    {
        TipoNotificacion.CorreoVerificacion =>
            CargaDeAviso.LeerToken(carga) is { } t ? Verificacion(nombre, EnlaceDeVerificacion(urlPublica, t)) : null,
        TipoNotificacion.RecuperarContrasena =>
            CargaDeAviso.LeerToken(carga) is { } t ? Recuperacion(nombre, EnlaceDeRecuperacion(urlPublica, t)) : null,
        TipoNotificacion.SuscripcionPorVencer =>
            int.TryParse(CargaDeAviso.LeerCampo(carga, "dias"), out var d)
                ? PorVencer(nombre, d, EnlaceDelPanel(urlPublica)) : null,
        TipoNotificacion.SuscripcionVencida => Vencida(nombre, EnlaceDelPanel(urlPublica)),
        // Con el slug se enlaza su ficha publica, que es lo que querra abrir y compartir. Sin el
        // —los avisos encolados ANTES de que existiera esta plantilla llevan una frase suelta en vez
        // de una carga JSON— se enlaza su panel: sigue siendo util y no se pierde el aviso.
        TipoNotificacion.PerfilPublicado => Publicado(
            nombre,
            CargaDeAviso.LeerCampo(carga, "slug") is { Length: > 0 } slug
                ? EnlaceDeLaFicha(urlPublica, slug)
                : EnlaceDelPanel(urlPublica)),
        // Las dos de resenas no piden NADA de la carga: devuelven correo siempre. Es
        // deliberado. S5 encolaba estos avisos desde el primer dia con un cuerpo en prosa, y
        // si la plantilla exigiera un campo, cada aviso ya encolado pasaria de «no sale» a
        // «Fallida» el dia que esto se despliegue, en vez de salir por fin.
        TipoNotificacion.ResenaNueva => ResenaNueva(nombre, EnlaceDelPanel(urlPublica)),
        TipoNotificacion.ResenaRespondida =>
            Respondida(nombre, EnlaceDeLaFicha(urlPublica, CargaDeAviso.LeerCampo(carga, "slug"))),
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

    /// <summary>
    /// RN-S3-09 apaga el perfil al dejar de pagar. Este aviso existe para que eso no ocurra
    /// por sorpresa: sin el, el prestador se enteraria porque dejan de llamarlo.
    /// </summary>
    public static CorreoRenderizado PorVencer(string nombre, int dias, string enlace)
    {
        var cuando = dias <= 1 ? "mañana" : $"en {dias} días";
        var parrafo =
            $"Tu suscripción a ZASS! vence {cuando}. Mientras esté vigente tu ficha aparece en las "
            + "búsquedas; cuando venza deja de aparecer, pero no se borra nada: tu ficha, tus "
            + "credenciales verificadas y tus reseñas te esperan y vuelven en cuanto renueves.";
        var texto =
            $"Hola {nombre}:\n\n" +
            $"{parrafo}\n\n" +
            $"Puedes revisarla aquí:\n\n{enlace}\n\n" +
            "Si ya renovaste, ignora este mensaje.\n\n" +
            "— El equipo de ZASS!";
        var html = Marco(
            $"Tu suscripción vence {cuando}",
            $"Hola {H(nombre)}:",
            parrafo,
            "Ver mi suscripción", enlace,
            "Si ya renovaste, ignora este mensaje.");
        return new($"Tu suscripción a ZASS! vence {cuando}", texto, html);
    }

    /// <summary>Ya venció: la ficha dejó de aparecer. Se dice sin rodeos y sin culpar a nadie.</summary>
    public static CorreoRenderizado Vencida(string nombre, string enlace)
    {
        const string parrafo =
            "Tu suscripción a ZASS! venció, así que tu ficha dejó de aparecer en las búsquedas. No se "
            + "borró nada: tu ficha, tus credenciales verificadas y tus reseñas siguen ahí, y vuelven a "
            + "verse en cuanto renueves.";
        var texto =
            $"Hola {nombre}:\n\n" +
            $"{parrafo}\n\n" +
            $"Puedes renovarla aquí:\n\n{enlace}\n\n" +
            "— El equipo de ZASS!";
        var html = Marco(
            "Tu suscripción venció",
            $"Hola {H(nombre)}:",
            parrafo,
            "Renovar mi suscripción", enlace,
            "Si renovaste hace un momento, puede que este aviso se haya cruzado con el pago.");
        return new("Tu suscripción a ZASS! venció", texto, html);
    }

    /// <summary>
    /// CU-03 flujo 5: la ficha se publico y ya aparece en las busquedas. Es el unico correo que trae
    /// una buena noticia, asi que no repite instrucciones: dice que ya esta, da el enlace que puede
    /// compartir y recuerda que puede pausarla cuando quiera, que es la pregunta que viene detras.
    /// </summary>
    public static CorreoRenderizado Publicado(string nombre, string enlace)
    {
        const string parrafo =
            "Tu ficha ya está publicada: desde ahora apareces en las búsquedas de las comunas donde "
            + "atiendes, y quien te encuentre puede ver tus servicios y escribirte.";
        var texto =
            $"Hola {nombre}:\n\n" +
            $"{parrafo}\n\n" +
            $"Esta es tu ficha, para compartirla con quien quieras:\n\n{enlace}\n\n" +
            "Si en algún momento no puedes atender, puedes pausarla desde «Mi ficha» y dejas de " +
            "aparecer al instante. No se borra nada.\n\n" +
            "— El equipo de ZASS!";
        var html = Marco(
            "Tu ficha ya está publicada",
            $"Hola {H(nombre)}:",
            parrafo,
            "Ver mi ficha", enlace,
            "¿No puedes atender por unos días? Puedes pausar tu ficha desde «Mi ficha» y vuelves cuando quieras.");
        return new("Tu ficha ya está publicada en ZASS!", texto, html);
    }

    /// <summary>
    /// El derecho a Saber de S5 («notificacion inmediata de cada resena nueva») y el paso 14
    /// de CU-16. No adelanta ni la calificacion ni el texto ni quien la escribio, por tres
    /// razones: el asunto se lee en la pantalla bloqueada, una resena puede acabar oculta en
    /// moderacion (CS-17) y un correo ya enviado no se retira, y el comentario es de otra
    /// persona: este modulo solo manda lo del propio destinatario.
    ///
    /// TEXTO PENDIENTE DE APROBACION de Pablo.
    /// </summary>
    public static CorreoRenderizado ResenaNueva(string nombre, string enlace)
    {
        const string parrafo =
            "Alguien con quien trabajaste dejó una reseña en tu ficha de ZASS!. Entra a tu panel "
            + "para leerla. Si quieres, puedes responderla una vez y tu respuesta queda publicada "
            + "debajo, a la vista de quien lea tu ficha.";
        var texto =
            $"Hola {nombre}:\n\n" +
            $"{parrafo}\n\n" +
            $"Puedes verla aquí:\n\n{enlace}\n\n" +
            "— El equipo de ZASS!";
        var html = Marco(
            "Tienes una reseña nueva",
            $"Hola {H(nombre)}:",
            parrafo,
            "Ver la reseña", enlace,
            "Si crees que esa reseña incumple las reglas, puedes reportarla desde tu panel.");
        return new("Tienes una reseña nueva en ZASS!", texto, html);
    }

    /// <summary>
    /// El otro lado de RN-S5-06: el prestador tiene una respuesta por resena, y quien
    /// escribio la resena merece enterarse de que la respondieron. Va a la ficha publica,
    /// que es donde se lee la respuesta; al prestador no se le nombra en el correo.
    ///
    /// TEXTO PENDIENTE DE APROBACION de Pablo.
    /// </summary>
    public static CorreoRenderizado Respondida(string nombre, string enlace)
    {
        const string parrafo =
            "El prestador al que reseñaste publicó una respuesta. Queda debajo de tu reseña, en "
            + "su ficha de ZASS!, a la vista de quien la lea. Tu reseña no cambió.";
        var texto =
            $"Hola {nombre}:\n\n" +
            $"{parrafo}\n\n" +
            $"Puedes leerla aquí:\n\n{enlace}\n\n" +
            "— El equipo de ZASS!";
        var html = Marco(
            "Respondieron tu reseña",
            $"Hola {H(nombre)}:",
            parrafo,
            "Ver la respuesta", enlace,
            "Si la respuesta incumple las reglas, puedes reportarla desde la ficha.");
        return new("Respondieron tu reseña en ZASS!", texto, html);
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
