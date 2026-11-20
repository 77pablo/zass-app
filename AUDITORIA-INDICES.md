# Auditoría de índices de la base

Medido el 14-nov-2026 contra PostgreSQL **16.4** real (`127.0.0.1:5433`, base `zass`), consultando
`pg_indexes`, `pg_constraint`, `pg_index`, `pg_stat_user_indexes` e `information_schema`. No se leyó
el modelo de EF para decidir nada: el catálogo manda.

**Índices 1 y 3 ya aplicados en Neon (15-nov).** Índice 2 en migración `IDX_Bloques_DisponibilidadEstadoInicio` (17-nov), sin aplicar en Neon. Índice 6 (3.c) en migración `IndiceNotificacionCita` (#59), **aplicado en Neon** (corrida `44090594566`). Índices 4 y 5 no se aplican (ver abajo).
El SQL de abajo es una propuesta para que Pablo decida, una migración a la vez.

Fotografía del clúster: 34 tablas del producto, **41 claves foráneas**, **106 índices** (≈2 MB en total).

---

> **Al día 17-nov-2026:** los dos índices de la sección 1 **ya están aplicados en producción**
> (migración `20261114204429_IndicesDeClavesForaneas`, aplicada en Neon el 15-nov).
> Índice 2 (`ix_bloque_disponibilidad_estado_inicio`) en migración `IDX_Bloques_DisponibilidadEstadoInicio`, **sin aplicar en Neon**.
> Índice 5 (`i_x_servicios_rubro_id`): no se quita, exige trucos (quitar FK y reconfigurar índice único); coste, no riesgo.
> Índice 4 no se hace: producción tiene 0 prestadores y la auditoría dice volver a medir con datos reales.
> En la sección 3, **3.c (índice 6) ya está aplicado en Neon** (#59, corrida `44090594566`).

## Lo que más duele, en orden

| # | Qué | Dónde | Por qué duele con 50 prestadores |
|---|---|---|---|
| 1 | FK sin índice | `citas.bloque_horario_id` → `bloques` | Es `ON DELETE RESTRICT` y el repositorio borra **cientos de bloques en una sola sentencia**. Un recorrido completo de `citas` **por cada fila borrada**. |
| 2 | Consulta sin índice | `bloques.disponibilidad_id` | La misma sentencia de borrado filtra por esta columna y no hay ningún índice que empiece por ella. `bloques` es ya hoy la tabla con más índice del clúster. |
| 3 | FK sin índice (y lectura caliente) | `suscripciones.perfil_prestador_id` → `perfiles_prestador` | Recorrido completo en cada `UltimaDePrestadorAsync`, que es la consulta de "¿este prestador está al día?". |
| 4 | Trabajo de fondo sin índice propio | `citas (estado, expira_en)` | A-09 corre en bucle y hoy lee **todas** las citas abiertas y las ordena en memoria. |
| 5 | Índice redundante | `i_x_servicios_rubro_id` | Prefijo exacto de otro índice. Coste, no riesgo. |
| 6 | 98 índices con `idx_scan = 0` | Todo el clúster | **No concluyente**: la base ha tenido 115 transacciones en su vida. Ver el apartado final. |

---

## 1. Claves foráneas sin índice

En PostgreSQL un `FOREIGN KEY` **no** crea índice en la tabla hija. Cada borrado o actualización de
la clave del padre dispara una sonda `SELECT 1 FROM hija WHERE fk = $1 FOR KEY SHARE`, y sin índice
esa sonda es un recorrido completo.

La consulta de catálogo marca **3** FK sin índice de las 41. Mirándolas una a una con el planificador,
solo **2** lo están de verdad:

| Tabla hija | Columna | Padre | Al borrar | ¿Hay índice usable? |
|---|---|---|---|---|
| `citas` | `bloque_horario_id` | `bloques` | `RESTRICT` | **No** |
| `suscripciones` | `perfil_prestador_id` | `perfiles_prestador` | `CASCADE` | **No** |
| `cupos_fundador` | `perfil_prestador_id` | `perfiles_prestador` | `SET NULL` | **Sí** — falso positivo, ver abajo |

### Por qué 3 en el catálogo y 2 de verdad

Las tres tienen un índice **parcial** encima de la columna, y el catálogo los descarta en bloque. Pero
el planificador sí usa uno de ellos. La prueba es poner `enable_seqscan = off` y ver qué elige: si
aun así recorre la tabla entera, es que **no existe ningún índice capaz** de responder.

```
-- citas.bloque_horario_id, con enable_seqscan = off
 LockRows  (cost=10000000000.00..10000000011.01 rows=1 width=10)
   ->  Seq Scan on citas x
         Filter: (bloque_horario_id = '...'::uuid)

-- suscripciones.perfil_prestador_id, con enable_seqscan = off
 LockRows  (cost=10000000000.00..10000000001.11 rows=1 width=10)
   ->  Seq Scan on suscripciones x
         Filter: (perfil_prestador_id = '...'::uuid)

-- cupos_fundador.perfil_prestador_id, con enable_seqscan = off
 LockRows  (cost=0.12..8.15 rows=1 width=10)
   ->  Index Scan using ux_cupo_fundador_perfil on cupos_fundador x
         Index Cond: (perfil_prestador_id = '...'::uuid)
```

El coste de 10.000 millones es la penalización que mete `enable_seqscan = off`: que el plan siga
siendo `Seq Scan` con ese castigo encima significa que no había alternativa.

La diferencia está en el predicado del índice parcial:

| Índice parcial | Predicado | ¿Sirve a la sonda de la FK? |
|---|---|---|
| `ux_cupo_fundador_perfil` | `WHERE perfil_prestador_id IS NOT NULL` | **Sí**: `col = $1` implica `col IS NOT NULL`, y el planificador lo deduce. |
| `ux_cita_bloque_activo` | `WHERE estado IN ('Solicitada','Confirmada')` | No: la sonda no filtra por estado y las citas canceladas o terminadas quedan fuera del índice. |
| `ux_suscripcion_vigente` | `WHERE estado IN ('Prueba','Activa')` | No, por lo mismo. |

**`cupos_fundador` no necesita nada.** Queda documentado para que nadie vuelva a "arreglarlo".

### El número que decide el orden

`citas.bloque_horario_id` es la peor de largo por tres cosas que se suman:

1. La FK es `ON DELETE RESTRICT`, así que la sonda corre **siempre**, en cada borrado de `bloques`.
2. `RepositorioAgenda.QuitarBloquesLibresDeAsync` borra en **una sola sentencia** todos los bloques
   libres de una disponibilidad. El propio comentario del método dice «son hasta **cientos** de filas».
   La sonda de integridad no se agrupa: corre **una vez por fila borrada**.
3. `citas` es la tabla que crece con el tráfico, no con el catálogo.

Con 50 prestadores y, digamos, 10.000 citas acumuladas, **un** prestador que edita su disponibilidad
dispara `200 × 10.000 = 2.000.000` de filas leídas, bajo el bloqueo de la transacción. Con el índice,
son 200 búsquedas de índice. Hoy no se nota porque `citas` tiene 0 filas.

### SQL propuesto

```sql
-- 1. La que más duele.
CREATE INDEX CONCURRENTLY ix_citas_bloque_horario_id
    ON public.citas (bloque_horario_id);

-- 3. FK en cascada y, además, la lectura de UltimaDePrestadorAsync.
--    Con inicia_en DESC de segunda columna, el ORDER BY ... LIMIT 1 sale del índice sin ordenar.
CREATE INDEX CONCURRENTLY ix_suscripciones_perfil_prestador_id
    ON public.suscripciones (perfil_prestador_id, inicia_en DESC);
```

`CONCURRENTLY` no bloquea escrituras, pero **no puede ir dentro de una migración de EF** (no corre en
transacción). Si se hacen por migración, van sin `CONCURRENTLY`: con los volúmenes de hoy el bloqueo
es de milisegundos. Esa decisión es de Pablo.

Coste: dos índices btree sobre `uuid`. Hoy 8 kB cada uno; con 10.000 citas, menos de 1 MB.

---

## 2. Índices duplicados o redundantes

Un solo caso en las 34 tablas, y es exacto: mismo orden, mismas columnas, ninguno parcial.

| Tabla | Índice redundante | Tamaño | Lo cubre | Nota |
|---|---|---|---|---|
| `servicios` | `i_x_servicios_rubro_id` (`rubro_id`) | 16 kB | `ux_servicio_rubro_nombre` (`rubro_id`, `nombre_normalizado`) | El índice que lo cubre es `UNIQUE`, y sirve igual a la FK `servicios.rubro_id → rubros`. |

```sql
-- Propuesto, no aplicado.
DROP INDEX CONCURRENTLY public.i_x_servicios_rubro_id;
```

Ahorra 16 kB y, sobre todo, una escritura de índice por cada alta de servicio. Es lo de menos de este
documento: va aquí por completitud, no por urgencia.

Pares revisados y **descartados** a propósito, para que no se borren por parecerse:

| Par | Por qué se quedan los dos |
|---|---|
| `coberturas (perfil, zona)` y `coberturas (zona, perfil)` | Órdenes inversos: uno responde "zonas de un prestador", el otro "prestadores de una zona". Es la consulta de la búsqueda. |
| `perfiles_prestador (rubro_id)` y `(estado, rubro_id)` | `rubro_id` no es prefijo de `(estado, rubro_id)`. |
| `contactos (perfil)` y `(buscador, perfil, ocurrido_en)` | Distinta columna de cabecera. |
| `ux_cobertura_principal`, `ux_cita_bloque_activo`, `ux_suscripcion_vigente` | Parciales con predicado propio: hacen de regla de negocio, no de atajo de lectura. |

---

## 3. Consultas del producto que recorren la tabla entera

### 3.a `bloques.disponibilidad_id` — sin índice y sin FK

`RepositorioAgenda.QuitarBloquesLibresDeAsync` filtra por `disponibilidad_id`, y es la **única**
columna `_id` de `bloques` sin índice. Con `enable_seqscan = off`, lo mejor que encuentra el
planificador es recorrer otro índice entero y filtrar a mano:

```
 Index Scan using i_x_bloques_prestador_servicio_id_inicio_en_estado on bloques
   Index Cond: ((inicio_en >= now()) AND (estado = 'Libre'))
   Filter: (disponibilidad_id = '...'::uuid)     <-- a mano, fila por fila
```

`disponibilidad_id` es `NOT NULL` y no tiene `FOREIGN KEY` declarada. Aquí solo se propone el índice;
si la FK debería existir, es otra conversación y otro nivel de revisión.

```sql
CREATE INDEX CONCURRENTLY ix_bloques_disponibilidad_id_estado
    ON public.bloques (disponibilidad_id, estado, inicio_en);
```

Deja el borrado en un único recorrido de índice acotado, en vez de uno por disponibilidad editada.

### 3.b A-09, el trabajo que expira citas

`RepositorioAgenda.CitasPorExpirarAsync` corre en bucle. Su plan hoy:

```
 Limit -> Sort (Sort Key: expira_en)
   -> Bitmap Heap Scan on citas
        Recheck Cond: (estado = ANY ('{Solicitada,Confirmada}'))
        Filter: ((expira_en IS NOT NULL) AND (estado = 'Solicitada') AND (expira_en <= now()))
        -> Bitmap Index Scan on ux_cita_bloque_activo
```

No es un recorrido de tabla, pero tampoco está acotado: se apoya en el índice parcial que cubre
**todas** las citas abiertas, las trae, las filtra y **las ordena en memoria**, cada vuelta del bucle.
Crece con el número de citas vivas, que es justo lo que crece en el lanzamiento.

```sql
CREATE INDEX CONCURRENTLY ix_citas_expira_en_solicitadas
    ON public.citas (expira_en)
    WHERE estado = 'Solicitada' AND expira_en IS NOT NULL;
```

Con él, el plan es un recorrido de índice ya ordenado con `LIMIT`: lee tantas filas como va a devolver
y desaparece el `Sort`. Es el único de los cuatro que **no** se puede verificar aquí: con `citas` a 0
filas el planificador no tiene con qué elegir. Antes de darlo por bueno hay que volver a medirlo con
datos reales.

---

### 3.c `notificaciones.cita_id` — lo que trajeron los recordatorios de cita

Lo añade **JQ-3** (PR #44, los recordatorios a 24 h y 2 h), y son **dos** caminos nuevos que
filtran por esa columna:

| Dónde | Cuándo corre | Qué hace |
|---|---|---|
| `CitasPorRecordarAsync` | cada pasada del planificador A-09 | un `NOT EXISTS` sobre `notificaciones` **por cada cita candidata**, para no encolar dos veces el mismo recordatorio |
| `AnularRecordatoriosAsync` | **cada** cancelación y **cada** reprogramación | apaga los recordatorios pendientes de esa cita |

Medido el 15-nov-2026 con `enable_seqscan = off`, que es lo que demuestra la ausencia de índice
usable aunque la tabla esté casi vacía:

```
-- notificaciones.cita_id
Seq Scan on notificaciones  (cost=10000000000.00..10000000002.05 rows=1 width=4)
  Filter: (cita_id = '...'::uuid)

-- y con el tipo, que es como lo consulta el NOT EXISTS
Seq Scan on notificaciones  (cost=10000000000.00..10000000002.06 rows=1 width=4)
  Filter: ((cita_id = '...'::uuid) AND ((tipo)::text = 'Recordatorio24h'::text))
```

La penalización de 10.000 millones encima y sigue en `Seq Scan`: **no hay índice alternativo**.
Los cuatro que tiene la tabla son la clave primaria, `clave_idempotencia` (único),
`(destinatario_id, enviada_en)` y `(estado, programada_para)`; ninguno empieza por `cita_id`.

Hoy no se nota: producción tiene **0 prestadores publicados** y la tabla está casi vacía. Con
tráfico real duele en el sitio peor, que es la cancelación: quien cancela una hora espera que la
pantalla responda, y esa espera incluye un recorrido completo de la tabla de notificaciones, que
crece con **cada** correo que el sistema manda, no solo con los recordatorios.

```sql
-- Propuesto, NO aplicado. El tipo va de segunda columna porque el NOT EXISTS pregunta por los
-- dos, y la anulación se conforma con la primera.
CREATE INDEX CONCURRENTLY ix_notificacion_cita_tipo
    ON public.notificaciones (cita_id, tipo);
```

> `cita_id` es **anulable** (la mayoría de las notificaciones no son de una cita: verificación de
> correo, recuperación, avisos de reseña). Un índice parcial `WHERE cita_id IS NOT NULL` sería más
> pequeño y el planificador lo usa igual para `cita_id = $1`, como ya se comprobó con
> `ux_cupo_fundador_perfil` en la sección 1. Decidirlo al aplicarlo, midiendo los dos.

### 3.d `citas.estado` — FALSO POSITIVO, y por qué no hay que «arreglarlo»

Al revisar JQ-3 anoté que la consulta de recordatorios filtra `estado = 'Confirmada'` y que no
había índice por `estado`. **Lo medí y es falso**, así que queda escrito para que nadie lo añada:

```
-- select 1 from citas where estado='Confirmada', con enable_seqscan = off
Bitmap Heap Scan on citas
  Filter: ((estado)::text = 'Confirmada'::text)
  ->  Bitmap Index Scan on ux_cita_bloque_activo
```

El planificador **sí** usa `ux_cita_bloque_activo`, que es R-1, el índice único parcial de la
doble reserva: su predicado es `estado IN ('Solicitada','Confirmada')`, y de ahí deduce que
`estado = 'Confirmada'` cae dentro. Es el mismo mecanismo que hizo de `cupos_fundador` un falso
positivo en la sección 1.

**Dos cosas que esto enseña, y valen más que el índice:** que una medición contradice una
intuición razonable —la mía, en una revisión de código—, y que **un índice puesto para garantizar
una regla termina sirviendo también para consultar**. Nadie planeó que R-1 acelerara esto.

---

## 4. Índices que nadie usa — y por qué este dato no sirve todavía

| Métrica | Valor |
|---|---|
| Índices con `idx_scan = 0` | **98 de 106** |
| Espacio que ocupan | 1992 kB |
| Transacciones en la vida de la base | **115** |
| `stats_reset` | nunca |

**No se recomienda borrar ni uno solo por este dato.** El cero no dice «este índice no sirve»: dice
«esta base no ha tenido tráfico». 115 transacciones en total, con `citas`, `bloques`, `resenas` y
`usuarios` prácticamente vacías. Los ocho índices que sí registran uso (`p_k_perfiles_prestador` 99,
`p_k_zonas` 67, `i_x_coberturas_zona_id_perfil_prestador_id` 20, `ux_suscripcion_vigente` 35…) son el
rastro de la carga de zonas y de unas pocas pruebas manuales, no de un patrón de uso.

Este apartado se vuelve a mirar **después** del lanzamiento, con `pg_stat_reset()` el día que se abra
y al menos un par de semanas de tráfico encima. Antes de eso, cualquier `DROP INDEX` por «no se usa»
sería borrar a ciegas.

El único índice que se propone borrar en este documento (`i_x_servicios_rubro_id`) **no** se propone
por tener `idx_scan = 0`, sino porque es prefijo exacto de otro: eso es una propiedad del catálogo y
no depende del tráfico.

---

## Resumen del SQL propuesto

| Orden | SQL | Qué arregla |
|---|---|---|
| ~~1~~ | ~~`CREATE INDEX ix_citas_bloque_horario_id …`~~ | **YA APLICADO** el 15-nov como `ix_cita_bloque_horario` |
| **2** | `CREATE INDEX ix_bloque_disponibilidad_estado_inicio ON bloques (disponibilidad_id, estado, inicio_en);` | El filtro del mismo borrado. **En migración `IDX_Bloques_DisponibilidadEstadoInicio` (17-nov), sin aplicar en Neon.** |
| ~~3~~ | ~~`CREATE INDEX ix_suscripciones_perfil_prestador_id …`~~ | **YA APLICADO** el 15-nov como `ix_suscripcion_perfil_inicia` |
| 4 | `CREATE INDEX ix_citas_expira_en_solicitadas ON citas (expira_en) WHERE estado = 'Solicitada' AND expira_en IS NOT NULL;` | A-09 sin ordenación en memoria. **Volver a medir con datos reales.** No se hace (producción tiene 0 prestadores). |
| 5 | `DROP INDEX i_x_servicios_rubro_id;` | Redundante por prefijo exacto. **No se quita**: exige quitar la FK `servicios.rubro_id → rubros` y reconfigurar el índice único `ux_servicio_rubro_nombre`; coste, no riesgo. |
| **6** | `CREATE INDEX ix_notificacion_cita_tipo ON notificaciones (cita_id, tipo) WHERE cita_id IS NOT NULL;` | 3.c: los dos caminos que trajo JQ-3, y el que duele es la cancelación. **En la migración `IndiceNotificacionCita` (#59), aplicado en Neon el 17-nov (corrida `44090594566`).** Se eligió el parcial: medido el 17-nov, `cita_id = $1` usa *Index Scan* y el `NOT EXISTS` (`cita_id` + `tipo`) *Index Only Scan* |

Todo esto es una sola migración si Pablo quiere, pero es **N3** y la decide él.

## Cómo se reproduce

Con la base local arriba y el `psql` de `herramientas\pgsql\bin`, sobre la base `zass`. Son todas
consultas de lectura; `enable_seqscan` se cambia solo en la sesión.

```sql
-- FK sin indice que las cubra por prefijo
WITH fk AS (
  SELECT c.conname, c.conrelid, c.conkey,
         c.conrelid::regclass::text  AS tabla_hija,
         c.confrelid::regclass::text AS tabla_padre
  FROM pg_constraint c
  JOIN pg_class t     ON t.oid = c.conrelid
  JOIN pg_namespace n ON n.oid = t.relnamespace
  WHERE c.contype = 'f' AND n.nspname = 'public'
)
SELECT tabla_hija, tabla_padre, conname FROM fk
WHERE NOT EXISTS (
  SELECT 1 FROM pg_index i
  WHERE i.indrelid = fk.conrelid AND i.indpred IS NULL
    AND ((string_to_array(i.indkey::text,' '))::int[])[1:array_length(fk.conkey,1)]
        = fk.conkey::int[]);

-- Y para cada candidata, la comprobacion que de verdad decide:
SET enable_seqscan = off;
EXPLAIN SELECT 1 FROM citas x WHERE x.bloque_horario_id = gen_random_uuid() FOR KEY SHARE OF x;
RESET enable_seqscan;
```
