# Auditoría de Retención de Datos — ZASS!

**Fecha:** 18 de noviembre de 2026  
**Tarea:** T34 de la Tanda 8  
**Alcance:** Todas las tablas de `ZassDbContext` que guardan datos de personas  
**Base consultada:** `zass_pruebas` (esquema idéntico a desarrollo `zass`) — solo lectura via `information_schema` y `pg_stat_user_tables`  
**Referencia legal:** Ley 19.628 (Protección de la Vida Privada) y Ley 21.719 (modifica la 19.628) — *marcado como referencia, no como consejo legal*

---

## Resumen ejecutivo

| Categoría | Tablas | Hallazgo principal |
|-----------|--------|-------------------|
| **Identidad y acceso** | `usuarios`, `sesiones`, `usuario_roles`, `perfiles_prestador`, `perfiles_buscador` | `usuarios` se **anonimiza** (no borra) al eliminar cuenta (CS-04); `sesiones` se revocan en cascada; tokens de verificación/recuperación expiran a 24h/1h y se purgan en `Anonimizar()` |
| **Catálogo y geografía** | `rubros`, `servicios`, `prestador_servicios`, `zonas`, `coberturas_prestador` | Datos de catálogo (no PII), salvo `prestador_servicios.precio_referencia` y `coberturas_prestador` (FK a prestador) |
| **Credenciales (S2)** | `entidades_certificadora`, `credenciales`, `documentos_credencial`, `verificaciones` | `documentos_credencial.clave_almacenamiento` y `nombre_original` son PII; `credenciales.nombre` es PII; **no hay purga automática** de documentos antiguos |
| **Agenda (S4)** | `disponibilidades`, `bloques_horario`, `bloqueos_agenda`, `citas`, `cambios_cita` | `citas.nota_buscador` y `clave_idempotencia` son PII; `cambios_cita` es historial inmutable (solo INSERT); **no hay purga de citas antiguas** |
| **Reputación (S5)** | `contactos`, `resenas`, `respuestas_resena`, `reportes_resena` | `resenas.comentario` y `respuestas_resena.texto` son PII; `resenas` **no se borran** al eliminar cuenta (RN-S1-07); `reportes_resena.detalle` y `resolucion` son PII |
| **Tarjetas (S6)** | `tarjetas`, `escaneos_tarjeta` | `tarjetas.url_estable` y `hash_codigo_activacion` son PII; `escaneos_tarjeta` **no tiene columnas de identidad** (R-20, decisión de privacidad) |
| **Suscripciones (S7)** | `planes`, `suscripciones`, `pagos`, `cupos_fundador`, `intentos_de_pago`, `cupones`, `canjes_de_cupon` | `intentos_de_pago.url_para_pagar`, `referencia_pasarela`, `motivo_fallo`, `clave_idempotencia` son PII; `pagos.referencia_pasarela`, `motivo_fallo` son PII; **no hay purga de intentos fallidos antiguos** |
| **Transversal** | `notificaciones`, `registro_auditoria`, `trabajos_de_fondo` | `notificaciones.cuerpo` (tokens) se purga tras envío/fallo (`Purgada = {"purgado":true}`); `registro_auditoria` **solo INSERT, nunca se borra**; `trabajos_de_fondo.ultimo_resumen` es metadato |

---

## Tabla por tabla

### 1. `usuarios` (TBL-01) — Identidad principal
| Columna | Tipo | PII | Se borra hoy | Crece sin límite | Al eliminar cuenta (CS-04) |
|---------|------|-----|--------------|------------------|----------------------------|
| `id` | uuid | FK | No | No | Se conserva (PK) |
| `correo` | citext | **SÍ** | No | No | → `eliminado-{id}@zass.local` |
| `hash_contrasena` | text | **SÍ** | No | No | → `""` (vacío) |
| `nombre` | varchar(150) | **SÍ** | No | No | → `"Usuario eliminado"` |
| `correo_verificado` | boolean | No | No | No | Se conserva |
| `token_verificacion` | text | **SÍ** | **Sí: 24 h** (expira) + purga en `Anonimizar()` | No | → `null` |
| `token_verificacion_expira_en` | timestamptz | Metadato | No | No | → `null` |
| `token_recuperacion` | text | **SÍ** | **Sí: 1 h** (expira) + purga en `Anonimizar()` | No | → `null` |
| `token_recuperacion_expira_en` | timestamptz | Metadato | No | No | → `null` |
| `estado` | varchar(30) | No | No | No | → `Eliminada` |
| `creado_en` | timestamptz | Metadato | No | No | Se conserva |
| `actualizado_en` | timestamptz | Metadato | No | No | Se actualiza |
| `anonimizado_en` | timestamptz | Metadato | No | No | Se setea a `ahora` |

**Plazo sugerido Chile (Ley 21.719, art. 11):** Datos de identificación → **5 años desde la última interacción** o hasta solicitud de supresión. El código ya anonimiza (no borra) al eliminar cuenta, lo que cumple con "bloqueo" más que supresión física.

---

### 2. `sesiones` (TBL-04) — Tokens de acceso
| Columna | Tipo | PII | Se borra hoy | Crece sin límite | Al eliminar cuenta |
|---------|------|-----|--------------|------------------|-------------------|
| `id` | uuid | FK | No | **Sí** (una por login) | **CASCADE** (borra todas) |
| `usuario_id` | uuid | FK | No | No | → borra fila |
| `hash_token` | text | Técnico | No | No | → borra fila |
| `ip_origen` | text | **SÍ** | No | **Sí** | → borra fila |
| `agente_usuario` | text | **SÍ** | No | **Sí** | → borra fila |
| `creada_en` | timestamptz | Metadato | No | No | → borra fila |
| `expira_en` | timestamptz | Metadato | **Sí: expira** (token de 30 días) | No | → borra fila |
| `revocada_en` | timestamptz | Metadato | No | No | → borra fila |

**Hallazgo:** Las sesiones **crecen sin límite** (una fila por login). Al eliminar cuenta, `CASCADE` borra todas. Pero **no hay job que limpie sesiones expiradas/revocadas de cuentas activas**. Plazo sugerido: **30 días post-expiración** (coincide con vida del token).

---

### 3. `perfiles_prestador` (TBL-05) — Perfil público del prestador
| Columna | Tipo | PII | Se borra hoy | Crece sin límite | Al eliminar cuenta |
|---------|------|-----|--------------|------------------|-------------------|
| `id` | uuid | FK | No | No | Se conserva (FK en resenas, etc.) |
| `usuario_id` | uuid | FK | No | No | **CASCADE** (borra perfil) |
| `rubro_id` | smallint | Catálogo | No | No | → borra fila |
| `slug` | varchar(120) | **SÍ** | No | No | → borra fila |
| `nombre_publico` | varchar(150) | **SÍ** | No | No | → borra fila |
| `descripcion` | varchar(1500) | **SÍ** | No | No | → borra fila |
| `url_foto` | text | **SÍ** | No | No | → borra fila |
| `direccion_cifrada` | bytea | **SÍ** (cifrada) | No | No | → borra fila |
| `latitud` / `longitud` | double | **SÍ** | No | No | → borra fila |
| `telefono_publico` | text | **SÍ** | No | No | → borra fila |
| `correo_publico` | text | **SÍ** | No | No | → borra fila |
| `whatsapp` / `sitio_web` | text | **SÍ** | No | No | → borra fila |
| `promedio_calificacion` / `total_resenas` | Derivados | No | No | No | → borra fila |
| `publicado_en` / `actualizado_en` | Metadato | No | No | No | → borra fila |

**Hallazgo:** Al eliminar usuario, el perfil se **borra en cascada** (FK `usuario_id` con `OnDelete.Cascade`). Pero **antes** se llama `perfil.Despublicar()` para que no salga en búsquedas. Los datos personales del perfil **se pierden** (no se anonimizan, se borran). Las reseñas quedan huérfanas de perfil pero conservan `perfil_prestador_id` (FK a perfil borrado → CASCADE borra reseñas también, ver abajo).

---

### 4. `perfiles_buscador` (TBL-06)
| Columna | Tipo | PII | Se borra hoy | Al eliminar cuenta |
|---------|------|-----|--------------|-------------------|
| `usuario_id` | uuid | FK | No | **CASCADE** (borra fila) |
| `favoritos` | jsonb | **SÍ** (lista de IDs) | No | → borra fila |

**Hallazgo:** Se borra en cascada al eliminar usuario. Los favoritos se pierden.

---

### 5. `credenciales` (TBL-12) + `documentos_credencial` (TBL-13) + `verificaciones` (TBL-15)
| Tabla | Columna PII | Se borra hoy | Crece sin límite | Al eliminar cuenta (CASCADE desde perfil_prestador) |
|-------|-------------|--------------|------------------|---------------------------------------------------|
| `credenciales` | `nombre` (nombre del título/certificado) | No | **Sí** (una por credencial declarada) | **CASCADE** (borra credenciales del perfil) |
| `documentos_credencial` | `clave_almacenamiento`, `nombre_original`, `tipo_mime` | No | **Sí** (archivos subidos) | **CASCADE** (borra docs de la credencial) |
| `verificaciones` | `motivo`, `detalle` | No | **Sí** (historial de revisiones) | **CASCADE** (borra verificaciones de la credencial) |

**Hallazgo crítico:** **No hay purga automática** de documentos antiguos ni credenciales vencidas. Los archivos en almacenamiento (R2/S3) **no se borran** al eliminar la fila (la `clave_almacenamiento` es solo referencia). Plazo sugerido Chile: **5 años desde vencimiento de la credencial** (Ley 21.719, datos profesionales).

---

### 6. `citas` (TBL-18) + `cambios_cita` (TBL-19) + `bloques_horario` (TBL-17) + `disponibilidades` (TBL-16) + `bloqueos_agenda` (TBL-20)
| Tabla | Columna PII | Se borra hoy | Crece sin límite | Al eliminar cuenta |
|-------|-------------|--------------|------------------|-------------------|
| `citas` | `nota_buscador` (libre), `clave_idempotencia` | No | **Sí** (historial de citas) | **CASCADE** desde `buscador_id` (borra citas del buscador) / `perfil_prestador_id` (borra citas del prestador) |
| `cambios_cita` | `motivo` (texto libre), `realizado_por` (FK) | No (solo INSERT) | **Sí** (historial inmutable) | **CASCADE** desde `citas` |
| `bloques_horario` | — (solo fechas/estado) | **Sí: `Vencer()`** pone estado `Vencido`, job diario borra libres antiguos (`QuitarBloquesLibresDeAsync`) | No (se borran los libres) | **CASCADE** desde `perfil_prestador` |
| `disponibilidades` | — | No | **Sí** (reglas recurrentes) | **CASCADE** desde `perfil_prestador` |
| `bloqueos_agenda` | `motivo` (texto libre) | No | **Sí** | **CASCADE** desde `perfil_prestador` |

**Hallazgo:** Las citas y su historial **crecen sin límite**. Al eliminar buscador, sus citas se borran (CASCADE). Al eliminar prestador, sus citas se borran (CASCADE). Pero **el historial `cambios_cita` también se borra en cascada**, perdiendo trazabilidad. Plazo sugerido: **2 años post-cita** para historial de cambios (Ley 21.719, registros de actividad).

---

### 7. `contactos` (TBL-21) + `resenas` (TBL-22) + `respuestas_resena` (TBL-23) + `reportes_resena` (TBL-24)
| Tabla | Columna PII | Se borra hoy | Crece sin límite | Al eliminar cuenta |
|-------|-------------|--------------|------------------|-------------------|
| `contactos` | `origen`, `canal` (enum) | No | **Sí** (puerta a reseña) | **CASCADE** desde `buscador_id` / `perfil_prestador_id` |
| `resenas` | `comentario` (1500 chars), `aspectos[]` | **No** (RN-S1-07: "la fila sobrevive anonimizada") | **Sí** | **NO se borra**: `autor_id` → `Anonimizar()` pone `correo = eliminado-{id}@zass.local`, `nombre = "Usuario eliminado"`, pero la reseña **permanece publicada** firmada como "usuario eliminado" |
| `respuestas_resena` | `texto` (1000 chars) | No | **Sí** (una por reseña) | **CASCADE** desde `resenas` (si se borra la reseña, se borra la respuesta) |
| `reportes_resena` | `detalle`, `resolucion` (texto libre) | No | **Sí** | **CASCADE** desde `resenas` |

**Hallazgo crítico:** Las reseñas **no se borran** al eliminar cuenta (por diseño RN-S1-07). El autor se anonimiza en la reseña, pero el texto completo permanece. Esto **cumple** con "derecho a la supresión" (Ley 21.719 art. 11) mediante anonimización, no borrado físico. Plazo sugerido: **Indefinido mientras el prestador exista** (parte de su reputación), anonimizado el autor.

---

### 8. `tarjetas` (TBL-25) + `escaneos_tarjeta` (TBL-26)
| Tabla | Columna PII | Se borra hoy | Crece sin límite | Al eliminar cuenta (CASCADE desde perfil_prestador) |
|-------|-------------|--------------|------------------|---------------------------------------------------|
| `tarjetas` | `url_estable`, `hash_codigo_activacion` | **Sí: `Desactivar()`** pone `hash_codigo_activacion = null` | **Sí** (una por tarjeta emitida) | **FK `perfil_prestador_id` con `OnDelete.SetNull`** → tarjeta queda sin dueño (`perfil_prestador_id = null`), estado `Emitida` o `Desactivada` |
| `escaneos_tarjeta` | **NINGUNA** (R-20: "no tiene columnas de identidad") | No | **Sí** (cada escaneo) | Se conserva (no tiene FK a usuario) |

**Hallazgo:** `escaneos_tarjeta` **crece sin límite** y **no se borra nunca** (no hay identidad del visitante por diseño de privacidad R-20). Plazo sugerido: **13 meses** (razonable para analítica de tarjetas NFC sin identificar personas).

---

### 9. `suscripciones` (TBL-30) + `pagos` (TBL-31) + `intentos_de_pago` (TBL-36) + `cupos_fundador` (TBL-33) + `cupones` (TBL-34) + `canjes_de_cupon` (TBL-35)
| Tabla | Columna PII | Se borra hoy | Crece sin límite | Al eliminar cuenta (CASCADE desde perfil_prestador) |
|-------|-------------|--------------|------------------|---------------------------------------------------|
| `suscripciones` | `referencia_pasarela` (ID cliente en pasarela) | No | **Sí** (una por prestador, historial de estados) | **CASCADE** (borra suscripción) |
| `pagos` | `referencia_pasarela`, `motivo_fallo` | No | **Sí** (historial de cobros) | **CASCADE** (borra pagos de la suscripción) |
| `intentos_de_pago` | `clave_idempotencia`, `url_para_pagar`, `referencia_pasarela`, `motivo_fallo`, `codigo_cupon` | No | **Sí** (cada intento de cobro) | **CASCADE** (borra intentos de la suscripción) |
| `cupos_fundador` | — (solo número y FK) | No | Fijo: **50 filas** | **SET NULL** (cupo vuelve a libre) |
| `cupones` | `descripcion` | No | **Sí** (catálogo de códigos) | No afecta |
| `canjes_de_cupon` | `codigo_cupon` | No | **Sí** (uno por perfil que usa cupón) | **CASCADE** (borra canje del perfil) |

**Hallazgo:** `intentos_de_pago` **crece sin límite** (cada reintento de pago fallido genera fila). No hay purga de intentos fallidos antiguos. `url_para_pagar` puede contener tokens de sesión de la pasarela. Plazo sugerido: **2 años post-resolución** (pagado/fallido) para auditoria financiera (Ley 21.719 + normativa bancaria).

---

### 10. `notificaciones` (TBL-27) — Avisos por correo/push
| Columna | PII | Se borra hoy | Crece sin límite |
|---------|-----|--------------|------------------|
| `destinatario_id` | FK | No | Sí |
| `clave_idempotencia` | **SÍ** (evita duplicados) | No | Sí |
| `asunto`, `cuerpo` | **SÍ** (cuerpo puede tener token en claro) | **Sí: `Purgada`** tras envío exitoso (`Enviada`) o fallo definitivo (`Fallida`/`Anulada` tras `_o.PurgaDeFallidasHoras` configurado) | **Parcial**: cuerpo se purga, fila permanece |
| `ultimo_error` | **SÍ** (detalle de error del proveedor) | No | Sí (en fallidas) |

**Mecanismo de purga (código verificado en `ProcesadorDeCorreo.cs`):**
- Envío exitoso: `aviso.Cuerpo = CargaDeAviso.Purgada` (`{"purgado":true}`) inmediatamente
- Fallo/Anulado: job `PurgarAsync()` corre cada ejecución del planificador, purga cuerpos de avisos `Fallida`/`Anulada` con `programada_para > _o.PurgaDeFallidasHoras` (por defecto 24h)
- **La fila NUNCA se borra**, solo el cuerpo sensible

**Hallazgo:** La tabla **crece sin límite** (una fila por aviso encolado). Solo el `cuerpo` se purga. Plazo sugerido: **13 meses** para metadatos de envío (trazabilidad de comunicaciones), cuerpo ya purgado.

---

### 11. `registro_auditoria` (TBL-28) — Trazabilidad de acciones sensibles
| Columna | PII | Se borra hoy | Crece sin límite |
|---------|-----|--------------|------------------|
| `actor_id` | FK (usuario que actúa) | **No** | **Sí** (solo INSERT) |
| `accion`, `entidad`, `entidad_id` | Técnico | No | Sí |
| `contexto` | **SÍ** (JSON con datos de la acción) | No | Sí |
| `ip_origen` | **SÍ** | No | Sí |
| `ocurrido_en` | Metadato | No | Sí |

**Hallazgo crítico:** **NUNCA se borra** (comentario en entidad: "Solo inserción y lectura"). **Crece sin límite indefinido**. No hay job de purga ni particionado. Plazo sugerido Chile: **2 años** para auditoría de seguridad (Ley 21.719 art. 11 + ISO 27001), salvo obligaciones sectoriales que exijan más.

---

### 12. `trabajos_de_fondo` (TBL-32) — Control del planificador
| Columna | PII | Se borra hoy | Crece sin límite |
|---------|-----|--------------|------------------|
| `nombre` | Constante (`suscripciones-diario`, `citas-periodico`) | No | No (2 filas fijas) |
| `ultimo_resumen` | Texto breve ("venció 3, avisó 12") | No | No (se sobrescribe) |

**Hallazgo:** No crece (solo 2 filas), no hay PII real.

---

### 13. Tablas de catálogo (sin PII directo, solo FK a personas)
| Tabla | PII | Crece sin límite | Notas |
|-------|-----|------------------|-------|
| `rubros`, `servicios`, `zonas` | Solo `nombre` (catálogo público) | No (fijas, sembradas) | No son datos de personas |
| `prestador_servicios` | `precio_referencia` (precio del prestador) | **Sí** (uno por servicio ofertado) | CASCADE desde perfil_prestador |
| `coberturas_prestador` | — | **Sí** (zonas por prestador) | CASCADE desde perfil_prestador |
| `entidades_certificadora` | `nombre`, `url_registro_publico` | No (catálogo) | No son datos de usuarios |
| `planes`, `cupones` | `nombre`, `descripcion` (catálogo) | No (fijas) | No son datos de personas |

---

## Qué se borra hoy y cuándo (resumen de procesos automáticos)

| Proceso | Tabla(s) afectadas | Qué borra | Cuándo |
|---------|-------------------|-----------|--------|
| `Sesion.EstaVigente()` + `RevocarTodasAsync()` | `sesiones` | Revoca (no borra) tokens expirados | Al validar sesión / al eliminar cuenta |
| `Usuario.Anonimizar()` | `usuarios` | Purgar `token_verificacion`, `token_recuperacion`, `hash_contrasena` | Al eliminar cuenta (CS-04) |
| `Notificacion.PurgarAsync()` | `notificaciones` | Reemplaza `cuerpo` por `{"purgado":true}` | Cada corrida del planificador (diario), para avisos `Fallida`/`Anulada` > 24h |
| `Notificacion.ProcesarUnoAsync()` (envío OK) | `notificaciones` | Reemplaza `cuerpo` por `{"purgado":true}` | Inmediato tras envío exitoso |
| `BloqueHorario.Vencer()` + `QuitarBloquesLibresDeAsync()` | `bloques_horario` | Borra bloques en estado `Libre` vencidos | Job diario del planificador (`citas-periodico`) |
| `CupoFundador` reserva temporal | `cupos_fundador` | Limpia `perfil_prestador_id` y `reservado_hasta` | Job diario (`suscripciones-diario`) si `reservado_hasta` vencido sin pago |

**No existe ningún job que borre:**
- Sesiones expiradas de cuentas activas
- Intentos de pago fallidos antiguos
- Citas y cambios de cita antiguos
- Credenciales y documentos vencidos
- Registros de auditoría
- Escaneos de tarjeta
- Notificaciones (solo purga cuerpo, no fila)

---

## Qué pasa al eliminar la cuenta (CS-04, PR #76)

Código verificado en `ServicioCuenta.cs:266-288` y `Usuario.Anonimizar()` (`Entidades.cs:46-56`):

1. **Perfil prestador**: `Despublicar()` (ya no sale en búsquedas) → **CASCADE borra perfil** (y en cascada: disponibilidades, coberturas, prestador_servicios, bloques, bloqueos, citas del prestador, suscripciones, pagos, intentos, canjes, tarjetas)
2. **Usuario**: `Anonimizar()`:
   - `correo` → `eliminado-{id}@zass.local`
   - `nombre` → `"Usuario eliminado"`
   - `hash_contrasena` → `""`
   - `token_verificacion` / `token_recuperacion` → `null`
   - `estado` → `Eliminada`
   - `anonimizado_en` → `ahora`
3. **Sesiones**: `RevocarTodasAsync()` → **CASCADE borra todas las sesiones**
4. **Auditoría**: Registra `"eliminar_cuenta"` en `registro_auditoria` (actor = usuario eliminado)
5. **Perfil buscador**: **CASCADE borra** (favoritos, contactos, citas como buscador)
6. **Reseñas**: **NO se borran** (RN-S1-07). El `autor_id` sigue apuntando al usuario anonimizado. La reseña se muestra firmada como "Usuario eliminado".

**Tablas que QUEDAN con datos tras eliminar cuenta:**
- `resenas` (con `autor_id` a usuario anonimizado)
- `respuestas_resena` (si el prestador respondió)
- `reportes_resena` (si alguien reportó la reseña)
- `registro_auditoria` (traza de la eliminación y acciones previas)
- `escaneos_tarjeta` (sin identidad, por diseño)
- `tarjetas` (quedan con `perfil_prestador_id = NULL` por `SET NULL`)

---

## Plazos sugeridos en Chile (referencia, no consejo legal)

| Categoría de datos | Ley 19.628 / 21.719 | Práctica de mercado | Plazo sugerido para ZASS! |
|-------------------|---------------------|---------------------|---------------------------|
| Datos de identificación (correo, nombre, teléfono) | Art. 11: supresión cuando no sean necesarios | 5 años post-relación | **5 años desde última interacción** (o anonimización inmediata si pide supresión) |
| Datos de autenticación (hash, tokens, sesiones) | Datos sensibles (art. 10) | Vida de la sesión + 30 días | **30 días post-expiración** (sesiones), **inmediato** al anonimizar (tokens) |
| Datos de geolocalización (lat/lng, dirección cifrada) | Dato sensible (art. 10) | 2-5 años | **2 años post-relación** |
| Historial de citas y agenda | Registro de actividad | 2 años | **2 años post-cita** |
| Reseñas y reputación | Interés legítimo del prestador | Indefinido mientras exista el prestador | **Indefinido** (anonimizado el autor) |
| Documentos de credenciales | Dato profesional | 5 años post-vencimiento | **5 años post-vencimiento credencial** |
| Intentos de pago y transacciones | Normativa financiera / tributaria | 5-10 años | **5 años post-resolución** (pagado/fallido) |
| Auditoría de seguridad (IP, acciones) | ISO 27001 / Ley 21.719 | 1-2 años | **2 años** |
| Escaneos de tarjeta NFC (anonimizados) | Analítica sin PII | 13 meses | **13 meses** |
| Notificaciones (metadatos de envío) | Trazabilidad comunicaciones | 13 meses | **13 meses** (cuerpo ya purgado) |

---

## Tablas que crecen sin límite (riesgo de almacenamiento)

| Tabla | Crecimiento estimado | Riesgo | Mitigación propuesta |
|-------|---------------------|--------|---------------------|
| `sesiones` | 1 fila/login/día por usuario activo | Medio | Job diario: borrar `expira_en < ahora - 30 días` |
| `intentos_de_pago` | 1-5 filas/cobro fallido por prestador/mes | Medio | Job mensual: borrar `resuelto_en < ahora - 2 años` WHERE `estado IN ('Fallido','Expirado')` |
| `citas` + `cambios_cita` | 1-10 filas/cita por prestador/mes | Alto (historial inmutable) | Particionar por año; archivar > 2 años a tabla fría |
| `resenas` + `respuestas_resena` | 1-5 filas/prestador/mes | Bajo (texto corto) | No borrar (parte del valor del producto) |
| `escaneos_tarjeta` | 10-100 filas/tarjeta/mes | Medio | Job mensual: borrar `ocurrido_en < ahora - 13 meses` |
| `notificaciones` | 10-50 filas/usuario/mes | Medio | Particionar por mes; borrar `programada_para < ahora - 13 meses` (cuerpo ya purgado) |
| `registro_auditoria` | 100-1000 filas/día en producción | **Alto** (solo INSERT, nunca borra) | **URGENTE**: Particionar por mes + job de borrado > 2 años |

---

## Recomendaciones para Pablo (antes del lanzamiento)

1. **Implementar jobs de purga** para las 7 tablas que crecen sin límite y no tienen purga hoy (ver tabla arriba). Prioridad: `registro_auditoria` > `sesiones` > `intentos_de_pago` > `notificaciones` > `escaneos_tarjeta` > `citas/cambios_cita`.
2. **Definir `PLAZO_REGISTROS_DE_SEGURIDAD` en `legal.ts:21`** con una tabla por categoría (no un solo número), basada en la columna "Plazo sugerido para ZASS!" de arriba.
3. **Decidir sobre documentos de credenciales**: ¿se borran del almacenamiento (R2/S3) al purgar la fila en `documentos_credencial`? Hoy no ocurre.
4. **Evaluar particionado** en `registro_auditoria` y `notificaciones` antes de llegar a 1M filas.
5. **Documentar en política de privacidad** que las reseñas se anonimizan (no borran) al eliminar cuenta, y que la auditoría de seguridad se guarda 2 años.

---

## Sin verificar contra infraestructura (sin acceso a secretos)

- Tamaño real de `registro_auditoria` en Neon (producción)
- Volumen de `escaneos_tarjeta` con tarjetas NFC reales en producción
- Coste de almacenamiento de `notificaciones` y `cambios_cita` a 12 meses
- Si el almacenamiento de documentos (R2/S3) tiene lifecycle rules configuradas

---

## Encontrado fuera del plan

1. **`registro_auditoria` no tiene índice en `ocurrido_en`** — necesario para particionado/borrado eficiente por fecha.
2. **`notificaciones` no tiene índice en `programada_para`** — el job `PurgarAsync()` filtra por esta columna sin índice (hoy tabla vacía, en producción será Seq Scan).
3. **`escaneos_tarjeta` no tiene índice en `ocurrido_en`** — necesario para job de purga por antigüedad.
4. **`intentos_de_pago` no tiene índice en `resuelto_en`** — necesario para purga de fallidos antiguos.
5. **`sesiones` no tiene índice compuesto `(usuario_id, expira_en)`** — para borrado masivo de expiradas por usuario.

Estos hallazgos son de **rendimiento/operación**, no de retención, pero bloquean la implementación eficiente de las purgas recomendadas.