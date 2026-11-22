namespace Zass.Aplicacion.Puertos;

/// <summary>
/// S1 · auditoría del 22-nov-2026. Lo que necesita la purga de cuentas que nunca verificaron el
/// correo (<see cref="Identidad.ServicioCuentasSinVerificar"/>).
///
/// <para>
/// <b>«Purgable»</b> se define en UN solo lugar (la implementación) y lo usan las tres operaciones:
/// cuenta <c>PendienteVerificacion</c>, correo sin verificar, creada antes del límite, sin ningún
/// rol interno y SIN ninguna actividad: ni citas, contactos, reseñas o reportes como persona, ni
/// verificaciones, ni un perfil de prestador que no sea un borrador vacío (credenciales,
/// suscripción, pagos, tarjeta, cupo fundador, cupón, fotos, coberturas, servicios, agenda).
/// Lo que se borra con la cuenta es lo que el propio registro crea: roles, perfil vacío,
/// sesiones, avisos y navegadores de push. La auditoría NO se toca (no tiene clave foránea).
/// </para>
/// </summary>
public interface IRepositorioCuentasSinVerificar
{
    /// <summary>Las purgables creadas antes de <paramref name="creadasAntesDe"/>, la más vieja primero.</summary>
    Task<IReadOnlyList<Guid>> PurgablesAsync(
        DateTime creadasAntesDe, int limite, CancellationToken ct = default);

    /// <summary>
    /// Las que siguen sin verificar pasado el plazo pero NO se purgan porque tienen actividad o un
    /// rol interno. Solo el número: va al resumen del trabajo para que alguien las mire.
    /// </summary>
    Task<int> ContarRetenidasAsync(DateTime creadasAntesDe, CancellationToken ct = default);

    /// <summary>
    /// Borra la cuenta en UNA sentencia que vuelve a comprobar que es purgable: si entre la lista y
    /// el borrado verificó el correo o hizo algo, no se borra. Devuelve si la borró.
    /// </summary>
    Task<bool> PurgarAsync(Guid usuarioId, DateTime creadasAntesDe, CancellationToken ct = default);
}
