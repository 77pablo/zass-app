# C1 · El cupo fundador, de punta a punta — especificación

**Fecha:** 13 de noviembre de 2026 · **Nivel:** N3 (pagos, seguridad, `Program.cs`) · **Aprobado
por Pablo** el 13-nov-2026 · **Carril:** Claude (ver `PLAN-JOAQUIN.md` §2).

## 1. Por qué existe esta especificación

El cupo fundador **no está enchufado a nada**. La lógica de aplicación está completa y su
concurrencia probada con 60 solicitantes a la vez, pero medido el 13-nov:

- `grep "Fundador" backend/src/Zass.Api/**/*.cs` no devuelve **ni una línea** de código fuente.
- No existe `EndpointsSuscripciones.cs`. Lo único que hay de S7 en la API es
  `GET /api/mi/suscripcion` (`EndpointsIdentidad.cs:293`).
- El frontend no menciona «fundador» ni «cupo» en ninguna pantalla.

O sea: **hoy nadie puede tomar un cupo**, y el lanzamiento del lunes 23 se apoya en ellos. No falta
un remate: falta el camino entero.

Y falta algo más grave para este proyecto, donde la documentación es la especificación: **el cobro
es la única parte del producto sin documento rector.** No existe `subsistema-7-suscripciones-y-pagos.md`,
no hay ninguna regla `RN-S7-`, y `api-rest.md` no numera ningún endpoint de suscripción. El máximo
identificador del documento hoy es **API-65**.

## 2. Lo que ya existe y no se vuelve a construir

| Pieza | Dónde | Estado |
|---|---|---|
| `CupoFundador` (TBL-33), 50 filas sembradas | `Dominio/Suscripciones/Entidades.cs` | hecho |
| Reservar, confirmar, liberar, contar disponibles | `Aplicacion/Suscripciones/ServicioSuscripciones.cs:299-355` | hecho |
| Índice único parcial que impide dos cupos por perfil | `ux_cupo_fundador_perfil` | hecho |
| Liberar reservas vencidas en el trabajo diario | `ServicioSuscripciones.cs:295` | hecho |
| Reserva de 25 min, tope 24 h | `DuracionReservaPorDefecto`, `DuracionReservaMaxima` | hecho |
| Plan `fundador` (id 3, $5.990, 50 cupos) | migraciones `PlanesDePagoSembrados`, `CuposFundadorA50` | hecho |
| 60 solicitantes simultáneos reparten 50 cupos sin repetir | `CuposFundadorIntegracionPruebas` | hecho, en verde |

**Nada de la capa de aplicación de cupos se toca.** Esta especificación la enchufa; no la reescribe.

## 3. Decisiones de Pablo, ya tomadas

1. **El cupo se confirma al inscribir el medio de pago**, con un cobro de $0 — que es exactamente
   lo que hace Oneclick de Transbank. No al tomarlo (los 50 se agotarían con gente que no piensa
   pagar) y no al primer pago real (durante un mes no sabríamos cuántos cupos son de verdad).
2. **El puerto tiene forma de redirección más aviso por detrás**, no de función que devuelve sí o
   no. Ninguna pasarela chilena cobra de forma síncrona: la persona se va al sitio del banco y la
   respuesta vuelve por el servidor. Un puerto síncrono obligaría a rehacer pantalla, flujo y
   pruebas cuando llegue la real.
3. **El cupo se ofrece en una página pública `/planes`** con el contador «quedan N de 50». El
   gancho del lanzamiento es un enlace que se pueda mandar por WhatsApp y donde se vea el contador
   bajando.
4. Decisiones anteriores que esta especificación respeta: primer mes gratis y después **$5.990
   congelado para siempre** (mensual normal $7.990) · la tarjeta NFC va aparte, a precio normal ·
   al llegar al cupo 50, **se cierra y punto** · el impago **desaparece** de la búsqueda (RN-S3-09)
   y su tarjeta abre la ficha con aviso pero sin contactar ni agendar (RN-S6-10).

## 4. Fuera de alcance, a propósito

- **El cupón de descuento** (necesita migración; va en C2, junto con el mes gratis general).
- **El cobro del día 31 y la gracia** (C3). `CobrarAsync` se **declara** aquí y se **usa** allá.
- **Cancelar la inscripción del medio de pago** desde el panel. Se anota como deuda en `ESTADO.md`.

**Riesgo con fecha, que Pablo aprobó sabiéndolo:** C1 no cobra. El mes gratis de quien tome un cupo
el lunes 23 termina el **23 de diciembre**; si C3 no existe para entonces, esas suscripciones
vencen y los fundadores desaparecen de la búsqueda. La fecha límite de C3 es el 23-dic-2026.

## 5. El puerto de pasarela

Vive en `Aplicacion/Pagos/` porque `Zass.Dominio` tiene **cero paquetes NuGet** y eso no se negocia.

```csharp
public interface IPasarelaDePagos
{
    /// Autoriza cobros futuros. NO cobra. Devuelve referencia y a donde mandar a la persona.
    Task<Resultado<InicioDeInscripcion>> IniciarInscripcionAsync(
        SolicitudDeInscripcion solicitud, CancellationToken ct = default);

    /// Lee un aviso ENTRANTE ya verificado. El adaptador verifica la firma; esto interpreta.
    Task<Resultado<ResultadoDeInscripcion>> LeerAvisoAsync(
        AvisoEntrante aviso, CancellationToken ct = default);

    /// Cobra contra un medio ya inscrito. Se declara en C1; se usa en C3.
    Task<Resultado<ResultadoDeCobro>> CobrarAsync(
        SolicitudDeCobro solicitud, CancellationToken ct = default);
}
```

Los registros que lo acompañan:

| Registro | Campos | Nota |
|---|---|---|
| `SolicitudDeInscripcion` | `SuscripcionId`, `PerfilPrestadorId`, `PlanId`, `UrlDeRetorno` | sin datos de tarjeta, nunca |
| `InicioDeInscripcion` | `ReferenciaExterna`, `UrlParaRedirigir` | la URL la decide el adaptador |
| `AvisoEntrante` | `Cuerpo`, `Firma`, `Cabeceras` | crudo; lo que llegó |
| `ResultadoDeInscripcion` | `ReferenciaExterna`, `Aceptada`, `Motivo?` | `Motivo` nunca llega a la pantalla tal cual |
| `SolicitudDeCobro` / `ResultadoDeCobro` | `SuscripcionId`, `Monto`, `Referencia` / `Aceptado`, `ReferenciaExterna`, `Motivo?` | declarados para C3 |

### 5.1 El adaptador simulado

`Infraestructura/Pagos/PasarelaSimulada.cs`:

- `IniciarInscripcionAsync` genera una referencia opaca (no secuencial, no adivinable) y devuelve
  `UrlParaRedirigir` apuntando a nuestra propia pantalla `/pago/inscripcion?ref=…`.
- Esa pantalla, al apretar el botón, **manda el aviso al endpoint de avisos firmado igual que lo
  haría una pasarela**, con HMAC sobre el cuerpo usando una clave de configuración
  (`Zass:Pagos:ClaveDeFirmaSimulada`). Firmar el falso no es teatro: es lo que hace que la
  verificación de firma esté ejercitada por las pruebas desde el primer día, en vez de escribirse
  por primera vez el día que entre dinero real.
- `CobrarAsync` acepta y registra, sin red.

### 5.2 La guarda de producción

**La pasarela simulada se niega a existir en Producción.** En `AgregarZass.cs`, si el entorno es
producción y el registro resuelto es `PasarelaSimulada`, la aplicación **no levanta**: lanza al
arrancar con un mensaje claro. Un cobro de mentira corriendo en producción sin que nadie lo note es
la clase de error que se descubre por el lado de la plata, y un `if` al arrancar lo hace imposible.

La prueba de arquitectura que lo vigila va con el resto de `Zass.Arquitectura.Pruebas`.

## 6. El flujo, paso por paso

1. **`/planes`** pide API-66 y muestra los dos planes y «quedan N de 50».
2. El prestador aprieta **«Quiero mi cupo»** → API-67:
   - exige sesión y CSRF; el perfil se saca **de la sesión**, nunca de un id del cuerpo;
   - reserva el cupo por **25 minutos** (`DuracionReservaPorDefecto`);
   - si ya no quedan, responde `409` y la pantalla muestra el plan fundador cerrado;
   - llama a `IniciarInscripcionAsync` y devuelve `{ numeroDeCupo, urlParaRedirigir }`.
3. La persona inscribe su medio de pago (pantalla propia con la simulada, sitio del banco con la
   real) y vuelve a **`/planes/listo`**, que **no confirma nada**: solo dice «estamos confirmando»
   y consulta el estado. Lo que confirma es el aviso del paso 4.
4. **API-68 recibe el aviso por detrás** → verifica firma → interpreta → si está aceptada:
   - `ConfirmarCupoFundadorAsync(perfilPrestadorId)`;
   - la suscripción queda en **plan fundador** con el **mes gratis**: estado `Prueba`,
     `VigenteHasta = ahora + 30 días`. `EstaVigente` ya cubre `Prueba`, así que el prestador
     aparece en la búsqueda desde ese segundo;
   - **si ya tenía una suscripción** —y casi siempre la va a tener, porque publicar la ficha
     arranca la prueba (API-22, `IniciarPruebaAsync`)— **no se crea otra**: se le cambia el plan a
     fundador y `VigenteHasta` queda en **la fecha más lejana entre la que ya tenía y ahora + 30
     días**. Ni se le quita tiempo que ya estaba prometido, ni se le regalan 30 días encima de los
     que le quedaban. Es `RN-S7-07`;
   - guarda la `ReferenciaPasarela` en la suscripción (ya existe la columna);
   - **todo en una transacción**: o se confirma el cupo y se arma la suscripción, o no pasa nada.
5. Si el aviso nunca llega, a los 25 minutos **el trabajo diario libera el cupo** —
   `LiberarReservasFundadorVencidasAsync`, que ya existe y ya corre.

**El mes gratis son 30 días y viven en una constante del flujo fundador**, no en `dias_prueba` del
plan (que es 0 explícito en los dos planes de pago). Moverlo al plan exige migración y eso es C2.

## 7. Los endpoints

| Número | Endpoint | Sesión | CSRF | Tasa |
|---|---|---|---|---|
| **API-66** | `GET /api/planes` | no | no (es GET) | general |
| **API-67** | `POST /api/mi/suscripcion/fundador` | sí | sí | sensible |
| **API-68** | `POST /api/pagos/aviso` | **no** | **no** (lo llama un servidor) | propia, generosa |

API-66 devuelve solo catálogo y un número: código, nombre, descripción, precio, y
`cuposFundadorDisponibles`. **Ningún dato de ninguna persona**, porque es público y se cachea.

API-68 es el endpoint más delicado que va a tener el proyecto. Sus reglas, explícitas:

1. **Verificar la firma antes de mirar el cuerpo.** Firma inválida → `401` y no se ejecuta nada.
2. **Idempotente por referencia externa.** El mismo aviso dos veces no confirma dos cupos ni crea
   dos pagos. Se apoya en el mismo criterio que ya usan las notificaciones (CC-04).
3. **Nunca creerle al monto del cuerpo**: se compara con el precio del plan en la base.
4. **Un duplicado responde `200`**, no error. Una pasarela que recibe un error reintenta para
   siempre, y a las tres de la mañana.
5. **No lleva CSRF** porque no hay navegador ni sesión; la autenticidad la da la firma. Esto es una
   excepción deliberada a la regla 8 de `CLAUDE.md` y queda escrita aquí y en el documento S7.
6. **No filtra nada**: responde lo mismo para una referencia que no existe y para una que ya se
   procesó.

## 8. La pantalla

**`/planes`**, pública. Los dos planes, el precio, qué incluye cada uno, y el contador. Cuatro
estados, los cuatro con prueba:

| Estado | Qué ve |
|---|---|
| Hay cupos y hay sesión de prestador con ficha | «Quedan N de 50» y el botón |
| **No quedan cupos** | El plan fundador aparece **cerrado** y queda el mensual. Sin botón |
| Sin sesión | Invita a crear cuenta y **vuelve a `/planes`** después |
| Con sesión pero sin ficha publicada | Manda a publicar la ficha primero: sin perfil de prestador no hay a quién asignarle el cupo |

**`/pago/inscripcion`** (solo con la pasarela simulada) y **`/planes/listo`** (la vuelta, que
consulta el estado y no confirma nada).

Los textos visibles se proponen, no se deciden: van en el PR bajo «Textos nuevos».

## 9. El documento, que es parte del entregable

1. **`docs/01-casos-de-uso/subsistema-7-suscripciones-y-pagos.md`**, nuevo. Con las reglas:
   `RN-S7-01` el cupo se confirma al inscribir el medio de pago, no al reservarlo · `RN-S7-02` la
   reserva dura 25 minutos y vencida libera el cupo · `RN-S7-03` el mes gratis son 30 días en
   estado `Prueba` sobre el plan fundador · `RN-S7-04` el aviso de la pasarela es idempotente por
   referencia · `RN-S7-05` el monto no se cree del cuerpo, se compara con el plan · `RN-S7-06` al
   llegar a 50 el plan fundador se cierra y no se reabre · `RN-S7-07` tomar el cupo **cambia de
   plan** la suscripción que ya existe y deja la vigencia en la fecha más lejana de las dos; nunca
   crea una segunda.
2. **`api-rest.md`**: API-66, API-67 y API-68 con su grupo de tasa y su excepción de CSRF.
3. **El sitio se regenera** con `python sitio\construir.py`. El HTML **nunca** se edita a mano.

## 10. Las pruebas, y la mutación de cada una

| Prueba | Mutación que la hace caer |
|---|---|
| Reservar devuelve número y deja el cupo tomado | quitar la escritura de `reservado_hasta` |
| El aviso aceptado confirma el cupo y arma la suscripción | quitar la llamada a `ConfirmarCupoFundadorAsync` |
| **El mismo aviso dos veces no confirma dos veces** | quitar la comprobación de idempotencia |
| Firma inválida: no pasa nada y responde `401` | invertir la comparación de la firma |
| Monto distinto al del plan: se rechaza | creerle al monto del cuerpo |
| Reserva vencida: el diario libera el cupo | comentar la llamada a `LiberarReservasFundadorVencidasAsync` |
| Sin cupos: API-67 responde `409` | devolver `200` cuando el repositorio no da número |
| El perfil sale de la sesión, no del cuerpo | leer el id del cuerpo |
| **Quien ya tenía prueba no termina con dos suscripciones ni con 30 días de regalo** (RN-S7-07) | poner `VigenteHasta = ahora + 30 días` a secas, sin comparar con la que tenía |
| **La simulada no levanta en Producción** | borrar la guarda de `AgregarZass.cs` |
| `/planes` en sus cuatro estados | quitar cada rama por turno |

**Candados con literal, obligatorios** (una prueba que saca el valor esperado de la constante que
vigila no vigila nada; aquí ya pasó cuatro veces en un día): **50** cupos, **25** minutos, **30**
días de mes gratis, **5990** y **7990** escritos a mano en una prueba aparte.

Y el guion de navegador: reservar → pagar en la simulada → vuelta → el prestador aparece en la
búsqueda. Un verde en jsdom no es haber visto la pantalla.

Migración: **ninguna**. Si aparece la necesidad de una, se para y se avisa antes de crearla.

## 11. Cómo se sabe que esto quedó bien

- Una persona entra a `/planes`, toma un cupo, inscribe el medio de pago, y **aparece en la
  búsqueda** — visto en un navegador, no en una prueba.
- El contador baja de 50 a 49 y lo confirma una consulta a la base, no la pantalla.
- Mandar el mismo aviso dos veces deja **un** cupo tomado y **un** registro, comprobado en
  `cupos_fundador` y en `pagos`.
- La aplicación **no levanta** en Producción con la pasarela simulada registrada.
- `pg_constraint` y `pg_indexes` siguen mostrando `ux_cupo_fundador_perfil`. Que la migración diga
  «Done» no prueba que el índice exista.
