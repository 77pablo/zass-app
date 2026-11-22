using Zass.Aplicacion.Comun;
using Zass.Aplicacion.Puertos;
using Zass.Dominio.Comun;
using Zass.Dominio.Identidad;

namespace Zass.Aplicacion.Identidad;

/// <summary>Lo que hizo una corrida de la purga. Solo números: va a <c>trabajos_de_fondo.ultimo_resumen</c>.</summary>
public sealed record ResumenDePurga(int Purgadas, int RetenidasConActividad)
{
    public override string ToString() =>
        $"cuentas sin verificar purgadas={Purgadas}; retenidas con actividad={RetenidasConActividad}";
}

/// <summary>
/// Auditoría del 22-nov-2026: <b>cuentas sin verificar que secuestran un RUT o un correo.</b>
/// Cualquiera podía registrar una cuenta, sin verificarla nunca, con el RUT (o el correo) de otra
/// persona; el índice único dejaba a la víctima fuera para siempre, y el aviso de RN-S1-13 le
/// llegaba al correo del que la creó. Aquí se borran las cuentas que siguen sin verificar el correo
/// <see cref="PlazoParaVerificar"/> después de crearse, y así sueltan el RUT y el correo.
///
/// <para>
/// <b>Borrado real, no anonimización.</b> Una cuenta que nunca confirmó su correo no tiene historia
/// que conservar (la definición de «purgable» excluye cualquier actividad, ver
/// <see cref="IRepositorioCuentasSinVerificar"/>), y anonimizarla dejaría filas sin dueño para
/// siempre. Lo único que queda es la auditoría: la del registro y una entrada
/// <c>cuenta_sin_verificar_purgada</c> por cada purga, sin correo ni RUT (solo el id).
/// </para>
///
/// <para>
/// Lo corre el planificador cada hora (<c>Trabajos.CuentasSinVerificar</c>), y el registro la usa
/// para una sola cuenta cuando choca con una ya vencida (<see cref="LiberarSiCaducoAsync"/>): así
/// la víctima no espera a la siguiente corrida.
/// </para>
/// </summary>
public sealed class ServicioCuentasSinVerificar
{
    /// <summary>Cuánto tiene una cuenta nueva para verificar el correo antes de borrarse.</summary>
    public static readonly TimeSpan PlazoParaVerificar = TimeSpan.FromHours(72);

    public const int Lote = 200;

    private readonly IRepositorioCuentasSinVerificar _repo;
    private readonly IUnidadDeTrabajo _unidad;
    private readonly IRelojSistema _reloj;
    private readonly IAuditor _auditor;

    public ServicioCuentasSinVerificar(
        IRepositorioCuentasSinVerificar repo, IUnidadDeTrabajo unidad, IRelojSistema reloj, IAuditor auditor)
    {
        _repo = repo;
        _unidad = unidad;
        _reloj = reloj;
        _auditor = auditor;
    }

    /// <summary>Si a esta cuenta ya se le pasó el plazo sin verificar (no mira la actividad).</summary>
    public static bool Caduco(Usuario u, DateTime ahora) =>
        !u.CorreoVerificado && u.Estado == EstadoCuenta.PendienteVerificacion
        && u.CreadoEn <= ahora - PlazoParaVerificar;

    public async Task<ResumenDePurga> PurgarVencidasAsync(CancellationToken ct = default)
    {
        var ahora = _reloj.Ahora;
        var limite = ahora - PlazoParaVerificar;
        var purgadas = 0;
        var vistas = new HashSet<Guid>();

        while (true)
        {
            var lote = (await _repo.PurgablesAsync(limite, Lote, ct)).Where(vistas.Add).ToList();
            if (lote.Count == 0) break;
            foreach (var id in lote)
                if (await PurgarUnaAsync(id, limite, ahora, "planificador", null, ct)) purgadas++;
        }

        return new ResumenDePurga(purgadas, await _repo.ContarRetenidasAsync(limite, ct));
    }

    /// <summary>
    /// El registro chocó con esta cuenta (por correo o por RUT). Si ya caducó y es purgable, se
    /// borra ahora mismo y devuelve true: el dato queda libre. Si no, no toca nada.
    /// </summary>
    public async Task<bool> LiberarSiCaducoAsync(Usuario cuenta, string? ip, CancellationToken ct = default)
    {
        var ahora = _reloj.Ahora;
        if (!Caduco(cuenta, ahora)) return false;
        return await PurgarUnaAsync(cuenta.Id, ahora - PlazoParaVerificar, ahora, "registro", ip, ct);
    }

    private Task<bool> PurgarUnaAsync(
        Guid id, DateTime limite, DateTime ahora, string origen, string? ip, CancellationToken ct) =>
        _unidad.EnTransaccionAsync(async t =>
        {
            if (!await _repo.PurgarAsync(id, limite, t)) return false;
            _auditor.Registrar(Auditar.Entrada(
                "cuenta_sin_verificar_purgada", "usuario", id, ahora,
                contexto: new { origen, plazoHoras = (int)PlazoParaVerificar.TotalHours }, ip: ip));
            await _unidad.GuardarAsync(t);
            return true;
        }, ct);
}
