# ZASS! — estado del desarrollo

**Última sesión:** 7 de noviembre de 2026, tarde (MOD-03 Catálogo, MOD-04 Geolocalización, MOD-05 Búsqueda, y el repositorio ya está en GitHub).

Este archivo existe para retomar sin reconstruir contexto. Dice **qué está hecho y
comprobado**, **qué no**, y **el siguiente paso exacto**.

---

## 1. Qué es esto

La plataforma ZASS! funcionando de verdad, construida sobre el diseño que está en
`C:\Users\pdani\OneDrive\Desktop\Repositorio ZASS\zass-documentacion`.

**Ese repositorio de documentación es la especificación.** Los identificadores que
aparecen en el código (`CU-12`, `TBL-18`, `R-1`, `CC-01`, `RN-S4-05`) apuntan a él.

### Modelo de negocio

Quien **ofrece** paga una mensualidad. Quien **busca** no paga nunca. Subsistema **S7**
con las tablas `planes`, `suscripciones` y `pagos`; la pasarela no existe todavía.

---

## 2. Resumen en una línea

**MOD-01 Identidad, MOD-03 Catálogo, MOD-04 Geolocalización, MOD-05 Búsqueda y MOD-13 Suscripciones
están construidos, con 549 pruebas en verde** (248 de aplicación sin base de datos, 210 de
integración contra PostgreSQL real y la API por HTTPS, 91 de interfaz) más dos
comprobaciones de navegador que crean una cuenta y entran contra la base de verdad.

MOD-01 llega hasta la pantalla. MOD-03, MOD-04, MOD-05 y MOD-13 están completos de dominio a API y
**todavía sin pantalla**.

**El camino completo de CU-03 ya está probado de punta a punta por HTTP**: un prestador se
registra, verifica su correo, elige rubro, guarda servicios, guarda su zona, publica, y le
arranca la suscripción de prueba de 30 días. Era la prueba que este archivo daba por
imposible en las dos sesiones anteriores, y lo único que faltaba para hacerla eran MOD-03 y
MOD-04. **La frase entera ya está probada, hasta la última palabra:** un visitante SIN sesión busca por
rubro y ubicación y encuentra al prestador recién publicado, con la distancia como texto
redondeado. Hay una prueba por HTTP que recorre registro, verificación, rubro, servicio, zona,
publicar, y la búsqueda anónima. Y otra que comprueba que al despublicarse desaparece en el acto.

**Todo el entorno local va por HTTPS.** `https://localhost:5173` la interfaz y
`https://localhost:7277` la API, con el certificado de `dotnet dev-certs`. Eso permitió
activar la marca `Secure` en las cookies también en desarrollo, y ahora hay una prueba
que la comprueba; antes solo se podía ver desplegando.

**El repositorio ya está en GitHub: `77pablo/zass-app`, privado.** 18 commits. La instancia portable de PostgreSQL, los dos secretos y la clave privada del
certificado quedan fuera, y eso se comprobó revisando la historia entera antes de subir,
no de oídas. Hay `README.md` para quien abra el repositorio.

Lo que falta es MOD-02 y MOD-06 a MOD-12, el resto de las pantallas y los trabajos de
fondo.

---

## 3. Lo que está hecho y comprobado

### Infraestructura local

| Pieza | Estado | Comprobado con |
|---|---|---|
| PostgreSQL 16.4 portable, puerto 5433 | **Funcionando** | `pg_isready`, `SELECT version()` |
| PostGIS 3.6.2, `citext`, `pgcrypto` | Instaladas | `pg_extension` |
| Base `zass`, usuario `zass` | Creada | conexión real |

### Base de datos — 5 migraciones aplicadas

| Migración | Qué hizo | Comprobado contra la base |
|---|---|---|
| `EsquemaInicial` | 31 tablas, R-1, 9 `CHECK`, índices únicos parciales | `pg_indexes`, `pg_constraint` |
| `TokensSeparados` | Un vencimiento por token en `usuarios` | `information_schema.columns`: las 4 columnas, con su tipo |
| `RolesDeCatalogo` | Los 5 roles de TBL-02 como datos del esquema | `SELECT * FROM roles` |
| `RubroOpcionalEnBorrador` | `rubro_id` nulo mientras el perfil es borrador | el perfil en borrador ya se inserta |
| `UbicacionFueraDelModelo` | **Índice GIST** en `ubicacion_exacta`, y la columna fuera del modelo de EF Core | `pg_indexes` muestra el GIST; la columna sigue `is_generated = ALWAYS` |
| `PlanDePruebaSembrado` | El plan `prueba` como dato del esquema | `SELECT * FROM planes` |
| `CatalogoSembrado` | 5 rubros y 39 servicios, la columna `nombre_normalizado`, el único `ux_servicio_rubro_nombre`, dos `CHECK` en `prestador_servicios` y la secuencia de `servicios` reiniciada en 1000 | `pg_indexes`, `pg_constraint`, y un `INSERT` real que devolvió el id **1000** |
| `ZonasDeTemuco` | Las 16 zonas, el único `(comuna, nombre)` y la secuencia de `zonas` reiniciada en 100 | `pg_indexes` dice `CREATE UNIQUE INDEX`; un `INSERT` duplicado da **23505** y uno nuevo devuelve el id **100** |
| `TramoDeDistancia` | La función `tramo_de_distancia(metros)`: el kilómetro entero que la búsqueda **muestra** y por el que **ordena** | Llamada con los bordes (1499, 1500, 2500, 3500) y comparada con `Geo.DistanciaPublica` por 13 casos de una prueba de integración. Con `round(double)` el 2,5 km da 2 y el dominio dice 3: la prueba cae justo ahí |

### Backend

| Proyecto | Estado |
|---|---|
| `Zass.Dominio` | Compila. **0 paquetes NuGet.** 31 entidades |
| `Zass.Aplicacion` | **MOD-01 completo**: 9 puertos, 5 servicios, 5 validadores, `IServicioIdentidad` |
| `Zass.Infraestructura` | Argon2id, AES-256-GCM, secretos, reloj, 3 repositorios EF Core, cola, auditor, y el registro `AgregarZass()` |
| `Zass.Api` | **Funciona**: 16 endpoints de identidad, 4 grupos de límite de tasa, cookie de 15 min, CSRF exigido, las 7 cabeceras, `problem+json` |

### MOD-13 Suscripciones — lo que decide quien aparece

Construido después de que la decisión de RN-S3-09 dejara al descubierto que las tres
tablas de S7 no tenían dueño.

| Pieza | Estado |
|---|---|
| `IServicioSuscripciones` | 9 operaciones. `VigentesEntreAsync` es la que usará la búsqueda |
| Plan `prueba`, 30 días, gratis | **Sembrado con el esquema**, migración `PlanDePruebaSembrado` |
| API-22 publicar | Ahora **compone** MOD-01 y MOD-13: publica y arranca la prueba |
| `GET /api/mi/suscripcion` | Plan, estado y días restantes |

Decisiones con prueba detrás:

- **Vigente exige las dos cosas**: estado `Prueba` o `Activa` **y** fecha futura. Con solo
  el estado, un impago seguiría apareciendo hasta que el trabajo de vencimiento corriera.
- **`VigentesEntre` es una consulta, no veinte.** Hay una prueba que cuenta las idas al
  repositorio: preguntar uno por uno serían 21 consultas por página de resultados, en la
  ruta más caliente del producto.
- **Iniciar la prueba es idempotente.** El índice único lo impediría igual, pero llegar
  hasta él con un `23505` convertiría un doble clic en un error 500.
- **Pagar antes de vencer suma al tiempo que quedaba.** Quien paga el día 25 de un mes de
  30 no pierde los 5 restantes.
- **El orden de API-22**: primero publicar, después arrancar la prueba. Al revés, un
  perfil incompleto gastaría días sin haber aparecido nunca.
- **`PorVencerAsync`** existe para avisar **antes**. Desaparecer de la búsqueda sin aviso
  previo es lo que volvería cruel a RN-S3-09.

**Lo que no se pudo probar todavía:** el camino completo publicar → prueba → visible
**por HTTP**. Publicar exige rubro, servicio y zona, y no hay endpoints ni datos de
ejemplo para ninguno hasta MOD-03 y MOD-04. Sí está probado que **publicar sin el mínimo
NO arranca la prueba**, que es la mitad que puede fallar en silencio.

### MOD-03 Catálogo — lo que desbloqueó el camino de publicar

Las tres tablas y las entidades ya existían desde la primera migración; lo que no existía
era el módulo. Es un **módulo hoja**: no consume a ningún otro, y no debe empezar a
hacerlo.

| Pieza | Estado |
|---|---|
| `IServicioCatalogo` | Las 3 operaciones de la tabla de módulos, más `ServiciosDePrestador` y `ReemplazarServicios` |
| API-02 `GET /api/rubros?q=` | Público. 5 rubros sembrados con el esquema |
| API-03 `GET /api/rubros/{id}/servicios` | Público. 39 servicios del catálogo |
| API-20 `PUT /api/mi/perfil-prestador/servicios` | Compone MOD-01 (rubro), MOD-13 (tope del plan) y MOD-03 (la oferta) |
| API-20 `GET` de la misma ruta | **No estaba en el documento.** Sin él, el formulario de CU-03 paso 4 no puede pintarse con lo guardado |
| `IServicioSuscripciones.LimiteDeServiciosAsync` | Nuevo en MOD-13: el tope de servicios es del plan, y el plan es cosa suya |

Decisiones con prueba detrás:

- **Reemplazar la oferta NO borra y vuelve a crear.** TBL-13 `disponibilidad` y TBL-18
  `cita` apuntan a `prestador_servicio` con `ON DELETE CASCADE`: recrear las filas se
  llevaría por delante los horarios y las citas con solo cambiar un precio. Lo que sale de
  la lista **se desactiva y se conserva**; lo que vuelve, se reactiva. Hay una prueba de
  integración que crea una disponibilidad, quita el servicio y comprueba que sigue ahí.
- **El prestador puede escribir servicios que no están en el catálogo** (CU-03 paso 4,
  `es_catalogo = false`), pero si el nombre ya existe en el rubro **se reutiliza** el que
  hay. "tasacion" escrito a mano encuentra "Tasación comercial".
- **El tope de servicios no se repite en dos sitios:** es `plan.max_servicios`, y para el
  plan de prueba son 10. Sin suscripción manda el plan inicial, porque el prestador elige
  sus servicios **antes** de publicar, que es cuando arranca la prueba.
- **Validar N servicios es UNA consulta**, no N: corre en el guardado del perfil y hay una
  prueba que cuenta las idas al repositorio.
- **La lista vacía se rechaza.** Vaciarla por API-20 dejaría un perfil `publicado` que no
  ve nadie; para dejar de aparecer está despublicar.
- **El texto de `?q=` no se interpreta como patrón.** Con `ILIKE`, un `?q=%` devolvería el
  catálogo entero; con `ToLower().Contains()` el porcentaje es un carácter más.

**Lo que MOD-03 desbloqueó, medido:** con rubro y servicio puestos, publicar ya no se queja
de ellos y **solo pide la zona**, que es MOD-04. Hay una prueba por HTTP que lo afirma, y
es justo la que la versión anterior de este archivo daba por imposible.

### MOD-04 Geolocalización — las zonas, y lo que cierra el mínimo publicable

| Pieza | Estado |
|---|---|
| `IServicioGeografia` | Las 5 operaciones de la tabla de módulos, más `ZonasDeCobertura` y `ReemplazarCobertura` |
| API-04 `GET /api/zonas?sector=` | Público. **16 zonas** sembradas con el esquema |
| API-21 `PUT /api/mi/perfil-prestador/cobertura` | Exige exactamente una zona principal (RN-S3-08, R-2) |
| API-21 `GET` de la misma ruta | **No estaba en el documento.** Sin él, el formulario de CU-10 no puede pintarse con lo guardado |
| `Geo` en `Zass.Dominio.Comun` | Haversine y el redondeo público. Cero paquetes, se prueba sin base |
| `GeocodificadorNoDisponible` | N-08 no tiene proveedor: devuelve la falla que lleva al flujo 4B de CU-03 |

Las dos decisiones que lo hicieron posible, y que Pablo tomó el 7 de noviembre:

- **La zona de un punto es la del centroide más cercano**, no la del polígono que lo
  contiene. TBL-10 declaraba una columna `limite` con el polígono del barrio, pero no
  existe en la base y no hay polígonos reales de los barrios de Temuco. Documento corregido.
- **El geocodificador queda en espera.** Azure Maps, Google y Nominatim exigen una clave o
  una decisión de costo que no está tomada. Es un puerto con una implementación que
  contesta que no está disponible, y eso dispara el flujo 4B de CU-03 —elegir la zona de
  una lista—, que ya estaba diseñado.

Decisiones con prueba detrás:

- **RN-S3-03 se hace cumplir con dos métodos, no con un comentario.** `DistanciaEnMetros`
  es exacta y de uso interno; `DistanciaPublica` devuelve texto y es la única que puede
  salir en una respuesta. Por debajo de un kilómetro **no da número**: "a 400 m" en tres
  fichas distintas ubica una casa por trilateración, y eso es lo que RN-S3-02 prohíbe. Hay
  una prueba que recorre varias distancias y comprueba que ningún texto público menciona
  metros.
- **`ResolverZona` tiene tope de 50 km**, el mismo radio máximo de RN-S3-04, porque "el más
  cercano" siempre devuelve algo: sin tope, un punto en Santiago resolvería a un barrio de
  Temuco.
- **Cambiar de zona principal se hace en dos guardados, y está medido.** Se escribió la
  versión ingenua —marcar la nueva en el mismo guardado— y la prueba de integración se cae
  con un 23505: `ux_cobertura_principal` es un único parcial y PostgreSQL lo comprueba
  después de **cada sentencia**, no al final de la transacción. En memoria las dos
  versiones pasan, así que esa prueba vive en integración.
- **Una prueba comprueba que ninguna de las 16 zonas quedó con latitud y longitud
  invertidas.** Es el error clásico y silencioso de todo lo geográfico: las coordenadas
  invertidas dan un punto perfectamente válido en otro continente.
- **`MaxZonas = 10` es una decisión del código, no del documento.** RN-S3-08 dice que un
  prestador puede cubrir varias zonas y no pone número. Sin tope, uno se marca las 16 y
  sale en todas las búsquedas de la ciudad. Si Pablo quiere otro número, está en una sola
  constante.

**De dónde salieron las coordenadas.** Son los nodos de OpenStreetMap de cada sector,
consultados por Overpass el 7 de noviembre de 2026. Overpass devuelve **256** lugares
poblados dentro de la comuna, hasta el último condominio, y eso no sirve como selector:
nadie dice "atiendo en Condominio Nueva Milano". Quedaron los 16 sectores con los que la
gente de Temuco dice dónde está. Ninguna coordenada está inventada, y eso importa: un
centroide mal puesto manda al prestador al barrio de al lado sin que nada falle.

### MOD-05 Búsqueda — la frase entera, por fin

Es el único módulo **sin tablas propias**: solo lee. Si alguna vez necesita una, está
asumiendo responsabilidades que no le tocan.

| Pieza | Estado |
|---|---|
| `IServicioBusqueda.Buscar` | Los cuatro filtros duros del paso 5 de CU-08, orden, cursor y distancia redondeada |
| API-01 `GET /api/busqueda?rubro=corretaje&lat=&lon=&radio=` | Público y sin sesión (P1 de CU-08) |
| `RubroPorCodigo` en MOD-03 | El rubro va por **código** en la URL, no por id: un enlace a una búsqueda se comparte y se lee |
| `FilaDeBusqueda` | Tipo sin clave y sin tabla, que EF Core materializa desde `FromSqlInterpolated` |

Decisiones con prueba detrás:

- **Los cuatro filtros duros van DENTRO del SQL**: publicado, rubro, cobertura de la zona y
  suscripción vigente. Si el de suscripción se aplicara sobre el resultado, una página de 20
  podría devolver 3 y el cursor apuntaría a un sitio que no existe.
- **Desvío respecto de lo que este archivo decía antes.** El paso 3 de la lista de pasos
  mandaba "llamar a `VigentesEntreAsync`, nunca a `EstaVigente`". No se hizo: el filtro es un
  `JOIN` a `suscripciones` dentro de la propia consulta, que es lo que mantiene sana la
  paginación. **`VigentesEntreAsync` existe, está probado, y hoy no lo usa nadie.**
- **La condición de vigencia vive dos veces**: en C# dentro de MOD-13 y en el SQL de la
  búsqueda. No se puede evitar sin sacar el filtro de la base. Hay una prueba que recorre
  seis casos (Prueba y Activa con fecha futura y pasada, Vencida, Cancelada) y afirma que lo
  que el SQL deja pasar es exactamente lo que `EstaVigenteAsync` llama vigente. **El día que
  alguien cambie una punta y no la otra, esa prueba se cae.** Al vencer el plazo el prestador
  deja de aparecer solo, sin ningún trabajo de fondo, porque el filtro compara con la hora
  que se le pasa y no con un estado que alguien tenga que marcar.
- **La búsqueda por punto resuelve primero la zona y la exige como cobertura.** El paso 5 de
  CU-08 pide cobertura, no solo cercanía: un corredor que atiende solo en el Centro no le sale
  a alguien de Labranza aunque esté dentro del radio.
- **Hoy nadie tiene punto exacto** (el geocodificador no existe), así que la distancia se mide
  al **centro de la zona principal**, que es lo que manda el flujo 4B.4 de CU-03. **El índice
  GIST todavía no se gana el sueldo**, y no tiene sentido fingir que sí.
- **La distancia viaja como texto, nunca como número**, y RN-S3-02 está comprobada **sobre los
  bytes de la respuesta**: ni latitud, ni longitud, ni dirección, ni metros, ni correo, ni
  teléfono pueden aparecer en el JSON, ni con otro nombre ni dentro de otro campo. Además hay
  una prueba estructural que falla si alguien agrega esos campos al DTO.
- **El orden: tramo de distancia, verificado, reputación pública, reseñas, id.** Por zona, lo
  mismo sin el tramo. Ver "Cómo se arregló el orden" más abajo, que es la decisión con más
  fondo de MOD-05.
- **Cursor keyset, nunca `OFFSET`**, y **sobre la tupla entera por la que se ordena**:
  `(tramo, verificado, reputación, reseñas, id)` por distancia y lo mismo sin el tramo por
  zona. Si el cursor llevara menos columnas que el `ORDER BY`, un empate haría que la página
  siguiente repitiera o se saltara a alguien. Una prueba pagina de a 2 una lista que mezcla
  sello, reputación y reseñas, y exige que dé **exactamente** la lista pedida entera. La
  consulta pide una fila más que la página para saber si hay siguiente sin contar el total, y
  el cursor apunta a la última fila **servida**, no a la extra. Un cursor de una búsqueda por
  distancia no sirve en una por zona, ni al revés, y uno fabricado a mano (un "verificado"
  que no es 0 ni 1, un id que no es un guid) se rechaza antes de ir a la base.
- **E-400 corrige, no rechaza:** un radio fuera de rango se lleva al valor válido más cercano y
  se informa. Por eso el validador de API-01 **no** mira el radio.
- **Buscar desde fuera de toda zona conocida** devuelve una página vacía con el motivo, no un
  404, para que el cliente ofrezca elegir otra zona.
- **RN-S5-11 también manda en el filtro**: filtrar por un promedio que nadie puede ver
  (menos de 3 reseñas) ocultaría perfiles por un número invisible. Y **una credencial en
  revisión no da sello**: si lo diera, subir un papel cualquiera bastaría.

**Lo que NO está hecho de MOD-05, y no hay que dar por hecho:**

| Falta | Por qué importa |
|---|---|
| **`BuscarAgregadoPorZona`** y registrar la búsqueda de forma agregada (paso 13 de CU-08) | Es el dato que se le entrega al municipio, el segundo valor del producto para la postulación |
| Flujos 4B (sugerencias sin resultados), 4D (reducir el radio si hay más de 200) y 4E (texto libre sin coincidencia) | Hoy sin resultados se devuelve una lista vacía y nada más |
| **Desempate por completitud** (paso 7 de CU-08) | No existe ninguna medida de completitud del perfil. Hoy desempata verificado, reputación pública, reseñas e id |
| El resto del flujo 4B de CU-08 | Hoy sin resultados se sugiere ampliar el radio, quitar los filtros u otra zona. Falta "buscar en un rubro cercano" y "avísame cuando haya alguien" (que necesita cuenta) |
| La **caché de 60 s** (Redis, N-06) | No existe |
| **La meta de rendimiento** (menos de 1 s en el percentil 95 con 1.000 perfiles) | **No está medida.** Las pruebas usan decenas de filas, y con el centroide de respaldo el GIST ni se usa. Hay que medir con `EXPLAIN` y con volumen antes de decir que cumple |

### Lo que encontró ver la búsqueda en un navegador real

Se levantó la base, la API y la interfaz, se sembraron 8 prestadores de prueba (5 buscables
y 3 que **no** deben salir: uno con suscripción vencida, uno en borrador y uno de otro rubro)
y se recorrió con Playwright a 390 px y a 1280 px. Salieron **exactamente los 5 correctos**,
que es RN-S3-01 y RN-S3-09 funcionando de punta a punta. Lo demás:

| # | Hallazgo | Estado |
|---|---|---|
| 10 | **El orden usa un promedio que la pantalla esconde.** "Estudio Nuevo Temuco" (2 reseñas, promedio guardado 5,0, **no público** por RN-S5-11) sale PRIMERO, por encima de "Corretaje Araucanía" (4,6 con 12 reseñas y verificado). La consulta ordena por el promedio guardado sin mirar si es público: el orden delata un número que se oculta, y a quien mira le parece arbitrario | **Arreglado**, con prueba que fallaba antes y una mutación por regla |
| 11 | **El orden no desempata como dice CU-08** ("por credencial verificada, reputación y completitud"). Hoy todos los prestadores de una zona están a la **misma** distancia (la de su centroide), así que el desempate es lo único que ordena, y hoy sale por `id`, que es un UUID: orden arbitrario | **Arreglado** (ahora desempata verificado, reputación pública, reseñas e id) |
| 12 | `salir()` dejaba escapar la promesa rechazada (un *Uncaught in promise*) cuando la API fallaba al cerrar sesión | Arreglado, con prueba |
| 13 | Mi comprobación de la fuente decía OK sin saber nada: `document.fonts.check()` devuelve `true` para una fuente que **ni existe** | Corregida: ahora mira las fuentes que el navegador descargó (400, 600 y 800) |
| 15 | **La pantalla no tiene control de radio.** Con la ubicación, el radio es 10 km y no se puede cambiar. Quien cubre tu zona pero se basa a más de 10 km (el centroide de su zona principal está a 14,5 km del Centro, en el caso de la prueba) **no aparece nunca**, y no hay cómo ampliar ni se le dice a la persona que hay alguien más lejos. El flujo 4B de CU-08 ("ampliar el radio") existe en el documento y no en el código | **Arreglado.** Con la ubicación aparece "¿Hasta qué distancia?" (5, 10, 20 y 50 km, el mayor es el máximo de RN-S3-04), y sin resultados se propone "Ampliar a 50 km", que repite **la búsqueda que se hizo** y no lo que el formulario diga ahora. Visto en el navegador real: con 20 km aparece el prestador de Labranza, último y "a 15 km". 12 pruebas y 6 mutaciones, 6 rojos |
| 16 | **"Quitar" medía 18 px de alto**, un objetivo táctil que en un celular se falla. Lo cazó la medición apenas apareció el control de radio, porque "Quitar" solo existe con la ubicación puesta y el guion no la ejercitaba | Arreglado: ahora mide 44 px, y el guion mide también con la ubicación activa |
| 14 | Los guiones `flujo-real.mjs` y `humo.mjs` asumían que la app abría en "Entrar", y `humo.mjs` habría medido **el botón de la búsqueda escondida** en vez del de la pantalla, sin avisar | Corregidos; pasan |

### Cómo se arregló el orden

El orden ahora es **tramo de distancia, verificado, reputación pública, número de reseñas, id**,
y por zona lo mismo sin el tramo. Cada pieza tiene una razón y una prueba:

- **La reputación que ordena es la pública.** La fila trae dos campos: el promedio tal como
  está guardado (que el servicio muestra solo con 3 reseñas o más) y `ReputacionOrden`, que
  vale -1 con menos de 3. Antes se ordenaba por el primero, y un 5,0 de 2 reseñas pasaba
  delante de un 4,6 de 12 por un número que la pantalla no muestra.
- **Se ordena por el TRAMO, no por los metros exactos.** El tramo es el kilómetro entero que
  la pantalla muestra ("a 3 km"). Dos razones. (1) Hoy todos los prestadores de una zona
  están a la **misma** distancia exacta —la de su centroide—, así que sin desempate el orden
  era el de un UUID. (2) Ordenar por la distancia exacta **filtra más que la redondeada**: dos
  prestadores de "a 3 km" que cambian de orden según desde dónde se busca delatan de qué lado
  de una línea están, y con muchas búsquedas esas líneas se cruzan. Ordenar por lo que se
  muestra no revela nada que no se muestre ya. Hay una prueba para cada lado: dentro del
  mismo tramo manda la calidad, y en otro tramo manda la distancia aunque el lejano tenga sello.
- **El redondeo vive una sola vez, en la base**, como la función `tramo_de_distancia`, y una
  prueba la compara con `Geo.DistanciaPublica` en 13 valores de borde. Así el orden no puede
  contradecir el texto que se ve ("a 3 km" delante de "a 2 km"). Y es `numeric`, no `double`:
  `round(double)` redondea los empates al par, y la prueba demostró que con eso 2,5 km daba 2.
- **El sello va antes que la reputación. Es una decisión de producto** que el documento no
  fijaba: CU-08 lista "credencial verificada, reputación y completitud", y el flujo 4C.4
  dice "reputación y credencial verificada" sin decir cuál primero. Se eligió el sello porque
  es la señal de confianza que ZASS! da por sí misma y la reputación de un prestador nuevo
  todavía no existe. Si Pablo prefiere lo contrario, es cambiar de lugar dos columnas en el
  SQL y en una prueba.
- **La completitud del perfil NO se usa**: no existe ninguna medida de ella.

**Cómo se supo que estaba bien:** se mutaron cuatro reglas del SQL (el promedio oculto, el
sello, el tramo y el cursor) y cayeron siete pruebas, cada mutación en la suya; y se mutó la
función de la base y cayó la de los empates. Y se **volvió a mirar en un navegador real**: por
zona sale Araucanía, Gestión Labranza, Nuevo Temuco, Asesoría y Propiedades del Sur, y con
ubicación el orden por tramo. El guion de navegador ahora **mira el orden**, que antes no
miraba y por eso el fallo solo se vio mirando una captura.

**Pendiente de decidir, que no es código:** Montserrat se carga desde **Google Fonts por
red**. Cada visitante le manda su dirección IP a Google antes de ver la página. Conviene
autoalojar la fuente, y más con una ley de protección de datos que empieza a regir en
diciembre de 2026 (revisar si aplica, no lo asumo).

**Repetirlo:** `frontend/pruebas-navegador/mirar-busqueda.mjs`, con los datos de
`datos/sembrar-visual.sql` y, **sin falta**, `datos/limpiar-visual.sql` al terminar: los
prestadores sembrados quedan publicados y hacen fallar las pruebas de integración de la
búsqueda. Las instrucciones exactas están en la cabecera del guion.

### Interfaz — `frontend/`

React 19 + TypeScript + Vite, con la marca aplicada (amarillo `#F6C81E`, Montserrat,
fondo crema `#FDF8E7`). Los colores **se midieron en el navegador**, no se dieron por
buenos mirando una captura.

| Pantalla | Endpoint | Estado |
|---|---|---|
| Crear cuenta, eligiendo buscador o prestador | API-08 | **Funciona contra la base real** |
| Entrar | API-09 | **Funciona contra la base real** |
| Salir | API-11 | Funciona |
| Aviso de correo sin verificar | — | Funciona |
| **Buscar** (rubro, zona o ubicación, filtros, ver más) | API-01, API-02, API-04 | **Es la portada. Probada con `fetch` simulado (54 pruebas) y VISTA en un navegador real contra la API y la base** (390 px y 1280 px): sin desborde horizontal, objetivos táctiles de 44 px, colores de marca y Montserrat medidos. El orden de los resultados tuvo un fallo que ya está arreglado, y con la ubicación tiene **control de radio** y la sugerencia de ampliarlo |

Decisiones que quedaron fijadas en el código y tienen prueba:

- **La pantalla de ingreso no valida el largo de la contraseña.** Decir “usa al menos 10”
  ahí regala el mínimo del sistema. La de registro sí lo dice, porque sin saberlo no hay
  forma de cumplirlo. Es la misma decisión que toma `ValidadorIniciarSesion`.
- **Vite reenvía `/api` a la API.** El navegador ve un solo origen, así que la cookie de
  sesión viaja como lo hará en producción. Apuntando directo al 5052 el navegador la
  descarta y el fallo parece “la contraseña está mala”.
- **Ningún token se guarda en JavaScript.** La sesión vive en una cookie `httpOnly`, y hay
  prueba de navegador de que `document.cookie` no la ve.

### Casos de uso que funcionan de verdad

**CU-01** registro · **CU-02** sesión · **CU-03** editar y publicar perfil ·
**CU-04** preferencias · **CS-01** verificar correo · **CS-02** recuperar contraseña ·
**CS-03** revocar sesiones · **CS-04** eliminar cuenta.

**CU-10** definir zonas de cobertura también funciona, y **CS-10** resuelve la zona de
un punto (no la de una dirección: eso espera a N-08).

Endpoints: **API-01 a API-05, 08-23**, más `GET /api/auth/csrf` y `GET /api/salud`. De los
23 primeros no falta ninguno. **CU-08** buscar por cercanía funciona.

### Las pruebas, y qué prueba cada grupo

| Proyecto | Cuántas | Qué cubre |
|---|---|---|
| `Zass.Aplicacion.Pruebas` | **248** | Las reglas, sin base de datos: RN-S1-01 a RN-S1-08, RN-S3-02 a RN-S3-08, RN-S5-11, CC-02, y MOD-03, MOD-04 y MOD-05 completos |
| `Zass.Integracion.Pruebas` | **210** | Argon2id y AES-GCM reales, el contenedor, PostgreSQL real, y la API por HTTPS |
| `frontend` (Vitest) | **31** | Cliente HTTP y pantallas, con `fetch` simulado |
| `frontend/pruebas-navegador` | 2 guiones | Navegador real contra la API real: que la página monta, los colores de marca, y el registro e ingreso completos |

Escritas con TDD: cada prueba se vio fallar antes de que existiera el código.

Las 38 de MOD-03 y las 35 de MOD-04 pasaron todas en la primera ejecución, y eso no
prueba nada por sí solo. En los dos casos se comprobó rompiendo cuatro reglas a propósito y
contando los rojos.

En MOD-03 cayeron exactamente cuatro. En MOD-04 cayeron **cinco**, y la quinta enseñó algo
que el código contaba mal: el punto `0,0` —lo que manda un formulario vacío— no lo rechaza
la validación de rango, porque 0 es una latitud perfectamente válida, sino el tope de 50 km,
que cae en el golfo de Guinea. El comentario decía lo contrario y quedó corregido.

**Esta es la forma más útil que ha tenido la regla de "un verde no prueba nada" en este
proyecto:** cuando una tanda de pruebas pasa de primera, romper el código a propósito y
contar los rojos dice si las pruebas miran de verdad, y tres veces ha destapado algo más.

La tercera fue en MOD-05 y es la más instructiva, porque el fallo era de una **prueba** y no
del código: la de la media coordenada solo miraba el código 400, y sin la regla la petición
igual devuelve 400 (cae en "dinos dónde buscas"). **Sobrevivió a la mutación.** Miraba el
código y no el motivo. Ahora exige que el mensaje diga cuál coordenada falta, y contra el
validador mutado falla.

Cosas que **tienen prueba propia**, no solo un comentario:

- Contraseña mala y correo inexistente devuelven **el mismo cuerpo byte a byte**.
- El intento fallido audita el **hash** del correo, nunca el correo.
- Renovar **rota** el token: el anterior no sirve dos veces.
- Ningún DTO de perfil **tiene campos** de latitud ni longitud, ni el propio ni el público.
- Las 7 cabeceras llegan **también en un 404**.
- La CSP no lleva `unsafe-inline` ni `unsafe-eval`.
- Un cambio sin `X-CSRF-Token` recibe 403; con él, pasa.
- El sexto inicio de sesión en la ventana recibe **429 con `Retry-After`**, y el cuerpo no
  dice cuántos intentos quedan.
- El índice único de `correo` rechaza el duplicado **saltándose el servicio**.
- La versión optimista la hace cumplir **la base**, moviendo la versión por detrás de EF Core.
- `VigentesDeUsuarioAsync` en SQL devuelve lo mismo que `Sesion.EstaVigente` en memoria.
- La aplicación **no arranca** sin la clave de cifrado, ni con una clave de largo erróneo.

---

## 4. Los seis fallos que encontraron las pruebas de verdad

Ninguno se veía al compilar y ninguno lo habrían visto las pruebas en memoria. Van aquí
porque son el argumento de por qué valió la pena la capa de integración.

| # | Fallo | Qué habría pasado en producción |
|---|---|---|
| 1 | `Verifica` de Argon2id **reventaba** con contraseña vacía | Mandar la contraseña vacía devolvía **500** en vez de 401 |
| 2 | El contexto de auditoría era texto plano y la columna es `jsonb` | **Toda** acción auditada fallaba contra la base real: nadie podía registrarse |
| 3 | `rubro_id` era obligatorio, pero el perfil nace en borrador sin rubro | Violación de clave foránea: ningún prestador podía registrarse |
| 4 | EF Core intentaba **leer de vuelta** la columna `geography` como texto | Fallaba todo `INSERT` y `UPDATE` de perfil |
| 5 | **Faltaba el índice GIST** en `ubicacion_exacta` | CU-08 recorrería la tabla entera: la columna estaba pero no servía |
| 6 | `AgregarZass` leía la configuración al **registrar**, no al resolver | Cualquier fuente agregada después quedaba ignorada; en producción, Key Vault |

### El séptimo, y el más caro de haber dejado pasar

| # | Fallo | Qué habría pasado en producción |
|---|---|---|
| 7 | El único de `(rubro_id, nombre)` iba sobre `lower(nombre)` y **no rechazaba nada** | Dos servicios iguales en un rubro, y la búsqueda mostrando dos filas para lo mismo |

Lo encontró una prueba de integración que esperaba el rechazo y **no lo obtuvo**. La causa:
este clúster se creó con locale `C`, y ahí `lower()` solo pliega ASCII, así que `lower('Ó')`
sigue siendo `'Ó'` y "TASACIÓN COMERCIAL" entraba al lado de "Tasación comercial". La regla
dependía del locale del servidor, que en Azure es otro: habría pasado en desarrollo y
fallado en producción, o al revés.

Arreglado en la raíz: `servicio.nombre_normalizado`, que calcula `Texto.ClaveDeNombre` en C#
(sin tildes, minúsculas, espacios comprimidos), y el índice compara por igualdad exacta. De
paso resolvió las tildes, que era otro agujero: "tasacion" ya encuentra "Tasación comercial".

**La lección, que es la de siempre con otra cara:** `lower()` parecía neutral y no lo era.
Si una regla de unicidad se apoya en una función de la base, depende de cómo se creó la
base.

### Los dos que encontró el camino completo de CU-03

Los encontró la prueba por HTTP que publica un perfil de verdad. No se podía escribir antes
de MOD-03 y MOD-04 porque no había forma de llegar al mínimo publicable, así que de API-22
solo se había comprobado el lado del fallo.

| # | Fallo | Qué habría pasado en producción |
|---|---|---|
| 8 | **Publicar dos veces devolvía 500** | Un doble clic en "Publicar". Y despublicar y volver a publicar, que es el flujo 4E de CU-03, habría fallado igual |
| 9 | La API serializaba los enums como **número**: `"estado": 1` | La pantalla del perfil tendría una tabla de números que se desalinea en silencio el día que alguien reordene el enum |

El 8 es el que vale la pena entender. `PublicadoEn` se asigna con `??=`, así que no cambia;
la clave de idempotencia del aviso se armaba con esa fecha, el segundo publicar generaba la
**misma** clave y chocaba con el único de R-18 en `notificaciones`. Arreglado donde
corresponde: solo se avisa cuando el perfil **entra** en publicado, y la clave es del aviso
y no de una fecha que no se mueve. **R-18 existe para que el aviso llegue una sola vez, no
para que el caso de uso le delegue la decisión de si hay algo que avisar.**

Además, un fallo latente encontrado leyendo: los dos tokens de un solo uso compartían una
columna de vencimiento, así que pedir recuperación le acortaba en silencio el plazo al
token de verificación de quien aún no confirmaba su correo.

---

## 4 bis. Lo que encontró construir la pantalla

Cuatro cosas que el backend solío no mostraba:

1. **El límite de tasa impide desarrollar.** El grupo A son 5 intentos por IP cada 15
   minutos, correcto en producción. En desarrollo se agota en una sola vuelta de pruebas
   y después **todo parece roto**, cuando en realidad es un 429. Se subió solo para el
   entorno de desarrollo, en `appsettings.Development.json`; en producción siguen
   mandando los valores por omisión del código.
2. **35 mensajes salían sin tilde ni eñe.** “contrasena”, “La sesion no es valida”,
   “Datos invalidos”. No eran comentarios: viajan en el cuerpo `problem+json` y la
   interfaz los muestra tal cual, así que los leía la persona. Corregidos.
3. **`vite.config.ts` importaba `defineConfig` de `vite`**, que no conoce la sección
   `test`: toda la configuración de pruebas estaba **sin revisar por el compilador**.
4. **Vitest no veía ningún archivo** porque busca `*.test.ts` y aquí se llaman
   `*.prueba.ts`. Peor que fallar: decía “0 pruebas” y salía con éxito.

Los dos últimos son el mismo patrón de siempre: la herramienta dice que todo está bien
porque no está mirando.

---

## 5. El documento, ya corregido

Las cinco discrepancias que había entre código y documento están resueltas **en el
documento**, y el sitio se regeneró (`python sitio\construir.py`, 38 diagramas, ninguno
desbalanceado).

| Archivo | Qué se corrigió |
|---|---|
| `03-datos/diccionario-de-tablas.md` | TBL-01 declara las 4 columnas de token, con la nota de por qué cada una lleva su vencimiento y por qué se guarda el hash |
| `03-datos/diccionario-de-tablas.md` | TBL-05: `rubro_id` nulo en borrador, y el índice GIST de `ubicacion_exacta` |
| `02-clases/clases-03-agenda-y-citas.md` | `TipoNotificacion`: los 16 valores reales, no 8 |
| `06-desarrollo/paquetes-por-capas.md` | No hay `Zass.Compartido`: `Resultado` vive en `Zass.Dominio.Comun`, con la explicación |
| `03-datos/diccionario-de-tablas.md` | TBL-07 declara `icono`, que existe en la base desde la primera migración, y dice que los 5 rubros se siembran con el esquema |
| `03-datos/diccionario-de-tablas.md` | TBL-08 declara `nombre_normalizado`, el único `(rubro_id, nombre_normalizado)` con el porqué del locale, y la secuencia de identidad reiniciada |
| `06-desarrollo/api-rest.md` | API-20 lleva también el GET de la misma ruta, como API-22 lleva publicar y despublicar. API-02 dice que `?q=` no es un patrón |
| `03-datos/diccionario-de-tablas.md` | TBL-10 queda como está de verdad: sin `limite` ni `centro` geográficos, con `comuna` y las dos columnas del centroide, el único `(comuna, nombre)`, y escrito **por qué** se resuelve por centroide y qué haría falta para volver al polígono |
| `06-desarrollo/api-rest.md` | API-04 lleva `?sector=`; API-21 lleva su GET y dice que exige exactamente una zona principal |
| `06-desarrollo/tabla-de-modulos.md` | MOD-04 dice que `ResolverZona` va por centroide con tope de 50 km, que `Geocodificar` espera proveedor, y por qué `DistanciaPublica` no da números por debajo de 1 km |

En los cuatro casos **se movió el documento**, no el código, salvo `rubro_id`, donde el
documento se contradecía consigo mismo: marcaba la columna obligatoria y a la vez CU-03
guarda el avance en borrador antes de elegir rubro. Mandó el caso de uso.

---

## 6. Lo que NO está hecho

| Capa | Falta |
|---|---|
| `Zass.Aplicacion` | **MOD-02 y MOD-06 a MOD-12.** MOD-01, MOD-03, MOD-04, MOD-05 (salvo `BuscarAgregadoPorZona`) y MOD-13 listos |
| `Zass.Api` | Filtro de idempotencia (`Idempotency-Key`), que se necesita desde API-36 |
| Pruebas de arquitectura | Las 4 reglas de `paquetes-por-capas.md` §5 **no se verifican solas** todavía |
| Datos de ejemplo | Prestadores y reseñas. Los roles, el plan de prueba, los rubros, los servicios y las **16 zonas** ya están, y como dato del esquema |
| Frontend | Hechas las pantallas de **registro e ingreso**. Faltan: verificar correo, recuperar contraseña, perfil del prestador (ya tiene API-02, API-03, API-04, API-20 y API-21 detrás) y cablear la búsqueda, que ya está construida |
| Trabajos en segundo plano | Recordatorios, vencimientos, bloques, purgas |
| Notificaciones | Se encolan en TBL-27; **nadie las despacha**. Decidido: buzón local `.eml` |
| Suscripciones | Tablas sí, lógica y pasarela no |
| Repositorio git | **Hecho y subido:** `77pablo/zass-app` en GitHub, privado |
| Geocodificar direcciones | **Falta elegir proveedor (N-08).** Mientras no esté, manda el flujo 4B de CU-03: el prestador elige su zona de una lista |

### El CSRF: resuelto

Lo que antes era un pendiente ya está cerrado. El token de antifalsificación de ASP.NET
queda ligado a la identidad de la sesión, así que **un token pedido antes de iniciar
sesión no sirve después**.

Se comprobó contra el servidor real, no de oídas: con el token de antes de entrar la API
contesta **403**; pidiendo uno nuevo, **204**.

`src/api/cliente.ts` lo resuelve así: ante un 403 pide un token nuevo y **reintenta una
vez**, nunca más de una. Además, `iniciarSesion` y `cerrarSesion` olvidan el token
guardado, porque la identidad cambió. Hay prueba de los dos caminos, incluido el de que
no reintente sin fin.

---

## 7. El siguiente paso exacto

```
HECHO   MOD-01 Identidad        dominio, API y pantalla
HECHO   MOD-13 Suscripciones    7-nov-2026
HECHO   MOD-03 Catalogo         7-nov-2026, con 72 pruebas nuevas
HECHO   MOD-04 Geolocalizacion   7-nov-2026, con 64 pruebas nuevas

HECHO   MOD-05 Busqueda          7-nov-2026, con 74 pruebas nuevas
                                 (falta BuscarAgregadoPorZona, ver su seccion)

1. LAS PANTALLAS: perfil del prestador y busqueda                      <-- AQUI
   - Todo el backend que necesitan existe y esta probado: API-01 a API-05 y
     API-18 a API-23. Es lo que hace visible y tocable el producto
   - ESTADO: HECHA la pantalla de BUSQUEDA, cableada como portada, vista en un
     navegador real, y con el orden de los resultados ya arreglado.
     FALTA: la pantalla del perfil del prestador, que no existe todavia. Es lo
     siguiente: la busqueda ya esta completa de punta a punta
   - Perfil del prestador: rubro (API-02), servicios (API-03/20), zona
     (API-04/21), publicar (API-22) y ver su suscripcion de prueba
   - Busqueda: rubro, donde (zona, o ubicacion del navegador con el flujo 4A de
     CU-08 si la niega), filtros de CS-09, resultados con la distancia en TEXTO
     y paginacion por cursor
   - Poner la atribucion a OpenStreetMap en el pie del mapa desde el dia uno
   - Verificar correo (API-14) y recuperar contrasena (API-15) van con estas

4. MOD-02 Credenciales -> MOD-06 Agenda -> MOD-11 Resenas -> MOD-12 Tarjetas
   - Antes de MOD-12 hace falta la respuesta a la pregunta 4 de la seccion 9

5. Pruebas de arquitectura (NetArchTest) y filtro de idempotencia

6. (Subido al paso 1: las pantallas.)

7. Trabajos de fondo: vencer suscripciones y avisar antes de que venzan
   - ServicioSuscripciones.VencerExpiradasAsync y PorVencerAsync ya existen
   - Falta quien los llame cada dia
```

**Por que las pantallas van ahora.** Con MOD-05 el camino completo del producto existe y
esta probado por HTTP, pero **nadie puede verlo ni tocarlo**: un prestador no tiene como
publicarse sin llamar a la API a mano, y un buscador no tiene donde buscar. Las pantallas son
lo que convierte 549 pruebas en algo que se le puede ensenar a un municipio.

Despues siguen MOD-02 Credenciales (hoy el sello de verificado no lo puede dar nadie, aunque
la busqueda ya lo lee) y MOD-06 Agenda. `BuscarAgregadoPorZona` conviene antes de la
postulacion municipal, porque es el dato que ese documento va a prometer.

---

## 8. Cómo levantar lo que hay

```bat
cd C:\Users\pdani\zass-app
iniciar-postgres.bat
```

Conexión: `Host=127.0.0.1;Port=5433;Database=zass;Username=zass;Password=<.clave-pg.txt>`

Pruebas (la base tiene que estar arriba):

```bat
cd backend
dotnet test
```

La API necesita dos ajustes, y **ya están puestos** en User Secrets de esta máquina
(`dotnet user-secrets list` dentro de `backend\src\Zass.Api`). Nunca en el repositorio:

```
Zass:CadenaConexion            la de arriba
Zass:ClaveDireccionesBase64    32 bytes en Base64
```

Por eso la API ya arranca sola, sin exportar nada:

```bat
dotnet run --project backend\src\Zass.Api        :: queda en http://localhost:5052
```

Y la interfaz, en otra consola:

```bat
cd frontend
npm install        :: solo la primera vez
npm run dev        :: queda en http://localhost:5173
```

**Se abre http://localhost:5173, no el 5052.** Vite reenvía `/api` a la API para que el
navegador vea un solo origen; si no, descarta la cookie de sesión.

Las pruebas de la interfaz:

```bat
cd frontend
npm run prueba                            :: 31, con fetch simulado, no necesita nada
node pruebas-navegador/flujo-real.mjs     :: registro e ingreso de verdad, necesita los 3 arriba
```

Al terminar: `detener-postgres.bat`.

> **Sobre `iniciar-postgres.bat` y los scripts.** El script arranca la base en unos
> segundos e imprime todo. Lo que puede parecer un cuelgue es otra cosa: el servidor
> hereda el flujo de salida de quien lo lanzó y no lo suelta mientras viva, así que
> **cualquier herramienta que espere a que ese flujo cierre espera para siempre**. No era
> el `timeout` del script, como decía la nota anterior de este archivo: se cambió por
> `ping` y se mandó la salida de `pg_ctl` a `registros\pg_ctl.log`, y aun así ocurre.
>
> Para automatizar, llamar a `pg_ctl` directamente:
>
> ```
> herramientas\pgsql\bin\pg_ctl.exe -D datos-pg -l registros\postgres.log start
> ```

---

## 9. Lo que falta confirmar con Pablo

1. ~~Si un prestador sin suscripción vigente desaparece de la búsqueda o solo aparece
   degradado.~~ **RESPONDIDA el 7 de noviembre de 2026: desaparece.** Escrita en el
   documento como **RN-S3-09** y como filtro duro de CU-08. Ya no bloquea MOD-05.
2. Precio y nombre de los planes.
3. Pasarela simulada en el prototipo, o sin cobro hasta integrar Transbank, Flow o
   Mercado Pago.
4. **Nueva, y la abrió la respuesta anterior.** Desaparecer de la búsqueda está decidido,
   pero no qué pasa con la **ficha pública por enlace directo y por tarjeta NFC** cuando
   la suscripción vence. La tarjeta es un objeto físico que ya está en manos de alguien:
   - Si **deja de funcionar**, el producto que se vendió se apaga al dejar de pagar. Es
     coherente con el modelo, pero quien la tiene en la mano no entiende por qué.
   - Si **sigue funcionando**, hay una puerta al perfil que no pasa por la búsqueda, y el
     prestador impago conserva lo que más se nota del producto.
   - Punto medio posible: la tarjeta abre, pero muestra la ficha **sin los botones de
     contacto ni de agendar**, y con un aviso.

   Afecta a S6 entero (MOD-12) y conviene resolverla **antes** de construirlo.

5. `EsMismaPersona` hoy solo detecta la misma cuenta o el mismo correo. El modelo no
   guarda nada que ligue dos cuentas distintas a una persona —no se pide RUT, por
   minimización—, así que RN-S2-09 y RN-S5-09 tienen un **falso negativo conocido**: dos
   cuentas de la misma persona pasan el control de conflicto de interés. Ampliarlo exige
   decidir **qué dato** lo probaría, y esa decisión no está tomada.

---

## 10. Avisos

| Aviso | Detalle |
|---|---|
| **`.clave-pg.txt` está en `.gitignore`** | Clave del superusuario local. Si se borra, hay que recrear el clúster |
| **El puerto 5433 debe estar libre** | Si no, mirar `registros\postgres.log` |
| **`herramientas/` pesa ~1,2 GB** | En `.gitignore`. Al clonar hay que volver a bajar PostgreSQL y PostGIS |
| **La carpeta no está en OneDrive, a propósito** | Una base sincronizándose en vivo se corrompe |
| **`CifradoAesGcm` exige 32 bytes y no tiene valor por omisión** | Revienta al construirse si falta. Deliberado: una clave de relleno cifraría direcciones reales con un secreto público |
| **Las pruebas de integración escriben en la base real** | Las de aplicación deshacen su transacción; las de HTTP borran por correo al terminar. Si una se interrumpe, puede dejar filas `api-*@zass.local` |
| **`DisableTestParallelization` está activo en integración** | Dos pruebas a la vez se bloquean en el índice único. Correr en serie cuesta segundos y evita rojos intermitentes |
| **El límite de tasa está subido en desarrollo** | `appsettings.Development.json` pone los grupos A y S en 1000. **Ese archivo está en `.gitignore`**, así que producción usa los valores reales del código (5 y 10). Si alguna vez se versiona, revisar esto primero |
| **La clave local de PostgreSQL quedó en el registro de una sesión** | `dotnet user-secrets set` imprime el valor que guarda. Es la clave de una instancia portable que solo escucha en `127.0.0.1:5433`, sin exposición de red, pero conviene rotarla: cambiarla con `ALTER USER`, actualizar `.clave-pg.txt` y volver a poner `Zass:CadenaConexion` en User Secrets |
| **El certificado de desarrollo hay que confiarlo** | `dotnet dev-certs https --trust`, una vez por maquina. Sin eso el navegador avisa en cada visita. La pagina funciona igual aceptando el aviso |
| **`certificados/` tiene una clave privada** | En `.gitignore`. Se regenera con `dotnet dev-certs https --export-path certificados/localhost.pem --format PEM --no-password` |
| **`node_modules` pesa ~300 MB** | En `.gitignore`. Al clonar hace falta `npm install` dentro de `frontend` |
| **Playwright baja Chromium (~123 MB)** | Queda en `%LOCALAPPDATA%\ms-playwright`, fuera del proyecto. Al clonar: `npx playwright install chromium` |
| **Las coordenadas de las zonas son de OpenStreetMap (ODbL)** | La licencia pide atribuir a *OpenStreetMap contributors* allá donde se muestren. Son 16 puntos y nombres de barrio, pero conviene poner la atribución en el pie del mapa desde el primer día en vez de acordarse después |
| **El clúster local se creó con locale `C`** | `lower()` y `upper()` solo pliegan ASCII. Ninguna regla puede apoyarse en ellas: por eso `servicio` lleva `nombre_normalizado` calculado en C#. Si alguna vez se recrea el clúster, **no** cambia nada de esto, y es a propósito: en Azure el locale será otro |
| **Subir al repositorio exige revisar la historia, no solo `.gitignore`** | Antes del primer `push` se revisaron los nombres de archivo de **todos** los commits buscando claves, `.env`, certificados y `appsettings.Development.json`. No había ninguno. Un `.gitignore` correcto no dice nada de lo que ya se versionó |
