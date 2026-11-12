using Zass.Dominio.Agenda;
using Zass.Dominio.Comun;
using Zass.Dominio.Reputacion;

namespace Zass.Aplicacion.Puertos;

/// <summary>
/// MOD-11. Lo que reseñas necesita de la base: TBL-21 a TBL-24, más el recálculo de los dos
/// derivados de TBL-05 (CC-07).
///
/// Vive en su propio archivo y no en Puertos.cs para no pisarse con quien agregue otro
/// módulo en paralelo: es un archivo que todos los módulos tocan por el final.
/// </summary>
public interface IRepositorioResenas
{
    /// <summary>
    /// Corre <paramref name="trabajo"/> dentro de UNA transacción y la confirma solo si
    /// <paramref name="confirmar"/> dice que el desenlace fue bueno. Es lo que hace que reseña,
    /// aviso, auditoría y recuento del prestador se vean todos o ninguno (CU-16 pasos 8 a 13).
    /// Si ya hay una transacción abierta se une a ella en vez de abrir otra.
    /// </summary>
    Task<T> EnTransaccionAsync<T>(
        Func<Task<T>> trabajo, Func<T, bool> confirmar, CancellationToken ct = default);

    /// <summary>
    /// Toma el cerrojo de la fila del prestador (SELECT ... FOR UPDATE) hasta el fin de la
    /// transacción. Todo lo que cambia las reseñas de un prestador pasa por aquí PRIMERO: así dos
    /// operaciones simultáneas sobre el mismo prestador se hacen en fila, y la segunda ve lo que
    /// confirmó la primera (CC-07).
    /// </summary>
    Task BloquearPerfilAsync(Guid perfilPrestadorId, CancellationToken ct = default);

    /// <summary>
    /// Recalcula promedio y recuento DENTRO del motor, contando solo las reseñas publicadas
    /// (una oculta o retirada no cuenta: CU-16 4F.3 y RN-S5-12). Hay que haber tomado antes el
    /// cerrojo, porque la instrucción nueva es la que ve lo confirmado por la anterior.
    /// </summary>
    Task RecalcularReputacionAsync(Guid perfilPrestadorId, CancellationToken ct = default);

    Task<DatosDePerfil?> PerfilAsync(Guid perfilPrestadorId, CancellationToken ct = default);

    Task<Contacto?> ContactoAsync(Guid contactoId, CancellationToken ct = default);

    /// <summary>Contactos del buscador con ese prestador que todavía no tienen reseña, el más viejo primero.</summary>
    Task<IReadOnlyList<Contacto>> ContactosSinResenaAsync(
        Guid buscadorId, Guid perfilPrestadorId, CancellationToken ct = default);

    /// <summary>
    /// Cuándo fue la última reseña de este autor a este prestador nacida de un contacto SIN cita
    /// y publicada desde <paramref name="desde"/>, del estado que sea. RN-S5-02: el tope de una
    /// cada 90 días no se "devuelve" porque la reseña se oculte o se retire; si no, quien la ve
    /// retirada podría volver a publicar el mismo día.
    /// </summary>
    Task<DateTime?> UltimaResenaPorContactoAsync(
        Guid autorId, Guid perfilPrestadorId, DateTime desde, CancellationToken ct = default);

    Task<Resena?> ResenaDeContactoAsync(Guid contactoId, CancellationToken ct = default);

    /// <summary>
    /// La reseña SIN seguimiento: sirve para saber de quién es y de qué prestador, antes de tomar
    /// el cerrojo. Lo que se va a modificar se vuelve a leer con <see cref="ResenaParaActualizarAsync"/>
    /// DESPUÉS del cerrojo, para no actuar sobre una copia que otra transacción ya cambió.
    /// </summary>
    Task<Resena?> ResenaPorIdAsync(Guid resenaId, CancellationToken ct = default);

    /// <summary>La reseña con su respuesta, con seguimiento, para modificarla. Leer después del cerrojo.</summary>
    Task<Resena?> ResenaParaActualizarAsync(Guid resenaId, CancellationToken ct = default);

    /// <summary>El contacto (si lo hay) que ya está ligado a esa cita: una cita, un solo contacto.</summary>
    Task<Contacto?> ContactoDeCitaAsync(Guid citaId, CancellationToken ct = default);

    /// <summary>La cita, solo para comprobar de quién es y con quién. La agenda (MOD-06) es su dueña.</summary>
    Task<Cita?> CitaAsync(Guid citaId, CancellationToken ct = default);

    /// <summary>
    /// Las publicadas, de la más nueva a la más vieja, keyset por (publicada_en, id). Cada fila
    /// trae además SI nació de una cita: el dato va aparte y no como la entidad `Contacto`
    /// entera, para que quien mapee la respuesta pública no pueda filtrar el contacto sin querer
    /// (CU-16 §7).
    /// </summary>
    Task<IReadOnlyList<FilaPublica>> PublicasAsync(
        Guid perfilPrestadorId, DateTime? antesDe, Guid? antesDeId, int limite,
        CancellationToken ct = default);

    /// <summary>
    /// Las reseñas que escribió esta persona, de la más nueva a la más vieja, del estado que sea
    /// (una retirada sigue siendo suya y tiene derecho a verla). El filtro por autor se aplica
    /// AQUI: es lo único que impide que alguien lea las de otro.
    /// </summary>
    Task<IReadOnlyList<FilaDeMisResenas>> MiasAsync(
        Guid autorId, int limite, CancellationToken ct = default);

    Task<bool> TieneReporteAbiertoAsync(Guid resenaId, Guid usuarioId, CancellationToken ct = default);

    Task<ReporteResena?> ReportePorIdAsync(Guid reporteId, CancellationToken ct = default);

    /// <summary>Los reportes sin resolver de esa reseña, con seguimiento. Leer después del cerrojo.</summary>
    Task<IReadOnlyList<ReporteResena>> ReportesAbiertosDeResenaAsync(
        Guid resenaId, CancellationToken ct = default);

    /// <summary>
    /// La cola de CU-18: abiertos o en revisión, SIN los que tocan al moderador (RN-S5-09), los
    /// graves primero y después los más viejos.
    /// </summary>
    Task<IReadOnlyList<FilaDeCola>> ColaAsync(
        Guid moderadorId, int limite, CancellationToken ct = default);

    void Agregar(Contacto contacto);
    void Agregar(Resena resena);
    void Agregar(RespuestaResena respuesta);
    void Agregar(ReporteResena reporte);
}

/// <summary>Lo mínimo de TBL-05 que reseñas necesita: de quién es y si se ve.</summary>
public sealed record DatosDePerfil(
    Guid Id, Guid UsuarioId, EstadoPerfil Estado, string NombrePublico);

/// <summary>
/// Una reseña de la lista pública y el único dato de su origen que sale hacia fuera: si hubo
/// cita en ZASS! o fue un contacto directo. Ni el id del contacto, ni el de la cita, ni fechas
/// con las que cruzarlo (CU-16 §7).
/// </summary>
public sealed record FilaPublica(Resena Resena, bool DeCita);

/// <summary>
/// Una reseña MIA: lo mismo, más a quién se la dejé, para poder volver a su ficha. Aquí sí hay
/// nombre y slug del prestador, porque esto solo lo ve su autor.
/// </summary>
public sealed record FilaDeMisResenas(
    Resena Resena, bool DeCita, string PerfilNombre, string PerfilSlug);

/// <summary>Una fila de la cola del moderador. Sin identidad de nadie: solo la reseña y el reclamo.</summary>
public sealed record FilaDeCola(
    Guid ReporteId, Guid ResenaId, MotivoReporte Motivo, string? Detalle, EstadoReporte Estado,
    DateTime CreadoEn, short Calificacion, string Comentario, EstadoResena EstadoResena,
    string PerfilNombre);
