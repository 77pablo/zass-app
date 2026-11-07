# ZASS!

> Conecta con un toque

Plataforma web comunitaria que conecta a quien **ofrece** un servicio —abogado, corredor de
propiedades, contador, arquitecto, técnico— con quien lo **busca cerca**, con credenciales
verificadas, reseñas, agenda de citas y una tarjeta NFC opcional con QR de respaldo.

**Modelo de negocio:** el prestador paga una mensualidad. El buscador no paga nunca.

---

## Qué hay aquí

Este repositorio es la **aplicación**. El diseño del sistema —20 casos de uso, 31 tablas,
61 endpoints, los diagramas— vive aparte, y es la especificación: los identificadores que
aparecen en el código (`CU-03`, `TBL-09`, `API-20`, `RN-S3-09`, `CC-02`) apuntan a ella.

| Carpeta | Qué es |
|---|---|
| `backend/src/Zass.Dominio` | Las 31 entidades. **Cero paquetes NuGet**, lo exige el documento |
| `backend/src/Zass.Aplicacion` | Los casos de uso y los puertos. Se prueba sin base de datos y sin red |
| `backend/src/Zass.Infraestructura` | EF Core, Argon2id, AES-256-GCM, las migraciones |
| `backend/src/Zass.Api` | La API REST con cookies httpOnly, CSRF, límite de tasa y cabeceras |
| `backend/pruebas` | 248 pruebas de aplicación y 210 de integración contra PostgreSQL real |
| `frontend` | React 19 + TypeScript + Vite, con 31 pruebas y 2 guiones de navegador |

`ESTADO.md` dice **qué está hecho y comprobado, qué no, y el siguiente paso exacto**. Es el
archivo que hay que leer para retomar.

## El estado, en una línea

**MOD-01 Identidad** está completo con pantalla incluida. **MOD-03 Catálogo**,
**MOD-04 Geolocalización**, **MOD-05 Búsqueda** y **MOD-13 Suscripciones** están completos
de dominio a API, todavía sin pantalla. En total, **537 pruebas en verde**: 248 de
aplicación, 210 de integración y 79 de interfaz. Falta MOD-02 y MOD-06 a MOD-12.

El camino completo de un prestador —registrarse, elegir rubro y servicios, declarar su zona,
publicar y arrancar la suscripción de prueba— está probado de punta a punta por HTTP contra
PostgreSQL. Y la búsqueda pública ya lo encuentra: lo que falta para que el producto se pueda ver y tocar
son las **pantallas**.

## Tecnología

.NET 8 · PostgreSQL 16 + PostGIS · React 19 + TypeScript (PWA) · Azure

## Cómo levantarlo

PostgreSQL es una **instancia portable** que vive dentro del proyecto, en el puerto 5433, y
no se instala en el sistema. No está en el repositorio: pesa 1,2 GB.

```bat
ver-zass.bat
```

Levanta la base, la API y la interfaz, espera a que la API responda y abre el navegador en
`https://localhost:5173`. Para cerrar todo: `cerrar-zass.bat`.

Una sola vez por máquina, para que el navegador no avise del certificado:

```bat
dotnet dev-certs https --trust
```

Las pruebas, con la base arriba:

```bat
cd backend && dotnet test
cd frontend && npm run prueba
```

## Qué no está en el repositorio, y por qué

| Fuera | Razón |
|---|---|
| `herramientas/` | La instancia portable de PostgreSQL y PostGIS: 1,2 GB |
| `datos-pg/`, `registros/` | Los datos y los registros del clúster local |
| `.clave-pg.txt`, `certificados/` | Un secreto y una clave privada |
| `appsettings.Development.json` | Sube los límites de tasa para poder desarrollar. En producción mandan los valores del código |
| `node_modules/` | 300 MB. `npm install` dentro de `frontend` |

La API necesita dos ajustes que nunca se versionan y viven en User Secrets:
`Zass:CadenaConexion` y `Zass:ClaveDireccionesBase64`.

## Datos de terceros

Las coordenadas de las 16 zonas de Temuco provienen de **OpenStreetMap**, bajo licencia
[ODbL](https://www.openstreetmap.org/copyright). Cualquier pantalla que las muestre debe
atribuir a *OpenStreetMap contributors*.

## Licencia

Software privativo. © Pablo Espinoza. Todos los derechos reservados.
