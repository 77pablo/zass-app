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

/// <summary>
/// API-23 - Preferencias del buscador (CU-04).
///
/// <see cref="RubrosInteres"/> es opcional y nulo significa «no los toques»: el PUT existia
/// antes que los rubros, y un cliente que no los conoce no debe borrarlos sin querer. Para
/// quitarlos todos se manda la lista vacia.
/// </summary>
public sealed record EditarPerfilBuscadorCmd(
    int? ZonaInteresId,
    short RadioPreferidoKm,
    bool AceptaAvisos,
    bool CanalCorreo,
    bool CanalPush,
    IReadOnlyList<short>? RubrosInteres = null);

/// <summary>
/// API-23b - Las preferencias tal como estan guardadas, mas los identificadores de los
/// favoritos (para pintar el boton de guardar en la busqueda sin pedir la lista entera).
/// </summary>
public sealed record MiPerfilBuscadorDto(
    int? ZonaInteresId,
    short RadioPreferidoKm,
    bool AceptaAvisos,
    bool CanalCorreo,
    bool CanalPush,
    IReadOnlyList<short> RubrosInteres,
    IReadOnlyList<Guid> Favoritos);

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
    string[] FaltaParaPublicar)
{
    /// <summary>JQ-9. La foto, si subió una: una ruta de la propia API (ver ServicioFotoDePerfil).</summary>
    public string? UrlFoto { get; init; }
}

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
                // La carga, no una frase: el cuerpo de un aviso es JSON y SOLO datos (hay un unico
                // formato, y asi `Purgada` sigue queriendo decir lo mismo). El texto que lee la
                // persona lo pone la plantilla. Aqui iba una frase suelta, asi que cuando se
                // escribio la plantilla hubo que dejarla tolerar su ausencia: los avisos encolados
                // antes siguen en la cola y tienen que poder salir.
                Cuerpo = CargaDeAviso.ConCampos(("slug", perfil.Slug)),
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

        var rubros = cmd.RubrosInteres?.Distinct().ToList();
        if (rubros is not null && (rubros.Count > MaxRubrosDeInteres || rubros.Any(r => r <= 0)))
            return Resultado.Falla(Fallas.Validacion,
                $"Puedes elegir hasta {MaxRubrosDeInteres} oficios.");

        var perfil = await BuscadorOCrearAsync(usuarioId, ct);

        perfil.ZonaInteresId = cmd.ZonaInteresId;
        perfil.RadioPreferidoKm = cmd.RadioPreferidoKm;
        perfil.AceptaAvisos = cmd.AceptaAvisos;
        perfil.CanalCorreo = cmd.CanalCorreo;
        perfil.CanalPush = cmd.CanalPush;
        if (rubros is not null) perfil.RubrosInteres = rubros;

        await _unidad.GuardarAsync(ct);
        return Resultado.Ok();
    }

    // ------------------------------------------------------------- CU-04 y CS-08, leer

    /// <summary>
    /// Cuantos oficios de interes caben (CU-04). Diez, por decision de Pablo (22-nov-2026); con los
    /// cinco rubros de la v1 equivale a «todos», y empieza a acotar cuando el catalogo crezca.
    /// </summary>
    public const int MaxRubrosDeInteres = 10;

    // Favoritos (CS-08): SIN tope, por decision de Pablo (22-nov-2026). No hace falta uno para
    // proteger la columna jsonb: solo se guarda un perfil PUBLICADO y nunca dos veces el mismo
    // (GuardarFavoritoAsync), asi que la lista no puede ser mas larga que la cantidad de
    // prestadores publicados. Con miles de identificadores inventados no se llega a nada: cada
    // uno tiene que existir y estar publicado.

    /// <summary>
    /// API-23b. Una cuenta sin perfil de buscador —un prestador que nunca busco— recibe los valores
    /// por omision, no un 404: para quien mira la pantalla no hay diferencia entre «no tengo
    /// preferencias» y «nunca las guarde».
    /// </summary>
    public async Task<MiPerfilBuscadorDto> MiPerfilBuscadorAsync(
        Guid usuarioId, CancellationToken ct = default)
    {
        var perfil = await _perfiles.BuscadorDeUsuarioAsync(usuarioId, ct)
                     ?? new PerfilBuscador { UsuarioId = usuarioId };

        return new MiPerfilBuscadorDto(
            perfil.ZonaInteresId, perfil.RadioPreferidoKm, perfil.AceptaAvisos,
            perfil.CanalCorreo, perfil.CanalPush,
            perfil.RubrosInteres.ToList(), perfil.Favoritos.ToList());
    }

    // ------------------------------------------------------------- CS-08, escribir

    /// <summary>
    /// Guarda un prestador como favorito. Solo uno PUBLICADO: un borrador o uno pausado no
    /// existe para el publico, y el mismo 404 para los dos casos evita que esto sirva para
    /// sondear quien se dio de alta sin publicar (igual que la ficha, API-05).
    ///
    /// Guardar dos veces el mismo no es un error: el boton se puede tocar desde dos pestañas.
    /// </summary>
    public async Task<Resultado> GuardarFavoritoAsync(
        Guid usuarioId, Guid perfilPrestadorId, CancellationToken ct = default)
    {
        var prestador = await _perfiles.PrestadorPorIdAsync(perfilPrestadorId, ct);
        if (prestador is null || prestador.Estado != EstadoPerfil.Publicado)
            return Resultado.Falla(Fallas.NoEncontrado, "Esta ficha no existe, o ya no está publicada.");

        var perfil = await BuscadorOCrearAsync(usuarioId, ct);
        if (perfil.Favoritos.Contains(perfilPrestadorId)) return Resultado.Ok();

        // El mas reciente primero. Una lista NUEVA y no Insert sobre la que ya existe: asi el
        // cambio se nota aunque alguien toque el comparador de la columna jsonb.
        perfil.Favoritos = new[] { perfilPrestadorId }.Concat(perfil.Favoritos).ToList();
        await _unidad.GuardarAsync(ct);
        return Resultado.Ok();
    }

    /// <summary>
    /// Quita un favorito. Quitar uno que no estaba responde igual que quitar uno que si: el
    /// resultado que la persona pidio —que no este— ya se cumple. No mira si el prestador sigue
    /// publicado: uno que se despublico tiene que poder quitarse igual.
    /// </summary>
    public async Task<Resultado> QuitarFavoritoAsync(
        Guid usuarioId, Guid perfilPrestadorId, CancellationToken ct = default)
    {
        var perfil = await _perfiles.BuscadorDeUsuarioAsync(usuarioId, ct);
        if (perfil is null || !perfil.Favoritos.Contains(perfilPrestadorId)) return Resultado.Ok();

        perfil.Favoritos = perfil.Favoritos.Where(id => id != perfilPrestadorId).ToList();
        await _unidad.GuardarAsync(ct);
        return Resultado.Ok();
    }

    /// <summary>
    /// El perfil de buscador de la cuenta, creandolo si no existe. El registro solo lo crea para
    /// quien entra como buscador, pero una misma cuenta puede ser las dos cosas (seccion 3 de S1):
    /// un prestador que guarda a un colega no deberia toparse con «no tienes perfil de buscador».
    /// </summary>
    private async Task<PerfilBuscador> BuscadorOCrearAsync(Guid usuarioId, CancellationToken ct)
    {
        var perfil = await _perfiles.BuscadorDeUsuarioAsync(usuarioId, ct);
        if (perfil is not null) return perfil;

        perfil = new PerfilBuscador { UsuarioId = usuarioId };
        _perfiles.Agregar(perfil);
        return perfil;
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
        Falta(perfil))
    {
        UrlFoto = perfil.UrlFoto,
    };
}
