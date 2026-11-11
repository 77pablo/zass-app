# Desplegar la API de ZASS! (Render + Neon, detrás de Cloudflare)

**Estado: preparado y probado en local; NO desplegado.** Al final hay una lista honesta de lo
que no se pudo probar sin cuentas reales.

```
Visitante ──► Cloudflare Pages (zass.cl)
                 │  Pages Function /api/*  (fija X-Forwarded-For = IP real, X-Forwarded-Proto = https)
                 ▼
              Render (zass-api, Docker)  ──►  Neon (PostgreSQL + PostGIS)
```

## 1. Qué trae el repositorio

| Pieza | Dónde |
|---|---|
| Imagen de la API (multi-etapa, usuario no root, puerto desde `PORT`, sin secretos) | `backend/Dockerfile`, `backend/.dockerignore` |
| Blueprint de Render | `render.yaml` |
| Sondas de salud | `GET /api/salud` (vivo) y `GET /api/salud/lista` (lista: consulta `SELECT 1` a la base, 3 s de espera, 503 si falla) |
| Proxies de confianza (IP real del visitante) | `Zass:ProxiesDeConfianza`, `Zass:SaltosDeProxy` |
| Validación de configuración al arrancar | `ValidacionDeArranque.cs` |
| Migraciones como paso explícito | `dotnet Zass.Api.dll --migrar` |
| Copia periódica de la base | `.github/workflows/copia-base-de-datos.yml` |

## 2. Variables de entorno

En .NET el `:` de la configuración se escribe `__` en una variable de entorno (`Zass__CadenaConexion`).

| Variable | Obligatoria | Significado |
|---|---|---|
| `PORT` | la inyecta Render | Puerto HTTP en el que escucha la API (8080 si no existe). |
| `ASPNETCORE_ENVIRONMENT` | sí (`Production`) | Todo lo que no sea `Development` se trata como producción: validación estricta y Swagger apagado. |
| `Zass__CadenaConexion` | **sí, secreta** | Cadena de Npgsql: `Host=ep-xxxx.neon.tech;Database=zass;Username=...;Password=...;SSL Mode=Require`. **Neon entrega una URL `postgresql://...`: Npgsql no la entiende, conviértela a esta forma.** En producción sin `SSL Mode=Require` la API no arranca. |
| `Zass__ClaveDireccionesBase64` | **sí, secreta** | Clave AES-256 que cifra las direcciones de los prestadores: 32 bytes en Base64 (por ejemplo `openssl rand -base64 32`). **Si se pierde, las direcciones guardadas no se pueden descifrar**: guárdala también fuera de Render. No la cambies con datos ya guardados. |
| `Zass__ExigirHttps` | no (por omisión `true`) | En producción no puede ser `false`: la API se niega a arrancar. |
| `Zass__ProxiesDeConfianza` | sí, detrás de proxy | Lista (separada por comas) de IP/CIDR desde las que la API cree `X-Forwarded-For` y `X-Forwarded-Proto`. Vacía = no se lee ninguna cabecera reenviada. Ver sección 4. |
| `Zass__SaltosDeProxy` | no (por omisión `2`) | Cuántos proxies se recorren hacia atrás en `X-Forwarded-For` (1 a 5). En el despliegue actual se usa `3` (Cloudflare + balanceador de Render; coincide con `render.yaml` y Render). Ver el comentario en `ProxiesDeConfianza.cs`. |
| `Zass__Tasa__P/A/G/S/Salud` | no | Límites de peticiones por IP cada 15 min. Los valores por omisión son los de `seguridad.md` (Salud: 2000, grupo propio para las sondas). **No los subas en producción sin medirlo.** |

## 3. Pasos

### 3.1 Base de datos en Neon
1. Crea el proyecto en Neon (elige una región cercana a la de Render).
2. En el editor SQL de Neon, **sobre la base de la aplicación**: `CREATE EXTENSION IF NOT EXISTS postgis;`
   (las migraciones de la aplicación crean `citext` y `pgcrypto` ellas mismas; verifica en la documentación de Neon que ambas están permitidas).
3. Copia la cadena de conexión y conviértela al formato de Npgsql (sección 2).
4. **Neon pausa el cómputo por inactividad** y la primera consulta tras la pausa tarda más. La sonda `/api/salud/lista` espera 3 s: tras una pausa larga puede dar 503 una vez y recuperarse. Verifica en la página de Neon cuánto tarda en pausarse tu plan.

### 3.2 Migrar la base (paso explícito, ANTES de desplegar código nuevo)
La API **no migra al arrancar**: con varias réplicas se pisarían, un despliegue a medias dejaría el esquema a medio hacer con el código viejo encima, y la API necesitaría permisos de DDL para atender peticiones. Se migra aparte:

```bash
cd backend
export Zass__CadenaConexion="Host=...;Database=zass;Username=...;Password=...;SSL Mode=Require"
export Zass__ClaveDireccionesBase64="<la misma clave de producción>"
export ASPNETCORE_ENVIRONMENT=Production
dotnet run --project src/Zass.Api -c Release -- --migrar
# o con la imagen:  docker run --rm -e Zass__CadenaConexion -e Zass__ClaveDireccionesBase64 zass-api --migrar
```

Aplica lo pendiente y termina (código de salida 0; 1 si falla). Es idempotente: sin pendientes no hace nada. Hazlo desde tu PC o desde un trabajo de CI, mirando el resultado, y recién entonces despliega.
Antes de una migración que borre o transforme datos, haz una copia (sección 5).

### 3.3 API en Render
1. Sube el repositorio a GitHub y en Render: **New > Blueprint** apuntando al repositorio (lee `render.yaml`).
2. Render pedirá los valores de las variables `sync: false`: `Zass__CadenaConexion`, `Zass__ClaveDireccionesBase64`, `Zass__ProxiesDeConfianza`.
3. `autoDeploy` está en `false` a propósito: despliegas tú, después de migrar.
4. **El servicio gratuito de Render se duerme tras un rato sin tráfico y despertar tarda** (decenas de segundos): la primera visita tras la inactividad es lenta. Verifica límites e inactividad en la página de Render.
5. Comprueba: `https://<tu-servicio>.onrender.com/api/salud` y `/api/salud/lista`.

### 3.4 Sitio en Cloudflare
Sigue `frontend/DESPLIEGUE-CLOUDFLARE.md`, con `API_ORIGEN=https://<tu-servicio>.onrender.com`.

## 4. IP real del visitante y límite de peticiones

Detrás de proxies, la conexión que ve la API es la del proxy. Sin corregirlo, **toda la ciudad compartiría un único contador de límite**. Y creerle a `X-Forwarded-For` de cualquiera permitiría falsificar la IP y evadir el límite. La API lo resuelve así:

* Sin `Zass:ProxiesDeConfianza` no se procesa ninguna cabecera `X-Forwarded-*` (comportamiento por omisión).
* Con la lista, solo se leen si la conexión directa viene de una de esas redes. Se recorre `X-Forwarded-For` de derecha a izquierda hasta la primera IP que no es de confianza (máximo `SaltosDeProxy`): lo que un atacante escriba a la izquierda no se lee. `0.0.0.0/0` y `::/0` se rechazan al arrancar.
* El límite, `Ip(http)` de la auditoría/sesiones, `Request.IsHttps`/esquema (cookies Secure, HSTS) usan la IP y el esquema ya corregidos.

**Qué poner en `Zass__ProxiesDeConfianza`:** (a) los rangos publicados de Cloudflare (ver `frontend/DESPLIEGUE-CLOUDFLARE.md`), (b) la red desde la que el balanceador de Render llega al contenedor (`10.0.0.0/8`, medido: llega desde 10.x) y, si hace falta para acceso local/directo, `127.0.0.1` y `::1`.
Tras el primer despliegue, comprueba mirando qué IP guarda la API (sección 7, punto 4). Si en `ip_origen` aparece una IP de Render o de Cloudflare en vez de la del visitante, falta esa red en la lista.

**Riesgo residual (conócelo):** los rangos de Cloudflare son compartidos por todos sus clientes. Alguien con su propia cuenta de Cloudflare podría pedirle a la API directamente (salvo que ocultes la URL de Render) con un `X-Forwarded-For` inventado y esquivar el límite por IP. La mitigación completa es un secreto compartido entre la Pages Function y la API (cabecera que la API exige); no está implementado. Mientras tanto, no publiques la URL `onrender.com`.

## 5. Copias de seguridad

Neon tiene sus propias restauraciones a un punto del tiempo, pero su alcance y retención dependen del plan: **verifícalo en la página del proveedor** y no lo tomes como tu única copia.

Copia propia y gratuita, **fuera del servidor de la base**: `.github/workflows/copia-base-de-datos.yml` ejecuta `pg_dump` cada día con GitHub Actions, cifra el volcado con `gpg` y lo guarda como artefacto del repositorio (privado). Requiere dos secretos del repositorio: `NEON_URL_COPIAS` (URL `postgresql://` de Neon) y `FRASE_COPIAS` (guárdala fuera de GitHub). Los artefactos caducan (el flujo pide 30 días; verifica los límites de GitHub): para una copia de largo plazo descarga una de vez en cuando a un disco tuyo.

Restaurar: `gpg -d zass-AAAAMMDD.dump.gpg > zass.dump` y `pg_restore --no-owner -d "<url de una base vacía con postgis>" zass.dump`. **Ensaya una restauración antes de necesitarla.**

## 6. Cookies y dominio

Las cookies (`zass_sesion`, `zass_refresco`) son `HttpOnly`, `Secure`, `SameSite=Lax` y **sin `Domain`**; la antifalsificación (`zass_csrf`) es `Secure` (no `HttpOnly`, el sitio debe leerla), `Lax`. Con el sitio y la API en el MISMO origen (`zass.cl`, la Pages Function reenvía `/api/*`) el navegador las trata como de primera parte: no hace falta CORS y no se añadió. Las cookies no tienen `Domain`, así que quedan atadas al host exacto que respondió.

Si algún día el sitio y la API van en subdominios distintos (`zass.cl` y `api.zass.cl`) cambiaría: (1) habría que habilitar CORS con `AllowCredentials` y origen explícito (nunca `*`); (2) el sitio tendría que llamar con `credentials: 'include'`; (3) como `zass.cl` y `api.zass.cl` son el mismo *sitio* (mismo dominio registrable), `SameSite=Lax` bastaría para que las cookies viajen en `fetch` con `credentials: 'include'`; con dominios registrables distintos haría falta `SameSite=None; Secure`, que reabre el CSRF y obligaría a revisar esa defensa; (4) la política de contenido (`connect-src`) y la Pages Function dejarían de ser necesarias o cambiarían. Por eso se recomienda mantener un solo origen.

## 7. Lista de verificación previa a salir

1. [ ] `dotnet test` en verde (en `backend/`, con `ZASS_CADENA_PRUEBAS` apuntando a una base de pruebas).
2. [ ] PostGIS activado en Neon; `--migrar` ejecutado contra la base de producción con resultado 0.
3. [ ] Variables de Render completas; `Zass__ClaveDireccionesBase64` guardada además en un lugar seguro fuera de Render.
4. [ ] `Zass__ProxiesDeConfianza` con rangos de Cloudflare **y** la red del balanceador de Render. Tras iniciar sesión una vez desde tu celular con datos móviles: `SELECT ip_origen FROM sesiones ORDER BY creada_en DESC LIMIT 3;` debe mostrar TU IP pública, no la de Cloudflare/Render.
5. [ ] `/api/salud` y `/api/salud/lista` dan 200; apagando la base (o con una cadena errónea en un entorno de prueba) `lista` da 503.
6. [ ] `/swagger` da 404 en producción.
7. [ ] Las cookies de sesión salen `Secure; HttpOnly; SameSite=Lax` (herramientas de desarrollo del navegador).
8. [ ] Probada una restauración de copia; secretos `NEON_URL_COPIAS` y `FRASE_COPIAS` puestos y el flujo ejecutado una vez a mano.
9. [ ] La URL `onrender.com` no está publicada en ninguna parte.
10. [ ] Decidido el valor real del límite público (100 cada 15 min por IP es poco para un sitio abierto: una portada hace varias peticiones).

## 8. Lo que NO se pudo probar (honestidad)

* **La imagen Docker:** Docker no está instalado en esta máquina; el `Dockerfile` **no se construyó ni ejecutó**. Sí se comprobó que `dotnet publish -c Release` compila; que la API publicada, con `--migrar`, sale con 0 contra una base local ya migrada (en Development: la base local no tiene SSL); que en Production esa misma cadena sin SSL se rechaza con un mensaje claro; y, por prueba de integración, que `--migrar` aplica todo sobre una base vacía y es idempotente.
* **Render:** no hay cuenta; `render.yaml` no se validó contra Render (claves como `dockerContext` y `healthCheckPath` según su documentación a la fecha; verifícalas al crear el Blueprint). Cuántas cabeceras `X-Forwarded-For` añade el balanceador de Render y desde qué red llega al contenedor: **sin verificar**.
* **Neon:** no hay cuenta; no se probó `CREATE EXTENSION postgis`, ni la pausa por inactividad, ni la conexión SSL real.
* **Cloudflare:** no se probó la Pages Function de extremo a extremo; las pruebas de proxy simulan la IP remota y las cabeceras dentro del servidor de pruebas.
* **El flujo de GitHub Actions de copias:** no se ejecutó.
* Las cifras de los planes gratuitos (horas, almacenamiento, tiempos de inactividad) **no se citan: verifícalas en la página de cada proveedor.**
