using Zass.Aplicacion.Comun;
using Zass.Aplicacion.Puertos;
using Zass.Dominio.Comun;
using Zass.Dominio.Suscripciones;

namespace Zass.Aplicacion.Suscripciones;

/// <summary>
/// MOD-13 - S7. Planes, vigencia y cobros.
///
/// La pasarela de pago no existe todavia. Este servicio esta escrito para que, cuando
/// llegue, solo tenga que llamar a <see cref="RegistrarPeriodoPagadoAsync"/>: ninguna
/// regla de vigencia vive fuera de aqui.
/// </summary>
public sealed class ServicioSuscripciones : IServicioSuscripciones
{
    /// <summary>Plan con el que arranca quien publica su perfil por primera vez.</summary>
    public const string PlanInicial = "prueba";

    private readonly IRepositorioSuscripciones _repo;
    private readonly IUnidadDeTrabajo _trabajo;
    private readonly IRelojSistema _reloj;
    private readonly IAuditor _auditor;

    public ServicioSuscripciones(
        IRepositorioSuscripciones repo, IUnidadDeTrabajo trabajo,
        IRelojSistema reloj, IAuditor auditor)
    {
        _repo = repo;
        _trabajo = trabajo;
        _reloj = reloj;
        _auditor = auditor;
    }

    // ------------------------------------------------------------- Consultas

    public async Task<bool> EstaVigenteAsync(Guid perfilPrestadorId, CancellationToken ct = default)
    {
        var s = await _repo.VigenteDePrestadorAsync(perfilPrestadorId, _reloj.Ahora, ct);
        return s is not null;
    }

    public async Task<IReadOnlyCollection<Guid>> VigentesEntreAsync(
        IReadOnlyCollection<Guid> perfilPrestadorIds, CancellationToken ct = default)
    {
        // Una lista vacia no es una consulta: es nada. Mandarla igual produce un
        // "WHERE id IN ()" que el motor tiene que planificar para no devolver nada.
        if (perfilPrestadorIds.Count == 0) return Array.Empty<Guid>();

        return await _repo.VigentesEntreAsync(perfilPrestadorIds, _reloj.Ahora, ct);
    }

    public async Task<Resultado<SuscripcionDto>> DePrestadorAsync(
        Guid perfilPrestadorId, CancellationToken ct = default)
    {
        var ahora = _reloj.Ahora;
        var s = await _repo.UltimaDePrestadorAsync(perfilPrestadorId, ct);

        // No tener suscripcion es un estado normal, no un error: el perfil recien
        // creado todavia no publico. Devolver 404 obligaria a cada pantalla a tratar
        // el caso como excepcion.
        if (s is null)
            return Resultado.Ok(new SuscripcionDto(
                Plan: "ninguno", Estado: "sin suscripcion", Vigente: false,
                EnPrueba: false, VigenteHasta: null, DiasRestantes: 0,
                RenovacionAutomatica: false));

        return Resultado.Ok(new SuscripcionDto(
            Plan: s.Plan?.Nombre ?? "desconocido",
            Estado: s.Estado.ToString(),
            Vigente: s.EstaVigente(ahora),
            EnPrueba: s.EnPrueba(ahora),
            VigenteHasta: s.VigenteHasta,
            DiasRestantes: s.EstaVigente(ahora) ? s.DiasRestantes(ahora) : 0,
            RenovacionAutomatica: s.RenovacionAutomatica));
    }

    public async Task<IReadOnlyList<PlanDto>> PlanesActivosAsync(CancellationToken ct = default)
    {
        var planes = await _repo.PlanesActivosAsync(ct);
        return planes
            .Select(p => new PlanDto(
                p.Id, p.Codigo, p.Nombre, p.Descripcion, p.PrecioMensual,
                p.DiasPrueba, p.MaxServicios, p.MaxTarjetas, p.PermiteCredenciales))
            .ToList();
    }

    // ------------------------------------------------------------- Ciclo de vida

    public async Task<Resultado> IniciarPruebaAsync(
        Guid perfilPrestadorId, CancellationToken ct = default)
    {
        var ahora = _reloj.Ahora;

        if (!await _repo.ExistePerfilPrestadorAsync(perfilPrestadorId, ct))
            return Resultado.Falla(Fallas.NoEncontrado, "Ese perfil de prestador no existe.");

        // Idempotente. El indice unico parcial ux_suscripcion_vigente lo impide igual,
        // pero llegar hasta el con un 23505 convertiria un doble clic en un error 500.
        var yaTiene = await _repo.VigenteDePrestadorAsync(perfilPrestadorId, ahora, ct);
        if (yaTiene is not null) return Resultado.Ok();

        var plan = await _repo.PlanPorCodigoAsync(PlanInicial, ct);
        if (plan is null)
            return Resultado.Falla(
                Fallas.ReglaDeNegocio,
                $"No existe el plan inicial '{PlanInicial}'. Falta sembrarlo en la base.");

        var suscripcion = new Suscripcion
        {
            PerfilPrestadorId = perfilPrestadorId,
            PlanId = plan.Id,
            Estado = EstadoSuscripcion.Prueba,
            IniciaEn = ahora,
            VigenteHasta = ahora.AddDays(plan.DiasPrueba),
            RenovacionAutomatica = false,
        };
        _repo.Agregar(suscripcion);

        _auditor.Registrar(Auditar.Entrada(
            "suscripcion_prueba_iniciada", "suscripciones", suscripcion.Id, ahora,
            contexto: new { plan = plan.Codigo, dias = plan.DiasPrueba }));

        await _trabajo.GuardarAsync(ct);
        return Resultado.Ok();
    }

    public async Task<Resultado> RegistrarPeriodoPagadoAsync(
        Guid suscripcionId, int meses = 1, int montoPagado = 0, string? referencia = null,
        CancellationToken ct = default)
    {
        var ahora = _reloj.Ahora;
        var s = await _repo.PorIdAsync(suscripcionId, ct);
        if (s is null)
            return Resultado.Falla(Fallas.NoEncontrado, "Esa suscripción no existe.");

        var desde = s.VigenteHasta > ahora ? s.VigenteHasta : ahora;

        // Lo hace la entidad, no este servicio: quien paga el dia 25 de un mes de 30
        // no puede perder los 5 que le quedaban, y esa regla pertenece al dominio.
        s.RegistrarPeriodoPagado(ahora, meses);

        _repo.AgregarPago(new Pago
        {
            SuscripcionId = s.Id,
            Monto = montoPagado,
            Moneda = "CLP",
            Estado = EstadoPago.Pagado,
            PeriodoDesde = desde,
            PeriodoHasta = s.VigenteHasta,
            ReferenciaPasarela = referencia,
            CreadoEn = ahora,
            PagadoEn = ahora,
        });

        _auditor.Registrar(Auditar.Entrada(
            "suscripcion_pagada", "suscripciones", s.Id, ahora,
            contexto: new { meses, vigenteHasta = s.VigenteHasta }));

        await _trabajo.GuardarAsync(ct);
        return Resultado.Ok();
    }

    public async Task<Resultado> CancelarAsync(Guid suscripcionId, CancellationToken ct = default)
    {
        var ahora = _reloj.Ahora;
        var s = await _repo.PorIdAsync(suscripcionId, ct);
        if (s is null)
            return Resultado.Falla(Fallas.NoEncontrado, "Esa suscripción no existe.");

        s.Estado = EstadoSuscripcion.Cancelada;
        s.CanceladaEn = ahora;
        s.RenovacionAutomatica = false;

        _auditor.Registrar(Auditar.Entrada(
            "suscripcion_cancelada", "suscripciones", s.Id, ahora));

        await _trabajo.GuardarAsync(ct);
        return Resultado.Ok();
    }

    public async Task<int> VencerExpiradasAsync(CancellationToken ct = default)
    {
        var ahora = _reloj.Ahora;
        var expiradas = await _repo.ExpiradasSinMarcarAsync(ahora, ct);
        if (expiradas.Count == 0) return 0;

        foreach (var s in expiradas)
        {
            s.Vencer();
            _auditor.Registrar(Auditar.Entrada(
                "suscripcion_vencida", "suscripciones", s.Id, ahora,
                contexto: new { vencioEn = s.VigenteHasta }));
        }

        await _trabajo.GuardarAsync(ct);
        return expiradas.Count;
    }

    public async Task<IReadOnlyList<SuscripcionPorVencer>> PorVencerAsync(
        int dentroDeDias, CancellationToken ct = default)
    {
        var ahora = _reloj.Ahora;
        var hasta = ahora.AddDays(dentroDeDias);
        var cercanas = await _repo.VigentesQueVencenAntesDeAsync(ahora, hasta, ct);

        return cercanas
            .Select(s => new SuscripcionPorVencer(
                s.Id, s.PerfilPrestadorId, s.VigenteHasta, s.DiasRestantes(ahora)))
            .ToList();
    }
}
