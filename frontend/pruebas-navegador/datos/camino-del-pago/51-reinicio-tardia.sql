\set ON_ERROR_STOP on
-- JQ-7 · Bloque REINICIO-TARDIA. Deja a las DOS prestadoras como estaban antes de la fase `tardia`:
--   · Marcela: igual que 10-reinicio-marcela.sql.
--   · Cristóbal: suelta su cupo, borra SOLO los intentos creados desde la hora de inicio, y vuelve
--     a los valores EXACTOS que mostró 40-tardia-antes.sql (plan_id, estado, vigente_hasta y
--     referencia_pasarela). No se supone nada: los valores llegan por -v.
--
-- NO se corre a mano escribiendo los valores: se usa la línea que imprimió 40-tardia-antes.sql.
-- Si falta cualquiera de las cinco variables, aborta antes de escribir.
--
-- Solo para la base LOCAL `zass` en 127.0.0.1:5433.

\if :{?plan_cristobal}
\else
  \echo 'JQ-7: falta -v plan_cristobal. Usa la línea que imprimió 40-tardia-antes.sql. No se tocó nada.'
  SELECT 1/0 AS falta_una_variable;
\endif
\if :{?estado_cristobal}
\else
  \echo 'JQ-7: falta -v estado_cristobal. Usa la línea que imprimió 40-tardia-antes.sql. No se tocó nada.'
  SELECT 1/0 AS falta_una_variable;
\endif
\if :{?vigente_cristobal}
\else
  \echo 'JQ-7: falta -v vigente_cristobal. Usa la línea que imprimió 40-tardia-antes.sql. No se tocó nada.'
  SELECT 1/0 AS falta_una_variable;
\endif
\if :{?referencia_cristobal}
\else
  \echo 'JQ-7: falta -v referencia_cristobal. Usa la línea que imprimió 40-tardia-antes.sql. No se tocó nada.'
  SELECT 1/0 AS falta_una_variable;
\endif
\if :{?desde}
\else
  \echo 'JQ-7: falta -v desde. Usa la línea que imprimió 40-tardia-antes.sql. No se tocó nada.'
  SELECT 1/0 AS falta_una_variable;
\endif

DO $$
BEGIN
  IF current_database() <> 'zass' OR inet_server_port() IS DISTINCT FROM 5433 THEN
    RAISE EXCEPTION 'JQ-7: este bloque es solo para la base local zass en el puerto 5433 (estás en % puerto %). No se tocó nada.',
      current_database(), inet_server_port();
  END IF;
END $$;

BEGIN;

-- Los valores se convierten ANTES de escribir nada: uno mal escrito aborta la transacción entera.
SELECT :'plan_cristobal'::smallint                AS plan_id,
       :'estado_cristobal'::text                  AS estado,
       :'vigente_cristobal'::timestamptz          AS vigente_hasta,
       NULLIF(:'referencia_cristobal', 'NULA')    AS referencia_pasarela,
       :'desde'::timestamptz                      AS desde;

-- Marcela, igual que 10-reinicio-marcela.sql.
UPDATE cupos_fundador SET perfil_prestador_id = NULL, reservado_hasta = NULL
 WHERE perfil_prestador_id = (SELECT id FROM perfiles_prestador WHERE slug = 'marcela-soto');

DELETE FROM intentos_de_pago
 WHERE perfil_prestador_id = (SELECT id FROM perfiles_prestador WHERE slug = 'marcela-soto');

UPDATE suscripciones SET plan_id = 1, estado = 'Prueba', vigente_hasta = now() + interval '10 days'
 WHERE perfil_prestador_id = (SELECT id FROM perfiles_prestador WHERE slug = 'marcela-soto');

-- Cristóbal, a sus valores exactos de antes.
UPDATE cupos_fundador SET perfil_prestador_id = NULL, reservado_hasta = NULL
 WHERE perfil_prestador_id = (SELECT id FROM perfiles_prestador WHERE slug = 'cristobal-henriquez');

DELETE FROM intentos_de_pago
 WHERE perfil_prestador_id = (SELECT id FROM perfiles_prestador WHERE slug = 'cristobal-henriquez')
   AND creado_en >= :'desde'::timestamptz;

UPDATE suscripciones
   SET plan_id = :'plan_cristobal'::smallint,
       estado = :'estado_cristobal',
       vigente_hasta = :'vigente_cristobal'::timestamptz,
       referencia_pasarela = NULLIF(:'referencia_cristobal', 'NULA')
 WHERE perfil_prestador_id = (SELECT id FROM perfiles_prestador WHERE slug = 'cristobal-henriquez');

COMMIT;

\echo '=== Cristóbal después del reinicio (compáralo con lo que mostró 40-tardia-antes) ==='
SELECT s.plan_id, pl.codigo AS plan, s.estado,
       to_char(s.vigente_hasta AT TIME ZONE 'UTC', 'YYYY-MM-DD"T"HH24:MI:SS.US"Z"') AS vigente_hasta_utc,
       coalesce(s.referencia_pasarela, '(nula)') AS referencia_pasarela,
       (SELECT numero FROM cupos_fundador c WHERE c.perfil_prestador_id = s.perfil_prestador_id) AS cupo,
       (SELECT count(*) FROM intentos_de_pago i WHERE i.perfil_prestador_id = s.perfil_prestador_id) AS intentos
  FROM suscripciones s
  JOIN planes pl ON pl.id = s.plan_id
 WHERE s.perfil_prestador_id = (SELECT id FROM perfiles_prestador WHERE slug = 'cristobal-henriquez');

\echo '=== Marcela después del reinicio (esperado: prueba | Prueba | 10 | vacío | 0) ==='
SELECT pl.codigo AS plan, s.estado, (s.vigente_hasta::date - now()::date) AS dias,
       (SELECT numero FROM cupos_fundador c WHERE c.perfil_prestador_id = p.id) AS cupo,
       (SELECT count(*) FROM intentos_de_pago i WHERE i.perfil_prestador_id = p.id) AS intentos
  FROM perfiles_prestador p
  JOIN suscripciones s ON s.perfil_prestador_id = p.id
  JOIN planes pl ON pl.id = s.plan_id
 WHERE p.slug = 'marcela-soto';
