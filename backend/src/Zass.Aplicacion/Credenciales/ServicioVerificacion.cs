using Zass.Aplicacion.Comun;
using Zass.Aplicacion.Puertos;
using Zass.Dominio.Comun;
using Zass.Dominio.Credenciales;

namespace Zass.Aplicacion.Credenciales;

/// <summary>CU-06. Ver <see cref="IServicioVerificacion"/>.</summary>
public sealed class ServicioVerificacion : IServicioVerificacion
{
    /// <summary>CC-03: cuánto dura la reserva de una credencial tomada.</summary>
    public static readonly TimeSpan DuracionDeLaReserva = TimeSpan.FromMinutes(30);

    public const int LargoMinimoDetalle = 10;
    public const int LargoMinimoDetalleSinDocumentos = 20;
    public const int LargoMaximoDetalle = 1000;

    private readonly IRepositorioCredenciales _repo;
    private readonly IUnidadDeTrabajo _trabajo;
    private readonly IRelojSistema _reloj;
    private readonly IAuditor _auditor;
    private readonly OpcionesVerificacion _opciones;

    public ServicioVerificacion(
        IRepositorioCredenciales repo, IUnidadDeTrabajo trabajo,
        IRelojSistema reloj, IAuditor auditor, OpcionesVerificacion opciones)
    {
        _repo = repo;
        _trabajo = trabajo;
        _reloj = reloj;
        _auditor = auditor;
        _opciones = opciones;
    }

    // ------------------------------------------------------------- API-29

    public async Task<IReadOnlyList<CredencialEnColaDto>> ColaAsync(
        Guid verificadorId, CancellationToken ct = default)
    {
        var ahora = _reloj.Ahora;
        var cola = await _repo.ColaAsync(verificadorId, ahora, ct);

        // El repositorio ya filtró; esta guarda repite RN-S2-09 por si otro lo implementa distinto:
        // ver la propia cuenta en la cola es justo lo que la regla quiere evitar.
        return cola
            .Where(c => c.PerfilPrestador is null || c.PerfilPrestador.UsuarioId != verificadorId)
            .Select(c => ADto(c, verificadorId, ahora)).ToList();
    }

    // ------------------------------------------------------------- API-30

    public async Task<Resultado<CredencialEnColaDto>> TomarAsync(
        Guid verificadorId, Guid credencialId, CancellationToken ct = default)
    {
        var ahora = _reloj.Ahora;
        var enRevision = await _repo.PorIdParaRevisionAsync(credencialId, ct);

        var noEsRevisable = await NoRevisableAsync<CredencialEnColaDto>(
            enRevision, verificadorId, credencialId, "tomar", ct);
        if (noEsRevisable is not null) return noEsRevisable;
        var credencial = enRevision!;

        if (credencial.EsperandoPrestador)
            return Resultado.Falla<CredencialEnColaDto>(Fallas.Conflicto,
                "Esta credencial está esperando respuesta del prestador.");

        // 4D: si otro la tiene vigente, lo mismo que si perdiera la carrera.
        if (!credencial.ReservaVencida(ahora) && credencial.ReservadaPor != verificadorId)
            return Resultado.Falla<CredencialEnColaDto>(Fallas.Conflicto, "Otro verificador ya la tomó.");

        var hasta = ahora + DuracionDeLaReserva;
        if (!await _repo.ReservarAsync(credencialId, verificadorId, ahora, hasta, ct))
            return Resultado.Falla<CredencialEnColaDto>(Fallas.Conflicto, "Otro verificador ya la tomó.");

        _auditor.Registrar(Auditar.Entrada(
            "tomar_credencial", "credencial", credencialId, ahora, actorId: verificadorId,
            contexto: new { hasta, sinDocumentos = !credencial.TieneRespaldo() }));
        await _trabajo.GuardarAsync(ct);

        // La entidad cargada es anterior a la reserva: se muestra la que se acaba de fijar.
        return Resultado.Ok(ADto(credencial, verificadorId, ahora) with
        {
            ReservadaPorMi = true,
            ReservadaHasta = hasta,
        });
    }

    // ------------------------------------------------------------- API-31

    public async Task<Resultado<ResolucionDto>> ResolverAsync(
        ResolverCredencialCmd cmd, CancellationToken ct = default)
    {
        // Lo que no depende de la base, primero: RN-S2-06, un rechazo exige motivo escrito.
        var detalle = string.IsNullOrWhiteSpace(cmd.Detalle) ? null : cmd.Detalle.Trim();
        if (!Enum.IsDefined(cmd.Resultado))
            return Resultado.Falla<ResolucionDto>(Fallas.Validacion, "Elige qué hacer con la credencial.");
        if (detalle is { Length: > LargoMaximoDetalle })
            return Resultado.Falla<ResolucionDto>(Fallas.Validacion,
                $"El detalle no puede superar los {LargoMaximoDetalle} caracteres.");

        if (cmd.Resultado == ResultadoVerificacion.Rechazada)
        {
            if (cmd.Motivo is null || !Enum.IsDefined(cmd.Motivo.Value))
                return Resultado.Falla<ResolucionDto>(Fallas.Validacion, "Un rechazo necesita un motivo.");
            if (detalle is null || detalle.Length < LargoMinimoDetalle)
                return Resultado.Falla<ResolucionDto>(Fallas.Validacion,
                    $"Explica el rechazo con al menos {LargoMinimoDetalle} caracteres: el prestador lo va a leer.");
        }
        else if (cmd.Resultado == ResultadoVerificacion.AntecedentesSolicitados
                 && (detalle is null || detalle.Length < LargoMinimoDetalle))
            return Resultado.Falla<ResolucionDto>(Fallas.Validacion,
                $"Escribe qué falta, con al menos {LargoMinimoDetalle} caracteres.");

        var ahora = _reloj.Ahora;
        var enRevision = await _repo.PorIdParaRevisionAsync(cmd.CredencialId, ct);

        var noEsRevisable = await NoRevisableAsync<ResolucionDto>(
            enRevision, cmd.VerificadorId, cmd.CredencialId, "resolver", ct);
        if (noEsRevisable is not null) return noEsRevisable;
        var credencial = enRevision!;

        if (credencial.EsperandoPrestador)
            return Resultado.Falla<ResolucionDto>(Fallas.Conflicto,
                "Esta credencial está esperando respuesta del prestador.");

        // 4C.3: la reserva venció o la tomó otro.
        if (credencial.ReservadaPor != cmd.VerificadorId || credencial.ReservaVencida(ahora))
            return Resultado.Falla<ResolucionDto>(Fallas.Conflicto,
                "Ya no tienes reservada esta credencial. Tómala de nuevo para resolverla.");

        var sinDocumentos = !credencial.TieneRespaldo();
        if (cmd.Resultado == ResultadoVerificacion.Verificada && sinDocumentos)
        {
            // Con la bandera apagada una credencial sin respaldo no se verifica nunca (E-410).
            if (!_opciones.PermitirSinDocumentos)
                return Resultado.Falla<ResolucionDto>(Fallas.ReglaDeNegocio,
                    "Esta credencial no tiene documentos de respaldo disponibles: no se puede verificar.");

            // Sin documento, lo único que respalda el sello es lo que el Verificador escribe.
            if (detalle is null || detalle.Length < LargoMinimoDetalleSinDocumentos)
                return Resultado.Falla<ResolucionDto>(Fallas.Validacion,
                    $"Esta credencial no tiene documentos: explica cómo la comprobaste (al menos {LargoMinimoDetalleSinDocumentos} caracteres).");
        }

        // CC-03 también aquí: solo una resolución concurrente consume la reserva.
        if (!await _repo.ConsumirReservaAsync(cmd.CredencialId, cmd.VerificadorId, ahora, ct))
            return Resultado.Falla<ResolucionDto>(Fallas.Conflicto,
                "Esta credencial ya fue resuelta o ya no la tienes reservada.");

        var verificacion = new Verificacion
        {
            CredencialId = credencial.Id,
            VerificadorId = cmd.VerificadorId,
            Resultado = cmd.Resultado,
            Detalle = detalle,
            ResueltaEn = ahora,
        };

        string accion;
        switch (cmd.Resultado)
        {
            case ResultadoVerificacion.Verificada:
                credencial.Verificar(cmd.VerificadorId, ahora);
                verificacion.VigenciaHasta = credencial.VenceEn;
                accion = sinDocumentos ? "verificar_sin_documentos" : "verificar_credencial";
                break;
            case ResultadoVerificacion.Rechazada:
                credencial.Rechazar();
                verificacion.Motivo = cmd.Motivo;
                accion = "rechazar_credencial";
                break;
            default:
                credencial.PedirAntecedentes();
                accion = "pedir_antecedentes";
                break;
        }
        _repo.AgregarVerificacion(verificacion);

        // RN-S2-08: quién, cuándo y por qué.
        _auditor.Registrar(Auditar.Entrada(
            accion, "credencial", credencial.Id, ahora, actorId: cmd.VerificadorId,
            contexto: new
            {
                verificacionId = verificacion.Id,
                resultado = cmd.Resultado.ToString(),
                motivo = cmd.Motivo?.ToString(),
                detalle,
                sinDocumentos,
            }));

        await _trabajo.GuardarAsync(ct);
        return Resultado.Ok(new ResolucionDto(credencial.Id, credencial.Estado, credencial.EsperandoPrestador));
    }

    // ------------------------------------------------------------- comunes

    /// <summary>
    /// Lo que impide revisar sin importar de qué lado venga: inexistente, cuenta propia (RN-S2-09,
    /// 4E.3: error genérico y queda registrado el intento) o ya resuelta (E-409). Nulo si sigue.
    /// </summary>
    private async Task<Resultado<T>?> NoRevisableAsync<T>(
        Credencial? credencial, Guid verificadorId, Guid credencialId, string intento, CancellationToken ct)
    {
        if (credencial is null)
            return Resultado.Falla<T>(Fallas.NoEncontrado, "No encontramos esa credencial.");

        if (credencial.PerfilPrestador?.UsuarioId == verificadorId)
        {
            _auditor.Registrar(Auditar.Entrada(
                "intento_revisar_propia", "credencial", credencialId, _reloj.Ahora,
                actorId: verificadorId, contexto: new { intento }));
            await _trabajo.GuardarAsync(ct);
            return Resultado.Falla<T>(Fallas.NoEncontrado, "No encontramos esa credencial.");
        }

        if (credencial.Estado != EstadoCredencial.EnRevision)
        {
            var cuando = credencial.SelloEn is { } sello ? $" el {sello:dd-MM-yyyy}" : "";
            return Resultado.Falla<T>(Fallas.Conflicto, $"Esta credencial ya fue resuelta{cuando}.");
        }
        return null;
    }

    private static CredencialEnColaDto ADto(Credencial c, Guid verificadorId, DateTime ahora)
    {
        var hoy = DateOnly.FromDateTime(ahora);
        var mia = c.ReservadaPor == verificadorId && !c.ReservaVencida(ahora);
        return new CredencialEnColaDto(
            c.Id,
            c.PerfilPrestador?.NombrePublico ?? "",
            c.PerfilPrestador?.Slug ?? "",
            c.Tipo, c.Nombre,
            c.EntidadCertificadora?.Nombre,
            c.EntidadCertificadora?.Catalogada,
            c.ObtenidaEn, c.VenceEn,
            c.VenceEn is { } vence && vence < hoy,
            c.Documentos.Count,
            !c.TieneRespaldo(),
            c.CreadaEn,
            mia,
            mia ? c.ReservadaHasta : null);
    }
}
