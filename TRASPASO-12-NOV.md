# ZASS! — traspaso del 12 de noviembre de 2026

Documento para que **otra IA (u otra persona) retome sin reconstruir contexto**. Todo lo que dice
aquí se comprobó corriendo las pruebas en este PC, no leyendo informes ajenos.

## Lo mínimo que hay que saber

- **El repositorio de trabajo es `C:\Users\pdani\zass-app-1`**, no `zass-app`. En GitHub es
  `77pablo/zass-app`.
- Plataforma web de servicios locales en Chile: quien ofrece un servicio aparece a quien lo busca
  cerca, con credenciales verificadas, reseñas, agenda de citas y tarjeta NFC opcional. El
  prestador paga una mensualidad; quien busca no paga nunca.
- Stack: .NET 8 por capas + PostgreSQL con PostGIS (16.4 portable en local, 18 en Neon) + React +
  TypeScript + Vite.
- La documentación técnica está **fuera del repositorio**, en
  `C:\Users\pdani\OneDrive\Desktop\Repositorio ZASS\zass-documentacion`, y **es la especificación**:
  los identificadores del código (CU, CS, TBL, API, RN, R, CC, MOD) apuntan a ella y son
  verificables. Si el código y el documento no coinciden, no se deja pasar: se corrige uno de los
  dos y se dice cuál se movió.
- Leer además `ESTADO.md` (estado real), `CONTRIBUIR.md` (flujo y niveles N1/N2/N3) y `CLAUDE.md`.

## La regla de verificación, que aquí ya costó cara

**Un verde no prueba nada por sí solo.** Antes de decir que algo funciona, pregúntate qué aspecto
tendría el fallo y comprueba **eso**:

- Para cada regla que escribas, **rompe la guarda a propósito (un mutante), corre la prueba y
  comprueba que se pone ROJA**; después deshazlo. **Un mutante a la vez**, nunca varios: hoy un
  agente aplicó tres a la vez, se quedó sin presupuesto y los dejó vivos en el árbol.
- Con la base de datos: consulta `pg_indexes`, `information_schema` y `pg_constraint`. No le creas
  al «Done» de una migración.
- Hoy esto encontró tres cosas que el verde escondía: 2 de las 5 mutaciones de J3 no ponen nada en
  rojo (son CSS, y jsdom no calcula estilos); las 4 mutaciones del endpoint de tarjetas dejan las
  701 pruebas de aplicación en verde (no hay ninguna unitaria que lo toque: solo la integración lo
  protege); y dos archivos de interfaz caían en rojo **al azar**, por tiempo de espera.

## Cómo se corre

```
cd C:\Users\pdani\zass-app-1
iniciar-postgres.bat        (PostgreSQL portable, puerto 5433; al terminar, detener-postgres.bat)
```

- Backend: `dotnet test backend/pruebas/Zass.Aplicacion.Pruebas` (unitarias, segundos),
  `...Zass.Arquitectura.Pruebas`, y `...Zass.Integracion.Pruebas` (~3 min, necesita PostgreSQL).
- Interfaz: dentro de `frontend`, **`npm run prueba`** (NO existe `npm test`) y `npx tsc -b`.
- Las pruebas de integración usan su propia base `zass_pruebas`, distinta de la de desarrollo. Si
  hay trabajo en paralelo, dale a cada uno la suya con la variable `ZASS_CADENA_PRUEBAS`.
- La clave local está en `.clave-pg.txt` (en `.gitignore`). **Nunca la imprimas.**
- **NUNCA** ejecutes migraciones ni `database update` contra **Neon o producción**: ese paso lo
  hace Pablo a mano desde Render.
- `CONTRIBUIR.md:135` — **una migración de EF a la vez**: no se empieza otra hasta que la anterior
  esté en `main`.

## Qué se hizo hoy

### Fusionado y subido a `main`

**Cupos fundador de 20 a 50** (`a567914`). La migración solo agrega los cupos 21-50: no toca los
1-20 ni sus reservas. Verificado contra la base, no contra el verde: 50 cupos numerados 1-50, 0
ocupados, texto del plan correcto, índice único parcial presente.
**Neon NO fue migrada**: la base de producción sigue con 20 cupos.

> **La lección del día:** la prueba de concurrencia que «fallaba» no tenía ningún defecto.
> `datos-pg/postgresql.conf` fijaba `max_connections = 50` **al final del archivo**, pisando el 100
> de más arriba, y la prueba de 60 solicitantes necesita 61 conexiones. Subido a 200. Antes de
> culpar a la lógica, mirar el techo del servidor.

### Ramas locales, verificadas, **sin fusionar**

| Rama | Commit | Qué tiene | Comprobación |
|---|---|---|---|
| `agente/mis-citas` | `a1a248d` | Reprogramar (API-41, RN-S4-05), agrupación por día chileno, y `CitaDto` ahora trae el `slug` del prestador | 674+16+614 backend, 392 interfaz, todo verde |
| `agente/rn-s6-10` | `494be16` | RN-S6-10: la ficha del impago se ve entera pero sin contactar ni agendar; el contacto **se vacía en el servidor**; el calendario responde 409 y no 404; la página sale con `noindex` y sin JSON-LD | 682+16+617, 382 interfaz |
| `agente/resenas-pantalla` | `19a06da` | Pantalla de reseñas: elegibilidad consultada **antes** de pintar el formulario, 422 de datos personales con «enviar igual», edición dentro de 72 h, lista pública con reputación, respuesta y reporte | 403 interfaz, 13 mutantes probados |
| `docs-numeros-correctos` | `ea39ecd` | `CONTRIBUIR.md` decía 613 pruebas de integración (son 614); `CLAUDE.md` decía «PostgreSQL 18» a secas (producción sí, local es 16.4) | Documentación |
| `pruebas-sin-rojos-al-azar` | `f81b672` | `testTimeout` de vitest de 5 s a 15 s | Dos corridas completas seguidas en verde |

### Ramas **a medio hacer** — NO FUSIONAR

| Rama y worktree | Commit | Qué falta exactamente |
|---|---|---|
| `agente/no-asistio-n3` en `C:\Users\pdani\zass-agentes\revision-joaquin` | `bd8024b` | La migración N3 `20261112211216_NoAsistioQuienFalto` está escrita; compila y las 709 unitarias pasan. **Falta**: la prueba de integración del CHECK, mirar la migración contra `information_schema` y `pg_constraint`, y mutar las guardas |
| `agente/resenas-pantalla` en `C:\Users\pdani\zass-agentes\resenas-pantalla` | `19b6395` | `GET /mi/resenas` y el origen en el DTO público. Compila, pero hay **una unitaria roja**: `Lo_publico_dice_si_hubo_cita_o_fue_contacto_directo_y_nada_mas` esperaba `[Directo, Cita]` y recibió `[Cita, Directo]` — es orden, no comportamiento. Integración e interfaz sin correr |

### Revisión del trabajo de Joaquín (Alex Tellez)

**J1 ya está en `main`.** De lo suyo quedan dos ramas en `origin`, ya revisadas corriendo sus
pruebas aquí:

| Rama | Veredicto | Qué falta |
|---|---|---|
| `revision-visual-agenda-ficha` (J3, `ee96db1`) | **FUSIONABLE** | Que Pablo apruebe 3 textos visibles |
| `tarjeta-abre-ficha` (J4, `a45934b`) | **FUSIONABLE CON CAMBIOS** | Restaurar el **BOM UTF-8** de `Program.cs`, que J4 le quitó sin querer (`main` empieza con `ef bb bf`, J4 no), más 3 textos |

Las ramas `origin/mut-*` son mutaciones desechables: existen como evidencia de que las pruebas
cazan el fallo, y **no se fusionan nunca**.

Dos cosas de J4 que **no se pueden comprobar en local**: que Cloudflare Pages no enrute `/T/…` a la
Function (todo el respaldo en mayúsculas depende de eso), y que `X-Forwarded-For` llegue bien a la
tasa P — si la cadena trae más de 3 saltos, **todos los escaneos de tarjeta contarían como una sola
IP** y el límite se agotaría para todos a la vez.

## Decisiones de Pablo ya tomadas — no volver a preguntarlas

**Lanzamiento (12-nov):** primer mes gratis y después **$5.990/mes congelado para siempre**
(mensual normal $7.990) · **la tarjeta NFC va aparte, a precio normal** · inscripción por
`zass.cl/registro` **más un Google Forms opcional** cuyo gancho es un **cupón de descuento** para
el primer mes pagado · **sin filtro de zona** · al llegar al cupo 50, **se cierra y punto**.

Eso deja por construir, y **nada de esto existe todavía**: el mes gratis, el campo de cupón o
código de descuento, y cerrar el registro fundador al llegar a 50.

**Producto:** la ficha de un impago **no debe ser indexable** (ya hecho) · hay que construir
**`GET /mi/resenas`**, porque hoy la ventana de edición de 72 h es inalcanzable si la persona
recarga la página · hay que **exponer el origen de la reseña** para poder rotular «contacto
directo, sin cita», exponiendo el origen **y nada más** (ni el id del contacto, ni el de la cita) ·
de CU-14, **reprogramar solo desde `confirmada`** (cancelar sí acepta `solicitada`); ya corregido
en la documentación.

**NoAsistio (11-nov):** cada parte solo puede acusar a la otra. Si faltó el prestador, el cliente
sí reseña; si faltó el cliente, no hay reseña; si se contradicen, la reseña se bloquea y la cita va
a una cola interna de revisión. Decidido además **borrar** los dos `HabilitaResena()` muertos y
dejar viva solo la respuesta de `ReglasDeResenas`. Ojo: `HuboCitaRealizadaAsync` **sí** tiene
llamador real (`ServicioAgenda.cs:863`), así que ese no es código muerto.

## Lo que espera a Pablo, y nadie más puede hacer

1. **Migrar Neon** desde Render. La base de producción sigue con 20 cupos.
2. **Los seis textos visibles** que bloquean J3 y J4 (regla 12 de `CONTRIBUIR.md`). Dijo que los
   reescribe él. Los actuales: «Quitar horario», «Quitar bloqueo», «Bloqueaste el 01-01-2027: nadie
   podrá agendar ese día.», «Esta tarjeta no es válida.», «Este perfil no está disponible por
   ahora.», «Esta tarjeta todavía no está activa.»
3. **De cuánto es el cupón de descuento** y **la forma de cobro** (pasarela simulada, o Transbank /
   Flow / Mercado Pago). No hay flujo de cobro automático verificado:
   `RegistrarPeriodoPagadoAsync` no tiene llamadores.

## El hueco más grande del proyecto

**Nadie ha visto ninguna de estas pantallas en un navegador de verdad.** `/mis-citas`,
`AgendarHora`, la ficha sin suscripción y la pantalla de reseñas están comprobadas solo con `fetch`
simulado en jsdom. Es el primer trabajo que vale la pena antes de seguir construyendo pantallas.

## Deuda conocida, con nombre y apellido

- **La vía «cita realizada» de la elegibilidad para reseñar está muerta en producción**: API-46
  rechaza `Origen = Cita` (`EndpointsResenas.cs:108`) y nadie crea nunca un contacto ligado a una
  cita. La única vía viva es contacto directo más 7 días, aunque S5 diga otra cosa.
- **`ServicioAgenda` no emite ninguna notificación** (no recibe `INotificaciones`, no hay plantilla
  de correo para citas). Nada puede depender de avisarle a alguien.
- **El 422 de reseñas no es `problem+json`**, como exige `api-rest.md:16`.
- **S5 promete aviso inmediato de cada reseña**: se encola, pero `Plantillas.TienePlantilla` no
  incluye `ResenaNueva` ni `ResenaRespondida`, así que el correo no sale nunca.
- **Defecto de IP**: `Zass__SaltosDeProxy=3` con Cloudflare delante de Render, y la cadena trae ≥3
  entradas. Se prueba subiéndolo en Render y entrando desde datos móviles, **sin tocar código**.
- **No se puede restaurar una copia en este PC**: `pg_restore` 16.4 rechaza un volcado de `pg_dump`
  18. Falta un cliente 18 (Docker, binarios portables o un job de Actions).
