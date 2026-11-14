# ZASS! — estado del desarrollo

**Última sesión:** 14 de noviembre de 2026 (mañana) — ver **«Sesión del 14-nov, mañana»** justo debajo.

**Sesión anterior:** 14 de noviembre de 2026 (madrugada), cuando se cerró C1.

**Antes de eso:** 13 de noviembre de 2026 (ver «Sesión del 13-nov» más abajo).

**Sesión anterior a esa:** 12 de noviembre de 2026.

**Sesión anterior a esa:** 9 de noviembre de 2026 (se rescataron las dos ramas de agentes que habían quedado sin
verificar —`verificacion` y `fondo`—, se construyó el planificador A-09 y **MOD-06 Agenda** entero en el
backend, sin la pantalla). **Lo que sigue pendiente está en «Sesión del 9-nov» más abajo.**

**Sesiones anteriores:** 8-nov noche (tres fusiones, anclajes, segunda tanda de agentes que se cayó) ·
8-nov mañana (Chile entero por comunas, pruebas de arquitectura) · 7-nov tarde (MOD-03, MOD-04, MOD-05,
repositorio en GitHub).

Este archivo existe para retomar sin reconstruir contexto. Dice **qué está hecho y
comprobado**, **qué no**, y **el siguiente paso exacto**.

---

## Sesión del 14-nov-2026 (mañana) — dónde retomar

### C3 está CERRADO: el cobro del día 31 existe y lo dispara el planificador

Fusionado de `c3/cobro-del-dia-31` a **`cupo-fundador`**. Era lo último que faltaba del camino
del dinero: C1 dejaba inscribir el medio de pago y **nadie cobraba nunca**.

Lo dispara **A-09 Planificador**, en la misma tarea diaria que vence suscripciones y avisa, y
**antes** que ellas: a quien se le acaba de cobrar bien no hay que mandarle un «te queda poco»,
y el aviso se calcula sobre la fecha que el cobro acaba de mover.

| Decisión | Por qué así |
|---|---|
| Cobra **7 días antes** del vencimiento | No es un calendario inventado: es el mayor umbral de aviso que ya existía, leído del mismo sitio. Un rechazo tiene margen en vez de ser «desapareces hoy» (RN-S7-11) |
| Clave determinista `cobro-automatico:<suscripción>:<vigente-hasta>` | Con la fecha de hoy, dos corridas dentro de la ventana de 7 días cobrarían **dos veces el mismo mes** (RN-S7-12) |
| Un rechazo **no se reintenta** solo | La política de reintento es decisión de Pablo y no existe (RN-S7-13) |
| Sin medio inscrito **no se cobra y no se supone nada** | Se cuenta y se audita: un agujero que se ve, no uno que se tapa (RN-S7-14) |

13 pruebas y 15 mutaciones. Dos mutantes no cayeron y el agente **los reemplazó en vez de
dejarlos pasar**, porque eran equivalentes.

### El hueco de C1 que encontró C3, y que importaba de verdad

La inscripción del medio de pago guardaba su referencia **en el intento y nunca en la
suscripción**, que es la columna que el cobro recurrente lee. El 23 de diciembre el cobro habría
recorrido las 50 suscripciones y **no habría cobrado a nadie**: la inscripción servía para tomar
el cupo y para nada más. Ninguna prueba de C1 lo veía porque todas miraban el intento.

Arreglado en `ServicioSuscripciones.PasarAFundadorConMesGratisAsync`, con el límite escrito al
lado: **la pasarela simulada devuelve la referencia de la TRANSACCIÓN, y una real devuelve la del
MEDIO inscrito** (el `tbk_user` de Oneclick). Eso lo tendrá que devolver el adaptador real, y
está dicho en el documento de S7.

Dos pruebas nuevas, la segunda vigilando el lado que se olvida: **una inscripción rechazada no
deja medio guardado**, porque si lo dejara el cobro intentaría cobrarle a quien nunca inscribió
nada.

### La auditoría del cobro nombraba una tabla que no existe

Cuatro entradas de `ServicioCobros` decían que la entidad afectada era `intentos_pago`, y la
tabla es **`intentos_de_pago`**. Es el **mismo error que el bloqueo de fila** de la madrugada,
en otro sitio: el SQL crudo reventaba con `42P01`, pero una etiqueta de auditoría no revienta
nunca. Se habría notado el día que alguien tuviera que explicar un cobro: la consulta por tabla
no devuelve ni una fila, y la conclusión natural es «no hay rastro de ese cobro».

`AuditoriaDeCobrosPruebas`, 3 pruebas. Es el primer archivo del cobro que le pasa a
`ServicioCobros` el **mismo** auditor que luego mira; los demás le pasan uno de usar y tirar, y
por eso ninguno podía ver esto. La tercera prueba comprueba que **el identificador es de la
tabla que dice ser**, y se ganó su sitio con una mutación: etiquetar `cobro_confirmado` como
`suscripciones` —que SÍ es una tabla— con el id del intento engaña a las dos primeras y solo
cae esa.

### La documentación, con dos inconsistencias mías cerradas

- **`clases-05-suscripciones-y-pagos.md`** existe: **CL-26 a CL-32**, las siete clases de S7, que
  eran el único subsistema sin diagrama de clases. La matriz decía **4 clases y un total de 29**
  cuando en el código son **siete**: corregido a 7 y 32, y su §3.1 pasa a llamarse «las 25 clases
  de S1 a S6» porque las de S7 no tienen caso de uso que las pida —eso es el hallazgo de su §3.5,
  no un hueco—.
- **S7 pasa de tres operaciones a cuatro** (cotizar, iniciar, **cobrar lo que vence**, confirmar),
  con una §2.1 nueva y **RN-S7-11 a RN-S7-14**. Su §9 decía que «el cobro del día 31 no existe»,
  que desde hoy es falso: ahora lo que queda abierto ahí es la pasarela real, la política de
  rechazo y `renovacion_automatica`.
- **A-09 en `beneficiarios.md`**: es el único actor que mueve dinero sin que haya nadie delante, y
  eso no estaba dicho.
- Comprobado con el guion nuevo **`mirar-diagramas.mjs`** en un navegador de verdad: 10 páginas,
  **42 diagramas, 0 con problemas**; `clases.html` pasa de 5 a 7 diagramas y el de S7 renderiza
  con su nodo nuevo. Ese guion existe porque **un error de sintaxis de Mermaid también dibuja un
  SVG**, así que buscar SVG no comprueba nada.

### Dos decisiones que te esperan, y bloquean el cobro de verdad

1. **La política de rechazo.** Cuando la tarjeta se rechaza el 16 de diciembre: ¿se reintenta
   solo —cuántas veces, cada cuántos días— o queda en manos de la persona? Y los «3 días de
   gracia»: ¿son visibilidad extra **después** del vencimiento, o margen **antes** de darlo por
   perdido? ¿Correo propio de «no pudimos cobrarte»?
2. **`RenovacionAutomatica` es `false` para todo el mundo** y el panel lo muestra. El cobro **no
   la consulta**, porque si la consultara no cobraría a nadie. ¿Se pone en `true` al inscribir y
   el cobro la respeta, o se saca de la pantalla? Tenerla visible y sin efecto es lo peor de las
   tres opciones.

### Los números, con el CI como autoridad

Carrera **43585114349** en verde sobre `cupo-fundador`: **824** de aplicación, **17** de
arquitectura y **723** de integración, cero fallos. Con las 3 de la auditoría, la suite de
aplicación va en **827** en local, y la carrera **43595018358** quedó en curso al escribir esto.

### Una trampa del entorno que costó tres rojos falsos

El bloqueo de Windows impide copiar el DLL de producción recién compilado, así que la suite corría
contra **el binario viejo**: tres pruebas «fallaban» por código que ya estaba arreglado. La pista
fue que una prueba **pasaba al añadirle una línea de diagnóstico**. Desde ahora,
`dotnet build --no-incremental` antes de creerle a un rojo local.

### El siguiente paso exacto

1. **Pablo:** lo de la madrugada sigue en pie (mergear **#11** y **#12**, aplicar la migración del
   cobro, **migrar Neon**, encender `Zass:Verificacion:PermitirSinDocumentos` en Render, desplegar
   `web/mantenimiento`), **más las dos decisiones de arriba**, que son las que separan a esto de
   cobrar de verdad.
2. **Claude:** la pasarela real es lo único que falta del camino del dinero, y necesita que Pablo
   elija proveedor (Transbank, Flow o Mercado Pago) y abra la cuenta. Mientras no esté, el cobro
   automático corre entero contra la simulada y **no entra un peso**.
3. **Joaquín:** sus doce tandas en `PLAN-JOAQUIN.md`.

## Sesión del 14-nov-2026 (madrugada) — dónde retomar

### C1 está CERRADO: el cupo fundador funciona de punta a punta

Las once tareas del plan `docs/superpowers/plans/2026-11-13-cupo-fundador.md`, en la rama
**`cupo-fundador`**, con el **CI en verde**: **810** pruebas de aplicación, **17** de
arquitectura y **722** de integración, cero fallos. Cada prueba nueva con su mutación.

| Tarea | Qué quedó |
|---|---|
| 1 | El doble en memoria con los 11 miembros nuevos del repositorio |
| 2 | La migración `20261114012145_CobroIntentosYCupones`: tres tablas, `ux_pago_intento`, y el cupón **LANZAMIENTO30 al 30 %**. **SIN APLICAR a ninguna base** |
| 3-5 | 17 pruebas de cotizar, 12 de iniciar, 10 de confirmar |
| 6 | La guarda: la pasarela simulada ya **no arranca en Producción** (su comentario lo prometía y el `switch` no lo cumplía) |
| 7 | La inscripción con cobro **$0** confirma el cupo y arranca el mes gratis (RN-S7-01, 03 y 07) |
| 8 | **API-66 a API-70** y 9 pruebas por HTTP. Son cinco y no cuatro: la inscripción va aparte del cobro |
| 9 | La página **`/planes`** con el contador «quedan N de 50», el cupón y sus cuatro estados, más `PagoSimulado` y `PlanesListo`. 12 pruebas |
| 10 | El documento rector de **S7**, que no existía |
| 11 | Las pruebas contra PostgreSQL y el guion de navegador |

### El defecto de producción que encontró la tarea 11

`RepositorioSuscripciones.cs:146` hacía `SELECT * FROM intentos_pago … FOR UPDATE` y la tabla
se llama **`intentos_de_pago`**: el **bloqueo de fila que impide que dos confirmaciones del
mismo cobro regalen dos meses reventaba con `42P01`** en cuanto se usaba de verdad.

Es SQL crudo —el compilador no lo ve— y ninguna prueba en memoria podía verlo, porque su doble
devuelve el mismo objeto sin tocar la base. **Lo cazó la primera prueba que lo corrió contra
PostgreSQL**, una hora después de escribirla. Es justo para lo que existía la tarea 11.

Y dos fallos de las pruebas nuevas, del mismo tipo y encontrados igual:
`new ContextoDePrueba()` **abre una transacción en su constructor**, así que `EnTransaccionAsync`
no abría la suya y **no commiteaba nada** (las dos confirmaciones se creían primeras); y la
columna de auditoría es `actor_id`, no `usuario_id`. Las pruebas de concurrencia ahora incluyen
una que **mira `pg_indexes`** antes de confiar en que `ux_pago_intento` existe.

### La documentación: documento nuevo, y por fin con historial

- **`subsistema-7-suscripciones-y-pagos.md`** existe, con **RN-S7-01 a RN-S7-10** y las dos
  excepciones de seguridad escritas (API-70 sin CSRF ni sesión; un aviso repetido responde 200).
- **API-66 a API-70** en `api-rest.md`: el contrato pasa de 71 a **76**.
- **TBL-33 a TBL-36** en el diccionario (ni los cupos fundador estaban documentados): de 32 a **36**.
- El actor **A-11 Pasarela de pago** y una **columna S7** en la matriz de actores, que no existía.
- **Ojo:** el documento **no llegaba al HTML** hasta registrarlo en `sitio/construir.py`. El
  script decía «Listo» y la página no tenía el texto. Comprobado ahora buscando seis textos
  **dentro** del HTML generado.
- La carpeta de documentación **ya es un repositorio git** y está en GitHub, privado:
  `https://github.com/77pablo/zass-documentacion`. Antes no tenía historial de ninguna clase.

### Lo demás que se movió

- **El sitio se puede cerrar**: rama `web/mantenimiento`, un `_middleware.js` de Cloudflare Pages
  que responde **503** (no 200, para que Google no indexe «en mantenimiento») con un pase para
  mirar el sitio por dentro. **Encendido por omisión**: para abrirlo hay que poner
  `MANTENIMIENTO=0` en Cloudflare. 10 pruebas, tres mutaciones. **Sin desplegar.**
- **`/planes` ya se puede encontrar**: antes nada la enlazaba. Ahora está en el pie y en
  `/profesionales`, que además **dice el precio por primera vez** en una pantalla pública.
- **`legal.ts`**: llenos el correo de privacidad (Ley 21.719) y la lista de proveedores con su
  país. Siguen pendientes **la razón social con RUT y el plazo de los registros**, y
  `BORRADOR_LEGAL` sigue en `true` hasta que un abogado revise las dos páginas.
- **PR #13 fusionado** (de otra IA): `aggregateRating` en el JSON-LD de la ficha desde 3 reseñas
  (RN-S5-11), con cinco mutaciones comprobadas.
- **El CI tenía un fallo intermitente propio**, no del código: Postgres arranca dos veces
  (initdb levanta un servidor temporal y lo apaga) y el paso del techo de conexiones llegaba en
  el apagón. Arreglado con reintento, en `cupo-fundador` y en `integracion/13-nov`.
- `docs-frontend` y `docs-raiz`, que llevaban dos días **solo en local**, ya están en `origin`.

### Dos cosas del entorno que bloquean, y son de Pablo

1. **Windows bloquea los DLL de prueba de .NET recién compilados** en este PC:
   `FileLoadException 0x800711C7`, «una directiva de Control de aplicaciones bloqueó este
   archivo». Comprobado en las dos direcciones: con el DLL del 12-nov pasan 17/17; con uno
   nuevo, caen antes de ejecutar nada. **Mientras siga así, el CI es la única autoridad sobre el
   backend.** Se arregla excluyendo la carpeta del repositorio en Seguridad de Windows.
2. **Otra IA trabaja en la carpeta `zass-app-1` y cambia de rama.** Por eso Claude se mudó a su
   propio worktree, `zass-wt-c1`.

### El siguiente paso exacto

1. **Pablo:** mergear **#11** (52 commits, CI verde) y **#12** (el plan de Joaquín);
   **aplicar la migración** del cobro y **migrar Neon** (producción sigue con 20 cupos y le
   faltan tres migraciones en orden); **encender `Zass:Verificacion:PermitirSinDocumentos` en
   Render**; **desplegar `web/mantenimiento`** si quiere el sitio cerrado; y decidir si se honra
   el booleano de `MarcarPagado` o se documenta como defensa redundante.
2. **Claude:** C3, el cobro del día 31 —lo está construyendo un agente en
   `c3/cobro-del-dia-31`—. **Fecha límite real: 23 de diciembre de 2026**, cuando se le termina
   el mes gratis a quien tome su cupo el 23 de noviembre. Si no existe para entonces, los
   fundadores desaparecen de la búsqueda sin que nadie les haya cobrado un peso.
3. **Joaquín:** sus doce tandas en `PLAN-JOAQUIN.md`, empezando por las cuatro ramas de rescate
   `web/*`, que ya están en `origin`.

## Sesión del 13-nov-2026 — dónde retomar

### Tarde del 13-nov — lo que pasó DESPUÉS de lo que cuenta el resto de esta sección

Lo de abajo (PR #9, PR #10, los 8 defectos) sigue siendo cierto, pero **ya no es el siguiente
paso**. Esto es lo nuevo:

**1. Todo está en `integracion/13-nov`, y es un solo merge.** Esa rama va **47 commits por
delante de `main`**, está subida y su CI está en verde. Contiene el contenido de los **cuatro**
PR abiertos (#6, #7, #9, #10) y de las **seis** ramas de agentes, comprobado uno por uno con
`git merge-base --is-ancestor`, no por los títulos de los commits. `main` no tiene nada que
ella no tenga. **El siguiente paso es un PR `integracion/13-nov` -> `main`, no cuatro
fusiones en orden.**

**Ojo con un verde que miente hoy mismo:** `gh run list --branch main` muestra un `success`
arriba, pero es el workflow *Copia de la base de datos*. El de *Pruebas* sobre `main` sigue en
**failure** del 12-nov.

**2. RN-S4-11, decisión de Pablo.** Una cita ya confirmada **se puede reprogramar aunque la
suscripción del prestador haya vencido**. Lo ya comprometido se honra: el impago pierde la
búsqueda (RN-S3-09), las horas nuevas y los botones de contacto, pero no lo que otra persona ya
tenía agendado. Mismo criterio que RN-S6-10.

Lo que este archivo tenía como pendiente —«decidir si `ReprogramarAsync` comprueba la
suscripción»— **está cerrado, y no hubo código que escribir**: el comportamiento elegido es el
que ya tenía. Lo que faltaba era que la especificación lo dijera. Escrito en
`subsistema-4-agendamiento-y-notificaciones.md` (la regla) y en
`cu-14-reprogramar-o-cancelar-cita.md` (flujo alternativo **5H**), y el sitio regenerado y
comprobado en el HTML. **No era un defecto del código: era un hueco del documento.**

**3. Tres defectos de interfaz arreglados (commit `021cf74`), los tres detrás de pruebas en
verde.** Salieron de una revisión de código que por error corrió sobre la carpeta vieja
`C:/Users/pdani/zass-app`; se comprobó a mano que existían también aquí antes de tocar nada.

| Defecto | Cómo se confirmó |
|---|---|
| `errorDeFranja` no rechazaba hora vacía: vaciar **solo «Desde»** manda `horaInicio: ""`, el servidor falla al convertir a `TimeOnly` **antes** de validar, y la pantalla solo puede decir «No se pudo completar la operación» sin marcar campo | Ejecutando la función con los tres casos: los otros dos sí daban error, solo ese se escapaba |
| `inicioDeDiaEnChile` se iba al **día anterior** el domingo del cambio de hora de septiembre (la medianoche chilena no existe: el reloj salta de 00:00 a 01:00). `2026-09-06` devolvía las 23:00 del sábado | Recorriendo los **730 días** de 2026 y 2027: antes fallaban los dos domingos de septiembre, ahora **0** |
| `robots.txt` no cubría `/mi-agenda`, `/mis-citas` ni `/mis-resenas`, y **cuatro** pantallas del panel no pedían `noindex` | Leyendo el archivo y las rutas de `App.tsx`. La revisión había visto solo `/mi-agenda`: eran tres rutas y cuatro pantallas |

**Las pruebas se comprobaron en rojo antes del arreglo**: revirtiendo los tres archivos y
dejando las pruebas puestas, **8 caían**. La del cambio de hora recorre los 730 días porque el
defecto existía justo porque las dos pruebas que había miraban un día de verano y uno de
invierno. Y la de `robots.txt` **ya no se escribe a mano**: compara la lista con las rutas que
`App.tsx` declara, así que la próxima pantalla del panel no se puede escapar.

Interfaz: **488/488** (eran 478). `tsc -b` limpio.

**4. Un cuarto hallazgo de esa revisión estaba caducado**, y conviene saberlo: decía que los
bloqueos no se pueden deshacer. Sí se pueden: existen `GET /api/mi/bloqueos`,
`DELETE /api/mi/bloqueos/{id}` y el botón «Quitar este bloqueo».

### Lo que de verdad bloquea el lanzamiento del lunes 23

Medido hoy, no recordado. Lo que el mensaje de un colaborador daba por pendiente —los PR, los
seis textos de J3/J4, el BOM de `Program.cs`— **ya estaba hecho**. Lo que sigue abierto:

| # | Qué falta | De quién depende |
|---|---|---|
| 1 | Fusionar `integracion/13-nov` -> `main`. Llave de todo: la regla de **una migración de EF a la vez** convierte este merge pendiente en un bloqueo de desarrollo | Nosotros |
| 2 | **El cupo fundador no está enchufado a nada.** La lógica existe y su concurrencia está probada con 60 solicitantes a la vez, pero `grep "Fundador" backend/src/Zass.Api` está **vacío**, no hay `EndpointsSuscripciones.cs` y el frontend no menciona «fundador» ni «cupo». **Nadie puede tomar un cupo.** No es «falta cerrar al 50»: falta el camino entero | Joaquín |
| 3 | **Mes gratis**: no existe, y los dos planes de pago tienen `dias_prueba = 0` **explícito**. El plan `prueba` de 30 días no es lo mismo | Joaquín |
| 4 | **Cupón del 20 %**: cero menciones en todo el código. Necesita migración, así que espera al punto 1 | Joaquín |
| 5 | **Cobro**: no hay puerto de pasarela, y `RegistrarPeriodoPagadoAsync` **solo lo llaman pruebas**. La forma de cobro sigue sin decidir, pero no tiene que frenar: la base ya tiene `Suscripcion.ReferenciaPasarela` y la tabla `pagos` | Joaquín, detrás de un puerto |
| 6 | **Avisos de cita por correo: no construidos.** El enum declara `CitaSolicitada/Confirmada/Rechazada/Cancelada/Reprogramada`, pero la agenda **nunca encola nada** y `Plantillas.TienePlantilla` no incluye ningún tipo `Cita*` | Nosotros, tras el punto 1 |
| 7 | **Migrar Neon** (producción sigue con **20** cupos, no 50) — y son **dos** migraciones: `CuposFundadorA50` y `NoAsistioQuienFalto` | Solo Pablo |
| 8 | **Manual Deploy en Render** (`autoDeploy: false`) y **rotar los 3 secretos** | Solo Pablo |

**Tres cosas que nadie tenía en la lista y pesan:**

1. **El correo de reseñas falla en el modo peligroso.** En `main` la reseña **sí** se encola,
   pero `Plantillas.cs` de `main` no reconoce el tipo, y el despacho deriva de
   `TienePlantilla` qué filas consulta: esas notificaciones quedan **`Programada` para
   siempre, con 0 intentos y sin error**. Quien mire la cola verá avisos «en camino» que nunca
   van a salir. El arreglo (`75612f2`) está solo en `integracion/13-nov`.
2. **Render está en `plan: free`** y el envío de correo y el `Planificador` son
   `BackgroundService` **dentro del proceso de la API**. Dormido por inactividad no vacía la
   cola. Riesgo de arquitectura para el lanzamiento.
3. **El cobro es la única parte del producto sin documento rector:** `01-casos-de-uso/` llega
   hasta el subsistema 6 y **no hay subsistema S7**; `api-rest.md` no numera ningún endpoint de
   suscripción.


### Lo primero: `main` ya tiene arreglo, y la causa no era lo que parecía

**El rojo de `main` era UNA prueba y no era un defecto del producto.** Con `gh` autenticado se pudo
leer por fin el registro de Actions: fallaba
`CuposFundadorIntegracionPruebas.Sesenta_pidiendo_a_la_vez_se_reparten_exactamente_cincuenta_cupos_sin_repetir`,
y su propio mensaje decía la causa — «Hacen falta 61 conexiones simultaneas y al servidor le quedan
30». Es el **techo de conexiones de Postgres en CI**, no el reparto de cupos. La rama de 20 cupos
estaba verde porque pedía menos conexiones.

Arreglado en **PR #9** (`ci-techo-conexiones`): Postgres se levanta con `docker run … -c
max_connections=300` porque `services:` no admite argumentos para el servidor, y un paso aparte
**mira** `SHOW max_connections` en vez de confiar en que el `docker run` no dio error. **CI de esa
rama en verde, con los números: 674 + 16 + 614, ninguna omitida.**

### Los 8 defectos del repaso en navegador: cerrados (PR #10)

`agente/navegador`, cuatro commits. D1 ya estaba arreglado de antes.

| # | Qué era | Qué se hizo |
|---|---|---|
| D2 | `/mi-ficha` mandaba a «crear una cuenta de profesional con otro correo», contra la cuenta única | Ahora invita a activar la ficha con la cuenta que ya tiene |
| D3 | Entrar bien y que falle `GET /api/mi/cuenta` dejaba la pantalla **muda** | Lo dice y ofrece reintentar (3 unitarias nuevas) |
| D4 | **El control de radio no existía** (ver más abajo: el documento lo daba por arreglado) | Reconstruido de punta a punta, incluido el parámetro en API-63 |
| D5 | El pie ofrecía «Entrar» a quien ya había entrado | El pie sigue la sesión |
| D6 | Tres objetivos táctiles bajo 44 px | Uno era una acción y se arregló; **los otros dos son enlaces dentro de una frase, que WCAG 2.5.8 exceptúa**, y el medidor aprendió la excepción |
| D7 | El 429 respondía `titulo`/`detalle` y el cliente lee `title`/`detail`: el aviso del limitador **no llegaba nunca** | Arreglado, y apareció la otra mitad: `WriteAsJsonAsync` pisaba el `Content-Type` |
| D8 | Dos guiones «muertos» | `flujo-real.mjs` no lo estaba: era **un selector**. `mirar-busqueda.mjs` sí: borrado, llevándose antes sus dos comprobaciones únicas |
| D9 | Los datos de demostración no permitían entrar | Contraseña real (`demo-zass-2026`) y sus dos roles, en los dos sembrados |

**Medido en esta máquina, no por informe ajeno:** interfaz **385/385**, aplicación **679/679**,
arquitectura **16/16**, integración **616/616**, y **los 7 guiones de navegador en verde** más el
recorrido completo del prestador (**14/14**).

### Tres cosas que este archivo daba por ciertas y eran falsas

1. **El defecto 15** (más abajo, «Arreglado… Visto en el navegador real»: «¿Hasta qué distancia?»,
   5/10/20/50 km) **no existía en el código**. Se perdió al pasar al sitio de varias páginas y nadie
   lo notó porque el soporte seguía vivo en la capa de servicio y sus pruebas seguían verdes:
   prueban la función, no la pantalla. Y faltaba más de lo que parecía — **API-63, la barra que usa
   el sitio hoy, ni aceptaba el parámetro `radio`**; el radio solo existía en API-01.
2. **`flujo-real.mjs` figuraba como prueba** de que el registro funciona de punta a punta, y estaba
   parado desde que la cuenta única quitó la casilla de rol. Hoy sí corre (14/14).
3. **`mirar-busqueda.mjs` figuraba como «cómo repetirlo»** y medía una pantalla que ya no existe.

### Lo que el arnés se hacía a sí mismo

- **`mirar-agendar.mjs` deshacía el arreglo de D9**: le prestaba a la prestadora de demostración un
  hash y el rol, y al limpiar le devolvía el `'x'`. Correr ese guion revertía el arreglo en
  silencio. Ya no toca la base: entra con la contraseña sembrada.
- **Dos rojos permanentes** en `mirar-publicas.mjs` eran el 404 correcto de una ficha inexistente.
- Los barridos en cadena agotaban el limitador (100 peticiones públicas cada 15 min) y los guiones
  informaban «defectos de pantalla» que eran 429. En **Development** los topes P y G subieron; los
  de producción no se tocan.

### El radio, como quedó (RN-S3-04)

- Sin radio, la barra busca en **todo Chile**. API-01 sigue aplicando 10 km por omisión: son dos
  búsquedas distintas, y recortar la barra a 10 km sin pedirlo escondería gente sin decirlo.
- Fuera de rango se **corrige y se avisa** (E-400 de CU-08). Sin ubicación se ignora con aviso.
- El radio **sí** va en la dirección (se comparte); la ubicación **no**, como ya era.
- Quitar la ubicación quita el radio.
- Contra la base de desarrollo: sin radio 21 personas, a 5 km 16, a 1 km 5, a 500 km se corrige a 50.

### Y los otros dos agujeros del cruce de ramas, cerrados también

**La numeración de endpoints.** `api-rest.md` decía «63 endpoints» y el código tenía **ocho que no
estaban en ninguna tabla**: API-20b, API-21b, API-22b, API-34-listar, API-34-quitar, API-42b, API-64
y API-65. Los dos de bloqueos (`API-34-listar` y `API-34-quitar`) aparecieron justo al hacer el
inventario; nadie los echaba de menos. El documento queda con una **sección 11** que los recoge, el
total pasa de 63 a **71** y se dice cuántos están construidos de verdad (55, contados contra el
código, no de memoria).

Y para que no vuelva a pasar, dos pruebas sobre el mapa de rutas real: una caza **nombres
repetidos** (dos `WithName` iguales devuelven **500 en todas las rutas** desde la primera petición,
y pasó el 12-nov) y otra es un **inventario escrito a mano**: crear un endpoint deja la prueba roja
hasta que se añade a la lista, y el mensaje pide añadirlo también al documento. Comprobado que
muerde, quitando un nombre a propósito.

**El correo de «tu ficha ya está publicada».** Se encolaba desde el primer día y **no tenía
plantilla**, así que se quedaba en la cola para siempre. Ya la tiene, y enlaza su ficha pública (la
que va a querer compartir), no el panel.

Al escribirla apareció lo de verdad importante: **al darle plantilla a un tipo, todo lo acumulado se
vuelve enviable de golpe**. Sin más, al desplegar habrían salido de una vez los avisos de fichas
publicadas hace semanas. Ahora el procesador **anula** lo que lleva más de 7 días esperando
(`OpcionesCorreo.CaducaDias`), y eso obligó a entender una trampa del propio esquema:
`programada_para` **deja de decir cuándo se encoló en cuanto el aviso se reserva** — pasa a valer la
hora de la reserva. Por eso la regla mira `intentos = 0`, que es el caso en que sigue siendo la
original. Añadir una columna de «creado en» habría sido más limpio, pero pide migración y la regla
del proyecto es **una migración de EF a la vez** (la de NoAsistio va primero).

**Un hueco que esto destapa y NO está arreglado:** no hay forma de **pedir otra vez el correo de
verificación**. Se encola solo al registrarse. Si a alguien no le llega, hoy no tiene salida.

### Las ramas, al cerrar la sesión

**Todas subidas** (`gh auth login` resuelto: el gestor de credenciales de Windows se cuelga esperando
una ventana invisible; hay que autenticar desde una terminal de verdad, y después `gh auth
setup-git`).

| Rama | Qué | Estado |
|---|---|---|
| `ci-techo-conexiones` | El arreglo del rojo de `main` | **PR #9**, CI verde |
| `agente/navegador` | Los 8 defectos | **PR #10** |
| `integracion/12-nov` | Las 5 ramas del 12-nov fusionadas | Subida, CI heredaba el rojo de `main` |
| `textos-j3`, `textos-j4` | Los seis textos visibles | Subidas, CI verde |
| `agente/no-asistio-n3`, `agente/resenas-pantalla`, `agente/correo-resenas`, `agente/mis-citas`, `agente/rn-s6-10` | Trabajo de agentes del 12-nov | Subidas, sin fusionar |
| `diag-ci-rojo` | Rama desechable para leer el CI | **Ya no hace falta**: el registro se leyó directo |

### El siguiente paso exacto

1. **Fusionar PR #9** (`main` vuelve a verde) y después **PR #10**.
2. Decidir el orden de fusión del resto de ramas, y qué hacer con `ReprogramarAsync`, que **no
   comprueba la suscripción** (decisión de producto: añadir la guarda deja a quien ya tiene hora sin
   poder moverla, solo cancelar).
3. **Migrar Neon**: producción sigue con 20 cupos.
4. Aprobar los textos de los dos correos de reseñas y los de «mis reseñas».

---
## Sesión del 12-nov-2026 — dónde retomar

### Lo que quedó HECHO y COMPROBADO

| Rama | Qué | Estado |
|---|---|---|
| `cupos-fundador-50` | Cupos fundador de 20 a 50 | **FUSIONADA EN `main` Y SUBIDA** (`a567914`). **Neon NO fue migrada**: ese paso lo hace Pablo desde Render |
| `agente/mis-citas` | Reprogramar (API-41) + agrupar por día chileno + `CitaDto` trae el `slug` | Commiteada (`4c014fb`, `a1a248d`), **sin subir** |

**Pruebas corridas por mí, no por informe ajeno:** 674 aplicación + 16 arquitectura + 614
integración + 392 interfaz, todo en verde.

**La prueba de concurrencia de cupos no tenía ningún defecto.** Fallaba con
`53300: sorry, too many clients already` porque el PostgreSQL local admitía **50** conexiones
(`datos-pg/postgresql.conf` lo fijaba al final del archivo, pisando el 100 de más arriba) y la
prueba de 60 solicitantes necesita 61. Subido a **200**; copia en `postgresql.conf.antes-de-200`.
Esa carpeta está en `.gitignore`: no entra a ningún commit. CI no necesita cambios (97 utilizables,
pico medido ~61). El arnés ahora comprueba la holgura ANTES de empezar y falla diciendo que no es
un defecto del reparto.

`/mis-citas` **ya existía** en `main` desde `b678b42` con cancelar, confirmar, rechazar y
asistencia; este archivo decía que no existía. Reparto aclarado: `/mi-agenda` = reglas de atención
(qué horas ofrezco, qué días bloqueo); `/mis-citas` = ciclo de una cita, de los dos lados.

**CU-14 precondición P2 corregida en la documentación** (y sitio regenerado): cancelar acepta
`confirmada` o `solicitada`; **reprogramar solo `confirmada`**, que es lo que hace el código
(`ServicioAgenda.cs:575`) y lo que dice `estados-cita.md`.

### Las dos ramas de agentes que se cayeron por el límite de 5 horas

Volvió a pasar lo del 8-nov: **el límite de la ventana, no la capacidad**. Esta vez el trabajo SÍ
quedó guardado y revisado.

| Rama | Qué dejó | Qué falta |
|---|---|---|
| `agente/revision-joaquin` (`4c51433`) | **RN-S6-10 implementada**: la ficha se ve entera pero sin contactar ni agendar; el contacto se vacía **en el servidor**; `BloquesLibresAsync` ya mira la suscripción; campo `SuscripcionVigente` en el DTO y en el frontend | **Dejó TRES mutantes vivos** (`var vigente = true`, calendario sin guarda, `const alDia = true`). Revertidos y reconstruidos; el árbol se comprobó después: 682 aplicación + 378 interfaz, `tsc -b` limpio. **Pero NO corrió sus mutaciones**, así que no está demostrado que sus guardas caigan; integración sin correr; y **nunca llegó el informe de la revisión de J3 y J4**, que era su primera tarea. **NO FUSIONAR así** |
| `agente/resenas-pantalla` (`f7c435e`) | Cliente de API ampliado y las carpetas `funciones/resenas` y `componentes/resenas` | Sin compilar, sin pruebas, sin informe. Sin mutantes |

### Revisión de J3 y J4 de Joaquín — HECHA (12-nov, tarde)

| Rama | Veredicto | Qué falta |
|---|---|---|
| J3 `revision-visual-agenda-ficha` (`ee96db1`) | **FUSIONABLE** | Que Pablo apruebe 3 textos visibles (regla 12 de `CONTRIBUIR.md`) |
| J4 `tarjeta-abre-ficha` (`a45934b`) | **FUSIONABLE CON CAMBIOS** | Restaurar el **BOM UTF-8** de `Program.cs`, que J4 le quitó sin querer (comprobado: `main` empieza con `ef bb bf`, J4 no) + 3 textos |

Números medidos aquí, no los del autor: J4 da 701 aplicación + 17 arquitectura + 628 integración +
391 interfaz; J3 da 374 de interfaz. Las mutaciones de `origin/mut-tarjeta-endpoint` caen las 5
prometidas; las de `origin/mut-j3-agenda` caen **3 de 5**.

**Dos hallazgos que valen más que el veredicto:**
1. **El aviso de J2 se repite en J4 y es estructural.** Las 4 mutaciones del endpoint de tarjetas
   dejan las **701 pruebas de aplicación en verde**: no existe ninguna unitaria que toque
   `EndpointsTarjetas`. Solo la integración las protege.
2. **En J3, las 2 mutaciones de CSS no ponen nada en rojo.** jsdom no calcula estilos, así que el
   objetivo táctil de 44 px y el `text-indent` no están protegidos por ninguna prueba: ese veredicto
   visual depende de que alguien lo mire en un navegador.

Sin comprobar en J4: que Cloudflare Pages **no** enrute `/T/…` a la Function (todo el respaldo en
mayúsculas depende de eso, y solo se ve desplegando); y que `X-Forwarded-For` llegue bien a la tasa P
— API-54 hereda el defecto de IP conocido, así que si la cadena trae más de 3 saltos **todos los
escaneos de tarjeta contarían como una sola IP**.

### RN-S6-10 — hecha, en `agente/rn-s6-10` (sin fusionar)

`92aaf00` la regla; `494be16` el `noindex`. La ficha del impago se ve entera, pero el contacto **se
vacía en el servidor** (esconder botones dejaría el teléfono viajando en el JSON), el calendario
responde **409 y no 404**, y la página sale con `noindex` y **sin JSON-LD**. Pablo decidió el
12-nov que la ficha impaga **no sea indexable**. Acumulado: 682 + 16 + 617 + 382 en verde.

La bandera se lee como `suscripcionVigente !== false` a propósito: si la API dejara de mandar el
campo, se indexa como siempre, en vez de sacar del índice **todas** las fichas de golpe. Pega menor
conocida: el borde cachea 5 min, así que quien vuelva a pagar puede seguir con `noindex` un rato.

### NoAsistio (N3) — estuvo BLOQUEADA, ya no

`CONTRIBUIR.md:135-137` prohíbe empezar una migración antes de que la anterior esté en `main`. Por eso
no se empezó hasta fusionar los cupos. **Ya está fusionada**, así que C puede partir de `main`
actualizado. Verificado para quien la retome: los **cuatro sitios de elegibilidad dan tres respuestas
distintas** ante `NoAsistio`, y `HuboCitaRealizadaAsync` **sí tiene llamador real**
(`ServicioAgenda.cs:863`), así que no es código muerto. Decidido: borrar los dos `HabilitaResena()`
y dejar viva solo la respuesta de `ReglasDeResenas`.

### Textos visibles pendientes de Pablo

Bloquean J3 y J4. J3: «Quitar horario», «Quitar bloqueo», «Bloqueaste el 01-01-2027: nadie podrá
agendar ese día.» · J4: «Esta tarjeta no es válida.», «Este perfil no está disponible por ahora.»,
«Esta tarjeta todavía no está activa.» Pablo dijo el 12-nov que **los reescribe él**.

### El siguiente paso exacto

1. **Terminar la revisión de J3 y J4 de Joaquín**, que nadie hizo: `origin/revision-visual-agenda-ficha`
   y `origin/tarjeta-abre-ficha` siguen sin fusionar. **J1 ya está en `main`**. Las tres ramas
   `origin/mut-*` son mutaciones desechables que sirven de evidencia: comprobar que de verdad ponen rojo.
2. **Cerrar `agente/revision-joaquin`**: correr integración y las mutaciones que no alcanzó.
3. Abrir el PR de `cupos-fundador-50` y fusionar `agente/mis-citas`.
4. **Nadie ha visto ninguna de estas pantallas en un navegador de verdad.** Todo es `fetch` simulado
   en jsdom. `/mis-citas`, `AgendarHora` y la ficha sin suscripción están sin mirar.

### Decisiones de lanzamiento tomadas por Pablo el 12-nov

Primer mes gratis y después **$5.990/mes congelado para siempre**; mensual normal $7.990. La
**tarjeta NFC va aparte, a precio normal**. Inscripción por `zass.cl/registro` **+ un Google Forms
opcional** cuyo gancho es un **cupón de descuento** para el primer mes pagado. **Sin filtro de zona.**
Al llegar al cupo 50, **se cierra y punto** (sin lista de espera).

Eso deja por construir, y **nada de esto existe**: el mes gratis, el campo de cupón/código de
descuento, y cerrar el registro fundador al llegar a 50. Sigue sin decidir **de cuánto es el cupón**
y **la forma de cobro**.

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
están construidos, y a eso se sumó lo de tres ramas de agentes ya fusionadas: la API lista para
producción, el envío real de correos y el SEO.** En total **836 pruebas de backend en verde**
(381 de aplicación sin base de datos, 15 de arquitectura, 440 de integración contra PostgreSQL
real y la API por HTTPS) más **283 de interfaz**, y dos comprobaciones de navegador que crean una
cuenta y entran contra la base de verdad.

La cifra de la que se partió esta sesión era 706. Las tres fusiones la llevaron a 836, y **ninguna
entró sin revisarla a mano**: las tres necesitaron arreglo, y dos tenían defectos que no existían
en ninguna de las dos ramas por separado y nacieron al juntarlas (ver «Las tres fusiones»).

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
| `DescripcionMaximaA1500` | La descripción del perfil baja de 2000 a **1500** caracteres (decisión de Pablo) | `information_schema` dice 1500, y la **guardia** de la migración se probó con un perfil de 1600: falla con "1 perfil(es) tienen una más larga" en vez de un error de truncamiento sin contexto |

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

### Chile entero: la comuna como unidad (8-nov-2026)

Pablo quiere salir **a nivel nacional y gratis**. Hasta hoy todo estaba armado alrededor de los 16 barrios de Temuco.

**Hecho y verificado:**
- **346 comunas en 16 regiones**, bajadas de OpenStreetMap (Overpass) con su **capital comunal** (`admin_centre`) como punto;
  cada región se validó contra la cifra oficial (RM 52, Biobío 33, Los Ríos 12…). Una sola comuna se usa con el centro de su
  caja (Rengo) y **Antártica no existe como relación en OSM**: se usa el nodo de Villa Las Estrellas (su capital legal),
  dicho en el archivo. Generado por `generar_zonas.py` → `Zass.Infraestructura/Datos/ZonasDeChile.cs` (no se edita a mano).
- **Modelo:** `Zona` ahora tiene `Tipo` (Comuna|Barrio), `Region` y `PadreId`. Los 16 barrios quedaron como barrios de la comuna de
  Temuco (id 377). La base lo exige (`ck_zona_tipo`, `ck_zona_padre`). Ids 101–446, fijos una vez migrados.
- **Migración `ComunasDeChile`** (+ dos funciones SQL: `zonas_relacionadas` y `etiqueta_de_zona`). Ya aplicada en la base de desarrollo.
- **La relación comuna–barrio manda en la búsqueda:** quien cubre «Temuco» aparece al buscar en «Centro, Temuco» y al revés; los
  barrios hermanos y las comunas vecinas no se mezclan. El conteo de la portada usa la misma relación y **una prueba exige que
  coincida con lo que la búsqueda devuelve en cada una de las 362 zonas**.
- Un barrio se nombra con su comuna («Centro, Temuco»); sin ubicación de la persona **no se mide desde ningún centro** (antes, la Plaza de Temuco).
- `ResolverZona` (un punto → zona) ahora devuelve la comuna más cercana y, si tiene barrios a menos de 8 km, el barrio.
- **Pantallas:** mapa de Chile hecho de puntos en la portada (reemplaza el radar), página **Comunas** por región con filtro (`/barrios`
  redirige), editor de cobertura de «Mi ficha» (se filtra escribiendo, máximo 10, la primera elegida queda de principal) y selector
  de zona de la búsqueda por región.
- **Se midió:** 372 pruebas de aplicación, 310 de integración y 174 de interfaz en verde; 10 mutantes de la relación y los
  conteos (todos muertos); recorrido real en navegador completo (crear cuenta → armar ficha con una comuna → publicar → aparece en la búsqueda → salir).

**Pendiente de pulir:** el «barrido» del mapa de Chile se ve como una franja rectangular; `ResolverZona` es por capital más cercana
(en una metrópoli de comunas pegadas un punto en el límite puede caer en la vecina: sirve para ubicar, no para fijar límites);
solo existen sinónimos de búsqueda para los 5 rubros actuales; límite de 10 comunas por prestador (decisión mía).

### Pruebas de arquitectura (8-nov-2026)

Proyecto nuevo `Zass.Arquitectura.Pruebas` (NetArchTest, 15 pruebas): las capas van de adentro hacia afuera (Dominio no conoce a nadie,
Aplicación no conoce Infraestructura ni Api, Infraestructura no conoce Api), el Dominio no usa marcos ni declara paquetes NuGet, la
Aplicación no usa EF Core/Npgsql/ASP.NET, los **endpoints no tocan la base ni los repositorios**, y **cada módulo solo habla con su propio
repositorio** (los demás lo consumen por su servicio). Más controles de que las reglas pueden fallar. **Se midió:** 5 violaciones reales
inyectadas, 4 cazadas; la que no (un `typeof` suelto) es un límite de la herramienta y está anotada en el archivo.
Atención al fusionar `agente/produccion`: su `/api/salud/lista` consulta la base; si lo hace desde `Zass.Api.Endpoints` esta prueba fallará y la solución es un puerto.

### Las tres fusiones (8-nov-2026, noche) — y lo que enseñaron

`main` ya tiene **produccion, seo y correo**. Ninguna entró por su informe: las tres se revisaron
corriendo sus pruebas aquí, y las tres necesitaron arreglo.

| Rama | Qué aportó | Qué hubo que arreglar al fusionar |
|---|---|---|
| `agente/produccion` | Proxies de confianza para `X-Forwarded-For` (rechaza `/0`, limpia los valores por omisión de ASP.NET, solo `For` y `Proto`, 2 saltos), validación de configuración al arrancar, `/api/salud/lista` con grupo de límite propio, modo `--migrar`, Dockerfile, `render.yaml`, `DESPLIEGUE-API.md`, copia de la base por GitHub Actions | La prueba de arquitectura **cazó su endpoint consultando la base**. Se movió detrás del puerto `ISondaDeBase` (`SondaDeBase` en Infraestructura). Eso puso rojas 17 pruebas de `ContenedorPruebas`: `AgregarZass` declaraba un servicio cuya dependencia (`ILoggerFactory`) no provee. Se añadió `AddLogging()` |
| `agente/seo` | Meta por ficha del lado del servidor (Pages Function), sitemap paginado, `robots.txt`, `useMeta`, imagen para compartir, `/terminos` y `/privacidad` (BORRADOR), API-64 `/api/prestadores/mapa` | Conflicto en `Barrios.tsx`, que main borró al pasar a comunas: su `useMeta` se llevó a `Comunas.tsx`. Y un defecto **que nació en la fusión**: el sitemap publicaba `/barrios`, que ya solo es una redirección a `/comunas` — Google habría quedado apuntado al redirect y la página real nunca se habría listado |
| `agente/correo` | Envío real (Brevo/Resend), cola con `FOR UPDATE SKIP LOCKED` y reserva de 120 s, reintento con espera creciente, purga del token, correo enmascarado en los registros | Había chocado con el **mismo** problema del registro y lo resolvió en la PRUEBA (`AddLogging()` en `ContenedorPruebas`). Pero esa prueba existe para descubrir registros que faltan: si ella provee la dependencia, deja de demostrar que el módulo se basta solo. Se quitó de la prueba y queda el de `AgregarZass` |

**Un agente dejó un mutante vivo en el árbol** al cortarse por el límite de uso: `TrabajoDeCorreo`
tenía `if (false)` en vez de la guarda del proveedor. Revertido. **Lección: al retomar una rama de
agente, lo primero es buscar mutantes**, no suponer que el árbol está limpio.

**Lo que se comprobó, y no se dio por bueno:**
- La regla de arquitectura se vio **fallar con la violación real** antes de arreglarla.
- El candado del sitemap se vio **ponerse rojo** al cambiar `RUTAS_ESTATICAS` en el origen.
- Mutante del límite de espera de la sonda (3 s → 60 s): la prueba de la base que acepta la
  conexión y calla se pone roja. El límite está vigilado de verdad.
- `ContenedorPruebas` resuelve todo el correo **sin** `AddLogging` en la prueba.

**Lo que NO se pudo comprobar:**
- La guarda `Proveedor == Ninguno` no se pudo matar con un mutante: con `if (false)` el trabajo de
  fondo no termina y la suite se **cuelga**, así que la señal no es un rojo limpio. Falta una prueba propia.
- **Docker no se probó**: no está instalado en esta máquina.
- No se envió ningún correo real: no hay cuenta de Brevo ni de Resend.

### Los anclajes de registro (8-nov-2026, noche)

`AgregarZass.cs` y `Program.cs` tienen **secciones vacías a propósito**, una por módulo pendiente
(MOD-06, MOD-11, MOD-02, trabajos de fondo) y separadas entre sí. Un agente registra **solo en su
anclaje**. Así cuatro a la vez insertan en regiones distintas y git fusiona sin conflicto.

Primero se pensó en descubrimiento por reflexión, y se descartó: las tres fusiones de hoy
fusionaron esos dos archivos **solas**. Pero al poner al día las ramas en pausa, el WIP de MOD-11 y
el de MOD-02 **sí** conflictuaron ahí, porque los dos habían registrado al final de la misma lista.
Los anclajes son exactamente para eso, y ya se usaron para resolver esos dos conflictos.

### Sesión del 9-nov-2026 — lo hecho, lo comprobado y lo que NO

**Backend: 1.261 pruebas en verde** (654 aplicación + 16 arquitectura + 591 integración), desde 836.
Las de interfaz (283) **no se corrieron esta sesión**: no se tocó el frontend.

| Qué | Estado | Cómo se comprobó |
|---|---|---|
| `agente/verificacion` (MOD-02: cola, tomar, resolver, API-27/29/30/31) | **Fusionada** | Compilaba, pero **3 pruebas rojas nacieron de juntarla con `correo`**: leía el token del aviso partiendo el cuerpo por espacios, y desde `correo` es una carga JSON. Se arregló con `CargaDeAviso.LeerToken` |
| `agente/fondo` (tabla `trabajos_de_fondo`) | **Fusionada y terminada** | Migración aplicada; tabla y PK consultadas en `information_schema`/`pg_constraint`; `has-pending-model-changes` limpio |
| Planificador A-09 (`TrabajoDiario`, `CoordinadorDeTrabajos`) | **Hecho** | 7 pruebas contra PostgreSQL con una réplica por contexto. Se **mutó** la guarda de CC-08 y 3 se pusieron rojas |
| Avisos de suscripción (7 y 1 día, y vencida) con plantilla | **Hecho** | La clave lleva el umbral, no la fecha; el segundo intento choca con R-18 (23505) y queda un aviso |
| MOD-06 Agenda, backend completo (API-32 a API-42) | **Hecho** | Ver abajo |
| Pantalla de la agenda | **NO hecha** | — |
| MOD-11 Reseñas (backend: API-46 a API-53) | **Fusionado, verde, REVISADO SOLO EN PARTE** | Ver abajo |

**Documento corregido (se movió el documento, no el código), con fecha puesta en cada uno:**

- **CC-08** prescribía `pg_try_advisory_lock` «sin tabla de bloqueos». El bloqueo impide que dos corran a la
  vez pero **no recuerda que ya corrió hoy**; un despliegue a media mañana repite el trabajo. Se cambió a
  una fila por trabajo con reserva que vence. La tabla es **TBL-32** (Parte 5 · Operación; el total pasa a 32).
- **TBL-18** no listaba `perfil_prestador_id` ni `clave_idempotencia`, que existían en el esquema.
- **TBL-16**: las horas son **locales de Chile**, no UTC (ver el defecto de abajo).
- El sitio se regeneró tras cada cambio y se comprobó en el HTML, no solo en el `.md`.

**El defecto más caro de la sesión: la agenda salía corrida 3-4 horas.** El generador de bloques tomaba
`hora_inicio` como UTC: «lunes 09:00» llegaba a las 06:00 de Chile. **Las 36 pruebas de la agenda estaban verdes**
porque contaban cuántos bloques salían y ninguna miraba a qué hora. Corregido con `HoraDeChile`
(`America/Santiago`, con respaldo al nombre de Windows; la hora que el horario de verano se salta no genera
bloque). 10 pruebas nuevas; **se revirtió el generador a UTC y 5 cayeron**.

**MOD-06, lo que importa:**

- `IUnidadDeTrabajo.EnTransaccionAsync` es nuevo y CC-01 lo exige: el `FOR UPDATE` solo sirve si el INSERT y el
  UPDATE viven en la misma transacción que el bloqueo.
- El contacto de TBL-21 se deja por un puerto de salida, `IContactos`. `MOD-06 → MOD-11` está prohibido
  (ciclo), pero el contacto debe quedar dentro de la transacción. Mismo patrón que `INotificaciones`.
- `FiltroIdempotencia`: 400 sin `Idempotency-Key`, y también con dos cabeceras distintas.
- `version` de la cita **sube** en cada transición; antes era token de concurrencia que nadie incrementaba.
- Quien no es parte de una cita recibe el mismo 404 que si no existiera.

**La lección de verificación, otra vez, dos veces:**

1. «Diez personas sobre el mismo bloque» **pasaba igual con el `FOR UPDATE` quitado**: las llamadas no se
   solapaban. Con `Task.Run` + `Barrier` sí mide; sin el bloqueo cae.
2. CC-06 se probó con dos reprogramaciones cruzadas y se **mutó el orden de bloqueo**: cae con
   `40P01: deadlock detected`, exactamente lo que el documento describe.

Además, dos afirmaciones mías resultaron falsas al mutar y se corrigieron: un comentario que decía que el orden
«vencer y luego avisar» importaba (no importa, `PorVencerAsync` solo mira vigentes) y una prueba de
idempotencia que no vigilaba nada porque el reloj de prueba no avanzaba.

**MOD-11 Reseñas: qué se verificó y qué no (leer antes de fiarse del verde).**

Se fusionó `agente/resenas` (4.400 líneas, la mayoría pruebas del agente: 115 de aplicación y 66 de
integración, todas verdes). **Yo no las revisé línea por línea ni les hice mutaciones**: confío en ellas lo
que confiaría en cualquier WIP de agente, que esta sesión demostró que es poco. Lo que sí se hizo:

- Falló la compilación por mi `EnTransaccionAsync` nuevo en `IUnidadDeTrabajo` (un doble de prueba del agente
  no lo implementaba) y **las 17 `ResenasApiPruebas` cayeron juntas**: el mismo defecto del token del aviso
  (`Split(' ')`), **la cuarta vez que pasa al fusionar una rama escrita antes de `correo`**.
- **Defecto de la unión, probado y corregido**: el contacto de una cita apunta a la PRIMERA de la cadena
  (RN-S4-05) y esa queda `Reprogramada`; la regla de reseñas la trataba como «la cita no llegó a realizarse».
  **Quien reprograma y luego era atendido perdía el derecho a reseñar.** Ninguna rama podía verlo sola. Hay un
  par de pruebas de punta a punta en `AgendaIntegracionPruebas` (agendar → reprogramar → pasar la hora →
  marcar asistencia → `EsElegible`): el caso base pasa y el de reprogramar **cayó con «CitaSinRealizar» antes
  del arreglo**. Ahora reseñas sigue el salto de `cita_origen_id` (`SeguirLaCadenaAsync`).
- **La sección 4.1 del documento decía que reseñas no toca la tabla `cita`**, pero la elegibilidad la lee a
  través de `Contacto.Cita`. Se dejó escrito en 4.1 por qué difiere y qué costaría cumplirla al pie de la
  letra. `IServicioAgenda.CitaRealizadaAsync` existe y **nadie la usa todavía**.

Abierto en reseñas: la cita `NoAsistio` hoy **no** habilita reseña (el agente lo dejó como «decisión pendiente»);
`estados-cita.md` dice que si faltó el prestador sí, pero la cita no guarda **quién** faltó. Es una decisión de
Pablo o un campo nuevo. Sin pantalla de reseñas.

**Cosas que conviene saber y que quedan abiertas:**

- **Resuelto el rojo intermitente (PR #4, commit 8e12792)**: `Dos_reservas_simultaneas_reciben_filas_distintas` (correo) era un defecto de `ProcesadorDeCorreo.TomarLoteAsync`, no contaminación entre pruebas. Con estadísticas de tabla casi vacía,
  PostgreSQL planificaba `WHERE id IN (SELECT ... LIMIT @lote FOR UPDATE SKIP LOCKED)` como Nested Loop Semi Join que reejecutaba la subconsulta por fila y el UPDATE tomaba de más (10 con lote 5 en CI PG 17.5; reproducible en PG 18).
  Podía afectar a producción (Neon PG 18). Se arregló con `WITH lote AS MATERIALIZED (...)` y prueba determinista `Un_lote_nunca_trae_mas_avisos_que_TamanoDeLote_aunque_el_plan_reejecute_la_subconsulta` que cae con el código viejo.
- **HECHAS las tres transiciones que dispara el tiempo** (T5, T8, T11), en `Planificador` (reemplaza a
  `TrabajoDiario`; dos filas en `trabajos_de_fondo`: `suscripciones-diario` y `citas-periodico`, esta cada 15 min).
  Cada cita se procesa en SU transacción y se relee el estado dentro de ella. **Comprobado**: contra la base, una
  cita llega de confirmada a realizada y el prestador marca asistencia; y se mutó la releída de estado, que
  **al principio sobrevivió** (ninguna prueba secuencial puede provocar esa carrera): se añadió un doble con lista
  obsoleta y ahora cae. Las transiciones del tiempo se anotan en TBL-19 como `Asistencia`/`Rechazo` con
  `realizado_por` nulo; el documento no tiene un tipo propio para «venció el plazo», y no se inventó uno.
  **Lo que NO se probó**: el `Planificador` entero corriendo con su temporizador (se apaga en las pruebas de la
  API); solo se probaron sus piezas y el servicio que llama.
- Recordatorios de cita (RN-S4-07) y avisos de cita (confirmada, cancelada…): **no hechos**. `TienePlantilla`
  solo cubre verificación, recuperación y los dos de suscripción.
- `appsettings.Development.json` **ahora se versiona a propósito** (sin secretos). Estaba en `.gitignore`
  pero la fusión de `verificacion` lo trajo igual, quedando rastreado e ignorado a la vez. La nota vieja de
  esta sección 10 que dice que está ignorado **ya no es cierta**.
- Los 32 commits de esta sesión **ya están subidos** a `origin/main` (hasta `ae36c0a`). Antes de cada push se revisan los nombres de archivo por claves o certificados.
- `ver-zass.bat` figura modificado en los worktrees de agentes; no es de `main`.

### PAUSA del 9-nov — dónde quedó y el siguiente paso exacto

**Lo último que se hizo (commiteado, NO subido):** la ficha pública ahora expone, por servicio, el `id` de la
oferta y el `modoAgenda`, porque el calendario (API-35) no se puede pedir sin ese id y CU-12 paso 1 manda mostrar
el modo. Eso obligó a cambiar **dos guardas que lo prohibían a propósito** (`FichaPublicaPruebas` y
`ApiPruebas.La_ficha_publica_no_filtra...`); siguen protegiendo el id de catálogo, el del perfil y todo lo de la
cuenta. Se actualizó el tipo `FichaPublica` del frontend.

**Verificación al pausar:** backend completo en verde tras el cambio, **re-corrido entero**: 654 de aplicación +
16 de arquitectura + 591 de integración (1.261). **Las pruebas de interfaz (283) no se corrieron**: solo se tocó
el tipo `FichaPublica`, pero no se comprobó que compile el frontend ni que sus pruebas sigan pasando.

**Siguiente paso exacto: la pantalla de la agenda** (el backend está completo, falta casi todo el frontend):
0. **HECHO**: `enviar` ya admite cabeceras propias (4º parámetro) y repite la MISMA `Idempotency-Key` en sus
   reintentos internos; 4 pruebas, y se mutó el reintento para ver que caen. **Las pruebas de interfaz pasan y
   `tsc -b` compila limpio** (esto cierra el hueco anotado arriba).
1. **HECHO** `frontend/src/funciones/agenda/servicio.ts` (+10 pruebas): las siete llamadas (API-35 a API-42), la
   clave de idempotencia (`nuevaClaveDeIdempotencia`, UNA por intención) y los ayudantes de hora de Chile
   (`horaDeChile`, `porDia`). **Falta la pantalla que lo use.** Los bloques se agrupan por día CHILENO, no UTC.
2. **HECHO** `AgendarHora.tsx`, montado en `Ficha.tsx` (12 pruebas): elegir servicio → días y horas libres en hora de
   Chile aviso de modo manual ANTES de elegir (4C) → nota → confirmar. Sin cuenta lleva a entrar/crear cuenta con
   `state.despues` = la ficha con `?servicio=&bloque=` y vuelve con la hora elegida (4G, sin inventar mecanismo). La
   clave de idempotencia nace al elegir la hora; reintentar reusa la misma y otra hora es otra intención. Un 409
   suelta la elección, recarga el calendario y deja el aviso visible: **un defecto real que cazó su propia prueba**
   (el aviso vivía dentro del panel que desaparecía; se reintrodujo y la prueba cae). **Falta verlo en un navegador:
   no se abrió el sitio, solo pruebas con fetch simulado.** El enlace «Ver mis citas» apunta a `/mis-citas`, que
   **sí existe** desde `b678b42` (este punto estaba equivocado).
3. **HECHO (12-nov-2026, rama `agente/mis-citas`, commit `4c014fb`, sin fusionar).** La página «Mis citas»
   ya existía en `main` con cancelar, confirmar, rechazar y marcar asistencia. Lo que faltaba de verdad y se
   construyó: **reprogramar** (API-41, RN-S4-05) y **agrupar por día CHILENO**. 23 pruebas nuevas; las de
   interfaz pasan de 373 a **396 en 29 archivos**, `tsc -b` limpio, 12 mutantes probados y los 12 en rojo.
   **No se abrió un navegador de verdad: todo es `fetch` simulado en jsdom.** Reparto aclarado:
   `/mi-agenda` = reglas de atención (qué horas ofrezco, qué días bloqueo); `/mis-citas` = ciclo de una cita,
   de los dos lados. Pendiente relacionado: `CitaDto` no trae el `slug` y API-35 solo se puede pedir por slug,
   así que hay un apaño en el frontend (`slug-del-prestador.ts`) que se borra cuando el backend lo devuelva.
4. Después, la pantalla de reseñas (API-46 a API-53).
Mantener la línea de diseño de `diseno/prototipo-comunidad.html`: sitio de comunidad, no formulario.

**Decidido por Pablo (11-nov-2026):** la cita `NoAsistio` será completa con migración N3 (guarda quién faltó). Si faltó el prestador, el cliente sí reseña; si faltó el cliente, no hay reseña; si se contradicen, se bloquea la reseña y pasa a cola interna.

### Segunda tanda de agentes (8-nov-2026, noche) — CÓMO RETOMAR

Cuatro agentes en worktrees bajo `C:\Users\pdani\zass-agentes\<nombre>`, cada uno en su rama
`agente/<nombre>` y con **su propia base de pruebas** (`zass_pruebas_<nombre>`). **Hay que REVISAR
cada rama antes de fusionarla: correr sus pruebas aquí, no fiarse del informe.** Las tres fusiones
de hoy demuestran por qué.

> **ACTUALIZACIÓN 9-nov: `verificacion` y `fondo` ya están fusionadas y terminadas (ver arriba). Siguen sin fusionar `resenas` y `agenda`; esta última queda obsoleta porque MOD-06 se hizo directamente en `main`. Sus worktrees se pueden borrar.**
>
> ### LOS CUATRO SE CAYERON AL ARRANCAR. NO ESTAN CORRIENDO.
>
> Los cuatro murieron al primer o segundo paso con el **límite de 5 horas agotado** (se reiniciaba
> a las 6:30 AM de Santiago). **Ninguno llegó a terminar nada.**
>
> **La causa fue lanzar cuatro agentes en paralelo sobre Opus**, más las fusiones que se estaban
> haciendo en la misma ventana. Cada agente consume del mismo presupuesto. El techo no era la
> capacidad de revisión, era el presupuesto de la ventana de 5 horas.
>
> **Recomendación para la próxima tanda:** o **dos agentes** en vez de cuatro, o **los cuatro sobre
> Sonnet** y dejar Opus para revisar y fusionar, que es donde se encontraron los defectos de hoy.
> Los prompts que se les dio eran muy específicos, y para trabajo así especificado Sonnet rinde.
>
> **Lo que SI quedó guardado** (se buscaron mutantes en los cuatro worktrees y **no hay ninguno**):
>
> | Rama | Qué dejó |
> |---|---|
> | `agente/fondo` | Un commit WIP con lo difícil ya diseñado: `TrabajoDeFondo` (una fila por trabajo programado, con próxima corrida y **reserva que vence**), su migración y el mapeo en `ZassDbContext`. Resuelve que el planificador corre en todas las réplicas y avisar dos veces mandaría dos correos. **Sin verificar: no se sabe si compila, la migración no se aplicó a ninguna base, y nadie llama todavía a `VencerExpiradasAsync` ni a `PorVencerAsync`, que era la tarea.** Hay que comprobar contra el documento los identificadores A-09 y CC-08 que cita |
> | `agente/verificacion` | Un commit WIP con cinco archivos que dejó preparados. **Sin verificar** |
> | `agente/resenas` | Nada nuevo. Sigue en su WIP anterior, al día con main, y **compila** |
> | `agente/agenda` | Nada. Limpio, partiendo de `main` |
>
> Si se relanzan, vale la pena rehacer los prompts con el mismo nivel de detalle: incluían la base
> de pruebas propia de cada uno, su anclaje de registro, las reglas de arquitectura que vigila
> NetArchTest, y cómo se verifica en este proyecto (mutar la guarda y verla en rojo).

| Rama | Qué es | Punto de partida |
|---|---|---|
| `agente/resenas` | MOD-11 Reseñas: backend + pantalla | Su WIP, ya commiteado y al día con main. **Compila** |
| `agente/verificacion` | MOD-02 Verificación manual de credenciales + MOD-08 | Su WIP, ya commiteado y al día con main. **Compila** |
| `agente/agenda` | MOD-06 Agenda (CU-12, R-1 doble reserva, CC-01 bloqueo de fila) + el filtro de idempotencia que API-36 necesita | Desde `main`, limpio |
| `agente/fondo` | Trabajos de fondo: vencer suscripciones, avisar antes, y despachar los avisos de TBL-27 (que ahora sí pueden salir, porque el correo existe) | Desde `main`, limpio |

Las ramas ya fusionadas (`agente/produccion`, `agente/seo`, `agente/correo`) siguen teniendo su
worktree en disco. No molestan, pero se pueden borrar con `git worktree remove --force`: su trabajo
está en `main` y lo único que tienen sin commitear es un espacio en blanco en `ver-zass.bat`.

Para fusionar: `git -C C:\Users\pdani\zass-app merge agente/<nombre>`.

### Lo que falta para PUBLICAR en Cloudflare (decisión de Pablo)

1. **Dónde corre la API y la base:** Cloudflare Pages solo aloja el sitio. Propuesta gratis: **Render** (API) + **Neon** (PostgreSQL con PostGIS).
2. Cuenta de Cloudflare con `zass.cl` y sesión de `wrangler` (`! npx wrangler login`), cuenta de Brevo o Resend para los correos.
3. Fusionar `agente/produccion` y `agente/correo` (sin ellas no hay IP real detrás del proxy ni correos), y `agente/seo` si se quiere que Google vea las fichas.
4. Reemplazar los datos de demostración por gente real antes de abrirlo (`limpiar-demo.sql`), y revisar los textos legales con un abogado.
5. Pregunta 4 sigue sin responder: ficha pública y NFC con suscripción vencida.

### La barra de búsqueda y el segundo rediseño (7-nov-2026, noche)

Pablo vio la página y dijo que **le faltaba mucho y se veía pobre**, que la frase fuera «busca a alguien de tu
**ciudad**» (no de tu barrio), y pidió **un buscador como el de Google o Instagram**: escribir «abogado» y ver todos los
abogados, ordenados de lo más cerca a lo más lejos, de lo más recomendado a lo menos, y por precio. Dijo que se podían
usar animaciones.

**API-63 `GET /api/buscar?q=&lat=&lon=&zona=&orden=&pagina=`** (MOD-05, público, tasa P): texto libre contra el
**nombre, el rubro y los servicios**, sin tildes ni mayúsculas, con singular/plural (`abogados` = `abogado`) y con
**sinónimos de oficio** (`abogado`→Derecho, `gasfiter`→Técnicos y oficios…; lista corta y a mano en
`TextoDeBusqueda.cs`). Cada palabra es un grupo y **hay que cumplirlos todos**. Órdenes: `Recomendados` (con sello,
luego reputación pública, luego cantidad de reseñas), `Cercania`, `PrecioAsc`, `PrecioDesc` (los sin precio, al final).
Mismo criterio de «visible» que la búsqueda por rubro. La distancia es **texto redondeado y solo existe con la ubicación
de la persona**; `Cercania` sin ubicación cae a `Recomendados` y lo dice. Sin texto lista a toda la comunidad. Páginas de 24.

| Detalle | |
|---|---|
| `normalizar_texto()` en la base | Migración `NormalizarTexto`. **La prueba que la compara con C# cazó un fallo real**: `lower()` depende de la configuración regional y no pasaba `Ñ` ni `Í` a minúscula, así que «GARCÍA» no coincidía; las mayúsculas con tilde ahora van en el `translate` |
| La ubicación | Vive **solo en memoria** (`marco/ubicacion.ts`), nunca en la URL. «Más cerca» la pide al pulsarlo, una vez, y si se niega no se insiste |
| Sugerencias mientras se escribe | Espera 280 ms, mínimo 2 letras, descarta respuestas viejas y **guarda en memoria lo ya preguntado** (el límite público es 100 peticiones por 15 min por IP) |
| Accesibilidad | Combobox con flechas, Enter y Escape; lista que se anuncia; 44 px de toque |

**Filtros (hechos):** `verificados=true` y `calificacion=1..5` en API-63, y los botones «Solo verificados» y «4 estrellas o más» en la pantalla. La nota mira la reputación **pública**: quien tiene menos de 3 reseñas no entra (RN-S5-11), y la pantalla lo avisa cuando no hay resultados. Se rechaza una nota fuera de 1 a 5 en vez de «corregirla». Mutación: 11 mutantes en el backend (uno sobrevivía: el endpoint podía ignorar `verificados` y la prueba HTTP no lo notaba) y 7 en la pantalla.

**Rediseño (oscuro, con la marca):** amarillo eléctrico sobre tinta, vidrio, cuadrícula tenue, JetBrains Mono para las
etiquetas. Piezas propias: **radar de Temuco** con los barrios reales (dirección real desde el Centro, distancia
comprimida con raíz, puntos separados, cada uno es un enlace a ese barrio), borde de luz giratorio en la barra, foco que
sigue al puntero, titular con brillo, cinta de dos filas, cifras que suben, entradas animadas al hacer scroll y tarjetas
con foco de luz. **Todo se apaga con «reducir movimiento».**

**Se midió:** 39 pruebas de aplicación nuevas, 15 de integración contra PostgreSQL, 8 por HTTP y 25 de interfaz. Mutación:
25 mutantes sobre el SQL y el servicio (al principio **sobrevivían 6**: los desempates tapaban el orden pedido; se
escribieron casos donde el orden pedido contradice al de recomendados) y 15 sobre la barra y la página. **Fallos reales
que destapó:** (1) el desplegable de sugerencias quedaba *debajo* de los atajos y recortado (las animaciones de entrada
crean un contexto de apilamiento por bloque); (2) al quitar la ubicación se hacía por un instante una búsqueda «más
cerca» sin ubicación (el estado local cambiaba antes que la URL); ahora **la URL es la única fuente de verdad**.

**Lo que NO está hecho:**

| Falta | Por qué importa |
|---|---|
| Sinónimos: solo los 5 rubros de hoy | Cada rubro nuevo necesita sus palabras |
| Fotos | Siguen siendo las iniciales |
| Rendimiento no medido | Con miles de prestadores hay que revisar el plan de la consulta (`ILIKE` sobre `LIKE '%…%'` no usa índice) |

### MOD-02 Credenciales — la primera parte (7-nov-2026, noche)

Se hizo lo que **no necesita archivos**: declarar, listar y eliminar credenciales (CU-05 pasos 1 a 5 y
flujo 4F), y mostrar las públicas en la ficha. Subir documentos (API-26) y enviar a revisión (API-27)
dependen de **MOD-08** (almacenamiento privado con antivirus), que no existe; la cola del Verificador
(API-29 a 31) depende de lo anterior.

| Hecho | Detalle |
|---|---|
| API-24 `GET /api/mi/credenciales` | Las del dueño, **con estado interno** y si tiene respaldo |
| API-25 `POST /api/mi/credenciales` | Crea en `Borrador`: solo su dueño la ve. Nombre 3–150, obtención no futura, vencimiento posterior, entidad del catálogo o escrita a mano (**queda «no catalogada»** y se reutiliza si ya existe, sin distinguir mayúsculas) |
| API-28 `DELETE /api/mi/credenciales/{id}` | Borra la propia y sus documentos (cascada). **La de otra persona responde igual que una inexistente** (404, mismo cuerpo) |
| Ficha pública (API-05) | Ahora trae `credenciales`: solo «Verificada» o «Declarada por el usuario», **ningún borrador**, sin estado interno ni archivos (RN-S2-03). Las verificadas van primero |
| Tope | 25 credenciales por prestador (decisión mía: holgura sobre «1 a 5» y límite al trabajo de revisión) |
| Auditoría | Crear y eliminar quedan registrados (RN-S2-08), **sin el contenido** |

**Se midió:** 16 mutantes sobre el servicio (todos muertos), 8 sobre el repositorio (**al principio 4
sobrevivían**: el servicio tapaba al repositorio; se escribieron pruebas que lo preguntan solo) y 7 sobre
los endpoints (uno equivalente: sin la política de autorización el endpoint ya responde 401 por su cuenta).

**Un fallo real que destapó una prueba:** un valor de enum inexistente en el JSON (`"tipo": "Hechicero"`)
daba **500**. ASP.NET lanza la excepción en Development en vez de responder 400, y el manejador global la
volvía 500: las pruebas veían una API distinta de la desplegada. Se fijó `ThrowOnBadRequest = false` en
`Program.cs` y hay una prueba con JSON roto (400 en todos los entornos).

**Lo que NO está hecho de MOD-02:**

| Falta | Por qué importa |
|---|---|
| **MOD-08**: subir archivos, almacenamiento privado, antivirus, URL firmada | Sin eso no hay `Disponible`, y sin eso nada puede enviarse a revisión (RN-S2-01) |
| API-27 enviar a revisión, API-29/30/31 cola y resolución del Verificador | El sello «Verificado» **sigue sin poder dárselo nadie** |
| Flujo 4E: editar una credencial verificada la devuelve a revisión (RN-S2-07) | No hay edición todavía |
| Borradores de 90 días (4D.3) | Falta el trabajo de fondo |
| Entidades nuevas duplicadas por carrera | Dos personas escribiendo a la vez la misma entidad nueva chocan con el índice único (500 raro, muy improbable) |
| Pantalla de «Mis credenciales» | Falta (se hace aparte) |

### El sitio multipágina (7-nov-2026, noche)

Pablo pidió *"una página web llamativa, no todo en una sola página, que una abra otra"*. La interfaz
se reconstruyó como **sitio de varias páginas con `react-router`** (una URL por página, se pueden
compartir y el botón Atrás funciona):

| Ruta | Qué es |
|---|---|
| `/` | Portada corta: titular, búsqueda en una frase («Busco *oficio* en *barrio*»), mazo de tarjetas con gente real (API-62), cinta de oficios, tres entradas a otras páginas, principios |
| `/buscar` | Búsqueda guiada por la URL (`rubro`, `zona`, `radio`, `verificados`, `cal`): se comparte y se recarga sin perderla. **La ubicación exacta nunca va en la URL**: vive en memoria |
| `/prestadores/:slug` | La ficha pública (API-05), pieza central: sobre la persona, servicios con precio, barrios, reseñas, y un lateral con el contacto que la persona eligió publicar |
| `/barrios`, `/oficios` | Índices con cuánta gente hay en cada uno (API-62) |
| `/profesionales` | La invitación a ofrecer un servicio: solo promete lo que la aplicación ya hace |
| `/entrar`, `/crear-cuenta` | Con `?rol=prestador` la cuenta parte como profesional; entrar lleva a Mi ficha |
| `/mi-ficha` | Editar el perfil es editar tu propia ficha: «Sobre ti», servicios y barrios se editan en su lugar, con una guía de lo que falta y publicar/pausar |
| `*` | Página «no encontrada» con salida |

**Decisiones mías (Pablo no las contestó, y quedan abiertas a cambio):** referencia visual = el
prototipo `diseno/` llevado más lejos (cabecera flotante, titular enorme, cinta, tarjetas que se
inclinan); **sin fotos por ahora** (las iniciales son la cara; la subida de archivos no existe);
**el buscador no tiene perfil público**. El texto del sitio no promete sellos revisados, agenda ni
reseñas escritas: no existen todavía.

**Cómo verlo:** API (`ASPNETCORE_URLS=https://localhost:7277 ASPNETCORE_ENVIRONMENT=Development dotnet run
--no-launch-profile` en `backend/src/Zass.Api`) y `npm run dev` en `frontend/` → https://localhost:5173.
Datos de demostración: `frontend/pruebas-navegador/datos/sembrar-demo.sql` (21 personas inventadas; el
promedio y el recuento de reseñas están escritos a mano, **no hay reseñas detrás**) y
`limpiar-demo.sql` para quitarlos.

**Se midió, no se supuso:**
- API-62: 8 mutantes sobre «visible», orden, zona principal y sello; al principio **sobrevivieron 2**
  (zona y sello) y se escribieron las pruebas que faltaban.
- Renovación de sesión ante 401 (`cliente.ts`): 5 mutantes muertos; no renueva en `/api/auth/`.
- Páginas: 8 mutantes, 6 muertos; 1 era una **redirección abierta** (`despues=//sitio-malo`), que ahora
  tiene su prueba; el otro es equivalente en los flujos actuales.
- `pruebas-navegador/mirar-sitio.mjs`: 11 páginas × escritorio y celular, sin desborde horizontal, sin
  errores de consola, sin objetivos táctiles de menos de 44 px, Montserrat cargada. Encontró tres
  fallos míos (desborde de 6–8 px en celular por `contenedor`+`seccion` en el mismo elemento, enlaces de
  16–20 px, y un 401 en consola en cada visita anónima).
- `pruebas-navegador/flujo-real.mjs`: cuenta → entrar → armar la ficha → publicar → aparece en la
  búsqueda → ficha pública → recargar → salir, contra la API y PostgreSQL reales. **Destapó un fallo
  real**: dos redirecciones competían tras entrar y un profesional caía en la portada.

**Lo que NO está hecho, y no hay que dar por hecho:**

| Falta | Por qué importa |
|---|---|
| **Envío de correos** (verificación y recuperación) | Las páginas ya existen (`/verificar-correo?token=…`, `/recuperar-contrasena`, `/restablecer-contrasena?token=…`, API-13/14/15), pero **no hay quien mande el correo**: el servicio de avisos no está construido. Cuando exista, los enlaces tienen que apuntar a esas rutas. El recorrido real sigue verificando por SQL |
| **Límite de la API pública: 100 peticiones por 15 min por IP** | Una portada hace ~3 (catálogo y comunidad se guardan en memoria). Detrás de una red compartida (café, datos móviles con NAT) varias personas comparten IP. **Hay que medir y decidir antes de salir a producción**; en desarrollo se sube con `Zass__Tasa__P` y `Zass__Tasa__G` |
| `zass.cl` en línea | Solo Pablo: hosting, DNS y HTTPS. Falta decidir dónde corre la API, cookies `Secure`/`SameSite` para el dominio real y CORS |
| Foto, títulos y dirección en Mi ficha | Dependen de subida de archivos y del geocodificador |
| Montserrat desde Google Fonts | Conviene autoalojarla (privacidad y velocidad) |
| Pruebas de Mi ficha en Vitest | Hoy la cubre el recorrido real en navegador; faltan pruebas de componente |
| Pregunta 4 (ficha pública con suscripción vencida) | Sigue abierta y es de Pablo |

### MOD-05 Búsqueda — la frase entera, por fin

Es el único módulo **sin tablas propias**: solo lee. Si alguna vez necesita una, está
asumiendo responsabilidades que no le tocan.

| Pieza | Estado |
|---|---|
| `IServicioBusqueda.Buscar` | Los cuatro filtros duros del paso 5 de CU-08, orden, cursor y distancia redondeada |
| API-01 `GET /api/busqueda?rubro=corretaje&lat=&lon=&radio=` | Público y sin sesión (P1 de CU-08) |
| `RubroPorCodigo` en MOD-03 | El rubro va por **código** en la URL, no por id: un enlace a una búsqueda se comparte y se lee |
| `IServicioBusqueda.FichaPublica` · API-05 `GET /api/prestadores/{slug}` | **CU-09, la ficha pública completa.** Público y sin sesión |
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

### CU-09, la ficha pública completa (API-05)

La ficha devolvía solo lo que sabe MOD-01: nombre, zonas **como nombres sueltos** y contacto. **Le
faltaban los servicios y el nombre del rubro** (solo traía el id), que es justo lo que cualquier
diseño de ficha necesita. Ahora API-05 devuelve la ficha **compuesta**.

- **Es de MOD-05**, porque la tabla de módulos le asigna CU-09 y porque consume a los tres: el
  perfil es de MOD-01, los servicios de MOD-03 y las zonas de MOD-04, y ninguno puede armarla
  solo. El contrato de MOD-01 (`PerfilPublicoPorSlug`) no se tocó.
- **Trae:** nombre, descripción, foto, el **rubro con su nombre**, las **zonas con la principal
  primero y marcada**, los **servicios** (nombre, duración y precio de referencia), el contacto que
  el prestador marcó como público, y la reputación según RN-S5-11.
- **Lo desactivado no sale.** Lo que un prestador dejó de ofrecer se conserva en la base (las citas
  y los horarios cuelgan de esas filas) pero no es público. Se probó recorriendo la desactivación
  contra PostgreSQL. Los servicios van ordenados **en español**: la "Á" y la "Ñ" ordenan donde
  corresponde y no después de la zeta, que es lo que hace una comparación ordinal.
- **Un rubro apagado sigue apareciendo** en la ficha de quien ya lo tiene: sin eso, apagar un rubro
  del selector dejaría fichas sin rubro de un día para otro.
- **"Sin publicar" y "no existe" son indistinguibles**: misma respuesta, byte a byte, comprobado por
  HTTP. Si no, probar slugs diría quién se dio de alta sin publicar.
- **RN-S3-02, sobre los bytes de la respuesta y sobre los tipos:** ni dirección, ni coordenadas, ni
  el id de la cuenta, ni el correo con el que se registró (que es otro que el correo público). Y el
  servicio público tampoco lleva lo interno: ni el id de la fila de oferta ni el modo de agenda.
- **Una consulta tras otra, no en paralelo:** los tres módulos comparten el `DbContext` de la
  petición, y un `DbContext` no admite dos operaciones a la vez.
- **Se midió:** seis reglas de la composición rotas a propósito (7 rojos) y el endpoint y la regla
  de "sin publicar" rotos por HTTP (4 rojos). Cada regla, cazada por su prueba.

**Sigue abierto, y no es mío decidirlo (pregunta 4 de la sección 9):** esta ficha existe mientras el
perfil esté **publicado**, sin mirar la suscripción. O sea que un prestador con la suscripción
vencida desaparece de la búsqueda pero **su ficha por enlace directo sigue abierta**. Es el mismo
asunto que la tarjeta NFC y conviene resolverlo antes de MOD-12.

**API-62 `GET /api/comunidad` (añadido al rediseñar el sitio):** total de prestadores, personas por barrio y por oficio, y los 6 últimos en sumarse, para que la portada, los barrios y los oficios muestren gente real. Público, tasa P, sin ubicación ni datos de la cuenta. "Visible" se repite en LINQ (el SQL de la búsqueda es aparte), y una prueba compara los conteos con lo que la búsqueda devuelve. **Se midió:** 8 mutantes (sin filtro de publicado, sin vigencia, cualquier estado de suscripción, orden invertido, zona no principal, sello sin verificar, sin corte, total por coberturas): al principio sobrevivieron 2 (zona y sello), se añadieron pruebas y ahora los 8 mueren.

**Lo que NO está hecho de MOD-05, y no hay que dar por hecho:**

| Falta | Por qué importa |
|---|---|
| **`BuscarAgregadoPorZona`** y registrar la búsqueda de forma agregada (paso 13 de CU-08) | Era el dato que se le entregaba al municipio. **Con la postulación municipal cancelada (8-nov-2026) baja de prioridad**, pero sigue siendo el paso 13 de CU-08 |
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
| 15 | ⚠️ **ESTO VOLVIÓ A ESTAR ROTO Y SE DIO POR ARREGLADO CASI UN MES.** Ver «Sesión del 13-nov»: el control desapareció al pasar al sitio de varias páginas, y lo de abajo («Visto en el navegador real») dejó de ser cierto sin que nada se pusiera rojo. Reconstruido el 13-nov, ahora con una prueba que mira la pantalla. **La pantalla no tiene control de radio.** Con la ubicación, el radio es 10 km y no se puede cambiar. Quien cubre tu zona pero se basa a más de 10 km (el centroide de su zona principal está a 14,5 km del Centro, en el caso de la prueba) **no aparece nunca**, y no hay cómo ampliar ni se le dice a la persona que hay alguien más lejos. El flujo 4B de CU-08 ("ampliar el radio") existe en el documento y no en el código | **Arreglado.** Con la ubicación aparece "¿Hasta qué distancia?" (5, 10, 20 y 50 km, el mayor es el máximo de RN-S3-04), y sin resultados se propone "Ampliar a 50 km", que repite **la búsqueda que se hizo** y no lo que el formulario diga ahora. Visto en el navegador real: con 20 km aparece el prestador de Labranza, último y "a 15 km". 12 pruebas y 6 mutaciones, 6 rojos |
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

**Repetirlo:** (⚠️ `mirar-busqueda.mjs` se borró el 13-nov: medía esta pantalla, que ya no existe.
Lo que sigue vivo son `mirar-sitio.mjs` y `mirar-publicas.mjs`.) `frontend/pruebas-navegador/mirar-busqueda.mjs`, con los datos de
`datos/sembrar-visual.sql`. **Ya no hace falta limpiar al terminar**: las pruebas de integración
tienen su propia base (ver los avisos), así que los prestadores sembrados en la base de
desarrollo no las afectan. `limpiar-visual.sql` queda para dejar la base de desarrollo vacía.

### Dirección de diseño — lo que dijo Pablo, y lo que implica

> "Quiero que sea un sitio web profesional, que no sea un formulario, recuerda que es como
> una comunidad al fin al cabo."

Esto no es un retoque de estilos: cambia la forma de las pantallas. Lo que implica, como
propuesta que Pablo todavía no ha visto:

- **Un sitio, no una herramienta.** La portada presenta ZASS! y su idea ("Conecta con un
  toque"), muestra a la gente y los rubros, y la búsqueda es una parte de ella, no toda ella.
- **Las personas por delante de los campos.** Lo central de la comunidad son los prestadores
  y su reputación: una **ficha pública** que se lea como la página de un profesional (quién
  es, qué ofrece, dónde atiende, qué dicen de él, cómo contactarlo), no como una fila de una
  lista.
- **Editar tu perfil es editar tu ficha.** CU-03 paso 12 ya dice "ve su ficha tal como la ve
  el público". La pantalla del prestador no es un formulario por secciones: es **su propia
  ficha, editable en el sitio**, con lo que falta para publicar como una guía de
  incorporación que va completándose, no como una lista de errores.
- **Tono de comunidad.** El texto habla de personas y de cercanía, no de "registros" y
  "entradas". Hoy los mensajes son los de un sistema.

**Lo que no se sabe y conviene preguntarle a Pablo:** qué referencia visual tiene en mente
(¿un directorio estilo mercado, un sitio de comunidad con presencia de los prestadores,
algo más sobrio y corporativo?), si habrá fotos de los prestadores desde el primer día (hoy
no existe subida de archivos, y una comunidad sin caras se ve vacía), y si la comunidad
incluye también al buscador como miembro con perfil. Sin eso se puede avanzar con una
propuesta razonable, pero es una apuesta.

### El prototipo de diseño (enviado a Pablo el 7-nov-2026)

`diseno/prototipo-comunidad.html`: un HTML estático con tres pantallas (portada, ficha pública y
**Mi ficha**), datos de ejemplo y la marca de siempre. Se abre en cualquier navegador. Las capturas
quedan en `registros/capturas/diseno-*.png`.

**Las decisiones, con su razón:**

- **La búsqueda es una frase que se completa**: *"Busco a alguien de [corretaje] en [el Centro]"*,
  con dos `<select>` reales con aspecto de pastilla, sobre un **amarillo de pared a pared**. Es el
  único riesgo estético: la marca es amarilla y casi todos los directorios son blancos y azules.
  Sigue siendo accesible (son selectores nativos con su etiqueta) y no es un formulario con campos.
- **Una mano de tarjetas** en la portada: una persona no es un barrio, así que la tarjeta de
  Marcela Soto va con otras dos de gente distinta asomando por detrás. Dice "comunidad" mejor que
  una frase, y "Conecta con un toque" aparece como lo que es: la tarjeta que te da alguien.
- **Los barrios son un índice por sector**, con cuánta gente hay en cada uno, en columnas que se
  acomodan solas (los sectores tienen alturas muy distintas y una rejilla dejaba uno solo en una
  segunda fila). **Los oficios son filas grandes**, no tarjetas iguales.
- **Mi ficha es la ficha pública con la edición encima**, no una página de formularios: la
  descripción se edita *sobre* la ficha (con el contador real de 1500), las secciones vacías son
  invitaciones con borde punteado, y la guía lateral va completándose en vez de listar errores.
- Montserrat y la paleta de la marca sin tocar. Lo único que se agrega es el verde del sello
  "Verificado", que ya existía en la aplicación, y un amarillo suave para las invitaciones.

**Lo que se midió** (con Playwright, no solo mirando): sin desborde horizontal a 390 px, todos los
objetivos táctiles de al menos 44 px, Montserrat descargada de verdad (500, 600, 700 y 800) y sin
errores de consola. **Y encontró tres fallos míos**: un `padding` en una clase que **pisaba los
márgenes laterales** de otra (la ficha quedaba pegada a los bordes del celular: es justo el fallo de
especificidad CSS que advierte la guía de diseño), el logo y un enlace que medían 34 y 22 px, y una
tarjeta que era un `<a>` (inline) y rompía el apilado fuera de una rejilla.

**LO QUE EL PROTOTIPO MUESTRA Y TODAVÍA NO EXISTE.** Esto importa: el sitio real no puede
afirmar nada que no haga.

| El prototipo dice o muestra | Estado real |
|---|---|
| "Con títulos revisados por una persona" y el sello Verificado | **MOD-02 Credenciales no existe.** Nadie puede emitir un sello hoy |
| "Reseñas de vecinos que los contrataron", promedio y citas | **MOD-11 Reseñas no existe**, y depende de MOD-06 Agenda (solo opina quien tuvo una cita) |
| "Conecta con un toque": la tarjeta NFC | **MOD-12 no existe**, y falta resolver la pregunta 4 de la sección 9 |
| Cuántas personas hay por barrio y por oficio | **No hay endpoint.** API-04 devuelve zonas, no conteos. Hace falta uno público y agregado, con el mismo criterio de "visible" que la búsqueda |
| Fotos de las personas | **No existe subida de archivos.** El prototipo usa monogramas, que además es lo que se vería sin foto |
| "Subir un documento" en Mi ficha | Depende de MOD-02 |
| Nombre del rubro "Arquitectura y construcción", "Técnicos y oficios" | Existen en la base con esos nombres |
| WhatsApp, llamar, correo y sitio web de la ficha | **Sí existen**: son los campos de contacto del perfil (API-18/19), con el formato de móvil chileno validado |
| "Su dirección no se publica", "a 2 km de ti" | **Sí es cierto** (RN-S3-02 y RN-S3-03) |
| "Al publicar empiezan tus 30 días de prueba gratis" | **Sí es cierto** (MOD-13, probado de punta a punta) |

**Al construirlo de verdad, el texto de la portada no puede prometer lo primero de la tabla.** Las
frases sobre títulos revisados y reseñas de vecinos solo entran cuando MOD-02 y MOD-11 existan; hasta
entonces la portada tiene que hablar de lo que sí hay (gente de tu barrio, con nombre, que atiende
donde tú estás, y a la que le escribes directo).

**Orden propuesto para construirlo**, cuando Pablo lo apruebe: (1) la ficha pública, que es la pieza
central y **ya tiene todo el backend que necesita** (API-05 completa, con servicios y rubro); (2) Mi ficha, que reutiliza la ficha y ya tiene toda la
capa de datos hecha; (3) la portada, que sí necesita el endpoint de conteos; (4) rehacer la búsqueda
con este lenguaje.

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
| `Zass.Aplicacion.Pruebas` | **372** | Las reglas, sin base de datos: RN-S1-01 a RN-S1-08, RN-S3-02 a RN-S3-08, RN-S5-11, CC-02, y MOD-03, MOD-04 y MOD-05 completos |
| `Zass.Integracion.Pruebas` | **310** | Argon2id y AES-GCM reales, el contenedor, PostgreSQL real, y la API por HTTPS |
| `frontend` (Vitest) | **174** | Cliente HTTP y pantallas, con `fetch` simulado |
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

## 3 bis. Correo transaccional real (agente/correo)

**Qué hay.** Los avisos de verificación de correo y recuperación de contraseña ya se envían de verdad
(antes solo se encolaban en TBL-27). Sin columnas nuevas ni migraciones: `programada_para` hace de
«próximo intento» y de reserva, y `intentos`/`ultimo_error` ya existían.

- `TrabajoDeCorreo` (BackgroundService) llama a `ProcesadorDeCorreo`, que reserva un lote con
  `UPDATE ... WHERE id IN (SELECT ... FOR UPDATE SKIP LOCKED) RETURNING id` (estado `Enviando`, reserva de
  120 s: si una réplica muere, otra retoma el aviso). Seguro con varias réplicas: hay prueba con dos
  trabajadores y 40 avisos, un solo envío por aviso.
- Fallo del proveedor: 5xx, 408, 425, 429, timeout y error de red se reintentan con espera 1 min, 2, 4... (tope 1 h);
  otro 4xx queda `Fallida` sin reintentar. Máximo 5 intentos (`Zass:Correo:MaximoIntentos`).
- Puerto `IEnviadorDeCorreo`; `EnviadorBrevo` (API v3) y `EnviadorResend`, por HTTP.
- Higiene del token: la carga del aviso es JSON `{"token":"..."}` (`CargaDeAviso`). Al enviarse (o anularse) pasa a
  `{"purgado":true}`; un aviso fallido se purga 24 h después del fallo. Asunto fijo, sin token ni nombre.
- Logs: correo enmascarado (`a***@dominio.cl`), nunca token, clave ni cuerpo de respuesta del proveedor.
- Si el usuario ya verificó su correo o la cuenta se eliminó, el aviso se anula sin enviar.
- Pedir recuperación con un correo inexistente no encola nada y responde igual (prueba por HTTP).
- Límite conocido: entrega «al menos una vez». Si el proveedor aceptó el correo y la base falla justo después, el aviso
  se reenvía al vencer la reserva.
- Un aviso pendiente (por ejemplo, sin `UrlPublica`) conserva el token en claro hasta que sale o se anula; no hay caducidad
  automática de avisos pendientes.

**Configuración (variables de entorno en Render; en Cloudflare solo si la API corre allí).**

| Variable | Valor |
|---|---|
| `Zass__Correo__Proveedor` | `Brevo`, `Resend` o `Ninguno` (por omisión: no envía, los avisos quedan pendientes) |
| `Zass__Correo__ApiKey` | clave de API del proveedor (secreto; nunca en el repositorio) |
| `Zass__Correo__Remitente` | dirección de un dominio verificado, p. ej. `no-responder@zass.cl` |
| `Zass__Correo__NombreRemitente` | opcional, por omisión `ZASS!` |
| `Zass__UrlPublica` | `https://zass.cl`; sin ella no se envía nada y se registra una advertencia |

Con proveedor distinto de `Ninguno` y sin clave o sin remitente, la aplicación se niega a arrancar con un mensaje claro.
Ajustes opcionales: `Zass__Correo__MaximoIntentos`, `EsperaBaseSegundos`, `TamanoDeLote`, `SondeoSegundos`, `PurgaDeFallidasHoras`.

**Límites del plan gratuito.** Brevo y Resend tienen cupos diarios/mensuales en su plan gratuito: verifícalo en la página del proveedor.
La cuota agotada suele llegar como 429 (se reintenta con espera) o como un 4xx del proveedor (queda fallida): revísalo al elegir.

**Verificar el dominio remitente (SPF/DKIM).** Sin esto los correos caen en spam o el proveedor los rechaza.
1. En el panel del proveedor, agrega el dominio (p. ej. `zass.cl`) en la sección de dominios/remitentes.
2. El proveedor muestra los registros DNS que debes crear (SPF como TXT, DKIM como TXT o CNAME, a veces DMARC). Copia exactamente los valores que te muestre.
3. Créalos en el DNS donde está `zass.cl`, espera la propagación y pulsa «verificar» en el panel.
4. Usa como `Remitente` una dirección de ese dominio. Si ya existe un SPF, no crees un segundo: se combinan en un único registro.

**Pruebas:** ver el informe de la sesión; todo en `backend/pruebas/Zass.Integracion.Pruebas/Correo/` (HttpMessageHandler falso, sin red).

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

### Lo primero, al retomar (estado al cierre del 8-nov-2026, noche)

1. **Los cuatro agentes NO están corriendo.** Se cayeron al arrancar por el límite de 5 horas. Ver
   «Segunda tanda de agentes» más arriba: dice qué dejó cada uno y por qué conviene relanzar con
   **dos agentes, o cuatro sobre Sonnet**, no cuatro sobre Opus.
2. **Hay commits sin subir en `main`.** Pablo los sube con GitHub Desktop. Comprobar con
   `git log --oneline origin/main..main`.
3. **`main` está en verde y limpio:** 381 aplicación + 15 arquitectura + 440 integración, más 283
   de interfaz. Es un buen punto de partida, y ya trae la API lista para producción, el envío real
   de correos y el SEO.
4. Lo que sigue bloqueado por Pablo, y solo por él: **precio y nombre de los planes**, y **pasarela
   simulada o esperar a Transbank, Flow o Mercado Pago**.


```
HECHO   MOD-01 Identidad        dominio, API y pantalla
HECHO   MOD-13 Suscripciones    7-nov-2026
HECHO   MOD-03 Catalogo         7-nov-2026, con 72 pruebas nuevas
HECHO   MOD-04 Geolocalizacion   7-nov-2026, con 64 pruebas nuevas

HECHO   MOD-05 Busqueda          7-nov-2026, con 74 pruebas nuevas
                                 (falta BuscarAgregadoPorZona, ver su seccion)

1. HECHO (7-nov-2026): SITIO MULTIPAGINA PROFESIONAL Y DE COMUNIDAD, con verificar
   correo y recuperar contrasena (ver "El sitio multipagina"). Lo que sigue pendiente de este
   paso esta en su tabla "Lo que NO esta hecho": correos reales, foto, dominio zass.cl.
   Lo de abajo es el historial de la decision, ya cumplida.
   - DIRECCION DE PABLO (7-nov-2026): "quiero que sea un sitio web profesional, que no
     sea un formulario, recuerda que es como una comunidad al fin al cabo". Aplica a TODO
     lo que se construya de aqui en adelante, y obliga a rehacer lo que ya existe
   - Lo que ya existe y NO cumple: la busqueda es una tarjeta con campos y una lista (un
     formulario con resultados), y la portada es esa tarjeta sola. La pantalla del perfil
     del prestador iba a ser una sucesion de formularios, y se PARO antes de construirla
   - Lo que si se conserva, porque es independiente del diseno: toda la capa de datos
     (api/catalogo, funciones/perfil/servicio y validacion, 123 pruebas) y el backend
   - PROPUESTA HECHA Y ENVIADA A PABLO: diseno/prototipo-comunidad.html (portada, ficha
     publica y Mi ficha). Falta su reaccion ANTES de construirla de verdad en React
   - Ver "Direccion de diseno" mas abajo para lo que esto significa
   - Poner la atribucion a OpenStreetMap en el pie del mapa desde el dia uno
   - Verificar correo (API-14) y recuperar contrasena (API-15) van con estas

4. MOD-02 Credenciales (primera parte HECHA: declarar, listar, eliminar; falta MOD-08)
   -> MOD-06 Agenda -> MOD-11 Resenas -> MOD-12 Tarjetas
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

**CANCELADO (8-nov-2026, decision de Pablo): el documento de postulacion + municipio ya no va.**
Con eso, `BuscarAgregadoPorZona` pierde su motivo principal (era el dato que se le entregaba al
municipio) y baja de prioridad. No se borra: sigue siendo el paso 13 de CU-08 y esta en el documento.

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
4. ~~Qué pasa con la ficha pública por enlace directo y por tarjeta NFC cuando la
   suscripción vence.~~ **RESPONDIDA el 8 de noviembre de 2026: la tarjeta SÍ abre la ficha,
   con un aviso, pero SIN los botones de contactar ni de agendar.** Escrita en el documento
   como **RN-S6-10** en `docs/01-casos-de-uso/subsistema-6-tarjetas-nfc.md`, distinguida de
   RN-S6-09 (que es el perfil despublicado por el propio prestador, un caso distinto). El
   sitio se regeneró y se comprobó que la regla llegó al HTML. **Ya no bloquea MOD-12.**

   El razonamiento que quedó registrado, por si alguna vez se reabre:
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
| **Las pruebas de integración tienen su propia base de datos, `zass_pruebas`** | La crea y la migra sola la primera vez (las migraciones traen roles, plan, rubros, servicios y zonas), y **se niega a correr contra `zass`**, la de desarrollo: lanza un error en vez de escribir y borrar filas ahí. Se comprobó con 7 prestadores publicados en `zass`: las 218 pasan, y apuntarlas a `zass` falla en 2 ms con un mensaje claro. Antes compartían la base de desarrollo, y cualquier dato de ejemplo hacía fallar las pruebas de la búsqueda: hubo que limpiar a mano varias veces. Se puede cambiar con `ZASS_CADENA_PRUEBAS` (no con `ZASS_CADENA_CONEXION`, que es la de desarrollo). Las de HTTP borran por correo al terminar; si una se interrumpe puede dejar filas `api-*@zass.local` en `zass_pruebas`, no en `zass` |
| **Nunca correr DOS suites de integración a la vez** | Pasó el 8-nov: se lanzó una en segundo plano y, creyendo que había terminado, otra en primer plano. Las dos fueron contra `zass_pruebas` y una prueba de búsqueda se puso roja. **No era un fallo del código**: la de segundo plano terminó en verde. Si aparece un rojo raro en integración, lo primero es comprobar que no haya otra suite corriendo (`Get-Process dotnet, testhost`) |
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

---

## Nota de la rama `agente/seo` (7-nov-2026): SEO y páginas legales

**Hecho (probado, sin Cloudflare real):** Function `/prestadores/:slug` con meta por ficha, `/sitemap.xml` (+ índice si pasa de 50 000), `robots.txt`, `GET /api/prestadores/mapa` (API-64) en el backend, hook `useMeta`, imagen `public/og-zass.png`, y las páginas `/terminos` y `/privacidad` enlazadas desde el pie y desde el registro. Detalle de despliegue en `frontend/DESPLIEGUE-CLOUDFLARE.md`.

**PENDIENTE PARA PABLO: las páginas legales son un BORRADOR, no asesoría legal.**

1. Llenar en `frontend/src/paginas/legal.ts`: `RESPONSABLE` (razón social y RUT), `CORREO_PRIVACIDAD` (casilla para ejercer derechos de la Ley 21.719), `PROVEEDORES` (alojamiento, base de datos, correo) y `PLAZO_REGISTROS_DE_SEGURIDAD`. Mientras estén entre corchetes se ven resaltados en la página.
2. **Decidir el plazo de los registros de seguridad (IP y fecha de acciones, tabla de auditoría y sesiones) y construir su borrado: hoy el sistema NO los borra solo.**
3. Llevar `Terminos.tsx` y `Privacidad.tsx` a un abogado. Cuando los apruebe, cambiar `BORRADOR_LEGAL` a `false` en `legal.ts` (desaparece el aviso «Borrador: pendiente de revisión legal») y actualizar `ULTIMA_ACTUALIZACION`.
4. Los textos dicen que hoy no hay cobro de la mensualidad, ni credenciales revisadas, ni pagos ni agenda: si eso cambia, hay que actualizarlos.
5. Anotado para el backend: reservar el slug `mapa` al generar slugs (choca con `/api/prestadores/mapa`).

---

## Publicación en línea (8-nov-2026, noche) — QUÉ ESTÁ Y QUÉ FALTA

**En línea y comprobado desde fuera:** sitio en Cloudflare Pages (`https://zass-59w.pages.dev`, proyecto `zass`),
API en Render (`https://zass-api.onrender.com`, servicio `zass-api`, plan gratis: se duerme), base en Neon
(US East, PostGIS 3.6). Las 13 migraciones aplicadas y consultadas en la base (362 zonas, 1 plan, 103 índices).
`/api/salud/lista`, `/api/zonas`, `/api/buscar` responden 200 **a través de Cloudflare**; registro e inicio de
sesión probados contra la base real (la cuenta de prueba se borró).

**Secretos:** viven en `C:\Users\pdani\zass-secretos\` (fuera del repo y de OneDrive) y en el panel de Render.
La clave AES de direcciones **debe tener copia en un gestor de contraseñas**; si se pierde, no se descifran.
La contraseña de Neon y el token de Cloudflare pasaron por el chat: **hay que rotarlos**.

**Lo que se midió y NO es lo que decía la guía:** en Render, `Zass__SaltosDeProxy` =3 y `Zass__ProxiesDeConfianza`
= rangos de Cloudflare + `::1`, `127.0.0.1`, `10.0.0.0/8` (el balanceador llega desde 10.x). `render.yaml` aún dice 2.
**Defecto abierto:** por el sitio de Cloudflare la API ve una IP de Cloudflare, no la del visitante (la función de
Pages → Render pierde `X-Forwarded-For`). Directo a Render sí registra la IP real e ignora las falsas. Arreglo:
cabecera propia desde la función + `ForwardedForHeaderName` en la API, con prueba por HTTP. Sin riesgo de seguridad,
pero el límite de peticiones se cuenta por salida de Cloudflare.

**Dominio zass.cl:** comprado en NIC Chile; zona agregada a Cloudflare (nameservers `doug`/`monroe`.ns.cloudflare.com
guardados en NIC); registros CNAME `@` y `www` → `zass-59w.pages.dev` creados por Pablo; dominios añadidos al
proyecto Pages. **A la espera de que NIC propague** (zona "pending"). Al activarse: comprobar HTTPS, www, rutas, API, sitemap.

**Falta:** correos reales (Brevo/Resend; sin eso nadie verifica cuenta), Render gratis se duerme, textos legales en
borrador, datos de demostración, y decidir pasarela/precio. Los commits locales siguen sin subir a GitHub.

### Dominio zass.cl ACTIVO (8-nov-2026, noche) — comprobado
- Zona Cloudflare `active`; 1.1.1.1 y 8.8.8.8 devuelven los nameservers `doug`/`monroe`.
- Pages: `zass.cl` y `www.zass.cl` en `active`, certificado `active` (emisor Google Trust Services, vence 6-feb-2027).
- HTTPS real, 200 y verificación de certificado correcta: `/`, `www`, `/buscar`, `/api/salud/lista`, `/api/zonas` (datos de Neon),
  `/robots.txt`, `/sitemap.xml` (todas las URL son `https://zass.cl`, ninguna `pages.dev`).
- Resend: dominio `zass.cl` **verified** (DKIM, SPF `rsend` y `send` verified).
- **NO comprobado:** envío real de un correo; las 5 variables de correo aún NO están cargadas en Render
  (`Zass__Correo__Proveedor=Resend`, `__ApiKey`, `__Remitente=no-responder@zass.cl`, `__NombreRemitente=ZASS!`,
  `Zass__UrlPublica=https://zass.cl`). Sin ellas nadie recibe el correo de verificación.
- Sigue abierto: IP del visitante vista como IP de Cloudflare; rotar contraseña de Neon, token de Cloudflare y clave de Resend.

---

## Una sola cuenta y contacto con cuenta (8-nov-2026, noche) — HECHO Y PROBADO LOCAL, SIN PUBLICAR

Decisión de Pablo: **buscar y ver fichas no exige cuenta; contactar sí**, y **una sola cuenta** sirve para buscar y para ofrecer.

- **API-05 (ficha pública):** sin sesión el contacto (teléfono, WhatsApp, correo, sitio) sale **vacío** y con `contactoRequiereCuenta: true`; con sesión sale completo. Se quita en el servidor (`EndpointsIdentidad.cs`), no solo en pantalla. La respuesta lleva `Cache-Control: private, no-store` y `Vary: Cookie`.
- **CS-04 `POST /api/mi/activar-prestador`:** agrega el rol de prestador a la cuenta existente, crea el perfil en borrador, es idempotente y renueva la sesión para que los roles se vean al instante. `ServicioRegistro.ActivarPrestadorAsync`.
- **Pantalla:** el registro ya no pregunta el rol (queda buscador; desde «Para profesionales» parte como prestador). Nueva página `/ofrecer-servicios` y enlace «Ofrecer mis servicios» en la cabecera. La ficha muestra «Crea tu cuenta gratis para contactar» y vuelve a la misma ficha tras entrar.
- **Comprobado:** 654 + 16 + 597 pruebas de backend y 326 de interfaz en verde. **Se mutó** el filtro del contacto (cayeron 2 pruebas) y el caso de uso de activar (cayeron 2).
- **NO hecho / ojo:** el contacto sigue sin registrar quién contactó a quién (TBL-21), así que contactar por WhatsApp no habilita reseña; solo agendar lo hace. **Esto NO está en producción**: el backend tiene que desplegarse ANTES que el frontend, porque `Ofrecer mis servicios` llama a un endpoint nuevo. Falta además actualizar el documento de especificación (CS-04 es nuevo y API-05 cambió).
- **Rendimiento en celular:** se publicó (desactivar blur, orbes y brillos en pantallas chicas). No se pudo medir en un teléfono real.

---

## Agenda del prestador — 8-nov-2026, noche (SIN COMMITEAR ANTES DE ESTA NOTA)

`/mi-agenda`: disponibilidad por servicio + bloqueo de días, con 21 pruebas nuevas (348 de interfaz en total, en verde).
**No se vio en un navegador todavía.** Falta endpoint para listar/quitar bloqueos (hoy no se pueden deshacer).
Detalle y decisiones en `TRASPASO.md` («ÚLTIMA SESIÓN»). Diseño y plan de Codex en `docs/superpowers/`.


---

## Sesión de decisiones y traspaso a Joaquín (11-nov-2026)

Trabajo en paralelo acordado entre Pablo (infraestructura, revisión, decisiones) y Joaquín (código principal). Reglas de revisión documentadas en `CONTRIBUIR.md` y `CLAUDE.md` bajo esquema N1/N2/N3.

### Tareas de infraestructura y documentación cerradas (Pablo)

- **P1 (Copia diaria en GitHub Actions):**
  - Workflow `.github/workflows/copia-base-de-datos.yml` actualizado a `postgres:18` (coincide con Neon 18.6).
  - Secretos `NEON_URL_COPIAS` y `FRASE_COPIAS` configurados y probados en GitHub Actions (PR #3 mergeado).
  - Corrida manual #4 en `main` verde (18 s).
  - Descifrado de artefacto probado localmente con éxito (PGDMP, 106.319 bytes, AES256).
  - Frase de descifrado respaldada fuera del PC.
  - Pendiente menor: verificar restauración con pg_restore 18 en base local cuando se requiera.

- **P2 (Proxies de Render):**
  - `render.yaml` actualizado a `Zass__SaltosDeProxy: "3"` (coincide con el servicio en producción).
  - `DESPLIEGUE-API.md` y `frontend/DESPLIEGUE-CLOUDFLARE.md` actualizados con el rango privado `10.0.0.0/8`, `::1` y `127.0.0.1` (PR #2 mergeado).

- **P4 (Duda de MOD-12 NFC resuelta):**
  - La pregunta 4 de la sección 9 ya estaba respondida (8-nov-2026): prestador sin suscripción activa mantiene ficha con aviso y sin botones de contacto/agendar (RN-S6-10). Actualizado en `TRASPASO.md` (PR #2 mergeado).

### Decisiones de producto tomadas por Pablo (11-nov-2026)

| Tema | Decisión acordada | Impacto técnico |
|---|---|---|
| **Fundador que cancela o falla cobro** | Tiene **14 días de plazo** desde que vence el período pagado (`vigente_hasta`). Si vuelve dentro de esos 14 días, recupera su precio de $5.990. Si no vuelve, el cupo **desaparece para siempre** (no vuelve a la bolsa de los 20). Si cancela o falla el primer cobro *antes de haber pagado ni una vez*, el cupo sí se libera para otra persona. | Requiere migración N3 para marcar cupo fundador como definitivamente perdido tras 14 días. Términos y condiciones deben reflejar la excepción. |
| **Gracia por cobro fallido** | **3 días** de gracia con servicio activo mientras la pasarela reintenta (corren dentro de los 14 días, no se suman). | Revisar constantes/configuración de gracia (baja de 5 a 3 días). |
| **NoAsistio en citas** | **Modelo completo:** cada parte solo puede acusar a la otra (prestador acusa «cliente no vino», cliente acusa «prestador no vino»). Si faltó el prestador, el cliente **sí puede reseñar**. Si faltó el cliente, **no hay reseña**. Si se contradicen, la reseña se bloquea de inmediato y la cita va a la cola interna de revisión. | Requiere migración N3 (columna para registrar quién faltó). |
| **Contacto directo (sin cita)** | Se agrega botón **«ya me atendió»** que marca el cliente desde su vista. La reseña queda rotulada «contacto directo, sin cita». | Nuevo endpoint y ajuste en modelo de reseñas. |
| **MOD-12 (Tarjeta NFC)** | Al tocar la tarjeta, abre la **ficha completa** del prestador directamente. Sin migración nueva de base de datos. | Caso de uso y endpoints de redirección/lectura. Tarea J4 de Joaquín. |
| **Prueba gratuita de 30 días** | **Única por prestador.** Paso 1: por cuenta con correo verificado. Paso 2 (a futuro): verificación por número de celular (SMS/WhatsApp). | Sin cambios inmediatos en base para el paso 1. |

### Reparto inicial de tareas a Joaquín

- **J1 (N3):** `correo-lote-estricto` (reproducir y corregir intermitencia en prueba `Dos_reservas_simultaneas_reciben_filas_distintas`).
- **J2 (N2):** `bloqueos-quitar` (endpoints y UI para listar y quitar bloqueos de agenda propios).
- **J3 (N2):** `revision-visual-agenda-ficha` (revisión visual en celular y escritorio de `/mi-agenda` y `/mi-ficha`).

---

## 11-nov-2026 — medición y correcciones

**Defecto de IP: la causa que decían `ESTADO.md:1368` y `TRASPASO.md:154`/`:197` es FALSA.** La API está
ella misma detrás de Cloudflare (`zass-api.onrender.com` → CNAME a
`gcp-us-west1-1.origin.onrender.com.cdn.cloudflare.net`; responde `Server: cloudflare` + `CF-RAY`), y
Cloudflare **añade** a `X-Forwarded-For`, no la sobreescribe. Luego la cadena trae **≥3 entradas** y la
hipótesis principal es que `Zass__SaltosDeProxy=3` se queda corto. Prueba pendiente, **sin código**:
subirlo en el panel de Render y entrar desde datos móviles. El arreglo que proponían los documentos
(cabecera propia + `ForwardedForHeaderName`) puede ser innecesario.

**Copias:** el ciclo volcado→cifrado→descifrado→`pg_restore` quedó probado entero en local (PostGIS y
columna generada incluidas, base idéntica hasta el md5 de las filas), pero con herramientas **16.4**.
El volcado real lo hace `pg_dump` **18**, y `pg_restore` 16.4 lo rechaza (probado: cabecera 1.17 →
«versión no soportada»). Una restauración de emergencia **NO se puede hacer hoy en este PC**: falta un
cliente 18 (Docker, binarios portables o job de Actions). Además `--no-owner --no-privileges` en el
`pg_dump` con `--format=custom` **no hacen nada**: van en el `pg_restore`.

**Pruebas, corridas y en verde:** 674 aplicación + **614** integración + 16 arquitectura + 373 frontend
en 28 archivos. `CONTRIBUIR.md` dice 613 de integración: es **614**. `CLAUDE.md:3` dice PostgreSQL 18 y
el entorno local es **16.4** (producción en Neon sí es 18).

### Otras lecturas verificadas ese día

- **J2 ya está en `main`** (`c0022e5`, `dc242a3`): los endpoints de listar y quitar bloqueos existen, así
  que `ESTADO.md:1407` y `TRASPASO.md:42-43` («falta endpoint para listar/quitar bloqueos») están
  caducados. Joaquín lo verificó con mutación en `mut-bloqueos-dueno` (`19b50aa`, sin mergear). Ojo a su
  aviso, confirmado: el doble de pruebas filtra por dueño por su cuenta, así que la unitaria no ve esa
  mutación; solo la protege la de integración.
- **J4 = API-54**, `GET /t/{segmento}` (grupo de tasa P), según `api-rest.md` de la documentación. La URL
  impresa es `zass.cl/t/XXXX-XXXX`. **Nivel N3 sin migración**: el modelo ya existe entero (`Tarjeta`,
  `EscaneoTarjeta`, tablas `tarjetas`/`escaneos` en la migración inicial, y `Tarjeta.Resolver()` ya
  escrita y sin una sola prueba que la ejecute). Es N3 por `Program.cs`, infraestructura y seguridad.
  API-55 a API-59 son otras tareas. Rama propuesta: `tarjeta-abre-ficha`.
- **RN-S6-10 no está implementada en ningún sitio**, y hay un hueco relacionado: `BloquesLibresAsync`
  (`ServicioAgenda.cs:268-296`) **no mira la suscripción**, solo que el perfil esté publicado. Hoy un
  prestador impago muestra calendario con horas y el error salta recién al confirmar (`:364`).
- **La agenda no emite ninguna notificación:** `ServicioAgenda` no recibe `INotificaciones` (solo
  identidad, reseñas y suscripciones la usan), y no hay plantilla de correo para citas. La opción B de
  NoAsistio («el prestador responde en 72 h») hoy **no tiene por dónde avisarle**.
- **La elegibilidad para reseñar está escrita en cuatro sitios y solo uno está vivo:**
  `ReglasDeResenas.EvaluarContacto` (rechaza `NoAsistio`). `HuboCitaRealizadaAsync` la acepta, pero solo
  se alcanza desde pruebas; y los dos `HabilitaResena()` (`Agenda/Entidades.cs:150`,
  `Reputacion/Entidades.cs:33`) son código muerto, sin un llamador. No es un fallo en producción: es una
  mina para quien conecte cualquiera de los tres.
- **Verificado del cupo fundador:** `LiberarCupoFundadorAsync` suelta también un cupo **confirmado** (el
  `UPDATE` no filtra por `reservado_hasta`) y 3 pruebas lo fijan; la reserva dura 25 min y la prueba 30
  días; `Suscripcion.Cancelar()` **no tiene llamadores**; y la «gracia de 3 días» **no existe en el
  código** (cero coincidencias).
- `render.yaml` ya dice `Zass__SaltosDeProxy: "3"`, así que la nota de `:1366` («aún dice 2») está
  caducada. Lo que sigue mal es el comentario de `ProxiesDeConfianza.cs:30-39`, que razona la cadena sin
  contar el Cloudflare de delante de Render.
