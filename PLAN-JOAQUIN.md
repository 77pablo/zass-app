# Plan de trabajo de Joaquín — al 14 de noviembre de 2026 (tarde)

ZASS! abre el **lunes 23**. Este archivo es tu cola y tu permiso para avanzar. Manda sobre
cualquier lista anterior (`TRASPASO.md`, `TRASPASO-12-NOV.md`, la versión vieja de este archivo en
la rama `plan-joaquin`): esas tienen cosas que ya están hechas.

**Si retomas desde cero, dile a Claude: «sigue con plan-joaquin de zass».** Él lee esto,
`ESTADO.md` y `CONTRIBUIR.md`, y sigue por la primera tanda sin terminar.

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

### Si acabas todo eso — JQ-8, métricas del prestador · N2

Cuántas veces vieron su ficha, cuántos escaneos tuvo su tarjeta y cuántos contactos recibió. Va
**después** de JQ-4 y JQ-5, porque vive de lo que esas dos registran.

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
