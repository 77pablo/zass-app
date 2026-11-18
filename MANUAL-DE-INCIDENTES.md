# Manual de incidentes de ZASS! — qué hacer cuando algo se rompe

**Quién lo usa:** Pablo (no hace falta ser programador). Cada paso dice su **fuente**
`(fuente: archivo, sección o línea)`. Cada sección tiene la misma forma:
**Cómo te das cuenta** · **Qué hacer, en orden** · **Cómo sabes que quedó bien** · **A quién avisar**.

Solo se dice lo que el repositorio dice que existe. Si un flujo de GitHub tiene `workflow_dispatch`,
se puede lanzar a mano desde la pestaña **Actions** del repositorio: aquí se dice. Si no lo tiene,
**no se dice** que se puede.

Un paso que hace falta pero no está en el repo se marca así:
«**Pendiente de Pablo:** confirmar cómo se hace X en el panel de …».

Nunca aparece un valor secreto ni un identificador de servicio o de despliegue: solo **nombres**
de variables y de flujos.

---

## En un minuto

- **La API no responde** → lanza a mano el flujo «Despertar API».
- **Un despliegue salió mal** → en Render, reactiva el despliegue anterior.
- **Una migración falló en Neon** → mira con qué paso falló la corrida y descarga la copia que hizo antes.
- **Se perdieron datos** → restaura una copia diaria en una base vacía, nunca sobre la viva.
- **Se filtró un secreto** → no copies el valor; rota el secreto; el escaneo vuelve a correr solo con un push/PR.
- **Abrir o cerrar el sitio** → cambia `MANTENIMIENTO` en Cloudflare Pages y vuelve a desplegar.
- **El sitio carga pero la API da error** → toma el `idPeticion` del error y búscalo en el registro de Render.

---

## 1. La API no responde

### Cómo te das cuenta
- Pedir `GET /api/salud` y no responde, o tarda decenas de segundos. La causa conocida es el plan
  gratis de Render, que duerme el servicio tras ~15 min sin tráfico. (fuente: `.github/workflows/despertar-api.yml`, líneas 1–3)
- El flujo «Despertar API» no está corriendo: su programación solo cubre la ventana de lanzamiento
  (del 22 al 24 de noviembre), no antes. (fuente: `.github/workflows/despertar-api.yml`, línea 9)

### Qué hacer, en orden
1. Lanza a mano el flujo «Despertar API» desde **Actions** del repositorio (tiene `workflow_dispatch`).
   (fuente: `.github/workflows/despertar-api.yml`, línea 10)
2. Mira la corrida: pide `/api/salud` y anota el código HTTP y el tiempo. Si dice `200`, se despertó.
   (fuente: `.github/workflows/despertar-api.yml`, líneas 20–28)
3. El despertador **no arregla la causa**: si Render vuelve a dormir el servicio, vuelve a pasar.
   Confirma que el servicio siga vivo en el panel de Render. (fuente: `DESPLIEGUE-API.md`, sección 3.3, punto 4)
4. Si el servicio en Render cayó o no arranca, hay que **volver a desplegarlo a mano**: Render no
   despliega solo. **Pendiente de Pablo:** confirmar cómo se hace «desplegar a mano» en el panel de Render.
   (fuente: `ESTADO.md`, sección «Sesión del 17-nov», «cada despliegue se lanza a mano»; `DESPLIEGUE-API.md`, sección 3.3)

### Cómo sabes que quedó bien
- `GET /api/salud` da `200` `{"estado":"vivo"}` y `GET /api/salud/lista` da `200` `{"estado":"lista"}`.
  (fuente: `DESPLIEGUE-API.md`, sección 1)

### A quién avisar
- A Pablo: el servicio de Render es suyo.

---

## 2. Un despliegue salió mal: volver al anterior

### Cómo te das cuenta
- Después de un despliegue, las respuestas vuelven a estar mal: un error de validación que ya no
  estaba, un texto en inglés cuando debía estar en castellano, o las sondas dejan de dar `200`.
  (fuente: `ESTADO.md`, sección «Sesión del 17-nov», tabla «Comprobado en producción después del despliegue»)

### Qué hacer, en orden
1. En Render, en el servicio de la API, **reactiva el despliegue anterior** (Render guarda el
   historial de despliegues del servicio y permite volver a un despliegue pasado).
   **Pendiente de Pablo:** confirmar en el panel de Render cuál es el despliegue anterior exacto y
   cómo se reactiva (en `ESTADO.md` queda anotado el despliegue anterior del 17-nov, pero este manual
   no copia identificadores de despliegue). (fuente: `ESTADO.md`, sección «Sesión del 17-nov», línea «Vuelta atrás si hiciera falta».)
2. No hay ningún flujo de rollback en el repositorio: esto se hace **solo** desde Render.
   (fuente: no existe ningún `.github/workflows/*rollback*.yml` en `origin/main`)
3. Render no despliega solo, así que el cambio queda hasta que se confirme desde el panel.
   (fuente: `ESTADO.md`, sección «Sesión del 17-nov», «cada despliegue se lanza a mano»)

### Cómo sabes que quedó bien
- Las sondas vuelven a dar `200` y las respuestas vuelven a las de **antes** del despliegue malo.
  (fuente: `DESPLIEGUE-API.md`, sección 1)

### A quién avisar
- A Pablo: Render es suyo.

---

## 3. Una migración falló en Neon

### Cómo te das cuenta
- La corrida del flujo **«Migrar la base de produccion»** sale **roja**. El registro dice con qué
  paso falló. (fuente: `.github/workflows/migrar-neon.yml`)
- Ese flujo **solo** se lanza a mano: exige escribir `MIGRAR` en mayúsculas en el campo de
  confirmación. No tiene programación ni se lanza con un push. (fuente: `.github/workflows/migrar-neon.yml`, líneas 12–25)

### Qué hacer, en orden
1. Abre el registro de la corrida y lee **con qué paso** falló:
   - «quedan migraciones pendientes después de aplicar»: el `database update` no aplicó todo.
     (fuente: `.github/workflows/migrar-neon.yml`, líneas 136–141)
   - «el modelo espera esto y la base no lo tiene»: falta una tabla, un índice o una clave. Sale del
     script que compara el modelo con el catálogo. (fuente: `.github/workflows/migrar-neon.yml`, líneas 153–167; `.github/scripts/esquema-esperado.py`, sección de uso)
2. El flujo, **antes** de tocar la base, ya volcó y cifró la base y la subió como artefacto
   `copia-antes-de-migrar`. Descárgalo (se descifra con la frase de copias) para tener la base tal
   como estaba. (fuente: `.github/workflows/migrar-neon.yml`, líneas 53–79)
3. Si la migración quedó a medias (se hizo un `CREATE` y falta su pareja, o falta un `DROP`),
   **no la relances a ciegas**: primero decide si se **restaura la copia anterior** o se **completa**
   la migración. **Pendiente de Pablo:** esa decisión (con la copia descargada a mano) es de Pablo.
   (fuente: `.github/workflows/migrar-neon.yml`, comentario «volcar y cifrar la base ANTES de tocarla»)

### Cómo sabes que quedó bien
- Una re-corrida del flujo sale **verde**: `migrations list` sin `(Pending)` y el catálogo completo
  («la base tiene todas las tablas, índices y claves del modelo»). (fuente: `.github/workflows/migrar-neon.yml`, líneas 136–142 y 168)

### A quién avisar
- A Pablo: Neon es suyo, y una migración de producción solo la decide y lanza él.

---

## 4. Se perdieron datos: restaurar una copia

### Cómo te das cuenta
- Una consulta que debía dar filas da **cero**, o la app pierde datos (una tabla vacía, una cita que
  desapareció). Se confirma contra la base de producción. (fuente: no hay flujo que lo detecte solo: se mira en Neon)

### Qué hacer, en orden
1. **Pendiente de Pablo:** fijar **desde cuándo** se perdió el dato. La copia diaria cubre desde la
   última vez que corrió «Copia de la base de datos» (una vez al día). (fuente: `.github/workflows/copia-base-de-datos.yml`, línea 10)
2. Elige la copia a restaurar: un artefacto `copia-zass` de la corrida «Copia de la base de datos»,
   la más cercana **antes** de la pérdida. (fuente: `.github/workflows/copia-base-de-datos.yml`, líneas 39–44)
3. **Antes de necesitarla**, verifica que esa copia restaura bien: el flujo «Probar la restauracion de
   la copia» toma la última copia, la descifra y la restaura en un Postgres 18 + PostGIS **desechable**
   del runner, y comprueba estructura y datos. Se puede lanzar a mano (tiene `workflow_dispatch`).
   (fuente: `.github/workflows/probar-restauracion.yml`, líneas 1–16)
4. La restauración real se hace en **Neon**, en una base vacía con PostGIS, **no sobre la base viva**:
   descifra (`gpg -d …`) y restaura con `pg_restore`. **Pendiente de Pablo:** la restauración real
   exige un cliente de Postgres **18** (el `pg_restore` de este PC es 16.4 y no lee un volcado de 18).
   (fuente: `DESPLIEGUE-API.md`, sección «Copias de seguridad», «Restaurar» y el aviso «No se puede
   restaurar con las herramientas de este PC»)

### Cómo sabes que quedó bien
- El flujo «Probar la restauracion de la copia» sale **verde**: la estructura es la del modelo de esa
  copia, los datos sembrados están (planes = 3, cupos fundador = 50, el índice de doble reserva
  presente, hay usuarios) y PostGIS funciona (una distancia real). (fuente: `.github/workflows/probar-restauracion.yml`, líneas 82–131)

### A quién avisar
- A Pablo: Neon es suyo.

---

## 5. Se filtró un secreto

### Cómo te das cuenta
- El flujo **«Escaneo de secretos»** (`secretos.yml`) salió **rojo** sobre una rama o sobre `main`,
  o alguien vio el valor de un secreto en el chat, en el código o en un log. (fuente: `.github/workflows/secretos.yml`, líneas 34–37)
- Ese flujo corre **solo** con un push a `main` y con cada PR: **no** tiene `workflow_dispatch`, así
  que **no se puede** lanzar a mano. (fuente: `.github/workflows/secretos.yml`, líneas 3–7)

### Qué hacer, en orden
1. **No copies el valor filtrado a ningún lado** (no al informe, no a un issue, no a un commit).
   Anota **solo** el archivo, el commit y el **tipo** de secreto. (fuente: regla del plan T2; `docs/07-fisica/seguridad.md`, sección 2 «Regla original», «Nunca claves en el código»)
2. **Rota** el secreto (cámbialo). Los secretos que hay que saber rotar, **solo por nombre de
   variable**:
   - En **Render** (variables de entorno de la API): `Zass__CadenaConexion`, `Zass__ClaveDireccionesBase64`,
     `Zass__Correo__ApiKey`. (fuente: `DESPLIEGUE-API.md`, sección «Variables de entorno»)
   - En **GitHub Actions** (secretos del repositorio): `NEON_URL_COPIAS`, `FRASE_COPIAS`.
     (fuente: `.github/workflows/copia-base-de-datos.yml`, líneas 4–5 y 21–28)
   - **Ojo con la clave de direcciones:** si se filtró `Zass__ClaveDireccionesBase64`, las direcciones
     ya guardadas **no se pueden descifrar** con la clave nueva. Hay que guardar la nueva y decidir qué
     se hace con las guardadas. (fuente: `DESPLIEGUE-API.md`, sección «Variables de entorno», `Zass__ClaveDireccionesBase64`)
   - Si se filtró la clave de API de Render (el archivo de secretos de Render, que solo Pablo tiene),
     se rota en el panel de Render. **Pendiente de Pablo:** confirmar cómo se rota esa clave de Render.
3. **Re-corre el escaneo**: como el escaneo no se lanza a mano, haz un push a la rama o abre un PR
   para que `gitleaks` vuelva a escanear. (fuente: `.github/workflows/secretos.yml`, líneas 3–7)

### Cómo sabes que quedó bien
- El flujo «Escaneo de secretos» sale **verde** sobre `main`, con el valor ya rotado.
  (fuente: `.github/workflows/secretos.yml`)

### A quién avisar
- A Pablo: es él quien rota los secretos de Render, Neon y GitHub.

---

## 6. Abrir o cerrar el sitio (mantenimiento)

### Cómo te das cuenta
- **Cerrado:** `https://zass.cl/` da **503** con la página de mantenimiento. **Abierto:** da **200**
  con el sitio. El sitio responde **503 a propósito** (no 200) para que el buscador no indexe «en
  mantenimiento». (fuente: `frontend/functions/_middleware.js`, líneas 23–26; `ESTADO.md`, sección «Sesión del 14-nov», tabla «Medido contra zass.cl»)

### Qué hacer, en orden
- **Para CERRAR el sitio:**
  1. En **Cloudflare Pages**, en las **variables del proyecto**, deja `MANTENIMIENTO` en un valor
     distinto de `0` (o quítala: el sitio está cerrado por omisión). (fuente: `frontend/functions/_middleware.js`, líneas 9–14 y 51–55)
  2. **Vuelve a desplegar**: una variable nueva **no** se aplica a un despliegue que ya existe.
     **Pendiente de Pablo:** el despliegue de Cloudflare Pages (`wrangler pages deploy --branch main`)
     lo hace la sesión principal con su permiso. (fuente: `ESTADO.md`, sección «Sesión del 14-nov», puntos «una variable nueva NO se aplica» y «wrangler pages deploy usa el nombre de la rama»)
- **Para ABRIR el sitio:**
  1. En **Cloudflare Pages**, en las variables del proyecto, pon `MANTENIMIENTO=0`. (fuente: `frontend/functions/_middleware.js`, línea 13)
  2. **Vuelve a desplegar** (mismo motivo). (fuente: `ESTADO.md`, sección «Sesión del 14-nov»)
- **Para MIRAR el sitio sin abrirlo (pase):**
  1. Pon `MANTENIMIENTO_PASE=<algo largo y que no se adivine>` en las variables de Cloudflare Pages.
     (fuente: `frontend/functions/_middleware.js`, línea 17)
  2. Entra **una** vez a `zass.cl/?pase=<ese valor>`: queda una cookie por **8 horas**. Sin esa
     variable puesta no hay puerta de atrás. (fuente: `frontend/functions/_middleware.js`, líneas 18–22)
- Las variables van en **Cloudflare Pages** (proyecto `zass` → Settings → Variables), **no** en
  Render, que solo corre la API. (fuente: `ESTADO.md`, sección «Sesión del 14-nov», «Cómo se maneja»)

### Cómo sabes que quedó bien
- **Cerrado:** `zass.cl/` da 503 con la página; `zass.cl/api/*` da 503; pero la API directa en Render
  da 200 (el servicio sigue vivo). (fuente: `ESTADO.md`, sección «Sesión del 14-nov», tabla «Medido contra zass.cl»)
- **Abierto:** `zass.cl/` da 200 con el sitio normal. (fuente: `ESTADO.md`)

### A quién avisar
- A Pablo: Cloudflare Pages es suyo, y abrir o cerrar el sitio es su decisión.

---

## 7. El sitio carga pero la API da error

### Cómo te das cuenta
- El sitio (`zass.cl`) carga, pero las llamadas a la API fallan: un **503** del proxy («API no
  configurada»), un **502/504** de Render, o un error de la API con `idPeticion` en el cuerpo.
  (fuente: `frontend/functions/api/[[ruta]].js`, líneas 14–20)
- El navegador ve la API a través del proxy de Cloudflare Pages: todo `/api/*` de `zass.cl` se
  reenvía al origen. (fuente: `frontend/functions/api/[[ruta]].js`, líneas 1–13)

### Qué hacer, en orden
1. Mira **cómo** falla:
   - Da **503** «API no configurada» / «Falta la variable API_ORIGEN»: falta la variable `API_ORIGEN`
     en Cloudflare Pages. (fuente: `frontend/functions/api/[[ruta]].js`, líneas 14–20)
   - Da **502/504** o tarda mucho: la API (Render) no responde. Ve a la **sección 1**.
     (fuente: `frontend/functions/api/[[ruta]].js`, línea 33, «devuelve la respuesta de la API tal cual»)
   - Da un **5xx o un 4xx inesperado** con `idPeticion` en el cuerpo: **toma ese código** (16
     caracteres). (fuente: `backend/src/Zass.Api/Configuracion/IdDePeticion.cs`, líneas 24–25 y 66–68)
2. Con ese código (`idPeticion`), busca en el **registro de Render** todas las líneas de esa
   petición: van juntas en un ámbito de registro con ese identificador. **Pendiente de Pablo:** el
   registro se ve en el panel de Render. **Ojo:** un 404 de ruta o un error de validación **no escribe
   ninguna línea**, así que su id no aparece (es lo esperado). (fuente: `backend/src/Zass.Api/Configuracion/IdDePeticion.cs`, líneas 8–17; `ESTADO.md`, sección «Sesión del 17-nov», «El id en el registro de Render funciona»)
3. El proxy **devuelve la respuesta de la API tal cual** (con las cabeceras), así que `X-Id-Peticion`
   llega al navegador y el `idPeticion` del cuerpo es el mismo id del registro. (fuente: `frontend/functions/api/[[ruta]].js`, línea 33; `backend/src/Zass.Api/Configuracion/IdDePeticion.cs`, líneas 41–45)

### Cómo sabes que quedó bien
- La respuesta por `zass.cl/api/...` vuelve a dar **200** con el cuerpo esperado, y, si hubo error, su
  `idPeticion` encuentra sus líneas en el registro de Render. (fuente: `DESPLIEGUE-API.md`, sección 1; `ESTADO.md`, sección «Sesión del 17-nov»)

### A quién avisar
- A Pablo: Render y su registro son suyos.
