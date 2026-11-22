using Zass.Aplicacion.Comun;
using Zass.Aplicacion.Puertos;
using Zass.Dominio.Comun;

namespace Zass.Aplicacion.Credenciales;

/// <summary>
/// JQ-16 · CU-05 4D.3. «Un borrador sin actividad por 90 días se elimina junto con sus archivos,
/// tras un aviso a los 83 días.» Lo corre el planificador (A-09) una vez por ciclo.
///
/// Las reglas, en orden:
/// 1. Con 83 días o más sin actividad y sin aviso para ESA actividad, se encola el aviso. La clave
///    lleva la fecha de la última actividad: dos corridas no avisan dos veces, y si la persona
///    sube un documento después del aviso, el ciclo empieza de cero con una clave nueva.
/// 2. Se borra solo si ya hubo aviso, con 90 días sin actividad Y al menos 7 desde el aviso. La
///    segunda condición importa si el planificador estuvo caído: avisar el día 95 y borrar el 96
///    no sería «tras un aviso». Nunca se borra sin aviso.
/// 3. Los archivos se borran DESPUÉS de que la base confirmó, igual que
///    <see cref="ServicioCredenciales.EliminarAsync"/>: lo peor posible es un archivo huérfano,
///    nunca una fila que apunta a un documento que ya no existe.
/// </summary>
public sealed class ServicioBorradoresAbandonados
{
    public const int DiasHastaElAviso = 83;
    public const int DiasHastaElBorrado = 90;
    public const int DiasMinimosDesdeElAviso = 7;

    private readonly IRepositorioBorradores _borradores;
    private readonly IRepositorioCredenciales _credenciales;
    private readonly INotificaciones _avisos;
    private readonly IUnidadDeTrabajo _trabajo;
    private readonly IAuditor _auditor;
    private readonly IRelojSistema _reloj;
    private readonly IAlmacenDeArchivos? _almacen;

    public ServicioBorradoresAbandonados(
        IRepositorioBorradores borradores, IRepositorioCredenciales credenciales, INotificaciones avisos,
        IUnidadDeTrabajo trabajo, IAuditor auditor, IRelojSistema reloj, IAlmacenDeArchivos? almacen = null)
    {
        _borradores = borradores;
        _credenciales = credenciales;
        _avisos = avisos;
        _trabajo = trabajo;
        _auditor = auditor;
        _reloj = reloj;
        _almacen = almacen;
    }

    /// <summary>La clave del aviso: una por borrador y por actividad (R-18, índice único).</summary>
    public static string ClaveDelAviso(Guid credencialId, DateTime ultimaActividad) =>
        $"BorradorPorEliminarse:{credencialId}:{ultimaActividad.ToUniversalTime():yyyyMMddHHmmss}";

    /// <summary>Lo que el planificador anota: solo números, nunca datos de nadie.</summary>
    public async Task<string> CorrerAsync(CancellationToken ct = default)
    {
        var ahora = _reloj.Ahora;
        var candidatos = await _borradores.InactivosAsync(ahora.AddDays(-DiasHastaElAviso), ct);

        var avisados = 0;
        var borrados = 0;
        var archivos = new List<string>();

        foreach (var b in candidatos)
        {
            var clave = ClaveDelAviso(b.CredencialId, b.UltimaActividad);
            var avisadoEn = await _avisos.ProgramadaParaAsync(clave, ct);

            if (avisadoEn is null)
            {
                var borrarEl = Max(b.UltimaActividad.AddDays(DiasHastaElBorrado), ahora.AddDays(DiasMinimosDesdeElAviso));
                _avisos.Encolar(new Notificacion
                {
                    DestinatarioId = b.UsuarioId,
                    Tipo = TipoNotificacion.BorradorPorEliminarse,
                    Canal = CanalNotificacion.Correo,
                    ClaveIdempotencia = clave,
                    Asunto = "Tu borrador de credencial se eliminará pronto",
                    Cuerpo = CargaDeAviso.ConCampos(
                        ("credencial", b.Nombre),
                        ("borrarEl", borrarEl.ToString("yyyy-MM-dd"))),
                    ProgramadaPara = ahora,
                });
                avisados++;
                continue;
            }

            var sePuedeBorrar = ahora >= b.UltimaActividad.AddDays(DiasHastaElBorrado)
                                && ahora >= avisadoEn.Value.AddDays(DiasMinimosDesdeElAviso);
            if (!sePuedeBorrar) continue;

            // Se vuelve a leer: entre la consulta y aquí pudo enviarse a revisión o borrarse.
            var credencial = await _credenciales.PorIdAsync(b.CredencialId, ct);
            if (credencial is null || credencial.Estado != EstadoCredencial.Borrador) continue;

            var claves = credencial.Documentos.Select(d => d.ClaveAlmacenamiento).ToList();
            archivos.AddRange(claves);
            _credenciales.Eliminar(credencial);
            _auditor.Registrar(Auditar.Entrada(
                "eliminar_borrador_abandonado", "credencial", credencial.Id, ahora,
                contexto: new { documentos = claves.Count }));
            borrados++;
        }

        if (avisados + borrados == 0) return "borradores: sin cambios";

        await _trabajo.GuardarAsync(ct);

        if (_almacen is not null)
            foreach (var clave in archivos)
                await _almacen.BorrarAsync(clave, CancellationToken.None);

        return $"borradores: {avisados} avisados, {borrados} borrados";
    }

    private static DateTime Max(DateTime a, DateTime b) => a > b ? a : b;
}
