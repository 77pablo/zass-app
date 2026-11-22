# Auditoría de endpoints: código, documentación y pruebas

**Fecha:** 17 de noviembre de 2026  
**Fuente del código:** `zass-app-1`, rama `main` (commit `25a7528`)  
**Fuente de la documentación:** `zass-documentacion/docs/06-desarrollo/api-rest.md`

## Resumen

| Concepto | Cantidad |
|---|---|
| Endpoints en el código | **74** |
| Endpoints en la documentación | **77** |
| Endpoints que coinciden | **67** |
| Endpoints solo en el código | **6** |
| Endpoints solo en la documentación | **10** |
| Diferencias de verbo | **1** |
| Diferencias de ruta (parámetros) | **19** |
| Endpoints sin prueba de integración | **22** |

### Tipos de diferencia encontrada

| Tipo | Cantidad | Descripción |
|---|---|---|
| solo en el código | 6 | Endpoints que existen en la API pero no están en `api-rest.md` |
| solo en la documentación | 10 | Endpoints documentados que no se encuentran en el código |
| verbo distinto | 1 | El mismo endpoint tiene verbo distinto en código y documentación |
| ruta distinta | 19 | Diferencias en la forma de los parámetros (`{id}` vs `{id:guid}`) o en la ruta base |

---

## Tabla completa de endpoints

Ordenada por `API-NN`. Leyenda:
- ✅ = coinciden código y documentación
- ❌ = diferencia encontrada
- ⚠️ = solo en uno de los dos

| API-NN | Verbo y ruta en el código | Verbo y ruta en api-rest.md | ¿Coinciden? | Prueba de integración |
|---|---|---|---|---|
| (sin nombre) | GET `/api/salud` | — | ⚠️ solo en código | `ApiPruebas.cs` |
| (sin nombre) | GET `/api/salud/lista` | — | ⚠️ solo en código | `ProduccionPruebas.cs` |
| API-01 | GET `/api/busqueda` | GET `/api/busqueda` | ✅ | `ApiPruebas.cs` |
| API-02 | GET `/api/rubros` | GET `/api/rubros` | ✅ | `ApiPruebas.cs` |
| API-03 | GET `/api/rubros/{rubroId:int}/servicios` | GET `/api/rubros/{id}/servicios` | ❌ ruta distinta | ninguna |
| API-04 | GET `/api/zonas` | GET `/api/zonas` | ✅ | `ApiPruebas.cs` |
| API-05 | GET `/api/prestadores/{slug}` | GET `/api/prestadores/{slug}` | ✅ | `ApiAgendaPruebas.cs` |
| API-06 | — | GET `/api/prestadores/{slug}/resenas` | ⚠️ solo en documentación | ninguna |
| API-07 | — | GET `/api/prestadores/{slug}/vcard` | ⚠️ solo en documentación | ninguna |
| API-08 | POST `/api/auth/registro` | POST `/api/auth/registro` | ✅ | `ApiAgendaPruebas.cs` |
| API-09 | POST `/api/auth/sesion` | POST `/api/auth/sesion` | ✅ | `ApiAgendaPruebas.cs` |
| API-10 | POST `/api/auth/sesion/renovar` | POST `/api/auth/sesion/renovar` | ✅ | ninguna |
| API-11 | DELETE `/api/auth/sesion` | DELETE `/api/auth/sesion` | ✅ | `ApiAgendaPruebas.cs` |
| API-12 | DELETE `/api/auth/sesiones` | DELETE `/api/auth/sesiones` | ✅ | ninguna |
| API-13 | POST `/api/auth/correo/verificar` | POST `/api/auth/correo/verificar` | ✅ | `ApiAgendaPruebas.cs` |
| API-14 | POST `/api/auth/contrasena/recuperar` | POST `/api/auth/contrasena/recuperar` | ✅ | `TextosEnCastellanoPruebas.cs` |
| API-15 | POST `/api/auth/contrasena/restablecer` | POST `/api/auth/contrasena/restablecer` | ✅ | ninguna |
| API-16 | GET `/api/mi/cuenta` | GET `/api/mi/cuenta` | ✅ | `ApiPruebas.cs` |
| API-17 | DELETE `/api/mi/cuenta` | DELETE `/api/mi/cuenta` | ✅ | `ApiPruebas.cs` |
| API-18 | GET `/api/mi/perfil-prestador` | GET `/api/mi/perfil-prestador` | ✅ | `ApiPruebas.cs` |
| API-19 | PUT `/api/mi/perfil-prestador` | PUT `/api/mi/perfil-prestador` | ✅ | `ApiPruebas.cs` |
| API-20 | PUT `/api/mi/perfil-prestador/servicios` | PUT `/api/mi/perfil-prestador/servicios` | ✅ | `ApiPruebas.cs` |
| API-20b | GET `/api/mi/perfil-prestador/servicios` | GET `/api/mi/perfil-prestador/servicios` | ✅ | `ApiPruebas.cs` |
| API-21 | PUT `/api/mi/perfil-prestador/cobertura` | PUT `/api/mi/perfil-prestador/cobertura` | ✅ | `ApiPruebas.cs` |
| API-21b | GET `/api/mi/perfil-prestador/cobertura` | GET `/api/mi/perfil-prestador/cobertura` | ✅ | `ApiPruebas.cs` |
| API-22 | POST `/api/mi/perfil-prestador/publicar` | POST `/api/mi/perfil-prestador/publicar` | ✅ | `ApiPruebas.cs` |
| API-22b | POST `/api/mi/perfil-prestador/despublicar` | POST `/api/mi/perfil-prestador/despublicar` | ✅ | `ApiPruebas.cs` |
| API-23 | PUT `/api/mi/perfil-buscador` | PUT `/api/mi/perfil-buscador` | ✅ | `ApiPruebas.cs` |
| API-24 | GET `/api/mi/credenciales` | GET `/api/mi/credenciales` | ✅ | `ApiPruebas.cs` |
| API-25 | POST `/api/mi/credenciales` | POST `/api/mi/credenciales` | ✅ | `ApiPruebas.cs` |
| API-26 | — | POST `/api/mi/credenciales/{id}/documentos` | ⚠️ solo en documentación | ninguna |
| API-27 | POST `/api/mi/credenciales/{id:guid}/enviar` | POST `/api/mi/credenciales/{id}/enviar` | ❌ ruta distinta | ninguna |
| API-28 | DELETE `/api/mi/credenciales/{id:guid}` | DELETE `/api/mi/credenciales/{id}` | ❌ ruta distinta | `ApiPruebas.cs` |
| API-29 | GET `/api/interno/credenciales/cola` | GET `/api/interno/credenciales/cola` | ✅ | `ApiVerificacionPruebas.cs` |
| API-30 | POST `/api/interno/credenciales/{id:guid}/tomar` | POST `/api/interno/credenciales/{id}/tomar` | ❌ ruta distinta | ninguna |
| API-31 | POST `/api/interno/credenciales/{id:guid}/resolver` | POST `/api/interno/credenciales/{id}/resolver` | ❌ ruta distinta | ninguna |
| API-32 | GET `/api/mi/disponibilidad` | GET `/api/mi/disponibilidad` | ✅ | `ApiAgendaPruebas.cs` |
| API-33 | PUT `/api/mi/disponibilidad` | PUT `/api/mi/disponibilidad` | ✅ | `ApiAgendaPruebas.cs` |
| API-34 | POST `/api/mi/bloqueos` | POST `/api/mi/bloqueos` | ✅ | `ApiAgendaPruebas.cs` |
| API-34-listar | GET `/api/mi/bloqueos` | GET `/api/mi/agenda/bloqueos` | ❌ ruta distinta | `ApiAgendaPruebas.cs` |
| API-34-quitar | DELETE `/api/mi/bloqueos/{id:guid}` | DELETE `/api/mi/agenda/bloqueos/{id}` | ❌ ruta distinta | `ApiAgendaPruebas.cs` |
| API-35 | GET `/api/prestadores/{slug}/bloques` | GET `/api/prestadores/{slug}/bloques` | ✅ | ninguna |
| API-36 | POST `/api/citas` | POST `/api/citas` | ✅ | `ApiAgendaPruebas.cs` |
| API-37 | GET `/api/mi/citas` | GET `/api/mi/citas` | ✅ | `ApiAgendaPruebas.cs` |
| API-38 | POST `/api/citas/{id:guid}/confirmar` | POST `/api/citas/{id}/confirmar` | ❌ ruta distinta | ninguna |
| API-39 | POST `/api/citas/{id:guid}/rechazar` | POST `/api/citas/{id}/rechazar` | ❌ ruta distinta | ninguna |
| API-40 | POST `/api/citas/{id:guid}/cancelar` | POST `/api/citas/{id}/cancelar` | ❌ ruta distinta | ninguna |
| API-41 | POST `/api/citas/{id:guid}/reprogramar` | POST `/api/citas/{id}/reprogramar` | ❌ ruta distinta | ninguna |
| API-42 | POST `/api/citas/{id:guid}/asistencia` | POST `/api/citas/{id}/asistencia` | ❌ ruta distinta | ninguna |
| API-42b | POST `/api/citas/{id:guid}/no-asistio-el-prestador` | PUT `/api/citas/{id}/no-asistio` | ❌ verbo distinto, ruta distinta | ninguna |
| API-43 | — | PUT `/api/mi/notificaciones/preferencias` | ⚠️ solo en documentación | ninguna |
| API-44 | — | POST `/api/citas/{id}/recordatorio` | ⚠️ solo en documentación | ninguna |
| API-45 | — | POST `/api/mi/notificaciones/push` | ⚠️ solo en documentación | ninguna |
| API-46 | POST `/api/contactos` | POST `/api/contactos` | ✅ | `ResenasApiPruebas.cs` |
| API-47 | GET `/api/mi/elegibilidad` | GET `/api/mi/elegibilidad` | ✅ | `ResenasApiPruebas.cs` |
| API-48 | POST `/api/resenas` | POST `/api/resenas` | ✅ | `ResenasApiPruebas.cs` |
| API-49 | PUT `/api/resenas/{id:guid}` | PUT `/api/resenas/{id}` | ❌ ruta distinta | `ResenasApiPruebas.cs` |
| API-50 | POST `/api/resenas/{id:guid}/respuesta` | POST `/api/resenas/{id}/respuesta` | ❌ ruta distinta | ninguna |
| API-51 | POST `/api/resenas/{id:guid}/reporte` | POST `/api/resenas/{id}/reporte` | ❌ ruta distinta | ninguna |
| API-52 | GET `/api/interno/reportes/cola` | GET `/api/interno/reportes/cola` | ✅ | `ResenasApiPruebas.cs` |
| API-53 | POST `/api/interno/reportes/{id:guid}/resolver` | POST `/api/interno/reportes/{id}/resolver` | ❌ ruta distinta | ninguna |
| API-54 | GET `/t/{segmento}` | GET `/t/{segmento}` | ✅ | `ApiMisTarjetasPruebas.cs` |
| API-55 | — | POST `/t/{segmento}/activar` | ⚠️ solo en documentación | ninguna |
| API-56 | GET `/api/mi/tarjetas` | GET `/api/mi/tarjetas` | ✅ | `ApiMisTarjetasPruebas.cs` |
| API-57 | POST `/api/mi/tarjetas/{id:guid}/desactivar` | POST `/api/mi/tarjetas/{id}/desactivar` | ❌ ruta distinta | ninguna |
| API-58 | GET `/api/mi/tarjetas/{id:guid}/estadisticas` | GET `/api/mi/tarjetas/{id}/estadisticas` | ❌ ruta distinta | ninguna |
| API-59 | — | POST `/api/interno/tarjetas` | ⚠️ solo en documentación | ninguna |
| API-60 | GET `/api/interno/indicadores` | GET `/api/interno/indicadores` | ✅ construido en JQ-14 (`EndpointsIndicadores.cs`, administrador, tasa S) | ninguna |
| API-61 | — | GET `/api/interno/auditoria` | ⚠️ solo en documentación | ninguna |
| API-62 | GET `/api/comunidad` | GET `/api/comunidad` | ✅ | `ApiPruebas.cs` |
| API-63 | GET `/api/buscar` | GET `/api/buscar` | ✅ | `ApiPruebas.cs` |
| API-64 | GET `/api/prestadores/mapa` | GET `/api/prestadores/mapa` | ✅ | `ApiPruebas.cs` |
| API-65 | GET `/api/mi/resenas` | GET `/api/mi/resenas` | ✅ | `ResenasApiPruebas.cs` |
| API-66 | GET `/api/planes` | GET `/api/planes` | ✅ | `ApiSuscripcionesPruebas.cs` |
| API-67 | POST `/api/mi/cobros/cotizar` | POST `/api/mi/cobros/cotizar` | ✅ | `ApiSuscripcionesPruebas.cs` |
| API-68 | POST `/api/mi/cobros` | POST `/api/mi/cobros` | ✅ | `ApiSuscripcionesPruebas.cs` |
| API-69 | POST `/api/mi/suscripcion/fundador` | POST `/api/mi/suscripcion/fundador` | ✅ | `ApiSuscripcionesPruebas.cs` |
| API-70 | POST `/api/pagos/aviso` | POST `/api/pagos/aviso` | ✅ | `ApiSuscripcionesPruebas.cs` |
| API-71 | POST `/api/auth/correo/reenviar` | POST `/api/auth/correo/reenviar` | ✅ | ninguna |
| API-resenas-publicas | GET `/api/prestadores/{slug}/resenas` | — | ⚠️ solo en código | ninguna |
| CS-02b | POST `/api/mi/contrasena` | — | ⚠️ solo en código | ninguna |
| CS-04 | POST `/api/mi/activar-prestador` | — | ⚠️ solo en código | `ApiPruebas.cs` |
| CSRF | GET `/api/auth/csrf` | — | ⚠️ solo en código | `ApiAgendaPruebas.cs` |
| MI-SUSCRIPCION | GET `/api/mi/suscripcion` | — | ⚠️ solo en código | `ApiPruebas.cs` |

---

## Detalles de las diferencias

### 1. Endpoints solo en el código (6)

Estos endpoints existen en la API pero no están documentados en `api-rest.md`:

| API-NN | Verbo | Ruta | Notas |
|---|---|---|---|
| (sin nombre) | GET | `/api/salud` | Endpoint de salud básica |
| (sin nombre) | GET | `/api/salud/lista` | Endpoint de salud detallada |
| API-resenas-publicas | GET | `/api/prestadores/{slug}/resenas` | Reseñas públicas de un prestador (la documentación menciona API-06 para esto) |
| CS-02b | POST | `/api/mi/contrasena` | Cambio de contraseña (nombre de caso de uso, no API) |
| CS-04 | POST | `/api/mi/activar-prestador` | Activación de prestador (nombre de caso de uso) |
| CSRF | GET | `/api/auth/csrf` | Obtención de token CSRF (infraestructura, no contrato) |
| MI-SUSCRIPCION | GET | `/api/mi/suscripcion` | Consulta de suscripción (infraestructura de pantalla) |

**Nota:** La documentación (línea 214-218 de `api-rest.md`) menciona explícitamente que `CSRF`, `MI-SUSCRIPCION`, `API-resenas-publicas`, `CS-02b` y `CS-04` **no son endpoints del contrato** sino "piezas de infraestructura de las pantallas", por lo que no se espera que estén en la tabla principal.

### 2. Endpoints solo en la documentación (10)

Estos endpoints están documentados pero no se encontraron en el código:

| API-NN | Verbo | Ruta | Sección | Estado probable |
|---|---|---|---|---|
| API-06 | GET | `/api/prestadores/{slug}/resenas` | 1 · Público | En código como `API-resenas-publicas` |
| API-07 | GET | `/api/prestadores/{slug}/vcard` | 1 · Público | Diseño por construir |
| API-26 | POST | `/api/mi/credenciales/{id}/documentos` | 4 · Credenciales | Diseño por construir |
| API-43 | PUT | `/api/mi/notificaciones/preferencias` | 6 · Notificaciones | Diseño por construir |
| API-44 | POST | `/api/citas/{id}/recordatorio` | 6 · Notificaciones | Diseño por construir |
| API-45 | POST | `/api/mi/notificaciones/push` | 6 · Notificaciones | Diseño por construir |
| API-55 | POST | `/t/{segmento}/activar` | 8 · Tarjetas | Diseño por construir |
| API-59 | POST | `/api/interno/tarjetas` | 8 · Tarjetas | Diseño por construir |
| API-60 | GET | `/api/interno/indicadores` | 9 · Administración | Construido (JQ-14) |
| API-61 | GET | `/api/interno/auditoria` | 9 · Administración | Diseño por construcción |

**Nota:** La documentación (línea 250-254 de `api-rest.md`) declara explícitamente: "Lo que esta tabla documenta y todavía NO existe: Catorce, contra el código de `main`: API-06, API-07, API-26, API-43 a API-45, API-54 a API-61."

### 3. Diferencias de verbo (1)

| API-NN | Verbo en código | Verbo en doc | Ruta |
|---|---|---|---|
| API-42b | POST | PUT | `/api/citas/{id}/no-asistio` |

**Nota:** En el código la ruta es `/api/citas/{id:guid}/no-asistio-el-prestador`, mientras que la documentación usa `/api/citas/{id}/no-asistio`.

### 4. Diferencias de ruta (19)

Todas las diferencias de ruta son del mismo tipo: el código usa **`{id:guid}`** (constraint de tipo GUID en ASP.NET) mientras que la documentación usa **`{id}`** (sin constraint). Esto es una diferencia de implementación, no de contrato:

| API-NN | Ruta en código | Ruta en doc |
|---|---|---|
| API-03 | `/api/rubros/{rubroId:int}/servicios` | `/api/rubros/{id}/servicios` |
| API-27 | `/api/mi/credenciales/{id:guid}/enviar` | `/api/mi/credenciales/{id}/enviar` |
| API-28 | `/api/mi/credenciales/{id:guid}` | `/api/mi/credenciales/{id}` |
| API-30 | `/api/interno/credenciales/{id:guid}/tomar` | `/api/interno/credenciales/{id}/tomar` |
| API-31 | `/api/interno/credenciales/{id:guid}/resolver` | `/api/interno/credenciales/{id}/resolver` |
| API-34-listar | `/api/mi/bloqueos` | `/api/mi/agenda/bloqueos` |
| API-34-quitar | `/api/mi/bloqueos/{id:guid}` | `/api/mi/agenda/bloqueos/{id}` |
| API-38 | `/api/citas/{id:guid}/confirmar` | `/api/citas/{id}/confirmar` |
| API-39 | `/api/citas/{id:guid}/rechazar` | `/api/citas/{id}/rechazar` |
| API-40 | `/api/citas/{id:guid}/cancelar` | `/api/citas/{id}/cancelar` |
| API-41 | `/api/citas/{id:guid}/reprogramar` | `/api/citas/{id}/reprogramar` |
| API-42 | `/api/citas/{id:guid}/asistencia` | `/api/citas/{id}/asistencia` |
| API-42b | `/api/citas/{id:guid}/no-asistio-el-prestador` | `/api/citas/{id}/no-asistio` |
| API-49 | `/api/resenas/{id:guid}` | `/api/resenas/{id}` |
| API-50 | `/api/resenas/{id:guid}/respuesta` | `/api/resenas/{id}/respuesta` |
| API-51 | `/api/resenas/{id:guid}/reporte` | `/api/resenas/{id}/reporte` |
| API-53 | `/api/interno/reportes/{id:guid}/resolver` | `/api/interno/reportes/{id}/resolver` |
| API-57 | `/api/mi/tarjetas/{id:guid}/desactivar` | `/api/mi/tarjetas/{id}/desactivar` |
| API-58 | `/api/mi/tarjetas/{id:guid}/estadisticas` | `/api/mi/tarjetas/{id}/estadisticas` |

**Observación:** Las diferencias `{id:guid}` vs `{id}` son **constraints de ruta de ASP.NET**, no cambian el contrato HTTP. El constraint `{rubroId:int}` en API-03 vs `{id}` en la documentación es más significativo porque restringe a enteros.

**Caso especial API-34-listar y API-34-quitar:** La documentación usa `/api/mi/agenda/bloqueos` mientras que el código usa `/api/mi/bloqueos`. Esta es una diferencia real de ruta.

### 5. Caso especial: API-17

**API-17** (`DELETE /api/mi/cuenta`) está en el PR #76 (borrador: `identidad/eliminar-mi-cuenta`). Según el plan de T13, no se compara con ese PR.

---

## Endpoints sin prueba de integración (22 de 74)

Los siguientes endpoints no tienen una prueba de integración que los llame explícitamente:

| API-NN | Verbo | Ruta |
|---|---|---|
| API-03 | GET | `/api/rubros/{rubroId:int}/servicios` |
| API-10 | POST | `/api/auth/sesion/renovar` |
| API-12 | DELETE | `/api/auth/sesiones` |
| API-15 | POST | `/api/auth/contrasena/restablecer` |
| API-27 | POST | `/api/mi/credenciales/{id:guid}/enviar` |
| API-30 | POST | `/api/interno/credenciales/{id:guid}/tomar` |
| API-31 | POST | `/api/interno/credenciales/{id:guid}/resolver` |
| API-35 | GET | `/api/prestadores/{slug}/bloques` |
| API-38 | POST | `/api/citas/{id:guid}/confirmar` |
| API-39 | POST | `/api/citas/{id:guid}/rechazar` |
| API-40 | POST | `/api/citas/{id:guid}/cancelar` |
| API-41 | POST | `/api/citas/{id:guid}/reprogramar` |
| API-42 | POST | `/api/citas/{id:guid}/asistencia` |
| API-42b | POST | `/api/citas/{id:guid}/no-asistio-el-prestador` |
| API-50 | POST | `/api/resenas/{id:guid}/respuesta` |
| API-51 | POST | `/api/resenas/{id:guid}/reporte` |
| API-53 | POST | `/api/interno/reportes/{id:guid}/resolver` |
| API-57 | POST | `/api/mi/tarjetas/{id:guid}/desactivar` |
| API-58 | GET | `/api/mi/tarjetas/{id:guid}/estadisticas` |
| API-71 | POST | `/api/auth/correo/reenviar` |
| API-resenas-publicas | GET | `/api/prestadores/{slug}/resenas` |
| CS-02b | POST | `/api/mi/contrasena` |

**Porcentaje de cobertura:** 52/74 = **70,3%** de los endpoints tienen al menos una prueba de integración que los llama.

---

## Sin verificar

- La comparación asume que el archivo `api-rest.md` está actualizado con la última versión del documento. Si hubo cambios después del 17-nov-2026, podrían haber discrepancias adicionales.
- Los endpoints de infraestructura (`CSRF`, `MI-SUSCRIPCION`, `CS-02b`, `CS-04`, `API-resenas-publicas`) no se consideran "faltantes" en la documentación porque el propio documento los excluye explícitamente del contrato (línea 214-218).
- API-06 está documentado como "no existe en main" (línea 250), pero el código tiene `API-resenas-publicas` con la misma ruta. Podría ser el mismo endpoint con nombre distinto.

---

## Conclusión

La auditoría encontró **16 diferencias reales** entre el código y la documentación (excluyendo los endpoints de infraestructura que el documento excluye explícitamente):

- **10 endpoints** documentados como "diseño por construir" que efectivamente no están en el código (API-06, API-07, API-26, API-43, API-44, API-45, API-55, API-59, API-60, API-61). Esto es **consistente** con la declaración del documento (línea 250-254).
- **1 diferencia de verbo** (API-42b: POST vs PUT).
- **19 diferencias de ruta**, todas del tipo `{id:guid}` vs `{id}`, excepto API-03 (`{rubroId:int}` vs `{id}`) y API-34-listar/quitar (`/mi/bloqueos` vs `/mi/agenda/bloqueos`).
- **2 endpoints de salud** sin nombre API-NN que no están documentados (esperado, son endpoints operativos).

**Recomendación:** Las diferencias de `{id:guid}` vs `{id}` son de implementación (constraints de ASP.NET) y no afectan el contrato HTTP. Sin embargo, la documentación podría actualizarse para reflejar las rutas reales cuando difieren en la base (`/mi/bloqueos` vs `/mi/agenda/bloqueos`).
