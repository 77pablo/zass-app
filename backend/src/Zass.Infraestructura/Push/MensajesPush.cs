using System.Globalization;
using System.Text.Json;
using Zass.Aplicacion.Agenda;
using Zass.Aplicacion.Puertos;
using Zass.Dominio.Comun;

namespace Zass.Infraestructura.Push;

/// <summary>Lo que se sabe de la cita al armar el aviso. Null si el aviso no es de una cita o ya no existe.</summary>
public sealed record DatosDeCitaPush(DateTime InicioUtc, string? OtraParte);

/// <summary>Titulo, texto y la pagina que abre al tocarlo (ruta relativa: el service worker la completa).</summary>
public sealed record MensajePush(string Titulo, string Cuerpo, string Ruta);

/// <summary>
/// ES-3. Los textos del push. TEXTOS PROPUESTOS, para revisar en el PR.
///
/// Se leen en la PANTALLA BLOQUEADA (decision del 21-nov: utiles, sin datos sensibles). Pueden
/// llevar el nombre de pila o el nombre publico de la otra parte y la hora de la cita. Nunca la
/// direccion, el telefono, el RUT, el correo, un monto ni un enlace con token.
/// </summary>
public static class MensajesPush
{
    private static readonly CultureInfo Chile = CultureInfo.GetCultureInfo("es-CL");

    private const string MisCitas = "/mis-citas";
    private const string MiFicha = "/mi-ficha";
    private const string Buscar = "/buscar";

    /// <summary>Null si el tipo no va por push (ver AvisosPush.AdmitePush).</summary>
    public static MensajePush? Para(TipoNotificacion tipo, string carga, DatosDeCitaPush? cita)
    {
        var prestador = CargaDeAviso.LeerCampo(carga, AvisosDeCita.CampoRol) == AvisosDeCita.RolPrestador;
        var otro = Nombre(cita?.OtraParte);
        var cuando = cita is null ? null : Cuando(cita.InicioUtc);

        return tipo switch
        {
            TipoNotificacion.CitaSolicitada => new("Tienes una solicitud de hora",
                Frase(otro is null ? "Alguien pidió una hora contigo" : $"{otro} pidió una hora contigo", cuando, "para el")
                + " Tienes 24 horas para responder.", MisCitas),

            TipoNotificacion.CitaConfirmada => prestador
                ? new("Tienes una hora nueva",
                    Frase(otro is null ? "Alguien agendó una hora contigo" : $"{otro} agendó una hora contigo", cuando, "para el"),
                    MisCitas)
                : new("Tu hora quedó confirmada",
                    Frase(otro is null ? "Tu hora quedó confirmada" : $"Tu hora con {otro} quedó confirmada", cuando, "para el"),
                    MisCitas),

            TipoNotificacion.CitaRechazada => new("Tu solicitud de hora no se confirmó",
                "No se pudo tomar la hora que pediste. Puedes buscar otra cuando quieras.", Buscar),

            TipoNotificacion.CitaCancelada => prestador
                ? new("Se canceló una hora",
                    Frase(otro is null ? "Se canceló una hora contigo" : $"{otro} canceló su hora", cuando, "del")
                    + " Esa hora quedó libre.", MisCitas)
                : new("Se canceló tu hora",
                    Frase(otro is null ? "Se canceló tu hora" : $"{otro} canceló tu hora", cuando, "del")
                    + " Puedes buscar otra.", Buscar),

            TipoNotificacion.CitaReprogramada => prestador
                ? new("Una cita cambió de hora",
                    Frase(otro is null ? "Una cita cambió de hora" : $"{otro} cambió su hora", cuando, "para el"),
                    MisCitas)
                : new("Tu hora cambió",
                    Frase(otro is null ? "Tu hora cambió" : $"Tu hora con {otro} cambió", cuando, "para el"),
                    MisCitas),

            TipoNotificacion.Recordatorio24h => new("Mañana tienes una hora",
                Frase(otro is null ? "Recuerda tu hora" : $"Recuerda tu hora con {otro}", cuando, "el"), MisCitas),

            TipoNotificacion.Recordatorio2h => new("Tu hora es en 2 horas",
                otro is null
                    ? (cita is null ? "Recuerda tu hora de hoy." : $"Recuerda tu hora de hoy a las {Hora(cita.InicioUtc)}.")
                    : (cita is null ? $"Recuerda tu hora de hoy con {otro}." : $"Recuerda tu hora con {otro} hoy a las {Hora(cita.InicioUtc)}."),
                MisCitas),

            TipoNotificacion.ResenaNueva => new("Tienes una reseña nueva",
                "Alguien dejó una reseña en tu ficha.", MiFicha),

            TipoNotificacion.ResenaRespondida => new("Respondieron tu reseña",
                "El prestador respondió la reseña que dejaste.",
                CargaDeAviso.LeerCampo(carga, "slug") is { Length: > 0 } slug && EsSlug(slug) ? $"/prestadores/{slug}" : "/"),

            TipoNotificacion.CredencialResuelta => new("Revisamos tu credencial",
                "Entra a «Mi ficha» para ver el resultado.", MiFicha),

            TipoNotificacion.CredencialVencida => new("Venció una de tus credenciales",
                "Ya no muestra el sello «Verificada». Renuévala en «Mi ficha».", MiFicha),

            // El nombre de la credencial lo escribio el prestador: no va en el push (se lee en la
            // pantalla bloqueada). La fecha si, porque es lo que importa para no perderla.
            TipoNotificacion.BorradorPorEliminarse => new("Tu borrador de credencial se eliminará",
                DateOnly.TryParseExact(CargaDeAviso.LeerCampo(carga, "borrarEl"), "yyyy-MM-dd", out var el)
                    ? $"Termínalo en «Mi ficha» antes del {el.ToString("d 'de' MMMM", Chile)} para no perderlo."
                    : "Termínalo en «Mi ficha» para no perderlo.", MiFicha),

            TipoNotificacion.PerfilPublicado => new("Tu ficha ya está publicada",
                "Ya apareces en las búsquedas de ZASS!.", MiFicha),

            TipoNotificacion.TarjetaActivada => new("Tu tarjeta ZASS! quedó activada",
                "Ya puedes usarla para que te encuentren más rápido.", "/mis-tarjetas"),

            TipoNotificacion.SuscripcionPorVencer => new("Tu plan vence pronto",
                int.TryParse(CargaDeAviso.LeerCampo(carga, "dias"), out var d) && d > 0
                    ? $"Te {(d == 1 ? "queda 1 día" : $"quedan {d} días")}. Revísalo en «Mi ficha»."
                    : "Revísalo en «Mi ficha» para que tu ficha siga apareciendo.", MiFicha),

            TipoNotificacion.SuscripcionVencida => new("Tu plan venció",
                "Tu ficha ya no aparece en las búsquedas. Puedes renovarlo cuando quieras.", MiFicha),

            TipoNotificacion.CobroRechazado => new("No pudimos cobrar tu plan",
                "Actualiza tu tarjeta en «Mi ficha» para que tu ficha siga apareciendo.", MiFicha),

            _ => null,
        };
    }

    /// <summary>El aviso que viaja (cifrado) hasta el service worker.</summary>
    public static string AJson(MensajePush m, string etiqueta) => JsonSerializer.Serialize(new
    {
        titulo = m.Titulo,
        cuerpo = m.Cuerpo,
        url = m.Ruta,
        etiqueta,
    });

    private static string Frase(string inicio, string? cuando, string enlace) =>
        cuando is null ? $"{inicio}." : $"{inicio} {enlace} {cuando}.";

    /// <summary>«martes 25 de noviembre a las 10:00», en hora de Chile.</summary>
    internal static string Cuando(DateTime inicioUtc)
    {
        var local = HoraDeChile.ALocal(inicioUtc);
        return $"{local.ToString("dddd d 'de' MMMM", Chile)} a las {local.ToString("HH:mm", Chile)}";
    }

    private static string Hora(DateTime inicioUtc) => HoraDeChile.ALocal(inicioUtc).ToString("HH:mm", Chile);

    /// <summary>
    /// El nombre de pila de quien busca, o el nombre publico del prestador (el mismo de su ficha), acotado.
    /// </summary>
    internal static string? Nombre(string? completo)
    {
        if (string.IsNullOrWhiteSpace(completo)) return null;
        var limpio = completo.Trim();
        return limpio.Length <= 40 ? limpio : limpio[..40].TrimEnd() + "…";
    }

    private static bool EsSlug(string s) => s.Length <= 120 && s.All(c => char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c) || c == '-');
}
