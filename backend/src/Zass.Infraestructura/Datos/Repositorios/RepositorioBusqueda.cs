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
/// prueba de integracion que compara las dos puntas: lo que esta consulta deja pasar es
/// exactamente lo que `IServicioSuscripciones.EstaVigenteAsync` llamaria vigente.
/// </summary>
public sealed class RepositorioBusquedaEfCore : IRepositorioBusqueda
{
    private readonly ZassDbContext _ctx;
    public RepositorioBusquedaEfCore(ZassDbContext ctx) => _ctx = ctx;

    public async Task<IReadOnlyList<FilaDeBusqueda>> PorDistanciaAsync(
        CriteriosDeConsulta c, double latitud, double longitud, int radioMetros,
        double? desdeMetros, Guid? desdeId, CancellationToken ct = default)
    {
        // El cursor es keyset: (metros, id) mayores que los de la ultima fila servida.
        // Con dos parametros nulos la condicion se vuelve verdadera y sirve de primera
        // pagina, asi que hay una sola consulta y no dos textos que se desincronicen.
        var cursorMetros = desdeMetros ?? -1;
        var cursorId = desdeId ?? Guid.Empty;
        var hayCursor = desdeMetros is not null;

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
                    p.total_resenas                             AS total_resenas,
                    EXISTS (
                        SELECT 1 FROM credenciales cr
                        WHERE cr.perfil_prestador_id = p.id
                          AND cr.estado = 'Verificada'
                    )                                           AS tiene_verificada,
                    ST_Distance(
                        -- Sin punto exacto se usa el centro de su zona principal, que es
                        -- lo que manda el flujo 4B.4 de CU-03 para quien publico sin
                        -- direccion geocodificada. Hoy son todos, porque N-08 no existe.
                        COALESCE(
                            p.ubicacion_exacta,
                            ST_SetSRID(ST_MakePoint(zp.centro_longitud, zp.centro_latitud), 4326)::geography
                        ),
                        (SELECT g FROM punto)
                    )                                           AS metros
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
              AND (NOT {c.SoloVerificados} OR tiene_verificada)
              -- RN-S5-11: el promedio solo es publico con 3 resenas o mas, asi que
              -- filtrar por el fuera de ese caso ocultaria perfiles por un numero que
              -- nadie puede ver.
              AND ({c.CalificacionMinima}::numeric IS NULL
                   OR (total_resenas >= 3
                       AND promedio_calificacion >= {c.CalificacionMinima}))
              AND (NOT {hayCursor}
                   OR metros > {cursorMetros}
                   OR (metros = {cursorMetros} AND id > {cursorId}))
            ORDER BY metros, id
            LIMIT {c.Limite}
            """).AsNoTracking().ToListAsync(ct);
    }

    public async Task<IReadOnlyList<FilaDeBusqueda>> PorZonaAsync(
        CriteriosDeConsulta c, decimal? desdePromedio, int? desdeResenas, Guid? desdeId,
        CancellationToken ct = default)
    {
        // Keyset sobre (promedio DESC, resenas DESC, id ASC). El promedio nulo —quien no
        // tiene resenas todavia— se trata como -1 para que el orden sea total y el cursor
        // no se quede dando vueltas sobre las mismas filas.
        var cursorPromedio = desdePromedio ?? -1m;
        var cursorResenas = desdeResenas ?? -1;
        var cursorId = desdeId ?? Guid.Empty;
        var hayCursor = desdePromedio is not null;

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
                    COALESCE(p.promedio_calificacion, -1)       AS promedio_calificacion,
                    p.total_resenas                             AS total_resenas,
                    EXISTS (
                        SELECT 1 FROM credenciales cr
                        WHERE cr.perfil_prestador_id = p.id
                          AND cr.estado = 'Verificada'
                    )                                           AS tiene_verificada,
                    NULL::double precision                      AS metros
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
            WHERE (NOT {c.SoloVerificados} OR tiene_verificada)
              AND ({c.CalificacionMinima}::numeric IS NULL
                   OR (total_resenas >= 3
                       AND promedio_calificacion >= {c.CalificacionMinima}))
              AND (NOT {hayCursor}
                   OR promedio_calificacion < {cursorPromedio}
                   OR (promedio_calificacion = {cursorPromedio}
                       AND total_resenas < {cursorResenas})
                   OR (promedio_calificacion = {cursorPromedio}
                       AND total_resenas = {cursorResenas}
                       AND id > {cursorId}))
            ORDER BY promedio_calificacion DESC, total_resenas DESC, id
            LIMIT {c.Limite}
            """).AsNoTracking().ToListAsync(ct);
    }
}
