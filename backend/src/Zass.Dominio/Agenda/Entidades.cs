using Zass.Dominio.Catalogo;
using Zass.Dominio.Comun;
using Zass.Dominio.Identidad;

namespace Zass.Dominio.Agenda;

/// <summary>CL-14 · TBL-16. La regla recurrente de atencion.</summary>
public class Disponibilidad
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid PerfilPrestadorId { get; set; }
    public Guid PrestadorServicioId { get; set; }
    /// <summary>1 lunes a 7 domingo.</summary>
    public short DiaSemana { get; set; }
    public TimeOnly HoraInicio { get; set; }
    public TimeOnly HoraFin { get; set; }
    public short DuracionBloqueMin { get; set; } = 30;
    public DateOnly VigenteDesde { get; set; }
    public DateOnly? VigenteHasta { get; set; }
    public bool Activa { get; set; } = true;

    public PerfilPrestador? PerfilPrestador { get; set; }
    public PrestadorServicio? PrestadorServicio { get; set; }

    public bool Solapa(Disponibilidad otra) =>
        PrestadorServicioId == otra.PrestadorServicioId
        && DiaSemana == otra.DiaSemana
        && HoraInicio < otra.HoraFin && otra.HoraInicio < HoraFin;

    /// <summary>
    /// Genera las instancias concretas. La disponibilidad es la regla; el bloque, la
    /// instancia. Cambiar la regla no destruye las citas ya confirmadas (RN-S4-09).
    /// </summary>
    public List<BloqueHorario> GenerarBloques(DateOnly desde, DateOnly hasta)
    {
        var bloques = new List<BloqueHorario>();
        if (!Activa) return bloques;

        for (var dia = desde; dia <= hasta; dia = dia.AddDays(1))
        {
            if ((short)(((int)dia.DayOfWeek + 6) % 7 + 1) != DiaSemana) continue;
            if (dia < VigenteDesde) continue;
            if (VigenteHasta is not null && dia > VigenteHasta) continue;

            var cursor = HoraInicio;
            while (cursor.AddMinutes(DuracionBloqueMin) <= HoraFin)
            {
                var inicio = dia.ToDateTime(cursor, DateTimeKind.Utc);
                bloques.Add(new BloqueHorario
                {
                    DisponibilidadId = Id,
                    PerfilPrestadorId = PerfilPrestadorId,
                    PrestadorServicioId = PrestadorServicioId,
                    InicioEn = inicio,
                    FinEn = inicio.AddMinutes(DuracionBloqueMin),
                    Estado = EstadoBloque.Libre
                });
                cursor = cursor.AddMinutes(DuracionBloqueMin);
            }
        }
        return bloques;
    }
}

/// <summary>CL-15 · TBL-17. Una franja concreta y reservable.</summary>
public class BloqueHorario
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid DisponibilidadId { get; set; }
    public Guid PerfilPrestadorId { get; set; }
    public Guid PrestadorServicioId { get; set; }
    public DateTime InicioEn { get; set; }
    public DateTime FinEn { get; set; }
    public EstadoBloque Estado { get; set; } = EstadoBloque.Libre;

    public PerfilPrestador? PerfilPrestador { get; set; }
    public PrestadorServicio? PrestadorServicio { get; set; }
    public Cita? Cita { get; set; }

    public double AnticipacionHoras(DateTime ahora) => (InicioEn - ahora).TotalHours;
    public bool EstaDisponible(DateTime ahora) =>
        Estado == EstadoBloque.Libre && AnticipacionHoras(ahora) >= 2;

    public void Reservar() => Estado = EstadoBloque.Reservado;
    public void Liberar() => Estado = EstadoBloque.Libre;
    public void Bloquear() => Estado = EstadoBloque.Bloqueado;
    public void Vencer() => Estado = EstadoBloque.Vencido;
}

/// <summary>TBL-20. No modifica los bloques: se cruza al consultar.</summary>
public class BloqueoAgenda
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid PerfilPrestadorId { get; set; }
    public DateTime DesdeEn { get; set; }
    public DateTime HastaEn { get; set; }
    public string? Motivo { get; set; }
}

/// <summary>CL-16 · TBL-18. El compromiso entre dos personas.</summary>
public class Cita
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Codigo { get; set; } = "";
    public Guid BloqueHorarioId { get; set; }
    public Guid BuscadorId { get; set; }
    public Guid PrestadorServicioId { get; set; }
    public Guid PerfilPrestadorId { get; set; }
    /// <summary>La PRIMERA de la cadena de reprogramaciones, no la anterior (RN-S4-05).</summary>
    public Guid? CitaOrigenId { get; set; }
    public EstadoCita Estado { get; set; } = EstadoCita.Solicitada;
    public ModoAgenda Modo { get; set; }
    public string? NotaBuscador { get; set; }
    public DateTime? ExpiraEn { get; set; }
    public string? ClaveIdempotencia { get; set; }
    public int Version { get; set; }
    public DateTime CreadaEn { get; set; } = DateTime.UtcNow;

    public BloqueHorario? BloqueHorario { get; set; }
    public Usuario? Buscador { get; set; }
    public PrestadorServicio? PrestadorServicio { get; set; }
    public PerfilPrestador? PerfilPrestador { get; set; }
    public List<CambioCita> Cambios { get; set; } = new();

    public static string GenerarCodigo()
    {
        const string alfabeto = "ABCDEFGHJKMNPQRSTUVWXYZ23456789";
        var bytes = System.Security.Cryptography.RandomNumberGenerator.GetBytes(4);
        return "ZS-" + new string(bytes.Select(b => alfabeto[b % alfabeto.Length]).ToArray());
    }

    public bool EstaActiva() =>
        Estado is EstadoCita.Solicitada or EstadoCita.Confirmada;

    /// <summary>RN-S4-04: menos de 12 h de anticipacion es cancelacion tardia.</summary>
    public bool EsTardia(DateTime ahora) =>
        BloqueHorario is not null && (BloqueHorario.InicioEn - ahora).TotalHours < 12;

    /// <summary>RN-S4-10. Solo una cita realizada habilita a resenar.</summary>
    public bool HabilitaResena() => Estado == EstadoCita.Realizada;

    public Resultado Confirmar()
    {
        if (Estado != EstadoCita.Solicitada)
            return Resultado.Falla(Fallas.Conflicto, "Esta cita ya no esta pendiente.");
        Estado = EstadoCita.Confirmada;
        ExpiraEn = null;
        return Resultado.Ok();
    }

    public Resultado Rechazar()
    {
        if (Estado != EstadoCita.Solicitada)
            return Resultado.Falla(Fallas.Conflicto, "Esta cita ya no esta pendiente.");
        Estado = EstadoCita.Rechazada;
        return Resultado.Ok();
    }

    public Resultado Cancelar()
    {
        if (!EstaActiva())
            return Resultado.Falla(Fallas.Conflicto, "Esta cita ya no se puede cancelar.");
        Estado = EstadoCita.Cancelada;
        return Resultado.Ok();
    }

    public void MarcarReprogramada() => Estado = EstadoCita.Reprogramada;

    public Resultado RegistrarAsistencia(bool asistio)
    {
        if (Estado != EstadoCita.PendienteAsistencia)
            return Resultado.Falla(Fallas.Conflicto,
                "Solo se registra asistencia despues de la hora de la cita.");
        Estado = asistio ? EstadoCita.Realizada : EstadoCita.NoAsistio;
        return Resultado.Ok();
    }
}

/// <summary>CL-17 · TBL-19. Historial inmutable: solo se inserta, nunca se edita.</summary>
public class CambioCita
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CitaId { get; set; }
    public TipoCambio Tipo { get; set; }
    public EstadoCita? EstadoAnterior { get; set; }
    public EstadoCita EstadoNuevo { get; set; }
    public Guid? RealizadoPor { get; set; }
    public string? Motivo { get; set; }
    public bool EsTardio { get; set; }
    public DateTime OcurridoEn { get; set; } = DateTime.UtcNow;
}
