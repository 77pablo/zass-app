using Zass.Aplicacion.Puertos;
using Zass.Dominio.Comun;

namespace Zass.Aplicacion.Resenas;

/// <summary>
/// MOD-11 · S5. Elegibilidad, reseñas, respuestas, reportes y reputación (CU-16, CU-17, CU-18,
/// CS-15, CS-16 y CS-17).
///
/// Recibe ids de USUARIO (quién eres lo decide la sesión) y busca por su cuenta el perfil que
/// necesita: así quien llama no compone nada y el dueño de una reseña se decide en un solo sitio.
/// </summary>
public interface IServicioResenas
{
    /// <summary>API-46. Deja constancia de un contacto desde la ficha: arranca el reloj de los 7 días.</summary>
    Task<Resultado<Guid>> RegistrarContactoAsync(
        Guid buscadorId, Guid perfilPrestadorId, OrigenContacto origen, CanalContacto canal,
        Guid? citaId = null, CancellationToken ct = default);

    /// <summary>API-47. Si puede reseñar a ese prestador y, si no, qué falta y desde cuándo.</summary>
    Task<Resultado<ResultadoElegibilidad>> EsElegibleAsync(
        Guid buscadorId, Guid perfilPrestadorId, CancellationToken ct = default);

    /// <summary>API-48. Publica. Revalida la elegibilidad en el servidor, dentro de la transacción.</summary>
    Task<Resultado<ResultadoPublicacion>> PublicarAsync(
        PublicarResenaCmd cmd, CancellationToken ct = default);

    /// <summary>API-49 · CS-16. Edita dentro de las 72 h; guarda la versión anterior en auditoría.</summary>
    Task<Resultado<ResultadoPublicacion>> EditarAsync(
        EditarResenaCmd cmd, CancellationToken ct = default);

    /// <summary>
    /// API-50. Crea la respuesta del prestador, o la reemplaza si ya había una (RN-S5-06: una por
    /// reseña, editable siempre). Devuelve true si la creó, false si la editó.
    /// </summary>
    Task<Resultado<bool>> ResponderAsync(
        Guid resenaId, Guid prestadorUsuarioId, string texto, CancellationToken ct = default);

    /// <summary>API-51. Reporta con motivo tipificado; un motivo grave oculta la reseña en el acto.</summary>
    Task<Resultado> ReportarAsync(
        Guid resenaId, Guid autorId, MotivoReporte motivo, string? detalle,
        CancellationToken ct = default);

    /// <summary>API-52. La cola del moderador, sin los reclamos en que él es parte.</summary>
    Task<Resultado<IReadOnlyList<FilaDeCola>>> ColaDeReportesAsync(
        Guid moderadorId, CancellationToken ct = default);

    /// <summary>API-53. Mantiene o retira la reseña, con el motivo escrito.</summary>
    Task<Resultado> ResolverReporteAsync(
        Guid reporteId, Guid moderadorId, bool retirar, string motivo,
        CancellationToken ct = default);

    /// <summary>MOD-11 `ResenasPublicas`. Lo que ve cualquiera: sin autor y sin estado interno.</summary>
    Task<Resultado<PaginaDeResenas>> ResenasPublicasAsync(
        Guid perfilPrestadorId, string? cursor, CancellationToken ct = default);
}

/// <summary>Códigos de falla propios de este módulo, además de los de <see cref="Fallas"/>.</summary>
public static class FallasResenas
{
    /// <summary>E-403 de CU-16: la elegibilidad no se cumple. La API la traduce a 403 con el motivo.</summary>
    public const string NoElegible = "no_elegible";
}

public enum MotivoDeNoElegibilidad
{
    Ninguno,
    /// <summary>El prestador es la misma persona (o una cuenta ligada). Mensaje neutro a propósito (CU-16 4G.3).</summary>
    Autoresena,
    SinContacto,
    /// <summary>Hay contacto pero no cumplió los 7 días (CU-16 4B).</summary>
    VentanaDeContacto,
    /// <summary>Ya reseñó a este prestador por contacto en los últimos 90 días (RN-S5-02).</summary>
    TopeDe90Dias,
    CitaSinRealizar,
    /// <summary>Cita marcada NoAsistio. Ver la decisión pendiente en ESTADO.md: la cita no dice quién faltó.</summary>
    CitaNoAsistio,
    /// <summary>Cuenta que no es de buscador, o con el correo sin verificar (P1).</summary>
    CuentaNoHabilitada,
}

/// <summary>
/// Respuesta de API-47. `ContactoId` es el contacto con el que se publicaría; `PuedeDesde` es la
/// fecha exacta en que se cumpliría la condición que falta (CU-16 4B.2), si hay una fecha.
/// </summary>
public sealed record ResultadoElegibilidad(
    bool Elegible,
    Guid? ContactoId,
    MotivoDeNoElegibilidad Motivo,
    string? Mensaje,
    DateTime? PuedeDesde);

public sealed record PublicarResenaCmd(
    Guid BuscadorId,
    Guid ContactoId,
    int Calificacion,
    string Comentario,
    IReadOnlyList<string>? Aspectos,
    /// <summary>CU-16 4E.3: si el chequeo de datos personales avisó, enviar igual.</summary>
    bool EnviarIgual,
    /// <summary>CC-04. Obligatoria: sin ella no hay forma de distinguir un reintento de un segundo envío.</summary>
    string Clave);

public sealed record EditarResenaCmd(
    Guid ResenaId,
    Guid AutorId,
    int Calificacion,
    string Comentario,
    IReadOnlyList<string>? Aspectos,
    bool EnviarIgual);

/// <summary>
/// Desenlace de publicar o editar. Exactamente una de estas dos cosas: la reseña (nueva, o ya
/// existente si fue un reintento: `YaExistia`), o la advertencia de datos personales pidiendo
/// confirmación (CU-16 4E.2), con los fragmentos señalados.
/// </summary>
public sealed record ResultadoPublicacion(
    ResenaDto? Resena,
    bool YaExistia,
    IReadOnlyList<string> DatosPersonalesDetectados)
{
    public bool RequiereConfirmacion => Resena is null;
}

/// <summary>Lo que ve su AUTOR: incluye el plazo de edición y el estado.</summary>
public sealed record ResenaDto(
    Guid Id,
    Guid PerfilPrestadorId,
    int Calificacion,
    string Comentario,
    IReadOnlyList<string> Aspectos,
    TipoResena Tipo,
    EstadoResena Estado,
    bool Editada,
    DateTime PublicadaEn,
    DateTime EditableHasta);

public sealed record RespuestaPublicaDto(string Texto, DateTime PublicadaEn, DateTime? EditadaEn);

/// <summary>
/// Lo que ve CUALQUIERA. Lo que NO tiene es lo importante (CU-16 §7 y RN-S1-07): ni autor, ni
/// su correo, ni el estado interno, ni el contacto o la cita que la habilitó.
/// </summary>
public sealed record ResenaPublicaDto(
    Guid Id,
    int Calificacion,
    string Comentario,
    IReadOnlyList<string> Aspectos,
    TipoResena Tipo,
    bool Editada,
    DateTime PublicadaEn,
    RespuestaPublicaDto? Respuesta);

public sealed record PaginaDeResenas(
    IReadOnlyList<ResenaPublicaDto> Resenas, string? SiguienteCursor);
