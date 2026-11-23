using Zass.Aplicacion.Agenda;
using Zass.Aplicacion.Identidad;
using Zass.Aplicacion.Puertos;
using Zass.Aplicacion.Suscripciones;
using Zass.Dominio.Comun;

namespace Zass.Api.Endpoints;

/// <summary>
/// CS-04, API-17: eliminar la cuenta, de punta a punta. Decidido por Pablo el 17-nov-2026:
///
/// 1. **Se pide la contraseña, y el código si la cuenta tiene la verificación en dos pasos.** Con
///    solo una sesión abierta (un computador compartido) nadie puede borrar una cuenta ajena para
///    siempre. Los fallos suman al bloqueo por intentos del inicio de sesión (auditoría H2).
/// 2. **La ficha se despublica primero** (auditoría H3), para que nadie agende mientras se cancelan
///    las citas. **Las citas futuras se cancelan, y se avisa a la otra parte**, con el mismo
///    <see cref="IServicioAgenda.CancelarAsync"/> de siempre: libera la hora, anula los
///    recordatorios y manda el aviso. Las pasadas quedan en el historial.
/// 3. Recién entonces se anonimiza la cuenta (<see cref="ServicioCuenta.EliminarAsync"/>), que
///    en la MISMA transacción termina la suscripción, sin reembolso: la cancela, olvida el medio
///    de pago inscrito para que el cobro automático no tenga contra qué cobrar y libera el cupo
///    fundador (<see cref="IServicioSuscripciones.TerminarPorCuentaEliminadaAsync"/>, PR #211), y
///    borra los datos personales de la ficha, las fotos, los documentos y los navegadores con push
///    (<see cref="ServicioBorradoDeDatos"/>, Ley 21.719).
///
/// Vive en la API, y no en un módulo de la aplicación, porque coordina tres módulos (Identidad,
/// Agenda y Suscripciones). Identidad es la base de la que dependen los demás, y llamar desde ahí
/// a la Agenda crearía un ciclo (lo prohíbe Zass.Arquitectura.Pruebas). Es el mismo patrón que ya
/// usan otros endpoints de identidad que consultan la suscripción.
///
/// No es una sola transacción: cada servicio guarda lo suyo. Por eso el orden importa y cada paso
/// es idempotente. La contraseña va primero, y si falla no se toca nada. Si algo falla a la mitad,
/// repetir la baja termina el trabajo: una cita ya cancelada se devuelve igual sin un segundo
/// aviso (RN-S4-08, por su clave de idempotencia).
/// </summary>
public sealed class BajaDeCuenta(
    ServicioCuenta cuenta,
    ServicioPerfiles perfiles,
    IServicioAgenda agenda,
    IRelojSistema reloj)
{
    /// <summary>
    /// El motivo que lee la otra parte. Hace falta: en una cancelación con menos de 12 horas
    /// (RN-S4-04) el motivo es obligatorio, y una baja no puede quedar trabada por eso.
    /// </summary>
    public const string MotivoDeCancelacion = "Esta persona eliminó su cuenta en ZASS!.";

    public async Task<Resultado> EjecutarAsync(
        Guid usuarioId, string contrasena, string? codigo, string? ip, CancellationToken ct = default)
    {
        var verificada = await cuenta.ConfirmarBajaAsync(usuarioId, contrasena, codigo, ip, ct);
        if (!verificada.Exito) return verificada;

        var despublicada = await cuenta.DespublicarParaBajaAsync(usuarioId, ct);
        if (!despublicada.Exito) return despublicada;

        var mio = await perfiles.MiPerfilPrestadorAsync(usuarioId, ct);
        Guid? perfilId = mio.Exito ? mio.Valor!.Id : null;

        var ahora = reloj.Ahora;
        var autor = new AutorDeCambio(usuarioId, perfilId);
        // Como cliente y como prestador: MisCitasAsync ya junta las dos.
        foreach (var cita in await agenda.MisCitasAsync(usuarioId, perfilId, ct))
        {
            if (!EsFuturaYActiva(cita, ahora)) continue;
            var r = await agenda.CancelarAsync(new CancelarCitaCmd(
                cita.Id, autor, $"baja-de-cuenta:{cita.Id}", MotivoDeCancelacion), ct);
            if (!r.Exito) return Resultado.Falla(r.Codigo!, r.Mensaje!);
        }

        return await cuenta.EliminarAsync(usuarioId, ip, ct);
    }

    /// <summary>Solicitada o confirmada, y todavía por ocurrir. Lo pasado queda como estaba.</summary>
    private static bool EsFuturaYActiva(CitaDto cita, DateTime ahora) =>
        cita.InicioEn > ahora
        && (cita.Estado == nameof(EstadoCita.Solicitada) || cita.Estado == nameof(EstadoCita.Confirmada));
}
