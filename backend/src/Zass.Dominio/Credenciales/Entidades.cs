using Zass.Dominio.Comun;
using Zass.Dominio.Identidad;

namespace Zass.Dominio.Credenciales;

/// <summary>CL-12 · TBL-14.</summary>
public class EntidadCertificadora
{
    public short Id { get; set; }
    public string Nombre { get; set; } = "";
    public string? UrlRegistroPublico { get; set; }
    public bool Catalogada { get; set; } = true;
    public bool Activa { get; set; } = true;
}

/// <summary>CL-09 · TBL-12. Lo que el prestador afirma haber obtenido.</summary>
public class Credencial
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid PerfilPrestadorId { get; set; }
    public short? EntidadCertificadoraId { get; set; }
    public TipoCredencial Tipo { get; set; }
    public string Nombre { get; set; } = "";
    public DateOnly ObtenidaEn { get; set; }
    public DateOnly? VenceEn { get; set; }
    public EstadoCredencial Estado { get; set; } = EstadoCredencial.Borrador;
    public DateTime? SelloEn { get; set; }
    public Guid? SelloPor { get; set; }
    public DateTime CreadaEn { get; set; } = DateTime.UtcNow;

    // Reserva de la cola de revision (CC-03).
    public Guid? ReservadaPor { get; set; }
    public DateTime? ReservadaHasta { get; set; }
    public bool EsperandoPrestador { get; set; }

    public PerfilPrestador? PerfilPrestador { get; set; }
    public EntidadCertificadora? EntidadCertificadora { get; set; }
    public List<DocumentoCredencial> Documentos { get; set; } = new();
    public List<Verificacion> Verificaciones { get; set; } = new();

    public bool TieneRespaldo() =>
        Documentos.Any(d => d.Estado == EstadoDocumento.Disponible);

    /// <summary>
    /// El publico solo ve dos etiquetas. Que sea un metodo impide que alguien
    /// serialice el estado interno por error y filtre que fue rechazada.
    /// </summary>
    public string EtiquetaPublica() => Estado == EstadoCredencial.Verificada
        ? "Verificada"
        : "Declarada por el usuario";

    public Resultado EnviarARevision()
    {
        if (Estado is not (EstadoCredencial.Borrador or EstadoCredencial.Rechazada
            or EstadoCredencial.Vencida))
            return Resultado.Falla(Fallas.Conflicto, "Esta credencial ya esta en revision.");
        if (!TieneRespaldo())
            return Resultado.Falla(Fallas.ReglaDeNegocio,
                "Sube al menos un documento de respaldo antes de enviar a revision.");
        Estado = EstadoCredencial.EnRevision;
        EsperandoPrestador = false;
        return Resultado.Ok();
    }

    public void Verificar(Guid verificadorId)
    {
        Estado = EstadoCredencial.Verificada;
        SelloPor = verificadorId;
        SelloEn = DateTime.UtcNow;
        LiberarReserva();
    }

    public void Rechazar()
    {
        Estado = EstadoCredencial.Rechazada;
        SelloPor = null;
        SelloEn = null;
        LiberarReserva();
    }

    public void Revocar()
    {
        Estado = EstadoCredencial.Vencida;
        LiberarReserva();
    }

    public bool ReservaVencida(DateTime ahora) =>
        ReservadaHasta is null || ReservadaHasta < ahora;

    public void Reservar(Guid verificadorId, DateTime hasta)
    {
        ReservadaPor = verificadorId;
        ReservadaHasta = hasta;
    }

    public void LiberarReserva()
    {
        ReservadaPor = null;
        ReservadaHasta = null;
    }
}

/// <summary>CL-10 · TBL-13. Accesible solo por URL firmada de corta duracion.</summary>
public class DocumentoCredencial
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CredencialId { get; set; }
    public string ClaveAlmacenamiento { get; set; } = "";
    public string NombreOriginal { get; set; } = "";
    public string TipoMime { get; set; } = "";
    public long TamanoBytes { get; set; }
    public EstadoDocumento Estado { get; set; } = EstadoDocumento.Subiendo;
    public DateTime SubidoEn { get; set; } = DateTime.UtcNow;

    public Credencial? Credencial { get; set; }
}

/// <summary>CL-11 · TBL-15. Historial de decisiones: una credencial puede tener varias.</summary>
public class Verificacion
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CredencialId { get; set; }
    public Guid VerificadorId { get; set; }
    public ResultadoVerificacion Resultado { get; set; }
    public MotivoRechazo? Motivo { get; set; }
    public string? Detalle { get; set; }
    public DateOnly? VigenciaHasta { get; set; }
    public DateTime ResueltaEn { get; set; } = DateTime.UtcNow;

    public Credencial? Credencial { get; set; }
    public bool EsRechazo() => Resultado == ResultadoVerificacion.Rechazada;
}
