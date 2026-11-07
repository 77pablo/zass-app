# ZASS! — estado del desarrollo

**Última sesión:** 7 de noviembre de 2026, tarde (MOD-03 Catálogo, y el repositorio ya está en GitHub).

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

**MOD-01 Identidad, MOD-03 Catálogo y MOD-13 Suscripciones están construidos, con 319
pruebas en verde** (170 de aplicación sin base de datos, 118 de integración contra
PostgreSQL real y la API por HTTPS, 31 de interfaz) más dos comprobaciones de navegador
que crean una cuenta y entran contra la base de verdad.

MOD-01 llega hasta la pantalla. MOD-03 y MOD-13 están completos de dominio a API y
**todavía sin pantalla**.

**Todo el entorno local va por HTTPS.** `https://localhost:5173` la interfaz y
`https://localhost:7277` la API, con el certificado de `dotnet dev-certs`. Eso permitió
activar la marca `Secure` en las cookies también en desarrollo, y ahora hay una prueba
que la comprueba; antes solo se podía ver desplegando.

**El repositorio ya está en GitHub: `77pablo/zass-app`, privado.** 11 commits, 116
archivos. La instancia portable de PostgreSQL, los dos secretos y la clave privada del
certificado quedan fuera, y eso se comprobó revisando la historia entera antes de subir,
no de oídas. Hay `README.md` para quien abra el repositorio.

Lo que falta es MOD-02 y MOD-04 a MOD-12, el resto de las pantallas y los trabajos de
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

Endpoints: **API-02, API-03, API-05, 08-20, 22, 23**, más `GET /api/auth/csrf` y
`GET /api/salud`. Falta **API-21** (cobertura), que es de MOD-04.

### Las pruebas, y qué prueba cada grupo

| Proyecto | Cuántas | Qué cubre |
|---|---|---|
| `Zass.Aplicacion.Pruebas` | **170** | Las reglas, sin base de datos: RN-S1-01 a RN-S1-08, RN-S3-02, RN-S5-11, CC-02, y MOD-03 completo |
| `Zass.Integracion.Pruebas` | **118** | Argon2id y AES-GCM reales, el contenedor, PostgreSQL real, y la API por HTTPS |
| `frontend` (Vitest) | **31** | Cliente HTTP y pantallas, con `fetch` simulado |
| `frontend/pruebas-navegador` | 2 guiones | Navegador real contra la API real: que la página monta, los colores de marca, y el registro e ingreso completos |

Escritas con TDD: cada prueba se vio fallar antes de que existiera el código.

Las 38 de MOD-03 se escribieron antes del servicio, pero pasaron todas en la primera
ejecución, y eso no prueba nada por sí solo. Se comprobó rompiendo cuatro reglas del
servicio a propósito —el `es_catalogo` de los servicios nuevos, la desactivación de lo que
sale de la lista, el rechazo de la lista vacía y la actualización de la duración—: cayeron
exactamente cuatro pruebas, una por regla. Después se restauró el código.

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

En los cuatro casos **se movió el documento**, no el código, salvo `rubro_id`, donde el
documento se contradecía consigo mismo: marcaba la columna obligatoria y a la vez CU-03
guarda el avance en borrador antes de elegir rubro. Mandó el caso de uso.

---

## 6. Lo que NO está hecho

| Capa | Falta |
|---|---|
| `Zass.Aplicacion` | **MOD-02 y MOD-04 a MOD-12.** MOD-01, MOD-03 y MOD-13 listos |
| `Zass.Api` | Filtro de idempotencia (`Idempotency-Key`), que se necesita desde API-36 |
| Pruebas de arquitectura | Las 4 reglas de `paquetes-por-capas.md` §5 **no se verifican solas** todavía |
| Datos de ejemplo | Zonas, prestadores, reseñas. Los roles, el plan de prueba, los **rubros** y los **servicios** ya están, y como dato del esquema, no como siembra aparte |
| Frontend | Hechas las pantallas de **registro e ingreso**. Faltan: verificar correo, recuperar contraseña, perfil del prestador (ya tiene API-02, API-03 y API-20 detrás) y búsqueda |
| Trabajos en segundo plano | Recordatorios, vencimientos, bloques, purgas |
| Notificaciones | Se encolan en TBL-27; **nadie las despacha**. Decidido: buzón local `.eml` |
| Suscripciones | Tablas sí, lógica y pasarela no |
| Repositorio git | **Hecho y subido:** `77pablo/zass-app` en GitHub, privado |

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

1. MOD-04 Geolocalizacion: zonas y cobertura (TBL-10, 11)      <-- AQUI
   - IServicioGeografia, API-04, API-21
   - zonas de Temuco como dato del esquema, igual que los rubros
   - R-2: una sola zona principal por prestador
   - CIERRA la prueba que todavia falta: publicar -> prueba -> visible.
     Hoy publicar ya solo pide la zona, y hay una prueba que lo afirma

3. MOD-05 Busqueda: API-01, con FromSqlInterpolated y el indice GIST
   - Ya desbloqueado: RN-S3-09 dice que el impago DESAPARECE
   - El filtro de suscripcion va DENTRO del SQL, no resultado por resultado
   - Llamar a IServicioSuscripciones.VigentesEntreAsync, nunca a EstaVigente

4. MOD-02 Credenciales -> MOD-06 Agenda -> MOD-11 Resenas -> MOD-12 Tarjetas
   - Antes de MOD-12 hace falta la respuesta a la pregunta 4 de la seccion 9

5. Pruebas de arquitectura (NetArchTest) y filtro de idempotencia

6. Las pantallas que faltan, sobre el cliente que ya existe en frontend/src/api:
   verificar correo (API-14), recuperar contrasena (API-15), perfil del
   prestador (API-17/18) y busqueda (API-01)

7. Trabajos de fondo: vencer suscripciones y avisar antes de que venzan
   - ServicioSuscripciones.VencerExpiradasAsync y PorVencerAsync ya existen
   - Falta quien los llame cada dia
```

**La decision a tomar primero** es si MOD-04 o la pantalla de perfil del prestador. Con
MOD-04 el perfil se puede publicar de verdad y el camino completo queda probado de punta a
punta; con la pantalla primero, se puede ver y tocar lo que ya existe, pero publicar sigue
sin poder completarse. Por eso va MOD-04.

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
| **El clúster local se creó con locale `C`** | `lower()` y `upper()` solo pliegan ASCII. Ninguna regla puede apoyarse en ellas: por eso `servicio` lleva `nombre_normalizado` calculado en C#. Si alguna vez se recrea el clúster, **no** cambia nada de esto, y es a propósito: en Azure el locale será otro |
| **Subir al repositorio exige revisar la historia, no solo `.gitignore`** | Antes del primer `push` se revisaron los nombres de archivo de **todos** los commits buscando claves, `.env`, certificados y `appsettings.Development.json`. No había ninguno. Un `.gitignore` correcto no dice nada de lo que ya se versionó |
