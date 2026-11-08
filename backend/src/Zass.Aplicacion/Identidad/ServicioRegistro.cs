using Zass.Aplicacion.Comun;
using Zass.Aplicacion.Puertos;
using Zass.Dominio.Comun;
using Zass.Dominio.Identidad;

namespace Zass.Aplicacion.Identidad;

/// <summary>CU-01 · Registrar cuenta. API-08.</summary>
public sealed record RegistrarCuentaCmd(string Correo, string Contrasena, string Nombre, string Rol);

public sealed record CuentaCreadaDto(Guid UsuarioId, string Rol);

/// <summary>
/// CU-01. Crea la cuenta en estado pendiente_verificacion, le da el rol publico que pidio
/// y encola el correo de verificacion (CS-01).
///
/// Reglas que hace cumplir: RN-S1-01 correo unico y normalizado, RN-S1-03 Argon2id,
/// RN-S1-08 ningun rol interno es autoasignable.
/// </summary>
public sealed class ServicioRegistro
{
    /// <summary>Lo unico que puede pedirse al registrarse. Lo demas lo asigna un Administrador.</summary>
    public static readonly string[] RolesPublicos = { "buscador", "prestador" };

    private static readonly TimeSpan VidaDelToken = TimeSpan.FromHours(24);

    private readonly IRepositorioUsuarios _usuarios;
    private readonly IRepositorioPerfiles _perfiles;
    private readonly IUnidadDeTrabajo _unidad;
    private readonly IHashContrasenas _hash;
    private readonly IGeneradorSecretos _secretos;
    private readonly IRelojSistema _reloj;
    private readonly INotificaciones _avisos;
    private readonly IAuditor _auditor;

    public ServicioRegistro(
        IRepositorioUsuarios usuarios, IRepositorioPerfiles perfiles, IUnidadDeTrabajo unidad,
        IHashContrasenas hash, IGeneradorSecretos secretos, IRelojSistema reloj,
        INotificaciones avisos, IAuditor auditor)
    {
        _usuarios = usuarios;
        _perfiles = perfiles;
        _unidad = unidad;
        _hash = hash;
        _secretos = secretos;
        _reloj = reloj;
        _avisos = avisos;
        _auditor = auditor;
    }

    public async Task<Resultado<CuentaCreadaDto>> RegistrarAsync(
        RegistrarCuentaCmd cmd, string? ip = null, CancellationToken ct = default)
    {
        var correo = Texto.NormalizarCorreo(cmd.Correo);
        var nombre = cmd.Nombre.Trim();

        var rol = await _usuarios.RolPorCodigoAsync(cmd.Rol, ct);
        if (rol is null)
            return Resultado.Falla<CuentaCreadaDto>(Fallas.Validacion,
                "Ese rol no existe. Al registrarse se elige entre buscador y prestador.");

        // RN-S1-08. Se comprueban las dos cosas a proposito: la lista blanca y la bandera
        // de la tabla. Si manana alguien agrega un rol interno y olvida la lista, el rol
        // sigue sin poder pedirse.
        if (rol.EsInterno || !RolesPublicos.Contains(rol.Codigo))
            return Resultado.Falla<CuentaCreadaDto>(Fallas.NoAutorizado,
                "Ese rol no se puede pedir al registrarse.");

        // RN-S1-01. Esta comprobacion es por cortesia: la que de verdad manda es el indice
        // unico de la base, porque dos registros a la vez pasarian los dos por aqui.
        if (await _usuarios.ExisteCorreoAsync(correo, ct))
            return Resultado.Falla<CuentaCreadaDto>(Fallas.Conflicto,
                "Ya existe una cuenta con ese correo.");

        var ahora = _reloj.Ahora;
        var token = _secretos.NuevoToken();

        var usuario = new Usuario
        {
            Correo = correo,
            Nombre = nombre,
            HashContrasena = _hash.Hash(cmd.Contrasena),   // RN-S1-03
            CorreoVerificado = false,
            Estado = EstadoCuenta.PendienteVerificacion,   // RN-S1-02
            TokenVerificacion = _secretos.HashToken(token),
            TokenVerificacionExpiraEn = ahora.Add(VidaDelToken),
            CreadoEn = ahora,
            ActualizadoEn = ahora,
        };
        usuario.Roles.Add(new UsuarioRol { UsuarioId = usuario.Id, RolId = rol.Id, Rol = rol, AsignadoEn = ahora });

        _usuarios.Agregar(usuario);

        if (rol.Codigo == "prestador")
            _perfiles.Agregar(new PerfilPrestador
            {
                UsuarioId = usuario.Id,
                NombrePublico = nombre,
                Slug = await SlugLibreAsync(nombre, ct),
                Estado = EstadoPerfil.Borrador,
                ActualizadoEn = ahora,
            });
        else
            _perfiles.Agregar(new PerfilBuscador { UsuarioId = usuario.Id });

        _avisos.Encolar(new Notificacion
        {
            DestinatarioId = usuario.Id,
            Tipo = TipoNotificacion.CorreoVerificacion,
            Canal = CanalNotificacion.Correo,
            ClaveIdempotencia = $"verificacion:{usuario.Id}:{ahora:yyyyMMddHHmmss}",
            Asunto = "Confirma tu correo en ZASS!",
            // El token va en una carga estructurada que el enviador purga tras el envio.
            Cuerpo = CargaDeAviso.ConToken(token),
            ProgramadaPara = ahora,
        });

        _auditor.Registrar(Auditar.Entrada(
            "registro_cuenta", "usuario", usuario.Id, ahora,
            actorId: usuario.Id, contexto: new { rol = rol.Codigo }, ip: ip));

        await _unidad.GuardarAsync(ct);

        return Resultado.Ok(new CuentaCreadaDto(usuario.Id, rol.Codigo));
    }

    /// <summary>
    /// «Ofrecer mis servicios»: quien ya tiene cuenta (como buscador) activa tambien el rol de prestador, sin
    /// salir ni crear otra cuenta. Una persona busca hoy y ofrece manana; el registro no obliga a elegir una vez.
    /// Mismas reglas que al registrarse (RN-S1-08: solo roles publicos). Es idempotente: si ya es prestador no
    /// hace nada, asi un doble clic no deja dos perfiles.
    /// </summary>
    public async Task<Resultado> ActivarPrestadorAsync(
        Guid usuarioId, string? ip = null, CancellationToken ct = default)
    {
        var usuario = await _usuarios.PorIdAsync(usuarioId, ct);
        if (usuario is null || usuario.Estado is EstadoCuenta.Suspendida or EstadoCuenta.Eliminada)
            return Resultado.Falla(Fallas.NoAutorizado, "La sesión no es válida. Vuelve a iniciar sesión.");

        if (usuario.Roles.Any(r => r.Rol.Codigo == "prestador")) return Resultado.Ok();

        var rol = await _usuarios.RolPorCodigoAsync("prestador", ct);
        if (rol is null || rol.EsInterno || !RolesPublicos.Contains(rol.Codigo))
            return Resultado.Falla(Fallas.NoAutorizado, "Ese rol no se puede activar.");

        var ahora = _reloj.Ahora;
        usuario.Roles.Add(new UsuarioRol { UsuarioId = usuario.Id, RolId = rol.Id, Rol = rol, AsignadoEn = ahora });
        usuario.ActualizadoEn = ahora;

        // Puede que el perfil ya exista (cuenta creada como prestador y con el rol quitado a mano): no se duplica.
        if (await _perfiles.PrestadorDeUsuarioAsync(usuario.Id, ct) is null)
            _perfiles.Agregar(new PerfilPrestador
            {
                UsuarioId = usuario.Id,
                NombrePublico = usuario.Nombre,
                Slug = await SlugLibreAsync(usuario.Nombre, ct),
                Estado = EstadoPerfil.Borrador,
                ActualizadoEn = ahora,
            });

        _auditor.Registrar(Auditar.Entrada(
            "activar_prestador", "usuario", usuario.Id, ahora,
            actorId: usuario.Id, contexto: new { rol = rol.Codigo }, ip: ip));

        await _unidad.GuardarAsync(ct);
        return Resultado.Ok();
    }

    /// <summary>
    /// El slug es unico en la base. Dos "Juan Perez" existen de verdad, asi que el segundo
    /// lleva sufijo en vez de un error que la persona no puede resolver.
    /// </summary>
    private async Task<string> SlugLibreAsync(string nombre, CancellationToken ct)
    {
        var baseSlug = Texto.Slug(nombre);
        if (!await _perfiles.ExisteSlugAsync(baseSlug, ct)) return baseSlug;

        for (var n = 2; n <= 50; n++)
        {
            var intento = $"{baseSlug}-{n}";
            if (!await _perfiles.ExisteSlugAsync(intento, ct)) return intento;
        }
        return $"{baseSlug}-{Guid.NewGuid():N}"[..Math.Min(baseSlug.Length + 9, 100)];
    }
}
