-- Gente de DEMOSTRACION para mirar el sitio completo en la base de DESARROLLO (`zass` en
-- 127.0.0.1:5433). JQ-6.
--
-- Que deja: 26 prestadores visibles (22 en Temuco por sector, 4 en Padre Las Casas) en los 5
-- rubros, mas dos casos de borde que NO deben salir en la busqueda (una suscripcion vencida y un
-- perfil en borrador); 14 personas que buscan; 94 resenas de verdad (filas en `resenas`, con su
-- contacto), y horarios con sus bloques para 8 semanas, para poder agendar sin crear nada a mano.
--
-- Son personas INVENTADAS. Nadie tiene telefono, WhatsApp ni sitio web: no se inventan numeros
-- que podrian ser de alguien. La unica excepcion es opcional (ver «Telefono de Marcela»).
--
-- SOLO DESARROLLO. La guarda de abajo aborta en cualquier base que no sea `zass` en
-- 127.0.0.1:5433 (tambien en `zass_pruebas`, cuyas pruebas de busqueda esperan una base sin
-- prestadores). Las cuentas usan la contrasena `demo-zass-2026`, PUBLICA a proposito, y el hash
-- es su Argon2id real (RN-S1-03, DT-07). Esto no se lleva a produccion.
--
-- Idempotente: se puede correr las veces que se quiera. Busca por correo y por slug y actualiza
-- en vez de duplicar; no borra en cascada (no toca citas, cupos ni pagos que hayan dejado los
-- guiones de navegador). Cada corrida extiende la ventana de bloques a 8 semanas desde hoy y
-- deja la suscripcion de prueba vigente 30 dias mas.
--
-- Telefono de Marcela (opcional): `mirar-agendar.mjs` exige que `marcela-soto` tenga telefono,
-- WhatsApp y correo. Sin un numero, el sembrado funciona igual y ese paso del guion sale en rojo.
-- Si quieres verlo en verde, pasa un numero TUYO al correrlo:  -v tel_demo="+56 9 ...". No queda
-- en el repositorio.
--
-- Uso (PowerShell, desde la raiz del proyecto; psql esta en el checkout principal):
--   & C:\Users\joaqu\zass-app\herramientas\pgsql\bin\psql.exe -h 127.0.0.1 -p 5433 -U zass -d zass -v ON_ERROR_STOP=1 -f frontend\pruebas-navegador\datos\sembrar-demo.sql
-- Para quitarlo todo: limpiar-demo.sql.

\set ON_ERROR_STOP on
-- Antes que nada: el archivo es UTF-8. Sin esto, psql en Windows toma la pagina de codigos de la
-- consola (WIN1252/850) y guarda «años» como «aÃ±os». La comprobacion del final lo detecta.
SET client_encoding = 'UTF8';

-- ------------------------------------------------------------------ Guarda de base
DO $$
BEGIN
  IF current_database() <> 'zass'
     OR inet_server_port() IS DISTINCT FROM 5433
     OR inet_server_addr() IS NULL
     OR inet_server_addr() NOT IN ('127.0.0.1'::inet, '::1'::inet) THEN
    RAISE EXCEPTION 'GUARDA: este sembrado es solo para la base de desarrollo `zass` en 127.0.0.1:5433. Estas en «%» en %:%. No se hizo nada.',
      current_database(), coalesce(host(inet_server_addr()), 'socket local'), coalesce(inet_server_port()::text, '?');
  END IF;
END $$;

-- ------------------------------------------------------------------ Telefono opcional
\if :{?tel_demo}
SELECT set_config('zass.tel_demo', :'tel_demo', false) IS NOT NULL AS telefono_de_marcela;
\endif

BEGIN;

-- ------------------------------------------------------------------ Datos
-- caso: normal | vencido | borrador.  horario: patron de demo_patrones, o NULL (sin horario).
CREATE TEMP TABLE demo_prestadores ON COMMIT DROP AS
SELECT * FROM (VALUES
  ( 1, 'marcela-soto', 'Marcela Soto Rivas',
    'Llevo quince años ayudando a familias de Temuco a comprar y vender su casa sin sobresaltos. Me gusta explicar cada paso, de la tasación a la firma, y que mis clientes sepan siempre cómo va todo. Atiendo el centro y Pedro de Valdivia, con visitas los fines de semana.',
    1::smallint, 'normal', NULL::text),
  ( 2, 'cristobal-henriquez', 'Cristóbal Henríquez',
    'Terrenos, parcelas y sitios hacia el poniente: Labranza, Amanecer y alrededores. Conozco la zona calle por calle.',
    1, 'normal', 'oficina'),
  ( 3, 'paola-neira', 'Paola Neira Cárcamo',
    'Arriendos y administración de propiedades en Santa Rosa y el oriente de la ciudad. Me encargo de que tu propiedad esté siempre arrendada y bien cuidada.',
    1, 'normal', 'oficina'),
  ( 4, 'ignacio-millar', 'Ignacio Millar',
    'Arriendo departamentos y casas en el sur de Temuco. Recién empiezo en ZASS!',
    1, 'normal', 'tarde'),
  ( 5, 'veronica-lienlaf', 'Verónica Lienlaf',
    'Corredora en Padre Las Casas desde 2016. Vendo y arriendo casas en la comuna y en el sur de Temuco, y te acompaño en el papeleo con el banco hasta la entrega de llaves.',
    1, 'normal', 'oficina'),
  ( 6, 'andrea-quilodran', 'Andrea Quilodrán',
    'Abogada de familia y de arriendos. Escribo los contratos en un lenguaje que se entiende, y acompaño a mis clientes hasta que el trámite termina. Primera conversación sin costo para saber si puedo ayudarte.',
    2, 'normal', 'oficina'),
  ( 7, 'felipe-antilef', 'Felipe Antilef Huenchumilla',
    'Derecho laboral y defensa de trabajadores. Atiendo en Ñielol y Caupolicán, y también por videollamada.',
    2, 'normal', 'oficina'),
  ( 8, 'daniela-sepulveda', 'Daniela Sepúlveda',
    'Escrituras de compraventa, constitución de sociedades y revisión de contratos para pequeñas empresas de Temuco.',
    2, 'normal', 'oficina'),
  ( 9, 'rodrigo-cayul', 'Rodrigo Cayul',
    'Cobranza de deudas y defensa del consumidor. Atiendo en las tardes, en Barrio Inglés y el centro.',
    2, 'normal', 'tarde'),
  (10, 'gonzalo-nancupil', 'Gonzalo Ñancupil',
    'Abogado en Padre Las Casas. Tramito posesiones efectivas y redacto contratos de arriendo y compraventa. Atiendo con hora.',
    2, 'normal', 'mixto'),
  (11, 'camila-painemal', 'Camila Painemal',
    'Contadora para emprendedores y pymes. Dejo tu contabilidad al día para que te dediques a tu negocio, y te explico los números sin tecnicismos.',
    3, 'normal', 'oficina'),
  (12, 'hector-troncoso', 'Héctor Troncoso',
    'Declaración de renta y trámites con el SII para personas. Atiendo en el poniente, con hora agendada.',
    3, 'normal', 'tarde'),
  (13, 'javiera-lagos', 'Javiera Lagos',
    'Liquidación de sueldos y finiquitos para pequeñas empresas.',
    3, 'normal', 'mixto'),
  (14, 'estudio-lonquimay', 'Estudio Contable Lonquimay',
    'Un equipo pequeño de contadores para empresas de Portal La Frontera y el sector poniente.',
    3, 'normal', 'oficina'),
  (15, 'tomas-ibanez', 'Tomás Ibáñez',
    'Arquitecto. Diseño remodelaciones y ampliaciones de casas, y me encargo de los permisos municipales. Trabajo en Las Mariposas y el oriente.',
    4, 'normal', 'mixto'),
  (16, 'valentina-cortes', 'Valentina Cortés',
    'Anteproyectos de vivienda e inspección técnica de obra. Si vas a construir, hablemos antes de comprar el terreno.',
    4, 'normal', 'oficina'),
  (17, 'rocio-alarcon', 'Rocío Alarcón Vergara',
    'Ingeniera civil. Hago cálculo estructural para ampliaciones y segundos pisos, y preparo lo necesario para la recepción final en la Dirección de Obras.',
    4, 'normal', 'oficina'),
  (18, 'luis-catrileo', 'Luis Catrileo',
    'Gasfitero con veinte años en Temuco. Llego a la hora que quedamos, dejo limpio y te explico qué le pasaba a tu cañería. Urgencias en Labranza y Amanecer.',
    5, 'normal', 'oficio'),
  (19, 'mauricio-fuentealba', 'Mauricio Fuentealba',
    'Electricista autorizado. Instalaciones nuevas, tableros y reparaciones.',
    5, 'normal', 'oficio'),
  (20, 'patricio-navarrete', 'Patricio Navarrete',
    'Pintura de interiores y exteriores. Presupuesto sin costo.',
    5, 'normal', 'oficio'),
  (21, 'sandra-huaiquil', 'Sandra Huaiquil',
    'Instalación de cerámica y piso flotante. Trabajo prolijo y a tiempo.',
    5, 'normal', 'oficio'),
  (22, 'jonathan-reyes', 'Jonathan Reyes',
    'Instalación y mantención de aire acondicionado y estufas. Centro y Santa Rosa.',
    5, 'normal', 'oficio'),
  (23, 'claudio-manquel', 'Claudio Manquel',
    'Reparación de techumbre antes del invierno. Parque Costanera y alrededores.',
    5, 'normal', 'oficio'),
  (24, 'taller-condor', 'Taller Cóndor',
    'Soldadura, rejas, portones y estructuras metálicas. Trabajamos en taller y en terreno, en Temuco y alrededores.',
    5, 'normal', 'oficio'),
  (25, 'hernan-conuecar', 'Hernán Coñuecar',
    'Gasfíter en Padre Las Casas. Reparo filtraciones, cambio llaves y calefones, y atiendo urgencias en el día cuando se puede. Te digo el precio antes de empezar.',
    5, 'normal', 'oficio'),
  (26, 'marisol-calfuqueo', 'Marisol Calfuqueo',
    'Cerrajería en Padre Las Casas y el sur de Temuco. Aperturas, cambio de chapas y copias de llaves.',
    5, 'normal', 'oficio'),
  -- Casos de borde: existen, pero NO deben aparecer en la busqueda (RN-S3-09 y RN-S3-01).
  (27, 'ricardo-munoz', 'Ricardo Muñoz Sandoval',
    'Corredor de propiedades en el centro de Temuco. Venta de casas y departamentos.',
    1, 'vencido', NULL),
  (28, 'francisca-rojas', 'Francisca Rojas Peña',
    'Contadora. Declaraciones de renta e IVA para personas y pequeños negocios.',
    3, 'borrador', NULL)
) AS t(orden, slug, nombre, descr, rubro, caso, horario);

-- Zonas: Temuco por sector (1..16, migracion ZonasDeTemuco) y Padre Las Casas (370, comuna).
-- La de orden 1 es la principal.
CREATE TEMP TABLE demo_zonas ON COMMIT DROP AS
SELECT * FROM (VALUES
  ('marcela-soto', 1, 1), ('marcela-soto', 5, 2),
  ('cristobal-henriquez', 14, 1), ('cristobal-henriquez', 11, 2),
  ('paola-neira', 2, 1), ('paola-neira', 3, 2),
  ('ignacio-millar', 15, 1),
  ('veronica-lienlaf', 370, 1),
  ('andrea-quilodran', 1, 1),
  ('felipe-antilef', 8, 1), ('felipe-antilef', 6, 2),
  ('daniela-sepulveda', 5, 1),
  ('rodrigo-cayul', 12, 1), ('rodrigo-cayul', 1, 2),
  ('gonzalo-nancupil', 370, 1),
  ('camila-painemal', 1, 1), ('camila-painemal', 2, 2),
  ('hector-troncoso', 11, 1),
  ('javiera-lagos', 5, 1), ('javiera-lagos', 1, 2),
  ('estudio-lonquimay', 13, 1), ('estudio-lonquimay', 11, 2),
  ('tomas-ibanez', 4, 1), ('tomas-ibanez', 3, 2),
  ('valentina-cortes', 1, 1),
  ('rocio-alarcon', 10, 1), ('rocio-alarcon', 9, 2),
  ('luis-catrileo', 14, 1), ('luis-catrileo', 11, 2),
  ('mauricio-fuentealba', 7, 1), ('mauricio-fuentealba', 8, 2),
  ('patricio-navarrete', 16, 1), ('patricio-navarrete', 15, 2),
  ('sandra-huaiquil', 15, 1), ('sandra-huaiquil', 16, 2),
  ('jonathan-reyes', 1, 1), ('jonathan-reyes', 2, 2),
  ('claudio-manquel', 3, 1),
  ('taller-condor', 10, 1),
  ('hernan-conuecar', 370, 1),
  ('marisol-calfuqueo', 370, 1), ('marisol-calfuqueo', 15, 2),
  ('ricardo-munoz', 1, 1),
  ('francisca-rojas', 8, 1)
) AS t(slug, zona_id, orden);

-- Servicios (catalogo de CatalogoSembrado). Marcela conserva EXACTAMENTE sus 3 servicios y
-- «Tasación comercial»: mirar-agendar.mjs los cuenta.
CREATE TEMP TABLE demo_servicios ON COMMIT DROP AS
SELECT * FROM (VALUES
  ('marcela-soto', 101, 60::smallint, 60000, 'Automatico'),
  ('marcela-soto', 103, 90, NULL::int, 'Automatico'),
  ('marcela-soto', 104, 60, NULL, 'Automatico'),
  ('cristobal-henriquez', 105, 120, NULL, 'Automatico'),
  ('cristobal-henriquez', 103, 90, NULL, 'Automatico'),
  ('paola-neira', 102, 60, 40000, 'Automatico'),
  ('paola-neira', 104, 45, NULL, 'Automatico'),
  ('ignacio-millar', 104, 45, NULL, 'Automatico'),
  ('veronica-lienlaf', 103, 60, NULL, 'Automatico'),
  ('veronica-lienlaf', 104, 45, NULL, 'Automatico'),
  ('veronica-lienlaf', 107, 60, NULL, 'Automatico'),
  ('andrea-quilodran', 202, 45, 35000, 'Manual'),
  ('andrea-quilodran', 204, 60, 180000, 'Manual'),
  ('andrea-quilodran', 205, 60, NULL, 'Manual'),
  ('felipe-antilef', 206, 60, NULL, 'Automatico'),
  ('felipe-antilef', 207, 45, NULL, 'Automatico'),
  ('daniela-sepulveda', 201, 60, NULL, 'Automatico'),
  ('daniela-sepulveda', 203, 90, 250000, 'Automatico'),
  ('daniela-sepulveda', 208, 45, 30000, 'Automatico'),
  ('rodrigo-cayul', 207, 30, NULL, 'Automatico'),
  ('gonzalo-nancupil', 205, 60, NULL, 'Automatico'),
  ('gonzalo-nancupil', 202, 45, 40000, 'Automatico'),
  ('camila-painemal', 303, 60, 45000, 'Automatico'),
  ('camila-painemal', 302, 30, NULL, 'Automatico'),
  ('camila-painemal', 305, 45, 25000, 'Automatico'),
  ('hector-troncoso', 301, 30, 20000, 'Automatico'),
  ('hector-troncoso', 306, 30, NULL, 'Automatico'),
  ('javiera-lagos', 304, 45, NULL, 'Automatico'),
  ('javiera-lagos', 307, 45, NULL, 'Automatico'),
  ('estudio-lonquimay', 303, 60, NULL, 'Automatico'),
  ('estudio-lonquimay', 306, 30, NULL, 'Automatico'),
  ('tomas-ibanez', 404, 90, 80000, 'Automatico'),
  ('tomas-ibanez', 403, 60, NULL, 'Automatico'),
  ('tomas-ibanez', 402, 60, NULL, 'Automatico'),
  ('valentina-cortes', 401, 90, NULL, 'Automatico'),
  ('valentina-cortes', 405, 60, NULL, 'Automatico'),
  ('rocio-alarcon', 406, 60, NULL, 'Automatico'),
  ('rocio-alarcon', 407, 45, NULL, 'Automatico'),
  ('luis-catrileo', 502, 60, 25000, 'Automatico'),
  ('luis-catrileo', 503, 90, 45000, 'Automatico'),
  ('mauricio-fuentealba', 501, 60, 30000, 'Automatico'),
  ('patricio-navarrete', 504, 120, NULL, 'Automatico'),
  ('sandra-huaiquil', 505, 180, NULL, 'Automatico'),
  ('jonathan-reyes', 508, 120, NULL, 'Automatico'),
  ('jonathan-reyes', 510, 60, 20000, 'Automatico'),
  ('claudio-manquel', 506, 180, NULL, 'Automatico'),
  ('taller-condor', 509, 120, NULL, 'Automatico'),
  ('hernan-conuecar', 502, 60, 25000, 'Automatico'),
  ('hernan-conuecar', 503, 90, 50000, 'Automatico'),
  ('marisol-calfuqueo', 507, 45, 20000, 'Automatico'),
  ('ricardo-munoz', 103, 60, NULL, 'Automatico'),
  ('francisca-rojas', 301, 30, NULL, 'Automatico')
) AS t(slug, servicio_id, minutos, precio, modo);

-- Credenciales verificadas (el sello). Quien no esta aqui no lleva sello.
CREATE TEMP TABLE demo_sellos ON COMMIT DROP AS
SELECT * FROM (VALUES
  ('marcela-soto', 'Curso', 'Curso de corretaje de propiedades', DATE '2012-06-20'),
  ('andrea-quilodran', 'Titulo', 'Título de abogada', DATE '2009-12-15'),
  ('felipe-antilef', 'Titulo', 'Título de abogado', DATE '2014-04-10'),
  ('camila-painemal', 'Titulo', 'Título de contadora auditora', DATE '2016-01-22'),
  ('estudio-lonquimay', 'Titulo', 'Título de contador auditor', DATE '2005-03-30'),
  ('tomas-ibanez', 'Titulo', 'Título de arquitecto', DATE '2011-08-05'),
  ('rocio-alarcon', 'Titulo', 'Título de ingeniera civil', DATE '2015-11-12'),
  ('luis-catrileo', 'Certificacion', 'Instalador de gas SEC', DATE '2008-05-14'),
  ('mauricio-fuentealba', 'Certificacion', 'Instalador eléctrico autorizado SEC', DATE '2013-09-02')
) AS t(slug, tipo, nombre, obtenida);

-- Horarios semanales (hora de pared en Chile; 1 = lunes). Las franjas de un mismo dia no se
-- pisan (R-4). La duracion de cada bloque es la del servicio.
CREATE TEMP TABLE demo_patrones ON COMMIT DROP AS
SELECT * FROM (VALUES
  ('oficina', 1::smallint, 1::smallint, TIME '09:00', TIME '13:00'),
  ('oficina', 2, 1, TIME '15:00', TIME '18:00'),
  ('oficina', 1, 2, TIME '09:00', TIME '13:00'),
  ('oficina', 2, 2, TIME '15:00', TIME '18:00'),
  ('oficina', 1, 3, TIME '09:00', TIME '13:00'),
  ('oficina', 2, 3, TIME '15:00', TIME '18:00'),
  ('oficina', 1, 4, TIME '09:00', TIME '13:00'),
  ('oficina', 2, 4, TIME '15:00', TIME '18:00'),
  ('oficina', 1, 5, TIME '09:00', TIME '13:00'),
  ('oficina', 2, 5, TIME '15:00', TIME '18:00'),
  ('oficio', 1, 1, TIME '09:00', TIME '13:00'),
  ('oficio', 2, 1, TIME '14:00', TIME '19:00'),
  ('oficio', 1, 2, TIME '09:00', TIME '13:00'),
  ('oficio', 2, 2, TIME '14:00', TIME '19:00'),
  ('oficio', 1, 3, TIME '09:00', TIME '13:00'),
  ('oficio', 2, 3, TIME '14:00', TIME '19:00'),
  ('oficio', 1, 4, TIME '09:00', TIME '13:00'),
  ('oficio', 2, 4, TIME '14:00', TIME '19:00'),
  ('oficio', 1, 5, TIME '09:00', TIME '13:00'),
  ('oficio', 2, 5, TIME '14:00', TIME '19:00'),
  ('oficio', 1, 6, TIME '09:00', TIME '13:00'),
  ('tarde', 1, 1, TIME '15:00', TIME '19:00'),
  ('tarde', 1, 2, TIME '15:00', TIME '19:00'),
  ('tarde', 1, 3, TIME '15:00', TIME '19:00'),
  ('tarde', 1, 4, TIME '15:00', TIME '19:00'),
  ('tarde', 1, 5, TIME '15:00', TIME '19:00'),
  ('mixto', 1, 1, TIME '09:00', TIME '13:00'),
  ('mixto', 1, 2, TIME '15:00', TIME '19:00'),
  ('mixto', 1, 3, TIME '09:00', TIME '13:00'),
  ('mixto', 1, 4, TIME '15:00', TIME '19:00'),
  ('mixto', 1, 5, TIME '09:00', TIME '13:00')
) AS t(patron, franja, dia, desde, hasta);

-- Personas que buscan. Sus nombres no se ven en ninguna parte publica: las resenas publicas no
-- muestran autor (CU-16 §7).
CREATE TEMP TABLE demo_buscadores ON COMMIT DROP AS
SELECT * FROM (VALUES
  ( 1, 'catalina-rios', 'Catalina Ríos'),
  ( 2, 'matias-contreras', 'Matías Contreras'),
  ( 3, 'fernanda-salazar', 'Fernanda Salazar'),
  ( 4, 'sebastian-vidal', 'Sebastián Vidal'),
  ( 5, 'constanza-parra', 'Constanza Parra'),
  ( 6, 'diego-sanhueza', 'Diego Sanhueza'),
  ( 7, 'valeria-jara', 'Valeria Jara'),
  ( 8, 'benjamin-toledo', 'Benjamín Toledo'),
  ( 9, 'antonia-riquelme', 'Antonia Riquelme'),
  (10, 'nicolas-fuentes', 'Nicolás Fuentes'),
  (11, 'carolina-leiva', 'Carolina Leiva'),
  (12, 'ignacia-cifuentes', 'Ignacia Cifuentes'),
  (13, 'martin-garrido', 'Martín Garrido'),
  (14, 'josefa-molina', 'Josefa Molina')
) AS t(orden, slug, nombre);

-- Resenas. Aspectos: solo la lista cerrada de ReglasDeResenas (puntualidad, claridad,
-- precio_acordado). Comentario de 20 a 1500 caracteres (ck_resena_comentario).
CREATE TEMP TABLE demo_resenas ON COMMIT DROP AS
SELECT * FROM (VALUES
  ('marcela-soto', 1, 5::smallint, ARRAY['claridad']::text[], 'Vendimos la casa de mi mamá en Pedro de Valdivia en menos de tres meses. Marcela nos explicó cada paso y siempre contestó.'),
  ('marcela-soto', 2, 5, ARRAY['puntualidad','claridad'], 'La tasación llegó en la fecha que dijo y con un informe fácil de entender. La recomiendo.'),
  ('marcela-soto', 3, 5, ARRAY[]::text[], 'Nos ayudó a arrendar el departamento del centro. Revisó a los postulantes con calma y elegimos bien.'),
  ('marcela-soto', 4, 4, ARRAY['claridad'], 'Buena corredora, muy ordenada con los papeles. La firma en la notaría se atrasó una semana, pero no fue culpa de ella.'),
  ('marcela-soto', 5, 5, ARRAY['precio_acordado'], 'Cobró la comisión que habíamos conversado, sin sorpresas al final. Muy profesional.'),
  ('marcela-soto', 6, 5, ARRAY['puntualidad'], 'Llegó puntual a todas las visitas, incluso un sábado en la mañana.'),
  ('marcela-soto', 7, 4, ARRAY[]::text[], 'Atenta y conoce bien el sector. Me hubiera gustado recibir más fotos de las visitas, pero el resultado fue bueno.'),
  ('marcela-soto', 8, 5, ARRAY['claridad'], 'Primera vez que vendíamos una propiedad y nos sentimos acompañados de principio a fin.'),

  ('cristobal-henriquez', 1, 4, ARRAY[]::text[], 'Me mostró varias parcelas en Labranza y conocía bien los derechos de agua de cada una.'),
  ('cristobal-henriquez', 2, 5, ARRAY['claridad'], 'Compré un sitio en Amanecer con su ayuda. Revisó que los papeles estuvieran en orden antes de firmar.'),
  ('cristobal-henriquez', 3, 4, ARRAY['puntualidad'], 'Cumplido con los horarios. Las visitas a terreno las coordinó rápido.'),
  ('cristobal-henriquez', 4, 4, ARRAY[]::text[], 'Buena atención. Demoró un poco en enviar la promesa de compraventa, pero todo salió bien.'),

  ('paola-neira', 1, 5, ARRAY[]::text[], 'Administra mi departamento hace un año y nunca he tenido que preocuparme del arriendo.'),
  ('paola-neira', 2, 4, ARRAY['claridad'], 'Responde rápido y manda un resumen mensual claro. Cobra un poco más que otros, pero vale la pena.'),

  ('veronica-lienlaf', 1, 5, ARRAY[]::text[], 'Nos vendió la casa en Padre Las Casas y se encargó de todo el trámite con el banco.'),
  ('veronica-lienlaf', 2, 4, ARRAY['puntualidad'], 'Muy amable y puntual. Le costó encontrar arrendatario, pero al final llegó una buena familia.'),
  ('veronica-lienlaf', 3, 5, ARRAY['claridad'], 'Explica todo con paciencia. Se nota que conoce la comuna.'),

  ('andrea-quilodran', 1, 5, ARRAY['claridad'], 'Me explicó el contrato de arriendo cláusula por cláusula. Por primera vez entendí lo que estaba firmando.'),
  ('andrea-quilodran', 2, 5, ARRAY[]::text[], 'Nos llevó el divorcio de mutuo acuerdo con mucho respeto y sin alargar las cosas.'),
  ('andrea-quilodran', 3, 5, ARRAY['claridad'], 'La posesión efectiva de mi papá salió en los plazos que me dijo. Siempre supe en qué etapa iba.'),
  ('andrea-quilodran', 4, 5, ARRAY['puntualidad'], 'Atiende a la hora y responde los correos el mismo día.'),
  ('andrea-quilodran', 5, 4, ARRAY['precio_acordado'], 'Buena abogada. Los honorarios fueron los acordados, aunque el trámite en el Registro Civil se demoró más de lo esperado.'),
  ('andrea-quilodran', 6, 5, ARRAY[]::text[], 'Me ayudó con un problema con mi arrendador. Muy clara para explicar qué podía exigir y qué no.'),
  ('andrea-quilodran', 7, 5, ARRAY['claridad'], 'Recomendable. Revisó el contrato antes de que lo firmara y me evitó un mal negocio.'),
  ('andrea-quilodran', 8, 5, ARRAY[]::text[], 'Cercana y profesional. La primera conversación fue sin costo, como decía su ficha.'),
  ('andrea-quilodran', 9, 4, ARRAY[]::text[], 'Muy buena atención. Tuvimos que cambiar la hora una vez por su agenda, pero avisó con tiempo.'),

  ('felipe-antilef', 1, 5, ARRAY['claridad'], 'Me asesoró en el finiquito y logramos que la empresa pagara las horas extra que me debían.'),
  ('felipe-antilef', 2, 4, ARRAY[]::text[], 'Buen abogado laboral. La videollamada funcionó bien y me ahorró viajar al centro.'),
  ('felipe-antilef', 3, 5, ARRAY[]::text[], 'Me explicó mis opciones antes de demandar. Muy honesto sobre lo que se podía lograr.'),
  ('felipe-antilef', 4, 5, ARRAY['puntualidad'], 'Puntual y ordenado con los plazos de la Inspección del Trabajo.'),
  ('felipe-antilef', 5, 4, ARRAY['precio_acordado'], 'Cobró lo conversado. A veces cuesta ubicarlo por teléfono, pero responde por correo.'),

  ('daniela-sepulveda', 1, 5, ARRAY['claridad'], 'Constituimos la sociedad de nuestro taller con ella. Todo quedó listo en dos semanas.'),
  ('daniela-sepulveda', 2, 4, ARRAY[]::text[], 'Revisó el contrato con un proveedor y encontró dos cláusulas que nos perjudicaban.'),

  ('rodrigo-cayul', 1, 4, ARRAY[]::text[], 'Logró cobrar una deuda que llevaba más de un año pendiente. Paciente con el proceso.'),
  ('rodrigo-cayul', 2, 4, ARRAY['claridad'], 'Me orientó bien en un reclamo contra una tienda. Simple y directo.'),
  ('rodrigo-cayul', 3, 2, ARRAY[]::text[], 'La gestión de cobranza avanzó, pero me costó mucho que me contestara el teléfono.'),

  ('camila-painemal', 1, 4, ARRAY[]::text[], 'Ordenó la contabilidad de mi almacén en dos meses. A veces se demora en responder, pero cumple.'),
  ('camila-painemal', 2, 5, ARRAY['claridad'], 'Me hizo el inicio de actividades y me explicó qué boletas tenía que emitir. Muy clara.'),
  ('camila-painemal', 3, 5, ARRAY[]::text[], 'Lleva la contabilidad de mi pyme hace un año. Nunca se le ha pasado una declaración.'),
  ('camila-painemal', 4, 5, ARRAY['puntualidad'], 'Siempre entrega el IVA a tiempo y me avisa cuánto voy a pagar antes del día 20.'),
  ('camila-painemal', 5, 5, ARRAY['claridad'], 'Explica los números sin enredos. Por fin entiendo el balance de mi negocio.'),
  ('camila-painemal', 6, 4, ARRAY['precio_acordado'], 'Precio justo para lo que hace. En abril estuvo con mucha pega y respondía más lento.'),
  ('camila-painemal', 7, 5, ARRAY[]::text[], 'Muy recomendable para emprendedores que recién parten.'),

  ('hector-troncoso', 1, 4, ARRAY[]::text[], 'Me hizo la declaración de renta y recuperé más devolución de la que esperaba.'),
  ('hector-troncoso', 2, 5, ARRAY['claridad'], 'Me explicó paso a paso cómo usar la factura electrónica del SII.'),
  ('hector-troncoso', 3, 4, ARRAY['puntualidad'], 'Atiende con hora y es puntual. La oficina es chica, pero cómoda.'),
  ('hector-troncoso', 4, 4, ARRAY['precio_acordado'], 'Cobró lo acordado. Me hubiera gustado un resumen por escrito al final.'),

  ('estudio-lonquimay', 1, 5, ARRAY[]::text[], 'Llevan la contabilidad de nuestra empresa de transporte sin problemas. Buen equipo.'),
  ('estudio-lonquimay', 2, 4, ARRAY['claridad'], 'Responden rápido por correo y siempre con la información completa.'),
  ('estudio-lonquimay', 3, 4, ARRAY[]::text[], 'Buen servicio. Al principio tardaron en ponerse al día con nuestros papeles atrasados.'),

  ('tomas-ibanez', 1, 5, ARRAY['claridad','puntualidad'], 'Nos hizo el proyecto de ampliación y se encargó del permiso municipal. Muy ordenado con los plazos.'),
  ('tomas-ibanez', 2, 5, ARRAY[]::text[], 'Diseñó la remodelación de nuestra cocina y quedó mejor de lo que imaginábamos.'),
  ('tomas-ibanez', 3, 4, ARRAY['precio_acordado'], 'Buen arquitecto. El presupuesto se respetó, aunque los planos tardaron una semana más.'),
  ('tomas-ibanez', 4, 5, ARRAY['claridad'], 'Regularizó la ampliación de la casa de mis papás. Supo cómo hablar con la Dirección de Obras.'),
  ('tomas-ibanez', 5, 5, ARRAY[]::text[], 'Escucha lo que uno quiere y propone ideas sin imponerlas.'),

  ('valentina-cortes', 1, 5, ARRAY['claridad'], 'Nos acompañó como ITO durante toda la obra y detectó varios errores a tiempo.'),

  ('rocio-alarcon', 1, 5, ARRAY['claridad'], 'Hizo el cálculo estructural del segundo piso y nos explicó por qué hacía falta reforzar.'),
  ('rocio-alarcon', 2, 5, ARRAY[]::text[], 'Gracias a su informe obtuvimos la recepción final sin observaciones.'),
  ('rocio-alarcon', 3, 4, ARRAY['puntualidad'], 'Muy profesional. Las visitas a terreno siempre a la hora.'),

  ('luis-catrileo', 1, 5, ARRAY['puntualidad','precio_acordado'], 'Llegó a la hora, encontró la filtración debajo del lavaplatos y dejó todo limpio. Me cobró lo que había dicho.'),
  ('luis-catrileo', 2, 5, ARRAY[]::text[], 'Me instaló el calefón nuevo y revisó la ventilación. Trabajo prolijo.'),
  ('luis-catrileo', 3, 5, ARRAY['puntualidad'], 'Lo llamé por una urgencia un domingo en Labranza y llegó en menos de una hora.'),
  ('luis-catrileo', 4, 5, ARRAY['claridad'], 'Explica qué falló y cómo evitar que vuelva a pasar. Así da gusto.'),
  ('luis-catrileo', 5, 4, ARRAY[]::text[], 'Buen trabajo. Tuvo que volver al otro día a buscar un repuesto, pero cumplió.'),
  ('luis-catrileo', 6, 5, ARRAY['precio_acordado'], 'Precio justo y sin cobros extra. Lo vuelvo a llamar sin duda.'),
  ('luis-catrileo', 7, 5, ARRAY[]::text[], 'Cambió todas las llaves del baño en una mañana. Muy ordenado.'),
  ('luis-catrileo', 8, 5, ARRAY[]::text[], 'Amable y cuidadoso dentro de la casa. Se puso cubrecalzado sin que se lo pidiera.'),
  ('luis-catrileo', 9, 4, ARRAY['puntualidad'], 'Llegó un poco más tarde de lo acordado, pero avisó y el trabajo quedó impecable.'),
  ('luis-catrileo', 10, 5, ARRAY[]::text[], 'El mejor gasfíter que hemos tenido en Amanecer.'),

  ('mauricio-fuentealba', 1, 5, ARRAY[]::text[], 'Cambió el tablero eléctrico de la casa y dejó todo rotulado.'),
  ('mauricio-fuentealba', 2, 4, ARRAY['puntualidad'], 'Cumplió con la hora. El trabajo quedó bien, aunque dejó algo de polvo.'),
  ('mauricio-fuentealba', 3, 5, ARRAY['claridad'], 'Me explicó por qué saltaba el automático y lo solucionó en el día.'),
  ('mauricio-fuentealba', 4, 5, ARRAY['precio_acordado'], 'Hizo la instalación de la cocina eléctrica al precio que cotizó.'),
  ('mauricio-fuentealba', 5, 4, ARRAY[]::text[], 'Buen electricista. Tuvimos que coordinar dos veces porque le faltó material.'),
  ('mauricio-fuentealba', 6, 5, ARRAY[]::text[], 'Responsable y prolijo. Revisó toda la instalación antes de irse.'),

  ('patricio-navarrete', 1, 4, ARRAY[]::text[], 'Pintó el living y los dormitorios en tres días. Buen resultado.'),
  ('patricio-navarrete', 2, 5, ARRAY['precio_acordado'], 'El presupuesto fue gratis y cobró exactamente eso. Muy prolijo con los bordes.'),
  ('patricio-navarrete', 3, 4, ARRAY[]::text[], 'Buen trabajo en la fachada. Un día no llegó por la lluvia, pero avisó.'),

  ('sandra-huaiquil', 1, 5, ARRAY[]::text[], 'Instaló el piso flotante del segundo piso y quedó perfecto. Muy prolija.'),
  ('sandra-huaiquil', 2, 5, ARRAY['puntualidad'], 'Cumplió con los días que dijo y dejó todo limpio.'),

  ('jonathan-reyes', 1, 4, ARRAY[]::text[], 'Instaló el aire acondicionado del dormitorio. Funciona bien, aunque el trabajo tomó más de lo esperado.'),
  ('jonathan-reyes', 2, 5, ARRAY['precio_acordado'], 'Hizo la mantención de la estufa a pellet antes del invierno. Precio justo.'),
  ('jonathan-reyes', 3, 4, ARRAY['claridad'], 'Me explicó cómo limpiar los filtros para que el equipo dure más.'),
  ('jonathan-reyes', 4, 4, ARRAY[]::text[], 'Buen servicio. Hay que agendar con tiempo porque tiene harta demanda.'),

  ('taller-condor', 1, 5, ARRAY[]::text[], 'Nos hicieron el portón corredero y quedó firme y bien terminado.'),
  ('taller-condor', 2, 4, ARRAY[]::text[], 'Buenas rejas para el antejardín. La instalación se atrasó unos días.'),
  ('taller-condor', 3, 5, ARRAY['precio_acordado'], 'Cumplieron con el precio y el diseño que acordamos.'),

  ('hernan-conuecar', 1, 4, ARRAY['precio_acordado'], 'Cambió el calefón en una mañana. Tuvo que volver por una conexión, pero no me cobró la segunda visita.'),
  ('hernan-conuecar', 2, 5, ARRAY['puntualidad'], 'Llegó rápido por una filtración en la cocina. Muy buena disposición.'),
  ('hernan-conuecar', 3, 5, ARRAY[]::text[], 'Destapó el alcantarillado del patio y dejó todo limpio. Recomendado en Padre Las Casas.'),
  ('hernan-conuecar', 4, 4, ARRAY[]::text[], 'Buen trabajo. Hubo que esperar un repuesto, pero me mantuvo informado.'),
  ('hernan-conuecar', 5, 5, ARRAY['claridad'], 'Me dijo el precio antes de empezar, tal como dice su ficha.'),

  ('marisol-calfuqueo', 1, 5, ARRAY['puntualidad'], 'Me quedé fuera de la casa un domingo y llegó en media hora. Abrió sin dañar la chapa.'),
  ('marisol-calfuqueo', 2, 4, ARRAY[]::text[], 'Cambió las chapas de la casa nueva. Buen trabajo, aunque cuesta conseguir hora.')
) AS t(slug, n, calificacion, aspectos, comentario);

-- ------------------------------------------------------------------ Cuentas
-- Argon2id de `demo-zass-2026` (ver la cabecera). Mismo valor que antes de JQ-6.
CREATE TEMP TABLE demo_clave ON COMMIT DROP AS
SELECT '$argon2id$v=19$m=19456,t=2,p=1$G74jP/dhSzmF2WXXOC5OzQ==$Zv3DBlK3ZOrEHT0HAKsSn21y1jfWN2HCt8lMYmCQy2g='::text AS hash;

INSERT INTO usuarios (id, correo, hash_contrasena, nombre, correo_verificado, estado, creado_en, actualizado_en)
SELECT gen_random_uuid(), 'demo-' || d.slug || '@zass.local', c.hash, d.nombre, true, 'Activa', now(), now()
  FROM demo_prestadores d CROSS JOIN demo_clave c
ON CONFLICT (correo) DO UPDATE
   SET hash_contrasena = EXCLUDED.hash_contrasena, nombre = EXCLUDED.nombre,
       correo_verificado = true, estado = 'Activa', actualizado_en = now();

INSERT INTO usuarios (id, correo, hash_contrasena, nombre, correo_verificado, estado, creado_en, actualizado_en)
SELECT gen_random_uuid(), 'demo-cliente-' || b.slug || '@zass.local', c.hash, b.nombre, true, 'Activa', now(), now()
  FROM demo_buscadores b CROSS JOIN demo_clave c
ON CONFLICT (correo) DO UPDATE
   SET hash_contrasena = EXCLUDED.hash_contrasena, nombre = EXCLUDED.nombre,
       correo_verificado = true, estado = 'Activa', actualizado_en = now();

CREATE TEMP TABLE demo_u_prestador ON COMMIT DROP AS
SELECT d.*, u.id AS usuario_id
  FROM demo_prestadores d JOIN usuarios u ON u.correo::text = 'demo-' || d.slug || '@zass.local';

CREATE TEMP TABLE demo_u_buscador ON COMMIT DROP AS
SELECT b.*, u.id AS usuario_id
  FROM demo_buscadores b JOIN usuarios u ON u.correo::text = 'demo-cliente-' || b.slug || '@zass.local';

-- La cuenta es UNA: rol 1 buscador y rol 2 prestador (D9). Quien busca, solo el 1.
INSERT INTO usuario_roles (usuario_id, rol_id, asignado_en)
SELECT usuario_id, r.rol, now() FROM demo_u_prestador CROSS JOIN (VALUES (1::smallint), (2::smallint)) AS r(rol)
ON CONFLICT DO NOTHING;
INSERT INTO usuario_roles (usuario_id, rol_id, asignado_en)
SELECT usuario_id, 1, now() FROM demo_u_buscador
ON CONFLICT DO NOTHING;

-- Lo mismo que crea el registro (ServicioRegistro) para quien busca.
INSERT INTO perfiles_buscador (id, usuario_id, radio_preferido_km, acepta_avisos, canal_correo, canal_push, favoritos)
SELECT gen_random_uuid(), usuario_id, 10, true, true, false, '[]'::jsonb FROM demo_u_buscador
ON CONFLICT (usuario_id) DO NOTHING;

-- ------------------------------------------------------------------ Perfiles
-- Contacto: nadie lo tiene, salvo Marcela si se paso `tel_demo` (y su correo, en un dominio
-- reservado que nunca entrega, RFC 2606). Sin sitio web. El promedio y el total se calculan
-- mas abajo desde las resenas.
INSERT INTO perfiles_prestador (id, usuario_id, rubro_id, slug, nombre_publico, descripcion, estado,
                                origen_ubicacion, telefono_publico, correo_publico, whatsapp, sitio_web,
                                promedio_calificacion, total_resenas, version, publicado_en, actualizado_en)
SELECT gen_random_uuid(), d.usuario_id, d.rubro, d.slug, d.nombre, d.descr,
       CASE WHEN d.caso = 'borrador' THEN 'Borrador' ELSE 'Publicado' END,
       'Declarada',
       CASE WHEN d.slug = 'marcela-soto' THEN nullif(current_setting('zass.tel_demo', true), '') END,
       CASE WHEN d.slug = 'marcela-soto' THEN 'marcela.soto@correo.invalid' END,
       CASE WHEN d.slug = 'marcela-soto' THEN nullif(current_setting('zass.tel_demo', true), '') END,
       NULL, NULL, 0, 0,
       CASE WHEN d.caso = 'borrador' THEN NULL ELSE now() - make_interval(days => 3 + d.orden * 4) END,
       now()
  FROM demo_u_prestador d
ON CONFLICT (slug) DO UPDATE
   SET rubro_id = EXCLUDED.rubro_id, nombre_publico = EXCLUDED.nombre_publico,
       descripcion = EXCLUDED.descripcion, estado = EXCLUDED.estado,
       origen_ubicacion = EXCLUDED.origen_ubicacion,
       telefono_publico = EXCLUDED.telefono_publico, correo_publico = EXCLUDED.correo_publico,
       whatsapp = EXCLUDED.whatsapp, sitio_web = NULL,
       publicado_en = CASE WHEN EXCLUDED.estado = 'Publicado'
                           THEN coalesce(perfiles_prestador.publicado_en, EXCLUDED.publicado_en) END,
       version = perfiles_prestador.version + 1, actualizado_en = now();

CREATE TEMP TABLE demo_p ON COMMIT DROP AS
SELECT d.*, p.id AS perfil_id
  FROM demo_u_prestador d JOIN perfiles_prestador p ON p.slug = d.slug;

-- ------------------------------------------------------------------ Coberturas
-- Primero nadie es principal: ux_cobertura_principal admite una sola por perfil.
UPDATE coberturas c SET es_principal = false
  FROM demo_p p WHERE c.perfil_prestador_id = p.perfil_id AND c.es_principal;

INSERT INTO coberturas (id, perfil_prestador_id, zona_id, es_principal, desde_en)
SELECT gen_random_uuid(), p.perfil_id, z.zona_id, z.orden = 1, now()
  FROM demo_zonas z JOIN demo_p p ON p.slug = z.slug
ON CONFLICT (perfil_prestador_id, zona_id) DO UPDATE SET es_principal = EXCLUDED.es_principal;

DELETE FROM coberturas c
 USING demo_p p
 WHERE c.perfil_prestador_id = p.perfil_id
   AND NOT EXISTS (SELECT 1 FROM demo_zonas z WHERE z.slug = p.slug AND z.zona_id = c.zona_id);

-- ------------------------------------------------------------------ Servicios
INSERT INTO prestador_servicios (id, perfil_prestador_id, servicio_id, duracion_minutos, modo_agenda,
                                 precio_referencia, activo)
SELECT gen_random_uuid(), p.perfil_id, s.servicio_id, s.minutos, s.modo, s.precio, true
  FROM demo_servicios s JOIN demo_p p ON p.slug = s.slug
ON CONFLICT (perfil_prestador_id, servicio_id) DO UPDATE
   SET duracion_minutos = EXCLUDED.duracion_minutos, modo_agenda = EXCLUDED.modo_agenda,
       precio_referencia = EXCLUDED.precio_referencia, activo = true;

-- Lo que salio de la lista se apaga, no se borra: lo pueden referenciar horarios y citas.
UPDATE prestador_servicios ps SET activo = false
  FROM demo_p p
 WHERE ps.perfil_prestador_id = p.perfil_id AND ps.activo
   AND NOT EXISTS (SELECT 1 FROM demo_servicios s WHERE s.slug = p.slug AND s.servicio_id = ps.servicio_id);

-- ------------------------------------------------------------------ Suscripciones
-- Solo plan 1 («Prueba»). Una suscripcion vigente de OTRO plan (la que deja cupo-fundador.mjs)
-- no se toca. Nada en planes, cupos, cupones ni pagos.
UPDATE suscripciones s SET estado = 'Vencida', vigente_hasta = least(s.vigente_hasta, now() - interval '5 days')
  FROM demo_p p
 WHERE s.perfil_prestador_id = p.perfil_id AND p.caso = 'vencido' AND s.estado IN ('Prueba', 'Activa');

INSERT INTO suscripciones (id, perfil_prestador_id, plan_id, estado, inicia_en, vigente_hasta, renovacion_automatica)
SELECT gen_random_uuid(), p.perfil_id, 1, 'Vencida', now() - interval '35 days', now() - interval '5 days', false
  FROM demo_p p
 WHERE p.caso = 'vencido'
   AND NOT EXISTS (SELECT 1 FROM suscripciones s WHERE s.perfil_prestador_id = p.perfil_id);

UPDATE suscripciones s SET vigente_hasta = greatest(s.vigente_hasta, now() + interval '30 days')
  FROM demo_p p
 WHERE s.perfil_prestador_id = p.perfil_id AND p.caso <> 'vencido'
   AND s.estado IN ('Prueba', 'Activa') AND s.plan_id = 1;

INSERT INTO suscripciones (id, perfil_prestador_id, plan_id, estado, inicia_en, vigente_hasta, renovacion_automatica)
SELECT gen_random_uuid(), p.perfil_id, 1, 'Prueba', now() - interval '1 day', now() + interval '30 days', false
  FROM demo_p p
 WHERE p.caso <> 'vencido'
   AND NOT EXISTS (SELECT 1 FROM suscripciones s
                    WHERE s.perfil_prestador_id = p.perfil_id AND s.estado IN ('Prueba', 'Activa'));

-- ------------------------------------------------------------------ Credenciales
-- Id determinista por perfil. `sello_por` es obligatorio con estado Verificada
-- (ck_credencial_sello) y no tiene FK: un verificador ficticio y fijo.
INSERT INTO credenciales (id, perfil_prestador_id, tipo, nombre, obtenida_en, estado, sello_en, sello_por,
                          creada_en, esperando_prestador)
SELECT md5('zass-demo:cred:' || p.slug)::uuid, p.perfil_id, s.tipo, s.nombre, s.obtenida, 'Verificada',
       now(), md5('zass-demo:verificador')::uuid, now(), false
  FROM demo_sellos s JOIN demo_p p ON p.slug = s.slug
ON CONFLICT (id) DO UPDATE
   SET tipo = EXCLUDED.tipo, nombre = EXCLUDED.nombre, obtenida_en = EXCLUDED.obtenida_en,
       estado = 'Verificada', sello_en = coalesce(credenciales.sello_en, EXCLUDED.sello_en),
       sello_por = EXCLUDED.sello_por, esperando_prestador = false;

-- Las credenciales de un perfil demo son de este sembrado: se quita lo que no es la suya (las
-- «Título profesional» con id al azar del sembrado anterior, y el sello de quien ya no lo lleva).
DELETE FROM credenciales c
 USING demo_p p
 WHERE c.perfil_prestador_id = p.perfil_id
   AND (c.id <> md5('zass-demo:cred:' || p.slug)::uuid
        OR NOT EXISTS (SELECT 1 FROM demo_sellos s WHERE s.slug = p.slug));

-- ------------------------------------------------------------------ Contactos y resenas
-- Cada resena nace de un contacto sin cita de hace 25 a ~120 dias, y se publica entre 8 y 14 dias
-- despues: cumple RN-S5-02 (7 dias desde el contacto). Autor distinto dentro de cada prestador.
CREATE TEMP TABLE demo_r ON COMMIT DROP AS
SELECT r.*, p.perfil_id, b.usuario_id AS autor_id,
       md5('zass-demo:contacto:' || r.slug || ':' || r.n)::uuid AS contacto_id,
       md5('zass-demo:resena:' || r.slug || ':' || r.n)::uuid AS resena_id,
       now() - make_interval(days => 25 + r.n * 9 + p.orden % 5) AS contacto_en,
       now() - make_interval(days => 25 + r.n * 9 + p.orden % 5)
             + make_interval(days => 8 + (r.n + p.orden) % 7) AS publicada_en
  FROM demo_resenas r
  JOIN demo_p p ON p.slug = r.slug
  JOIN demo_u_buscador b ON b.orden = ((r.n - 1 + p.orden) % 14) + 1;

INSERT INTO contactos (id, buscador_id, perfil_prestador_id, cita_id, escaneo_id, origen, canal, ocurrido_en)
SELECT r.contacto_id, r.autor_id, r.perfil_id, NULL, NULL,
       CASE WHEN r.n % 3 = 0 THEN 'Busqueda' ELSE 'Ficha' END,
       CASE WHEN pp.whatsapp IS NOT NULL THEN 'Whatsapp' ELSE 'Formulario' END,
       r.contacto_en
  FROM demo_r r JOIN perfiles_prestador pp ON pp.id = r.perfil_id
ON CONFLICT (id) DO UPDATE
   SET buscador_id = EXCLUDED.buscador_id, perfil_prestador_id = EXCLUDED.perfil_prestador_id,
       origen = EXCLUDED.origen, canal = EXCLUDED.canal, ocurrido_en = EXCLUDED.ocurrido_en;

INSERT INTO resenas (id, contacto_id, perfil_prestador_id, autor_id, calificacion, comentario, tipo, estado,
                     aspectos, editada, publicada_en, editable_hasta)
SELECT r.resena_id, r.contacto_id, r.perfil_id, r.autor_id, r.calificacion, r.comentario, 'Servicio', 'Publicada',
       r.aspectos, false, r.publicada_en, r.publicada_en + interval '72 hours'
  FROM demo_r r
ON CONFLICT (id) DO UPDATE
   SET autor_id = EXCLUDED.autor_id, calificacion = EXCLUDED.calificacion, comentario = EXCLUDED.comentario,
       tipo = 'Servicio', estado = 'Publicada', aspectos = EXCLUDED.aspectos, editada = false,
       publicada_en = EXCLUDED.publicada_en, editable_hasta = EXCLUDED.editable_hasta;

-- Resenas y contactos que este sembrado creo y que ya no estan en la lista. Las que dejaron los
-- guiones de navegador (id al azar) no se tocan.
DELETE FROM contactos c
 USING demo_p p
 WHERE c.perfil_prestador_id = p.perfil_id
   AND c.id = ANY (SELECT md5('zass-demo:contacto:' || p.slug || ':' || g)::uuid FROM generate_series(1, 50) g)
   AND c.id NOT IN (SELECT contacto_id FROM demo_r);

-- ------------------------------------------------------------------ Reputacion
-- EL MISMO calculo que RepositorioResenasEfCore.RecalcularReputacionAsync: total y promedio de las
-- resenas Publicadas, redondeado a un decimal. Nunca cifras a mano.
UPDATE perfiles_prestador p
   SET total_resenas = r.total, promedio_calificacion = r.promedio
  FROM (SELECT dp.perfil_id,
               count(re.id)::int AS total,
               CASE WHEN count(re.id) = 0 THEN NULL
                    ELSE round(avg(re.calificacion)::numeric, 1) END AS promedio
          FROM demo_p dp
          LEFT JOIN resenas re ON re.perfil_prestador_id = dp.perfil_id AND re.estado = 'Publicada'
         GROUP BY dp.perfil_id) r
 WHERE p.id = r.perfil_id;

-- ------------------------------------------------------------------ Horarios
-- Sin horario para marcela-soto: mirar-agendar.mjs arma el suyo desde la pantalla contando
-- «franja 1..7», y mirar-bloqueos.mjs borra los de ella al terminar. Tampoco para los casos de
-- borde. Id determinista; si ya existe (o la app lo desactivo), no se toca.
CREATE TEMP TABLE demo_d ON COMMIT DROP AS
SELECT md5('zass-demo:disp:' || p.slug || ':' || s.servicio_id || ':' || pt.dia || ':' || pt.franja)::uuid AS id,
       p.perfil_id, ps.id AS prestador_servicio_id, pt.dia, pt.desde, pt.hasta, s.minutos
  FROM demo_p p
  JOIN demo_servicios s ON s.slug = p.slug
  JOIN prestador_servicios ps ON ps.perfil_prestador_id = p.perfil_id AND ps.servicio_id = s.servicio_id
  JOIN demo_patrones pt ON pt.patron = p.horario
 WHERE p.caso = 'normal' AND p.horario IS NOT NULL AND p.slug <> 'marcela-soto';

INSERT INTO disponibilidades (id, perfil_prestador_id, prestador_servicio_id, dia_semana, hora_inicio, hora_fin,
                              duracion_bloque_min, vigente_desde, vigente_hasta, activa)
SELECT d.id, d.perfil_id, d.prestador_servicio_id, d.dia, d.desde, d.hasta, d.minutos,
       (now() AT TIME ZONE 'America/Santiago')::date, NULL, true
  FROM demo_d d
ON CONFLICT (id) DO NOTHING;

-- ------------------------------------------------------------------ Bloques
-- Como ServicioAgenda.GenerarBloquesAsync: ventana de 8 semanas desde ahora, dias CHILENOS
-- (1 = lunes), hora de pared convertida con America/Santiago, sin repetir un inicio que ya
-- exista para el mismo servicio. Hace falta aqui porque nada extiende los bloques de noche.
INSERT INTO bloques (id, disponibilidad_id, perfil_prestador_id, prestador_servicio_id, inicio_en, fin_en, estado)
SELECT gen_random_uuid(), d.id, d.perfil_prestador_id, d.prestador_servicio_id,
       x.inicio, x.inicio + make_interval(mins => d.duracion_bloque_min), 'Libre'
  FROM disponibilidades d
  JOIN demo_d dd ON dd.id = d.id
  -- Dias como fecha + entero (no generate_series de fechas, que depende de la zona de la sesion).
  CROSS JOIN LATERAL (SELECT (now() AT TIME ZONE 'America/Santiago')::date + j AS dia
                        FROM generate_series(0, 56) AS j) g
  CROSS JOIN LATERAL generate_series(0, floor(extract(epoch FROM (d.hora_fin - d.hora_inicio)) / 60
                                              / d.duracion_bloque_min)::int - 1) AS k(i)
  CROSS JOIN LATERAL (SELECT ((g.dia + d.hora_inicio + make_interval(mins => k.i * d.duracion_bloque_min))
                              AT TIME ZONE 'America/Santiago') AS inicio) x
 WHERE d.activa
   AND extract(isodow FROM g.dia) = d.dia_semana
   AND g.dia >= d.vigente_desde
   AND (d.vigente_hasta IS NULL OR g.dia <= d.vigente_hasta)
   AND x.inicio > now() AND x.inicio <= now() + interval '56 days'
   AND NOT EXISTS (SELECT 1 FROM bloques b
                    WHERE b.prestador_servicio_id = d.prestador_servicio_id AND b.inicio_en = x.inicio);

-- ------------------------------------------------------------------ Comprobaciones
-- Si algo no calza, se aborta y se deshace TODO (la transaccion no llega al COMMIT).
DO $$
DECLARE
  n int;
  detalle text;
BEGIN
  -- Visibles con la misma regla que la busqueda (RN-S3-09: perfil publicado y suscripcion vigente).
  SELECT count(DISTINCT p.perfil_id) INTO n
    FROM demo_p p
    JOIN perfiles_prestador pp ON pp.id = p.perfil_id AND pp.estado = 'Publicado'
    JOIN suscripciones s ON s.perfil_prestador_id = p.perfil_id
                        AND s.estado IN ('Prueba', 'Activa') AND s.vigente_hasta > now();
  IF n <> 26 THEN
    RAISE EXCEPTION 'COMPROBACION: tenian que quedar 26 prestadores visibles y quedan %.', n;
  END IF;

  SELECT string_agg(p.slug, ', ') INTO detalle
    FROM demo_p p
    JOIN perfiles_prestador pp ON pp.id = p.perfil_id AND pp.estado = 'Publicado'
    JOIN suscripciones s ON s.perfil_prestador_id = p.perfil_id
                        AND s.estado IN ('Prueba', 'Activa') AND s.vigente_hasta > now()
   WHERE p.caso <> 'normal';
  IF detalle IS NOT NULL THEN
    RAISE EXCEPTION 'COMPROBACION: los casos de borde NO deben salir en la busqueda y salen: %.', detalle;
  END IF;

  -- Codificacion: «Ã» (195) o «Â» (194) en un texto en castellano es UTF-8 leido como WIN1252.
  SELECT count(*) INTO n FROM (
    SELECT pp.nombre_publico AS t FROM perfiles_prestador pp JOIN demo_p p ON p.perfil_id = pp.id
    UNION ALL SELECT coalesce(pp.descripcion, '') FROM perfiles_prestador pp JOIN demo_p p ON p.perfil_id = pp.id
    UNION ALL SELECT u.nombre FROM usuarios u WHERE u.correo::text LIKE 'demo-%'
    UNION ALL SELECT re.comentario FROM resenas re JOIN demo_r r ON r.resena_id = re.id
    UNION ALL SELECT c.nombre FROM credenciales c JOIN demo_p p ON p.perfil_id = c.perfil_prestador_id
  ) textos WHERE strpos(t, chr(195)) > 0 OR strpos(t, chr(194)) > 0;
  IF n > 0 THEN
    RAISE EXCEPTION 'COMPROBACION: % textos con tildes rotas («Ã»). ¿Falta SET client_encoding = ''UTF8''?', n;
  END IF;

  SELECT count(*) INTO n FROM resenas re JOIN demo_r r ON r.resena_id = re.id;
  IF n <> 94 THEN
    RAISE EXCEPTION 'COMPROBACION: tenian que quedar 94 resenas de demostracion y quedan %.', n;
  END IF;

  -- La reputacion guardada es la que sale de las resenas (mismo calculo que la aplicacion).
  SELECT string_agg(p.slug, ', ') INTO detalle
    FROM demo_p p
    JOIN perfiles_prestador pp ON pp.id = p.perfil_id
    LEFT JOIN LATERAL (SELECT count(*)::int AS total,
                              CASE WHEN count(*) = 0 THEN NULL
                                   ELSE round(avg(calificacion)::numeric, 1) END AS promedio
                         FROM resenas re
                        WHERE re.perfil_prestador_id = p.perfil_id AND re.estado = 'Publicada') r ON true
   WHERE pp.total_resenas <> r.total OR pp.promedio_calificacion IS DISTINCT FROM r.promedio;
  IF detalle IS NOT NULL THEN
    RAISE EXCEPTION 'COMPROBACION: promedio o total de resenas no calza con las filas en: %.', detalle;
  END IF;

  -- Todo prestador visible con horario sembrado tiene horas para agendar.
  SELECT string_agg(p.slug, ', ') INTO detalle
    FROM demo_p p
   WHERE p.caso = 'normal' AND p.horario IS NOT NULL AND p.slug <> 'marcela-soto'
     AND NOT EXISTS (SELECT 1 FROM bloques b
                      WHERE b.perfil_prestador_id = p.perfil_id AND b.estado = 'Libre' AND b.inicio_en > now());
  IF detalle IS NOT NULL THEN
    RAISE EXCEPTION 'COMPROBACION: sin horas libres para agendar: %.', detalle;
  END IF;
END $$;

-- Resumen (antes del COMMIT: las tablas temporales se borran al confirmar).
SELECT r.nombre AS rubro,
       coalesce(z.comuna, '-') AS comuna,
       count(*) FILTER (WHERE p.caso = 'normal') AS prestadores_visibles,
       sum(pp.total_resenas) AS resenas,
       sum((SELECT count(*) FROM bloques b WHERE b.perfil_prestador_id = p.perfil_id
                                             AND b.estado = 'Libre' AND b.inicio_en > now())) AS horas_libres
  FROM demo_p p
  JOIN perfiles_prestador pp ON pp.id = p.perfil_id
  JOIN rubros r ON r.id = p.rubro
  LEFT JOIN coberturas c ON c.perfil_prestador_id = p.perfil_id AND c.es_principal
  LEFT JOIN zonas z ON z.id = c.zona_id
 GROUP BY r.id, r.nombre, z.comuna
 ORDER BY r.id, z.comuna;

COMMIT;

SELECT (SELECT count(*) FROM usuarios WHERE correo::text LIKE 'demo-%') AS cuentas_demo;
