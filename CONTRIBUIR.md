# Cómo trabajar en ZASS!

Guía para quien se suma al repositorio. Si algo de aquí no calza con lo que ves, avisa antes de improvisar.

## Requisitos

- Windows, Git, .NET SDK 8 y Node 26 (es lo que usa el CI: `8.0.x` y `26`).
- No hace falta ser administrador: PostgreSQL es portable y vive dentro de la carpeta del repo.
- Si tienes también el SDK de .NET 10: el repo no fija versión (no hay `global.json`), así que `dotnet` usa el más nuevo. El proyecto apunta a `net8.0`. Si ves algo raro al compilar, avisa.

## Primera vez en tu máquina

1. Clona el repo y entra a la carpeta.

2. Pídele a Pablo `pgsql.zip` (por un canal privado, nunca por GitHub) y descomprímelo en la raíz del repo, de modo que exista `herramientas\pgsql\bin\initdb.exe`. Trae PostgreSQL 16 y PostGIS.

3. Crea tu base local (una sola vez). Crea `datos-pg`, el rol `zass`, las bases `zass` y `zass_pruebas`, y guarda una clave aleatoria en `.clave-pg.txt`. Si algo ya existe, se detiene sin pisarlo.

        powershell -NoProfile -ExecutionPolicy Bypass -File .\preparar-postgres.ps1

   Después, para apagar y volver a encender: `detener-postgres.bat` e `iniciar-postgres.bat`. Escucha en el puerto 5433.

4. Secretos de la API. Son tuyos y de tu máquina: no son los de producción, y se guardan fuera del repo (User Secrets).

        $clave = (Get-Content .clave-pg.txt -Raw).Trim()
        dotnet user-secrets set "Zass:CadenaConexion" "Host=127.0.0.1;Port=5433;Database=zass;Username=zass;Password=$clave" --project backend\src\Zass.Api
        $b = New-Object byte[] 32
        [Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($b)
        dotnet user-secrets set "Zass:ClaveDireccionesBase64" ([Convert]::ToBase64String($b)) --project backend\src\Zass.Api

5. Migra tu base de desarrollo (la API no migra sola al arrancar):

        $env:ASPNETCORE_ENVIRONMENT = "Development"
        dotnet run --project backend\src\Zass.Api -- --migrar
        Remove-Item Env:\ASPNETCORE_ENVIRONMENT

   Debe terminar con código 0. Es idempotente.

6. Herramientas y dependencias:

        cd backend
        dotnet tool restore
        cd ..\frontend
        npm ci

7. Certificado de desarrollo, una vez por máquina: `dotnet dev-certs https --trust`.

8. Para ver todo andando: `ver-zass.bat` (base, API en `https://localhost:7277` e interfaz en `https://localhost:5173`). Para apagarlo: `cerrar-zass.bat`.

## Pruebas

- Backend, con la base arriba: `cd backend` y `dotnet test`.
- Frontend: `cd frontend`, y luego `npm run prueba`, `npx tsc -b` y `npx oxlint`.
- Las pruebas de integración usan la base `zass_pruebas` (la crean y migran solas) y se niegan a correr contra `zass`.
- Nunca corras dos suites de integración a la vez: se pisan entre sí y dan rojos falsos.
- La prueba `Dos_reservas_simultaneas_reciben_filas_distintas` falló una vez en CI y pasó al repetir la corrida con el mismo commit. Si te falla, repite la corrida y avisa; no la "arregles" a ciegas.

## Flujo de ramas

- Nadie trabaja en `main`. Cada tarea va en su rama:

        git checkout main
        git pull
        git checkout -b nombre-corto-de-la-tarea

- Ramas cortas, una funcionalidad completa por rama (de punta a punta, no por capa).
- Sube la rama y abre un Pull Request hacia `main`. El CI (`Pruebas`) corre solo y debe quedar verde: compilar, pruebas de aplicación, de arquitectura y de integración, y en el frontend `tsc`, `vitest` y `oxlint`.
- Pablo revisa y hace el merge.
- Un verde no prueba nada por sí solo. Para una prueba nueva, rompe el código a propósito en una rama desechable (`mut-algo`, que nunca se mergea) y comprueba que cae justo esa prueba.

## Migraciones de EF: una persona a la vez

`ZassDbContextModelSnapshot.cs` es un archivo generado de miles de líneas y casi no se puede fusionar a mano.

- Antes de crear una migración, avisa a la otra persona. Ella hace `git pull` antes de crear la suya.
- Si hay conflicto en el snapshot, no lo resuelvas a mano: descarta tu migración y vuelve a generarla después de traer los cambios.
- Una migración ya mergeada no se edita: se crea otra.
- Para crearla, con la variable apuntando a tu base local y solo en esa sesión de PowerShell:

        cd backend
        $env:ZASS_CADENA_CONEXION = "Host=127.0.0.1;Port=5433;Database=zass_pruebas;Username=zass;Password=$((Get-Content ..\.clave-pg.txt -Raw).Trim())"
        dotnet ef migrations add NombreDeLaMigracion --project src\Zass.Infraestructura --startup-project src\Zass.Infraestructura
        Remove-Item Env:\ZASS_CADENA_CONEXION

- Esa variable nunca se define como variable permanente de Windows ni apunta a Neon. Las migraciones en producción las aplica solo Pablo.

## Secretos

- No subas ni pegues en ningún chat: `.clave-pg.txt`, User Secrets, `.env`, cadenas de conexión ni claves de API.
- No tendrás acceso a los paneles de Render, Neon, Cloudflare ni Resend. Si algo de ahí te hace falta, pídeselo a Pablo.
- Si una clave se filtra, avisa de inmediato.

## Documentos compartidos

- `ESTADO.md` y `TRASPASO.md` los edita una sola persona (Pablo), o cada quien al final de su rama, nunca los dos a la vez.
- `ESTADO.md` es la fuente de verdad del estado del proyecto. El `README.md` está desactualizado.