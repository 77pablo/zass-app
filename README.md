# ZASS!

> Conecta con un toque

Plataforma web comunitaria que conecta a quien **ofrece** un servicio —abogado, corredor de
propiedades, contador, arquitecto, técnico— con quien lo **busca cerca**, con credenciales
verificadas, reseñas, agenda de citas y una tarjeta NFC opcional con QR de respaldo.

**Modelo de negocio:** el prestador paga una mensualidad. El buscador no paga nunca.

---

## Dónde leer qué

| Archivo | Para qué |
|---|---|
| `ESTADO.md` | **Fuente de verdad** de qué está hecho y comprobado, qué no, y las cuentas de pruebas al día |
| `CONTRIBUIR.md` | Montar el proyecto por primera vez, flujo de trabajo y niveles de revisión |
| `TRASPASO.md` | Dónde vive cada pieza en producción y la arquitectura del código |
| `DESPLIEGUE-API.md` | Despliegue de la API (Render) y su configuración |
| `frontend/DESPLIEGUE-CLOUDFLARE.md` | Despliegue del sitio y de las Functions (Cloudflare Pages) |
| `CLAUDE.md` | Reglas para Claude Code |

Este README es solo la entrada: no repite el estado del producto ni cifras de pruebas,
porque cambian con cada PR. Si este archivo y `ESTADO.md` no coinciden, manda `ESTADO.md`.

## En línea

El sitio está publicado en **<https://zass.cl>** (también `https://www.zass.cl`).

| Pieza | Dónde corre |
|---|---|
| Sitio (React) y Functions | Cloudflare Pages, proyecto `zass` |
| API (.NET 8) | Render, servicio `zass-api`, con Docker (`render.yaml`, `backend/Dockerfile`) |
| Base de datos | Neon (PostgreSQL con PostGIS) |

El navegador habla solo con `zass.cl`: una Pages Function (`frontend/functions/api/[[ruta]].js`)
reenvía `/api/*` a la API, para que las cookies de sesión viajen sin CORS. Así,
`https://zass.cl/api/salud` responde desde la API. El detalle (cuentas, DNS, variables) está
en `TRASPASO.md` §2 y §3.

## Qué hay aquí

Este repositorio es la **aplicación**. El diseño del sistema —casos de uso, tablas,
endpoints, diagramas— vive aparte, y es la especificación: los identificadores que
aparecen en el código (`CU-03`, `TBL-09`, `API-20`, `RN-S3-09`, `CC-02`, `MOD-01`) apuntan a ella.

| Carpeta | Qué es |
|---|---|
| `backend/src/Zass.Dominio` | Las entidades. **Cero paquetes NuGet**, lo exige el documento |
| `backend/src/Zass.Aplicacion` | Los casos de uso y los puertos |
| `backend/src/Zass.Infraestructura` | EF Core con Npgsql y PostGIS, Argon2id, las migraciones (`Datos/Migraciones`) |
| `backend/src/Zass.Api` | La API REST (minimal APIs) |
| `backend/pruebas` | `Zass.Aplicacion.Pruebas`, `Zass.Arquitectura.Pruebas` y `Zass.Integracion.Pruebas` (esta última contra PostgreSQL real) |
| `frontend` | React 19 + TypeScript + Vite; pruebas con Vitest |
| `frontend/functions` | Cloudflare Pages Functions: proxy de `/api`, ficha por slug, sitemap |
| `.github/workflows` | `pruebas.yml` y `copia-base-de-datos.yml` |

## Tecnología

- **Backend:** .NET 8 (`net8.0` en todos los `.csproj`). El repo no tiene `global.json`.
- **Frontend:** React 19 · TypeScript · Vite · `react-router-dom` 7 (`frontend/package.json`).
- **Base de datos:** PostgreSQL con PostGIS, y **no es la misma versión en todas partes**:
  - **Producción (Neon):** PostgreSQL 18, según `CLAUDE.md` y `ESTADO.md`.
  - **Local (portable, `herramientas\pgsql`):** PostgreSQL 16.4
    (`herramientas\pgsql\bin\pg_restore.exe --version`).
  - **GitHub Actions (`pruebas.yml`):** imagen `postgis/postgis:17-3.5`.

## Cómo levantarlo en local

**La primera vez**, sigue `CONTRIBUIR.md` de punta a punta: PostgreSQL portable, creación del
clúster, User Secrets, migración y dependencias. Sin esos pasos, lo de abajo no arranca.

PostgreSQL es una **instancia portable** que vive dentro del proyecto (`herramientas\pgsql`,
datos en `datos-pg\`), en el puerto 5433, y no se instala en el sistema. No está en el
repositorio.

Desde la raíz del repo:

```bat
ver-zass.bat
```

Levanta la base si no estaba arriba, la API y la interfaz, espera a que la API responda y
abre el navegador en `https://localhost:5173`. Para cerrar todo: `cerrar-zass.bat`.

| Pieza | Dirección local |
|---|---|
| Interfaz (Vite) | `https://localhost:5173` — es la que se abre |
| API | `https://localhost:7277` — Vite le reenvía `/api` |
| PostgreSQL | `127.0.0.1:5433` |

Solo la base: `iniciar-postgres.bat` y `detener-postgres.bat`.

Una sola vez por máquina, para que el navegador no avise del certificado:

```bat
dotnet dev-certs https --trust
```

## Pruebas

```bat
cd backend && dotnet test
cd frontend && npm run prueba && npx tsc -b && npx oxlint
```

Las pruebas de integración necesitan la base local arriba y usan la base `zass_pruebas`:
**nunca corras dos suites de integración a la vez**. Las cuentas vigentes de pruebas están
en `ESTADO.md`; las reglas (mutación obligatoria para toda prueba nueva), en `CONTRIBUIR.md`.

## Qué no está en el repositorio, y por qué

Todo esto está en `.gitignore`:

| Fuera | Razón |
|---|---|
| `herramientas/` | La instancia portable de PostgreSQL y PostGIS |
| `datos-pg/`, `registros/` | Los datos y los registros del clúster local |
| `.clave-pg.txt`, `certificados/`, `.env` | Secretos y una clave privada |
| `node_modules/`, `dist/`, `bin/`, `obj/` | Dependencias y compilados: se regeneran |

En local, la API necesita dos ajustes que nunca se versionan y viven en User Secrets:
`Zass:CadenaConexion` y `Zass:ClaveDireccionesBase64` (`CONTRIBUIR.md`, paso 4). En
producción son variables de entorno del servicio (`render.yaml` las declara sin valor).

## Datos de terceros

Las zonas —las comunas de Chile (`backend/src/Zass.Infraestructura/Datos/ZonasDeChile.cs`) y
los 16 sectores de Temuco— provienen de **OpenStreetMap**, bajo licencia
[ODbL](https://www.openstreetmap.org/copyright). Cualquier pantalla que las muestre debe
atribuir a *OpenStreetMap contributors*; hoy la atribución está en el pie del sitio
(`frontend/src/marco/Pie.tsx`).

## Licencia

Software privativo. © Pablo Espinoza. Todos los derechos reservados.
