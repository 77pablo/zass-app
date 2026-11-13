# C1 · Cupo fundador — plan de implementación

> **Para quien lo ejecute:** usa `superpowers:subagent-driven-development` (recomendado) o
> `superpowers:executing-plans` tarea por tarea. Los pasos llevan casilla (`- [ ]`).

**Objetivo:** que un prestador pueda tomar uno de los 50 cupos fundador, inscribir su medio de
pago y quedar con el plan fundador y el mes gratis corriendo, visible en la búsqueda.

**Especificación:** `docs/superpowers/specs/2026-11-13-cupo-fundador-design.md`. Si algo de este
plan la contradice, manda la especificación y se dice en el PR.

**Arquitectura:** un puerto `IPasarelaDePagos` en la capa de aplicación con forma de
**redirección más aviso por detrás** (la que tienen Transbank, Flow y Mercado Pago), un adaptador
simulado que firma sus propios avisos con HMAC para que la verificación de firma esté ejercitada
desde el primer día, tres endpoints nuevos y una página pública `/planes`. La lógica de cupos ya
existe y no se toca: esto la enchufa.

**Herramientas:** .NET 8, minimal APIs, EF Core, xUnit · React 19 + TypeScript + Vite, vitest,
@testing-library/react, Playwright.

## Restricciones globales

- `Zass.Dominio` tiene **cero paquetes NuGet**. El puerto va en `Zass.Aplicacion/Pagos/`.
- **Ninguna migración de EF.** Si aparece la necesidad, se para y se avisa antes de crearla.
- Nunca se trabaja en `main`. Rama de este plan: `cupo-fundador` (ya creada, sale de
  `integracion/13-nov`).
- Toda prueba nueva necesita su **mutación**: romper a propósito el código que vigila, ver caer la
  prueba, deshacer. Sin eso no cuenta.
- **Candado con literal:** 50 cupos, 25 minutos, 30 días, 5990 y 7990 escritos a mano en una prueba
  aparte, además de la que use la constante.
- Secretos: jamás en el repositorio, ni en una prueba, ni en un `appsettings` versionado.
- Textos visibles: se proponen, no se deciden. Van listados en el PR bajo «Textos nuevos».
- Las pruebas de integración usan `zass_pruebas` y **nunca** corren dos suites a la vez.
- `403` y `404` responden igual, a propósito (`Respuestas.cs`).

## Mapa de archivos

| Archivo | Responsabilidad |
|---|---|
| `backend/src/Zass.Aplicacion/Pagos/IPasarelaDePagos.cs` | El puerto y sus registros. Nada más |
| `backend/src/Zass.Infraestructura/Pagos/OpcionesPagos.cs` | Sección `Zass:Pagos` y su validador (la guarda de Producción) |
| `backend/src/Zass.Infraestructura/Pagos/PasarelaSimulada.cs` | El adaptador falso: referencia, URL de redirección, firma HMAC |
| `backend/src/Zass.Aplicacion/Suscripciones/ServicioSuscripciones.cs` | Dos métodos nuevos: tomar el cupo y confirmar la inscripción |
| `backend/src/Zass.Aplicacion/Puertos/Puertos.cs` | Un método nuevo de lectura: el cupo de un perfil |
| `backend/src/Zass.Infraestructura/Datos/Repositorios/RepositorioSuscripciones.cs` | Su implementación EF |
| `backend/src/Zass.Api/Endpoints/EndpointsSuscripciones.cs` | API-66, API-67 y API-68 |
| `frontend/src/funciones/planes/servicio.ts` | Las tres llamadas y sus tipos |
| `frontend/src/paginas/Planes.tsx` | La página pública y sus cuatro estados |
| `frontend/src/paginas/PagoSimulado.tsx` | La pantalla del adaptador falso |
| `docs/01-casos-de-uso/subsistema-7-suscripciones-y-pagos.md` | El documento rector que no existe |

---

### Task 1: El puerto, sus opciones y el adaptador simulado

**Archivos:**
- Crear: `backend/src/Zass.Aplicacion/Pagos/IPasarelaDePagos.cs`
- Crear: `backend/src/Zass.Infraestructura/Pagos/OpcionesPagos.cs`
- Crear: `backend/src/Zass.Infraestructura/Pagos/PasarelaSimulada.cs`
- Modificar: `backend/src/Zass.Infraestructura/Configuracion/AgregarZass.cs` (registro, junto al
  bloque `--- MOD-13 Suscripciones ---`, línea ~180)
- Prueba: `backend/pruebas/Zass.Integracion.Pruebas/Pagos/PasarelaSimuladaPruebas.cs`

**Interfaces:**
- Consume: `Resultado`, `Resultado<T>`, `Fallas` (de `Zass.Dominio.Comun`).
- Produce: `IPasarelaDePagos` con `IniciarInscripcionAsync(SolicitudDeInscripcion, CancellationToken)`,
  `LeerAvisoAsync(AvisoEntrante, CancellationToken)`, `CobrarAsync(SolicitudDeCobro, CancellationToken)`;
  y los registros `SolicitudDeInscripcion(Guid SuscripcionId, Guid PerfilPrestadorId, short PlanId, string UrlDeRetorno)`,
  `InicioDeInscripcion(string ReferenciaExterna, string UrlParaRedirigir)`,
  `AvisoEntrante(string Cuerpo, string Firma)`,
  `ResultadoDeInscripcion(string ReferenciaExterna, Guid PerfilPrestadorId, int Monto, bool Aceptada, string? Motivo)`,
  `SolicitudDeCobro(Guid SuscripcionId, int Monto, string ReferenciaDeMedio)`,
  `ResultadoDeCobro(bool Aceptado, string ReferenciaExterna, string? Motivo)`.

- [ ] **Paso 1: Escribir el puerto**

`backend/src/Zass.Aplicacion/Pagos/IPasarelaDePagos.cs`:

```csharp
using Zass.Dominio.Comun;

namespace Zass.Aplicacion.Pagos;

/// <summary>
/// S7. El cobro, visto desde la aplicacion. Tiene forma de REDIRECCION mas AVISO POR DETRAS
/// porque ninguna pasarela chilena cobra de forma sincrona: la persona se va al sitio del
/// banco y la respuesta vuelve por el servidor. Un puerto que devolviera si/no al tiro
/// obligaria a rehacer pantalla, flujo y pruebas el dia que entre la pasarela real.
///
/// Aqui NUNCA viaja un numero de tarjeta. Solo referencias opacas del proveedor.
/// </summary>
public interface IPasarelaDePagos
{
    /// <summary>Autoriza cobros futuros (Oneclick). NO cobra: el monto es 0.</summary>
    Task<Resultado<InicioDeInscripcion>> IniciarInscripcionAsync(
        SolicitudDeInscripcion solicitud, CancellationToken ct = default);

    /// <summary>
    /// Interpreta un aviso entrante. El adaptador verifica la firma ANTES de mirar el
    /// cuerpo; si no cuadra, falla con <see cref="Fallas.NoAutorizado"/> y nadie se entera
    /// de que habia dentro.
    /// </summary>
    Task<Resultado<ResultadoDeInscripcion>> LeerAvisoAsync(
        AvisoEntrante aviso, CancellationToken ct = default);

    /// <summary>Cobra contra un medio ya inscrito. Se declara en C1; se usa en C3.</summary>
    Task<Resultado<ResultadoDeCobro>> CobrarAsync(
        SolicitudDeCobro solicitud, CancellationToken ct = default);
}

public sealed record SolicitudDeInscripcion(
    Guid SuscripcionId, Guid PerfilPrestadorId, short PlanId, string UrlDeRetorno);

public sealed record InicioDeInscripcion(string ReferenciaExterna, string UrlParaRedirigir);

/// <summary>Lo que llego, crudo. La firma se compara con lo que se calcula sobre el cuerpo.</summary>
public sealed record AvisoEntrante(string Cuerpo, string Firma);

public sealed record ResultadoDeInscripcion(
    string ReferenciaExterna, Guid PerfilPrestadorId, int Monto, bool Aceptada, string? Motivo);

public sealed record SolicitudDeCobro(Guid SuscripcionId, int Monto, string ReferenciaDeMedio);

public sealed record ResultadoDeCobro(bool Aceptado, string ReferenciaExterna, string? Motivo);
```

- [ ] **Paso 2: Escribir la prueba de la firma, que debe fallar**

`backend/pruebas/Zass.Integracion.Pruebas/Pagos/PasarelaSimuladaPruebas.cs`:

```csharp
using System.Text.Json;
using Microsoft.Extensions.Options;
using Zass.Aplicacion.Pagos;
using Zass.Infraestructura.Pagos;

namespace Zass.Integracion.Pruebas.Pagos;

/// <summary>
/// El adaptador falso firma sus propios avisos a proposito: asi la verificacion de firma
/// queda ejercitada desde el primer dia, en vez de escribirse por primera vez el dia que
/// entre dinero de verdad.
/// </summary>
public class PasarelaSimuladaPruebas
{
    private const string Clave = "clave-de-prueba-no-es-un-secreto-real";

    private static PasarelaSimulada Nueva() => new(Options.Create(new OpcionesPagos
    {
        Proveedor = ProveedorDePagos.Simulada,
        ClaveDeFirmaSimulada = Clave,
        UrlPublica = "https://local.test",
    }));

    private static string Firmar(string cuerpo) => PasarelaSimulada.Firma(Clave, cuerpo);

    [Fact]
    public async Task Iniciar_devuelve_referencia_y_una_url_a_la_pantalla_simulada()
    {
        var r = await Nueva().IniciarInscripcionAsync(new SolicitudDeInscripcion(
            Guid.NewGuid(), Guid.NewGuid(), 3, "https://local.test/planes/listo"));

        Assert.True(r.Exito);
        Assert.NotEmpty(r.Valor!.ReferenciaExterna);
        Assert.Contains("/pago/inscripcion?ref=", r.Valor!.UrlParaRedirigir);
    }

    [Fact]
    public async Task Dos_inicios_no_repiten_la_referencia()
    {
        var pasarela = Nueva();
        var a = await pasarela.IniciarInscripcionAsync(new SolicitudDeInscripcion(
            Guid.NewGuid(), Guid.NewGuid(), 3, "https://local.test/planes/listo"));
        var b = await pasarela.IniciarInscripcionAsync(new SolicitudDeInscripcion(
            Guid.NewGuid(), Guid.NewGuid(), 3, "https://local.test/planes/listo"));

        Assert.NotEqual(a.Valor!.ReferenciaExterna, b.Valor!.ReferenciaExterna);
    }

    [Fact]
    public async Task Un_aviso_bien_firmado_se_lee()
    {
        var perfil = Guid.NewGuid();
        var cuerpo = JsonSerializer.Serialize(new
        {
            referencia = "sim_abc", perfilPrestadorId = perfil, monto = 0, aceptada = true,
        });

        var r = await Nueva().LeerAvisoAsync(new AvisoEntrante(cuerpo, Firmar(cuerpo)));

        Assert.True(r.Exito);
        Assert.Equal(perfil, r.Valor!.PerfilPrestadorId);
        Assert.True(r.Valor!.Aceptada);
        Assert.Equal(0, r.Valor!.Monto);
    }

    [Fact]
    public async Task Un_aviso_con_firma_mala_no_se_lee_y_no_dice_que_traia()
    {
        var cuerpo = JsonSerializer.Serialize(new
        {
            referencia = "sim_abc", perfilPrestadorId = Guid.NewGuid(), monto = 0, aceptada = true,
        });

        var r = await Nueva().LeerAvisoAsync(new AvisoEntrante(cuerpo, "firma-inventada"));

        Assert.False(r.Exito);
        Assert.Equal(Fallas.NoAutorizado, r.Codigo);
        Assert.Null(r.Valor);
    }

    [Fact]
    public async Task Cambiar_un_byte_del_cuerpo_invalida_la_firma()
    {
        var cuerpo = JsonSerializer.Serialize(new
        {
            referencia = "sim_abc", perfilPrestadorId = Guid.NewGuid(), monto = 0, aceptada = true,
        });
        var firma = Firmar(cuerpo);
        var alterado = cuerpo.Replace("\"monto\":0", "\"monto\":5990");

        var r = await Nueva().LeerAvisoAsync(new AvisoEntrante(alterado, firma));

        Assert.False(r.Exito);
        Assert.Equal(Fallas.NoAutorizado, r.Codigo);
    }
}
```

Añade `using Zass.Dominio.Comun;` arriba para `Fallas`.

- [ ] **Paso 3: Correr la prueba y verla fallar**

```
cd backend
dotnet test pruebas/Zass.Integracion.Pruebas --filter "FullyQualifiedName~PasarelaSimulada"
```

Esperado: **no compila** — «The type or namespace name 'PasarelaSimulada' could not be found».
Eso cuenta como rojo: la prueba no puede pasar porque lo que vigila no existe.

- [ ] **Paso 4: Escribir las opciones y su validador**

`backend/src/Zass.Infraestructura/Pagos/OpcionesPagos.cs`:

```csharp
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Zass.Infraestructura.Pagos;

/// <summary>Que pasarela cobra. Simulada = desarrollo: no existe dinero de por medio.</summary>
public enum ProveedorDePagos { Simulada, Transbank, Flow, MercadoPago }

/// <summary>
/// Seccion <c>Zass:Pagos</c>. La clave de firma viene SOLO de la configuracion del entorno,
/// nunca del codigo ni del repositorio.
/// </summary>
public sealed class OpcionesPagos
{
    public const string Seccion = "Zass:Pagos";

    public ProveedorDePagos Proveedor { get; set; } = ProveedorDePagos.Simulada;

    /// <summary>Con la simulada, con que se firman los avisos. Obligatoria si Proveedor=Simulada.</summary>
    public string ClaveDeFirmaSimulada { get; set; } = "";

    /// <summary>Base de la URL de retorno y de la pantalla de pago falsa.</summary>
    public string UrlPublica { get; set; } = "";
}

/// <summary>
/// La guarda que importa: la pasarela SIMULADA no existe en Produccion. Un cobro de mentira
/// corriendo en produccion sin que nadie lo note es la clase de error que se descubre por el
/// lado de la plata, asi que la aplicacion no levanta.
///
/// Mismo patron que ValidadorOpcionesCorreo: con ValidateOnStart, esto corre al arrancar.
/// </summary>
public sealed class ValidadorOpcionesPagos : IValidateOptions<OpcionesPagos>
{
    private readonly IHostEnvironment _entorno;

    public ValidadorOpcionesPagos(IHostEnvironment entorno) => _entorno = entorno;

    public ValidateOptionsResult Validate(string? nombre, OpcionesPagos opciones)
    {
        if (opciones.Proveedor == ProveedorDePagos.Simulada && _entorno.IsProduction())
            return ValidateOptionsResult.Fail(
                "Zass:Pagos:Proveedor es Simulada y el entorno es Produccion. La pasarela " +
                "simulada no cobra: configura una real antes de desplegar.");

        if (opciones.Proveedor == ProveedorDePagos.Simulada
            && string.IsNullOrWhiteSpace(opciones.ClaveDeFirmaSimulada))
            return ValidateOptionsResult.Fail(
                "Zass:Pagos:ClaveDeFirmaSimulada es obligatoria con la pasarela simulada: " +
                "sin ella los avisos no se pueden firmar ni verificar.");

        if (string.IsNullOrWhiteSpace(opciones.UrlPublica))
            return ValidateOptionsResult.Fail("Zass:Pagos:UrlPublica es obligatoria.");

        return ValidateOptionsResult.Success;
    }
}
```

- [ ] **Paso 5: Escribir el adaptador simulado**

`backend/src/Zass.Infraestructura/Pagos/PasarelaSimulada.cs`:

```csharp
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Zass.Aplicacion.Pagos;
using Zass.Dominio.Comun;

namespace Zass.Infraestructura.Pagos;

/// <summary>
/// La pasarela de desarrollo. Recorre el camino COMPLETO —referencia, redireccion, aviso
/// firmado por detras— sin red y sin dinero, para que cambiar a Transbank o Flow sea escribir
/// otra clase y no rehacer el flujo.
///
/// No levanta en Produccion: ValidadorOpcionesPagos lo impide al arrancar.
/// </summary>
public sealed class PasarelaSimulada : IPasarelaDePagos
{
    private readonly OpcionesPagos _opciones;

    public PasarelaSimulada(IOptions<OpcionesPagos> opciones) => _opciones = opciones.Value;

    /// <summary>HMAC-SHA256 del cuerpo, en hexadecimal minuscula. Publico para que la pantalla falsa firme igual.</summary>
    public static string Firma(string clave, string cuerpo)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(clave));
        return Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(cuerpo))).ToLowerInvariant();
    }

    public Task<Resultado<InicioDeInscripcion>> IniciarInscripcionAsync(
        SolicitudDeInscripcion solicitud, CancellationToken ct = default)
    {
        // Opaca y no secuencial: una referencia adivinable deja probar avisos por fuerza bruta.
        var referencia = "sim_" + Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
        var url = $"{_opciones.UrlPublica.TrimEnd('/')}/pago/inscripcion?ref={referencia}"
                + $"&perfil={solicitud.PerfilPrestadorId}";

        return Task.FromResult(Resultado.Ok(new InicioDeInscripcion(referencia, url)));
    }

    public Task<Resultado<ResultadoDeInscripcion>> LeerAvisoAsync(
        AvisoEntrante aviso, CancellationToken ct = default)
    {
        var esperada = Firma(_opciones.ClaveDeFirmaSimulada, aviso.Cuerpo);

        // Comparacion en tiempo fijo: comparar firmas con == filtra informacion por el reloj.
        var iguales = CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(esperada), Encoding.UTF8.GetBytes(aviso.Firma ?? ""));

        if (!iguales)
            return Task.FromResult(Resultado.Falla<ResultadoDeInscripcion>(
                Fallas.NoAutorizado, "Aviso no reconocido."));

        var cuerpo = JsonSerializer.Deserialize<CuerpoDeAviso>(aviso.Cuerpo);
        if (cuerpo is null || cuerpo.Referencia is null)
            return Task.FromResult(Resultado.Falla<ResultadoDeInscripcion>(
                Fallas.Validacion, "Aviso sin contenido."));

        return Task.FromResult(Resultado.Ok(new ResultadoDeInscripcion(
            cuerpo.Referencia, cuerpo.PerfilPrestadorId, cuerpo.Monto, cuerpo.Aceptada, cuerpo.Motivo)));
    }

    public Task<Resultado<ResultadoDeCobro>> CobrarAsync(
        SolicitudDeCobro solicitud, CancellationToken ct = default) =>
        Task.FromResult(Resultado.Ok(new ResultadoDeCobro(
            true, "sim_cobro_" + Convert.ToHexString(RandomNumberGenerator.GetBytes(8)).ToLowerInvariant(), null)));

    private sealed record CuerpoDeAviso(
        string? Referencia, Guid PerfilPrestadorId, int Monto, bool Aceptada, string? Motivo);
}
```

Si `JsonSerializer` no casa los nombres en minúscula, añade
`JsonSerializerOptions { PropertyNameCaseInsensitive = true }` y úsalo en el `Deserialize`.

- [ ] **Paso 6: Registrarlo**

En `AgregarZass.cs`, justo debajo del bloque `--- MOD-13 Suscripciones ---`:

```csharp
        // --- S7 Pagos ---
        // Con Proveedor=Simulada en Produccion la aplicacion NO levanta (ValidadorOpcionesPagos).
        servicios.AddOptions<OpcionesPagos>()
            .Bind(configuracion.GetSection(OpcionesPagos.Seccion))
            .ValidateOnStart();
        servicios.AddSingleton<IValidateOptions<OpcionesPagos>, ValidadorOpcionesPagos>();
        servicios.AddScoped<IPasarelaDePagos, PasarelaSimulada>();
```

Añade los `using Zass.Aplicacion.Pagos;` y `using Zass.Infraestructura.Pagos;` que falten.

- [ ] **Paso 7: Correr las pruebas y verlas pasar**

```
cd backend
dotnet test pruebas/Zass.Integracion.Pruebas --filter "FullyQualifiedName~PasarelaSimulada"
```

Esperado: **5 passed**.

- [ ] **Paso 8: La prueba de la guarda de Producción, y su mutación**

Añade al final de `PasarelaSimuladaPruebas.cs`:

```csharp
public class GuardaDeProduccionPruebas
{
    private sealed class Entorno : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Production";
        public string ApplicationName { get; set; } = "Zass.Api";
        public string ContentRootPath { get; set; } = ".";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private static OpcionesPagos Validas() => new()
    {
        Proveedor = ProveedorDePagos.Simulada,
        ClaveDeFirmaSimulada = "clave-de-prueba",
        UrlPublica = "https://local.test",
    };

    [Fact]
    public void La_simulada_no_pasa_la_validacion_en_Produccion()
    {
        var r = new ValidadorOpcionesPagos(new Entorno { EnvironmentName = "Production" })
            .Validate(null, Validas());

        Assert.True(r.Failed);
        Assert.Contains("no cobra", r.FailureMessage);
    }

    [Fact]
    public void La_simulada_si_pasa_en_Desarrollo()
    {
        var r = new ValidadorOpcionesPagos(new Entorno { EnvironmentName = "Development" })
            .Validate(null, Validas());

        Assert.True(r.Succeeded);
    }

    [Fact]
    public void La_simulada_sin_clave_de_firma_no_pasa_ni_en_Desarrollo()
    {
        var opciones = Validas();
        opciones.ClaveDeFirmaSimulada = "";

        var r = new ValidadorOpcionesPagos(new Entorno { EnvironmentName = "Development" })
            .Validate(null, opciones);

        Assert.True(r.Failed);
    }
}
```

Usa `Microsoft.Extensions.FileProviders` para `NullFileProvider`.

**La mutación, obligatoria:** comenta —no borres— la primera comprobación de
`ValidadorOpcionesPagos.Validate` (la de `IsProduction`), corre
`dotnet test pruebas/Zass.Integracion.Pruebas --filter "FullyQualifiedName~GuardaDeProduccion"`,
y comprueba que **cae `La_simulada_no_pasa_la_validacion_en_Produccion`**. Después descoméntala y
vuelve a verlas en verde. Pega el resultado en el PR.

- [ ] **Paso 9: Commit**

```bash
git add backend/src/Zass.Aplicacion/Pagos backend/src/Zass.Infraestructura/Pagos \
        backend/src/Zass.Infraestructura/Configuracion/AgregarZass.cs \
        backend/pruebas/Zass.Integracion.Pruebas/Pagos
git commit -m "S7: el puerto de pasarela, el adaptador simulado y la guarda de produccion"
```

---

### Task 2: El repositorio sabe en qué estado está el cupo de un perfil

Hace falta para la idempotencia del aviso: **el cupo confirmado es el registro de que el aviso ya
se procesó**, y así no hace falta ninguna tabla nueva ni ninguna migración.

**Archivos:**
- Modificar: `backend/src/Zass.Aplicacion/Puertos/Puertos.cs` (dentro de `IRepositorioSuscripciones`,
  junto a los demás métodos de cupo, ~línea 197)
- Modificar: `backend/src/Zass.Infraestructura/Datos/Repositorios/RepositorioSuscripciones.cs`
- Modificar: `backend/pruebas/Zass.Aplicacion.Pruebas/Dobles/RepositorioSuscripciones.cs`
- Prueba: `backend/pruebas/Zass.Integracion.Pruebas/CuposFundadorIntegracionPruebas.cs`

**Interfaces:**
- Produce: `Task<CupoDePerfil?> CupoFundadorDePerfilAsync(Guid perfilPrestadorId, CancellationToken ct = default)`
  y el registro `CupoDePerfil(short Numero, DateTime? ReservadoHasta)`, en el namespace
  `Zass.Aplicacion.Puertos`. `ReservadoHasta is null` significa **confirmado**.

- [ ] **Paso 1: Escribir la prueba, que debe fallar**

Añade a `CuposFundadorIntegracionPruebas.cs`, siguiendo el estilo del archivo:

```csharp
    [Fact]
    public async Task El_cupo_de_un_perfil_se_puede_leer_reservado_y_confirmado()
    {
        var perfil = await UnPerfilPrestador();

        Assert.Null(await _repo.CupoFundadorDePerfilAsync(perfil));

        var numero = await _repo.IntentarReservarCupoFundadorAsync(
            perfil, Ahora, Ahora.AddMinutes(25));
        await _base.SaveChangesAsync();

        var reservado = await _repo.CupoFundadorDePerfilAsync(perfil);
        Assert.NotNull(reservado);
        Assert.Equal(numero, reservado!.Numero);
        Assert.NotNull(reservado.ReservadoHasta);

        await _repo.ConfirmarCupoFundadorAsync(perfil);
        await _base.SaveChangesAsync();

        var confirmado = await _repo.CupoFundadorDePerfilAsync(perfil);
        Assert.NotNull(confirmado);
        Assert.Null(confirmado!.ReservadoHasta);   // null = confirmado
    }
```

Ajusta `UnPerfilPrestador()`, `_repo`, `_base` y `Ahora` a los nombres que ya usa ese archivo —
**léelo antes**; no inventes ayudantes nuevos si ya existen.

- [ ] **Paso 2: Verla fallar**

```
cd C:\Users\pdani\zass-app-1
.\iniciar-postgres.bat
set ZASS_CADENA_CONEXION=Host=127.0.0.1;Port=5433;Database=zass_pruebas;Username=zass;Password=<la de .clave-pg.txt>
cd backend
dotnet test pruebas/Zass.Integracion.Pruebas --filter "FullyQualifiedName~El_cupo_de_un_perfil"
```

Esperado: no compila — `CupoFundadorDePerfilAsync` no existe.

- [ ] **Paso 3: Añadir el método al puerto**

En `Puertos.cs`, dentro de `IRepositorioSuscripciones`:

```csharp
    /// <summary>
    /// El cupo de ese perfil, si tiene. <c>ReservadoHasta is null</c> = CONFIRMADO; con fecha =
    /// reserva temporal. Es lo que hace idempotente el aviso de la pasarela sin tabla nueva.
    /// </summary>
    Task<CupoDePerfil?> CupoFundadorDePerfilAsync(
        Guid perfilPrestadorId, CancellationToken ct = default);
```

Y, fuera de la interfaz, en el mismo archivo:

```csharp
/// <summary>Un cupo fundador visto desde fuera. Sin fecha = confirmado.</summary>
public sealed record CupoDePerfil(short Numero, DateTime? ReservadoHasta);
```

- [ ] **Paso 4: Implementarlo en EF**

En `RepositorioSuscripciones.cs`, junto a los otros métodos de cupo:

```csharp
    public async Task<CupoDePerfil?> CupoFundadorDePerfilAsync(
        Guid perfilPrestadorId, CancellationToken ct = default) =>
        await _base.CuposFundador
            .Where(c => c.PerfilPrestadorId == perfilPrestadorId)
            .Select(c => new CupoDePerfil(c.Numero, c.ReservadoHasta))
            .SingleOrDefaultAsync(ct);
```

Usa el nombre real del `DbContext` del archivo (`_base`, `_db` o el que sea) y su `DbSet`.

- [ ] **Paso 5: Implementarlo en el doble en memoria**

En `backend/pruebas/Zass.Aplicacion.Pruebas/Dobles/RepositorioSuscripciones.cs`:

```csharp
    public Task<CupoDePerfil?> CupoFundadorDePerfilAsync(
        Guid perfilPrestadorId, CancellationToken ct = default)
    {
        var cupo = _base.CuposFundador
            .SingleOrDefault(c => c.PerfilPrestadorId == perfilPrestadorId);
        return Task.FromResult(cupo is null
            ? null
            : new CupoDePerfil(cupo.Numero, cupo.ReservadoHasta));
    }
```

**Ojo con la lección del proyecto:** este doble **no debe filtrar por su cuenta** nada que el
código de producción deba filtrar. Devuelve el cupo tal como está y nada más; si filtra, una
mutación del servicio no se va a notar.

- [ ] **Paso 6: Verla pasar**

Mismo comando del paso 2. Esperado: **1 passed**.

- [ ] **Paso 7: Arreglar dos nombres de prueba que mienten**

En `backend/pruebas/Zass.Aplicacion.Pruebas/Suscripciones/CuposFundadorPruebas.cs`:
`Al_empezar_los_20_cupos_estan_disponibles` afirma 50, y
`Reservar_entrega_el_cupo_mas_bajo_y_baja_los_disponibles_a_19` afirma 49. Quedaron del tiempo de
los 20 cupos. Renómbralos a `..._los_50_cupos_...` y `..._a_49`. Es N1 y evita que el próximo que
los lea crea que el tope son 20.

- [ ] **Paso 8: Commit**

```bash
git add backend/src/Zass.Aplicacion/Puertos/Puertos.cs \
        backend/src/Zass.Infraestructura/Datos/Repositorios/RepositorioSuscripciones.cs \
        backend/pruebas/Zass.Aplicacion.Pruebas backend/pruebas/Zass.Integracion.Pruebas
git commit -m "Cupos: leer el cupo de un perfil (confirmado = sin fecha), y dos nombres de prueba que mentian"
```

---

### Task 3: Tomar el cupo (reservar e iniciar la inscripción)

**Archivos:**
- Modificar: `backend/src/Zass.Aplicacion/Suscripciones/IServicioSuscripciones.cs`
- Modificar: `backend/src/Zass.Aplicacion/Suscripciones/ServicioSuscripciones.cs`
- Prueba: `backend/pruebas/Zass.Aplicacion.Pruebas/Suscripciones/TomarCupoFundadorPruebas.cs`

**Interfaces:**
- Consume: `IPasarelaDePagos.IniciarInscripcionAsync`, `ReservarCupoFundadorAsync` (ya existe),
  `IRepositorioSuscripciones.PlanPorCodigoAsync`, `UltimaDePrestadorAsync`.
- Produce: `Task<Resultado<CupoTomado>> TomarCupoFundadorAsync(Guid perfilPrestadorId, CancellationToken ct = default)`
  y `public sealed record CupoTomado(short NumeroDeCupo, string UrlParaRedirigir)`.

- [ ] **Paso 1: Escribir las pruebas, que deben fallar**

```csharp
using Zass.Aplicacion.Pagos;
using Zass.Aplicacion.Pruebas.Dobles;
using Zass.Aplicacion.Suscripciones;
using Zass.Dominio.Comun;
using Zass.Dominio.Identidad;

namespace Zass.Aplicacion.Pruebas.Suscripciones;

/// <summary>
/// Tomar el cupo son DOS cosas que tienen que pasar juntas: reservarlo en la base y pedirle a
/// la pasarela a donde mandar a la persona. Si lo segundo falla, lo primero no puede quedar
/// hecho: un cupo reservado sin camino a inscribirse es un cupo perdido 25 minutos.
/// </summary>
public class TomarCupoFundadorPruebas
{
    private static readonly DateTime Ahora = new(2026, 11, 13, 12, 0, 0, DateTimeKind.Utc);

    private readonly BaseEnMemoria _base = new();
    private readonly RelojFijo _reloj = new(Ahora);
    private readonly PasarelaFalsa _pasarela = new();
    private readonly ServicioSuscripciones _servicio;

    public TomarCupoFundadorPruebas()
    {
        _servicio = new ServicioSuscripciones(
            new RepositorioSuscripcionesEnMemoria(_base), _base, _reloj,
            new AuditoriaEnMemoria(), new ColaEnMemoria(), _pasarela);
    }

    private sealed class PasarelaFalsa : IPasarelaDePagos
    {
        public bool Falla { get; set; }
        public int Llamadas { get; private set; }

        public Task<Resultado<InicioDeInscripcion>> IniciarInscripcionAsync(
            SolicitudDeInscripcion s, CancellationToken ct = default)
        {
            Llamadas++;
            return Task.FromResult(Falla
                ? Resultado.Falla<InicioDeInscripcion>(Fallas.ReglaDeNegocio, "sin pasarela")
                : Resultado.Ok(new InicioDeInscripcion("sim_1", "https://local.test/pago/inscripcion?ref=sim_1")));
        }

        public Task<Resultado<ResultadoDeInscripcion>> LeerAvisoAsync(
            AvisoEntrante a, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<Resultado<ResultadoDeCobro>> CobrarAsync(
            SolicitudDeCobro s, CancellationToken ct = default) =>
            throw new NotSupportedException();
    }

    private PerfilPrestador UnPrestador()
    {
        var perfil = new PerfilPrestador { Id = Guid.NewGuid(), UsuarioId = Guid.NewGuid() };
        _base.PerfilesPrestador.Add(perfil);
        return perfil;
    }

    [Fact]
    public async Task Tomar_el_cupo_lo_reserva_y_devuelve_a_donde_ir()
    {
        var perfil = UnPrestador();

        var r = await _servicio.TomarCupoFundadorAsync(perfil.Id);

        Assert.True(r.Exito);
        Assert.Equal(1, r.Valor!.NumeroDeCupo);
        Assert.Contains("/pago/inscripcion", r.Valor!.UrlParaRedirigir);
        Assert.Equal(49, await _servicio.CuposFundadorDisponiblesAsync());
    }

    [Fact]
    public async Task La_reserva_dura_25_minutos()
    {
        var perfil = UnPrestador();
        await _servicio.TomarCupoFundadorAsync(perfil.Id);

        var cupo = _base.CuposFundador.Single(c => c.PerfilPrestadorId == perfil.Id);

        // Candado con literal: 25 escrito a mano. Si alguien cambia la constante, esto cae.
        Assert.Equal(Ahora.AddMinutes(25), cupo.ReservadoHasta);
    }

    [Fact]
    public async Task Si_la_pasarela_falla_el_cupo_NO_queda_reservado()
    {
        var perfil = UnPrestador();
        _pasarela.Falla = true;

        var r = await _servicio.TomarCupoFundadorAsync(perfil.Id);

        Assert.False(r.Exito);
        Assert.Equal(50, await _servicio.CuposFundadorDisponiblesAsync());
        Assert.Null(_base.CuposFundador.SingleOrDefault(c => c.PerfilPrestadorId == perfil.Id));
    }

    [Fact]
    public async Task Sin_cupos_responde_conflicto_y_no_llama_a_la_pasarela()
    {
        foreach (var cupo in _base.CuposFundador)
        {
            cupo.PerfilPrestadorId = Guid.NewGuid();
            cupo.ReservadoHasta = null;
        }
        var perfil = UnPrestador();

        var r = await _servicio.TomarCupoFundadorAsync(perfil.Id);

        Assert.False(r.Exito);
        Assert.Equal(Fallas.Conflicto, r.Codigo);
        Assert.Equal(0, _pasarela.Llamadas);
    }

    [Fact]
    public async Task Un_perfil_que_no_existe_no_toma_cupo()
    {
        var r = await _servicio.TomarCupoFundadorAsync(Guid.NewGuid());

        Assert.False(r.Exito);
        Assert.Equal(Fallas.NoEncontrado, r.Codigo);
    }
}
```

- [ ] **Paso 2: Verlas fallar**

```
cd backend
dotnet test pruebas/Zass.Aplicacion.Pruebas --filter "FullyQualifiedName~TomarCupoFundador"
```

Esperado: no compila — `TomarCupoFundadorAsync` no existe y el constructor no toma pasarela.

- [ ] **Paso 3: Implementarlo**

En `IServicioSuscripciones.cs`:

```csharp
    /// <summary>
    /// CU nuevo (S7): reserva un cupo fundador y devuelve a donde mandar a la persona para
    /// inscribir su medio de pago. El cupo queda DEFINITIVO solo cuando llega el aviso de la
    /// pasarela (RN-S7-01), no aqui.
    /// </summary>
    Task<Resultado<CupoTomado>> TomarCupoFundadorAsync(
        Guid perfilPrestadorId, CancellationToken ct = default);
```

y el registro, junto a los otros DTO del archivo:

```csharp
/// <summary>Lo que necesita la pantalla despues de tomar el cupo. Nada mas.</summary>
public sealed record CupoTomado(short NumeroDeCupo, string UrlParaRedirigir);
```

En `ServicioSuscripciones.cs`: añade `IPasarelaDePagos` como último parámetro del constructor,
guárdalo en `_pasarela`, y añade:

```csharp
    public async Task<Resultado<CupoTomado>> TomarCupoFundadorAsync(
        Guid perfilPrestadorId, CancellationToken ct = default)
    {
        var plan = await _repo.PlanPorCodigoAsync("fundador", ct);
        if (plan is null || !plan.Activo)
            return Resultado.Falla<CupoTomado>(
                Fallas.ReglaDeNegocio, "El plan fundador no esta disponible.");

        var reserva = await ReservarCupoFundadorAsync(perfilPrestadorId, ct: ct);
        if (!reserva.Exito)
            return Resultado.Falla<CupoTomado>(reserva.Codigo!, reserva.Mensaje!);

        var suscripcion = await _repo.UltimaDePrestadorAsync(perfilPrestadorId, ct);

        var inicio = await _pasarela.IniciarInscripcionAsync(new SolicitudDeInscripcion(
            suscripcion?.Id ?? Guid.Empty, perfilPrestadorId, plan.Id, "/planes/listo"), ct);

        if (!inicio.Exito)
        {
            // Un cupo reservado sin camino a inscribirse es un cupo perdido 25 minutos.
            await LiberarCupoFundadorAsync(perfilPrestadorId, ct);
            return Resultado.Falla<CupoTomado>(
                Fallas.ReglaDeNegocio, "No pudimos abrir el pago. Intenta de nuevo.");
        }

        return Resultado.Ok(new CupoTomado(reserva.Valor, inicio.Valor!.UrlParaRedirigir));
    }
```

`ReservarCupoFundadorAsync` ya hace `GuardarAsync`, así que la liberación también guarda. Si
`Resultado.Falla<T>(codigo, mensaje)` no acepta un `string?`, usa el operador `!` como arriba.

- [ ] **Paso 4: Verlas pasar**

Mismo comando del paso 2. Esperado: **5 passed**. Corre además la suite completa de aplicación,
porque el constructor cambió: `dotnet test pruebas/Zass.Aplicacion.Pruebas`. Arregla los otros
constructores que ahora necesitan la pasarela (`CuposFundadorPruebas` entre ellos).

- [ ] **Paso 5: Las mutaciones**

Tres, una por prueba que vale:

1. Comenta la liberación del cupo cuando la pasarela falla → debe caer
   `Si_la_pasarela_falla_el_cupo_NO_queda_reservado`.
2. Cambia `DuracionReservaPorDefecto` a 30 minutos → debe caer `La_reserva_dura_25_minutos`
   (**ese es el candado con literal funcionando**).
3. Mueve la llamada a la pasarela ANTES de la reserva → debe caer
   `Sin_cupos_responde_conflicto_y_no_llama_a_la_pasarela`.

Deshaz las tres. Pega los resultados en el PR.

- [ ] **Paso 6: Commit**

```bash
git add backend/src/Zass.Aplicacion backend/pruebas/Zass.Aplicacion.Pruebas
git commit -m "S7: tomar el cupo fundador reserva e inicia la inscripcion, o no hace nada"
```

---

### Task 4: Confirmar la inscripción (el aviso de la pasarela)

Es el corazón del plan: idempotencia, monto y **RN-S7-07**.

**Archivos:**
- Modificar: `backend/src/Zass.Aplicacion/Suscripciones/IServicioSuscripciones.cs`
- Modificar: `backend/src/Zass.Aplicacion/Suscripciones/ServicioSuscripciones.cs`
- Prueba: `backend/pruebas/Zass.Aplicacion.Pruebas/Suscripciones/ConfirmarInscripcionPruebas.cs`

**Interfaces:**
- Produce: `Task<Resultado> ConfirmarInscripcionFundadorAsync(ResultadoDeInscripcion aviso, CancellationToken ct = default)`
  y la constante `public static readonly int DiasDeMesGratis = 30;`

- [ ] **Paso 1: Escribir las pruebas, que deben fallar**

```csharp
using Zass.Aplicacion.Pagos;
using Zass.Aplicacion.Pruebas.Dobles;
using Zass.Aplicacion.Suscripciones;
using Zass.Dominio.Comun;
using Zass.Dominio.Identidad;
using Zass.Dominio.Suscripciones;

namespace Zass.Aplicacion.Pruebas.Suscripciones;

/// <summary>
/// RN-S7-01 a RN-S7-07. El aviso de la pasarela es el unico hecho que confirma un cupo, y llega
/// por detras: puede llegar dos veces, tarde, o inventado. Todo eso se prueba aqui.
/// </summary>
public class ConfirmarInscripcionPruebas
{
    private static readonly DateTime Ahora = new(2026, 11, 13, 12, 0, 0, DateTimeKind.Utc);

    private readonly BaseEnMemoria _base = new();
    private readonly RelojFijo _reloj = new(Ahora);
    private readonly ServicioSuscripciones _servicio;

    public ConfirmarInscripcionPruebas()
    {
        _servicio = new ServicioSuscripciones(
            new RepositorioSuscripcionesEnMemoria(_base), _base, _reloj,
            new AuditoriaEnMemoria(), new ColaEnMemoria(), new PasarelaQueNoSeUsa());
    }

    private sealed class PasarelaQueNoSeUsa : IPasarelaDePagos
    {
        public Task<Resultado<InicioDeInscripcion>> IniciarInscripcionAsync(
            SolicitudDeInscripcion s, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Resultado<ResultadoDeInscripcion>> LeerAvisoAsync(
            AvisoEntrante a, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Resultado<ResultadoDeCobro>> CobrarAsync(
            SolicitudDeCobro s, CancellationToken ct = default) => throw new NotSupportedException();
    }

    private PerfilPrestador UnPrestadorConCupoReservado()
    {
        var perfil = new PerfilPrestador { Id = Guid.NewGuid(), UsuarioId = Guid.NewGuid() };
        _base.PerfilesPrestador.Add(perfil);
        var cupo = _base.CuposFundador.First(c => c.PerfilPrestadorId is null);
        cupo.PerfilPrestadorId = perfil.Id;
        cupo.ReservadoHasta = Ahora.AddMinutes(25);
        return perfil;
    }

    private static ResultadoDeInscripcion Aviso(Guid perfil, int monto = 0, bool aceptada = true) =>
        new("sim_1", perfil, monto, aceptada, null);

    private Suscripcion DeLaBase(Guid perfilId) =>
        _base.Suscripciones.Single(s => s.PerfilPrestadorId == perfilId);

    [Fact]
    public async Task El_aviso_aceptado_confirma_el_cupo_y_deja_el_plan_fundador_con_mes_gratis()
    {
        var perfil = UnPrestadorConCupoReservado();

        var r = await _servicio.ConfirmarInscripcionFundadorAsync(Aviso(perfil.Id));

        Assert.True(r.Exito);
        Assert.Null(_base.CuposFundador.Single(c => c.PerfilPrestadorId == perfil.Id).ReservadoHasta);

        var s = DeLaBase(perfil.Id);
        Assert.Equal("fundador", _base.Planes.Single(p => p.Id == s.PlanId).Codigo);
        Assert.Equal(EstadoSuscripcion.Prueba, s.Estado);
        // Candado con literal: 30 dias escritos a mano.
        Assert.Equal(Ahora.AddDays(30), s.VigenteHasta);
        Assert.True(s.EstaVigente(Ahora));   // aparece en la busqueda desde ya
    }

    [Fact]
    public async Task RN_S7_07_quien_ya_tenia_prueba_no_termina_con_dos_suscripciones()
    {
        var perfil = UnPrestadorConCupoReservado();
        _base.Suscripciones.Add(new Suscripcion
        {
            PerfilPrestadorId = perfil.Id, PlanId = 1,
            Estado = EstadoSuscripcion.Prueba, VigenteHasta = Ahora.AddDays(12),
        });

        await _servicio.ConfirmarInscripcionFundadorAsync(Aviso(perfil.Id));

        Assert.Single(_base.Suscripciones.Where(s => s.PerfilPrestadorId == perfil.Id));
    }

    [Fact]
    public async Task RN_S7_07_la_vigencia_queda_en_la_fecha_mas_lejana_de_las_dos()
    {
        var perfil = UnPrestadorConCupoReservado();
        _base.Suscripciones.Add(new Suscripcion
        {
            PerfilPrestadorId = perfil.Id, PlanId = 1,
            Estado = EstadoSuscripcion.Prueba, VigenteHasta = Ahora.AddDays(45),
        });

        await _servicio.ConfirmarInscripcionFundadorAsync(Aviso(perfil.Id));

        // Tenia 45 dias: no se le quitan 15 para dejarlo en 30.
        Assert.Equal(Ahora.AddDays(45), DeLaBase(perfil.Id).VigenteHasta);
    }

    [Fact]
    public async Task RN_S7_07_a_quien_le_quedaban_pocos_dias_se_le_dan_los_30()
    {
        var perfil = UnPrestadorConCupoReservado();
        _base.Suscripciones.Add(new Suscripcion
        {
            PerfilPrestadorId = perfil.Id, PlanId = 1,
            Estado = EstadoSuscripcion.Prueba, VigenteHasta = Ahora.AddDays(3),
        });

        await _servicio.ConfirmarInscripcionFundadorAsync(Aviso(perfil.Id));

        Assert.Equal(Ahora.AddDays(30), DeLaBase(perfil.Id).VigenteHasta);
    }

    [Fact]
    public async Task RN_S7_04_el_mismo_aviso_dos_veces_no_cambia_nada_la_segunda()
    {
        var perfil = UnPrestadorConCupoReservado();
        await _servicio.ConfirmarInscripcionFundadorAsync(Aviso(perfil.Id));
        var vigenciaTrasElPrimero = DeLaBase(perfil.Id).VigenteHasta;

        var segundo = await _servicio.ConfirmarInscripcionFundadorAsync(Aviso(perfil.Id));

        Assert.True(segundo.Exito);   // 200, no error: la pasarela reintentaria para siempre
        Assert.Single(_base.Suscripciones.Where(s => s.PerfilPrestadorId == perfil.Id));
        Assert.Equal(vigenciaTrasElPrimero, DeLaBase(perfil.Id).VigenteHasta);
    }

    [Fact]
    public async Task RN_S7_05_un_monto_distinto_de_cero_en_la_inscripcion_se_rechaza()
    {
        var perfil = UnPrestadorConCupoReservado();

        var r = await _servicio.ConfirmarInscripcionFundadorAsync(Aviso(perfil.Id, monto: 5990));

        Assert.False(r.Exito);
        Assert.NotNull(_base.CuposFundador.Single(c => c.PerfilPrestadorId == perfil.Id).ReservadoHasta);
    }

    [Fact]
    public async Task Un_aviso_rechazado_libera_el_cupo()
    {
        var perfil = UnPrestadorConCupoReservado();

        var r = await _servicio.ConfirmarInscripcionFundadorAsync(Aviso(perfil.Id, aceptada: false));

        Assert.True(r.Exito);   // procesado correctamente: lo que paso es que no se inscribio
        Assert.Null(_base.CuposFundador.SingleOrDefault(c => c.PerfilPrestadorId == perfil.Id));
    }

    [Fact]
    public async Task Un_aviso_de_un_perfil_sin_cupo_no_hace_nada()
    {
        var r = await _servicio.ConfirmarInscripcionFundadorAsync(Aviso(Guid.NewGuid()));

        Assert.False(r.Exito);
        Assert.Equal(Fallas.NoEncontrado, r.Codigo);
        Assert.Empty(_base.Suscripciones);
    }
}
```

Si `BaseEnMemoria` no expone `Planes` con el plan `fundador` sembrado, siémbralo en el ayudante
del propio archivo de pruebas — **no** cambies el doble para que mienta.

- [ ] **Paso 2: Verlas fallar**

```
cd backend
dotnet test pruebas/Zass.Aplicacion.Pruebas --filter "FullyQualifiedName~ConfirmarInscripcion"
```

Esperado: no compila — `ConfirmarInscripcionFundadorAsync` no existe.

- [ ] **Paso 3: Implementarlo**

En `IServicioSuscripciones.cs`:

```csharp
    /// <summary>
    /// Procesa el aviso de la pasarela. Es idempotente (RN-S7-04): el cupo YA confirmado es el
    /// registro de que esto se hizo, asi que el segundo aviso no cambia nada y devuelve exito.
    /// </summary>
    Task<Resultado> ConfirmarInscripcionFundadorAsync(
        ResultadoDeInscripcion aviso, CancellationToken ct = default);
```

En `ServicioSuscripciones.cs`:

```csharp
    /// <summary>RN-S7-03. El mes gratis del fundador. No vive en dias_prueba del plan, que es 0.</summary>
    public static readonly int DiasDeMesGratis = 30;

    public async Task<Resultado> ConfirmarInscripcionFundadorAsync(
        ResultadoDeInscripcion aviso, CancellationToken ct = default)
    {
        var cupo = await _repo.CupoFundadorDePerfilAsync(aviso.PerfilPrestadorId, ct);
        if (cupo is null)
            return Resultado.Falla(Fallas.NoEncontrado, "No hay un cupo que confirmar.");

        // RN-S7-04: sin fecha = ya confirmado. Segundo aviso, nada que hacer, exito.
        if (cupo.ReservadoHasta is null) return Resultado.Ok();

        if (!aviso.Aceptada)
        {
            await LiberarCupoFundadorAsync(aviso.PerfilPrestadorId, ct);
            return Resultado.Ok();
        }

        // RN-S7-05: la inscripcion no cobra. Un monto distinto de 0 no es esta operacion.
        if (aviso.Monto != 0)
            return Resultado.Falla(
                Fallas.ReglaDeNegocio, "El monto del aviso no corresponde a una inscripcion.");

        var plan = await _repo.PlanPorCodigoAsync("fundador", ct);
        if (plan is null || !plan.Activo)
            return Resultado.Falla(Fallas.ReglaDeNegocio, "El plan fundador no esta disponible.");

        var confirmado = await _repo.ConfirmarCupoFundadorAsync(aviso.PerfilPrestadorId, ct);
        if (!confirmado)
            return Resultado.Falla(Fallas.Conflicto, "El cupo ya no estaba reservado.");

        var ahora = _reloj.Ahora;
        var mesGratis = ahora.AddDays(DiasDeMesGratis);
        var existente = await _repo.UltimaDePrestadorAsync(aviso.PerfilPrestadorId, ct);

        if (existente is null)
        {
            _repo.Agregar(new Suscripcion
            {
                PerfilPrestadorId = aviso.PerfilPrestadorId,
                PlanId = plan.Id,
                Estado = EstadoSuscripcion.Prueba,
                IniciaEn = ahora,
                VigenteHasta = mesGratis,
                ReferenciaPasarela = aviso.ReferenciaExterna,
            });
        }
        else
        {
            // RN-S7-07: no se crea una segunda ni se regalan 30 dias encima de los que quedaban.
            existente.PlanId = plan.Id;
            existente.Estado = EstadoSuscripcion.Prueba;
            existente.VigenteHasta = existente.VigenteHasta > mesGratis
                ? existente.VigenteHasta
                : mesGratis;
            existente.ReferenciaPasarela = aviso.ReferenciaExterna;
            existente.CanceladaEn = null;
        }

        _auditor.Registrar(Auditar.Entrada(
            "inscripcion_fundador_confirmada", "perfiles_prestador",
            aviso.PerfilPrestadorId, ahora,
            contexto: new { cupo = cupo.Numero }));

        await _trabajo.GuardarAsync(ct);
        return Resultado.Ok();
    }
```

**No metas la referencia de la pasarela en el contexto de auditoría**: es dato de la pasarela, y
la auditoría la leen personas.

- [ ] **Paso 4: Verlas pasar**

Mismo comando del paso 2. Esperado: **8 passed**.

- [ ] **Paso 5: Las mutaciones (cuatro, y son las que valen de verdad)**

1. Borra la comprobación `if (cupo.ReservadoHasta is null) return Resultado.Ok();` → debe caer
   `RN_S7_04_el_mismo_aviso_dos_veces_no_cambia_nada_la_segunda`.
2. Cambia la vigencia a `existente.VigenteHasta = mesGratis;` a secas → debe caer
   `RN_S7_07_la_vigencia_queda_en_la_fecha_mas_lejana_de_las_dos`.
3. Borra la comprobación del monto → debe caer
   `RN_S7_05_un_monto_distinto_de_cero_en_la_inscripcion_se_rechaza`.
4. Cambia `DiasDeMesGratis` a 31 → debe caer
   `El_aviso_aceptado_confirma_el_cupo_y_deja_el_plan_fundador_con_mes_gratis`.

Deshaz las cuatro y pega los resultados en el PR.

- [ ] **Paso 6: Commit**

```bash
git add backend/src/Zass.Aplicacion backend/pruebas/Zass.Aplicacion.Pruebas
git commit -m "S7: el aviso de la pasarela confirma el cupo, es idempotente y respeta RN-S7-07"
```

---

### Task 5: Los tres endpoints (API-66, API-67, API-68)

**Archivos:**
- Crear: `backend/src/Zass.Api/Endpoints/EndpointsSuscripciones.cs`
- Modificar: `backend/src/Zass.Api/Program.cs` (una línea junto a los otros `app.Mapear…`, ~90-105)
- Prueba: `backend/pruebas/Zass.Integracion.Pruebas/SuscripcionesEndpointsPruebas.cs`

**Interfaces:**
- Consume: `IServicioSuscripciones.PlanesActivosAsync`, `CuposFundadorDisponiblesAsync`,
  `TomarCupoFundadorAsync`, `ConfirmarInscripcionFundadorAsync`; `IPasarelaDePagos.LeerAvisoAsync`;
  `ServicioPerfiles.MiPerfilPrestadorAsync`; `Respuestas`, `Tasa`, `FiltroCsrf`.
- Produce: `MapearSuscripciones()` como extensión de `IEndpointRouteBuilder`.

- [ ] **Paso 1: Escribir los endpoints**

```csharp
using System.Security.Claims;
using Zass.Api.Configuracion;
using Zass.Aplicacion.Identidad;
using Zass.Aplicacion.Pagos;
using Zass.Aplicacion.Suscripciones;

namespace Zass.Api.Endpoints;

/// <summary>
/// API-66, API-67 y API-68. S7 Suscripciones y Pagos.
///
/// API-68 es el endpoint mas delicado del proyecto: lo llama un SERVIDOR, no un navegador, asi
/// que no lleva sesion ni CSRF. Su autenticidad la da la FIRMA, y por eso la firma se verifica
/// antes de mirar el cuerpo. Un duplicado responde 200 a proposito: una pasarela que recibe un
/// error reintenta para siempre, y a las tres de la manana.
/// </summary>
public static class EndpointsSuscripciones
{
    public static IEndpointRouteBuilder MapearSuscripciones(this IEndpointRouteBuilder app)
    {
        MapearPublico(app.MapGroup("/api"));
        MapearMio(app.MapGroup("/api/mi"));
        MapearAvisos(app.MapGroup("/api/pagos"));
        return app;
    }

    private static void MapearPublico(RouteGroupBuilder grupo)
    {
        // API-66 - Catalogo publico de planes y cuantos cupos fundador quedan.
        // Sin un solo dato de ninguna persona: es publico y se cachea.
        grupo.MapGet("/planes", async (IServicioSuscripciones suscripciones) =>
                Results.Ok(new
                {
                    planes = await suscripciones.PlanesActivosAsync(),
                    cuposFundadorDisponibles = await suscripciones.CuposFundadorDisponiblesAsync(),
                }))
            .RequireRateLimiting(Tasa.Publico)
            .WithName("API-66");
    }

    private static void MapearMio(RouteGroupBuilder grupo)
    {
        grupo.RequireAuthorization();
        grupo.AddEndpointFilter<FiltroCsrf>();

        // API-67 - Toma un cupo fundador. El perfil sale de la SESION, nunca del cuerpo:
        // con un id en el cuerpo, cualquiera tomaria el cupo de otro.
        grupo.MapPost("/suscripcion/fundador", async (
                IServicioSuscripciones suscripciones, ServicioPerfiles perfiles,
                ClaimsPrincipal quien) =>
            {
                var usuarioId = quien.UsuarioId();
                if (usuarioId is null) return Results.Unauthorized();

                var mio = await perfiles.MiPerfilPrestadorAsync(usuarioId.Value);
                if (!mio.Exito) return Respuestas.Problema(mio);

                return Respuestas.DeResultado(
                    await suscripciones.TomarCupoFundadorAsync(mio.Valor!.Id));
            })
            .RequireRateLimiting(Tasa.Sensible)
            .WithName("API-67");
    }

    private static void MapearAvisos(RouteGroupBuilder grupo)
    {
        // API-68 - El aviso de la pasarela. Sin sesion y sin CSRF: lo llama un servidor.
        grupo.MapPost("/aviso", async (
                HttpRequest peticion, IPasarelaDePagos pasarela,
                IServicioSuscripciones suscripciones) =>
            {
                using var lector = new StreamReader(peticion.Body);
                var cuerpo = await lector.ReadToEndAsync();
                var firma = peticion.Headers["X-Firma-Pasarela"].ToString();

                var leido = await pasarela.LeerAvisoAsync(new AvisoEntrante(cuerpo, firma));
                if (!leido.Exito) return Respuestas.Problema(leido);

                var r = await suscripciones.ConfirmarInscripcionFundadorAsync(leido.Valor!);

                // Lo que no se pudo procesar NO se repite al infinito: 200 con nada dentro.
                // Lo que fallo queda en la auditoria y en el registro, no en la respuesta.
                return r.Exito ? Results.Ok() : Respuestas.Problema(r);
            })
            .RequireRateLimiting(Tasa.General)
            .WithName("API-68");
    }
}
```

- [ ] **Paso 2: Mapearlo en `Program.cs`**

Junto a los otros, después de `app.MapearResenas();`:

```csharp
app.MapearSuscripciones();                             // API-66, API-67, API-68 (S7)
```

- [ ] **Paso 3: Escribir la prueba de integración, y verla fallar**

```csharp
    [Fact]
    public async Task API_66_es_publico_y_dice_cuantos_cupos_quedan()
    {
        var r = await _cliente.GetAsync("/api/planes");

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var cuerpo = await r.Content.ReadFromJsonAsync<JsonDocument>();
        Assert.Equal(50, cuerpo!.RootElement.GetProperty("cuposFundadorDisponibles").GetInt32());
    }

    [Fact]
    public async Task API_67_sin_sesion_no_toma_nada()
    {
        var r = await _cliente.PostAsync("/api/mi/suscripcion/fundador", null);

        Assert.True(r.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task API_68_con_firma_mala_responde_401_y_no_toca_ningun_cupo()
    {
        var contenido = new StringContent("{\"referencia\":\"x\"}", Encoding.UTF8, "application/json");
        contenido.Headers.Add("X-Firma-Pasarela", "no-es-la-firma");

        var r = await _cliente.PostAsync("/api/pagos/aviso", contenido);

        Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode);
    }
```

Úsalo dentro de la clase de pruebas de endpoints que ya exista en
`Zass.Integracion.Pruebas` (hay una fábrica de aplicación y un cliente ya montados: **léela y
reúsala**, no montes otra). Si la cabecera `X-Firma-Pasarela` no se puede añadir a
`StringContent`, ponla con `_cliente.DefaultRequestHeaders`.

```
cd backend
dotnet test pruebas/Zass.Integracion.Pruebas --filter "FullyQualifiedName~API_6"
```

Esperado: `404` en los tres antes de mapear; verde después.

- [ ] **Paso 4: Verlas pasar y correr la suite entera**

```
dotnet test
```

Esperado: todo verde. Si el arranque falla por `Zass:Pagos`, añade a
`appsettings.Development.json` (que **no** lleva secretos de producción):

```json
"Zass": {
  "Pagos": {
    "Proveedor": "Simulada",
    "ClaveDeFirmaSimulada": "desarrollo-local-no-es-un-secreto",
    "UrlPublica": "http://localhost:5173"
  }
}
```

- [ ] **Paso 5: La mutación**

Quita `.AddEndpointFilter<FiltroCsrf>()` del grupo `/api/mi` y comprueba que cae la prueba de CSRF
que ya existe para ese grupo (hay una por endpoint que escribe). Si **no** cae ninguna, escribe
una para API-67: un POST con sesión y sin token CSRF tiene que responder `403`.

- [ ] **Paso 6: Commit**

```bash
git add backend/src/Zass.Api backend/pruebas/Zass.Integracion.Pruebas
git commit -m "API-66, API-67 y API-68: el camino HTTP del cupo fundador"
```

---

### Task 6: La página `/planes` y sus cuatro estados

**Archivos:**
- Crear: `frontend/src/funciones/planes/servicio.ts`
- Crear: `frontend/src/paginas/Planes.tsx` y `frontend/src/paginas/Planes.prueba.tsx`
- Crear: `frontend/src/paginas/PagoSimulado.tsx`
- Modificar: `frontend/src/App.tsx` (rutas `planes`, `planes/listo`, `pago/inscripcion`)

**Interfaces:**
- Consume: `obtener`, `enviar` de `../../api/cliente`; `Contexto` de `../sesion/contexto`.
- Produce: `type Plan`, `type CatalogoDePlanes`, `planes()`, `tomarCupoFundador()`;
  componentes `Planes`, `PagoSimulado`.

- [ ] **Paso 1: El servicio**

```ts
import { enviar, obtener } from '../../api/cliente'

/** API-66. Lo que el servidor manda del catalogo. Sin datos de personas: es publico. */
export interface Plan {
  id: number
  codigo: string
  nombre: string
  descripcion: string | null
  precioMensual: number
  diasPrueba: number
  maxServicios: number
  maxTarjetas: number
  permiteCredenciales: boolean
}

export interface CatalogoDePlanes {
  planes: Plan[]
  cuposFundadorDisponibles: number
}

/** API-67. Lo que devuelve tomar el cupo: el numero y a donde ir a inscribir el medio de pago. */
export interface CupoTomado {
  numeroDeCupo: number
  urlParaRedirigir: string
}

export function planes(): Promise<CatalogoDePlanes> {
  return obtener<CatalogoDePlanes>('/api/planes')
}

export function tomarCupoFundador(): Promise<CupoTomado> {
  return enviar<CupoTomado>('/api/mi/suscripcion/fundador')
}
```

- [ ] **Paso 2: Las pruebas de la pantalla, que deben fallar**

`frontend/src/paginas/Planes.prueba.tsx`:

```tsx
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router-dom'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { olvidarCsrf } from '../api/cliente'
import { Contexto, type EstadoDeSesion } from '../sesion/contexto'
import { Planes } from './Planes'

function json(cuerpo: unknown, estado = 200) {
  return new Response(JSON.stringify(cuerpo), {
    status: estado, headers: { 'content-type': 'application/json' },
  })
}

const PLANES = [
  { id: 2, codigo: 'mensual', nombre: 'ZASS! Mensual', descripcion: 'El plan normal.',
    precioMensual: 7990, diasPrueba: 0, maxServicios: 10, maxTarjetas: 1, permiteCredenciales: true },
  { id: 3, codigo: 'fundador', nombre: 'ZASS! Fundador', descripcion: 'Precio de lanzamiento.',
    precioMensual: 5990, diasPrueba: 0, maxServicios: 10, maxTarjetas: 1, permiteCredenciales: true },
]

let cupos: number
let sesion: EstadoDeSesion

beforeEach(() => {
  cupos = 37
  olvidarCsrf()
  vi.stubGlobal('fetch', vi.fn(async (entrada: RequestInfo) => {
    const ruta = String(entrada)
    if (ruta.includes('/api/planes')) return json({ planes: PLANES, cuposFundadorDisponibles: cupos })
    if (ruta.includes('/api/mi/suscripcion/fundador')) {
      return json({ numeroDeCupo: 14, urlParaRedirigir: '/pago/inscripcion?ref=sim_1' })
    }
    if (ruta.includes('/api/csrf')) return json({ token: 't' })
    throw new Error('ruta no esperada: ' + ruta)
  }))
})

function pintar() {
  return render(
    <MemoryRouter>
      <Contexto.Provider value={sesion}>
        <Planes />
      </Contexto.Provider>
    </MemoryRouter>,
  )
}

describe('Planes', () => {
  it('muestra los dos planes y cuantos cupos quedan', async () => {
    sesion = { estado: 'invitado' } as EstadoDeSesion
    pintar()

    expect(await screen.findByText(/ZASS! Fundador/)).toBeTruthy()
    expect(screen.getByText(/ZASS! Mensual/)).toBeTruthy()
    expect(screen.getByText(/37/)).toBeTruthy()
  })

  it('sin cupos, el plan fundador aparece cerrado y sin boton', async () => {
    cupos = 0
    sesion = { estado: 'invitado' } as EstadoDeSesion
    pintar()

    expect(await screen.findByText(/cerrado/i)).toBeTruthy()
    expect(screen.queryByRole('button', { name: /quiero mi cupo/i })).toBeNull()
  })

  it('sin sesion, invita a crear cuenta y vuelve aqui', async () => {
    sesion = { estado: 'invitado' } as EstadoDeSesion
    pintar()

    const enlace = await screen.findByRole('link', { name: /crear (mi )?cuenta/i })
    expect(enlace.getAttribute('href')).toContain('/crear-cuenta')
    expect(enlace.getAttribute('href')).toContain('planes')
  })

  it('con sesion pero sin ficha publicada, manda a publicarla primero', async () => {
    sesion = { estado: 'dentro', usuario: { esPrestador: false } } as unknown as EstadoDeSesion
    pintar()

    expect(await screen.findByRole('link', { name: /publicar|ofrecer/i })).toBeTruthy()
  })

  it('con ficha, tomar el cupo lleva a inscribir el medio de pago', async () => {
    sesion = { estado: 'dentro', usuario: { esPrestador: true } } as unknown as EstadoDeSesion
    pintar()

    await userEvent.click(await screen.findByRole('button', { name: /quiero mi cupo/i }))

    await waitFor(() => {
      expect(screen.getByText(/14/)).toBeTruthy()
    })
  })
})
```

Ajusta la forma de `EstadoDeSesion` y el nombre del campo que dice si es prestador a lo que
`src/sesion/contexto.tsx` define de verdad — **léelo primero**. Si la pantalla redirige con
`useNavigate` en vez de pintar, comprueba la navegación con una ruta de prueba, como hace
`MisResenas.prueba.tsx`.

- [ ] **Paso 3: Verlas fallar**

```
cd frontend
npx vitest run src/paginas/Planes.prueba.tsx
```

Esperado: falla al importar `./Planes`, que no existe.

- [ ] **Paso 4: Escribir la pantalla**

`frontend/src/paginas/Planes.tsx`. Requisitos, todos con prueba arriba:

1. Pide API-66 al montar; mientras carga, dice que está cargando; si falla, lo dice y **ofrece
   reintentar** (como `/mi-ficha` tras el defecto D3).
2. Pinta los dos planes con su precio formateado en pesos chilenos
   (`new Intl.NumberFormat('es-CL')`), qué incluye, y el contador «Quedan N de 50 cupos».
3. Con `cuposFundadorDisponibles === 0`, el plan fundador se pinta **cerrado**, sin botón.
4. Sin sesión: enlace a `/crear-cuenta?volver=/planes`.
5. Con sesión y sin ficha de prestador: enlace a `/ofrecer-servicios`.
6. Con ficha: botón «Quiero mi cupo» que llama API-67 y, con la respuesta, manda a
   `urlParaRedirigir`. Mientras la llamada está en vuelo, el botón queda deshabilitado — si no,
   dos clics toman dos cupos.
7. Si API-67 responde `409`, vuelve a pedir API-66 y pinta el estado «cerrado»: alguien se llevó
   el último mientras mirabas.

**TEXTOS PROPUESTOS, pendientes de Pablo** (ponlos en el PR bajo «Textos nuevos»): «Quiero mi
cupo» · «Quedan {n} de 50 cupos de fundador» · «Los 50 cupos de fundador ya están tomados. El plan
mensual sigue abierto.» · «Primero publica tu ficha: el cupo se asigna a tu perfil de prestador.»
· «No pudimos abrir el pago. Vuelve a intentarlo.»

- [ ] **Paso 5: La pantalla del pago simulado**

`frontend/src/paginas/PagoSimulado.tsx`: lee `ref` y `perfil` de la URL, muestra que es una
pasarela **de prueba** —con todas las letras, para que nadie la confunda con un cobro real— y un
botón «Inscribir mi medio de pago» que manda el aviso a `/api/pagos/aviso` con el cuerpo
`{ referencia, perfilPrestadorId, monto: 0, aceptada: true }`.

**La firma no se calcula en el navegador.** Una clave de firma en el frontend es una clave
pública. Que la pantalla pida al backend que firme por ella: añade a `EndpointsSuscripciones` un
endpoint **solo para desarrollo** que firme el cuerpo, y **no lo registres si el proveedor no es
`Simulada`**. Si eso te parece demasiada superficie, la alternativa aceptable es que esta pantalla
llame a un endpoint `POST /api/pagos/simular` que haga las dos cosas (firmar y entregar el aviso a
API-68), registrado bajo la misma condición. Elige una, y escribe en el PR cuál y por qué.

- [ ] **Paso 6: Las rutas**

En `App.tsx`, junto a las demás:

```tsx
        <Route path="planes" element={<Planes />} />
        <Route path="planes/listo" element={<PlanesListo />} />
        <Route path="pago/inscripcion" element={<PagoSimulado />} />
```

`PlanesListo` puede ser un componente pequeño dentro de `Planes.tsx`: dice que se está
confirmando, consulta `GET /api/mi/suscripcion` y, cuando aparece el plan fundador, felicita y
enlaza a `/mi-ficha`. **No confirma nada por su cuenta**: lo que confirma es el aviso.

- [ ] **Paso 7: Verlas pasar, con lint y tipos**

```
cd frontend
npx vitest run src/paginas/Planes.prueba.tsx
npm run prueba
npx tsc -b
npx oxlint
```

Esperado: las 5 nuevas en verde, la suite entera en verde (eran 488), `tsc` limpio y 0 avisos
nuevos de lint.

- [ ] **Paso 8: Las mutaciones**

1. Quita la guarda del doble clic (el `deshabilitado` mientras la llamada está en vuelo) → debe
   caer la prueba que la vigila. Si no existe, **escríbela**: dos clics, una sola llamada.
2. Pinta el botón también con `cuposFundadorDisponibles === 0` → debe caer «sin cupos… sin botón».

- [ ] **Paso 9: Commit**

```bash
git add frontend/src
git commit -m "Pantalla /planes: los dos planes, el contador de cupos y sus cuatro estados"
```

---

### Task 7: El documento rector (S7) y la regeneración del sitio

Aquí la documentación **es** la especificación, así que esto no es un extra: es parte del
entregable. Hoy no existe `subsistema-7`, no hay ninguna regla `RN-S7-` y `api-rest.md` no numera
ningún endpoint de suscripción.

**Archivos** (en `C:\Users\pdani\OneDrive\Desktop\Repositorio ZASS\zass-documentacion`):
- Crear: `docs/01-casos-de-uso/subsistema-7-suscripciones-y-pagos.md`
- Modificar: `docs/.../api-rest.md` (API-66, API-67, API-68)
- Modificar: el índice que corresponda, para que el documento nuevo no quede huérfano

- [ ] **Paso 1: Escribir el documento**

Con la estructura de los otros seis subsistemas (léelos primero: la forma manda). Las siete reglas,
con este texto como base:

| Regla | Qué dice |
|---|---|
| RN-S7-01 | El cupo fundador se confirma al **inscribir el medio de pago**, no al reservarlo |
| RN-S7-02 | La reserva dura **25 minutos**; vencida sin confirmar, el cupo vuelve a estar disponible |
| RN-S7-03 | El mes gratis son **30 días** en estado `Prueba` sobre el plan fundador |
| RN-S7-04 | El aviso de la pasarela es **idempotente**: el cupo ya confirmado es el registro |
| RN-S7-05 | El monto **no se cree del cuerpo** del aviso: 0 en la inscripción, el precio del plan en el cobro |
| RN-S7-06 | Al llegar a **50** el plan fundador se cierra y no se reabre |
| RN-S7-07 | Tomar el cupo **cambia de plan** la suscripción existente y deja la vigencia en la fecha más lejana; nunca crea una segunda |

Y la excepción que hay que dejar escrita: **API-68 no lleva CSRF** porque no hay navegador ni
sesión; su autenticidad la da la firma. Es una excepción deliberada a la regla 8 de `CLAUDE.md`.

- [ ] **Paso 2: Numerar los endpoints en `api-rest.md`**

API-66 (`GET /api/planes`, público, grupo P), API-67 (`POST /api/mi/suscripcion/fundador`, sesión +
CSRF, grupo S), API-68 (`POST /api/pagos/aviso`, sin sesión, con firma, grupo G). El máximo
anterior del documento es **API-65**: comprueba que sigue siéndolo antes de numerar, con
`grep -roh "API-[0-9]\+" docs/ | sort -t- -k2 -n | uniq | tail -3`.

- [ ] **Paso 3: Regenerar el sitio**

```
cd "C:\Users\pdani\OneDrive\Desktop\Repositorio ZASS\zass-documentacion"
python sitio\construir.py
```

El HTML **nunca** se edita a mano. Y comprueba el resultado como manda la lección del proyecto:
abre el HTML generado y busca el texto de RN-S7-04 **dentro** de él. Que el script no diera error
no prueba que la página tenga el contenido — aquí un error de sintaxis de Mermaid también dibujaba
un SVG, y dos diagramas estaban rotos.

- [ ] **Paso 4: Commit (en el repositorio de documentación, si es otro)**

```bash
git add docs sitio
git commit -m "S7: el documento rector de suscripciones y pagos, con RN-S7-01 a 07 y API-66 a 68"
```

---

### Task 8: El camino completo, visto en un navegador

Un verde en jsdom no es haber visto la pantalla. Esto cierra el plan.

**Archivos:**
- Crear: `frontend/pruebas-navegador/cupo-fundador.mjs` (sigue el estilo de los 7 guiones que ya hay)

- [ ] **Paso 1: Levantar todo**

```
cd C:\Users\pdani\zass-app-1
.\iniciar-postgres.bat
.\ver-zass.bat
```

- [ ] **Paso 2: Escribir el guion**

Recorrido: entrar con la cuenta de demostración (`demo-zass-2026`) → `/planes` → ver el contador →
«Quiero mi cupo» → la pantalla de pago simulado → inscribir → volver → **y comprobar que el
prestador aparece en la búsqueda**. Captura de pantalla en cada paso, como los demás guiones.

- [ ] **Paso 3: Correrlo**

```
cd frontend
node pruebas-navegador/cupo-fundador.mjs
```

Esperado: termina sin error y deja sus capturas.

- [ ] **Paso 4: Comprobar en la base, no en la pantalla**

```sql
SELECT numero, perfil_prestador_id, reservado_hasta FROM cupos_fundador WHERE perfil_prestador_id IS NOT NULL;
SELECT plan_id, estado, vigente_hasta FROM suscripciones WHERE perfil_prestador_id = '<el del demo>';
SELECT indexname FROM pg_indexes WHERE tablename = 'cupos_fundador';
```

Esperado: **un** cupo con `reservado_hasta` nulo, **una** suscripción en plan fundador con estado
`Prueba` y 30 días, y `ux_cupo_fundador_perfil` presente.

- [ ] **Paso 5: Mandar el mismo aviso dos veces, a mano**

Repite la petición a `/api/pagos/aviso` con el mismo cuerpo y la misma firma. Esperado: `200`, y
las dos consultas de arriba **sin cambios**. Esto es RN-S7-04 comprobado contra PostgreSQL, no
contra un doble.

- [ ] **Paso 6: Commit y PR**

```bash
git add frontend/pruebas-navegador
git commit -m "Guion de navegador: tomar cupo, inscribir y aparecer en la busqueda"
```

PR `[N3]` contra `main` (o contra `integracion/13-nov` si el PR #11 sigue abierto), con la
plantilla llena: **Mutación** con las once mutaciones de este plan y qué cayó en cada una,
**Pruebas** con los números de las cuatro suites, y **Textos nuevos** con la lista literal.

---

## Qué queda fuera, dicho para que nadie lo busque

- El **cupón de descuento** y el **mes gratis general**: C2, y necesitan migración.
- El **cobro del día 31** y la **gracia**: C3. `CobrarAsync` está declarado y sin usar a propósito.
  **Fecha límite: 23 de diciembre de 2026**, que es cuando se termina el mes gratis de quien tome
  su cupo el lunes 23.
- **Dar de baja el medio de pago** desde el panel: deuda, va a `ESTADO.md`.
