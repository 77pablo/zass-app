\set ON_ERROR_STOP on
-- JQ-7 · Bloque DEVOLVER. Deshace 20-ocupar.sql: libera SOLO los cupos con la marca exacta
-- reservado_hasta = '2099-12-31 23:59:59+00'. Ningún otro cupo de la tabla se toca.
--
-- Solo para la base LOCAL `zass` en 127.0.0.1:5433.
--   herramientas\pgsql\bin\psql.exe -h 127.0.0.1 -p 5433 -U zass -d zass -f frontend\pruebas-navegador\datos\camino-del-pago\21-devolver.sql

DO $$
BEGIN
  IF current_database() <> 'zass' OR inet_server_port() IS DISTINCT FROM 5433 THEN
    RAISE EXCEPTION 'JQ-7: este bloque es solo para la base local zass en el puerto 5433 (estás en % puerto %). No se tocó nada.',
      current_database(), inet_server_port();
  END IF;
END $$;

\echo '=== Cupos con la marca, antes de devolver ==='
SELECT c.numero, p.slug
  FROM cupos_fundador c JOIN perfiles_prestador p ON p.id = c.perfil_prestador_id
 WHERE c.reservado_hasta = '2099-12-31 23:59:59+00'
 ORDER BY c.numero;

BEGIN;

UPDATE cupos_fundador
   SET perfil_prestador_id = NULL, reservado_hasta = NULL
 WHERE reservado_hasta = '2099-12-31 23:59:59+00';

COMMIT;

\echo '=== Cómo quedó la tabla (esperado: con_marca_jq7 0) ==='
SELECT count(*) FILTER (WHERE perfil_prestador_id IS NULL)                                  AS libres,
       count(*) FILTER (WHERE perfil_prestador_id IS NOT NULL AND reservado_hasta > now())  AS reservados,
       count(*) FILTER (WHERE perfil_prestador_id IS NOT NULL AND reservado_hasta IS NULL)  AS confirmados,
       count(*) FILTER (WHERE reservado_hasta = '2099-12-31 23:59:59+00')                   AS con_marca_jq7
  FROM cupos_fundador;
