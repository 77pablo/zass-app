@echo off
REM ZASS! - Detiene la instancia local de PostgreSQL de forma limpia.

setlocal
set RAIZ=%~dp0
set BIN=%RAIZ%herramientas\pgsql\bin
set DATOS=%RAIZ%datos-pg

echo Deteniendo PostgreSQL...
"%BIN%\pg_ctl.exe" -D "%DATOS%" -m fast stop
echo Listo.
endlocal
