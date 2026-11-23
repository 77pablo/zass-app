# Auditoría de Retención de Datos — ZASS!

**Fecha:** 18 de noviembre de 2026  
**Tarea:** T34 de la Tanda 8  
**Alcance:** Todas las tablas de `ZassDbContext` que guardan datos de personas  
**Base consultada:** `zass_pruebas` (esquema idéntico a desarrollo `zass`) — solo lectura via `information_schema` y `pg_stat_user_tables`  
**Referencia legal:** Ley 19.628 (Protección de la Vida Privada) y Ley 21.719 (modifica la 19.628) — *marcado como referencia, no como consejo legal*

> **Corrección (23-nov-2026).** En las columnas «Al eliminar cuenta» de este documento, todo lo que dice **CASCADE** estaba mal: ese CASCADE solo se dispara si se borra la fila de `usuarios`, y eliminar la cuenta la **anonimiza** y la conserva (RN-S1-07). Lo que de verdad pasa está en la sección «Qué pasa al eliminar la cuenta», más abajo. Las tablas de perfil, credenciales y documentos ya están corregidas; las de citas, reseñas, tarjetas y suscripciones siguen con el texto original y hay que leerlas con esta salvedad: sus filas **se conservan**.

---

## Resumen ejecutivo

| Categoría | Tablas | Hallazgo principal |
|-----------|--------|-------------------|
| **Identidad y acceso** | `usuarios`, `sesiones`, `usuario_roles`, `perfiles_prestador`, `perfiles_buscador` | `usuarios` se **anonimiza** (no borra) al eliminar cuenta (CS-04); `sesiones` se revocan (no se borran: la fila de `usuarios` sigue); tokens de verificación/recuperación expiran a 24h/1h y se purgan en `Anonimizar()` |
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
| `id` | uuid | FK | No | **Sí** (una por login) | Se revocan todas (`RevocarTodasAsync`); las filas se conservan |
| `usuario_id` | uuid | FK | No | No | → borra fila |
| `hash_token` | text | Técnico | No | No | → borra fila |
| `ip_origen` | text | **SÍ** | No | **Sí** | → borra fila |
| `agente_usuario` | text | **SÍ** | No | **Sí** | → borra fila |
| `creada_en` | timestamptz | Metadato | No | No | → borra fila |
| `expira_en` | timestamptz | Metadato | **Sí: expira** (token de 30 días) | No | → borra fila |
| `revocada_en` | timestamptz | Metadato | No | No | → borra fila |

**Hallazgo:** Las sesiones **crecen sin límite** (una fila por login). Al eliminar cuenta se revocan todas, pero las filas se conservan. Pero **no hay job que limpie sesiones expiradas/revocadas de cuentas activas**. Plazo sugerido: **30 días post-expiración** (coincide con vida del token).

---

### 3. `perfiles_prestador` (TBL-05) — Perfil público del prestador
| Columna | Tipo | PII | Se borra hoy | Crece sin límite | Al eliminar cuenta |
|---------|------|-----|--------------|------------------|-------------------|
| `id` | uuid | FK | No | No | Se conserva (FK en resenas, etc.) |
| `usuario_id` | uuid | FK | No | No | Se conserva (la fila del usuario se anonimiza, no se borra, así que el CASCADE no se dispara) |
| `rubro_id` | smallint | Catálogo | No | No | Se conserva (no es dato personal) |
| `slug` | varchar(120) | **SÍ** | No | No | → `eliminado-{id}` (se armaba con el nombre) |
| `nombre_publico` | varchar(150) | **SÍ** | No | No | → `"Perfil eliminado"` |
| `descripcion` | varchar(1500) | **SÍ** | No | No | → `null` |
| `url_foto` | text | **SÍ** | No | No | → `null`, y se borran los dos archivos (grande y chica) |
| `direccion_cifrada` | bytea | **SÍ** (cifrada) | No | No | → `null` |
| `latitud` / `longitud` | double | **SÍ** | No | No | → `null` |
| `telefono_publico` | text | **SÍ** | No | No | → `null` |
| `correo_publico` | text | **SÍ** | No | No | → `null` |
| `whatsapp` / `sitio_web` | text | **SÍ** | No | No | → `null` |
| `promedio_calificacion` / `total_resenas` | Derivados | No | No | No | Se conservan (no son datos personales) |
| `publicado_en` / `actualizado_en` | Metadato | No | No | No | Se conservan |

**Corrección (23-nov-2026):** este documento decía que al eliminar la cuenta el perfil se borraba en cascada. Era falso: el CASCADE de `usuario_id` solo se dispara si se borra la fila de `usuarios`, y la baja **no la borra**, la anonimiza (`Usuario.Anonimizar()`, RN-S1-07). Hasta esta corrección la ficha, sus fotos y sus documentos se quedaban enteros en la base y en el almacenamiento, y las fotos se seguían sirviendo por API-73. Hoy la baja hace esto con el perfil (`PerfilPrestador.BorrarDatosPersonales()` y `ServicioBorradoDeDatos`): lo **despublica** (la ficha pública responde 404, como cualquier perfil despublicado) y deja la **fila** (la referencian citas, reseñas, suscripciones y auditoría) **sin ningún dato personal**: slug, nombre público, descripción, foto, dirección, coordenadas, teléfono, correo, WhatsApp, sitio web y zonas de cobertura (la comuna). Se conservan el rubro, los servicios del catálogo y los promedios de reseñas, que no son datos personales.

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
| `credenciales` | `nombre` (nombre del título/certificado), `entidad_certificadora_id` | No | **Sí** (una por credencial declarada) | **Se conservan, anonimizadas**: `nombre` → `"Credencial eliminada"`, entidad → `null`. Quedan el estado y las fechas, que sirven a la auditoría de la verificación |
| `documentos_credencial` | `clave_almacenamiento`, `nombre_original`, `tipo_mime` | No | **Sí** (archivos subidos) | **Se borran las filas y los archivos** (después de que la base confirma la baja) |
| `verificaciones` | `motivo`, `detalle` | No | **Sí** (historial de revisiones) | **Se conservan**; el texto libre `detalle` del verificador se deja en `null`. Quedan el resultado y el motivo (auditoría) |

**Hallazgo crítico:** **No hay purga automática** de documentos antiguos ni credenciales vencidas. Los archivos en almacenamiento (R2/S3) **no se borraban** al eliminar la fila (la `clave_almacenamiento` es solo referencia); desde la corrección del 23-nov-2026 la **baja de la cuenta sí los borra**, pero sigue sin haber purga para quien no se da de baja. Plazo sugerido Chile: **5 años desde vencimiento de la credencial** (Ley 21.719, datos profesionales).

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

## Qué pasa al eliminar la cuenta (CS-04, API-17)

Código: `BajaDeCuenta` (API), `ServicioCuenta.EliminarAsync`, `ServicioBorradoDeDatos` y `Usuario.Anonimizar()`. Corregido el 23-nov-2026 (Ley 21.719): la versión anterior de esta sección decía que un CASCADE borraba el perfil y todo lo que cuelga de él, y eso era falso.

**Antes de tocar nada**
- Se pide la contraseña y, si la cuenta tiene la verificación en dos pasos, también un código (TOTP o de respaldo). Un fallo suma al mismo bloqueo de intentos que el inicio de sesión (10 intentos / 15 minutos) y la respuesta no dice cuál de los dos falló.
- Se **despublica la ficha primero**, para que nadie agende mientras se cancelan las citas. Después se cancelan las citas futuras (cada una en su transacción, avisando a la otra parte).

**En una sola transacción (`EliminarAsync`)**
1. **Suscripción**: se cancela, se olvida el medio de pago inscrito y se libera el cupo fundador.
2. **Perfil prestador** (si existe): la fila **NO se borra** (la referencian citas, reseñas y auditoría). Queda despublicada y sin datos personales (ver la tabla de `perfiles_prestador`); la ficha pública responde 404.
3. **Foto de perfil**: se quita la referencia y se piden borrar los dos archivos.
4. **Fotos de trabajos**: se borran las filas y se piden borrar los archivos.
5. **Documentos de credenciales**: se borran las filas y se piden borrar los archivos. Las credenciales quedan anonimizadas (ver su tabla).
6. **Navegadores con push** (`suscripciones_push`): se borran todos.
7. **Usuario**: `Anonimizar()`:
   - `correo` → `eliminado-{id}@zass.local`
   - `nombre` → `"Usuario eliminado"`; apellido, RUT, teléfono y razón social → `null`
   - `hash_contrasena` → `""`
   - `token_verificacion` / `token_recuperacion` → `null`; MFA borrado
   - `estado` → `Eliminada`
   - `anonimizado_en` → `ahora`
8. **Sesiones**: `RevocarTodasAsync()` las revoca todas (la fila de `usuarios` sigue, así que no hay CASCADE).
9. **Auditoría**: `"eliminar_cuenta"` y `"baja_borrar_datos_perfil"` (con solo los conteos, sin contenido).

**Después de que la base confirma**: se borran los archivos del almacenamiento (foto grande y chica, fotos de trabajos y documentos). Si borrar uno falla, se registra un error en el log con la clave del archivo y la baja queda hecha: lo peor es un archivo huérfano, nunca una fila que apunte a un archivo que no está.

**Lo que NO se hace hoy (pendiente)**
- `perfiles_buscador` (zona de interés, favoritos, rubros) y los `contactos` del buscador **no** se borran: tampoco los borra ningún CASCADE, porque la fila de `usuarios` no se elimina. Habría que decidir qué se anonimiza ahí.
- Las citas pasadas y su historial (`cambios_cita`) se conservan como están (y las notas libres del buscador en `citas.nota_buscador`).
- El cliente de pago sigue existiendo en la pasarela (Flow); solo se olvida la referencia local.
- Los archivos huérfanos por una caída entre el guardado y el borrado no tienen barrido.

**Reseñas**: **NO se borran** (RN-S1-07). El `autor_id` sigue apuntando al usuario anonimizado y la reseña se muestra firmada como "Usuario eliminado".

**Tablas que QUEDAN con datos tras eliminar cuenta:**
- `perfiles_prestador` (fila anonimizada y despublicada)
- `credenciales` y `verificaciones` (anonimizadas, sin documentos)
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
3. **Decidir sobre documentos de credenciales de quien NO se da de baja**: al eliminar la cuenta los archivos ya se borran del almacenamiento (R2/S3) junto con la fila; falta decidir la purga de los documentos antiguos o vencidos de cuentas activas (y un barrido de archivos huérfanos).
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