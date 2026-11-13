# ZASS! — reglas para Claude Code

ZASS! es una plataforma comunitaria de servicios locales en Chile. Backend .NET 8 por capas (Dominio, Aplicación, Infraestructura, Api, con minimal APIs). Frontend React 19 + TypeScript + Vite. Base PostgreSQL con PostGIS: **18 en producción (Neon)** y **16.4 en el clúster portable local**. No son la misma versión, y eso ya impidió una restauración de emergencia: `pg_restore` 16.4 rechaza un volcado de `pg_dump` 18.

Antes de cualquier tarea, lee `CONTRIBUIR.md` (flujo y niveles de revisión) y la sección de `ESTADO.md` que corresponda. `ESTADO.md` es la fuente de verdad del estado del proyecto.

## Reglas duras

1. Nunca trabajas en `main`. Cada tarea va en su propia rama corta.
2. No haces commit, push ni abres PR sin que la persona que te usa lo pida. Antes le muestras el diff.
3. Nunca lees, imprimes, copias ni subes secretos: `.clave-pg.txt`, `zass-secretos`, `.env`, valores de User Secrets, cadenas de conexión, claves de servicios. Si necesitas un valor, pide que lo pongan ellos fuera del chat.
4. Nunca tocas producción. Nunca corres `--migrar` ni `database update` contra algo que no sea la base local `zass` o `zass_pruebas` en `127.0.0.1:5433`.
5. Migraciones: una a la vez. No editas una migración ya mergeada; agregas una nueva. Antes de crearla, avisas.
6. Nada es «arreglado» ni «verificado» sin evidencia. Para una prueba nueva, muestra la mutación que la hizo caer. Si no la tienes, dilo.
7. No instalas PostgreSQL en el sistema. El de desarrollo es portable, en `herramientas\pgsql`.
8. Seguridad en todo endpoint: sesión cuando corresponde, CSRF en todo lo que no sea GET, límite de peticiones `Tasa.*`, validación de entrada. 403 y 404 responden igual, a propósito.
9. Respeta los identificadores de los documentos (CU-, TBL-, API-, RN-, CC-, R-, MOD-). Si el código y el documento no coinciden, lo dices; no corriges en silencio.
10. Las pruebas de integración usan `zass_pruebas`. Nunca corras dos suites de integración a la vez.
11. Cada PR lleva nivel (N1, N2 o N3). Si la tarea toca una zona N3 (migraciones, seguridad, `Program.cs`, correo, infraestructura, secretos, pagos), para y pregunta antes de escribir código.
12. Textos visibles nuevos: los propones, no los decides.
13. Si algo de la tarea no calza con el código real, pregunta. No adaptes el brief por tu cuenta.

## Comandos

```
cd backend;  dotnet test;  cd ..
cd frontend; npm run prueba; npx tsc -b; npx oxlint; cd ..
.\iniciar-postgres.bat     (levantar la base local)
.\ver-zass.bat             (levantar API e interfaz)
.\cerrar-zass.bat          (cerrar todo)
```