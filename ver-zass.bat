@echo off
REM ============================================================
REM  ZASS! - Levanta todo y abre la pagina en el navegador.
REM
REM  Un solo clic. Deja tres ventanas abiertas: la base, la API y
REM  la interfaz. Para apagar todo: cerrar-zass.bat
REM ============================================================

setlocal
set RAIZ=%~dp0
set BIN=%RAIZ%herramientas\pgsql\bin

title ZASS! - arrancando

echo.
echo   ZASS!  Conecta con un toque
echo   ---------------------------------------------
echo.

REM --- 1. La base -----------------------------------------------
echo   [1/3] PostgreSQL...
if not exist "%RAIZ%registros" mkdir "%RAIZ%registros"
"%BIN%\pg_isready.exe" -h 127.0.0.1 -p 5433 >nul 2>&1
if errorlevel 1 (
  "%BIN%\pg_ctl.exe" -D "%RAIZ%datos-pg" -l "%RAIZ%registros\postgres.log" start >>"%RAIZ%registros\pg_ctl.log" 2>&1
  ping -n 5 127.0.0.1 >nul
) else (
  echo         ya estaba arriba
)
"%BIN%\pg_isready.exe" -h 127.0.0.1 -p 5433
if errorlevel 1 (
  echo.
  echo   NO ARRANCO LA BASE. Mira registros\postgres.log
  pause
  exit /b 1
)

REM --- 2. La API ------------------------------------------------
REM En ventana aparte: si corriera aqui, bloquearia el resto. Los
REM secretos salen de User Secrets, no de variables de entorno.
echo   [2/3] API en el puerto 7277 (HTTPS)...
start "ZASS! API" cmd /k "cd /d %RAIZ%backend\src\Zass.Api && set ASPNETCORE_URLS=https://localhost:7277&& set ASPNETCORE_ENVIRONMENT=Development&& dotnet run --no-launch-profile"

REM --- 3. La interfaz -------------------------------------------
echo   [3/3] Interfaz en el puerto 5173 (HTTPS)...
if not exist "%RAIZ%frontend\node_modules" (
  echo         primera vez: instalando dependencias, esto tarda
  pushd "%RAIZ%frontend" && call npm install && popd
)
start "ZASS! Web" cmd /k "cd /d %RAIZ%frontend && npm run dev"

REM --- Esperar a que la API responda antes de abrir el navegador --
REM Sin esto el navegador abre antes de tiempo, la primera peticion
REM falla y parece que la pagina esta rota cuando solo iba adelantada.
echo.
echo   Esperando a que la API responda...
set INTENTOS=0
:ESPERAR
set /a INTENTOS+=1
ping -n 3 127.0.0.1 >nul
REM -k porque el certificado de desarrollo no lo firma ninguna autoridad.
curl -s -k -o nul https://localhost:7277/api/auth/csrf 2>nul
if not errorlevel 1 goto LISTO
if %INTENTOS% GEQ 30 goto RENDIRSE
goto ESPERAR

:RENDIRSE
echo   La API tardo mas de lo normal. Mira la ventana "ZASS! API".
echo   Abro igual el navegador: puede que ya este por responder.

:LISTO
echo.
echo   ---------------------------------------------
echo    Interfaz:  https://localhost:5173   ^<-- esta
echo    API:       https://localhost:7277
echo    Base:      127.0.0.1:5433
echo   ---------------------------------------------
echo.
echo    Se abre el 5173, NO el 7277: la interfaz manda
echo    las peticiones a la API por detras.
echo.
echo    Si el navegador avisa del certificado, falta confiarlo:
echo       dotnet dev-certs https --trust
echo    Es un certificado local, de esta maquina. Se hace una vez.
echo.
start https://localhost:5173
echo   Para apagar todo: cerrar-zass.bat
echo.
pause
endlocal
