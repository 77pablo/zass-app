# ZASS! - Prepara una instancia LOCAL de PostgreSQL (portable) para desarrollar.
# Uso:  powershell -NoProfile -ExecutionPolicy Bypass -File .\preparar-postgres.ps1
# Crea datos-pg (cluster nuevo en el puerto 5433), el rol zass, las bases zass y
# zass_pruebas, y guarda la clave en .clave-pg.txt. No pisa nada: si ya existe algo, se detiene.
# Requiere herramientas\pgsql (binarios de PostgreSQL 16 + PostGIS). Ver CONTRIBUIR.md.
param(
    [string]$Datos = "datos-pg",
    [int]$Puerto = 5433,
    [string]$ArchivoClave = ".clave-pg.txt"
)
$ErrorActionPreference = "Stop"

function Falla($mensaje) {
    Write-Host ""
    Write-Host "DETENIDO: $mensaje" -ForegroundColor Red
    exit 1
}
function Ruta($p) {
    if (Split-Path -Path $p -IsAbsolute) { return $p }
    return (Join-Path $PSScriptRoot $p)
}

$bin   = Join-Path $PSScriptRoot "herramientas\pgsql\bin"
$datos = Ruta $Datos
$clave = Ruta $ArchivoClave
$logs  = Join-Path $PSScriptRoot "registros"

foreach ($e in "initdb.exe", "pg_ctl.exe", "pg_isready.exe", "psql.exe") {
    if (-not (Test-Path (Join-Path $bin $e))) {
        Falla "falta $e en $bin. Descomprime herramientas\pgsql (ver CONTRIBUIR.md)."
    }
}
if (Test-Path $datos) { Falla "ya existe $datos. No se toca. Para empezar de cero, borra esa carpeta a mano." }
if (Test-Path $clave) { Falla "ya existe $clave. No se pisa." }
& (Join-Path $bin "pg_isready.exe") -h 127.0.0.1 -p $Puerto | Out-Null
if ($LASTEXITCODE -eq 0) { Falla "ya hay un PostgreSQL escuchando en el puerto $Puerto. Detenlo o usa -Puerto." }

# Clave aleatoria: dos GUID sin guiones (64 caracteres hexadecimales, seguros en una cadena de conexion).
$pass = [guid]::NewGuid().ToString("N") + [guid]::NewGuid().ToString("N")
$tmp  = Join-Path $env:TEMP ("zass-pw-" + [guid]::NewGuid().ToString("N") + ".txt")
Set-Content -Path $tmp -Value $pass -Encoding ASCII

Write-Host "[1/4] Creando el cluster en $datos ..."
& (Join-Path $bin "initdb.exe") -D $datos -U zass -E UTF8 --locale=C --auth=scram-sha-256 --pwfile=$tmp | Out-Null
$codigo = $LASTEXITCODE
Remove-Item $tmp -ErrorAction SilentlyContinue
if ($codigo -ne 0) { Falla "initdb fallo (codigo $codigo). Si quedo una carpeta a medias en $datos, borrala a mano." }

Set-Content -Path $clave -Value $pass -Encoding ASCII -NoNewline
Write-Host "[2/4] Clave guardada en $clave (no se muestra)."

Add-Content -Path (Join-Path $datos "postgresql.conf") -Encoding ASCII -Value "`r`nport = $Puerto`r`nlisten_addresses = 'localhost'`r`nmax_connections = 50"

Write-Host "[3/4] Arrancando en el puerto $Puerto ..."
New-Item -ItemType Directory -Force -Path $logs | Out-Null
# Mismo truco que iniciar-postgres.bat: la salida de pg_ctl va a un archivo, para que el servidor
# no herede la consola y nadie quede esperando para siempre.
$cmd = Join-Path $env:TEMP ("zass-arranque-" + [guid]::NewGuid().ToString("N") + ".cmd")
$linea = '"{0}" -D "{1}" -l "{2}" start >>"{3}" 2>&1' -f (Join-Path $bin "pg_ctl.exe"), $datos, (Join-Path $logs "preparar-postgres.log"), (Join-Path $logs "preparar-pg_ctl.log")
Set-Content -Path $cmd -Value @("@echo off", $linea) -Encoding ASCII
& $cmd
Remove-Item $cmd -ErrorAction SilentlyContinue

$arriba = $false
for ($i = 0; $i -lt 30; $i++) {
    Start-Sleep -Seconds 1
    & (Join-Path $bin "pg_isready.exe") -h 127.0.0.1 -p $Puerto | Out-Null
    if ($LASTEXITCODE -eq 0) { $arriba = $true; break }
}
if (-not $arriba) { Falla "no arranco en 30 segundos. Mira registros\preparar-postgres.log" }

Write-Host "[4/4] Creando las bases zass y zass_pruebas ..."
$env:PGPASSWORD = $pass
& (Join-Path $bin "psql.exe") -h 127.0.0.1 -p $Puerto -U zass -d postgres -w -v ON_ERROR_STOP=1 -c "CREATE DATABASE zass" -c "CREATE DATABASE zass_pruebas"
$codigo = $LASTEXITCODE
Remove-Item Env:\PGPASSWORD -ErrorAction SilentlyContinue
if ($codigo -ne 0) { Falla "no se pudieron crear las bases (codigo $codigo)." }

Write-Host ""
Write-Host "LISTO. PostgreSQL queda corriendo en 127.0.0.1:$Puerto (rol zass, bases zass y zass_pruebas)."
Write-Host "Para detenerlo: detener-postgres.bat. Para volver a levantarlo: iniciar-postgres.bat."
