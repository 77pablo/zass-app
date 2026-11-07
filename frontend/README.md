# Zass.Web — la interfaz de ZASS!

React 19 + TypeScript + Vite. Es la capa de presentacion que describe
`docs/06-desarrollo/paquetes-por-capas.md`: pantallas y estado de pantalla, sin
una sola regla de negocio. Todo lo que decide algo vive en la API.

## Levantarlo

Hacen falta tres cosas, en este orden:

```bat
cd C:\Users\pdani\zass-app
iniciar-postgres.bat                            :: 1. la base, puerto 5433
dotnet run --project backend\src\Zass.Api       :: 2. la API, puerto 5052
cd frontend && npm run dev                      :: 3. la interfaz, puerto 5173
```

Se abre en http://localhost:5173. El navegador habla **solo** con 5173: Vite
reenvia `/api` a 5052, asi el frontend y la API comparten origen y las cookies
de sesion viajan como lo haran en produccion. Si se apuntara directo a 5052, el
navegador descartaria la cookie y el fallo pareceria "la contrasena esta mala".

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
