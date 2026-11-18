# Auditoría del registro de seguridad (T25)

> PLAN-IA-2, T25. Compueba los 11 eventos de `seguridad.md` §6 contra lo que el código
> registra realmente. Rama `docs/auditoria-registro`. Sin modificaciones al código.

---

## Resumen ejecutivo

De los 11 eventos que `seguridad.md` §6 exige registrar:

- **6 se registran** (eventos 1-2, 6, 9-10), todos a TBL-28.
- **1 se registra pero con destino incorrecto** (evento 11, va a TBL-28 cuando debería ir a N-11).
- **4 NO se registran** (eventos 3-4, 7-8) porque el código correspondiente no existe o no tiene logging.
- **1 posible violación** de «qué no se registra» (evento 4 del historial `resena.editada` incluye texto completo de reseña anterior).

### Hallazgo serio

**`resena.editada` guarda el texto completo del comentario anterior** (`comentarioAnterior`) en
la auditoría. La regla 6 dice «Texto completo de reseñas: quedaría duplicado fuera del control
de supresión». La llamada está en `ServicioResenas.cs:324`. Es una decisión de diseño intencional
(comentario «4D.2: la versión anterior se guarda en la auditoría antes de pisarla») pero
contradice la prohibición explícita.

---

## Tabla de los 11 eventos

| # | Evento | ¿Se registra? | Dónde (archivo:línea) | Campos que faltan | ¿Va a donde dice? | ¿Hay prueba? |
|---|--------|:---:|-----------------------|:---:|:---:|:---:|
| 1 | Inicio de sesión fallido | **Parcial** | `ServicioSesion.cs:78` | — | TBL-28 ✓ · **N-11 ✗** (no hay ILogger call) | Unit: `SesionPruebas.cs:143` (acción y campos). Sin integración que mire TBL-28 directamente. |
| 2 | Inicio de sesión exitoso | ✓ | `ServicioSesion.cs:93` | — | TBL-28 ✓ | No hay prueba específica. |
| 3 | Límite de tasa excedido | **✗** | — | Todo | **N-11 ✗** (ni TBL-28 ni N-11). `OnRejected` en `Seguridad.cs:64` escribe JSON a la respuesta HTTP pero no tiene call de logging. **Sin registrar: no hay ILogger en la API.** | — |
| 4 | Validación rechazada | **✗** | — | Todo | **N-11 ✗** (ni TBL-28 ni N-11). `FiltroDeValidacion<T>` en `Respuestas.cs:68` devuelve 400 pero no tiene call de logging. **Sin registrar: no hay ILogger en la API.** | — |
| 5 | Acceso a documento de credencial | **✗** | — | Todo | Ni TBL-28 ni N-11. El código de acceso a documentos (URL firmada, descarga) **no existe todavía**. | — |
| 6 | Decisión de verificación | ✓ | `ServicioVerificacion.cs:180` | — | TBL-28 ✓ | Integración: `VerificacionIntegracionPruebas.cs:406` (con documentos) y `:470` (sin documentos). |
| 7 | Activación de tarjeta | **✗** | — | Todo | Ni TBL-28 ni N-11. API-55 **no implementada** (comentario en `ServicioTarjetas.cs:10` y `IServicioTarjetas.cs`). Carril de Joaquín (tarjetas). | — |
| 8 | Intento fallido de activación | **✗** | — | Todo | Ni TBL-28 ni N-11. API-55 **no implementada**. Carril de Joaquín (tarjetas). | — |
| 9 | Reseña retirada | ✓ | `ServicioResenas.cs:558` | — | TBL-28 ✓ | Integración: `ResenasIntegracionPruebas.cs:757` (mira auditoría de reseña retirada). |
| 10 | Eliminación de cuenta | ✓ | `ServicioCuenta.cs:282` | — | TBL-28 ✓ | Unit: `CuentaPruebas.cs:414`. Sin integración que mire TBL-28 directamente. |
| 11 | Intento de reseñar sin elegibilidad | **Destino incorrecto** | `ServicioResenas.cs:275` | — | **TBL-28 ✓** · **N-11 ✗** (documento dice N-11, código va a TBL-28) | Unit en `ServicioResenas` cubre el flujo de rechazo. |

---

## Lista completa de llamadas `.Registrar()` encontradas (53)

### Identidad (13)

| Archivo:línea | Acción | Campos |
|---|---|---|
| `ServicioRegistro.cs:118` | `registro_cuenta` | entidad: usuario, contexto: `{roles, correoVerificado}` |
| `ServicioRegistro.cs:161` | `registro_cuenta` (reintento) | entidad: usuario, contexto: `{motivo}` |
| `ServicioCuenta.cs:93` | `verificar_correo` | entidad: usuario, actorId |
| `ServicioCuenta.cs:130` | `pedir_recuperacion` | entidad: usuario, actorId, ip |
| `ServicioCuenta.cs:196` | `reenviar_verificacion` | entidad: usuario, actorId, ip |
| `ServicioCuenta.cs:224` | `restablecer_contrasena` | entidad: usuario, actorId, ip |
| `ServicioCuenta.cs:251` | `cambiar_contrasena` | entidad: usuario, actorId |
| `ServicioCuenta.cs:282` | `eliminar_cuenta` | entidad: usuario, actorId, ip |
| `ServicioSesion.cs:78` | `inicio_sesion_fallido` | entidad: usuario?, actorId?, contexto: `{correoHash, motivo}`, ip |
| `ServicioSesion.cs:93` | `inicio_sesion` | entidad: usuario, actorId, ip |
| `ServicioSesion.cs:146` | `revocar_sesiones` | entidad: usuario, actorId, contexto: `{revocadas}` |
| `ServicioPerfiles.cs:211` | `publicar_perfil` | entidad: perfil_prestador, actorId, contexto: `{slug}` |
| `ServicioPerfiles.cs:228` | `despublicar_perfil` | entidad: perfil_prestador, actorId |

### Credenciales (6)

| Archivo:línea | Acción | Campos |
|---|---|---|
| `ServicioCredenciales.cs:123` | `crear_credencial` | entidad: credencial, contexto: `{tipo, entidadNueva}` |
| `ServicioCredenciales.cs:146` | `eliminar_credencial` | entidad: credencial, contexto: `{teniaSello}` |
| `ServicioCredenciales.cs:169` | `enviar_credencial_a_revision` / `responder_antecedentes` | entidad: credencial, contexto: `{sinDocumentos}` |
| `ServicioVerificacion.cs:75` | `tomar_credencial` | entidad: credencial, actorId, contexto: `{hasta, sinDocumentos}` |
| `ServicioVerificacion.cs:180` | `verificar_credencial` / `verificar_sin_documentos` / `rechazar_credencial` / `pedir_antecedentes` | entidad: credencial, actorId, contexto: `{verificacionId, resultado, motivo, detalle, sinDocumentos}` |
| `ServicioVerificacion.cs:209` | `intento_revisar_propia` | entidad: credencial, actorId, contexto: `{intento}` |

### Agenda (6)

| Archivo:línea | Acción | Campos |
|---|---|---|
| `ServicioAgenda.cs:172` | `definir_disponibilidad` | entidad: disponibilidad, contexto: `{franjas, bloques}` |
| `ServicioAgenda.cs:235` | `bloquear_agenda` | entidad: bloqueo_agenda, contexto: `{desdeEn, hastaEn}` |
| `ServicioAgenda.cs:261` | `desbloquear_agenda` | entidad: bloqueo_agenda, contexto: `{desdeEn, hastaEn}` |
| `ServicioAgenda.cs:440` | `agendar_cita` / `solicitar_cita` | entidad: cita, actorId, contexto: `{codigo, bloque, modo}` |
| `ServicioAgenda.cs:715` | `reprogramar_cita` | entidad: cita, actorId, contexto: `{hacia, codigoNuevo, tardio}` |
| `ServicioAgenda.cs:856` | `cita_confirmacion` / `cita_rechazo` / `cita_cancelacion` / `cita_reprogramacion` / `cita_asistencia` | entidad: cita, actorId, contexto: `{estado, tardio}` |

### Reseñas (10)

| Archivo:línea | Acción | Campos |
|---|---|---|
| `ServicioResenas.cs:254` | `resena.publicada` | entidad: resena, actorId, contexto: `{perfilPrestadorId, calificacion, datosPersonales}` |
| `ServicioResenas.cs:261` | `resena.revision_preventiva` (publicación) | entidad: resena, actorId, contexto: `{motivo, fragmentos}` |
| `ServicioResenas.cs:275` | `resena.autoresena_bloqueada` / `resena.intento_rechazado` | entidad: contacto, actorId, contexto: `{motivo}` |
| `ServicioResenas.cs:324` | `resena.editada` | entidad: resena, actorId, contexto: `{calificacionAnterior, **comentarioAnterior**, aspectosAnteriores, calificacionNueva, datosPersonales}` |
| `ServicioResenas.cs:335` | `resena.revision_preventiva` (edición) | entidad: resena, actorId, contexto: `{motivo, fragmentos}` |
| `ServicioResenas.cs:397` | `resena.respondida` | entidad: resena, actorId |
| `ServicioResenas.cs:405` | `resena.respuesta_editada` | entidad: resena, actorId |
| `ServicioResenas.cs:470` | `resena.reportada` | entidad: resena, actorId, contexto: `{motivo, ocultada}` |
| `ServicioResenas.cs:558` | `resena.retirada` / `resena.mantenida` | entidad: resena, actorId, contexto: `{reporteId, reportesCerrados, motivo}` |
| `ServicioResenas.cs:569` | `reporte.resolucion_rechazada` | entidad: reporte_resena, actorId, contexto: `{motivo}` |

### Catálogo (1)

| Archivo:línea | Acción | Campos |
|---|---|---|
| `ServicioCatalogo.cs:329` | `activar_servicio` / `desactivar_servicio` | entidad: prestador_servicio, actorId, contexto |

### Geografía (1)

| Archivo:línea | Acción | Campos |
|---|---|---|
| `ServicioGeografia.cs:241` | `registrar_direccion` | entidad: direccion, actorId |

### Suscripciones (16)

| Archivo:línea | Acción | Campos |
|---|---|---|
| `ServicioSuscripciones.cs:168` | `iniciar_prueba` | entidad: suscripcion, actorId, contexto: `{plan}` |
| `ServicioSuscripciones.cs:210` | `activar_suscripcion` | entidad: suscripcion, actorId, contexto |
| `ServicioSuscripciones.cs:229` | `cancelar_suscripcion` | entidad: suscripcion, actorId, contexto |
| `ServicioSuscripciones.cs:248` | `suspender_suscripcion` | entidad: suscripcion, actorId, contexto |
| `ServicioSuscripciones.cs:383` | `registrar_periodo_pagado` | entidad: suscripcion, actorId, contexto |
| `ServicioSuscripciones.cs:399` | `rechazar_cobro` | entidad: suscripcion, actorId, contexto |
| `ServicioSuscripciones.cs:412` | `renovar_suscripcion` | entidad: suscripcion, actorId, contexto |
| `ServicioSuscripciones.cs:452` | `expirar_suscripcion` | entidad: suscripcion, contexto |
| `ServicioCobros.cs:232` | `cobro_iniciado` | entidad: intentos_de_pago, contexto: `{plan, monto, descuento, pasarela}` |
| `ServicioCobros.cs:379` | `inscripcion_fundador_iniciada` | entidad: intentos_de_pago, contexto |
| `ServicioCobros.cs:524` | `cobro_sin_medio_inscrito` | entidad: suscripciones, contexto |
| `ServicioCobros.cs:570` | `cobro_automatico_iniciado` | entidad: intentos_de_pago, contexto |
| `ServicioCobros.cs:697` | `cobro_rechazado` | entidad: intentos_de_pago, contexto: `{motivo}` |
| `ServicioCobros.cs:715` | `cobro_monto_no_coincide` | entidad: intentos_de_pago, contexto: `{esperado, confirmado}` |
| `ServicioCobros.cs:739` | `inscripcion_fundador_confirmada` | entidad: intentos_de_pago, contexto: `{vigenteHasta}` |
| `ServicioCobros.cs:776` | `cobro_confirmado` | entidad: intentos_de_pago, contexto: `{monto, cupon}` |

---

## Acciones registradas que NO están entre los 11 eventos de seguridad.md

Son 44 acciones que el código registra y que no aparecen en la lista de los 11 de `seguridad.md` §6.
Esto no es un error: la tabla del documento es un mínimo, no un exhaustivo.

| Acción | Módulo | Breve descripción |
|---|---|---|
| `registro_cuenta` | Identidad | Alta de cuenta nueva |
| `verificar_correo` | Identidad | Verificación del correo del usuario |
| `pedir_recuperacion` | Identidad | Solicitud de recuperación de contraseña |
| `reenviar_verificacion` | Identidad | Reenvío del enlace de verificación |
| `restablecer_contrasena` | Identidad | Cambio de contraseña vía recuperación |
| `cambiar_contrasena` | Identidad | Cambio de contraseña desde sesión activa |
| `revocar_sesiones` | Identidad | Revocación masiva de sesiones |
| `publicar_perfil` | Identidad | Perfil de prestador publicado |
| `despublicar_perfil` | Identidad | Perfil de prestador despublicado |
| `crear_credencial` | Credenciales | Creación de credencial en borrador |
| `eliminar_credencial` | Credenciales | Eliminación de credencial |
| `enviar_credencial_a_revision` | Credenciales | Envío a revisión |
| `responder_antecedentes` | Credenciales | Respuesta a solicitud de antecedentes |
| `tomar_credencial` | Verificación | Verificador toma una credencial de la cola |
| `verificar_credencial` | Verificación | Credencial verificada (con documentos) |
| `verificar_sin_documentos` | Verificación | Credencial verificada sin documentos |
| `rechazar_credencial` | Verificación | Credencial rechazada |
| `pedir_antecedentes` | Verificación | Antecedentes solicitados al prestador |
| `intento_revisar_propia` | Verificación | Intento de revisar credencial propia (bloqueado) |
| `definir_disponibilidad` | Agenda | Publicación de franjas horarias |
| `bloquear_agenda` | Agenda | Bloqueo de fechas |
| `desbloquear_agenda` | Agenda | Remoción de bloqueo |
| `agendar_cita` | Agenda | Creación automática de cita |
| `solicitar_cita` | Agenda | Solicitud manual de cita |
| `reprogramar_cita` | Agenda | Reprogramación de cita |
| `cita_confirmacion` | Agenda | Confirmación de cita |
| `cita_rechazo` | Agenda | Rechazo de cita |
| `cita_cancelacion` | Agenda | Cancelación de cita |
| `cita_reprogramacion` | Agenda | Cambio de cita (vía CerrarCambio) |
| `cita_asistencia` | Agenda | Registro de asistencia / inasistencia |
| `resena.publicada` | Reseñas | Publicación de reseña |
| `resena.revision_preventiva` | Reseñas | Marca de revisión por datos personales |
| `resena.autoresena_bloqueada` | Reseñas | Bloqueo de autorreseña |
| `resena.intento_rechazado` | Reseñas | Rechazo por elegibilidad (evento 11) |
| `resena.editada` | Reseñas | Edición de reseña |
| `resena.respondida` | Reseñas | Respuesta del prestador |
| `resena.respuesta_editada` | Reseñas | Edición de respuesta |
| `resena.reportada` | Reseñas | Reporte de reseña |
| `resena.retirada` | Reseñas | Retirada por moderación |
| `resena.mantenida` | Reseñas | Mantenida por moderación |
| `reporte.resolucion_rechazada` | Reseñas | Moderador rechazado por ser parte |
| `activar_servicio` | Catálogo | Activación/desactivación de servicio |
| `desactivar_servicio` | Catálogo | (misma línea, acción distinta) |
| `registrar_direccion` | Geografía | Registro de dirección |
| `iniciar_prueba` | Suscripciones | Inicio período de prueba |
| `activar_suscripcion` | Suscripciones | Activación |
| `cancelar_suscripcion` | Suscripciones | Cancelación |
| `suspender_suscripcion` | Suscripciones | Suspensión |
| `registrar_periodo_pagado` | Suscripciones | Período pagado |
| `rechazar_cobro` | Suscripciones | Cobro rechazado |
| `renovar_suscripcion` | Suscripciones | Renovación |
| `expirar_suscripcion` | Suscripciones | Expiración |
| `cobro_iniciado` | Cobros | Inicio de cobro manual |
| `inscripcion_fundador_iniciada` | Cobros | Inscripción fundador iniciada |
| `cobro_sin_medio_inscrito` | Cobros | Cobro automático sin medio inscrito |
| `cobro_automatico_iniciado` | Cobros | Cobro automático iniciado |
| `cobro_rechazado` | Cobros | Cobro rechazado (pasarela) |
| `cobro_monto_no_coincide` | Cobros | Monto confirmado no coincide |
| `inscripcion_fundador_confirmada` | Cobros | Inscripción fundador confirmada |
| `cobro_confirmado` | Cobros | Pago confirmado |

---

## Paso 5: Verificación de «qué no se registra, nunca»

Revisión de las 53 llamadas `.Registrar()` contra las 6 prohibiciones de `seguridad.md` §6:

### ⚠️ **Texto completo de reseñas — VIOLACIÓN en `ServicioResenas.cs:324`**

**La acción `resena.editada` guarda el texto completo del comentario anterior** (`comentarioAnterior`) en
la auditoría. La prohibición 6 de `seguridad.md` §6 dice: *«Texto completo de reseñas: quedaría
duplicado fuera del control de supresión»*.

La llamada en `ServicioResenas.cs:324` escribe este contexto JSON:
```csharp
new {
    calificacionAnterior = (int)resena.Calificacion,
    comentarioAnterior = resena.Comentario,      // ← texto completo de la reseña anterior
    aspectosAnteriores = resena.Aspectos,
    calificacionNueva = cmd.Calificacion,
    datosPersonales = datos.Count > 0,
}
```

El código tiene un comentario que justifica la decisión:
> «4D.2: la versión anterior se guarda en la auditoría antes de pisarla.»

Es una **tensión de diseño intencional**: preservar el historial vs. no duplicar texto
fuera de la tabla de reseñas. Si se retira, la auditoría pierde la capacidad de reconstruir
qué decía la reseña antes de cada edición. Si se mantiene, la auditoría duplica texto que
no está sujeto a supresión de reseñas.
**Carril de Joaquín** (reseñas). No se arregla en esta tarea.

### Contraseñas, ni siquiera fallidas

**No encontrada violación.** Ninguna llamada pasa `Contrasena`, `Password` ni el hash de contraseña
como campo de contexto. `ServicioSesion.IniciarAsync` verifica el hash incluso cuando la cuenta no existe
(para evitar timing attack, línea 66) pero no lo registra. El evento `inicio_sesion_fallido`
(`ServicioSesion.cs:78`) usa `_secretos.HashToken(correo)` para el correo.

### Tokens de sesión o refresco

**No encontrada violación.** Los tokens se generan en `AbrirSesion` y nunca pasan a `.Registrar()`.
Solo se guarda el hash en TBL-04, no en auditoría.

### Contenido de documentos

**No encontrada violación.** Las credenciales guardan tipo, nombre y entidad en auditoría, pero
nunca el contenido de documentos adjuntos. `ServicioVerificacion.cs:180` lleva un campo
`detalle` que es la justificación del verificador, no el contenido del documento.

### Direcciones de prestadores

**No encontrada violación.** `ServicioGeografia.cs:241` registra `registrar_direccion` con
entidad y actorId, pero el contexto no incluye coordenadas ni texto de dirección.

### Correos en claro en eventos de error

**No encontrada violación.** El único evento que toca correo es `inicio_sesion_fallido`
(`ServicioSesion.cs:78`) y usa `_secretos.HashToken(correo)` → `correoHash`.
Los avisos de correo usan `Enmascarar.Correo` (`a***@dominio.cl`).

---

## Paso 6: Pruebas de integración que miran la auditoría

| Evento | Prueba de integración que verifica la auditoría | Resultado |
|---|---|---|
| **Inicio de sesión fallido** | **Ninguna** integración. La unidad `SesionPruebas.cs:143` verifica la acción y los campos contra un `IAuditor` fake. Las integraciones `ProduccionPruebas.cs:255` y `:316` verifican que la IP llega a la auditoría pero no prueban el login fallido específico. | Sin integración que lo mire. |
| **Eliminación de cuenta** | **Ninguna** integración verifica la auditoría de eliminación. `IdentidadIntegracionPruebas.cs:253` verifica que la fila queda anonimizada pero no mira TBL-28. La unidad `CuentaPruebas.cs:414` verifica `eliminar_cuenta` contra fake. | Sin integración que lo mire. |

Otras pruebas de integración que sí miran la auditoría (contexto adicional):

- `IdentidadIntegracionPruebas.cs:236` — auditoría del registro de cuenta (`registro_cuenta`)
- `ResenasIntegracionPruebas.cs:162` — auditoría de reseña publicada
- `ResenasIntegracionPruebas.cs:285` — auditoría de reseña editada (`resena.editada`)
- `SuscripcionesIntegracionPruebas.cs:308` — auditoría de inicio de prueba
- `VerificacionIntegracionPruebas.cs:406` — auditoría de decisión de verificación (con documentos)
- `VerificacionIntegracionPruebas.cs:470` — auditoría de decisión de verificación (sin documentos)
- `ApiVerificacionPruebas.cs:147` — auditoría del flujo completo envío→sello

---

## Resumen de gaps

| Gap | Severidad | Acción sugerida |
|---|---|---|
| **Evento 3 (límite de tasa)** no se registra en N-11 | Media | Agregar `_logger.LogWarning` en `OnRejected` de `Seguridad.cs:64` con endpoint, IP, grupo. |
| **Evento 4 (validación)** no se registra en N-11 | Baja | Agregar logging en `FiltroDeValidacion<T>` de `Respuestas.cs`. Puede ser `LogDebug` o `LogWarning` según nivel de ruido. |
| **Evento 5 (acceso a documento)** no se registra | Media | Pendiente de implementación del acceso a documentos. |
| **Evento 7-8 (activación de tarjeta)** no se registran | Media | API-55 no implementada. Carril de Joaquín (tarjetas). |
| **Evento 11 (reseñar sin elegibilidad)** va a TBL-28, debería ir a N-11 | Baja | Mover o duplicar a `_logger.LogWarning`. El código actual (`ServicioResenas.cs:275`) usa `_auditor.Registrar`. |
| **Evento 1** falta N-11 | Baja | Agregar `_logger.LogWarning` junto a la llamada a `_auditor.Registrar` en `ServicioSesion.cs:78`. |
| **`resena.editada` guarda texto completo** | Media | Ver arriba. Tensión de diseño intencional. Carril de Joaquín (reseñas). |

---

## Sin verificar

- **N-11 (registro de la aplicación en Render):** no se puede verificar contra Render ni Neon
  sin acceso a `C:\Users\pdani\zass-secretos\`. Las afirmaciones sobre qué se envía al registro
  de Render son deducciones del código: la API usa `ILogger` de ASP.NET Core, pero no hay
  llamadas `_logger.Log*` explícitas para los eventos 3, 4 ni 11.
- **Las pruebas de integración que miran la auditoría** se buscaron con `Select-String` en
  `backend/pruebas/Zass.Integracion.Pruebas/`; no se corrieron porque esta tarea es solo
  informe y no modifica código.
- **Sin verificar contra la infraestructura** (sin acceso a secretos): no se leyó el registro
  real de Render ni se consultó la tabla `auditoria` de Neon.
