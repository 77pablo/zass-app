using System.Globalization;
using System.Text.RegularExpressions;
using Zass.Dominio.Comun;
using Zass.Dominio.Reputacion;

namespace Zass.Aplicacion.Resenas;

/// <summary>
/// Las reglas de S5 que no necesitan base de datos: se prueban con valores y se leen sin tener
/// que seguir un caso de uso entero.
/// </summary>
public static class ReglasDeResenas
{
    public const int CalificacionMinima = 1;
    public const int CalificacionMaxima = 5;
    public const int ComentarioMinimo = 20;
    public const int ComentarioMaximo = 1500;
    public const int RespuestaMaxima = 1000;
    public const int DetalleMaximo = 1000;
    public const int MotivoResolucionMinimo = 10;
    public const int MotivoResolucionMaximo = 1000;
    public const int MaximoClave = 100;

    /// <summary>RN-S5-02 y la tabla de elegibilidad: un contacto sin cita espera 7 días.</summary>
    public static readonly TimeSpan VentanaDeContacto = TimeSpan.FromDays(7);

    /// <summary>RN-S5-02: por contacto, una reseña por prestador cada 90 días.</summary>
    public static readonly TimeSpan TopePorContacto = TimeSpan.FromDays(90);

    /// <summary>RN-S5-05.</summary>
    public static readonly TimeSpan PlazoDeEdicion = TimeSpan.FromHours(72);

    /// <summary>
    /// Los aspectos de CU-16 paso 6. Es lista cerrada: son etiquetas, no texto libre, y no afectan
    /// al promedio. Texto libre aquí sería otro campo de comentario sin el límite de largo.
    /// </summary>
    public static readonly string[] AspectosPermitidos = { "puntualidad", "claridad", "precio_acordado" };

    /// <summary>
    /// Largo en caracteres como lo cuenta PostgreSQL (`char_length`: puntos de código), no en
    /// unidades UTF-16 como `string.Length`. Con emojis las dos cifras difieren, y el CHECK de la
    /// base es el segundo.
    /// </summary>
    public static int Largo(string texto) => texto.EnumerateRunes().Count();

    /// <summary>
    /// Elegibilidad de UN contacto concreto (tabla de la sección 3 de S5). No mira si es la misma
    /// persona ni si ya hay reseña: eso lo decide quien llama.
    /// </summary>
    /// <param name="ultimaPorContacto">Última reseña del autor a ese prestador nacida de un contacto sin cita, dentro de los 90 días.</param>
    public static ResultadoElegibilidad EvaluarContacto(
        Contacto contacto, DateTime ahora, DateTime? ultimaPorContacto)
    {
        if (contacto.CitaId is not null || contacto.Cita is not null)
        {
            return contacto.Cita?.Estado switch
            {
                EstadoCita.Realizada => Si(contacto.Id),
                // Decisión de Pablo (11-nov-2026) y cuadro de elegibilidad de S5 §3. Una cita
                // que no ocurrió ya no es un solo caso: depende de quién faltó.
                EstadoCita.NoAsistio => PorInasistencia(contacto),
                EstadoCita.Solicitada or EstadoCita.Confirmada or EstadoCita.PendienteAsistencia => No(
                    MotivoDeNoElegibilidad.CitaSinRealizar,
                    "Todavía no hay experiencia que reseñar: la cita aún no se realiza."),
                _ => No(
                    MotivoDeNoElegibilidad.CitaSinRealizar,
                    "La cita no llegó a realizarse, así que no hay servicio que reseñar."),
            };
        }

        var desde = contacto.OcurridoEn + VentanaDeContacto;
        var motivo = MotivoDeNoElegibilidad.VentanaDeContacto;
        var falta = ahora < desde;

        if (ultimaPorContacto is { } ultima && ultima + TopePorContacto > ahora)
        {
            var finTope = ultima + TopePorContacto;
            if (finTope > desde) { desde = finTope; motivo = MotivoDeNoElegibilidad.TopeDe90Dias; }
            falta = true;
        }

        if (!falta) return Si(contacto.Id);

        var cuando = desde.ToString("d 'de' MMMM 'de' yyyy", new CultureInfo("es-CL"));
        return motivo == MotivoDeNoElegibilidad.TopeDe90Dias
            ? new ResultadoElegibilidad(false, contacto.Id, motivo,
                $"Ya dejaste una reseña a este prestador hace poco. Podrás dejar otra desde el {cuando}.", desde)
            : new ResultadoElegibilidad(false, contacto.Id, motivo,
                $"Para opinar tienen que pasar 7 días desde tu contacto. Podrás reseñar desde el {cuando}.", desde);

        static ResultadoElegibilidad Si(Guid id) =>
            new(true, id, MotivoDeNoElegibilidad.Ninguno, null, null);
        static ResultadoElegibilidad No(MotivoDeNoElegibilidad m, string mensaje) =>
            new(false, null, m, mensaje, null);
    }

    /// <summary>
    /// Una cita que no ocurrió, según quién faltó (decisión de Pablo del 11-nov-2026, y cuadro
    /// de elegibilidad de S5 §3):
    ///
    /// <list type="bullet">
    /// <item>Faltó el PRESTADOR: el cliente sí reseña, pero una reseña LIMITADA, de tipo
    /// inasistencia (CU-16 flujo 4C): opina sobre el plantón, no sobre un servicio que nunca
    /// recibió.</item>
    /// <item>Faltó el CLIENTE: no hay reseña. No se presentó.</item>
    /// <item>Se contradicen: la reseña se bloquea DE INMEDIATO y la cita queda en la cola
    /// interna de revisión. Dos versiones opuestas y ninguna prueba; publicar la de quien
    /// llegue primero sería darle la razón.</item>
    /// <item><see cref="FaltaEnCita.Nadie"/> con la cita en `no_asistio` no debería existir:
    /// la única forma de llegar a ese estado es acusando. Si aparece, es una fila anterior a
    /// esta regla o un error, y se trata como el caso que no habilita.</item>
    /// </list>
    /// </summary>
    private static ResultadoElegibilidad PorInasistencia(Contacto contacto) =>
        contacto.Cita!.QuienFalto switch
        {
            FaltaEnCita.Prestador => new ResultadoElegibilidad(
                true, contacto.Id, MotivoDeNoElegibilidad.Ninguno, null, null,
                TipoResena.Inasistencia),

            FaltaEnCita.Discrepancia => new ResultadoElegibilidad(
                false, null, MotivoDeNoElegibilidad.CitaEnDisputa,
                "Tú y el prestador dijeron cosas distintas sobre esta cita. La estamos revisando, "
                + "y por ahora no se puede reseñar.", null),

            _ => new ResultadoElegibilidad(
                false, null, MotivoDeNoElegibilidad.CitaNoAsistio,
                "Esta cita quedó marcada como inasistencia tuya, así que no habilita reseña.", null),
        };

    // ------------------------------------------------ datos personales (CU-16 paso 5)

    // Chequeo BÁSICO, como dice el caso de uso: avisa, no bloquea. Prefiere un falso aviso a un
    // dato personal publicado, y por eso el aviso se puede saltar con "enviar igual".
    private static readonly Regex Rut = new(
        @"(?<![\d.])\d{1,2}\.?\d{3}\.?\d{3}\s?-\s?[\dkK](?!\w)", RegexOptions.Compiled);

    // Móvil chileno (9 xxxx xxxx, con o sin +56) y fijo (2 xxxx xxxx...). Tolera espacios y guiones.
    private static readonly Regex Telefono = new(
        @"(?<!\d)(?:\+?56[\s-]?)?(?:9[\s-]?\d{4}[\s-]?\d{4}|[2-7]\d[\s-]?\d{3}[\s-]?\d{4})(?!\d)",
        RegexOptions.Compiled);

    private static readonly Regex Direccion = new(
        @"\b(?:calle|av\.?|avenida|pasaje|pje\.?|camino|psje\.?)\s+[\p{L}\d .]{2,40}?\s+(?:n[°ºo]\.?\s*)?\d{1,5}\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>Los fragmentos que parecen RUT, teléfono o dirección. Vacío si no hay ninguno.</summary>
    public static IReadOnlyList<string> DatosPersonales(string texto)
    {
        var encontrados = new List<string>();
        foreach (var regex in new[] { Rut, Telefono, Direccion })
            foreach (Match m in regex.Matches(texto))
                encontrados.Add(m.Value.Trim());
        return encontrados.Distinct().ToList();
    }
}
