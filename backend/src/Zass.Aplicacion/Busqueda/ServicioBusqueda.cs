using System.Globalization;
using System.Text;
using Zass.Aplicacion.Catalogo;
using Zass.Aplicacion.Credenciales;
using Zass.Aplicacion.Geografia;
using Zass.Aplicacion.Identidad;
using Zass.Aplicacion.Puertos;
using Zass.Dominio.Comun;

namespace Zass.Aplicacion.Busqueda;

/// <summary>
/// MOD-05 - Busqueda (CU-08). El caso mas ejecutado del sistema.
///
/// Consume MOD-03 —para rechazar un rubro inexistente— y MOD-04 —para resolver el punto a
/// zona y para nombrarla—, que es lo que la tabla de modulos permite. No tiene tablas
/// propias y no debe tenerlas.
/// </summary>
public sealed class ServicioBusqueda : IServicioBusqueda
{
    private readonly IRepositorioBusqueda _repo;
    private readonly IServicioCatalogo _catalogo;
    private readonly IServicioGeografia _geografia;
    private readonly IServicioIdentidad _identidad;
    private readonly IServicioCredenciales _credenciales;
    private readonly IRelojSistema _reloj;

    public ServicioBusqueda(
        IRepositorioBusqueda repo, IServicioCatalogo catalogo,
        IServicioGeografia geografia, IServicioIdentidad identidad,
        IServicioCredenciales credenciales, IRelojSistema reloj)
    {
        _credenciales = credenciales;
        _repo = repo;
        _catalogo = catalogo;
        _geografia = geografia;
        _identidad = identidad;
        _reloj = reloj;
    }

    public async Task<Resultado<PaginaDeBusqueda>> BuscarAsync(
        CriteriosDeBusqueda criterios, CancellationToken ct = default)
    {
        // E-422. El rubro es el filtro duro del paso 2: sin el, la busqueda no significa
        // nada, asi que un rubro inexistente es un error y no una pagina vacia.
        if (!await _catalogo.ExisteRubroActivoAsync(criterios.RubroId, ct))
            return Resultado.Falla<PaginaDeBusqueda>(Fallas.Validacion, "Ese rubro no existe.");

        var avisos = new List<string>();

        // E-400 de CU-08: el radio fuera de rango se CORRIGE al valor valido mas cercano
        // y se informa, en vez de rechazar la busqueda. Quien pide 500 km quiere buscar
        // lejos, no leer un error.
        var radioKm = criterios.RadioKm;
        if (radioKm < LimitesDeBusqueda.RadioMinimoKm)
        {
            radioKm = LimitesDeBusqueda.RadioMinimoKm;
            avisos.Add($"El radio minimo es {LimitesDeBusqueda.RadioMinimoKm} km.");
        }
        else if (radioKm > LimitesDeBusqueda.RadioMaximoKm)
        {
            radioKm = LimitesDeBusqueda.RadioMaximoKm;
            avisos.Add($"El radio maximo es {LimitesDeBusqueda.RadioMaximoKm} km.");
        }

        // Donde se busca. El punto manda sobre la zona: si vienen los dos, el punto es
        // mas preciso y la zona se deduce de el.
        ZonaDto? zona;
        if (criterios.TienePunto)
        {
            var resuelta = await _geografia.ResolverZonaAsync(
                new Punto(criterios.Latitud!.Value, criterios.Longitud!.Value), ct);

            // Fuera de toda zona conocida no es un error: es que todavia no llegamos ahi.
            // Devolver una pagina vacia con el motivo deja que el cliente ofrezca elegir
            // otra zona (flujo 4B de CU-08) en vez de mostrar una pantalla de error.
            if (!resuelta.Exito)
                return Resultado.Ok(Vacia(
                    ordenadoPorDistancia: false, zonaBuscada: null,
                    aviso: resuelta.Mensaje));

            zona = resuelta.Valor;
        }
        else if (criterios.ZonaId is > 0)
        {
            var pedida = await _geografia.ZonaPorIdAsync(criterios.ZonaId.Value, ct);
            if (!pedida.Exito)
                return Resultado.Falla<PaginaDeBusqueda>(pedida.Codigo!, pedida.Mensaje!);
            zona = pedida.Valor;
        }
        else
        {
            return Resultado.Falla<PaginaDeBusqueda>(Fallas.Validacion,
                "Dinos donde buscar: comparte tu ubicacion o elige una zona.");
        }

        var consulta = new CriteriosDeConsulta(
            criterios.RubroId,
            zona!.Id,
            criterios.ServicioId,
            criterios.SoloVerificados,
            criterios.CalificacionMinima,
            _reloj.Ahora,
            // Una mas que la pagina: es como se sabe si hay siguiente sin contar el total,
            // que con cursor seria otra consulta y sobre toda la tabla.
            LimitesDeBusqueda.PorPagina + 1);

        if (criterios.TienePunto)
        {
            var cursor = LeerCursor(criterios.Cursor, porDistancia: true);
            if (!cursor.Exito)
                return Resultado.Falla<PaginaDeBusqueda>(cursor.Codigo!, cursor.Mensaje!);

            var filas = await _repo.PorDistanciaAsync(
                consulta, criterios.Latitud!.Value, criterios.Longitud!.Value,
                radioKm * 1000, cursor.Valor, ct);

            return Resultado.Ok(Armar(
                filas, criterios, zona, ordenadoPorDistancia: true, avisos));
        }
        else
        {
            var cursor = LeerCursor(criterios.Cursor, porDistancia: false);
            if (!cursor.Exito)
                return Resultado.Falla<PaginaDeBusqueda>(cursor.Codigo!, cursor.Mensaje!);

            var filas = await _repo.PorZonaAsync(consulta, cursor.Valor, ct);

            // RN-S3-07 y flujo 4C de CU-08: sin punto no hay orden por distancia, y la
            // respuesta tiene que decirlo. El cliente muestra "Mostramos por zona".
            avisos.Add("No ordenamos por distancia porque no sabemos desde donde buscas. "
                       + $"Mostramos los de {zona.Nombre}.");

            return Resultado.Ok(Armar(
                filas, criterios, zona, ordenadoPorDistancia: false, avisos));
        }
    }

    // ------------------------------------------------------------- API-63, la barra de busqueda

    public const int PorPaginaDeTexto = 24;
    public const int PaginaMaximaDeTexto = 50;

    /// <summary>
    /// La Plaza de Armas de Temuco. Sin ubicacion de la persona se mide desde aqui, solo para que
    /// «mas cerca» y los empates tengan sentido; esa distancia NO se muestra, porque no es la de nadie.
    /// </summary>
    public static readonly Punto CentroDeTemuco = new(-38.7359, -72.5904);

    private static string CodigoDeOrden(OrdenDeTexto orden) => orden switch
    {
        OrdenDeTexto.Cercania => "cercania",
        OrdenDeTexto.PrecioAsc => "precio_asc",
        OrdenDeTexto.PrecioDesc => "precio_desc",
        _ => "recomendados",
    };

    public async Task<Resultado<PaginaDeTexto>> BuscarPorTextoAsync(
        CriteriosDeTextoDeBusqueda criterios, CancellationToken ct = default)
    {
        // El punto viene entero o no viene: media coordenada es un dato roto, y adivinar la otra
        // mitad seria buscar desde un lugar donde no esta nadie.
        var conLat = criterios.Latitud is not null;
        var conLon = criterios.Longitud is not null;
        if (conLat != conLon)
            return Resultado.Falla<PaginaDeTexto>(Fallas.Validacion,
                "La ubicacion necesita latitud y longitud, las dos.");

        var conUbicacion = conLat && conLon;
        if (conUbicacion && !(criterios.Latitud is >= -90 and <= 90 && criterios.Longitud is >= -180 and <= 180))
            return Resultado.Falla<PaginaDeTexto>(Fallas.Validacion, "Esa ubicacion no es valida.");

        if (!Enum.IsDefined(criterios.Orden))
            return Resultado.Falla<PaginaDeTexto>(Fallas.Validacion, "Ese orden no existe.");

        int? zonaId = null;
        if (criterios.ZonaId is not null)
        {
            var zona = await _geografia.ZonaPorIdAsync(criterios.ZonaId.Value, ct);
            if (!zona.Exito)
                return Resultado.Falla<PaginaDeTexto>(zona.Codigo!, zona.Mensaje!);
            zonaId = zona.Valor!.Id;
        }

        // Ordenar por cercania sin saber donde esta la persona no tiene sentido. No es un error:
        // se ordena por recomendados y se dice, para que la pantalla pueda ofrecer compartir la ubicacion.
        var orden = criterios.Orden;
        string? aviso = null;
        if (orden == OrdenDeTexto.Cercania && !conUbicacion)
        {
            orden = OrdenDeTexto.Recomendados;
            aviso = "Para ordenar por cercania necesitamos tu ubicacion.";
        }

        var pagina = Math.Clamp(criterios.Pagina, 1, PaginaMaximaDeTexto);
        var (formas, grupos) = TextoDeBusqueda.Grupos(criterios.Texto);
        var origen = conUbicacion ? new Punto(criterios.Latitud!.Value, criterios.Longitud!.Value) : CentroDeTemuco;

        var filas = await _repo.PorTextoAsync(new CriteriosDeTexto(
            formas, grupos, origen.Latitud, origen.Longitud, zonaId, CodigoDeOrden(orden),
            _reloj.Ahora, PorPaginaDeTexto + 1, (pagina - 1) * PorPaginaDeTexto), ct);

        var hayMas = filas.Count > PorPaginaDeTexto;
        var visibles = hayMas ? filas.Take(PorPaginaDeTexto).ToList() : filas.ToList();
        var total = filas.Count == 0 ? 0 : (int)Math.Min(int.MaxValue, filas[0].TotalFilas);

        var resultados = visibles.Select(f => new ResultadoDeTexto(
            f.Id, f.Slug, f.NombrePublico, f.Descripcion, f.UrlFoto,
            f.RubroNombre, f.ZonaPrincipal,
            // RN-S3-03: solo con la ubicacion REAL de la persona hay una distancia que decir, y
            // siempre redondeada. Los metros se quedan aqui.
            conUbicacion && f.Metros is not null ? Geo.DistanciaPublica(f.Metros.Value) : null,
            f.TieneVerificada,
            // RN-S5-11: con menos de 3 resenas el promedio no es publico.
            f.TotalResenas >= 3 ? f.PromedioCalificacion : null,
            f.TotalResenas,
            f.PrecioDesde)).ToList();

        return Resultado.Ok(new PaginaDeTexto(resultados, total, pagina, hayMas, orden, conUbicacion, aviso));
    }

    // ------------------------------------------------------------- API-62, la comunidad

    /// <summary>Cuantas tarjetas de "recien llegados" se muestran. Una portada, no un listado.</summary>
    public const int RecienLlegadosMaximos = 6;

    public async Task<ResumenDeComunidadDto> ResumenDeComunidadAsync(CancellationToken ct = default)
    {
        var crudo = await _repo.ResumenAsync(_reloj.Ahora, RecienLlegadosMaximos, ct);

        return new ResumenDeComunidadDto(
            crudo.Total,
            crudo.PorZona.Select(z => new PersonasPorZonaDto(z.Id, z.Personas)).ToList(),
            crudo.PorRubro.Select(r => new PersonasPorRubroDto((short)r.Id, r.Personas)).ToList(),
            crudo.Recientes.Select(r => new RecienLlegadoDto(
                r.Slug, r.NombrePublico, r.RubroNombre, r.ZonaPrincipal, r.Verificada)).ToList());
    }

    // ------------------------------------------------------------- CU-09, la ficha

    public async Task<FichaPublicaDto?> FichaPublicaAsync(string slug, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(slug)) return null;

        // MOD-01 decide si existe y esta publicada, y devuelve nulo para las dos cosas.
        var perfil = await _identidad.PerfilPublicoPorSlugAsync(slug.Trim(), ct);
        if (perfil is null) return null;

        // Una consulta tras otra, NO en paralelo: los tres modulos comparten el DbContext de la
        // peticion, y un DbContext no admite dos operaciones a la vez.
        var rubro = perfil.RubroId is > 0
            ? await _catalogo.RubroPorIdAsync(perfil.RubroId.Value, ct)
            : null;
        var servicios = await _catalogo.ServiciosPublicosDePrestadorAsync(perfil.Id, ct);
        var zonas = await _geografia.ZonasDeCoberturaAsync(perfil.Id, ct);
        var credenciales = await _credenciales.CredencialesPublicasAsync(perfil.Id, ct);

        return new FichaPublicaDto(
            perfil.Id,
            perfil.Slug,
            perfil.NombrePublico,
            perfil.Descripcion,
            perfil.UrlFoto,
            rubro,
            // MOD-04 ya las entrega con la principal primero (RN-S3-08).
            zonas.Select(z => new ZonaPublicaDto(z.Nombre, z.Sector, z.EsPrincipal)).ToList(),
            servicios,
            credenciales,
            perfil.PromedioCalificacion,      // MOD-01 ya aplico RN-S5-11
            perfil.TotalResenas,
            new ContactoPublicoDto(
                perfil.TelefonoPublico, perfil.Whatsapp, perfil.CorreoPublico, perfil.SitioWeb),
            perfil.PublicadoEn);
    }

    // ------------------------------------------------------------- armado

    private PaginaDeBusqueda Armar(
        IReadOnlyList<FilaDeBusqueda> filas, CriteriosDeBusqueda criterios,
        ZonaDto zona, bool ordenadoPorDistancia, List<string> avisos)
    {
        var hayMas = filas.Count > LimitesDeBusqueda.PorPagina;
        var pagina = hayMas ? filas.Take(LimitesDeBusqueda.PorPagina).ToList() : filas;

        var resultados = pagina.Select(f => new ResultadoDeBusqueda(
            f.Id,
            f.Slug,
            f.NombrePublico,
            f.Descripcion,
            f.UrlFoto,
            f.RubroId,
            f.ZonaPrincipal,
            // RN-S3-03. Aqui es donde los metros exactos se quedan: lo que sigue viaje es
            // texto redondeado, y el DTO publico no tiene donde guardar un numero.
            ordenadoPorDistancia && f.Metros is not null
                ? Geo.DistanciaPublica(f.Metros.Value)
                : null,
            f.TieneVerificada,
            // RN-S5-11. Con menos de 3 resenas se muestra el recuento, no el promedio. El
            // promedio de la fila es el guardado, sin tocar: lo que ordena es OTRO campo
            // (ReputacionOrden), y asi un -1 de ordenar nunca puede llegar al cliente.
            f.TotalResenas >= 3 ? f.PromedioCalificacion : null,
            f.TotalResenas,
            criterios.QuienBuscaPerfilId is not null && f.Id == criterios.QuienBuscaPerfilId)).ToList();

        var siguiente = hayMas && pagina.Count > 0
            ? EscribirCursor(pagina[^1], ordenadoPorDistancia)
            : null;

        return new PaginaDeBusqueda(
            resultados, siguiente, ordenadoPorDistancia, zona.Nombre,
            avisos.Count == 0 ? null : string.Join(" ", avisos));
    }

    private static PaginaDeBusqueda Vacia(
        bool ordenadoPorDistancia, string? zonaBuscada, string? aviso) =>
        new(Array.Empty<ResultadoDeBusqueda>(), null, ordenadoPorDistancia, zonaBuscada, aviso);

    // ------------------------------------------------------------- cursor
    //
    // El cursor lleva la POSICION de la ultima fila servida —la misma tupla por la que se
    // ordena—, no un numero de pagina: con OFFSET, la pagina 50 obliga al motor a contar
    // y descartar 1.000 filas en cada peticion (CU-08, seccion 7). Si llevara menos que la
    // tupla entera, un empate haria que la pagina siguiente repitiera o se saltara a
    // alguien. Va en Base64 para que nadie lo lea como una invitacion a fabricarlo a mano,
    // no por seguridad: lo que contiene es publico y de todas formas se valida.
    //
    //   por distancia:  d|tramo|verificado|reputacion|resenas|id
    //   por zona:       r|verificado|reputacion|resenas|id

    private static string EscribirCursor(FilaDeBusqueda ultima, bool porDistancia)
    {
        var verificada = ultima.TieneVerificada ? 1 : 0;
        var texto = porDistancia
            ? string.Create(CultureInfo.InvariantCulture,
                $"d|{ultima.Tramo ?? 0}|{verificada}|{ultima.ReputacionOrden:0.###}|{ultima.TotalResenas}|{ultima.Id}")
            : string.Create(CultureInfo.InvariantCulture,
                $"r|{verificada}|{ultima.ReputacionOrden:0.###}|{ultima.TotalResenas}|{ultima.Id}");

        return Convert.ToBase64String(Encoding.UTF8.GetBytes(texto));
    }

    /// <summary>Nulo (con exito) es "primera pagina". Un cursor roto es entrada invalida.</summary>
    private static Resultado<PosicionDeBusqueda?> LeerCursor(string? cursor, bool porDistancia)
    {
        if (string.IsNullOrWhiteSpace(cursor)) return Resultado.Ok<PosicionDeBusqueda?>(null);

        var invalido = Resultado.Falla<PosicionDeBusqueda?>(
            Fallas.Validacion, "Ese cursor no es valido. Vuelve a buscar.");

        var partes = Partes(cursor);
        var letra = porDistancia ? "d" : "r";
        var esperadas = porDistancia ? 6 : 5;

        // Un cursor de una busqueda por distancia no sirve en una por zona, y al reves: el
        // orden es otro y la fila a la que apunta no significa lo mismo. Aceptarlo
        // devolveria una pagina que se salta resultados sin que nadie lo note.
        if (partes is null || partes.Length != esperadas || partes[0] != letra) return invalido;

        var i = 1;
        int? tramo = null;
        if (porDistancia)
        {
            if (!int.TryParse(partes[i++], NumberStyles.Integer, CultureInfo.InvariantCulture, out var t))
                return invalido;
            tramo = t;
        }

        if (!int.TryParse(partes[i++], NumberStyles.Integer, CultureInfo.InvariantCulture, out var verificada)
            || verificada is not (0 or 1)
            || !decimal.TryParse(partes[i++], NumberStyles.Float, CultureInfo.InvariantCulture, out var reputacion)
            || !int.TryParse(partes[i++], NumberStyles.Integer, CultureInfo.InvariantCulture, out var resenas)
            || !Guid.TryParse(partes[i], out var id))
            return invalido;

        return Resultado.Ok<PosicionDeBusqueda?>(
            new PosicionDeBusqueda(tramo, verificada == 1, reputacion, resenas, id));
    }

    private static string[]? Partes(string cursor)
    {
        try
        {
            return Encoding.UTF8.GetString(Convert.FromBase64String(cursor)).Split('|');
        }
        catch (FormatException)
        {
            // Un cursor que no es Base64 llega de una URL manipulada o recortada. No es
            // una excepcion del programa: es entrada invalida, y se trata como tal.
            return null;
        }
    }
}
