using Zass.Aplicacion.Catalogo;
using Zass.Aplicacion.Comun;
using Zass.Aplicacion.Identidad;
using Zass.Aplicacion.Puertos;
using Zass.Aplicacion.Suscripciones;
using Zass.Dominio.Agenda;
using Zass.Dominio.Comun;
using Zass.Dominio.Reputacion;

namespace Zass.Aplicacion.Agenda;

/// <summary>
/// MOD-06 · S4. La implementación.
///
/// Habla con MOD-03 y MOD-01 por sus **servicios**, nunca por sus repositorios: la duración y
/// el modo salen de <c>IServicioCatalogo.DatosDePrestadorServicioAsync</c> y el estado del
/// perfil de <c>IServicioIdentidad</c>. MOD-13 entra porque un prestador sin suscripción
/// vigente desaparece de la búsqueda (RN-S3-09) y por tanto tampoco da horas.
/// </summary>
public sealed class ServicioAgenda : IServicioAgenda
{
    /// <summary>RN-S4-02. No se agenda en el pasado ni con menos de 2 horas.</summary>
    public const double AnticipacionMinimaHoras = 2;

    /// <summary>Los bloques se generan con 8 semanas de anticipación (S4, sección 4).</summary>
    public const int SemanasDeVentana = 8;

    /// <summary>RN-S4-06. Más de esto con el mismo prestador es señal de otra cosa.</summary>
    public const int MaximoCitasActivasPorPrestador = 3;

    /// <summary>RN-S4-03. Una cita manual sin respuesta expira y libera el bloque.</summary>
    public static readonly TimeSpan PlazoDeRespuestaManual = TimeSpan.FromHours(24);

    /// <summary>T11. Si el prestador no marca asistencia en este plazo, se asume realizada.</summary>
    public static readonly TimeSpan PlazoParaMarcarAsistencia = TimeSpan.FromHours(72);

    /// <summary>
    /// Tope por pasada. Si hay más, la siguiente (en 15 min) sigue: mejor una pasada corta
    /// que una transacción larga o una reserva del planificador que venza a mitad.
    /// </summary>
    public const int MaximoPorPasada = 200;

    public const int LargoMaximoNota = 500;
    public const int LargoMaximoMotivo = 300;

    /// <summary>
    /// CC-06. Si una reprogramación espera más que esto por un bloqueo, falla rápido con un
    /// mensaje claro en vez de dejar a alguien mirando una pantalla que no responde.
    /// </summary>
    public static readonly TimeSpan EsperaDeBloqueo = TimeSpan.FromSeconds(3);

    private readonly IRepositorioAgenda _repo;
    private readonly IUnidadDeTrabajo _trabajo;
    private readonly IRelojSistema _reloj;
    private readonly IAuditor _auditor;
    private readonly IContactos _contactos;
    private readonly IServicioCatalogo _catalogo;
    private readonly IServicioIdentidad _identidad;
    private readonly IServicioSuscripciones _suscripciones;

    public ServicioAgenda(
        IRepositorioAgenda repo, IUnidadDeTrabajo trabajo, IRelojSistema reloj,
        IAuditor auditor, IContactos contactos, IServicioCatalogo catalogo,
        IServicioIdentidad identidad, IServicioSuscripciones suscripciones)
    {
        _repo = repo;
        _trabajo = trabajo;
        _reloj = reloj;
        _auditor = auditor;
        _contactos = contactos;
        _catalogo = catalogo;
        _identidad = identidad;
        _suscripciones = suscripciones;
    }

    // ------------------------------------------------------------- CU-11 Publicar disponibilidad

    public async Task<IReadOnlyList<DisponibilidadDto>> MiDisponibilidadAsync(
        Guid perfilPrestadorId, CancellationToken ct = default)
    {
        var franjas = await _repo.DisponibilidadesDePrestadorAsync(perfilPrestadorId, ct);
        var ofrecidos = await _catalogo.ServiciosDePrestadorAsync(perfilPrestadorId, ct);

        // Se listan TODOS los servicios, también los que aún no tienen horario: la pantalla
        // tiene que poder mostrar «este servicio no tiene horas» y dejar definirlas.
        return ofrecidos
            .Select(s => new DisponibilidadDto(
                s.Id, s.Nombre, s.ModoAgenda,
                franjas
                    .Where(f => f.PrestadorServicioId == s.Id && f.Activa)
                    .OrderBy(f => f.DiaSemana).ThenBy(f => f.HoraInicio)
                    .Select(f => new FranjaDto(
                        f.Id, f.DiaSemana, f.HoraInicio, f.HoraFin, f.DuracionBloqueMin))
                    .ToList()))
            .ToList();
    }

    public async Task<Resultado<int>> DefinirDisponibilidadAsync(
        DefinirDisponibilidadCmd cmd, CancellationToken ct = default)
    {
        var oferta = await _catalogo.DatosDePrestadorServicioAsync(cmd.PrestadorServicioId, ct);
        if (!oferta.Exito || oferta.Valor is null)
            return Resultado.Falla<int>(Fallas.NoEncontrado, "Ese servicio no existe.");

        // 403 y 404 responden igual cuando distinguirlos filtraría información (api-rest.md):
        // el servicio de otro prestador no existe, desde aquí.
        if (oferta.Valor.PerfilPrestadorId != cmd.PerfilPrestadorId)
            return Resultado.Falla<int>(Fallas.NoEncontrado, "Ese servicio no existe.");

        foreach (var f in cmd.Franjas)
        {
            if (f.DiaSemana is < 1 or > 7)
                return Resultado.Falla<int>(Fallas.Validacion, "El día va de 1 (lunes) a 7 (domingo).");
            if (f.HoraFin <= f.HoraInicio)
                return Resultado.Falla<int>(Fallas.Validacion,
                    "La hora de término tiene que ser posterior a la de inicio.");
            if (f.DuracionBloqueMin <= 0)
                return Resultado.Falla<int>(Fallas.Validacion, "La duración del bloque tiene que ser positiva.");
            if ((f.HoraFin - f.HoraInicio).TotalMinutes < f.DuracionBloqueMin)
                return Resultado.Falla<int>(Fallas.Validacion,
                    $"La franja es más corta que un bloque de {f.DuracionBloqueMin} minutos.");
        }

        // R-4: dos franjas del mismo servicio no se solapan. Se comprueba en código porque el
        // mensaje tiene que decir CUÁLES, y la restricción de exclusión de la base no lo dice.
        var nuevas = cmd.Franjas
            .Select(f => new Disponibilidad
            {
                PerfilPrestadorId = cmd.PerfilPrestadorId,
                PrestadorServicioId = cmd.PrestadorServicioId,
                DiaSemana = f.DiaSemana,
                HoraInicio = f.HoraInicio,
                HoraFin = f.HoraFin,
                DuracionBloqueMin = f.DuracionBloqueMin,
                VigenteDesde = HoraDeChile.DiaLocal(_reloj.Ahora),
            })
            .ToList();

        for (var i = 0; i < nuevas.Count; i++)
            for (var j = i + 1; j < nuevas.Count; j++)
                if (nuevas[i].Solapa(nuevas[j]))
                    return Resultado.Falla<int>(Fallas.Validacion,
                        $"Dos franjas del día {nuevas[i].DiaSemana} se pisan: "
                        + $"{nuevas[i].HoraInicio:HH\\:mm}-{nuevas[i].HoraFin:HH\\:mm} y "
                        + $"{nuevas[j].HoraInicio:HH\\:mm}-{nuevas[j].HoraFin:HH\\:mm}.");

        var ahora = _reloj.Ahora;
        var hasta = ahora.AddDays(SemanasDeVentana * 7);

        return await _trabajo.EnTransaccionAsync(async t =>
        {
            // RN-S4-09: las reglas viejas se desactivan y sus bloques FUTUROS Y LIBRES se
            // quitan. Los reservados sobreviven, y con ellos las citas: por eso no se borra
            // la disponibilidad, que los bloques referencian.
            var anteriores = await _repo.DisponibilidadesDeServicioAsync(cmd.PrestadorServicioId, t);
            foreach (var vieja in anteriores.Where(d => d.Activa))
            {
                vieja.Activa = false;
                vieja.VigenteHasta = HoraDeChile.DiaLocal(ahora);
                await _repo.QuitarBloquesLibresDeAsync(vieja.Id, ahora, t);
            }

            foreach (var n in nuevas) _repo.AgregarDisponibilidad(n);
            await _trabajo.GuardarAsync(t);

            var generados = await GenerarBloquesAsync(
                cmd.PrestadorServicioId, nuevas, ahora, hasta, t);

            _auditor.Registrar(Auditar.Entrada(
                "definir_disponibilidad", "disponibilidad", cmd.PrestadorServicioId, ahora,
                contexto: new { franjas = nuevas.Count, bloques = generados }));
            await _trabajo.GuardarAsync(t);

            return Resultado.Ok(generados);
        }, ct);
    }

    /// <summary>
    /// De la regla a las instancias. Es idempotente: se consultan los inicios que ya existen y
    /// se omiten, porque el Planificador va a extender esta misma ventana cada noche y no debe
    /// duplicar lo de ayer.
    /// </summary>
    private async Task<int> GenerarBloquesAsync(
        Guid prestadorServicioId, IReadOnlyList<Disponibilidad> reglas,
        DateTime desde, DateTime hasta, CancellationToken ct)
    {
        var yaEstan = (await _repo.IniciosYaGeneradosAsync(prestadorServicioId, desde, hasta, ct))
            .ToHashSet();

        // Los dias se recorren en el calendario CHILENO, no en el UTC: a las 02:00 UTC del
        // martes en Chile todavia es lunes, y «lunes» es lo que el prestador publico.
        var nuevos = reglas
            .SelectMany(r => r.GenerarBloques(
                HoraDeChile.DiaLocal(desde), HoraDeChile.DiaLocal(hasta)))
            // Los dos bordes, y el de arriba hace falta: el generador recorre DIAS locales
            // completos, asi que el ultimo dia de la ventana entrega tambien los bloques
            // posteriores a la hora de corte. Sin este filtro, «8 semanas» eran 8 semanas y
            // tres bloques, y el numero crecia o no segun la hora a la que se guardara.
            .Where(b => b.InicioEn > desde && b.InicioEn <= hasta
                        && !yaEstan.Contains(b.InicioEn))
            .ToList();

        if (nuevos.Count == 0) return 0;
        _repo.AgregarBloques(nuevos);
        await _trabajo.GuardarAsync(ct);
        return nuevos.Count;
    }

    // ------------------------------------------------------------- CS-11 Bloquear fecha

    public async Task<Resultado<BloqueoDto>> BloquearFechaAsync(
        BloquearFechaCmd cmd, CancellationToken ct = default)
    {
        if (cmd.HastaEn <= cmd.DesdeEn)
            return Resultado.Falla<BloqueoDto>(Fallas.Validacion,
                "El fin del bloqueo tiene que ser posterior al inicio.");
        if (cmd.Motivo is { Length: > LargoMaximoMotivo })
            return Resultado.Falla<BloqueoDto>(Fallas.Validacion,
                $"El motivo no puede superar los {LargoMaximoMotivo} caracteres.");

        var bloqueo = new BloqueoAgenda
        {
            PerfilPrestadorId = cmd.PerfilPrestadorId,
            DesdeEn = cmd.DesdeEn,
            HastaEn = cmd.HastaEn,
            Motivo = string.IsNullOrWhiteSpace(cmd.Motivo) ? null : cmd.Motivo.Trim(),
        };
        _repo.AgregarBloqueo(bloqueo);

        // El motivo es privado y puede decir por qué alguien no trabaja ese día: no va al
        // registro de auditoría, solo las fechas.
        _auditor.Registrar(Auditar.Entrada(
            "bloquear_agenda", "bloqueo_agenda", bloqueo.Id, _reloj.Ahora,
            actorId: null, contexto: new { cmd.DesdeEn, cmd.HastaEn }));
        await _trabajo.GuardarAsync(ct);

        return Resultado.Ok(new BloqueoDto(
            bloqueo.Id, bloqueo.DesdeEn, bloqueo.HastaEn, bloqueo.Motivo));
    }

    public async Task<IReadOnlyList<BloqueoDto>> MisBloqueosAsync(
        Guid perfilPrestadorId, CancellationToken ct = default)
    {
        var vigentes = await _repo.BloqueosVigentesDeAsync(perfilPrestadorId, _reloj.Ahora, ct);
        return vigentes.Select(b => new BloqueoDto(b.Id, b.DesdeEn, b.HastaEn, b.Motivo)).ToList();
    }

    public async Task<Resultado<bool>> QuitarBloqueoAsync(
        Guid perfilPrestadorId, Guid bloqueoId, CancellationToken ct = default)
    {
        var bloqueo = await _repo.BloqueoDeAsync(perfilPrestadorId, bloqueoId, ct);
        if (bloqueo is null)
            return Resultado.Falla<bool>(Fallas.NoEncontrado, "Ese bloqueo no existe.");

        _repo.QuitarBloqueo(bloqueo);

        // Igual que al crearlo: solo las fechas, nunca el motivo.
        _auditor.Registrar(Auditar.Entrada(
            "desbloquear_agenda", "bloqueo_agenda", bloqueo.Id, _reloj.Ahora,
            actorId: null, contexto: new { bloqueo.DesdeEn, bloqueo.HastaEn }));
        await _trabajo.GuardarAsync(ct);

        return Resultado.Ok(true);
    }

    // ------------------------------------------------------------- CU-12 pasos 2-4: el calendario

    public async Task<Resultado<CalendarioDto>> BloquesLibresAsync(
        Guid prestadorServicioId, DateTime desde, DateTime hasta, CancellationToken ct = default)
    {
        var oferta = await _catalogo.DatosDePrestadorServicioAsync(prestadorServicioId, ct);
        if (!oferta.Exito || oferta.Valor is null || !oferta.Valor.Activo)
            return Resultado.Falla<CalendarioDto>(Fallas.NoEncontrado, "Ese servicio no existe.");
        var o = oferta.Valor;

        // Una agenda es pública solo si el perfil lo es. Un perfil en borrador o despublicado
        // no puede dar horas por una URL que alguien guardó.
        if (!await _identidad.PerfilPublicadoAsync(o.PerfilPrestadorId, ct))
            return Resultado.Falla<CalendarioDto>(Fallas.NoEncontrado, "Ese servicio no existe.");

        var ahora = _reloj.Ahora;
        if (hasta <= desde) hasta = desde.AddDays(SemanasDeVentana * 7);

        var bloques = await _repo.BloquesDeServicioAsync(prestadorServicioId, desde, hasta, ct);
        var bloqueos = await _repo.BloqueosDePrestadorAsync(o.PerfilPrestadorId, desde, hasta, ct);

        var dto = bloques
            .Where(b => b.InicioEn > ahora)
            .OrderBy(b => b.InicioEn)
            .Select(b => new BloqueDto(b.Id, b.InicioEn, b.FinEn, EstadoVisible(b, bloqueos, ahora)))
            .ToList();

        return Resultado.Ok(new CalendarioDto(
            o.Id, o.PerfilPrestadorId, o.Nombre, o.ModoAgenda, o.DuracionMinutos,
            AnticipacionMinimaHoras, dto));
    }

    /// <summary>
    /// Lo que se le dice a quien mira: `libre`, `ocupado` o `bloqueado`. Nunca con quién está
    /// ocupado (paso 3 de CU-12), y un bloque a menos de 2 horas se muestra como ocupado y no
    /// como libre: ofrecerlo para que el servidor lo rechace sería mentirle a quien elige.
    /// </summary>
    private static string EstadoVisible(
        BloqueHorario b, IReadOnlyList<BloqueoAgenda> bloqueos, DateTime ahora)
    {
        if (b.Estado == EstadoBloque.Bloqueado) return "bloqueado";
        if (bloqueos.Any(x => x.DesdeEn < b.FinEn && b.InicioEn < x.HastaEn)) return "bloqueado";
        if (b.Estado != EstadoBloque.Libre) return "ocupado";
        return b.AnticipacionHoras(ahora) >= AnticipacionMinimaHoras ? "libre" : "ocupado";
    }

    // ------------------------------------------------------------- CU-12 / CU-13: agendar

    public async Task<Resultado<CitaDto>> AgendarAsync(
        AgendarCmd cmd, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(cmd.ClaveIdempotencia))
            return Resultado.Falla<CitaDto>(Fallas.Validacion,
                "Falta la clave de idempotencia de la solicitud.");
        var nota = string.IsNullOrWhiteSpace(cmd.NotaBuscador) ? null : cmd.NotaBuscador.Trim();
        if (nota is { Length: > LargoMaximoNota })
            return Resultado.Falla<CitaDto>(Fallas.Validacion,
                $"La nota no puede superar los {LargoMaximoNota} caracteres.");

        // Flujo 4B, antes de abrir nada: el doble envío devuelve la MISMA cita. Se vuelve a
        // comprobar dentro de la transacción, porque entre esta lectura y el INSERT cabe otra
        // solicitud con la misma clave.
        if (await _repo.CitaPorClaveAsync(cmd.ClaveIdempotencia, ct) is { } yaExiste)
            return Resultado.Ok(ADto(yaExiste, nueva: false));

        return await _trabajo.EnTransaccionAsync(async t =>
        {
            // CC-01, paso 7: el bloqueo de fila. La segunda solicitud ESPERA aquí.
            var bloque = await _repo.TomarBloqueAsync(cmd.BloqueId, t);
            if (bloque is null)
                return Resultado.Falla<CitaDto>(Fallas.NoEncontrado,
                    "Esa hora ya no existe. Vuelve a cargar el calendario.");

            if (await _repo.CitaPorClaveAsync(cmd.ClaveIdempotencia, t) is { } carrera)
                return Resultado.Ok(ADto(carrera, nueva: false));

            // Paso 8 / flujo 4A: con el bloqueo en la mano, el estado que se lee es el real.
            if (bloque.Estado == EstadoBloque.Bloqueado)
                return Resultado.Falla<CitaDto>(Fallas.Conflicto,
                    "El prestador bloqueó ese horario.");
            if (bloque.Estado != EstadoBloque.Libre)
                return Resultado.Falla<CitaDto>(Fallas.Conflicto,
                    "Alguien tomó esa hora recién. Elige otra, por favor.");

            // Paso 9: se revalida TODO en el servidor. Lo que validó el cliente no cuenta.
            var oferta = await _catalogo.DatosDePrestadorServicioAsync(bloque.PrestadorServicioId, t);
            if (!oferta.Exito || oferta.Valor is null || !oferta.Valor.Activo)
                return Resultado.Falla<CitaDto>(Fallas.Conflicto,
                    "Ese servicio ya no está disponible.");
            var o = oferta.Valor;

            if (!await _identidad.PerfilPublicadoAsync(o.PerfilPrestadorId, t))
                return Resultado.Falla<CitaDto>(Fallas.Conflicto,
                    "Ese prestador ya no está publicado.");

            // RN-S3-09: sin suscripción vigente el perfil desaparece de la búsqueda, y
            // tampoco da horas. Es la misma regla que deja la ficha de la tarjeta NFC sin
            // botón de agendar (RN-S6-10).
            if (!await _suscripciones.EstaVigenteAsync(o.PerfilPrestadorId, t))
                return Resultado.Falla<CitaDto>(Fallas.Conflicto,
                    "Ese prestador no está recibiendo horas por ahora.");

            var ahora = _reloj.Ahora;
            if (bloque.AnticipacionHoras(ahora) < AnticipacionMinimaHoras)
                return Resultado.Falla<CitaDto>(Fallas.Validacion,
                    $"Las horas se toman con al menos {AnticipacionMinimaHoras:0} horas de anticipación.");

            var bloqueos = await _repo.BloqueosDePrestadorAsync(
                o.PerfilPrestadorId, bloque.InicioEn, bloque.FinEn, t);
            if (bloqueos.Any(x => x.DesdeEn < bloque.FinEn && bloque.InicioEn < x.HastaEn))
                return Resultado.Falla<CitaDto>(Fallas.Conflicto, "El prestador bloqueó ese horario.");

            // Flujo 4E, RN-S4-06.
            var activas = await _repo.CitasActivasEntreAsync(cmd.BuscadorId, o.PerfilPrestadorId, t);
            if (activas >= MaximoCitasActivasPorPrestador)
                return Resultado.Falla<CitaDto>(Fallas.Conflicto,
                    $"Ya tienes {MaximoCitasActivasPorPrestador} horas con este prestador. "
                    + "Cancela una antes de pedir otra.");

            var manual = o.ModoAgenda == nameof(ModoAgenda.Manual);
            var cita = new Cita
            {
                Codigo = Cita.GenerarCodigo(),
                BloqueHorarioId = bloque.Id,
                BuscadorId = cmd.BuscadorId,
                PrestadorServicioId = bloque.PrestadorServicioId,
                PerfilPrestadorId = o.PerfilPrestadorId,
                Estado = manual ? EstadoCita.Solicitada : EstadoCita.Confirmada,
                Modo = manual ? ModoAgenda.Manual : ModoAgenda.Automatico,
                NotaBuscador = nota,
                ExpiraEn = manual ? ahora + PlazoDeRespuestaManual : null,
                ClaveIdempotencia = cmd.ClaveIdempotencia,
                CreadaEn = ahora,
            };

            // Paso 10: la cita nace y el bloque pasa a reservado, en la misma transacción.
            bloque.Reservar();
            _repo.AgregarCita(cita);
            _repo.AgregarCambio(new CambioCita
            {
                CitaId = cita.Id,
                Tipo = TipoCambio.Creacion,
                EstadoAnterior = null,
                EstadoNuevo = cita.Estado,
                RealizadoPor = cmd.BuscadorId,
                OcurridoEn = ahora,
            });

            // Paso 11: el contacto que habilitará la reseña (S5). Va DENTRO de la
            // transacción porque es la única puerta a la elegibilidad: crearlo después y
            // fallar la perdería sin que nadie se enterara.
            _contactos.Registrar(new Contacto
            {
                BuscadorId = cmd.BuscadorId,
                PerfilPrestadorId = o.PerfilPrestadorId,
                CitaId = cita.Id,
                Origen = OrigenContacto.Cita,
                Canal = CanalContacto.Formulario,
                OcurridoEn = ahora,
            });

            _auditor.Registrar(Auditar.Entrada(
                manual ? "solicitar_cita" : "agendar_cita", "cita", cita.Id, ahora,
                actorId: cmd.BuscadorId,
                contexto: new { cita.Codigo, bloque = bloque.Id, modo = cita.Modo.ToString() }));

            // Paso 12: confirmar. R-1, el único parcial sobre el bloque, es la última
            // garantía: si alguien agregara mañana un camino sin FOR UPDATE, la base lo para.
            await _trabajo.GuardarAsync(t);

            cita.BloqueHorario = bloque;
            return Resultado.Ok(ADto(cita, nueva: true, servicio: o.Nombre));
        }, ct);
    }

    public async Task<IReadOnlyList<CitaDto>> MisCitasAsync(
        Guid usuarioId, Guid? perfilPrestadorId, CancellationToken ct = default)
    {
        var mias = await _repo.CitasDeBuscadorAsync(usuarioId, ct);
        var recibidas = perfilPrestadorId is null
            ? Array.Empty<Cita>()
            : await _repo.CitasDePrestadorAsync(perfilPrestadorId.Value, ct);

        return mias.Concat(recibidas)
            .DistinctBy(c => c.Id)
            .OrderBy(c => c.BloqueHorario?.InicioEn ?? c.CreadaEn)
            .Select(c => ADto(c, nueva: false))
            .ToList();
    }

    // ------------------------------------------------------------- CU-13: responder

    public Task<Resultado<CitaDto>> ConfirmarAsync(
        Guid citaId, AutorDeCambio autor, CancellationToken ct = default) =>
        _trabajo.EnTransaccionAsync(async t =>
        {
            var (cita, falla) = await ParaResponderAsync(citaId, autor, t);
            if (falla is not null) return falla;

            var ahora = _reloj.Ahora;
            if (cita!.ExpiraEn is { } limite && ahora > limite)
                return Resultado.Falla<CitaDto>(Fallas.Conflicto,
                    "El plazo de 24 horas para responder ya pasó y la hora se liberó.");

            var anterior = cita.Estado;
            var r = cita.Confirmar();
            if (!r.Exito) return Resultado.Falla<CitaDto>(r.Codigo!, r.Mensaje!);

            await CerrarCambioAsync(cita, anterior, TipoCambio.Confirmacion, autor, null, false, ahora, t);
            return Resultado.Ok(ADto(cita, nueva: false));
        }, ct);

    public Task<Resultado<CitaDto>> RechazarAsync(
        Guid citaId, AutorDeCambio autor, string? motivo, CancellationToken ct = default) =>
        _trabajo.EnTransaccionAsync(async t =>
        {
            var limpio = Limpiar(motivo);
            if (limpio is { Length: > LargoMaximoMotivo })
                return Resultado.Falla<CitaDto>(Fallas.Validacion,
                    $"El motivo no puede superar los {LargoMaximoMotivo} caracteres.");

            var (cita, falla) = await ParaResponderAsync(citaId, autor, t);
            if (falla is not null) return falla;

            var anterior = cita!.Estado;
            var r = cita.Rechazar();
            if (!r.Exito) return Resultado.Falla<CitaDto>(r.Codigo!, r.Mensaje!);

            // T4: la hora vuelve a estar disponible para quien sea.
            cita.BloqueHorario?.Liberar();
            await CerrarCambioAsync(
                cita, anterior, TipoCambio.Rechazo, autor, limpio, false, _reloj.Ahora, t);
            return Resultado.Ok(ADto(cita, nueva: false));
        }, ct);

    // ------------------------------------------------------------- CU-14: cambiar el plan

    public async Task<Resultado<CitaDto>> CancelarAsync(
        CancelarCitaCmd cmd, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(cmd.ClaveIdempotencia))
            return Resultado.Falla<CitaDto>(Fallas.Validacion,
                "Falta la clave de idempotencia de la solicitud.");

        var motivo = Limpiar(cmd.Motivo);
        if (motivo is { Length: > LargoMaximoMotivo })
            return Resultado.Falla<CitaDto>(Fallas.Validacion,
                $"El motivo no puede superar los {LargoMaximoMotivo} caracteres.");

        return await _trabajo.EnTransaccionAsync(async t =>
        {
            var cita = await _repo.CitaParaCambioAsync(cmd.CitaId, t);
            var quien = QuienEs(cita, cmd.Autor);
            if (quien is Papel.Ninguno)
                return Resultado.Falla<CitaDto>(Fallas.NoEncontrado, "Esa cita no existe.");

            // CC-04 contra el DATO y no con una tabla aparte: si ya quedó cancelada, el
            // reintento devuelve lo mismo en vez de un conflicto. Un reintento de red no debe
            // producir un segundo aviso de cancelación (RN-S4-08).
            if (cita!.Estado == EstadoCita.Cancelada)
                return Resultado.Ok(ADto(cita, nueva: false));

            var ahora = _reloj.Ahora;
            var tardia = cita.EsTardia(ahora);

            // RN-S4-04 y TBL-19: en una cancelación tardía el motivo es obligatorio. Quien se
            // queda con la hora vacía a última hora merece saber por qué.
            if (tardia && motivo is null)
                return Resultado.Falla<CitaDto>(Fallas.ReglaDeNegocio,
                    "Faltan menos de 12 horas: escribe el motivo de la cancelación.");

            var anterior = cita.Estado;
            var r = cita.Cancelar();
            if (!r.Exito) return Resultado.Falla<CitaDto>(r.Codigo!, r.Mensaje!);

            cita.BloqueHorario?.Liberar();
            await CerrarCambioAsync(
                cita, anterior, TipoCambio.Cancelacion, cmd.Autor, motivo, tardia, ahora, t);
            return Resultado.Ok(ADto(cita, nueva: false));
        }, ct);
    }

    public async Task<Resultado<CitaDto>> ReprogramarAsync(
        ReprogramarCitaCmd cmd, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(cmd.ClaveIdempotencia))
            return Resultado.Falla<CitaDto>(Fallas.Validacion,
                "Falta la clave de idempotencia de la solicitud.");

        // El reintento devuelve la cita que ya nació de esta misma intención. Va fuera de la
        // transacción para no abrir una por un doble clic.
        if (await _repo.CitaPorClaveAsync(cmd.ClaveIdempotencia, ct) is { } yaExiste)
            return Resultado.Ok(ADto(yaExiste, nueva: false));

        return await _trabajo.EnTransaccionAsync(async t =>
        {
            // CC-06: si algo imprevisto hace esperar, fallar rápido y con un mensaje claro.
            await _repo.FijarEsperaDeBloqueoAsync(EsperaDeBloqueo, t);

            var cita = await _repo.CitaParaCambioAsync(cmd.CitaId, t);
            var quien = QuienEs(cita, cmd.Autor);
            if (quien is Papel.Ninguno)
                return Resultado.Falla<CitaDto>(Fallas.NoEncontrado, "Esa cita no existe.");
            if (cita!.BloqueHorarioId == cmd.BloqueNuevoId)
                return Resultado.Falla<CitaDto>(Fallas.Validacion, "Esa es la misma hora que ya tienes.");

            if (await _repo.CitaPorClaveAsync(cmd.ClaveIdempotencia, t) is { } carrera)
                return Resultado.Ok(ADto(carrera, nueva: false));

            if (cita.Estado != EstadoCita.Confirmada)
                return Resultado.Falla<CitaDto>(Fallas.Conflicto,
                    "Solo se puede reprogramar una cita confirmada.");

            var ahora = _reloj.Ahora;

            // Sección 5 de estados-cita.md: el prestador puede mover su hora dentro de las 2
            // horas y el buscador no. Es la agenda del prestador, y si le surge un imprevisto
            // impedirle moverla solo produce una inasistencia; del lado del buscador el
            // límite protege al prestador de un cambio que ya no alcanza a llenar.
            if (quien is Papel.Buscador
                && cita.BloqueHorario is { } viejo
                && viejo.AnticipacionHoras(ahora) < AnticipacionMinimaHoras)
                return Resultado.Falla<CitaDto>(Fallas.ReglaDeNegocio,
                    $"Faltan menos de {AnticipacionMinimaHoras:0} horas: ya no puedes mover esta hora. "
                    + "Puedes cancelarla explicando el motivo.");

            // CC-06: los dos bloques, bloqueados ORDENADOS POR ID. El orden global es lo que
            // hace imposible el ciclo entre dos reprogramaciones cruzadas.
            var bloques = await _repo.TomarDosBloquesAsync(cita.BloqueHorarioId, cmd.BloqueNuevoId, t);
            var nuevoBloque = bloques.FirstOrDefault(b => b.Id == cmd.BloqueNuevoId);
            var bloqueActual = bloques.FirstOrDefault(b => b.Id == cita.BloqueHorarioId);
            if (nuevoBloque is null)
                return Resultado.Falla<CitaDto>(Fallas.NoEncontrado,
                    "Esa hora ya no existe. Vuelve a cargar el calendario.");

            if (nuevoBloque.PrestadorServicioId != cita.PrestadorServicioId)
                return Resultado.Falla<CitaDto>(Fallas.Validacion,
                    "La hora nueva tiene que ser del mismo servicio.");
            if (nuevoBloque.Estado == EstadoBloque.Bloqueado)
                return Resultado.Falla<CitaDto>(Fallas.Conflicto, "El prestador bloqueó ese horario.");
            if (nuevoBloque.Estado != EstadoBloque.Libre)
                return Resultado.Falla<CitaDto>(Fallas.Conflicto,
                    "Alguien tomó esa hora recién. Elige otra, por favor.");
            if (nuevoBloque.AnticipacionHoras(ahora) < AnticipacionMinimaHoras)
                return Resultado.Falla<CitaDto>(Fallas.Validacion,
                    $"Las horas se toman con al menos {AnticipacionMinimaHoras:0} horas de anticipación.");

            var bloqueos = await _repo.BloqueosDePrestadorAsync(
                cita.PerfilPrestadorId, nuevoBloque.InicioEn, nuevoBloque.FinEn, t);
            if (bloqueos.Any(x => x.DesdeEn < nuevoBloque.FinEn && nuevoBloque.InicioEn < x.HastaEn))
                return Resultado.Falla<CitaDto>(Fallas.Conflicto, "El prestador bloqueó ese horario.");

            // T7: la vieja se cierra y su bloque vuelve a estar libre.
            var anterior = cita.Estado;
            var eraTardia = cita.EsTardia(ahora);
            cita.MarcarReprogramada();
            cita.Version++;
            bloqueActual?.Liberar();
            _repo.AgregarCambio(new CambioCita
            {
                CitaId = cita.Id,
                Tipo = TipoCambio.Reprogramacion,
                EstadoAnterior = anterior,
                EstadoNuevo = cita.Estado,
                RealizadoPor = cmd.Autor.UsuarioId,
                Motivo = Limpiar(cmd.Motivo),
                EsTardio = eraTardia,
                OcurridoEn = ahora,
            });

            // RN-S4-05: el vínculo apunta a la PRIMERA de la cadena, nunca a la anterior. Así
            // la elegibilidad de reseña está a un salto y ningún eslabón intermedio la puede
            // romper (estados-cita.md, sección 4).
            var nacida = new Cita
            {
                Codigo = Cita.GenerarCodigo(),
                BloqueHorarioId = nuevoBloque.Id,
                BuscadorId = cita.BuscadorId,
                PrestadorServicioId = cita.PrestadorServicioId,
                PerfilPrestadorId = cita.PerfilPrestadorId,
                CitaOrigenId = cita.CitaOrigenId ?? cita.Id,
                Estado = EstadoCita.Confirmada,
                Modo = cita.Modo,
                NotaBuscador = cita.NotaBuscador,
                ClaveIdempotencia = cmd.ClaveIdempotencia,
                CreadaEn = ahora,
            };
            nuevoBloque.Reservar();
            _repo.AgregarCita(nacida);
            _repo.AgregarCambio(new CambioCita
            {
                CitaId = nacida.Id,
                Tipo = TipoCambio.Creacion,
                EstadoAnterior = null,
                EstadoNuevo = nacida.Estado,
                RealizadoPor = cmd.Autor.UsuarioId,
                OcurridoEn = ahora,
            });

            _auditor.Registrar(Auditar.Entrada(
                "reprogramar_cita", "cita", cita.Id, ahora, actorId: cmd.Autor.UsuarioId,
                contexto: new { hacia = nuevoBloque.Id, codigoNuevo = nacida.Codigo, tardio = eraTardia }));
            await _trabajo.GuardarAsync(t);

            nacida.BloqueHorario = nuevoBloque;
            nacida.PrestadorServicio = cita.PrestadorServicio;
            nacida.PerfilPrestador = cita.PerfilPrestador;
            return Resultado.Ok(ADto(nacida, nueva: true));
        }, ct);
    }

    // ------------------------------------------------------------- CS-12: asistencia

    public Task<Resultado<CitaDto>> RegistrarAsistenciaAsync(
        Guid citaId, AutorDeCambio autor, bool asistio, CancellationToken ct = default) =>
        _trabajo.EnTransaccionAsync(async t =>
        {
            var cita = await _repo.CitaParaCambioAsync(citaId, t);

            // Solo el prestador, y el buscador recibe el MISMO 404 que un desconocido. Es la
            // defensa que cierra CU-16: si el buscador pudiera marcar «asistió», bastaría
            // agendar y automarcar para fabricar elegibilidad de reseña.
            if (QuienEs(cita, autor) is not Papel.Prestador)
                return Resultado.Falla<CitaDto>(Fallas.NoEncontrado, "Esa cita no existe.");

            var anterior = cita!.Estado;
            // `asistio: false` ya no es «no pasó nada»: es el prestador acusando al CLIENTE.
            var r = asistio
                ? cita.RegistrarAsistencia()
                : cita.AcusarInasistencia(FaltaEnCita.Cliente);
            if (!r.Exito) return Resultado.Falla<CitaDto>(r.Codigo!, r.Mensaje!);

            // T9/T10: el bloque pasa a vencido, no vuelve a estar libre: la hora ya ocurrió.
            cita.BloqueHorario?.Vencer();
            await CerrarCambioAsync(
                cita, anterior, TipoCambio.Asistencia, autor, null, false, _reloj.Ahora, t);
            return Resultado.Ok(ADto(cita, nueva: false));
        }, ct);

    /// <summary>
    /// La otra mitad de T10 (decisión de Pablo, 11-nov-2026): el CLIENTE dice que el prestador
    /// no llegó.
    ///
    /// No hay un parámetro para decir a quién se acusa, y eso ES la regla: quien llama no elige,
    /// se deduce de su papel en la cita. Acusarse a uno mismo, o acusar en nombre del otro, no
    /// es algo que haya que rechazar: es algo que no se puede ni escribir.
    ///
    /// Esto NO abre el agujero que cierra la guarda de arriba. Marcar que el prestador faltó
    /// habilita una reseña LIMITADA sobre el plantón (CU-16 4C); no fabrica la reseña de
    /// servicio, que sigue necesitando que el prestador marque «asistió» o que pasen las 72 h.
    /// </summary>
    public Task<Resultado<CitaDto>> AcusarAusenciaDelPrestadorAsync(
        Guid citaId, AutorDeCambio autor, CancellationToken ct = default) =>
        _trabajo.EnTransaccionAsync(async t =>
        {
            var cita = await _repo.CitaParaCambioAsync(citaId, t);

            if (QuienEs(cita, autor) is not Papel.Buscador)
                return Resultado.Falla<CitaDto>(Fallas.NoEncontrado, "Esa cita no existe.");

            var anterior = cita!.Estado;
            var r = cita.AcusarInasistencia(FaltaEnCita.Prestador);
            if (!r.Exito) return Resultado.Falla<CitaDto>(r.Codigo!, r.Mensaje!);

            cita.BloqueHorario?.Vencer();
            await CerrarCambioAsync(
                cita, anterior, TipoCambio.Asistencia, autor, null, false, _reloj.Ahora, t);
            return Resultado.Ok(ADto(cita, nueva: false));
        }, ct);

    /// <summary>
    /// La cola interna de revisión. Hoy no la consume ninguna pantalla —el panel interno no
    /// existe todavía— pero la consulta vive aquí y no en un comentario, para que quien
    /// construya ese panel no tenga que volver a decidir qué es «una cita en disputa».
    /// </summary>
    public Task<IReadOnlyList<Cita>> CitasEnDisputaAsync(
        int maximo = MaximoPorPasada, CancellationToken ct = default) =>
        _repo.CitasEnDisputaAsync(maximo, ct);

    // ------------------------------------------------------------- lo compartido

    private enum Papel { Ninguno, Buscador, Prestador }

    /// <summary>
    /// Qué es quien actúa respecto de esta cita. <c>Ninguno</c> también cuando la cita no
    /// existe: quien llama responde 404 en los dos casos, y así probar identificadores no
    /// dice de quién es una cita.
    /// </summary>
    private static Papel QuienEs(Cita? cita, AutorDeCambio autor)
    {
        if (cita is null) return Papel.Ninguno;
        if (autor.PerfilPrestadorId is { } perfil && cita.PerfilPrestadorId == perfil)
            return Papel.Prestador;
        return cita.BuscadorId == autor.UsuarioId ? Papel.Buscador : Papel.Ninguno;
    }

    /// <summary>Confirmar y rechazar son del prestador, y solo desde `solicitada`.</summary>
    private async Task<(Cita? Cita, Resultado<CitaDto>? Falla)> ParaResponderAsync(
        Guid citaId, AutorDeCambio autor, CancellationToken ct)
    {
        var cita = await _repo.CitaParaCambioAsync(citaId, ct);
        return QuienEs(cita, autor) is Papel.Prestador
            ? (cita, null)
            : (null, Resultado.Falla<CitaDto>(Fallas.NoEncontrado, "Esa cita no existe."));
    }

    /// <summary>
    /// El historial inmutable de TBL-19 y la versión optimista, en un solo sitio: cada
    /// transición deja su fila, y olvidarse de una dejaría un cambio de estado sin rastro.
    /// </summary>
    private async Task CerrarCambioAsync(
        Cita cita, EstadoCita anterior, TipoCambio tipo, AutorDeCambio autor,
        string? motivo, bool tardio, DateTime ahora, CancellationToken ct)
    {
        cita.Version++;
        _repo.AgregarCambio(new CambioCita
        {
            CitaId = cita.Id,
            Tipo = tipo,
            EstadoAnterior = anterior,
            EstadoNuevo = cita.Estado,
            RealizadoPor = autor.UsuarioId,
            Motivo = motivo,
            EsTardio = tardio,
            OcurridoEn = ahora,
        });

        // El motivo puede ser privado: al registro de auditoría va el QUÉ y no el por qué. Si
        // fue tardía sí va, porque es un hecho de la cita y no una explicación de nadie.
        _auditor.Registrar(Auditar.Entrada(
            $"cita_{tipo.ToString().ToLowerInvariant()}", "cita", cita.Id, ahora,
            actorId: autor.UsuarioId,
            contexto: new { estado = cita.Estado.ToString(), tardio }));

        await _trabajo.GuardarAsync(ct);
    }

    private static string? Limpiar(string? texto) =>
        string.IsNullOrWhiteSpace(texto) ? null : texto.Trim();

    // ------------------------------------------------------------- A-09: lo que mueve el tiempo

    public async Task<ResumenDeCitas> AvanzarPorElTiempoAsync(CancellationToken ct = default)
    {
        var ahora = _reloj.Ahora;

        var expiradas = await ProcesarAsync(
            await _repo.CitasPorExpirarAsync(ahora, MaximoPorPasada, ct), ct,
            (c, a) =>
            {
                // Se vuelve a mirar: mientras tanto el prestador pudo responder.
                if (c.Estado != EstadoCita.Solicitada || c.ExpiraEn is not { } l || a <= l) return null;
                var anterior = c.Estado;
                c.Rechazar();
                c.BloqueHorario?.Liberar();
                return (anterior, TipoCambio.Rechazo, "Sin respuesta del prestador en 24 horas");
            });

        var porAsistir = await ProcesarAsync(
            await _repo.CitasTerminadasAsync(ahora, MaximoPorPasada, ct), ct,
            (c, a) =>
            {
                if (c.Estado != EstadoCita.Confirmada || c.BloqueHorario is not { } b || b.FinEn > a) return null;
                var anterior = c.Estado;
                c.PasarAPendienteAsistencia();
                return (anterior, TipoCambio.Asistencia, "Pasó la hora de la cita");
            });

        var asumidas = await ProcesarAsync(
            await _repo.CitasSinAsistenciaAsync(ahora - PlazoParaMarcarAsistencia, MaximoPorPasada, ct), ct,
            (c, a) =>
            {
                if (c.Estado != EstadoCita.PendienteAsistencia || c.BloqueHorario is not { } b
                    || b.FinEn > a - PlazoParaMarcarAsistencia) return null;
                var anterior = c.Estado;
                c.AsumirRealizada();
                c.BloqueHorario?.Vencer();
                return (anterior, TipoCambio.Asistencia, "Sin asistencia marcada en 72 horas: se asume realizada");
            });

        return new ResumenDeCitas(expiradas, porAsistir, asumidas);
    }

    /// <summary>
    /// Una cita, una transacción. Si una falla por concurrencia (alguien la tocó justo ahora) no
    /// tumba a las otras 199: queda para la pasada siguiente, que la verá ya cambiada.
    /// </summary>
    private async Task<int> ProcesarAsync(
        IReadOnlyList<Guid> ids, CancellationToken ct,
        Func<Cita, DateTime, (EstadoCita Anterior, TipoCambio Tipo, string Motivo)?> transicion)
    {
        var hechas = 0;
        foreach (var id in ids)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var ok = await _trabajo.EnTransaccionAsync(async t =>
                {
                    var cita = await _repo.CitaParaCambioAsync(id, t);
                    if (cita is null) return false;

                    var ahora = _reloj.Ahora;
                    if (transicion(cita, ahora) is not { } cambio) return false;

                    cita.Version++;
                    _repo.AgregarCambio(new CambioCita
                    {
                        CitaId = cita.Id,
                        Tipo = cambio.Tipo,
                        EstadoAnterior = cambio.Anterior,
                        EstadoNuevo = cita.Estado,
                        RealizadoPor = null,        // TBL-19: nulo si lo hizo el Planificador
                        Motivo = cambio.Motivo,
                        OcurridoEn = ahora,
                    });
                    await _trabajo.GuardarAsync(t);
                    return true;
                }, ct);
                if (ok) hechas++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException && EsConcurrencia(ex))
            {
                // La toma la pasada siguiente.
            }
        }
        return hechas;
    }

    /// <summary>
    /// La aplicación no conoce EF Core (una regla de arquitectura lo vigila), así que reconoce la
    /// excepción de concurrencia por su nombre.
    /// </summary>
    private static bool EsConcurrencia(Exception ex) =>
        ex.GetType().Name == "DbUpdateConcurrencyException";

    // ------------------------------------------------------------- lo que consume MOD-11

    public Task<bool> CitaRealizadaAsync(
        Guid buscadorId, Guid perfilPrestadorId, CancellationToken ct = default) =>
        _repo.HuboCitaRealizadaAsync(buscadorId, perfilPrestadorId, ct);

    // ------------------------------------------------------------- traducción

    private static CitaDto ADto(Cita c, bool nueva, string? servicio = null) => new(
        c.Id,
        c.Codigo,
        c.Estado.ToString(),
        c.Modo.ToString(),
        c.BloqueHorario?.InicioEn ?? default,
        c.BloqueHorario?.FinEn ?? default,
        servicio ?? c.PrestadorServicio?.Servicio?.Nombre ?? "",
        c.PrestadorServicioId,
        c.PerfilPrestadorId,
        c.PerfilPrestador?.NombrePublico ?? "",
        c.NotaBuscador,
        c.ExpiraEn,
        nueva);
}
