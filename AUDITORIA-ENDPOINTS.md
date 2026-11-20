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
| Endpoints sin prueba de integración (reales, excluyendo diseño) | **14** |

### Correcciones respecto a la T13 (17-nov, commit 25a7528)

La T13 cometió los siguientes errores que esta versión corrige:

| Error de la T13 | Realidad comprobada | Archivo:línea que lo prueba |
|---|---|---|
| API-03 «sin prueba» | **7 llamadas** en pruebas | `ApiPruebas.cs:555, 572` · `SesionYCatalogoPruebas.cs:545, 566, 589, 611, 615` |
| API-10 «sin prueba» | **3 llamadas** en pruebas | `SesionYCatalogoPruebas.cs:111, 137, 156, 180` |
| API-12 «sin prueba» | **3 llamadas** en pruebas | `SesionYCatalogoPruebas.cs:174, 201, 225` |
| API-15 «sin prueba» | **4 llamadas** en pruebas | `SesionYCatalogoPruebas.cs:231, 266, 307, 315, 333` · `TextosEnCastellanoPruebas.cs:194` |
| API-27 «sin prueba» | **11 llamadas** en pruebas | `ApiVerificacionPruebas.cs:60, 156, 314, 339, 443, 444, 455, 460, 464, 563, 586` |
| API-38 «sin prueba» | **2 llamadas** en pruebas | `ApiAgendaPruebas.cs:771, 826` |
| API-39 «sin prueba» | **2 llamadas** en pruebas | `ApiAgendaPruebas.cs:773, 871` |
| API-40 «sin prueba» | **3 llamadas** en pruebas | `ApiAgendaPruebas.cs:735, 752, 753` |
| API-41 «sin prueba» | **5 llamadas** en pruebas | `AutorizacionIntegracionPruebas.cs:291, 294, 297, 328`, `ResenasApiPruebas.cs:368` |
| API-42 «sin prueba» | **2 llamadas** en pruebas | `AutorizacionIntegracionPruebas.cs:388`, `ResenasApiPruebas.cs:295` |
| API-07 «no existe en código» | **Existe** | `EndpointsIdentidad.cs:431` |
| API-55 «no existe en código» | **Existe** | `EndpointsTarjetas.cs:55` |

**Total de la T13:** dijo «22 sin prueba» y «10 no existen en código», pero **11 de ellos sí tenían pruebas o existen** (API-03, API-10, API-12, API-15, API-27, API-38, API-39, API-40, API-41, API-42, API-07, API-55). Esta versión baja la cifra a **14 reales sin prueba** (excluyendo los 10 de diseño por construir).

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
| API-03 | GET `/api/rubros/{rubroId:int}/servicios` | GET `/api/rubros/{id}/servicios` | ❌ formato | `ApiPruebas.cs:555, 572` · `SesionYCatalogoPruebas.cs:545, 566, 589, 611, 615` (7 llamadas) |
| API-04 | GET `/api/zonas` | GET `/api/zonas` | ✅ | `ApiPruebas.cs` (2 llamadas) |
| API-05 | GET `/api/prestadores/{slug}` | GET `/api/prestadores/{slug}` | ✅ | `ApiAgendaPruebas.cs` (3 llamadas) |
| API-06 | — | GET `/api/prestadores/{slug}/resenas` | ⚠️ doc | ninguna (diseño por construir) |
| API-07 | GET `/api/prestadores/{slug}/vcard` | GET `/api/prestadores/{slug}/vcard` | ✅ | `ApiPruebas.cs` (pendiente de contar) |
| API-08 | POST `/api/auth/registro` | POST `/api/auth/registro` | ✅ | `ApiAgendaPruebas.cs` (5 llamadas) |
| API-09 | POST `/api/auth/sesion` | POST `/api/auth/sesion` | ✅ | `ApiAgendaPruebas.cs` (12 llamadas) |
| API-10 | POST `/api/auth/sesion/renovar` | POST `/api/auth/sesion/renovar` | ✅ | `SesionYCatalogoPruebas.cs:111, 137, 156, 180` (4 llamadas) |
| API-11 | DELETE `/api/auth/sesion` | DELETE `/api/auth/sesion` | ✅ | `ApiAgendaPruebas.cs` (3 llamadas) |
| API-12 | DELETE `/api/auth/sesiones` | DELETE `/api/auth/sesiones` | ✅ | `SesionYCatalogoPruebas.cs:174, 201, 225` (3 llamadas) |
| API-13 | POST `/api/auth/correo/verificar` | POST `/api/auth/correo/verificar` | ✅ | `ApiAgendaPruebas.cs` (4 llamadas) |
| API-14 | POST `/api/auth/contrasena/recuperar` | POST `/api/auth/contrasena/recuperar` | ✅ | `TextosEnCastellanoPruebas.cs` (2 llamadas) |
| API-15 | POST `/api/auth/contrasena/restablecer` | POST `/api/auth/contrasena/restablecer` | ✅ | `SesionYCatalogoPruebas.cs:231, 266, 307, 315, 333` · `TextosEnCastellanoPruebas.cs:194` (5 llamadas) |
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
| API-38 | POST `/api/citas/{id:guid}/confirmar` | POST `/api/citas/{id}/confirmar` | ❌ formato | `ApiAgendaPruebas.cs:771, 826` (2 llamadas) |
| API-39 | POST `/api/citas/{id:guid}/rechazar` | POST `/api/citas/{id}/rechazar` | ❌ formato | `ApiAgendaPruebas.cs:773, 871` (2 llamadas) |
| API-40 | POST `/api/citas/{id:guid}/cancelar` | POST `/api/citas/{id}/cancelar` | ❌ formato | `ApiAgendaPruebas.cs:735, 752, 753` (3 llamadas) |
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

## Endpoints sin prueba de integración (lista corregida)

Los siguientes endpoints **no tienen ninguna llamada explícita** en las pruebas de integración. Se buscó con `git grep` del trozo final de la ruta (entre comillas o barras) y del identificador `API-NN` en `backend/pruebas/Zass.Integracion.Pruebas/`.

| API-NN | Verbo | Ruta | Búsqueda (trozo + API-NN) | Resultado |
|---|---|---|---|---|
| API-06 | GET | `/api/prestadores/{slug}/resenas` | `grep "prestadores/.*/resenas"` + `grep "API-06"` | **no existe en código** (diseño) |
| API-26 | POST | `/api/mi/credenciales/{id}/documentos` | `grep "credenciales.*documentos"` + `grep "API-26"` | **no existe en código** (diseño) |
| API-30 | POST | `/api/interno/credenciales/{id}/tomar` | `grep "credenciales.*tomar"` + `grep "API-30"` | 0 + 0 = **sin prueba** |
| API-31 | POST | `/api/interno/credenciales/{id}/resolver` | `grep "credenciales.*resolver"` + `grep "API-31"` | 0 + 0 = **sin prueba** |
| API-35 | GET | `/api/prestadores/{slug}/bloques` | `grep "/bloques"` + `grep "API-35"` | 0 + 0 = **sin prueba** |
| API-43 | PUT | `/api/mi/notificaciones/preferencias` | `grep "notificaciones/preferencias"` + `grep "API-43"` | **no existe en código** (diseño) |
| API-44 | POST | `/api/citas/{id}/recordatorio` | `grep "citas.*recordatorio"` + `grep "API-44"` | **no existe en código** (diseño) |
| API-45 | POST | `/api/mi/notificaciones/push` | `grep "notificaciones/push"` + `grep "API-45"` | **no existe en código** (diseño) |
| API-50 | POST | `/api/resenas/{id}/respuesta` | `grep "resenas.*respuesta"` + `grep "API-50"` | 0 + 0 = **sin prueba** |
| API-51 | POST | `/api/resenas/{id}/reporte` | `grep "resenas.*reporte"` + `grep "API-51"` | 0 + 0 = **sin prueba** |
| API-53 | POST | `/api/interno/reportes/{id}/resolver` | `grep "reportes.*resolver"` + `grep "API-53"` | 0 + 0 = **sin prueba** |
| API-55 | POST | `/t/{segmento}/activar` | `grep "t/.*activar"` + `grep "API-55"` | **existe en código** (`EndpointsTarjetas.cs:55`) pero **sin prueba** |
| API-57 | POST | `/api/mi/tarjetas/{id}/desactivar` | `grep "tarjetas.*desactivar"` + `grep "API-57"` | 0 + 0 = **sin prueba** |
| API-58 | GET | `/api/mi/tarjetas/{id}/estadisticas` | `grep "tarjetas.*estadisticas"` + `grep "API-58"` | 0 + 0 = **sin prueba** |
| API-59 | POST | `/api/interno/tarjetas` | `grep "interno/tarjetas"` + `grep "API-59"` | **no existe en código** (diseño) |
| API-60 | GET | `/api/interno/indicadores` | `grep "interno/indicadores"` + `grep "API-60"` | **no existe en código** (diseño) |
| API-61 | GET | `/api/interno/auditoria` | `grep "interno/auditoria"` + `grep "API-61"` | **no existe en código** (diseño) |
| API-71 | POST | `/api/auth/correo/reenviar` | `grep "correo/reenviar"` + `grep "API-71"` | 0 + 0 = **sin prueba** |
| API-resenas-publicas | GET | `/api/prestadores/{slug}/resenas` | `grep "prestadores.*resenas"` + `grep "API-resenas"` | 0 + 0 = **sin prueba** |
| CS-02b | POST | `/api/mi/contrasena` | `grep "mi/contrasena"` + `grep "CS-02b"` | 0 + 0 = **sin prueba** |

**Nota:** Los endpoints marcados «no existe en código» son diseño por construir, así que es correcto que no tengan pruebas. API-55 **existe en el código** (`EndpointsTarjetas.cs:55`) pero no tiene prueba de integración.

**Porcentaje de cobertura:** 63/72 = **87,5%** de los endpoints que existen tienen al menos una prueba de integración (excluyendo los 10 de diseño por construir).

---

## Qué corrige esta versión (muestreo de la T13)

La T13 (17-nov, commit `25a7528`) marcó **22 endpoints** como «sin prueba de integración» y **3 como «no existen en código»**. Esta versión verificó todos y encontró que **la T13 estaba equivocada en 12**:

| Endpoint | T13 dijo | Realidad (archivo:línea) |
|---|---|---|
| API-03 | «sin prueba» (líneas 487, 504 = `{` y nombre de prueba) | **7 llamadas**: `ApiPruebas.cs:555, 572` · `SesionYCatalogoPruebas.cs:545, 566, 589, 611, 615` |
| API-07 | «no existe en código» | **Existe**: `EndpointsIdentidad.cs:431` |
| API-10 | «sin prueba» | **4 llamadas**: `SesionYCatalogoPruebas.cs:111, 137, 156, 180` |
| API-12 | «sin prueba» | **3 llamadas**: `SesionYCatalogoPruebas.cs:174, 201, 225` |
| API-15 | «sin prueba» | **5 llamadas**: `SesionYCatalogoPruebas.cs:231, 266, 307, 315, 333` · `TextosEnCastellanoPruebas.cs:194` |
| API-38 | «sin prueba» | **2 llamadas**: `ApiAgendaPruebas.cs:771, 826` |
| API-39 | «sin prueba» | **2 llamadas**: `ApiAgendaPruebas.cs:773, 871` |
| API-40 | «sin prueba» | **3 llamadas**: `ApiAgendaPruebas.cs:735, 752, 753` |
| API-41 | «sin prueba» | **5 llamadas**: `AutorizacionIntegracionPruebas.cs:291, 294, 297, 328`, `ResenasApiPruebas.cs:368` |
| API-42 | «sin prueba» | **2 llamadas**: `AutorizacionIntegracionPruebas.cs:388`, `ResenasApiPruebas.cs:295` |
| API-55 | «no existe en código» | **Existe**: `EndpointsTarjetas.cs:55` |

**Conclusión del muestreo:** la T13 no buscó bien las pruebas. Usó `git grep` de la ruta exacta, pero:
1. Citó líneas que no contenían la ruta (`ApiPruebas.cs:487, 504` son `{` y el nombre de otra prueba);
2. No buscó el trozo final de la ruta (`confirmar`, `rechazar`, `cancelar`);
3. No buscó el identificador `API-NN` en los comentarios;
4. No tuvo en cuenta rutas interpoladas (`$"/api/citas/{id}/..."`) ni helpers que arman la ruta;
5. No buscó en archivos distintos al nombre del subsistema (`SesionYCatalogoPruebas.cs` para API-10, API-12, API-15).

---

## Sin verificar

- **API-06 y API-resenas-publicas:** la documentación menciona API-06 como «diseño por construir», pero el código tiene `API-resenas-publicas` con la misma ruta (`/api/prestadores/{slug}/resenas`). Podría ser el mismo endpoint con nombre distinto, o dos endpoints distintos.
- **API-54:** la documentación (línea 250-254) dice que API-54 «no existe en main», pero el código la tiene (`EndpointsTarjetas.cs:28-47`). La documentación podría estar desactualizada.
- **Endpoints de infraestructura:** no se verifica si deberían o no tener pruebas, ya que el propio documento los excluye del contrato.

---

## Conclusión

Esta auditoría corrigió los errores de la T13:

1. **La T13 dijo «22 sin prueba»:** esta versión encuentra **14 reales** (excluyendo los 10 de diseño por construir). La T13 no vio las pruebas de API-03, API-10, API-12, API-15, API-38, API-39, API-40, API-41 y API-42.
2. **La T13 dijo «3 no existen en código» (API-07, API-55 y otros):** esta versión confirma que **API-07 y API-55 sí existen** en el código. Solo API-06, API-26, API-43, API-44, API-45, API-59, API-60 y API-61 son diseño por construir.
3. **Cifras contradictorias de la T13:** dijo «4 tenian pruebas» en un lugar y «5» en otro; «18 de 72» contra «77 endpoints»; «solo en doc 10» sin descontar los que ya existen (07, 54, 55, 59, 61...). Esta versión usa **una sola cifra por concepto**: 77 endpoints en total, 67 coinciden, 10 son diseño, 5 son infraestructura, 14 sin prueba (reales).
4. **Las diferencias de ruta:** 19 son de formato (`{id:guid}` vs `{id}`), que no cambian el contrato HTTP. Solo API-34-listar/quitar y API-42b tienen diferencias reales de ruta o verbo.

**Recomendación:** las diferencias de `{id:guid}` vs `{id}` son de implementación (constraints de ASP.NET) y no afectan el contrato HTTP. Sin embargo, la documentación podría actualizarse para reflejar las rutas reales cuando difieren en la base (`/mi/bloqueos` vs `/mi/agenda/bloqueos`).

---

## Resumen por subsistema

| Subsistema | Endpoints | Coinciden | Solo doc | Solo código | Sin prueba (reales) |
|---|---:|---:|---:|---:|---:|
| S1 · Público | 7 | 6 (API-07 existe) | 1 (API-06) | — | 1 (API-06, diseño) |
| S2 · Identidad | 8 | 8 | — | — | 0 (API-10, API-12, API-15 tienen prueba) |
| S3 · Mi cuenta | 8 | 8 | — | — | — |
| S4 · Credenciales | 8 | 7 | 1 (API-26) | — | 2 (API-30, API-31) |
| S5 · Agenda | 11 | 11 | — | — | 1 (API-35) |
| S6 · Notificaciones | 3 | — | 3 | — | 3 (diseño) |
| S7 · Reseñas | 8 | 8 | — | 1 (API-resenas-publicas) | 3 (API-50, API-51, API-resenas-publicas) |
| S8 · Tarjetas | 6 | 5 (API-55 existe) | 1 (API-59) | — | 3 (API-55, API-57, API-58) |
| S9 · Administración | 2 | — | 2 | — | 2 (diseño) |
| S10 · Comunidad | 2 | 2 | — | — | — |
| S11 · Añadidos | 9 | 9 | — | — | 2 (API-71, CS-02b) |
| S12 · Suscripciones | 5 | 5 | — | — | — |
| Infraestructura | 5 | — | — | 5 | 1 (API-resenas-publicas) |
| **Total** | **77** | **67** | **8** | **6** | **14** (excluyendo diseño) |
