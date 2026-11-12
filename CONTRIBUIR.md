# Cómo colaborar en ZASS!

Este documento sirve para montar el proyecto por primera vez y para trabajar en él a diario. Las reglas para Claude Code están en `CLAUDE.md`. El estado del proyecto está en `ESTADO.md`, que es la fuente de verdad. El `README.md` está desactualizado: no te guíes por él.

## Requisitos

- Windows 10 u 11 con Git.
- .NET SDK 8. Si tienes también el SDK 10, `dotnet` puede elegir ese; el repo todavía no tiene `global.json`. Si algo falla por eso, avisa a Pablo.
- Node.js 26.
- No hace falta instalar PostgreSQL ni tener permisos de administrador: se usa una copia portable.

## 1. Clonar

```powershell
cd C:\Users\<tu-usuario>
git clone https://github.com/77pablo/zass-app.git
cd zass-app
git log --oneline -1
```

Debe mostrar `41857d9` o un commit posterior en `main`.

## 2. PostgreSQL portable

Pablo te pasa `pgsql.zip` por un canal privado. **No está en GitHub y nunca debe subirse.**

Antes de extraerlo, compara su hash con el que te envió Pablo por el chat del equipo:

```powershell
Get-FileHash "<ruta>\pgsql.zip" -Algorithm SHA256 | Select-Object -ExpandProperty Hash
```

Extráelo **dentro de `herramientas\`**. El zip ya trae la carpeta `pgsql` adentro, así que no lo descomprimas en la raíz del repo:

```powershell
New-Item -ItemType Directory -Force herramientas | Out-Null
tar.exe -xf "<ruta>\pgsql.zip" -C herramientas
Test-Path .\herramientas\pgsql\bin\initdb.exe
```

Debe decir `True`. Si dice `False`, revisa que no exista `herramientas\pgsql\pgsql` (doble carpeta) y avisa a Pablo con el error completo.

`herramientas\` está en `.gitignore`.

## 3. Crear el clúster local (una sola vez)

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\preparar-postgres.ps1
```

Crea `datos-pg\` en el puerto 5433, guarda la clave del rol `zass` en `.clave-pg.txt`, arranca el servidor y crea las bases `zass` y `zass_pruebas`. Debe terminar con código 0. Si imprime `DETENIDO:`, pega el mensaje completo a Pablo.

`datos-pg\`, `registros\` y `.clave-pg.txt` están en `.gitignore`. No compartas ni pegues su contenido en ningún chat.

Para levantar y bajar el servidor: `.\iniciar-postgres.bat` y `.\detener-postgres.bat`.

## 4. Secretos de la API (User Secrets)

Desde `backend\src\Zass.Api`:

```powershell
cd backend\src\Zass.Api
$clave = (Get-Content ..\..\..\.clave-pg.txt -Raw).Trim()
dotnet user-secrets set "Zass:CadenaConexion" "Host=127.0.0.1;Port=5433;Database=zass;Username=zass;Password=$clave"
$b = New-Object byte[] 32; [Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($b)
dotnet user-secrets set "Zass:ClaveDireccionesBase64" ([Convert]::ToBase64String($b))
dotnet user-secrets list | ForEach-Object { ($_ -split '=')[0] }
cd ..\..\..
```

La última línea debe listar solo los nombres `Zass:CadenaConexion` y `Zass:ClaveDireccionesBase64`, sin valores. La clave de direcciones es local: no es la de producción.

## 5. Migrar la base local

```powershell
$env:ASPNETCORE_ENVIRONMENT = "Development"
dotnet run --project backend\src\Zass.Api -- --migrar
```

Código de salida 0. Es idempotente: se puede correr de nuevo sin daño.

## 6. Herramientas y dependencias

```powershell
cd backend;  dotnet tool restore;  cd ..
cd frontend; npm ci;               cd ..
dotnet dev-certs https --trust
```

## 7. Levantar y ver

```powershell
.\ver-zass.bat
```

Levanta la base si no está arriba, la API en `https://localhost:7277` y la interfaz en `https://localhost:5173`, y abre el navegador. Para cerrar todo: `.\cerrar-zass.bat`.

## 8. Pruebas

Antes de tocar nada, todo debe estar verde:

```powershell
cd backend;  dotnet test;  cd ..
cd frontend; npm run prueba; npx tsc -b; npx oxlint; cd ..
```

Referencia: backend 674 (aplicación) + 614 (integración) + 16 (arquitectura); frontend 373 pruebas en 28 archivos.

- Las pruebas de integración usan la base `zass_pruebas`. Nunca corras dos suites de integración a la vez.
- Smart App Control de Windows puede bloquear en local las DLL de pruebas y `oxlint`. Si pasa, dilo: el backend se valida en GitHub Actions.

## Flujo de trabajo

1. Nadie trabaja directamente en `main`.
2. Cada tarea va en una rama corta: `git checkout main`, `git pull origin main`, `git checkout -b <nombre-corto>`.
3. Antes de abrir el PR: `git pull origin main` y las pruebas otra vez.
4. Un PR por funcionalidad. Si trae varias, se parte.
5. El PR se abre con el nivel en el título (`[N1]`, `[N2]` o `[N3]`) y con la plantilla completa, que aparece sola al abrirlo.
6. Actions debe estar en verde. Nada se mergea en rojo.
7. Mientras un PR espera revisión, sigue con otra tarea desde `main` actualizado. Nunca sigas desde una rama que aún no se aprobó.

## Niveles de revisión

| Nivel | Qué incluye | Quién mergea | Revisión de Pablo |
|---|---|---|---|
| **N1** | Texto, estilos o pruebas nuevas sin cambio de código de producción | Quien abre el PR, con CI verde y la mutación anotada | Después, en la siguiente ventana |
| **N2** | Endpoints o lógica con el patrón existente (sesión, CSRF, `Tasa.*`), sin migración | Pablo | En la ventana: endpoints y pruebas |
| **N3** | Migraciones, seguridad (autenticación, CSRF, límites, cabeceras), `Program.cs`, correo, infraestructura, secretos, pagos, copias | Pablo | Todo el PR |

- Si dudas entre dos niveles, va el más alto.
- Un PR que mezcla niveles se sube al más alto o se parte.
- **Cambios de `frontend\`:** hasta que Pablo confirme cómo despliega Cloudflare Pages, son N2 como mínimo.
- **Ventanas:** Pablo revisa dos veces al día, en las horas que avise en el chat del equipo. Un PR que llega fuera de ventana entra en la siguiente.

## Migraciones de EF: una a la vez

- Antes de crear una migración, avisa en el chat. No empieces otra hasta que la anterior esté en `main` y los demás hayan hecho `git pull`.
- Nunca edites una migración ya mergeada: se agrega una nueva.
- Comando: `dotnet ef migrations add <Nombre> --project backend\src\Zass.Infraestructura --startup-project backend\src\Zass.Infraestructura`. Con `--startup-project backend\src\Zass.Api` falla.
- `ZASS_CADENA_CONEXION` solo en la sesión de la terminal, apuntando a la base local. Nunca guardada.
- Toda migración es N3. Las migraciones de producción las corre solo Pablo.

## Pruebas nuevas: mutación obligatoria

- Toda prueba nueva se valida rompiendo el código a propósito en una rama `mut-<algo>`. Se comprueba que la prueba cae.
- En el PR se anota qué se cambió y qué prueba cayó.
- Después se borra la rama `mut-*`.
- Un verde por sí solo no prueba nada.

## Secretos

- Nunca en el repo, en el chat, en capturas ni en commits: ni `.clave-pg.txt`, ni User Secrets, ni `zass-secretos`, ni cadenas de conexión.
- Nunca contra producción. Nunca `--migrar` ni `database update` contra algo que no sea tu base local.

## Documentos compartidos

- `ESTADO.md` y `TRASPASO.md` los edita solo Pablo. Al final de cada PR deja el párrafo «Para ESTADO.md» de tres líneas.
- Los identificadores de los documentos (CU-, TBL-, API-, RN-, MOD-) se respetan. Si el código y el documento no coinciden, se avisa; no se corrige en silencio.
- Textos visibles nuevos (botones, avisos): se proponen en el PR y Pablo los aprueba.

## Seguridad en endpoints

Sesión cuando corresponda, CSRF en todo lo que no sea GET, límite de peticiones `Tasa.*` y validación de entrada. 403 y 404 responden igual, a propósito (`seguridad.md` §5).

## Si algo no calza

Si la tarea no coincide con el código real, o toca una zona N3 sin que se haya dicho, para y pregunta. No adaptes el brief por tu cuenta.