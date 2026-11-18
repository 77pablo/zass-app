using Zass.Aplicacion.Comun;
using Zass.Aplicacion.Puertos;
using Zass.Dominio.Comun;
using Zass.Dominio.Identidad;

namespace Zass.Aplicacion.Identidad;

/// <summary>API-16. Lo que la sesion puede saber de si misma. Sin el hash de la contrasena.</summary>
public sealed record MiCuentaDto(
    Guid Id,
    string Correo,
    string Nombre,
    bool CorreoVerificado,
    EstadoCuenta Estado,
    string[] Roles,
    DateTime CreadoEn);

/// <summary>
/// CS-01 verificar correo, CS-02 recuperar contrasena, CS-04 eliminar cuenta y API-16.
///
/// De los dos tokens de un solo uso se guarda el hash, nunca el valor que viajo por
/// correo: una fuga de la tabla `usuario` no permite tomar cuentas ajenas.
/// </summary>
public sealed class ServicioCuenta
{
    public static readonly TimeSpan VidaDelTokenDeVerificacion = TimeSpan.FromHours(24);
    public static readonly TimeSpan VidaDelTokenDeRecuperacion = TimeSpan.FromHours(1);

    /// <summary>
    /// API-14 responde esto siempre, exista o no el correo. Si distinguiera, cualquiera
    /// podria averiguar quien tiene cuenta en ZASS! probando correos.
    /// </summary>
    /// <summary>
    /// Lo minimo entre dos reenvios del enlace de verificacion, por cuenta. El limite de
    /// peticiones protege por IP y aqui eso no alcanza: el daño no lo recibe quien llama, lo
    /// recibe el dueño del correo.
    /// </summary>
    public static readonly TimeSpan EsperaEntreReenvios = TimeSpan.FromMinutes(2);

    /// <summary>
    /// Lo que responde el reenvio, exista o no la cuenta y este o no verificada. Decir «ya
    /// estaba verificado» seria decir «esa persona tiene cuenta aqui».
    /// </summary>
    private const string RespuestaDeReenvio =
        "Si ese correo tiene una cuenta sin verificar, le llegará un enlace nuevo.";

    private const string RespuestaDeRecuperacion =
        "Si ese correo tiene cuenta, le llegará un enlace para crear una contraseña nueva.";

    private readonly IRepositorioUsuarios _usuarios;
    private readonly IRepositorioPerfiles _perfiles;
    private readonly IUnidadDeTrabajo _unidad;
    private readonly IHashContrasenas _hash;
    private readonly IGeneradorSecretos _secretos;
    private readonly IRelojSistema _reloj;
    private readonly INotificaciones _avisos;
    private readonly IAuditor _auditor;
    private readonly ServicioSesion _sesiones;

    public ServicioCuenta(
        IRepositorioUsuarios usuarios, IRepositorioPerfiles perfiles, IUnidadDeTrabajo unidad,
        IHashContrasenas hash, IGeneradorSecretos secretos, IRelojSistema reloj,
        INotificaciones avisos, IAuditor auditor, ServicioSesion sesiones)
    {
        _usuarios = usuarios;
        _perfiles = perfiles;
        _unidad = unidad;
        _hash = hash;
        _secretos = secretos;
        _reloj = reloj;
        _avisos = avisos;
        _auditor = auditor;
        _sesiones = sesiones;
    }

    // ------------------------------------------------------------------ CS-01

    /// <summary>CS-01 - Verificar correo. API-13. El token se consume al usarse.</summary>
    public async Task<Resultado> VerificarCorreoAsync(string token, CancellationToken ct = default)
    {
        var usuario = await _usuarios.PorTokenVerificacionAsync(_secretos.HashToken(token), ct);

        // El mismo resultado para "no existe" y "ya vencio": el enlace no sirve y punto.
        if (usuario is null || usuario.TokenVerificacionExpiraEn is null
            || usuario.TokenVerificacionExpiraEn <= _reloj.Ahora)
            return Resultado.Falla(Fallas.NoEncontrado,
                "Ese enlace de verificación no sirve. Pide uno nuevo.");

        usuario.VerificarCorreo();
        usuario.TokenVerificacionExpiraEn = null;
        usuario.ActualizadoEn = _reloj.Ahora;

        _auditor.Registrar(Auditar.Entrada(
            "verificar_correo", "usuario", usuario.Id, _reloj.Ahora, actorId: usuario.Id));

        await _unidad.GuardarAsync(ct);
        return Resultado.Ok();
    }

    // ------------------------------------------------------------------ CS-02

    /// <summary>CS-02 - Recuperar contrasena, primer paso. API-14.</summary>
    public async Task<Resultado> PedirRecuperacionAsync(
        string correo, string? ip = null, CancellationToken ct = default)
    {
        var usuario = await _usuarios.PorCorreoAsync(Texto.NormalizarCorreo(correo), ct);

        // Si no hay cuenta no se encola nada, pero la respuesta es la misma de siempre.
        if (usuario is not null && usuario.Estado is not EstadoCuenta.Eliminada)
        {
            var ahora = _reloj.Ahora;
            var token = _secretos.NuevoToken();

            usuario.TokenRecuperacion = _secretos.HashToken(token);
            usuario.TokenRecuperacionExpiraEn = ahora.Add(VidaDelTokenDeRecuperacion);
            usuario.ActualizadoEn = ahora;

            _avisos.Encolar(new Notificacion
            {
                DestinatarioId = usuario.Id,
                Tipo = TipoNotificacion.RecuperarContrasena,
                Canal = CanalNotificacion.Correo,
                ClaveIdempotencia = $"recuperacion:{usuario.Id}:{ahora:yyyyMMddHHmmss}",
                Asunto = "Crear una contrasena nueva en ZASS!",
                // El token va en una carga estructurada que el enviador purga tras el envio.
                Cuerpo = CargaDeAviso.ConToken(token),
                ProgramadaPara = ahora,
            });

            _auditor.Registrar(Auditar.Entrada(
                "pedir_recuperacion", "usuario", usuario.Id, ahora, actorId: usuario.Id, ip: ip));

            await _unidad.GuardarAsync(ct);
        }

        return Resultado.OkConMensaje(RespuestaDeRecuperacion);
    }

    /// <summary>
    /// CS-01 bis. Reenvia el enlace de verificacion del correo.
    ///
    /// <para>
    /// Existe porque sin esto hay un camino sin salida: el enlace del registro se pierde, se
    /// va a la carpeta de no deseado o caduca a las 24 horas, y la cuenta se queda sin poder
    /// verificarse. Desde que el contacto de la ficha exige correo verificado, eso deja a la
    /// persona sin poder contactar a nadie y sin forma de arreglarlo.
    /// </para>
    ///
    /// <para>
    /// **Responde lo mismo exista o no la cuenta**, igual que <see cref="PedirRecuperacionAsync"/>:
    /// una respuesta distinta convierte este endpoint en una forma de averiguar quien tiene
    /// cuenta en ZASS!. Tampoco dice si el correo ya estaba verificado, por lo mismo.
    /// </para>
    ///
    /// <para>
    /// Lleva una **espera minima entre envios** por cuenta. El limite de peticiones protege por
    /// IP, y eso no alcanza aqui: el daño de este endpoint no lo recibe quien lo llama, lo
    /// recibe el dueño del correo. Sin la espera, alguien desde varias IP puede llenarle la
    /// bandeja a una persona cuyo correo conoce.
    /// </para>
    /// </summary>
    public async Task<Resultado> ReenviarVerificacionAsync(
        string correo, string? ip = null, CancellationToken ct = default)
    {
        var usuario = await _usuarios.PorCorreoAsync(Texto.NormalizarCorreo(correo), ct);
        var ahora = _reloj.Ahora;

        // Cuando se emitio el que ya tiene: el token caduca a las 24 h de emitirse.
        var emitidoEn = usuario?.TokenVerificacionExpiraEn?.Subtract(VidaDelTokenDeVerificacion);
        var recienEnviado = emitidoEn is not null && ahora - emitidoEn < EsperaEntreReenvios;

        if (usuario is not null
            && !usuario.CorreoVerificado
            && usuario.Estado is not EstadoCuenta.Eliminada
            && !recienEnviado)
        {
            var token = _secretos.NuevoToken();

            // El token viejo deja de servir: si el correo llego dos veces, el enlace bueno es
            // el ultimo, y asi no quedan dos puertas abiertas a la vez.
            usuario.TokenVerificacion = _secretos.HashToken(token);
            usuario.TokenVerificacionExpiraEn = ahora.Add(VidaDelTokenDeVerificacion);
            usuario.ActualizadoEn = ahora;

            _avisos.Encolar(new Notificacion
            {
                DestinatarioId = usuario.Id,
                Tipo = TipoNotificacion.CorreoVerificacion,
                Canal = CanalNotificacion.Correo,
                ClaveIdempotencia = $"verificacion:{usuario.Id}:{ahora:yyyyMMddHHmmss}",
                Asunto = "Confirma tu correo en ZASS!",
                Cuerpo = CargaDeAviso.ConToken(token),
                ProgramadaPara = ahora,
            });

            _auditor.Registrar(Auditar.Entrada(
                "reenviar_verificacion", "usuario", usuario.Id, ahora, actorId: usuario.Id, ip: ip));

            await _unidad.GuardarAsync(ct);
        }

        return Resultado.OkConMensaje(RespuestaDeReenvio);
    }

    /// <summary>
    /// CS-02, segundo paso. API-15. Revoca TODAS las sesiones, sin perdonar ninguna:
    /// quien llego hasta aqui es porque perdio el control de la cuenta.
    /// </summary>
    public async Task<Resultado> RestablecerContrasenaAsync(
        string token, string contrasenaNueva, string? ip = null, CancellationToken ct = default)
    {
        var usuario = await _usuarios.PorTokenRecuperacionAsync(_secretos.HashToken(token), ct);

        if (usuario is null || usuario.TokenRecuperacionExpiraEn is null
            || usuario.TokenRecuperacionExpiraEn <= _reloj.Ahora)
            return Resultado.Falla(Fallas.NoEncontrado,
                "Ese enlace para cambiar la contraseña no sirve. Pide uno nuevo.");

        usuario.HashContrasena = _hash.Hash(contrasenaNueva);
        usuario.TokenRecuperacion = null;
        usuario.TokenRecuperacionExpiraEn = null;
        usuario.ActualizadoEn = _reloj.Ahora;

        _auditor.Registrar(Auditar.Entrada(
            "restablecer_contrasena", "usuario", usuario.Id, _reloj.Ahora,
            actorId: usuario.Id, ip: ip));

        await _sesiones.RevocarTodasAsync(usuario.Id, exceptoToken: null, ct);
        await _unidad.GuardarAsync(ct);
        return Resultado.Ok();
    }

    /// <summary>
    /// RN-S1-04. Cambiar la contrasena desde una sesion viva revoca las demas y conserva
    /// la actual: la persona no tiene por que volver a entrar en el aparato que esta usando.
    /// </summary>
    public async Task<Resultado> CambiarContrasenaAsync(
        Guid usuarioId, string contrasenaActual, string contrasenaNueva,
        string? sesionActual = null, CancellationToken ct = default)
    {
        var usuario = await _usuarios.PorIdAsync(usuarioId, ct);
        if (usuario is null)
            return Resultado.Falla(Fallas.NoEncontrado, "Esa cuenta no existe.");

        if (!_hash.Verifica(contrasenaActual, usuario.HashContrasena))
            return Resultado.Falla(Fallas.NoAutorizado, "La contraseña actual no es esa.");

        usuario.HashContrasena = _hash.Hash(contrasenaNueva);
        usuario.ActualizadoEn = _reloj.Ahora;

        _auditor.Registrar(Auditar.Entrada(
            "cambiar_contrasena", "usuario", usuario.Id, _reloj.Ahora, actorId: usuario.Id));

        await _sesiones.RevocarTodasAsync(usuario.Id, sesionActual, ct);
        await _unidad.GuardarAsync(ct);
        return Resultado.Ok();
    }

    // ------------------------------------------------------------------ CS-04

    /// <summary>
    /// CS-04, el primer paso: que quien pide borrar la cuenta sea su dueño, y no alguien frente a
    /// una sesión olvidada abierta. Decisión de Pablo, 17-nov-2026. No cambia nada: solo
    /// responde. Va aparte de <see cref="EliminarAsync"/> porque la baja toca otros módulos
    /// (las citas, la suscripción), y la contraseña se comprueba ANTES de tocar ninguno.
    ///
    /// Es <see cref="Fallas.ReglaDeNegocio"/> (422) y no <see cref="Fallas.NoAutorizado"/> (401):
    /// la sesión es válida, lo que falla es la confirmación. Un 401 haría que el cliente intente
    /// renovar la sesión, como si hubiera vencido.
    /// </summary>
    public async Task<Resultado> VerificarContrasenaAsync(
        Guid usuarioId, string contrasena, CancellationToken ct = default)
    {
        var usuario = await _usuarios.PorIdAsync(usuarioId, ct);
        if (usuario is null)
            return Resultado.Falla(Fallas.NoEncontrado, "Esa cuenta no existe.");

        return _hash.Verifica(contrasena, usuario.HashContrasena)
            ? Resultado.Ok()
            : Resultado.Falla(Fallas.ReglaDeNegocio, "La contraseña no es correcta.");
    }

    /// <summary>
    /// CS-04 - Eliminar cuenta. API-17. RN-S1-07: la fila sobrevive anonimizada porque
    /// las resenas que esta persona escribio son parte del historial de otro, que tiene
    /// derecho a conservarlo.
    /// </summary>
    public async Task<Resultado> EliminarAsync(
        Guid usuarioId, string? ip = null, CancellationToken ct = default)
    {
        var usuario = await _usuarios.PorIdAsync(usuarioId, ct);
        if (usuario is null)
            return Resultado.Falla(Fallas.NoEncontrado, "Esa cuenta no existe.");

        // Primero el perfil: una vez anonimizado el usuario ya no hay a quien avisar, y
        // un perfil publicado de una cuenta eliminada seguiria saliendo en la busqueda.
        var perfil = await _perfiles.PrestadorDeUsuarioAsync(usuarioId, ct);
        perfil?.Despublicar();

        usuario.Anonimizar();
        usuario.AnonimizadoEn = _reloj.Ahora;
        usuario.ActualizadoEn = _reloj.Ahora;

        _auditor.Registrar(Auditar.Entrada(
            "eliminar_cuenta", "usuario", usuario.Id, _reloj.Ahora, actorId: usuario.Id, ip: ip));

        await _sesiones.RevocarTodasAsync(usuarioId, exceptoToken: null, ct);
        await _unidad.GuardarAsync(ct);
        return Resultado.Ok();
    }

    // ------------------------------------------------------------------ API-16

    public async Task<Resultado<MiCuentaDto>> MiCuentaAsync(
        Guid usuarioId, CancellationToken ct = default)
    {
        var usuario = await _usuarios.PorIdAsync(usuarioId, ct);
        if (usuario is null)
            return Resultado.Falla<MiCuentaDto>(Fallas.NoEncontrado, "Esa cuenta no existe.");

        return Resultado.Ok(new MiCuentaDto(
            usuario.Id,
            usuario.Correo,
            usuario.Nombre,
            usuario.CorreoVerificado,
            usuario.Estado,
            usuario.Roles.Where(r => r.Rol is not null).Select(r => r.Rol!.Codigo).ToArray(),
            usuario.CreadoEn));
    }
}
