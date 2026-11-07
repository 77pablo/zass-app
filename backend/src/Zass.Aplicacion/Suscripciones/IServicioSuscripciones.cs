using Zass.Dominio.Comun;

namespace Zass.Aplicacion.Suscripciones;

/// <summary>
/// MOD-13 - S7. La interfaz publica del modulo. Todo lo demas es interno.
///
/// Existe por RN-S3-09: un prestador sin suscripcion vigente DESAPARECE de la busqueda.
/// Alguien tiene que poder responder "este prestador, hoy, aparece o no", y ese alguien
/// es este modulo. Antes del 7 de noviembre de 2026 las tres tablas de S7 existian en la
/// base sin modulo que las gobernara.
/// </summary>
public interface IServicioSuscripciones
{
    /// <summary>Un solo prestador. Para la ficha, el panel y las comprobaciones puntuales.</summary>
    Task<bool> EstaVigenteAsync(Guid perfilPrestadorId, CancellationToken ct = default);

    /// <summary>
    /// Varios de una vez, en UNA consulta. Es lo que usa la busqueda.
    ///
    /// No es una comodidad: preguntar <see cref="EstaVigenteAsync"/> por cada resultado
    /// convertiria una pagina de 20 en 21 consultas, y en la ruta mas caliente del
    /// producto. Devuelve solo los identificadores que estan vigentes.
    /// </summary>
    Task<IReadOnlyCollection<Guid>> VigentesEntreAsync(
        IReadOnlyCollection<Guid> perfilPrestadorIds, CancellationToken ct = default);

    /// <summary>Lo que el prestador ve en su panel. Nunca falla por no tener suscripcion.</summary>
    Task<Resultado<SuscripcionDto>> DePrestadorAsync(
        Guid perfilPrestadorId, CancellationToken ct = default);

    /// <summary>
    /// Arranca el periodo de prueba. Es idempotente: si ya hay una vigente no crea otra
    /// y devuelve exito igual, porque un doble clic no es un error de nadie.
    /// </summary>
    Task<Resultado> IniciarPruebaAsync(Guid perfilPrestadorId, CancellationToken ct = default);

    /// <summary>La llamara la pasarela cuando exista. Extiende el periodo y deja el cobro registrado.</summary>
    Task<Resultado> RegistrarPeriodoPagadoAsync(
        Guid suscripcionId, int meses = 1, int montoPagado = 0, string? referencia = null,
        CancellationToken ct = default);

    /// <summary>El prestador deja de pagar por voluntad propia. Sale de la busqueda de inmediato.</summary>
    Task<Resultado> CancelarAsync(Guid suscripcionId, CancellationToken ct = default);

    /// <summary>Trabajo de fondo. Devuelve cuantas marco. Vencer no borra: solo cambia el estado.</summary>
    Task<int> VencerExpiradasAsync(CancellationToken ct = default);

    /// <summary>
    /// Las que vencen dentro de N dias, para avisar ANTES.
    ///
    /// Desaparecer de la busqueda sin aviso previo es lo que volveria cruel a RN-S3-09:
    /// el perfil se apaga de golpe y el prestador se entera porque dejan de llamarlo.
    /// </summary>
    Task<IReadOnlyList<SuscripcionPorVencer>> PorVencerAsync(
        int dentroDeDias, CancellationToken ct = default);

    Task<IReadOnlyList<PlanDto>> PlanesActivosAsync(CancellationToken ct = default);
}

/// <summary>
/// Lo que el prestador ve de su propia suscripcion. Sin <c>ReferenciaPasarela</c> a
/// proposito: es dato de facturacion y no tiene nada que hacer en una pantalla.
/// </summary>
public sealed record SuscripcionDto(
    string Plan,
    string Estado,
    bool Vigente,
    bool EnPrueba,
    DateTime? VigenteHasta,
    int DiasRestantes,
    bool RenovacionAutomatica);

/// <summary>Lo minimo para armar el aviso: a quien y cuanto le queda.</summary>
public sealed record SuscripcionPorVencer(
    Guid SuscripcionId,
    Guid PerfilPrestadorId,
    DateTime VigenteHasta,
    int DiasRestantes);

public sealed record PlanDto(
    short Id,
    string Codigo,
    string Nombre,
    string? Descripcion,
    int PrecioMensual,
    short DiasPrueba,
    int MaxServicios,
    int MaxTarjetas,
    bool PermiteCredenciales);
