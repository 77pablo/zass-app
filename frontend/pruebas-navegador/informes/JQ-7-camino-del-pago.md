# JQ-7 · El camino del pago, visto en un navegador

Recorrido del 19 y 20 de noviembre de 2026, rama `j/pago-recorrido`, base local `zass`
(127.0.0.1:5433), API en `https://localhost:7277` y front en `http://localhost:5173` (en este
worktree no hay certificado, así que Vite levanta en HTTP).

**Qué se usó.** El guion `pruebas-navegador/camino-del-pago.mjs`, fase por fase, con Chromium
(Playwright) en escritorio (1100×900) y en celular (390×844). La pasarela es la **simulada**: el
código no tiene ningún adaptador de pasarela real, así que no se cobró nada. El guion no toca la base. Los estados que
había que forzar (reserva vencida, agotado) y las comprobaciones en la base se hicieron con los bloques de
`datos/camino-del-pago/`, que corrió Joaquín a mano.

**Es un informe, no un arreglo.** Todo lo de abajo es zona de Pablo (suscripciones, planes, pagos,
cupos, cupón) o necesita una migración. No se cambió ninguna línea del producto.

**Cómo leerlo.** «Visto» significa que se vio en pantalla o en la respuesta de la API, o que lo
confirmó un bloque SQL. Lo que solo sale de leer el código va marcado como tal. Las capturas
quedaron en local (`registros/capturas/jq7/`, ignorada por git); se citan por nombre.

---

## Resumen

| # | Qué | Prioridad | Dónde |
|---|---|---|---|
| 1 | Un aviso tardío deja a una persona como fundadora **sin cupo**: hubo 2 fundadores para 1 cupo | **Alta** (posible defecto) | `ServicioCobros.cs:732` |
| 2 | Volver a tomar el cupo ya confirmado corre el mes gratis hasta hoy + 30 días | **Alta** | `Planes.tsx:27-30`, `ServicioSuscripciones.cs:430-450` |
| 3 | Después de «Simular un rechazo», la pantalla dice «Tu medio de pago quedó inscrito» | **Alta** | `PagoSimulado.tsx:47`, `Planes.tsx:327-345` |
| 4 | `/planes`, la pasarela y la vuelta no tienen ningún estilo: el contenido queda pegado al borde | Media | `Planes.tsx`, `PagoSimulado.tsx`; sin reglas en `index.css` |
| 5 | El aviso «Esta es una pasarela de PRUEBA» casi no se ve; los errores tampoco tienen estilo | Media | `PagoSimulado.tsx:68`; `index.css:660-662` |
| 6 | No hay forma de suscribirse al plan Mensual, aunque la pantalla y el 409 lo ofrecen | Media | `Planes.tsx:267-274` |
| 7 | El cupón se cotiza pero no se puede usar, y no dice a qué plan se aplica | Media | `Planes.tsx:277-311`; `iniciarCobro` sin uso |
| 8 | Sin sesión, el Fundador no dice que el primer mes es gratis | Baja | `Planes.tsx:228-245` |
| 9 | Faltan tildes en las descripciones de los planes («suscripcion», «busquedas») | Baja | Datos sembrados por migración |
| 10 | Crear la cuenta desde `/planes` no lleva de vuelta a `/planes` | Baja | `CrearCuenta.tsx`, `Entrar.tsx:39` |
| — | Fuera del alcance: el registro dice «Ya existe una cuenta con ese correo» | Para Pablo (seguridad) | `ServicioRegistro.cs:73` |

Lo que sí funciona, visto: el contador «Quedan N de 50» coincide con la API en todos los estados; el
camino feliz (tomar el cupo, inscribir, quedar como fundador en Prueba con 30 días y aparecer en la
búsqueda); el doble clic toma **un** solo cupo y deja **un** solo intento; el rechazo devuelve el cupo
al tiro; la reserva vencida vuelve a contar como libre sin esperar al trabajo diario; el cupón
`LANZAMIENTO30` calcula bien (7.990 − 30 % = 5.593), acepta minúsculas y espacios, y un código
inexistente da error sin borrar lo escrito; la pantalla de agotado muestra el Fundador «Cerrado» y sin botón.

---

## Defectos

### 1 · Posible defecto, prioridad alta — dos fundadores para un solo cupo

**Pasos** (fases `tardia`, `tardia-reservar` y `tardia-seguir`, con la segunda prestadora demo `cristobal-henriquez`):
1. Marcela aprieta «Quiero mi cupo» y se queda en la pasarela sin inscribir. Reserva el cupo 1.
2. Pasan los 25 minutos (bloque `30-vencida.sql`). El contador vuelve a 50: el cupo cuenta como libre.
3. Cristóbal toma un cupo e inscribe. Se lleva **el cupo 1** y queda como fundador; el contador baja a 49.
4. Marcela vuelve a su pestaña de la pasarela y aprieta «Inscribir».

**Qué se ve.** El aviso responde `200`: «Tu cupo de fundador quedó confirmado y tu mes gratis empezó a
correr». Marcela queda como **ZASS! Fundador**, en Prueba, con 30 días. El contador sigue en 49.

**Qué confirmó la base** (`50-tardia-revisar.sql`, solo lectura):
```
cristobal-henriquez | fundador | Prueba | cupo 1 | confirmado
marcela-soto        | fundador | Prueba | (sin cupo)
suscripciones_fundador 2 | cupos_confirmados 1
Fundadores vigentes SIN cupo confirmado: marcela-soto
```

**Qué se esperaba.** Que un aviso que llega cuando el cupo ya es de otra persona no deje a nadie como
fundador sin cupo. La regla RN-S7-02 no dice qué hacer con una confirmación tardía, pero la pantalla mostró
ese cupo libre y después lo confirmó igual. Repetido, deja más de 50 fundadores con precio congelado.

**Por qué pasa (leído en el código).**
- `TomarCupoLibreAsync` (`RepositorioSuscripciones.cs:286-301`) toma el número más bajo y cuenta las
  reservas vencidas como libres. Por eso Cristóbal se lleva el cupo 1 de Marcela.
- Al confirmar, `ServicioCobros.cs:732` llama a `ConfirmarCupoFundadorAsync` y **no mira el resultado**,
  que ya es una falla («Este perfil no tiene un cupo fundador que confirmar», `ServicioSuscripciones.cs:394-397`).
- En la línea siguiente, `PasarAFundadorConMesGratisAsync` la deja en el plan fundador igual.

Captura: `DEFECTO-tardia-seguir-1.png`, `9-marcela-inscribe-tarde.png`.

### 2 · Volver a tomar el cupo corre el mes gratis

**Pasos** (fase `retomar`): Marcela ya es fundadora y vuelve a `/planes`.

**Qué se ve.** Se le ofrece otra vez «Quiero mi cupo». Si lo aprieta e inscribe, su vigencia pasa de
`2026-12-20T02:54:21Z` a `2026-12-20T03:12:50Z`: 30 días contados desde ese momento. El estado y el
contador no cambian.

**Qué se esperaba.** Que a quien ya tiene su cupo no se le ofrezca tomarlo de nuevo, o que hacerlo no
mueva la vigencia. Hoy, apretando el botón cada tanto, el mes gratis no termina nunca.

**Por qué pasa (leído en el código).** La pantalla decide qué mostrar solo por el rol (`Planes.tsx:27-30`),
sin saber si la persona ya tiene cupo; la API acepta la inscripción (devuelve el mismo cupo); y al
confirmar, `PasarAFundadorConMesGratisAsync` deja la vigencia en «la más lejana entre la que tenía y hoy
+ 30 días» (`ServicioSuscripciones.cs:450`), que el día siguiente siempre es la segunda.

Capturas: `DEFECTO-retomar-1.png`, `8-planes-ya-fundadora.png`, `8-pasarela-por-segunda-vez.png`.

### 3 · Un rechazo se anuncia como éxito

**Pasos** (fase `rechazo`): «Quiero mi cupo» → «Simular un rechazo».

**Qué se ve.** La persona llega a `/planes/listo`, que dice «Estamos confirmando tu cupo. Tu medio de pago
quedó inscrito y estamos esperando la confirmación… Cuando llegue, tu ficha pasa al plan fundador». Por
debajo el rechazo funcionó bien: el contador volvió de 49 a 50 y Marcela siguió en Prueba.

**Qué se esperaba.** Que dijera que no se pudo inscribir y ofreciera intentar de nuevo.

**Por qué pasa.** `PagoSimulado.tsx:47` navega a `/planes/listo` tanto si se aprueba como si se rechaza, y
`PlanesListo` (`Planes.tsx:327-345`) es texto fijo: no consulta cómo quedó el intento. La especificación
dice que la vuelta «consulta el estado» (`docs/superpowers/specs/2026-11-13-cupo-fundador-design.md`, §6 y §8).
Con una pasarela real, cualquier rechazo del banco se vería igual.

Capturas: `DEFECTO-rechazo-4.png`, `4-vuelta-tras-rechazo.png`.

### 4 · Las pantallas del pago no tienen estilo: contenido pegado al borde

**Qué se ve.** En `/planes`, `/pago/inscripcion` y `/planes/listo`, el título, el contador y los precios
empiezan en **x = 0 px**, en escritorio y en celular; el menú empieza en 12 px. En el navegador de Joaquín
se llega a cortar la primera letra. Además no hay tarjetas, el Fundador no se destaca, las listas no
tienen viñetas y el botón ocupa todo el ancho.

**Por qué pasa.** Ninguna de las clases de `Planes.tsx` (`pagina--planes`, `planes-cabecera`,
`planes-cupos`, `plan-tarjeta`, `plan-tarjeta--destacada`, `planes-rejilla`, `plan-letra-chica`,
`cupon-desglose`, `planes-pie`…) tiene reglas en `frontend/src`, y la página no usa `.contenedor`
(`index.css:97`), que es el que da el margen al resto del sitio.

Capturas: `1-planes-sin-sesion.png` y `-celular`, `DEFECTO-sin-sesion-1..6.png`, `4-pasarela-de-prueba.png`.

### 5 · El aviso de PRUEBA y los errores no se ven como avisos

**Qué se ve.** «Esta es una pasarela de PRUEBA. No es un banco, no se cobra nada…» sale como una línea
de texto en negrita, sin fondo ni borde (fondo `rgba(0, 0, 0, 0)`, borde `none`), más chica que el título.
El error del cupón («Ese código de descuento no existe.») tampoco tiene fondo ni borde.

**Por qué importa.** El comentario de `PagoSimulado.tsx` pide que el aviso se vea «en grande», porque
una pantalla de pago que se parece a la de un banco y no lo es se presta a confusión.

**Por qué pasa.** Las clases `aviso--atencion` (`PagoSimulado.tsx:68`) y `aviso--error` (`Planes.tsx`,
tres lugares) no existen; en `index.css:661-662` solo hay `aviso--malo` y `aviso--bueno`.

Capturas: `DEFECTO-rechazo-1.png`, `DEFECTO-cupon-2.png`, `3-cupon-inexistente.png`.

### 6 · El plan Mensual no se puede tomar

**Qué se ve.** La tarjeta del Mensual no tiene ningún botón, ni con sesión ni sin ella. Con los cupos
agotados la pantalla dice «El plan mensual sigue abierto», y la respuesta 409 de API-69 dice «Puedes
suscribirte al plan mensual»; en los dos casos no hay por dónde hacerlo.

**Dónde.** `Planes.tsx:267-274`: la tarjeta del Mensual solo tiene letra chica. `iniciarCobro`
(`funciones/planes/servicio.ts:69`, API-68) existe y ninguna pantalla lo llama.

Capturas: `6p-agotado-sin-sesion.png`, `7-planes-con-sesion.png`.

### 7 · El cupón se cotiza pero no se usa

**Qué se ve.** `LANZAMIENTO30` muestra el desglose correcto (7.990 − 2.397 = 5.593), pero después no hay
ningún botón para pagar con ese descuento. Además, el bloque del cupón queda debajo de los dos planes y no
dice que se calcula sobre el Mensual: quien viene por el Fundador puede creer que paga $5.593.

**Dónde.** `Planes.tsx:277-311` (cotiza contra `CODIGO_MENSUAL`); `iniciarCobro` sin uso, igual que en el 6.

Capturas: `3-cupon-valido.png`, `DEFECTO-cupon-1.png`.

### 8 · Sin sesión, el Fundador no dice que el primer mes es gratis

**Qué se ve.** Sin sesión, o con sesión pero sin ficha, la tarjeta del Fundador no menciona el mes gratis; la
del Mensual sí («Tu primer mes también es gratis»), y la bajada de arriba también. Con sesión de
prestador sí aparece.

**Dónde.** `Planes.tsx:239-243`: la letra chica del Fundador va dentro de la rama `puerta === 'puede'`.

Capturas: `DEFECTO-sin-sesion-8.png`, `DEFECTO-sin-ficha-10.png`.

### 9 · Faltan tildes en las descripciones de los planes

**Qué se ve.** «…mientras mantengas tu **suscripcion** activa» (Fundador) y «apareces en las **busquedas**»
(Mensual). También «5990 pesos» y «7990 pesos» sin punto de miles, al lado de «$5.990».

**Dónde.** No están en el frontend: son la columna `descripcion` de `planes`, sembrada por migración
(`ZassDbContextModelSnapshot.cs:6667` y `6680`). Corregirlas necesita una migración nueva.

Capturas: `DEFECTO-sin-sesion-7.png`.

### 10 · Crear la cuenta desde `/planes` no vuelve a `/planes`

**Qué se ve.** «Crear mi cuenta» lleva a `/crear-cuenta?volver=/planes`. Después de crearla, la pantalla
«Revisa tu correo» no tiene ningún enlace a `/planes`, y al entrar, el buscador cae en `/`. La
especificación (§8) dice que «vuelve a `/planes` después».

**Dónde.** `CrearCuenta.tsx` no lee `volver`; `Entrar.tsx:39` decide el destino con `destinoDe(state)`, que
no viene de la dirección. No es zona de pagos: es identidad.

Capturas: `DEFECTO-sin-ficha-1.png`, `DEFECTO-sin-ficha-2.png`.

---

## Fuera del alcance, para Pablo

**El registro revela qué correos tienen cuenta.** Al crear una cuenta con un correo que ya existe, la
pantalla dice «Ya existe una cuenta con ese correo» (`ServicioRegistro.cs:73`, responde 409). Cualquiera
puede averiguar si una persona usa ZASS!. El propio código del reenvío de verificación evita decir eso a
propósito (`ServicioCuenta.cs:41-45`). Es identidad y seguridad (N3): se informa y no se toca.

---

## Lo que NO se recorrió

- **El 409 real de API-69 con los 50 cupos tomados.** `20-ocupar.sql` abortó sin escribir: hay 27 perfiles
  sin cupo para 50 cupos, y el índice `ux_cupo_fundador_perfil` permite uno por perfil. La pantalla de
  agotado se vio interceptando `GET /api/planes` (fase `agotado-pantalla`), sin tocar la base.
- **«Esta cuenta ya usó un código de descuento».** Solo se llega pagando un mes con el cupón, y ninguna
  pantalla lo permite (defecto 7).
- **Una pasarela real.** No existe en el código; todo fue con la simulada.

## Diferencias entre documentos y código (regla 9 de `CLAUDE.md`)

- La numeración de los endpoints no coincide: la especificación usa API-66 a API-68, el plan de
  `docs/superpowers/plans/2026-11-13-cupo-fundador.md` usa API-66 a API-69, y el código usa API-66 a API-70
  (`EndpointsSuscripciones.cs`).
- La especificación pide que la pasarela simulada **firme** el aviso con HMAC
  (`Zass:Pagos:ClaveDeFirmaSimulada`); `PasarelaSimulada.LeerConfirmacionAsync` solo revisa la forma del aviso.
- No existe `docs/01-casos-de-uso/subsistema-7-suscripciones-y-pagos.md`, que la especificación (§9) da como
  parte del entregable.

---

## La base, antes y después

Todo lo que el recorrido cambió en la base se deshizo con los bloques de reinicio.

| | `00-inicio` (después del reinicio) | `99-estado-final` |
|---|---|---|
| Cupos | libres 50 · reservados 0 · vencidas 0 · confirmados 0 · marca 0 · total 50 | **igual** |
| Marcela | prueba · Prueba · 10 días · sin cupo · 0 intentos | **igual** |
| Cristóbal | plan 1 · prueba · Prueba · `2026-12-18T02:58:49.349460Z` · referencia nula · sin cupo · 0 intentos (según `40-tardia-antes`) | **igual** |

Queda en la base la cuenta inventada `buscador-jq7@example.test` (sin verificar, sin ficha), creada por la
fase `sin-ficha`. Ningún bloque la borra; se reutiliza en la próxima corrida.

## Cómo se repite

Ver la cabecera de `camino-del-pago.mjs` y de cada bloque en `datos/camino-del-pago/`. Orden:
`00-inicio` → `sin-sesion` → `sin-ficha` → `cupon` → `rechazo` → `vencida` (pausa `30-vencida`) →
`vencida-seguir` (pausa `10-reinicio-marcela`) → `agotado` (pausa `20-ocupar`; si aborta,
`agotado-pantalla`) → `tomar` → `retomar` (pausa `10-reinicio-marcela`) → `tardia` (pausa
`40-tardia-antes`) → `tardia-reservar` (pausa `30-vencida`) → `tardia-seguir` (pausas
`50-tardia-revisar` y `51-reinicio-tardia`) → `99-estado-final`.
