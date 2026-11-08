# Subir ZASS! a Cloudflare

**Estado: preparado, NO publicado.** Falta una decisión (dónde corre la API) y la sesión de Pablo en Cloudflare.

## Qué va a Cloudflare y qué no

| Pieza | ¿Puede ir en Cloudflare Pages? |
|---|---|
| **El sitio** (`frontend/`, 381 KB ya compilado) | **Sí.** Es estático. |
| **La API** (.NET 8) | **No.** Cloudflare Pages/Workers no ejecutan .NET. Hay que alojarla en otro lado. |
| **La base de datos** (PostgreSQL + PostGIS) | **No.** Tampoco. Hay que usar un PostgreSQL con PostGIS (Neon, Supabase, Azure…). |

Publicar solo el sitio sin API deja una página que dice «No se pudo conectar» en todo: no sirve para mostrar.

## Lo que ya está listo en el repositorio

- `public/_redirects`: que `zass.cl/buscar`, `/barrios`… abran la aplicación (el sitio tiene varias páginas, y sin esto un enlace directo da 404).
- `public/_headers`: cabeceras de seguridad (CSP, HSTS, nosniff, X-Frame-Options, Permissions-Policy) y caché larga para los archivos con huella.
- `functions/api/[[ruta]].js`: reenvía `zass.cl/api/*` a la API, para que las cookies de sesión funcionen sin CORS.

## Pasos (los que solo puede hacer Pablo están marcados)

1. **[Pablo]** Decidir dónde corre la API y la base (ver «Decisión pendiente»).
2. Compilar: `cd frontend && npm run build` → carpeta `dist/`.
3. **[Pablo]** Iniciar sesión en Cloudflare desde esta terminal: `! npx wrangler login` (abre el navegador).
4. Crear el proyecto y subir: `npx wrangler pages deploy dist --project-name zass`.
5. En el proyecto, **Settings → Variables**: `API_ORIGEN = https://api.zass.cl` (o donde esté la API).
6. **[Pablo]** Si `zass.cl` ya está en Cloudflare (nameservers), en el proyecto: **Custom domains → zass.cl**. Si el dominio está en otro registrador, hay que apuntar sus nameservers a Cloudflare o crear un CNAME.
7. En la API: `Zass:ExigirHttps=true`, cookies con `Secure` (ya), y confiar en `X-Forwarded-For` (ver abajo).

## Decisión pendiente: dónde corre la API

| Opción | Cuándo conviene |
|---|---|
| **Túnel de Cloudflare desde el PC de Pablo** (`cloudflared`) | **Solo para mostrar** a alguien por unos días. El PC tiene que estar prendido, y la base es la local. No es producción. |
| **Azure** (App Service + PostgreSQL Flexible Server con PostGIS) | Es lo que dice el diseño del proyecto. Lo más completo y lo más caro. |
| **Render / Fly.io + Neon** (PostGIS incluido) | Más barato y rápido de montar; suficiente para empezar. |

## Dos cosas que hay que arreglar en la API ANTES de salir a producción

1. **El límite de peticiones se cuenta por IP**, y hoy la API lee la IP de la conexión directa. Detrás de Cloudflare esa IP es siempre la de Cloudflare: **toda la ciudad compartiría un contador** (100 peticiones cada 15 minutos para la búsqueda pública). Hay que activar `UseForwardedHeaders` confiando solo en el proxy, y tener una prueba que lo demuestre.
2. **100 peticiones públicas cada 15 min por IP es poco** para un sitio abierto: una portada hace 3 y escribir en la barra hace varias. Hay que medir y decidir el valor real.

## Secretos

Nada de esto lleva claves en el código. `API_ORIGEN` es una variable del proyecto de Cloudflare, no un archivo del repositorio.

## SEO: fichas con meta del lado del servidor, sitemap y robots

Google ve una SPA casi vacía, así que hay tres Functions y dos archivos estáticos (probados con `Request`/`Response` y un `fetch` simulado, **no en Cloudflare real**):

| Ruta | Archivo | Qué hace |
|---|---|---|
| `/prestadores/:slug` | `functions/prestadores/[slug].js` | Pide la ficha a la API (`GET /api/prestadores/{slug}`) y devuelve `index.html` con `<title>`, descripción, canonical, Open Graph, Twitter Card y JSON-LD (`ProfessionalService`) de esa ficha. Ficha inexistente: la app con `noindex` y **404**. API caída, lenta (4 s) o con respuesta rara: la app normal, **200**, `s-maxage=10`; nunca un 500. Ficha ok: `s-maxage=300`. |
| `/sitemap.xml` | `functions/sitemap.xml.js` | Portada, páginas fijas y TODAS las fichas visibles (lee `GET /api/prestadores/mapa` por cursor, de a 1000). Con más de 50 000 URLs entrega un **índice** que apunta a `/sitemaps/1.xml`, `/sitemaps/2.xml`… (`functions/sitemaps/[n].js`). Caché de 1 h en el borde. Si la API falla: 503 con `Retry-After`, nunca un sitemap a medias. |
| `/robots.txt` | `public/robots.txt` | Apunta al sitemap y bloquea `/api/`, `/mi-ficha`, `/interno`, `/entrar`, `/crear-cuenta`, `/recuperar-contrasena`, `/restablecer-contrasena`, `/verificar-correo`. |
| imagen al compartir | `public/og-zass.png` | 1200×630. Se regenera con `node scripts/generar-imagen-og.mjs` (Playwright sobre `scripts/og-plantilla.html`). |

**Variables del proyecto Pages** (además de `API_ORIGEN`):

- `SITIO_ORIGEN` (opcional, `https://zass.cl` por omisión): el origen que va en canonical, Open Graph y sitemap. Tiene que ser `https://…`; cualquier otra cosa se ignora.
- `SITEMAP_MAX_PAGINAS` (opcional, 45 por omisión): páginas de 1000 fichas que el sitemap recorre. **Plan gratis de Cloudflare: 50 subpeticiones por invocación**, así que 45 000 fichas es el techo; más allá, el sitemap se corta en silencio. Con plan de pago se puede subir (hasta 1000 subpeticiones) y el índice de sitemaps ya está listo. Con menos de 45 000 fichas no hay nada que tocar.

**Lo que NO es obvio:**

1. **`_headers` no se aplica a las respuestas de las Functions**, solo a los archivos estáticos. Por eso `functions/_comun/seo.js` repite las mismas cabeceras (`CABECERAS_DE_SEGURIDAD`) en la ficha y en el sitemap, y una prueba compara ese objeto con el bloque `/*` de `public/_headers` para que no se separen. Si cambias la CSP en `_headers`, cámbiala también ahí.
2. **La CSP no necesita cambios.** El JSON-LD es `<script type="application/ld+json">`: un bloque de datos, no se ejecuta, y `script-src 'self'` sigue prohibiendo scripts en línea. Una prueba comprueba que la CSP no tenga `unsafe-inline` en `script-src`.
3. `index.html` lleva las marcas `<!--seo:inicio-->` y `<!--seo:fin-->`: lo que está entre ellas es lo que la Function reemplaza por las etiquetas de cada ficha. Si las quitas, las fichas salen sin meta (una prueba lo detecta). Vite las conserva al compilar (comprobado).
4. Todo texto que viene de la API (nombres, descripciones, zonas) es contenido de usuarios: se escapa para HTML y se escapa aparte para JSON-LD (`<`, `>`, `&`, separadores de línea), y solo se usa una lista blanca de campos (nombre, oficio, zonas, descripción). El contacto (teléfono, WhatsApp, correo, sitio) **no** va en las etiquetas aunque la API lo entregue.
5. La Function manda `X-Forwarded-For` con la IP del visitante (como el proxy de `/api`), para que el límite de peticiones de la API no cuente a Cloudflare entero como un solo cliente. El sitemap hace ≈ N/1000 llamadas a `/api/prestadores/mapa` cada hora como mucho (grupo de límite **P**, 100 cada 15 min por IP): con 45 000 fichas son 45 llamadas por regeneración, bajo el límite.
6. Una ficha cuyo slug sea exactamente `mapa` no sería alcanzable por la API (la ruta literal `/api/prestadores/mapa` gana a `/api/prestadores/{slug}`). Es un caso casi imposible, pero conviene reservar ese slug en el backend cuando se toque la generación de slugs.
