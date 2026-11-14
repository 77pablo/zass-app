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

    /// <summary>Plan de lanzamiento, el de los 50 cupos. Precio congelado en la fila del plan.</summary>
    public const string PlanFundador = "fundador";

    /// <summary>El plan de siempre, al que puede pasarse quien no alcanzo cupo fundador.</summary>
    public const string PlanMensual = "mensual";

    /// <summary>
    /// Con cuanta antelacion se avisa. Dos momentos, como los recordatorios de cita
    /// (RN-S4-07): uno con tiempo para decidir y otro el dia antes, para quien lo dejo pasar.
    ///
    /// Cada umbral manda UN aviso y no mas, porque la clave de idempotencia lo incluye.
    /// </summary>
    public static readonly int[] DiasDeAviso = { 7, 1 };
    /// <summary>
    /// Cuanto se guarda un cupo fundador mientras la persona inscribe su tarjeta. Pasado
    /// ese plazo sin confirmar, el cupo vuelve a estar disponible para otra persona.
    /// </summary>
    public static readonly TimeSpan DuracionReservaPorDefecto = TimeSpan.FromMinutes(25);

    /// <summary>
    /// RN-S7-03. El mes gratis del fundador, en dias.
    ///
    /// Vive aqui y NO en <c>dias_prueba</c> del plan, que es 0 explicito en los dos planes
    /// de pago: el mes gratis es del camino del fundador, no una propiedad del plan.
    /// </summary>
    public const int DiasDeMesGratis = 30;

    /// <summary>Tope de una reserva: evita que un llamado erroneo deje un cupo bloqueado dias.</summary>
    private static readonly TimeSpan DuracionReservaMaxima = TimeSpan.FromHours(24);

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

    public async Task<Resultado<Guid>> RegistrarPeriodoPagadoAsync(
        Guid suscripcionId, int meses = 1, int montoPagado = 0, string? referencia = null,
        Guid? intentoDePagoId = null, CancellationToken ct = default)
    {
        var ahora = _reloj.Ahora;
        var s = await _repo.PorIdAsync(suscripcionId, ct);
        if (s is null)
            return Resultado.Falla<Guid>(Fallas.NoEncontrado, "Esa suscripción no existe.");

        if (meses < 1)
            return Resultado.Falla<Guid>(
                Fallas.Validacion, "Un periodo pagado es de un mes o más.");

        var desde = s.VigenteHasta > ahora ? s.VigenteHasta : ahora;

        // Lo hace la entidad, no este servicio: quien paga el dia 25 de un mes de 30
        // no puede perder los 5 que le quedaban, y esa regla pertenece al dominio.
        s.RegistrarPeriodoPagado(ahora, meses);

        var pago = new Pago
        {
            SuscripcionId = s.Id,
            Monto = montoPagado,
            Moneda = "CLP",
            Estado = EstadoPago.Pagado,
            PeriodoDesde = desde,
            PeriodoHasta = s.VigenteHasta,
            ReferenciaPasarela = referencia,
            IntentoDePagoId = intentoDePagoId,
            CreadoEn = ahora,
            PagadoEn = ahora,
        };
        _repo.AgregarPago(pago);

        _auditor.Registrar(Auditar.Entrada(
            "suscripcion_pagada", "suscripciones", s.Id, ahora,
            contexto: new { meses, vigenteHasta = s.VigenteHasta }));

        await _trabajo.GuardarAsync(ct);
        return Resultado.Ok(pago.Id);
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
        // Reservas de cupo fundador que vencieron sin confirmarse: el cupo vuelve a quedar
        // libre. Las confirmadas no se tocan. No cambia ResumenDelDiario a proposito.
        await _repo.LiberarReservasFundadorVencidasAsync(_reloj.Ahora, ct);
        return new ResumenDelDiario(vencidas, avisadas);
    }

    // ------------------------------------------------------------- Cupos fundador

    public Task<int> CuposFundadorDisponiblesAsync(CancellationToken ct = default) =>
        _repo.CuposFundadorDisponiblesAsync(_reloj.Ahora, ct);

    public async Task<Resultado<PlanFundadorDto>> EstadoDelPlanFundadorAsync(
        CancellationToken ct = default)
    {
        var plan = await _repo.PlanPorCodigoAsync(PlanFundador, ct);
        if (plan is null)
            return Resultado.Falla<PlanFundadorDto>(
                Fallas.ReglaDeNegocio,
                $"No existe el plan '{PlanFundador}'. Falta sembrarlo en la base.");

        var disponibles = await _repo.CuposFundadorDisponiblesAsync(_reloj.Ahora, ct);

        // El total no se lee de una constante que ademas vigile la prueba: se cuenta lo
        // sembrado. Si alguien cambiara los cupos de la base sin migracion, esto lo dice.
        var totales = await _repo.CuposFundadorTotalesAsync(ct);

        return Resultado.Ok(new PlanFundadorDto(
            Abierto: disponibles > 0,
            CuposTotales: totales,
            CuposDisponibles: disponibles,
            PrecioMensual: plan.PrecioMensual,
            Mensaje: disponibles > 0
                ? $"Quedan {disponibles} de {totales} cupos del precio de lanzamiento."
                : $"El plan fundador está cerrado: se tomaron los {totales} cupos. "
                  + "El precio de lanzamiento no vuelve a abrirse."));
    }

    public async Task<Resultado<short>> ReservarCupoFundadorAsync(
        Guid perfilPrestadorId, TimeSpan? duracion = null, CancellationToken ct = default)
    {
        var plazo = duracion ?? DuracionReservaPorDefecto;
        if (plazo <= TimeSpan.Zero || plazo > DuracionReservaMaxima)
            return Resultado.Falla<short>(
                Fallas.Validacion, "La duracion de la reserva no es valida.");

        if (!await _repo.ExistePerfilPrestadorAsync(perfilPrestadorId, ct))
            return Resultado.Falla<short>(Fallas.NoEncontrado, "Ese perfil de prestador no existe.");

        var ahora = _reloj.Ahora;
        var numero = await _repo.IntentarReservarCupoFundadorAsync(
            perfilPrestadorId, ahora, ahora.Add(plazo), ct);

        // El cierre, dicho entero y en una sola frase. Antes esto era «Ya no quedan cupos
        // del plan fundador», que deja a quien lo lee esperando una lista de espera o un
        // paso automatico al plan normal. No hay ni lo uno ni lo otro (decision de Pablo,
        // 12-nov-2026): quien quiera el plan mensual lo elige a mano.
        if (numero is null)
            return Resultado.Falla<short>(
                Fallas.Conflicto,
                "El plan fundador está cerrado: se tomaron todos los cupos y el precio de "
                + "lanzamiento no vuelve a abrirse. Puedes suscribirte al plan mensual.");

        _auditor.Registrar(Auditar.Entrada(
            "cupo_fundador_reservado", "perfiles_prestador", perfilPrestadorId, ahora,
            contexto: new { cupo = numero.Value, minutos = (int)plazo.TotalMinutes }));
        await _trabajo.GuardarAsync(ct);

        return Resultado.Ok(numero.Value);
    }

    public async Task<Resultado> ConfirmarCupoFundadorAsync(
        Guid perfilPrestadorId, CancellationToken ct = default)
    {
        var confirmado = await _repo.ConfirmarCupoFundadorAsync(perfilPrestadorId, ct);
        if (!confirmado)
            return Resultado.Falla(
                Fallas.Conflicto, "Este perfil no tiene un cupo fundador que confirmar.");

        _auditor.Registrar(Auditar.Entrada(
            "cupo_fundador_confirmado", "perfiles_prestador", perfilPrestadorId, _reloj.Ahora));
        await _trabajo.GuardarAsync(ct);
        return Resultado.Ok();
    }

    public async Task<Resultado> LiberarCupoFundadorAsync(
        Guid perfilPrestadorId, CancellationToken ct = default)
    {
        // Idempotente: liberar lo que no se tiene no es un error de nadie.
        var libero = await _repo.LiberarCupoFundadorAsync(perfilPrestadorId, ct);
        if (!libero) return Resultado.Ok();

        _auditor.Registrar(Auditar.Entrada(
            "cupo_fundador_liberado", "perfiles_prestador", perfilPrestadorId, _reloj.Ahora));
        await _trabajo.GuardarAsync(ct);
        return Resultado.Ok();
    }

    public async Task<Resultado<DateTime>> PasarAFundadorConMesGratisAsync(
        Guid suscripcionId, string? medioInscrito = null, CancellationToken ct = default)
    {
        var s = await _repo.PorIdAsync(suscripcionId, ct);
        if (s is null)
            return Resultado.Falla<DateTime>(Fallas.NoEncontrado, "Esa suscripcion no existe.");

        var plan = await _repo.PlanPorCodigoAsync(PlanFundador, ct);
        if (plan is null || !plan.Activo)
            return Resultado.Falla<DateTime>(
                Fallas.ReglaDeNegocio, "El plan fundador no esta disponible.");

        var mesGratis = _reloj.Ahora.AddDays(DiasDeMesGratis);

        s.PlanId = plan.Id;
        s.Estado = EstadoSuscripcion.Prueba;
        s.CanceladaEn = null;

        // El medio de pago inscrito, en la SUSCRIPCION y no solo en el intento. Sin esta
        // linea, el cobro automatico del dia 31 (C3) lee la suscripcion, no encuentra con
        // que cobrar, y no le cobra a NADIE: la inscripcion servia para tomar el cupo y
        // para nada mas. Lo encontro C3 al construir el cobro, no una prueba de C1.
        //
        // Ojo con lo que se guarda: la pasarela simulada devuelve la referencia de la
        // transaccion, y con esa se puede cobrar. Una pasarela real devuelve un
        // identificador del MEDIO inscrito (el `tbk_user` de Oneclick), que es otra cosa;
        // cuando se escriba el adaptador real, lo que tiene que devolver en la inscripcion
        // es ese identificador y no el de la transaccion.
        if (!string.IsNullOrWhiteSpace(medioInscrito)) s.ReferenciaPasarela = medioInscrito;

        // RN-S7-07. La fecha mas lejana de las dos: a quien le quedaban 45 dias no se le
        // quitan 15 para dejarlo en 30, y a quien le quedaban 3 no se le suman 30 encima.
        s.VigenteHasta = s.VigenteHasta > mesGratis ? s.VigenteHasta : mesGratis;

        _auditor.Registrar(Auditar.Entrada(
            "suscripcion_a_fundador", "suscripciones", s.Id, _reloj.Ahora,
            contexto: new { vigenteHasta = s.VigenteHasta, diasDeMesGratis = DiasDeMesGratis }));

        return Resultado.Ok(s.VigenteHasta);
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
