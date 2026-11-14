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
| Despertador de la API (el plan gratis de Render la duerme tras ~15 min) | `.github/workflows/despertar-api.yml`, cada 10 min |

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
9. [ ] La URL `onrender.com` no está publicada en ninguna parte. **Hoy no se cumple dentro del repositorio:** el host del servicio aparece en `.github/workflows/despertar-api.yml` (valor por omisión de `URL_API`), en `ESTADO.md` y en `TRASPASO.md`. Mientras el repositorio sea privado no está «publicada», pero el día que deje de serlo lo estaría; lo que no se puede comprobar desde aquí es si aparece fuera del repositorio.
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
