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
7. En la API: `Zass:ExigirHttps=true`, cookies con `Secure` (ya), y llenar `Zass:ProxiesDeConfianza` (ver «Cómo llenar `Zass:ProxiesDeConfianza`» más abajo; la guía completa de la API está en `DESPLIEGUE-API.md`).

## Decisión pendiente: dónde corre la API

| Opción | Cuándo conviene |
|---|---|
| **Túnel de Cloudflare desde el PC de Pablo** (`cloudflared`) | **Solo para mostrar** a alguien por unos días. El PC tiene que estar prendido, y la base es la local. No es producción. |
| **Azure** (App Service + PostgreSQL Flexible Server con PostGIS) | Es lo que dice el diseño del proyecto. Lo más completo y lo más caro. |
| **Render / Fly.io + Neon** (PostGIS incluido) | Más barato y rápido de montar; suficiente para empezar. |

## La IP real del visitante y el límite de peticiones (resuelto en la API)

**El problema:** el límite de peticiones se cuenta por IP. Detrás de Cloudflare la conexión que ve la API es la de Cloudflare: sin corregirlo, **toda la ciudad compartiría un contador** (100 peticiones cada 15 minutos para la búsqueda pública).

**Cómo está resuelto:** la Pages Function (`functions/api/[[ruta]].js`) fija `X-Forwarded-For` con la IP real (`CF-Connecting-IP`) y `X-Forwarded-Proto: https`; la API (`UseForwardedHeaders`) solo les cree **si la conexión directa viene de una red listada en `Zass:ProxiesDeConfianza`**. Vacía (por omisión) no lee ninguna cabecera reenviada; una IP no listada que falsifique `X-Forwarded-For` se sigue contando por su IP real. Hay pruebas por HTTP que lo demuestran.

### Cómo llenar `Zass:ProxiesDeConfianza`

Es una lista de IP/CIDR separada por comas, en la variable de entorno `Zass__ProxiesDeConfianza` de la API. Debe contener **dos cosas**:

1. **Los rangos de IP de Cloudflare.** Los publica Cloudflare oficialmente; **no los copies de memoria ni de este archivo**, cambian:
   * Página: <https://www.cloudflare.com/ips/> (listas IPv4 e IPv6 en texto)
   * Texto plano: <https://www.cloudflare.com/ips-v4> y <https://www.cloudflare.com/ips-v6>
   * API: <https://api.cloudflare.com/client/v4/ips>

   Para armar la variable (una línea, separada por comas):
   `curl -s https://www.cloudflare.com/ips-v4 https://www.cloudflare.com/ips-v6 | tr '\n' ',' | sed 's/,$//'`
2. **La red desde la que el balanceador del alojamiento llega a la API** (en Render, su red interna; confírmala en la documentación de Render: la API ve esa IP como conexión directa y, si no está en la lista, ignora todo `X-Forwarded-For`).

Vuelve a consultar los rangos de Cloudflare de vez en cuando (cambian rara vez, pero cambian) y actualiza la variable. Comprobación tras desplegar: inicia sesión desde tu celular con datos móviles y mira `SELECT ip_origen FROM sesiones ORDER BY creada_en DESC LIMIT 3;`: debe aparecer tu IP pública, no una de Cloudflare ni de Render.

**Riesgo residual:** los rangos de Cloudflare son compartidos por todos sus clientes (ver `DESPLIEGUE-API.md`, sección 4). No publiques la URL directa de la API.

### Pendiente de decisión

**100 peticiones públicas cada 15 min por IP es poco** para un sitio abierto: una portada hace 3 y escribir en la barra hace varias. Hay que medir y decidir el valor real.

## Secretos

Nada de esto lleva claves en el código. `API_ORIGEN` es una variable del proyecto de Cloudflare, no un archivo del repositorio.
