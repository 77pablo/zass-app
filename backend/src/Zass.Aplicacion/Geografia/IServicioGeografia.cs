using Zass.Dominio.Comun;

namespace Zass.Aplicacion.Geografia;

/// <summary>
/// MOD-04 - S3. Traduce entre puntos y zonas, y calcula distancias (TBL-10, TBL-11).
///
/// Dos decisiones tomadas el 7 de noviembre de 2026 y que hay que conocer para leer esto:
///
/// 1. <b>La zona de un punto es la del centroide mas cercano</b>, no la del poligono que
///    lo contiene. TBL-10 declara una columna `limite` con el poligono del barrio, pero
///    no existe en la base y no hay poligonos reales de los barrios de Temuco. Con un
///    punto por zona la regla es exacta en el centro y aproximada en los bordes, que es
///    mejor que no tener zonas.
/// 2. <b>El geocodificador todavia no existe.</b> El proveedor (N-08) no esta elegido, asi
///    que <see cref="GeocodificarAsync"/> contesta que no esta disponible y eso dispara el
///    flujo 4B de CU-03: el prestador elige su zona de una lista. Esta escrito como puerto
///    para que el dia que haya proveedor no cambie nada mas.
/// </summary>
public interface IServicioGeografia
{
    /// <summary>API-04. Barrios y sectores, con filtro opcional por sector.</summary>
    Task<IReadOnlyList<ZonaDto>> ZonasAsync(string? sector = null, CancellationToken ct = default);

    /// <summary>
    /// CS-10. La zona de unas coordenadas: la del centroide mas cercano.
    ///
    /// Falla si el punto no es valido o si queda a mas de
    /// <see cref="ServicioGeografia.MaxKmDesdeElCentro"/> km de toda zona conocida. Eso
    /// ultimo no es un detalle: sin tope, un punto en Arica resolveria a un barrio de
    /// Temuco y el prestador apareceria como si atendiera ahi.
    /// </summary>
    Task<Resultado<ZonaDto>> ResolverZonaAsync(Punto punto, CancellationToken ct = default);

    /// <summary>
    /// CS-10, la parte que necesita a N-08. Hoy siempre falla, y falla a proposito:
    /// quien llama tiene que estar preparado para el flujo 4B desde el primer dia.
    /// </summary>
    Task<Resultado<ZonaDto>> ZonaDeDireccionAsync(string direccion, CancellationToken ct = default);

    Task<IReadOnlyList<ZonaCubiertaDto>> ZonasDeCoberturaAsync(
        Guid perfilPrestadorId, CancellationToken ct = default);

    /// <summary>
    /// API-21. Reemplaza las zonas que cubre un prestador (CU-10).
    ///
    /// Exige exactamente una zona principal (RN-S3-08, R-2). Ni cero —la ficha no
    /// tendria que mostrar— ni dos, que es lo que el indice unico parcial
    /// ux_cobertura_principal rechazaria con un 23505 convertido en 500.
    /// </summary>
    Task<Resultado> ReemplazarCoberturaAsync(
        ReemplazarCoberturaCmd cmd, CancellationToken ct = default);

    /// <summary>
    /// Metros, exacta, y de uso INTERNO: ordena resultados de busqueda. No puede salir en
    /// una respuesta publica (RN-S3-02): con la distancia exacta a tres prestadores se
    /// trilatera donde esta cada uno.
    /// </summary>
    double DistanciaEnMetros(Punto a, Punto b);

    /// <summary>RN-S3-03. Texto redondeado, y lo unico que puede viajar al cliente.</summary>
    string DistanciaPublica(Punto a, Punto b);
}

/// <summary>
/// Una zona como la ve el cliente. Lleva el centroide a proposito —es dato publico, es el
/// centro de un barrio, no la casa de nadie— porque el mapa de la busqueda necesita algo
/// que dibujar cuando el buscador no da su posicion (RN-S3-07).
/// </summary>
public sealed record ZonaDto(
    int Id,
    string Nombre,
    string Sector,
    string Comuna,
    double CentroLatitud,
    double CentroLongitud);

/// <summary>Una zona cubierta, con lo que distingue a la principal.</summary>
public sealed record ZonaCubiertaDto(
    int ZonaId,
    string Nombre,
    string Sector,
    bool EsPrincipal,
    DateTime DesdeEn);

public sealed record ZonaCubiertaCmd(int ZonaId, bool EsPrincipal);

/// <summary>API-21. La lista completa de zonas cubiertas, no un parche.</summary>
public sealed record ReemplazarCoberturaCmd(
    Guid PerfilPrestadorId,
    IReadOnlyList<ZonaCubiertaCmd> Zonas);
