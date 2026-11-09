# Agenda del prestador Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Completar el primer recorrido frontend de MOD-06 para que un prestador configure disponibilidad, bloquee horarios y gestione sus citas desde `/mi-agenda`.

**Architecture:** Añadir los contratos HTTP que faltan en `funciones/agenda/servicio.ts`, construir una página protegida y enfocada en tareas en `paginas/MiAgenda.tsx`, y enlazarla desde la navegación y `Mis citas`. La página consumirá la API existente (`API-32` a `API-34` y `API-37` a `API-42`) y mantendrá el formato de hora de Chile en un único servicio compartido.

**Tech Stack:** React 19, TypeScript, React Router 7, Vitest, Testing Library, CSS existente de ZASS!, API REST .NET ya desplegable.

**Spec:** `docs/superpowers/specs/2026-11-08-sitio-completo-design.md`

## Global Constraints

- Mantener React 19, TypeScript, Vite y `react-router-dom` 7; no añadir dependencias sin necesidad clara.
- Seguir los nombres y patrones existentes en `src/paginas`, `src/funciones` y `src/marco`.
- Toda conducta nueva debe tener una prueba que falle antes de implementar el comportamiento.
- La agenda siempre muestra las horas en `America/Santiago`, aunque el navegador esté en otra zona.
- No revelar a terceros quién ocupa un bloque; el frontend solo usa `estado` (`libre`, `ocupado`, `bloqueado`).
- No tocar secretos, despliegues, migraciones, backend ni hacer commit/push.

## Review Focus

- **Cuenta sin sesión:** `/mi-agenda` redirige a `/entrar` y vuelve a la agenda después de entrar; prueba en `MiAgenda.prueba.tsx`.
- **Cuenta sin perfil de prestador:** la página muestra una salida útil hacia `/ofrecer-servicios` y no intenta enviar `PerfilPrestadorId`; prueba en `MiAgenda.prueba.tsx`.
- **Disponibilidad vacía o con varios servicios:** cada servicio muestra su estado y una configuración independiente; prueba de selección y guardado en `MiAgenda.prueba.tsx`.
- **Horas y cambio de día:** las franjas se editan como horas locales y las citas se presentan con hora de Chile; prueba de render de `horaDeChile` y payload de `FranjaCmd`.
- **Conflicto o error de servidor:** un `409`, `422` o error de red conserva los datos escritos, muestra el mensaje y permite reintentar; prueba de cada acción en `MiAgenda.prueba.tsx`.

### Task 1: Completar el servicio frontend de agenda

**Files:**
- Modify: `frontend/src/funciones/agenda/servicio.ts`
- Test: `frontend/src/funciones/agenda/servicio.prueba.ts`

**Interfaces:**
- Consumes: `obtener` y `enviar` de `frontend/src/api/cliente.ts`; tipos de `Cita`, `EstadoDeCita`, `horaDeChile` y `diaDeChile` existentes.
- Produces: `Franja`, `Disponibilidad`, `Bloqueo`, `miDisponibilidad()`, `guardarDisponibilidad(prestadorServicioId, franjas)`, `crearBloqueo(desdeEn, hastaEn, motivo)` y `eliminarBloqueo(id)` si el backend no ofrece eliminación, se deja fuera de esta fase y la UI solo crea bloqueos; `misCitas()` y las acciones existentes se conservan.

- [ ] **Step 1: Write the failing tests** para que `miDisponibilidad` haga `GET /api/mi/disponibilidad`, `guardarDisponibilidad` haga `PUT /api/mi/disponibilidad` con `{ prestadorServicioId, franjas }`, y `crearBloqueo` haga `POST /api/mi/bloqueos` con `{ desdeEn, hastaEn, motivo }`.
- [ ] **Step 2: Run the focused service tests** con `npm run prueba -- src/funciones/agenda/servicio.prueba.ts`; confirmar que fallan porque las funciones/tipos no existen.
- [ ] **Step 3: Implementar los tipos y funciones** en `servicio.ts`, usando `TimeOnly` serializado como `HH:mm:ss` cuando se envía JSON y conservando las respuestas del servidor sin transformar identificadores.
- [ ] **Step 4: Ejecutar de nuevo la prueba enfocada** y confirmar que pasa sin romper las pruebas existentes del servicio.

### Task 2: Construir la página de agenda del prestador

**Files:**
- Create: `frontend/src/paginas/MiAgenda.tsx`
- Create: `frontend/src/paginas/MiAgenda.prueba.tsx`
- Modify: `frontend/src/App.tsx`
- Modify: `frontend/src/index.css`

**Interfaces:**
- Consumes: `useSesion`, `miPerfil`, servicios del perfil, tipos y funciones de Task 1, y acciones de citas de `funciones/agenda/servicio.ts`.
- Produces: ruta `/mi-agenda`; pantalla con secciones `Disponibilidad`, `Bloqueos` y `Citas que te han pedido`.

- [ ] **Step 1: Write failing component tests** para: redirección sin sesión; enlace a `/ofrecer-servicios` sin perfil; selección de servicio; carga de disponibilidad; edición de día/hora/duración; guardado; creación de bloqueo; confirmación/rechazo de una cita solicitada; marcado de asistencia; y mensaje de error con reintento.
- [ ] **Step 2: Run `npm run prueba -- src/paginas/MiAgenda.prueba.tsx`** y confirmar fallos por la ruta/componente inexistentes.
- [ ] **Step 3: Implementar `MiAgenda.tsx`** con carga protegida, estado local de formularios, mensajes por acción y botones deshabilitados durante cada solicitud. Agrupar citas por estado y mostrar solo acciones válidas: confirmar/rechazar en `Solicitada`, marcar asistencia en `PendienteAsistencia`, y sin acciones destructivas para estados finales.
- [ ] **Step 4: Añadir la ruta** `mi-agenda` dentro de `Marco` y una entrada visible desde `Cabecera`/`MiFicha` solo para cuentas con rol de prestador.
- [ ] **Step 5: Añadir CSS mínimo** para la agenda: tarjetas de servicio, filas de franjas, bloqueos y acciones de cita, reutilizando tokens y componentes existentes; mantener foco visible y layout móvil sin efectos costosos.
- [ ] **Step 6: Ejecutar la prueba de página** y confirmar que todas las interacciones pasan.

### Task 3: Integrar el recorrido de citas y navegación

**Files:**
- Modify: `frontend/src/paginas/MisCitas.tsx`
- Modify: `frontend/src/paginas/MiFicha.tsx`
- Modify: `frontend/src/marco/Cabecera.tsx`
- Test: `frontend/src/paginas/MisCitas.prueba.tsx` (extender pruebas existentes)

**Interfaces:**
- Consumes: `/mi-agenda` de Task 2 y acciones ya existentes `confirmarCita`, `rechazarCita`, `registrarAsistencia`.
- Produces: enlaces consistentes entre `Mis citas`, `Mi agenda`, `Mi ficha` y la ficha pública.

- [ ] **Step 1: Write failing navigation tests** para que una cuenta prestadora vea “Gestionar agenda” y que las citas recibidas enlacen a `/mi-agenda` sin perder el contexto.
- [ ] **Step 2: Run the focused page tests** y confirmar el fallo de navegación.
- [ ] **Step 3: Implementar los enlaces y estados compartidos** sin duplicar llamadas a la API ni cambiar la semántica actual de `Mis citas`.
- [ ] **Step 4: Ejecutar `npm run prueba -- src/paginas/MisCitas.prueba.tsx src/paginas/MiAgenda.prueba.tsx`** y confirmar que pasa.

### Task 4: Verificación del bloque de agenda

**Files:**
- No new files; verify the files from Tasks 1–3.

**Interfaces:**
- Consumes: la ruta `/mi-agenda`, la suite frontend y los comandos de build.
- Produces: evidencia de que la agenda no rompe el resto del sitio.

- [ ] **Step 1: Run `npm run prueba`** desde `frontend`; esperado: todas las pruebas pasan.
- [ ] **Step 2: Run `npm run build`** desde `frontend`; esperado: `tsc -b` y `vite build` terminan correctamente.
- [ ] **Step 3: Run `npm run lint`** desde `frontend`; esperado: sin errores nuevos.
- [ ] **Step 4: Inspect the diff** y comprobar que no contiene secretos, cambios de despliegue, migraciones ni commits.
- [ ] **Step 5: Run the local browser flow** cuando la API y PostgreSQL estén disponibles; comprobar login, apertura de `/mi-agenda`, guardado de disponibilidad y acción sobre una cita de prueba.

## Gaps intentionally deferred

- Reprogramar una cita desde la pantalla del prestador puede reutilizar la agenda pública en una entrega posterior; esta fase cubre configuración, bloqueos, confirmación, rechazo y asistencia.
- La eliminación de bloqueos no se implementa porque la API actual expone creación (`API-34`) pero no un endpoint de borrado.
- Reseñas, planes, pago, legal y pulido transversal tienen planes separados después de que esta fase quede verde.

