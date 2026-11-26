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
    string? RazonSocial = null, bool MayorDeEdad = false) : IDatosDeCuenta;

/// <summary>
/// Los datos de la cuenta que pide el registro completo (20-nov-2026). Los comparten el registro
/// (API-08) y ES-2, para que los valide UN solo validador (<see cref="ValidadorDatosDeCuenta"/>).
/// </summary>
public interface IDatosDeCuenta
{
    string? Tipo { get; }
    string? Apellido { get; }
    string? Rut { get; }
    string? Telefono { get; }
    string? RazonSocial { get; }
    bool MayorDeEdad { get; }
}

/// <summary>
/// ES-2. Los mismos datos que el registro, para una cuenta creada antes del 20-nov-2026 que no
/// los tiene. Se piden al entrar y no se puede seguir sin ellos.
/// </summary>
public sealed record CompletarDatosCuentaCmd(
    string? Tipo = null, string? Apellido = null, string? Rut = null, string? Telefono = null,
    string? RazonSocial = null, bool MayorDeEdad = false) : IDatosDeCuenta;

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

    /// <summary>
    /// ES-2: completar los datos de una cuenta antigua con un RUT que ya usa OTRA cuenta. Desde
    /// el 21-nov-2026 el registro (API-08) no usa este texto sino <see cref="MensajeRutConCuenta"/>.
    /// </summary>
    public const string MensajeRutOcupado =
        "Ya existe una cuenta con ese RUT. Si es tuya, entra con tu correo.";

    /// <summary>
    /// API-08 con un RUT que ya tiene cuenta (decision de Pablo, 21-nov-2026). Es SIEMPRE este
    /// texto, haya salido o no el correo (la espera entre avisos no se nota por fuera), y no
    /// dice nada de la cuenta original: ni su correo, ni su telefono, ni enmascarados.
    /// </summary>
    public const string MensajeRutConCuenta =
        "Este RUT ya tiene una cuenta en ZASS!. Enviamos un mensaje al correo con que se creó esa " +
        "cuenta, con un enlace para entrar y otro para crear una contraseña nueva.";

    /// <summary>
    /// API-08 con un RUT cuya cuenta todavia NO verifico su correo (auditoria 22-nov-2026). No se
    /// escribe a ese correo: nadie demostro que sea de la dueña del RUT, y podria ser justamente el
    /// de quien lo registro con un RUT ajeno. Se dice la verdad sin dar datos: la cuenta se borra
    /// sola si nadie la confirma en 72 horas desde que se creo, y entonces el RUT queda libre.
    /// </summary>
    public const string MensajeRutSinConfirmar =
        "Este RUT tiene un registro que todavía no se ha confirmado. Si no se confirma dentro de " +
        "72 horas desde que se creó, se borra solo y podrás crear tu cuenta con este RUT. Si es tuyo, " +
        "busca en tu correo el mensaje para confirmarlo.";

    /// <summary>
    /// Lo minimo entre dos avisos de «intentaron registrarse con tu RUT» a una misma cuenta. Como
    /// API-71: el limite de tasa protege por IP, y el daño de este aviso no lo recibe quien llama
    /// sino la dueña del correo. Una hora y no dos minutos porque el correo no lleva nada que
    /// caduque (solo enlaces a «Entrar» y a «Recuperar contraseña»): el primero sigue sirviendo.
    /// </summary>
    public static readonly TimeSpan EsperaEntreAvisosDeRut = TimeSpan.FromHours(1);

    private readonly IRepositorioUsuarios _usuarios;
    private readonly IRepositorioPerfiles _perfiles;
    private readonly IUnidadDeTrabajo _unidad;
    private readonly IHashContrasenas _hash;
    private readonly IGeneradorSecretos _secretos;
    private readonly IRelojSistema _reloj;
    private readonly INotificaciones _avisos;
    private readonly IAuditor _auditor;
    private readonly IContrasenasFiltradas _filtradas;
    private readonly ServicioCuentasSinVerificar? _sinVerificar;

    /// <param name="sinVerificar">
    /// Opcional para no tocar las pruebas que no lo necesitan; en la aplicacion siempre esta
    /// (AgregarZass). Sin el, una cuenta caducada que choca con el registro espera a la purga
    /// del planificador en vez de liberarse en el acto.
    /// </param>
    public ServicioRegistro(
        IRepositorioUsuarios usuarios, IRepositorioPerfiles perfiles, IUnidadDeTrabajo unidad,
        IHashContrasenas hash, IGeneradorSecretos secretos, IRelojSistema reloj,
        INotificaciones avisos, IAuditor auditor, IContrasenasFiltradas filtradas,
        ServicioCuentasSinVerificar? sinVerificar = null)
    {
        _sinVerificar = sinVerificar;
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

        // P19 (T49): rechazar contrasenas que aparecen en filtraciones conocidas (HIBP).
        // 25-nov-2026: va ANTES de mirar el correo. Si fuera despues, «contraseña filtrada» solo
        // saldria con correos SIN cuenta, y la respuesta volveria a decir quien tiene cuenta.
        if (await _filtradas.EstaFiltradaAsync(cmd.Contrasena, ct))
            return Resultado.Falla<CuentaCreadaDto>(Fallas.Validacion,
                "Esa contraseña aparece en filtraciones de datos conocidas. Elige otra.");

        // RN-S1-01. Esta comprobacion es por cortesia: la que de verdad manda es el indice
        // unico de la base, porque dos registros a la vez pasarian los dos por aqui.
        //
        // Auditoria 22-nov-2026: si el correo lo retiene una cuenta que nunca se verifico y ya
        // paso su plazo de 72 h, se borra en el acto (si no tiene actividad) y el correo queda libre.
        //
        // 25-nov-2026 (Pablo): un correo con cuenta ya NO responde «Ya existe una cuenta con ese
        // correo»: eso le decia a cualquiera quien esta en ZASS!. Responde EXACTAMENTE lo mismo que
        // un registro nuevo («revisa tu correo»), y lo que cambia es el correo que sale, que solo
        // lee la dueña de esa direccion: ver AvisarALaDuenaDelCorreoAsync.
        if (await _usuarios.ExisteCorreoAsync(correo, ct)
            && await _usuarios.PorCorreoAsync(correo, ct) is { } duenaDelCorreo
            && !await LiberarSiCaducoAsync(duenaDelCorreo, ip, ct))
        {
            // El registro nuevo calcula el hash (Argon2id, lo caro); este camino tambien, para que
            // el tiempo de respuesta no lo delate. El resultado se descarta.
            _ = _hash.Hash(cmd.Contrasena);
            await AvisarALaDuenaDelCorreoAsync(duenaDelCorreo, ip, ct);
            // Un id inventado: la interfaz no lo usa, y uno vacio o repetido delataria el caso.
            return Resultado.Ok(new CuentaCreadaDto(Guid.NewGuid(), rol.Codigo));
        }

        // Una persona o empresa, una cuenta. Igual que el correo: por cortesia, porque la que
        // manda es el indice unico parcial de la base.
        var rut = Rut.Normalizar(cmd.Rut);
        if (rut is not null && await _usuarios.PorRutAsync(rut, ct) is { } duena
            && !await LiberarSiCaducoAsync(duena, ip, ct))
        {
            // Auditoria 22-nov-2026: el aviso de RN-S1-13 va SOLO a un correo verificado. Uno sin
            // verificar puede ser el de quien registro un RUT ajeno: avisarle seria contarle al
            // atacante que la dueña intento registrarse. Se responde con la verdad y sin datos.
            if (!duena.CorreoVerificado)
            {
                _auditor.Registrar(Auditar.Entrada(
                    "registro_con_rut_sin_confirmar", "usuario", duena.Id, _reloj.Ahora, ip: ip));
                await _unidad.GuardarAsync(ct);
                return Resultado.Falla<CuentaCreadaDto>(Fallas.RutConCuenta, MensajeRutSinConfirmar);
            }
            await AvisarALaDuenaDelRutAsync(duena, ip, ct);
            return Resultado.Falla<CuentaCreadaDto>(Fallas.RutConCuenta, MensajeRutConCuenta);
        }
        var tipo = string.Equals(cmd.Tipo, "empresa", StringComparison.OrdinalIgnoreCase)
            ? TipoCuenta.Empresa : TipoCuenta.Particular;
        var razonSocial = tipo == TipoCuenta.Empresa ? Recortar(cmd.RazonSocial) : null;

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
    /// Decision de Pablo (21-nov-2026): un RUT que ya tiene cuenta no crea otra, y en vez de un
    /// «ya existe» sin salida se le escribe a la cuenta ORIGINAL, a su correo, con el enlace para
    /// entrar y el de recuperar la contraseña. Quien escribio el formulario no recibe nada: si no
    /// es la dueña, no se entera de ningun dato de ella.
    ///
    /// <para>
    /// Espera por cuenta (<see cref="EsperaEntreAvisosDeRut"/>): dentro de ella no sale otro
    /// correo y la respuesta es la misma. Lo que se mira es el ultimo aviso de este tipo en la
    /// cola, asi que no hace falta columna nueva.
    /// </para>
    ///
    /// <para>
    /// Dos intentos a la vez: los dos ven el mismo «ultimo aviso» y arman la MISMA clave de
    /// idempotencia (cuelga del aviso anterior, no del reloj), asi que el indice unico deja
    /// entrar uno solo, y el otro recibe un falso, no un 500.
    /// </para>
    /// </summary>
    private async Task AvisarALaDuenaDelRutAsync(Usuario duena, string? ip, CancellationToken ct)
    {
        // Una cuenta eliminada se anonimiza y suelta el RUT, asi que aqui no deberia llegar; si
        // llegara, no se le escribe a un correo que ya no es de nadie.
        if (duena.Estado is EstadoCuenta.Eliminada) return;

        var ahora = _reloj.Ahora;
        var ultimo = await _avisos.UltimoAsync(duena.Id, TipoNotificacion.RegistroConRutExistente, ct);
        var recienAvisada = ultimo is not null && ahora - ultimo.ProgramadaPara < EsperaEntreAvisosDeRut;

        var encolado = !recienAvisada && await _avisos.EncolarSiNoExisteAsync(new Notificacion
        {
            DestinatarioId = duena.Id,
            Tipo = TipoNotificacion.RegistroConRutExistente,
            Canal = CanalNotificacion.Correo,
            ClaveIdempotencia = $"rut-con-cuenta:{duena.Id:N}:tras:{ultimo?.Id.ToString("N") ?? "ninguno"}",
            Asunto = "Alguien intentó crear otra cuenta con tu RUT en ZASS!",
            // Nada que guardar: la plantilla solo necesita el nombre de la dueña y la URL publica.
            Cuerpo = "{}",
            ProgramadaPara = ahora,
        }, ct);

        // Cada intento queda, haya salido correo o no: muchos seguidos contra la misma cuenta es
        // justo lo que hay que poder ver. Quien lo intento no tiene cuenta: no hay actor, queda la IP.
        _auditor.Registrar(Auditar.Entrada(
            "registro_con_rut_existente", "usuario", duena.Id, ahora,
            contexto: new { avisoEncolado = encolado }, ip: ip));
        await _unidad.GuardarAsync(ct);
    }

    /// <summary>
    /// 25-nov-2026 (Pablo): alguien se registro con un correo que ya tiene cuenta. Quien lo
    /// escribio recibe la respuesta de siempre; el correo sale a ESA direccion, asi que solo lo lee
    /// su dueña, y no le cuenta nada a quien no lo es.
    ///
    /// <para>
    /// Cuenta sin verificar: lo mas probable es que sea la misma persona que perdio el enlace de
    /// confirmacion (o se le vencio) y vuelve a registrarse. Se le manda un enlace NUEVO, con la
    /// misma espera que el reenvio de CS-01 bis (<see cref="ServicioCuenta.EsperaEntreReenvios"/>).
    /// Cuenta verificada: un aviso con los enlaces para entrar y para crear una contraseña nueva
    /// (<see cref="TipoNotificacion.RegistroConCorreoExistente"/>), con la espera del aviso de RUT.
    /// </para>
    /// </summary>
    private async Task AvisarALaDuenaDelCorreoAsync(Usuario duena, string? ip, CancellationToken ct)
    {
        if (duena.Estado is EstadoCuenta.Eliminada) return;
        var ahora = _reloj.Ahora;
        bool encolado;

        if (!duena.CorreoVerificado)
        {
            var emitidoEn = duena.TokenVerificacionExpiraEn?.Subtract(VidaDelToken);
            encolado = emitidoEn is null || ahora - emitidoEn >= ServicioCuenta.EsperaEntreReenvios;
            if (encolado)
            {
                var token = _secretos.NuevoToken();
                // El enlace anterior deja de servir: el bueno es el ultimo (igual que CS-01 bis).
                duena.TokenVerificacion = _secretos.HashToken(token);
                duena.TokenVerificacionExpiraEn = ahora.Add(VidaDelToken);
                duena.ActualizadoEn = ahora;
                _avisos.Encolar(new Notificacion
                {
                    DestinatarioId = duena.Id,
                    Tipo = TipoNotificacion.CorreoVerificacion,
                    Canal = CanalNotificacion.Correo,
                    ClaveIdempotencia = $"verificacion:{duena.Id}:{ahora:yyyyMMddHHmmss}:{Guid.NewGuid():N}",
                    Asunto = "Confirma tu correo en ZASS!",
                    // El token va en una carga estructurada que el enviador purga tras el envio.
                    Cuerpo = CargaDeAviso.ConToken(token),
                    ProgramadaPara = ahora,
                });
            }
        }
        else
        {
            var ultimo = await _avisos.UltimoAsync(duena.Id, TipoNotificacion.RegistroConCorreoExistente, ct);
            var recienAvisada = ultimo is not null && ahora - ultimo.ProgramadaPara < EsperaEntreAvisosDeRut;
            // La clave cuelga del aviso anterior, como en el RUT: dos intentos a la vez encolan
            // uno solo, y el otro recibe un falso, no un 500.
            encolado = !recienAvisada && await _avisos.EncolarSiNoExisteAsync(new Notificacion
            {
                DestinatarioId = duena.Id,
                Tipo = TipoNotificacion.RegistroConCorreoExistente,
                Canal = CanalNotificacion.Correo,
                ClaveIdempotencia = $"correo-con-cuenta:{duena.Id:N}:tras:{ultimo?.Id.ToString("N") ?? "ninguno"}",
                Asunto = "Alguien intentó crear una cuenta con tu correo en ZASS!",
                Cuerpo = "{}",
                ProgramadaPara = ahora,
            }, ct);
        }

        // Quien lo intento no tiene cuenta: no hay actor, queda la IP.
        _auditor.Registrar(Auditar.Entrada(
            "registro_con_correo_existente", "usuario", duena.Id, ahora,
            contexto: new { verificada = duena.CorreoVerificado, avisoEncolado = encolado }, ip: ip));
        await _unidad.GuardarAsync(ct);
    }

    private async Task<bool> LiberarSiCaducoAsync(Usuario? cuenta, string? ip, CancellationToken ct) =>
        cuenta is not null && _sinVerificar is not null
        && await _sinVerificar.LiberarSiCaducoAsync(cuenta, ip, ct);

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
    /// ES-2. Completa los datos que le faltan a una cuenta creada antes del registro completo
    /// (20-nov-2026): tipo, apellido o razon social, RUT, telefono y la casilla de mayoria de
    /// edad. Las validaciones de forma son las del registro (<see cref="ValidadorDatosDeCuenta"/>,
    /// en la API); aqui van las que miran la base, con el mismo mensaje que el registro.
    ///
    /// <para>
    /// Solo LLENA lo que falta: un RUT que la cuenta ya tiene no se cambia por aqui (cambiar el
    /// RUT de una cuenta seria cambiar de quien es). Si ya estaba completa no hay nada que hacer,
    /// y se dice: este no es un formulario para editar datos.
    /// </para>
    /// </summary>
    public async Task<Resultado> CompletarDatosAsync(
        Guid usuarioId, CompletarDatosCuentaCmd cmd, string? ip = null, CancellationToken ct = default)
    {
        var usuario = await _usuarios.PorIdAsync(usuarioId, ct);
        if (usuario is null || usuario.Estado is EstadoCuenta.Suspendida or EstadoCuenta.Eliminada)
            return Resultado.Falla(Fallas.NoAutorizado, "La sesión no es válida. Vuelve a iniciar sesión.");

        if (DatosDeCuenta.Pendientes(usuario).Length == 0)
            return Resultado.Falla(Fallas.Conflicto, "Tu cuenta ya tiene sus datos completos.");

        var rut = Rut.Normalizar(cmd.Rut);
        if (usuario.Rut is not null)
        {
            if (rut != usuario.Rut)
                return Resultado.Falla(Fallas.Validacion,
                    "El RUT de tu cuenta ya está registrado y no se puede cambiar aquí.");
        }
        else if (rut is not null && await _usuarios.ExisteRutAsync(rut, ct))
        {
            // Por cortesia, como en el registro: el indice unico parcial es la garantia.
            return Resultado.Falla(Fallas.Conflicto, MensajeRutOcupado);
        }

        var ahora = _reloj.Ahora;
        if (usuario.Rut is null)
        {
            var tipo = string.Equals(cmd.Tipo, "empresa", StringComparison.OrdinalIgnoreCase)
                ? TipoCuenta.Empresa : TipoCuenta.Particular;
            usuario.Tipo = tipo;
            usuario.Rut = rut;
            usuario.Apellido = tipo == TipoCuenta.Particular ? Recortar(cmd.Apellido) : null;
            usuario.RazonSocial = tipo == TipoCuenta.Empresa ? Recortar(cmd.RazonSocial) : null;
        }
        usuario.Telefono ??= Recortar(cmd.Telefono);
        if (cmd.MayorDeEdad) usuario.MayorDeEdadConfirmadoEn ??= ahora;
        usuario.ActualizadoEn = ahora;

        _auditor.Registrar(Auditar.Entrada(
            "datos_de_cuenta_completados", "usuario", usuario.Id, ahora, actorId: usuario.Id, ip: ip));

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
