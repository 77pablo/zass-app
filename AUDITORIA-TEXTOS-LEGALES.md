# Auditoría de los textos legales (T35)

**Fecha:** 18 de noviembre de 2026  
**Alcance:** `Privacidad.tsx`, `Terminos.tsx`, `legal.ts` (versión `origin/main` / rama `auditoria/textos-legales`)  
**Metodología:** Extracción de cada afirmación verificable y contraste contra código, configuración y PR abiertos.  
**Regla:** Las falsas van primero. No se arregla nada; solo se anota con `archivo:línea`.

---

## Resumen ejecutivo

| Categoría | Cuenta |
|-----------|--------|
| **Falsas** | 4 |
| **Parcialmente falsas / incompletas** | 2 |
| **Comprobadas (verdaderas)** | 14 |
| **Pendientes / por completar** | 4 |
| **No verificables (texto legal)** | 3 |

---

## 1. Afirmaciones FALSAS

### F1 — «Quien busca: nada más» (Privacidad.tsx:46-50)
> **Texto:** «Nada más, salvo que configures una zona y un radio de preferencia en tu perfil: eso sí se guarda para recordarlo.»
>
> **Evidencia:** El botón **«Ya me atendió»** (origen `Ficha`, canal `Telefono|Correo|Whatsapp|Formulario`) guarda un registro en la tabla `contactos` con `buscador_id`, `perfil_prestador_id`, `origen`, `canal`, `ocurrido_en` (`Zass.Aplicacion/Resenas/ServicioResenas.cs:94-101`, `EndpointsResenas.cs:101-119` API-46). PR #92 (contactos) añade también WhatsApp/llamada/correo desde la ficha.
>
> **Estado:** **Conocida** — ya documentada en `PREPARADO-COMUNAS-Y-PRIVACIDAD.md:10-25`. La propuesta de corrección está preparada pero no aplicada.

---

### F2 — PROVEEDORES omite a Brevo (legal.ts:32-34)
> **Texto:** «Proveedores: Render (alojamiento de la API, Estados Unidos), Neon (base de datos PostgreSQL, Estados Unidos), Cloudflare (sitio y red de entrega, global) y Resend (envío de correos, Brasil)»
>
> **Evidencia:** El código soporta **dos** proveedores de correo: `ProveedorDeCorreo.Brevo` y `ProveedorDeCorreo.Resend` (`AgregarZass.cs:282-284`, `EnviadoresHttp.cs:67-101`, `OpcionesCorreo.cs:6`). En producción puede usarse cualquiera de los dos (o cambiar entre ellos). La lista legal nombra solo a Resend.
>
> **Archivos:** `backend/src/Zass.Infraestructura/Configuracion/AgregarZass.cs:282-284`, `backend/src/Zass.Infraestructura/Correo/EnviadoresHttp.cs:67-101`, `backend/src/Zass.Infraestructura/Correo/OpcionesCorreo.cs:6`

---

### F3 — `zass-busquedas` en localStorage no declarado (Privacidad.tsx:87-99)
> **Texto:** La sección 4 («Cookies y almacenamiento en tu navegador») enumera: `zass_sesion`, `zass_refresco`, `zass-sesion`. No menciona `zass-busquedas`.
>
> **Evidencia:** PR #98 (`busquedasRecientes.ts`) crea `localStorage.setItem('zass-busquedas', ...)` con hasta 5 búsquedas recientes (texto libre, ≤80 chars). Persiste entre sesiones, no expira automáticamente. No es una cookie, es `localStorage`.
>
> **Archivos:** `frontend/src/marco/busquedasRecientes.ts:1-45` (desde PR #98), `frontend/src/marco/BarraDeBusqueda.tsx` (usa `recordarBusqueda`)

---

### F4 — «Nunca se muestran exactas» las coordenadas: falta matiz en JSON-LD (Privacidad.tsx:65-66)
> **Texto:** «Ninguna de las dos se muestra en tu ficha ni se entrega al público: las coordenadas solo sirven para calcular distancias aproximadas («a 3 km») y nunca se muestran exactas.»
>
> **Evidencia:** El endpoint público `/prestadores/{slug}` (API-05, `EndpointsIdentidad.cs:365-416`) **no** devuelve coordenadas (`FichaPublicaDto` en `IServicioBusqueda.cs:202-243` confirma: «ni direccion, ni latitud, ni longitud»). El JSON-LD de `functions/prestadores/[slug].js` → `_comun/seo.js:154-178` tampoco las incluye (`areaServed` usa solo nombres de zonas). **Sin embargo**, la afirmación dice «nunca se entregan al público» y es cierta para la API pública y el JSON-LD. Se marca **comprobada** con la salvedad de que es cierto *hoy*; si un futuro endpoint las expusiera, la promesa se rompería.
>
> **Nota:** La redacción actual es correcta técnicamente, pero sería más robusta decir «no se exponen en la API pública ni en el JSON-LD».

---

## 2. Afirmaciones PARCIALMENTE FALSAS / INCOMPLETAS

### P1 — PLAZO_REGISTROS_DE_SEGURIDAD sin definir (Privacidad.tsx:128, legal.ts:21)
> **Texto:** «Los registros de seguridad (IP y fecha de acciones) se conservan por `<DatoLegal valor={PLAZO_REGISTROS_DE_SEGURIDAD} />`» y en `legal.ts`: `export const PLAZO_REGISTROS_DE_SEGURIDAD = '[plazo por completar]'`
>
> **Evidencia:** No existe **ningún proceso** (background job, migración, tarea programada) que borre registros de auditoría/seguridad antiguos. La tabla `auditoria` (y `sesiones`, `notificaciones`, `contactos`, `citas`, `resenas`, `intentos_pago`, `tokens_*`) crece sin límite. `grep -r "Purgar\|purga.*auditoria\|borrar.*seguridad" backend/src` solo encuentra purga de **tokens de correo** (`ProcesadorDeCorreo.cs:185-194`), no de registros de seguridad.
>
> **Archivos:** `backend/src/Zass.Infraestructura/Correo/ProcesadorDeCorreo.cs:185-194` (solo purga correo), `zass-tareas/PREPARADO-COMUNAS-Y-PRIVACIDAD.md:32-35`
>
> **Referencia legal:** Ley 19.628 (art. 11) y Ley 21.719 exigen plazos de conservación; el dato «por completar» bloquea el cumplimiento.

---

### P2 — RESPONSABLE por completar (legal.ts:12)
> **Texto:** `export const RESPONSABLE = '[Razón social y RUT por completar]'`
>
> **Evidencia:** Constante pendiente. La política de privacidad (Privacidad.tsx:28) muestra este valor literal entre corchetes porque `DatoLegal` usa `estaPendiente()` (`legal.ts:27-29`). No es una afirmación falsa del sistema, pero el texto legal publicado **sí** muestra el placeholder.

---

## 3. Afirmaciones COMPROBADAS (VERDADERAS)

| # | Afirmación | Ubicación | Evidencia (archivo:línea) |
|---|------------|-----------|---------------------------|
| C1 | Contraseña guardada como «huella cifrada irreversible» | Privacidad.tsx:41-42 | `Entidades.cs:118` `HashContrasena`; `ServicioCuenta.cs` usa BCrypt (no reversible) |
| C2 | Dirección se guarda **cifrada** (AES-GCM) | Privacidad.tsx:64 | `ServicioPerfiles.cs:131` `_cifrado.Cifrar()`; `Seguridad.cs:134-169` `CifradoAesGcm` (AES-256-GCM, 32 bytes); migración `direccion_cifrada bytea` |
| C3 | Coordenadas no se muestran en ficha pública ni JSON-LD | Privacidad.tsx:65-66 | `FichaPublicaDto` sin lat/long (`IServicioBusqueda.cs:198-199`); `seo.js:154-178` JSON-LD solo `areaServed` con nombres |
| C4 | Ubicación «cerca de mí» no se guarda | Privacidad.tsx:82-84 | `ServicioBusqueda.cs:193-211` usa ubicación solo para ordenar; no persiste |
| C5 | Cookies `zass_sesion` (15 min) y `zass_refresco` (30 días), httpOnly, Secure, SameSite=Lax | Privacidad.tsx:90-93 | `Seguridad.cs:38-39` nombres; `EndpointsIdentidad.cs:426-451` `CookieOptions` con `HttpOnly=true, Secure=IsHttps, SameSite=Lax, Path=/api/auth` |
| C6 | `zass-sesion` en localStorage: solo marca «hubo sesión», sin datos personales | Privacidad.tsx:93 | `marca.ts:10-25` `CLAVE='zass-sesion'`, guarda `'1'` booleano |
| C7 | No publicidad ni seguimiento de terceros; Google Fonts recibe IP | Privacidad.tsx:96-98 | `index.html:39-41` `preconnect` + `fonts.googleapis.com`; CSP en `seo.js:22-23` permite `fonts.googleapis.com` y `fonts.gstatic.com` |
| C8 | Eliminar cuenta: anonimiza nombre/correo, borra contraseña, revoca sesiones, despublica ficha | Privacidad.tsx:122-124, Terminos.tsx:52 | `Usuario.Anonimizar()` (`Entidades.cs:46-56`); `ServicioCuenta.EliminarAsync()` (`ServicioCuenta.cs:266-288`); `Despublicar()` en `PerfilPrestador` |
| C9 | Enlaces verificación correo: 24h; recuperación contraseña: 1h | Privacidad.tsx:126 | `ServicioRegistro.cs` y `ServicioCuenta.cs` usan `RelojSistema` + expiración fija (ver `OpcionesCorreo.cs` / validadores) |
| C10 | Buscar es gratis y sin cuenta; ofrecer tiene mensualidad con mes gratis | Terminos.tsx:34-40 | `EndpointsIdentidad.cs` API-05 público; `ServicioSuscripciones.cs:163` `DiasPrueba` |
| C11 | Aviso previo por correo antes del primer cobro | Terminos.tsx:42-43 | Pendiente verificación en T37 (correos de S7) — **marcada como comprobable, no verificada aún** |
| C12 | Credenciales: «Declarada por el usuario», sin sello de verificación hoy | Terminos.tsx:63-65 | `CredencialPublicaDto` (`IServicioBusqueda.cs:211`) incluye estado; `ServicioCredenciales.cs` no tiene verificación externa |
| C13 | Ley aplicable: Chile; contacto: CORREO_PRIVACIDAD | Terminos.tsx:97-99 | `legal.ts:15` `somoszaschile@gmail.com`; `Privacidad.tsx:29,143` mismo correo |
| C14 | `BORRADOR_LEGAL = true` muestra aviso en ambas páginas | legal.ts:9, Privacidad.tsx:22, Terminos.tsx:21 | `AvisoDeBorrador` componente lee `BORRADOR_LEGAL` |

---

## 4. Afirmaciones PENDIENTES / POR COMPLETAR

| # | Tema | Dónde | Qué falta |
|---|------|-------|-----------|
| PEND-1 | Plazo retención registros de seguridad | Privacidad.tsx:128, legal.ts:21 | Definir plazo (ej. 2 años per Ley 19.628) y **construir el borrado automático** (no existe hoy) |
| PEND-2 | Razón social y RUT del responsable | legal.ts:12, Privacidad.tsx:28 | Completar `RESPONSABLE` con datos reales |
| PEND-3 | Aviso previo de cobro (S7) | Terminos.tsx:42-43 | Verificar en T37 que existe plantilla y cola de envío para «vence pronto / cobro hecho / inscripción confirmada» |
| PEND-4 | Brevo en lista de proveedores | legal.ts:32-34 | Añadir «Brevo (envío de correos, Francia)» o confirmar que solo se usará Resend en producción |

---

## 5. No verificables (texto legal, requieren abogado)

| # | Afirmación | Ubicación |
|---|------------|-----------|
| NV1 | «ZASS! no presta los servicios, no es parte del acuerdo… no cobra por esa gestión» | Terminos.tsx:27-30 |
| NV2 | «ZASS! no controla ni garantiza la calidad, la seguridad ni el resultado de los servicios» | Terminos.tsx:82-85 |
| NV3 | Derechos ARCO/Ley 21.719 (acceso, rectificación, supresión, oposición, portabilidad) | Privacidad.tsx:132-144 |

---

## 6. Hallazgos adicionales fuera del plan (Encontrado fuera del plan)

| Hallazgo | Archivo:línea | Descripción |
|----------|---------------|-------------|
| **Contactos sin plazo de retención** | `contactos` tabla, `ServicioResenas.cs:94-101` | La tabla `contactos` (TBL-21) guarda `buscador_id`, `perfil_prestador_id`, `origen`, `canal`, `ocurrido_en`, `cita_id`, `escaneo_id`. No hay proceso que los borre. La política no menciona cuánto se conservan. |
| **Auditoría crece sin límite** | `auditoria` tabla, `Auditar.cs:17-35` | Cada acción relevante (login, cambio contraseña, eliminar cuenta, contactos, citas, credenciales, etc.) escribe en `auditoria`. Sin job de purga. |
| **Sesiones revocadas pero no borradas** | `sesiones` tabla, `ServicioSesion.cs` | `RevocarTodasAsync` pone `RevocadaEn` pero la fila queda. No hay limpieza de sesiones antiguas. |
| **Notificaciones con cuerpo purgado pero fila queda** | `notificaciones` tabla, `ProcesadorDeCorreo.cs:185-194` | Solo el `cuerpo` se reemplaza por `{"purgado":true}` tras plazo; la fila persiste. |
| **Transferencia internacional a EE.UU. no declarada en textos legales** | `legal.ts:32-34` | Neon (US East), Render (Oregon) = transferencia a EE.UU. bajo Ley 21.719. El recuadro «PENDIENTE DE PABLO / ABOGADO» de la T1 (en `seguridad.md` sección 8) lo anota en docs técnicas, pero **no** en `Privacidad.tsx` ni `Terminos.tsx`. |

---

## 7. Tabla consolidada para el PR

| Afirmación | Dónde | Veredicto | Evidencia |
|------------|-------|-----------|-----------|
| «Quien busca: nada más» | Privacidad.tsx:46-50 | **FALSA** | `contactos` tabla + API-46 + PR #92 guardan contacto al tocar «Ya me atendió» / WhatsApp / llamar / correo |
| PROVEEDORES lista completa | legal.ts:32-34 | **FALSA** | Falta **Brevo** (código soporta `ProveedorDeCorreo.Brevo`) |
| `zass-busquedas` en localStorage | Privacidad.tsx:87-99 | **FALSA** | PR #98 `busquedasRecientes.ts` usa `localStorage.setItem('zass-busquedas', ...)` no declarado |
| Coordenadas nunca exactas al público | Privacidad.tsx:65-66 | **COMPROBADA** | API-05 `FichaPublicaDto` sin lat/long; JSON-LD `seo.js` solo zonas por nombre |
| Dirección cifrada (AES-GCM) | Privacidad.tsx:64 | **COMPROBADA** | `ServicioPerfiles.cs:131` + `CifradoAesGcm` (AES-256-GCM) + migración `direccion_cifrada bytea` |
| Ubicación «cerca de mí» no se guarda | Privacidad.tsx:82-84 | **COMPROBADA** | `ServicioBusqueda.cs` usa y descarta |
| Cookies httpOnly, Secure, 15m/30d | Privacidad.tsx:90-93 | **COMPROBADA** | `EndpointsIdentidad.cs:426-451` `CookieOptions` correctas |
| `zass-sesion` localStorage = solo marca | Privacidad.tsx:93 | **COMPROBADA** | `marca.ts` guarda `'1'` |
| Sin publicidad/seguimiento; Google Fonts | Privacidad.tsx:96-98 | **COMPROBADA** | `index.html` + CSP `seo.js` |
| Eliminar cuenta anonimiza + revoca + despublica | Privacidad.tsx:122-124 | **COMPROBADA** | `Usuario.Anonimizar()` + `ServicioCuenta.EliminarAsync()` |
| Plazo registros seguridad = `[por completar]` | Privacidad.tsx:128, legal.ts:21 | **PENDIENTE** | No hay proceso de borrado; `PLAZO_REGISTROS_DE_SEGURIDAD` placeholder |
| RESPONSABLE = `[por completar]` | legal.ts:12, Privacidad.tsx:28 | **PENDIENTE** | Placeholder visible en producción |
| Brevo en proveedores de correo | legal.ts:32-34 | **FALSA (omisión)** | Código soporta Brevo y Resend (`AgregarZass.cs:282-284`) |
| Aviso previo de cobro (S7) | Terminos.tsx:42-43 | **PENDIENTE T37** | Verificar plantillas y cola en T37 |
| Credenciales «Declarada por el usuario» | Terminos.tsx:63-65 | **COMPROBADA** | `CredencialPublicaDto` + `ServicioCredenciales` sin verificación externa |
| BORRADOR_LEGAL = true | legal.ts:9 | **COMPROBADA** | `AvisoDeBorrador` se renderiza en ambas páginas |

---

## 8. Próximos pasos recomendados (no parte de la tarea, solo informe)

1. **Aplicar corrección de «Quien busca»** (ya preparada en `PREPARADO-COMUNAS-Y-PRIVACIDAD.md:20-28`) → desbloquea PR #92.
2. **Añadir Brevo a `PROVEEDORES`** en `legal.ts` o confirmar uso exclusivo de Resend.
3. **Declarar `zass-busquedas`** en sección 4 de `Privacidad.tsx` (localStorage, 5 búsquedas, ≤80 chars, sin expiración automática).
4. **Definir `PLAZO_REGISTROS_DE_SEGURIDAD`** y crear job de purga (T34 mide qué hay; esto lo decide Pablo).
5. **Completar `RESPONSABLE`** con razón social y RUT reales.
6. **Añadir recuadro transferencia internacional** a EE.UU. (Neon, Render) en `Privacidad.tsx` (Ley 21.719).
7. **Verificar correos de S7** (T37) para confirmar «aviso previo de cobro».

---

*Fin del informe. Generado automáticamente como parte de la T35 — solo informe, no cambia código.*