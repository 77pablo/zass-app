using Zass.Dominio.Comun;

namespace Zass.Aplicacion.Credenciales;

/// <summary>
/// MOD-02 · CU-06. La verificación manual: la cola del Verificador, tomar una credencial
/// (reserva de 30 minutos, CC-03) y resolverla (CS-05). Es lo que permite que el sello
/// «Verificado» exista aunque todavía no haya subida de archivos (MOD-08).
///
/// Recibe el id del USUARIO verificador: RN-S2-09 compara contra el dueño de la cuenta, no
/// contra el perfil.
/// </summary>
public interface IServicioVerificacion
{
    /// <summary>API-29. Cola de revisión, sin las propias ni las que otro tiene reservadas.</summary>
    Task<IReadOnlyList<CredencialEnColaDto>> ColaAsync(
        Guid verificadorId, CancellationToken ct = default);

    /// <summary>
    /// API-30. Reserva por 30 minutos. Dos verificadores no pueden tomar la misma. Una cuenta
    /// propia se contesta como inexistente.
    /// </summary>
    Task<Resultado<CredencialEnColaDto>> TomarAsync(
        Guid verificadorId, Guid credencialId, CancellationToken ct = default);

    /// <summary>API-31. Verifica, rechaza (con motivo escrito) o pide antecedentes.</summary>
    Task<Resultado<ResolucionDto>> ResolverAsync(
        ResolverCredencialCmd cmd, CancellationToken ct = default);
}

/// <summary>
/// Lo que decide la etapa de lanzamiento. DECISIÓN DE PRODUCTO REVISABLE: mientras no exista
/// MOD-08, con `PermitirSinDocumentos` el dueño puede enviar a revisión sin archivos y un
/// Verificador la comprueba por otra vía. En falso (por omisión) rige RN-S2-01 intacta.
/// </summary>
public sealed class OpcionesVerificacion
{
    public const string ClaveDeConfiguracion = "Zass:Verificacion:PermitirSinDocumentos";
    public bool PermitirSinDocumentos { get; init; }
}

public sealed record ResolverCredencialCmd(
    Guid VerificadorId,
    Guid CredencialId,
    ResultadoVerificacion Resultado,
    MotivoRechazo? Motivo,
    string? Detalle);

/// <summary>Lo que llega por HTTP: quién resuelve y qué credencial lo ponen la sesión y la ruta.</summary>
public sealed record ResolverCredencialHttpCmd(
    ResultadoVerificacion Resultado,
    MotivoRechazo? Motivo,
    string? Detalle);

public sealed record ResolucionDto(Guid Id, EstadoCredencial Estado, bool EsperandoPrestador);

/// <summary>
/// Una credencial para el Verificador. Es una vista INTERNA: nunca se sirve al público ni al dueño.
/// `SinDocumentos` le avisa que el dueño la envió sin respaldo en la plataforma y debe comprobarla
/// fuera de ella (decisión de producto de lanzamiento).
/// </summary>
public sealed record CredencialEnColaDto(
    Guid Id,
    string Prestador,
    string PrestadorSlug,
    TipoCredencial Tipo,
    string Nombre,
    string? Entidad,
    bool? EntidadReconocida,
    DateOnly ObtenidaEn,
    DateOnly? VenceEn,
    /// <summary>4F: se marca antes de resolver; puede verificarse igual.</summary>
    bool VencimientoPasado,
    int Documentos,
    bool SinDocumentos,
    DateTime EnColaDesde,
    bool ReservadaPorMi,
    DateTime? ReservadaHasta);
