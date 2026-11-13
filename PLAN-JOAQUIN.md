# Plan de trabajo de Joaquín — desde el 13 de noviembre de 2026

Hola Joaquín. Este archivo es tu plan de trabajo y tu permiso para avanzar solo. Lo escribió
Claude y lo aprobó Pablo el 13-nov-2026. **Manda sobre `TRASPASO.md` y `TRASPASO-12-NOV.md`**,
que son de días anteriores y tienen cosas ya hechas.

Mientras estés en tu carril: **no tienes que pedir permiso para nada**. Commitea, pushea y abre
tu PR. Pablo ya dijo que sí.

---

## 1. Lo que cambia hoy

| Antes | Ahora |
|---|---|
| No se commiteaba sin que Pablo lo pidiera | **Commitea y pushea tu rama cuando quieras.** Es más: hazlo seguido, porque es como sabemos en qué andas |
| Los textos visibles los aprobaba Pablo antes | **Escríbelos tú** siguiendo §6 y anótalos en el PR. Pablo los cambia después si quiere; no te bloquean |
| El nivel del PR se discutía | **Tú decides N1 o N2.** N3 solo lo que §4 te autoriza por escrito |
| Tu cola era J5 → J6 → J7 | **Esa cola ya está cerrada** (ver §3). Tu cola nueva está en §7 |

Lo único que sigue siendo de Pablo es **mergear a `main`**. Tú abres el PR; el botón es suyo.

---

## 2. Cómo no pisarnos (esta es la parte importante)

Hay dos de nosotros escribiendo código en el mismo repo. El reparto es por **archivos**, no por
buena voluntad:

| Carril | De quién | Archivos y temas que posee |
|---|---|---|
| **Suscripciones y cobro** | **Claude** | Todo lo que toque planes, cupo fundador, mes gratis, cupón de descuento, pasarela y pagos: `Dominio/Suscripciones/`, `Aplicacion/Suscripciones/`, `Datos/Repositorios/RepositorioSuscripciones.cs`, el `EndpointsSuscripciones.cs` que va a nacer, y la pantalla de planes |
| **Citas, reseñas y pantallas** | **Joaquín** | `Dominio/Agenda/`, `Dominio/Reputacion/`, `Aplicacion/Agenda/`, `Aplicacion/Reputacion/`, `Endpoints/EndpointsAgenda.cs`, `Endpoints/EndpointsResenas.cs`, y las pantallas de `frontend/src/paginas/` que no sean de planes ni de pago |
| **Infraestructura, decisiones y merges** | **Pablo** | `ESTADO.md`, `TRASPASO*.md`, Render, Neon, Cloudflare, el dominio y los secretos |

Y tres reglas de borde, que son las que de verdad evitan el choque:

1. **La ficha de migración de EF la tengo yo.** El cupón y el mes gratis necesitan migración, y la
   regla del proyecto es **una a la vez**. Tu cola de §7 está elegida para que **no necesites
   ninguna**. Si crees que necesitas una, **no la crees**: escríbelo en el PR y seguimos hablando.
2. **`Infraestructura/Correo/Plantillas.cs` es tuyo mientras dure JQ-1.** Ese archivo ya tiene las
   plantillas de suscripción, que son de mi carril; no las toco mientras tú estés dentro. Cuando
   cierres JQ-1, el archivo vuelve a ser de los dos.
3. **`zass_pruebas` es un turno, no un recurso.** Nunca corren dos suites de integración a la vez.
   Antes de correr integración, mira que nadie más la esté corriendo; si dudas, corre primero las
   unitarias.

Si te toca escribir en un archivo de mi carril, **no lo edites**: déjalo dicho en el PR y lo veo.
Una línea tuya ahí cuesta un minuto; un conflicto en `ServicioSuscripciones.cs` cuesta una tarde.

---

## 3. Lo que ya está hecho — no lo vuelvas a hacer

Comprobado en el repo el 13-nov, archivo por archivo y no de memoria:

| Tarea | Estado real |
|---|---|
| J1 | en `main` |
| J3 revisión visual de Mi agenda y Mi ficha | dentro del PR #11, con los tres textos aprobados |
| J4 la tarjeta abre la ficha (API-54) | dentro del PR #11, con el BOM UTF-8 de `Program.cs` restaurado y sus tres textos |
| J5 pantalla de reseñas | `frontend/src/paginas/MisResenas.tsx`, enrutada en `App.tsx:51`, con pruebas |
| J6 unificar la elegibilidad de reseña | hecho: los dos `HabilitaResena()` muertos están borrados (quedan los comentarios en `Dominio/Agenda/Entidades.cs:252` y `Dominio/Reputacion/Entidades.cs:29`) |
| J7 reprogramar desde el prestador | `ReprogramarCita.tsx` vive dentro de `MisCitas.tsx:287` y sus pruebas lo cubren |
| El 422 de reseñas en `problem+json` | arreglado |
| El correo de reseñas que nunca salía | arreglado |
| Quitar y listar bloqueos | existen `GET /api/mi/bloqueos`, `DELETE /api/mi/bloqueos/{id}` y el botón «Quitar este bloqueo» |

**Ojo:** casi todo eso vive en la rama `integracion/13-nov` (PR #11), que a la fecha de este
archivo **todavía no está en `main`**. Así que **arranca tus ramas desde `integracion/13-nov`**
hasta que ese PR se mergee; después, desde `main`. Si no, vas a reconstruir cosas que ya existen.

```
git fetch origin
git switch -c j/<tema> origin/integracion/13-nov     # mientras el PR #11 esté abierto
git switch -c j/<tema> origin/main                   # cuando ya esté mergeado
```

---

## 4. Lo que puedes decidir solo, y lo que no

**Decides tú, sin preguntar:**

- El nivel del PR (N1 o N2), cómo estructuras el código, y los nombres.
- Los textos visibles nuevos (§6).
- Las pruebas: cuáles, cuántas y de qué tipo. Lo único no negociable es la evidencia (§5).
- Commit, push y abrir el PR. Las veces que quieras.
- Borrar código muerto **de tu carril**, si muestras que nadie lo llama.

**N3 que Pablo ya te autorizó, con el borde escrito:** las plantillas de correo de citas de JQ-1.
Dentro de ese borde no preguntas nada. El borde es: **no** toques la política de reintentos de
`ProcesadorDeCorreo`, **no** agregues variables de configuración nuevas, **no** cambies el
remitente, y **reusa** la clave determinista de idempotencia que ya existe (CC-04). Si para
terminar JQ-1 hay que salir de ese borde, pára y escríbelo en el PR.

**No es desconfianza: no tienes las llaves.** Esto es de Pablo porque solo él tiene acceso:

- Neon (la base de producción), Render (el panel y el despliegue), Cloudflare y el dominio.
- Los tres secretos y su rotación.
- Mergear a `main`.

Y esto sigue valiendo siempre, para todos: **nunca** corres una migración contra algo que no sea
`zass` o `zass_pruebas` en `127.0.0.1:5433`, y **nunca** imprimes, copias ni subes un secreto.

---

## 5. La evidencia, que aquí ya costó cara

Esta es la única parte del plan donde no hay flexibilidad, y no es burocracia: son cuatro errores
que ya nos pasaron en este proyecto, cada uno con su factura.

1. **Un verde no prueba nada hasta que lo ves fallar.** Para cada prueba nueva: rompe a propósito
   el código que vigila, muestra que la prueba cae, deshaz la rotura. Eso va en el PR, bajo
   «Mutación». Sin eso, la prueba no cuenta como prueba.
2. **El candado con literal.** Una prueba que saca el valor esperado de la misma constante que
   vigila no vigila nada: si alguien cambia la constante, la prueba cambia con ella y sigue verde.
   Hace falta **además** una prueba con el número escrito a mano. Nos pasó cuatro veces en un día.
3. **Fecha y hora se prueban en un entorno hostil, nunca en el correcto.** Bajo el entorno bueno,
   el código malo acierta por casualidad. Aquí hay un caso real: `inicioDeDiaEnChile` se iba al día
   anterior los dos domingos del cambio de hora de septiembre, y las dos pruebas que había no lo
   veían porque miraban un día de verano y uno de invierno. Ahora recorre los 730 días de 2026 y
   2027. Si tocas algo con horas, piensa en esos dos domingos.
4. **El doble de prueba que filtra solo no ve la mutación.** Si tu repositorio falso filtra por su
   cuenta lo que el código de producción debería filtrar, puedes borrar el filtro de producción y
   la prueba sigue pasando. Ya nos pasó con el filtro por dueño de los bloqueos.

La regla corta, para cuando tengas dudas: **antes de decir que algo funciona, pregúntate qué
aspecto tendría el fallo y comprueba eso**, no la presencia del artefacto. Y para la base de datos,
consulta `pg_indexes`, `information_schema` y `pg_constraint`; que la migración diga «Done» no
prueba que el índice exista.

---

## 6. Los textos visibles

Pablo te los delegó. Escríbelos así:

- Español de Chile, directo, sin tecnicismos y sin «Ha ocurrido un error».
- Di qué pasó y qué puede hacer la persona. «No pudimos avisarle al prestador. Tu hora quedó
  guardada; vuelve a intentar el aviso» es útil; «Error de notificación» no.
- Nunca muestres el nombre de un campo, una tabla, un identificador ni un código de estado.
- En los correos: asunto corto sin signos de exclamación, y que la primera línea diga de qué se
  trata sin tener que abrir nada.
- La marca es **ZASS!** con doble S y con signo. Amarillo `#F6C81E`, tinta `#1A1A1A`, crema
  `#FDF8E7`. La frase es «Conecta con un toque».

En el PR, pon una sección **«Textos nuevos»** con la lista literal de lo que escribiste. Así Pablo
los cambia leyendo una lista, no buscándolos por el diff.

Esto reemplaza la regla 12 de `CONTRIBUIR.md` para ti, por decisión de Pablo del 13-nov.

---

## 7. Tu cola: cuatro tandas grandes

Una tanda es una rebanada completa —backend, pantalla y pruebas— que se puede soltar sola. Una
rama y un PR por tanda; dentro de la tanda, commitea todas las veces que quieras.

**No esperes a que te mergeen.** Terminada una tanda, pushea, abre el PR y **arranca la siguiente
desde la rama de la anterior**. Si Pablo pide cambios en la primera, los haces ahí y la segunda los
hereda al rebasar. Esperar el merge para empezar es el modo más lento de trabajar y no hace falta.

El orden es por valor, no por tamaño: A es lo que más le falta al producto, D lo que menos.

### Tanda A · Que ZASS! avise por correo cuando algo pasa con una cita

**Es el hueco más grande de tu carril:** `ServicioAgenda` no avisa a nadie, nunca. Hoy pides una
hora y el prestador se entera si se le ocurre entrar al panel. Para una plataforma de citas, eso no
es una falta: es el producto a medias.

Esta tanda es N3 por el correo, y es la que Pablo te autorizó por escrito en §4.

**Es el hueco más grande de tu carril:** `ServicioAgenda` no avisa a nadie, nunca. Hoy pides una
hora y el prestador se entera si se le ocurre entrar al panel.

Lo comprobado: el enum ya declara `CitaSolicitada`, `CitaConfirmada`, `CitaRechazada`,
`CitaCancelada` y `CitaReprogramada` (`Dominio/Comun/Enumeraciones.cs:53`), pero
`Plantillas.TienePlantilla` no reconoce ninguno, y `ServicioAgenda` no recibe `INotificaciones`.
**No hace falta migración**: `Notificacion.Tipo` se guarda como texto de hasta 40 caracteres
(`ZassDbContext.cs:835`) y no hay restricción `CHECK` sobre sus valores — compruébalo tú en
`pg_constraint` antes de confiar en esta línea.

Cuidado con el orden: si encolas un tipo que `TienePlantilla` no reconoce, el despacho **no lo
consulta nunca** y la notificación queda `Programada` para siempre, con 0 intentos y sin error.
Eso ya pasó con las reseñas: la cola se llena de avisos «en camino» que no van a salir jamás. Así
que **la plantilla primero, el encolado después**, y una prueba que falle si se encola un tipo sin
plantilla.

**Listo cuando:** los cinco tipos tienen plantilla y texto; la agenda encola en los cinco momentos
(pedir, confirmar, rechazar, cancelar y reprogramar), avisando **a quien le pasó la cosa**, no a
quien la hizo; el aviso llega una sola vez aunque el caso corra dos veces (clave determinista,
CC-04); existe una prueba que cae si alguien agrega un tipo `Cita*` sin plantilla; y lo viste salir
de verdad en local, no solo pasar en una prueba.

Y una decisión que es tuya, no mía: **qué lleva el correo**. Mi recomendación es día, hora, oficio y
un enlace al panel, y **nunca** el teléfono ni el correo de la otra persona: eso se ve entrando, que
es donde queda registrado quién vio qué.

### Tanda B · Que la reseña diga la verdad de dónde viene

Hoy la vía «cita realizada» está **muerta en producción**: API-46 rechaza `Origen = Cita`
(`EndpointsResenas.cs:109`) y nadie crea nunca un contacto ligado a una cita, así que la única vía
viva es «contacto directo más 7 días», aunque S5 diga otra cosa. O sea: alguien puede atenderte en
una cita agendada por ZASS! y no poder reseñarte, y alguien que solo apretó «llamar» sí.

Decisión de Pablo ya tomada: hay que **exponer el origen de la reseña** para poder rotular
«contacto directo, sin cita», exponiendo **el origen y nada más** — ni el id del contacto, ni el de
la cita.

Entra entera en tu carril: `Dominio/Reputacion/`, `Aplicacion/Reputacion/`, `EndpointsResenas.cs`,
`Ficha.tsx`, `ResenasDelPrestador.tsx` y `MisResenas.tsx`.

**Listo cuando:** marcar la asistencia de una cita deja al cliente elegible para reseñar; la ficha y
la lista rotulan el origen; ninguna respuesta de la API filtra un identificador interno (pruébalo
mirando el JSON, no el DTO); y la regla de los 72 h de edición sigue valiendo igual por las dos vías.

### Tanda C · El panel interno, que existe en la API y no existe para nadie

Hay cinco endpoints de revisión interna sin una sola pantalla: la cola de credenciales
(API-29 `GET /api/interno/credenciales/cola`, API-30 tomar, API-31 resolver) y la de reportes de
reseñas (API-52 y API-53, en `EndpointsResenas.cs:232` y `:244`). Eso significa que hoy **nadie
puede verificar una credencial ni resolver un reporte**, que son dos de las promesas del producto.

**Esto ya no necesita ninguna decisión:** el rol es `verificador` y su política existe
(`Seguridad.cs:32` y `:153`); el grupo entero exige ese rol y responde 403 sin ejecutar nada,
tampoco para listar. Comprueba qué política exigen API-52 y API-53, que son del otro lado.

Tres cosas que la pantalla tiene que respetar porque la API ya las impone: la reserva de una
credencial dura **30 minutos** (CC-03), nadie revisa la propia (RN-S2-09), y **403 y 404 responden
igual a propósito** — así que la pantalla no puede dar a entender cuál de los dos fue.

**Listo cuando:** un usuario con rol `verificador` entra, ve su cola, toma un caso, lo resuelve y
ve el resultado; alguien sin el rol no ve ni el enlace; y existe una prueba de que tomar un caso ya
tomado por otro no lo roba.

### Tanda D · Los huecos de la cuenta

Dos cosas chicas que cierran la cuenta de una persona:

- **Eliminar la cuenta (API-17** en `EndpointsIdentidad.cs:174`): el endpoint existe y anonimiza
  (RN-S1-07), y no hay pantalla. Pide confirmación escrita, explica qué se borra y qué no, y no
  dejes a la persona en una pantalla muerta después.
- **Credenciales, el camino completo:** `MisCredenciales.tsx` existe y API-27 (`enviar a revisión`)
  también. Recórrelo en un navegador de punta a punta y cierra lo que falte; si el documento mismo
  no se puede subir todavía, escribe en el PR exactamente qué falta y hasta dónde llegaste.

**Listo cuando:** una persona puede borrar su cuenta sola, y un prestador puede llevar una
credencial desde que la crea hasta que queda en la cola de revisión, visto en un navegador.

---

## 8. Cómo se corre, cómo se abre el PR

```
.\iniciar-postgres.bat                               # base local, puerto 5433
cd backend;  dotnet test;  cd ..
cd frontend; npm run prueba; npx tsc -b; npx oxlint; cd ..
.\ver-zass.bat                                       # API e interfaz
.\cerrar-zass.bat
```

En el PR, usa la plantilla del repo y llena de verdad **Mutación** y **Pruebas** (los comandos que
corriste y sus números, no «todo verde»). Si algo no lo comprobaste, dilo en el PR. Decirlo no
tiene costo; que se descubra después, sí.

Una cosa más, que nos pasó esta semana: `gh run list` puede mostrar un `success` arriba que es de
otro flujo de trabajo, no del de pruebas. Mira el nombre del flujo antes de creerle al color.

---

## 9. Si algo de aquí no calza con el código

Pasa, y es más útil decirlo que adaptarse. Si una línea de este plan no coincide con lo que ves,
**no adaptes la tarea por tu cuenta y tampoco corrijas en silencio**: escribe en el PR qué dice el
plan, qué dice el código y qué hiciste. Los identificadores (`CU-`, `TBL-`, `API-`, `RN-`, `MOD-`,
`R-`, `CC-`) son del documento, y cuando el código y el documento no coinciden, lo que no se puede
hacer es dejar la discrepancia callada.
