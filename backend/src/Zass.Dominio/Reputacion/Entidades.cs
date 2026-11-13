using Zass.Dominio.Agenda;
using Zass.Dominio.Comun;
using Zass.Dominio.Identidad;

namespace Zass.Dominio.Reputacion;

/// <summary>
/// CL-19 · TBL-21. La prueba de interaccion.
/// Es la UNICA puerta a la elegibilidad de resena.
/// </summary>
public class Contacto
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid BuscadorId { get; set; }
    public Guid PerfilPrestadorId { get; set; }
    public Guid? CitaId { get; set; }
    public Guid? EscaneoId { get; set; }
    public OrigenContacto Origen { get; set; }
    public CanalContacto Canal { get; set; }
    public DateTime OcurridoEn { get; set; } = DateTime.UtcNow;

    public Usuario? Buscador { get; set; }
    public PerfilPrestador? PerfilPrestador { get; set; }
    public Cita? Cita { get; set; }
    public Resena? Resena { get; set; }

    public int DiasTranscurridos(DateTime ahora) => (int)(ahora - OcurridoEn).TotalDays;

    // Aqui vivia `HabilitaResena(ahora)`, que decia llamarse «la regla completa de elegibilidad»
    // y no lo era: no tenia un solo llamador, y respondia que si ante CUALQUIER `no_asistio`,
    // justo lo contrario de lo que respondia la regla viva. Se borro. La regla completa es
    // `ReglasDeResenas.EvaluarContacto`, y es la unica.
}

/// <summary>CL-20 · TBL-22. Opinion con calificacion.</summary>
public class Resena
{
    public Guid Id { get; set; } = Guid.NewGuid();
    /// <summary>Obligatorio y unico (R-11). Media defensa de la elegibilidad.</summary>
    public Guid ContactoId { get; set; }
    public Guid PerfilPrestadorId { get; set; }
    public Guid AutorId { get; set; }
    public short Calificacion { get; set; }
    /// <summary>Se guarda crudo y se escapa al renderizar (RN-S5-04).</summary>
    public string Comentario { get; set; } = "";
    public TipoResena Tipo { get; set; } = TipoResena.Servicio;
    public EstadoResena Estado { get; set; } = EstadoResena.Publicada;
    public List<string> Aspectos { get; set; } = new();
    public bool Editada { get; set; }
    public DateTime PublicadaEn { get; set; } = DateTime.UtcNow;
    public DateTime EditableHasta { get; set; } = DateTime.UtcNow.AddHours(72);

    public Contacto? Contacto { get; set; }
    public PerfilPrestador? PerfilPrestador { get; set; }
    public Usuario? Autor { get; set; }
    public RespuestaResena? Respuesta { get; set; }
    public List<ReporteResena> Reportes { get; set; } = new();

    public bool PuedeEditarse(DateTime ahora) =>
        Estado == EstadoResena.Publicada && ahora <= EditableHasta;

    public bool EsVisible() => Estado == EstadoResena.Publicada;

    public void Ocultar() => Estado = EstadoResena.OcultaEnModeracion;
    public void Restaurar() => Estado = EstadoResena.Publicada;
    public void Retirar() => Estado = EstadoResena.Retirada;

    // No existe metodo Eliminar: el prestador no puede borrar resenas, ni en el modelo.
}

/// <summary>CL-21 · TBL-23. Derecho a replica. Una por resena (R-14).</summary>
public class RespuestaResena
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ResenaId { get; set; }
    public string Texto { get; set; } = "";
    public DateTime PublicadaEn { get; set; } = DateTime.UtcNow;
    public DateTime? EditadaEn { get; set; }

    public Resena? Resena { get; set; }
}

/// <summary>CL-22 · TBL-24.</summary>
public class ReporteResena
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ResenaId { get; set; }
    public Guid ReportadoPor { get; set; }
    public Guid? ResueltoPor { get; set; }
    public MotivoReporte Motivo { get; set; }
    public string? Detalle { get; set; }
    public EstadoReporte Estado { get; set; } = EstadoReporte.Abierto;
    public string? Resolucion { get; set; }
    public DateTime CreadoEn { get; set; } = DateTime.UtcNow;
    public DateTime? ResueltoEn { get; set; }

    public Resena? Resena { get; set; }

    /// <summary>RN-S5-08: un motivo grave oculta la resena antes de que un humano la vea.</summary>
    public bool EsGrave() =>
        Motivo is MotivoReporte.DatosPersonales or MotivoReporte.LenguajeDeOdio;
}
