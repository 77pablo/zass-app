using Zass.Aplicacion.Identidad;
using Zass.Aplicacion.Pruebas.Dobles;
using Zass.Dominio.Comun;
using Zass.Dominio.Identidad;

namespace Zass.Aplicacion.Pruebas.Identidad;

/// <summary>
/// Revision de seguridad del 25-nov-2026, punto 1 (decision de Pablo): el bloqueo por intentos sube
/// de 10 a 30 fallos seguidos —con el limite por IP de 5 cada 15 min, con 10 bastaban 2 IP para
/// bloquear una cuenta ajena— y la dueña recibe un correo, uno por dia como mucho.
/// </summary>
public class AvisoDeBloqueoPruebas
{
    private const string Clave = "contrasena-larga-1";

    private readonly BaseEnMemoria _base = new();
    private readonly RelojFijo _reloj = new();
    private readonly HashDeJuguete _hash = new();
    private readonly SecretosDeterministas _secretos = new();
    private readonly AuditoriaEnMemoria _auditoria = new();
    private readonly ColaEnMemoria _cola = new();
    private readonly ServicioSesion _sesion;
    private readonly ServicioMfa _mfa;

    public AvisoDeBloqueoPruebas()
    {
        var usuarios = new RepositorioUsuariosEnMemoria(_base);
        _sesion = new ServicioSesion(usuarios, new RepositorioSesionesEnMemoria(_base),
            _base, _hash, _secretos, _reloj, _auditoria, _cola);
        _mfa = new ServicioMfa(usuarios, _base, _hash, _secretos, new CifradoDeJuguete(), _reloj, _auditoria, _sesion, _cola);
    }

    private Usuario UnaCuenta()
    {
        var u = new Usuario
        {
            Correo = "rosa@correo.cl", Nombre = "Rosa", HashContrasena = _hash.Hash(Clave),
            Estado = EstadoCuenta.Activa, CorreoVerificado = true,
        };
        _base.Usuarios.Add(u);
        return u;
    }

    private async Task Fallar(int veces)
    {
        for (var i = 0; i < veces; i++)
            Assert.False((await _sesion.EntrarAsync(new IniciarSesionCmd("rosa@correo.cl", "otra-contrasena-mala"))).Exito);
    }

    private int AvisosDeBloqueo() => _cola.Encoladas.Count(n => n.Tipo == TipoNotificacion.CuentaBloqueadaPorIntentos);

    [Fact]
    public void Son_treinta_fallos_y_quince_minutos()
    {
        Assert.Equal(30, ServicioSesion.IntentosParaBloquear);
        Assert.Equal(TimeSpan.FromMinutes(15), ServicioSesion.DuracionDelBloqueo);
    }

    [Fact]
    public async Task Veintinueve_fallos_no_bloquean_ni_avisan_y_el_trigesimo_si()
    {
        var u = UnaCuenta();

        await Fallar(29);
        Assert.False(u.EstaBloqueada(_reloj.Ahora));
        Assert.Equal(0, AvisosDeBloqueo());

        await Fallar(1);
        Assert.True(u.EstaBloqueada(_reloj.Ahora));
        var aviso = Assert.Single(_cola.Encoladas, n => n.Tipo == TipoNotificacion.CuentaBloqueadaPorIntentos);
        Assert.Equal(u.Id, aviso.DestinatarioId);
        Assert.Equal(CanalNotificacion.Correo, aviso.Canal);
    }

    /// <summary>
    /// Quien prueba contraseñas puede volver a bloquearla cada 15 minutos: un correo por bloqueo le
    /// llenaria la bandeja. Uno por dia; al dia siguiente, otro.
    /// </summary>
    [Fact]
    public async Task Un_aviso_por_dia_aunque_la_bloqueen_varias_veces()
    {
        UnaCuenta();
        _reloj.Avanzar(TimeSpan.FromHours(1) - TimeSpan.FromTicks(_reloj.Ahora.TimeOfDay.Ticks)); // 01:00 UTC

        await Fallar(30);
        _reloj.Avanzar(ServicioSesion.DuracionDelBloqueo);
        await Fallar(30);
        Assert.Equal(1, AvisosDeBloqueo());

        _reloj.Avanzar(TimeSpan.FromDays(1));
        await Fallar(30);
        Assert.Equal(2, AvisosDeBloqueo());
    }

    [Fact]
    public async Task Los_codigos_malos_del_segundo_paso_tambien_avisan_al_bloquear()
    {
        var u = UnaCuenta();
        var alta = await _mfa.IniciarAltaAsync(u.Id);
        Assert.True((await _mfa.ConfirmarAltaAsync(u.Id,
            new ConfirmarMfaCmd(Clave, Totp.Codigo(alta.Valor!.Secreto, Totp.PasoDe(_reloj.Ahora))))).Exito);

        for (var i = 0; i < ServicioSesion.IntentosParaBloquear; i++)
        {
            var desafio = (await _sesion.EntrarAsync(new IniciarSesionCmd("rosa@correo.cl", Clave))).Valor!.Desafio!;
            Assert.False((await _mfa.SegundoPasoAsync(new SegundoPasoCmd(desafio.Desafio, "ZZZZZ-ZZZZZ"))).Exito);
        }

        Assert.True(u.EstaBloqueada(_reloj.Ahora));
        Assert.Equal(1, AvisosDeBloqueo());
    }

    [Fact]
    public async Task Un_correo_que_no_existe_no_avisa_a_nadie()
    {
        for (var i = 0; i < 40; i++)
            await _sesion.EntrarAsync(new IniciarSesionCmd("nadie@correo.cl", "otra-contrasena-mala"));

        Assert.Equal(0, AvisosDeBloqueo());
    }
}
