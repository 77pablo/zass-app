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

    /// <summary>
    /// Con cuanta antelacion se avisa. Dos momentos, como los recordatorios de cita
    /// (RN-S4-07): uno con tiempo para decidir y otro el dia antes, para quien lo dejo pasar.
    ///
    /// Cada umbral manda UN aviso y no mas, porque la clave de idempotencia lo incluye.
    /// </summary>
    public static readonly int[] DiasDeAviso = { 7, 1 };

    private readonly IRepositorioSuscripciones _repo;
    private readonly IUnidadDeTrabajo _trabajo;
    private readonly IRelojSistema _reloj;
    private readonly IAuditor _auditor;
    private readonly INotificaciones _avisos;

    public ServicioSuscripciones(
        IRepositorioSuscripciones repo, IUnidadDeTrabajo trabajo,
        IRelojSistema reloj, IAuditor auditor, INotificaciones avisos)
    {
        _repo = repo;
        _trabajo = trabajo;
        _reloj = reloj;
        _auditor = auditor;
        _avisos = avisos;
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

    public async Task<int> LimiteDeServiciosAsync(
        Guid perfilPrestadorId, CancellationToken ct = default)
    {
        var vigente = await _repo.VigenteDePrestadorAsync(perfilPrestadorId, _reloj.Ahora, ct);
        var plan = vigente?.Plan ?? await _repo.PlanPorCodigoAsync(PlanInicial, ct);
        return plan?.MaxServicios ?? 0;
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

        var duenos = await _repo.UsuariosDePerfilesAsync(
            expiradas.Select(s => s.PerfilPrestadorId).Distinct().ToList(), ct);

        foreach (var s in expiradas)
        {
            s.Vencer();
            _auditor.Registrar(Auditar.Entrada(
                "suscripcion_vencida", "suscripciones", s.Id, ahora,
                contexto: new { vencioEn = s.VigenteHasta }));

            // Quien pierde la visibilidad tiene derecho a saberlo. Si el perfil ya no tiene
            // dueno (cuenta eliminada) no hay a quien avisarle, y vencer sigue siendo correcto.
            if (duenos.TryGetValue(s.PerfilPrestadorId, out var usuarioId))
                _avisos.Encolar(new Notificacion
                {
                    DestinatarioId = usuarioId,
                    Tipo = TipoNotificacion.SuscripcionVencida,
                    ClaveIdempotencia = $"SuscripcionVencida:{s.Id}",
                    Asunto = "Tu suscripción a ZASS! venció",
                    Cuerpo = CargaDeAviso.ConCampos(("dias", "0")),
                    ProgramadaPara = ahora,
                });
        }

        await _trabajo.GuardarAsync(ct);
        return expiradas.Count;
    }

    /// <summary>
    /// Encola el aviso de «te queda poco» para cada umbral de <see cref="DiasDeAviso"/>.
    ///
    /// La clave lleva el umbral, no la fecha: si el trabajo corre dos veces hoy, o dos
    /// replicas lo corren a la vez, la segunda choca con el unico de TBL-27 (R-18, CC-04) y
    /// nadie recibe el correo dos veces. Y como cada umbral es una clave distinta, el aviso
    /// de 7 dias no impide el de 1.
    /// </summary>
    private async Task<int> AvisarPorVencerAsync(CancellationToken ct)
    {
        var mayor = DiasDeAviso.Max();
        var cercanas = await PorVencerAsync(mayor, ct);
        if (cercanas.Count == 0) return 0;

        var duenos = await _repo.UsuariosDePerfilesAsync(
            cercanas.Select(s => s.PerfilPrestadorId).Distinct().ToList(), ct);

        var encolados = 0;
        foreach (var s in cercanas)
        {
            // El umbral que le corresponde hoy es el mas pequeño que ya alcanzo: a 6 dias le
            // toca el de 7, a 1 el de 1. Asi no se salta ninguno si el trabajo no corrio un dia.
            var umbral = DiasDeAviso.Where(d => s.DiasRestantes <= d).DefaultIfEmpty(0).Min();
            if (umbral == 0) continue;
            if (!duenos.TryGetValue(s.PerfilPrestadorId, out var usuarioId)) continue;

            _avisos.Encolar(new Notificacion
            {
                DestinatarioId = usuarioId,
                Tipo = TipoNotificacion.SuscripcionPorVencer,
                ClaveIdempotencia = $"SuscripcionPorVencer:{s.SuscripcionId}:{umbral}",
                Asunto = "Tu suscripción a ZASS! está por vencer",
                Cuerpo = CargaDeAviso.ConCampos(("dias", Math.Max(1, s.DiasRestantes).ToString())),
                ProgramadaPara = _reloj.Ahora,
            });
            encolados++;
        }

        await _trabajo.GuardarAsync(ct);
        return encolados;
    }

    public async Task<ResumenDelDiario> CorrerDiarioAsync(CancellationToken ct = default)
    {
        // Vencer primero porque se lee mejor, NO porque el orden importe: se comprobo
        // invirtiendolo y nada cambia. La razon es que PorVencerAsync solo mira vigentes
        // (estado Prueba|Activa Y fecha futura), asi que una suscripcion pasada de fecha
        // nunca esta «por vencer», se haya marcado ya o no. Si algun dia ese filtro se
        // relaja, el orden SI pasa a importar y esto hay que volver a pensarlo.
        var vencidas = await VencerExpiradasAsync(ct);
        var avisadas = await AvisarPorVencerAsync(ct);
        return new ResumenDelDiario(vencidas, avisadas);
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
