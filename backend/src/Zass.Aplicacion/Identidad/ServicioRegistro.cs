using Zass.Aplicacion.Comun;
using Zass.Aplicacion.Puertos;
using Zass.Dominio.Comun;
using Zass.Dominio.Identidad;

namespace Zass.Aplicacion.Identidad;

/// <summary>
/// CU-01 · Registrar cuenta. API-08. Desde el 20-nov-2026 (decision de Pablo) pide ademas el
/// tipo de cuenta y sus datos: Particular = nombre, apellido, RUT y telefono; Empresa = razon
/// social, RUT de la empresa, nombre de contacto (en <paramref name="Nombre"/>) y telefono. Y la
/// casilla «soy mayor de 18»: no se pide la fecha de nacimiento (minimizacion, Ley 21.719).
///
/// Los campos nuevos llevan valor por omision para que el caso de uso se pueda llamar sin
/// ellos desde las pruebas de otras cosas; quien los EXIGE es el validador de la API.
/// </summary>
public sealed record RegistrarCuentaCmd(
    string Correo, string Contrasena, string Nombre, string Rol,
    string? Tipo = null, string? Apellido = null, string? Rut = null, string? Telefono = null,
    string? RazonSocial = null, bool MayorDeEdad = false);

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
    private readonly IContrasenasFiltradas _filtradas;

    public ServicioRegistro(
        IRepositorioUsuarios usuarios, IRepositorioPerfiles perfiles, IUnidadDeTrabajo unidad,
        IHashContrasenas hash, IGeneradorSecretos secretos, IRelojSistema reloj,
        INotificaciones avisos, IAuditor auditor, IContrasenasFiltradas filtradas)
    {
        _usuarios = usuarios;
        _perfiles = perfiles;
        _unidad = unidad;
        _hash = hash;
        _secretos = secretos;
        _reloj = reloj;
        _avisos = avisos;
        _auditor = auditor;
        _filtradas = filtradas;
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

        // Una persona o empresa, una cuenta. Igual que el correo: por cortesia, porque la que
        // manda es el indice unico parcial de la base.
        var rut = Rut.Normalizar(cmd.Rut);
        if (rut is not null && await _usuarios.ExisteRutAsync(rut, ct))
            return Resultado.Falla<CuentaCreadaDto>(Fallas.Conflicto,
                "Ya existe una cuenta con ese RUT. Si es tuya, entra con tu correo.");
        var tipo = string.Equals(cmd.Tipo, "empresa", StringComparison.OrdinalIgnoreCase)
            ? TipoCuenta.Empresa : TipoCuenta.Particular;
        var razonSocial = tipo == TipoCuenta.Empresa ? Recortar(cmd.RazonSocial) : null;

        // P19 (T49): rechazar contrasenas que aparecen en filtraciones conocidas (HIBP).
        if (await _filtradas.EstaFiltradaAsync(cmd.Contrasena, ct))
            return Resultado.Falla<CuentaCreadaDto>(Fallas.Validacion,
                "Esa contraseña aparece en filtraciones de datos conocidas. Elige otra.");

        var ahora = _reloj.Ahora;
        var token = _secretos.NuevoToken();

        var usuario = new Usuario
        {
            Correo = correo,
            Nombre = nombre,
            Tipo = tipo,
            Apellido = tipo == TipoCuenta.Particular ? Recortar(cmd.Apellido) : null,
            Rut = rut,
            Telefono = Recortar(cmd.Telefono),
            RazonSocial = razonSocial,
            MayorDeEdadConfirmadoEn = cmd.MayorDeEdad ? ahora : null,
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

        // El nombre con que parte la ficha: el de la empresa, o nombre y apellido. Se cambia
        // despues en Mi ficha.
        var nombrePublico = razonSocial
            ?? (usuario.Apellido is { } apellido ? $"{nombre} {apellido}" : nombre);
        if (rol.Codigo == "prestador")
            _perfiles.Agregar(new PerfilPrestador
            {
                UsuarioId = usuario.Id,
                NombrePublico = nombrePublico,
                Slug = await SlugLibreAsync(nombrePublico, ct),
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

        if (usuario.TieneRol("prestador")) return Resultado.Ok();

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
    ///
    /// Un slug reservado (Texto.SlugsReservados: choca con una ruta literal como
    /// /api/prestadores/mapa) se trata igual que uno ya ocupado: sufijo, nunca un registro que
    /// falla. Quien se llama «Mapa» no tiene por que saber como enruta ASP.NET.
    /// </summary>
    private async Task<string> SlugLibreAsync(string nombre, CancellationToken ct)
    {
        var baseSlug = Texto.Slug(nombre);
        if (!await OcupadoAsync(baseSlug)) return baseSlug;

        for (var n = 2; n <= 50; n++)
        {
            var intento = $"{baseSlug}-{n}";
            if (!await OcupadoAsync(intento)) return intento;
        }
        return $"{baseSlug}-{Guid.NewGuid():N}"[..Math.Min(baseSlug.Length + 9, 100)];

        async Task<bool> OcupadoAsync(string slug) =>
            Texto.EsSlugReservado(slug) || await _perfiles.ExisteSlugAsync(slug, ct);
    }

    private static string? Recortar(string? texto) =>
        string.IsNullOrWhiteSpace(texto) ? null : texto.Trim();
}
