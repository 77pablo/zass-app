# ZASS! — traspaso completo (8-nov-2026, noche)

Documento para quien continúe el proyecto (Codex u otra persona). **Léelo entero antes de tocar nada.**
Complementa a `ESTADO.md` (bitácora larga, ~1.400 líneas, fuente de verdad del detalle) y a `DESPLIEGUE-API.md`
y `frontend/DESPLIEGUE-CLOUDFLARE.md` (guías de despliegue). **Aquí no hay ningún secreto**: solo dónde están.

---

## ⭐ PRIORIDAD DECIDIDA POR PABLO (manda sobre el orden de abajo)

**Primero terminar la web entera. Los despliegues y los secretos quedan para después.**

- **Ahora:** construir lo que falta del producto (§7 B): pantalla de agenda del prestador, pantalla de reseñas,
  decisión sobre el registro de contacto, MOD-12 tarjetas NFC, página de planes. Trabajar y probar **en local**.
- **Después (no ahora):** Manual Deploy en Render, publicar el sitio y rotar los 3 secretos (§3 y §6).
- **Ojo por esa decisión:** `zass.cl` sigue sirviendo el backend viejo hasta que se despliegue; no pasa nada
  mientras no se publique el sitio nuevo, pero **no publicar el sitio sin antes desplegar el backend** (§6).
  Y los 3 secretos expuestos siguen siendo válidos hasta que se roten: no compartir este chat ni los archivos de `zass-secretos`.
- Seguir sin hacer commit ni push sin que Pablo lo pida.

---

## 🟡 ÚLTIMA SESIÓN (Claude, 8-nov noche): agenda del prestador, a medio cerrar

Siguiendo `docs/superpowers/specs/2026-11-08-sitio-completo-design.md` y `docs/superpowers/plans/2026-11-08-agenda-prestador.md`
(escritos por Codex; **sin código suyo**). Hecho y **en verde** (`tsc` limpio, **348 pruebas de interfaz**, 0 avisos de lint en lo nuevo):

- `funciones/agenda/servicio.ts`: `miDisponibilidad` (API-32), `guardarDisponibilidad` (API-33, horas `HH:mm:ss`), `crearBloqueo` (API-34),
  `DIAS`, `horaCorta`, `inicioDeDiaEnChile` (medianoche de Chile en UTC; verano UTC-3, invierno UTC-4). 8 pruebas nuevas.
- `paginas/MiAgenda.tsx` (+ `.prueba.tsx`, 13 pruebas): ruta `/mi-agenda`. Editor de franjas por servicio (día, desde, hasta, duración),
  validación local igual a la del servidor, errores que conservan lo escrito, y bloqueo de días completos.
  **Se mutaron** el «día siguiente» del bloqueo y la validación previa: en ambos cayeron pruebas.
- Navegación: «Mi agenda» en la cabecera (escritorio y menú móvil) solo para prestadores, y enlaces desde `Mis citas` y `Mi ficha`.
- Estilos de agenda en `index.css`.

**Desviaciones deliberadas del plan de Codex** (para no duplicar): `Mis citas` YA confirma, rechaza y marca asistencia de las citas
que te piden, así que `/mi-agenda` **no** repite «Citas que te han pedido»: solo cubre **disponibilidad y bloqueos** y enlaza a `Mis citas`.

**Pendiente de esta fase:**
1. **Mirarla en un navegador** (nadie la ha visto renderizada; solo hay pruebas de comportamiento). Se iba a hacer con `vite preview` +
   Playwright interceptando `/api/*`; el servidor se detuvo antes. Revisar escritorio y móvil.
2. **Falta backend: quitar y listar bloqueos.** Hoy un bloqueo creado por error **no se puede deshacer** (no hay `GET`/`DELETE /api/mi/bloqueos`);
   la pantalla lo advierte. Es un hueco real de producto: añadir endpoints + pruebas (mutadas) + botón «Quitar».
3. Aún no hay pantalla para **reprogramar desde el lado del prestador** (diferido en el plan).
4. Sin commitear: ver `git status`. Nada de esto está publicado ni desplegado.
5. Siguen las fases 2 (reseñas), 3 (planes/suscripción visible, sin precios inventados) y 4 (pulido) del diseño de Codex, sin empezar.

---

## 1. Qué es

Plataforma web comunitaria de servicios locales en Chile. Quien **ofrece** un servicio (abogado, contador,
corredor, arquitecto, gasfíter, electricista…) aparece ante quien lo **busca** cerca, con credenciales
verificadas, reseñas, agenda de citas y, más adelante, una tarjeta NFC con QR.

- **Modelo de negocio:** el prestador paga una mensualidad. **El buscador no paga nunca.**
- **Marca:** ZASS! (doble S, con signo). Amarillo `#F6C81E`, tinta `#1A1A1A`, crema `#FDF8E7`, Montserrat. «Conecta con un toque».
- **Dirección de diseño de Pablo:** «profesional, que no sea un formulario, es una comunidad».
- **Dueño:** Pablo Espinoza (`pdanielespinozavega@gmail.com`). Habla español chileno informal. Responde en español.

## 2. Dónde vive todo (estado real en producción)

| Pieza | Dónde | URL / dato |
|---|---|---|
| Sitio (React) + Functions | **Cloudflare Pages**, proyecto `zass` | `https://zass.cl`, `https://www.zass.cl`, `https://zass-59w.pages.dev` |
| API (.NET 8) | **Render**, servicio `zass-api`, Docker, plan **gratis** (se duerme tras ~15 min) | `https://zass-api.onrender.com` |
| Base de datos | **Neon**, PostgreSQL 16 + PostGIS 3.6, región AWS US East (N. Virginia), base `neondb` | cadena en archivo local (ver §3) |
| Correo | **Resend** (dominio `zass.cl` verificado, región São Paulo) | remitente `no-responder@zass.cl` |
| Dominio | Comprado en **NIC Chile**; DNS en **Cloudflare** (nameservers `doug` y `monroe`) | zona activa |
| Código | **GitHub** `77pablo/zass-app` (privado), rama `main` | Pablo sube con **GitHub Desktop** |
| Documentación (especificación) | `C:\Users\pdani\OneDrive\Desktop\Repositorio ZASS\zass-documentacion` | 45 `.md` en 9 bloques + sitio generado |

**Cómo se conectan:** el navegador habla con `zass.cl`; una **Pages Function** (`frontend/functions/api/[[ruta]].js`)
reenvía `/api/*` a Render (variable `API_ORIGEN` del proyecto Pages) para que las cookies de sesión viajen sin CORS.
Otras Functions: `/prestadores/:slug` (SEO por ficha), `/sitemap.xml`, `/sitemaps/:n`.

**Registros DNS en Cloudflare (`zass.cl`):** CNAME `@` y `www` → `zass-59w.pages.dev` (proxied, nube naranja);
CNAME `rsend` → `rsend-sae1.forge.rmta.net` y `send` → `send.forge.rmta.net` (**DNS only, nube gris, obligatorio**);
TXT `resend._domainkey` (DKIM) y TXT `_dmarc` (`v=DMARC1; p=none;`).

## 3. Secretos y cuentas (qué hay y dónde, nunca el valor)

Carpeta **fuera del repo y de OneDrive**: `C:\Users\pdani\zass-secretos\`
- `neon-url.txt` (URL `postgresql://`), `neon-npgsql.txt` (formato Npgsql, el que usa la API), `clave-direcciones-base64.txt`
  (clave AES-256 que cifra las direcciones; **si se pierde no se descifran los datos; ya hay copia en el gestor de Pablo**),
  `proxies.txt` (valor de `Zass__ProxiesDeConfianza`), `resend-apikey.txt`.
- **Variables en Render (`zass-api` → Environment):** `Zass__CadenaConexion`, `Zass__ClaveDireccionesBase64`,
  `Zass__ProxiesDeConfianza`, `Zass__SaltosDeProxy=3`, `Zass__ExigirHttps=true`, `ASPNETCORE_ENVIRONMENT=Production`,
  `Zass__Correo__Proveedor=Resend`, `Zass__Correo__ApiKey`, `Zass__Correo__Remitente=no-responder@zass.cl`,
  `Zass__Correo__NombreRemitente=ZASS!`, `Zass__UrlPublica=https://zass.cl`.
- **Variable en Cloudflare Pages:** `API_ORIGEN=https://zass-api.onrender.com` (guardada como secreto de Pages).
- Postgres **local** de desarrollo: portable en `herramientas\pgsql`, puerto **5433**, clave en `.clave-pg.txt` (ignorada por git).

### ⚠️ Rotar cuanto antes (pasaron por el chat de la sesión de hoy)
1. **Contraseña de Neon** (Neon → Settings → Reset password) y actualizar `Zass__CadenaConexion` en Render.
2. **Token de API de Cloudflare** usado para desplegar (borrarlo en Cloudflare → My Profile → API Tokens y crear otro con permisos de Pages/Workers/DNS).
3. **Clave de API de Resend** (crear otra, cargarla en Render, borrar la vieja).
Los archivos de `zass-secretos` ya contienen valores viejos hasta que se actualicen.

## 4. Arquitectura del código

- `backend/` — **.NET 8**, capas `Zass.Dominio` (**cero paquetes NuGet**, lo exige el documento) → `Zass.Aplicacion` →
  `Zass.Infraestructura` (EF Core, Npgsql, PostGIS) → `Zass.Api` (minimal APIs). Pruebas: `Zass.Aplicacion.Pruebas`
  (sin base), `Zass.Arquitectura.Pruebas` (NetArchTest), `Zass.Integracion.Pruebas` (contra PostgreSQL real y la API por HTTPS).
- `frontend/` — **React 19 + TypeScript + Vite** (SPA/PWA), `react-router-dom` 7, Vitest + Testing Library. `functions/` = Cloudflare Pages Functions.
- Identificadores del documento (`CU-`, `TBL-`, `API-`, `RN-`, `CC-`, `R-`, `MOD-`) aparecen en el código y apuntan a la especificación.
  Si código y documento no coinciden, **se corrige uno y se dice cuál se movió**.
- Módulos: MOD-01 Identidad · MOD-02 Credenciales · MOD-03 Catálogo · MOD-04 Geolocalización · MOD-05 Búsqueda ·
  MOD-06 Agenda · MOD-11 Reseñas · MOD-12 Tarjetas NFC · MOD-13 Suscripciones.
- Seguridad: cookies httpOnly+Secure+SameSite=Lax (acceso 15 min, refresco 30 días), CSRF por cabecera `X-CSRF-Token`
  (atado a la identidad: se pide uno nuevo tras entrar), Argon2id, límite de peticiones por IP (grupos P/A/G/S/Salud),
  validación de configuración al arrancar (`ValidacionDeArranque`), direcciones cifradas con AES.
- **Migraciones NO corren al arrancar.** Se aplican a mano: `dotnet run --project src/Zass.Api -c Release -- --migrar`
  con `Zass__CadenaConexion`, `Zass__ClaveDireccionesBase64` y `ASPNETCORE_ENVIRONMENT=Production` en el entorno.
  Hoy hay **13 migraciones, todas aplicadas en Neon**.

## 5. Qué está hecho (y comprobado)

- **MOD-01 Identidad:** registro, verificación de correo (ahora **sí llega**, probado con correo real), recuperar contraseña, sesión, perfil.
- **MOD-03 Catálogo, MOD-04 Geolocalización (362 zonas = Chile por comunas), MOD-05 Búsqueda** (incl. barra con sugerencias, mapa, comunas).
- **MOD-13 Suscripciones:** tablas y lógica de vencimiento/avisos; **la pasarela de pago NO existe**.
- **MOD-02 Credenciales:** declarar/listar/eliminar y cola de verificación (backend de `agente/verificacion`, fusionado; pantalla `MisCredenciales`).
- **MOD-06 Agenda:** backend completo (API-32 a API-42, bloqueo `FOR UPDATE`, idempotencia, zona horaria de Chile) y en el frontend:
  agendar desde la ficha y **Mis citas** (esta noche). **No hay pantalla de agenda del prestador** (definir horarios, confirmar/rechazar, marcar asistencia).
- **MOD-11 Reseñas:** backend fusionado (API-46 a 53). **Sin pantalla.** Se revisó solo en parte (ver §9).
- Trabajos de fondo (planificador A-09, vencimiento y avisos), correo transaccional (Brevo/Resend), SEO (meta por ficha, sitemap, robots), páginas legales (**en borrador**).
- **Sitio en línea** en `zass.cl` con HTTPS, y flujo completo probado: registro → correo (Resend) → verificar → entrar.
- **Rendimiento en celular** (publicado): sin `backdrop-filter`, sin orbes con desenfoque, sin animaciones que repintan cada cuadro. **No se midió en un teléfono real.**

**Pruebas al cierre:** backend **654 + 16 + 597 = 1.267** en verde; frontend **326** en verde, `tsc` limpio, build ok.

## 6. Trabajo de esta sesión que está COMMITEADO pero NO publicado

Commit `b678b42` (+ merge `f21045b`), **commits ya subidos a GitHub (push hecho el 8-nov noche; falta Manual Deploy en Render)**. Contenido:
- **Decisión de Pablo:** buscar y ver fichas no exige cuenta; **contactar sí**; y **una sola cuenta** sirve para buscar y ofrecer.
- API-05 (ficha pública): sin sesión, `contacto` vacío y `contactoRequiereCuenta: true` (se quita en el servidor), con `Cache-Control: private, no-store`.
- **CS-04** `POST /api/mi/activar-prestador` («Ofrecer mis servicios»): agrega el rol, crea el perfil en borrador, idempotente, renueva la sesión.
- Registro sin elegir rol; nueva página `/ofrecer-servicios`; la ficha muestra «Crear mi cuenta gratis» y vuelve a la misma ficha.
- Pantalla **Mis citas** (`/mis-citas`) y los cambios de rendimiento móvil.

### 🔴 Siguiente paso inmediato (orden obligatorio)
1. ~~Subir los commits~~ **HECHO** (push a `origin/main` el 8-nov noche, HEAD `b082e8a`). Ya está en GitHub.
2. **PENDIENTE (lo hace Pablo):** **Render → zass-api → Manual Deploy → Deploy latest commit** (`autoDeploy: false` a propósito). **No hay migraciones nuevas.**
3. **PENDIENTE:** comprobar que el backend nuevo esté arriba: `POST https://zass-api.onrender.com/api/mi/activar-prestador` sin sesión debe dar **401** (hoy da 404 = código viejo).
4. **Solo entonces** publicar el sitio: `cd frontend && npm run build && npx wrangler pages deploy dist --project-name zass --branch main`
   (requiere `CLOUDFLARE_API_TOKEN` en el entorno). **Si el sitio sale antes que la API, «Ofrecer mis servicios» llama a un endpoint inexistente.**
5. Probar en `zass.cl`: ficha sin sesión (sin teléfono, con botón de crear cuenta), crear cuenta, volver a la ficha con contacto, «Ofrecer mis servicios».

## 7. Lo que falta, por prioridad

**A. Seguridad / operación (hacer pronto)**
1. Rotar los 3 secretos del §3.
2. **Proxies de Render:** `render.yaml` actualizado a `Zass__SaltosDeProxy: "3"`. Guías `DESPLIEGUE-API.md` y `DESPLIEGUE-CLOUDFLARE.md` actualizadas con `10.0.0.0/8`, `::1` y `127.0.0.1` (PR #2 mergeado).
3. **Defecto abierto de la IP del visitante:** por `zass.cl` la API ve una IP de Cloudflare, no la del visitante (la Pages Function pone `X-Forwarded-For` pero Cloudflare lo pierde en el salto Function→Render). Directo a Render sí funciona y las IP falsas se ignoran. Arreglo propuesto: la función manda la IP en una cabecera propia (ej. `X-Zass-IP-Cliente`) y la API la lee con `ForwardedHeadersOptions.ForwardedForHeaderName`, con prueba por HTTP. Efecto actual: el límite de peticiones se cuenta por salida de Cloudflare, no por persona. No es un agujero de seguridad.
4. **Copias de seguridad diarias:** activo `.github/workflows/copia-base-de-datos.yml` con `postgres:18` (PR #3). Secretos `NEON_URL_COPIAS` y `FRASE_COPIAS` cargados en GitHub. Descifrado de volcado probado con éxito (PGDMP, 106 KB). Falta probar restauración con pg_restore 18.
5. Plan gratis de Render: se duerme y la primera visita tarda ~1 min. Antes de abrir al público, pasar a un plan que no duerma o añadir una sonda de despertar.

**B. Producto**
1. **Pantalla de agenda del prestador** (MOD-06): horarios/bloques, confirmar o rechazar (modo manual), marcar asistencia. El backend ya existe. Tarea asignada a Joaquín (J2/J3).
2. **Pantalla de reseñas** (MOD-11): escribir y ver. **Decidido por Pablo:** modelo completo con migración N3. Cada parte solo acusa a la otra; si faltó el prestador, el cliente sí reseña; si faltó el cliente, no hay reseña; si se contradicen, se bloquea la reseña y la cita pasa a la cola de revisión interna.
3. **Registro de contacto:** contactar por WhatsApp/teléfono no deja registro hoy. **Decidido por Pablo:** botón «ya me atendió» que marca el cliente; la reseña queda rotulada «contacto directo, sin cita».
4. **MOD-12 Tarjetas NFC:** **Decidido por Pablo:** al tocar abre la ficha completa del prestador (sin migración nueva). Si el prestador no tiene suscripción vigente, abre la ficha con aviso sin botones de contacto/agendar (RN-S6-10).
5. **Página de planes / premium visible pero bloqueado** (idea de Pablo, estilo Instagram: comparar básico vs premium a la vista). Es para **prestadores** (el buscador no paga). Cualquier número de «clientes hoy» **debe ser real** (publicidad engañosa es sancionable en Chile); con pocos usuarios, mostrar acumulados.
6. **Entrar con Google** reduciría mucho la fricción del registro (idea mía, no decidida).
7. Pantalla de «recuperar» y otras ya existen; falta revisar el flujo completo en móvil real.

**C. Decisiones que solo puede tomar Pablo (bloquean cosas)**
- **Precio y nombre de los planes**, y qué incluye cada uno.
- **Pasarela de pago:** simulada por ahora, o esperar integración real (Transbank, Flow o Mercado Pago).
- Dónde correrá la API a largo plazo (hoy Render gratis).
- Geocodificar direcciones (proveedor N-08): hoy el prestador elige su zona de una lista.

**D. Antes de abrir al público**
- **Textos legales** (términos, privacidad) están marcados **BORRADOR**: un abogado debe revisarlos. Cumplir Ley 21.719 (datos personales, vigente desde dic-2026).
- Reemplazar datos de demostración.
- Actualizar el **documento de especificación**: `CS-04` es nuevo, `API-05` cambió (contacto con sesión), y `render.yaml`.
- `BuscarAgregadoPorZona` quedó de baja prioridad (el documento de postulación municipal se **canceló**, no proponerlo de nuevo).

## 8. Cómo trabajar (reglas que Pablo ya corrigió — respétalas)

- **Un verde no prueba nada.** Antes de creer una prueba, pregunta cómo se vería el fallo y comprueba **eso**. Practica obligatoria: **mutar** el código (romperlo a propósito) y ver que la prueba cae; esta sesión se hizo con el filtro de contacto y con `ActivarPrestadorAsync`. Un error de sintaxis de Mermaid también dibuja un SVG.
- Con la base: consultar `pg_indexes`, `information_schema`, `pg_constraint`, no confiar en que la migración dijo «listo».
- **Seguridad en todo endpoint** (límite de peticiones, validación, cabeceras, sin secretos en el repo).
- **El sitio de documentación se genera:** `python sitio\construir.py`; nunca editar el HTML a mano.
- Un archivo por entregable; nombres en español con guiones; tablas y diagramas en vez de volcados.
- No proponer instalar PostgreSQL en el sistema (sin permisos de administrador): el local es portable.
- **No hacer commit ni push sin que Pablo lo pida.** Los commits van a nombre de **Pablo Espinoza <pdanielespinozavega@gmail.com>**.
- El código de ejemplo de un plan no está revisado: si choca con las restricciones del propio plan, manda la restricción.
- Mensajes a prospectos: tono de vendedor profesional, sin dar las gracias (reglas aparte, no aplican al código).

## 9. Trampas aprendidas esta sesión (te ahorran horas)

- **Render y `SIGSEGV 139`:** parece un fallo de memoria pero era la excepción de arranque por variables mal cargadas (cadena en formato URL, clave no Base64). Leer el log completo.
- **Variables de Render:** pegar el **contenido** del archivo, no la ruta; sin comillas ni saltos de línea al final. Cambiarlas reinicia el servicio.
- **Neon da una URL `postgresql://`; Npgsql no la entiende.** Convertir a `Host=...;Database=...;Username=...;Password=...;SSL Mode=Require` (sin `channel_binding`).
- **`dotnet run` usa el perfil de lanzamiento (Development, puerto 5052).** Para probar producción: `--no-launch-profile` y `ASPNETCORE_HTTP_PORTS`.
- **Proxies:** el balanceador de Render llega desde `10.x`; la cadena tiene Pages Function → Cloudflare → Render, por eso `SaltosDeProxy=3`. Lo que manda la Function en `X-Forwarded-For` **no sobrevive**. Medir con `SELECT ip_origen FROM sesiones ORDER BY creada_en DESC LIMIT 3;` (siempre desde un registro real, no suponer).
- **Resend:** los CNAME `rsend`/`send` deben ir en **DNS only**. Con proxy naranja la verificación queda en pending para siempre.
- **Cloudflare:** el token de API de «Edit Cloudflare Workers» **no** cubre DNS ni `activation_check`; para crear registros hay que dar permiso de Zone DNS. `wrangler login` interactivo falló; se usó token por variable de entorno.
- **NIC Chile** puede tardar de minutos a horas en publicar los nameservers; Cloudflare muestra «pending» hasta entonces y Pages no emite certificado.
- **Vite preview** usa HTTPS (certificados en `certificados/`), y si el 4173 está ocupado cambia de puerto en silencio.
- **PowerShell vs Bash:** el entorno de la sesión usa Git Bash; para `clip` (portapapeles) sirve en ambos. Nunca imprimir secretos: dejarlos en el portapapeles con `clip < archivo`.
- El emulador de Playwright **no reproduce** el costo de GPU de un teléfono (daba ~57 fps antes y después). No presentar eso como medición.
- Las pruebas de integración corren contra `localhost:5433`; exportar `ZASS_CADENA_CONEXION` con la clave de `.clave-pg.txt` antes de `dotnet test`. La suite completa tarda ~4 min.

## 10. Cómo correr y probar

```bat
:: Base local
C:\Users\pdani\zass-app\iniciar-postgres.bat      :: puerto 5433 (no mostrar su salida: se queda colgando algunas herramientas)
C:\Users\pdani\zass-app\detener-postgres.bat
```
```bash
# Backend: pruebas (necesita la base local arriba)
cd backend
export ZASS_CADENA_CONEXION="Host=127.0.0.1;Port=5433;Database=zass;Username=zass;Password=<.clave-pg.txt>"
dotnet test pruebas/Zass.Aplicacion.Pruebas && dotnet test pruebas/Zass.Arquitectura.Pruebas && dotnet test pruebas/Zass.Integracion.Pruebas

# Frontend
cd frontend && npm run build && npx vitest run && npx tsc --noEmit
```
`ver-zass.bat` levanta API (https://localhost:7277) y sitio juntos en local; `cerrar-zass.bat` los baja.

## 11. Fuera de ZASS (contexto, no urgente)

- **OpenJarvis** (asistente local de Stanford) instalado en `C:\Users\pdani\OpenJarvis`, con LM Studio y el modelo `qwen3.5-4b`
  (config en `C:\Users\pdani\.openjarvis\config.toml`; arranque: `C:\Users\pdani\OpenJarvis-iniciar.bat`). Solo chat con herramientas;
  **no** conectado al correo (Pablo lo dejó para después). No usan Claude porque exige API de pago.
- Otros proyectos de Pablo (App Iglesia, Encuentra tu Mascota, ServicioWebSP, etc.) viven en otras carpetas y no tienen relación con este repo.

---
*Escrito al cierre de la sesión del 8-nov-2026. Si algo de aquí contradice `ESTADO.md` o el código, manda lo verificable: comprueba primero y corrige este documento.*
