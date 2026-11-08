# Imagen de Zass.Api. Contexto de construcción: la carpeta backend/.
#
#   docker build -t zass-api backend
#   docker run --rm -p 8080:8080 -e PORT=8080 -e Zass__CadenaConexion=... -e Zass__ClaveDireccionesBase64=... zass-api
#
# SIN SECRETOS DENTRO DE LA IMAGEN: todo llega por variables de entorno en tiempo de ejecución
# (ver DESPLIEGUE-API.md). Una imagen con la clave dentro la tiene cualquiera que pueda bajarla.

# ---------- Etapa 1: compilar y publicar ----------
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS compilar
WORKDIR /origen

# Primero SOLO los .csproj y la restauración: mientras no cambien los paquetes, Docker
# reutiliza esta capa y las reconstrucciones no vuelven a bajar NuGet.
COPY src/Zass.Dominio/Zass.Dominio.csproj src/Zass.Dominio/
COPY src/Zass.Aplicacion/Zass.Aplicacion.csproj src/Zass.Aplicacion/
COPY src/Zass.Infraestructura/Zass.Infraestructura.csproj src/Zass.Infraestructura/
COPY src/Zass.Api/Zass.Api.csproj src/Zass.Api/
RUN dotnet restore src/Zass.Api/Zass.Api.csproj

COPY src/ src/
RUN dotnet publish src/Zass.Api/Zass.Api.csproj -c Release --no-restore -o /publicado /p:UseAppHost=false

# ---------- Etapa 2: ejecutar ----------
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app
COPY --from=compilar /publicado .

# Producción por omisión: Swagger apagado y la validación de arranque en modo estricto.
ENV ASPNETCORE_ENVIRONMENT=Production \
    DOTNET_EnableDiagnostics=0

# Usuario sin privilegios. La imagen oficial de .NET 8 ya trae el usuario «app».
USER app

# Render inyecta PORT y espera que el servicio escuche ahí. Fuera de Render (docker run sin
# PORT) se usa 8080. ASPNETCORE_HTTP_PORTS es lo que lee Kestrel en .NET 8; se fija al
# arrancar porque PORT no existe en el momento de construir la imagen.
# Solo HTTP: el TLS lo termina Render/Cloudflare. `exec` para que dotnet reciba SIGTERM.
EXPOSE 8080
ENTRYPOINT ["sh", "-c", "export ASPNETCORE_HTTP_PORTS=${PORT:-8080}; exec dotnet Zass.Api.dll \"$@\"", "--"]
