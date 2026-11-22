using Zass.Aplicacion.Credenciales;
using Zass.Aplicacion.Pruebas.Dobles;
using Zass.Dominio.Comun;
using Zass.Dominio.Credenciales;
using Zass.Dominio.Identidad;

namespace Zass.Aplicacion.Pruebas.Credenciales;

/// <summary>
/// Auditoría 22-nov-2026, hallazgo 2: una credencial verificada y vencida conservaba el sello.
/// CS-06 (el trabajo que la vence) y el sello que mira la fecha, sin base de datos.
/// </summary>
public class VencimientoPruebas
{
    // 12:00 UTC del 7-nov = 09:00 en Chile: «hoy» es el 7 en los dos relojes.
    private static readonly DateTime Ahora = new(2026, 11, 7, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateOnly Hoy = new(2026, 11, 7);

    private readonly BaseEnMemoria _base = new();
    private readonly AuditoriaEnMemoria _auditoria = new();
    private readonly ColaEnMemoria _avisos = new();
    private readonly RelojFijo _reloj = new(Ahora);
    private readonly ServicioCredenciales _servicio;
    private readonly ServicioVencimientoCredenciales _vencimiento;
    private readonly PerfilPrestador _perfil;

    public VencimientoPruebas()
    {
        var repo = new RepositorioCredencialesEnMemoria(_base);
        _servicio = new ServicioCredenciales(repo, _base, _reloj, _auditoria);
        _vencimiento = new ServicioVencimientoCredenciales(repo, _base, _reloj, _auditoria, _avisos);
        _perfil = new PerfilPrestador { Id = Guid.NewGuid(), UsuarioId = Guid.NewGuid(), NombrePublico = "Estudio", Slug = "estudio" };
        _base.PerfilesPrestador.Add(_perfil);
    }

    private Credencial Una(EstadoCredencial estado, DateOnly? vence, string nombre = "Técnico eléctrico")
    {
        var c = new Credencial
        {
            PerfilPrestadorId = _perfil.Id, Nombre = nombre, Estado = estado, VenceEn = vence,
            ObtenidaEn = new DateOnly(2020, 1, 1), CreadaEn = Ahora,
            SelloPor = estado == EstadoCredencial.Verificada ? Guid.NewGuid() : null,
        };
        _base.Credenciales.Add(c);
        return c;
    }

    // ------------------------------------------------------------- el sello mira la fecha

    [Fact]
    public async Task Una_verificada_con_vence_en_pasado_ya_no_muestra_el_sello_aunque_el_trabajo_no_haya_corrido()
    {
        Una(EstadoCredencial.Verificada, Hoy.AddDays(-1));

        var publica = Assert.Single(await _servicio.CredencialesPublicasAsync(_perfil.Id));

        Assert.Equal("Declarada por el usuario", publica.Etiqueta);
        Assert.False(publica.Verificada);
        Assert.False(await _servicio.TieneVerificadasAsync(_perfil.Id));
    }

    [Fact]
    public async Task El_dia_del_vencimiento_todavia_vale_y_sin_fecha_vale_siempre()
    {
        Una(EstadoCredencial.Verificada, Hoy, "Vence hoy");
        Una(EstadoCredencial.Verificada, null, "Sin vencimiento");

        var publicas = await _servicio.CredencialesPublicasAsync(_perfil.Id);

        Assert.All(publicas, p => Assert.Equal("Verificada", p.Etiqueta));
        Assert.True(await _servicio.TieneVerificadasAsync(_perfil.Id));
    }

    [Fact]
    public void Hoy_es_el_dia_de_Chile_y_no_el_de_UTC()
    {
        // 02:00 UTC del 8-nov son las 23:00 del 7-nov en Chile (verano, UTC-3).
        var c = new Credencial { Estado = EstadoCredencial.Verificada, VenceEn = Hoy };
        var hoyEnChile = HoraDeChile.DiaLocal(new DateTime(2026, 11, 8, 2, 0, 0, DateTimeKind.Utc));

        Assert.Equal(Hoy, hoyEnChile);
        Assert.True(c.TieneSelloVigente(hoyEnChile));
        Assert.False(c.TieneSelloVigente(Hoy.AddDays(1)));
    }

    // ------------------------------------------------------------- CS-06, el trabajo

    [Fact]
    public async Task CS06_pasa_a_vencida_la_verificada_pasada_de_fecha_deja_traza_y_avisa_al_prestador()
    {
        var c = Una(EstadoCredencial.Verificada, Hoy.AddDays(-3), "Instalador SEC clase D");

        var r = await _vencimiento.VencerAsync();

        Assert.Equal(1, r.Vencidas);
        Assert.Equal(EstadoCredencial.Vencida, c.Estado);
        var traza = Assert.Single(_auditoria.Registros);
        Assert.Equal("credencial.vencida", traza.Accion);
        Assert.Equal(c.Id, traza.EntidadId);
        var aviso = Assert.Single(_avisos.Encoladas);
        Assert.Equal(TipoNotificacion.CredencialVencida, aviso.Tipo);
        Assert.Equal(_perfil.UsuarioId, aviso.DestinatarioId);
        Assert.Contains("Instalador SEC clase D", aviso.Cuerpo);
    }

    [Fact]
    public async Task CS06_no_toca_las_que_vencen_hoy_ni_las_sin_fecha_ni_las_que_no_estan_verificadas()
    {
        var hoy = Una(EstadoCredencial.Verificada, Hoy);
        var sinFecha = Una(EstadoCredencial.Verificada, null);
        var enRevision = Una(EstadoCredencial.EnRevision, Hoy.AddDays(-10));
        var rechazada = Una(EstadoCredencial.Rechazada, Hoy.AddDays(-10));

        var r = await _vencimiento.VencerAsync();

        Assert.Equal(0, r.Vencidas);
        Assert.Equal(EstadoCredencial.Verificada, hoy.Estado);
        Assert.Equal(EstadoCredencial.Verificada, sinFecha.Estado);
        Assert.Equal(EstadoCredencial.EnRevision, enRevision.Estado);
        Assert.Equal(EstadoCredencial.Rechazada, rechazada.Estado);
        Assert.Empty(_avisos.Encoladas);
    }

    [Fact]
    public async Task CS06_dos_corridas_vencen_y_avisan_una_sola_vez()
    {
        Una(EstadoCredencial.Verificada, Hoy.AddDays(-1));

        await _vencimiento.VencerAsync();
        var segunda = await _vencimiento.VencerAsync();

        Assert.Equal(0, segunda.Vencidas);
        Assert.Single(_avisos.Encoladas);
    }

    [Fact]
    public async Task CS06_recorre_mas_de_un_lote()
    {
        for (var i = 0; i < ServicioVencimientoCredenciales.Lote + 5; i++)
            Una(EstadoCredencial.Verificada, Hoy.AddDays(-1));

        var r = await _vencimiento.VencerAsync();

        Assert.Equal(ServicioVencimientoCredenciales.Lote + 5, r.Vencidas);
    }
}
