using System.Text;
using Zass.Aplicacion.Comun;
using Zass.Aplicacion.Identidad;
using Zass.Aplicacion.Puertos;
using Zass.Dominio.Comun;
using Zass.Dominio.Reputacion;

namespace Zass.Aplicacion.Resenas;

/// <summary>
/// MOD-11 · Reseñas. Ver <see cref="IServicioResenas"/>.
///
/// <b>El patrón de todas las operaciones que cambian reseñas.</b> (1) validar lo barato sin tocar
/// la base; (2) leer SIN seguimiento para saber de qué prestador se trata; (3) abrir la
/// transacción y tomar el cerrojo de ese prestador; (4) releer lo que se va a cambiar, ya con el
/// cerrojo; (5) cambiar, guardar y recalcular la reputación; (6) confirmar. El cerrojo es lo que
/// vuelve seguro el recálculo (CC-07) y, de paso, hace que dos publicaciones simultáneas hacia el
/// mismo prestador no puedan saltarse el tope de 90 días ni duplicar una reseña.
/// </summary>
public sealed class ServicioResenas : IServicioResenas
{
    public const int TamanoDePagina = 20;

    private readonly IRepositorioResenas _repo;
    private readonly IServicioIdentidad _identidad;
    private readonly IUnidadDeTrabajo _trabajo;
    private readonly IRelojSistema _reloj;
    private readonly IAuditor _auditor;
    private readonly INotificaciones _avisos;

    public ServicioResenas(
        IRepositorioResenas repo, IServicioIdentidad identidad, IUnidadDeTrabajo trabajo,
        IRelojSistema reloj, IAuditor auditor, INotificaciones avisos)
    {
        _repo = repo;
        _identidad = identidad;
        _trabajo = trabajo;
        _reloj = reloj;
        _auditor = auditor;
        _avisos = avisos;
    }

    // ------------------------------------------------------------- API-46

    public async Task<Resultado<Guid>> RegistrarContactoAsync(
        Guid buscadorId, Guid perfilPrestadorId, OrigenContacto origen, CanalContacto canal,
        Guid? citaId = null, CancellationToken ct = default)
    {
        if (!Enum.IsDefined(origen) || !Enum.IsDefined(canal))
            return Resultado.Falla<Guid>(Fallas.Validacion, "Indica cómo te contactaste con el prestador.");

        // El origen "Cita" es del módulo de agenda, que es quien conoce la cita; y una cita sin
        // ese origen sería un contacto con la cita puesta a mano.
        if ((origen == OrigenContacto.Cita) != (citaId is not null))
            return Resultado.Falla<Guid>(Fallas.Validacion, "El contacto por cita necesita la cita, y solo ese.");

        if (!await HabilitadoAsync(buscadorId, ct))
            return Resultado.Falla<Guid>(FallasResenas.NoElegible, MensajeCuentaNoHabilitada);

        var perfil = await _repo.PerfilAsync(perfilPrestadorId, ct);
        if (perfil is null || perfil.Estado != EstadoPerfil.Publicado)
            return Resultado.Falla<Guid>(Fallas.NoEncontrado, "No encontramos a ese prestador.");

        if (await _identidad.EsMismaPersonaAsync(perfil.UsuarioId, buscadorId, ct))
            return Resultado.Falla<Guid>(Fallas.ReglaDeNegocio, "No puedes registrar un contacto contigo mismo.");

        if (citaId is { } cita)
        {
            var laCita = await _repo.CitaAsync(cita, ct);
            if (laCita is null || laCita.BuscadorId != buscadorId || laCita.PerfilPrestadorId != perfilPrestadorId)
                return Resultado.Falla<Guid>(Fallas.NoEncontrado, "No encontramos esa cita.");

            // Una cita, un contacto: dos contactos darían dos reseñas por la misma cita (RN-S5-02).
            var ligado = await _repo.ContactoDeCitaAsync(cita, ct);
            if (ligado is not null) return Resultado.Ok(ligado.Id);
        }
        else
        {
            // Un solo contacto abierto a la vez, y el reloj de los 7 días corre desde el PRIMERO.
            // Si cada clic en "llamar" abriera otro, repetir el gesto no adelantaría nada, pero
            // llenaría la tabla; y si reiniciara el reloj, sería un castigo por insistir.
            var abierto = (await _repo.ContactosSinResenaAsync(buscadorId, perfilPrestadorId, ct))
                .FirstOrDefault(c => c.CitaId is null);
            if (abierto is not null) return Resultado.Ok(abierto.Id);
        }

        var contacto = new Contacto
        {
            BuscadorId = buscadorId,
            PerfilPrestadorId = perfilPrestadorId,
            CitaId = citaId,
            Origen = origen,
            Canal = canal,
            OcurridoEn = _reloj.Ahora,
        };
        _repo.Agregar(contacto);
        await _trabajo.GuardarAsync(ct);
        return Resultado.Ok(contacto.Id);
    }

    // ------------------------------------------------------------- API-47

    public async Task<Resultado<ResultadoElegibilidad>> EsElegibleAsync(
        Guid buscadorId, Guid perfilPrestadorId, CancellationToken ct = default)
    {
        if (!await HabilitadoAsync(buscadorId, ct))
            return Resultado.Ok(new ResultadoElegibilidad(
                false, null, MotivoDeNoElegibilidad.CuentaNoHabilitada, MensajeCuentaNoHabilitada, null));

        var perfil = await _repo.PerfilAsync(perfilPrestadorId, ct);
        if (perfil is null)
            return Resultado.Falla<ResultadoElegibilidad>(Fallas.NoEncontrado, "No encontramos a ese prestador.");

        if (await _identidad.EsMismaPersonaAsync(perfil.UsuarioId, buscadorId, ct))
            return Resultado.Ok(new ResultadoElegibilidad(
                false, null, MotivoDeNoElegibilidad.Autoresena, MensajeNeutro, null));

        var contactos = await _repo.ContactosSinResenaAsync(buscadorId, perfilPrestadorId, ct);
        if (contactos.Count == 0)
            return Resultado.Ok(new ResultadoElegibilidad(
                false, null, MotivoDeNoElegibilidad.SinContacto,
                "Para dejar una reseña primero tienes que haberte contactado con este prestador.", null));

        var ahora = _reloj.Ahora;
        var ultima = await _repo.UltimaResenaPorContactoAsync(
            buscadorId, perfilPrestadorId, ahora - ReglasDeResenas.TopePorContacto, ct);

        var evaluados = contactos
            .Select(c => ReglasDeResenas.EvaluarContacto(c, ahora, c.CitaId is null ? ultima : null))
            .ToList();

        // Si hay uno elegible se ofrece ese (primero el de cita, que es la prueba más fuerte).
        // Si no, se informa el que antes se cumpliría: es lo más útil que se le puede decir.
        var elegible = evaluados.FirstOrDefault(e => e.Elegible);
        if (elegible is not null) return Resultado.Ok(elegible);

        var conFecha = evaluados.Where(e => e.PuedeDesde is not null).OrderBy(e => e.PuedeDesde).FirstOrDefault();
        return Resultado.Ok(conFecha ?? evaluados[0]);
    }

    // ------------------------------------------------------------- API-48

    public async Task<Resultado<ResultadoPublicacion>> PublicarAsync(
        PublicarResenaCmd cmd, CancellationToken ct = default)
    {
        // CC-04: sin clave no se publica, y no se inventa una por defecto. Una clave que genere el
        // servidor no protege de nada, porque cada reintento generaría una distinta.
        var clave = cmd.Clave?.Trim() ?? "";
        if (clave.Length == 0 || clave.Length > ReglasDeResenas.MaximoClave)
            return Resultado.Falla<ResultadoPublicacion>(Fallas.Validacion,
                "Falta la clave de idempotencia de la operación.");

        var contenido = ValidarContenido(cmd.Calificacion, cmd.Comentario, cmd.Aspectos);
        if (!contenido.Exito)
            return Resultado.Falla<ResultadoPublicacion>(contenido.Codigo!, contenido.Mensaje!);
        var (comentario, aspectos) = contenido.Valor;

        if (!await HabilitadoAsync(cmd.BuscadorId, ct))
            return Resultado.Falla<ResultadoPublicacion>(FallasResenas.NoElegible, MensajeCuentaNoHabilitada);

        var contactoInicial = await _repo.ContactoAsync(cmd.ContactoId, ct);
        if (contactoInicial is null || contactoInicial.BuscadorId != cmd.BuscadorId)
            return Resultado.Falla<ResultadoPublicacion>(Fallas.NoEncontrado, "No encontramos ese contacto.");

        // CU-16 paso 5 y flujo 4E: avisar ANTES de publicar, y dejar enviar igual.
        var datos = ReglasDeResenas.DatosPersonales(comentario);
        if (datos.Count > 0 && !cmd.EnviarIgual)
            return Resultado.Ok(new ResultadoPublicacion(null, false, datos));

        var ahora = _reloj.Ahora;
        MotivoDeNoElegibilidad? rechazo = null;

        var r = await _repo.EnTransaccionAsync(async () =>
        {
            await _repo.BloquearPerfilAsync(contactoInicial.PerfilPrestadorId, ct);

            // Todo lo que sigue se lee de nuevo, ya con el cerrojo: es la revalidación del paso 9.
            var perfil = await _repo.PerfilAsync(contactoInicial.PerfilPrestadorId, ct);
            var contacto = await _repo.ContactoAsync(cmd.ContactoId, ct);
            if (perfil is null || contacto is null)
                return Resultado.Falla<ResultadoPublicacion>(Fallas.NoEncontrado, "No encontramos ese contacto.");

            // Paso 10. Un reintento con el mismo contenido devuelve lo ya creado, sin duplicar ni
            // avisar otra vez (4H.2). Con otro contenido es un segundo intento sobre la misma
            // interacción, y eso es un conflicto (E-409).
            var existente = await _repo.ResenaDeContactoAsync(contacto.Id, ct);
            if (existente is not null)
            {
                return MismoContenido(existente, cmd.Calificacion, comentario, aspectos)
                    ? Resultado.Ok(new ResultadoPublicacion(ADto(existente), true, Array.Empty<string>()))
                    : Resultado.Falla<ResultadoPublicacion>(Fallas.Conflicto,
                        "Ya dejaste una reseña por esta interacción. Puedes editarla mientras dure el plazo.");
            }

            // 4G: la autorreseña se bloquea con un mensaje que no dice qué regla la detectó.
            if (await _identidad.EsMismaPersonaAsync(perfil.UsuarioId, cmd.BuscadorId, ct))
            {
                rechazo = MotivoDeNoElegibilidad.Autoresena;
                return Resultado.Falla<ResultadoPublicacion>(FallasResenas.NoElegible, MensajeNeutro);
            }

            var ultima = contacto.CitaId is null
                ? await _repo.UltimaResenaPorContactoAsync(
                    cmd.BuscadorId, perfil.Id, ahora - ReglasDeResenas.TopePorContacto, ct)
                : null;
            var elegibilidad = ReglasDeResenas.EvaluarContacto(contacto, ahora, ultima);
            if (!elegibilidad.Elegible)
            {
                rechazo = elegibilidad.Motivo;
                return Resultado.Falla<ResultadoPublicacion>(FallasResenas.NoElegible, elegibilidad.Mensaje!);
            }

            var resena = new Resena
            {
                ContactoId = contacto.Id,
                PerfilPrestadorId = perfil.Id,
                AutorId = cmd.BuscadorId,
                Calificacion = (short)cmd.Calificacion,
                Comentario = comentario,
                Tipo = TipoResena.Servicio,
                Estado = EstadoResena.Publicada,
                Aspectos = aspectos.ToList(),
                Editada = false,
                PublicadaEn = ahora,
                EditableHasta = ahora + ReglasDeResenas.PlazoDeEdicion,
            };
            _repo.Agregar(resena);

            _avisos.Encolar(new Notificacion
            {
                DestinatarioId = perfil.UsuarioId,
                Tipo = TipoNotificacion.ResenaNueva,
                // Determinista (CC-04): si el aviso se reintentara, la restricción única lo frena.
                ClaveIdempotencia = $"resena-nueva:{resena.Id}",
                Asunto = "Tienes una reseña nueva",
                // Sin campos a proposito: el correo va al panel del propio prestador y la
                // plantilla no necesita nada de aquí. Y lo que NO va es la calificación ni el
                // comentario: la reseña puede acabar oculta en moderación (CS-17) y un correo
                // enviado ya no se retira.
                Cuerpo = CargaDeAviso.ConCampos(),
                ProgramadaPara = ahora,
            });

            _auditor.Registrar(Auditar.Entrada(
                "resena.publicada", "resena", resena.Id, ahora, cmd.BuscadorId,
                new { perfilPrestadorId = perfil.Id, calificacion = cmd.Calificacion, datosPersonales = datos.Count > 0 }));
            if (datos.Count > 0)
            {
                // 4E.4: se publica, pero queda marcada para revisión preventiva. No hay columna
                // para la marca (ver ESTADO.md); la auditoría es hoy el único lugar donde queda.
                _auditor.Registrar(Auditar.Entrada(
                    "resena.revision_preventiva", "resena", resena.Id, ahora, cmd.BuscadorId,
                    new { motivo = "datos_personales", fragmentos = datos.Count }));
            }

            await _trabajo.GuardarAsync(ct);
            await _repo.RecalcularReputacionAsync(perfil.Id, ct);   // paso 12, misma transacción
            return Resultado.Ok(new ResultadoPublicacion(ADto(resena), false, Array.Empty<string>()));
        }, resultado => resultado.Exito, ct);

        if (!r.Exito && rechazo is { } motivo)
        {
            // 4A.4 y 4G.2: el intento rechazado queda registrado, FUERA de la transacción que se
            // deshizo. Los intentos repetidos son la señal de abuso.
            _auditor.Registrar(Auditar.Entrada(
                motivo == MotivoDeNoElegibilidad.Autoresena ? "resena.autoresena_bloqueada" : "resena.intento_rechazado",
                "contacto", cmd.ContactoId, ahora, cmd.BuscadorId, new { motivo = motivo.ToString() }));
            await _trabajo.GuardarAsync(ct);
        }
        return r;
    }

    // ------------------------------------------------------------- API-49

    public async Task<Resultado<ResultadoPublicacion>> EditarAsync(
        EditarResenaCmd cmd, CancellationToken ct = default)
    {
        var contenido = ValidarContenido(cmd.Calificacion, cmd.Comentario, cmd.Aspectos);
        if (!contenido.Exito)
            return Resultado.Falla<ResultadoPublicacion>(contenido.Codigo!, contenido.Mensaje!);
        var (comentario, aspectos) = contenido.Valor;

        var inicial = await _repo.ResenaPorIdAsync(cmd.ResenaId, ct);
        // La de otra persona se contesta igual que una que no existe (seguridad.md, sección 5).
        if (inicial is null || inicial.AutorId != cmd.AutorId)
            return Resultado.Falla<ResultadoPublicacion>(Fallas.NoEncontrado, "No encontramos esa reseña.");

        var datos = ReglasDeResenas.DatosPersonales(comentario);
        if (datos.Count > 0 && !cmd.EnviarIgual)
            return Resultado.Ok(new ResultadoPublicacion(null, false, datos));

        var ahora = _reloj.Ahora;
        return await _repo.EnTransaccionAsync(async () =>
        {
            await _repo.BloquearPerfilAsync(inicial.PerfilPrestadorId, ct);
            var resena = await _repo.ResenaParaActualizarAsync(cmd.ResenaId, ct);
            if (resena is null || resena.AutorId != cmd.AutorId)
                return Resultado.Falla<ResultadoPublicacion>(Fallas.NoEncontrado, "No encontramos esa reseña.");

            // Estado antes que plazo: una reseña oculta o retirada no se edita aunque esté a tiempo.
            if (resena.Estado != EstadoResena.Publicada)
                return Resultado.Falla<ResultadoPublicacion>(Fallas.ReglaDeNegocio,
                    "Esta reseña no se puede editar en este momento.");
            if (!resena.PuedeEditarse(ahora))
                return Resultado.Falla<ResultadoPublicacion>(Fallas.ReglaDeNegocio,
                    "El plazo de 72 horas para editar tu reseña ya terminó.");

            // Sin cambios no se toca nada: marcarla "editada" por guardar lo mismo sería mentirle
            // al lector.
            if (MismoContenido(resena, cmd.Calificacion, comentario, aspectos))
                return Resultado.Ok(new ResultadoPublicacion(ADto(resena), true, Array.Empty<string>()));

            // 4D.2: la versión anterior se guarda en la auditoría antes de pisarla.
            _auditor.Registrar(Auditar.Entrada(
                "resena.editada", "resena", resena.Id, ahora, cmd.AutorId,
                new
                {
                    calificacionAnterior = (int)resena.Calificacion,
                    comentarioAnterior = resena.Comentario,
                    aspectosAnteriores = resena.Aspectos,
                    calificacionNueva = cmd.Calificacion,
                    datosPersonales = datos.Count > 0,
                }));
            if (datos.Count > 0)
                _auditor.Registrar(Auditar.Entrada(
                    "resena.revision_preventiva", "resena", resena.Id, ahora, cmd.AutorId,
                    new { motivo = "datos_personales", fragmentos = datos.Count }));

            resena.Calificacion = (short)cmd.Calificacion;
            resena.Comentario = comentario;
            resena.Aspectos = aspectos.ToList();
            resena.Editada = true;

            await _trabajo.GuardarAsync(ct);
            await _repo.RecalcularReputacionAsync(resena.PerfilPrestadorId, ct);
            return Resultado.Ok(new ResultadoPublicacion(ADto(resena), false, Array.Empty<string>()));
        }, resultado => resultado.Exito, ct);
    }

    // ------------------------------------------------------------- API-50

    public async Task<Resultado<bool>> ResponderAsync(
        Guid resenaId, Guid prestadorUsuarioId, string texto, CancellationToken ct = default)
    {
        var limpio = texto?.Trim() ?? "";
        if (limpio.Length == 0)
            return Resultado.Falla<bool>(Fallas.Validacion, "Escribe tu respuesta.");
        if (ReglasDeResenas.Largo(limpio) > ReglasDeResenas.RespuestaMaxima)
            return Resultado.Falla<bool>(Fallas.Validacion,
                $"La respuesta no puede pasar de {ReglasDeResenas.RespuestaMaxima} caracteres.");

        var inicial = await _repo.ResenaPorIdAsync(resenaId, ct);
        var perfil = inicial is null ? null : await _repo.PerfilAsync(inicial.PerfilPrestadorId, ct);

        // Solo el dueño del perfil reseñado responde; para cualquier otro la reseña no existe.
        if (inicial is null || perfil is null || perfil.UsuarioId != prestadorUsuarioId
            || inicial.Estado != EstadoResena.Publicada)
            return Resultado.Falla<bool>(Fallas.NoEncontrado, "No encontramos esa reseña.");

        var ahora = _reloj.Ahora;
        return await _repo.EnTransaccionAsync(async () =>
        {
            // El índice único de la respuesta (R-14) tumbaría con un 500 a dos respuestas
            // simultáneas; el cerrojo las pone en fila y la segunda se convierte en edición.
            await _repo.BloquearPerfilAsync(perfil.Id, ct);
            var resena = await _repo.ResenaParaActualizarAsync(resenaId, ct);
            if (resena is null || resena.Estado != EstadoResena.Publicada)
                return Resultado.Falla<bool>(Fallas.NoEncontrado, "No encontramos esa reseña.");

            bool creada;
            if (resena.Respuesta is null)
            {
                var respuesta = new RespuestaResena { ResenaId = resena.Id, Texto = limpio, PublicadaEn = ahora };
                _repo.Agregar(respuesta);
                _avisos.Encolar(new Notificacion
                {
                    DestinatarioId = resena.AutorId,
                    Tipo = TipoNotificacion.ResenaRespondida,
                    ClaveIdempotencia = $"resena-respondida:{resena.Id}",
                    Asunto = "El prestador respondió tu reseña",
                    // El cuerpo es la CARGA del aviso, no el texto del correo: eso lo arma
                    // Plantillas. Lleva el slug para que el enlace vaya a la ficha donde esta
                    // la respuesta; sin el, el correo sale igual pero apunta al inicio.
                    Cuerpo = CargaDeAviso.ConCampos(("slug", perfil.Slug)),
                    ProgramadaPara = ahora,
                });
                _auditor.Registrar(Auditar.Entrada(
                    "resena.respondida", "resena", resena.Id, ahora, prestadorUsuarioId));
                creada = true;
            }
            else
            {
                resena.Respuesta.Texto = limpio;
                resena.Respuesta.EditadaEn = ahora;
                _auditor.Registrar(Auditar.Entrada(
                    "resena.respuesta_editada", "resena", resena.Id, ahora, prestadorUsuarioId));
                creada = false;
            }

            await _trabajo.GuardarAsync(ct);
            return Resultado.Ok(creada);
        }, resultado => resultado.Exito, ct);
    }

    // ------------------------------------------------------------- API-51

    public async Task<Resultado> ReportarAsync(
        Guid resenaId, Guid autorId, MotivoReporte motivo, string? detalle,
        CancellationToken ct = default)
    {
        if (!Enum.IsDefined(motivo))
            return Resultado.Falla(Fallas.Validacion, "Elige el motivo del reporte.");
        var detalleLimpio = string.IsNullOrWhiteSpace(detalle) ? null : detalle.Trim();
        if (detalleLimpio is not null && ReglasDeResenas.Largo(detalleLimpio) > ReglasDeResenas.DetalleMaximo)
            return Resultado.Falla(Fallas.Validacion,
                $"El detalle no puede pasar de {ReglasDeResenas.DetalleMaximo} caracteres.");

        var usuario = await _identidad.ObtenerUsuarioAsync(autorId, ct);
        if (usuario is null || !usuario.CorreoVerificado || usuario.Estado != EstadoCuenta.Activa)
            return Resultado.Falla(Fallas.ReglaDeNegocio, "Para reportar una reseña necesitas la cuenta activa y el correo verificado.");

        var inicial = await _repo.ResenaPorIdAsync(resenaId, ct);
        if (inicial is null || inicial.Estado == EstadoResena.Retirada)
            return Resultado.Falla(Fallas.NoEncontrado, "No encontramos esa reseña.");
        if (inicial.AutorId == autorId)
            return Resultado.Falla(Fallas.ReglaDeNegocio, "No puedes reportar tu propia reseña.");

        var ahora = _reloj.Ahora;
        return await _repo.EnTransaccionAsync(async () =>
        {
            await _repo.BloquearPerfilAsync(inicial.PerfilPrestadorId, ct);
            var resena = await _repo.ResenaParaActualizarAsync(resenaId, ct);
            if (resena is null || resena.Estado == EstadoResena.Retirada)
                return Resultado.Falla(Fallas.NoEncontrado, "No encontramos esa reseña.");

            if (await _repo.TieneReporteAbiertoAsync(resenaId, autorId, ct))
                return Resultado.Falla(Fallas.Conflicto, "Ya reportaste esta reseña; un moderador la está revisando.");

            var reporte = new ReporteResena
            {
                ResenaId = resenaId,
                ReportadoPor = autorId,
                Motivo = motivo,
                Detalle = detalleLimpio,
                Estado = EstadoReporte.Abierto,
                CreadoEn = ahora,
            };
            _repo.Agregar(reporte);

            // RN-S5-08 y CS-17: el motivo grave oculta la reseña aquí mismo, en la misma
            // transacción, sin esperar a ningún humano. El "menos de 1 minuto" de la regla se
            // cumple por construcción: no hay cola ni trabajo de fondo de por medio.
            var oculta = false;
            if (reporte.EsGrave() && resena.Estado == EstadoResena.Publicada)
            {
                resena.Ocultar();
                oculta = true;
            }

            _auditor.Registrar(Auditar.Entrada(
                "resena.reportada", "resena", resenaId, ahora, autorId,
                new { motivo = motivo.ToString(), ocultada = oculta }));

            await _trabajo.GuardarAsync(ct);
            // 4F.3: la reputación se recalcula sin ella mientras dura la moderación.
            if (oculta) await _repo.RecalcularReputacionAsync(resena.PerfilPrestadorId, ct);
            return Resultado.Ok();
        }, resultado => resultado.Exito, ct);
    }

    // ------------------------------------------------------------- API-52

    public async Task<Resultado<IReadOnlyList<FilaDeCola>>> ColaDeReportesAsync(
        Guid moderadorId, CancellationToken ct = default)
    {
        if (!await _identidad.TieneRolAsync(moderadorId, Roles.Moderador, ct))
            return Resultado.Falla<IReadOnlyList<FilaDeCola>>(Fallas.NoEncontrado, "No encontrado.");

        return Resultado.Ok(await _repo.ColaAsync(moderadorId, 100, ct));
    }

    // ------------------------------------------------------------- API-53

    public async Task<Resultado> ResolverReporteAsync(
        Guid reporteId, Guid moderadorId, bool retirar, string motivo,
        CancellationToken ct = default)
    {
        var motivoLimpio = motivo?.Trim() ?? "";
        var largo = ReglasDeResenas.Largo(motivoLimpio);
        if (largo < ReglasDeResenas.MotivoResolucionMinimo || largo > ReglasDeResenas.MotivoResolucionMaximo)
            return Resultado.Falla(Fallas.Validacion,
                $"Explica la decisión en {ReglasDeResenas.MotivoResolucionMinimo} a {ReglasDeResenas.MotivoResolucionMaximo} caracteres.");

        if (!await _identidad.TieneRolAsync(moderadorId, Roles.Moderador, ct))
            return Resultado.Falla(Fallas.NoEncontrado, "No encontrado.");

        var inicial = await _repo.ReportePorIdAsync(reporteId, ct);
        var resenaInicial = inicial is null ? null : await _repo.ResenaPorIdAsync(inicial.ResenaId, ct);
        if (inicial is null || resenaInicial is null)
            return Resultado.Falla(Fallas.NoEncontrado, "No encontramos ese reporte.");

        var ahora = _reloj.Ahora;
        ReporteResena? rechazadoPorParte = null;
        var r = await _repo.EnTransaccionAsync(async () =>
        {
            await _repo.BloquearPerfilAsync(resenaInicial.PerfilPrestadorId, ct);
            var abiertos = await _repo.ReportesAbiertosDeResenaAsync(resenaInicial.Id, ct);
            var objetivo = abiertos.FirstOrDefault(x => x.Id == reporteId);
            if (objetivo is null)
                return Resultado.Falla(Fallas.Conflicto, "Este reporte ya fue resuelto.");

            var perfil = await _repo.PerfilAsync(resenaInicial.PerfilPrestadorId, ct);
            if (perfil is null)
                return Resultado.Falla(Fallas.NoEncontrado, "No encontramos ese reporte.");

            // RN-S5-09. Es parte quien escribió la reseña, quien la recibió, o quien reportó
            // cualquiera de los reclamos que esta decisión va a cerrar.
            var partes = new List<Guid> { resenaInicial.AutorId, perfil.UsuarioId };
            partes.AddRange(abiertos.Select(x => x.ReportadoPor));
            foreach (var parte in partes.Distinct())
            {
                if (await _identidad.EsMismaPersonaAsync(moderadorId, parte, ct))
                {
                    rechazadoPorParte = objetivo;
                    return Resultado.Falla(Fallas.NoEncontrado, "No puedes resolver un reporte en el que eres parte.");
                }
            }

            var resena = await _repo.ResenaParaActualizarAsync(resenaInicial.Id, ct);
            if (resena is null)
                return Resultado.Falla(Fallas.NoEncontrado, "No encontramos ese reporte.");

            // Una reseña tiene una sola decisión: todos los reclamos abiertos sobre ella se cierran
            // con la misma. Si no, "mantener" podría devolver a la vista una reseña que otro
            // reporte grave, todavía abierto, tenía oculta.
            foreach (var reporte in abiertos)
            {
                reporte.Estado = retirar ? EstadoReporte.ResueltoRetira : EstadoReporte.ResueltoMantiene;
                reporte.ResueltoPor = moderadorId;
                reporte.Resolucion = motivoLimpio;
                reporte.ResueltoEn = ahora;
            }

            if (retirar) resena.Retirar();
            else if (resena.Estado == EstadoResena.OcultaEnModeracion) resena.Restaurar();

            // RN-S5-12: retirar deja traza. Mantener también: es una decisión de moderación.
            _auditor.Registrar(Auditar.Entrada(
                retirar ? "resena.retirada" : "resena.mantenida", "resena", resena.Id, ahora, moderadorId,
                new { reporteId, reportesCerrados = abiertos.Count, motivo = motivoLimpio }));

            await _trabajo.GuardarAsync(ct);
            await _repo.RecalcularReputacionAsync(resena.PerfilPrestadorId, ct);
            return Resultado.Ok();
        }, resultado => resultado.Exito, ct);

        if (rechazadoPorParte is not null)
        {
            _auditor.Registrar(Auditar.Entrada(
                "reporte.resolucion_rechazada", "reporte_resena", reporteId, ahora, moderadorId,
                new { motivo = "es_parte" }));
            await _trabajo.GuardarAsync(ct);
        }
        return r;
    }

    // ------------------------------------------------------------- lectura pública

    public async Task<Resultado<PaginaDeResenas>> ResenasPublicasAsync(
        Guid perfilPrestadorId, string? cursor, CancellationToken ct = default)
    {
        var perfil = await _repo.PerfilAsync(perfilPrestadorId, ct);
        // Igual que la ficha: lo que no se ve en la búsqueda tampoco se lee por aquí.
        if (perfil is null || perfil.Estado != EstadoPerfil.Publicado)
            return Resultado.Falla<PaginaDeResenas>(Fallas.NoEncontrado, "No encontramos a ese prestador.");

        DateTime? antesDe = null;
        Guid? antesDeId = null;
        if (!string.IsNullOrWhiteSpace(cursor))
        {
            if (!LeerCursor(cursor, out var fecha, out var id))
                return Resultado.Falla<PaginaDeResenas>(Fallas.Validacion, "El cursor no es válido.");
            antesDe = fecha;
            antesDeId = id;
        }

        // Una de más: si llega, hay página siguiente, y no hace falta contar todo.
        var filas = await _repo.PublicasAsync(perfilPrestadorId, antesDe, antesDeId, TamanoDePagina + 1, ct);
        var hayMas = filas.Count > TamanoDePagina;
        var pagina = filas.Take(TamanoDePagina).ToList();

        return Resultado.Ok(new PaginaDeResenas(
            pagina.Select(APublica).ToList(),
            hayMas ? EscribirCursor(pagina[^1]) : null));
    }

    // ------------------------------------------------------------- apoyo

    private const string MensajeNeutro = "No puedes dejar una reseña en este perfil.";
    private const string MensajeCuentaNoHabilitada =
        "Para dejar una reseña necesitas una cuenta de buscador con el correo verificado.";

    /// <summary>P1 de CU-16: cuenta activa, con rol de buscador y correo verificado.</summary>
    private async Task<bool> HabilitadoAsync(Guid usuarioId, CancellationToken ct)
    {
        var usuario = await _identidad.ObtenerUsuarioAsync(usuarioId, ct);
        return usuario is { CorreoVerificado: true, Estado: EstadoCuenta.Activa }
            && usuario.Roles.Contains(Roles.Buscador);
    }

    /// <summary>
    /// RN-S5-03 y el paso 6. Devuelve el comentario ya recortado y los aspectos sin repetir.
    /// El texto NO se toca más allá de recortar: se guarda crudo y se escapa al renderizar
    /// (RN-S5-04). Quitar o "limpiar" HTML aquí daría una falsa sensación de seguridad y
    /// alteraría lo que la persona escribió.
    /// </summary>
    private static Resultado<(string Comentario, string[] Aspectos)> ValidarContenido(
        int calificacion, string? comentario, IReadOnlyList<string>? aspectos)
    {
        if (calificacion is < ReglasDeResenas.CalificacionMinima or > ReglasDeResenas.CalificacionMaxima)
            return Resultado.Falla<(string, string[])>(Fallas.Validacion, "La calificación va de 1 a 5.");

        var limpio = comentario?.Trim() ?? "";
        var largo = ReglasDeResenas.Largo(limpio);
        if (largo < ReglasDeResenas.ComentarioMinimo || largo > ReglasDeResenas.ComentarioMaximo)
            return Resultado.Falla<(string, string[])>(Fallas.Validacion,
                $"El comentario debe tener entre {ReglasDeResenas.ComentarioMinimo} y {ReglasDeResenas.ComentarioMaximo} caracteres.");

        var lista = (aspectos ?? Array.Empty<string>()).Select(a => a?.Trim().ToLowerInvariant() ?? "").Distinct().ToArray();
        if (lista.Any(a => !ReglasDeResenas.AspectosPermitidos.Contains(a)))
            return Resultado.Falla<(string, string[])>(Fallas.Validacion,
                "Uno de los aspectos marcados no existe: puntualidad, claridad o precio acordado.");

        return Resultado.Ok((limpio, lista));
    }

    private static bool MismoContenido(Resena r, int calificacion, string comentario, string[] aspectos) =>
        r.Calificacion == calificacion
        && r.Comentario == comentario
        && r.Aspectos.OrderBy(x => x).SequenceEqual(aspectos.OrderBy(x => x));

    private static ResenaDto ADto(Resena r) => new(
        r.Id, r.PerfilPrestadorId, r.Calificacion, r.Comentario, r.Aspectos.ToList(),
        r.Tipo, r.Estado, r.Editada, r.PublicadaEn, r.EditableHasta);

    private static ResenaPublicaDto APublica(Resena r) => new(
        r.Id, r.Calificacion, r.Comentario, r.Aspectos.ToList(), r.Tipo, r.Editada, r.PublicadaEn,
        r.Respuesta is null
            ? null
            : new RespuestaPublicaDto(r.Respuesta.Texto, r.Respuesta.PublicadaEn, r.Respuesta.EditadaEn));

    // El cursor es keyset sobre la misma tupla por la que se ordena (publicada_en desc, id).
    private static string EscribirCursor(Resena ultima) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes($"{ultima.PublicadaEn.Ticks}|{ultima.Id}"));

    private static bool LeerCursor(string cursor, out DateTime fecha, out Guid id)
    {
        fecha = default;
        id = default;
        try
        {
            var partes = Encoding.UTF8.GetString(Convert.FromBase64String(cursor)).Split('|');
            if (partes.Length != 2 || !long.TryParse(partes[0], out var ticks)
                || ticks < DateTime.MinValue.Ticks || ticks > DateTime.MaxValue.Ticks
                || !Guid.TryParse(partes[1], out id))
                return false;
            fecha = new DateTime(ticks, DateTimeKind.Utc);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
