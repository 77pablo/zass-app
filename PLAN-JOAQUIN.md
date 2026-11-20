# Plan de trabajo de Joaquín — al 20 de noviembre de 2026

ZASS! abre el **lunes 23**. Este archivo es tu cola y tu permiso para avanzar. Manda sobre
cualquier lista anterior (`TRASPASO.md`, `TRASPASO-12-NOV.md`, la versión vieja de este archivo en
la rama `plan-joaquin`): esas tienen cosas que ya están hechas.

**Si retomas desde cero, dile a Claude: «sigue con plan-joaquin de zass».** Él lee esto,
`ESTADO.md` y `CONTRIBUIR.md`, y sigue por la primera tanda sin terminar.

---

## 0. Actualización del 20 de noviembre — LÉELA PRIMERO

**JQ-9 ya no es tuya: la está programando Claude** (rama `archivos/jq9-fotos-documentos`). No la
empieces. Tu **JQ-7** está en el **PR #151**: si te piden cambios ahí, eso va antes que lo nuevo.

**Lo que hoy tiene dueño, y no tienes que tocar:**

| Qué | Quién |
|---|---|
| JQ-9: fotos y documentos (`IAlmacenDeArchivos`, `EndpointsArchivos.cs`, `FotoDePerfil.tsx`, la sección de documentos de `MisCredenciales.tsx`) | Claude |
| La pasarela Flow (`Pagos/`, `Pasarela.cs`) | Claude |
| El bloqueo de tarjetas tras 5 intentos (PR #149, `ServicioTarjetas.ActivarAsync`) | Otra IA |
| La limpieza de la prueba del día de Chile (`ApiAgendaPruebas.cs`) | Otra IA |
| Los borradores de interfaz #98, #103, #106, #107, #128, #130 y el #76 (eliminar cuenta) | Claude |

**Por qué esta cola:** el backend de todo lo INTERNO ya existe y está probado: la cola del
Verificador (API-29, 30, 31), la moderación de reseñas (API-52, 53), emitir tarjetas (API-59) y la
auditoría (API-61). **Pero ninguna pantalla usa `/api/interno`.** Hoy, para verificar un título o
retirar una reseña ofensiva, alguien tendría que tocar la base a mano. Sin estas pantallas, ZASS!
no se puede operar el día que abra.

Las reglas de siempre siguen valiendo (sección 2 y sección 4): mutación anotada en cada PR, CI en
verde, una migración a la vez y avisada. Un PR por tarea.

### JQ-11 — El panel interno y la mesa del Verificador · N3 · la base de las demás

1. **La puerta:** una sección `/interno` en el frontend, que solo se ve con el rol `verificador` o
   `administrador` (`usuario.roles`, igual que `MiFicha` mira `prestador`). Sin el rol, la ruta
   responde como una página que no existe, no como «prohibido»: no se anuncia que hay un panel. Con
   el rol, un enlace en el menú «Más». Fuera del índice de Google (`useMeta({ noindex: true })`).
2. **La mesa de verificación** (`/interno/verificacion`), con lo que ya existe:
   - la cola (API-29), la más antigua primero, con el prestador, la credencial, la entidad, si la
     entidad no está reconocida y si viene **sin documentos**;
   - **tomar** (API-30): muestra cuánto queda de la reserva de 30 minutos. Si otro la tomó, dilo
     claro (409);
   - los **documentos de respaldo**: la lista viene en `respaldo` de la credencial tomada, y cada
     uno se abre en una pestaña nueva con `GET /api/interno/credenciales/{id}/documentos/{doc}`
     (API-74). **API-74 llega con JQ-9**: construye contra ese contrato, y si JQ-9 no está en
     `main` cuando abras el PR, dilo en el PR;
   - **resolver** (API-31): verificar, rechazar (el motivo es obligatorio: los del enum
     `MotivoRechazo`, más el texto) o pedir antecedentes. Si viene sin documentos y se verifica,
     el texto mínimo es de 20 caracteres (lo exige la API: muestra su mensaje tal cual).
3. Pruebas de interfaz de todo lo anterior, y **una de navegador** (`frontend/pruebas-navegador/`)
   que recorra cola → tomar → abrir un documento → verificar, con un usuario con el rol puesto por
   base.

**Mutación mínima:** mostrar el panel sin el rol, y resolver un rechazo sin motivo.

### JQ-12 — Moderación de reseñas · N2

En el mismo panel, `/interno/reportes`: la cola de reportes abiertos (API-52, «sin los propios») y
resolver cada uno (API-53): **mantener o retirar, con motivo**. Muestra la reseña completa, quién
reportó (sin su correo) y el motivo del reporte. Después de resolver, sale de la cola sin recargar
la página. Pruebas, y la mutación: que se pueda mandar sin motivo.

### JQ-13 — Emitir tarjetas y consultar la auditoría · N3

Dos pantallas del panel, solo para `administrador`:

1. **Emitir tarjetas** (API-59): emite una tarjeta y **muestra el código de activación UNA sola
   vez**, con su QR y un botón para imprimir la etiqueta que va en el sobre. Una recarga NO lo
   vuelve a mostrar: la API no lo devuelve dos veces, y la pantalla tiene que dejarlo claro antes
   de cerrar («anótalo o imprímelo ahora»). Para el QR ya existe `componentes/tarjetas/matrizQr.ts`
   (sobre `qrcode-generator`): reúsalo, no agregues otra biblioteca.
2. **Auditoría** (API-61): buscar el rastro por entidad o por actor, paginado hacia atrás con el
   cursor que ya da la API. Solo se muestra lo que la API devuelve: nada de pedir columnas que no
   están.

**Mutación mínima:** que la pantalla de emitir guarde el código en `localStorage` (no tiene que
estar en ningún lado después de cerrar), y que un `verificador` sin `administrador` vea estas
pantallas.

### JQ-14 — Indicadores de la operación (API-60) · N2 · backend + pantalla

`GET /api/interno/indicadores` **no existe**: está en `api-rest.md` («tiempos de revisión, tasa de
rechazo, avisos fallidos»). Constrúyelo **sin migración**: todo sale de tablas que ya existen
(los `DbSet` `Verificaciones`, `Credenciales`, `Notificaciones`, `ReportesResena` y `Tarjetas`
de `ZassDbContext`).

- Mínimo: credenciales en cola y la más antigua; tiempo medio entre enviar a revisión y resolver
  en los últimos 30 días; tasa de rechazo; avisos (correos) fallidos en los últimos 7 días;
  reportes abiertos; tarjetas emitidas sin activar.
- Grupo de tasa **S**, solo para `administrador`, y su nombre en el inventario
  (`El_inventario_de_endpoints_es_el_que_esta_documentado`) y en `api-rest.md`.
- Pantalla `/interno/indicadores` con esos números: cifras, no gráficos.
- Pruebas de integración con datos armados a mano, donde cada cifra se pueda calcular de cabeza.
  **Mutación:** contar los rechazos de más de 30 días.

### JQ-15 — La foto en toda la web · N2 · chica · DESPUÉS de que JQ-9 esté en `main`

JQ-9 pone la foto en la ficha, en la búsqueda y en «Mi ficha». Quedan sin foto:

- **«Recién llegados» de la portada:** `/api/comunidad` no trae `urlFoto`. Agrégalo al DTO y pásalo
  a `<Monograma foto=…>`.
- **Las sugerencias de la barra de búsqueda** (`BarraDeBusqueda.tsx`, el `Monograma` de tamaño `p`).

Pruebas, y mirarlo en un navegador con una persona con foto y otra sin foto.

### JQ-16 — Borradores de credenciales abandonados (CU-05 4D.3) · N2 · DESPUÉS de JQ-9

«Un borrador sin actividad por 90 días se elimina junto con sus archivos, tras un aviso a los 83
días.» Hoy nadie lo hace, y con JQ-9 un borrador ya puede tener documentos guardados.

- Un trabajo del planificador (`Zass.Infraestructura/Fondo/`, mira cómo corren los que ya existen):
  a los 83 días encola el aviso (idempotente por clave: el mismo borrador no se avisa dos veces);
  a los 90 borra la credencial **y sus archivos** con `IAlmacenDeArchivos.BorrarAsync`, después
  de que la base confirmó (igual que `ServicioCredenciales.EliminarAsync`).
- «Actividad» = creación o último documento subido. Si eso no alcanza para decidirlo bien sin
  migración, **dilo en el PR y espera**.
- Pruebas con el reloj de prueba (día 82: nada; 83: un aviso; 84: el mismo aviso, no otro; 90:
  borrada y sin archivos en disco). **Mutación:** borrar sin pasar por el aviso.

Después de estas seis sigue **JQ-8** (métricas del prestador), como estaba.

---

## 1. Lo primero: lo que ya NO tienes que hacer

Tu cola vieja (cupo fundador, mes gratis, cupón, cobro) **está cerrada y fusionada en `main`**.
No la vuelvas a abrir. El camino del dinero existe entero: `/planes`, los 50 cupos, el mes gratis
de 30 días (RN-S7-03), el cupón y el cobro automático que dispara el planificador 7 días antes
del vencimiento.

Lo único que falta de ese carril es **la pasarela de verdad**, y no puede empezar hasta que Pablo
elija Flow, Transbank o Mercado Pago. No es tuyo todavía.

---

## 2. Tu carril, y de qué no tienes que pedir permiso

**Mientras estés en tu carril, decides, escribes, commiteas, pusheas y abres tu PR.** Lo único de
Pablo es apretar el botón de mergear.

| Es tuyo | No lo toques |
|---|---|
| Agenda y citas, reseñas, tarjetas NFC, perfiles, búsqueda, datos de demostración | Suscripciones, planes, pagos y pasarela (`Zass.*/Suscripciones/`, `Pagos/`, `EndpointsSuscripciones.cs`, la pantalla `/planes`) |
| Las pantallas de todo eso | Neon, Render, los secretos y los textos legales: son de Pablo |
| Tus propias pruebas, incluidas las de navegador | `ESTADO.md` (lo lleva la sesión principal) |

**Hay otra IA trabajando en paralelo.** Ahora mismo tiene estas seis cosas, así que déjalas
quietas: la frecuencia del workflow `despertar-api`, probar la restauración de la copia de la
base, las cabeceras de `frontend/public/_headers`, el barrido de textos en inglés que llegan al
usuario, las rutas estáticas del `sitemap.xml`, y el identificador de petición en el registro.

### Dos reglas duras

1. **Migraciones de EF: una a la vez, y avisas antes.** Hay **tres sin aplicar en producción**
   (`CuposFundadorA50`, `NoAsistioQuienFalto`, `CobroIntentosYCupones`). Si tu tanda necesita una,
   dilo en el PR y espera: aplicarlas en Neon es de Pablo.
2. **Una prueba que nunca se vio roja no prueba nada.** Antes de decir que algo funciona, rompe a
   propósito el código que acabas de escribir, comprueba que la prueba **cae**, deshazlo y anota
   en el PR qué mutación la tumbó. En este proyecto ya hubo pruebas verdes sobre pantallas rotas.

---

## 3. Tu cola nueva, en orden

### JQ-3 — Recordatorios de cita (24 h y 2 h) · N2

`TipoNotificacion` declara `Recordatorio24h` y `Recordatorio2h` desde hace días. **Nadie los
encola y no tienen plantilla**: `Plantillas.TienePlantilla` devuelve `false` para los dos, y hay
una prueba que lo fija a conciencia. Hoy la gente llega a su hora sin que nadie le recuerde nada,
y el prestador tampoco.

Qué tiene que quedar:

- El planificador A-09 los encola en la tanda diaria, junto a lo que ya hace.
- Plantillas para los dos, al cliente y al prestador, con el enlace a `/mis-citas`.
- **Que no se repitan**: la clave de cada aviso tiene que ser determinista, como ya se hizo con el
  cobro automático (`cobro-automatico:<id>:<fecha>`). Dos corridas el mismo día no pueden mandar
  dos veces el mismo recordatorio.
- **Reprogramar o cancelar tiene que apagar el recordatorio viejo.** Es el caso que se escapa
  siempre: avisar de una hora que ya no existe.
- Si descubres que hace falta una columna para marcar lo enviado, **párate y dilo en el PR** antes
  de escribir la migración.

Mira cómo lo hace el cobro automático en `ServicioSuscripciones` y `Planificador` antes de
inventar nada: el patrón ya está resuelto ahí.

### JQ-4 — Tarjetas NFC: el resto de S6 · N2, y es la tanda más grande

De todo el subsistema 6 solo existe **`GET /t/{segmento}`** (API-54), que abre la ficha. Falta el
resto: **API-55 a API-59**. El modelo ya está entero (`Tarjeta`, `EscaneoTarjeta`, las tablas
`tarjetas` y `escaneos` vienen desde la migración inicial), así que **esta tanda no necesita
ninguna migración**.

Qué tiene que quedar:

- Crear o asociar una tarjeta a tu perfil, listar las tuyas, darla de baja.
- **Registrar el escaneo** cuando alguien la usa, con su origen.
- El tope de tarjetas del plan (`MaxTarjetas`) respetado **en el servidor**, no solo en pantalla.
- La pantalla «Mis tarjetas» en el panel del prestador, con el enlace `zass.cl/t/XXXX-XXXX` y su
  QR de respaldo.
- **RN-S6-10 vigente**: si la suscripción venció, la tarjeta **abre la ficha con un aviso** pero
  **sin contactar ni agendar**. Ya está decidido; que tu código no lo contradiga.

Es la tanda que más se nota del producto: es lo que el prestador tiene en la mano.

### JQ-5 — Que contactar quede registrado de verdad · N2

El endpoint de contacto y la entidad `Contacto` **ya existen en el backend**, con
`OrigenContacto` (Ficha, Tarjeta, Búsqueda, Cita) y su canal. Pero **la ficha no los llama**:
cuando alguien toca WhatsApp, el teléfono o el correo, no se registra nada. Consecuencia concreta
y ya escrita en `ESTADO.md`: **contactar por WhatsApp no habilita reseña**; solo agendar lo hace.

Qué tiene que quedar:

- La ficha registra el contacto al tocar cada canal, con el origen correcto.
- La elegibilidad para reseñar lo tiene en cuenta **respetando la pieza que manda**:
  `ReglasDeResenas.EvaluarContacto`. Ojo, que la elegibilidad estuvo escrita en cuatro sitios y
  solo uno estaba vivo: no revivas los otros.
- Comprobado **en un navegador de verdad**, no solo en jsdom.

### JQ-6 — Datos de demostración creíbles y reproducibles · N1

Para abrir, la búsqueda no puede verse vacía ni de juguete. Hoy hay dos sembrados
(`sembrar-demo.sql` y `sembrar-visual.sql`) que no cuentan la misma historia.

Qué tiene que quedar: un sembrado **idempotente** (se puede correr dos veces sin ensuciar), con
prestadores chilenos creíbles repartidos por comuna y oficio, con reseñas, horarios y fotos
coherentes, y que los dos archivos no se contradigan. Nada que se note de prueba («Juan Pérez 1»,
«asdf»). Y que sirva para mirar la búsqueda, la ficha y la agenda sin crear nada a mano.

### JQ-7 — El camino del pago, visto en un navegador · N2

El cupo fundador y el cobro están fusionados y probados, **pero casi nadie los ha mirado en
pantalla**. Hay un guion `frontend/pruebas-navegador/cupo-fundador.mjs`: amplíalo hasta recorrer
el camino completo con la pasarela simulada — `/planes`, el contador «quedan N de 50», tomar el
cupo, el cupón, inscribir el medio de pago, y los cuatro estados de la pantalla (incluidos el cupo
agotado y la reserva vencida a los 25 minutos).

Informa de lo que veas aunque no lo arregles tú: en este proyecto mirar las pantallas por primera
vez ha encontrado nueve defectos de una sentada.

### JQ-9 — Subir fotos y documentos · N3 (es la que más vale de todas)

> **TOMADA POR CLAUDE (Pablo, 20-nov).** No la hagas: ver la sección 0.

Hoy **no se puede subir ni un archivo**. `MiFicha` lo dice con todas sus letras: «Por ahora no
puedes subir tu foto, tus títulos ni tu dirección», y `UrlFoto` **nunca se escribe en ninguna
capa**. Dos consecuencias: un directorio de profesionales sin caras se ve abandonado, y la
verificación de credenciales existe en el backend (`POST /mi/credenciales`, `/enviar`) pero
**nadie puede adjuntar el título** que habría que verificar.

**No esperes a Pablo para empezar.** Constrúyelo detrás de un puerto (`IAlmacenDeArchivos` en
`Zass.Aplicacion/Puertos/`, igual que se hizo con la pasarela), con dos implementaciones: una
local para desarrollo y pruebas, y otra contra almacenamiento compatible con S3 — el destino
previsto es **R2 de Cloudflare**, y las credenciales las pone Pablo cuando la pieza esté. Así el
código entero se escribe, se prueba y se mergea sin depender de que exista la cuenta.

Qué tiene que quedar:

- Subir y reemplazar la **foto de perfil**, con redimensionado en el servidor: nadie sube una foto
  de 8 MB desde el celular, y el navegador no es quien debe decidir eso.
- Adjuntar **documentos a una credencial**, que es lo que desbloquea S2 entero.
- **Validación en el servidor, no en el formulario**: tipo real del archivo leyendo sus primeros
  bytes (no la extensión ni el `Content-Type` que manda el cliente), tope de tamaño, y nombre de
  archivo generado por nosotros — jamás el que venía puesto.
- **La foto es pública; el título NO.** Un documento de credencial es dato personal sensible: va
  en almacenamiento privado, se sirve con enlace firmado y de corta vida, y no aparece en el
  sitemap ni en ninguna respuesta pública. Si esto queda mal es una filtración, no un defecto.
- Borrar la foto, y que borrar la cuenta se lleve los archivos.

Es **N3** por seguridad e infraestructura: describe el PR entero para que Pablo lo revise
completo. Si necesitas una migración, **dilo en el PR y espera**.

**Agregado el 17-nov: el botón «Enviar a revisión» ya está empezado, y va CON esta tanda.**
Un agente lo dejó a medias el 13-nov en la rama `web/panel-prestador` (commit `2ab306c`):
`enviarCredencialARevision` (API-27) en `funciones/perfil/servicio.ts` y el botón, con su error
junto a la credencial, en `MisCredenciales.tsx`, más 116 líneas de pruebas sin terminar.
**Pablo decidió el 17-nov que no se publica sola: espera a JQ-9.** El motivo, medido:

- En producción `Zass:Verificacion:PermitirSinDocumentos` **no está definida**, así que vale
  `false`. En `appsettings.Development.json` vale `true`, y por eso en local funciona.
- Con la bandera apagada, API-27 responde **422** a toda credencial sin documento (RN-S2-01).
  Publicado hoy, el botón respondería siempre «todavía no podemos revisarla».
- Con JQ-9, la persona sube el documento y **entonces** el botón sirve, sin tocar la bandera.

Al hacerlo: `git cherry-pick 2ab306c` sobre tu rama de JQ-9. El texto del 422 que escribió el
agente («subir archivos aún no está disponible») **deja de ser cierto** con JQ-9: cámbialo por
uno que mande a subir el documento. Y la nota del pie («Subir documentos todavía no está
disponible») también. Los textos nuevos los aprueba Pablo.

### Si acabas todo eso — JQ-8, métricas del prestador · N2

Cuántas veces vieron su ficha, cuántos escaneos tuvo su tarjeta y cuántos contactos recibió. Va
**después** de JQ-4 y JQ-5, porque vive de lo que esas dos registran.

### JQ-10 — Rescate: la comuna que desaparece cuando hay distancia · N2 · chica (agregada el 17-nov)

> **✅ CUBIERTA, no la hagas (Pablo, 18-nov).** El mismo arreglo entró en el **PR #106**
> (`web/buscador-citas`, cambio «B3»): la comuna se ve siempre y primero, y la distancia al lado. Tiene su
> prueba en `TarjetaPersona.prueba.tsx` y su mutación (volver a `p.zona && !p.distancia`) cae. No hagas el
> `cherry-pick` de `b0e7b8d`: chocaría con el #106. Si al revisar el #106 ves algo distinto de lo que
> pedía esta tarea (por ejemplo, el pin repetido), coméntalo en ese PR.

**Ya está hecho y con pruebas, pero nunca llegó a `main`.** Lo dejó un agente el 13-nov y quedó en
una rama que nadie fusionó. Pablo decidió el 17-nov que va a tu carril, porque es búsqueda.

**El defecto:** en la tarjeta de un prestador (`frontend/src/marco/TarjetaPersona.tsx`), cuando
la persona compartió su ubicación se muestra «a 3 km» y **se esconde la comuna**. En `main`:
```tsx
{p.zona && !p.distancia && <span className="dato"><Icono nombre="pin" />{p.zona}</span>}
```
«a 3 km» dice cuánto, no dónde, y en Chile la comuna es el dato con el que se decide.

**El arreglo:** commit **`b0e7b8d`** («Mostrar la comuna tambien cuando hay distancia»), en la
rama `web/busqueda-accesibilidad`:
- `TarjetaPersona.tsx`: +4 / −1. La comuna se ve siempre, y el pin solo en la primera etiqueta.
- `TarjetaPersona.prueba.tsx`: +48, un archivo nuevo.

**Qué hacer:**
1. Rama desde `main` y `git cherry-pick b0e7b8d`. **Solo ese commit**: el otro de esa rama
   (`e2c8ffc`, «WIP RESCATE», el título al compartir) lo termina la sesión principal.
2. `npm run prueba`, y la mutación de siempre: vuelve a poner `!p.distancia` y la prueba nueva tiene que caer.
3. Mírala en un navegador con distancia **y** sin distancia, en celular: que no se repita el pin.

---

## 4. Cómo se cierra cada tanda

1. Rama corta desde `main` actualizado. Un PR por tanda; si trae dos cosas, se parte.
2. Título con el nivel: `[N1]`, `[N2]`. **N3 solo si toca migraciones, seguridad, `Program.cs`,
   correo, infraestructura, secretos o pagos** — y entonces lo dices y esperas.
3. Plantilla del PR completa, con la mutación anotada.
4. CI en verde. **Nada se mergea en rojo.** Si ves un rojo raro en integración, lo primero es
   comprobar que no haya otra suite corriendo a la vez.
5. Números de referencia al 14-nov (si los tuyos son menores, falta algo): aplicación **848**,
   arquitectura **20**, integración **777**, interfaz **528** en 39 archivos.
