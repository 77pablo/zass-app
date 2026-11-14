# Plan de trabajo hasta el lunes 23 — Joaquín y Pablo

Hoy es **13 de noviembre de 2026**. ZASS! abre el **lunes 23**. Quedan diez días.

Este archivo es el plan de trabajo y el permiso para avanzar. Lo escribió Claude y lo aprobó Pablo
el 13-nov. **Manda sobre `TRASPASO.md` y `TRASPASO-12-NOV.md`**, que son de días anteriores y
tienen cosas ya hechas.

Todo lo que falta para el lunes 23 está repartido entre **dos personas y nada más: Joaquín y
Pablo**. Hay una tercera lista —la de Claude— y está escrita aquí **solo para que nadie la toque**.

| Parte | Qué hay | Dónde |
|---|---|---|
| **Lo de Joaquín** | **11 tandas**, de las cuales 4 son rescates ya medio andados | §8 |
| **Lo de Pablo** | **12 puntos**, cada uno con lo que hay que hacer | §9 |
| **Lo de Claude** | El carril de cobro. Ni Joaquín ni Pablo escriben código ahí | §2 |

**Joaquín: mientras estés en tu carril, no tienes que pedir permiso para nada.** Decides, escribes,
commiteas, pusheas y abres tu PR. Lo único que es de Pablo es **apretar el botón de mergear**.

---

## 1. Lo que cambia hoy

| Antes | Ahora |
|---|---|
| No se commiteaba sin que Pablo lo pidiera | **Commitea y pushea tu rama cuando quieras.** Es más: hazlo seguido, porque es como sabemos en qué andas |
| Los textos visibles los aprobaba Pablo antes | **Escríbelos tú** siguiendo §6 y anótalos en el PR bajo «Textos nuevos». Pablo los cambia después si quiere; **no te bloquean, ni antes ni durante ni al cerrar** |
| El nivel del PR se discutía | **Tú decides N1 o N2.** N3 solo lo que §4 te autoriza por escrito |
| Tu cola era J5 → J6 → J7 | **Esa cola ya está cerrada** (ver §3) |
| Tu cola nueva eran cuatro tandas A–D | **Siguen vigentes las cuatro**, y ahora van dentro de una cola de **11** (§8). El orden cambió, y en §8.0 está por qué |
| Pablo revisaba en ventanas, dos veces al día | **Ya no hay ventana que esperar.** Ninguna tanda de Joaquín depende de que Pablo mire nada antes |
| Lo que faltaba estaba en listas sueltas | **Está repartido en dos: §8 es de Joaquín, §9 es de Pablo.** Si algo no aparece en ninguna de las dos y no es de §2, es que no está |

**Una corrección de reparto que conviene decir en voz alta:** `ESTADO.md` (sección «Lo que de
verdad bloquea el lunes 23») pone el cupo fundador, el mes gratis, el cupón y el cobro **a nombre
de Joaquín**. Eso cambió hoy: **esos cuatro son de Claude** (§2). `ESTADO.md` lo edita solo Pablo,
así que la línea sigue ahí; manda esta.

---

## 2. Cómo no pisarnos (esta es la parte importante)

Hay tres manos sobre el mismo repositorio. El reparto es por **archivos**, no por buena voluntad.

### 2.1 El carril de Claude — nadie más lo toca, ni para una línea

Suscripciones, planes, cupo fundador, mes gratis, cupón, pasarela y pagos. **No es trabajo
pendiente de Joaquín ni de Pablo: está aquí para que se sepa cuál es la frontera.**

| Archivos y carpetas | Qué hay ahí |
|---|---|
| `backend/src/Zass.Dominio/Suscripciones/` | Las entidades de plan, suscripción y cupo |
| `backend/src/Zass.Aplicacion/Suscripciones/` | `ServicioSuscripciones` y el reparto de los 50 cupos |
| `backend/src/Zass.Aplicacion/Pagos/` y `backend/src/Zass.Aplicacion/Puertos/Pasarela.cs` | El puerto de la pasarela |
| `backend/src/Zass.Infraestructura/Pagos/` | La implementación de ese puerto |
| `backend/src/Zass.Infraestructura/Datos/Repositorios/RepositorioSuscripciones.cs` | |
| `backend/src/Zass.Infraestructura/Datos/Migraciones/` | **Toda** migración de EF |
| `backend/src/Zass.Api/Endpoints/EndpointsSuscripciones.cs` | Todavía no existe; va a nacer |
| La pantalla `/planes` y la del pago | Todavía no existen; van a nacer |

**Y con ellas, los textos de precio.** Cualquier frase que diga un peso —«$5.990», «$7.990», «mes
gratis», «50 cupos», «cupón»— es del carril de Claude, **aunque esté en un archivo de Joaquín**.
Si Joaquín toca `Profesionales.tsx` u `OfrecerServicios.tsx` por otra razón, no escribe ahí el
precio: lo anota en el PR y sigue.

### 2.2 El carril de Joaquín — todo el resto del producto

Citas, reseñas, credenciales, el panel interno, la ficha pública, la búsqueda, la portada, el
camino de entrada, la accesibilidad y el SEO.

| Zona | Archivos |
|---|---|
| Agenda | `Dominio/Agenda/`, `Aplicacion/Agenda/`, `Api/Endpoints/EndpointsAgenda.cs` |
| Reputación | `Dominio/Reputacion/`, `Aplicacion/Resenas/`, `Api/Endpoints/EndpointsResenas.cs` |
| Credenciales y verificación | `Aplicacion/Credenciales/`, `Api/Endpoints/EndpointsCredenciales.cs`, `Api/Endpoints/EndpointsVerificacion.cs` |
| Identidad, la parte de cuenta | `Api/Endpoints/EndpointsIdentidad.cs` (API-17 y la verificación de correo) |
| Interfaz | Todo `frontend/src/` que no sea planes ni pago, `frontend/functions/`, `frontend/pruebas-navegador/` |
| Correo | `Infraestructura/Correo/Plantillas.cs`, **mientras dure la Tanda 7** |

### 2.3 El carril de Pablo — lo que solo él puede hacer

`ESTADO.md`, `TRASPASO*.md`, Render, Neon, Cloudflare, el dominio, los secretos, el merge a `main`,
la decisión legal y las dos decisiones abiertas de §9.

### 2.4 Las cuatro reglas de borde, que son las que de verdad evitan el choque

1. **La ficha de migración de EF la tiene Claude.** Las 11 tandas de §8 están elegidas para que
   **ninguna necesite migración**. Si Joaquín cree que necesita una, **no la crea**: para, lo
   escribe en el PR bajo «Migración que haría falta» y sigue con otra tanda. Una migración suelta
   en paralelo con la de cobro cuesta una tarde de conflictos en el snapshot del modelo.
2. **`Infraestructura/Correo/Plantillas.cs` es de Joaquín mientras dure la Tanda 7.** Ese archivo
   ya tiene las plantillas de suscripción, que son del carril de Claude; Claude no las toca
   mientras Joaquín esté dentro. Cerrada la Tanda 7, el archivo vuelve a ser de los dos.
3. **`zass_pruebas` es un turno, no un recurso.** Nunca corren dos suites de integración a la vez.
   Antes de correr integración, mira que nadie más la esté corriendo; si dudas, corre primero las
   unitarias.
4. **Si te toca escribir en un archivo de otro carril, no lo edites.** Déjalo dicho en el PR. Una
   línea tuya ahí cuesta un minuto; un conflicto en `ServicioSuscripciones.cs` cuesta una tarde.

---

## 3. Lo que ya está hecho — no lo vuelvas a hacer

Comprobado en el repositorio el 13-nov, archivo por archivo y no de memoria:

| Tarea | Estado real |
|---|---|
| J1 | en `main` |
| J3 revisión visual de Mi agenda y Mi ficha | dentro del PR #11, con los tres textos aprobados |
| J4 la tarjeta abre la ficha (API-54) | dentro del PR #11, con el BOM UTF-8 de `Program.cs` restaurado y sus tres textos |
| J5 pantalla de reseñas | `frontend/src/paginas/MisResenas.tsx`, enrutada en `App.tsx:51`, con pruebas |
| J6 unificar la elegibilidad de reseña | hecho: los dos `HabilitaResena()` muertos están borrados (quedan los comentarios en `Dominio/Agenda/Entidades.cs:252` y `Dominio/Reputacion/Entidades.cs:29`) |
| J7 reprogramar desde el prestador | `ReprogramarCita.tsx` vive dentro de `MisCitas.tsx` y sus pruebas lo cubren |
| El 422 de reseñas en `problem+json` | arreglado |
| El correo de reseñas que nunca salía | arreglado |
| Quitar y listar bloqueos | existen `GET /api/mi/bloqueos`, `DELETE /api/mi/bloqueos/{id}` y el botón «Quitar este bloqueo» |
| El control de radio de la búsqueda | reconstruido entero, incluido el parámetro en API-63 (`PaginaBuscar.tsx:258`) |
| El aviso del limitador (429) | llega: el 429 ya responde `title`/`detail` y el `Content-Type` correcto |
| El pie que sigue la sesión | `marco/Pie.tsx` |
| «Saltar al contenido», `:focus-visible` de 3 px, `prefers-reduced-motion`, 44 px en lo pulsable | ya están; la base de accesibilidad es buena |
| El escape de todo texto de usuario en las etiquetas del servidor | `functions/_comun/seo.js:31-54` |
| El vaciado del contacto **en el servidor**, no en la pantalla | `EndpointsIdentidad.cs:353-364` |

**Ojo:** casi todo eso vive en la rama `integracion/13-nov` (PR #11), que a la fecha de este
archivo **todavía no está en `main`**.

### De dónde arranca cada rama

```
git fetch origin
git switch -c j/<tema> origin/integracion/13-nov     # mientras el PR #11 esté abierto
git switch -c j/<tema> origin/main                   # cuando ya esté mergeado
```

**Y no esperes el merge para empezar la siguiente tanda.** Terminada una tanda: pushea, abre el PR
y **arranca la siguiente desde la rama de la anterior**. Si Pablo pide cambios en la primera, los
haces ahí y la segunda los hereda al rebasar. Esperar el merge es el modo más lento de trabajar y
no hace falta. Esto **reemplaza para Joaquín** la regla 7 del flujo de `CONTRIBUIR.md` («nunca
sigas desde una rama que aún no se aprobó»), por decisión de Pablo del 13-nov.

---

## 4. Lo que puedes decidir solo, y lo que no

**Decide Joaquín, sin preguntar y sin esperar:**

- El nivel del PR (N1 o N2), cómo estructura el código, y los nombres.
- **Los textos visibles nuevos** (§6). Pablo ya se los delegó. Van en el PR bajo «Textos nuevos» y
  Pablo los cambia **después**, leyendo esa lista. **Un texto pendiente de opinión no detiene una
  tanda, ni su commit, ni su push, ni su PR.**
- Las pruebas: cuáles, cuántas y de qué tipo. Lo único no negociable es la evidencia (§5).
- Commit, push y abrir el PR. Las veces que quiera.
- Borrar código muerto **de su carril**, si muestra que nadie lo llama.
- El orden interno de cada tanda, y partir una tanda en dos PR si le queda grande.

**N3 que Pablo ya autorizó por escrito, con el borde dicho:** las plantillas de correo de citas de
la **Tanda 7**. Dentro de ese borde no se pregunta nada. El borde es: **no** tocar la política de
reintentos de `ProcesadorDeCorreo`, **no** agregar variables de configuración nuevas, **no**
cambiar el remitente, y **reusar** la clave determinista de idempotencia que ya existe (CC-04). Si
para terminar la Tanda 7 hay que salir de ese borde, para y escríbelo en el PR.

**No es desconfianza: no tienes las llaves.** Esto es de Pablo porque solo él tiene acceso: Neon,
Render, Cloudflare, el dominio, los tres secretos y su rotación, y el merge a `main`.

Y esto sigue valiendo siempre, para todos: **nunca** se corre una migración contra algo que no sea
`zass` o `zass_pruebas` en `127.0.0.1:5433`, y **nunca** se imprime, copia ni sube un secreto.

---

## 5. La evidencia, que aquí ya costó cara

Esta es la única parte del plan donde no hay flexibilidad, y no es burocracia: son cuatro errores
que ya nos pasaron en este proyecto, cada uno con su factura.

1. **Un verde no prueba nada hasta que lo ves fallar.** Para cada prueba nueva: rompe a propósito
   el código que vigila, muestra que la prueba cae, deshaz la rotura. Eso va en el PR, bajo
   «Mutación». Sin eso, la prueba no cuenta como prueba. Y para probar una guarda, el mutante es
   **comentar la línea**, no borrarla: borrarla a veces ni compila, y entonces no mediste nada.
2. **El candado con literal.** Una prueba que saca el valor esperado de la misma constante que
   vigila no vigila nada: si alguien cambia la constante, la prueba cambia con ella y sigue verde.
   Hace falta **además** una prueba con el número escrito a mano. Nos pasó cuatro veces en un día.
3. **Fecha y hora se prueban en un entorno hostil, nunca en el correcto.** Bajo el entorno bueno,
   el código malo acierta por casualidad. Caso real: `inicioDeDiaEnChile` se iba al día anterior
   los dos domingos del cambio de hora de septiembre, y las dos pruebas que había no lo veían
   porque miraban un día de verano y uno de invierno. Ahora recorre los 730 días de 2026 y 2027. Si
   tocas algo con horas, piensa en esos dos domingos.
4. **El doble de prueba que filtra solo no ve la mutación.** Si tu repositorio falso filtra por su
   cuenta lo que el código de producción debería filtrar, puedes borrar el filtro de producción y
   la prueba sigue pasando. Ya nos pasó con el filtro por dueño de los bloqueos.

La regla corta, para cuando tengas dudas: **antes de decir que algo funciona, pregúntate qué
aspecto tendría el fallo y comprueba eso**, no la presencia del artefacto. Y para la base de datos,
consulta `pg_indexes`, `information_schema` y `pg_constraint`; que la migración diga «Done» no
prueba que el índice exista.

**Las cuatro ramas de rescate llegan con esta deuda pagada a cero.** Su propio commit lo dice: «No
está comprobado: faltan las pruebas, su mutación, y correr `npm run prueba`, `tsc -b` y `oxlint`».
Rescatar una rama **no** es empujarla: es ponerle encima lo de arriba antes de decir que está.

---

## 6. Los textos visibles

Pablo te los delegó entero. Escríbelos así:

- Español de Chile, directo, sin tecnicismos y sin «Ha ocurrido un error».
- Di qué pasó y qué puede hacer la persona. «No pudimos avisarle al prestador. Tu hora quedó
  guardada; vuelve a intentar el aviso» es útil; «Error de notificación» no.
- Nunca muestres el nombre de un campo, una tabla, un identificador ni un código de estado.
- En los correos: asunto corto sin signos de exclamación, y que la primera línea diga de qué se
  trata sin tener que abrir nada.
- La marca es **ZASS!** con doble S y con signo. Amarillo `#F6C81E`, tinta `#1A1A1A`, crema
  `#FDF8E7`. La frase es «Conecta con un toque».
- **El correo de contacto es `somoszaschile@gmail.com`.** Es un Gmail, y es el único que existe.
  Comprobado hoy: **en el repositorio no hay ningún correo de contacto escrito** —ni
  `hola@zass.cl` ni ningún otro—; lo único que aparece es `no-responder@zass.cl` como remitente en
  las pruebas de correo. Así que no hay nada que corregir en las cuatro ramas, pero **cualquier
  texto nuevo que mande a escribirnos usa `somoszaschile@gmail.com`**. Si ves `hola@zass.cl` en
  alguna propuesta suelta, es un error heredado de un borrador: cámbialo.

En el PR, pon una sección **«Textos nuevos»** con la lista literal de lo que escribiste, con su
archivo y su línea. Así Pablo los cambia leyendo una lista, no buscándolos por el diff. **No
esperas respuesta a esa lista para nada.**

Esto reemplaza la regla 12 de `CONTRIBUIR.md` para Joaquín, por decisión de Pablo del 13-nov.

---

## 7. Cómo se corre, cómo se abre el PR

```
.\iniciar-postgres.bat                               # base local, puerto 5433
cd backend;  dotnet test;  cd ..
cd frontend; npm run prueba; npx tsc -b; npx oxlint; cd ..
.\ver-zass.bat                                       # API e interfaz
.\cerrar-zass.bat
```

Para los guiones de navegador, con `.\ver-zass.bat` arriba:

```
cd frontend; node pruebas-navegador\mirar-portada.mjs; cd ..
```

En el PR, usa la plantilla del repositorio y llena de verdad **Mutación** y **Pruebas** (los
comandos que corriste y sus números, no «todo verde»). Si algo no lo comprobaste, dilo en el PR.
Decirlo no tiene costo; que se descubra después, sí.

Cuatro cosas que ya mordieron y conviene tener a mano:

- **`gh run list` puede mostrar un `success` arriba que es de otro flujo de trabajo**, no del de
  pruebas. Mira el nombre del flujo antes de creerle al color.
- **La suite de arquitectura no corre en este PC** por un bloqueo de Windows sobre el DLL recién
  compilado (`FileLoadException 0x800711C7`). Es el punto 9 de §9, y es de Pablo. Mientras no esté,
  esas 16 pruebas las valida GitHub Actions, no tu máquina. **No es un rojo tuyo.**
- **Agregar una ruta de panel deja roja una prueba a propósito**:
  `frontend/src/pruebas-functions/sitemap.prueba.js:206` compara `robots.txt` con las rutas que
  declara `App.tsx`. Es un rojo esperado hasta que actualices `public/robots.txt`.
- **Smart App Control de Windows** puede bloquear en local las DLL de pruebas y `oxlint`. Si pasa,
  dilo: el backend se valida en Actions.

---

## 8. LO DE JOAQUÍN — once tandas

Una tanda es una rebanada completa —backend si hace falta, pantalla y pruebas— que se puede soltar
sola. **Una rama y un PR por tanda**; dentro de la tanda, commitea todas las veces que quieras.

### 8.0 El orden, y por qué es este

| # | Tanda | Rama | Por qué va aquí |
|---|---|---|---|
| 1 | El sello «Verificado», alcanzable | `web/panel-prestador` | Ya está medio escrita, y es lo más rentable del repositorio: hoy **nadie puede tener sello**, y la búsqueda vende un filtro que devuelve a nadie |
| 2 | La ficha pública | `web/ficha-movil` | Medio escrita. Es la pantalla donde se decide contratar, y en teléfono hoy está al revés |
| 3 | La portada y el camino de entrada | `web/portada-entrada` | Medio escrita. Con Render dormido, la primera visita del día ve media portada **muda** |
| 4 | La búsqueda, la accesibilidad y el SEO de `/buscar` | `web/busqueda-accesibilidad` | Medio escrita. Es la segunda pantalla de todo el mundo |
| 5 | Mirar todo en un navegador de verdad | `j/navegador` | Va **después** de las cuatro rescatadas y **antes** de lo nuevo: es donde se descubre qué de 1–4 no quedó bien, mientras todavía es barato |
| 6 | La reseña: de dónde viene y por dónde se entra | `j/resenas-origen` | Lo primero nuevo, porque hoy la única puerta a una reseña es un correo |
| 7 | Avisos de cita por correo | `j/correo-citas` | El hueco más grande del carril, pero es N3 y tarda; va cuando lo barato ya está hecho |
| 8 | El panel interno | `j/panel-interno` | Cinco endpoints sin una sola pantalla. Depende de una decisión de Pablo (§9.7) para **usarse**, no para construirse |
| 9 | Los huecos de la cuenta | `j/cuenta` | Cierra lo que una persona tiene derecho a hacer sola |
| 10 | Fotos de perfil | `j/fotos` | Varios días. Para un oficio de confianza, la foto es la mitad de la ficha |
| 11 | Páginas de aterrizaje por oficio y comuna | `j/aterrizaje` | Varios días, y lo que más paga a mediano plazo. Va al final porque **no** alcanza a dar fruto el día 23 |

**Qué pasó con las cuatro tandas A–D del plan anterior.** Siguen vigentes, enteras, y están aquí:

| Antes | Ahora | Por qué se movió |
|---|---|---|
| A · avisos de cita por correo | **Tanda 7** | Era la primera por valor. Baja porque es N3, toca correo, y hay cuatro ramas a medio hacer que valen más por hora |
| B · la vía «cita realizada» de la reseña | **Tanda 6**, fundida con «enlazar la reseña desde Mis citas» | **Van juntas o no van.** Hoy API-46 rechaza `Origen = Cita` (`EndpointsResenas.cs:109`): poner el botón «Dejar reseña» en «Mis citas» sin abrir esa vía primero es poner un botón que responde 422. Separarlas era construir una puerta a un muro |
| C · el panel interno | **Tanda 8** | Igual de necesaria; baja porque 1–4 estaban ya medio hechas |
| D · eliminar cuenta y credenciales | **Tanda 9** | Igual que antes: lo que menos le falta al producto de las cuatro |

---

### Tanda 1 · Que el sello «Verificado» se pueda tener

**Rama:** `web/ficha-movil` no; esta es **`web/panel-prestador`**, que ya existe y ya tiene trabajo
dentro. Arráncala con `git switch web/panel-prestador` y sigue ahí.

**Por qué va primera.** El sello «Verificado» es hoy **inalcanzable**, y encima la pantalla dice
que es imposible. Mientras tanto la búsqueda ofrece un filtro «Solo verificados» que el día 23
devuelve a nadie y hace parecer que el sitio está roto. Es el arreglo más rentable del repositorio:
pocas líneas de interfaz más un interruptor que aprieta Pablo (§9.4).

**Lo que ya está en el commit de rescate `2ab306c`** (sin pruebas, sin mutación, sin comprobar):

- `funciones/perfil/servicio.ts`: la función `enviarCredencialARevision(id)` que llama a API-27.
- `MisCredenciales.tsx`: el botón «Enviar a revisión» en cada credencial en `Borrador`, el estado
  «Enviando…», el error puesto **junto a la credencial** y no arriba, y el texto de pie cambiado.
- `MisCredenciales.prueba.tsx`: 116 líneas nuevas, a medio hacer.

**Lo comprobado que lo sostiene:** API-27 existe (`backend/src/Zass.Api/Endpoints/EndpointsCredenciales.cs:78-92`,
`POST /api/mi/credenciales/{id}/enviar`, `.WithName("API-27")`). La bandera
`Zass:Verificacion:PermitirSinDocumentos` existe (`Aplicacion/Credenciales/IServicioVerificacion.cs:38`)
y está en `true` **solo** en `appsettings.Development.json:14`. Encenderla en Render es de Pablo
(§9.4); **tú no esperas a que lo haga**: tu pantalla tiene que funcionar bien en los dos casos.

**Lo que falta en esta rama, además de terminar lo de arriba:**

| Qué | Dónde | Detalle |
|---|---|---|
| La contradicción de «Mi ficha» | `MiFicha.tsx:285-286` | Dice «Por ahora no puedes subir tu foto, **tus títulos** ni tu dirección» justo encima del bloque que declara los títulos. La parte de los títulos es falsa |
| El prestador vencido no tiene salida | `MiFicha.tsx:186` | Pinta el aviso de `estadoSusc` y **no hay ninguna acción al lado**. Pon la acción; el texto que diga qué hacer es tuyo (§6). **El precio no lo escribes tú** (§2.1) |
| `modoAgenda` no se puede elegir | `MiFicha.tsx:390, 399, 415, 420, 444` | El campo viaja al servidor (`modoAgenda: f.modo`, `:444`) y se lee de vuelta (`:399`), pero las dos vías que crean una fila lo fijan en `'automatico'` (`:415` y `:420`) y **ninguna pantalla lo deja cambiar**. Resultado: todo servicio nace en automático, y a un abogado le confirma solo una hora un desconocido. Es una objeción de venta, no un detalle |
| Compartir la ficha | `MiFicha.tsx:179-181` | Hoy solo hay «Ver como la ve el público». Falta copiar el enlace y/o abrir WhatsApp. El prestador que paga es el primer canal de difusión y no tiene cómo pasar su enlace |

**Un detalle del rescate que hay que mirar:** el texto nuevo del pie («Un borrador solo lo ves tú.
Envíalo a revisión…») promete una revisión a mano. Que esa revisión exista depende de la Tanda 8 y
de §9.7. Si el día 23 nadie va a revisar, el texto miente: decide tú cómo redactarlo y anótalo.

**Listo cuando:**

1. Un prestador con una credencial en `Borrador` ve el botón, lo aprieta, y la credencial pasa a
   revisión — **visto en un navegador**, no solo en una prueba.
2. Con la bandera **apagada** la pantalla dice algo verdadero y útil, y no repite el mensaje del
   servidor que manda a subir un archivo que no se puede subir.
3. «Mi ficha» ya no se contradice sobre los títulos.
4. El prestador vencido tiene **una acción** al lado del aviso.
5. Se puede elegir `modoAgenda` por servicio, y lo elegido vuelve bien al recargar.
6. El prestador puede copiar el enlace de su ficha en un clic.
7. Cada prueba nueva con su mutación anotada, y `npm run prueba`, `npx tsc -b` y `npx oxlint`
   limpios, con los números en el PR.

---

### Tanda 2 · La ficha pública, que es donde se decide contratar

**Rama:** `web/ficha-movil`, que ya existe.

**Lo que ya está en el commit de rescate `ff368fb`** (sin pruebas, sin mutación, sin comprobar):

- `index.css`: `.ficha-rejilla > .lateral { order: -1 }` bajo 960 px, con el comentario que explica
  por qué se usa `order` y **no** se reordena el DOM (el orden del HTML es el correcto para un
  lector de pantalla y para el foco). Más `.nota-enlace` y `.credenciales-nota`.
- `Ficha.tsx`: las credenciales movidas **antes** del calendario; el enlace «Leer las N reseñas»
  a `/prestadores/:slug/resenas`; el párrafo que explica qué significan «Verificada» y «Declarada
  por el usuario»; y el ajuste del vacío de contacto.
- `Ficha.prueba.tsx`: 194 líneas nuevas, a medio hacer.

**Lo comprobado:** `.ficha-rejilla` colapsa a una columna bajo 960 px (`index.css:470-471` en
`integracion/13-nov`) y el `<aside class="lateral">` va después del `<article>` (`Ficha.tsx:227`).
El promedio se pintaba sin enlace (`Ficha.tsx:206-224`). Las credenciales iban después del
calendario (`Ficha.tsx:162-189`). Las etiquetas se pintan sin explicación (`Ficha.tsx:183`).

**Lo que falta en esta rama, además de terminar lo de arriba:**

| Qué | Dónde | Detalle |
|---|---|---|
| Reservar el alto, para que no salte | `index.css:637-640` | `.hero-rejilla`, `.hero-texto` y `.hero-mapa` no tienen `min-height`: el mapa y las cifras de la portada se insertan al llegar los datos y empujan todo. **Comprobado:** las reglas están ahí; el `min-height` no |
| La distancia también en la ficha | `Ficha.tsx:114-132` | Al abrir la ficha se pierde el dato por el que se la eligió. La tarjeta la muestra; la ficha no |
| Desde cuándo está la persona en ZASS! | `Ficha.tsx:114-132` | «Aquí desde noviembre de 2026» vale más que nada cuando todavía no hay reseñas. **Sin comprobar:** no verifiqué que la fecha de alta viaje en el DTO de la ficha. Si no viaja, **para**: añadirla es backend de tu carril, pero mira primero que no pida migración (§2.4.1) |
| Esqueleto mientras carga | `Ficha.tsx:91-97` | Hoy pinta una línea («Cargando la ficha…», `:94`) y después la ficha entera de golpe |

**Listo cuando:**

1. En un teléfono (ancho ≤ 960 px) la caja de contacto se ve **antes** de desplazar, y el orden del
   DOM sigue siendo artículo → aside (compruébalo con el tabulador, no solo con los ojos).
2. El promedio enlaza a las reseñas, y el enlace dice cuántas son.
3. Las credenciales van antes del calendario y traen su explicación.
4. La ficha dice la distancia cuando se llegó a ella con ubicación, y desde cuándo está la persona
   —o el PR dice por qué eso último no se pudo.
5. Mientras carga hay un esqueleto, y al llegar los datos **nada salta**.
6. Las pruebas nuevas, con su mutación.

---

### Tanda 3 · La portada y el camino de entrada

**Rama:** `web/portada-entrada`, que ya existe. **Ojo: esta es la que menos trabajo hecho trae.**

**Lo que ya está:**

- Un commit bueno y terminado: `d2a7020` «Pedir el token CSRF sin asumir que la respuesta es
  JSON» (`api/cliente.ts`). Ese **no** es de rescate: está entero.
- El commit de rescate `7da4bb6`, que toca **solo `Portada.tsx`**: añade el componente
  `EstadoDeLaPortada`, que mira `.error` y `.cargando` de las dos cargas y ofrece reintentar.

**Lo comprobado:** `Portada.tsx:36-39` (en `integracion/13-nov`) lee `catalogo.datos?…` y
`comunidad.datos?…` y **nunca** `.error` ni `.cargando`; cada sección está tras un `&&`, así que
sin datos desaparecen el mapa, la cinta, las cifras y los recién llegados **sin una palabra**.
`Oficios.tsx:28-34` ya hace lo correcto (error con «Reintentar» y línea de «Cargando…»), y es el
patrón que hay que copiar — el rescate lo copió bien.

**Un defecto que el propio rescate metió y hay que arreglar antes de nada:** el commit dejó
`const OFICIOS_EN_CINTA =['abogado', …]` sin el espacio tras el `=`. Es cosmético, pero `oxlint`
está en la puerta.

**Todo lo demás de esta rama está sin empezar:**

| Qué | Dónde | Detalle |
|---|---|---|
| El destino de regreso se pierde al **crear** cuenta | `CrearCuenta.tsx` entero | `AgendarHora.tsx:227` promete «volverás aquí con esta hora elegida» y **sí** manda el estado: `<Link to="/crear-cuenta" state={{ despues: regreso }}>`. Pero `CrearCuenta.tsx` solo lee `?rol` (`:9-11`) y **nunca** llama a `destinoDe(state)`. `Entrar.tsx` sí lo hace (`:8`, `:14`, `:39`, con `sesion/destino.ts`): ahí está el patrón. Se rompe justo para quien no tenía cuenta, que es el caso que más importa el día 23 |
| El enlace «Crea una» no reenvía el destino | `Entrar.tsx:61` | `<Link to="/crear-cuenta">Crea una</Link>`, sin `state`. Quien llega a «Entrar» con un destino y descubre ahí que no tiene cuenta, lo pierde en ese clic |
| Tras crear la cuenta nadie dice qué sigue | `funciones/identidad/Registrarse.tsx:67-76` | Termina en «Revisa tu correo» + «Vence en 24 horas» y nada más: ni qué pasa al abrirlo, ni dónde mirar si no llega, ni a quién escribir |
| `/oficios` desaprovecha el vacío | `Oficios.tsx:45` | Dice «Aún nadie», y el 23 va a ser casi toda la página. Puede decir «Nadie todavía — ¿eres tú?» enlazando a `/profesionales`. **El texto es tuyo** (§6); el precio no |
| Un callejón sin salida en la verificación de correo | `Recuperacion.tsx:75` | «Si el enlace venció, entra y pide uno nuevo» — y **no existe forma de pedirlo**: el correo de verificación se encola solo al registrarse. Sin correo verificado no se puede agendar (`AgendarHora.tsx:221-222`). **Dos salidas, y eliges tú:** (a) el texto manda a escribir a `somoszaschile@gmail.com`, que es barato y honesto; (b) construyes el reenvío, que es backend de tu carril. Si eliges (b), mira antes que no pida migración |

**Listo cuando:**

1. Con la API caída o dormida, la portada **dice** que está cargando o que falló, y ofrece
   reintentar — comprobado apagando la API, no imaginándolo.
2. Quien elige una hora, crea cuenta y la verifica, **vuelve a esa ficha con esa hora elegida**.
   Lo mismo pasando por «Entrar» → «Crea una».
3. Tras crear la cuenta, la pantalla dice el paso siguiente y a quién escribir si el correo no
   llega.
4. `/oficios` invita en vez de solo informar del vacío.
5. Ningún texto manda a hacer algo que la interfaz no ofrece.
6. Las pruebas nuevas, con su mutación. `oxlint` limpio.

---

### Tanda 4 · La búsqueda: accesibilidad, filtros y SEO

**Rama:** `web/busqueda-accesibilidad`, que ya existe.

**Lo que ya está:**

- Un commit bueno y terminado: `b0e7b8d` «Mostrar la comuna también cuando hay distancia»
  (`TarjetaPersona.tsx`). Ese **no** es de rescate.
- El commit de rescate `e2c8ffc`, que toca `PaginaBuscar.tsx` y `useMeta.ts`: saca los filtros del
  grupo «Ordenar por» y los pone en su propio `role="group"`; convierte los dos `title` en texto
  visible con `aria-describedby`; quita el `aria-live` de toda la lista y lo deja en el recuento;
  convierte el recuento en el `<h2>` que faltaba; le pone `aria-label` al `<ul>`; y añade el campo
  `titulo` a la interfaz `Meta`.

**Lo comprobado en `integracion/13-nov`**, línea por línea:

| Línea | Qué hay |
|---|---|
| `PaginaBuscar.tsx:207` | `<div className="orden-barra" role="group" aria-label="Ordenar por">`, y dentro caen los filtros, el selector de comuna y «Quitar mi ubicación» |
| `:223` | «Solo verificados» |
| `:228` | `title="Cuenta solo a quienes ya tienen 3 reseñas"` — con el dedo no hay `hover`, así que en un teléfono esa frase **no existe** |
| `:286` | `<div aria-live="polite">` envolviendo la lista entera |
| `:293` | `<h2 className="vacio-titulo">` — ya hay un `h2`, pero solo en el caso vacío |
| `:317` | `<p className="resultados-cuenta">` — el recuento, que es lo único que hay que anunciar |
| `:321` | `<ul className="tarjetas-rejilla">` sin `aria-label` |
| `:91` | `useTitulo(…)` y **ningún** `useMeta`: `/buscar` está en el mapa del sitio y sale a Google con la descripción de la portada |
| `useMeta.ts:44-48` | Fija `description`, `og:description` y `twitter:description`, y **no** `og:title`: al compartir, el título y el texto no hablan de lo mismo |

**Lo que falta en esta rama, además de terminar y probar lo de arriba:**

| Qué | Dónde | Detalle |
|---|---|---|
| `useMeta` tiene el campo `titulo` pero **no lo usa** | `useMeta.ts:41-53` | El rescate añadió el campo a la interfaz `Meta` y no tocó la función. Hoy declarar `titulo` no hace nada. Hay que fijar `og:title` y `twitter:title`, y deshacerlo al desmontar como hacen los demás |
| `/buscar` sin descripción propia | `PaginaBuscar.tsx:91` | Llamar a `useMeta` con descripción propia, que cambie con el oficio y la comuna buscados |
| El filtro «Solo verificados» puede dejar la pantalla vacía el día 23 | `PaginaBuscar.tsx:220-231` y `:293` | Mientras nadie tenga sello, el filtro parece un sitio roto. El texto de aviso que trae el rescate ayuda; mira además qué se ve al **aplicarlo** y quedarse sin nadie |
| Cerrar el menú móvil con `Escape` y devolver el foco | `marco/Cabecera.tsx:66-117` | **Comprobado:** no hay `onKeyDown`, ni `Escape`, ni manejo de foco. El menú se abre en `:66-70` y se pinta en `:74-117` |
| El desborde de la cabecera entre 880 y 1100 px | `marco/Cabecera.tsx:33-51`, `index.css:130-133` | Con sesión de prestador la cápsula lleva hasta nueve elementos. **No comprobado en navegador por nadie todavía**: hay que mirarlo en pantalla, no deducirlo del CSS |

**Listo cuando:**

1. Un lector de pantalla anuncia «Ordenar por» y «Filtrar» como dos grupos distintos; al cambiar un
   filtro lee **el recuento**, no la lista entera; y hay un `h2` entre el `h1` y los `h3` de las
   tarjetas.
2. Lo que los dos filtros hacen se lee **con los ojos en un teléfono**, sin `hover`.
3. `/buscar` sale a Google con su propia descripción, y `useMeta` cambia el `og:title` de verdad —
   con una prueba que lo mire en el `<head>`.
4. El menú móvil se cierra con `Escape` y el foco vuelve al botón que lo abrió.
5. El desborde de la cabecera entre 880 y 1100 px está **mirado en navegador**, con el resultado
   escrito en el PR aunque sea «no desborda».
6. Las pruebas nuevas, con su mutación.

---

### Tanda 5 · Mirar todo en un navegador de verdad

**Rama:** `j/navegador`, desde la rama de la Tanda 4.

**Por qué existe esta tanda.** Varias pantallas **nunca se han visto renderizadas**. Las
recomendaciones que alimentaron las tandas 1–4 salieron de leer código, no de mirar: su propio
informe lo dice («Nada de esto se vio en un navegador»). Y las tandas 1–4 acaban de cambiar cuatro
pantallas grandes. Este es el momento barato de descubrir lo que no calzó.

**Lo que hay para seguir el patrón.** `frontend/pruebas-navegador/` tiene **diez** guiones
(comprobado: `flujo-real.mjs`, `mirar-agendar.mjs`, `mirar-bloqueos.mjs`, `mirar-cercania.mjs`,
`mirar-mensajes-de-error.mjs`, `mirar-portada.mjs`, `mirar-prestador.mjs`, `mirar-publicas.mjs`,
`mirar-resenas.mjs`, `mirar-sitio.mjs`, más la carpeta `datos`). **Nota:** `ESTADO.md` habla de
«los 7 guiones de navegador en verde»; hoy hay diez archivos. Cuál es el número verde de verdad lo
dirá correrlos.

**Dos trampas del arnés que ya mordieron y siguen ahí:**

- Los barridos en cadena **agotan el limitador** (100 peticiones públicas cada 15 min) y los
  guiones informan «defectos de pantalla» que son 429. En `Development` los topes subieron; si ves
  rarezas en cadena, mira eso antes de creer en un defecto.
- Un guion que presta roles o hashes a la base de demostración **revierte arreglos en silencio**.
  Pasó con `mirar-agendar.mjs`. Ningún guion nuevo toca la base: entra con la contraseña sembrada.

**Qué hacer:**

1. Levanta todo (`.\ver-zass.bat`) y corre los diez guiones. Anota cuántos pasan **hoy**, antes de
   tocar nada: ese es tu punto de partida y va en el PR.
2. Mira a mano, en un ancho de teléfono y en uno de escritorio, las cuatro pantallas de las tandas
   1–4: `/prestadores/:slug`, `/buscar`, `/`, `/mi-ficha`.
3. Mira las que nunca se han visto: `/mis-resenas`, `/prestadores/:slug/resenas`,
   `/prestadores/:slug/resena`, `/mis-citas`, `/comunas`, `/oficios`, `/terminos`, `/privacidad`,
   y las tres de `tarjeta/*`.
4. **Mira el ancho 880–1100 px con sesión de prestador** (es el punto abierto de la Tanda 4).
5. Escribe un guion nuevo por cada defecto que encuentres y arregles, siguiendo el patrón de los
   que ya hay.

**Listo cuando:** los diez guiones están en verde o el PR dice cuál no y por qué; las pantallas de
la lista de arriba están miradas en los dos anchos; y cada defecto encontrado está **arreglado o
escrito** en el PR con su archivo y su línea. Un defecto que viste y no arreglaste es trabajo
útil si queda escrito; es trabajo perdido si no.

---

### Tanda 6 · La reseña: que diga de dónde viene, y que haya por dónde entrar

**Rama:** `j/resenas-origen`.

**Las dos mitades van juntas, y por eso son una sola tanda.** Hoy la vía «cita realizada» está
**muerta**: API-46 rechaza `Origen = Cita` (`EndpointsResenas.cs:109`, comprobado) y nadie crea
nunca un contacto ligado a una cita. La única vía viva es «contacto directo más 7 días». O sea:
alguien puede atenderte en una hora agendada por ZASS! y no poder reseñarte, y alguien que solo
apretó «llamar» sí. Y al mismo tiempo, **`MisCitas.tsx` no menciona la palabra «reseña» ni una vez**
(comprobado con `grep` sobre el archivo entero), así que la única puerta a una reseña hoy es un
correo. Poner el botón sin abrir la vía es poner un botón que responde 422.

**Decisión de Pablo ya tomada, no hay nada que preguntar:** hay que **exponer el origen de la
reseña** para poder rotular «contacto directo, sin cita», exponiendo **el origen y nada más** — ni
el id del contacto, ni el de la cita.

**Entra entera en tu carril:** `Dominio/Reputacion/`, `Aplicacion/Resenas/`, `EndpointsResenas.cs`,
`Ficha.tsx`, `ResenasDelPrestador.tsx`, `MisResenas.tsx` y `MisCitas.tsx`.

**Lo que hay que construir:**

| Qué | Dónde |
|---|---|
| Que marcar la asistencia de una cita deje al cliente elegible | `Aplicacion/Resenas/`, `EndpointsResenas.cs:101-119` (API-46) y API-47 (`:122-134`) |
| El origen expuesto en la API y rotulado en pantalla | `EndpointsResenas.cs`, `Ficha.tsx`, `ResenasDelPrestador.tsx` |
| Ofrecer «Dejar reseña» en una cita `Realizada` | `MisCitas.tsx:226-230` (la cabecera de cada fila) |
| El nombre del prestador como enlace a su ficha | `MisCitas.tsx:227` — hoy es texto plano: `{comoPrestador ? 'Te pidieron esta hora' : \`con ${cita.prestador}\`}`. **El slug ya viene en el DTO**: `funciones/agenda/servicio.ts:48`, `prestadorSlug` |

**Aviso sobre dos líneas del encargo que no calzaron:** las recomendaciones apuntaban a
`MisCitas.tsx:138` para el nombre del prestador. En `integracion/13-nov` la línea 138 es
`<PorDia citas={grupos.proximas} …/>`; el nombre se pinta en **`:227`**, dentro de `FilaDeCita`. Es
el único sitio donde aparece.

**Listo cuando:**

1. Marcar la asistencia de una cita deja al cliente elegible para reseñar, y hay una prueba que lo
   demuestra **por la API**, no por el servicio.
2. La ficha y la lista rotulan el origen.
3. **Ninguna respuesta de la API filtra un identificador interno** — compruébalo mirando el JSON
   que sale, no el DTO que escribiste. Un DTO limpio con un serializador distraído sigue filtrando.
4. La regla de las 72 h de edición sigue valiendo igual por las dos vías.
5. Desde «Mis citas», una cita `Realizada` ofrece escribir la reseña, y el enlace lleva a una
   pantalla que **acepta** la reseña (ese es el punto de fundir las dos mitades).
6. El nombre del prestador enlaza a su ficha.
7. Las pruebas nuevas, con su mutación. **Cuidado con el punto 4 de §5**: si tu doble de prueba de
   elegibilidad filtra por su cuenta, puedes borrar la guarda de producción y seguir en verde.

---

### Tanda 7 · Que ZASS! avise por correo cuando algo pasa con una cita

**Rama:** `j/correo-citas`. **Es N3 por el correo, y es la que Pablo autorizó por escrito en §4.**

**Es el hueco más grande del carril:** `ServicioAgenda` no avisa a nadie, nunca. Hoy pides una hora
y el prestador se entera si se le ocurre entrar al panel. Para una plataforma de citas, eso no es
una falta: es el producto a medias.

**Lo comprobado en `integracion/13-nov`:**

- El enum ya declara los cinco tipos: `CitaSolicitada, CitaConfirmada, CitaRechazada,
  CitaCancelada, CitaReprogramada` (`Dominio/Comun/Enumeraciones.cs:53`).
- `Plantillas.TienePlantilla` (`Infraestructura/Correo/Plantillas.cs:52-59`) reconoce **siete**
  tipos: `CorreoVerificacion`, `RecuperarContrasena`, `ResenaNueva`, `ResenaRespondida`,
  `SuscripcionPorVencer`, `SuscripcionVencida` y `PerfilPublicado`. **Ninguno `Cita*`.**
- `ProcesadorDeCorreo` **deriva de `TienePlantilla` qué filas consulta**
  (`Infraestructura/Correo/ProcesadorDeCorreo.cs:26-31`), y no de una lista a mano.
- **No hace falta migración:** `Notificacion.Tipo` se guarda como texto de hasta 40 caracteres
  (`Datos/ZassDbContext.cs:835`: `e.Property(x => x.Tipo).HasConversion<string>().HasMaxLength(40)`).
  **Compruébalo tú en `pg_constraint`** antes de confiar en esta línea: que el modelo no declare un
  `CHECK` no prueba que la base no lo tenga.

**Cuidado con el orden, porque esto ya pasó dos veces:** si encolas un tipo que `TienePlantilla` no
reconoce, el despacho **no lo consulta nunca** y la notificación queda `Programada` para siempre,
con 0 intentos y sin error. Quien mire la cola verá avisos «en camino» que no van a salir jamás.
Así que **la plantilla primero, el encolado después**.

Y la otra cara, que también pasó: **al darle plantilla a un tipo, todo lo acumulado se vuelve
enviable de golpe**. Por eso el procesador anula lo que lleva más de 7 días esperando
(`OpcionesCorreo.CaducaDias`), mirando `intentos = 0`, porque `programada_para` deja de decir
cuándo se encoló en cuanto el aviso se reserva. **No toques esa regla**: es parte del borde N3.

**Decisión tuya, no mía: qué lleva el correo.** Mi recomendación es día, hora, oficio y un enlace
al panel, y **nunca** el teléfono ni el correo de la otra persona: eso se ve entrando, que es donde
queda registrado quién vio qué.

**Listo cuando:**

1. Los cinco tipos tienen plantilla y texto.
2. La agenda encola en los cinco momentos (pedir, confirmar, rechazar, cancelar y reprogramar),
   avisando **a quien le pasó la cosa**, no a quien la hizo.
3. El aviso llega una sola vez aunque el caso corra dos veces (clave determinista, CC-04).
4. Existe una prueba que **cae** si alguien agrega un tipo `Cita*` sin plantilla.
5. Lo viste salir de verdad en local, no solo pasar en una prueba.
6. Las pruebas nuevas, con su mutación, y el PR dice que comprobaste `pg_constraint`.

---

### Tanda 8 · El panel interno, que existe en la API y no existe para nadie

**Rama:** `j/panel-interno`.

Hay cinco endpoints de revisión interna sin una sola pantalla, así que hoy **nadie puede verificar
una credencial ni resolver un reporte**, que son dos de las promesas del producto.

| Endpoint | Dónde | Qué exige |
|---|---|---|
| API-29 `GET /api/interno/credenciales/cola` | `EndpointsVerificacion.cs:24-32` | rol `verificador` |
| API-30 tomar | `EndpointsVerificacion.cs:35-43` | rol `verificador` |
| API-31 resolver | `EndpointsVerificacion.cs:46-57` | rol `verificador` |
| API-52 cola de reportes | `EndpointsResenas.cs:233-242` | **rol `moderador`** |
| API-53 resolver reporte | `EndpointsResenas.cs:245-257` | **rol `moderador`** |

**Esto ya no necesita ninguna decisión, y aquí está la respuesta a la pregunta que el plan anterior
dejaba abierta.** Comprobado hoy:

- El grupo `/api/interno` entero exige `Politicas.Verificador` (`EndpointsVerificacion.cs:19-20`),
  y la política existe: `Configuracion/Seguridad.cs:32-33` y `:150-153`
  (`p.RequireRole("verificador")`). Responde 403 **sin ejecutar nada**, tampoco para listar.
- **Los reportes de reseñas son del otro lado: exigen `moderador`, no `verificador`**
  (`EndpointsResenas.cs:229`: `interno.RequireAuthorization(p => p.RequireRole(Roles.Moderador))`,
  y `Dominio/Comun/Enumeraciones.cs:92`: `public const string Moderador = "moderador"`). **Son dos
  colas con dos roles distintos**, y la pantalla tiene que respetarlo: a quien solo es
  `verificador` no se le ofrece la cola de reportes, y al revés.

**Tres cosas que la pantalla tiene que respetar porque la API ya las impone:** la reserva de una
credencial dura **30 minutos** (CC-03); nadie revisa la propia (RN-S2-09); y **403 y 404 responden
igual a propósito**, así que la pantalla no puede dar a entender cuál de los dos fue.

**Y una cuarta, de tu lado:** las rutas nuevas del panel van en `public/robots.txt` y piden
`noindex`, o la prueba `frontend/src/pruebas-functions/sitemap.prueba.js:206` te deja en rojo. Es
un rojo que muerde a propósito: no lo silencies, actualiza el archivo.

**Esta tanda no espera a nadie.** Quién va a sellar las credenciales de los primeros 50 es una
decisión de Pablo (§9.7), pero es una decisión sobre **quién usa** la pantalla, no sobre si se
construye. Constrúyela.

**Listo cuando:**

1. Un usuario con rol `verificador` entra, ve su cola, toma un caso, lo resuelve y ve el resultado.
2. Un usuario con rol `moderador` hace lo mismo con la cola de reportes.
3. A quien no tiene el rol **no se le ve ni el enlace**, y si llega a la ruta a mano no se entera
   de si existe o no.
4. Existe una prueba de que tomar un caso ya tomado por otro **no lo roba**.
5. La reserva de 30 minutos se nota en pantalla.
6. `robots.txt` cubre las rutas nuevas y la prueba del mapa del sitio está verde.
7. Las pruebas nuevas, con su mutación.

---

### Tanda 9 · Los huecos de la cuenta

**Rama:** `j/cuenta`.

- **Eliminar la cuenta (API-17).** El endpoint existe y anonimiza (RN-S1-07):
  `EndpointsIdentidad.cs:174-182`, `DELETE /api/mi/cuenta`. **No hay pantalla.** Pide confirmación
  escrita, explica qué se borra y qué no, y no dejes a la persona en una pantalla muerta después.
- **Credenciales, el camino completo.** Tras la Tanda 1 ya se puede declarar y enviar a revisión.
  Recorre el camino entero en un navegador y cierra lo que falte. Si subir el documento todavía no
  se puede (MOD-08), escribe en el PR exactamente qué falta y hasta dónde llegaste.

**Listo cuando:** una persona puede borrar su cuenta sola, entendiendo qué pasa con sus citas y sus
reseñas; y un prestador lleva una credencial desde que la crea hasta que queda en la cola de
revisión, **visto en un navegador**. Con sus pruebas y su mutación.

---

### Tanda 10 · Fotos de perfil

**Rama:** `j/fotos`. **Varios días.**

**Comprobado:** `urlFoto` ya viaja por todas partes —`Aplicacion/Busqueda/IServicioBusqueda.cs:87`,
`:186` y `:207` (los tres DTO), `Aplicacion/Identidad/IServicioIdentidad.cs:25`,
`Aplicacion/Puertos/Puertos.cs:382` y `:448`, y el dominio en
`Dominio/Identidad/Entidades.cs:114`— y **ninguna pantalla lo pinta**: la ficha usa `<Monograma>`
(`Ficha.tsx:117`) y la cara de todos son iniciales. Para un oficio de confianza, la foto es la
mitad de la ficha.

**Lo que hay que decidir y hacer tú:** dónde se guarda el archivo, cómo se sube, qué tamaños y qué
pasa con quien no tiene foto (el monograma sigue siendo la respuesta correcta). **Dos bordes:**

1. **Si esto pide migración, para** (§2.4.1). La columna ya existe en el dominio; comprueba si
   existe en la base antes de asumirlo.
2. `MiFicha.tsx:285-286` dice hoy que no se puede subir foto. Cuando se pueda, esa frase cambia —
   y si se queda a medias, tiene que decir la verdad de lo que quedó.

**Listo cuando:** un prestador sube su foto desde el panel, se ve en su ficha, en la tarjeta de
resultados y en el panel; quien no tiene sigue viendo su monograma sin que nada se rompa; el
archivo tiene un tope de tamaño y de tipo comprobado **en el servidor**, no solo en el navegador; y
visto en un navegador. Con sus pruebas y su mutación.

---

### Tanda 11 · Páginas de aterrizaje por oficio y comuna

**Rama:** `j/aterrizaje`. **Varios días, y es lo que más paga a mediano plazo.**

Hoy `/oficios` y `/comunas` enlazan a `/buscar?q=…` (`Oficios.tsx:42`, comprobado), y eso Google
**no lo indexa con contenido propio**: es una búsqueda, no una página. «Abogado en Temuco» es como
se busca en Chile, y hoy ZASS! no tiene una página que se llame así.

**Lo que hay para apoyarse:** `frontend/functions/prestadores/[slug].js` ya hace etiquetas del lado
del servidor para las fichas, y `functions/_comun/seo.js` ya escapa todo texto de usuario
(`:31-54`). Ahí está el patrón.

**Tres cosas que no se pueden olvidar:**

1. Las rutas nuevas van al mapa del sitio (`functions/_comun/sitemap.js`) y **a la prueba**
   (`frontend/src/pruebas-functions/sitemap.prueba.js`).
2. Una página de aterrizaje vacía es peor que no tenerla: decide qué dice una comuna donde todavía
   no hay nadie, y que no sea una página en blanco indexada.
3. **No inventes combinaciones**: si son 346 comunas por N oficios, decide el criterio y escríbelo.

**Listo cuando:** existe una ruta por oficio y comuna, con título, descripción y contenido propios
servidos **sin ejecutar JavaScript**; está en el mapa del sitio; la prueba del mapa está verde; y
se ve en un navegador con y sin gente en la comuna. Con sus pruebas y su mutación.

---

## 9. LO DE PABLO — doce puntos

Cada uno dice **qué hay que hacer**, y trae el dato cuando ya está medido. Ninguno de estos lo
puede hacer Joaquín: o hacen falta llaves, o hace falta una decisión que es de Pablo.

| # | Qué | Por qué es tuyo | Cuándo |
|---|---|---|---|
| 1 | **Mergear** | Solo tú mergeas a `main` | Desde ya, y seguido |
| 2 | **Aplicar la migración de cobro en local** | Hace falta la clave de PostgreSQL local | Antes de que Claude siga |
| 3 | **Migrar Neon** | Solo tú tienes producción | Antes del 23 |
| 4 | **Encender la bandera de verificación en Render** | Solo tú tienes Render | Antes del 23 |
| 5 | **Manual Deploy y rotar los tres secretos** | Solo tú | Antes del 23 |
| 6 | **Decidir qué hacer con el plan gratis de Render** | Es plata | Antes del 23 |
| 7 | **Decidir quién sella las credenciales de los primeros 50** | Es una decisión de operación | Antes del 23 |
| 8 | **Los cuatro huecos legales, y el abogado** | Son datos tuyos y una revisión profesional | Antes del 23 |
| 9 | **La exclusión de Windows para la carpeta del repositorio** | Es tu PC | Cuanto antes |
| 10 | **Un cliente `pg_restore` 18** | Es tu PC y tu respaldo | Cuanto antes |
| 11 | **La fecha de vencimiento del cupón** | Es comercial | Antes de que Claude cierre el cupón |
| 12 | **Reemplazar los datos de demostración** | Es tuyo decidir con qué se abre | **El 23, antes de abrir** |

### 9.1 Mergear

- **PR #11** — `integracion/13-nov` → `main`. **49 commits, CI en verde.** Es la llave de todo: la
  regla de **una migración de EF a la vez** convierte este merge pendiente en un bloqueo de
  desarrollo. `main` no tiene nada que esa rama no tenga.
- **PR #12** — este plan.
- **Los PR que vaya abriendo Joaquín**, uno por tanda. Él no te espera para seguir (§3), así que te
  van a llegar encadenados: el segundo contiene al primero hasta que mergees el primero. Es normal
  y es a propósito.
- **Antes de creerle al verde:** `gh run list` puede mostrar un `success` que es del flujo *Copia
  de la base de datos* y no del de *Pruebas*. Mira el nombre del flujo.

**Listo cuando:** `main` tiene lo de `integracion/13-nov` y el flujo *Pruebas* sobre `main` está en
verde.

### 9.2 Aplicar la migración de cobro en local

Claude dejó **`20261113205944_CobroIntentosYCupones`** escrita y **sin aplicar** (intentos de pago
TBL-36, cupones TBL-34 y canjes, con `ux_pago_intento`). Aplicarla pide la clave de PostgreSQL
local, que está en `.clave-pg.txt` y **Claude no la lee nunca**.

```
.\iniciar-postgres.bat
$env:ASPNETCORE_ENVIRONMENT = "Development"
dotnet run --project backend\src\Zass.Api -- --migrar
```

**Listo cuando:** sale con código 0 y **compruebas en la base** que las tablas y el índice único
existen (`pg_indexes`, `information_schema`), no solo que el comando dijo «Done».

### 9.3 Migrar Neon

**Producción sigue con 20 cupos, no 50.** Hay tres migraciones pendientes de producción, en este
orden: `20261112175540_CuposFundadorA50`, `20261112211216_NoAsistioQuienFalto` y
`20261113205944_CobroIntentosYCupones`.

**Listo cuando:** producción muestra **50** cupos y lo compruebas consultando la base, no el panel.

### 9.4 Encender `Zass:Verificacion:PermitirSinDocumentos` en Render

**Esto es lo que desbloquea la Tanda 1 en producción, y es un interruptor.** Es un `bool` sin valor
por omisión, o sea **falso**, y está en `true` **solo** en `appsettings.Development.json:14`
(comprobado). Sin encenderla, el botón «Enviar a revisión» responde «Sube al menos un documento» y
el sello sigue sin poder darse — y el filtro «Solo verificados» de la búsqueda sigue devolviendo a
nadie el día 23.

La clave exacta es `Zass:Verificacion:PermitirSinDocumentos`
(`Aplicacion/Credenciales/IServicioVerificacion.cs:38`).

**Listo cuando:** en producción, una credencial sin documentos pasa a revisión.

### 9.5 Manual Deploy en Render y rotar los tres secretos

`render.yaml` tiene `autoDeploy: false`, así que **un merge a `main` no publica nada**: hay que
apretar Manual Deploy. Y los tres secretos siguen sin rotar desde que se publicó.

**Listo cuando:** lo desplegado en Render corresponde al `main` de hoy, y los tres secretos son
nuevos.

### 9.6 El plan gratis de Render, y los 25,9 segundos

**Medido hoy, no recordado:** `/api/salud` respondió 200 **en 25,9 segundos** tras estar dormida.
**El primer visitante del día ve eso.** Y hay una segunda cara: el envío de correo y el
`Planificador` son `BackgroundService` **dentro del proceso de la API**, así que dormida **no vacía
la cola de correo**. Eso toca directamente la Tanda 7: los avisos de cita pueden quedarse esperando
a que alguien despierte la API.

Que se pague el plan o se viva con eso **es decisión tuya**. La Tanda 3 de Joaquín hace que la
portada al menos no se quede muda mientras despierta, pero eso es maquillaje sobre 25 segundos.

**Listo cuando:** está decidido y escrito en `ESTADO.md`. Decidir «vivimos con eso» también cuenta,
siempre que quede escrito.

### 9.7 Quién sella las credenciales de los primeros 50

El rol `verificador` existe y su política también (`Configuracion/Seguridad.cs:32-33` y `:150-153`),
y Joaquín construye la pantalla en la Tanda 8. Lo que falta es **quién se sienta a revisar, con qué
criterio y en cuánto tiempo responde**. Hoy, sin pantalla, sellar exige llamar la API a mano.

Hay un segundo rol que decidir: **`moderador`**, que es el que exigen las dos colas de reportes de
reseñas (§8, Tanda 8). Son dos personas o una con dos roles, pero hay que decirlo.

**Listo cuando:** hay un nombre por cada rol, y está dicho en cuánto se promete responder.

### 9.8 Los cuatro huecos legales, y el abogado

Los cuatro se ven **resaltados** hoy en `/terminos` y `/privacidad`. Están en
`frontend/src/paginas/legal.ts` y son constantes con el hueco escrito dentro (comprobado):

| Constante | Línea | Lo que dice hoy |
|---|---|---|
| `CORREO_PRIVACIDAD` | `:15` | `'[correo de privacidad por completar]'` |
| `RESPONSABLE` | `:12` | `'[Razón social y RUT por completar]'` |
| `PLAZO_REGISTROS_DE_SEGURIDAD` | `:21` | `'[plazo por completar]'` |
| `PROVEEDORES` | `:32` | `'[lista de proveedores por completar]'` |

Y `BORRADOR_LEGAL` (`:9`) sigue en `true`, que es lo que pinta el resaltado. **Baja a `false` solo
cuando un abogado haya revisado las dos páginas.** Eso no lo arregla ninguna rama y no lo puede
hacer Joaquín.

Dos avisos: el plazo de los registros de seguridad **no se puede inventar**, porque hoy el sistema
no los borra solo — decidir el plazo implica construir el borrado o decir que es manual. Y el
correo de privacidad, si no hay otro, es `somoszaschile@gmail.com`.

**Listo cuando:** las cuatro constantes tienen valor real, un abogado revisó las dos páginas, y
`BORRADOR_LEGAL` está en `false`.

### 9.9 La exclusión de Windows para la carpeta del repositorio

Hoy Windows **bloquea el `Zass.Infraestructura.dll` recién compilado** al cargarlo por reflexión
(`FileLoadException 0x800711C7`) y por eso **la suite de arquitectura no corre en local**.

**Comprobado en las dos direcciones, que es lo que lo convierte en diagnóstico y no en sospecha:**
con el DLL del 12-nov pasan **16/16**; con el de hoy caen **9 antes de ejecutar nada**.

Mientras no esté, esas 16 pruebas solo las valida GitHub Actions, y ni Joaquín ni Claude pueden
comprobar una regla de arquitectura en su máquina.

**Listo cuando:** `cd backend; dotnet test` pasa las 16 de arquitectura en tu PC.

### 9.10 Un cliente `pg_restore` 18

**Hoy no hay restauración de emergencia posible en ese PC.** Producción (Neon) es PostgreSQL **18**
y el clúster portable local es **16.4**: `pg_restore` 16.4 **rechaza** un volcado de `pg_dump` 18.
O sea que el respaldo que se está guardando no se puede devolver.

**Listo cuando:** existe un `pg_restore` 18 en el PC y **restauraste un volcado de prueba con él**.
Tener el binario no es tenerlo probado.

### 9.11 La fecha de vencimiento del cupón

Hoy está en **31-mar-2027**, **sin confirmar**. Es del carril de Claude escribirla; es tuyo
decidirla.

**Listo cuando:** la fecha está confirmada y escrita en `ESTADO.md`.

### 9.12 Reemplazar los datos de demostración

El sitio está sembrado con gente de demostración, con contraseña real (`demo-zass-2026`) y dos
roles, en los dos sembrados. **El día 23 eso no puede estar visible.**

Decide qué va en su lugar: gente real que ya se inscribió, o un sitio vacío que dice honestamente
que está empezando. Las dos son defendibles; la que no lo es es abrir con prestadores inventados.

**Listo cuando:** producción no tiene ni una ficha de demostración y la contraseña sembrada no
entra en ningún lado.

---

## 10. Si algo de aquí no calza con el código

Pasa, y es más útil decirlo que adaptarse. Si una línea de este plan no coincide con lo que ves,
**no adaptes la tarea por tu cuenta y tampoco corrijas en silencio**: escribe en el PR qué dice el
plan, qué dice el código y qué hiciste. Los identificadores (`CU-`, `TBL-`, `API-`, `RN-`, `MOD-`,
`R-`, `CC-`) son del documento, y cuando el código y el documento no coinciden, lo que no se puede
hacer es dejar la discrepancia callada.

**Este plan ya trae tres de esas correcciones hechas, para que nadie las vuelva a buscar:**

1. El nombre del prestador en «Mis citas» está en **`MisCitas.tsx:227`**, no en `:138`.
2. Las colas de reportes de reseñas exigen el rol **`moderador`**, no `verificador`
   (`EndpointsResenas.cs:229`), y viven en `:233` y `:245`.
3. En `frontend/pruebas-navegador/` hay **diez** archivos de guion, no siete.

Y trae tres cosas marcadas como **sin comprobar**, que no se pueden dar por ciertas sin mirarlas:
si la fecha de alta viaja en el DTO de la ficha (Tanda 2); si la cabecera desborda entre 880 y
1100 px (Tanda 4); y si la base tiene un `CHECK` sobre `notificaciones.tipo` (Tanda 7).

---

## 11. Las cuatro ramas de rescate, en una tabla

Para que nadie las pierda. **Su trabajo está en commits marcados «WIP RESCATE»: sin pruebas y sin
comprobar, y el propio mensaje del commit lo dice.** No son entregas: son trabajo salvado de un
`checkout` que lo habría borrado.

| Rama | Commits suyos | Qué trae | Tanda |
|---|---|---|---|
| `web/panel-prestador` | `2ab306c` (rescate) | `MisCredenciales.tsx`, `funciones/perfil/servicio.ts`, `MisCredenciales.prueba.tsx` | **1** |
| `web/ficha-movil` | `ff368fb` (rescate) | `Ficha.tsx`, `index.css`, `Ficha.prueba.tsx` | **2** |
| `web/portada-entrada` | `d2a7020` (bueno) + `7da4bb6` (rescate) | CSRF sin asumir JSON; `Portada.tsx` | **3** |
| `web/busqueda-accesibilidad` | `b0e7b8d` (bueno) + `e2c8ffc` (rescate) | la comuna con distancia; `PaginaBuscar.tsx`, `useMeta.ts` | **4** |

Los dos commits marcados «bueno» (`d2a7020` y `b0e7b8d`) están terminados y no hay que rehacerlos:
léelos antes de tocar sus archivos.
