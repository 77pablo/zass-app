# Desplegar la API de ZASS! (Render + Neon, detrás de Cloudflare)

**Estado: desplegado y en línea.** Sitio en Cloudflare Pages (`zass.cl`), API en Render
(servicio `zass-api`, `https://zass-api.onrender.com`) y base en Neon. Comprobado el 11-nov-2026 (hora de Chile):
`GET https://zass-api.onrender.com/api/salud` responde `200` con `x-render-origin-server: Kestrel`.
La §8 ya no es «lo que no se pudo probar sin cuentas»: es la lista de lo que **hoy sigue sin
verificar**. En la §4 hay un **defecto abierto** (la IP real del visitante) cuya causa **no está
confirmada**.

Cadena real, medida el 11-nov-2026. Lo que el dibujo anterior se saltaba: **la propia API de
Render está detrás de Cloudflare**, así que hay borde de Cloudflare en los dos extremos.

```
Visitante
   │
   ▼
Cloudflare (DNS y Pages de zass.cl; nameservers doug/monroe.ns.cloudflare.com)
   │   Pages Function /api/*  (frontend/functions/api/[[ruta]].js): reenvía a API_ORIGEN y
   │   ESCRIBE X-Forwarded-For = CF-Connecting-IP (sobreescribe lo que mandara el visitante),
   │   X-Forwarded-Proto = https y X-Forwarded-Host = zass.cl  (de los tres la API solo lee
   │   For y Proto: ver §4)
   ▼
Cloudflare otra vez, delante de zass-api.onrender.com
   │   (CNAME → gcp-us-west1-1.origin.onrender.com.cdn.cloudflare.net; la respuesta trae
   │   Server: cloudflare y CF-RAY)
   ▼
Balanceador de Render  (llega al contenedor desde 10.x)
   │
   ▼
Contenedor de la API (Docker, Kestrel)  ──►  Neon (PostgreSQL + PostGIS)
```

Cada tramo puede **añadir** su entrada a `X-Forwarded-For`, y de cuántas entradas llegan depende
que la API acierte con la IP del visitante: §4.

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
| `Zass__SaltosDeProxy` | no (por omisión `2`: `ProxiesDeConfianza.SaltosPorOmision`) | Cuántas entradas de `X-Forwarded-For` se procesan como máximo, de derecha a izquierda; el valor se pasa tal cual a `ForwardLimit` del middleware. `ValidacionDeArranque` solo acepta de 1 a 5. En el despliegue actual vale `3`, en `render.yaml` y en Render. **Si la cadena trae más entradas que este número, la API se queda en una IP intermedia: es una de las hipótesis del defecto abierto de la §4.** Ver el comentario en `ProxiesDeConfianza.cs`. |
| `Zass__Tasa__P/A/G/S/Salud` | no | Límites de peticiones por IP cada 15 min. Los valores por omisión son los de `seguridad.md` (Salud: 2000, grupo propio para las sondas). **No los subas en producción sin medirlo.** |

## 3. Pasos

### 3.1 Base de datos en Neon
1. Crea el proyecto en Neon (elige una región cercana a la de Render).
2. En el editor SQL de Neon, **sobre la base de la aplicación**: `CREATE EXTENSION IF NOT EXISTS postgis;`
   (la migración inicial declara las extensiones `citext` y `postgis` —`AlterDatabase` en
   `20261107022203_EsquemaInicial.cs`—, así que `--migrar` las crea si el proveedor las permite;
   en Neon están permitidas: PostGIS 3.6 y las migraciones del 8-nov quedaron aplicadas.
   **Ninguna migración usa `pgcrypto`**, al contrario de lo que decía esta guía).
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
* Con la lista, solo se leen si la conexión directa viene de una de esas redes: `ProxiesDeConfianza.cs` vacía `KnownProxies`/`KnownNetworks` y deja **exactamente** lo configurado (los valores por omisión de ASP.NET, que confían en loopback, no quedan). Se recorre `X-Forwarded-For` de derecha a izquierda y se para en la primera conexión que no viene de una red de confianza; `SaltosDeProxy` se pasa a `ForwardLimit` y acota **cuántas entradas** se procesan. Lo que un atacante escriba más a la izquierda no se lee. `0.0.0.0/0` y `::/0` se rechazan al arrancar.
* El límite, `Ip(http)` de la auditoría/sesiones, `Request.IsHttps`/esquema (cookies Secure, HSTS) usan la IP y el esquema ya corregidos.

**Qué poner en `Zass__ProxiesDeConfianza`:** (a) los rangos publicados de Cloudflare (ver `frontend/DESPLIEGUE-CLOUDFLARE.md`), (b) la red desde la que el balanceador de Render llega al contenedor (`10.0.0.0/8`, medido: llega desde 10.x) y, si hace falta para acceso local/directo, `127.0.0.1` y `::1`.
Tras cada cambio de esa variable, comprueba qué IP guarda la API (§7, punto 4).

### Defecto ABIERTO: por `zass.cl` la API ve una IP de Cloudflare (causa sin confirmar)

**Lo medido** (sesiones del 8-nov-2026, anotado en `ESTADO.md` y `TRASPASO.md`): entrando por
`zass.cl`, `ip_origen` de `sesiones` queda con una IP de Cloudflare y no con la del visitante;
pidiendo **directo** a Render sí queda la IP real, y un `X-Forwarded-For` falso desde un origen
no listado se sigue ignorando. **Efecto:** el límite por IP cuenta por salida de Cloudflare y no
por persona, así que una salida puede agotarle el cupo a muchos visitantes a la vez. No hay
robo de identidad de IP: lo que llega desde una red no listada no se cree.

**La causa NO está confirmada.** Hipótesis vivas, las tres compatibles con lo medido:

1. **`SaltosDeProxy` se queda corto.** Delante del contenedor hay tres tramos (borde de
   Cloudflare de `zass.cl` con su Pages Function, borde de Cloudflare delante de
   `zass-api.onrender.com` y balanceador de Render), así que `X-Forwarded-For` puede llegar con
   más entradas que el `3` configurado. El middleware procesa como máximo `ForwardLimit`
   entradas y se queda en una intermedia, que es de Cloudflare. Es la hipótesis principal: si
   cada tramo añade su entrada ya son tres, y basta una más para que `3` no alcance.
2. **Falta una red en `Zass__ProxiesDeConfianza`.** Si alguna IP del camino no está en la lista
   (una salida de Cloudflare fuera de los rangos que se cargaron, o la red del balanceador), el
   recorrido se detiene ahí y deja esa IP como si fuera la del visitante.
3. **Algo del camino reemplaza o descarta la cabecera** que escribe la Pages Function. Es lo que
   supusieron las notas del 8-nov, pero **no se comprobó**; y la documentación de Cloudflare
   describe que su borde **añade** la IP del cliente a `X-Forwarded-For` en vez de
   sobreescribirla (no verificado en este despliegue), que es precisamente el escenario de la
   hipótesis 1. Por eso no se puede dar esta por hecha ni descartar las otras dos.

Ninguna prueba del repositorio distingue entre ellas: las de proxy simulan la IP remota y las
cabeceras dentro del servidor de pruebas, y **ninguna cubre una cadena con más entradas que
`SaltosDeProxy`** (`ProduccionPruebas.cs`).

**Prueba pendiente para distinguirlas** (no se ha corrido; no inventes el resultado):

* Mirar `X-Forwarded-For` **tal como llega** a la API, en una petición por `zass.cl` y en otra
  directa a Render, y contar sus entradas. Más entradas que `SaltosDeProxy` → hipótesis 1; la
  IP del visitante presente pero el recorrido detenido en una red que no está en la lista →
  hipótesis 2; la cabecera sin la IP del visitante → hipótesis 3. Hoy no hay forma de verlo sin
  tocar código: la API no registra la cabecera cruda en ninguna parte, y añadir una ruta o un registro
  de diagnóstico es **zona N3: lo decide Pablo**.
* Alternativa sin tocar código, también de Pablo: subir `Zass__SaltosDeProxy` en Render (la
  validación acepta hasta `5`), repetir el inicio de sesión desde datos móviles y volver a mirar
  `ip_origen`. Si con un salto más aparece la IP real, la hipótesis 1 queda confirmada.

Mientras el defecto siga abierto, el punto 4 de la §7 **no se puede marcar**.

**Riesgo residual (conócelo):** los rangos de Cloudflare son compartidos por todos sus clientes. Alguien con su propia cuenta de Cloudflare podría pedirle a la API directamente (salvo que ocultes la URL de Render) con un `X-Forwarded-For` inventado y esquivar el límite por IP. La mitigación completa es un secreto compartido entre la Pages Function y la API (cabecera que la API exige); no está implementado. Mientras tanto, no publiques la URL `onrender.com`.

## 5. Copias de seguridad

Neon tiene sus propias restauraciones a un punto del tiempo, pero su alcance y retención dependen del plan: **verifícalo en la página del proveedor** y no lo tomes como tu única copia.

Copia propia y gratuita, **fuera del servidor de la base**: `.github/workflows/copia-base-de-datos.yml` ejecuta `pg_dump` cada día con GitHub Actions, cifra el volcado con `gpg` y lo guarda como artefacto del repositorio (privado). Requiere dos secretos del repositorio: `NEON_URL_COPIAS` (URL `postgresql://` de Neon) y `FRASE_COPIAS` (guárdala fuera de GitHub). Los artefactos caducan (el flujo pide 30 días; verifica los límites de GitHub): para una copia de largo plazo descarga una de vez en cuando a un disco tuyo.

**Qué hace exactamente ese volcado:** el flujo corre `pg_dump --format=custom --no-owner
--no-privileges` dentro de la imagen `postgres:18`, o sea con un **cliente `pg_dump` 18**. De las
dos banderas:

* `--no-privileges` (`-x`) sí actúa al volcar: los `GRANT`/`REVOKE` no entran en el archivo.
* `--no-owner` **no hace nada aquí**: la documentación de PostgreSQL dice que «se ignora al
  emitir un archivo (no texto)» y que para los formatos de archivo la opción se pasa a
  `pg_restore`. Hay que repetirla al restaurar, y por eso aparece abajo.

**Restaurar, y por qué hoy no se puede en este PC:**

El volcado lo escribe `pg_dump` 18, así que para leerlo hace falta un `pg_restore` **18 o más
nuevo**: un volcado de PostgreSQL se carga en versiones iguales o más nuevas, nunca más viejas,
y `pg_restore` rechaza una cabecera de archivo que su versión no soporta (`unsupported version
(...) in file header`). Las herramientas portables del proyecto son **16.4**
(`herramientas\pgsql\bin\pg_restore.exe --version` → `pg_restore (PostgreSQL) 16.4`): **con ellas
este volcado no se restaura**. Con ese 16.4 tampoco se puede volcar desde Neon, porque `pg_dump`
se niega a leer un servidor de versión mayor que la suya. Hace falta un cliente 18: la propia
imagen `postgres:18`, o un `pgsql` portable 18 al lado del actual (decisión de infraestructura:
la pide Pablo, `herramientas\` no está en git).

Y `pg_restore` **no crea la base**: hay que crearla vacía y con PostGIS antes, porque el volcado
trae geometrías y sin la extensión disponible en el clúster destino la restauración falla. Con un
cliente 18:

```bash
gpg -d zass-AAAAMMDD.dump.gpg > zass.dump
createdb -T template0 zass_restaurada                 # pg_restore NO crea la base
psql -d zass_restaurada -c "CREATE EXTENSION IF NOT EXISTS postgis;"
pg_restore --no-owner --no-privileges -d zass_restaurada zass.dump
```

El volcado ya trae `CREATE EXTENSION IF NOT EXISTS`, así que haberla creado antes no estorba.
(`pg_restore -C -d postgres zass.dump` también crea la base, con el nombre que viene en el
archivo, pero entonces no puedes preparar nada dentro de ella antes de restaurar.)

**Ensaya una restauración antes de necesitarla:** sigue sin ensayarse (§8).

## 6. Cookies y dominio

Las cookies (`zass_sesion`, `zass_refresco`) son `HttpOnly`, `Secure`, `SameSite=Lax` y **sin `Domain`**; la antifalsificación (`zass_csrf`) es `Secure` (no `HttpOnly`, el sitio debe leerla), `Lax`. Con el sitio y la API en el MISMO origen (`zass.cl`, la Pages Function reenvía `/api/*`) el navegador las trata como de primera parte: no hace falta CORS y no se añadió. Las cookies no tienen `Domain`, así que quedan atadas al host exacto que respondió.

Si algún día el sitio y la API van en subdominios distintos (`zass.cl` y `api.zass.cl`) cambiaría: (1) habría que habilitar CORS con `AllowCredentials` y origen explícito (nunca `*`); (2) el sitio tendría que llamar con `credentials: 'include'`; (3) como `zass.cl` y `api.zass.cl` son el mismo *sitio* (mismo dominio registrable), `SameSite=Lax` bastaría para que las cookies viajen en `fetch` con `credentials: 'include'`; con dominios registrables distintos haría falta `SameSite=None; Secure`, que reabre el CSRF y obligaría a revisar esa defensa; (4) la política de contenido (`connect-src`) y la Pages Function dejarían de ser necesarias o cambiarían. Por eso se recomienda mantener un solo origen.

## 7. Lista de verificación previa a salir

1. [ ] `dotnet test` en verde (en `backend/`, con `ZASS_CADENA_PRUEBAS` apuntando a una base de pruebas).
2. [x] PostGIS activado en Neon (3.6) y migraciones aplicadas: `ESTADO.md` registra 13 aplicadas y consultadas en la base el 8-nov-2026. **Ojo:** el repositorio tiene hoy 15 migraciones; antes del próximo despliegue, `--migrar` otra vez y comprobar que no queda ninguna pendiente.
3. [x] Variables de Render cargadas y `Zass__ClaveDireccionesBase64` con copia en el gestor de contraseñas de Pablo (`TRASPASO.md` §3). **Pendiente ahí mismo, sin constancia de que se haya hecho:** rotar la contraseña de Neon, el token de Cloudflare y la clave de Resend, que pasaron por un chat.
4. [ ] **Hoy NO se cumple: defecto abierto (§4).** `Zass__ProxiesDeConfianza` está cargada con los rangos de Cloudflare, `10.0.0.0/8`, `127.0.0.1` y `::1`, y `Zass__SaltosDeProxy=3`; aun así, entrando por `zass.cl`, `SELECT ip_origen FROM sesiones ORDER BY creada_en DESC LIMIT 3;` muestra una IP de Cloudflare y no la del visitante (directo a Render sí muestra la real). Se marca cuando se cierre el defecto, no antes.
5. [ ] **Parcial.** `/api/salud` da 200 (medido el 11-nov-2026) y `ESTADO.md` registra `/api/salud/lista`, `/api/zonas` y `/api/buscar` en 200 **a través de Cloudflare** el 8-nov. Falta: que `lista` dé 503 con la base caída está cubierto por pruebas, no provocado en producción.
6. [ ] `/swagger` da 404 en producción (sin comprobar contra el servicio desplegado).
7. [ ] Las cookies de sesión salen `Secure; HttpOnly; SameSite=Lax` (herramientas de desarrollo del navegador, desde `zass.cl`).
8. [ ] **Parcial.** Secretos `NEON_URL_COPIAS` y `FRASE_COPIAS` puestos, flujo ejecutado a mano y verde, y artefacto descifrado en este PC (`ESTADO.md`, P1). **Falta la restauración**, y con `pg_restore` 16.4 no se puede hacer aquí (§5).
9. [ ] La URL `onrender.com` no está publicada en ninguna parte. Aparece en los documentos internos del repositorio privado (`ESTADO.md`, `TRASPASO.md`, esta guía): si el repositorio alguna vez se hace público, deja de cumplirse.
10. [ ] Decidido el valor real del límite público (100 cada 15 min por IP es poco para un sitio abierto: una portada hace varias peticiones).

## 8. Lo que HOY sigue sin verificar (honestidad)

Esto ya está desplegado: lo que se midió salió de esta lista y quedó con su evidencia en la §7.
Lo que queda aquí es lo que **no** tiene constancia en `ESTADO.md`, `TRASPASO.md`, `render.yaml`,
los flujos de `.github/workflows/` ni `frontend/DESPLIEGUE-CLOUDFLARE.md`.

* **La IP real del visitante:** defecto abierto y **causa sin confirmar** (§4). Es lo más
  importante de esta lista. Tampoco está medido cuántas entradas de `X-Forwarded-For` añade cada
  tramo de la cadena.
* **`render.yaml` como Blueprint:** el servicio `zass-api` existe y sirve, pero no hay constancia
  de que se haya creado desde el Blueprint, así que claves como `dockerContext` y
  `healthCheckPath` siguen **sin validar contra Render**. Además `render.yaml` no incluye las
  variables de correo (`Zass__Correo__*`, `Zass__UrlPublica`), que tampoco están en la §2 de esta
  guía: quien recree el servicio desde el archivo lo deja sin correo. Y si hoy están cargadas en
  Render **no está claro**: `TRASPASO.md` §3 las lista como cargadas y `ESTADO.md` (8-nov) dice
  que aún no.
* **Las migraciones al día en producción:** `ESTADO.md` registra 13 aplicadas en Neon (8-nov) y el
  repositorio tiene hoy 15; de las dos del 10-nov no hay constancia de que estén aplicadas, y
  `TRASPASO.md` deja un «Manual Deploy» pendiente en Render.
* **La imagen Docker en este PC:** `docker` no está instalado (`docker --version` → no se
  encuentra el comando), así que el `Dockerfile` **no se ha construido ni corrido aquí**. Sí se
  construye y corre en Render (el servicio es `runtime: docker` y la API responde). En local se
  comprobó lo de siempre: `dotnet publish -c Release` compila; la API publicada con `--migrar`
  sale con 0 contra una base local ya migrada; en Production una cadena sin SSL se rechaza con un
  mensaje claro; y, por prueba de integración, `--migrar` aplica todo sobre una base vacía y es
  idempotente.
* **Neon:** la conexión SSL real, PostGIS 3.6 y las migraciones están comprobadas. Sin medir:
  cuánto tarda en pausar el cómputo por inactividad y cómo se porta la sonda `/api/salud/lista`
  (3 s de espera) tras una pausa larga.
* **Restaurar una copia:** el flujo diario corre y el artefacto se descifra, pero la
  **restauración no se ha ensayado nunca** y con las herramientas de este PC (`pg_restore` 16.4)
  no se puede: hace falta un cliente 18 (§5).
* **503 y 404 en producción:** que `/api/salud/lista` dé 503 con la base caída y que `/swagger` dé
  404 están cubiertos por pruebas, no comprobados contra el servicio desplegado.
* **Las cookies en producción:** no se ha mirado en el navegador que salgan
  `Secure; HttpOnly; SameSite=Lax` desde `zass.cl`.
* **Secretos por rotar:** contraseña de Neon, token de Cloudflare y clave de Resend pasaron por un
  chat (`TRASPASO.md` §3). No hay constancia de que se hayan rotado.
* Las cifras de los planes gratuitos (horas, almacenamiento, tiempos de inactividad) **no se
  citan: verifícalas en la página de cada proveedor.** Lo único medido y anotado: el plan gratis
  de Render duerme el servicio y la primera visita tarda cerca de un minuto (`TRASPASO.md`).
