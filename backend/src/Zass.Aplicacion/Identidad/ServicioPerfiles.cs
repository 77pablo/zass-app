using Zass.Aplicacion.Comun;
using Zass.Aplicacion.Puertos;
using Zass.Dominio.Comun;
using Zass.Dominio.Identidad;

namespace Zass.Aplicacion.Identidad;

/// <summary>API-19 - Editar el perfil de prestador. Exige la version leida (CC-02).</summary>
public sealed record EditarPerfilPrestadorCmd(
    string NombrePublico,
    string? Descripcion,
    short? RubroId,
    string? TelefonoPublico,
    string? CorreoPublico,
    string? Whatsapp,
    string? SitioWeb,
    string? Direccion,
    double? Latitud,
    double? Longitud,
    int Version);

/// <summary>API-23 - Preferencias del buscador (CU-04).</summary>
public sealed record EditarPerfilBuscadorCmd(
    int? ZonaInteresId,
    short RadioPreferidoKm,
    bool AceptaAvisos,
    bool CanalCorreo,
    bool CanalPush);

/// <summary>
/// API-18 - El perfil como lo ve su dueno: con la zona y sin la ubicacion exacta.
/// No tener campos de coordenadas no es un descuido, es la regla RN-S3-02 puesta en el
/// tipo: ninguna vista puede filtrar lo que el DTO no trae.
/// </summary>
public sealed record MiPerfilPrestadorDto(
    Guid Id,
    string Slug,
    string NombrePublico,
    string? Descripcion,
    short? RubroId,
    EstadoPerfil Estado,
    string? ZonaPrincipal,
    bool TieneDireccion,
    string? TelefonoPublico,
    string? CorreoPublico,
    string? Whatsapp,
    string? SitioWeb,
    int TotalResenas,
    decimal? PromedioCalificacion,
    int Version,
    DateTime? PublicadoEn,
    string[] FaltaParaPublicar);

/// <summary>
/// CU-03 y CU-04. Las tablas TBL-05 y TBL-06 son de MOD-01, asi que la edicion y la
/// publicacion del perfil viven aqui, aunque los servicios (MOD-03) y la cobertura
/// (MOD-04) los administre su propio modulo.
/// </summary>
public sealed class ServicioPerfiles
{
    public const short RadioMinimoKm = 1;
    public const short RadioMaximoKm = 50;

    private readonly IRepositorioUsuarios _usuarios;
    private readonly IRepositorioPerfiles _perfiles;
    private readonly IUnidadDeTrabajo _unidad;
    private readonly IRelojSistema _reloj;
    private readonly ICifradoDeDatos _cifrado;
    private readonly INotificaciones _avisos;
    private readonly IAuditor _auditor;

    public ServicioPerfiles(
        IRepositorioUsuarios usuarios, IRepositorioPerfiles perfiles, IUnidadDeTrabajo unidad,
        IRelojSistema reloj, ICifradoDeDatos cifrado, INotificaciones avisos, IAuditor auditor)
    {
        _usuarios = usuarios;
        _perfiles = perfiles;
        _unidad = unidad;
        _reloj = reloj;
        _cifrado = cifrado;
        _avisos = avisos;
        _auditor = auditor;
    }

    // ------------------------------------------------------------- CU-03

    public async Task<Resultado<MiPerfilPrestadorDto>> MiPerfilPrestadorAsync(
        Guid usuarioId, CancellationToken ct = default)
    {
        var perfil = await _perfiles.PrestadorDeUsuarioAsync(usuarioId, ct);
        if (perfil is null)
            return Resultado.Falla<MiPerfilPrestadorDto>(Fallas.NoEncontrado,
                "Esta cuenta no tiene perfil de prestador.");

        return Resultado.Ok(Describir(perfil));
    }

    /// <summary>
    /// API-19. La comprobacion de propiedad va aqui, en el caso de uso, y no en el
    /// endpoint: se busca el perfil POR el usuario de la sesion, nunca por un id que
    /// venga en la peticion. Asi no hay identificador que cambiar para editar lo ajeno.
    /// </summary>
    public async Task<Resultado> EditarPrestadorAsync(
        Guid usuarioId, EditarPerfilPrestadorCmd cmd, CancellationToken ct = default)
    {
        var perfil = await _perfiles.PrestadorDeUsuarioAsync(usuarioId, ct);
        if (perfil is null)
            return Resultado.Falla(Fallas.NoEncontrado, "Esta cuenta no tiene perfil de prestador.");

        // CC-02. La version que el cliente leyo tiene que ser la que esta guardada.
        // La base lo vuelve a comprobar con su token de concurrencia; esto evita el
        // viaje y da un mensaje util en vez de una excepcion de EF Core.
        if (cmd.Version != perfil.Version)
            return Resultado.Falla(Fallas.VersionDesactualizada,
                "Alguien editó este perfil mientras lo tenías abierto. Vuelve a cargarlo.");

        perfil.NombrePublico = cmd.NombrePublico.Trim();
        perfil.Descripcion = cmd.Descripcion?.Trim();
        perfil.RubroId = cmd.RubroId;
        perfil.TelefonoPublico = cmd.TelefonoPublico?.Trim();
        perfil.CorreoPublico = cmd.CorreoPublico is null
            ? null
            : Texto.NormalizarCorreo(cmd.CorreoPublico);
        perfil.Whatsapp = cmd.Whatsapp?.Trim();
        perfil.SitioWeb = cmd.SitioWeb?.Trim();

        if (cmd.Direccion is not null)
        {
            // RN-S1-06. La direccion literal entra cifrada y no se indexa. Lo que se
            // consulta son latitud y longitud, que alimentan la columna geography.
            perfil.DireccionCifrada = _cifrado.Cifrar(cmd.Direccion.Trim());
            perfil.Latitud = cmd.Latitud;
            perfil.Longitud = cmd.Longitud;
            perfil.OrigenUbicacion = cmd.Latitud is null
                ? OrigenUbicacion.Declarada
                : OrigenUbicacion.Geocodificada;
        }

        perfil.Version++;
        perfil.ActualizadoEn = _reloj.Ahora;

        await _unidad.GuardarAsync(ct);
        return Resultado.Ok();
    }

    /// <summary>API-22. Publica si se cumple el minimo publicable; si no, dice que falta.</summary>
    /// <summary>
    /// CU-03. Devuelve el id del perfil publicado: quien compone lo necesita para
    /// arrancar la suscripcion de prueba (MOD-13) sin volver a buscarlo.
    /// </summary>
    public async Task<Resultado<Guid>> PublicarAsync(
        Guid usuarioId, CancellationToken ct = default)
    {
        var perfil = await _perfiles.PrestadorDeUsuarioAsync(usuarioId, ct);
        if (perfil is null)
            return Resultado.Falla<Guid>(Fallas.NoEncontrado, "Esta cuenta no tiene perfil de prestador.");

        var usuario = await _usuarios.PorIdAsync(usuarioId, ct);
        if (usuario is null || !usuario.CorreoVerificado)
            return Resultado.Falla<Guid>(Fallas.ReglaDeNegocio,
                "Verifica tu correo antes de publicar el perfil.");   // RN-S1-02

        // El dominio decide si esta completo; aqui solo se traduce a lenguaje humano
        // cual de las cuatro cosas falta (flujo alternativo 4A de CU-03).
        var falta = Falta(perfil);
        if (falta.Length > 0)
            return Resultado.Falla<Guid>(Fallas.ReglaDeNegocio,
                "Para publicar falta: " + string.Join(", ", falta) + ".");

        // Hay que saberlo ANTES de publicar: es lo que distingue "lo acaba de publicar"
        // de "volvio a pulsar el boton".
        var yaEstabaPublicado = perfil.Estado == EstadoPerfil.Publicado;

        var publicado = perfil.Publicar();
        if (!publicado.Exito)
            return Resultado.Falla<Guid>(publicado.Codigo!, publicado.Mensaje!);
        perfil.ActualizadoEn = _reloj.Ahora;

        // Solo se avisa cuando el perfil ENTRA en publicado.
        //
        // Antes se encolaba siempre, con la clave armada desde PublicadoEn. Y PublicadoEn
        // se asigna con ??=, asi que no cambia: el segundo publicar generaba la MISMA
        // clave de idempotencia, chocaba con el unico de R-18 en notificaciones y salia
        // un 500. Un doble clic en "Publicar" bastaba. Lo encontro la prueba por HTTP que
        // publica dos veces.
        //
        // R-18 existe para que el aviso llegue una sola vez, no para que el caso de uso
        // le delegue la decision de si hay algo que avisar.
        if (!yaEstabaPublicado)
        {
            _avisos.Encolar(new Notificacion
            {
                DestinatarioId = usuarioId,
                Tipo = TipoNotificacion.PerfilPublicado,
                Canal = CanalNotificacion.Correo,
                // La clave es del AVISO, no de la fecha de publicacion: despublicar y
                // volver a publicar (flujo 4E de CU-03) es una publicacion nueva y su
                // aviso tambien, y con la clave vieja habria chocado igual.
                ClaveIdempotencia = $"perfil-publicado:{perfil.Id}:{_reloj.Ahora:yyyyMMddHHmmss}",
                Asunto = "Tu perfil ya esta publicado en ZASS!",
                Cuerpo = "Tu perfil ya aparece en las búsquedas de quienes buscan cerca de ti.",
                ProgramadaPara = _reloj.Ahora,
            });
        }

        _auditor.Registrar(Auditar.Entrada(
            "publicar_perfil", "perfil_prestador", perfil.Id, _reloj.Ahora, actorId: usuarioId));

        await _unidad.GuardarAsync(ct);
        return Resultado.Ok(perfil.Id);
    }

    /// <summary>API-22. Despublicar no borra: el perfil vuelve cuando su dueno quiera.</summary>
    public async Task<Resultado> DespublicarAsync(Guid usuarioId, CancellationToken ct = default)
    {
        var perfil = await _perfiles.PrestadorDeUsuarioAsync(usuarioId, ct);
        if (perfil is null)
            return Resultado.Falla(Fallas.NoEncontrado, "Esta cuenta no tiene perfil de prestador.");

        perfil.Despublicar();
        perfil.ActualizadoEn = _reloj.Ahora;

        _auditor.Registrar(Auditar.Entrada(
            "despublicar_perfil", "perfil_prestador", perfil.Id, _reloj.Ahora, actorId: usuarioId));

        await _unidad.GuardarAsync(ct);
        return Resultado.Ok();
    }

    // ------------------------------------------------------------- CU-04

    public async Task<Resultado> EditarBuscadorAsync(
        Guid usuarioId, EditarPerfilBuscadorCmd cmd, CancellationToken ct = default)
    {
        // Invariante de CL-04, comprobada antes de tocar nada: 0 km no devuelve a nadie
        // y 200 km deja de ser "cerca".
        if (cmd.RadioPreferidoKm < RadioMinimoKm || cmd.RadioPreferidoKm > RadioMaximoKm)
            return Resultado.Falla(Fallas.Validacion,
                $"El radio tiene que estar entre {RadioMinimoKm} y {RadioMaximoKm} km.");

        var perfil = await _perfiles.BuscadorDeUsuarioAsync(usuarioId, ct);
        if (perfil is null)
            return Resultado.Falla(Fallas.NoEncontrado, "Esta cuenta no tiene perfil de buscador.");

        perfil.ZonaInteresId = cmd.ZonaInteresId;
        perfil.RadioPreferidoKm = cmd.RadioPreferidoKm;
        perfil.AceptaAvisos = cmd.AceptaAvisos;
        perfil.CanalCorreo = cmd.CanalCorreo;
        perfil.CanalPush = cmd.CanalPush;

        await _unidad.GuardarAsync(ct);
        return Resultado.Ok();
    }

    // ------------------------------------------------------------ auxiliares

    /// <summary>RN-S1-05, en palabras que la persona pueda accionar.</summary>
    private static string[] Falta(PerfilPrestador perfil)
    {
        var pendientes = new List<string>();
        if (string.IsNullOrWhiteSpace(perfil.NombrePublico)) pendientes.Add("el nombre publico");
        if (perfil.RubroId is null or <= 0) pendientes.Add("el rubro");
        if (!perfil.Servicios.Any(s => s.Activo)) pendientes.Add("al menos un servicio");
        if (perfil.Cobertura.Count == 0) pendientes.Add("al menos una zona de cobertura");
        return pendientes.ToArray();
    }

    private static MiPerfilPrestadorDto Describir(PerfilPrestador perfil) => new(
        perfil.Id,
        perfil.Slug,
        perfil.NombrePublico,
        perfil.Descripcion,
        perfil.RubroId,
        perfil.Estado,
        perfil.ZonaPrincipal()?.Zona?.Nombre,
        perfil.DireccionCifrada is not null,
        perfil.TelefonoPublico,
        perfil.CorreoPublico,
        perfil.Whatsapp,
        perfil.SitioWeb,
        perfil.TotalResenas,
        perfil.PromedioVisible() ? perfil.PromedioCalificacion : null,
        perfil.Version,
        perfil.PublicadoEn,
        Falta(perfil));
}
