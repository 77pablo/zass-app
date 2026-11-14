namespace Zass.Dominio.Comun;

// Los valores se guardan como texto en PostgreSQL, igual que en el modelo documentado.

public enum EstadoCuenta { PendienteVerificacion, Activa, Suspendida, Eliminada }

public enum EstadoPerfil { Borrador, Publicado, Despublicado }

public enum ModoAgenda { Automatico, Manual }

public enum OrigenUbicacion { Geocodificada, Declarada }

public enum TipoCredencial { Titulo, Certificacion, Curso, Colegiatura }

public enum EstadoCredencial { Borrador, EnRevision, Verificada, Rechazada, Vencida }

public enum EstadoDocumento { Subiendo, EnAnalisis, Disponible, RechazadoSeguridad }

public enum ResultadoVerificacion { Verificada, Rechazada, AntecedentesSolicitados }

public enum MotivoRechazo
{
    Ilegible, NoCorresponde, EntidadNoReconocida, DocumentoVencido, SospechaAdulteracion
}

public enum EstadoBloque { Libre, Reservado, Bloqueado, Vencido }

public enum EstadoCita
{
    Solicitada, Confirmada, Rechazada, Cancelada,
    Reprogramada, PendienteAsistencia, Realizada, NoAsistio
}

/// <summary>
/// T10 de estados-cita.md: «Marcar no asistio … **Indica quien falto**». Quien falto a una cita
/// que no ocurrio, segun lo que haya dicho cada parte (decision de Pablo, 11-nov-2026).
///
/// Cada parte solo puede acusar a la OTRA, asi que estos valores no se eligen desde afuera: se
/// deducen de quien hablo. <see cref="Discrepancia"/> NO significa «faltaron los dos»: significa
/// que cada uno acusa al otro y el sistema no sabe cual es verdad. Es un desenlace distinto, y
/// peor, porque no se puede resolver solo mirando la cita.
/// </summary>
public enum FaltaEnCita { Nadie, Cliente, Prestador, Discrepancia }

public enum TipoCambio
{
    Creacion, Confirmacion, Rechazo, Cancelacion, Reprogramacion, Asistencia
}

public enum TipoNotificacion
{
    CorreoVerificacion, RecuperarContrasena, PerfilPublicado,
    CitaSolicitada, CitaConfirmada, CitaRechazada, CitaCancelada, CitaReprogramada,
    Recordatorio24h, Recordatorio2h,
    ResenaNueva, ResenaRespondida, CredencialResuelta, TarjetaActivada,
    SuscripcionPorVencer, SuscripcionVencida
}

public enum CanalNotificacion { Correo, Push }

public enum EstadoEnvio { Programada, Enviando, Enviada, Fallida, Anulada }

public enum OrigenContacto { Ficha, Tarjeta, Busqueda, Cita }

public enum CanalContacto { Telefono, Correo, Whatsapp, Formulario }

public enum TipoResena { Servicio, Inasistencia }

public enum EstadoResena { Publicada, OcultaEnModeracion, Retirada }

public enum MotivoReporte
{
    DatosPersonales, LenguajeDeOdio, Falsedad, ConflictoDeInteres, FueraDeTema
}

public enum EstadoReporte { Abierto, EnRevision, ResueltoMantiene, ResueltoRetira }

public enum EstadoTarjeta { Emitida, Activa, Desactivada, Reemplazada }

public enum MedioEscaneo { Nfc, Qr }

// S7 — Suscripciones. La pasarela de pago llega despues; el modelo ya la espera.
public enum EstadoSuscripcion { Prueba, Activa, Vencida, Cancelada }

public enum EstadoPago { Pendiente, Pagado, Fallido, Reembolsado }

/// <summary>
/// En que quedo un cobro empezado (TBL-36). No es lo mismo que EstadoPago: un intento
/// Fallido NO deja fila en pagos, porque nadie pago nada. Anulado es el que se abandona
/// sin que la pasarela conteste: la persona cerro la pestaña.
/// </summary>
public enum EstadoIntentoPago { Pendiente, Pagado, Fallido, Anulado }

/// <summary>
/// Que se esta haciendo con la pasarela.
///
/// <c>Cobro</c> es plata: se cobra un mes y se extiende la suscripcion.
/// <c>Inscripcion</c> autoriza el medio de pago con monto CERO —lo que hace Oneclick de
/// Transbank— y es lo que confirma el cupo fundador (RN-S7-01) y arranca el mes gratis
/// (RN-S7-03). Una inscripcion no crea fila de pago: no se cobro nada.
/// </summary>
public enum TipoDeIntento { Cobro, Inscripcion }

public static class Roles
{
    public const string Buscador = "buscador";
    public const string Prestador = "prestador";
    public const string Verificador = "verificador";
    public const string Moderador = "moderador";
    public const string Administrador = "administrador";

    public static readonly string[] Internos =
        { Verificador, Moderador, Administrador };
}
