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
