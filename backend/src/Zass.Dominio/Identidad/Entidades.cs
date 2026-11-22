using Zass.Dominio.Catalogo;
using Zass.Dominio.Comun;

namespace Zass.Dominio.Identidad;

/// <summary>CL-01 · TBL-01. Identidad y credenciales de acceso.</summary>
public class Usuario
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Correo { get; set; } = "";
    public string HashContrasena { get; set; } = "";
    /// <summary>De pila si es Particular; el de la persona de contacto si es Empresa.</summary>
    public string Nombre { get; set; } = "";

    // Datos de la cuenta (decision de Pablo, 20-nov-2026). Nulos en las cuentas creadas antes.
    public TipoCuenta Tipo { get; set; } = TipoCuenta.Particular;
    public string? Apellido { get; set; }
    /// <summary>Normalizado «12345678-K». Unico: una persona o empresa, una cuenta.</summary>
    public string? Rut { get; set; }
    public string? Telefono { get; set; }
    public string? RazonSocial { get; set; }
    /// <summary>Cuando marco «soy mayor de 18». No se pide ni se guarda la fecha de nacimiento.</summary>
    public DateTime? MayorDeEdadConfirmadoEn { get; set; }

    public bool CorreoVerificado { get; set; }
    // Los dos tokens de un solo uso de S1. Se guarda el hash, nunca el token que viajo
    // por correo, y cada uno lleva su propio vencimiento porque duran distinto: 24 h el
    // de verificacion (CS-01) y 1 h el de recuperacion (CS-02). Una sola columna de
    // vencimiento hacia que pedir recuperacion le acortara en silencio el plazo al otro.
    public string? TokenVerificacion { get; set; }
    public DateTime? TokenVerificacionExpiraEn { get; set; }
    public string? TokenRecuperacion { get; set; }
    public DateTime? TokenRecuperacionExpiraEn { get; set; }
    public EstadoCuenta Estado { get; set; } = EstadoCuenta.PendienteVerificacion;

    // Bloqueo por intentos fallidos de entrar (decision de Pablo, 22-nov-2026): tras 10
    // fallos seguidos la cuenta queda 15 minutos sin poder entrar, aunque la contrasena sea la
    // buena. Se cuenta en la base y no en memoria para que valga con varias instancias de la
    // API. Al bloquear, el contador vuelve a cero: pasado el plazo se empieza de nuevo.
    // Quien cambia estos valores es SQL atomico (RepositorioUsuariosEfCore), no esta clase.
    public int IntentosFallidos { get; set; }
    public DateTime? BloqueadaHasta { get; set; }

    public bool EstaBloqueada(DateTime ahora) => BloqueadaHasta is { } hasta && hasta > ahora;

    // Verificacion en dos pasos con TOTP (decision de Pablo, 22-nov-2026): obligatoria para
    // los roles del panel interno y opcional para el resto. El secreto se guarda cifrado con
    // AES-256-GCM (ICifradoDeDatos), nunca en claro. Quien cambia el ultimo paso aceptado, los
    // codigos de respaldo y el desafio pendiente es SQL atomico (RepositorioUsuariosEfCore):
    // dos peticiones simultaneas no pueden gastar dos veces el mismo codigo.
    /// <summary>El secreto TOTP en uso, cifrado. Nulo si la cuenta no tiene MFA.</summary>
    public byte[]? MfaSecretoCifrado { get; set; }
    /// <summary>El secreto del alta en curso, cifrado, hasta que se confirma con un codigo.</summary>
    public byte[]? MfaSecretoPendienteCifrado { get; set; }
    public DateTime? MfaActivadoEn { get; set; }
    /// <summary>El ultimo paso de 30 s aceptado: un codigo de ese paso o de uno anterior no vuelve a servir.</summary>
    public long? MfaUltimoPaso { get; set; }
    /// <summary>Los hash (SHA-256) de los codigos de respaldo que quedan sin usar.</summary>
    public string[]? MfaCodigosRespaldo { get; set; }
    /// <summary>Hash del desafio del segundo paso del inicio de sesion (vive 5 minutos, un solo uso).</summary>
    public string? MfaDesafioHash { get; set; }
    public DateTime? MfaDesafioExpiraEn { get; set; }

    public bool MfaActivo => MfaActivadoEn is not null && MfaSecretoCifrado is not null;

    /// <summary>Si la cuenta tiene algun rol del panel interno, que obliga a usar MFA.</summary>
    public bool TieneRolInterno => Roles.Any(r => r.Rol is { EsInterno: true });

    public void QuitarMfa()
    {
        MfaSecretoCifrado = null;
        MfaSecretoPendienteCifrado = null;
        MfaActivadoEn = null;
        MfaUltimoPaso = null;
        MfaCodigosRespaldo = null;
        MfaDesafioHash = null;
        MfaDesafioExpiraEn = null;
    }

    public DateTime CreadoEn { get; set; } = DateTime.UtcNow;
    public DateTime ActualizadoEn { get; set; } = DateTime.UtcNow;
    public DateTime? AnonimizadoEn { get; set; }

    public List<UsuarioRol> Roles { get; set; } = new();
    public PerfilPrestador? PerfilPrestador { get; set; }
    public PerfilBuscador? PerfilBuscador { get; set; }

    public bool TieneRol(string codigo) =>
        Roles.Any(r => r.Rol is not null && r.Rol.Codigo == codigo);

    public void VerificarCorreo()
    {
        CorreoVerificado = true;
        TokenVerificacion = null;
        if (Estado == EstadoCuenta.PendienteVerificacion) Estado = EstadoCuenta.Activa;
        ActualizadoEn = DateTime.UtcNow;
    }

    /// <summary>
    /// CS-04. Borra los datos personales pero conserva la fila: las resenas que esta
    /// persona escribio siguen en pie, firmadas como "usuario eliminado" (RN-S1-07).
    /// </summary>
    public void Anonimizar()
    {
        Correo = $"eliminado-{Id:N}@zass.local";
        Nombre = "Usuario eliminado";
        Apellido = null;
        Rut = null;
        Telefono = null;
        RazonSocial = null;
        HashContrasena = "";
        TokenVerificacion = null;
        TokenRecuperacion = null;
        QuitarMfa();
        Estado = EstadoCuenta.Eliminada;
        AnonimizadoEn = DateTime.UtcNow;
        ActualizadoEn = DateTime.UtcNow;
    }
}

/// <summary>CL-02 · TBL-02.</summary>
public class Rol
{
    public short Id { get; set; }
    public string Codigo { get; set; } = "";
    public string Nombre { get; set; } = "";
    public bool EsInterno { get; set; }
}

/// <summary>TBL-03. Resuelve la relacion muchos a muchos.</summary>
public class UsuarioRol
{
    public Guid UsuarioId { get; set; }
    public short RolId { get; set; }
    public DateTime AsignadoEn { get; set; } = DateTime.UtcNow;
    public Guid? AsignadoPor { get; set; }

    public Usuario? Usuario { get; set; }
    public Rol? Rol { get; set; }
}

/// <summary>TBL-04. Guarda el hash del token, nunca el token (RN-S1-03).</summary>
public class Sesion
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UsuarioId { get; set; }
    public string HashToken { get; set; } = "";
    public string? IpOrigen { get; set; }
    public string? AgenteUsuario { get; set; }
    public DateTime CreadaEn { get; set; } = DateTime.UtcNow;
    public DateTime ExpiraEn { get; set; }
    public DateTime? RevocadaEn { get; set; }

    public Usuario? Usuario { get; set; }
    public bool EstaVigente(DateTime ahora) => RevocadaEn is null && ExpiraEn > ahora;
}

/// <summary>
/// CL-03 · TBL-05. La oferta publicable.
/// Latitud y Longitud se guardan como numeros; la columna geography la genera
/// PostgreSQL, de modo que esta capa no depende de ninguna libreria geoespacial.
/// </summary>
public class PerfilPrestador
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UsuarioId { get; set; }
    /// <summary>
    /// Nulo mientras el perfil esta en borrador: CU-03 guarda el avance en el paso 2 y
    /// el rubro se elige en el paso 3, asi que exigirlo antes impediria el guardado
    /// parcial que el propio caso de uso pide. RN-S1-05 lo exige para publicar.
    /// </summary>
    public short? RubroId { get; set; }
    public string Slug { get; set; } = "";
    public string NombrePublico { get; set; } = "";
    public string? Descripcion { get; set; }
    public string? UrlFoto { get; set; }
    public EstadoPerfil Estado { get; set; } = EstadoPerfil.Borrador;

    /// <summary>Direccion literal cifrada. Nunca se consulta ni se indexa (RN-S1-06).</summary>
    public byte[]? DireccionCifrada { get; set; }
    public double? Latitud { get; set; }
    public double? Longitud { get; set; }
    public OrigenUbicacion OrigenUbicacion { get; set; } = OrigenUbicacion.Declarada;

    public string? TelefonoPublico { get; set; }
    public string? CorreoPublico { get; set; }
    public string? Whatsapp { get; set; }
    public string? SitioWeb { get; set; }

    // Derivados. Unica desnormalizacion deliberada del modelo (CC-07).
    public decimal? PromedioCalificacion { get; set; }
    public int TotalResenas { get; set; }

    /// <summary>Token de version optimista (CC-02).</summary>
    public int Version { get; set; }
    public DateTime? PublicadoEn { get; set; }
    public DateTime ActualizadoEn { get; set; } = DateTime.UtcNow;

    public Usuario? Usuario { get; set; }
    public Rubro? Rubro { get; set; }
    public List<PrestadorServicio> Servicios { get; set; } = new();
    public List<CoberturaPrestador> Cobertura { get; set; } = new();

    public CoberturaPrestador? ZonaPrincipal() =>
        Cobertura.FirstOrDefault(c => c.EsPrincipal);

    /// <summary>RN-S1-05: rubro, al menos un servicio y al menos una zona.</summary>
    public bool EstaCompleto() =>
        !string.IsNullOrWhiteSpace(NombrePublico)
        && RubroId is > 0
        && Servicios.Any(s => s.Activo)
        && Cobertura.Count > 0;

    public Resultado Publicar()
    {
        if (!EstaCompleto())
            return Resultado.Falla(Fallas.ReglaDeNegocio,
                "Para publicar falta el nombre, el rubro, al menos un servicio y al menos una zona.");
        Estado = EstadoPerfil.Publicado;
        PublicadoEn ??= DateTime.UtcNow;
        ActualizadoEn = DateTime.UtcNow;
        return Resultado.Ok();
    }

    public void Despublicar()
    {
        Estado = EstadoPerfil.Despublicado;
        ActualizadoEn = DateTime.UtcNow;
    }

    /// <summary>RN-S5-11: con menos de 3 resenas se muestra el recuento, no el promedio.</summary>
    public bool PromedioVisible() => TotalResenas >= 3;
}

/// <summary>
/// CL-03 · TBL-NUEVA. Fotos de trabajos realizados que sube el prestador para su ficha publica.
/// Son PUBLICAS como la foto de perfil (no como los documentos de credenciales).
/// El tope de cuantas puede tener lo dice su plan vigente (Plan.MaxFotosTrabajos).
/// </summary>
public class FotoTrabajo
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid PerfilPrestadorId { get; set; }
    /// <summary>Clave en el almacenamiento (p. ej. "fotos/abc123.webp"). La genera el servidor.</summary>
    public string ClaveAlmacen { get; set; } = "";
    /// <summary>Orden de visualizacion en la ficha (0 = primero).</summary>
    public int Orden { get; set; }
    public DateTime CreadaEn { get; set; } = DateTime.UtcNow;

    public PerfilPrestador? PerfilPrestador { get; set; }
}

/// <summary>CL-04 · TBL-06.</summary>
public class PerfilBuscador
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UsuarioId { get; set; }
    public int? ZonaInteresId { get; set; }
    public short RadioPreferidoKm { get; set; } = 10;
    public bool AceptaAvisos { get; set; } = true;
    public bool CanalCorreo { get; set; } = true;
    public bool CanalPush { get; set; }

    /// <summary>
    /// CS-08. Los perfiles de prestador guardados, del mas reciente al mas antiguo. Son
    /// identificadores y nada mas: el nombre, la foto o la nota se leen al listar, porque
    /// copiarlos aqui los dejaria viejos en cuanto el prestador cambie su ficha.
    /// </summary>
    public List<Guid> Favoritos { get; set; } = new();

    /// <summary>
    /// CU-04. Los rubros que le interesan a quien busca (TBL-07), como punto de partida de la
    /// portada. Un arreglo y no una tabla, por lo mismo que `aspectos` de la resena: un conjunto
    /// chico que nunca se consulta por separado (normalizacion, excepcion deliberada).
    /// </summary>
    public List<short> RubrosInteres { get; set; } = new();

    public Usuario? Usuario { get; set; }
    public Zona? ZonaInteres { get; set; }
}
