\set ON_ERROR_STOP on
-- JQ-7 · Bloque ESTADO-FINAL. Solo lee. Se corre al terminar todas las fases (después del
-- último 10-reinicio-marcela.sql) y se compara con lo que imprimió 00-inicio.sql «después del
-- reinicio»: libres, reservados y confirmados tienen que dar igual, y con_marca_jq7 tiene que
-- dar 0.
--
-- Solo para la base LOCAL `zass` en 127.0.0.1:5433.
--   herramientas\pgsql\bin\psql.exe -h 127.0.0.1 -p 5433 -U zass -d zass -f frontend\pruebas-navegador\datos\camino-del-pago\99-estado-final.sql

DO $$
BEGIN
  IF current_database() <> 'zass' OR inet_server_port() IS DISTINCT FROM 5433 THEN
    RAISE EXCEPTION 'JQ-7: este bloque es solo para la base local zass en el puerto 5433 (estás en % puerto %).',
      current_database(), inet_server_port();
  END IF;
END $$;

BEGIN READ ONLY;

\echo '=== ESTADO FINAL — compáralo con el de 00-inicio (después del reinicio) ==='
SELECT count(*) FILTER (WHERE perfil_prestador_id IS NULL)                                  AS libres,
       count(*) FILTER (WHERE perfil_prestador_id IS NOT NULL AND reservado_hasta > now())  AS reservados,
       count(*) FILTER (WHERE perfil_prestador_id IS NOT NULL AND reservado_hasta <= now()) AS reservas_vencidas,
       count(*) FILTER (WHERE perfil_prestador_id IS NOT NULL AND reservado_hasta IS NULL)  AS confirmados,
       count(*) FILTER (WHERE reservado_hasta = '2099-12-31 23:59:59+00')                   AS con_marca_jq7,
       count(*)                                                                             AS total
  FROM cupos_fundador;

\echo '=== Marcela ==='
SELECT pl.codigo AS plan, s.estado, (s.vigente_hasta::date - now()::date) AS dias,
       (SELECT numero FROM cupos_fundador c WHERE c.perfil_prestador_id = p.id) AS cupo,
       (SELECT count(*) FROM intentos_de_pago i WHERE i.perfil_prestador_id = p.id) AS intentos
  FROM perfiles_prestador p
  JOIN suscripciones s ON s.perfil_prestador_id = p.id
  JOIN planes pl ON pl.id = s.plan_id
 WHERE p.slug = 'marcela-soto';

\echo '=== Cristóbal (segunda prestadora de la fase tardia): tiene que coincidir con lo que mostró 40-tardia-antes ==='
SELECT s.plan_id, pl.codigo AS plan, s.estado,
       to_char(s.vigente_hasta AT TIME ZONE 'UTC', 'YYYY-MM-DD"T"HH24:MI:SS.US"Z"') AS vigente_hasta_utc,
       coalesce(s.referencia_pasarela, '(nula)') AS referencia_pasarela,
       (SELECT numero FROM cupos_fundador c WHERE c.perfil_prestador_id = s.perfil_prestador_id) AS cupo,
       (SELECT count(*) FROM intentos_de_pago i WHERE i.perfil_prestador_id = s.perfil_prestador_id) AS intentos
  FROM suscripciones s
  JOIN planes pl ON pl.id = s.plan_id
 WHERE s.perfil_prestador_id = (SELECT id FROM perfiles_prestador WHERE slug = 'cristobal-henriquez');

\echo '=== La cuenta buscador de la fase sin-ficha (no se borra; queda para la próxima corrida) ==='
SELECT u.correo, u.correo_verificado,
       EXISTS (SELECT 1 FROM perfiles_prestador p WHERE p.usuario_id = u.id) AS tiene_ficha
  FROM usuarios u
 WHERE u.correo = 'buscador-jq7@example.test';

COMMIT;
