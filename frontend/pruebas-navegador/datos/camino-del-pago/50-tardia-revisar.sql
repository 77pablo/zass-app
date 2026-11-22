\set ON_ERROR_STOP on
-- JQ-7 · Bloque TARDIA-REVISAR. Solo lee. Se corre al final de la fase `tardia-seguir`, después
-- de que el aviso tardío de Marcela llegó cuando Cristóbal ya se había llevado su cupo.
--
-- Responde una pregunta: ¿hay más suscripciones en el plan fundador que cupos confirmados? Si la
-- primera cuenta es mayor, alguien quedó como fundador SIN cupo: el caso de los «51 fundadores».
--
-- Solo para la base LOCAL `zass` en 127.0.0.1:5433.

DO $$
BEGIN
  IF current_database() <> 'zass' OR inet_server_port() IS DISTINCT FROM 5433 THEN
    RAISE EXCEPTION 'JQ-7: este bloque es solo para la base local zass en el puerto 5433 (estás en % puerto %).',
      current_database(), inet_server_port();
  END IF;
END $$;

BEGIN READ ONLY;

\echo '=== Marcela y Cristóbal: plan, estado, vigencia y cupo ==='
SELECT p.slug, pl.codigo AS plan, s.estado,
       to_char(s.vigente_hasta AT TIME ZONE 'UTC', 'YYYY-MM-DD"T"HH24:MI:SS.US"Z"') AS vigente_hasta_utc,
       c.numero AS cupo,
       CASE WHEN c.numero IS NULL THEN '(sin cupo)'
            WHEN c.reservado_hasta IS NULL THEN 'confirmado'
            ELSE 'reservado' END AS estado_cupo
  FROM perfiles_prestador p
  JOIN suscripciones s ON s.perfil_prestador_id = p.id
  JOIN planes pl ON pl.id = s.plan_id
  LEFT JOIN cupos_fundador c ON c.perfil_prestador_id = p.id
 WHERE p.slug IN ('marcela-soto', 'cristobal-henriquez')
 ORDER BY p.slug;

\echo '=== Suscripciones fundador vigentes frente a cupos confirmados (si la primera es mayor: hay fundadores sin cupo) ==='
SELECT (SELECT count(*) FROM suscripciones s JOIN planes pl ON pl.id = s.plan_id
         WHERE pl.codigo = 'fundador' AND s.estado IN ('Prueba', 'Activa') AND s.vigente_hasta > now())
         AS suscripciones_fundador,
       (SELECT count(*) FROM cupos_fundador WHERE perfil_prestador_id IS NOT NULL AND reservado_hasta IS NULL)
         AS cupos_confirmados;

\echo '=== Fundadores vigentes SIN cupo confirmado (esperado: ninguno) ==='
SELECT p.slug, s.estado
  FROM suscripciones s
  JOIN planes pl ON pl.id = s.plan_id
  JOIN perfiles_prestador p ON p.id = s.perfil_prestador_id
 WHERE pl.codigo = 'fundador' AND s.estado IN ('Prueba', 'Activa') AND s.vigente_hasta > now()
   AND NOT EXISTS (SELECT 1 FROM cupos_fundador c
                    WHERE c.perfil_prestador_id = s.perfil_prestador_id AND c.reservado_hasta IS NULL);

COMMIT;
