using Microsoft.EntityFrameworkCore;
using Zass.Aplicacion.Puertos;

namespace Zass.Infraestructura.Datos.Repositorios;

/// <summary>
/// MOD-05 contra PostgreSQL. La consulta de CU-08, escrita a mano.
///
/// <b>Por que SQL y no LINQ.</b> `ubicacion_exacta` es una columna `geography` generada por
/// la base y deliberadamente fuera del modelo de EF Core (migracion
/// `UbicacionFueraDelModelo`), asi que ninguna consulta LINQ la ve. Y las funciones de
/// PostGIS que esto necesita —`ST_Distance`, `ST_SetSRID`, `ST_MakePoint`— no tienen
/// traduccion sin el paquete espacial de Npgsql, que `Zass.Dominio` no admite.
///
/// <b>Los cuatro filtros duros del paso 5 de CU-08 van DENTRO del SQL</b>, no sobre el
/// resultado: perfil publicado, rubro, cobertura de la zona y suscripcion vigente. Si el
/// de suscripcion se aplicara despues, una pagina de 20 podria devolver 3, y el cursor
/// quedaria apuntando a un sitio que no existe.
///
/// <b>La duplicacion que esto crea, y como se vigila.</b> La condicion de vigencia
/// —estado en (Prueba, Activa) y fecha futura— vive en C# dentro de MOD-13 y otra vez
/// aqui en SQL. No hay forma de evitarlo sin renunciar al filtro en la base. Hay una
/// prueba de integracion que compara las dos puntas.
///
/// <b>El orden.</b> Verificado primero, luego reputacion PUBLICA, luego numero de resenas,
/// luego el identificador. Y por distancia, el TRAMO que se muestra y no los metros
/// exactos (ver <see cref="IRepositorioBusqueda"/>). La reputacion que ordena es
/// <c>reputacion_orden</c>, que vale -1 con menos de 3 resenas (RN-S5-11): ordenar por el
/// promedio guardado hacia que un 5,0 de 2 resenas, que la pantalla no muestra, pasara
/// delante de un 4,6 de 12. El mismo criterio esta en las dos consultas y, por la forma
/// en que el cursor es una tupla, tiene que estar identico en el ORDER BY y en el cursor.
/// </summary>
public sealed class RepositorioBusquedaEfCore : IRepositorioBusqueda
{
    private readonly ZassDbContext _ctx;
    public RepositorioBusquedaEfCore(ZassDbContext ctx) => _ctx = ctx;

    public async Task<IReadOnlyList<FilaDeBusqueda>> PorDistanciaAsync(
        CriteriosDeConsulta c, double latitud, double longitud, int radioMetros,
        PosicionDeBusqueda? desde, CancellationToken ct = default)
    {
        // Con cursor nulo, las condiciones de abajo se vuelven verdaderas y sirven de
        // primera pagina: hay una sola consulta y no dos textos que se desincronicen.
        var hayCursor = desde is not null;
        var cTramo = desde?.Tramo ?? -1;
        var cVerificada = desde is { Verificada: true } ? 1 : 0;
        var cReputacion = desde?.Reputacion ?? -1m;
        var cResenas = desde?.Resenas ?? -1;
        var cId = desde?.Id ?? Guid.Empty;

        return await _ctx.Set<FilaDeBusqueda>().FromSqlInterpolated($"""
            WITH punto AS (
                SELECT ST_SetSRID(ST_MakePoint({longitud}, {latitud}), 4326)::geography AS g
            ),
            base AS (
                SELECT
                    p.id                                        AS id,
                    p.slug                                      AS slug,
                    p.nombre_publico                            AS nombre_publico,
                    p.descripcion                               AS descripcion,
                    p.url_foto                                  AS url_foto,
                    p.rubro_id                                  AS rubro_id,
                    zp.nombre                                   AS zona_principal,
                    p.promedio_calificacion                     AS promedio_calificacion,
                    CASE WHEN p.total_resenas >= 3
                         THEN COALESCE(p.promedio_calificacion, -1)
                         ELSE -1 END                            AS reputacion_orden,
                    p.total_resenas                             AS total_resenas,
                    EXISTS (
                        SELECT 1 FROM credenciales cr
                        WHERE cr.perfil_prestador_id = p.id
                          AND cr.estado = 'Verificada'
                    )                                           AS tiene_verificada,
                    d.metros                                    AS metros,
                    tramo_de_distancia(d.metros)                AS tramo
                FROM perfiles_prestador p
                -- RN-S3-09. Filtro duro, no penalizacion de orden: sin suscripcion
                -- vigente el perfil DESAPARECE. Es un JOIN y no un EXISTS para que el
                -- planificador pueda usarlo como filtro principal.
                JOIN suscripciones s
                     ON s.perfil_prestador_id = p.id
                    AND s.estado IN ('Prueba', 'Activa')
                    AND s.vigente_hasta > {c.Ahora}
                -- La zona principal, para mostrarla y para el centroide de respaldo.
                LEFT JOIN coberturas cpp
                     ON cpp.perfil_prestador_id = p.id AND cpp.es_principal
                LEFT JOIN zonas zp ON zp.id = cpp.zona_id
                -- Sin punto exacto se mide al centro de su zona principal, que es lo que
                -- manda el flujo 4B.4 de CU-03 para quien publico sin direccion
                -- geocodificada. Hoy son todos, porque N-08 no existe.
                CROSS JOIN LATERAL (
                    SELECT ST_Distance(
                        COALESCE(
                            p.ubicacion_exacta,
                            ST_SetSRID(ST_MakePoint(zp.centro_longitud, zp.centro_latitud), 4326)::geography
                        ),
                        (SELECT g FROM punto)
                    ) AS metros
                ) d
                WHERE p.estado = 'Publicado'
                  AND p.rubro_id = {c.RubroId}
                  -- Cobertura de la zona buscada, tambien del paso 5 de CU-08: el
                  -- prestador tiene que atender AHI, no solo estar cerca.
                  AND EXISTS (
                        SELECT 1 FROM coberturas cz
                        WHERE cz.perfil_prestador_id = p.id
                          AND cz.zona_id = {c.ZonaId}
                  )
                  AND ({c.ServicioId}::int IS NULL OR EXISTS (
                        SELECT 1 FROM prestador_servicios ps
                        WHERE ps.perfil_prestador_id = p.id
                          AND ps.servicio_id = {c.ServicioId}
                          AND ps.activo
                  ))
            )
            SELECT * FROM base
            WHERE metros IS NOT NULL
              AND metros <= {radioMetros}
              AND ({c.SoloVerificados} IS NOT TRUE OR tiene_verificada)
              -- RN-S5-11: el promedio solo es publico con 3 resenas o mas, asi que
              -- filtrar por el fuera de ese caso ocultaria perfiles por un numero que
              -- nadie puede ver.
              AND ({c.CalificacionMinima}::numeric IS NULL
                   OR (total_resenas >= 3
                       AND promedio_calificacion >= {c.CalificacionMinima}))
              -- Keyset sobre (tramo ASC, verificado DESC, reputacion DESC, resenas DESC,
              -- id ASC). Lo que viene DESPUES de la ultima fila servida, comparando la
              -- tupla entera: con las columnas DESC, "despues" significa "menor".
              AND (NOT {hayCursor}
                   OR tramo > {cTramo}
                   OR (tramo = {cTramo} AND (
                         tiene_verificada::int < {cVerificada}
                      OR (tiene_verificada::int = {cVerificada} AND (
                            reputacion_orden < {cReputacion}
                         OR (reputacion_orden = {cReputacion} AND (
                               total_resenas < {cResenas}
                            OR (total_resenas = {cResenas} AND id > {cId}))))))))
            ORDER BY tramo, tiene_verificada DESC, reputacion_orden DESC,
                     total_resenas DESC, id
            LIMIT {c.Limite}
            """).AsNoTracking().ToListAsync(ct);
    }

    /// <summary>
    /// LINQ y no SQL a mano: aqui no hay PostGIS, solo conteos. "Visible" es la misma condicion
    /// que el SQL de la busqueda —publicado y con suscripcion vigente—, y hay una prueba que
    /// compara los conteos con lo que la busqueda devuelve de verdad.
    /// </summary>
    public async Task<ResumenCrudo> ResumenAsync(
        DateTime ahora, int recientes, CancellationToken ct = default)
    {
        var visibles = _ctx.PerfilesPrestador.Where(p =>
            p.Estado == Zass.Dominio.Comun.EstadoPerfil.Publicado
            && _ctx.Suscripciones.Any(s =>
                s.PerfilPrestadorId == p.Id
                && (s.Estado == Zass.Dominio.Comun.EstadoSuscripcion.Prueba
                    || s.Estado == Zass.Dominio.Comun.EstadoSuscripcion.Activa)
                && s.VigenteHasta > ahora));

        var total = await visibles.CountAsync(ct);

        // Una cobertura por (prestador, zona) es unica, asi que contar filas es contar personas.
        var porZona = await _ctx.Coberturas
            .Where(c => visibles.Any(p => p.Id == c.PerfilPrestadorId))
            .GroupBy(c => c.ZonaId)
            .Select(g => new ConteoCrudo(g.Key, g.Count()))
            .ToListAsync(ct);

        var porRubro = await visibles
            .Where(p => p.RubroId != null)
            .GroupBy(p => p.RubroId!.Value)
            .Select(g => new ConteoCrudo(g.Key, g.Count()))
            .ToListAsync(ct);

        // El id desempata cuando dos se publicaron en el mismo instante: sin eso el orden de
        // los empates cambiaria entre una peticion y otra.
        var recientesFilas = await visibles
            .OrderByDescending(p => p.PublicadoEn)
            .ThenBy(p => p.Id)
            .Take(recientes)
            .Select(p => new RecienLlegadoFila(
                p.Slug,
                p.NombrePublico,
                _ctx.Rubros.Where(r => r.Id == p.RubroId).Select(r => r.Nombre).FirstOrDefault(),
                _ctx.Coberturas
                    .Where(c => c.PerfilPrestadorId == p.Id && c.EsPrincipal)
                    .Select(c => c.Zona!.Nombre)
                    .FirstOrDefault(),
                _ctx.Credenciales.Any(cr =>
                    cr.PerfilPrestadorId == p.Id
                    && cr.Estado == Zass.Dominio.Comun.EstadoCredencial.Verificada)))
            .ToListAsync(ct);

        return new ResumenCrudo(total, porZona, porRubro, recientesFilas);
    }

    public async Task<IReadOnlyList<FilaDeBusqueda>> PorZonaAsync(
        CriteriosDeConsulta c, PosicionDeBusqueda? desde, CancellationToken ct = default)
    {
        var hayCursor = desde is not null;
        var cVerificada = desde is { Verificada: true } ? 1 : 0;
        var cReputacion = desde?.Reputacion ?? -1m;
        var cResenas = desde?.Resenas ?? -1;
        var cId = desde?.Id ?? Guid.Empty;

        return await _ctx.Set<FilaDeBusqueda>().FromSqlInterpolated($"""
            WITH base AS (
                SELECT
                    p.id                                        AS id,
                    p.slug                                      AS slug,
                    p.nombre_publico                            AS nombre_publico,
                    p.descripcion                               AS descripcion,
                    p.url_foto                                  AS url_foto,
                    p.rubro_id                                  AS rubro_id,
                    zp.nombre                                   AS zona_principal,
                    p.promedio_calificacion                     AS promedio_calificacion,
                    CASE WHEN p.total_resenas >= 3
                         THEN COALESCE(p.promedio_calificacion, -1)
                         ELSE -1 END                            AS reputacion_orden,
                    p.total_resenas                             AS total_resenas,
                    EXISTS (
                        SELECT 1 FROM credenciales cr
                        WHERE cr.perfil_prestador_id = p.id
                          AND cr.estado = 'Verificada'
                    )                                           AS tiene_verificada,
                    NULL::double precision                      AS metros,
                    NULL::integer                               AS tramo
                FROM perfiles_prestador p
                JOIN suscripciones s
                     ON s.perfil_prestador_id = p.id
                    AND s.estado IN ('Prueba', 'Activa')
                    AND s.vigente_hasta > {c.Ahora}
                LEFT JOIN coberturas cpp
                     ON cpp.perfil_prestador_id = p.id AND cpp.es_principal
                LEFT JOIN zonas zp ON zp.id = cpp.zona_id
                WHERE p.estado = 'Publicado'
                  AND p.rubro_id = {c.RubroId}
                  AND EXISTS (
                        SELECT 1 FROM coberturas cz
                        WHERE cz.perfil_prestador_id = p.id
                          AND cz.zona_id = {c.ZonaId}
                  )
                  AND ({c.ServicioId}::int IS NULL OR EXISTS (
                        SELECT 1 FROM prestador_servicios ps
                        WHERE ps.perfil_prestador_id = p.id
                          AND ps.servicio_id = {c.ServicioId}
                          AND ps.activo
                  ))
            )
            SELECT * FROM base
            WHERE ({c.SoloVerificados} IS NOT TRUE OR tiene_verificada)
              AND ({c.CalificacionMinima}::numeric IS NULL
                   OR (total_resenas >= 3
                       AND promedio_calificacion >= {c.CalificacionMinima}))
              AND (NOT {hayCursor}
                   OR tiene_verificada::int < {cVerificada}
                   OR (tiene_verificada::int = {cVerificada} AND (
                         reputacion_orden < {cReputacion}
                      OR (reputacion_orden = {cReputacion} AND (
                            total_resenas < {cResenas}
                         OR (total_resenas = {cResenas} AND id > {cId}))))))
            ORDER BY tiene_verificada DESC, reputacion_orden DESC, total_resenas DESC, id
            LIMIT {c.Limite}
            """).AsNoTracking().ToListAsync(ct);
    }
}
