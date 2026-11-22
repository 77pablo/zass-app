\set ON_ERROR_STOP on
-- JQ-7 · Bloque OCUPAR. Deja el plan fundador AGOTADO para la fase `agotado` del guion.
--
-- Ocupa todos los cupos LIBRES (perfil_prestador_id IS NULL) con perfiles que no tienen cupo,
-- menos `marcela-soto`, que es la que tiene que encontrarlo cerrado. Cada cupo ocupado queda
-- MARCADO con reservado_hasta = '2099-12-31 23:59:59+00', una fecha que el código nunca
-- escribe (la reserva normal dura 25 minutos). 21-devolver.sql libera SOLO las filas con esa
-- marca exacta.
--
-- Mientras estén ocupados, no entres con ninguno de esos perfiles ni tomes su cupo: el código le
-- renovaría la reserva a 25 minutos y se perdería la marca.
--
-- El índice ux_cupo_fundador_perfil permite un solo cupo por perfil. Si no hay perfiles
-- suficientes, el bloque ABORTA SIN ESCRIBIR y dice cuántos faltan.
--
-- Solo para la base LOCAL `zass` en 127.0.0.1:5433.
--   herramientas\pgsql\bin\psql.exe -h 127.0.0.1 -p 5433 -U zass -d zass -f frontend\pruebas-navegador\datos\camino-del-pago\20-ocupar.sql

DO $$
BEGIN
  IF current_database() <> 'zass' OR inet_server_port() IS DISTINCT FROM 5433 THEN
    RAISE EXCEPTION 'JQ-7: este bloque es solo para la base local zass en el puerto 5433 (estás en % puerto %). No se tocó nada.',
      current_database(), inet_server_port();
  END IF;
END $$;

BEGIN;

DO $$
DECLARE
  libres int;
  vencidas int;
  disponibles int;
  ya_marcados int;
BEGIN
  SELECT count(*) INTO ya_marcados FROM cupos_fundador WHERE reservado_hasta = '2099-12-31 23:59:59+00';
  IF ya_marcados > 0 THEN
    RAISE EXCEPTION 'JQ-7: ya hay % cupos con la marca 2099. Corre 21-devolver.sql antes de volver a ocupar. No se tocó nada.', ya_marcados;
  END IF;

  -- Una reserva vencida cuenta como libre para el contador, pero tiene perfil: este bloque no
  -- la puede ocupar sin pisar a su dueño. Se para en vez de adivinar.
  SELECT count(*) INTO vencidas FROM cupos_fundador
   WHERE perfil_prestador_id IS NOT NULL AND reservado_hasta IS NOT NULL AND reservado_hasta <= now();
  IF vencidas > 0 THEN
    RAISE EXCEPTION 'JQ-7: hay % reservas vencidas sin liberar. Si una es de Marcela, corre 10-reinicio-marcela.sql. No se tocó nada.', vencidas;
  END IF;

  SELECT count(*) INTO libres FROM cupos_fundador WHERE perfil_prestador_id IS NULL;
  SELECT count(*) INTO disponibles FROM perfiles_prestador p
   WHERE p.slug <> 'marcela-soto'
     AND NOT EXISTS (SELECT 1 FROM cupos_fundador c WHERE c.perfil_prestador_id = p.id);

  IF disponibles < libres THEN
    RAISE EXCEPTION 'JQ-7: hay % cupos libres y solo % perfiles sin cupo para ocuparlos (faltan %). No se tocó nada: la fase agotado se recorre solo en pantalla.',
      libres, disponibles, libres - disponibles;
  END IF;
END $$;

-- Cupo libre n-ésimo con el perfil n-ésimo (por slug, para que sea reproducible).
WITH libres AS (
  SELECT numero, row_number() OVER (ORDER BY numero) AS n
    FROM cupos_fundador WHERE perfil_prestador_id IS NULL
), perfiles AS (
  SELECT p.id, row_number() OVER (ORDER BY p.slug) AS n
    FROM perfiles_prestador p
   WHERE p.slug <> 'marcela-soto'
     AND NOT EXISTS (SELECT 1 FROM cupos_fundador c WHERE c.perfil_prestador_id = p.id)
)
UPDATE cupos_fundador c
   SET perfil_prestador_id = pf.id,
       reservado_hasta = '2099-12-31 23:59:59+00'
  FROM libres l
  JOIN perfiles pf ON pf.n = l.n
 WHERE c.numero = l.numero;

DO $$
BEGIN
  IF EXISTS (SELECT 1 FROM cupos_fundador WHERE perfil_prestador_id IS NULL) THEN
    RAISE EXCEPTION 'JQ-7: quedó algún cupo libre después de ocupar. Se deshace todo.';
  END IF;
END $$;

COMMIT;

\echo '=== Cupos que ocupó este bloque (los que 21-devolver.sql va a liberar) ==='
SELECT c.numero, p.slug
  FROM cupos_fundador c JOIN perfiles_prestador p ON p.id = c.perfil_prestador_id
 WHERE c.reservado_hasta = '2099-12-31 23:59:59+00'
 ORDER BY c.numero;

\echo '=== Cómo quedó la tabla (esperado: libres 0) ==='
SELECT count(*) FILTER (WHERE perfil_prestador_id IS NULL)                                  AS libres,
       count(*) FILTER (WHERE perfil_prestador_id IS NOT NULL AND reservado_hasta > now())  AS reservados,
       count(*) FILTER (WHERE perfil_prestador_id IS NOT NULL AND reservado_hasta IS NULL)  AS confirmados,
       count(*) FILTER (WHERE reservado_hasta = '2099-12-31 23:59:59+00')                   AS con_marca_jq7
  FROM cupos_fundador;
