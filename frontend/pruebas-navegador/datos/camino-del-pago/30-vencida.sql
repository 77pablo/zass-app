\set ON_ERROR_STOP on
-- JQ-7 · Bloque VENCIDA. Hace como si hubieran pasado los 25 minutos de la reserva de Marcela
-- (RN-S7-02), sin esperarlos: deja su reservado_hasta un minuto en el pasado.
--
-- Solo toca el cupo de `marcela-soto`, y solo si está RESERVADO (reservado_hasta no nulo).
-- Un cupo confirmado no se toca. Si Marcela no tiene una reserva, aborta: primero hay que
-- apretar «Quiero mi cupo» en el guion (fase `vencida`).
--
-- Solo para la base LOCAL `zass` en 127.0.0.1:5433.
--   herramientas\pgsql\bin\psql.exe -h 127.0.0.1 -p 5433 -U zass -d zass -f frontend\pruebas-navegador\datos\camino-del-pago\30-vencida.sql

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
  IF NOT EXISTS (
    SELECT 1 FROM cupos_fundador
     WHERE perfil_prestador_id = (SELECT id FROM perfiles_prestador WHERE slug = 'marcela-soto')
       AND reservado_hasta IS NOT NULL
       AND reservado_hasta > now()) THEN
    RAISE EXCEPTION 'JQ-7: Marcela no tiene una reserva vigente. Corre la fase vencida del guion hasta la pausa. No se tocó nada.';
  END IF;
END $$;

UPDATE cupos_fundador
   SET reservado_hasta = now() - interval '1 minute'
 WHERE perfil_prestador_id = (SELECT id FROM perfiles_prestador WHERE slug = 'marcela-soto')
   AND reservado_hasta IS NOT NULL;

COMMIT;

\echo '=== El cupo de Marcela (esperado: vencida = t) ==='
SELECT c.numero, c.reservado_hasta, c.reservado_hasta <= now() AS vencida
  FROM cupos_fundador c
 WHERE c.perfil_prestador_id = (SELECT id FROM perfiles_prestador WHERE slug = 'marcela-soto');
