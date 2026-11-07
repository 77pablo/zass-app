using Zass.Dominio.Comun;

namespace Zass.Aplicacion.Credenciales;

/// <summary>
/// MOD-02 - S2. Qué acredita un prestador y quién lo respalda (TBL-12 a TBL-15).
///
/// Recibe el id del PERFIL, no el del usuario: quien compone (la capa de API) resuelve
/// "quién eres" con MOD-01, igual que ya hace con MOD-03. Así este módulo no conoce a
/// MOD-01 y el dueño de una credencial se decide en un solo sitio.
///
/// Esta primera parte cubre declarar, listar y eliminar credenciales (CU-05 pasos 1 a 5 y
/// flujo 4F). Subir documentos, enviar a revisión y la cola del Verificador (API-26, 27, 29
/// a 31) esperan a MOD-08, el almacenamiento con antivirus, que todavía no existe.
/// </summary>
public interface IServicioCredenciales
{
    /// <summary>API-24. Las credenciales del propio prestador, con su estado INTERNO.</summary>
    Task<IReadOnlyList<MiCredencialDto>> MisCredencialesAsync(
        Guid perfilPrestadorId, CancellationToken ct = default);

    /// <summary>API-25. Crea una credencial en borrador: solo su dueño la ve.</summary>
    Task<Resultado<Guid>> CrearAsync(CrearCredencialCmd cmd, CancellationToken ct = default);

    /// <summary>
    /// API-28. Elimina una credencial y sus archivos. La de otra persona se contesta igual que
    /// una que no existe: confirmar que existe ya le diria algo a quien la busca (E-403).
    /// </summary>
    Task<Resultado> EliminarAsync(
        Guid perfilPrestadorId, Guid credencialId, CancellationToken ct = default);

    /// <summary>
    /// Lo que ve el público en la ficha. Nunca el estado interno ni los archivos (RN-S2-03):
    /// solo "Verificada" o "Declarada por el usuario", y ningún borrador.
    /// </summary>
    Task<IReadOnlyList<CredencialPublicaDto>> CredencialesPublicasAsync(
        Guid perfilPrestadorId, CancellationToken ct = default);

    /// <summary>Si tiene alguna credencial con sello. Lo usa MOD-05 para desempatar.</summary>
    Task<bool> TieneVerificadasAsync(Guid perfilPrestadorId, CancellationToken ct = default);
}

/// <summary>
/// `EntidadId` si la eligió del catálogo, `EntidadNombre` si la escribió porque no estaba.
/// Las dos a la vez no tienen sentido, y el servicio lo rechaza.
/// </summary>
public sealed record CrearCredencialCmd(
    Guid PerfilPrestadorId,
    TipoCredencial Tipo,
    string Nombre,
    short? EntidadId,
    string? EntidadNombre,
    DateOnly ObtenidaEn,
    DateOnly? VenceEn);

/// <summary>Lo que llega por HTTP: el perfil no viaja, lo pone la sesión.</summary>
public sealed record CrearCredencialHttpCmd(
    TipoCredencial Tipo,
    string Nombre,
    short? EntidadId,
    string? EntidadNombre,
    DateOnly ObtenidaEn,
    DateOnly? VenceEn);

/// <summary>Lo que el dueño ve de su credencial, estado interno incluido.</summary>
public sealed record MiCredencialDto(
    Guid Id,
    TipoCredencial Tipo,
    string Nombre,
    string? Entidad,
    /// <summary>Falso si la escribió el propio prestador y ZASS! aún no la reconoce.</summary>
    bool? EntidadReconocida,
    DateOnly ObtenidaEn,
    DateOnly? VenceEn,
    EstadoCredencial Estado,
    bool TieneRespaldo,
    int Documentos);

/// <summary>Lo que ve cualquiera: sin estado interno, sin archivos, sin id.</summary>
public sealed record CredencialPublicaDto(
    TipoCredencial Tipo,
    string Nombre,
    string? Entidad,
    DateOnly ObtenidaEn,
    string Etiqueta,
    bool Verificada);
