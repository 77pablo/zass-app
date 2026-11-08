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

    public const int LargoMaximoNota = 500;
    public const int LargoMaximoMotivo = 300;

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
                VigenteDesde = DateOnly.FromDateTime(_reloj.Ahora),
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
                vieja.VigenteHasta = DateOnly.FromDateTime(ahora);
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

        var nuevos = reglas
            .SelectMany(r => r.GenerarBloques(
                DateOnly.FromDateTime(desde), DateOnly.FromDateTime(hasta)))
            .Where(b => b.InicioEn > desde && !yaEstan.Contains(b.InicioEn))
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
