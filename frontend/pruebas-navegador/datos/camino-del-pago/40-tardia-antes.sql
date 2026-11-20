\set ON_ERROR_STOP on
-- JQ-7 · Bloque TARDIA-ANTES. Solo lee. Se corre ANTES de la fase `tardia` del guion, antes de
-- tocar a la segunda prestadora (`cristobal-henriquez`).
--
-- 1. Comprueba que se puede empezar: Cristóbal en estado Prueba, sin cancelar y sin cupo; Marcela
--    sin cupo (después de 10-reinicio-marcela.sql). Si algo no calza, ABORTA y la fase no sigue.
-- 2. Muestra los valores exactos de la suscripción de Cristóbal que la fase va a cambiar (plan_id,
--    estado, vigente_hasta y referencia_pasarela, que PasarAFundadorConMesGratisAsync también
--    sobrescribe) y la hora de inicio.
-- 3. Imprime la línea completa para 51-reinicio-tardia.sql, con esos valores en -v. GUÁRDALA.
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

DO $$
DECLARE
  c uuid := (SELECT id FROM perfiles_prestador WHERE slug = 'cristobal-henriquez');
  m uuid := (SELECT id FROM perfiles_prestador WHERE slug = 'marcela-soto');
  n_susc int;
  s record;
BEGIN
  IF c IS NULL THEN
    RAISE EXCEPTION 'JQ-7: DETÉN LA FASE TARDIA. No existe el perfil de demostración cristobal-henriquez.';
  END IF;
  IF m IS NULL THEN
    RAISE EXCEPTION 'JQ-7: DETÉN LA FASE TARDIA. No existe el perfil de demostración marcela-soto.';
  END IF;

  SELECT count(*) INTO n_susc FROM suscripciones WHERE perfil_prestador_id = c;
  IF n_susc <> 1 THEN
    RAISE EXCEPTION 'JQ-7: DETÉN LA FASE TARDIA. Cristóbal tiene % suscripciones y el bloque de reinicio supone una.', n_susc;
  END IF;

  SELECT * INTO s FROM suscripciones WHERE perfil_prestador_id = c;
  IF s.estado <> 'Prueba' THEN
    RAISE EXCEPTION 'JQ-7: DETÉN LA FASE TARDIA. Cristóbal está en estado % y no en Prueba.', s.estado;
  END IF;
  IF s.cancelada_en IS NOT NULL THEN
    RAISE EXCEPTION 'JQ-7: DETÉN LA FASE TARDIA. La suscripción de Cristóbal está cancelada.';
  END IF;
  IF EXISTS (SELECT 1 FROM cupos_fundador WHERE perfil_prestador_id = c) THEN
    RAISE EXCEPTION 'JQ-7: DETÉN LA FASE TARDIA. Cristóbal ya tiene un cupo fundador (reservado o confirmado).';
  END IF;
  IF EXISTS (SELECT 1 FROM cupos_fundador WHERE perfil_prestador_id = m) THEN
    RAISE EXCEPTION 'JQ-7: DETÉN LA FASE TARDIA. Marcela todavía tiene cupo: corre antes 10-reinicio-marcela.sql.';
  END IF;
END $$;

\echo '=== Cristóbal ANTES de la fase tardia (estos valores son los que 51-reinicio-tardia restituye) ==='
SELECT s.plan_id, pl.codigo AS plan, s.estado,
       to_char(s.vigente_hasta AT TIME ZONE 'UTC', 'YYYY-MM-DD"T"HH24:MI:SS.US"Z"') AS vigente_hasta_utc,
       coalesce(s.referencia_pasarela, '(nula)') AS referencia_pasarela,
       (SELECT count(*) FROM intentos_de_pago i WHERE i.perfil_prestador_id = s.perfil_prestador_id) AS intentos
  FROM suscripciones s
  JOIN planes pl ON pl.id = s.plan_id
 WHERE s.perfil_prestador_id = (SELECT id FROM perfiles_prestador WHERE slug = 'cristobal-henriquez');

\echo '=== GUARDA ESTA LÍNEA: es la que restituye a los dos al final (51-reinicio-tardia.sql) ==='
SELECT format(
         '$env:PGCLIENTENCODING="UTF8"; & "C:\Users\joaqu\zass-app\herramientas\pgsql\bin\psql.exe" -h 127.0.0.1 -p 5433 -U zass -d zass'
         ' -v plan_cristobal=%s -v estado_cristobal=%s -v vigente_cristobal=%s -v referencia_cristobal=%s -v desde=%s'
         ' -f "C:\Users\joaqu\zass-app-pago\frontend\pruebas-navegador\datos\camino-del-pago\51-reinicio-tardia.sql"',
         s.plan_id, s.estado,
         to_char(s.vigente_hasta AT TIME ZONE 'UTC', 'YYYY-MM-DD"T"HH24:MI:SS.US"Z"'),
         coalesce(s.referencia_pasarela, 'NULA'),
         to_char(now() AT TIME ZONE 'UTC', 'YYYY-MM-DD"T"HH24:MI:SS.US"Z"')) AS linea_para_51
  FROM suscripciones s
 WHERE s.perfil_prestador_id = (SELECT id FROM perfiles_prestador WHERE slug = 'cristobal-henriquez');

COMMIT;
