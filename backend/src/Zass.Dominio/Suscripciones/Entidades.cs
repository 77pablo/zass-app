using Zass.Dominio.Comun;
using Zass.Dominio.Identidad;

namespace Zass.Dominio.Suscripciones;

// S7 · Suscripciones y Pagos.
//
// Quien OFRECE un servicio paga una mensualidad; quien BUSCA no paga nunca.
// La pasarela de pago llega despues: estas tres tablas existen ya para que
// conectarla no obligue a migrar la base ni a tocar el resto del modelo.
//
// Regla de visibilidad: la busqueda solo muestra prestadores con suscripcion
// en estado Prueba o Activa (ver EstaVigente).

/// <summary>TBL-29. Catalogo de planes.</summary>
public class Plan
{
    public short Id { get; set; }
    public string Codigo { get; set; } = "";
    public string Nombre { get; set; } = "";
    public string? Descripcion { get; set; }
    /// <summary>Precio mensual en pesos chilenos. 0 = plan gratuito.</summary>
    public int PrecioMensual { get; set; }
    public short DiasPrueba { get; set; } = 30;
    public int MaxServicios { get; set; } = 10;
    public int MaxTarjetas { get; set; } = 3;
    public bool PermiteCredenciales { get; set; } = true;
    public bool Activo { get; set; } = true;
}

/// <summary>TBL-30. La suscripcion de un prestador. Un prestador, una suscripcion vigente.</summary>
public class Suscripcion
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid PerfilPrestadorId { get; set; }
    public short PlanId { get; set; }
    public EstadoSuscripcion Estado { get; set; } = EstadoSuscripcion.Prueba;
    public DateTime IniciaEn { get; set; } = DateTime.UtcNow;
    /// <summary>Fin del periodo pagado o de la prueba.</summary>
    public DateTime VigenteHasta { get; set; }
    public DateTime? CanceladaEn { get; set; }
    public bool RenovacionAutomatica { get; set; } = true;

    /// <summary>
    /// Identificador del cliente en la pasarela. Nulo hasta que exista la pasarela;
    /// aqui se guardara el id de Transbank, Flow, Mercado Pago o quien sea.
    /// Nunca se guarda un numero de tarjeta en esta base.
    /// </summary>
    public string? ReferenciaPasarela { get; set; }

    public PerfilPrestador? PerfilPrestador { get; set; }
    public Plan? Plan { get; set; }
    public List<Pago> Pagos { get; set; } = new();

    /// <summary>Lo que decide si el prestador aparece en la busqueda.</summary>
    public bool EstaVigente(DateTime ahora) =>
        Estado is EstadoSuscripcion.Prueba or EstadoSuscripcion.Activa
        && VigenteHasta > ahora;

    public bool EnPrueba(DateTime ahora) =>
        Estado == EstadoSuscripcion.Prueba && VigenteHasta > ahora;

    public int DiasRestantes(DateTime ahora) =>
        Math.Max(0, (int)Math.Ceiling((VigenteHasta - ahora).TotalDays));

    public void Vencer()
    {
        Estado = EstadoSuscripcion.Vencida;
        RenovacionAutomatica = false;
    }

    public void Cancelar()
    {
        Estado = EstadoSuscripcion.Cancelada;
        CanceladaEn = DateTime.UtcNow;
        RenovacionAutomatica = false;
    }

    /// <summary>Extiende el periodo tras un pago confirmado. La llamara la pasarela.</summary>
    public void RegistrarPeriodoPagado(DateTime ahora, int meses = 1)
    {
        var baseFecha = VigenteHasta > ahora ? VigenteHasta : ahora;
        VigenteHasta = baseFecha.AddMonths(meses);
        Estado = EstadoSuscripcion.Activa;
    }
}

/// <summary>TBL-31. Historial de cobros. Sin datos de tarjeta: solo la referencia externa.</summary>
public class Pago
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SuscripcionId { get; set; }
    public int Monto { get; set; }
    public string Moneda { get; set; } = "CLP";
    public EstadoPago Estado { get; set; } = EstadoPago.Pendiente;
    public DateTime PeriodoDesde { get; set; }
    public DateTime PeriodoHasta { get; set; }
    /// <summary>Identificador de la transaccion en la pasarela.</summary>
    public string? ReferenciaPasarela { get; set; }
    public string? MotivoFallo { get; set; }
    public DateTime CreadoEn { get; set; } = DateTime.UtcNow;
    public DateTime? PagadoEn { get; set; }

    public Suscripcion? Suscripcion { get; set; }
}

/// <summary>
/// TBL-33. Cupos del plan fundador: exactamente 50 filas, una por numero.
/// perfil_prestador_id nulo = cupo libre. El indice unico parcial
/// (ux_cupo_fundador_perfil) impide que un mismo perfil tome dos cupos.
/// La reserva temporal (reservado_hasta) evita que dos inscripciones
/// simultaneas tomen el mismo cupo; si vence sin pago confirmado, el
/// trabajo diario la libera.
/// </summary>
public class CupoFundador
{
    /// <summary>1..50. PK fija, sembrada con el esquema; nunca se genera.</summary>
    public short Numero { get; set; }

    /// <summary>Perfil que reservo u ocupo el cupo. Nulo = libre.</summary>
    public Guid? PerfilPrestadorId { get; set; }

    /// <summary>Hasta cuando vale la reserva temporal. Con perfil asignado y fecha nula = cupo confirmado.</summary>
    public DateTime? ReservadoHasta { get; set; }

    public PerfilPrestador? PerfilPrestador { get; set; }
}
