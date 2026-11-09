# ZASS! — Diseño para completar el sitio

## Intención

Terminar el frontend de ZASS! por recorridos completos, de modo que una persona pueda
buscar y contactar, un prestador pueda configurar su oferta y agenda, ambas partes puedan
gestionar una cita y luego dejar una reseña. El sitio debe sentirse como una comunidad
profesional local, no como una colección de formularios.

## Objetivos

- Cerrar primero el recorrido de agenda del prestador usando la API existente.
- Añadir la experiencia de reseñas pública y autenticada usando los contratos de MOD-11.
- Hacer visible la suscripción para prestadores sin inventar precios, nombres de planes ni
  pasarela de pago.
- Pulir estados de carga, vacío, error, sesión, móvil y navegación entre pantallas.
- Mantener el diseño ZASS!: amarillo `#F6C81E`, tinta `#1A1A1A`, crema `#FDF8E7`, Montserrat,
  tono profesional y comunitario.
- Probar cada recorrido con pruebas de comportamiento y conservar la suite frontend verde.

## No objetivos

- No desplegar Render ni Cloudflare durante este trabajo.
- No rotar ni leer secretos.
- No implementar pasarela de pago ni decidir precio o nombre de planes.
- No completar los textos legales con razón social, RUT o plazos inventados; siguen siendo
  borrador hasta que Pablo entregue esos datos y un abogado los revise.
- No cambiar contratos de backend salvo que el frontend revele una ausencia real; la API ya
  expone los contratos necesarios para agenda y reseñas.

## Orden de entrega

### Fase 1 — Agenda del prestador

Crear la pantalla de gestión para quien ofrece servicios:

- listar sus servicios y la disponibilidad guardada;
- crear, editar y eliminar bloques de disponibilidad;
- crear y quitar bloqueos;
- listar sus citas separadas de las citas que pidió como buscador;
- confirmar o rechazar solicitudes manuales;
- marcar asistencia después de la hora;
- mostrar errores de autorización, conflictos, estados vacíos y reintento;
- enlazar cada acción con `Mis citas`, la ficha y el editor de la oferta sin duplicar lógica.

La UI debe respetar la zona horaria de Chile que ya usa `funciones/agenda/servicio.ts`. No
debe revelar a terceros quién ocupa un bloque; esa regla pertenece también a la API.

### Fase 2 — Reseñas

- Mostrar reseñas públicas en la ficha con calificación, comentario, aspectos y respuesta.
- Mostrar estados de “sin reseñas” y paginación/carga adicional si la respuesta trae cursor.
- Permitir registrar un contacto desde una ficha cuando corresponda.
- Mostrar la elegibilidad y explicar por qué todavía no se puede reseñar.
- Crear, editar y confirmar el envío de una reseña cuando el servidor detecte datos personales.
- Permitir al prestador responder una reseña y a cualquier persona autorizada reportarla.
- No crear una pantalla de moderación: los endpoints internos son para el rol moderador y no
  forman parte del producto público en esta fase.

### Fase 3 — Planes y suscripción

- Añadir una presentación para prestadores que explique qué resuelve la suscripción.
- Mostrar el plan y el estado actual donde la persona administra su ficha.
- Dejar el cobro explícitamente bloqueado o en espera hasta que Pablo decida precio, nombre y
  pasarela.
- No mostrar métricas de “clientes hoy” ni cifras de marketing que no provengan de datos reales.

### Fase 4 — Pulido transversal

- Revisar rutas protegidas y retornos después de entrar.
- Unificar mensajes, estados de carga, avisos, botones y foco accesible.
- Revisar responsive en los recorridos principales y mantener la estrategia móvil ya aplicada:
  sin `backdrop-filter`, orbes desenfocados ni animaciones que repinten continuamente.
- Revisar meta/títulos, enlaces del pie, 404, navegación vieja `/barrios` y páginas legales.
- Ejecutar pruebas frontend, `tsc`, build y recorridos de navegador local.

## Alternativas consideradas

1. **Recorridos verticales completos — elegida.** Termina un flujo funcional y comprobable antes
   de iniciar el siguiente. Encaja con el backend ya construido y reduce el riesgo de diseñar
   pantallas aisladas que no puedan usarse.
2. **Pulido visual global primero.** Daría una apariencia uniforme antes, pero retrasaría los
   recorridos que hoy faltan y repetiría trabajo al incorporar agenda y reseñas.
3. **Implementar todas las pantallas de módulos primero.** Maximiza cobertura nominal, pero
   deja decisiones de producto abiertas (planes y cobro) mezcladas con código incompleto.

## Restricciones técnicas

- React 19, TypeScript, Vite y `react-router-dom` 7; mantener dependencias actuales salvo una
  necesidad clara.
- Seguir los nombres y patrones existentes en `src/paginas`, `src/funciones` y `src/marco`.
- Toda conducta nueva debe tener una prueba que falle antes de implementar el comportamiento.
- No confiar solo en un resultado verde: las pruebas de seguridad y permisos deben cubrir el
  fallo esperado.
- No comprometer ni publicar cambios; los commits y despliegues quedan bajo instrucción de Pablo.

## Resultado esperado

El frontend tendrá todos los recorridos públicos y de cuenta que el backend actual soporta,
con agenda y reseñas utilizables, suscripción presentada sin decisiones inventadas y una capa
visual coherente. El único contenido deliberadamente pendiente será el legal y la definición
comercial de planes/pagos.

