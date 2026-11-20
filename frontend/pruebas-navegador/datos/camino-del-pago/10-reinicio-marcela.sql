\set ON_ERROR_STOP on
-- JQ-7 · Bloque REINICIO-MARCELA. Deja a `marcela-soto` como al principio: sin cupo, sin
-- intentos de pago y en el plan de prueba con 10 días. Se corre después de las fases
-- `vencida` y `retomar`, que le cambian el cupo o la vigencia.
--
-- Toca SOLO filas de Marcela. Solo para la base LOCAL `zass` en 127.0.0.1:5433.
--   herramientas\pgsql\bin\psql.exe -h 127.0.0.1 -p 5433 -U zass -d zass -f frontend\pruebas-navegador\datos\camino-del-pago\10-reinicio-marcela.sql

DO $$
BEGIN
  IF current_database() <> 'zass' OR inet_server_port() IS DISTINCT FROM 5433 THEN
    RAISE EXCEPTION 'JQ-7: este bloque es solo para la base local zass en el puerto 5433 (estás en % puerto %). No se tocó nada.',
      current_database(), inet_server_port();
  END IF;
END $$;

BEGIN;

DO $$
BEGIN
  IF NOT EXISTS (SELECT 1 FROM perfiles_prestador WHERE slug = 'marcela-soto') THEN
    RAISE EXCEPTION 'JQ-7: no existe el perfil de demostración marcela-soto.';
  END IF;
END $$;

UPDATE cupos_fundador SET perfil_prestador_id = NULL, reservado_hasta = NULL
 WHERE perfil_prestador_id = (SELECT id FROM perfiles_prestador WHERE slug = 'marcela-soto');

DELETE FROM intentos_de_pago
 WHERE perfil_prestador_id = (SELECT id FROM perfiles_prestador WHERE slug = 'marcela-soto');

UPDATE suscripciones SET plan_id = 1, estado = 'Prueba', vigente_hasta = now() + interval '10 days'
 WHERE perfil_prestador_id = (SELECT id FROM perfiles_prestador WHERE slug = 'marcela-soto');

COMMIT;

\echo '=== Marcela después del reinicio (esperado: prueba | Prueba | 10 | vacío | 0) ==='
SELECT pl.codigo AS plan, s.estado, (s.vigente_hasta::date - now()::date) AS dias,
       (SELECT numero FROM cupos_fundador c WHERE c.perfil_prestador_id = p.id) AS cupo,
       (SELECT count(*) FROM intentos_de_pago i WHERE i.perfil_prestador_id = p.id) AS intentos
  FROM perfiles_prestador p
  JOIN suscripciones s ON s.perfil_prestador_id = p.id
  JOIN planes pl ON pl.id = s.plan_id
 WHERE p.slug = 'marcela-soto';
