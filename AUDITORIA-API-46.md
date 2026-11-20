# Auditoría de API-46 contra el PR #92 (T39)

**Fecha:** 18-nov-2026  
**Rama auditada:** `j/jq5-contacto-whatsapp` (PR #92)  
**Documentación:** `api-rest.md` (sección API-46), `subsistema-5-resenas-y-reputacion.md` (S5), `cu-16-publicar-resena.md` (CU-16)  
**Código comparado:** backend (`EndpointsResenas.cs`, `ServicioResenas.cs`, `Enumeraciones.cs`), frontend (`Ficha.tsx`, `servicio.ts`)

---

## Resumen ejecutivo

El PR #92 implementa el registro de contacto desde la ficha pública (API-46) para activar la ventana de 7 días que habilita la elegibilidad para reseñar (RN-S5-02). **En general coincide con la documentación**, pero hay **diferencias relevantes** en los canales permitidos, la regla de "un contacto abierto a la vez" y el bloqueo `BloquearPerfilAsync`.

---

## Tabla de contrastación

| # | Afirmación (documentación) | Código del #92 | Coincide / Difiere | Evidencia |
|---|---|---|---|---|
| 1 | **API-46: POST `/contactos`** registra un contacto desde la ficha, arranca los 7 días. Grupo G. | ✅ Implementado en `EndpointsResenas.cs:101-119` con `RequireRateLimiting(Tasa.General)` y `.WithName("API-46")` | **Coincide** | `EndpointsResenas.cs:100-119` |
| 2 | **Orígenes permitidos**: Ficha, Tarjeta, Búsqueda, Cita (enum `OrigenContacto`). | ⚠️ **Difiere**: el endpoint **rechaza** `OrigenContacto.Cita` con error de validación ("Ese origen de contacto no se puede registrar desde aquí"). Los orígenes válidos desde la ficha son solo `Ficha`, `Tarjeta`, `Busqueda`. | **Difiere** | `EndpointsResenas.cs:107-111`, `Enumeraciones.cs:63` |
| 3 | **Canales permitidos**: Teléfono, Correo, WhatsApp, Formulario (enum `CanalContacto`). | ✅ Coincide: usa el mismo enum `CanalContacto` | **Coincide** | `Enumeraciones.cs:65`, `servicio.ts:65` |
| 4 | **"Solo el primer canal de cada interacción"** (CU-16, S5): un contacto por cita; por contacto (sin cita), máximo uno cada 90 días (RN-S5-02). | ⚠️ **Difiere en la implementación**: el código permite **un contacto "abierto" (sin reseña) a la vez por buscador+prestador**. Si ya existe uno sin reseña, **devuelve ese mismo ID** en lugar de crear otro (`ServicioResenas.cs:86-91`). **No aplica la ventana de 90 días** al registrar el contacto; esa validación se hace luego en `EsElegibleAsync` (API-47). | **Difiere** | `ServicioResenas.cs:86-91`, `ReglasDeResenas.TopePorContacto` |
| 5 | **Regla de una vez por ficha y visita** (JQ-5, PR #92): "API-46 una sola vez por ficha y visita: el servidor ya devuelve el contacto abierto, pero cada toque de más gastaría peticiones del límite sin cambiar nada." | ✅ Implementado en frontend: `Ficha.tsx` usa `useRef` `registrado` para hacer **una sola llamada por visita** (`registrado.current === ficha.id`). El backend ya devuelve el contacto abierto si existe. | **Coincide** | `Ficha.tsx:37-43`, `ServicioResenas.cs:86-91` |
| 6 | **Bloqueo con `BloquearPerfilAsync`** al publicar reseña (CU-16 paso 9, `ServicioResenas.cs:182`). | ✅ Existe y se usa **solo al publicar la reseña** (`PublicarAsync`), **no al registrar el contacto** (API-46). La documentación de S5/CU-16 lo menciona en el flujo de publicación, no en el de contacto. | **Coincide** | `ServicioResenas.cs:182`, `cu-16-publicar-resena.md:46-50` |
| 7 | **Tabla `contactos`** (TBL-18): columnas `id`, `buscador_id`, `perfil_prestador_id`, `cita_id` (nullable), `origen`, `canal`, `ocurrido_en`. Índices: `i_x_contactos_buscador_id_perfil_prestador_id_ocurrido_en`, `i_x_contactos_cita_id`, `i_x_contactos_perfil_prestador_id`. FK a `citas`, `perfiles_prestador`, `usuarios`. | ✅ Coincide con la migración `20261107022203_EsquemaInicial.cs:719-746` y el diccionario TBL-18. | **Coincide** | `EsquemaInicial.cs:719-746`, `diccionario-de-tablas.md` (TBL-18) |
| 8 | **El contacto por cita** requiere `cita_id` y origen `Cita`; un origen `Cita` sin `cita_id` o viceversa es error (RN-S5-02). | ✅ Validado en `ServicioResenas.cs:61-62`: `(origen == OrigenContacto.Cita) != (citaId is not null)` | **Coincide** | `ServicioResenas.cs:61-62` |
| 9 | **Autorreseña bloqueada**: no puedes registrar contacto contigo mismo. | ✅ Validado en `ServicioResenas.cs:71-72` con `_identidad.EsMismaPersonaAsync` | **Coincide** | `ServicioResenas.cs:71-72` |
| 10 | **Perfil debe estar publicado** para recibir contactos. | ✅ Validado en `ServicioResenas.cs:67-69` | **Coincide** | `ServicioResenas.cs:67-69` |
| 11 | **Cuenta habilitada** (correo verificado + rol buscador) para registrar contactos. | ✅ Validado en `ServicioResenas.cs:64-65` con `HabilitadoAsync` | **Coincide** | `ServicioResenas.cs:64-65` |
| 12 | **Frontend: canales que disparan API-46** — WhatsApp, Teléfono, Correo. El sitio web NO registra contacto ("no es hablar con nadie"). | ✅ Implementado en `Ficha.tsx:193-215` con `alContactar('Whatsapp')`, `alContactar('Telefono')`, `alContactar('Correo')`. El sitio web no llama a `alContactar`. | **Coincide** | `Ficha.tsx:193-215`, `FichaContacto.prueba.tsx:78-85` |
| 13 | **Frontend: `keepalive: true`** en la petición para que se complete aunque el usuario navegue fuera. | ✅ Implementado en `Ficha.tsx:41` (`{ keepalive: true }`) y confirmado en `servicio.ts:179` (usa `enviar` que respeta `keepalive`). | **Coincide** | `Ficha.tsx:41`, `servicio.ts:179` |
| 14 | **Frontend: si el registro falla, el enlace abre igual y no hay aviso** (fallo silencioso). | ✅ Implementado: `catch(() => {})` en `Ficha.tsx:42` y prueba en `FichaContacto.prueba.tsx:108-118`. | **Coincide** | `Ficha.tsx:42`, `FichaContacto.prueba.tsx:108-118` |

---

## Detalle de las diferencias

### D1. Origen "Cita" rechazado en API-46 (línea 2 de la tabla)
**Documentación:** El enum `OrigenContacto` incluye `Cita` como valor válido (`Enumeraciones.cs:63`, `api-rest.md` no lo enumera explícitamente pero el enum lo permite).  
**Código #92:** El endpoint **rechaza explícitamente** `OrigenContacto.Cita` con un error de validación (`EndpointsResenas.cs:107-111`). El comentario dice: "El origen 'cita' es del módulo de agenda y no se puede pedir desde fuera".  
**Análisis:** Es una **decisión de diseño coherente**: los contactos por cita los crea el módulo de agenda (`ServicioAgenda.cs:435-436`), no la ficha pública. La documentación del enum no debería incluir `Cita` como origen válido para API-46, o debería aclararse que API-46 no lo acepta.  
**Archivo:línea:** `EndpointsResenas.cs:107-111`, `Enumeraciones.cs:63`

### D2. "Solo el primer canal" vs "un contacto abierto a la vez" (línea 4)
**Documentación (S5, RN-S5-02):** "Por contacto, máximo una por prestador cada 90 días". La regla de 90 días se aplica a la **elegibilidad para reseñar**, no al registro del contacto.  
**Código #92:** Al registrar un contacto sin cita (`ServicioResenas.cs:86-91`), si ya existe un contacto **sin reseña** (`ContactosSinResenaAsync` filtra `CitaId is null`), **devuelve ese mismo contacto** en lugar de crear uno nuevo. Esto implementa "un contacto abierto a la vez", no "uno cada 90 días".  
**Análisis:** El comportamiento del código es **más restrictivo y sensato** (evita llenar la tabla con contactos duplicados sin reseña), pero **no coincide con la redacción literal de RN-S5-02**. La regla de 90 días se valida luego en `EsElegibleAsync` (API-47) vía `ReglasDeResenas.TopePorContacto`.  
**Archivo:línea:** `ServicioResenas.cs:86-91`, `ReglasDeResenas.cs` (TopePorContacto), `ResenasApiPruebas.cs:913-917`

### D3. Bloqueo `BloquearPerfilAsync` solo en publicación, no en contacto (línea 6)
**Documentación (CU-16 paso 9):** "Abre transacción → Revalida elegibilidad en el servidor → **BloquearPerfilAsync**".  
**Código:** `BloquearPerfilAsync` se llama **solo en `PublicarAsync`** (`ServicioResenas.cs:182`), no en `RegistrarContactoAsync` (API-46).  
**Análisis:** **Correcto**. El bloqueo es necesario al publicar la reseña (para evitar condiciones de carrera al recalcular reputación), no al registrar el contacto. La documentación de CU-16 lo sitúa en el flujo de publicación.  
**Archivo:línea:** `ServicioResenas.cs:182`, `cu-16-publicar-resena.md:46-50`

---

## Hallazgos fuera del plan (no bloquean, solo informan)

| # | Hallazgo | Archivo:línea |
|---|---|---|
| F1 | El enum `OrigenContacto` incluye `Cita` pero API-46 lo rechaza. Debería documentarse que `Cita` es solo para uso interno del módulo de agenda. | `Enumeraciones.cs:63`, `EndpointsResenas.cs:107-111` |
| F2 | La regla "un contacto abierto a la vez" en el backend no está documentada explícitamente en S5 ni CU-16. El comentario en `ServicioResenas.cs:86-88` lo explica bien pero no está en la documentación técnica. | `ServicioResenas.cs:86-88` |
| F3 | El frontend usa `useRef` para deduplicar en cliente, pero el backend ya deduplicaría. Es defensa en profundidad (correcta). | `Ficha.tsx:37-43` |
| F4 | Las pruebas de integración (`ResenasApiPruebas.cs`) cubren bien los casos: origen cita rechazado, contacto duplicado devuelve el mismo, sitio web no registra, fallo silencioso. | `ResenasApiPruebas.cs:913-917`, `FichaContacto.prueba.tsx` |

---

## Conclusión

| Aspecto | Veredicto |
|---|---|
| **Contrato API-46 (ruta, método, grupo de tasa, nombre)** | ✅ Coincide |
| **Orígenes y canales permitidos** | ⚠️ Difiere: `Cita` rechazado en API-46 (correcto por diseño, mal documentado en el enum) |
| **Regla "solo el primer canal / una vez por ficha y visita"** | ⚠️ Difiere en redacción: backend implementa "un contacto abierto a la vez", no "uno cada 90 días al registrar" |
| **Bloqueo `BloquearPerfilAsync`** | ✅ Coincide (solo en publicación, no en contacto) |
| **Tabla `contactos` vs diccionario** | ✅ Coincide |
| **Frontend: canales, keepalive, fallo silencioso, deduplicación** | ✅ Coincide |

**Recomendación:** Actualizar la documentación técnica para reflejar que:
1. `OrigenContacto.Cita` **no** es válido en API-46 (solo en agenda).
2. La deduplicación en el registro es "un contacto sin reseña abierto a la vez por buscador+prestador", y la ventana de 90 días (RN-S5-02) se valida en la elegibilidad (API-47), no al registrar.

---

**Hecho por FreeLLMAPI (qwen3.5-397b (con relevos)) via opencode**