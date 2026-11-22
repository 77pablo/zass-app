using System.Security.Cryptography;
using Zass.Aplicacion.Comun;
using Zass.Aplicacion.Puertos;
using Zass.Dominio.Comun;
using Zass.Dominio.Identidad;

namespace Zass.Aplicacion.Identidad;

public sealed record SegundoPasoCmd(string Desafio, string Codigo);
public sealed record ConfirmarMfaCmd(string Contrasena, string Codigo);
public sealed record DesactivarMfaCmd(string Contrasena, string Codigo);
public sealed record RegenerarCodigosMfaCmd(string Contrasena, string Codigo);

public sealed record EstadoMfaDto(bool Activo, DateTime? ActivadoEn, int CodigosDeRespaldoRestantes, bool Obligatorio);
public sealed record AltaMfaDto(string Secreto, string UriOtpauth);
public sealed record CodigosDeRespaldoDto(string[] Codigos);

/// <summary>
/// Verificacion en dos pasos con TOTP (decision de Pablo, 22-nov-2026): obligatoria para los
/// roles del panel interno y opcional para el resto. API-79 a API-83.
///
/// <para>
/// Lo que importa y no se ve:
/// - un mismo codigo TOTP no sirve dos veces (el ultimo paso aceptado se guarda y se compara de
///   forma atomica), ni un codigo de respaldo tampoco (se borra al usarlo, en la misma sentencia);
/// - los fallos del segundo paso cuentan para el MISMO bloqueo de 10 intentos / 15 minutos de la
///   contrasena (ServicioSesion), y entrar bien solo reinicia el contador tras el segundo paso;
/// - la respuesta de un fallo es una sola, sea el desafio vencido, el codigo malo o la cuenta
///   bloqueada.
/// </para>
/// </summary>
public sealed class ServicioMfa
{
    public const string Emisor = "ZASS!";
    public const int CantidadDeCodigosDeRespaldo = 10;
    private const int LargoCodigoRespaldo = 10;   // 50 bits de azar
    private const string AlfabetoRespaldo = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789"; // sin 0/O ni 1/I

    private const string FalloSegundoPaso =
        "El código no es válido o el tiempo para ingresarlo terminó. Vuelve a intentarlo.";
    private const string FalloDesactivar = "La contraseña o el código no corresponden.";
    private const string FalloAlta =
        "La contraseña o el código no son correctos. Revisa que la hora de tu teléfono esté bien.";

    private readonly IRepositorioUsuarios _usuarios;
    private readonly IUnidadDeTrabajo _unidad;
    private readonly IHashContrasenas _hash;
    private readonly IGeneradorSecretos _secretos;
    private readonly ICifradoDeDatos _cifrado;
    private readonly IRelojSistema _reloj;
    private readonly IAuditor _auditor;
    private readonly ServicioSesion _sesion;
    private readonly INotificaciones _avisos;

    public ServicioMfa(
        IRepositorioUsuarios usuarios, IUnidadDeTrabajo unidad, IHashContrasenas hash,
        IGeneradorSecretos secretos, ICifradoDeDatos cifrado, IRelojSistema reloj, IAuditor auditor,
        ServicioSesion sesion, INotificaciones avisos)
    {
        _usuarios = usuarios;
        _unidad = unidad;
        _hash = hash;
        _secretos = secretos;
        _cifrado = cifrado;
        _reloj = reloj;
        _auditor = auditor;
        _sesion = sesion;
        _avisos = avisos;
    }

    private enum CodigoAceptado { Ninguno, Totp, Respaldo }

    // ------------------------------------------------------------ Segundo paso (API-79)

    public async Task<Resultado<SesionAbiertaDto>> SegundoPasoAsync(
        SegundoPasoCmd cmd, string? ip = null, string? agente = null, CancellationToken ct = default)
    {
        var ahora = _reloj.Ahora;
        var hashDesafio = _secretos.HashToken(cmd.Desafio ?? "");
        var usuario = await _usuarios.PorDesafioMfaAsync(hashDesafio, ct);

        var vigente = usuario is not null
            && usuario.MfaActivo
            && usuario.MfaDesafioExpiraEn is { } vence && vence > ahora
            && usuario.Estado is EstadoCuenta.Activa or EstadoCuenta.PendienteVerificacion;
        if (!vigente) return Resultado.Falla<SesionAbiertaDto>(Fallas.NoAutorizado, FalloSegundoPaso);

        // Bloqueada: no se mira el codigo (ni se gasta), y la respuesta es la de un codigo malo.
        var aceptado = usuario!.EstaBloqueada(ahora)
            ? CodigoAceptado.Ninguno
            : await VerificarCodigoAsync(usuario, cmd.Codigo, ahora, ct);

        if (aceptado == CodigoAceptado.Ninguno
            || !await _usuarios.GastarDesafioMfaAsync(usuario.Id, hashDesafio, ahora, ct)
            || !await _usuarios.ReiniciarIntentosSiNoBloqueadaAsync(usuario.Id, ahora, ct))
        {
            await RegistrarFalloAsync(usuario, "inicio_sesion", ip, ct);
            return Resultado.Falla<SesionAbiertaDto>(Fallas.NoAutorizado, FalloSegundoPaso);
        }

        var dto = _sesion.AbrirSesion(usuario, ip, agente);

        _auditor.Registrar(Auditar.Entrada(
            "inicio_sesion", "usuario", usuario.Id, ahora, actorId: usuario.Id,
            contexto: new { mfa = aceptado == CodigoAceptado.Totp ? "totp" : "respaldo" }, ip: ip));
        if (aceptado == CodigoAceptado.Respaldo)
            _auditor.Registrar(Auditar.Entrada(
                "mfa_codigo_respaldo_usado", "usuario", usuario.Id, ahora, actorId: usuario.Id,
                contexto: new { restantes = Math.Max(0, (usuario.MfaCodigosRespaldo?.Length ?? 1) - 1) }, ip: ip));

        await _unidad.GuardarAsync(ct);
        return Resultado.Ok(dto);
    }

    // ------------------------------------------------------------ Mi cuenta (API-80 a API-83)

    public async Task<Resultado<EstadoMfaDto>> EstadoAsync(Guid usuarioId, CancellationToken ct = default)
    {
        var usuario = await _usuarios.PorIdAsync(usuarioId, ct);
        if (usuario is null) return Resultado.Falla<EstadoMfaDto>(Fallas.NoEncontrado, "Esa cuenta no existe.");

        return Resultado.Ok(new EstadoMfaDto(
            usuario.MfaActivo,
            usuario.MfaActivadoEn,
            usuario.MfaActivo ? usuario.MfaCodigosRespaldo?.Length ?? 0 : 0,
            usuario.TieneRolInterno));
    }

    /// <summary>API-81. Genera el secreto y lo deja pendiente hasta que se confirme con un codigo.</summary>
    public async Task<Resultado<AltaMfaDto>> IniciarAltaAsync(Guid usuarioId, CancellationToken ct = default)
    {
        var usuario = await _usuarios.PorIdAsync(usuarioId, ct);
        if (usuario is null) return Resultado.Falla<AltaMfaDto>(Fallas.NoEncontrado, "Esa cuenta no existe.");
        if (usuario.MfaActivo)
            return Resultado.Falla<AltaMfaDto>(Fallas.Conflicto,
                "Ya tienes la verificación en dos pasos activa.");

        var secreto = Totp.NuevoSecreto();
        usuario.MfaSecretoPendienteCifrado = _cifrado.Cifrar(secreto);
        usuario.ActualizadoEn = _reloj.Ahora;
        await _unidad.GuardarAsync(ct);

        return Resultado.Ok(new AltaMfaDto(secreto, Totp.Uri(Emisor, usuario.Correo, secreto)));
    }

    /// <summary>
    /// API-82. Con la contrasena Y un codigo correcto del secreto pendiente, la verificacion queda
    /// activa y se entregan los 10 codigos de respaldo. Es la UNICA vez que se ven: se guardan con
    /// hash. Pide la contrasena para que quien solo robo la sesion no pueda activarla con SU telefono
    /// y dejar fuera a la dueña; el fallo (contrasena, codigo o cuenta bloqueada) es uno solo y suma
    /// al bloqueo por intentos. Al activar se cierran las demas sesiones.
    /// </summary>
    public async Task<Resultado<CodigosDeRespaldoDto>> ConfirmarAltaAsync(
        Guid usuarioId, ConfirmarMfaCmd cmd, string? ip = null, string? sesionActual = null,
        CancellationToken ct = default)
    {
        var usuario = await _usuarios.PorIdAsync(usuarioId, ct);
        if (usuario is null) return Resultado.Falla<CodigosDeRespaldoDto>(Fallas.NoEncontrado, "Esa cuenta no existe.");
        if (usuario.MfaActivo)
            return Resultado.Falla<CodigosDeRespaldoDto>(Fallas.Conflicto,
                "Ya tienes la verificación en dos pasos activa.");
        if (usuario.MfaSecretoPendienteCifrado is null)
            return Resultado.Falla<CodigosDeRespaldoDto>(Fallas.ReglaDeNegocio,
                "Primero genera el código QR para activar la verificación en dos pasos.");

        var ahora = _reloj.Ahora;
        long? paso = null;
        if (_hash.Verifica(cmd.Contrasena, usuario.HashContrasena) && !usuario.EstaBloqueada(ahora))
            paso = Totp.PasoDelCodigo(_cifrado.Descifrar(usuario.MfaSecretoPendienteCifrado), cmd.Codigo, ahora);
        if (paso is null)
        {
            await RegistrarFalloAsync(usuario, "alta", ip, ct);
            return Resultado.Falla<CodigosDeRespaldoDto>(Fallas.ReglaDeNegocio, FalloAlta);
        }

        var codigos = NuevosCodigosDeRespaldo();
        usuario.MfaSecretoCifrado = usuario.MfaSecretoPendienteCifrado;
        usuario.MfaSecretoPendienteCifrado = null;
        usuario.MfaActivadoEn = ahora;
        usuario.MfaUltimoPaso = paso;   // el codigo que activo tampoco sirve para entrar
        usuario.MfaCodigosRespaldo = codigos.Select(c => HashRespaldo(usuario.Id, c.Replace("-", ""))).ToArray();
        usuario.MfaDesafioHash = null;
        usuario.MfaDesafioExpiraEn = null;
        usuario.ActualizadoEn = ahora;

        _auditor.Registrar(Auditar.Entrada(
            "mfa_activado", "usuario", usuario.Id, ahora, actorId: usuario.Id, ip: ip));

        // Decision de Pablo (22-nov-2026): un correo cada vez que se activa. A una cuenta que aun no
        // la tenia, quien le robe la contraseña podria activarla con SU telefono; asi la dueña se
        // entera. Va en la misma transaccion: si la activacion no se guarda, el correo tampoco.
        _avisos.Encolar(new Notificacion
        {
            DestinatarioId = usuario.Id,
            Tipo = TipoNotificacion.MfaActivado,
            Canal = CanalNotificacion.Correo,
            ClaveIdempotencia = $"mfa-activado:{usuario.Id:N}:{ahora.Ticks}",
            Asunto = "Se activó la verificación en dos pasos en tu cuenta de ZASS!",
            Cuerpo = "{}",
            ProgramadaPara = ahora,
        });

        await _unidad.GuardarAsync(ct);
        await _sesion.RevocarTodasAsync(usuario.Id, sesionActual, ct);
        return Resultado.Ok(new CodigosDeRespaldoDto(codigos));
    }

    /// <summary>
    /// API-84 (decision de Pablo, 22-nov-2026). Diez codigos de respaldo nuevos; los anteriores
    /// dejan de servir. Pide lo mismo que desactivar (contrasena Y codigo), y los fallos suman al
    /// mismo bloqueo: con la sesion robada sola no se pueden sacar codigos para entrar despues.
    /// </summary>
    public async Task<Resultado<CodigosDeRespaldoDto>> RegenerarCodigosAsync(
        Guid usuarioId, RegenerarCodigosMfaCmd cmd, string? ip = null, string? sesionActual = null,
        CancellationToken ct = default)
    {
        var usuario = await _usuarios.PorIdAsync(usuarioId, ct);
        if (usuario is null) return Resultado.Falla<CodigosDeRespaldoDto>(Fallas.NoEncontrado, "Esa cuenta no existe.");
        if (!usuario.MfaActivo)
            return Resultado.Falla<CodigosDeRespaldoDto>(Fallas.ReglaDeNegocio,
                "La verificación en dos pasos no está activa.");

        var ahora = _reloj.Ahora;
        var contrasenaCorrecta = _hash.Verifica(cmd.Contrasena, usuario.HashContrasena);
        var aceptado = contrasenaCorrecta && !usuario.EstaBloqueada(ahora)
            ? await VerificarCodigoAsync(usuario, cmd.Codigo, ahora, ct)
            : CodigoAceptado.Ninguno;

        if (aceptado == CodigoAceptado.Ninguno)
        {
            await RegistrarFalloAsync(usuario, "regenerar_codigos", ip, ct);
            return Resultado.Falla<CodigosDeRespaldoDto>(Fallas.ReglaDeNegocio, FalloDesactivar);
        }

        // Si se uso un codigo de respaldo para pedirlo, ya se gasto en la base (atomico); la lista
        // entera se reemplaza igual, asi que no hace falta releerla.
        var codigos = NuevosCodigosDeRespaldo();
        usuario.MfaCodigosRespaldo = codigos.Select(c => HashRespaldo(usuario.Id, c.Replace("-", ""))).ToArray();
        usuario.ActualizadoEn = ahora;
        _auditor.Registrar(Auditar.Entrada(
            "mfa_codigos_regenerados", "usuario", usuario.Id, ahora, actorId: usuario.Id,
            contexto: new { con = aceptado == CodigoAceptado.Totp ? "totp" : "respaldo" }, ip: ip));

        await _unidad.GuardarAsync(ct);
        await _sesion.RevocarTodasAsync(usuario.Id, sesionActual, ct);
        return Resultado.Ok(new CodigosDeRespaldoDto(codigos));
    }

    /// <summary>
    /// API-83. Pide la contrasena Y un codigo (TOTP o de respaldo): quien solo robo la sesion no
    /// puede apagarla. Los fallos cuentan para el bloqueo por intentos, como los del inicio.
    /// </summary>
    public async Task<Resultado> DesactivarAsync(
        Guid usuarioId, DesactivarMfaCmd cmd, string? ip = null, string? sesionActual = null,
        CancellationToken ct = default)
    {
        var usuario = await _usuarios.PorIdAsync(usuarioId, ct);
        if (usuario is null) return Resultado.Falla(Fallas.NoEncontrado, "Esa cuenta no existe.");
        if (!usuario.MfaActivo)
            return Resultado.Falla(Fallas.ReglaDeNegocio, "La verificación en dos pasos no está activa.");

        var ahora = _reloj.Ahora;
        var contrasenaCorrecta = _hash.Verifica(cmd.Contrasena, usuario.HashContrasena);
        var aceptado = contrasenaCorrecta && !usuario.EstaBloqueada(ahora)
            ? await VerificarCodigoAsync(usuario, cmd.Codigo, ahora, ct)
            : CodigoAceptado.Ninguno;

        if (aceptado == CodigoAceptado.Ninguno)
        {
            await RegistrarFalloAsync(usuario, "desactivar", ip, ct);
            return Resultado.Falla(Fallas.ReglaDeNegocio, FalloDesactivar);
        }

        usuario.QuitarMfa();
        usuario.ActualizadoEn = ahora;
        _auditor.Registrar(Auditar.Entrada(
            "mfa_desactivado", "usuario", usuario.Id, ahora, actorId: usuario.Id,
            contexto: new { con = aceptado == CodigoAceptado.Totp ? "totp" : "respaldo" }, ip: ip));

        await _unidad.GuardarAsync(ct);
        await _sesion.RevocarTodasAsync(usuario.Id, sesionActual, ct);
        return Resultado.Ok();
    }

    // ------------------------------------------------------------ Piezas

    /// <summary>
    /// Seis digitos: TOTP. Diez caracteres del alfabeto de respaldo (con o sin guion): codigo
    /// de respaldo. Los dos se GASTAN aqui mismo, de forma atomica, si son correctos.
    /// </summary>
    private async Task<CodigoAceptado> VerificarCodigoAsync(
        Usuario usuario, string? codigo, DateTime ahora, CancellationToken ct)
    {
        var limpio = (codigo ?? "").Replace("-", "").Replace(" ", "").Trim().ToUpperInvariant();

        if (Totp.EsCodigoTotp(limpio))
        {
            var secreto = _cifrado.Descifrar(usuario.MfaSecretoCifrado!);
            return Totp.PasoDelCodigo(secreto, limpio, ahora) is { } paso
                && await _usuarios.GastarPasoTotpAsync(usuario.Id, paso, ct)
                ? CodigoAceptado.Totp
                : CodigoAceptado.Ninguno;
        }

        if (limpio.Length == LargoCodigoRespaldo && limpio.All(c => AlfabetoRespaldo.Contains(c)))
            return await _usuarios.GastarCodigoRespaldoAsync(usuario.Id, HashRespaldo(usuario.Id, limpio), ct)
                ? CodigoAceptado.Respaldo
                : CodigoAceptado.Ninguno;

        return CodigoAceptado.Ninguno;
    }

    /// <summary>Suma el fallo al MISMO contador del bloqueo por intentos de la contrasena.</summary>
    private async Task RegistrarFalloAsync(Usuario usuario, string paso, string? ip, CancellationToken ct)
    {
        var ahora = _reloj.Ahora;
        var bloqueoAhora = await _usuarios.SumarIntentoFallidoAsync(
            usuario.Id, ahora, ServicioSesion.IntentosParaBloquear,
            ahora.Add(ServicioSesion.DuracionDelBloqueo), ct);

        _auditor.Registrar(Auditar.Entrada(
            "mfa_fallido", "usuario", usuario.Id, ahora, actorId: usuario.Id,
            contexto: new { paso, bloqueada = usuario.EstaBloqueada(ahora) }, ip: ip));

        if (bloqueoAhora)
            _auditor.Registrar(Auditar.Entrada(
                "cuenta_bloqueada_por_intentos", "usuario", usuario.Id, ahora,
                contexto: new
                {
                    intentos = ServicioSesion.IntentosParaBloquear,
                    bloqueadaHasta = ahora.Add(ServicioSesion.DuracionDelBloqueo),
                },
                ip: ip));

        await _unidad.GuardarAsync(ct);
    }

    /// <summary>
    /// SHA-256 con el id de la cuenta delante: el mismo codigo en dos cuentas da hash distinto, y
    /// una tabla precalculada no sirve para todas. Los codigos tienen 50 bits de azar.
    /// </summary>
    private string HashRespaldo(Guid usuarioId, string codigoLimpio) =>
        HashDeCodigoDeRespaldo(_secretos, usuarioId, codigoLimpio);

    /// <summary>El hash con que se guarda un codigo de respaldo ya limpio (sin guion, en mayusculas).</summary>
    public static string HashDeCodigoDeRespaldo(IGeneradorSecretos secretos, Guid usuarioId, string codigoLimpio) =>
        secretos.HashToken($"mfa-respaldo:{usuarioId:N}:{codigoLimpio}");

    private static string[] NuevosCodigosDeRespaldo() =>
        Enumerable.Range(0, CantidadDeCodigosDeRespaldo).Select(_ =>
        {
            var c = new string(Enumerable.Range(0, LargoCodigoRespaldo)
                .Select(_ => AlfabetoRespaldo[RandomNumberGenerator.GetInt32(AlfabetoRespaldo.Length)])
                .ToArray());
            return $"{c[..5]}-{c[5..]}";
        }).ToArray();
}
