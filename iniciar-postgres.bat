@echo off
REM ZASS! - Levanta la instancia local de PostgreSQL 16 + PostGIS.
REM No usa el PostgreSQL del sistema: vive entera dentro de esta carpeta.

setlocal
set RAIZ=%~dp0
set BIN=%RAIZ%herramientas\pgsql\bin
set DATOS=%RAIZ%datos-pg
set LOG=%RAIZ%registros\postgres.log

if not exist "%RAIZ%registros" mkdir "%RAIZ%registros"

echo Levantando PostgreSQL en el puerto 5433...
REM La salida de pg_ctl se manda al log a proposito. Si no, el servidor que

REM arranca hereda la salida de este .bat y no la suelta mientras viva: quien

REM lance el script esperando a que esa salida cierre, espera para siempre

REM (una prueba, un script, un agente). La base arranca en segundos; lo que

REM se quedaba colgado era el que miraba.

"%BIN%\pg_ctl.exe" -D "%DATOS%" -l "%LOG%" start >>"%RAIZ%registros\pg_ctl.log" 2>&1

REM ping en vez de timeout: timeout espera una tecla y se cuelga cuando el .bat

REM lo lanza algo sin consola interactiva (una prueba, un script, un agente).

ping -n 4 127.0.0.1 >nul
"%BIN%\pg_isready.exe" -h 127.0.0.1 -p 5433
echo.
echo Base:    zass
echo Usuario: zass
echo Puerto:  5433
echo Clave:   la de .clave-pg.txt
echo.
echo Para detenerlo: detener-postgres.bat
endlocal
