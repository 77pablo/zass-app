@echo off
REM Apaga lo que levanto ver-zass.bat: interfaz, API y base.

setlocal
set RAIZ=%~dp0

echo   Cerrando ZASS!...

REM La interfaz y la API corren en ventanas propias, con titulo.
taskkill /FI "WINDOWTITLE eq ZASS! Web*" /T /F >nul 2>&1
taskkill /FI "WINDOWTITLE eq ZASS! API*" /T /F >nul 2>&1

REM La base se baja con su propia herramienta, no a la fuerza:
REM matar el proceso deja el cluster marcado como mal cerrado.
"%RAIZ%herramientas\pgsql\bin\pg_ctl.exe" -D "%RAIZ%datos-pg" stop -m fast >nul 2>&1

echo   Listo. Puertos 5173, 7277 y 5433 libres.
echo.
pause
endlocal
