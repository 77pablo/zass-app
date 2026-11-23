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

    /// <summary>Plan profesional: estadisticas, fotos de trabajos, verificacion prioritaria.</summary>
    public const string PlanProfesional = "profesional";

    /// <summary>Plan equipo: para estudios y talleres, hasta 5 profesionales. Inactivo por ahora.</summary>
    public const string PlanEquipo = "equipo";

    /// <summary>
    /// Con cuanta antelacion se avisa. Dos momentos, como los recordatorios de cita
    /// (RN-S4-07): uno con tiempo para decidir y otro el dia antes, para quien lo dejo pasar.
    ///
    /// Cada umbral manda UN aviso y no mas, porque la clave de idempotencia lo incluye.
    /// </summary>
    public static readonly int[] DiasDeAviso = { 7, 1 };
    /// <summary>
    /// Cuanto se guarda un cupo fundador mientras la persona inscribe su tarjeta (N3, 22-nov-2026:
    /// 30 minutos). Pasado ese plazo sin confirmar, el cupo vuelve a estar disponible para otra
    /// persona: el conteo lo da por libre al instante, y el trabajo diario limpia la fila.
    /// </summary>
    public static readonly TimeSpan DuracionReservaPorDefecto = TimeSpan.FromMinutes(30);

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

        var esFundador = s.Plan?.Codigo == PlanFundador;
        var tarjeta = !string.IsNullOrWhiteSpace(s.ReferenciaPasarela);

        return Resultado.Ok(new SuscripcionDto(
            Plan: s.Plan?.Nombre ?? "desconocido",
            Estado: s.Estado.ToString(),
            Vigente: s.EstaVigente(ahora),
            EnPrueba: s.EnPrueba(ahora),
            VigenteHasta: s.VigenteHasta,
            DiasRestantes: s.EstaVigente(ahora) ? s.DiasRestantes(ahora) : 0,
            // Lo que de verdad va a pasar, y no la columna: renovacion_automatica nace en falso
            // con la prueba y nada la ponia en verdadero (ver Suscripcion.RenovacionCancelada).
            RenovacionAutomatica: tarjeta && !s.RenovacionCancelada && s.EstaVigente(ahora),
            EsFundador: esFundador,
            TarjetaInscrita: tarjeta,
            DiasDeGracia: esFundador && tarjeta && s.EstaVigente(ahora)
                && await EnGraciaAsync(s, ct)
                ? Math.Max(1, s.DiasRestantes(ahora))
                : null,
            RenovacionCancelada: s.RenovacionCancelada));
    }

    /// <summary>
    /// ES-1. En gracia = el cobro automatico de ESTE periodo se intento, ninguno de sus
    /// intentos esta pagado ni en curso, y el ultimo fue rechazado. Se deduce de los intentos
    /// que ya existen: no hace falta una columna que pueda desincronizarse de ellos.
    /// </summary>
    private async Task<bool> EnGraciaAsync(Suscripcion s, CancellationToken ct)
    {
        var intentos = await _repo.IntentosPorPrefijoDeClaveAsync(
            ServicioCobros.ClaveDeCobroAutomatico(s.Id, s.VigenteHasta), ct);
        return intentos.Count > 0
            && intentos.All(i => i.Estado == EstadoIntentoPago.Fallido);
    }

    public async Task<IReadOnlyList<PlanDto>> PlanesActivosAsync(CancellationToken ct = default)
    {
        var planes = await _repo.PlanesActivosAsync(ct);
        return planes
            .Select(p => new PlanDto(
                p.Id, p.Codigo, p.Nombre, p.Descripcion, p.PrecioMensual,
                p.DiasPrueba, p.MaxServicios, p.MaxTarjetas, p.PermiteCredenciales,
                p.MaxFotosTrabajos, p.PermiteEstadisticas, p.VerificacionPrioritaria))
            .ToList();
    }

    public async Task<int> LimiteDeServiciosAsync(
        Guid perfilPrestadorId, CancellationToken ct = default)
    {
        var vigente = await _repo.VigenteDePrestadorAsync(perfilPrestadorId, _reloj.Ahora, ct);
        var plan = vigente?.Plan ?? await _repo.PlanPorCodigoAsync(PlanInicial, ct);
        return plan?.MaxServicios ?? 0;
    }

    public async Task<int> LimiteDeTarjetasAsync(
        Guid perfilPrestadorId, CancellationToken ct = default)
    {
        var vigente = await _repo.VigenteDePrestadorAsync(perfilPrestadorId, _reloj.Ahora, ct);
        var plan = vigente?.Plan ?? await _repo.PlanPorCodigoAsync(PlanInicial, ct);
        return plan?.MaxTarjetas ?? 0;
    }

    public async Task<int> LimiteDeFotosDeTrabajoAsync(
        Guid perfilPrestadorId, CancellationToken ct = default)
    {
        var vigente = await _repo.VigenteDePrestadorAsync(perfilPrestadorId, _reloj.Ahora, ct);
        var plan = vigente?.Plan ?? await _repo.PlanPorCodigoAsync(PlanInicial, ct);
        return plan?.MaxFotosTrabajos ?? 0;
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

        // La prueba gratis es UNA por perfil (22-nov-2026). Hasta entonces esto solo miraba la
        // vigente, y como API-22 acepta publicar un perfil ya publicado, quien dejaba vencer su
        // suscripcion volvia a publicar y tenia otros 30 dias gratis, cada mes. Con cualquier
        // suscripcion previa no se crea otra: el perfil queda publicado pero fuera de la
        // busqueda (RN-S3-09) y la persona tiene que pagar en /planes.
        if (await _repo.UltimaDePrestadorAsync(perfilPrestadorId, ct) is not null)
            return Resultado.Ok();

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
        Guid? intentoDePagoId = null, bool esCobroAutomatico = false, short? planPagado = null,
        CancellationToken ct = default)
    {
        var ahora = _reloj.Ahora;
        var s = await _repo.PorIdAsync(suscripcionId, ct);
        if (s is null)
            return Resultado.Falla<Guid>(Fallas.NoEncontrado, "Esa suscripción no existe.");

        if (meses < 1)
            return Resultado.Falla<Guid>(
                Fallas.Validacion, "Un periodo pagado es de un mes o más.");

        // Lo hace la entidad, no este servicio: quien paga el dia 25 de un mes de 30
        // no puede perder los 5 que le quedaban, y esa regla pertenece al dominio.
        // Si es cobro automatico (cobro tardio incluido), se extiende desde el
        // VigenteHasta de la suscripcion TAL COMO ESTA (VencerExpiradasAsync solo cambia
        // Estado, no VigenteHasta). Si es pago manual, la logica de siempre:
        // desde hoy si ya vencio.
        var planAnterior = s.PlanId;
        s.RegistrarPeriodoPagado(ahora, meses, esCobroAutomatico, planPagado);
        if (s.PlanId != planAnterior)
            _auditor.Registrar(Auditar.Entrada(
                "suscripcion_cambio_de_plan", "suscripciones", s.Id, ahora,
                contexto: new { desde = planAnterior, hacia = s.PlanId }));

        // PeriodoDesde para el registro de pago: si es cobro automatico, el periodo
        // empieza en el VigenteHasta original; si es manual, en la base que se usara
        // para la extension (vigencia si futuro, hoy si pasado).
        var periodoDesde = esCobroAutomatico
            ? s.VigenteHasta.AddMonths(-meses) // VigenteHasta ya se actualizo, restamos los meses
            : (s.VigenteHasta > ahora ? s.VigenteHasta.AddMonths(-meses) : ahora);

        var pago = new Pago
        {
            SuscripcionId = s.Id,
            Monto = montoPagado,
            Moneda = "CLP",
            Estado = EstadoPago.Pagado,
            PeriodoDesde = periodoDesde,
            PeriodoHasta = s.VigenteHasta,
            ReferenciaPasarela = referencia,
            IntentoDePagoId = intentoDePagoId,
            CreadoEn = ahora,
            PagadoEn = ahora,
        };
        _repo.AgregarPago(pago);

        _auditor.Registrar(Auditar.Entrada(
            "suscripcion_pagada", "suscripciones", s.Id, ahora,
            contexto: new { meses, vigenteHasta = s.VigenteHasta, cobro_tardio = esCobroAutomatico }));

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

    public async Task<Resultado<SuscripcionDto>> CancelarRenovacionAsync(
        Guid perfilPrestadorId, CancellationToken ct = default)
    {
        var ahora = _reloj.Ahora;
        var s = await _repo.VigenteDePrestadorAsync(perfilPrestadorId, ahora, ct);
        if (s is null)
            return Resultado.Falla<SuscripcionDto>(
                Fallas.Conflicto, "No tienes una suscripción vigente que cancelar.");

        if (!s.RenovacionCancelada)
        {
            s.CancelarRenovacion(ahora);
            _auditor.Registrar(Auditar.Entrada(
                "suscripcion_renovacion_cancelada", "suscripciones", s.Id, ahora,
                contexto: new { vigenteHasta = s.VigenteHasta }));
            await _trabajo.GuardarAsync(ct);
        }

        return await DePrestadorAsync(perfilPrestadorId, ct);
    }

    public async Task<Resultado> TerminarPorCuentaEliminadaAsync(
        Guid perfilPrestadorId, CancellationToken ct = default)
    {
        var ahora = _reloj.Ahora;
        var s = await _repo.UltimaDePrestadorAsync(perfilPrestadorId, ct);
        if (s is not null)
        {
            if (s.Estado is EstadoSuscripcion.Prueba or EstadoSuscripcion.Activa)
                s.Estado = EstadoSuscripcion.Cancelada;
            s.CancelarRenovacion(ahora);
            // Sin la referencia del medio inscrito, el cobro automatico no tiene contra que
            // cobrar aunque algun filtro fallara. (La tarjeta sigue guardada en Flow: borrar
            // el cliente alli es una llamada a la pasarela que este cambio NO hace.)
            s.ReferenciaPasarela = null;
            _auditor.Registrar(Auditar.Entrada(
                "suscripcion_cancelada_por_eliminacion", "suscripciones", s.Id, ahora));
        }

        if (await _repo.LiberarCupoFundadorAsync(perfilPrestadorId, ct))
            _auditor.Registrar(Auditar.Entrada(
                "cupo_fundador_liberado_por_eliminacion", "perfiles_prestador", perfilPrestadorId, ahora));

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

            // Decisiones de Pablo (20-nov-2026): el cupo se toma sin tarjeta y la tarjeta se
            // pide al terminar el mes gratis; y si la tarjeta falla, hay 7 dias de gracia (ES-1).
            // Llegar aqui es haber vencido sin pagar, con o sin tarjeta: en los dos casos el
            // cupo vuelve a quedar libre para otra persona. A quien SI pago no le llega este
            // camino, porque el cobro le movio VigenteHasta antes (corre primero en la misma
            // tarea del planificador).
            //
            // Sin mirar el plan (22-nov-2026): desde que pagar cambia el plan, un fundador puede
            // haberse pasado a Profesional conservando su cupo para volver al precio congelado.
            // Ese cupo es suyo MIENTRAS mantenga la suscripcion; al vencer sin pagar se libera
            // igual que el de quien sigue en el plan fundador.
            if (await _repo.LiberarCupoFundadorAsync(s.PerfilPrestadorId, ct))
                _auditor.Registrar(Auditar.Entrada(
                    string.IsNullOrWhiteSpace(s.ReferenciaPasarela)
                        ? "cupo_fundador_liberado_sin_tarjeta"
                        : "cupo_fundador_liberado_sin_pago",
                    "perfiles_prestador", s.PerfilPrestadorId, ahora));

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

    public async Task AvisarCobroRechazadoAsync(
        Guid perfilPrestadorId, Guid suscripcionId, DateTime vigenteHasta,
        CancellationToken ct = default)
    {
        var clave = $"CobroRechazado:{suscripcionId:D}:{vigenteHasta:yyyy-MM-dd}";

        // El reintento del dia siguiente tambien puede ser rechazado: el que encuentra la
        // clave no encola nada. Si dos procesos llegaran a la vez, el unico de TBL-27 (R-18)
        // es la garantia; esto evita llegar hasta el.
        if (await _avisos.ExisteAsync(clave, ct)) return;

        var duenos = await _repo.UsuariosDePerfilesAsync(new[] { perfilPrestadorId }, ct);
        if (!duenos.TryGetValue(perfilPrestadorId, out var usuarioId)) return;

        var ahora = _reloj.Ahora;
        var s = await _repo.PorIdAsync(suscripcionId, ct);
        if (s is null) return;
        var dias = Math.Max(1, s.DiasRestantes(ahora));
        var fundador = (await _repo.PlanPorIdAsync(s.PlanId, ct))?.Codigo == PlanFundador;

        _avisos.Encolar(new Notificacion
        {
            DestinatarioId = usuarioId,
            Tipo = TipoNotificacion.CobroRechazado,
            ClaveIdempotencia = clave,
            Asunto = "No pudimos cobrar tu suscripción a ZASS!",
            Cuerpo = fundador
                ? CargaDeAviso.ConCampos(("dias", dias.ToString()), ("fundador", "si"))
                : CargaDeAviso.ConCampos(("dias", dias.ToString())),
            ProgramadaPara = ahora,
        });
        await _trabajo.GuardarAsync(ct);
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
                Cuerpo = s.FundadorSinTarjeta
                    ? CargaDeAviso.ConCampos(
                        ("dias", Math.Max(1, s.DiasRestantes).ToString()), ("sinTarjeta", "si"))
                    : CargaDeAviso.ConCampos(("dias", Math.Max(1, s.DiasRestantes).ToString())),
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

    public async Task<Resultado<CupoTomadoDto>> TomarCupoFundadorAsync(
        Guid perfilPrestadorId, CancellationToken ct = default)
    {
        var s = await _repo.UltimaDePrestadorAsync(perfilPrestadorId, ct);
        if (s is null)
            return Resultado.Falla<CupoTomadoDto>(
                Fallas.Conflicto,
                "Publica tu perfil antes de tomar tu cupo: la suscripción nace al publicarlo.");

        var plan = await _repo.PlanPorCodigoAsync(PlanFundador, ct);
        if (plan is null || !plan.Activo)
            return Resultado.Falla<CupoTomadoDto>(
                Fallas.ReglaDeNegocio, "El plan fundador no esta disponible.");

        // Idempotente: quien ya tiene su cupo (doble clic, o vuelve a la pantalla) recibe el
        // mismo, sin otro mes gratis.
        if (s.PlanId == plan.Id && await _repo.CupoFundadorDeAsync(perfilPrestadorId, ct) is { } propio)
            return Resultado.Ok(new CupoTomadoDto(
                propio, s.VigenteHasta, !string.IsNullOrWhiteSpace(s.ReferenciaPasarela)));

        // Ya fue fundador y perdio el cupo porque el mes gratis termino sin tarjeta: el mes
        // gratis es uno solo. Sin esto, tomar, no inscribir, perder y volver a tomar daria
        // un mes gratis tras otro.
        if (s.PlanId == plan.Id)
            return Resultado.Falla<CupoTomadoDto>(
                Fallas.Conflicto,
                "Tu mes gratis de fundador ya terminó sin una tarjeta inscrita y el cupo se liberó. "
                + "Puedes suscribirte al plan mensual.");

        // El mes gratis de fundador es para quien viene de la prueba y nunca cambio de plan
        // (22-nov-2026). Desde que pagar cambia el plan, mirar solo «PlanId == fundador» dejaba
        // encadenar: fundador sin tarjeta, pierde el cupo, paga un mes de Esencial (el plan pasa
        // a mensual), lo deja vencer y vuelve a tomar el cupo con OTRO mes gratis. Quien ya pago
        // otro plan puede pagar el plan fundador directamente (API-68), sin mes gratis.
        if (!await VieneDelPlanInicialAsync(s, ct))
            return Resultado.Falla<CupoTomadoDto>(Fallas.Conflicto, MensajeMesGratisSoloDelInicial);

        // N3 (decision de Pablo del 22-nov-2026, reemplaza la del 20-nov): tomar el cupo ya NO lo
        // deja definitivo ni arranca el mes gratis. Lo RESERVA por DuracionReservaPorDefecto; lo
        // confirma la inscripcion de la tarjeta (ServicioCobros.ConfirmarAsync). Sin tarjeta a
        // tiempo, el cupo vuelve a estar libre. Asi una cuenta falsa no se queda con un cupo un
        // mes entero: lo retiene media hora, y para volver a tomarlo pasa otra vez por el captcha.
        //
        // Una reserva vigente se devuelve TAL CUAL, sin renovar el plazo: si repetir el llamado
        // la renovara, una cuenta podria retener el cupo para siempre sin inscribir nada.
        var ahora = _reloj.Ahora;
        if (await _repo.EstadoCupoFundadorDeAsync(perfilPrestadorId, ct) is { } reserva
            && reserva.ReservaVigente(ahora))
            return Resultado.Ok(new CupoTomadoDto(
                reserva.Numero, MesGratisHasta: null, TarjetaInscrita: false, reserva.ReservadoHasta));

        var hasta = ahora.Add(DuracionReservaPorDefecto);
        var numero = await _repo.IntentarReservarCupoFundadorAsync(perfilPrestadorId, ahora, hasta, ct);

        if (numero is null)
        {
            var estado = await EstadoDelPlanFundadorAsync(ct);
            return Resultado.Falla<CupoTomadoDto>(
                Fallas.Conflicto,
                (estado.Valor?.Mensaje ?? "El plan fundador está cerrado.")
                + " Puedes suscribirte al plan mensual.");
        }

        _auditor.Registrar(Auditar.Entrada(
            "cupo_fundador_reservado", "perfiles_prestador", perfilPrestadorId, ahora,
            contexto: new { cupo = numero.Value, minutos = (int)DuracionReservaPorDefecto.TotalMinutes }));
        await _trabajo.GuardarAsync(ct);

        return Resultado.Ok(new CupoTomadoDto(
            numero.Value, MesGratisHasta: null, TarjetaInscrita: false, hasta));
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

    private const string MensajeMesGratisSoloDelInicial =
        "El mes gratis de fundador es para quien empieza en ZASS!. Mientras queden cupos, "
        + "puedes pagar el plan fundador directamente.";

    /// <summary>
    /// Regla unica del mes gratis de fundador: solo para quien sigue en el plan inicial. La usan
    /// API-69 (tomar el cupo) y la confirmacion de la inscripcion, para que no queden dos copias.
    /// </summary>
    private async Task<bool> VieneDelPlanInicialAsync(Suscripcion s, CancellationToken ct)
    {
        var inicial = await _repo.PlanPorCodigoAsync(PlanInicial, ct);
        return inicial is not null && s.PlanId == inicial.Id;
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

        // Quien YA es fundador y vuelve a inscribir su tarjeta (cambia de tarjeta, o apreto
        // «Quiero mi cupo» otra vez) actualiza el medio de pago y nada mas. Antes la vigencia
        // pasaba a «hoy + 30» cada vez, y repitiendolo el mes gratis no terminaba nunca
        // (defecto 2 del informe JQ-7).
        var yaEraFundador = s.PlanId == plan.Id;

        // Misma regla que API-69 (TomarCupoFundadorAsync): el mes gratis es solo para quien viene
        // del plan inicial. La inscripcion tambien se alcanza por API-68 (pagar el plan fundador
        // reserva cupo) + API-76, y por ahi llegaba quien ya habia pagado otro plan.
        if (!yaEraFundador && !await VieneDelPlanInicialAsync(s, ct))
            return Resultado.Falla<DateTime>(Fallas.Conflicto, MensajeMesGratisSoloDelInicial);

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
        if (!yaEraFundador)
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
        var fundador = cercanas.Count == 0 ? null : await _repo.PlanPorCodigoAsync(PlanFundador, ct);

        return cercanas
            .Select(s => new SuscripcionPorVencer(
                s.Id, s.PerfilPrestadorId, s.VigenteHasta, s.DiasRestantes(ahora),
                FundadorSinTarjeta: fundador is not null && s.PlanId == fundador.Id
                                    && string.IsNullOrWhiteSpace(s.ReferenciaPasarela)))
            .ToList();
    }
}
