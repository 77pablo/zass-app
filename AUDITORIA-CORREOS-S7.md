# Auditoría de los correos de S7 — T37

**Fecha:** 18-nov-2026  
**Encargado:** IA-2 (T37)  
**Documento base:** `subsistema-7-suscripciones-y-pagos.md` (en `zass-documentacion/docs/01-casos-de-uso/`)

---

## Resumen ejecutivo

| Correo | Lo dice el documento (doc:línea) | Plantilla en código | Quién lo encola | Estado |
|---|---|---|---|---|
| **Suscripción por vencer** (7 días y 1 día) | "mayor umbral de aviso que ya existía" (S7:68), "Hay que avisar antes, no después. La notificación de vencimiento próximo... pasa a ser parte de S7" (S3:107) | ✅ `Plantillas.PorVencer()` (Plantillas.cs:186-206) | `ServicioSuscripciones.AvisarPorVencerAsync()` (ServicioSuscripciones.cs:278-310) | **Existe y funciona** |
| **Suscripción vencida** | "quien pierde la visibilidad tiene derecho a saberlo" (ServicioSuscripciones.cs:252-253) | ✅ `Plantillas.Vencida()` (Plantillas.cs:209-227) | `ServicioSuscripciones.VencerExpiradasAsync()` (ServicioSuscripciones.cs:236-268) | **Existe y funciona** |
| **Cobro hecho / Pago confirmado** | "Confirmar... si cobró, extiende la suscripción un mes" (S7:54), diagrama VEN → CONF → BUSCA (S7:44) | ❌ **No existe** | ❌ **No se encola** | **FALTA** |
| **Inscripción confirmada (cupo fundador)** | "El cupo fundador se confirma al inscribir el medio de pago" (RN-S7-01, S7:164), "Confirmar cupo fundador... y tu mes gratis empezó a correr" (ServicioCobros.cs:747) | ❌ **No existe** | ❌ **No se encola** | **FALTA** |
| **No pudimos cobrarte (cobro rechazado/fallido)** | "y si hay un correo propio de «no pudimos cobrarte»" (S7:221) — decisión pendiente de Pablo (RN-S7-13) | ❌ **No existe** (conocido) | ❌ **No se encola** | **FALTA (conocida, decisión pendiente)** |

---

## Detalle por correo

### 1. Suscripción por vencer (`SuscripcionPorVencer`)

**Lo que promete el documento:**
- S7 §2.1 (línea 68): "El cobro automático corre en los **7 días previos** al vencimiento... El 7 es el **mayor umbral de aviso** que ya existía"
- S3 (subsistema-3, línea 107): "Hay que avisar **antes**, no después. La notificación de vencimiento próximo deja de ser un extra y pasa a ser parte de S7"

**Plantilla:** `Plantillas.PorVencer(nombre, dias, enlace)` en `Plantillas.cs:186-206`
- Asunto: "Tu suscripción a ZASS! vence [mañana/en N días]"
- Cuerpo: explica que la ficha deja de aparecer, pero no se borra nada; da enlace al panel
- Parámetro `dias` viene en la carga del aviso (campo "dias")

**Quién lo encola:** `ServicioSuscripciones.AvisarPorVencerAsync()` (ServicioSuscripciones.cs:278-310)
- Se ejecuta en el trabajo diario (`CorrerDiarioAsync`, línea 320)
- Usa `DiasDeAviso = { 7, 1 }` (línea 32): **dos umbrales**, 7 días y 1 día
- Clave de idempotencia: `SuscripcionPorVencer:{SuscripcionId}:{umbral}` (línea 300)
- Si el trabajo no corrió un día, el umbral que se envía es el menor que ya alcanzó (`DefaultIfEmpty(0).Min()`, línea 292)

**Estado:** ✅ **Existe y funciona** — dos avisos por suscripción (7 días y 1 día antes)

---

### 2. Suscripción vencida (`SuscripcionVencida`)

**Lo que promete el documento:**
- ServicioSuscripciones.cs:252-253: "Quien pierde la visibilidad tiene derecho a saberlo. Si el perfil ya no tiene dueño (cuenta eliminada) no hay a quien avisarle, y vencer sigue siendo correcto."

**Plantilla:** `Plantillas.Vencida(nombre, enlace)` en `Plantillas.cs:209-227`
- Asunto: "Tu suscripción a ZASS! venció"
- Cuerpo: dice que la ficha dejó de aparecer, que no se borró nada, y da enlace para renovar

**Quién lo encola:** `ServicioSuscripciones.VencerExpiradasAsync()` (ServicioSuscripciones.cs:236-268)
- Se ejecuta en el trabajo diario (`CorrerDiarioAsync`, línea 319) — **antes** que los avisos por vencer
- Busca suscripciones `ExpiradasSinMarcarAsync` (fecha `VigenteHasta` pasada y estado no `Vencida`)
- Clave de idempotencia: `SuscripcionVencida:{SuscripcionId}` (línea 259)
- Solo encola si encuentra el usuario dueño del perfil (línea 254)

**Estado:** ✅ **Existe y funciona** — un aviso por suscripción al vencer

---

### 3. Cobro hecho / Pago confirmado

**Lo que promete el documento:**
- S7 §2 tabla "Confirmar" (línea 54): "Recibe el aviso de la pasarela y, **si cobró, extiende la suscripción un mes**"
- S7 §1 diagrama (línea 44): `CONF` → `BUSCA` (la confirmación habilita la aparición en búsquedas)
- S7 §5.1 tabla: `IniciarCobroAsync` → "Por el aviso o la vuelta, vía `LeerConfirmacionAsync`" (línea 132)
- RN-S7-04 (línea 167): "Confirmar es **idempotente**: un intento ya resuelto no vuelve a extender nada"

**Plantilla:** ❌ **No existe** en `Plantillas.cs` ni en `TipoNotificacion`

**Quién debería encolarlo:** `ServicioCobros.ResolverAsync()` (ServicioCobros.cs:656-788)
- Es el camino común para **ambos** orígenes de confirmación:
  - Aviso de la pasarela (`ConfirmarAsync`, línea 629)
  - Cobro automático del planificador (`CobrarVencimientosAsync`, línea 615)
- Al final devuelve mensaje: `"Pago registrado. Tu suscripción quedó vigente un mes más."` (línea 786) o `"Tu cupo de fundador quedó confirmado y tu mes gratis empezó a correr."` (línea 748)
- **Pero no encola ningún `Notificacion`** — solo registra auditoría

**Estado:** ❌ **FALTA** — El documento da por hecho que hay un aviso al confirmar el pago, pero el código no envía ningún correo

---

### 4. Inscripción confirmada (cupo fundador)

**Lo que promete el documento:**
- RN-S7-01 (línea 164): "El cupo fundador se confirma al **inscribir el medio de pago**, no al reservarlo ni al primer pago real"
- S7 §5.1: `IniciarInscripcionAsync` → "Igual: devuelve referencia y dirección a la que mandarla" (línea 133)
- ServicioCobros.ResolverAsync (línea 747): mensaje de confirmación: `"Tu cupo de fundador quedó confirmado y tu mes gratis empezó a correr."`

**Plantilla:** ❌ **No existe** en `Plantillas.cs` ni en `TipoNotificacion`

**Quién debería encolarlo:** `ServicioCobros.ResolverAsync()` rama `Inscripcion` (líneas 730-748)
- Cuando `intento.Tipo == TipoDeIntento.Inscripcion` (línea 730)
- Llama a `ConfirmarCupoFundadorAsync` y `PasarAFundadorConMesGratisAsync`
- Devuelve mensaje de éxito pero **no encola notificación**

**Estado:** ❌ **FALTA** — La confirmación del cupo fundador (momento clave para el fundador) no genera correo

---

### 5. No pudimos cobrarte (cobro rechazado / fallido)

**Lo que promete el documento:**
- S7 §9 "Lo que todavía no existe" (línea 221): "y **si hay un correo propio de «no pudimos cobrarte»**" — marcado como decisión pendiente de Pablo (política de rechazo, RN-S7-13)
- RN-S7-13 (línea 176): "Un cobro automático **no se reintenta solo**... el intento se queda `Pendiente` con su clave, nadie extiende nada y lo mira una persona"

**Plantilla:** ❌ **No existe** (conocido)

**Quién debería encolarlo:** `ServicioCobros.ResolverAsync()` rama `!r.Aprobado` (líneas 687-707)
- Cuando la pasarela rechaza el cobro (`r.Aprobado == false`)
- Marca el intento como `Fallido` (línea 689)
- Si es inscripción, libera el cupo fundador (línea 694-695)
- Devuelve mensaje: `"El pago no se completó. Puedes intentarlo otra vez."` (línea 706)
- **Pero no encola notificación** — y el documento dice explícitamente que es una decisión pendiente

**Estado:** ❌ **FALTA (conocida, decisión pendiente de Pablo)** — RN-S7-13 dice que no se reintenta solo; falta definir si hay correo propio y cuándo

---

## Qué falta en el código (resumen técnico)

### Enumeración `TipoNotificacion` (Enumeraciones.cs:50-57) — faltan:
```csharp
// Faltan para S7:
CobroConfirmado,        // Pago exitoso (cobro manual o automático)
InscripcionConfirmada,  // Cupo fundador confirmado + mes gratis iniciado
CobroRechazado,         // Pago fallido (tarjeta rechazada, sin saldo, etc.)
```

### Plantillas en `Plantillas.cs` — faltan:
- `TienePlantilla()` debe incluir los tres nuevos tipos
- Métodos `Renderizar` para cada uno:
  - `CobroConfirmado(nombre, enlace, monto?, plan?)` — "Tu pago de $X por [plan] se confirmó. Tu suscripción está vigente hasta [fecha]."
  - `InscripcionConfirmada(nombre, enlace, diasGratis?)` — "Tu cupo de fundador quedó confirmado. Tu mes gratis de [N] días empezó a correr."
  - `CobroRechazado(nombre, enlace, motivo?)` — "No pudimos cobrar tu suscripción. Motivo: [motivo]. Tu ficha sigue vigente [N] días más."

### Encolado en `ServicioCobros.ResolverAsync()`:
- Rama `Aprobado == true` + `TipoDeIntento.Cobro` → encolar `CobroConfirmado`
- Rama `Aprobado == true` + `TipoDeIntento.Inscripcion` → encolar `InscripcionConfirmada`
- Rama `Aprobado == false` → encolar `CobroRechazado` (sujeto a política de rechazo)

### Claves de idempotencia sugeridas:
- `CobroConfirmado:{IntentoDePagoId}`
- `InscripcionConfirmada:{IntentoDePagoId}`
- `CobroRechazado:{IntentoDePagoId}`

---

## Conclusión

| Categoría | Cantidad |
|---|---|
| **Existen y funcionan** | 2 (por vencer ×2 umbrales, vencida) |
| **Faltan (prometidos por el documento)** | 2 (cobro confirmado, inscripción confirmada) |
| **Faltan (conocidos, decisión pendiente)** | 1 (cobro rechazado / "no pudimos cobrarte") |

Los dos correos que **sí existen** cubren el vencimiento (antes y en el día). Los que **faltan** son la confirmación positiva (pago e inscripción) y la negativa (rechazo), que el documento da por hechos o deja como decisión explícita.

> **Nota:** Los avisos de cita (`CitaSolicitada`, `CitaConfirmada`, `Recordatorio24h`, etc.) son de **Joaquín (JQ-1, JQ-3)** y **no entran** en esta auditoría, según la regla de la Tanda 8.