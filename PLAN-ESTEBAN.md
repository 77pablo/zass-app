# Plan de trabajo de Esteban — al 21 de noviembre de 2026

Bienvenido a ZASS!. Este archivo es tu cola y tu permiso para avanzar. Tu usuario en GitHub es
`lazyctm` y ya eres colaborador del repositorio.

**Si retomas desde cero, dile a Claude: «sigue con plan-esteban de zass».** Claude lee este archivo,
`ESTADO.md` y `CONTRIBUIR.md`, y sigue por la primera tanda sin terminar.

> **21-nov: hay un plan para terminar la web entre los tres**, en `PLAN-TERMINAR-LA-WEB.md` (raíz del repositorio).
> Tu carril es **la cuenta, los avisos y los cobros**: ES-1 y ES-2 ya están en `main`; sigues con **ES-3** y
> después **ES-4 a ES-8** (al final de la sección 2). Regla nueva: **el CSS y los componentes compartidos son de
> Joaquín**; usa las clases que existen y, si te falta un estilo, pídeselo en el PR.

---

## 0. Antes de empezar

### Qué es ZASS!, en dos líneas

Una plataforma donde quien necesita un servicio encuentra a alguien cercano que lo ofrece, con
credenciales verificadas, reseñas de citas reales y agenda en línea. **Paga el prestador**, con una
suscripción mensual; quien busca no paga nunca.

### Qué leer, en este orden

1. `CONTRIBUIR.md`: cómo se trabaja aquí, con la plantilla del PR.
2. `ESTADO.md`, solo el principio: qué está hecho.
3. La documentación técnica es **la especificación**. Los códigos del código apuntan a ella:
   `CU-15` es un caso de uso, `API-43` un endpoint, `TBL-27` una tabla, `RN-S7-05` una regla de negocio.
   Si el código y el documento no coinciden, no lo dejes así: se corrige uno de los dos y se dice
   cuál en el PR.

### El stack

.NET 10 (cuatro proyectos: `Zass.Dominio`, `Zass.Aplicacion`, `Zass.Infraestructura`, `Zass.Api`),
PostgreSQL 16 con PostGIS, y React 19 con TypeScript y Vite. **`Zass.Dominio` no lleva ningún
paquete NuGet**, y 20 pruebas de arquitectura lo vigilan.

---

## 1. Tu carril, y quién tiene lo demás

**Mientras estés en tu carril, decides, escribes, commiteas, pusheas y abres tu PR.** Para que tu PR se
fusione no tienes que pedirle nada a nadie: ver «Quién fusiona», abajo.

**Quién fusiona (Pablo, 20-nov-2026):** cuando tu PR tenga el CI en verde y esté listo (no en borrador), **Claude lo revisa y lo fusiona**. No tienes que avisarle a Pablo ni pedirle nada. Si la revisión encuentra algo, Claude lo comenta en el PR y lo corriges. Claude solo le consulta a Pablo si el PR cambia precios o cobros de forma distinta a lo ya decidido, toca temas legales o necesita algo de producción. **Publicar en producción** sigue siendo de Pablo.

| Es tuyo | Es de otro (no lo toques) |
|---|---|
| ~~ES-1~~ (hecho, #180) · ~~ES-2~~ (hecho, #183) | **Joaquín**: panel interno y Verificador, moderación, emitir tarjetas y auditoría, indicadores, métricas del prestador, fotos en la portada y borradores de credenciales (sus PR #159 a #169) |
| **ES-4**: la pantalla «Mi cuenta» · **ES-5**: eliminar la cuenta (el #76 pasa a ser tuyo) | **Joaquín**: toda la interfaz, el CSS (`index.css`, `marco/`, `componentes/`) y los borradores #98, #103, #106, #107, #128, #130 y #171 |
| **ES-3**: notificaciones push y preferencias de aviso (API-43, API-45) · **ES-6**: Flow en producción · **ES-7**: la boleta · **ES-8**: cupones desde el panel. El adaptador de Flow (`Pagos/`, `Pasarela.cs`) ahora también es tuyo | **Pablo con Claude**: fotos de trabajos, favoritos, vCard, producción y seguridad. **Pablo**: Neon, Render, R2, los secretos, los textos legales y aplicar migraciones en producción |
| Tus propias pruebas, incluidas las de navegador | `ESTADO.md`: lo lleva la sesión principal |

Si para tu tarea necesitas tocar un archivo que es de otro, **avísalo en el PR antes de hacerlo**.

### Cómo decidir sin esperar a Pablo

No tienes que preguntarle a Pablo para avanzar. Cuando te surja una duda, resuélvela en este orden:

1. **Si ya hay una decisión escrita, manda esa.** Búscala en este plan, en `ESTADO.md`, en los
   comentarios del código y en la documentación técnica. Si dos fuentes se contradicen, **gana la
   decisión de Pablo más antigua que esté escrita en el código o en la documentación**, no este plan:
   el plan lo escribe Claude y se puede equivocar. Dilo en el PR.
2. **Si no hay nada escrito, decide tú** lo más prudente (lo que no cobra de más, no expone datos y
   se puede deshacer), y explica en el PR qué elegiste y por qué. En la revisión se acepta o se cambia.
3. **La documentación técnica no la tocas tú:** vive en el OneDrive de Pablo, fuera del repositorio.
   Si tu cambio contradice un documento (una regla `RN-…`, un caso de uso, un endpoint), escríbelo en
   el PR bajo un título **«Documento que cambia»**, con el código y qué debería decir. Claude lo
   actualiza después de fusionar.
4. **Los textos que ve el usuario** (correos, avisos, botones) los propones tú en el PR. Los revisa
   Claude en la revisión del PR, contra el tono del sitio: en español de Chile, claros, sin
   tecnicismos y sin culpar al usuario. No esperes a Pablo para eso.
5. **Solo es de Pablo** (y te lo consulta Claude, no tú): cambiar precios o lo que se cobra, decisiones legales, secretos y claves,
   y todo lo que se hace en producción (Neon, Render, Cloudflare). Si tu tarea necesita una de esas
   cosas, déjala anotada en el PR y sigue con lo demás.

### Tres reglas duras

1. **Migraciones de EF: una a la vez, y avisas antes.** Dilo en el título y en el cuerpo del PR.
   Aplicarlas en producción es de Pablo.
2. **Una prueba que nunca se vio roja no prueba nada.** Antes de decir que algo funciona, rompe a
   propósito lo que acabas de escribir, comprueba que la prueba **cae**, deshazlo y anota en el PR
   qué cambio la tumbó. En este proyecto ya hubo pruebas en verde sobre pantallas rotas.
3. **Nada de secretos en el repositorio.** Es público. Las claves (incluidas las de push) van en
   variables de entorno; `gitleaks` revisa cada PR y bloquea si encuentra una.

---

## 2. Tu cola, en orden

### ES-0 — Levantar el proyecto · sin PR · lo primero

Clona el repositorio, levanta PostgreSQL y corre las cuatro suites de pruebas. Tus números tienen
que parecerse a estos, que son los de `main` al 20-nov; si te salen menos, falta algo:

| Suite | Pruebas |
|---|---|
| Aplicación | 1.013 |
| Arquitectura | 20 |
| Integración (contra PostgreSQL real) | 1.054 |
| Interfaz (Vitest) | alrededor de 745 |

Anota cualquier traba que tengas para dejarlo funcionando: le sirve al próximo que llegue.

### ES-1 — Gracia de 7 días si falla el cobro del fundador · N3 · **HECHO (#180)**

**Por qué:** el plan Fundador ($5.990 congelado, 50 cupos) es el gancho del lanzamiento. Hoy, si
la tarjeta de un fundador falla al cobrar, nadie le avisa ni se reintenta, y la regla de qué pasa
con su cupo no está programada. Los primeros cobros llegan unos 30 días después de abrir: tiene que
estar listo antes.

**Lo que decidió Pablo (20-nov):** si la tarjeta falla, el fundador tiene **7 días de gracia**. Si no
paga en ese plazo, **pierde el cupo fundador**.

**Qué tiene que quedar:**

- El cobro automático parte **7 días antes del vencimiento** (día −7). Si falla:
  - un correo «actualiza tu tarjeta», **uno solo por período**;
  - un **reintento diario** hasta el vencimiento, con clave de idempotencia
    `…:reintento:yyyy-MM-dd`, y **sin reintentar si ya hay un intento `Pendiente`**.
- Al vencer sin pago, **el fundador pierde el cupo, tenga o no tenga tarjeta**. Hoy
  `VencerExpiradasAsync` (`ServicioSuscripciones.cs`, cerca de la línea 268) solo libera el cupo si
  `ReferenciaPasarela` está vacía, o sea, si no inscribió tarjeta. Esa condición hay que quitarla para
  el caso de la tarjeta que falla.
- **No cobrar el precio fundador a quien ya no tiene cupo.**
- **Al perder el cupo NO hay paso automático al Mensual** (decisión de Pablo del 12-nov, ya anotada
  en `ServicioSuscripciones.cs`). La suscripción **vence**, la ficha sale de la búsqueda y no se
  le cobra nada más. Si quiere volver, elige el Mensual a mano en `/planes`. Así nunca se le
  cobra un precio distinto sin que lo acepte. *(Corregido el 20-nov: la primera versión de este
  plan decía lo contrario, y era un error del plan.)*
- Un aviso visible en **«Mi ficha»** mientras está en gracia: cuántos días le quedan y un enlace para
  actualizar la tarjeta.

**Dónde mirar:** `IServicioCobros.CobrarVencimientosAsync`, `IServicioSuscripciones.VencerExpiradasAsync`
y `PorVencerAsync`, y el `Planificador` (`Zass.Infraestructura/Fondo/Planificador.cs`). El adaptador
de Flow es de Claude: tu código habla con la pasarela **solo a través del puerto** que ya existe.

**Pruebas mínimas:** falla el cobro → sale un correo y no dos; el reintento del día siguiente paga →
conserva el cupo; pasan los 7 días sin pago → pierde el cupo, la suscripción vence y **no se
intenta ningún cobro más**; dos procesos cobrando a la vez → un solo cobro.

**Documento que cambia:** RN-S7-10 (`subsistema-7-suscripciones-y-pagos.md`) dice que un rechazo
no se reintenta solo. Con ES-1 sí se reintenta dentro de la gracia. Anótalo en el PR (ver «Cómo decidir sin esperar a Pablo»,
en la sección 1); el documento lo actualiza Claude.

### ES-2 — Completar los datos de las cuentas antiguas · N2 · **HECHO (#183)**

**Por qué:** desde el #174 (20-nov) el registro exige particular o empresa, RUT único, teléfono y la
casilla «mayor de 18». Las cuentas creadas **antes** no tienen esos datos.

**Lo que decidió Pablo:** pedírselos **al entrar**.

**Qué tiene que quedar:**

- Al iniciar sesión, si a la cuenta le falta el RUT, el teléfono o `MayorDeEdadConfirmadoEn`, la
  persona ve una pantalla para completarlos **antes de seguir**. No se puede saltar.
- **Las mismas validaciones del registro, sin duplicarlas:** RUT con módulo 11 y único,
  `TipoCuenta`, teléfono, y la razón social si es empresa. Reutiliza lo del #174 (`ServicioRegistro`).
- Un RUT que ya usa otra cuenta se rechaza **con el mismo mensaje que en el registro**, sin revelar
  de quién es.
- La API también tiene que exigirlo: no basta con esconder pantallas. Decide qué endpoints se
  bloquean mientras faltan datos y escríbelo en el PR.

### ES-3 — Notificaciones push y preferencias de aviso · N3 · la más grande

**Por qué:** hoy todo aviso sale por correo. La especificación ya define el push, y el enum
`CanalNotificacion { Correo, Push }` existe, pero **nadie lo usa**.

**La especificación:** CU-15 (recibir notificaciones), CS-14 (configurar canales), MOD-07,
**API-43** `PUT /mi/notificaciones/preferencias` y **API-45** `POST /mi/notificaciones/push`.
Están en `docs/06-desarrollo/api-rest.md` y en `docs/08-trazabilidad/matriz.md` de la documentación.

**Qué tiene que quedar:**

- **API-45:** registrar la suscripción push del navegador (Web Push con VAPID), varias por usuario
  (celular y computador), y borrar las que el servicio de push declare vencidas.
- **API-43:** elegir canal por tipo de aviso (correo, push o los dos). **El correo no se puede apagar
  para los avisos de seguridad ni de cobro.**
- El trabajo de fondo que hoy manda correos también manda push, con los mismos reintentos y sin
  duplicar.
- El service worker ya existe (`frontend/src/registrarServiceWorker.ts`): agrégale la recepción del
  push y el clic que abre la cita.
- Una sección «Avisos» en la cuenta, para elegir los canales y activar el push en este dispositivo.
- **Las claves VAPID son secretos:** genera unas tuyas para desarrollo y **no las subas**. Las de
  producción las pone Pablo en Render.
- La librería de Web Push va en `Zass.Infraestructura`, **nunca** en `Zass.Dominio`.

Si la tarea queda muy grande para un solo PR, pártela: (a) API-45 + envío, y (b) API-43 + pantalla.

### ES-4 — «Mi cuenta» · N2 · después de ES-3

**Por qué:** hoy no existe una pantalla de cuenta. Nadie puede cambiar su teléfono, su contraseña ni cerrar
sus sesiones en otros aparatos sin pasar por «olvidé mi contraseña».

**Qué tiene que quedar:** una ruta `/mi-cuenta` (en el menú y en la barra), con:

- Nombre y teléfono, editables. El RUT y el tipo de cuenta se ven pero no se cambian aquí.
- Cambiar la contraseña, pidiendo la actual (ya existe `POST /api/auth/contrasena`).
- **Cerrar sesión en todos los aparatos** (API-12, `DELETE /api/auth/sesiones`, CS-03).
- Los avisos de ES-3 (correo, push o los dos).
- El enlace a eliminar la cuenta (ES-5), en rojo oscuro y al final.

Usa las clases que ya existen: **el CSS es de Joaquín** (si te falta un estilo, pídeselo en el PR).

### ES-5 — Eliminar mi cuenta (CS-04) · N3 · rescatar el #76

**Por qué:** la Ley 21.719 exige poder borrar los datos personales desde diciembre, y las tiendas de apps también.

El **#76** ya tiene la pantalla y la lógica, pero se escribió antes del registro completo (#174) y tiene el CI en rojo.
Actualízalo con `main` (trabajas en la misma rama) y deja:

- Pide la contraseña antes de borrar.
- **Cancela las citas futuras** (y avisa a la otra parte) y **la suscripción**, sin cobrar nada más.
- **Borra fotos y documentos** con `IAlmacenDeArchivos.BorrarAsync`, después de que la base confirmó.
- Las reseñas que escribió quedan como «Usuario eliminado», sin su nombre.
- Trae una migración: **avísalo en el título** y espera tu turno (regla dura 1).

### ES-6 — Flow en producción · N3 · necesita el SII

**Espera a que Pablo tenga el inicio de actividades en el SII:** Flow no entrega credenciales reales sin él.
Después: las credenciales reales van en Render (las pone Pablo), el aviso de pago apunta a la URL pública, y se
hace **un pago real de prueba y su devolución**. Revisa en la base que la suscripción y el cupo quedaron bien,
y escribe en el PR lo que comprobaste.

### ES-7 — La boleta electrónica · N1 primero, N3 después

Con inicio de actividades, **cada cobro necesita boleta**. Flow cobra, pero no la emite. **Primero una propuesta
escrita** (sin programar nada): emitirla a mano en el portal del SII o integrar un emisor por API, con lo que
cuesta cada opción y cuánto trabajo es. Pablo decide, y recién entonces se programa.

### ES-8 — Cupones desde el panel · N3 · fase 2

El cupón **ya existe**: tiene código, porcentaje y vencimiento, API-67 lo cotiza y `/planes` lo canjea. Pero hoy
solo se crea con SQL. Falta, en `/interno` (solo el rol `administrador`, y cada cambio a la auditoría): crear un
cupón, desactivarlo y ver cuántas veces se usó. Comprueba además que funciona con los tres planes nuevos.


---

## 3. Cómo se cierra cada tanda

1. Rama corta desde `main` actualizado, con nombre `e/es1-gracia-cobro`, `e/es2-datos-cuentas`, etc.
   Un PR por tanda.
2. Título con el nivel: `[N1]`, `[N2]` o `[N3]`. **Es N3 si toca migraciones, seguridad,
   `Program.cs`, correo, infraestructura, secretos o pagos**, y en ese caso lo dices y esperas.
3. La plantilla del PR completa, **con la mutación anotada**.
4. CI en verde. **Nada se fusiona en rojo.**
5. Antes de abrir el PR, actualiza tu rama con `main`: hay tres personas trabajando a la vez y los
   choques aparecen ahí.
