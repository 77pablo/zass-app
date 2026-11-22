using Zass.Dominio.Comun;

namespace Zass.Aplicacion.Credenciales;

/// <summary>
/// MOD-02 - S2. Qué acredita un prestador y quién lo respalda (TBL-12 a TBL-15).
///
/// Recibe el id del PERFIL, no el del usuario: quien compone (la capa de API) resuelve
/// "quién eres" con MOD-01, igual que ya hace con MOD-03. Así este módulo no conoce a
/// MOD-01 y el dueño de una credencial se decide en un solo sitio.
///
/// Cubre declarar, listar y eliminar credenciales (CU-05 pasos 1 a 5 y flujo 4F), enviar a
/// revisión (API-27) y, desde JQ-9, los documentos de respaldo (API-26) sobre el almacenamiento
/// de <see cref="Puertos.IAlmacenDeArchivos"/>. El análisis antivirus de CU-05 paso 8 NO está:
/// ver el comentario de <see cref="ServicioCredenciales.SubirDocumentoAsync"/>.
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
    /// API-27. Envía a revisión. Exige un documento disponible (RN-S2-01), salvo que la decisión
    /// de producto de lanzamiento `Zass:Verificacion:PermitirSinDocumentos` esté activa. También
    /// es la respuesta a un "pedir antecedentes": devuelve la credencial a la cola.
    /// </summary>
    Task<Resultado> EnviarARevisionAsync(
        Guid perfilPrestadorId, Guid credencialId, CancellationToken ct = default);

    /// <summary>
    /// Lo que ve el público en la ficha. Nunca el estado interno ni los archivos (RN-S2-03):
    /// solo "Verificada" o "Declarada por el usuario", y ningún borrador.
    /// </summary>
    Task<IReadOnlyList<CredencialPublicaDto>> CredencialesPublicasAsync(
        Guid perfilPrestadorId, CancellationToken ct = default);

    /// <summary>Si tiene alguna credencial con sello. Lo usa MOD-05 para desempatar.</summary>
    Task<bool> TieneVerificadasAsync(Guid perfilPrestadorId, CancellationToken ct = default);

    /// <summary>
    /// API-26 (JQ-9). Adjunta un documento de respaldo: PDF, JPG o PNG, 10 MB, 5 por credencial.
    /// El tipo se lee por dentro; las imagenes se vuelven a codificar sin metadatos; el nombre con
    /// que se guarda lo arma ZASS!. La de otra persona responde igual que una inexistente (E-403).
    /// </summary>
    Task<Resultado<MiDocumentoDto>> SubirDocumentoAsync(
        SubirDocumentoCmd cmd, CancellationToken ct = default);

    /// <summary>Quita un documento de una credencial propia que todavía no tiene sello.</summary>
    Task<Resultado> QuitarDocumentoAsync(
        Guid perfilPrestadorId, Guid credencialId, Guid documentoId, CancellationToken ct = default);

    /// <summary>Abre un documento propio para verlo. Solo su dueño; para el Verificador, ver IServicioVerificacion.</summary>
    Task<Resultado<DocumentoParaVer>> AbrirDocumentoAsync(
        Guid perfilPrestadorId, Guid credencialId, Guid documentoId, CancellationToken ct = default);
}

/// <summary>Lo que llega para subir un documento. El perfil lo pone la sesión.</summary>
public sealed record SubirDocumentoCmd(
    Guid PerfilPrestadorId,
    Guid CredencialId,
    Stream Contenido,
    long LargoDeclarado,
    string? NombreOriginal);

/// <summary>Un documento tal como lo ve su dueño (o el Verificador): nunca la clave de almacenamiento.</summary>
public sealed record MiDocumentoDto(
    Guid Id,
    string Nombre,
    string TipoMime,
    long TamanoBytes,
    EstadoDocumento Estado,
    DateTime SubidoEn);

/// <summary>Un documento abierto para servirlo. Quien lo recibe cierra el archivo.</summary>
public sealed record DocumentoParaVer(Puertos.ArchivoAbierto Archivo, string Nombre);

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
    int Documentos)
{
    /// <summary>Los documentos de respaldo, sin la clave con que se guardan. Vacío si no tiene.</summary>
    public IReadOnlyList<MiDocumentoDto> Respaldo { get; init; } = Array.Empty<MiDocumentoDto>();

    /// <summary>
    /// Solo si está rechazada: el motivo tipificado del último rechazo (estados-credencial.md §3:
    /// el prestador ve «Rechazada y el motivo»). Desde el 21-nov-2026 una rechazada ya no sale en
    /// la ficha, así que el dueño tiene que poder saber por qué. El texto libre del Verificador,
    /// quién la revisó y cuándo siguen sin salir.
    /// </summary>
    public MotivoRechazo? MotivoDelRechazo { get; init; }
}

/// <summary>Lo que ve cualquiera: sin estado interno, sin archivos, sin id.</summary>
public sealed record CredencialPublicaDto(
    TipoCredencial Tipo,
    string Nombre,
    string? Entidad,
    DateOnly ObtenidaEn,
    string Etiqueta,
    bool Verificada);
