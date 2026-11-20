\set ON_ERROR_STOP on
-- JQ-7 · Bloque INICIO. Se corre UNA vez, antes de la primera fase del guion
-- `camino-del-pago.mjs`. Hace dos cosas:
--   1. Muestra cómo está la tabla de cupos ANTES de tocar nada (para compararlo al final con
--      99-estado-final.sql).
--   2. Reinicia a la prestadora de demostración `marcela-soto` (lo mismo que hace
--      10-reinicio-marcela.sql).
--
-- Solo para la base LOCAL `zass` en 127.0.0.1:5433. La guarda de abajo aborta en cualquier otra.
--   herramientas\pgsql\bin\psql.exe -h 127.0.0.1 -p 5433 -U zass -d zass -f frontend\pruebas-navegador\datos\camino-del-pago\00-inicio.sql

-- Guarda: antes de cualquier escritura.
DO $$
BEGIN
  IF current_database() <> 'zass' OR inet_server_port() IS DISTINCT FROM 5433 THEN
    RAISE EXCEPTION 'JQ-7: este bloque es solo para la base local zass en el puerto 5433 (estás en % puerto %). No se tocó nada.',
      current_database(), inet_server_port();
  END IF;
END $$;

\echo '=== ESTADO DE INICIO (antes del reinicio) ==='
SELECT count(*) FILTER (WHERE perfil_prestador_id IS NULL)                                  AS libres,
       count(*) FILTER (WHERE perfil_prestador_id IS NOT NULL AND reservado_hasta > now())  AS reservados,
       count(*) FILTER (WHERE perfil_prestador_id IS NOT NULL AND reservado_hasta <= now()) AS reservas_vencidas,
       count(*) FILTER (WHERE perfil_prestador_id IS NOT NULL AND reservado_hasta IS NULL)  AS confirmados,
       count(*) FILTER (WHERE reservado_hasta = '2099-12-31 23:59:59+00')                   AS con_marca_jq7,
       count(*)                                                                             AS total
  FROM cupos_fundador;

BEGIN;

DO $$
BEGIN
  IF NOT EXISTS (SELECT 1 FROM perfiles_prestador WHERE slug = 'marcela-soto') THEN
    RAISE EXCEPTION 'JQ-7: no existe el perfil de demostración marcela-soto. Corre el sembrado demo primero.';
  END IF;
END $$;

-- El cupo de Marcela vuelve a estar libre, sus intentos se borran y su suscripción vuelve al
-- plan de prueba (plan 1) con 10 días, igual que cupo-fundador.mjs:56-60.
UPDATE cupos_fundador SET perfil_prestador_id = NULL, reservado_hasta = NULL
 WHERE perfil_prestador_id = (SELECT id FROM perfiles_prestador WHERE slug = 'marcela-soto');

DELETE FROM intentos_de_pago
 WHERE perfil_prestador_id = (SELECT id FROM perfiles_prestador WHERE slug = 'marcela-soto');

UPDATE suscripciones SET plan_id = 1, estado = 'Prueba', vigente_hasta = now() + interval '10 days'
 WHERE perfil_prestador_id = (SELECT id FROM perfiles_prestador WHERE slug = 'marcela-soto');

COMMIT;

\echo '=== ESTADO DE INICIO (después del reinicio de Marcela) — anótalo para compararlo con 99-estado-final ==='
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
