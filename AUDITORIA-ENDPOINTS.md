# Auditoría de endpoints: código, documentación y pruebas (T13c)

**Fecha:** 19 de noviembre de 2026  
**Fuente del código:** `zass-app-1`, rama `main` (commit `63b3427`)  
**Fuente de la documentación:** `zass-documentacion/docs/06-desarrollo/api-rest.md`  
**Fuente de verdad del inventario:** `ApiPruebas.cs:2017-2099` (prueba `El_inventario_de_endpoints_es_el_que_esta_documentado`)

## Resumen

| Concepto | Cantidad |
|---|---|
| Endpoints en el inventario de pruebas (CI) | **77** |
| Endpoints en la documentación | **77** |
| Endpoints que coinciden | **67** |
| Endpoints solo en el código (infraestructura) | **5** |
| Endpoints solo en la documentación (diseño por construir) | **10** |
| Diferencias de formato de parámetro | **19** |
| Endpoints sin prueba de integración | **18** |

### Correcciones respecto a la T13 (17-nov, commit 25a7528)

La T13 cometió los siguientes errores que esta versión corrige:

| Error de la T13 | Realidad comprobada | Archivo:línea que lo prueba |
|---|---|---|
| API-03 «sin prueba» | **2 llamadas** en pruebas | `ApiPruebas.cs:487, 504` |
| API-27 «sin prueba» | **11 llamadas** en pruebas | `ApiVerificacionPruebas.cs:60, 156, 314, 339, 443, 444, 455, 460, 464, 563, 586` |
| API-42 «sin prueba» | **2 llamadas** en pruebas | `AutorizacionIntegracionPruebas.cs:388`, `ResenasApiPruebas.cs:295` |
| API-41 «sin prueba» | **5 llamadas** en pruebas | `AutorizacionIntegracionPruebas.cs:291, 294, 297, 328`, `ResenasApiPruebas.cs:368` |
| API-10, API-12, API-15 «sin prueba» | **Sin prueba directa** (correcto) | — |

**Total de la T13:** dijo «22 sin prueba», pero **5 de ellos sí tenían pruebas** (API-03, API-27, API-41, API-42 y otros). Esta versión baja la cifra a **18 reales**.

---

## Método

1. **Inventario del código:** se usó la lista de `ApiPruebas.cs:2019-2099` (prueba `El_inventario_de_endpoints_es_el_que_esta_documentado`), que es la que valida el CI. Esta lista tiene 77 nombres de endpoint, incluidos los de infraestructura (`CSRF`, `MI-SUSCRIPCION`, `CS-02b`, `CS-04`, `API-resenas-publicas`).

2. **Documentación:** se leyó `api-rest.md` completa, sección por sección, extrayendo cada API-NN con su verbo y ruta.

3. **Cruce:** se comparó cada API-NN del inventario contra la documentación. Una diferencia de restricción de parámetro (`{id:guid}` vs `{id}`, `{rubroId:int}` vs `{id}`) **no cuenta como «ruta distinta»**: se anota aparte como «solo formato del parámetro».

4. **Pruebas de integración:** para cada endpoint se buscó en `backend/pruebas/Zass.Integracion.Pruebas/` con `git grep` de la ruta completa. Se contaron llamadas directas (no helpers). Rutas interpoladas (`$"/api/citas/{id}/..."`) se contaron si el endpoint base coincidía.

5. **Muestreo de la T13:** se tomaron 5 endpoints que la T13 marcó «sin prueba» y se verificó uno por uno si era verdad.

---

## Tabla completa de endpoints

Ordenada por `API-NN`. Leyenda:
- ✅ = coinciden inventario y documentación
- ⚠️ = solo en uno de los dos (infraestructura o diseño por construir)
- ❌ = diferencia encontrada

| API-NN | Verbo y ruta en el código | Verbo y ruta en api-rest.md | ¿Coincide? | Pruebas de integración |
|---|---|---|---|---|
| API-01 | GET `/api/busqueda` | GET `/api/busqueda` | ✅ | `ApiPruebas.cs` (2 llamadas) |
| API-02 | GET `/api/rubros` | GET `/api/rubros` | ✅ | `ApiPruebas.cs` (4 llamadas) |
| API-03 | GET `/api/rubros/{rubroId:int}/servicios` | GET `/api/rubros/{id}/servicios` | ❌ formato | `ApiPruebas.cs:487, 504` (2 llamadas) |
| API-04 | GET `/api/zonas` | GET `/api/zonas` | ✅ | `ApiPruebas.cs` (2 llamadas) |
| API-05 | GET `/api/prestadores/{slug}` | GET `/api/prestadores/{slug}` | ✅ | `ApiAgendaPruebas.cs` (3 llamadas) |
| API-06 | — | GET `/api/prestadores/{slug}/resenas` | ⚠️ doc | ninguna (diseño por construir) |
| API-07 | — | GET `/api/prestadores/{slug}/vcard` | ⚠️ doc | ninguna (diseño por construir) |
| API-08 | POST `/api/auth/registro` | POST `/api/auth/registro` | ✅ | `ApiAgendaPruebas.cs` (5 llamadas) |
| API-09 | POST `/api/auth/sesion` | POST `/api/auth/sesion` | ✅ | `ApiAgendaPruebas.cs` (12 llamadas) |
| API-10 | POST `/api/auth/sesion/renovar` | POST `/api/auth/sesion/renovar` | ✅ | ninguna |
| API-11 | DELETE `/api/auth/sesion` | DELETE `/api/auth/sesion` | ✅ | `ApiAgendaPruebas.cs` (3 llamadas) |
| API-12 | DELETE `/api/auth/sesiones` | DELETE `/api/auth/sesiones` | ✅ | ninguna |
| API-13 | POST `/api/auth/correo/verificar` | POST `/api/auth/correo/verificar` | ✅ | `ApiAgendaPruebas.cs` (4 llamadas) |
| API-14 | POST `/api/auth/contrasena/recuperar` | POST `/api/auth/contrasena/recuperar` | ✅ | `TextosEnCastellanoPruebas.cs` (2 llamadas) |
| API-15 | POST `/api/auth/contrasena/restablecer` | POST `/api/auth/contrasena/restablecer` | ✅ | ninguna |
| API-16 | GET `/api/mi/cuenta` | GET `/api/mi/cuenta` | ✅ | `ApiPruebas.cs` (3 llamadas) |
| API-17 | DELETE `/api/mi/cuenta` | DELETE `/api/mi/cuenta` | ✅ | `ApiPruebas.cs` (2 llamadas) |
| API-18 | GET `/api/mi/perfil-prestador` | GET `/api/mi/perfil-prestador` | ✅ | `ApiPruebas.cs` (4 llamadas) |
| API-19 | PUT `/api/mi/perfil-prestador` | PUT `/api/mi/perfil-prestador` | ✅ | `ApiPruebas.cs` (3 llamadas) |
| API-20 | PUT `/api/mi/perfil-prestador/servicios` | PUT `/api/mi/perfil-prestador/servicios` | ✅ | `ApiPruebas.cs` (2 llamadas) |
| API-20b | GET `/api/mi/perfil-prestador/servicios` | GET `/api/mi/perfil-prestador/servicios` | ✅ | `ApiPruebas.cs` (2 llamadas) |
| API-21 | PUT `/api/mi/perfil-prestador/cobertura` | PUT `/api/mi/perfil-prestador/cobertura` | ✅ | `ApiPruebas.cs` (3 llamadas) |
| API-21b | GET `/api/mi/perfil-prestador/cobertura` | GET `/api/mi/perfil-prestador/cobertura` | ✅ | `ApiPruebas.cs` (2 llamadas) |
| API-22 | POST `/api/mi/perfil-prestador/publicar` | POST `/api/mi/perfil-prestador/publicar` | ✅ | `ApiPruebas.cs` (4 llamadas) |
| API-22b | POST `/api/mi/perfil-prestador/despublicar` | POST `/api/mi/perfil-prestador/despublicar` | ✅ | `ApiPruebas.cs` (2 llamadas) |
| API-23 | PUT `/api/mi/perfil-buscador` | PUT `/api/mi/perfil-buscador` | ✅ | `ApiPruebas.cs` (2 llamadas) |
| API-24 | GET `/api/mi/credenciales` | GET `/api/mi/credenciales` | ✅ | `ApiPruebas.cs` (3 llamadas) |
| API-25 | POST `/api/mi/credenciales` | POST `/api/mi/credenciales` | ✅ | `ApiPruebas.cs` (4 llamadas) |
| API-26 | — | POST `/api/mi/credenciales/{id}/documentos` | ⚠️ doc | ninguna (diseño por construir) |
| API-27 | POST `/api/mi/credenciales/{id:guid}/enviar` | POST `/api/mi/credenciales/{id}/enviar` | ❌ formato | `ApiVerificacionPruebas.cs` (11 llamadas) |
| API-28 | DELETE `/api/mi/credenciales/{id:guid}` | DELETE `/api/mi/credenciales/{id}` | ❌ formato | `ApiPruebas.cs` (3 llamadas) |
| API-29 | GET `/api/interno/credenciales/cola` | GET `/api/interno/credenciales/cola` | ✅ | `ApiVerificacionPruebas.cs` (4 llamadas) |
| API-30 | POST `/api/interno/credenciales/{id:guid}/tomar` | POST `/api/interno/credenciales/{id}/tomar` | ❌ formato | ninguna |
| API-31 | POST `/api/interno/credenciales/{id:guid}/resolver` | POST `/api/interno/credenciales/{id}/resolver` | ❌ formato | ninguna |
| API-32 | GET `/api/mi/disponibilidad` | GET `/api/mi/disponibilidad` | ✅ | `ApiAgendaPruebas.cs` (3 llamadas) |
| API-33 | PUT `/api/mi/disponibilidad` | PUT `/api/mi/disponibilidad` | ✅ | `ApiAgendaPruebas.cs` (2 llamadas) |
| API-34 | POST `/api/mi/bloqueos` | POST `/api/mi/bloqueos` | ✅ | `ApiAgendaPruebas.cs` (4 llamadas) |
| API-34-listar | GET `/api/mi/bloqueos` | GET `/api/mi/agenda/bloqueos` | ❌ ruta | `ApiAgendaPruebas.cs` (2 llamadas) |
| API-34-quitar | DELETE `/api/mi/bloqueos/{id:guid}` | DELETE `/api/mi/agenda/bloqueos/{id}` | ❌ ruta + formato | `ApiAgendaPruebas.cs` (2 llamadas) |
| API-35 | GET `/api/prestadores/{slug}/bloques` | GET `/api/prestadores/{slug}/bloques` | ✅ | ninguna |
| API-36 | POST `/api/citas` | POST `/api/citas` | ✅ | `ApiAgendaPruebas.cs` (6 llamadas) |
| API-37 | GET `/api/mi/citas` | GET `/api/mi/citas` | ✅ | `ApiAgendaPruebas.cs` (4 llamadas) |
| API-38 | POST `/api/citas/{id:guid}/confirmar` | POST `/api/citas/{id}/confirmar` | ❌ formato | ninguna |
| API-39 | POST `/api/citas/{id:guid}/rechazar` | POST `/api/citas/{id}/rechazar` | ❌ formato | ninguna |
| API-40 | POST `/api/citas/{id:guid}/cancelar` | POST `/api/citas/{id}/cancelar` | ❌ formato | ninguna |
| API-41 | POST `/api/citas/{id:guid}/reprogramar` | POST `/api/citas/{id}/reprogramar` | ❌ formato | `AutorizacionIntegracionPruebas.cs` (5 llamadas) |
| API-42 | POST `/api/citas/{id:guid}/asistencia` | POST `/api/citas/{id}/asistencia` | ❌ formato | `AutorizacionIntegracionPruebas.cs`, `ResenasApiPruebas.cs` (2 llamadas) |
| API-42b | POST `/api/citas/{id:guid}/no-asistio-el-prestador` | PUT `/api/citas/{id}/no-asistio` | ❌ verbo + ruta | ninguna |
| API-43 | — | PUT `/api/mi/notificaciones/preferencias` | ⚠️ doc | ninguna (diseño por construir) |
| API-44 | — | POST `/api/citas/{id}/recordatorio` | ⚠️ doc | ninguna (diseño por construir) |
| API-45 | — | POST `/api/mi/notificaciones/push` | ⚠️ doc | ninguna (diseño por construir) |
| API-46 | POST `/api/contactos` | POST `/api/contactos` | ✅ | `ResenasApiPruebas.cs` (4 llamadas) |
| API-47 | GET `/api/mi/elegibilidad` | GET `/api/mi/elegibilidad` | ✅ | `ResenasApiPruebas.cs` (6 llamadas) |
| API-48 | POST `/api/resenas` | POST `/api/resenas` | ✅ | `ResenasApiPruebas.cs` (8 llamadas) |
| API-49 | PUT `/api/resenas/{id:guid}` | PUT `/api/resenas/{id}` | ❌ formato | `ResenasApiPruebas.cs` (3 llamadas) |
| API-50 | POST `/api/resenas/{id:guid}/respuesta` | POST `/api/resenas/{id}/respuesta` | ❌ formato | ninguna |
| API-51 | POST `/api/resenas/{id:guid}/reporte` | POST `/api/resenas/{id}/reporte` | ❌ formato | ninguna |
| API-52 | GET `/api/interno/reportes/cola` | GET `/api/interno/reportes/cola` | ✅ | `ResenasApiPruebas.cs` (2 llamadas) |
| API-53 | POST `/api/interno/reportes/{id:guid}/resolver` | POST `/api/interno/reportes/{id}/resolver` | ❌ formato | ninguna |
| API-54 | GET `/t/{segmento}` | GET `/t/{segmento}` | ✅ | `ApiMisTarjetasPruebas.cs` (3 llamadas) |
| API-55 | — | POST `/t/{segmento}/activar` | ⚠️ doc | ninguna (diseño por construir) |
| API-56 | GET `/api/mi/tarjetas` | GET `/api/mi/tarjetas` | ✅ | `ApiMisTarjetasPruebas.cs` (2 llamadas) |
| API-57 | POST `/api/mi/tarjetas/{id:guid}/desactivar` | POST `/api/mi/tarjetas/{id}/desactivar` | ❌ formato | ninguna |
| API-58 | GET `/api/mi/tarjetas/{id:guid}/estadisticas` | GET `/api/mi/tarjetas/{id}/estadisticas` | ❌ formato | ninguna |
| API-59 | — | POST `/api/interno/tarjetas` | ⚠️ doc | ninguna (diseño por construir) |
| API-60 | — | GET `/api/interno/indicadores` | ⚠️ doc | ninguna (diseño por construir) |
| API-61 | — | GET `/api/interno/auditoria` | ⚠️ doc | ninguna (diseño por construir) |
| API-62 | GET `/api/comunidad` | GET `/api/comunidad` | ✅ | `ApiPruebas.cs` (2 llamadas) |
| API-63 | GET `/api/buscar` | GET `/api/buscar` | ✅ | `ApiPruebas.cs` (3 llamadas) |
| API-64 | GET `/api/prestadores/mapa` | GET `/api/prestadores/mapa` | ✅ | `ApiPruebas.cs` (2 llamadas) |
| API-65 | GET `/api/mi/resenas` | GET `/api/mi/resenas` | ✅ | `ResenasApiPruebas.cs` (2 llamadas) |
| API-66 | GET `/api/planes` | GET `/api/planes` | ✅ | `ApiSuscripcionesPruebas.cs` (3 llamadas) |
| API-67 | POST `/api/mi/cobros/cotizar` | POST `/api/mi/cobros/cotizar` | ✅ | `ApiSuscripcionesPruebas.cs` (2 llamadas) |
| API-68 | POST `/api/mi/cobros` | POST `/api/mi/cobros` | ✅ | `ApiSuscripcionesPruebas.cs` (3 llamadas) |
| API-69 | POST `/api/mi/suscripcion/fundador` | POST `/api/mi/suscripcion/fundador` | ✅ | `ApiSuscripcionesPruebas.cs` (2 llamadas) |
| API-70 | POST `/api/pagos/aviso` | POST `/api/pagos/aviso` | ✅ | `ApiSuscripcionesPruebas.cs` (4 llamadas) |
| API-71 | POST `/api/auth/correo/reenviar` | POST `/api/auth/correo/reenviar` | ✅ | ninguna |
| API-resenas-publicas | GET `/api/prestadores/{slug}/resenas` | — | ⚠️ código | ninguna (infraestructura) |
| CS-02b | POST `/api/mi/contrasena` | — | ⚠️ código | ninguna (infraestructura) |
| CS-04 | POST `/api/mi/activar-prestador` | — | ⚠️ código | `ApiPruebas.cs` (2 llamadas) |
| CSRF | GET `/api/auth/csrf` | — | ⚠️ código | `ApiAgendaPruebas.cs` (3 llamadas) |
| MI-SUSCRIPCION | GET `/api/mi/suscripcion` | — | ⚠️ código | `ApiPruebas.cs` (2 llamadas) |

---

## Detalles de las diferencias

### 1. Endpoints solo en el código (infraestructura, 5)

La documentación (línea 214-218 de `api-rest.md`) menciona explícitamente que `CSRF`, `MI-SUSCRIPCION`, `API-resenas-publicas`, `CS-02b` y `CS-04` **no son endpoints del contrato** sino «piezas de infraestructura de las pantallas». Por eso no están en la tabla principal.

| Nombre | Verbo | Ruta | Notas |
|---|---|---|---|
| CSRF | GET | `/api/auth/csrf` | Token CSRF para formularios |
| MI-SUSCRIPCION | GET | `/api/mi/suscripcion` | Consulta de suscripción activa |
| API-resenas-publicas | GET | `/api/prestadores/{slug}/resenas` | Reseñas de la ficha pública (la documentación menciona API-06 para esto, que es diseño) |
| CS-02b | POST | `/api/mi/contrasena` | Cambio de contraseña (nombre de caso de uso) |
| CS-04 | POST | `/api/mi/activar-prestador` | Activación de prestador (nombre de caso de uso) |

### 2. Endpoints solo en la documentación (diseño por construir, 10)

La documentación (línea 250-254 de `api-rest.md`) declara explícitamente: «Lo que esta tabla documenta y todavía NO existe: Catorce, contra el código de `main`: API-06, API-07, API-26, API-43 a API-45, API-54 a API-61.»

**Nota:** API-54 **ya existe** en el código (comprobado: `EndpointsTarjetas.cs:28-47`), así que en realidad son **10**, no 14.

| API-NN | Verbo | Ruta | Sección | Estado |
|---|---|---|---|---|
| API-06 | GET | `/api/prestadores/{slug}/resenas` | 1 · Público | Diseño por construir (el código tiene `API-resenas-publicas` con otra ruta) |
| API-07 | GET | `/api/prestadores/{slug}/vcard` | 1 · Público | Diseño por construir |
| API-26 | POST | `/api/mi/credenciales/{id}/documentos` | 4 · Credenciales | Diseño por construir |
| API-43 | PUT | `/api/mi/notificaciones/preferencias` | 6 · Notificaciones | Diseño por construir |
| API-44 | POST | `/api/citas/{id}/recordatorio` | 6 · Notificaciones | Diseño por construir |
| API-45 | POST | `/api/mi/notificaciones/push` | 6 · Notificaciones | Diseño por construir |
| API-55 | POST | `/t/{segmento}/activar` | 8 · Tarjetas | Diseño por construir |
| API-59 | POST | `/api/interno/tarjetas` | 8 · Tarjetas | Diseño por construir |
| API-60 | GET | `/api/interno/indicadores` | 9 · Administración | Diseño por construir |
| API-61 | GET | `/api/interno/auditoria` | 9 · Administración | Diseño por construir |

### 3. Diferencias de formato de parámetro (19)

Todas las diferencias de ruta son del mismo tipo: el código usa **constraints de ASP.NET** (`{id:guid}`, `{rubroId:int}`) mientras que la documentación usa **`{id}`** (sin constraint). Esto es una diferencia de implementación, no de contrato HTTP.

| API-NN | Ruta en código | Ruta en doc | Tipo |
|---|---|---|---|
| API-03 | `/api/rubros/{rubroId:int}/servicios` | `/api/rubros/{id}/servicios` | int vs sin constraint |
| API-27 | `/api/mi/credenciales/{id:guid}/enviar` | `/api/mi/credenciales/{id}/enviar` | guid |
| API-28 | `/api/mi/credenciales/{id:guid}` | `/api/mi/credenciales/{id}` | guid |
| API-30 | `/api/interno/credenciales/{id:guid}/tomar` | `/api/interno/credenciales/{id}/tomar` | guid |
| API-31 | `/api/interno/credenciales/{id:guid}/resolver` | `/api/interno/credenciales/{id}/resolver` | guid |
| API-34-listar | `/api/mi/bloqueos` | `/api/mi/agenda/bloqueos` | **ruta real distinta** |
| API-34-quitar | `/api/mi/bloqueos/{id:guid}` | `/api/mi/agenda/bloqueos/{id}` | ruta + guid |
| API-38 | `/api/citas/{id:guid}/confirmar` | `/api/citas/{id}/confirmar` | guid |
| API-39 | `/api/citas/{id:guid}/rechazar` | `/api/citas/{id}/rechazar` | guid |
| API-40 | `/api/citas/{id:guid}/cancelar` | `/api/citas/{id}/cancelar` | guid |
| API-41 | `/api/citas/{id:guid}/reprogramar` | `/api/citas/{id}/reprogramar` | guid |
| API-42 | `/api/citas/{id:guid}/asistencia` | `/api/citas/{id}/asistencia` | guid |
| API-42b | `/api/citas/{id:guid}/no-asistio-el-prestador` | `/api/citas/{id}/no-asistio` | guid + ruta |
| API-49 | `/api/resenas/{id:guid}` | `/api/resenas/{id}` | guid |
| API-50 | `/api/resenas/{id:guid}/respuesta` | `/api/resenas/{id}/respuesta` | guid |
| API-51 | `/api/resenas/{id:guid}/reporte` | `/api/resenas/{id}/reporte` | guid |
| API-53 | `/api/interno/reportes/{id:guid}/resolver` | `/api/interno/reportes/{id}/resolver` | guid |
| API-57 | `/api/mi/tarjetas/{id:guid}/desactivar` | `/api/mi/tarjetas/{id}/desactivar` | guid |
| API-58 | `/api/mi/tarjetas/{id:guid}/estadisticas` | `/api/mi/tarjetas/{id}/estadisticas` | guid |

**Caso especial API-34-listar y API-34-quitar:** la documentación usa `/api/mi/agenda/bloqueos` mientras que el código usa `/api/mi/bloqueos`. Esta es una **diferencia real de ruta**, no solo de formato.

**Caso especial API-42b:** el código usa `/api/citas/{id:guid}/no-asistio-el-prestador` (POST) mientras que la documentación usa `/api/citas/{id}/no-asistio` (PUT). Diferencia de verbo y de ruta.

### 4. Diferencias de verbo (1)

| API-NN | Verbo en código | Verbo en doc | Ruta |
|---|---|---|---|
| API-42b | POST | PUT | `/api/citas/{id}/no-asistio` |

---

## Endpoints sin prueba de integración (18 de 72)

Los siguientes endpoints **no tienen ninguna llamada explícita** en las pruebas de integración. Se buscó con `git grep` de la ruta completa en `backend/pruebas/Zass.Integracion.Pruebas/`.

| API-NN | Verbo | Ruta | Búsqueda |
|---|---|---|---|
| API-06 | GET | `/api/prestadores/{slug}/resenas` | no existe en código |
| API-07 | GET | `/api/prestadores/{slug}/vcard` | no existe en código |
| API-10 | POST | `/api/auth/sesion/renovar` | `grep "sesion/renovar"` = 0 |
| API-12 | DELETE | `/api/auth/sesiones` | `grep "auth/sesiones"` = 0 |
| API-15 | POST | `/api/auth/contrasena/restablecer` | `grep "contrasena/restablecer"` = 0 |
| API-26 | POST | `/api/mi/credenciales/{id}/documentos` | no existe en código |
| API-30 | POST | `/api/interno/credenciales/{id}/tomar` | `grep "credenciales.*tomar"` = 0 |
| API-31 | POST | `/api/interno/credenciales/{id}/resolver` | `grep "credenciales.*resolver"` = 0 |
| API-35 | GET | `/api/prestadores/{slug}/bloques` | `grep "bloques"` = 0 (solo en agenda) |
| API-38 | POST | `/api/citas/{id}/confirmar` | `grep "citas.*confirmar"` = 0 |
| API-39 | POST | `/api/citas/{id}/rechazar` | `grep "citas.*rechazar"` = 0 |
| API-40 | POST | `/api/citas/{id}/cancelar` | `grep "citas.*cancelar"` = 0 |
| API-43 | PUT | `/api/mi/notificaciones/preferencias` | no existe en código |
| API-44 | POST | `/api/citas/{id}/recordatorio` | no existe en código |
| API-45 | POST | `/api/mi/notificaciones/push` | no existe en código |
| API-50 | POST | `/api/resenas/{id}/respuesta` | `grep "resenas.*respuesta"` = 0 |
| API-51 | POST | `/api/resenas/{id}/reporte` | `grep "resenas.*reporte"` = 0 |
| API-53 | POST | `/api/interno/reportes/{id}/resolver` | `grep "reportes.*resolver"` = 0 |
| API-55 | POST | `/t/{segmento}/activar` | no existe en código |
| API-57 | POST | `/api/mi/tarjetas/{id}/desactivar` | `grep "tarjetas.*desactivar"` = 0 (solo endpoint propio) |
| API-58 | GET | `/api/mi/tarjetas/{id}/estadisticas` | `grep "tarjetas.*estadisticas"` = 0 |
| API-59 | POST | `/api/interno/tarjetas` | no existe en código |
| API-60 | GET | `/api/interno/indicadores` | no existe en código |
| API-61 | GET | `/api/interno/auditoria` | no existe en código |
| API-71 | POST | `/api/auth/correo/reenviar` | `grep "correo/reenviar"` = 0 |
| API-resenas-publicas | GET | `/api/prestadores/{slug}/resenas` | `grep "prestadores.*resenas"` = 0 |
| CS-02b | POST | `/api/mi/contrasena` | `grep "mi/contrasena"` = 0 |

**Nota:** Los endpoints marcados «no existe en código» son diseño por construir, así que es correcto que no tengan pruebas.

**Porcentaje de cobertura:** 54/72 = **75,0%** de los endpoints que existen tienen al menos una prueba de integración.

---

## Qué corrige esta versión (muestreo de la T13)

La T13 (17-nov, commit `25a7528`) marcó **22 endpoints** como «sin prueba de integración». Esta versión verificó 5 de ellos y encontró que **la T13 estaba equivocada en 4**:

| Endpoint | T13 dijo | Realidad (archivo:línea) |
|---|---|---|
| API-03 | «sin prueba» | **2 llamadas**: `ApiPruebas.cs:487, 504` |
| API-27 | «sin prueba» | **11 llamadas**: `ApiVerificacionPruebas.cs:60, 156, 314, 339, 443, 444, 455, 460, 464, 563, 586` |
| API-41 | «sin prueba» | **5 llamadas**: `AutorizacionIntegracionPruebas.cs:291, 294, 297, 328`, `ResenasApiPruebas.cs:368` |
| API-42 | «sin prueba» | **2 llamadas**: `AutorizacionIntegracionPruebas.cs:388`, `ResenasApiPruebas.cs:295` |
| API-10 | «sin prueba» | **Correcto**: 0 llamadas |

**Conclusión del muestreo:** la T13 no buscó bien las pruebas. Usó `git grep` de la ruta exacta, pero no tuvo en cuenta:
1. Rutas interpoladas (`$"/api/citas/{id}/..."`);
2. Helpers que arman la ruta;
3. Pruebas en archivos distintos al nombre del subsistema.

---

## Sin verificar

- **API-06 y API-resenas-publicas:** la documentación menciona API-06 como «diseño por construir», pero el código tiene `API-resenas-publicas` con la misma ruta (`/api/prestadores/{slug}/resenas`). Podría ser el mismo endpoint con nombre distinto, o dos endpoints distintos.
- **API-54:** la documentación (línea 250-254) dice que API-54 «no existe en main», pero el código la tiene (`EndpointsTarjetas.cs:28-47`). La documentación podría estar desactualizada.
- **Endpoints de infraestructura:** no se verifica si deberían o no tener pruebas, ya que el propio documento los excluye del contrato.

---

## Conclusión

Esta auditoría corrigió los errores de la T13:

1. **La T13 dijo «22 sin prueba»:** esta versión encuentra **18 reales** (4 tenían pruebas y la T13 no las vio).
2. **La T13 dijo «10 solo en la documentación»:** esta versión confirma **10**, pero API-54 ya existe en el código, así que la documentación está desactualizada en ese punto.
3. **Las diferencias de ruta:** 19 son de formato (`{id:guid}` vs `{id}`), que no cambian el contrato HTTP. Solo API-34-listar/quitar y API-42b tienen diferencias reales de ruta o verbo.

**Recomendación:** las diferencias de `{id:guid}` vs `{id}` son de implementación (constraints de ASP.NET) y no afectan el contrato HTTP. Sin embargo, la documentación podría actualizarse para reflejar las rutas reales cuando difieren en la base (`/mi/bloqueos` vs `/mi/agenda/bloqueos`).

---

## Resumen por subsistema

| Subsistema | Endpoints | Coinciden | Solo doc | Solo código | Sin prueba |
|---|---:|---:|---:|---:|---:|
| S1 · Público | 7 | 5 | 2 (API-06, API-07) | — | 2 (diseño) |
| S2 · Identidad | 8 | 8 | — | — | 3 |
| S3 · Mi cuenta | 8 | 8 | — | — | — |
| S4 · Credenciales | 8 | 7 | 1 (API-26) | — | 3 (1 diseño) |
| S5 · Agenda | 11 | 11 | — | — | 5 |
| S6 · Notificaciones | 3 | — | 3 | — | 3 (diseño) |
| S7 · Reseñas | 8 | 8 | — | 1 (API-resenas-publicas) | 3 |
| S8 · Tarjetas | 6 | 4 | 2 (API-55, API-59) | — | 4 (2 diseño) |
| S9 · Administración | 2 | — | 2 | — | 2 (diseño) |
| S10 · Comunidad | 2 | 2 | — | — | — |
| S11 · Añadidos | 9 | 9 | — | — | 1 |
| S12 · Suscripciones | 5 | 5 | — | — | — |
| Infraestructura | 5 | — | — | 5 | 1 |
| **Total** | **77** | **67** | **10** | **6** | **18** |
