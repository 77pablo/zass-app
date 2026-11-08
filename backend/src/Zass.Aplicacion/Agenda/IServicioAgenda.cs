using Zass.Aplicacion.Comun;
using Zass.Dominio.Comun;

namespace Zass.Aplicacion.Agenda;

/// <summary>
/// MOD-06 · S4. Disponibilidad, bloques y citas.
///
/// Es el módulo más grande del sistema y el de concurrencia más densa. La regla de
/// tabla-de-modulos.md es que esa complejidad **no se filtre a esta interfaz**: quien la usa
/// no sabe que hay un <c>FOR UPDATE</c>, ni un índice único parcial, ni una clave de
/// idempotencia. Pide una hora y recibe una cita o un conflicto.
/// </summary>
public interface IServicioAgenda
{
    // ------------------------------------------------------------- CU-11 Publicar disponibilidad

    /// <summary>API-32. Las reglas de atención del prestador, por servicio.</summary>
    Task<IReadOnlyList<DisponibilidadDto>> MiDisponibilidadAsync(
        Guid perfilPrestadorId, CancellationToken ct = default);

    /// <summary>
    /// API-33. Reemplaza las franjas de UN servicio y genera los bloques de la ventana de 8
    /// semanas. Las franjas del mismo servicio no se pueden solapar (R-4).
    ///
    /// Cambiar la regla **no borra citas ya confirmadas** (RN-S4-09): solo desaparecen los
    /// bloques futuros que nadie reservó.
    /// </summary>
    Task<Resultado<int>> DefinirDisponibilidadAsync(
        DefinirDisponibilidadCmd cmd, CancellationToken ct = default);

    // ------------------------------------------------------------- CS-11 Bloquear fecha

    /// <summary>
    /// API-34. Un feriado o una ausencia. **No modifica los bloques**: se cruza al consultar,
    /// así que al borrar el bloqueo las horas vuelven a ofrecerse sin regenerar nada.
    /// </summary>
    Task<Resultado<BloqueoDto>> BloquearFechaAsync(
        BloquearFechaCmd cmd, CancellationToken ct = default);

    // ------------------------------------------------------------- CU-12 paso 2-4: el calendario

    /// <summary>
    /// API-35. Los bloques de un servicio en una ventana. Marca libres, ocupados y bloqueados
    /// y **no revela con quién** está ocupado (paso 3 de CU-12).
    /// </summary>
    Task<Resultado<CalendarioDto>> BloquesLibresAsync(
        Guid prestadorServicioId, DateTime desde, DateTime hasta, CancellationToken ct = default);

    // ------------------------------------------------------------- CU-12 / CU-13: agendar

    /// <summary>
    /// API-36. Toma un bloque. Según el modo del servicio deja la cita <c>confirmada</c>
    /// (CU-12) o <c>solicitada</c> (CU-13), y encapsula CC-01 y CC-04.
    ///
    /// Con la misma clave de idempotencia devuelve **la misma cita**, no una segunda
    /// (flujo 4B). Quien llama distingue las dos cosas por <see cref="CitaDto.Nueva"/>.
    /// </summary>
    Task<Resultado<CitaDto>> AgendarAsync(AgendarCmd cmd, CancellationToken ct = default);

    /// <summary>API-37. Mis citas, de los dos lados, ordenadas por fecha.</summary>
    Task<IReadOnlyList<CitaDto>> MisCitasAsync(
        Guid usuarioId, Guid? perfilPrestadorId, CancellationToken ct = default);

    // ------------------------------------------------------------- lo que consume MOD-11

    /// <summary>
    /// La consulta de elegibilidad de la tabla de módulos. MOD-11 pregunta esto y no la tabla
    /// `cita`: así un cambio de estados no rompe la elegibilidad en silencio (sección 4.1).
    /// </summary>
    Task<bool> CitaRealizadaAsync(
        Guid buscadorId, Guid perfilPrestadorId, CancellationToken ct = default);
}

// ----------------------------------------------------------------------- Comandos

/// <summary>
/// Una franja recurrente tal como llega de la pantalla. El día es 1 lunes a 7 domingo, como
/// TBL-16, y las horas son locales del prestador: la ventana se calcula en UTC al generar.
/// </summary>
public sealed record FranjaCmd(
    short DiaSemana,
    TimeOnly HoraInicio,
    TimeOnly HoraFin,
    short DuracionBloqueMin);

/// <summary>
/// API-33. <paramref name="PrestadorServicioId"/> dice a qué servicio aplica: el modo de
/// agenda es por servicio, no por cuenta, y la disponibilidad también.
/// </summary>
public sealed record DefinirDisponibilidadCmd(
    Guid PerfilPrestadorId,
    Guid PrestadorServicioId,
    IReadOnlyList<FranjaCmd> Franjas);

/// <summary>Lo que llega por HTTP: el perfil lo pone la sesión, no el cuerpo.</summary>
public sealed record DefinirDisponibilidadHttpCmd(
    Guid PrestadorServicioId,
    IReadOnlyList<FranjaCmd> Franjas);

public sealed record BloquearFechaCmd(
    Guid PerfilPrestadorId,
    DateTime DesdeEn,
    DateTime HastaEn,
    string? Motivo);

public sealed record BloquearFechaHttpCmd(DateTime DesdeEn, DateTime HastaEn, string? Motivo);

/// <summary>
/// API-36. <paramref name="ClaveIdempotencia"/> la genera el cliente al abrir el formulario
/// —una por INTENCIÓN, no por solicitud— y llega en la cabecera `Idempotency-Key`. Sin ella
/// el endpoint responde 400: una clave puesta por el servidor no protege de nada.
/// </summary>
public sealed record AgendarCmd(
    Guid BuscadorId,
    Guid BloqueId,
    string ClaveIdempotencia,
    string? NotaBuscador);

/// <summary>Lo que llega por HTTP. El buscador sale de la sesión; la clave, de la cabecera.</summary>
public sealed record AgendarHttpCmd(Guid BloqueId, string? NotaBuscador);

// ----------------------------------------------------------------------- Lo que sale

public sealed record FranjaDto(
    Guid Id,
    short DiaSemana,
    TimeOnly HoraInicio,
    TimeOnly HoraFin,
    short DuracionBloqueMin);

public sealed record DisponibilidadDto(
    Guid PrestadorServicioId,
    string Servicio,
    string ModoAgenda,
    IReadOnlyList<FranjaDto> Franjas);

public sealed record BloqueoDto(Guid Id, DateTime DesdeEn, DateTime HastaEn, string? Motivo);

/// <summary>
/// Un bloque como lo ve quien busca hora. <paramref name="Estado"/> dice `libre`, `ocupado` o
/// `bloqueado` y nunca quién lo ocupa: el paso 3 de CU-12 lo prohíbe expresamente.
/// </summary>
public sealed record BloqueDto(Guid Id, DateTime InicioEn, DateTime FinEn, string Estado);

/// <summary>
/// El calendario de un servicio. Trae el modo para que la pantalla pueda avisar ANTES de
/// elegir bloque si la cita necesita confirmación (flujo 4C de CU-12).
/// </summary>
public sealed record CalendarioDto(
    Guid PrestadorServicioId,
    Guid PerfilPrestadorId,
    string Servicio,
    string ModoAgenda,
    short DuracionMinutos,
    double AnticipacionMinimaHoras,
    IReadOnlyList<BloqueDto> Bloques);

/// <summary>
/// Una cita. <paramref name="Nueva"/> es false cuando la clave de idempotencia ya existía:
/// la API responde 200 en vez de 201 y no se manda un segundo aviso (RN-S4-08).
/// </summary>
public sealed record CitaDto(
    Guid Id,
    string Codigo,
    string Estado,
    string Modo,
    DateTime InicioEn,
    DateTime FinEn,
    string Servicio,
    Guid PrestadorServicioId,
    Guid PerfilPrestadorId,
    string Prestador,
    string? NotaBuscador,
    DateTime? ExpiraEn,
    bool Nueva);
