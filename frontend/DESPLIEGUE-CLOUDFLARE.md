# ZASS! en Cloudflare Pages

**Estado: publicado.** El sitio responde en `https://zass.cl` y `https://www.zass.cl` con HTTPS, y `https://zass.cl/api/salud` llega a la API a través de la Pages Function (comprobado con peticiones GET el 12-nov-2026). Queda **un defecto abierto**: la IP del visitante (ver más abajo).

## Qué va a Cloudflare y qué no

| Pieza | Dónde corre |
|---|---|
| **El sitio** (`frontend/`) y sus Functions | **Cloudflare Pages**, proyecto `zass`. |
| **La API** (.NET 8) | **Render**, servicio `zass-api`, con Docker (`render.yaml`). Cloudflare Pages/Workers no ejecutan .NET. Guía: `DESPLIEGUE-API.md`. |
| **La base de datos** (PostgreSQL + PostGIS) | **Neon**. |

Las cuentas, el DNS y los nombres de las variables de cada servicio están en `TRASPASO.md` §2 y §3.

## Lo que hay en el repositorio

Archivos estáticos (`public/`):

- `_redirects`: una sola regla, `/* /index.html 200`, para que `zass.cl/buscar`, `/barrios`… abran la aplicación (sin esto un enlace directo da 404).
- `_headers`: cabeceras de seguridad (CSP, HSTS, nosniff, X-Frame-Options, Permissions-Policy) y caché larga para los archivos con huella.
- `robots.txt`, `og-zass.png`, `favicon.svg`.

Pages Functions (`functions/`):

| Archivo | Ruta que atiende |
|---|---|
| `functions/api/[[ruta]].js` | `/api/*`: reenvía a la API, para que las cookies de sesión funcionen sin CORS. |
| `functions/prestadores/[slug].js` | `/prestadores/:slug`: la ficha con meta del lado del servidor. |
| `functions/sitemap.xml.js` | `/sitemap.xml` |
| `functions/sitemaps/[n].js` | `/sitemaps/:n` (partes del índice de sitemaps). |
| `functions/_comun/seo.js`, `functions/_comun/sitemap.js` | Código compartido; no atienden rutas. |

## Cómo publicar una versión nueva del sitio

**Orden:** si el cambio del sitio depende de un cambio de la API, primero se despliega la API (`DESPLIEGUE-API.md`) y después el sitio.

1. Compilar: `cd frontend && npm run build` (es `tsc -b && vite build`) → carpeta `dist/`.
2. **[Pablo]** Credenciales de Cloudflare en la terminal. `TRASPASO.md` §9 anota que `wrangler login` interactivo falló y que se usó un token de API por variable de entorno. El token nunca se escribe en el repo ni en el chat.
3. Subir, desde `frontend/`: `npx wrangler pages deploy dist --project-name zass --branch main` (el comando que registra `TRASPASO.md`). `wrangler` no está en `package.json`: `npx` lo descarga.
4. Comprobar desde fuera: `https://zass.cl/`, `https://zass.cl/api/salud`, `https://zass.cl/sitemap.xml`.

## Configuración del proyecto Pages (ya hecha; referencia)

- **Variable `API_ORIGEN`**: el origen `https://…` de la API en Render (el valor está en `TRASPASO.md` §3). Sin ella, `/api/*` responde 503 «API no configurada», la ficha sale sin meta y el sitemap responde 503.
- **Dominios**: `zass.cl` y `www.zass.cl` responden con HTTPS. El DNS de `zass.cl` está en Cloudflare (`TRASPASO.md` §2).
- **En la API**: `Zass__ExigirHttps=true`, `Zass__ProxiesDeConfianza` y `Zass__SaltosDeProxy` (ver abajo y `DESPLIEGUE-API.md`).

## La IP real del visitante y el límite de peticiones — DEFECTO ABIERTO

**El problema:** el límite de peticiones se cuenta por IP. Detrás de Cloudflare la conexión que ve la API es la de un proxy: si la API no recupera la IP del visitante, **muchos visitantes comparten un contador** (100 peticiones cada 15 minutos en el grupo público, `Seguridad.cs`).

**Lo que hace el código:**

- La Pages Function (`functions/api/[[ruta]].js`) **sobreescribe** `X-Forwarded-For` con `CF-Connecting-IP` y fija `X-Forwarded-Proto: https` y `X-Forwarded-Host`. Las Functions de la ficha y del sitemap también mandan `X-Forwarded-For` con `CF-Connecting-IP`.
- La API (`ProxiesDeConfianza.cs`, `UseForwardedHeaders`) solo cree `X-Forwarded-For` y `X-Forwarded-Proto` **si la conexión directa viene de una red listada en `Zass:ProxiesDeConfianza`**. Con la lista vacía (por omisión) no lee ninguna cabecera reenviada. Recorre `X-Forwarded-For` de derecha a izquierda, se detiene en la primera dirección que no es de confianza y recorre como máximo `Zass:SaltosDeProxy` entradas (por omisión `2` en el código; `render.yaml` fija `3`).

**El defecto:** en producción, entrando por `zass.cl`, la API registra una IP de Cloudflare y no la del visitante (`ESTADO.md`, «Publicación en línea»). **Sigue abierto y su causa no está confirmada.**

**Lo medido** (GET, 12-nov-2026):

- El dominio `onrender.com` de la API responde con `Server: cloudflare` y `CF-RAY`: **Render está él mismo detrás de Cloudflare**. Entre la Pages Function y el contenedor hay, entonces, al menos otro salto de Cloudflare además del balanceador de Render.
- Cloudflare documenta que **añade** la IP del cliente a un `X-Forwarded-For` que ya existe; no lo borra. Por eso la explicación anterior («la Function pone `X-Forwarded-For` pero se pierde en el camino») **no se sostiene tal cual**.

**Hipótesis, sin confirmar:**

1. *(principal)* La cadena de `X-Forwarded-For` que llega al contenedor trae más entradas de las que `Zass__SaltosDeProxy=3` permite recorrer, y la API se detiene en una IP de Cloudflare antes de llegar a la del visitante.
2. Alguna red de la cadena no está en `Zass__ProxiesDeConfianza`, y el recorrido se corta ahí.

**Lo que falta para cerrarlo:** ver qué `X-Forwarded-For` llega de verdad al contenedor (cuántas entradas y cuáles) y recién entonces decidir. **No hay ningún arreglo aplicado.** Cambiar `Zass__SaltosDeProxy` es configuración de seguridad en producción: lo decide y lo hace Pablo. El arreglo que proponían documentos anteriores (cabecera propia desde la Function + `ForwardedForHeaderName` en la API) tampoco está aplicado y puede no hacer falta.

**Mientras siga abierto:** el límite de peticiones de quien entra por el sitio se cuenta por salida de Cloudflare, no por visitante.

### Cómo llenar `Zass:ProxiesDeConfianza`

Es una lista de IP/CIDR separada por comas, en la variable de entorno `Zass__ProxiesDeConfianza` de la API (en `render.yaml` va con `sync: false`: el valor se escribe en el panel de Render, no en el repo). Debe contener **dos cosas**:

1. **Los rangos de IP de Cloudflare.** Los publica Cloudflare oficialmente; **no los copies de memoria ni de este archivo**, cambian:
   * Página: <https://www.cloudflare.com/ips/> (listas IPv4 e IPv6 en texto)
   * Texto plano: <https://www.cloudflare.com/ips-v4> y <https://www.cloudflare.com/ips-v6>
   * API: <https://api.cloudflare.com/client/v4/ips>

   Para armar la variable (una línea, separada por comas):
   `curl -s https://www.cloudflare.com/ips-v4 https://www.cloudflare.com/ips-v6 | tr '\n' ',' | sed 's/,$//'`
2. **La red desde la que el balanceador del alojamiento llega a la API.** En Render llega desde `10.0.0.0/8` (red interna; es el mismo valor que da `DESPLIEGUE-API.md`, sección 4). Si necesitas acceso local/directo, agrega `127.0.0.1` y `::1`. Si la API ve como conexión directa una IP que no está en la lista, ignora todo `X-Forwarded-For`.

La API rechaza `0.0.0.0/0` y `::/0` (`ProxiesDeConfianza.cs`).

Vuelve a consultar los rangos de Cloudflare de vez en cuando y actualiza la variable. **Comprobación** (es también la que dirá si el defecto está cerrado): inicia sesión desde tu celular con datos móviles y mira `SELECT ip_origen FROM sesiones ORDER BY creada_en DESC LIMIT 3;`: debe aparecer tu IP pública, no una de Cloudflare ni de Render. Mientras el defecto siga abierto, no la pasa.

**Riesgo residual:** los rangos de Cloudflare son compartidos por todos sus clientes (ver `DESPLIEGUE-API.md`, sección 4). No publiques la URL directa de la API.

### Pendiente de decisión

**100 peticiones públicas cada 15 min por IP es poco** para un sitio abierto, y más mientras el defecto de arriba haga que varios visitantes compartan contador. Hay que medir y decidir el valor real.

## Secretos

Nada de esto lleva claves en el código. `API_ORIGEN` es una variable del proyecto de Cloudflare, no un archivo del repositorio.

## SEO: fichas con meta del lado del servidor, sitemap y robots

Google ve una SPA casi vacía, así que hay tres Functions y dos archivos estáticos. Tienen pruebas con `Request`/`Response` y un `fetch` simulado (`src/pruebas-functions/`), y el 12-nov-2026 se comprobó en `zass.cl` con GET: `/sitemap.xml` responde 200 con XML y `s-maxage=3600`, `/robots.txt` 200, y una ficha inexistente responde 404 con `s-maxage=60`. En un primer intento, minutos antes, se vio también el camino degradado: ficha 200 con `s-maxage=10` y sitemap «no disponible».

| Ruta | Archivo | Qué hace |
|---|---|---|
| `/prestadores/:slug` | `functions/prestadores/[slug].js` | Pide la ficha a la API (`GET /api/prestadores/{slug}`) y devuelve `index.html` con `<title>`, descripción, canonical, Open Graph, Twitter Card y JSON-LD (`ProfessionalService`) de esa ficha. Ficha inexistente: la app con `noindex` y **404**, `s-maxage=60`. API caída, lenta (4 s) o con respuesta rara: la app normal, **200**, `s-maxage=10`; nunca un 500. Ficha ok: `s-maxage=300`. |
| `/sitemap.xml` | `functions/sitemap.xml.js` | Portada, páginas fijas y TODAS las fichas visibles (lee `GET /api/prestadores/mapa` por cursor, de a 1000). Con más de 50 000 URLs entrega un **índice** que apunta a `/sitemaps/1.xml`, `/sitemaps/2.xml`… (`functions/sitemaps/[n].js`). Caché de 1 h en el borde. Si la API falla: 503 con `Retry-After`, nunca un sitemap a medias. |
| `/robots.txt` | `public/robots.txt` | Apunta al sitemap y bloquea `/api/`, `/mi-ficha`, `/interno`, `/entrar`, `/crear-cuenta`, `/recuperar-contrasena`, `/restablecer-contrasena`, `/verificar-correo`. |
| imagen al compartir | `public/og-zass.png` | Se regenera con `node scripts/generar-imagen-og.mjs` (Playwright sobre `scripts/og-plantilla.html`). |

**Variables del proyecto Pages** (además de `API_ORIGEN`):

- `SITIO_ORIGEN` (opcional, `https://zass.cl` por omisión): el origen que va en canonical, Open Graph y sitemap. Tiene que ser `https://…`; cualquier otra cosa se ignora.
- `SITEMAP_MAX_PAGINAS` (opcional, 45 por omisión): páginas de 1000 fichas que el sitemap recorre. El código asume **50 subpeticiones por invocación en el plan gratis de Cloudflare** (`functions/_comun/sitemap.js`), así que 45 000 fichas es el techo; más allá, el sitemap se corta en silencio. Con plan de pago se puede subir la variable, y el índice de sitemaps ya está listo. Con menos de 45 000 fichas no hay nada que tocar.

**Lo que NO es obvio:**

1. **`_headers` no se aplica a las respuestas de las Functions**, solo a los archivos estáticos. Por eso `functions/_comun/seo.js` repite las mismas cabeceras (`CABECERAS_DE_SEGURIDAD`) en la ficha y en el sitemap, y una prueba compara ese objeto con el bloque `/*` de `public/_headers` para que no se separen. Si cambias la CSP en `_headers`, cámbiala también ahí.
2. **La CSP no necesita cambios.** El JSON-LD es `<script type="application/ld+json">`: un bloque de datos, no se ejecuta, y `script-src 'self'` sigue prohibiendo scripts en línea. Una prueba comprueba que la CSP no tenga `unsafe-inline` en `script-src`.
3. `index.html` lleva las marcas `<!--seo:inicio-->` y `<!--seo:fin-->`: lo que está entre ellas es lo que la Function reemplaza por las etiquetas de cada ficha. Si las quitas, las fichas salen sin meta (una prueba lo detecta). Vite las conserva al compilar (comprobado).
4. Todo texto que viene de la API (nombres, descripciones, zonas) es contenido de usuarios: se escapa para HTML y se escapa aparte para JSON-LD (`<`, `>`, `&`, separadores de línea), y solo se usa una lista blanca de campos (nombre, oficio, zonas, descripción). El contacto (teléfono, WhatsApp, correo, sitio) **no** va en las etiquetas aunque la API lo entregue.
5. Las Functions de la ficha y del sitemap mandan `X-Forwarded-For` con la IP del visitante (como el proxy de `/api`), con la intención de que el límite de peticiones de la API no cuente a Cloudflare entero como un solo cliente. **Mientras el defecto de la IP siga abierto, esa intención no está comprobada en producción.** El sitemap hace una llamada a `/api/prestadores/mapa` por cada 1000 fichas y su respuesta se guarda 1 h en el borde (grupo de límite **P**, 100 cada 15 min por IP): con 45 000 fichas son 45 llamadas por regeneración, bajo el límite.
6. Una ficha cuyo slug sea exactamente `mapa` no sería alcanzable por la API (la ruta literal `/api/prestadores/mapa` gana a `/api/prestadores/{slug}`). Es un caso casi imposible, pero conviene reservar ese slug en el backend cuando se toque la generación de slugs.
