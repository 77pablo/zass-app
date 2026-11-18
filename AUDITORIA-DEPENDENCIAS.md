# Auditoría de dependencias — 14-nov-2026

## 17-nov-2026: segunda pasada

Segunda auditoría de dependencias (3 días después). Mismas herramientas: `npm audit`, `npm outdated`, `dotnet list package --vulnerable --include-transitive`, `dotnet list package --outdated`. **No se cambia ninguna dependencia**: solo informe.

**Resumen:** **7 vulnerabilidades altas en el frontend** (todas en dependencias de desarrollo: `@vitejs/plugin-react`, `@vitest/coverage-v8`, `fdir`, `picomatch`, `tinyglobby`, `vite`, `vitest`); **0 en producción**. Backend: **ninguna vulnerabilidad** en ningún proyecto (las 2 altas de la pasada anterior se corrigieron subiendo `xunit` a 2.9.3). **.NET 8 llegó a fin de soporte el 10-nov-2026** (fuente: <https://dotnet.microsoft.com/platform/support/policy/dotnet-core>, tabla «Out of support versions», fila .NET 8). Lo demás es atraso de versión.

### 1. Vulnerabilidades frontend (`npm audit --json`)

| Paquete | Severidad | Directa/Transitiva | Producción/Pruebas | Fix disponible (mayor) |
|---|---|---|---|---|
| `@vitejs/plugin-react` | **high** | Directa | Desarrollo (build) | `@vitejs/plugin-react` 5.2.0 |
| `@vitest/coverage-v8` | **high** | Directa | Pruebas | `@vitest/coverage-v8` 3.1.4 |
| `fdir` | **high** | Transitiva (`picomatch`) | Desarrollo | — (via vitest 3.1.4) |
| `picomatch` | **high** | Transitiva | Desarrollo | — (via vitest 3.1.4) |
| `tinyglobby` | **high** | Transitiva (`fdir`, `picomatch`) | Desarrollo | — (via vitest 3.1.4) |
| `vite` | **high** | Directa | Desarrollo (build) | `vite` 6.2.7 |
| `vitest` | **high** | Directa | Pruebas | `vitest` 3.1.4 |

**Total:** 7 altas, 0 críticas, 0 moderadas, 0 bajas. **Producción (react, react-dom, react-router-dom, qrcode-generator): 0**.

### 2. Frontend: paquetes desactualizados (`npm outdated`)

| Paquete | Actual | Al día | Salto | Producción/Pruebas |
|---|---|---|---|---|
| `@testing-library/user-event` | 14.6.8 | 14.6.12 | parche | Pruebas |
| `@types/node` | 24.19.2 | 26.6.6 | **mayor** | Desarrollo |
| `@vitejs/plugin-react` | 6.2.1 | 6.3.0 | menor | Desarrollo |
| `jsdom` | 31.0.0 | 31.0.1 | parche | Pruebas |
| `oxlint` | 1.92.0 | 1.94.0 | menor | Desarrollo |
| `playwright` | 1.65.0 | 1.65.1 | parche | Pruebas |
| `vite` | 8.3.6 | 8.3.8 | parche | Desarrollo (build) |

**Cambio vs 14-nov:** **aparecen 7 vulnerabilidades altas** (antes 0). El resto igual.

### 3. `qrcode-generator@2.0.4` (entró en PR #51)

- Licencia: **MIT** (`npm view qrcode-generator@2.0.4 license`).
- Fecha de publicación: **2025-08-07** (`npm view qrcode-generator time --json` → `"2.0.4": "2025-08-07T14:11:54.669Z"`).
- No tiene vulnerabilidades conocidas.

### 4. Backend: vulnerabilidades (`dotnet list package --vulnerable --include-transitive`, proyecto por proyecto)

| Proyecto | Resultado |
|---|---|
| `Zass.Dominio` | Sin vulnerabilidades |
| `Zass.Aplicacion` | Sin vulnerabilidades |
| `Zass.Api` | Sin vulnerabilidades |
| `Zass.Infraestructura` | Sin vulnerabilidades |
| `Zass.Aplicacion.Pruebas` | Sin vulnerabilidades |
| `Zass.Arquitectura.Pruebas` | Sin vulnerabilidades |
| `Zass.Integracion.Pruebas` | Sin vulnerabilidades |

**Cambio vs 14-nov:** **las 2 vulnerabilidades altas en proyectos de pruebas (`System.Net.Http` y `System.Text.RegularExpressions`) ya no aparecen** (se corrigieron subiendo `xunit` a 2.9.3).

### 5. Backend: paquetes desactualizados (`dotnet list package --outdated`, proyecto por proyecto)

#### `Zass.Aplicacion`
| Paquete | Actual | Más reciente | Tipo |
|---|---|---|---|
| `FluentValidation` | 11.11.0 | 12.2.0 | **mayor** |
| `NetTopologySuite` | 2.5.0 | 2.6.0 | menor |

#### `Zass.Api`
| Paquete | Actual | Más reciente | Tipo |
|---|---|---|---|
| `Swashbuckle.AspNetCore` | 7.2.0 | 11.0.1 | **mayor** |

#### `Zass.Infraestructura`
| Paquete | Actual | Más reciente | Tipo |
|---|---|---|---|
| `FluentValidation.DependencyInjectionExtensions` | 11.11.0 | 12.2.0 | **mayor** |
| `Microsoft.EntityFrameworkCore.Design` | 8.0.10 | 11.0.0 | **mayor** |
| `Microsoft.Extensions.Configuration.Binder` | 8.0.2 | 11.0.0 | **mayor** |
| `Microsoft.Extensions.Hosting.Abstractions` | 8.0.1 | 11.0.0 | **mayor** |
| `Microsoft.Extensions.Logging.Abstractions` | 8.0.2 | 11.0.0 | **mayor** |
| `Microsoft.Extensions.Options.ConfigurationExtensions` | 8.0.0 | 11.0.0 | **mayor** |
| `Microsoft.Extensions.Options.DataAnnotations` | 8.0.0 | 11.0.0 | **mayor** |
| `Npgsql.EntityFrameworkCore.PostgreSQL` | 8.0.10 | 10.0.3 | **mayor** |
| `Npgsql.EntityFrameworkCore.PostgreSQL.NetTopologySuite` | 8.0.10 | 10.0.3 | **mayor** |

#### `Zass.Aplicacion.Pruebas` / `Zass.Arquitectura.Pruebas`
| Paquete | Actual | Más reciente | Tipo |
|---|---|---|---|
| `coverlet.collector` | 6.0.0 | 10.1.1 | **mayor** |
| `Microsoft.NET.Test.Sdk` | 17.8.0 | 18.12.0 | **mayor** |
| `xunit.runner.visualstudio` | 2.8.2 | 4.0.1 | **mayor** |

#### `Zass.Integracion.Pruebas`
| Paquete | Actual | Más reciente | Tipo |
|---|---|---|---|
| `coverlet.collector` | 6.0.0 | 10.1.1 | **mayor** |
| `Microsoft.AspNetCore.Mvc.Testing` | 8.0.10 | 11.0.0 | **mayor** |
| `Microsoft.Extensions.Configuration` | 8.0.0 | 11.0.0 | **mayor** |
| `Microsoft.NET.Test.Sdk` | 17.8.0 | 18.12.0 | **mayor** |
| `xunit.runner.visualstudio` | 2.8.2 | 4.0.1 | **mayor** |

### 6. .NET 8 — fin de soporte

- **Fecha fin de soporte:** **10-nov-2026** (segunda martes de noviembre — Patch Tuesday).
- **Fuente:** <https://dotnet.microsoft.com/platform/support/policy/dotnet-core> (tabla «Out of support versions», fila .NET 8: «End of support: November 10, 2026»).
- **Estado:** **fuera de soporte** desde el 10-nov-2026. No hay más parches de seguridad del runtime.

---

Primera auditoría de dependencias del proyecto. Se miró el frontend (`npm audit`,
`npm outdated`) y el backend (`dotnet list package --vulnerable --include-transitive`,
`dotnet list package --outdated`).

**Resumen:** dos avisos de seguridad reales, los dos **altos**, los dos **solo en los
proyectos de pruebas** y ya **arreglados en este PR**. Ningún paquete de producción, ni
en backend ni en frontend, tiene vulnerabilidad conocida. Lo demás es atraso de versión,
y el punto importante que necesita decisión de Pablo es que **.NET 8 llegó a fin de
soporte el 10-nov-2026**.

## 1. Vulnerabilidades encontradas (y arregladas aquí)

| Paquete | Versión | Versión segura | Directa o transitiva | Producción o pruebas | Qué pasa si no se hace nada |
|---|---|---|---|---|---|
| `System.Net.Http` 4.3.0 ([GHSA-7jgj-8wvc-jh57](https://github.com/advisories/GHSA-7jgj-8wvc-jh57), alta) | 4.3.0 | se va al subir `xunit` a 2.9.3 | Transitiva (`xunit` 2.5.3 → `NETStandard.Library` 1.6.1) | **Solo pruebas** (los tres proyectos de `backend/pruebas`) | El aviso sigue saliendo en cada auditoría y tapa el día que aparezca uno de verdad. El riesgo real es bajo: en `net8.0` esos paquetes son ensamblados de fachada y en ejecución manda el framework compartido |
| `System.Text.RegularExpressions` 4.3.0 ([GHSA-cmhx-cq75-c4mj](https://github.com/advisories/GHSA-cmhx-cq75-c4mj), alta) | 4.3.0 | se va al subir `xunit` a 2.9.3 | Transitiva (misma cadena) | **Solo pruebas** | Igual que la anterior |

Arreglo aplicado en los tres proyectos de pruebas: `xunit` 2.5.3 → **2.9.3** y
`xunit.runner.visualstudio` 2.5.3 → **2.8.2**. Ambos saltos son dentro de la misma versión
mayor y no cambian ninguna API que el proyecto use. Después del cambio,
`dotnet list package --vulnerable --include-transitive` da limpio en los siete proyectos.

Efecto secundario conocido: los analizadores nuevos de xunit añaden **28 avisos de estilo**
al compilar (xUnit2031 «Assert.Single con Where» y xUnit2029 «Assert.Empty en vez de
Assert.DoesNotContain»). Son recomendaciones de estilo en código de pruebas, no fallos, y el
CI no trata los avisos como errores. Se dejan tal cual para no mezclar cambios de pruebas en
un PR de dependencias.

## 2. Frontend

`npm audit`: **0 vulnerabilidades** (179 dependencias). No hay nada que arreglar, ni siquiera
en herramientas de desarrollo.

Lo único que hay es atraso de versión, y **todo es dependencia de desarrollo**; las tres de
producción (`react`, `react-dom`, `react-router-dom`) están al día.

| Paquete | Actual | Al día | Salto | Producción o pruebas | Qué pasa si no se hace nada |
|---|---|---|---|---|---|
| `@testing-library/user-event` | 14.6.8 | 14.6.10 | parche | Pruebas | Nada; `npm install` ya lo instalaría |
| `@vitejs/plugin-react` | 6.2.1 | 6.3.0 | menor | Desarrollo | Nada |
| `jsdom` | 31.0.0 | 31.0.1 | parche | Pruebas | Nada |
| `oxlint` | 1.92.0 | 1.93.0 | menor | Desarrollo | Nada |
| `playwright` | 1.65.0 | 1.65.1 | parche | Pruebas | Nada |
| `vite` | 8.3.6 | 8.3.8 | parche | Desarrollo (construye el sitio) | Nada conocido |
| `@types/node` | 24.19.2 | 26.6.6 | **mayor** | Desarrollo | Nada hoy; se revisa cuando se suba Node |

No se tocó nada del frontend: ninguno de estos atrasos tiene vulnerabilidad conocida ni
arreglo que haga falta. Los seis primeros ya caben en los rangos de `package.json`, así que
se recogen solos al refrescar `package-lock.json` cuando toque.

## 3. Backend: atrasos que NO se tocaron

Nada de esto tiene vulnerabilidad conocida. Todo son decisiones de Pablo.

| Paquete | Actual | Recomendado | Directa o transitiva | Producción o pruebas | Riesgo de no hacer nada |
|---|---|---|---|---|---|
| **Runtime .NET 8** (`net8.0` en los 7 proyectos) | 8.0 | .NET 10 (LTS) | — | **Producción** | **Lo más serio del informe.** .NET 8 llegó a fin de soporte el **10-nov-2026**: a partir de ahora no hay parches de seguridad del runtime. Subir de versión mayor toca todo el proyecto y es decisión de Pablo |
| `Microsoft.EntityFrameworkCore.Design` | 8.0.10 | 8.0.34 (parche) o esperar al salto de .NET | Directa | Producción (herramienta de diseño, `PrivateAssets=all`) | 24 parches de atraso. Sin vulnerabilidad conocida. No se subió para no mover EF en vísperas de abrir, y porque el salto de .NET lo arrastra |
| `Npgsql.EntityFrameworkCore.PostgreSQL` (+ `.NetTopologySuite`) | 8.0.10 | 8.0.11 (parche) | Directa | **Producción, la base de datos** | Sin vulnerabilidad conocida. Es el paquete que habla con Postgres: un parche aquí se prueba con la suite de integración completa, que este PR no puede correr |
| `Microsoft.Extensions.*` (6 paquetes) | 8.0.0–8.0.2 | al día dentro de 8.0 | Directas | Producción | Ninguno con vulnerabilidad. Van atados al runtime |
| `FluentValidation` (+ `.DependencyInjectionExtensions`) | 11.11.0 | 12.2.0 | Directas | Producción | **Salto mayor con cambios de API** (la 12 cambia validadores y extensiones). No se toca |
| `NetTopologySuite` | 2.5.0 | 2.6.0 | Directa | Producción (geometría/PostGIS) | Menor, pero va emparejado con el proveedor de Npgsql: se suben juntos o ninguno |
| `Swashbuckle.AspNetCore` | 7.2.0 | 11.0.1 | Directa | Producción (Swagger) | Cuatro versiones mayores de distancia. Sin vulnerabilidad conocida |
| `Microsoft.AspNetCore.Mvc.Testing` | 8.0.10 | 8.0.34 | Directa | Pruebas (integración) | Debe ir igualado al runtime; se mueve cuando se mueva .NET |
| `Microsoft.NET.Test.Sdk` | 17.8.0 | 18.12.0 | Directa | Pruebas | Salto mayor del ejecutor de pruebas; se dejó fuera para no arriesgar el CI en este PR |
| `coverlet.collector` | 6.0.0 | 10.1.0 | Directa | Pruebas (cobertura) | Salto mayor. No urge |
| `dotnet-ef` (herramienta) | 8.0.10 | igual al runtime | Herramienta | Desarrollo | Debe quedar igualada a EF cuando se decida subir |

## 4. La restricción de `Zass.Dominio`

`Zass.Dominio` no admite ni un solo paquete NuGet. Se comprobó: su `.csproj` **no tiene
ningún `PackageReference`** y ninguna recomendación de este informe lo tocaría; los
hallazgos están todos en los proyectos de pruebas, en `Zass.Infraestructura` y en
`Zass.Api`. La restricción queda intacta.

## 5. Recomendación de orden

1. Decidir el salto de .NET 8 → 10 (fin de soporte). Arrastra EF, Npgsql,
   `Microsoft.Extensions.*`, `Mvc.Testing` y `dotnet-ef`: conviene una sola operación.
2. Después del salto, `Swashbuckle` y `FluentValidation` 12, cada uno en su PR.
3. Refrescar `package-lock.json` del frontend en cualquier PR que ya toque el frontend.
