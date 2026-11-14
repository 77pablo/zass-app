# Auditoría de dependencias — 14-nov-2026

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
