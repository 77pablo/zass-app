using Zass.Aplicacion.Comun;
using Zass.Aplicacion.Puertos;
using Zass.Dominio.Comun;

namespace Zass.Aplicacion.Credenciales;

/// <summary>Lo que hizo una corrida de CS-06. Solo números: va a <c>trabajos_de_fondo.ultimo_resumen</c>.</summary>
public sealed record ResumenDeVencimientos(int Vencidas)
{
    public override string ToString() => $"credenciales vencidas={Vencidas}";
}

/// <summary>
/// CS-06 «Revocar credencial vencida» (T8 de estados-credencial.md). La corre el planificador (A-09)
/// una vez al día: pasa a <c>vencida</c> cada credencial <c>verificada</c> con <c>vence_en &lt; hoy</c>
/// (hoy de Chile), deja traza y avisa al prestador dueño (CS-07, tipo <c>CredencialVencida</c>).
///
/// <para>
/// <b>Por qué existe.</b> La auditoría del 22-nov-2026 encontró que este caso NUNCA se programó:
/// <c>Credencial.Revocar()</c> no tenía ninguna referencia, así que un título vencido conservaba el
/// sello «Verificada» para siempre. El sello ahora también mira la fecha
/// (<c>Credencial.TieneSelloVigente</c>), así que el público deja de verlo aunque este trabajo se
/// atrase; lo que este trabajo añade es el estado guardado, la traza y el aviso.
/// </para>
///
/// <para>
/// Cada credencial va en su propia transacción y el cambio es condicional en la base (sigue
/// verificada y vencida): si el prestador la edita en el mismo instante (T7, pasa a revisión), no
/// se le pisa; y si dos réplicas corrieran a la vez, solo una la vence y solo una avisa.
/// </para>
/// </summary>
public sealed class ServicioVencimientoCredenciales
{
    /// <summary>Cuántas se leen por vuelta. Se repite hasta que no quede ninguna.</summary>
    public const int Lote = 200;

    private readonly IRepositorioCredenciales _repo;
    private readonly IUnidadDeTrabajo _trabajo;
    private readonly IRelojSistema _reloj;
    private readonly IAuditor _auditor;
    private readonly INotificaciones _avisos;

    public ServicioVencimientoCredenciales(
        IRepositorioCredenciales repo, IUnidadDeTrabajo trabajo, IRelojSistema reloj,
        IAuditor auditor, INotificaciones avisos)
    {
        _repo = repo;
        _trabajo = trabajo;
        _reloj = reloj;
        _auditor = auditor;
        _avisos = avisos;
    }

    public async Task<ResumenDeVencimientos> VencerAsync(CancellationToken ct = default)
    {
        var ahora = _reloj.Ahora;
        var hoy = HoraDeChile.DiaLocal(ahora);
        var vencidas = 0;
        var vistas = new HashSet<Guid>();

        while (true)
        {
            var lote = (await _repo.VerificadasPasadasDeFechaAsync(hoy, Lote, ct))
                .Where(c => vistas.Add(c.CredencialId)).ToList();
            if (lote.Count == 0) break;

            foreach (var c in lote)
            {
                var hecha = await _trabajo.EnTransaccionAsync(async t =>
                {
                    if (!await _repo.VencerSiCorrespondeAsync(c.CredencialId, hoy, t)) return false;

                    _auditor.Registrar(Auditar.Entrada(
                        "credencial.vencida", "credencial", c.CredencialId, ahora,
                        contexto: new { venceEn = c.VenceEn.ToString("yyyy-MM-dd") }));
                    _avisos.Encolar(new Notificacion
                    {
                        DestinatarioId = c.UsuarioId,
                        Tipo = TipoNotificacion.CredencialVencida,
                        Canal = CanalNotificacion.Correo,
                        // Una por credencial y fecha de vencimiento: si la renueva y vuelve a
                        // vencer otro día, es otro aviso.
                        ClaveIdempotencia = $"credencial-vencida:{c.CredencialId:N}:{c.VenceEn:yyyy-MM-dd}",
                        Asunto = "Venció una de tus credenciales en ZASS!",
                        Cuerpo = CargaDeAviso.ConCampos(("credencial", c.Nombre)),
                        ProgramadaPara = ahora,
                    });
                    await _trabajo.GuardarAsync(t);
                    return true;
                }, ct);
                if (hecha) vencidas++;
            }
        }

        return new ResumenDeVencimientos(vencidas);
    }
}
