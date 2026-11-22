# Desplegar la API de ZASS! (Render + Neon, detrás de Cloudflare)

**Estado al 14-nov-2026: DESPLEGADO y respondiendo en producción.** `https://zass.cl/api/salud`
da `{"estado":"vivo"}` y `https://zass.cl/api/salud/lista` da `{"estado":"lista"}` (200), o sea
que la API corre en Render, la Pages Function de Cloudflare reenvía `/api/*` y la consulta
`SELECT 1` contra Neon responde. La sección 8 ya no dice «nada se pudo probar»: dice qué quedó
comprobado y qué sigue sin comprobarse.

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
| Despertador de la API | `.github/workflows/despertar-api.yml`. **Desactivado desde el 20-nov-2026:** la API pasó al plan pagado `0.5c-512mb` y ya no se duerme. Solo servía mientras estuvo en el plan gratis |

## 2. Variables de entorno

En .NET el `:` de la configuración se escribe `__` en una variable de entorno (`Zass__CadenaConexion`).

| Variable | Obligatoria | Significado |
|---|---|---|
| `PORT` | la inyecta Render | Puerto HTTP en el que escucha la API (8080 si no existe). |
| `SIXLABORS_LICENSE_KEY` | **sí, al CONSTRUIR, secreta** | Licencia de ImageSharp (fotos de perfil). Desde la 4.0 la librería la exige al compilar en Release, y sin ella **el build de Render falla** («No Six Labors license found»; pasó el 20-nov-2026). Es la licencia comunitaria gratuita, pedida en licensing.sixlabors.com. Va el texto **completo** de la licencia, sin escapar. Render la pasa al `Dockerfile` como build arg; la imagen final no la lleva. |
| `ASPNETCORE_ENVIRONMENT` | sí (`Production`) | Todo lo que no sea `Development` se trata como producción: validación estricta y Swagger apagado. |
| `Zass__CadenaConexion` | **sí, secreta** | Cadena de Npgsql: `Host=ep-xxxx.neon.tech;Database=zass;Username=...;Password=...;SSL Mode=Require`. **Neon entrega una URL `postgresql://...`: Npgsql no la entiende, conviértela a esta forma.** En producción sin `SSL Mode=Require` la API no arranca. |
| `Zass__ClaveDireccionesBase64` | **sí, secreta** | Clave AES-256 que cifra las direcciones de los prestadores: 32 bytes en Base64 (por ejemplo `openssl rand -base64 32`). **Si se pierde, las direcciones guardadas no se pueden descifrar**: guárdala también fuera de Render. No la cambies con datos ya guardados. |
| `Zass__ExigirHttps` | no (por omisión `true`) | En producción no puede ser `false`: la API se niega a arrancar. |
| `Zass__ProxiesDeConfianza` | sí, detrás de proxy | Lista (separada por comas) de IP/CIDR desde las que la API cree `X-Forwarded-For` y `X-Forwarded-Proto`. Vacía = no se lee ninguna cabecera reenviada. Ver sección 4. |
| `Zass__SecretoDeOrigen` | no, pero **recomendada** (secreta) | P-11. El secreto que la Pages Function manda en la cabecera `X-Origen-Zass`. Con él puesto, la API responde **403** a toda petición que no lo traiga (salvo `/api/salud`, `/api/salud/lista` y los dos avisos de Flow). Sin él, la API se comporta como siempre y en producción lo advierte en el registro. Si está puesto y tiene menos de 32 caracteres, o espacios o tildes, la API **no arranca**. Tiene que ser el mismo valor que `SECRETO_ORIGEN` en Cloudflare Pages. Se pone **después** de esa: ver la sección 4.1. |
| `Zass__SaltosDeProxy` | no (por omisión `2`) | Cuántos proxies se recorren hacia atrás en `X-Forwarded-For` (1 a 5). En el despliegue actual se usa `3`, y lo fija `render.yaml`. Son tres porque `zass-api.onrender.com` está él mismo detrás de Cloudflare (medido el 11-nov-2026: CNAME a `gcp-us-west1-1.origin.onrender.com.cdn.cloudflare.net`, responde con `Server: cloudflare` y `CF-RAY`). La cadena deja tres entradas en `X-Forwarded-For`: el visitante (que escribe la Pages Function), la salida de esa Function (que añade el Cloudflare de Render) y el borde de Cloudflare (que añade el balanceador interno de Render). Ver el comentario en `ProxiesDeConfianza.cs`. |
| `Zass__Tasa__P/A/G/S/Salud` | no | Límites de peticiones por IP cada 15 min. Los valores por omisión son los de `seguridad.md` (Salud: 2000, grupo propio para las sondas). **No los subas en producción sin medirlo.** |
| `Zass__UrlPublica` | **sí en la práctica** | Raíz pública del sitio (`https://zass.cl`). Sin ella `ProcesadorDeCorreo` **no envía ningún correo** —los enlaces saldrían rotos— y los avisos se quedan en la cola; también la usa el retorno de la pasarela. No la valida `ValidacionDeArranque`: la API arranca igual y el fallo solo se ve en el registro. |
| `Zass__Pasarela` | **sí en producción** | Qué adaptador de pagos se usa. Por omisión resuelve a la pasarela simulada, y en `Production` eso hace **fallar el arranque** a propósito (`RegistroDeServicios.ClavePasarela`): un nombre vacío o desconocido no cae en la simulada. Hoy el único adaptador que existe en el código es el simulado. |
| `Zass__Correo__Proveedor` | no (por omisión `Ninguno`) | `Ninguno`, `Brevo` o `Resend`. Con `Ninguno` los avisos se encolan y nunca salen (es el modo de desarrollo). |
| `Zass__Correo__ApiKey` | **sí si hay proveedor, secreta** | Clave del proveedor. Si hay proveedor y falta, el arranque se niega (`ValidadorOpcionesCorreo`). Solo por variable de entorno, nunca en el repositorio. |
| `Zass__Correo__Remitente` | **sí si hay proveedor** | Dirección remitente, de un dominio verificado en el proveedor; debe contener `@` o el arranque se niega. |
| `Zass__Correo__NombreRemitente` | no (por omisión `ZASS!`) | Nombre visible del remitente. |
| `Zass__Correo__MaximoIntentos` / `EsperaBaseSegundos` / `EsperaMaximaSegundos` | no (`5` / `60` / `3600`) | Reintentos del envío y espera, que se duplica en cada intento. `MaximoIntentos` menor que 1 impide arrancar. |
| `Zass__Correo__TamanoDeLote` / `SondeoSegundos` / `TiempoLimiteHttpSegundos` | no (`10` / `15` / `15`) | Cuántos avisos por vuelta, cada cuánto mira la cola y cuánto espera al proveedor. `TamanoDeLote` menor que 1 impide arrancar. |
| `Zass__Correo__ReservaSegundos` / `PurgaDeFallidasHoras` / `CaducaDias` | no (`120` / `24` / `7`) | Plazo que un trabajador reserva un aviso, cuándo se purga la carga de una fallida, y a los cuántos días un aviso sin enviar se **anula** en vez de salir tarde. |
| `Zass__Fondo__Activo` | no (por omisión `true`) | Planificador de trabajos A-09. Apagarlo deja sin correr vencimientos de suscripción y avisos de citas. Las pruebas de la API lo apagan. |
| `Zass__Fondo__SondeoSegundos` / `IntervaloHoras` / `CitasCadaMinutos` | no (`300` / `24` / `15`) | Cada cuánto pregunta por trabajos vencidos, cada cuánto corre el diario de suscripciones y cada cuánto revisa las citas (T5, T8, T11). |
| `Zass__Fondo__ReservaMinutos` / `EsperaTrasFalloMinutos` | no (`10` / `30`) | Cuánto se reserva un trabajo quien lo toma y cuánto espera antes de reintentar uno caído. |
| `Zass__Verificacion__PermitirSinDocumentos` | no (por omisión `false`) | Decisión de producto: mientras no exista MOD-08, permite enviar una credencial a revisión sin documentos. **En producción debe quedar en `false`** (el valor por omisión del código); solo `appsettings.Development.json` lo pone en `true`. |

## 3. Pasos

### 3.1 Base de datos en Neon
1. Crea el proyecto en Neon (elige una región cercana a la de Render).
2. En el editor SQL de Neon, **sobre la base de la aplicación**: `CREATE EXTENSION IF NOT EXISTS postgis;`
   (las migraciones de la aplicación declaran `citext` y `postgis` ellas mismas —no usan `pgcrypto`—; verifica en la documentación de Neon que ambas están permitidas).
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
2. Render pedirá los valores de las variables `sync: false`: `Zass__CadenaConexion`, `Zass__ClaveDireccionesBase64`, `Zass__SecretoAvisoPagos`, `Zass__ProxiesDeConfianza` y `Zass__SecretoDeOrigen` (esta última se deja vacía al principio: sección 4.1).
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

**Riesgo residual (conócelo):** los rangos de Cloudflare son compartidos por todos sus clientes. Alguien con su propia cuenta de Cloudflare podría pedirle a la API directamente con un `X-Forwarded-For` inventado y esquivar el límite por IP; y cualquiera que conozca la URL de Render se salta además el modo mantenimiento. La URL **ya es pública**: está en el historial del repositorio, que es público. La mitigación es el secreto de origen de la sección 4.1: **ya está implementado, pero no hace nada hasta que se pongan las dos variables.**

### 4.1 Secreto de origen (P-11): que la API solo acepte lo que llega por Cloudflare

**Cómo funciona.** Las Functions que llaman a la API (`functions/api/[[ruta]].js`, `functions/t/[segmento].js`, `functions/prestadores/[slug].js` y el mapa del sitio, `functions/_comun/sitemap.js`) agregan la cabecera `X-Origen-Zass` con el valor de la variable `SECRETO_ORIGEN` de Cloudflare Pages (`functions/_comun/origen.js`). Lo que el visitante mande con ese nombre se borra. La API, si tiene `Zass__SecretoDeOrigen`, responde **403** a toda petición sin esa cabecera exacta (comparación en tiempo constante, `SecretoDeOrigen.cs`). Si cualquiera de las dos variables falta, nada cambia: la Function no manda la cabecera y la API no la pide.

**Qué queda exento, y por qué:**

| Ruta | Por qué no se le pide el secreto |
|---|---|
| `/api/salud`, `/api/salud/lista` | Render consulta `/api/salud/lista` directo al contenedor (`healthCheckPath`); si se le pidiera el secreto daría el servicio por caído y ningún despliegue terminaría. El flujo de despertar usa `/api/salud`. |
| `/api/pagos/aviso` (API-70) y `/api/pagos/vuelta` (API-75) | Los llama Flow (su servidor, y el navegador que vuelve de flow.cl), no la Function. La URL es `Zass__Flow__UrlAvisos`, una variable de Render que no está en el repositorio: desde aquí no se puede saber si apunta a `zass.cl` o a `onrender.com`. Y aunque apuntara a `zass.cl`, con el sitio en mantenimiento la Function le respondería 503 a Flow. No es un agujero: ninguno de los dos cree lo que trae la petición, el adaptador le pregunta el estado a Flow con nuestra firma. |

Nada más: `/api/pagos` a secas, `/api/pagos/avisos` o `/api/saludx` sí piden el secreto (hay pruebas).

**En producción, sin el secreto, la API arranca igual** y escribe en el registro de Render una advertencia («Zass:SecretoDeOrigen no está configurado…»). No se exige a propósito: exigirlo tumbaría la API el mismo día que se despliegue este código, antes de que Cloudflare mande la cabecera. Cuándo pasar a exigirlo lo decide Pablo.

**Pasos para Pablo, EN ESTE ORDEN** (primero Cloudflare manda la cabecera, después Render la exige; al revés, el sitio entero da 403 hasta terminar):

1. **Desplegar el código** de este cambio en los dos lados, sin poner todavía ningún secreto: el sitio (`npm run build` y `npx wrangler pages deploy dist --project-name zass`) y la API (Manual Deploy en Render). Sin variables no cambia nada.
2. **Generar el secreto** en tu PC, sin pegarlo en ningún chat, archivo del repositorio ni commit. En Git Bash: `openssl rand -hex 32` (64 caracteres hexadecimales). En PowerShell: `[Convert]::ToHexString([Security.Cryptography.RandomNumberGenerator]::GetBytes(32))`. Guárdalo en tu gestor de contraseñas.
3. **Cloudflare Pages:** proyecto `zass` → *Settings* → *Variables and Secrets* → entorno **Production** → *Add* → tipo **Secret** (cifrada), nombre `SECRETO_ORIGEN`, el valor. Guardar. **Una variable nueva solo la ven los despliegues nuevos:** vuelve a desplegar (`npx wrangler pages deploy dist --project-name zass`). Desde ahora las Functions mandan la cabecera y la API todavía no la pide: nada se nota.
4. **Comprobar que zass.cl sigue andando** (en mantenimiento, con tu pase): `curl -s -o /dev/null -w '%{http_code}\n' -b 'zass_pase=<tu pase>' https://zass.cl/api/planes` → `200`.
5. **Render:** servicio `zass-api` → *Environment* → agregar `Zass__SecretoDeOrigen` con **el mismo valor** → *Save* (Render reinicia la API). En el registro del arranque ya no debe salir la advertencia del secreto de origen.
6. **Comprobar con curl** (`<servicio>` es el nombre del servicio en Render):
   * `curl -s -o /dev/null -w '%{http_code}\n' https://<servicio>.onrender.com/api/planes` → **`403`** (directo, sin la cabecera).
   * `curl -s -o /dev/null -w '%{http_code}\n' https://<servicio>.onrender.com/api/salud` → `200` (exenta: la sonda de Render sigue viva).
   * `curl -s -o /dev/null -w '%{http_code}\n' -b 'zass_pase=<tu pase>' https://zass.cl/api/planes` → `200` (por Cloudflare, con la cabecera).
   * Con el sitio ya abierto, lo mismo sin `-b`.

**Si algo sale mal:** borra `Zass__SecretoDeOrigen` en Render (*Environment*, *Save*): la API vuelve a aceptar todo. No hace falta tocar Cloudflare.

**Para cambiar el secreto sin cortar el sitio:** (1) borra `Zass__SecretoDeOrigen` en Render; (2) cambia `SECRETO_ORIGEN` en Cloudflare y vuelve a desplegar; (3) pon el valor nuevo en Render. La API acepta un solo secreto a la vez, así que ese es el orden que no corta nada.

**Lo que esto NO arregla:** la URL de Render sigue en el historial público del repositorio (y en `ESTADO.md` y `TRASPASO.md`); con el secreto puesto da igual que se conozca. El flujo de despertar (`despertar-api.yml`, desactivado desde el 20-nov) ya no la lleva escrita: la lee de la variable de repositorio `URL_API` y, si no existe, pide por `zass.cl`.

### 4.2 HSTS

La API manda `Strict-Transport-Security: max-age=63072000; includeSubDomains` (2 años). Antes producción mandaba `max-age=2592000` (30 días, medido el 21-nov-2026): el valor por omisión de `UseHsts`, que pisaba al de `UsarCabecerasDeSeguridad`. **Sin `preload`, a propósito:** entrar a la lista de precarga de los navegadores obliga a HTTPS en todo subdominio de `zass.cl`, presente y futuro, y salir tarda meses. Esa decisión es de Pablo.

## 5. Copias de seguridad

Neon tiene sus propias restauraciones a un punto del tiempo, pero su alcance y retención dependen del plan: **verifícalo en la página del proveedor** y no lo tomes como tu única copia.

Copia propia y gratuita, **fuera del servidor de la base**: `.github/workflows/copia-base-de-datos.yml` ejecuta `pg_dump` cada día con GitHub Actions, cifra el volcado con `gpg` y lo guarda como artefacto del repositorio (privado). Requiere dos secretos del repositorio: `NEON_URL_COPIAS` (URL `postgresql://` de Neon) y `FRASE_COPIAS` (guárdala fuera de GitHub). Los artefactos caducan (el flujo pide 30 días; verifica los límites de GitHub): para una copia de largo plazo descarga una de vez en cuando a un disco tuyo.

Restaurar: `gpg -d zass-AAAAMMDD.dump.gpg > zass.dump` y `pg_restore --no-owner -d "<url de una base vacía con postgis>" zass.dump`. **Ensaya una restauración antes de necesitarla.**

> **No se puede restaurar con las herramientas de este PC.** El flujo vuelca con `pg_dump` 18
> (imagen `postgres:18`, porque Neon es 18) y el `pg_restore` del clúster portable local es
> **16.4** (`herramientas\pgsqlin`): un `pg_restore` viejo no lee el archivo de una mayor más
> nueva. Para ensayar la restauración hace falta un cliente 18 —por ejemplo el mismo
> `docker run --rm postgres:18 pg_restore ...` que usa el flujo de copias— o una máquina con
> Postgres 18 instalado. Escribir el procedimiento dando por hecho que todo es 18 ya dejó un
> pendiente imposible de cumplir.

## 6. Cookies y dominio

Las cookies (`zass_sesion`, `zass_refresco`) son `HttpOnly`, `Secure`, `SameSite=Lax` y **sin `Domain`**; la antifalsificación (`zass_csrf`) es `Secure` (no `HttpOnly`, el sitio debe leerla), `Lax`. Con el sitio y la API en el MISMO origen (`zass.cl`, la Pages Function reenvía `/api/*`) el navegador las trata como de primera parte: no hace falta CORS y no se añadió. Las cookies no tienen `Domain`, así que quedan atadas al host exacto que respondió.

Si algún día el sitio y la API van en subdominios distintos (`zass.cl` y `api.zass.cl`) cambiaría: (1) habría que habilitar CORS con `AllowCredentials` y origen explícito (nunca `*`); (2) el sitio tendría que llamar con `credentials: 'include'`; (3) como `zass.cl` y `api.zass.cl` son el mismo *sitio* (mismo dominio registrable), `SameSite=Lax` bastaría para que las cookies viajen en `fetch` con `credentials: 'include'`; con dominios registrables distintos haría falta `SameSite=None; Secure`, que reabre el CSRF y obligaría a revisar esa defensa; (4) la política de contenido (`connect-src`) y la Pages Function dejarían de ser necesarias o cambiarían. Por eso se recomienda mantener un solo origen.

## 7. Lista de verificación previa a salir

Estado al 14-nov-2026: `[x]` = comprobado desde fuera y se dice cómo; `[ ]` = sigue sin comprobar
o solo se ve en el panel de Render/Neon, que es de Pablo.

1. [x] `dotnet test` en verde: carrera de CI **43608890662** sobre `main`, 845 + 17 + 775 del backend y 509 del frontend en 38 archivos.
2. [x] PostGIS activado en Neon y esquema migrado: `GET https://zass.cl/api/buscar?lat=-33.45&lon=-70.66&q=gasfiter` responde 200 con `"conUbicacion":true`, y esa consulta es la de `ST_Distance`/`ST_SetSRID` de `RepositorioBusqueda`. El resultado de `--migrar` en sí solo se ve en el panel.
3. [ ] Variables de Render completas; `Zass__ClaveDireccionesBase64` guardada además en un lugar seguro fuera de Render. **Solo lo ve Pablo en el panel.** Ojo con las que no están en `render.yaml` y sí hacen falta: `Zass__UrlPublica`, `Zass__Pasarela` y las de `Zass__Correo__*` (sección 2).
4. [ ] `Zass__ProxiesDeConfianza` con rangos de Cloudflare **y** la red del balanceador de Render. Tras iniciar sesión una vez desde tu celular con datos móviles: `SELECT ip_origen FROM sesiones ORDER BY creada_en DESC LIMIT 3;` debe mostrar TU IP pública, no la de Cloudflare/Render. **No verificable desde aquí: requiere la base de producción.**
5. [x] `/api/salud` y `/api/salud/lista` dan 200 (medido sobre `https://zass.cl`, 14-nov-2026). El 503 con la base caída **no se probó en producción**, solo por pruebas.
6. [ ] `/swagger` da 404 en producción. **No comprobable por `zass.cl`:** `/swagger` no empieza por `/api/`, así que lo atiende Cloudflare Pages y devuelve el HTML del sitio con 200. Para comprobarlo de verdad hay que pedirlo al origen de Render, que no se publica.
7. [ ] Las cookies de sesión salen `Secure; HttpOnly; SameSite=Lax` (herramientas de desarrollo del navegador). **Sin comprobar aquí:** exige iniciar sesión con una cuenta real de producción.
8. [ ] Probada una restauración de copia. El flujo **sí corre**: carreras diarias de «Copia de la base de datos» en verde (43247183426 el 12-nov, 43445027827 el 13-nov, 43600708505 el 14-nov), así que `NEON_URL_COPIAS` y `FRASE_COPIAS` están puestos. Lo que sigue pendiente es la **restauración**, y no se puede ensayar con el `pg_restore` 16.4 de este PC: ver el aviso de la sección 5.
9. [ ] La API solo atiende lo que llega por Cloudflare: `Zass__SecretoDeOrigen` en Render y `SECRETO_ORIGEN` en Cloudflare Pages, con el mismo valor (sección 4.1), y `https://<servicio>.onrender.com/api/planes` responde **403**. La URL `onrender.com` **ya es pública** (el repositorio es público desde el 20-nov y la URL está en su historial, en `ESTADO.md` y en `TRASPASO.md`): esconderla ya no es una defensa; el secreto sí.
10. [ ] Decidido el valor real del límite público (100 cada 15 min por IP es poco para un sitio abierto: una portada hace varias peticiones).

## 8. Qué quedó comprobado y qué no

Medido el 14-nov-2026 desde fuera (peticiones a `https://zass.cl` y carreras de GitHub Actions).
Lo que depende del panel de Render o de Neon **no se puede ver desde aquí: es de Pablo**.

**Comprobado:**

* **La imagen Docker y Render:** el servicio está en línea. `GET https://zass.cl/api/salud` → 200 `{"estado":"vivo"}`. Como Render construye y corre el servicio desde `backend/Dockerfile` (`runtime: docker` en `render.yaml`), que la API responda es la prueba de que la imagen se construye y arranca. Además hay un flujo `.github/workflows/despertar-api.yml` que la despierta, lo que solo tiene sentido con el servicio creado.
* **Neon:** `GET https://zass.cl/api/salud/lista` → 200 `{"estado":"lista"}`, y esa sonda hace un `SELECT 1` contra la base: hay conexión real con SSL.
* **PostGIS en Neon:** `GET https://zass.cl/api/buscar?lat=-33.45&lon=-70.66&q=gasfiter` → 200 con `"conUbicacion":true`, que es el camino de `ST_Distance`/`ST_SetSRID` de `RepositorioBusqueda`.
* **La Pages Function de Cloudflare, de extremo a extremo:** todo lo anterior se pidió a `zass.cl`, no al origen de Render, así que el reenvío de `/api/*` funciona.
* **El flujo de copias de GitHub Actions:** corre a diario y en verde (carreras 43247183426, 43445027827 y 43600708505, del 12 al 14-nov). Los secretos `NEON_URL_COPIAS` y `FRASE_COPIAS` están puestos. *(El comentario de cabecera de `copia-base-de-datos.yml` todavía dice «NO PROBADO todavía»: eso ya no es cierto, pero ese archivo no se toca en este PR porque no es documentación.)*

**Sin comprobar (no se puede desde aquí):**

* **La restauración de una copia:** ver el aviso de la sección 5. No es «falta hacerlo»: con el `pg_restore` 16.4 del clúster portable **no se puede**, hace falta un cliente 18.
* **Cuántas cabeceras `X-Forwarded-For` añade el balanceador de Render y desde qué red llega al contenedor.** El `3` de `Zass__SaltosDeProxy` y el `10.0.0.0/8` de la sección 4 vienen de medidas anteriores que **no se han vuelto a comprobar aquí**; la comprobación buena es la del punto 4 de la sección 7, contra la base de producción.
* **El valor real de las variables en Render** (incluidas `Zass__UrlPublica`, `Zass__Pasarela` y `Zass__Correo__*`, que no están en `render.yaml`): solo se ven en el panel.
* **La pausa por inactividad de Neon y el sueño del plan gratuito de Render**, y el 503 de `/api/salud/lista` con la base caída.
* **Las cookies de sesión en producción** (`Secure; HttpOnly; SameSite=Lax`): hay que iniciar sesión con una cuenta real.
* **Que `/swagger` dé 404 en producción:** por `zass.cl` lo atiende Cloudflare Pages y devuelve el sitio con 200, así que esa petición no dice nada del origen.
* Las cifras de los planes gratuitos (horas, almacenamiento, tiempos de inactividad) **no se citan: verifícalas en la página de cada proveedor.**
