# Auditoría de Tablas: Diccionario vs. Base de Datos

**Fecha:** 2026-11-18
**Autor:** IA-2
**Tarea:** T26 de PLAN-IA-2.md
**Fuente documentación:** `docs/03-datos/diccionario-de-tablas.md` (36 tablas, TBL-01 a TBL-36)
**Fuente base de datos:** Esquema Postgres extraído vía `information_schema`

---

## Resumen ejecutivo

| Métrica | Cantidad |
|---|---|
| Tablas documentadas | 36 |
| Tablas en base (excluyendo sistema) | 36 |
| Tablas con coincidencia exacta | 11 |
| Tablas con diferencias | 25 |
| Tablas solo en documentación | 0 |
| Tablas solo en base | 0 |

**Tablas excluidas de la base:** `__EFMigrationsHistory` (EF Core), `spatial_ref_sys` (PostGIS), `geography_columns` / `geometry_columns` (vistas PostGIS)

**Equivalencias aplicadas:**
- `varchar(n)` ≡ `character varying(n)` (representación Postgres)
- `timestamptz` ≡ `timestamp with time zone`
- `geography(Point,4326)` ≡ `USER-DEFINED` (PostGIS)
- `citext` ≡ `USER-DEFINED` (extensión)
- `time` ≡ `time without time zone`
- `text(n)` ≡ `varchar(n)` (notación del documento con límite implícito)

---

## Tablas sin diferencias (5)

| TBL | Doc | Base |
|---|---|---|
| TBL-03 | `usuario_rol` | `usuario_roles` |
| TBL-11 | `cobertura_prestador` | `coberturas` |
| TBL-16 | `disponibilidad` | `disponibilidades` |
| TBL-20 | `bloqueo_agenda` | `bloqueos_agenda` |
| TBL-33 | `cupos_fundador` | `cupos_fundador` |

---

## Tablas con diferencias

### TBL-01 · `usuario` → `usuarios`

| Columna | Doc | Base | Diferencia |
|---|---|---|---|
| `nombre` | `text` | `varchar(150)` | **tipo distinto** |

El documento declara `nombre` como `text` (sin límite). La base limita a 150 caracteres.

---

### TBL-02 · `rol` → `roles`

| Columna | Doc | Base | Diferencia |
|---|---|---|---|
| `codigo` | `text` | `varchar(30)` | **tipo distinto** |

---

### TBL-04 · `sesion` → `sesiones`

| Columna | Doc | Base | Diferencia |
|---|---|---|---|
| `ip_origen` | `inet` | `text` | **tipo distinto** |

El documento usa `inet` (tipo Postgres con validación de dirección IP). La base usa `text` (más flexible, pierde validación implícita del formato).

---

### TBL-05 · `perfil_prestador` → `perfiles_prestador`

| Columna | Doc | Base | Diferencia |
|---|---|---|---|
| `nombre_publico` | `text` | `varchar(150)` | **tipo distinto** |
| `estado` | `text` | `varchar(20)` | **tipo distinto** |
| `ubicacion_origen` | `text` | — | **columna solo en la documentación** |
| — | — | `slug` (`varchar(120)`, NO NULL) | **columna solo en la base** |
| — | — | `telefono_publico` (`text`, NULL) | **columna solo en la base** |
| — | — | `correo_publico` (`text`, NULL) | **columna solo en la base** |
| — | — | `whatsapp` (`text`, NULL) | **columna solo en la base** |
| — | — | `sitio_web` (`text`, NULL) | **columna solo en la base** |
| — | — | `latitud` (`double precision`, NULL) | **columna solo en la base** |
| — | — | `longitud` (`double precision`, NULL) | **columna solo en la base** |

- `ubicacion_origen` (doc) vs `origen_ubicacion` (base, `varchar(20)`) — mismo concepto, **nombre y tipo distintos**.
- `latitud` y `longitud` no aparecen como columnas en el documento, aunque el doc dice que `ubicacion_exacta` se genera "desde `latitud` y `longitud`". En la base son columnas reales.
- 7 columnas no documentadas: `slug`, `telefono_publico`, `correo_publico`, `whatsapp`, `sitio_web`, `latitud`, `longitud`.

---

### TBL-06 · `perfil_buscador` → `perfiles_buscador`

| Columna | Doc | Base | Diferencia |
|---|---|---|---|
| `canales_notificacion` | `text[]` | — | **columna solo en la documentación** |
| — | — | `canal_correo` (`boolean`, NO NULL) | **columna solo en la base** |
| — | — | `canal_push` (`boolean`, NO NULL) | **columna solo en la base** |
| — | — | `favoritos` (`jsonb`, NO NULL) | **columna solo en la base** |

Rediseño: `canales_notificacion` (array) fue reemplazado por columnas booleanas individuales + `favoritos`.

---

### TBL-07 · `rubro` → `rubros`

| Columna | Doc | Base | Diferencia |
|---|---|---|---|
| `codigo` | `text` | `varchar(40)` | **tipo distinto** |
| `nombre` | `text` | `varchar(80)` | **tipo distinto** |

---

### TBL-08 · `servicio` → `servicios`

| Columna | Doc | Base | Diferencia |
|---|---|---|---|
| `nombre` | `text` | `varchar(120)` | **tipo distinto** |
| `nombre_normalizado` | `text` | `varchar(160)` | **tipo distinto** |

---

### TBL-09 · `prestador_servicio` → `prestador_servicios`

| Columna | Doc | Base | Diferencia |
|---|---|---|---|
| `modo_agenda` | `text` | `varchar(20)` | **tipo distinto** |

---

### TBL-10 · `zona` → `zonas`

| Columna | Doc | Base | Diferencia |
|---|---|---|---|
| `nombre` | `text` | `varchar(120)` | **tipo distinto** |
| `sector` | `text` | `varchar(80)` | **tipo distinto** |
| `comuna` | `text` | `varchar(120)` | **tipo distinto** |
| — | — | `padre_id` (`integer`, NULL) | **columna solo en la base** |
| — | — | `region` (`varchar(80)`, NO NULL) | **columna solo en la base** |
| — | — | `tipo` (`varchar(20)`, NO NULL) | **columna solo en la base** |

---

### TBL-12 · `credencial` → `credenciales`

| Columna | Doc | Base | Diferencia |
|---|---|---|---|
| `tipo` | `text` | `varchar(30)` | **tipo distinto** |
| `nombre` | `text` | `varchar(150)` | **tipo distinto** |
| `estado` | `text` | `varchar(30)` | **tipo distinto** |
| — | — | `reservada_por` (`uuid`, NULL) | **columna solo en la base** |
| — | — | `reservada_hasta` (`timestamptz`, NULL) | **columna solo en la base** |
| — | — | `esperando_prestador` (`boolean`, NO NULL) | **columna solo en la base** |

---

### TBL-13 · `documento_credencial` → `documentos_credencial`

| Columna | Doc | Base | Diferencia |
|---|---|---|---|
| — | — | `nombre_original` (`text`, NO NULL) | **columna solo en la base** |

---

### TBL-14 · `entidad_certificadora` → `entidades_certificadoras`

| Columna | Doc | Base | Diferencia |
|---|---|---|---|
| `nombre` | `text` | `varchar(200)` | **tipo distinto** |

---

### TBL-15 · `verificacion` → `verificaciones`

| Columna | Doc | Base | Diferencia |
|---|---|---|---|
| `motivo` | `text` | `varchar(40)` | **tipo distinto** |

---

### TBL-17 · `bloque_horario` → `bloques`

| Columna | Doc | Base | Diferencia |
|---|---|---|---|
| `estado` | `text` | `varchar(20)` | **tipo distinto** |
| — | — | `prestador_servicio_id` (`uuid`, NO NULL) | **columna solo en la base** |

---

### TBL-18 · `cita` → `citas`

| Columna | Doc | Base | Diferencia |
|---|---|---|---|
| `codigo` | `text` | `varchar(12)` | **tipo distinto** |
| `estado` | `text` | `varchar(30)` | **tipo distinto** |
| `modo` | `text` | `varchar(20)` | **tipo distinto** |
| `nota_buscador` | `text` | `varchar(500)` | **tipo distinto** |
| — | — | `quien_falto` (`varchar(20)`, NO NULL) | **columna solo en la base** |

---

### TBL-19 · `cambio_cita` → `cambios_cita`

| Columna | Doc | Base | Diferencia |
|---|---|---|---|
| `tipo` | `text` | `varchar(30)` | **tipo distinto** |
| `estado_anterior` | `text` | `varchar(30)` | **tipo distinto** |
| `estado_nuevo` | `text` | `varchar(30)` | **tipo distinto** |

---

### TBL-21 · `contacto` → `contactos`

| Columna | Doc | Base | Diferencia |
|---|---|---|---|
| `origen` | `text` | `varchar(20)` | **tipo distinto** |
| `canal` | `text` | `varchar(20)` | **tipo distinto** |

---

### TBL-22 · `resena` → `resenas`

| Columna | Doc | Base | Diferencia |
|---|---|---|---|
| `comentario` | `text` | `varchar(1500)` | **tipo distinto** |
| `aspectos` | `text[]` | `ARRAY` | **tipo distinto** (equivalente — `ARRAY` es la representación Postgres de `text[]`) |
| `aspectos` | N (nullable) | NO | **nulo distinto** |

> **Nota:** `text[]` ≡ `ARRAY` es la misma representación. Marcar como "tipo distinto" pero equivalente.

---

### TBL-23 · `respuesta_resena` → `respuestas_resena`

| Columna | Doc | Base | Diferencia |
|---|---|---|---|
| `texto` | `text` | `varchar(1000)` | **tipo distinto** |

---

### TBL-24 · `reporte_resena` → `reportes_resena`

| Columna | Doc | Base | Diferencia |
|---|---|---|---|
| `motivo` | `text` | `varchar(40)` | **tipo distinto** |
| `estado` | `text` | `varchar(30)` | **tipo distinto** |

---

### TBL-25 · `tarjeta` → `tarjetas`

| Columna | Doc | Base | Diferencia |
|---|---|---|---|
| `url_estable` | `text` | `varchar(20)` | **tipo distinto** |
| `estado` | `text` | `varchar(20)` | **tipo distinto** |
| — | — | `bloqueada_hasta` (`timestamptz`, NULL) | **columna solo en la base** |

---

### TBL-26 · `escaneo_tarjeta` → `escaneos`

| Columna | Doc | Base | Diferencia |
|---|---|---|---|
| `medio` | `text` | `varchar(10)` | **tipo distinto** |

---

### TBL-27 · `notificacion` → `notificaciones`

| Columna | Doc | Base | Diferencia |
|---|---|---|---|
| `tipo` | `text` | `varchar(40)` | **tipo distinto** |
| `canal` | `text` | `varchar(20)` | **tipo distinto** |
| `clave_idempotencia` | `text` | `varchar(200)` | **tipo distinto** |
| `estado` | `text` | `varchar(20)` | **tipo distinto** |
| — | — | `asunto` (`varchar(200)`, NO NULL) | **columna solo en la base** |
| — | — | `cuerpo` (`text`, NO NULL) | **columna solo en la base** |
| — | — | `leida_en` (`timestamptz`, NULL) | **columna solo en la base** |

---

### TBL-28 · `registro_auditoria` → `auditoria`

| Columna | Doc | Base | Diferencia |
|---|---|---|---|
| *(nombre)* | `registro_auditoria` | `auditoria` | **nombre de tabla distinto** |
| `accion` | `text` | `varchar(80)` | **tipo distinto** |
| `entidad` | `text` | `varchar(60)` | **tipo distinto** |
| `ip_origen` | `inet` | `text` | **tipo distinto** |

Las columnas coinciden en estructura; solo nombre de tabla y tipos varían.

---

### TBL-29 · `planes` → `planes`

Sin diferencias. ✅
(Todos los tipos son `text(n)` en el doc, equivalente a `varchar(n)` en la base.)

---

### TBL-30 · `suscripciones` → `suscripciones`

Sin diferencias. ✅
(Todos los tipos son `text(n)` en el doc, equivalente a `varchar(n)` en la base.)

---

### TBL-31 · `pagos` → `pagos`

| Columna | Doc | Base | Diferencia |
|---|---|---|---|
| `referencia_pasarela` | `text` | `varchar(100)` | **tipo distinto** |
| — | — | `intento_de_pago_id` (`uuid`, NULL) | **columna solo en la base** |

---

### TBL-32 · `trabajos_de_fondo` → `trabajos_de_fondo`

Sin diferencias. ✅
(`text(60)` ≡ `varchar(60)`, `text(300)` ≡ `varchar(300)`)

---

### TBL-34 · `cupones` → `cupones`

Sin diferencias. ✅
(`text(30)` ≡ `varchar(30)`, `text(200)` ≡ `varchar(200)`)

---

### TBL-35 · `canjes_de_cupon` → `canjes_de_cupon`

Sin diferencias. ✅
(`text(30)` ≡ `varchar(30)`)

---

### TBL-36 · `intentos_de_pago` → `intentos_de_pago`

Sin diferencias. ✅
(Todos `text(n)` ≡ `varchar(n)` equivalentes.)

---

## Resumen de diferencias por categoría

### Columnas solo en la base (24)

| TBL | Columna | Observación |
|---|---|---|
| TBL-05 | `slug` | URL amigable del perfil |
| TBL-05 | `telefono_publico` | Contacto público |
| TBL-05 | `correo_publico` | Contacto público |
| TBL-05 | `whatsapp` | Contacto público |
| TBL-05 | `sitio_web` | Contacto público |
| TBL-05 | `latitud` | Coordenada (usada para `ubicacion_exacta`) |
| TBL-05 | `longitud` | Coordenada (usada para `ubicacion_exacta`) |
| TBL-06 | `canal_correo` | Notificaciones descompuestas |
| TBL-06 | `canal_push` | Notificaciones descompuestas |
| TBL-06 | `favoritos` | Preferencias del buscador |
| TBL-10 | `padre_id` | Jerarquía de zonas |
| TBL-10 | `region` | Clasificación regional |
| TBL-10 | `tipo` | Clasificación de zona |
| TBL-12 | `reservada_por` | Flujo de reserva |
| TBL-12 | `reservada_hasta` | Flujo de reserva |
| TBL-12 | `esperando_prestador` | Flujo de reserva |
| TBL-13 | `nombre_original` | Nombre del archivo subido |
| TBL-17 | `prestador_servicio_id` | Denormalización |
| TBL-18 | `quien_falto` | Registro de inasistencia |
| TBL-25 | `bloqueada_hasta` | Bloqueo temporal de tarjeta |
| TBL-27 | `asunto` | Contenido de notificación |
| TBL-27 | `cuerpo` | Contenido de notificación |
| TBL-27 | `leida_en` | Estado de lectura |
| TBL-31 | `intento_de_pago_id` | Vinculación con intentos |

### Columnas solo en la documentación (2)

| TBL | Columna | Observación |
|---|---|---|
| TBL-05 | `ubicacion_origen` | Existe en base como `origen_ubicacion` (nombre y tipo distintos) |
| TBL-06 | `canales_notificacion` | Reemplazada por `canal_correo` + `canal_push` en la base |

### Tipos distintos: `text` (ilimitado) vs `varchar(n)` (limitado)

Patrón predominante: el documento declara muchas columnas como `text` sin límite, mientras la base usa `varchar(n)` con límites razonables para el dominio.

| TBL | Columna | Doc | Base |
|---|---|---|---|
| TBL-01 | `nombre` | `text` | `varchar(150)` |
| TBL-02 | `codigo` | `text` | `varchar(30)` |
| TBL-05 | `nombre_publico` | `text` | `varchar(150)` |
| TBL-05 | `estado` | `text` | `varchar(20)` |
| TBL-07 | `codigo` | `text` | `varchar(40)` |
| TBL-07 | `nombre` | `text` | `varchar(80)` |
| TBL-08 | `nombre` | `text` | `varchar(120)` |
| TBL-08 | `nombre_normalizado` | `text` | `varchar(160)` |
| TBL-09 | `modo_agenda` | `text` | `varchar(20)` |
| TBL-10 | `nombre` | `text` | `varchar(120)` |
| TBL-10 | `sector` | `text` | `varchar(80)` |
| TBL-10 | `comuna` | `text` | `varchar(120)` |
| TBL-12 | `tipo` | `text` | `varchar(30)` |
| TBL-12 | `nombre` | `text` | `varchar(150)` |
| TBL-12 | `estado` | `text` | `varchar(30)` |
| TBL-15 | `motivo` | `text` | `varchar(40)` |
| TBL-17 | `estado` | `text` | `varchar(20)` |
| TBL-18 | `codigo` | `text` | `varchar(12)` |
| TBL-18 | `estado` | `text` | `varchar(30)` |
| TBL-18 | `modo` | `text` | `varchar(20)` |
| TBL-18 | `nota_buscador` | `text` | `varchar(500)` |
| TBL-19 | `tipo` | `text` | `varchar(30)` |
| TBL-19 | `estado_anterior` | `text` | `varchar(30)` |
| TBL-19 | `estado_nuevo` | `text` | `varchar(30)` |
| TBL-21 | `origen` | `text` | `varchar(20)` |
| TBL-21 | `canal` | `text` | `varchar(20)` |
| TBL-22 | `comentario` | `text` | `varchar(1500)` |
| TBL-23 | `texto` | `text` | `varchar(1000)` |
| TBL-24 | `motivo` | `text` | `varchar(40)` |
| TBL-24 | `estado` | `text` | `varchar(30)` |
| TBL-25 | `url_estable` | `text` | `varchar(20)` |
| TBL-25 | `estado` | `text` | `varchar(20)` |
| TBL-26 | `medio` | `text` | `varchar(10)` |
| TBL-27 | `tipo` | `text` | `varchar(40)` |
| TBL-27 | `canal` | `text` | `varchar(20)` |
| TBL-27 | `clave_idempotencia` | `text` | `varchar(200)` |
| TBL-27 | `estado` | `text` | `varchar(20)` |
| TBL-28 | `accion` | `text` | `varchar(80)` |
| TBL-28 | `entidad` | `text` | `varchar(60)` |
| TBL-31 | `referencia_pasarela` | `text` | `varchar(100)` |

### Tipos distintos: `inet` vs `text` (2)

| TBL | Columna | Doc | Base |
|---|---|---|---|
| TBL-04 | `ip_origen` | `inet` | `text` |
| TBL-28 | `ip_origen` | `inet` | `text` |

### Tipo equivalente con nombre distinto (1)

| TBL | Columna | Doc | Base |
|---|---|---|---|
| TBL-22 | `aspectos` | `text[]` | `ARRAY` |

`ARRAY` es la representación interna de Postgres para arrays de texto. Son equivalentes.

### Nombre de tabla distinto (1)

| TBL | Doc | Base |
|---|---|---|
| TBL-28 | `registro_auditoria` | `auditoria` |

---

## Observaciones generales

1. **Patrón `text` vs `varchar(n)`:** El patrón dominante es que el documento declara columnas como `text` (sin límite) mientras la base impone `varchar(n)` con límites acotados al dominio. En todos los casos, los largos son razonables (20 para enum/estados, 30-80 para identificadores, 150 para nombres, 500-1500 para textos). Esto sugiere que la base añadió restricciones explícitas que el documento no reflejó.

2. **Convención de nombres de tabla:** La base usa plural consistentemente (`roles`, `rubros`, `servicios`, `zonas`, `usuarios`, `sesiones`, etc.) mientras el documento usa singular. No se considera diferencia de sustancia. La excepción es TBL-28 (`registro_auditoria` → `auditoria`), que es una abreviación.

3. **`inet` vs `text` para IPs:** Dos tablas (`sesiones` y `auditoria`) tienen `ip_origen` como `text` en la base en lugar de `inet`. El tipo `inet` de Postgres validaría automáticamente el formato de dirección IP; `text` delega esa validación a la aplicación.

4. **Columnas no documentadas:** 24 columnas existen en la base sin estar en el diccionario. Los agrupamientos más significativos son: contacto público del prestador (TBL-05, 7 columnas), rediseño de notificaciones del buscador (TBL-06, 3 columnas), jerarquía de zonas (TBL-10, 3 columnas), y contenido de notificaciones (TBL-27, 3 columnas).

5. **`ubicacion_origen` → `origen_ubicacion`:** La documentación define `ubicacion_origen` como `text`, la base lo implementa como `origen_ubicacion` (`varchar(20)`). Mismo propósito, nombre y tipo distintos.

---

*Fin de la auditoría. Este documento es un reporte informativo. No se realizaron cambios en el código ni en la documentación.*
