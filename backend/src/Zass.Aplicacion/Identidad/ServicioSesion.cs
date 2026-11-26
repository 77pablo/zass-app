using Zass.Aplicacion.Comun;
using Zass.Aplicacion.Puertos;
using Zass.Dominio.Comun;
using Zass.Dominio.Identidad;

namespace Zass.Aplicacion.Identidad;

/// <summary>CU-02 - Iniciar sesion. API-09.</summary>
public sealed record IniciarSesionCmd(string Correo, string Contrasena);

public sealed record SesionAbiertaDto(
    Guid UsuarioId,
    string TokenRefresco,
    DateTime ExpiraEn,
    string[] Roles,
    bool CorreoVerificado);

/// <summary>
/// Lo que deja la contrasena correcta en una cuenta con MFA: no una sesion, sino un desafio de
/// corta vida que el segundo paso (API-79) canjea junto al codigo TOTP o de respaldo.
/// </summary>
public sealed record DesafioMfaDto(string Desafio, DateTime ExpiraEn);

/// <summary>
/// API-09. O se abrio la sesion (cuenta sin MFA) o falta el segundo paso. Nunca las dos: el
/// tipo obliga a quien llama a mirar cual de las dos le toco antes de poner cookies.
/// </summary>
public sealed record InicioDeSesion(SesionAbiertaDto? Sesion, DesafioMfaDto? Desafio);

/// <summary>
/// CU-02 y CS-03. Abre, renueva, cierra y revoca sesiones (API-09 a API-12).
///
/// De la sesion solo se guarda el hash del token de refresco (TBL-04): quien lea la tabla
/// no puede suplantar a nadie. Renovar rota el token y revoca el anterior, asi que un
/// token robado deja de servir en cuanto el dueno legitimo renueva.
/// </summary>
public sealed class ServicioSesion
{
    /// <summary>
    /// Fallos seguidos que bloquean la cuenta. Eran 10 (decision de Pablo, 22-nov-2026); la revision
    /// del 25-nov mostro que con el limite por IP (5 cada 15 min) bastaban 2 IP para bloquear una
    /// cuenta ajena. Con 30 hacen falta 6 a la vez, y la dueña recibe un correo (Pablo, 25-nov-2026).
    /// </summary>
    public const int IntentosParaBloquear = 30;

    /// <summary>Cuanto dura el bloqueo, contado desde el fallo que lo disparo. Fijo.</summary>
    public static readonly TimeSpan DuracionDelBloqueo = TimeSpan.FromMinutes(15);

    /// <summary>DT-08. El token de acceso dura 15 min; el de refresco, 30 dias.</summary>
    public static readonly TimeSpan VidaDelRefresco = TimeSpan.FromDays(30);

    /// <summary>Cuanto vive el desafio del segundo paso del inicio de sesion (MFA). Un solo uso.</summary>
    public static readonly TimeSpan VidaDelDesafioMfa = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Revision de seguridad 25-nov-2026: el correo a la dueña cuando su cuenta se bloquea por
    /// intentos. Uno por dia como mucho (la clave lleva la fecha): quien prueba contraseñas puede
    /// bloquearla cada 15 minutos, y un correo por bloqueo seria una forma de llenarle la bandeja.
    /// No lleva token: el enlace manda a pedir una contraseña nueva, que tambien desbloquea.
    /// </summary>
    public static Notificacion AvisoDeBloqueo(Guid usuarioId, DateTime ahora) => new()
    {
        DestinatarioId = usuarioId,
        Tipo = TipoNotificacion.CuentaBloqueadaPorIntentos,
        Canal = CanalNotificacion.Correo,
        ClaveIdempotencia = $"cuenta-bloqueada:{usuarioId:N}:{ahora:yyyy-MM-dd}",
        Asunto = "Bloqueamos tu cuenta de ZASS! por intentos fallidos",
        Cuerpo = "{}",
        ProgramadaPara = ahora,
    };

    /// <summary>
    /// Un mensaje unico para "no existe la cuenta" y para "la contrasena no es esa".
    /// Distinguirlos regalaria un verificador de correos registrados (seguridad.md 5).
    /// </summary>
    private const string CredencialesInvalidas = "El correo o la contraseña no corresponden.";

    private readonly IRepositorioUsuarios _usuarios;
    private readonly INotificaciones? _avisos;
    private readonly IRepositorioSesiones _sesiones;
    private readonly IUnidadDeTrabajo _unidad;
    private readonly IHashContrasenas _hash;
    private readonly IGeneradorSecretos _secretos;
    private readonly IRelojSistema _reloj;
    private readonly IAuditor _auditor;

    public ServicioSesion(
        IRepositorioUsuarios usuarios, IRepositorioSesiones sesiones, IUnidadDeTrabajo unidad,
        IHashContrasenas hash, IGeneradorSecretos secretos, IRelojSistema reloj, IAuditor auditor,
        INotificaciones? avisos = null)
    {
        _avisos = avisos;
        _usuarios = usuarios;
        _sesiones = sesiones;
        _unidad = unidad;
        _hash = hash;
        _secretos = secretos;
        _reloj = reloj;
        _auditor = auditor;
    }

    /// <summary>
    /// Para quien solo sabe abrir sesiones de un paso: con MFA activo responde NoAutorizado, porque
    /// sin el segundo paso no hay sesion. La API usa <see cref="EntrarAsync"/>.
    /// </summary>
    public async Task<Resultado<SesionAbiertaDto>> IniciarAsync(
        IniciarSesionCmd cmd, string? ip = null, string? agente = null, CancellationToken ct = default)
    {
        var r = await EntrarAsync(cmd, ip, agente, ct);
        if (!r.Exito) return Resultado.Falla<SesionAbiertaDto>(r.Codigo!, r.Mensaje!);
        return r.Valor!.Sesion is { } sesion
            ? Resultado.Ok(sesion)
            : Resultado.Falla<SesionAbiertaDto>(Fallas.NoAutorizado, "Falta el segundo paso de verificación.");
    }

    /// <summary>
    /// API-09, primer paso. Sin MFA abre la sesion. Con MFA, la contrasena correcta NO abre nada:
    /// deja un desafio de 5 minutos y el contador de intentos fallidos NO vuelve a cero (si
    /// volviera, quien tiene la contrasena podria alternar contrasena y 9 codigos sin fin).
    /// </summary>
    /// <summary>
    /// El hash contra el que se verifica cuando la cuenta no existe. Uno por instancia de hasher (en
    /// la aplicacion es un singleton), calculado la primera vez: asi ese camino hace un solo
    /// Argon2id, igual que el de una cuenta que existe.
    /// </summary>
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<IHashContrasenas, string>
        HashesDeRelleno = new();

    private static string HashDeRelleno(IHashContrasenas hash) =>
        HashesDeRelleno.GetValue(hash, h => h.Hash("sin-cuenta"));

    public async Task<Resultado<InicioDeSesion>> EntrarAsync(
        IniciarSesionCmd cmd, string? ip = null, string? agente = null, CancellationToken ct = default)
    {
        var correo = Texto.NormalizarCorreo(cmd.Correo);
        var usuario = await _usuarios.PorCorreoAsync(correo, ct);
        var ahora = _reloj.Ahora;

        // Se verifica el hash incluso cuando la cuenta no existe, y tambien cuando esta
        // bloqueada. Si no, el tiempo de respuesta diria cuales correos estan registrados, o
        // en que momento la cuenta quedo bloqueada.
        var contrasenaCorrecta = usuario is not null
            && _hash.Verifica(cmd.Contrasena, usuario.HashContrasena);
        //
        // Revision del 25-nov-2026: antes se hacia Hash + Verifica (DOS Argon2id) con la cuenta que
        // no existe y uno solo con la que existe; el doble de tiempo delataba los correos libres. El
        // hash de relleno se calcula una vez por hasher y se reusa: aqui va UN Verifica, como arriba.
        if (usuario is null)
            _hash.Verifica(cmd.Contrasena, HashDeRelleno(_hash));

        // Bloqueo por intentos (decision de Pablo, 22-nov-2026). Bloqueada, NO entra aunque la
        // contrasena sea la buena, y responde exactamente lo mismo que una contrasena mala.
        var bloqueada = usuario is not null && usuario.EstaBloqueada(ahora);

        var puedeEntrar = usuario is not null
            && contrasenaCorrecta
            && !bloqueada
            && usuario.Estado is EstadoCuenta.Activa or EstadoCuenta.PendienteVerificacion;

        // La lectura de arriba pudo quedar vieja: otro intento pudo bloquear la cuenta en el
        // medio. El reinicio del contador es atomico y solo pasa si NO esta bloqueada ahora.
        if (puedeEntrar && !usuario!.MfaActivo
            && !await _usuarios.ReiniciarIntentosSiNoBloqueadaAsync(usuario.Id, ahora, ct))
        {
            puedeEntrar = false;
            bloqueada = true;
        }

        if (!puedeEntrar)
        {
            // Todo intento fallido de una cuenta que existe suma uno. Con la cuenta que no
            // existe se hace el mismo viaje a la base con un id vacio, que no toca ninguna fila:
            // asi los dos caminos tardan lo mismo.
            var bloqueoAhora = await _usuarios.SumarIntentoFallidoAsync(
                usuario?.Id ?? Guid.Empty, ahora, IntentosParaBloquear, ahora.Add(DuracionDelBloqueo), ct);

            // El correo se guarda con hash: alcanza para correlacionar intentos sin
            // convertir la auditoria en una lista de correos registrados.
            _auditor.Registrar(Auditar.Entrada(
                "inicio_sesion_fallido", "usuario", usuario?.Id, ahora,
                actorId: usuario?.Id,
                contexto: new
                {
                    correoHash = _secretos.HashToken(correo),
                    motivo = Motivo(usuario, contrasenaCorrecta, bloqueada),
                },
                ip: ip));

            // Una entrada por bloqueo, para que el administrador vea los ataques (ver el
            // filtro por accion en el panel de auditoria). No se manda correo al administrador
            // aqui: tardaria lo bastante para que el decimo intento se distinga de los demas.
            if (bloqueoAhora)
            {
                _auditor.Registrar(Auditar.Entrada(
                    "cuenta_bloqueada_por_intentos", "usuario", usuario!.Id, ahora,
                    contexto: new
                    {
                        intentos = IntentosParaBloquear,
                        bloqueadaHasta = ahora.Add(DuracionDelBloqueo),
                    },
                    ip: ip));
                if (_avisos is not null)
                    await _avisos.EncolarSiNoExisteAsync(AvisoDeBloqueo(usuario.Id, ahora), ct);
            }

            await _unidad.GuardarAsync(ct);
            return Resultado.Falla<InicioDeSesion>(Fallas.NoAutorizado, CredencialesInvalidas);
        }

        if (usuario!.MfaActivo)
        {
            // Un desafio nuevo reemplaza al anterior: solo vale el del ultimo inicio.
            var desafio = _secretos.NuevoToken();
            var vence = ahora.Add(VidaDelDesafioMfa);
            usuario.MfaDesafioHash = _secretos.HashToken(desafio);
            usuario.MfaDesafioExpiraEn = vence;
            await _unidad.GuardarAsync(ct);
            return Resultado.Ok(new InicioDeSesion(null, new DesafioMfaDto(desafio, vence)));
        }

        var dto = AbrirSesion(usuario, ip, agente);

        _auditor.Registrar(Auditar.Entrada(
            "inicio_sesion", "usuario", usuario!.Id, ahora, actorId: usuario.Id, ip: ip));

        await _unidad.GuardarAsync(ct);
        return Resultado.Ok(new InicioDeSesion(dto, null));
    }

    /// <summary>API-10. Rota el token: el nuevo nace y el viejo muere en el mismo acto.</summary>
    public async Task<Resultado<SesionAbiertaDto>> RenovarAsync(
        string tokenRefresco, string? ip = null, string? agente = null, CancellationToken ct = default)
    {
        var sesion = await _sesiones.PorHashTokenAsync(_secretos.HashToken(tokenRefresco), ct);
        if (sesion is null || !sesion.EstaVigente(_reloj.Ahora))
            return Resultado.Falla<SesionAbiertaDto>(Fallas.NoAutorizado,
                "La sesión no es válida. Vuelve a iniciar sesión.");

        var usuario = await _usuarios.PorIdAsync(sesion.UsuarioId, ct);
        if (usuario is null || usuario.Estado is EstadoCuenta.Suspendida or EstadoCuenta.Eliminada)
            return Resultado.Falla<SesionAbiertaDto>(Fallas.NoAutorizado,
                "La sesión no es válida. Vuelve a iniciar sesión.");

        sesion.RevocadaEn = _reloj.Ahora;
        var dto = AbrirSesion(usuario, ip ?? sesion.IpOrigen, agente ?? sesion.AgenteUsuario);

        await _unidad.GuardarAsync(ct);
        return Resultado.Ok(dto);
    }

    /// <summary>API-11. Cierra la sesion de la que viene la peticion y solo esa.</summary>
    public async Task<Resultado> CerrarAsync(string tokenRefresco, CancellationToken ct = default)
    {
        var sesion = await _sesiones.PorHashTokenAsync(_secretos.HashToken(tokenRefresco), ct);
        if (sesion is null)
            return Resultado.Falla(Fallas.NoEncontrado, "Esa sesión no existe.");

        sesion.RevocadaEn ??= _reloj.Ahora;
        await _unidad.GuardarAsync(ct);
        return Resultado.Ok();
    }

    /// <summary>
    /// API-12 y CS-03. Revoca de verdad, no espera a que expiren. Tambien lo usa
    /// RN-S1-04 tras cambiar la contrasena, perdonando la sesion que la cambio.
    /// </summary>
    public async Task<Resultado> RevocarTodasAsync(
        Guid usuarioId, string? exceptoToken = null, CancellationToken ct = default)
    {
        var perdonado = exceptoToken is null ? null : _secretos.HashToken(exceptoToken);
        var vigentes = await _sesiones.VigentesDeUsuarioAsync(usuarioId, _reloj.Ahora, ct);

        foreach (var sesion in vigentes.Where(s => s.HashToken != perdonado))
            sesion.RevocadaEn = _reloj.Ahora;

        _auditor.Registrar(Auditar.Entrada(
            "revocar_sesiones", "usuario", usuarioId, _reloj.Ahora, actorId: usuarioId,
            contexto: new { revocadas = vigentes.Count(s => s.HashToken != perdonado) }));

        await _unidad.GuardarAsync(ct);
        return Resultado.Ok();
    }

    /// <summary>Tambien la usa el segundo paso (<see cref="ServicioMfa"/>), ya comprobado el codigo.</summary>
    internal SesionAbiertaDto AbrirSesion(Usuario usuario, string? ip, string? agente)
    {
        var ahora = _reloj.Ahora;
        var token = _secretos.NuevoToken();
        var expira = ahora.Add(VidaDelRefresco);

        _sesiones.Agregar(new Sesion
        {
            UsuarioId = usuario.Id,
            HashToken = _secretos.HashToken(token),
            IpOrigen = ip,
            AgenteUsuario = agente,
            CreadaEn = ahora,
            ExpiraEn = expira,
        });

        var roles = usuario.Roles
            .Where(r => r.Rol is not null)
            .Select(r => r.Rol!.Codigo)
            .ToArray();

        return new SesionAbiertaDto(usuario.Id, token, expira, roles, usuario.CorreoVerificado);
    }

    private static string Motivo(Usuario? usuario, bool contrasenaCorrecta, bool bloqueada) => usuario switch
    {
        null => "sin_cuenta",
        _ when bloqueada => "bloqueada",
        _ when !contrasenaCorrecta => "contrasena",
        { Estado: EstadoCuenta.Suspendida } => "suspendida",
        { Estado: EstadoCuenta.Eliminada } => "eliminada",
        _ => "desconocido",
    };
}
