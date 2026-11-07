# Zass.Web — la interfaz de ZASS!

React 19 + TypeScript + Vite. Es la capa de presentacion que describe
`docs/06-desarrollo/paquetes-por-capas.md`: pantallas y estado de pantalla, sin
una sola regla de negocio. Todo lo que decide algo vive en la API.

## Levantarlo

De un solo clic, desde la raiz del proyecto:

```bat
ver-zass.bat        :: levanta base + API + interfaz y abre el navegador
cerrar-zass.bat     :: apaga las tres
```

A mano, si se prefiere ver cada pieza por separado:

```bat
cd C:\Users\pdani\zass-app
iniciar-postgres.bat                            :: 1. la base, puerto 5433
dotnet run --project backend\src\Zass.Api       :: 2. la API, https://localhost:7277
cd frontend && npm run dev                      :: 3. la interfaz, https://localhost:5173
```

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

Esto es lo que permite que la cookie de sesion lleve la marca `Secure` tambien en
desarrollo. Antes no la llevaba, y la unica forma de comprobarlo era desplegar.

La API toma sus secretos de User Secrets (`dotnet user-secrets list` dentro de
`backend\src\Zass.Api`). Si faltan, no arranca: es a proposito.

## Las pruebas

| Comando | Que prueba | Necesita levantado |
|---|---|---|
| `npm run prueba` | Logica y pantallas, con `fetch` simulado | nada |
| `node pruebas-navegador/humo.mjs <carpeta>` | Que la pagina monta y los colores de marca son exactos | los 3 |
| `node pruebas-navegador/flujo-real.mjs` | Registro e ingreso completos de punta a punta | los 3 |

Las dos ultimas son las que importan de verdad: `npm run prueba` simula `fetch`,
asi que **no puede ver** un proxy mal configurado, una cookie rechazada ni un
token CSRF caducado. Esas tres cosas solo aparecen con el navegador y la API de
verdad, y las tres ya fallaron una vez aqui.

## Organizacion

```
src/
  api/          cliente HTTP: cookies, token CSRF y traduccion de errores
  componentes/  lo compartido entre pantallas
  funciones/    una carpeta por subsistema; hoy solo identidad (S1)
```

## Lo que falta

Esto cubre API-08 (registro) y API-09 (inicio de sesion). Sin pantalla todavia:
verificar el correo, recuperar la contrasena, el perfil del prestador y la
busqueda. El cliente de `src/api` ya sirve para todas.
