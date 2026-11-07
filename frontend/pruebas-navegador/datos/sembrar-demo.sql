-- Gente de DEMOSTRACION para ver el sitio completo en la base de desarrollo (`zass`).
--
-- Son personas inventadas: nombres, telefonos y descripciones no corresponden a nadie real
-- (los numeros son del bloque 5550, que no se asigna a nadie). Los promedios y recuentos de
-- resenas estan escritos a mano en el perfil: NO hay filas de resenas detras, porque MOD-11
-- no existe todavia. Sirven solo para ver como se ve la pagina; no se publican en ninguna
-- parte que no sea esta base.
--
-- Todos llevan el correo `demo-*@zass.local`. limpiar-demo.sql los borra en cascada.
-- Las pruebas de integracion usan su propia base (`zass_pruebas`), asi que esto no las afecta.
--
-- Uso (desde la raiz del proyecto):
--   herramientas\pgsql\bin\psql.exe -h 127.0.0.1 -p 5433 -U zass -d zass -f frontend\pruebas-navegador\datos\sembrar-demo.sql

DO $$
DECLARE
  r record;
  u uuid;
  p uuid;
  i int;
  z int;
BEGIN
  FOR r IN
    SELECT * FROM (VALUES
      -- slug, nombre, descripcion, rubro, promedio, resenas, verificado, zonas (la 1a es la principal),
      -- servicios (terna: servicio, minutos, precio o NULL), dias desde que se publico, tel, whatsapp, correo, sitio
      ('marcela-soto', 'Marcela Soto Rivas',
       'Llevo quince años ayudando a familias de Temuco a comprar y vender su casa sin sobresaltos. Me gusta explicar cada paso, de la tasación a la firma, y que mis clientes sepan siempre cómo va todo. Atiendo el centro y Pedro de Valdivia, con visitas los fines de semana.',
       1, 4.8::numeric, 23, true, ARRAY[1,5], ARRAY[101,60,60000, 103,90,NULL, 104,60,NULL], 2,
       '+56 9 5550 0101', '+56 9 5550 0101', 'marcela@demo.zass.local', 'https://ejemplo.invalid/marcela'),
      ('cristobal-henriquez', 'Cristóbal Henríquez',
       'Terrenos, parcelas y sitios hacia el poniente: Labranza, Amanecer y alrededores. Conozco la zona calle por calle.',
       1, 4.2::numeric, 7, false, ARRAY[14,11], ARRAY[105,120,NULL, 103,90,NULL], 9,
       NULL, '+56 9 5550 0102', NULL, NULL),
      ('paola-neira', 'Paola Neira Cárcamo',
       'Arriendos y administración de propiedades en Santa Rosa y el oriente de la ciudad. Me encargo de que tu propiedad esté siempre arrendada y bien cuidada.',
       1, NULL::numeric, 1, false, ARRAY[2,3], ARRAY[102,60,40000, 104,45,NULL], 1,
       '+56 9 5550 0103', NULL, 'paola@demo.zass.local', NULL),
      ('ignacio-millar', 'Ignacio Millar',
       NULL,
       1, NULL::numeric, 0, false, ARRAY[15], ARRAY[104,45,NULL], 0,
       NULL, '+56 9 5550 0104', NULL, NULL),

      ('andrea-quilodran', 'Andrea Quilodrán',
       'Abogada de familia y de arriendos. Escribo los contratos en un lenguaje que se entiende, y acompaño a mis clientes hasta que el trámite termina. Primera conversación sin costo para saber si puedo ayudarte.',
       2, 4.9::numeric, 31, true, ARRAY[1], ARRAY[202,45,35000, 204,60,180000, 205,60,NULL], 5,
       '+56 9 5550 0201', '+56 9 5550 0201', 'andrea@demo.zass.local', 'https://ejemplo.invalid/andrea'),
      ('felipe-antilef', 'Felipe Antilef Huenchumilla',
       'Derecho laboral y defensa de trabajadores. Atiendo en Ñielol y Caupolicán, y también por videollamada.',
       2, 4.5::numeric, 12, true, ARRAY[8,6], ARRAY[206,60,NULL, 207,45,NULL], 14,
       NULL, '+56 9 5550 0202', 'felipe@demo.zass.local', NULL),
      ('daniela-sepulveda', 'Daniela Sepúlveda',
       'Escrituras de compraventa, constitución de sociedades y revisión de contratos para pequeñas empresas de Temuco.',
       2, NULL::numeric, 2, false, ARRAY[5], ARRAY[201,60,NULL, 203,90,250000, 208,45,30000], 3,
       '+56 9 5550 0203', NULL, 'daniela@demo.zass.local', NULL),
      ('rodrigo-cayul', 'Rodrigo Cayul',
       'Cobranza de deudas y defensa del consumidor.',
       2, 3.8::numeric, 4, false, ARRAY[12,1], ARRAY[207,30,NULL], 20,
       NULL, '+56 9 5550 0204', NULL, NULL),

      ('camila-painemal', 'Camila Painemal',
       'Contadora para emprendedores y pymes. Dejo tu contabilidad al día para que te dediques a tu negocio, y te explico los números sin tecnicismos.',
       3, 4.7::numeric, 18, true, ARRAY[1,2], ARRAY[303,60,45000, 302,30,NULL, 305,45,25000], 4,
       '+56 9 5550 0301', '+56 9 5550 0301', 'camila@demo.zass.local', 'https://ejemplo.invalid/camila'),
      ('hector-troncoso', 'Héctor Troncoso',
       'Declaración de renta y trámites con el SII para personas. Atiendo en el poniente, con hora agendada.',
       3, 4.1::numeric, 9, false, ARRAY[11], ARRAY[301,30,20000, 306,30,NULL], 11,
       NULL, '+56 9 5550 0302', NULL, NULL),
      ('javiera-lagos', 'Javiera Lagos',
       'Liquidación de sueldos y finiquitos para pequeñas empresas.',
       3, NULL::numeric, 0, false, ARRAY[5,1], ARRAY[304,45,NULL, 307,45,NULL], 0,
       '+56 9 5550 0303', NULL, 'javiera@demo.zass.local', NULL),
      ('estudio-lonquimay', 'Estudio Contable Lonquimay',
       'Un equipo pequeño de contadores para empresas de Portal La Frontera y el sector poniente.',
       3, 4.4::numeric, 6, true, ARRAY[13,11], ARRAY[303,60,NULL, 306,30,NULL], 25,
       '+56 9 5550 0304', '+56 9 5550 0304', NULL, NULL),

      ('tomas-ibanez', 'Tomás Ibáñez',
       'Arquitecto. Diseño remodelaciones y ampliaciones de casas, y me encargo de los permisos municipales. Trabajo en Las Mariposas y el oriente.',
       4, 4.6::numeric, 8, true, ARRAY[4,3], ARRAY[404,90,80000, 403,60,NULL, 402,60,NULL], 7,
       NULL, '+56 9 5550 0401', 'tomas@demo.zass.local', 'https://ejemplo.invalid/tomas'),
      ('valentina-cortes', 'Valentina Cortés',
       'Anteproyectos de vivienda e inspección técnica de obra. Si vas a construir, hablemos antes de comprar el terreno.',
       4, NULL::numeric, 1, false, ARRAY[1], ARRAY[401,90,NULL, 405,60,NULL], 6,
       '+56 9 5550 0402', '+56 9 5550 0402', NULL, NULL),
      ('taller-condor', 'Taller Cóndor',
       'Cálculo estructural y certificados de recepción final.',
       4, 4.0::numeric, 3, false, ARRAY[10], ARRAY[406,60,NULL, 407,45,NULL], 30,
       NULL, NULL, 'contacto@demo.zass.local', NULL),

      ('luis-catrileo', 'Luis Catrileo',
       'Gasfitero con veinte años en Temuco. Llego a la hora que quedamos, dejo limpio y te explico qué le pasaba a tu cañería. Urgencias en Labranza y Amanecer.',
       5, 4.9::numeric, 42, true, ARRAY[14,11], ARRAY[502,60,25000, 503,90,45000], 1,
       NULL, '+56 9 5550 0501', NULL, NULL),
      ('mauricio-fuentealba', 'Mauricio Fuentealba',
       'Electricista autorizado. Instalaciones nuevas, tableros y reparaciones.',
       5, 4.6::numeric, 15, true, ARRAY[7,8], ARRAY[501,60,30000], 8,
       NULL, '+56 9 5550 0502', NULL, NULL),
      ('patricio-navarrete', 'Patricio Navarrete',
       'Pintura de interiores y exteriores. Presupuesto sin costo.',
       5, 4.2::numeric, 5, false, ARRAY[16,15], ARRAY[504,120,NULL], 16,
       NULL, '+56 9 5550 0503', NULL, NULL),
      ('sandra-huaiquil', 'Sandra Huaiquil',
       'Instalación de cerámica y piso flotante. Trabajo prolijo y a tiempo.',
       5, NULL::numeric, 2, false, ARRAY[15,16], ARRAY[505,180,NULL], 12,
       NULL, '+56 9 5550 0504', NULL, NULL),
      ('jonathan-reyes', 'Jonathan Reyes',
       'Instalación y mantención de aire acondicionado y estufas. Centro y Santa Rosa.',
       5, 4.3::numeric, 6, false, ARRAY[1,2], ARRAY[508,120,NULL, 510,60,20000], 3,
       NULL, '+56 9 5550 0505', 'jonathan@demo.zass.local', NULL),
      ('claudio-manquel', 'Claudio Manquel',
       'Reparación de techumbre antes del invierno. Parque Costanera y alrededores.',
       5, NULL::numeric, 0, false, ARRAY[3], ARRAY[506,180,NULL, 509,120,NULL], 0,
       NULL, '+56 9 5550 0506', NULL, NULL)
    ) AS t(slug, nombre, descr, rubro, prom, resenas, verif, zonas, servs, dias_pub, tel, wasap, correo, sitio)
  LOOP
    u := gen_random_uuid();
    p := gen_random_uuid();

    INSERT INTO usuarios (id, correo, hash_contrasena, nombre, correo_verificado,
                          estado, creado_en, actualizado_en)
    VALUES (u, 'demo-' || r.slug || '@zass.local', 'x', r.nombre, true, 'Activa', now(), now());

    INSERT INTO perfiles_prestador (id, usuario_id, rubro_id, slug, nombre_publico, descripcion,
                                    estado, origen_ubicacion, telefono_publico, correo_publico,
                                    whatsapp, sitio_web, promedio_calificacion, total_resenas,
                                    version, publicado_en, actualizado_en)
    VALUES (p, u, r.rubro, r.slug, r.nombre, r.descr, 'Publicado', 'Declarada',
            r.tel, r.correo, r.wasap, r.sitio, r.prom, r.resenas, 0,
            now() - make_interval(days => r.dias_pub), now());

    INSERT INTO suscripciones (id, perfil_prestador_id, plan_id, estado, inicia_en,
                               vigente_hasta, renovacion_automatica)
    VALUES (gen_random_uuid(), p, 1, 'Prueba', now() - interval '1 day',
            now() + interval '30 days', false);

    FOR i IN 1 .. array_length(r.zonas, 1) LOOP
      INSERT INTO coberturas (id, perfil_prestador_id, zona_id, es_principal, desde_en)
      VALUES (gen_random_uuid(), p, r.zonas[i], i = 1, now());
    END LOOP;

    i := 1;
    WHILE i <= array_length(r.servs, 1) LOOP
      INSERT INTO prestador_servicios (id, perfil_prestador_id, servicio_id, duracion_minutos,
                                       modo_agenda, precio_referencia, activo)
      VALUES (gen_random_uuid(), p, r.servs[i], r.servs[i + 1], 'Automatico', r.servs[i + 2], true);
      i := i + 3;
    END LOOP;

    IF r.verif THEN
      INSERT INTO credenciales (id, perfil_prestador_id, tipo, nombre, obtenida_en,
                                estado, sello_en, sello_por, creada_en, esperando_prestador)
      VALUES (gen_random_uuid(), p, 'Titulo', 'Título profesional', DATE '2018-03-15',
              'Verificada', now(), gen_random_uuid(), now(), false);
    END IF;
  END LOOP;
END $$;

SELECT count(*) AS demo_sembrados FROM usuarios WHERE correo::text LIKE 'demo-%';
