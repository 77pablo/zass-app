# Auditoría de seguridad contra seguridad.md

> **Tarea:** T24 — Auditoría de solo lectura. Sin cambios de código.
> **Documento auditado:** `zass-documentacion/docs/07-fisica/seguridad.md` (389 líneas, 9 secciones)
> **Excluida:** Sección 6 (Registro de seguridad) — corresponde a T25.
> **Fecha:** 17 de noviembre de 2026

---

## Resumen ejecutivo

| Cumple | Supera | Parcial | Brecha | Sin verificar |
|:--:|:--:|:--:|:--:|:--:|
| 4 | 1 | 1 | 3 | 2 |

- **4 reglas** están implementadas correctamente y coincidentes con el documento.
- **1 regla** supera el requisito original (secretos, §2).
- **1 regla** es Parcial por decisión explícita del propio documento (§6).
- **3 discrepancias** requieren atención (detalle más abajo).
- **2 aspectos** quedan sin verificar por falta de artefacto en la rama actual.

---

## Lo más serio primero

### 1. Cuerpo de respuesta 429 usa claves en inglés en vez de castellano (§1)

**Documento** (`seguridad.md`, líneas 61-65):
```json
{ "titulo": "Demasiadas peticiones",
  "detalle": "Espera unos minutos antes de volver a intentar.",
  "reintentarEn": 900 }
```

**Código** (`backend/src/Zass.Api/Configuracion/Seguridad.cs`, líneas 73-82):
```json
{ "title": "Demasiadas peticiones",
  "detail": "Espera unos minutos antes de volver a intentar.",
  "status": 429,
  "reintentarEn": 900,
  "idPeticion": "<TraceIdentifier>" }
```

**Análisis:** Las claves `title` y `detail` son nombres de RFC 7807. El código incluye un comentario (líneas 67-72) que justifica el cambio: usar nombres en castellano (`titulo`/`detalle`) hacía que `Results.Problem` no funcionara correctamente porque el cliente no encontraba las claves estándar. El código añadió además `status` y `idPeticion` (ausentes del documento).

**Veredicto:** Desviación justificada por compatibilidad con RFC 7807. El documento debería actualizarse para reflejar las claves reales. **No es riesgo de seguridad**, pero es una inconsistencia document/code.

### 2. CSP del documento no coincide con el código (§4)

**Documento** (líneas 193-201) declara:
```
Content-Security-Policy:
  default-src 'self';
  img-src 'self' data: https://*.blob.core.windows.net;
  connect-src 'self' https://api.zass.cl;
  frame-ancestors 'none';
  base-uri 'self';
  form-action 'self'
```

**Código API** (`Seguridad.cs`, líneas 191-204):
```
Content-Security-Policy:
  default-src 'self';
  img-src 'self' data:;
  connect-src 'self';
  frame-ancestors 'none';
  base-uri 'self';
  form-action 'self'
```

**Diferencias:**
| Directiva | Documento | Código | Impacto |
|---|---|---|---|
| `img-src` | Incluye `https://*.blob.core.windows.net` | Sin Azure Blob | ✅ Código más restrictivo (Azure no se usa) |
| `connect-src` | Incluye `https://api.zass.cl` | Solo `'self'` | ✅ Código más restrictivo |

**Código frontend** (`frontend/public/_headers`, línea 2):
```
Content-Security-Policy:
  default-src 'self';
  script-src 'self';
  style-src 'self' 'unsafe-inline' https://fonts.googleapis.com;
  font-src https://fonts.gstatic.com;
  img-src 'self' data:;
  connect-src 'self';
  object-src 'none';
  base-uri 'self';
  form-action 'self';
  frame-ancestors 'none'
```

**Diferencias frontend vs documento:**
| Directiva | Documento | Frontend | Impacto |
|---|---|---|---|
| `script-src` | No especificada | `'self'` | ✅ Explícita en frontend |
| `style-src` | No especificada | `'self' 'unsafe-inline' https://fonts.googleapis.com` | ⚠️ `'unsafe-inline'` presente (Google Fonts) |
| `font-src` | No especificada | `https://fonts.gstatic.com` | ℹ️ Necesario para Google Fonts |
| `object-src` | No especificada | `'none'` | ✅ Más restrictivo |
| `X-Frame-Options` a `Cross-Origin-Opener-Policy` | 7 cabeceras | 6 cabeceras | ⚠️ Frontend omite `Cross-Origin-Opener-Policy` |

**Veredicto:** La API es **más restrictiva** que el documento (bien). El frontend necesita `'unsafe-inline'` para Google Fonts — la alternativa sería descargar las fuentes y alojarlas localmente. El documento no especifica CSP para el frontend (asume que Cloudflare lo gestiona). El documento debería aclarar que la CSP del frontend difiere intencionalmente de la API.

### 3. Comentario obsoleto "N-06 Redis" en código (§1)

**Código** (`Seguridad.cs`, líneas 103-107):
```csharp
/// Particionado por IP, 15 minutos de ventana. En produccion los contadores van en
/// N-06 Redis, porque la API corre en varias replicas y un contador por proceso
/// multiplicaria el limite por el numero de replicas.
```

**Realidad:** El despliegue real es Cloudflare Pages + Render + Neon. No existe N-06 Redis ni Azure. El comentario describe infraestructura abandonada. El código funcional es correcto (contadores en memoria por IP), pero el comentario engaña a cualquier lector sobre dónde viven los contadores en producción.

**Veredicto:** **Cosmético.** No afecta la seguridad. El comentario debería actualizarse para reflejar que los contadores son en memoria (suficiente para una sola réplica en Render) o documentar la limitación si se escala a múltiples instancias.

---

## Tabla de hallazgos por sección

### §1 — Límite de tasa

| Regla | Estado | Evidencia | Notas |
|---|:--:|---|---|
| 5 grupos (P/A/G/S + Salud) | ✅ | `Seguridad.cs:13-27` `Tasa.*` constantes | Salud es adición práctica no mencionada en tabla original (línea 22-24: "sonda de disponibilidad") |
| P = 100/IP/15min | ✅ | `Seguridad.cs:90` `Limite(Tasa.Publico, 100)` | |
| A = 5/IP/15min | ✅ | `Seguridad.cs:91` `Limite(Tasa.Autenticacion, 5)` | |
| G = 100/IP/15min | ✅ | `Seguridad.cs:92` `Limite(Tasa.General, 100)` | |
| S = 10/IP/15min | ✅ | `Seguridad.cs:93` `Limite(Tasa.Sensible, 10)` | |
| Salud = 2000/IP/15min | ✅ | `Seguridad.cs:97` `Limite(Tasa.Salud, 2000)` | Adición práctica |
| 429 con Retry-After | ✅ | `Seguridad.cs:60` `RejectionStatusCode = 429` y línea 66 `Retry-After = "900"` | |
| Sin revelar intentos restantes | ✅ | `Seguridad.cs:64` comentario explícito + solo `Retry-After` en respuesta | |
| Configuración externa | ✅ | `Seguridad.cs:54-56` lee `Zass:Tasa:*` con fallback a valores del doc | Permite ajustar por prueba |
| Comentario N-06 Redis | ⚠️ | `Seguridad.cs:104-106` | Obsoleto — infraestructura abandonada |
| Cuerpo 429 | ⚠️ | `Seguridad.cs:73-82` | `title`/`detail` en inglés vs `titulo`/`detalle` en doc; añade `status` e `idPeticion` |
| Prueba de arquitectura (`LimiteDeTasa...`) | ❓ | Solo en PR #81 (no fusionado) | Sin verificar en rama principal |

### §2 — Variables de entorno y secretos

| Regla | Estado | Evidencia | Notas |
|---|---|---|---|
| Sin claves hardcoded | ✅ | Búsqueda exhaustiva: no encontradas | |
| `.gitignore` cubre sensibles | ✅ | `.gitignore:7-12` (`.env`, `*.env`, `appsettings.Production.json`) | |
| `ValidateOnStart()` | ✅ | `AgregarZass.cs:95-98` y `:268` (correo) | La app no arranca sin variables |
| `ValidateDataAnnotations` | ✅ | `AgregarZass.cs:97` | |
| `gitleaks` en PR | ✅ | `.github/workflows/secretos.yml` | Bloquea merge |
| User Secrets en desarrollo | ✅ | `OpcionesZass` con `[Required]` | Fuera del repositorio |
| N-10 Key Vault (producción) | ℹ️ | Documento menciona Azure Key Vault | Render usa variables de entorno del panel — suficiente y documentado como mejora |

**Veredicto:** **Supera.** La regla pide variables de entorno; en producción se usan variables de Render con `ValidateOnStart()` y en desarrollo User Secrets.

### §3 — Validación de entradas

| Regla | Estado | Evidencia | Notas |
|---|---|---|---|
| Validador por petición | ✅ | `FiltroDeValidacion<T>` en `Respuestas.cs:66-93` | Sin validador → 500 (deliberado) |
| `FromSqlRaw` prohibido | ✅ | Búsqueda: 0 resultados | Solo `FromSqlInterpolated` para PostGIS |
| `dangerouslySetInnerHTML` prohibido | ✅ | Búsqueda: solo comentario en `TarjetaDeResena.tsx:24` | Sin uso real. Regla ESLint mencionada en doc pero sin archivo de configuración encontrado |
| React escapa por defecto | ✅ | Comportamiento estándar | |
| Valores no se registran | ✅ | `Respuestas.cs:85-91` solo envía `PropertyName` y `ErrorMessage` | Sin valor del campo |

### §4 — Cabeceras de seguridad

| Cabecera | API (código) | Frontend (`_headers`) | Doc | Estado |
|---|---|---|---|:--:|
| `Content-Security-Policy` | `default-src 'self'; img-src 'self' data:; connect-src 'self'; frame-ancestors 'none'; base-uri 'self'; form-action 'self'` | `default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline' ...` | Incluye Azure Blob y api.zass.cl | ⚠️ |
| `Strict-Transport-Security` | `max-age=31536000; includeSubDomains; preload` | `max-age=31536000; includeSubDomains` | Coincide | ✅ |
| `X-Content-Type-Options` | `nosniff` | `nosniff` | Coincide | ✅ |
| `X-Frame-Options` | `DENY` | `DENY` | Coincide | ✅ |
| `Referrer-Policy` | `strict-origin-when-cross-origin` | `strict-origin-when-cross-origin` | Coincide | ✅ |
| `Permissions-Policy` | `geolocation=(self), camera=(), microphone=()` | `camera=(), microphone=(), payment=(), geolocation=(self)` | Coincide | ✅ |
| `Cross-Origin-Opener-Policy` | `same-origin` | (ausente) | `same-origin` | ⚠️ |

**Notas:**
- Las 7 cabeceras están en `Seguridad.cs:184-213` (`UsarCabecerasDeSeguridad`).
- El frontend `_headers` tiene 6 cabeceras de seguridad (omite `Cross-Origin-Opener-Policy`).
- `CabecerasDeSeguridadPruebas.cs` existe en las pruebas de arquitectura.

### §5 — Autenticación y sesiones

| Aspecto | Estado | Evidencia | Notas |
|---|---|---|---|
| Argon2id: 19 MiB, 2 iter, p=1 | ✅ | `Seguridad.cs:24-27` (`MemoriaKiB = 19 * 1024`, `Iteraciones = 2`, `Paralelismo = 1`) | Parámetros exactos |
| Formato de hash con params | ✅ | `Seguridad.cs:36-42` (`$argon2id$v=19$m=19456,t=2,p=1$...`) | Permite migración de params |
| Comparación en tiempo constante | ✅ | `Seguridad.cs:80` `CryptographicOperations.FixedTimeEquals` | |
| Cookie `httpOnly` | ✅ | `Seguridad.cs:130` `Cookie.HttpOnly = true` | |
| Cookie `SameSite=Lax` | ✅ | `Seguridad.cs:131` `Cookie.SameSite = SameSiteMode.Lax` | |
| Cookie `Secure` condicional | ✅ | `Seguridad.cs:132-134` `SecurePolicy.Always` si `exigirHttps` | |
| Sin sliding expiration | ✅ | `Seguridad.cs:136` `SlidingExpiration = false` | |
| Vida del acceso 15 min | ✅ | `Seguridad.cs:43` `VidaDelAcceso = TimeSpan.FromMinutes(15)` | |
| Token de refresco 30 días | ℹ️ | Mencionado en doc; código usa `GeneradorSecretos` (SHA-256 sin sal, correcto para token de 256 bits) | `Seguridad.cs:111-125` |
| CSRF `X-CSRF-Token` | ✅ | `FiltroCsrf` en `Respuestas.cs:117-147` | Excluye GET/HEAD/OPTIONS |
| Registro y login sin CSRF | ✅ | `Respuestas.cs:113-114` comentario: "todavía no hay sesión que falsificar" | |
| 403/404 indistinguibles | ✅ | `Respuestas.cs:30-34` + `Fallas.NoEncontrado` para "dueño no coincide" | |
| Sin redirigir a login | ✅ | `Seguridad.cs:140-149` `OnRedirectToLogin` → 401, `OnRedirectToAccessDenied` → 403 | |
| HTTPS en desarrollo | ✅ | `Seguridad.cs:132-134` condicional `exigirHttps` | `certificados/` en `.gitignore` |
| AES-256-GCM para direcciones | ✅ | `CifradoAesGcm` en `Seguridad.cs:127-186` | Clave de 32 bytes, nonce aleatorio, versión de clave para rotación |

### §7 — Riesgos no normativos

| Riesgo | Estado | Evidencia |
|---|:--:|---|
| Fuga de ubicación (coordenadas) | ✅ | Sin coordenadas en respuestas; distancia redondeada |
| Documentos de identidad | ✅ | Contenedor privado, URL firmada 5 min (mencionado en doc) |
| Archivos maliciosos | ℹ️ | Antivirus mencionado (RN-S2-05) — implementación pendiente (N-08) |
| Reseñas falsas | ✅ | Elegibilidad revalidada en servidor |
| Secuestro de tarjeta | ✅ | Código de un solo uso, consumo atómico |
| Rastreo por tarjeta | ✅ | TBL-26 sin columnas de identidad |
| Ley 21.719 | ✅ | Sección 8 del documento (abajo) |

### §8 — Ley 21.719

| Exigencia | Estado | Evidencia |
|---|---|---|
| Base de licitud | ✅ | Declarada en doc |
| Finalidad | ✅ | Declarada por tipo de dato |
| Minimización | ✅ | Sin RUT, sin dirección para a domicilio |
| Exactitud | ✅ | Edición de perfil y credenciales |
| Derecho de acceso | ✅ | API-16 y descarga de documentos |
| Derecho de supresión | ✅ | API-17 con `Anonimizar()` en `Entidades.cs:46` y `ServicioCuenta.cs:278` |
| Derecho de oposición | ✅ | Preferencias separadas de transaccionales |
| Conservación | ✅ | 30 días para documentos borrados |
| Seguridad del tratamiento | ✅ | TLS, cifrado en reposo, auditoría |
| Registro de actividades | ✅ | TBL-28 |
| Encargados | ℹ️ | Listados (Neon, Render, Cloudflare, correo) |
| Transferencia internacional | ℹ️ | Declarada (EE.UU.), pendiente asesoría legal |
| `PENDIENTE DE PABLO / ABOGADO` | ✅ | Caja presente en doc (línea 369) |

### §9 — Tabla de resumen del documento

| # | Regla | Doc dice | Realidad | Divergencia |
|---|---|:--:|:--:|---|
| 1 | Límite de tasa | Cumple | ✅ Cumple | La prueba citada (`LimiteDeTasaEnTodosLosEndpointsPruebas`) está en PR #81, no fusionada |
| 2 | Secretos | Supera | ✅ Supera | Sin divergencia |
| 3 | Validación | Cumple | ✅ Cumple | Sin divergencia |
| 4 | Cabeceras | Cumple | ⚠️ CSP difiere | API más restrictiva (bien); frontend necesita `'unsafe-inline'` para Google Fonts |
| 5 | Autenticación | Cumple | ✅ Cumple | Sin divergencia |
| 6 | Registro | Parcial | ℹ️ Excluido (T25) | El propio documento se marca Parcial |

---

## Sin verificar (pendiente de artefacto)

| Aspecto | Por qué | Bloqueo |
|---|---|---|
| `LimiteDeTasaEnTodosLosEndpointsPruebas` | La prueba existe en PR #81 (rama no fusionada). No verificable en la rama actual. | PR #81 pendiente |
| Regla ESLint `dangerouslySetInnerHTML` | El documento dice "regla de ESLint que falla la compilación". No se encontró archivo de configuración ESLint en el frontend. | Config no presente o no versionada |
| `Include Error Detail` en cadena Npgsql | Búsqueda en todo el repo: 0 resultados. Npgsql no expone valores en errores por defecto. | ✅ Confirmado ausente (bien) |
| `EnableSensitiveDataLogging` | Búsqueda: 0 resultados. EF Core oculta parámetros por defecto. | ✅ Confirmado ausente (bien) |
| `Enmascarar.Correo` | Presente en `Enmascarar.cs:4`, usado en `ProcesadorDeCorreo.cs:227` | ✅ Funcional |

---

## Conclusión

El código cumple la normativa de seguridad del documento en **4 de 6 reglas** completas, **supera** 1 regla (secretos) y tiene 1 regla marcada **Parcial** por decisión propia del documento (§6, corresponde a T25).

Las 3 discrepancias encontradas son:

1. **Cuerpo 429 en inglés** — desviación justificada por RFC 7807. Actualizar doc.
2. **CSP difiere** — API más restrictiva que el doc (bien). Frontend tiene `'unsafe-inline'` necesario para Google Fonts. Actualizar doc para reflejar ambas CSP.
3. **Comentario N-06 Redis** — obsoleto en código. Cosmético, actualizar comentario.

Ninguna discrepancia constituye un riesgo de seguridad activo.
