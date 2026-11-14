using Zass.Aplicacion.Puertos;
using Zass.Dominio.Comun;
using Zass.Dominio.Suscripciones;

namespace Zass.Aplicacion.Pruebas.Dobles;

/// <summary>
/// MOD-13 en memoria. No imita a PostgreSQL: el indice unico parcial
/// ux_suscripcion_vigente se comprueba en las pruebas de integracion, contra la base.
///
/// Lleva la cuenta de <see cref="ConsultasEnLote"/> porque hay una prueba que mide
/// justamente eso: que buscar veinte prestadores sea una consulta y no veinte.
/// </summary>
public sealed class RepositorioSuscripcionesEnMemoria : IRepositorioSuscripciones
{
    private readonly BaseEnMemoria _base;
    public RepositorioSuscripcionesEnMemoria(BaseEnMemoria baseEnMemoria) => _base = baseEnMemoria;

    /// <summary>Cuantas veces se pidio el lote. Lo usa la prueba del N+1.</summary>
    public int ConsultasEnLote { get; private set; }

    /// <summary>Lo mismo para los duenos: el trabajo diario tampoco debe preguntar uno por uno.</summary>
    public int ConsultasDeDuenos { get; private set; }

    private static bool Vigente(Suscripcion s, DateTime ahora) =>
        s.Estado is EstadoSuscripcion.Prueba or EstadoSuscripcion.Activa
        && s.VigenteHasta > ahora;

    public Task<Suscripcion?> PorIdAsync(Guid id, CancellationToken ct = default) =>
        Task.FromResult(_base.Suscripciones.FirstOrDefault(s => s.Id == id));

    public Task<Suscripcion?> VigenteDePrestadorAsync(
        Guid perfilPrestadorId, DateTime ahora, CancellationToken ct = default)
    {
        var s = _base.Suscripciones
            .FirstOrDefault(x => x.PerfilPrestadorId == perfilPrestadorId && Vigente(x, ahora));

        // El repositorio real trae el plan con Include; aqui se enlaza a mano para que
        // el servicio vea lo mismo en los dos lados.
        if (s is not null && s.Plan is null)
            s.Plan = _base.Planes.FirstOrDefault(p => p.Id == s.PlanId);

        return Task.FromResult(s);
    }

    public Task<Suscripcion?> UltimaDePrestadorAsync(
        Guid perfilPrestadorId, CancellationToken ct = default)
    {
        var s = _base.Suscripciones
            .Where(x => x.PerfilPrestadorId == perfilPrestadorId)
            .OrderByDescending(x => x.IniciaEn)
            .FirstOrDefault();

        // El repositorio real trae el plan con Include; aqui se enlaza a mano para
        // que el servicio vea lo mismo en los dos lados.
        if (s is not null && s.Plan is null)
            s.Plan = _base.Planes.FirstOrDefault(p => p.Id == s.PlanId);

        return Task.FromResult(s);
    }

    public Task<IReadOnlyCollection<Guid>> VigentesEntreAsync(
        IReadOnlyCollection<Guid> perfilPrestadorIds, DateTime ahora,
        CancellationToken ct = default)
    {
        ConsultasEnLote++;
        var ids = perfilPrestadorIds.ToHashSet();
        IReadOnlyCollection<Guid> vigentes = _base.Suscripciones
            .Where(s => ids.Contains(s.PerfilPrestadorId) && Vigente(s, ahora))
            .Select(s => s.PerfilPrestadorId)
            .Distinct()
            .ToList();
        return Task.FromResult(vigentes);
    }

    public Task<IReadOnlyList<Suscripcion>> ExpiradasSinMarcarAsync(
        DateTime ahora, CancellationToken ct = default)
    {
        IReadOnlyList<Suscripcion> r = _base.Suscripciones
            .Where(s => s.Estado is EstadoSuscripcion.Prueba or EstadoSuscripcion.Activa
                        && s.VigenteHasta <= ahora)
            .ToList();
        return Task.FromResult(r);
    }

    public Task<IReadOnlyList<Suscripcion>> VigentesQueVencenAntesDeAsync(
        DateTime ahora, DateTime hasta, CancellationToken ct = default)
    {
        IReadOnlyList<Suscripcion> r = _base.Suscripciones
            .Where(s => Vigente(s, ahora) && s.VigenteHasta <= hasta)
            .OrderBy(s => s.VigenteHasta)
            .ToList();
        return Task.FromResult(r);
    }

    public Task<Plan?> PlanPorCodigoAsync(string codigo, CancellationToken ct = default) =>
        Task.FromResult(_base.Planes.FirstOrDefault(p => p.Codigo == codigo && p.Activo));

    public Task<IReadOnlyList<Plan>> PlanesActivosAsync(CancellationToken ct = default)
    {
        IReadOnlyList<Plan> r = _base.Planes.Where(p => p.Activo).OrderBy(p => p.PrecioMensual).ToList();
        return Task.FromResult(r);
    }

    public Task<bool> ExistePerfilPrestadorAsync(
        Guid perfilPrestadorId, CancellationToken ct = default) =>
        Task.FromResult(_base.PerfilesPrestador.Any(p => p.Id == perfilPrestadorId));

    public Task<IReadOnlyDictionary<Guid, Guid>> UsuariosDePerfilesAsync(
        IReadOnlyCollection<Guid> perfilPrestadorIds, CancellationToken ct = default)
    {
        ConsultasDeDuenos++;
        var ids = perfilPrestadorIds.ToHashSet();
        IReadOnlyDictionary<Guid, Guid> r = _base.PerfilesPrestador
            .Where(p => ids.Contains(p.Id))
            .ToDictionary(p => p.Id, p => p.UsuarioId);
        return Task.FromResult(r);
    }

    public void Agregar(Suscripcion suscripcion) => _base.Suscripciones.Add(suscripcion);
    public void AgregarPago(Pago pago) => _base.Pagos.Add(pago);
    // ------------------------------------------------------------ Cupos fundador
    //
    // Misma logica que las sentencias SQL del repositorio real. El lock imita lo unico que
    // la base garantiza de verdad: que dos llamadas no se lleven el mismo cupo. La
    // concurrencia real se prueba en integracion, contra PostgreSQL.

    private static bool Libre(CupoFundador c, DateTime ahora) =>
        c.PerfilPrestadorId is null
        || (c.ReservadoHasta is not null && c.ReservadoHasta <= ahora);

    public Task<int> CuposFundadorDisponiblesAsync(DateTime ahora, CancellationToken ct = default)
    {
        lock (_base.CuposFundador)
        {
            return Task.FromResult(_base.CuposFundador.Count(c => Libre(c, ahora)));
        }
    }

    public Task<short?> IntentarReservarCupoFundadorAsync(
        Guid perfilPrestadorId, DateTime ahora, DateTime hasta, CancellationToken ct = default)
    {
        lock (_base.CuposFundador)
        {
            var propio = _base.CuposFundador.FirstOrDefault(c => c.PerfilPrestadorId == perfilPrestadorId);
            if (propio is not null)
            {
                // Confirmado (sin plazo) no se toca; reservado se renueva.
                if (propio.ReservadoHasta is not null) propio.ReservadoHasta = hasta;
                return Task.FromResult<short?>(propio.Numero);
            }

            var libre = _base.CuposFundador.OrderBy(c => c.Numero).FirstOrDefault(c => Libre(c, ahora));
            if (libre is null) return Task.FromResult<short?>(null);

            libre.PerfilPrestadorId = perfilPrestadorId;
            libre.ReservadoHasta = hasta;
            return Task.FromResult<short?>(libre.Numero);
        }
    }

    public Task<bool> ConfirmarCupoFundadorAsync(Guid perfilPrestadorId, CancellationToken ct = default)
    {
        lock (_base.CuposFundador)
        {
            var propio = _base.CuposFundador.FirstOrDefault(c => c.PerfilPrestadorId == perfilPrestadorId);
            if (propio is null) return Task.FromResult(false);
            propio.ReservadoHasta = null;
            return Task.FromResult(true);
        }
    }

    public Task<bool> LiberarCupoFundadorAsync(Guid perfilPrestadorId, CancellationToken ct = default)
    {
        lock (_base.CuposFundador)
        {
            var propio = _base.CuposFundador.FirstOrDefault(c => c.PerfilPrestadorId == perfilPrestadorId);
            if (propio is null) return Task.FromResult(false);
            propio.PerfilPrestadorId = null;
            propio.ReservadoHasta = null;
            return Task.FromResult(true);
        }
    }

    // ------------------------------------------------------- TBL-36 intentos de pago
    //
    // Los nombres y la semantica se copiaron de RepositorioSuscripciones.cs, no se
    // inventaron: un doble que significa otra cosa que la consulta real hace pasar
    // pruebas que no valen.

    public Task<IntentoDePago?> IntentoPorIdAsync(Guid id, CancellationToken ct = default) =>
        Task.FromResult(_base.IntentosDePago.FirstOrDefault(i => i.Id == id));

    public Task<IntentoDePago?> IntentoPorClaveAsync(
        string clave, CancellationToken ct = default) =>
        Task.FromResult(_base.IntentosDePago.FirstOrDefault(i => i.ClaveIdempotencia == clave));

    /// <summary>
    /// En memoria no hay <c>FOR UPDATE</c>: devuelve el mismo objeto. Que la segunda
    /// confirmacion ESPERE de verdad en el bloqueo de fila solo lo puede probar
    /// integracion, contra PostgreSQL.
    /// </summary>
    public Task<IntentoDePago?> TomarIntentoAsync(Guid id, CancellationToken ct = default) =>
        IntentoPorIdAsync(id, ct);

    public void AgregarIntento(IntentoDePago intento) => _base.IntentosDePago.Add(intento);

    /// <summary>
    /// Igual que la consulta EF: se mira la tabla de PAGOS y no el estado de la
    /// suscripcion, porque una Activa pudo llegar ahi sin pagar.
    /// </summary>
    public Task<bool> TuvoMesPagadoAsync(
        Guid perfilPrestadorId, CancellationToken ct = default) =>
        Task.FromResult(_base.Pagos.Any(
            p => p.Estado == EstadoPago.Pagado
                 && _base.Suscripciones.Any(
                     s => s.Id == p.SuscripcionId && s.PerfilPrestadorId == perfilPrestadorId)));

    // ------------------------------------------------------------ TBL-34/35 el cupon

    public Task<Cupon?> CuponPorCodigoAsync(string codigo, CancellationToken ct = default) =>
        Task.FromResult(_base.Cupones.FirstOrDefault(c => c.Codigo == codigo));

    public Task<bool> YaCanjeoCuponAsync(
        Guid perfilPrestadorId, CancellationToken ct = default) =>
        Task.FromResult(_base.Canjes.Any(c => c.PerfilPrestadorId == perfilPrestadorId));

    public void AgregarCanje(CanjeDeCupon canje) => _base.Canjes.Add(canje);

    // ------------------------------------------------------------ Planes y cupos

    public Task<Plan?> PlanPorIdAsync(short id, CancellationToken ct = default) =>
        Task.FromResult(_base.Planes.FirstOrDefault(p => p.Id == id));

    public Task<int> CuposFundadorTotalesAsync(CancellationToken ct = default) =>
        Task.FromResult(_base.CuposFundador.Count);

    /// <summary>
    /// El numero del cupo de ese perfil, reservado o confirmado. Nulo si no tiene. Igual
    /// que la consulta EF: no distingue estado, solo pertenencia.
    /// </summary>
    public Task<short?> CupoFundadorDeAsync(
        Guid perfilPrestadorId, CancellationToken ct = default)
    {
        lock (_base.CuposFundador)
        {
            var propio = _base.CuposFundador
                .FirstOrDefault(c => c.PerfilPrestadorId == perfilPrestadorId);
            return Task.FromResult(propio is null ? (short?)null : propio.Numero);
        }
    }

    public Task<int> LiberarReservasFundadorVencidasAsync(DateTime ahora, CancellationToken ct = default)
    {
        lock (_base.CuposFundador)
        {
            var vencidos = _base.CuposFundador
                .Where(c => c.PerfilPrestadorId is not null
                            && c.ReservadoHasta is not null
                            && c.ReservadoHasta <= ahora)
                .ToList();
            foreach (var c in vencidos)
            {
                c.PerfilPrestadorId = null;
                c.ReservadoHasta = null;
            }
            return Task.FromResult(vencidos.Count);
        }
    }
}
