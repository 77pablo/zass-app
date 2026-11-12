# Zass.Web — la interfaz de ZASS!

React 19 + TypeScript + Vite. Es la capa de presentacion que describe
`06-desarrollo/paquetes-por-capas.md`, que no esta en este repositorio: vive en el
repositorio de documentacion, aparte (la ruta esta en `ESTADO.md` §1). Pantallas y
estado de pantalla, sin una sola regla de negocio. Todo lo que decide algo vive en
la API.

## Levantarlo

De un solo clic, desde la raiz del proyecto:

```bat
ver-zass.bat        :: levanta base + API + interfaz y abre el navegador
cerrar-zass.bat     :: apaga las tres
```

A mano, si se prefiere ver cada pieza por separado, tambien desde la raiz del
proyecto:

```bat
iniciar-postgres.bat                                               :: 1. la base, puerto 5433
dotnet run --project backend\src\Zass.Api --launch-profile https   :: 2. la API, https://localhost:7277
cd frontend && npm run dev                                         :: 3. la interfaz, https://localhost:5173
```

El perfil `https` no es opcional: sin el, `dotnet run` toma el primero de
`launchSettings.json`, que es HTTP en el 5052, y el proxy de Vite (que apunta al
7277) se queda sin nadie al otro lado.

Se abre en **https://localhost:5173**. El navegador habla **solo** con 5173: Vite
reenvia `/api` al 7277, asi el frontend y la API comparten origen y las cookies de
sesion viajan como lo haran en produccion. Si se apuntara directo al 7277, el
navegador descartaria la cookie y el fallo pareceria "la contrasena esta mala".

## HTTPS, tambien aqui

Las dos puntas usan el mismo certificado de desarrollo, el de `dotnet dev-certs`.
Hay que confiarlo **una vez** en esta maquina:

```bat
dotnet dev-certs https --trust
```

Sin eso el navegador avisa de que el certificado no es de fiar. La pagina funciona
igual aceptando el aviso, pero conviene quitarlo de en medio.

Si falta el certificado exportado en `certificados/`, Vite lo dice y se levanta en
HTTP. Para generarlo, desde la raiz del proyecto:

```bat
dotnet dev-certs https --export-path certificados/localhost.pem --format PEM --no-password
```

Deja `localhost.pem` y `localhost.key`, los dos archivos que lee `vite.config.ts`.
`certificados/` esta en `.gitignore`.

Esto es lo que permite que la cookie de sesion lleve la marca `Secure` tambien en
desarrollo. Antes no la llevaba, y la unica forma de comprobarlo era desplegar.

La API toma sus secretos de User Secrets (`dotnet user-secrets list` dentro de
`backend\src\Zass.Api`). Si faltan, no arranca: es a proposito
(`Configuracion/ValidacionDeArranque.cs`).

## Las pruebas

| Comando | Que prueba | Necesita levantado |
|---|---|---|
| `npm run prueba` | Logica y pantallas en jsdom, con `fetch` simulado: todos los `src/**/*.prueba.{ts,tsx,js}` | nada |
| `npx tsc -b` y `npx oxlint` | Que compila y que pasa el linter | nada |
| `node pruebas-navegador/mirar-portada.mjs` | Deja capturas de la portada en escritorio y en celular, y del desplegable de sugerencias, para juzgar a ojo el hero y el radar. No comprueba nada: solo fotografia | los 3 |
| `node pruebas-navegador/mirar-sitio.mjs` | Recorre once rutas en escritorio y en celular y las MIDE: desborde horizontal, errores de consola, objetivos tactiles de menos de 44 px, que Montserrat cargo, y el titulo y el `h1` de cada pagina | los 3, con `datos/sembrar-demo.sql` sembrado |
| `node pruebas-navegador/mirar-busqueda.mjs` | La pantalla de busqueda medida de verdad: resultados y su orden, distancias, el radio, los colores de marca, la fuente y los tamanos tactiles, con y sin ubicacion del navegador | los 3, con `datos/sembrar-visual.sql` sembrado |
| `node pruebas-navegador/flujo-real.mjs` | El recorrido entero de un profesional: crea su cuenta, entra, arma su ficha, la publica, aparece en la busqueda, su ficha publica la muestra y sale | los 3 |

Los cuatro guiones usan Playwright, se corren desde `frontend/` y **no toman
argumentos**. Las capturas quedan en `registros/capturas/`, en la raiz del proyecto
(`registros/` esta en `.gitignore`). `mirar-sitio`, `mirar-busqueda` y `flujo-real`
salen con codigo 1 si algo esta en rojo; `mirar-portada` no comprueba nada.
`package.json` no declara ningun script de npm para ellos: solo `dev`, `build`,
`lint`, `preview`, `prueba` y `prueba:ver`.

`flujo-real` crea y borra su propia cuenta hablando con `psql` contra la base local,
asi que necesita tambien el PostgreSQL portable de `herramientas\pgsql`. La API
permite 5 intentos de autenticacion cada 15 minutos por IP (`Tasa.Autenticacion`):
para correrlo varias veces seguidas, levantarla con `Zass__Tasa__A=100`.

Las de navegador son las que importan de verdad: `npm run prueba` simula `fetch` y
corre en jsdom, donde no hay CSS, ni maquetacion, ni fuentes, **asi que no puede
ver** un proxy mal configurado, una cookie rechazada, un token CSRF caducado ni una
pagina que se desborda. Esas cosas solo aparecen con el navegador y la API de
verdad, y las tres primeras ya fallaron una vez aqui.

## Organizacion

```
src/
  api/                el cliente HTTP (cookies, token CSRF, reintentos, traduccion de errores)
                      y las llamadas de catalogo y comunidad
  componentes/        lo compartido entre pantallas: Aviso, Campo, Seleccion
  funciones/          una carpeta por subsistema: agenda, busqueda, identidad, perfil
  marco/              lo que rodea a toda pagina: cabecera, pie, barra de busqueda, mapa de Chile
  paginas/            una por ruta, mas las piezas que viven dentro de otra pagina
  sesion/             quien esta dentro, y a donde volver despues de entrar
  pruebas-functions/  pruebas de las Functions de Cloudflare Pages, que estan fuera de src/
  App.tsx             las rutas, todas en un sitio
```

Fuera de `src/`: `functions/` son las Cloudflare Pages Functions (reenvio de `/api`,
SEO por ficha, mapa del sitio), `pruebas-navegador/` los guiones de arriba con sus
datos de siembra, y `scripts/` genera la imagen de `og:`. El despliegue esta en
`DESPLIEGUE-CLOUDFLARE.md`.

## Lo que falta

Ya tienen pantalla, y ruta en `App.tsx`: registro e ingreso (API-08, API-09),
verificar el correo (API-13), recuperar y restablecer la contrasena (API-14,
API-15), la busqueda, la ficha publica, la ficha propia del prestador con sus
servicios, sus comunas y sus credenciales, la agenda y sus citas, y los legales.

Sin pantalla todavia:

| Falta | Que hay detras |
|---|---|
| Escribir una resena, editarla, responderla o reportarla | API-46 a API-51, ya en la API. La ficha muestra el promedio y el recuento, pero no hay por donde escribir |
| Las dos colas internas: credenciales por revisar y resenas reportadas | API-29, API-30, API-31, API-52 y API-53. El frontend no llama a ninguna ruta `/api/interno` |
| Subir el documento de una credencial y enviarla a revision | API-27. Espera a MOD-08, y `MisCredenciales` lo dice en vez de prometer una revision que no puede ocurrir |
| Eliminar la cuenta | API-17 |
| Pagar la mensualidad | nada: la API no tiene endpoints de pagos todavia |

El cliente de `src/api` ya sirve para todas: son peticiones mas, no un cliente nuevo.
