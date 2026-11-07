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
/// CU-02 y CS-03. Abre, renueva, cierra y revoca sesiones (API-09 a API-12).
///
/// De la sesion solo se guarda el hash del token de refresco (TBL-04): quien lea la tabla
/// no puede suplantar a nadie. Renovar rota el token y revoca el anterior, asi que un
/// token robado deja de servir en cuanto el dueno legitimo renueva.
/// </summary>
public sealed class ServicioSesion
{
    /// <summary>DT-08. El token de acceso dura 15 min; el de refresco, 30 dias.</summary>
    public static readonly TimeSpan VidaDelRefresco = TimeSpan.FromDays(30);

    /// <summary>
    /// Un mensaje unico para "no existe la cuenta" y para "la contrasena no es esa".
    /// Distinguirlos regalaria un verificador de correos registrados (seguridad.md 5).
    /// </summary>
    private const string CredencialesInvalidas = "El correo o la contraseña no corresponden.";

    private readonly IRepositorioUsuarios _usuarios;
    private readonly IRepositorioSesiones _sesiones;
    private readonly IUnidadDeTrabajo _unidad;
    private readonly IHashContrasenas _hash;
    private readonly IGeneradorSecretos _secretos;
    private readonly IRelojSistema _reloj;
    private readonly IAuditor _auditor;

    public ServicioSesion(
        IRepositorioUsuarios usuarios, IRepositorioSesiones sesiones, IUnidadDeTrabajo unidad,
        IHashContrasenas hash, IGeneradorSecretos secretos, IRelojSistema reloj, IAuditor auditor)
    {
        _usuarios = usuarios;
        _sesiones = sesiones;
        _unidad = unidad;
        _hash = hash;
        _secretos = secretos;
        _reloj = reloj;
        _auditor = auditor;
    }

    public async Task<Resultado<SesionAbiertaDto>> IniciarAsync(
        IniciarSesionCmd cmd, string? ip = null, string? agente = null, CancellationToken ct = default)
    {
        var correo = Texto.NormalizarCorreo(cmd.Correo);
        var usuario = await _usuarios.PorCorreoAsync(correo, ct);

        // Se verifica el hash incluso cuando la cuenta no existe. Si no, el tiempo de
        // respuesta diria cuales correos estan registrados y cuales no.
        var contrasenaCorrecta = usuario is not null
            && _hash.Verifica(cmd.Contrasena, usuario.HashContrasena);
        if (usuario is null)
            _hash.Verifica(cmd.Contrasena, _hash.Hash("sin-cuenta"));

        var puedeEntrar = usuario is not null
            && contrasenaCorrecta
            && usuario.Estado is EstadoCuenta.Activa or EstadoCuenta.PendienteVerificacion;

        if (!puedeEntrar)
        {
            // El correo se guarda con hash: alcanza para correlacionar intentos sin
            // convertir la auditoria en una lista de correos registrados.
            await RegistrarAsync(Auditar.Entrada(
                "inicio_sesion_fallido", "usuario", usuario?.Id, _reloj.Ahora,
                actorId: usuario?.Id,
                contexto: new
                {
                    correoHash = _secretos.HashToken(correo),
                    motivo = Motivo(usuario, contrasenaCorrecta),
                },
                ip: ip), ct);

            return Resultado.Falla<SesionAbiertaDto>(Fallas.NoAutorizado, CredencialesInvalidas);
        }

        var dto = AbrirSesion(usuario!, ip, agente);

        _auditor.Registrar(Auditar.Entrada(
            "inicio_sesion", "usuario", usuario!.Id, _reloj.Ahora, actorId: usuario.Id, ip: ip));

        await _unidad.GuardarAsync(ct);
        return Resultado.Ok(dto);
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

    private SesionAbiertaDto AbrirSesion(Usuario usuario, string? ip, string? agente)
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

    private async Task RegistrarAsync(RegistroAuditoria entrada, CancellationToken ct)
    {
        _auditor.Registrar(entrada);
        await _unidad.GuardarAsync(ct);
    }

    private static string Motivo(Usuario? usuario, bool contrasenaCorrecta) => usuario switch
    {
        null => "sin_cuenta",
        _ when !contrasenaCorrecta => "contrasena",
        { Estado: EstadoCuenta.Suspendida } => "suspendida",
        { Estado: EstadoCuenta.Eliminada } => "eliminada",
        _ => "desconocido",
    };
}
