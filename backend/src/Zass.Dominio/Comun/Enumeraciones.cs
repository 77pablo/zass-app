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
