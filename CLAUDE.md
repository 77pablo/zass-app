# ZASS! - instrucciones para Claude

Plataforma comunitaria de servicios locales en Chile: el prestador paga una mensualidad y quien busca no paga. Backend .NET 8 por capas (Zass.Dominio, Zass.Aplicacion, Zass.Infraestructura, Zass.Api, minimal APIs). Frontend React 19, TypeScript, Vite y Vitest. PostgreSQL con PostGIS.

Antes de tocar nada, lee `CONTRIBUIR.md`. `ESTADO.md` es la fuente de verdad del estado del proyecto; el `README.md` está desactualizado.

## Reglas duras

1. No trabajes en `main` ni hagas push a `main`. Cada tarea va en su propia rama, creada desde `main` actualizado (`git pull`).
2. No hagas commit ni push sin que la persona te lo pida.
3. Nunca leas, imprimas ni subas secretos: `.clave-pg.txt`, User Secrets, `.env`, cadenas de conexión, claves de API. Si ves uno, avisa y no lo copies.
4. Nunca toques producción (Neon, Render, Cloudflare, Resend). Ningún `dotnet ef database update` ni `--migrar` contra una base que no sea la local. `ZASS_CADENA_CONEXION` solo en la sesión y solo hacia `localhost`.
5. Migraciones de EF: una persona a la vez. Pregunta antes de crear una. Si hay conflicto en `ZassDbContextModelSnapshot.cs`, no lo resuelvas a mano: descarta tu migración y vuelve a generarla tras `git pull`. Una migración ya mergeada no se edita.
6. Verifica antes de afirmar. No digas que algo pasa o está arreglado sin haber corrido la prueba y visto la salida. Un verde no prueba nada: toda prueba nueva se comprueba rompiendo el código a propósito (rama desechable `mut-*`, que no se mergea) y viendo que cae justo esa prueba.
7. No instales PostgreSQL en el sistema: el local es portable (`preparar-postgres.ps1`, binarios en `herramientas\pgsql`).
8. Seguridad en todo endpoint: límite de peticiones (`Tasa.*`), validación de entrada, sin secretos en el repo, token CSRF en las operaciones con sesión. 403 y 404 se mapean igual a propósito (seguridad.md §5).
9. Los identificadores del documento (CU-, TBL-, API-, RN-, CC-, R-, MOD-) se respetan. Si el código y el documento difieren, corrige uno y di cuál.
10. Las pruebas de integración usan `zass_pruebas` y no corren contra `zass`. Nunca corras dos suites de integración a la vez.
11. Si algo no calza con lo que dice este archivo, pregunta antes de improvisar.

## Comandos (desde la raíz del repo)

- Backend: `cd backend` y `dotnet test`
- Frontend: `cd frontend`, y `npm run prueba`, `npx tsc -b`, `npx oxlint`
- Base local: `iniciar-postgres.bat` y `detener-postgres.bat`
- Todo andando: `ver-zass.bat` y `cerrar-zass.bat`