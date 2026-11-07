using Zass.Aplicacion.Puertos;

namespace Zass.Aplicacion.Pruebas.Dobles;

/// <summary>
/// MOD-05, y este doble es TONTO a proposito.
///
/// No filtra, no ordena y no sabe de zonas: devuelve las filas que la prueba le dejo y
/// anota con que criterios lo llamaron. Es deliberado. Reimplementar aqui los filtros
/// duros y el orden seria escribir una segunda version del SQL en C#, y entonces las
/// pruebas pasarian comprobando mi imitacion de PostgreSQL en vez de la consulta que corre
/// en produccion. Esa parte se prueba en integracion, contra la base.
///
/// Lo que SI se puede probar sin base de datos es todo lo que decide el servicio: que el
/// rubro exista, que el radio se corrija, que el punto se resuelva a zona, que el cursor
/// vaya y vuelva entero, que la distancia salga redondeada y que el promedio respete
/// RN-S5-11.
/// </summary>
public sealed class RepositorioBusquedaFalso : IRepositorioBusqueda
{
    /// <summary>Lo que devolvera la proxima consulta, en el orden en que esta.</summary>
    public List<FilaDeBusqueda> Filas { get; } = new();

    public CriteriosDeConsulta? UltimosCriterios { get; private set; }
    public int? UltimoRadioMetros { get; private set; }

    /// <summary>La posicion que el servicio le paso al repositorio, o nulo en la primera pagina.</summary>
    public PosicionDeBusqueda? UltimaPosicion { get; private set; }

    public int Llamadas { get; private set; }

    /// <summary>Lo que devolvera ResumenAsync, y con que argumentos lo llamaron.</summary>
    public ResumenCrudo Resumen { get; set; } = new(0, Array.Empty<ConteoCrudo>(), Array.Empty<ConteoCrudo>(), Array.Empty<RecienLlegadoFila>());
    public DateTime? ResumenAhora { get; private set; }
    public int? ResumenRecientes { get; private set; }
    public bool PidioPorDistancia { get; private set; }

    public Task<IReadOnlyList<FilaDeBusqueda>> PorDistanciaAsync(
        CriteriosDeConsulta criterios, double latitud, double longitud, int radioMetros,
        PosicionDeBusqueda? desde, CancellationToken ct = default)
    {
        Llamadas++;
        PidioPorDistancia = true;
        UltimosCriterios = criterios;
        UltimoRadioMetros = radioMetros;
        UltimaPosicion = desde;

        IReadOnlyList<FilaDeBusqueda> r = Filas.Take(criterios.Limite).ToList();
        return Task.FromResult(r);
    }

    public Task<ResumenCrudo> ResumenAsync(DateTime ahora, int recientes, CancellationToken ct = default)
    {
        ResumenAhora = ahora;
        ResumenRecientes = recientes;
        return Task.FromResult(Resumen);
    }

    /// <summary>Lo que devolvera PorTextoAsync, y con que criterios lo llamaron.</summary>
    public List<FilaDeTexto> FilasDeTexto { get; } = new();
    public CriteriosDeTexto? UltimosCriteriosDeTexto { get; private set; }

    public Task<IReadOnlyList<FilaDeTexto>> PorTextoAsync(CriteriosDeTexto criterios, CancellationToken ct = default)
    {
        UltimosCriteriosDeTexto = criterios;
        IReadOnlyList<FilaDeTexto> r = FilasDeTexto.Skip(criterios.Desplazamiento).Take(criterios.Limite).ToList();
        return Task.FromResult(r);
    }

    public Task<IReadOnlyList<FilaDeBusqueda>> PorZonaAsync(
        CriteriosDeConsulta criterios, PosicionDeBusqueda? desde,
        CancellationToken ct = default)
    {
        Llamadas++;
        PidioPorDistancia = false;
        UltimosCriterios = criterios;
        UltimaPosicion = desde;

        IReadOnlyList<FilaDeBusqueda> r = Filas.Take(criterios.Limite).ToList();
        return Task.FromResult(r);
    }
}
