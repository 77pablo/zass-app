# Plan para terminar la web de ZASS!

**Para:** Pablo, Joaquín y Esteban · **Hecho:** 21 de noviembre de 2026 · **Medido contra:** `main` = e6f5ea1, la documentación (20 casos de uso, 19 secundarios, 71 endpoints) y los PR abiertos.

Este plan reparte **todo lo que falta** para que la web quede terminada. Cada uno tiene su carril y sus archivos, para que no choquen al unir el trabajo. Las fechas son una propuesta: lo que no se puede mover es el orden.

---

## 1. Qué significa «la web está terminada»

La web está terminada cuando se cumplen las diez condiciones. Ninguna se da por hecha con una prueba en verde: cada una se comprueba **en un navegador de verdad, en el celular y en el escritorio, en producción**.

| # | Condición | Cómo se comprueba |
|---|---|---|
| 1 | Los 20 casos de uso y los 19 secundarios tienen **pantalla y servidor** | Recorrido a mano de cada uno, con la lista de la sección 7 |
| 2 | Los 71 endpoints documentados existen en el código | Hoy faltan 4 (API-06 ya está; faltan **API-43, API-45 y API-60**) |
| 3 | Todas las pantallas tienen el **estilo iPhone** | Capturas a 390 px y a 1280 px de cada ruta |
| 4 | Un prestador puede **pagar de verdad** con Flow en producción y recibe su boleta | Un pago real de prueba, devuelto después |
| 5 | Subir fotos y documentos funciona **en producción** (R2) | Subir una foto y verla en la ficha pública |
| 6 | Producción tiene todas las migraciones y la última versión de `main` | Comparar la lista de migraciones de Neon con la del código |
| 7 | Los textos legales los revisó un abogado | Correo del abogado |
| 8 | Los secretos que pasaron por el chat están **rotados** | Neon, Cloudflare y Resend con claves nuevas |
| 9 | Se probó **restaurar una copia** de la base | Restaurar en una base aparte y contar filas |
| 10 | `zass.cl` está **abierto** (sin la página de mantenimiento) y sin datos de demostración | Entrar desde un celular que nunca la visitó |

---

## 2. Dónde estamos (medido el 21-nov)

**Ya está en `main`:** registro con RUT, inicio de sesión, verificación de correo y recuperación de contraseña · búsqueda por cercanía en todo Chile · ficha pública · credenciales con documentos y la mesa del Verificador · agenda, citas automáticas y manuales, reprogramar, cancelar y asistencia · reseñas, respuesta, reporte y moderación · tarjetas NFC: activar, desactivar, reemplazar, bloqueo y escaneos · suscripción con mes gratis, cupo fundador sin tarjeta, gracia de 7 días y Flow · los tres planes (Esencial, Profesional y Equipo inactivo) con verificación prioritaria · foto de perfil · panel interno con auditoría · servidor en .NET 10 · sitemap e imagen para compartir.

**Lo que falta**, en cuatro grupos:

| Grupo | Qué falta |
|---|---|
| **Pantallas sin construir** | Los tres planes en columnas (`/planes`) · fotos de trabajos · indicadores del panel · mis estadísticas · **«Mi cuenta»** (datos, contraseña, sesiones, avisos, eliminar cuenta) · **favoritos y preferencias del buscador** (CU-04, CS-08: el servidor existe, la pantalla no) · botón **«Guardar contacto»** (vCard, CS-19: el servidor existe, el botón no) |
| **Servidor** | Fotos de trabajos (T69, en curso) · push y preferencias de aviso (API-43, API-45) · indicadores (API-60, en el #163) · boleta electrónica · crear cupones sin tocar la base (el cupón ya existe y se canjea en `/planes`, pero hoy solo se crea con SQL) |
| **Estilo iPhone** | Solo 4 pantallas lo tienen (Inicio, Buscar, Ficha y Mis citas). Faltan las demás, unas 25 rutas |
| **Para abrir** | Migrar Neon y desplegar · R2 · Flow en producción (necesita el SII) · abogado · rotar secretos · probar la restauración · quitar el mantenimiento |

---

## 3. El reparto: tres carriles

| Carril | Quién | Qué es suyo | Sus archivos (para no chocar) |
|---|---|---|---|
| **P · Plataforma y producción** | **Pablo con Claude** | Funciones completas que cruzan servidor y pantalla, producción, seguridad, la prueba final y la apertura | `backend/` de búsqueda, perfiles y archivos · `frontend/src/paginas/` de favoritos · `.github/` · `render.yaml` · `frontend/functions/` |
| **J · Interfaz** | **Joaquín** | Todo lo que se ve: el estilo iPhone en todas las pantallas, los planes, las fotos de trabajos, el panel y las estadísticas | `frontend/src/index.css` · `frontend/src/marco/` · `frontend/src/componentes/` compartidos · las páginas que rediseña |
| **E · Cuenta, avisos y cobros** | **Esteban** (≈15 h a la semana) | La cuenta del usuario, las notificaciones push, Flow en producción, la boleta y el cupón | `backend/` de suscripciones, pagos y notificaciones · `frontend/src/paginas/` de «Mi cuenta» |

**La regla para no chocar:** `index.css` y los componentes compartidos (botones, tarjetas, barra inferior) son **solo de Joaquín**. Si Pablo o Esteban necesitan un estilo nuevo, usan las clases que ya existen o se lo piden a Joaquín en el PR. Así, al unir, nadie pisa el CSS de otro.

---

## 4. Las tareas, carril por carril

El nivel dice cuánto cuidado pide: **N1** sin riesgo (textos, documentos) · **N2** pantalla o lógica normal · **N3** toca la base de datos, los pagos o la seguridad (lleva migración o prueba de mutación).

### Carril P — Pablo con Claude

> **Avance al 22-nov-2026 (mañana):** P-1 (#192), P-2 y P-3 (#195) y P-4 (#193) **hechos y en `main`**.
> De P-6, la parte de código avanzó mucho: una auditoría de endpoints encontró 11 problemas y quedaron
> arreglados en #193, #194, #196 y #198 (el más serio: **un prestador podía hacer que el cobro
> automático de otra persona no ocurriera**). Además apareció y se hizo **P-10** (abajo).
> Siguen pendientes: P-5, P-7, P-8, P-9, el resto de P-6 (rotar secretos) y **P-11**.

| ID | Tarea | Nivel | Depende de | Entrega |
|---|---|---|---|---|
| **P-1** | **Fotos de trabajos, servidor** (T69, la está haciendo un modelo gratis): revisar lo que deje, completar lo que falte, migración `FotosDeTrabajos`, límite por plan (`maxFotosTrabajos`) | N3 | — | PR con CI verde y mutaciones que caen |
| **P-2** | **Favoritos** (CS-08): corazón en la ficha y en los resultados; pantalla «Guardados» en la barra; sin cuenta, el corazón invita a crearla. Usa API-23; si falta listar los favoritos, se agrega el GET | N2 | — | PR con la pantalla y las pruebas |
| **P-3** | **Preferencias del buscador** (CU-04): comuna y rubros de interés; la portada los usa como punto de partida | N2 | P-2 | PR |
| **P-4** | **«Guardar contacto»** en la ficha (CS-19): botón que descarga la vCard (API-07, ya existe). Solo con los canales que el prestador marcó como públicos | N2 | — | PR chico |
| **P-5** | **Producción al día**: correr `migrar-neon` con todas las migraciones nuevas y desplegar `main`. Comprobar desde fuera: salud, planes, cupos, subir foto | N3 | JQ-17 y P-1 en `main` | Producción = `main` |
| **P-6** | **Seguridad antes de abrir**: rotar Neon, Cloudflare y Resend · revisión de seguridad de la rama entera (`/security-review`) · cabeceras y límites de peticiones en cada endpoint nuevo · nada de secretos en el repo (es público) | N3 | — | Informe y PR con lo que salga |
| **P-7** | **Probar la copia de seguridad**: restaurar Neon en una base aparte (con las herramientas de su misma versión, no con el PostgreSQL 16 local) y contar filas | N3 | — | Restauración comprobada y escrita en `ESTADO.md` |
| **P-8** | **La prueba final**: los 39 casos de uso recorridos en el celular y en el escritorio, en producción, con la lista de la sección 7. Lo que falle vuelve al carril de su dueño | N2 | Todo lo anterior | Lista firmada |
| **P-9** | **Abrir `zass.cl`**: borrar los datos de demostración, quitar el mantenimiento, comprobar desde un celular nuevo | N3 | P-8 y el abogado | La web abierta |
| **P-10** | **Activar la tarjeta NFC desde la web** (CU-19): no se podía (no había pantalla y la Function de Cloudflare rechazaba el POST). **Hecho en #199**: `/tarjeta/sin-activar/{segmento}` pide el código; API-55 pasó a `/api/mi/tarjetas/{segmento}/activar`. Falta 4A: si la tarjeta ya es tuya, llevarte a tu panel | N3 | — | Hecho |
| **P-11** | **Cerrar la API directa en Render** (decisión de Pablo: *antes de abrir*, no ahora). La URL de Render está en el repo público y no hay secreto compartido Cloudflare↔API, así que se saltan el modo mantenimiento y los límites por IP. Claude programa el secreto (`X-Origen`); Pablo pone las dos variables | N3 | Antes de P-9 | Secreto en Render y Cloudflare |

### Carril J — Joaquín

Empieza por lo que **bloquea la publicación** (JQ-17) y después pasa al estilo iPhone, pantalla por pantalla.

| ID | Tarea | Nivel | Depende de | Entrega |
|---|---|---|---|---|
| **#171** | Las pantallas del pago con estilo (`/planes`, `/pago/inscripcion`, `/planes/listo`). Va primero porque JQ-17 se apoya en él | N2 | — | Fusionado |
| **JQ-17** | **La pantalla de los tres planes**: tres columnas, el Profesional al centro con «El más elegido» (primero en el celular), lo que incluye cada uno leído de `/api/planes`, el Equipo como «Próximamente», el aviso del Fundador con los cupos que quedan y «Ningún plan compra un mejor lugar en la búsqueda» | N2 | #171 | **Bloquea P-5**: es la primera de la cola |
| **#167** | Mis estadísticas (JQ-8): resolver los 4 choques con `main` y mostrarlas **solo** si el plan tiene `permiteEstadisticas`; a quien no lo tiene, qué se pierde y un botón para pasarse al Profesional | N3 | — | Fusionado |
| **#163** | Indicadores del panel (JQ-14, API-60): resolver los 6 choques con `main` | N3 | — | Fusionado |
| **JQ-18** | **Fotos de trabajos, pantalla**: subir, ordenar y quitar en «Mi ficha», y la galería en la ficha pública. El límite lo da el plan | N2 | P-1 | Fusionado |
| **#168 · #169** | La foto en la portada y en la barra (JQ-15) · avisar y borrar los borradores de credencial abandonados (JQ-16) | N2 · N3 | — | Fusionados |
| **JQ-19a** | **Estilo iPhone: la cuenta**: entrar, crear cuenta, recuperar y restablecer contraseña, verificar correo | N2 | — | Capturas antes y después a 390 y 1280 px |
| **JQ-19b** | **Estilo iPhone: el prestador**: ofrecer servicios, Mi ficha, Mi agenda, Mis tarjetas, Mis reseñas, Mis credenciales | N2 | — | Ídem |
| **JQ-19c** | **Estilo iPhone: reseñas, tarjetas y legal**: escribir reseña, todas las reseñas, `t/:segmento` y los tres estados de tarjeta, términos, privacidad, profesionales | N2 | — | Ídem |
| **JQ-19d** | **Estilo iPhone: el panel interno**: Verificador, reportes, tarjetas, auditoría, indicadores (en el escritorio, estilo iPad) | N2 | #163 | Ídem |
| **JQ-22** | **«Guardados» en la navegación** (#195): hoy `/mis-guardados` solo se alcanza desde la ficha y la portada. Va en la `Cabecera` y en la barra inferior, y el ícono de corazón (hoy es `estrella`). Mirar también en el celular la pantalla de activar tarjeta (#199) | N2 | — | Fusionado |
| **JQ-20** | **Los borradores heredados**: actualizar con `main` y fusionar #130, #106, #107 y #98; **rehacer** #103 (choca con `index.css`) y #128 (la página de mantenimiento sigue en el estilo v3) o cerrarlos | N2 | — | Ninguno abierto |
| **JQ-21** | Correr `verificacion-interna.mjs` del #159 y dejar el resultado en el PR | N1 | — | Hecho |

### Carril E — Esteban

| ID | Tarea | Nivel | Depende de | Entrega |
|---|---|---|---|---|
| **ES-3** | **Notificaciones push y preferencias de aviso** (API-43, API-45, CS-14): registrar la suscripción del navegador, elegir correo, push o ambos, y que los recordatorios de cita respeten esa elección | N3 | — | PR (ya está en tu plan) |
| **ES-4** | **«Mi cuenta»**, una pantalla nueva: cambiar nombre y teléfono, cambiar contraseña, cerrar todas las sesiones (API-12, CS-03), las preferencias de aviso de ES-3 y el enlace a eliminar la cuenta | N2 | ES-3 | PR |
| **ES-5** | **Eliminar mi cuenta** (CS-04): rescatar el #76 (tiene el CI en rojo), actualizarlo con `main`, que cancele las citas y la suscripción, y que borre fotos y documentos de R2. Lo exige la Ley 21.719 desde diciembre | N3 | ES-4 | #76 fusionado |
| **ES-6** | **Flow en producción**: credenciales reales, aviso de pago contra la URL pública, un pago real de prueba y su devolución, y revisar que el cupo y la suscripción quedaron bien en la base | N3 | El SII (inicio de actividades) | Pago real comprobado |
| **ES-7** | **La boleta**: con inicio de actividades, cada cobro necesita boleta electrónica. Flow cobra pero no la emite. Investigar las opciones (emitirla en el portal del SII o integrar un emisor por API), con su costo, y **proponerla a Pablo antes de programar nada** | N1 → N3 | El SII | Propuesta escrita y, si Pablo dice sí, la integración |
| **ES-8** | **Cupones desde el panel**: el cupón ya existe (código, porcentaje, vencimiento; API-67 lo cotiza y `/planes` lo canjea), pero solo se crea con SQL. Falta: crear, desactivar y ver cuántas veces se usó, desde `/interno` (solo administrador, con auditoría), y comprobar que funciona con los tres planes | N3 | JQ-17 | PR |

---

## 5. Calendario

| Fase | Fechas | Meta | Pablo y Claude | Joaquín | Esteban |
|---|---|---|---|---|---|
| **0 · Postulación** | 21 al 24-nov | Enviar la postulación **sin dejar de avanzar** | P-1 (la T69) · P-4 | #171 → **JQ-17** | ES-3 |
| **1 · Lanzable** | 25-nov al 5-dic | Todo lo necesario para abrir en Temuco | P-5 · P-6 · P-7 · P-2 | #167 · #163 · JQ-18 · JQ-19a | ES-4 · ES-5 · ES-6 · ES-7 (propuesta) |
| **2 · Completa** | 6 al 19-dic | La web entera, con estilo iPhone en todo | P-3 · **P-8** · **P-9** | JQ-19b · JQ-19c · JQ-19d · JQ-20 · #168 · #169 | ES-7 (integración) · ES-8 |
| **3 · Después** | Desde enero | Lo que no hace falta para abrir | Plan Equipo · App Store y Play Store con Capacitor | Pantallas del plan Equipo | Antivirus de documentos · informe de ingresos |

**La apertura de `zass.cl` (P-9) va al final de la fase 2**, no antes: abrir con la mitad de las pantallas en el estilo viejo se ve como dos productos distintos.

---

## 6. Cómo se une todo sin romper nada

1. **Una rama por tarea, siempre desde `main` actualizado.** El nombre lleva el carril: `j/jq17-planes`, `e/es4-mi-cuenta`, `p/favoritos`.
2. **PR chico, con el ID de la tarea en el título** (`[N2] JQ-17: …`). Si pasa de unas 600 líneas, se divide.
3. **Antes de pedir revisión:** actualizar con `main`, CI verde, y en el PR capturas de la pantalla en 390 y 1280 px.
4. **Claude revisa y fusiona** los PR de Joaquín y Esteban que estén en verde (regla de Pablo del 20-nov). Antes de fusionar mira **a qué rama apunta el PR**: el 20-nov se fusionaron dos PR apilados sobre otra rama y su código no llegó a `main`.
5. **Lo que lleva migración (N3) se fusiona de a uno.** Dos migraciones creadas en paralelo chocan en el `ModelSnapshot`: el segundo actualiza con `main` y la vuelve a generar.
6. **Producción es de Pablo.** Migrar Neon, desplegar, las variables de Render y los precios no los toca nadie más.
7. **Una prueba en verde no prueba nada por sí sola.** Cada PR que cambia una regla trae una prueba que **falla** si se rompe la regla, y se comprueba rompiéndola a propósito.
8. **El repositorio es público.** Nada de claves, RUT ni datos personales en el código, en los PR ni en los comentarios.

---

## 7. La lista de la prueba final (P-8)

Cada fila se recorre en el **celular** y en el **escritorio**, en producción.

| Quién | Recorrido | Casos de uso |
|---|---|---|
| Quien busca | Crear cuenta, verificar correo, entrar, salir, recuperar contraseña | CU-01 · CU-02 · CS-01 · CS-02 · CS-03 |
| Quien busca | Buscar cerca, filtrar por verificado y nota, abrir una ficha, ver credenciales y reseñas, guardar contacto, guardar en favoritos | CU-08 · CU-09 · CU-07 · CS-09 · CS-10 · CS-19 · CS-08 · CU-04 |
| Quien busca | Agendar una cita automática, pedir una manual, reprogramar y cancelar | CU-12 · CU-13 · CU-14 |
| Quien busca | Escribir una reseña, editarla, reportar otra | CU-16 · CS-16 · CU-18 |
| Prestador | Ofrecer servicios, completar la ficha con foto y fotos de trabajos, zonas y horario | CU-03 · CU-10 · CU-11 |
| Prestador | Subir una credencial con documentos, recibir el resultado | CU-05 · CS-07 |
| Prestador | Tomar el cupo fundador, inscribir la tarjeta, pagar, ver el plan | S7 |
| Prestador | Bloquear una franja, confirmar una cita, marcar asistencia, reenviar recordatorio | CS-11 · CS-12 · CS-13 |
| Prestador | Responder una reseña, ver sus estadísticas | CU-17 · JQ-8 |
| Prestador | Activar la tarjeta NFC, escanearla, desactivarla y reemplazarla | CU-19 · CU-20 · CS-18 |
| Los dos | Recibir avisos por correo y push y elegir el canal | CU-15 · CS-14 |
| Los dos | Eliminar la cuenta | CS-04 |
| Verificador | Revisar, aprobar y rechazar con motivo; revocar una vencida | CU-06 · CS-05 · CS-06 |
| Administrador | Moderar una reseña, emitir tarjetas, ver la auditoría y los indicadores | CU-18 · CS-17 · CS-15 · API-60 |

---

## 8. Lo que solo puede hacer Pablo (no es código, pero lo bloquea)

| Qué | Bloquea | Para cuándo |
|---|---|---|
| Inicio de actividades en el SII (con el contador) | ES-6, ES-7 y la postulación | **Antes del 24-nov** |
| Crear el balde de R2 y poner las 5 variables en Render | Subir fotos y documentos en producción | Fase 1 |
| Correr `migrar-neon` y desplegar | P-5 | Cuando JQ-17 y P-1 estén en `main` |
| Llevar los textos legales a un abogado (#129) | P-9 | Fase 1, porque demora |
| Decidir la boleta cuando Esteban la proponga | ES-7 | Fase 1 |
| Decidir qué cupones se reparten al abrir (el 30 % ya está decidido) | Lanzamiento | Fase 2 |

---

## 9. Riesgos

| Riesgo | Qué hacemos |
|---|---|
| Joaquín tiene la cola más larga (unas 25 pantallas en estilo iPhone) | Va primero lo que bloquea (JQ-17). Si al 5-dic no llegó a JQ-19b, Claude toma JQ-19c con su «sí» |
| La postulación se come la semana del 21 al 24 | La fase 0 solo pide una tarea por persona |
| El SII se atrasa | ES-6 y ES-7 se corren; lo demás sigue. Sin SII **no se abre**, porque no se puede cobrar con boleta |
| Los modelos gratis dicen «hecho» sin estarlo (ya pasó con la T65 y la T68) | Claude revisa el PR, el CI y la causa de cada falla antes de creerles |
| Dos migraciones a la vez | Regla 5 de la sección 6 |
