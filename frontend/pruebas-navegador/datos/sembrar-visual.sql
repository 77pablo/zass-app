-- Prestadores de PRUEBA para mirar la pantalla de busqueda en un navegador real.
--
-- Todos llevan el correo `visual-*@zass.local`. Borrar sus usuarios arrastra en cascada
-- perfil, suscripcion, cobertura, servicios y credencial, y es lo que hace
-- limpiar_visual.sql. Si alguno quedara publicado, romperia las pruebas de
-- integracion de la busqueda, que esperan una base sin prestadores visibles.
--
-- Mismo arreglo que en sembrar-demo.sql (D9): contrasena real y roles. La clave de todas es
-- `demo-zass-2026`; el hash es su Argon2id (RN-S1-03 / DT-07). Cuentas inventadas de una base
-- local con correo `*@zass.local`: no se despliegan a ninguna parte.

DO $$
DECLARE
  -- Argon2id de `demo-zass-2026` (ver la cabecera del archivo).
  LA_CLAVE constant text := '$argon2id$v=19$m=19456,t=2,p=1$G74jP/dhSzmF2WXXOC5OzQ==$Zv3DBlK3ZOrEHT0HAKsSn21y1jfWN2HCt8lMYmCQy2g=';
  r record;
  u uuid;
  p uuid;
BEGIN
  FOR r IN
    SELECT * FROM (VALUES
      -- slug, nombre, descripcion, rubro, estado perfil, dias de suscripcion,
      -- promedio, resenas, verificada, zona principal, servicio, zona secundaria
      ('corretaje-araucania', 'Corretaje Araucanía',
       'Corretaje y administración de propiedades en Temuco con más de quince años de experiencia en el sector Centro y sus alrededores, con atención personalizada para cada cliente.',
       1, 'Publicado', 30, 4.6::numeric, 12, true, 1, 101, NULL::int),
      ('propiedades-del-sur', 'Propiedades del Sur',
       'Arriendos y ventas de casas y departamentos.',
       1, 'Publicado', 30, NULL::numeric, 0, false, 1, 103, NULL::int),
      ('estudio-nuevo-temuco', 'Estudio Nuevo Temuco',
       NULL,
       1, 'Publicado', 30, 5.0::numeric, 2, false, 1, 104, NULL::int),
      ('gestion-labranza', 'Gestión Inmobiliaria Labranza',
       'Terrenos y parcelas hacia el poniente de la ciudad.',
       1, 'Publicado', 30, 3.9::numeric, 3, true, 14, 105, 1),
      ('asesoria-larga', 'Asesoría Integral en Corretaje de Propiedades y Arriendos del Sur de Chile Limitada',
       'Un nombre larguísimo para ver cómo se parte en una pantalla angosta.',
       1, 'Publicado', 30, NULL::numeric, 1, false, 1, 102, NULL::int),
      -- Estos tres NO deben aparecer, y verlo es parte de la comprobacion:
      ('vencido-corretajes', 'Vencido Corretajes (NO debe salir)',
       'Suscripción vencida: RN-S3-09 dice que desaparece.',
       1, 'Publicado', -5, NULL::numeric, 0, false, 1, 101, NULL::int),
      ('borrador-corretajes', 'Borrador Corretajes (NO debe salir)',
       'Perfil sin publicar.',
       1, 'Borrador', 30, NULL::numeric, 0, false, 1, 101, NULL::int),
      ('abogado-centro', 'Abogados del Centro (otro rubro)',
       'Derecho. No debe salir al buscar corretaje.',
       2, 'Publicado', 30, 4.8::numeric, 20, true, 1, 201, NULL::int)
    ) AS t(slug, nombre, descr, rubro, estado, dias, prom, resenas, verif, zona, serv, zona2)
  LOOP
    u := gen_random_uuid();
    p := gen_random_uuid();

    INSERT INTO usuarios (id, correo, hash_contrasena, nombre, correo_verificado,
                          estado, creado_en, actualizado_en)
    VALUES (u, 'visual-' || r.slug || '@zass.local', LA_CLAVE, r.nombre, true,
            'Activa', now(), now());

    -- La cuenta es UNA: rol 1 buscador y rol 2 prestador (D9).
    INSERT INTO usuario_roles (usuario_id, rol_id, asignado_en)
    VALUES (u, 1, now()), (u, 2, now());

    INSERT INTO perfiles_prestador (id, usuario_id, rubro_id, slug, nombre_publico,
                                    descripcion, estado, origen_ubicacion,
                                    promedio_calificacion, total_resenas, version,
                                    publicado_en, actualizado_en)
    VALUES (p, u, r.rubro, 'visual-' || r.slug, r.nombre, r.descr, r.estado, 'Declarada',
            r.prom, r.resenas, 0,
            CASE WHEN r.estado = 'Publicado' THEN now() END, now());

    INSERT INTO suscripciones (id, perfil_prestador_id, plan_id, estado, inicia_en,
                               vigente_hasta, renovacion_automatica)
    VALUES (gen_random_uuid(), p, 1, 'Prueba', now() - interval '1 day',
            now() + make_interval(days => r.dias), false);

    INSERT INTO coberturas (id, perfil_prestador_id, zona_id, es_principal, desde_en)
    VALUES (gen_random_uuid(), p, r.zona, true, now());
    IF r.zona2 IS NOT NULL THEN
      INSERT INTO coberturas (id, perfil_prestador_id, zona_id, es_principal, desde_en)
      VALUES (gen_random_uuid(), p, r.zona2, false, now());
    END IF;

    INSERT INTO prestador_servicios (id, perfil_prestador_id, servicio_id,
                                     duracion_minutos, modo_agenda, activo)
    VALUES (gen_random_uuid(), p, r.serv, 60, 'Automatico', true);

    IF r.verif THEN
      INSERT INTO credenciales (id, perfil_prestador_id, tipo, nombre, obtenida_en,
                                estado, sello_en, sello_por, creada_en, esperando_prestador)
      VALUES (gen_random_uuid(), p, 'Titulo', 'Título profesional', DATE '2020-01-15',
              'Verificada', now(), gen_random_uuid(), now(), false);
    END IF;
  END LOOP;
END $$;

SELECT count(*) AS sembrados FROM usuarios WHERE correo::text LIKE 'visual-%';
