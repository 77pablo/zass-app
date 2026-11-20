-- Borra la gente de demostracion que deja sembrar-demo.sql (todo usuario `demo-%@zass.local`),
-- con lo que cuelga de ella. Solo base de DESARROLLO: misma guarda que el sembrado.
--
-- NO toca cupos ni pagos. Si un perfil demo tiene un cupo fundador, un canje de cupon, un intento
-- de pago o un pago (lo deja cupo-fundador.mjs), se NIEGA en vez de liberarlo o borrarlo en
-- cascada sin avisar: `cupos_fundador` es SET NULL hacia el perfil, e `intentos_de_pago` y `pagos`
-- son CASCADE desde la suscripcion.
--
-- Funciona con y sin la migracion CobroIntentosYCupones: si una de esas tablas no existe en la
-- base, se avisa con un NOTICE y se omite; las que existen se revisan igual.
--
-- Orden, por las claves foraneas (releidas en las migraciones):
--   notificaciones.cita_id no tiene FK  -> se borran a mano antes que las citas.
--   contactos -> citas es NO ACTION     -> contactos antes que citas.
--   citas -> bloques es RESTRICT        -> citas antes que el cascade que se lleva los bloques.
--   tarjetas -> perfiles sin regla      -> tarjetas a mano (los escaneos van en cascade).
--   bloqueos_agenda no tiene ninguna FK -> a mano, o quedaria basura.
--
-- Uso (PowerShell, desde la raiz del proyecto):
--   & C:\Users\joaqu\zass-app\herramientas\pgsql\bin\psql.exe -h 127.0.0.1 -p 5433 -U zass -d zass -v ON_ERROR_STOP=1 -f frontend\pruebas-navegador\datos\limpiar-demo.sql

\set ON_ERROR_STOP on
SET client_encoding = 'UTF8';

DO $$
BEGIN
  IF current_database() <> 'zass'
     OR inet_server_port() IS DISTINCT FROM 5433
     OR inet_server_addr() IS NULL
     OR inet_server_addr() NOT IN ('127.0.0.1'::inet, '::1'::inet) THEN
    RAISE EXCEPTION 'GUARDA: esta limpieza es solo para la base de desarrollo `zass` en 127.0.0.1:5433. Estas en «%» en %:%. No se hizo nada.',
      current_database(), coalesce(host(inet_server_addr()), 'socket local'), coalesce(inet_server_port()::text, '?');
  END IF;
END $$;

BEGIN;

CREATE TEMP TABLE u_demo ON COMMIT DROP AS
SELECT id FROM usuarios WHERE correo::text LIKE 'demo-%';

CREATE TEMP TABLE p_demo ON COMMIT DROP AS
SELECT p.id FROM perfiles_prestador p JOIN u_demo u ON u.id = p.usuario_id;

CREATE TEMP TABLE s_demo ON COMMIT DROP AS
SELECT s.id FROM suscripciones s JOIN p_demo p ON p.id = s.perfil_prestador_id;

-- Cupos y pagos: se niega antes de tocar nada. Cada tabla se revisa solo si existe (to_regclass);
-- la consulta va con EXECUTE para que el nombre de la tabla se resuelva recien al ejecutarla.
DO $$
DECLARE
  cupos int := 0; canjes int := 0; intentos int := 0; pagos_n int := 0;
BEGIN
  IF to_regclass('cupos_fundador') IS NOT NULL THEN
    EXECUTE 'SELECT count(*) FROM cupos_fundador WHERE perfil_prestador_id IN (SELECT id FROM p_demo)'
      INTO cupos;
  ELSE
    RAISE NOTICE 'No existe la tabla cupos_fundador en esta base: se omite.';
  END IF;

  IF to_regclass('canjes_de_cupon') IS NOT NULL THEN
    EXECUTE 'SELECT count(*) FROM canjes_de_cupon WHERE perfil_prestador_id IN (SELECT id FROM p_demo)'
      INTO canjes;
  ELSE
    RAISE NOTICE 'No existe la tabla canjes_de_cupon en esta base: se omite.';
  END IF;

  IF to_regclass('intentos_de_pago') IS NOT NULL THEN
    EXECUTE 'SELECT count(*) FROM intentos_de_pago
              WHERE perfil_prestador_id IN (SELECT id FROM p_demo) OR suscripcion_id IN (SELECT id FROM s_demo)'
      INTO intentos;
  ELSE
    RAISE NOTICE 'No existe la tabla intentos_de_pago en esta base: se omite.';
  END IF;

  IF to_regclass('pagos') IS NOT NULL THEN
    EXECUTE 'SELECT count(*) FROM pagos WHERE suscripcion_id IN (SELECT id FROM s_demo)'
      INTO pagos_n;
  ELSE
    RAISE NOTICE 'No existe la tabla pagos en esta base: se omite.';
  END IF;

  IF cupos + canjes + intentos + pagos_n > 0 THEN
    RAISE EXCEPTION 'NO SE BORRO NADA: hay perfiles demo con cupos fundador (%), canjes de cupon (%), intentos de pago (%) o pagos (%). Esta limpieza no toca cupos ni pagos: sueltalos primero (cupo-fundador.mjs lo hace al empezar) y vuelve a correrla.',
      cupos, canjes, intentos, pagos_n;
  END IF;
END $$;

CREATE TEMP TABLE c_demo ON COMMIT DROP AS
SELECT id FROM citas
 WHERE perfil_prestador_id IN (SELECT id FROM p_demo) OR buscador_id IN (SELECT id FROM u_demo);

DELETE FROM notificaciones WHERE cita_id IN (SELECT id FROM c_demo);

-- Los contactos de las citas demo, y de paso todos los de perfiles y personas demo (el cascade se
-- los llevaria igual): asi ninguno queda apuntando a un escaneo de una tarjeta que se borra abajo.
DELETE FROM contactos
 WHERE cita_id IN (SELECT id FROM c_demo)
    OR perfil_prestador_id IN (SELECT id FROM p_demo)
    OR buscador_id IN (SELECT id FROM u_demo);

DELETE FROM citas WHERE id IN (SELECT id FROM c_demo);

DELETE FROM tarjetas WHERE perfil_prestador_id IN (SELECT id FROM p_demo);

DELETE FROM bloqueos_agenda WHERE perfil_prestador_id IN (SELECT id FROM p_demo);

-- El cascade desde usuarios se lleva perfil, coberturas, servicios, suscripcion, credenciales,
-- disponibilidades, bloques, resenas, roles y perfiles de busqueda.
DELETE FROM usuarios WHERE id IN (SELECT id FROM u_demo);

SELECT
  (SELECT count(*) FROM usuarios WHERE correo::text LIKE 'demo-%')                                AS demo_restantes,
  (SELECT count(*) FROM perfiles_prestador WHERE id IN (SELECT id FROM p_demo))                  AS perfiles,
  (SELECT count(*) FROM bloqueos_agenda WHERE perfil_prestador_id IN (SELECT id FROM p_demo))    AS bloqueos_agenda,
  (SELECT count(*) FROM notificaciones WHERE cita_id IN (SELECT id FROM c_demo))                 AS notificaciones,
  (SELECT count(*) FROM citas WHERE id IN (SELECT id FROM c_demo))                               AS citas;

COMMIT;
