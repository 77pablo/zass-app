using System.Globalization;
using System.Text;
using Zass.Aplicacion.Catalogo;
using Zass.Aplicacion.Geografia;
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
    private readonly IRelojSistema _reloj;

    public ServicioBusqueda(
        IRepositorioBusqueda repo, IServicioCatalogo catalogo,
        IServicioGeografia geografia, IRelojSistema reloj)
    {
        _repo = repo;
        _catalogo = catalogo;
        _geografia = geografia;
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
            var cursor = LeerCursorDeDistancia(criterios.Cursor);
            if (!cursor.Exito)
                return Resultado.Falla<PaginaDeBusqueda>(cursor.Codigo!, cursor.Mensaje!);

            var filas = await _repo.PorDistanciaAsync(
                consulta, criterios.Latitud!.Value, criterios.Longitud!.Value,
                radioKm * 1000, cursor.Valor.Metros, cursor.Valor.Id, ct);

            return Resultado.Ok(Armar(
                filas, criterios, zona, ordenadoPorDistancia: true, avisos));
        }
        else
        {
            var cursor = LeerCursorDeReputacion(criterios.Cursor);
            if (!cursor.Exito)
                return Resultado.Falla<PaginaDeBusqueda>(cursor.Codigo!, cursor.Mensaje!);

            var filas = await _repo.PorZonaAsync(
                consulta, cursor.Valor.Promedio, cursor.Valor.Resenas, cursor.Valor.Id, ct);

            // RN-S3-07 y flujo 4C de CU-08: sin punto no hay orden por distancia, y la
            // respuesta tiene que decirlo. El cliente muestra "Mostramos por zona".
            avisos.Add("No ordenamos por distancia porque no sabemos desde donde buscas. "
                       + $"Mostramos los de {zona.Nombre}.");

            return Resultado.Ok(Armar(
                filas, criterios, zona, ordenadoPorDistancia: false, avisos));
        }
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
            // RN-S5-11. Con menos de 3 resenas se muestra el recuento, no el promedio. La
            // consulta por zona guarda -1 en vez de nulo para poder ordenar, asi que eso
            // tambien se traduce aqui y no se escapa al cliente.
            f.TotalResenas >= 3 && f.PromedioCalificacion is > 0 ? f.PromedioCalificacion : null,
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
    // El cursor lleva la ultima fila servida, no un numero de pagina: con OFFSET, la
    // pagina 50 obliga al motor a contar y descartar 1.000 filas en cada peticion
    // (CU-08, seccion 7). Va en Base64 para que nadie lo lea como una invitacion a
    // fabricarlo a mano, no por seguridad: lo que contiene es publico y de todas formas
    // se valida.

    private static string EscribirCursor(FilaDeBusqueda ultima, bool porDistancia)
    {
        var texto = porDistancia
            ? string.Create(CultureInfo.InvariantCulture,
                $"d|{ultima.Metros ?? 0:0.###}|{ultima.Id}")
            : string.Create(CultureInfo.InvariantCulture,
                $"r|{ultima.PromedioCalificacion ?? -1:0.###}|{ultima.TotalResenas}|{ultima.Id}");

        return Convert.ToBase64String(Encoding.UTF8.GetBytes(texto));
    }

    private static Resultado<(double? Metros, Guid? Id)> LeerCursorDeDistancia(string? cursor)
    {
        if (string.IsNullOrWhiteSpace(cursor)) return Resultado.Ok<(double?, Guid?)>((null, null));

        var partes = Partes(cursor);
        if (partes is null || partes.Length != 3 || partes[0] != "d"
            || !double.TryParse(partes[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var metros)
            || !Guid.TryParse(partes[2], out var id))
            return Resultado.Falla<(double?, Guid?)>(Fallas.Validacion,
                "Ese cursor no es valido. Vuelve a buscar.");

        return Resultado.Ok<(double?, Guid?)>((metros, id));
    }

    private static Resultado<(decimal? Promedio, int? Resenas, Guid? Id)> LeerCursorDeReputacion(
        string? cursor)
    {
        if (string.IsNullOrWhiteSpace(cursor))
            return Resultado.Ok<(decimal?, int?, Guid?)>((null, null, null));

        var partes = Partes(cursor);
        if (partes is null || partes.Length != 4 || partes[0] != "r"
            || !decimal.TryParse(partes[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var promedio)
            || !int.TryParse(partes[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var resenas)
            || !Guid.TryParse(partes[3], out var id))
            return Resultado.Falla<(decimal?, int?, Guid?)>(Fallas.Validacion,
                "Ese cursor no es valido. Vuelve a buscar.");

        return Resultado.Ok<(decimal?, int?, Guid?)>((promedio, resenas, id));
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
