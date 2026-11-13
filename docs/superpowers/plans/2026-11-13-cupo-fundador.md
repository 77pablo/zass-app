# C1 · Cupo fundador y cobro — plan de implementación

> **Para quien lo ejecute:** usa `superpowers:subagent-driven-development` (recomendado) o
> `superpowers:executing-plans` tarea por tarea. Los pasos llevan casilla (`- [ ]`).

**Objetivo:** que un prestador pueda tomar uno de los 50 cupos fundador, inscribir su medio de
pago y quedar con el plan fundador y el mes gratis corriendo, visible en la búsqueda. Y que lo que
ya está escrito para cobrar tenga, por fin, pruebas y base de datos.

**Especificación:** `docs/superpowers/specs/2026-11-13-cupo-fundador-design.md`. Donde este plan la
contradice, lo dice con todas las letras (§0).

**Arquitectura:** sobre el esqueleto de cobro rescatado (`65042b9`), que ya trae el puerto
`IPasarelaDePagos` agnóstico de la pasarela, `ServicioCobros` con cotizar/iniciar/confirmar, y
`IntentoDePago` (TBL-36) como ancla del monto y de la idempotencia. Este plan le pone lo que le
falta: pruebas, migración, la guarda de producción, la inscripción con cobro $0, los endpoints, la
pantalla y el documento rector.

**Herramientas:** .NET 8, minimal APIs, EF Core, xUnit · React 19 + TypeScript + Vite, vitest,
@testing-library/react, Playwright.

---

## §0. Lo que cambió respecto de la especificación, y por qué

La especificación se escribió antes de descubrir que ya existía un esqueleto de cobro sin
commitear. Tras compararlos, Pablo decidió adoptar el rescatado. Tres cambios, con su motivo:

| La especificación decía | Ahora | Por qué |
|---|---|---|
| El aviso entra como cuerpo + firma HMAC | Entra como **diccionario de campos** (`AvisoDePasarela`) y lo verifica el adaptador | Webpay de Transbank **no manda un webhook firmado**: devuelve un token por el navegador y uno consulta al banco. Poner HMAC en el puerto era elegir la pasarela en el puerto |
| Idempotencia: «el cupo confirmado es el registro» | Idempotencia por **`IntentoDePago`**, con bloqueo de fila y el índice único `ux_pago_intento` como garantía última | El monto queda fijado en el servidor antes de salir a internet, y hay algo contra lo que comparar lo que conteste la pasarela |
| **Ninguna migración** | **Una migración**, con las tres tablas que el modelo ya declara | El esqueleto declara `IntentoDePago`, `Cupon` y `CanjeDeCupon` en `ZassDbContext`. Sin migración la aplicación no arranca contra la base real |

Lo que **no** cambió: el cupo se confirma al **inscribir el medio de pago** con cobro $0, no al
primer pago real (decisión de Pablo del 13-nov). El esqueleto rescatado hace lo segundo, así que la
tarea 7 lo adapta.

## Restricciones globales

- `Zass.Dominio` tiene **cero paquetes NuGet**.
- **Una migración y solo una** (tarea 2). Antes de crearla se avisa, porque es zona N3.
- Rama: `cupo-fundador`, que sale de `integracion/13-nov` y ya trae el esqueleto adoptado.
- Toda prueba nueva necesita su **mutación**: romper a propósito el código que vigila, ver caer la
  prueba, deshacer. Sin eso no cuenta.
- **Candados con literal:** 50 cupos, 25 minutos, 30 días, 5990, 7990 y el 20 % del cupón,
  escritos a mano en una prueba aparte de la que usa la constante.
- El doble en memoria **no filtra por su cuenta** lo que el código de producción debe filtrar. Si
  lo hace, la mutación no se nota.
- Secretos: nunca en el repositorio, ni en una prueba, ni en un `appsettings` versionado.
- Textos visibles: se proponen, no se deciden. Listados en el PR bajo «Textos nuevos».
- Integración contra `zass_pruebas`, y **nunca** dos suites de integración a la vez.
- `403` y `404` responden igual, a propósito.

## Lo que el esqueleto adoptado ya trae (no lo reescribas)

| Pieza | Dónde | Qué hace |
|---|---|---|
| `IPasarelaDePagos` | `Zass.Aplicacion/Puertos/Pasarela.cs` | `Nombre`, `IniciarCobroAsync`, `LeerConfirmacionAsync`. Sin un solo concepto de Transbank, Flow ni Mercado Pago |
| `ServicioCobros` (432 líneas) | `Zass.Aplicacion/Suscripciones/ServicioCobros.cs` | Cotizar, iniciar con clave de idempotencia (CC-04), confirmar con bloqueo de fila, comparación de monto, canje del cupón y confirmación del cupo |
| `IntentoDePago` (TBL-36), `Cupon` (TBL-34), `CanjeDeCupon` | `Zass.Dominio/Suscripciones/Entidades.cs` | El monto fijado en el servidor; `MarcarPagado` devuelve falso si ya estaba resuelto |
| 11 métodos nuevos de repositorio | `RepositorioSuscripciones.cs` | `TomarIntentoAsync` (bloqueo de fila), `IntentoPorClaveAsync`, `CuponPorCodigoAsync`, `CupoFundadorDeAsync`… |
| `PasarelaSimulada` | `Zass.Infraestructura/Pagos/` | El adaptador de desarrollo |

**Y lo que NO trae, que es este plan:** ni una prueba, ninguna migración, la guarda de producción
que su propio comentario promete, la inscripción con cobro $0, los endpoints, la pantalla y el
documento S7.

---

## BLOQUE 1 — Que compile y esté medido

### Task 1: El doble en memoria al día, y las cuatro suites verdes

El código de producción **compila** sobre `integracion/13-nov`; lo que no compila es el doble de
pruebas, al que le faltan los 11 miembros nuevos de `IRepositorioSuscripciones`. Son **11 errores
`CS0535`**, todos en el mismo archivo.

**Archivos:**
- Modificar: `backend/pruebas/Zass.Aplicacion.Pruebas/Dobles/RepositorioSuscripciones.cs`
- Modificar: `backend/pruebas/Zass.Aplicacion.Pruebas/Dobles/Repositorios.cs` (las tres listas nuevas en `BaseEnMemoria`)

**Interfaces:**
- Produce: `BaseEnMemoria.IntentosDePago`, `.Cupones`, `.Canjes`; y las 11 implementaciones.

- [ ] **Paso 1: Ver los 11 errores**

```
cd backend
dotnet build -v q --nologo
```

Esperado: `11 Errores`, todos `CS0535` en `Dobles/RepositorioSuscripciones.cs(14,57)`. Esa es la
lista de trabajo de esta tarea; no la adivines.

- [ ] **Paso 2: Las tres listas nuevas**

En `Dobles/Repositorios.cs`, junto a las demás listas de `BaseEnMemoria`:

```csharp
    public List<IntentoDePago> IntentosDePago { get; } = new();
    public List<Cupon> Cupones { get; } = new();
    public List<CanjeDeCupon> Canjes { get; } = new();
```

- [ ] **Paso 3: Los 11 miembros**

En `Dobles/RepositorioSuscripciones.cs`:

```csharp
    public Task<Plan?> PlanPorIdAsync(short id, CancellationToken ct = default) =>
        Task.FromResult(_base.Planes.FirstOrDefault(p => p.Id == id));

    public Task<int> CuposFundadorTotalesAsync(CancellationToken ct = default) =>
        Task.FromResult(_base.CuposFundador.Count);

    public Task<short?> CupoFundadorDeAsync(
        Guid perfilPrestadorId, CancellationToken ct = default)
    {
        var cupo = _base.CuposFundador.FirstOrDefault(c => c.PerfilPrestadorId == perfilPrestadorId);
        return Task.FromResult(cupo is null ? (short?)null : cupo.Numero);
    }

    public Task<IntentoDePago?> IntentoPorIdAsync(Guid id, CancellationToken ct = default) =>
        Task.FromResult(_base.IntentosDePago.FirstOrDefault(i => i.Id == id));

    public Task<IntentoDePago?> IntentoPorClaveAsync(
        string clave, CancellationToken ct = default) =>
        Task.FromResult(_base.IntentosDePago.FirstOrDefault(i => i.ClaveIdempotencia == clave));

    /// <summary>
    /// En memoria no hay bloqueo de fila: devuelve el mismo objeto. Que la segunda
    /// confirmacion ESPERE de verdad lo prueba integracion, contra PostgreSQL.
    /// </summary>
    public Task<IntentoDePago?> TomarIntentoAsync(Guid id, CancellationToken ct = default) =>
        IntentoPorIdAsync(id, ct);

    public void AgregarIntento(IntentoDePago intento) => _base.IntentosDePago.Add(intento);

    public Task<bool> TuvoMesPagadoAsync(
        Guid perfilPrestadorId, CancellationToken ct = default) =>
        Task.FromResult(_base.Pagos.Any(p =>
            p.Estado == EstadoPago.Pagado
            && _base.Suscripciones.Any(s => s.Id == p.SuscripcionId
                                         && s.PerfilPrestadorId == perfilPrestadorId)));

    public Task<Cupon?> CuponPorCodigoAsync(string codigo, CancellationToken ct = default) =>
        Task.FromResult(_base.Cupones.FirstOrDefault(c => c.Codigo == codigo));

    public Task<bool> YaCanjeoCuponAsync(
        Guid perfilPrestadorId, CancellationToken ct = default) =>
        Task.FromResult(_base.Canjes.Any(c => c.PerfilPrestadorId == perfilPrestadorId));

    public void AgregarCanje(CanjeDeCupon canje) => _base.Canjes.Add(canje);
```

Si `BaseEnMemoria` no tiene `Pagos`, añade `public List<Pago> Pagos { get; } = new();` y
comprueba cómo lo escribe `AgregarPago`. **No inventes una condición distinta en
`TuvoMesPagadoAsync`**: tiene que significar lo mismo que la consulta EF de
`RepositorioSuscripciones.cs` — ábrela y compárala línea a línea antes de dar esto por bueno.

- [ ] **Paso 4: Compilar y correr las cuatro suites**

```
cd backend
dotnet build -v q --nologo
dotnet test pruebas/Zass.Aplicacion.Pruebas
dotnet test pruebas/Zass.Arquitectura.Pruebas
```

Esperado: 0 errores, y las dos suites **con los mismos números que antes del esqueleto**
(aplicación 679, arquitectura 16). Si alguna cae, el esqueleto rompió algo que ya funcionaba: eso
es un hallazgo, va al PR, y se arregla antes de seguir.

Integración y frontend se corren en la tarea 2 y en la 9, cuando ya hay base de datos y pantalla.

- [ ] **Paso 5: Commit**

```bash
git add backend/pruebas/Zass.Aplicacion.Pruebas
git commit -m "El doble en memoria implementa los 11 miembros nuevos del repositorio de suscripciones"
```

---

### Task 2: La migración de las tres tablas (N3 — avisa antes de crearla)

**Zona N3.** Antes de ejecutar esta tarea, avisa en el chat que vas a crear una migración y espera
el visto bueno. Es la única de este plan.

**Archivos:**
- Crear: `backend/src/Zass.Infraestructura/Datos/Migraciones/<fecha>_CobroIntentosYCupones.cs` (la genera EF)
- Modificar: `ZassDbContextModelSnapshot.cs` (la genera EF)

- [ ] **Paso 1: Comprobar qué declara el modelo**

```
cd backend
grep -n "IntentoDePago\|Cupon\|CanjeDeCupon\|ux_pago_intento\|ux_canje_cupon" src/Zass.Infraestructura/Datos/ZassDbContext.cs
```

Esperado: tres `Entity<>` nuevos, índices únicos con filtro (`ux_pago_intento` sobre
`Pago.IntentoDePagoId`, el de `ClaveIdempotencia`, `ux_canje_cupon_pago`) y restricciones `CHECK`.
Anótalos: son lo que hay que verificar en el paso 5.

- [ ] **Paso 2: Levantar la base local y exportar la cadena**

```
cd C:\Users\pdani\zass-app-1
.\iniciar-postgres.bat
$env:ZASS_CADENA_CONEXION = "Host=127.0.0.1;Port=5433;Database=zass;Username=zass;Password=<la de .clave-pg.txt>"
```

La clave la pones tú desde fuera del chat. **No la pegues en ningún archivo ni en el PR.**

- [ ] **Paso 3: Crear la migración**

```
cd backend
dotnet ef migrations add CobroIntentosYCupones --project src/Zass.Infraestructura --startup-project src/Zass.Api
```

- [ ] **Paso 4: Leerla antes de aplicarla**

Ábrela y comprueba, una por una: las tres tablas con sus columnas en `snake_case`; el índice único
**parcial** sobre `pagos.intento_de_pago_id` con su filtro `IS NOT NULL`; el índice único sobre
`intentos_pago.clave_idempotencia`; las restricciones `CHECK`; y que **no toca ninguna tabla
existente** salvo la columna nueva de `pagos`. Si toca algo más, para y dilo: el modelo y la base
no están de acuerdo en algo que nadie vio.

- [ ] **Paso 5: Aplicarla y comprobar en la base, no en la salida del comando**

```
dotnet ef database update --project src/Zass.Infraestructura --startup-project src/Zass.Api
```

Y después, con `psql`:

```sql
SELECT tablename FROM pg_tables WHERE tablename IN ('intentos_pago','cupones','canjes_cupon');
SELECT indexname, indexdef FROM pg_indexes WHERE tablename IN ('pagos','intentos_pago','canjes_cupon');
SELECT conname, pg_get_constraintdef(oid) FROM pg_constraint
 WHERE conrelid IN ('intentos_pago'::regclass,'cupones'::regclass,'canjes_cupon'::regclass);
```

Esperado: las tres tablas, `ux_pago_intento` **con su filtro**, el único de la clave de
idempotencia, `ux_canje_cupon_pago`, y los `CHECK`. **Que la migración diga «Done» no prueba nada**
— en este proyecto ya mintió un verde que solo comprobaba que el artefacto existía.

- [ ] **Paso 6: Correr la suite de integración entera**

```
dotnet test pruebas/Zass.Integracion.Pruebas
```

Esperado: los mismos números que antes (616), ninguna omitida.

- [ ] **Paso 7: Commit**

```bash
git add backend/src/Zass.Infraestructura/Datos/Migraciones
git commit -m "Migracion: intentos de pago (TBL-36), cupones (TBL-34) y canjes, con ux_pago_intento"
```

---

## BLOQUE 2 — Las pruebas que el esqueleto no tiene

Son tres archivos nuevos de pruebas unitarias, uno por operación. Todas con dobles en memoria;
lo que necesita PostgreSQL de verdad va en la tarea 11.

### Task 3: Cotizar — el precio, el cupón y el cierre al cupo 50

**Archivos:**
- Prueba: `backend/pruebas/Zass.Aplicacion.Pruebas/Suscripciones/CotizarCobroPruebas.cs`

**Interfaces:**
- Consume: `IServicioCobros.CotizarAsync(Guid perfilPrestadorId, string codigoPlan, string? codigoCupon, CancellationToken)`
  → `Resultado<CotizacionDto>` con `PrecioMensual`, `Descuento`, `Total`, `Cupon`,
  `PorcentajeDescuento`, `EsElPrimerMesQueSePaga`, `Detalle`.

- [ ] **Paso 1: Escribir las pruebas, que deben fallar**

```csharp
using Zass.Aplicacion.Pruebas.Dobles;
using Zass.Aplicacion.Suscripciones;
using Zass.Dominio.Comun;
using Zass.Dominio.Identidad;
using Zass.Dominio.Suscripciones;

namespace Zass.Aplicacion.Pruebas.Suscripciones;

/// <summary>
/// Cotizar no escribe nada: dice cuanto se cobraria. Existe porque la pantalla necesita
/// mostrar «$4.792 con tu cupon» ANTES de que exista un cobro.
/// </summary>
public class CotizarCobroPruebas
{
    private static readonly DateTime Ahora = new(2026, 11, 13, 12, 0, 0, DateTimeKind.Utc);

    private readonly BaseEnMemoria _base = new();
    private readonly RelojFijo _reloj = new(Ahora);
    private readonly ServicioCobros _cobros;

    public CotizarCobroPruebas()
    {
        var repo = new RepositorioSuscripcionesEnMemoria(_base);
        var suscripciones = new ServicioSuscripciones(
            repo, _base, _reloj, new AuditoriaEnMemoria(), new ColaEnMemoria());
        _cobros = new ServicioCobros(
            repo, _base, _reloj, new AuditoriaEnMemoria(), suscripciones, new PasarelaFalsa());
    }

    private PerfilPrestador UnPrestador()
    {
        var perfil = new PerfilPrestador { Id = Guid.NewGuid(), UsuarioId = Guid.NewGuid() };
        _base.PerfilesPrestador.Add(perfil);
        return perfil;
    }

    private void UnCupon(string codigo, short porcentaje = 20)
        => _base.Cupones.Add(new Cupon
        {
            Codigo = codigo, PorcentajeDescuento = porcentaje,
            Activo = true, VigenteHasta = Ahora.AddDays(30),
        });

    [Fact]
    public async Task El_plan_mensual_cuesta_7990_sin_cupon()
    {
        var perfil = UnPrestador();

        var r = await _cobros.CotizarAsync(perfil.Id, "mensual", null);

        Assert.True(r.Exito);
        Assert.Equal(7990, r.Valor!.PrecioMensual);   // candado con literal
        Assert.Equal(0, r.Valor!.Descuento);
        Assert.Equal(7990, r.Valor!.Total);
    }

    [Fact]
    public async Task El_plan_fundador_cuesta_5990()
    {
        var perfil = UnPrestador();

        var r = await _cobros.CotizarAsync(perfil.Id, "fundador", null);

        Assert.True(r.Exito);
        Assert.Equal(5990, r.Valor!.Total);           // candado con literal
    }

    [Fact]
    public async Task El_cupon_del_20_por_ciento_descuenta_1598_del_mensual()
    {
        var perfil = UnPrestador();
        UnCupon("ZASS20");

        var r = await _cobros.CotizarAsync(perfil.Id, "mensual", "ZASS20");

        Assert.True(r.Exito);
        // 20 % de 7990 = 1598. Los tres numeros escritos a mano a proposito.
        Assert.Equal(1598, r.Valor!.Descuento);
        Assert.Equal(6392, r.Valor!.Total);
        Assert.Equal((short)20, r.Valor!.PorcentajeDescuento);
    }

    [Fact]
    public async Task El_codigo_en_minuscula_y_con_espacios_igual_sirve()
    {
        var perfil = UnPrestador();
        UnCupon("ZASS20");

        var r = await _cobros.CotizarAsync(perfil.Id, "mensual", "  zass20 ");

        Assert.True(r.Exito);
        Assert.Equal(1598, r.Valor!.Descuento);
    }

    [Fact]
    public async Task Un_cupon_que_no_existe_FALLA_en_vez_de_cobrar_el_precio_completo()
    {
        var perfil = UnPrestador();

        var r = await _cobros.CotizarAsync(perfil.Id, "mensual", "NOEXISTE");

        Assert.False(r.Exito);
        Assert.Equal(Fallas.NoEncontrado, r.Codigo);
    }

    [Fact]
    public async Task Un_cupon_vencido_no_sirve()
    {
        var perfil = UnPrestador();
        _base.Cupones.Add(new Cupon
        {
            Codigo = "VIEJO", PorcentajeDescuento = 20, Activo = true,
            VigenteHasta = Ahora.AddDays(-1),
        });

        var r = await _cobros.CotizarAsync(perfil.Id, "mensual", "VIEJO");

        Assert.False(r.Exito);
        Assert.Equal(Fallas.Validacion, r.Codigo);
    }

    [Fact]
    public async Task El_plan_de_prueba_no_se_cotiza_porque_no_se_paga()
    {
        var perfil = UnPrestador();

        var r = await _cobros.CotizarAsync(perfil.Id, "prueba", null);

        Assert.False(r.Exito);
        Assert.Equal(Fallas.Validacion, r.Codigo);
    }

    [Fact]
    public async Task Sin_cupos_el_plan_fundador_no_se_cotiza_a_quien_no_tiene_cupo()
    {
        foreach (var cupo in _base.CuposFundador)
        {
            cupo.PerfilPrestadorId = Guid.NewGuid();
            cupo.ReservadoHasta = null;
        }
        var perfil = UnPrestador();

        var r = await _cobros.CotizarAsync(perfil.Id, "fundador", null);

        Assert.False(r.Exito);
        Assert.Equal(Fallas.Conflicto, r.Codigo);
    }

    [Fact]
    public async Task Sin_cupos_pero_CON_cupo_propio_si_se_cotiza_al_precio_de_lanzamiento()
    {
        var perfil = UnPrestador();
        var mio = _base.CuposFundador.First();
        mio.PerfilPrestadorId = perfil.Id;
        mio.ReservadoHasta = null;
        foreach (var cupo in _base.CuposFundador.Where(c => c.PerfilPrestadorId is null))
        {
            cupo.PerfilPrestadorId = Guid.NewGuid();
        }

        var r = await _cobros.CotizarAsync(perfil.Id, "fundador", null);

        // El precio congelado es suyo mientras mantenga la suscripcion, tambien cuando ya
        // no quedan cupos para nadie mas.
        Assert.True(r.Exito);
        Assert.Equal(5990, r.Valor!.Total);
    }

    [Fact]
    public async Task Un_perfil_que_no_existe_no_cotiza()
    {
        var r = await _cobros.CotizarAsync(Guid.NewGuid(), "mensual", null);

        Assert.False(r.Exito);
        Assert.Equal(Fallas.NoEncontrado, r.Codigo);
    }
}
```

`PasarelaFalsa` se define una vez y se reusa en las tareas 4 y 5: ponla en
`Dobles/PasarelaFalsa.cs`, con `Nombre => "falsa"`, `IniciarCobroAsync` devolviendo
`CobroIniciado("ref_1", "https://local.test/pagar/ref_1")` y un `Veredicto` público que
`LeerConfirmacionAsync` devuelve, para que las pruebas de la tarea 5 lo fijen.

Si `BaseEnMemoria` no siembra los planes (`prueba`, `mensual` 7990, `fundador` 5990) ni los 50
cupos, siémbralos ahí: es el estado que la base real tiene por migración, y un doble que arranca
vacío obliga a cada prueba a montarlo.

- [ ] **Paso 2: Verlas fallar**

```
cd backend
dotnet test pruebas/Zass.Aplicacion.Pruebas --filter "FullyQualifiedName~CotizarCobro"
```

Esperado: no compila (falta `PasarelaFalsa`) y, una vez compile, **fallan las que descubran
diferencias reales** entre lo que el esqueleto hace y lo que estas pruebas esperan.

- [ ] **Paso 3: Decidir caso por caso quién tiene razón**

Esto es lo importante de esta tarea: el código no está probado, así que **una prueba roja puede
significar que el código está mal, o que mi expectativa está mal**. Para cada roja, decide y
escríbelo en el PR: «arreglé el código porque…» o «corregí la prueba porque…». No toques el código
sin decir por qué.

- [ ] **Paso 4: Verlas pasar**

Esperado: **10 passed**.

- [ ] **Paso 5: Las mutaciones**

1. En `CalcularAsync`, borra la comprobación `YaCanjeoCuponAsync` → tiene que caer una prueba. Si
   no cae ninguna, **falta una prueba**: escríbela (la misma cuenta no canjea dos veces).
2. Cambia `descuento = cupon.DescuentoSobre(...)` por `descuento = 0` → deben caer las del cupón.
3. Borra el `if (plan.PrecioMensual <= 0)` → debe caer `El_plan_de_prueba_no_se_cotiza…`.

- [ ] **Paso 6: Commit**

```bash
git add backend/pruebas/Zass.Aplicacion.Pruebas
git commit -m "Pruebas de cotizar: precios, cupon del 20 % y el cierre al cupo 50"
```

---

### Task 4: Iniciar — la idempotencia y la reserva del cupo

**Archivos:**
- Prueba: `backend/pruebas/Zass.Aplicacion.Pruebas/Suscripciones/IniciarCobroPruebas.cs`

**Interfaces:**
- Consume: `IServicioCobros.IniciarAsync(IniciarCobroCmd(Guid PerfilPrestadorId, string CodigoPlan, string? CodigoCupon, string ClaveIdempotencia))`
  → `Resultado<IntentoDePagoDto>` con `Id`, `Total`, `Estado`, `UrlParaPagar`, `Nueva`.

- [ ] **Paso 1: Escribir las pruebas, que deben fallar**

Monta el servicio igual que en la tarea 3, con una suscripción de prueba ya creada para el perfil
(porque iniciar exige tenerla: «la suscripción nace al publicar el perfil»). Las pruebas:

```csharp
    [Fact]
    public async Task Iniciar_crea_el_intento_con_el_monto_fijado_en_el_servidor()
    {
        var perfil = UnPrestadorConSuscripcion();

        var r = await _cobros.IniciarAsync(new IniciarCobroCmd(perfil.Id, "mensual", null, "k1"));

        Assert.True(r.Exito);
        Assert.True(r.Valor!.Nueva);
        Assert.Equal(7990, r.Valor!.Total);
        var intento = Assert.Single(_base.IntentosDePago);
        Assert.Equal(7990, intento.Monto);
        Assert.Equal(EstadoIntentoPago.Pendiente, intento.Estado);
        Assert.Equal("falsa", intento.Pasarela);
        Assert.NotNull(intento.UrlParaPagar);
    }

    [Fact]
    public async Task CC_04_el_mismo_envio_dos_veces_crea_UN_intento()
    {
        var perfil = UnPrestadorConSuscripcion();

        var primero = await _cobros.IniciarAsync(new IniciarCobroCmd(perfil.Id, "mensual", null, "k1"));
        var segundo = await _cobros.IniciarAsync(new IniciarCobroCmd(perfil.Id, "mensual", null, "k1"));

        Assert.True(primero.Valor!.Nueva);
        Assert.False(segundo.Valor!.Nueva);
        Assert.Equal(primero.Valor!.Id, segundo.Valor!.Id);
        Assert.Single(_base.IntentosDePago);
    }

    [Fact]
    public async Task Dos_claves_distintas_si_son_dos_intentos()
    {
        var perfil = UnPrestadorConSuscripcion();

        await _cobros.IniciarAsync(new IniciarCobroCmd(perfil.Id, "mensual", null, "k1"));
        await _cobros.IniciarAsync(new IniciarCobroCmd(perfil.Id, "mensual", null, "k2"));

        Assert.Equal(2, _base.IntentosDePago.Count);
    }

    [Fact]
    public async Task Sin_clave_de_idempotencia_no_se_inicia_nada()
    {
        var perfil = UnPrestadorConSuscripcion();

        var r = await _cobros.IniciarAsync(new IniciarCobroCmd(perfil.Id, "mensual", null, "  "));

        Assert.False(r.Exito);
        Assert.Equal(Fallas.Validacion, r.Codigo);
        Assert.Empty(_base.IntentosDePago);
    }

    [Fact]
    public async Task El_plan_fundador_RESERVA_el_cupo_al_iniciar_y_la_reserva_dura_25_minutos()
    {
        var perfil = UnPrestadorConSuscripcion();

        await _cobros.IniciarAsync(new IniciarCobroCmd(perfil.Id, "fundador", null, "k1"));

        var cupo = _base.CuposFundador.Single(c => c.PerfilPrestadorId == perfil.Id);
        Assert.Equal(Ahora.AddMinutes(25), cupo.ReservadoHasta);   // candado con literal
        Assert.Equal(49, await _suscripciones.CuposFundadorDisponiblesAsync());
    }

    [Fact]
    public async Task Si_alguien_se_llevo_el_ultimo_cupo_el_mensaje_dice_que_esta_cerrado()
    {
        foreach (var cupo in _base.CuposFundador)
        {
            cupo.PerfilPrestadorId = Guid.NewGuid();
            cupo.ReservadoHasta = null;
        }
        var perfil = UnPrestadorConSuscripcion();

        var r = await _cobros.IniciarAsync(new IniciarCobroCmd(perfil.Id, "fundador", null, "k1"));

        Assert.False(r.Exito);
        Assert.Equal(Fallas.Conflicto, r.Codigo);
        Assert.Contains("mensual", r.Mensaje!);   // ofrece la salida, no deja a la persona sin nada
        Assert.Empty(_base.IntentosDePago);
    }

    [Fact]
    public async Task Sin_suscripcion_no_se_puede_pagar_todavia()
    {
        var perfil = UnPrestador();   // sin suscripcion: el perfil no se publico

        var r = await _cobros.IniciarAsync(new IniciarCobroCmd(perfil.Id, "mensual", null, "k1"));

        Assert.False(r.Exito);
        Assert.Equal(Fallas.Conflicto, r.Codigo);
    }

    [Fact]
    public async Task Si_la_pasarela_no_contesta_el_intento_queda_pendiente_y_se_puede_retomar()
    {
        var perfil = UnPrestadorConSuscripcion();
        _pasarela.FallaAlIniciar = true;

        var primero = await _cobros.IniciarAsync(new IniciarCobroCmd(perfil.Id, "mensual", null, "k1"));
        Assert.False(primero.Exito);

        _pasarela.FallaAlIniciar = false;
        var segundo = await _cobros.IniciarAsync(new IniciarCobroCmd(perfil.Id, "mensual", null, "k1"));

        Assert.True(segundo.Exito);
        Assert.NotNull(segundo.Valor!.UrlParaPagar);
        Assert.Single(_base.IntentosDePago);   // el mismo, no uno nuevo
    }
```

- [ ] **Paso 2: Verlas fallar, decidir y verlas pasar**

Igual que en la tarea 3: `dotnet test pruebas/Zass.Aplicacion.Pruebas --filter "FullyQualifiedName~IniciarCobro"`.
Para cada roja, decide si el malo es el código o la expectativa, y escríbelo en el PR.

- [ ] **Paso 3: Las mutaciones**

1. Borra la comprobación `IntentoPorClaveAsync` de la entrada → debe caer
   `CC_04_el_mismo_envio_dos_veces_crea_UN_intento`.
2. Mueve la reserva del cupo **después** de crear el intento → debe caer
   `Si_alguien_se_llevo_el_ultimo_cupo…` (quedaría un intento huérfano).
3. Cambia `Monto = c.Total` por `Monto = c.Plan.PrecioMensual` → debe caer una del cupón; si no
   cae ninguna, añade una prueba de iniciar **con** cupón.

- [ ] **Paso 4: Commit**

```bash
git add backend/pruebas/Zass.Aplicacion.Pruebas
git commit -m "Pruebas de iniciar: CC-04, la reserva del cupo y la pasarela que no contesta"
```

---

### Task 5: Confirmar — que nadie reciba dos meses por un aviso repetido

**Archivos:**
- Prueba: `backend/pruebas/Zass.Aplicacion.Pruebas/Suscripciones/ConfirmarCobroPruebas.cs`

**Interfaces:**
- Consume: `IServicioCobros.ConfirmarAsync(AvisoDePasarela)` → `Resultado<ConfirmacionDto>` con
  `Aprobado`, `Estado`, `VigenteHasta`, `Mensaje`, `Nueva`.

- [ ] **Paso 1: Escribir las pruebas, que deben fallar**

```csharp
    private async Task<IntentoDePago> UnIntentoPendiente(string plan = "mensual", string? cupon = null)
    {
        var perfil = UnPrestadorConSuscripcion();
        await _cobros.IniciarAsync(new IniciarCobroCmd(perfil.Id, plan, cupon, "k1"));
        return _base.IntentosDePago.Single();
    }

    [Fact]
    public async Task Un_cobro_aprobado_extiende_la_suscripcion_un_mes()
    {
        var intento = await UnIntentoPendiente();
        _pasarela.Veredicto = new CobroResuelto(intento.Id, "ref_1", true, intento.Monto, null);

        var r = await _cobros.ConfirmarAsync(new AvisoDePasarela(new Dictionary<string, string>()));

        Assert.True(r.Exito);
        Assert.True(r.Valor!.Aprobado);
        Assert.True(r.Valor!.Nueva);
        Assert.Equal(EstadoIntentoPago.Pagado, _base.IntentosDePago.Single().Estado);
        var s = _base.Suscripciones.Single(x => x.Id == intento.SuscripcionId);
        Assert.Equal(EstadoSuscripcion.Activa, s.Estado);
        Assert.Single(_base.Pagos);
    }

    [Fact]
    public async Task El_MISMO_aviso_dos_veces_NO_regala_un_segundo_mes()
    {
        var intento = await UnIntentoPendiente();
        _pasarela.Veredicto = new CobroResuelto(intento.Id, "ref_1", true, intento.Monto, null);
        var aviso = new AvisoDePasarela(new Dictionary<string, string>());

        var primero = await _cobros.ConfirmarAsync(aviso);
        var vigenciaTrasElPrimero = primero.Valor!.VigenteHasta;
        var segundo = await _cobros.ConfirmarAsync(aviso);

        Assert.True(segundo.Exito);                     // 200, no error
        Assert.False(segundo.Valor!.Nueva);
        Assert.Equal(vigenciaTrasElPrimero, segundo.Valor!.VigenteHasta);
        Assert.Single(_base.Pagos);                     // UN pago, no dos
    }

    [Fact]
    public async Task Un_monto_que_no_coincide_NO_extiende_nada()
    {
        var intento = await UnIntentoPendiente();
        _pasarela.Veredicto = new CobroResuelto(intento.Id, "ref_1", true, 1, null);

        var r = await _cobros.ConfirmarAsync(new AvisoDePasarela(new Dictionary<string, string>()));

        Assert.False(r.Exito);
        Assert.Equal(Fallas.Conflicto, r.Codigo);
        Assert.Empty(_base.Pagos);
        Assert.Equal(EstadoIntentoPago.Pendiente, _base.IntentosDePago.Single().Estado);
    }

    [Fact]
    public async Task Una_tarjeta_rechazada_es_un_desenlace_normal_y_no_un_error_del_sistema()
    {
        var intento = await UnIntentoPendiente();
        _pasarela.Veredicto = new CobroResuelto(intento.Id, null, false, null, "Tarjeta rechazada");

        var r = await _cobros.ConfirmarAsync(new AvisoDePasarela(new Dictionary<string, string>()));

        Assert.True(r.Exito);                 // el caso de uso funciono
        Assert.False(r.Valor!.Aprobado);      // el cobro no
        Assert.Equal(EstadoIntentoPago.Fallido, _base.IntentosDePago.Single().Estado);
        Assert.Empty(_base.Pagos);
    }

    [Fact]
    public async Task Un_aviso_ilegible_no_toca_nada()
    {
        await UnIntentoPendiente();
        _pasarela.FallaAlLeer = true;

        var r = await _cobros.ConfirmarAsync(new AvisoDePasarela(new Dictionary<string, string>()));

        Assert.False(r.Exito);
        Assert.Equal(EstadoIntentoPago.Pendiente, _base.IntentosDePago.Single().Estado);
        Assert.Empty(_base.Pagos);
    }

    [Fact]
    public async Task El_canje_del_cupon_nace_con_el_pago_y_no_antes()
    {
        UnCupon("ZASS20");
        var intento = await UnIntentoPendiente(cupon: "ZASS20");
        Assert.Empty(_base.Canjes);           // al iniciar no hay canje

        _pasarela.Veredicto = new CobroResuelto(intento.Id, "ref_1", true, intento.Monto, null);
        await _cobros.ConfirmarAsync(new AvisoDePasarela(new Dictionary<string, string>()));

        var canje = Assert.Single(_base.Canjes);
        Assert.Equal(1598, canje.MontoDescontado);      // candado con literal
    }

    [Fact]
    public async Task Una_tarjeta_rechazada_NO_le_quema_el_cupon_a_nadie()
    {
        UnCupon("ZASS20");
        var intento = await UnIntentoPendiente(cupon: "ZASS20");
        _pasarela.Veredicto = new CobroResuelto(intento.Id, null, false, null, "Rechazada");

        await _cobros.ConfirmarAsync(new AvisoDePasarela(new Dictionary<string, string>()));

        Assert.Empty(_base.Canjes);
    }

    [Fact]
    public async Task Pagado_el_plan_fundador_el_cupo_deja_de_ser_una_reserva_y_es_suyo()
    {
        var intento = await UnIntentoPendiente(plan: "fundador");
        _pasarela.Veredicto = new CobroResuelto(intento.Id, "ref_1", true, intento.Monto, null);

        await _cobros.ConfirmarAsync(new AvisoDePasarela(new Dictionary<string, string>()));

        var cupo = _base.CuposFundador.Single(c => c.PerfilPrestadorId == intento.PerfilPrestadorId);
        Assert.Null(cupo.ReservadoHasta);     // sin fecha = confirmado
    }

    [Fact]
    public async Task Un_aviso_de_un_intento_que_no_existe_no_hace_nada()
    {
        _pasarela.Veredicto = new CobroResuelto(Guid.NewGuid(), "ref_1", true, 7990, null);

        var r = await _cobros.ConfirmarAsync(new AvisoDePasarela(new Dictionary<string, string>()));

        Assert.False(r.Exito);
        Assert.Equal(Fallas.NoEncontrado, r.Codigo);
        Assert.Empty(_base.Pagos);
    }
```

- [ ] **Paso 2: Verlas fallar, decidir y verlas pasar**

`dotnet test pruebas/Zass.Aplicacion.Pruebas --filter "FullyQualifiedName~ConfirmarCobro"`.
Esperado al final: **9 passed**.

- [ ] **Paso 3: Las mutaciones (estas son las que más valen de todo el plan)**

1. Borra el `if (intento.Estado == EstadoIntentoPago.Pagado)` → debe caer
   `El_MISMO_aviso_dos_veces_NO_regala_un_segundo_mes`.
2. Borra la comparación de monto → debe caer `Un_monto_que_no_coincide_NO_extiende_nada`.
3. Mueve `AgregarCanje` a `IniciarAsync` → debe caer
   `Una_tarjeta_rechazada_NO_le_quema_el_cupon_a_nadie`.
4. Borra la llamada a `ConfirmarCupoFundadorAsync` → debe caer `Pagado_el_plan_fundador…`.
5. Haz que `MarcarPagado` devuelva siempre `true` → debe caer alguna de idempotencia. Si no cae
   ninguna, **es un hueco**: escribe la prueba que lo cubra.

- [ ] **Paso 4: Commit**

```bash
git add backend/pruebas/Zass.Aplicacion.Pruebas
git commit -m "Pruebas de confirmar: idempotencia, monto que no coincide, canje y cupo fundador"
```

---

### Task 6: La guarda de producción, de verdad

**Hay un comentario que miente, y es el que más importa.** `PasarelaSimulada.cs:35` dice: «NO se
registra en Producción. AgregarZass se niega a arrancar con este adaptador». **No es cierto:** el
`switch` de `AgregarZass` solo falla con un nombre *desconocido*; si `Zass:Pasarela` no está
puesta, **cae en la simulada por omisión, en cualquier entorno, Producción incluida**. Es
exactamente el fallo que su propio comentario dice que evita.

**Archivos:**
- Modificar: `backend/src/Zass.Infraestructura/Configuracion/AgregarZass.cs`
- Prueba: `backend/pruebas/Zass.Arquitectura.Pruebas/PasarelaEnProduccionPruebas.cs`

- [ ] **Paso 1: Escribir la prueba, que debe fallar**

```csharp
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Zass.Aplicacion.Puertos;
using Zass.Infraestructura.Configuracion;

namespace Zass.Arquitectura.Pruebas;

/// <summary>
/// Cobrar de mentira en produccion es la clase de error que se descubre por el lado de la
/// plata. No alcanza con un comentario: tiene que no arrancar.
/// </summary>
public class PasarelaEnProduccionPruebas
{
    private static IServiceProvider Contenedor(string entorno, string? pasarela)
    {
        var ajustes = new Dictionary<string, string?>
        {
            ["Zass:ClaveDeCifrado"] = new string('x', 44),
            ["ConnectionStrings:Zass"] = "Host=127.0.0.1;Port=5433;Database=zass;Username=zass;Password=x",
        };
        if (pasarela is not null) ajustes["Zass:Pasarela"] = pasarela;

        var configuracion = new ConfigurationBuilder().AddInMemoryCollection(ajustes).Build();
        var servicios = new ServiceCollection();
        servicios.AddSingleton<IHostEnvironment>(new HostingEnvironment { EnvironmentName = entorno });
        servicios.AgregarZass(configuracion);
        return servicios.BuildServiceProvider();
    }

    [Fact]
    public void En_Produccion_y_sin_configurar_la_pasarela_NO_se_resuelve_la_simulada()
    {
        var contenedor = Contenedor("Production", pasarela: null);

        Assert.ThrowsAny<Exception>(() => contenedor.GetRequiredService<IPasarelaDePagos>());
    }

    [Fact]
    public void En_Produccion_pedir_la_simulada_a_proposito_tampoco_se_puede()
    {
        var contenedor = Contenedor("Production", pasarela: "simulada");

        Assert.ThrowsAny<Exception>(() => contenedor.GetRequiredService<IPasarelaDePagos>());
    }

    [Fact]
    public void En_Desarrollo_la_simulada_es_la_de_siempre()
    {
        var contenedor = Contenedor("Development", pasarela: null);

        var pasarela = contenedor.GetRequiredService<IPasarelaDePagos>();

        Assert.Equal("simulada", pasarela.Nombre);
    }
}
```

Ajusta los ajustes mínimos de configuración a los que `AgregarZass` exige de verdad — los
descubres corriendo la prueba y leyendo el error, no adivinando. `HostingEnvironment` está en
`Microsoft.Extensions.Hosting.Internal`.

- [ ] **Paso 2: Verla fallar**

```
cd backend
dotnet test pruebas/Zass.Arquitectura.Pruebas --filter "FullyQualifiedName~PasarelaEnProduccion"
```

Esperado: **caen las dos primeras**, porque hoy la simulada se resuelve igual en Producción.

- [ ] **Paso 3: Arreglarlo**

En el registro de `IPasarelaDePagos` de `AgregarZass.cs`, antes del `switch`:

```csharp
            var entorno = sp.GetRequiredService<IHostEnvironment>();
            if (entorno.IsProduction()
                && nombre.Trim().ToLowerInvariant() == PasarelaSimulada.NombreDeLaPasarela)
            {
                throw new OptionsValidationException(
                    OpcionesZass.Seccion, typeof(OpcionesZass),
                    new[]
                    {
                        $"{ClavePasarela} resuelve a '{PasarelaSimulada.NombreDeLaPasarela}' y el "
                        + "entorno es Produccion. La pasarela simulada no cobra: configura una real.",
                    });
            }
```

Y **corrige el comentario de `PasarelaSimulada.cs:35`** para que diga lo que el código hace ahora,
no lo que prometía.

- [ ] **Paso 4: Verlas pasar y mutar**

Esperado: **3 passed**. Mutación: comenta el `if` nuevo → deben caer las dos primeras. Deshaz.

- [ ] **Paso 5: Commit**

```bash
git add backend/src/Zass.Infraestructura backend/pruebas/Zass.Arquitectura.Pruebas
git commit -m "La pasarela simulada ya no arranca en Produccion: el comentario prometia una guarda que no existia"
```

---

## BLOQUE 3 — La decisión de Pablo: el cupo se cierra al inscribir, no al pagar

### Task 7: La inscripción con cobro $0 y el mes gratis

El esqueleto confirma el cupo cuando se confirma **un pago**. Pablo decidió otra cosa: el cupo se
confirma al **inscribir el medio de pago** (cobro $0, que es lo que hace Oneclick de Transbank), y
el mes gratis corre desde ahí. Esta tarea lo adapta sin romper el camino de cobro, que sigue
existiendo para el día 31 (C3).

**Archivos:**
- Modificar: `backend/src/Zass.Dominio/Suscripciones/Entidades.cs` (`TipoDeIntento` en `IntentoDePago`)
- Modificar: `backend/src/Zass.Aplicacion/Suscripciones/IServicioCobros.cs` y `ServicioCobros.cs`
- Modificar: la migración de la tarea 2 **solo si aún no se aplicó**; si ya se aplicó, **una
  migración nueva** (avisa antes)
- Prueba: `backend/pruebas/Zass.Aplicacion.Pruebas/Suscripciones/InscripcionFundadorPruebas.cs`

**Interfaces:**
- Produce: `Task<Resultado<IntentoDePagoDto>> IniciarInscripcionFundadorAsync(Guid perfilPrestadorId, string claveIdempotencia, CancellationToken ct = default)`;
  `enum TipoDeIntento { Cobro, Inscripcion }`; `ServicioCobros.DiasDeMesGratis = 30`.

- [ ] **Paso 1: Escribir las pruebas, que deben fallar**

```csharp
    [Fact]
    public async Task La_inscripcion_crea_un_intento_de_monto_cero_y_reserva_el_cupo()
    {
        var perfil = UnPrestadorConSuscripcion();

        var r = await _cobros.IniciarInscripcionFundadorAsync(perfil.Id, "k1");

        Assert.True(r.Exito);
        var intento = Assert.Single(_base.IntentosDePago);
        Assert.Equal(0, intento.Monto);
        Assert.Equal(TipoDeIntento.Inscripcion, intento.Tipo);
        Assert.Equal(Ahora.AddMinutes(25), _base.CuposFundador
            .Single(c => c.PerfilPrestadorId == perfil.Id).ReservadoHasta);
    }

    [Fact]
    public async Task Confirmada_la_inscripcion_el_cupo_es_suyo_y_el_mes_gratis_corre()
    {
        var perfil = UnPrestadorConSuscripcion(vigenteHasta: Ahora.AddDays(3));
        await _cobros.IniciarInscripcionFundadorAsync(perfil.Id, "k1");
        var intento = _base.IntentosDePago.Single();
        _pasarela.Veredicto = new CobroResuelto(intento.Id, "ref_1", true, 0, null);

        var r = await _cobros.ConfirmarAsync(new AvisoDePasarela(new Dictionary<string, string>()));

        Assert.True(r.Exito);
        Assert.Null(_base.CuposFundador.Single(c => c.PerfilPrestadorId == perfil.Id).ReservadoHasta);

        var s = _base.Suscripciones.Single(x => x.PerfilPrestadorId == perfil.Id);
        Assert.Equal("fundador", _base.Planes.Single(p => p.Id == s.PlanId).Codigo);
        Assert.Equal(EstadoSuscripcion.Prueba, s.Estado);
        Assert.Equal(Ahora.AddDays(30), s.VigenteHasta);   // candado con literal
        Assert.True(s.EstaVigente(Ahora));                 // aparece en la busqueda desde ya
    }

    [Fact]
    public async Task Una_inscripcion_NO_crea_una_fila_de_pago_porque_no_se_cobro_nada()
    {
        var perfil = UnPrestadorConSuscripcion();
        await _cobros.IniciarInscripcionFundadorAsync(perfil.Id, "k1");
        var intento = _base.IntentosDePago.Single();
        _pasarela.Veredicto = new CobroResuelto(intento.Id, "ref_1", true, 0, null);

        await _cobros.ConfirmarAsync(new AvisoDePasarela(new Dictionary<string, string>()));

        Assert.Empty(_base.Pagos);
    }

    [Fact]
    public async Task RN_S7_07_a_quien_le_quedaban_45_dias_no_se_le_quitan_15()
    {
        var perfil = UnPrestadorConSuscripcion(vigenteHasta: Ahora.AddDays(45));
        await _cobros.IniciarInscripcionFundadorAsync(perfil.Id, "k1");
        var intento = _base.IntentosDePago.Single();
        _pasarela.Veredicto = new CobroResuelto(intento.Id, "ref_1", true, 0, null);

        await _cobros.ConfirmarAsync(new AvisoDePasarela(new Dictionary<string, string>()));

        Assert.Equal(Ahora.AddDays(45),
            _base.Suscripciones.Single(x => x.PerfilPrestadorId == perfil.Id).VigenteHasta);
    }

    [Fact]
    public async Task RN_S7_07_no_nace_una_segunda_suscripcion()
    {
        var perfil = UnPrestadorConSuscripcion();
        await _cobros.IniciarInscripcionFundadorAsync(perfil.Id, "k1");
        var intento = _base.IntentosDePago.Single();
        _pasarela.Veredicto = new CobroResuelto(intento.Id, "ref_1", true, 0, null);

        await _cobros.ConfirmarAsync(new AvisoDePasarela(new Dictionary<string, string>()));

        Assert.Single(_base.Suscripciones.Where(s => s.PerfilPrestadorId == perfil.Id));
    }

    [Fact]
    public async Task La_misma_inscripcion_confirmada_dos_veces_no_estira_el_mes_gratis()
    {
        var perfil = UnPrestadorConSuscripcion();
        await _cobros.IniciarInscripcionFundadorAsync(perfil.Id, "k1");
        var intento = _base.IntentosDePago.Single();
        _pasarela.Veredicto = new CobroResuelto(intento.Id, "ref_1", true, 0, null);
        var aviso = new AvisoDePasarela(new Dictionary<string, string>());

        await _cobros.ConfirmarAsync(aviso);
        var vigencia = _base.Suscripciones.Single(s => s.PerfilPrestadorId == perfil.Id).VigenteHasta;
        await _cobros.ConfirmarAsync(aviso);

        Assert.Equal(vigencia,
            _base.Suscripciones.Single(s => s.PerfilPrestadorId == perfil.Id).VigenteHasta);
    }

    [Fact]
    public async Task Sin_cupos_no_hay_inscripcion()
    {
        foreach (var cupo in _base.CuposFundador)
        {
            cupo.PerfilPrestadorId = Guid.NewGuid();
            cupo.ReservadoHasta = null;
        }
        var perfil = UnPrestadorConSuscripcion();

        var r = await _cobros.IniciarInscripcionFundadorAsync(perfil.Id, "k1");

        Assert.False(r.Exito);
        Assert.Equal(Fallas.Conflicto, r.Codigo);
        Assert.Empty(_base.IntentosDePago);
    }

    [Fact]
    public async Task La_inscripcion_rechazada_libera_el_cupo()
    {
        var perfil = UnPrestadorConSuscripcion();
        await _cobros.IniciarInscripcionFundadorAsync(perfil.Id, "k1");
        var intento = _base.IntentosDePago.Single();
        _pasarela.Veredicto = new CobroResuelto(intento.Id, null, false, null, "No se inscribio");

        await _cobros.ConfirmarAsync(new AvisoDePasarela(new Dictionary<string, string>()));

        Assert.Null(_base.CuposFundador.SingleOrDefault(c => c.PerfilPrestadorId == perfil.Id));
    }
```

- [ ] **Paso 2: Verlas fallar**

```
dotnet test pruebas/Zass.Aplicacion.Pruebas --filter "FullyQualifiedName~InscripcionFundador"
```

Esperado: no compila — no existen `TipoDeIntento` ni `IniciarInscripcionFundadorAsync`.

- [ ] **Paso 3: Implementarlo**

1. En `IntentoDePago`, la columna nueva y su enum:

```csharp
/// <summary>
/// Cobro = se cobra plata. Inscripcion = se autoriza el medio de pago con monto 0, que es
/// lo que confirma el cupo fundador (RN-S7-01) y arranca el mes gratis (RN-S7-03).
/// </summary>
public enum TipoDeIntento { Cobro, Inscripcion }
```

y en la entidad: `public TipoDeIntento Tipo { get; set; } = TipoDeIntento.Cobro;`, mapeada en
`ZassDbContext` con `.HasConversion<string>().HasMaxLength(20)`.

2. `IniciarInscripcionFundadorAsync`: igual que `IniciarAsync` del plan fundador —misma clave de
   idempotencia, misma reserva de cupo, misma llamada a la pasarela— pero con `Monto = 0`,
   `MontoBruto = 0`, `MontoDescuento = 0`, `Meses = 0` y `Tipo = Inscripcion`. **Reusa el código
   que ya existe** en vez de copiarlo: extrae lo común a un método privado.

3. En `ConfirmarAsync`, antes de `RegistrarPeriodoPagadoAsync`, el camino de la inscripción:

```csharp
            if (intento.Tipo == TipoDeIntento.Inscripcion)
            {
                // RN-S7-01: lo que confirma el cupo es la inscripcion, no un pago.
                await _suscripciones.ConfirmarCupoFundadorAsync(intento.PerfilPrestadorId, t);

                // RN-S7-03 y RN-S7-07: plan fundador, mes gratis, y la vigencia en la
                // fecha mas lejana de las dos. No se crea fila de pago: no se cobro nada.
                var cambio = await _suscripciones.PasarAFundadorConMesGratisAsync(
                    intento.SuscripcionId, DiasDeMesGratis, t);
                if (!cambio.Exito)
                    return Resultado.Falla<ConfirmacionDto>(cambio.Codigo!, cambio.Mensaje!);

                await _trabajo.GuardarAsync(t);
                return Resultado.Ok(new ConfirmacionDto(
                    intento.Id, Aprobado: true, intento.Estado.ToString(),
                    cambio.Valor, "Tu cupo de fundador quedó confirmado.", Nueva: true));
            }
```

4. `PasarAFundadorConMesGratisAsync` va en `ServicioSuscripciones` (la vigencia es su regla, no la
   del cobro) y devuelve `Resultado<DateTime>` con la vigencia resultante:

```csharp
    public async Task<Resultado<DateTime>> PasarAFundadorConMesGratisAsync(
        Guid suscripcionId, int diasDeMesGratis, CancellationToken ct = default)
    {
        var s = await _repo.PorIdAsync(suscripcionId, ct);
        if (s is null) return Resultado.Falla<DateTime>(Fallas.NoEncontrado, "No existe esa suscripcion.");

        var plan = await _repo.PlanPorCodigoAsync(PlanFundador, ct);
        if (plan is null || !plan.Activo)
            return Resultado.Falla<DateTime>(Fallas.ReglaDeNegocio, "El plan fundador no esta disponible.");

        var mesGratis = _reloj.Ahora.AddDays(diasDeMesGratis);

        s.PlanId = plan.Id;
        s.Estado = EstadoSuscripcion.Prueba;
        s.CanceladaEn = null;
        // RN-S7-07: ni se quita tiempo ya prometido, ni se regalan 30 dias encima.
        s.VigenteHasta = s.VigenteHasta > mesGratis ? s.VigenteHasta : mesGratis;

        return Resultado.Ok(s.VigenteHasta);
    }
```

5. Y el `MarcarPagado` de un intento de inscripción: sigue marcándose resuelto (es lo que hace
   idempotente el segundo aviso), aunque no haya pago.

- [ ] **Paso 4: Verlas pasar**

Esperado: **8 passed**, y la suite de aplicación completa en verde.

- [ ] **Paso 5: Las mutaciones**

1. Cambia `s.VigenteHasta = ...` por `s.VigenteHasta = mesGratis;` a secas → debe caer
   `RN_S7_07_a_quien_le_quedaban_45_dias_no_se_le_quitan_15`.
2. Borra `ConfirmarCupoFundadorAsync` del camino de inscripción → debe caer
   `Confirmada_la_inscripcion_el_cupo_es_suyo…`.
3. Cambia `DiasDeMesGratis` a 31 → debe caer la misma por el candado con literal.
4. Haz que la inscripción cree fila de pago → debe caer
   `Una_inscripcion_NO_crea_una_fila_de_pago…`.

- [ ] **Paso 6: Commit**

```bash
git add backend/src backend/pruebas
git commit -m "RN-S7-01, 03 y 07: la inscripcion con cobro cero confirma el cupo y arranca el mes gratis"
```

---

## BLOQUE 4 — Lo visible

### Task 8: Los endpoints (API-66 a API-69)

El máximo del documento es **API-65**; compruébalo antes de numerar con
`grep -roh "API-[0-9]\+" docs/ | sort -t- -k2 -n | uniq | tail -3`.

**Archivos:**
- Crear: `backend/src/Zass.Api/Endpoints/EndpointsSuscripciones.cs`
- Modificar: `backend/src/Zass.Api/Program.cs` (una línea junto a los otros `app.Mapear…`)
- Prueba: añadir a la clase de pruebas de endpoints que ya exista en `Zass.Integracion.Pruebas`
  (**léela y reúsala**, no montes otra fábrica)

| Número | Endpoint | Sesión | CSRF | Tasa |
|---|---|---|---|---|
| **API-66** | `GET /api/planes` — planes activos y cupos que quedan | no | no (GET) | `Tasa.Publico` |
| **API-67** | `POST /api/mi/cobros/cotizar` — cuánto se cobraría, con cupón | sí | sí | `Tasa.General` |
| **API-68** | `POST /api/mi/cobros` — inicia cobro o inscripción | sí | sí | `Tasa.Sensible` |
| **API-69** | `POST /api/pagos/aviso` — lo que manda la pasarela | **no** | **no** | `Tasa.General` |

- [ ] **Paso 1: Escribirlos**

El grupo `/api/mi` lleva `grupo.RequireAuthorization()` y `grupo.AddEndpointFilter<FiltroCsrf>()`,
como en `EndpointsCatalogo.cs:58-59`. El perfil **sale de la sesión** vía
`perfiles.MiPerfilPrestadorAsync(usuarioId.Value)`, nunca de un id del cuerpo. Las respuestas, con
`Respuestas.DeResultado(...)`, que ya traduce `Conflicto` a 409 y `ReglaDeNegocio` a 422.

API-69 recibe el aviso y lo pasa tal cual: lee los campos del cuerpo y de la cadena de consulta a
un diccionario y construye `AvisoDePasarela`. **No interpreta nada**: quien traduce es el
adaptador. Y:

- La clave de idempotencia de API-68 llega en la cabecera `Idempotency-Key` (el cliente ya la sabe
  mandar, ver `api/cliente.ts`). Sin ella, `400`.
- API-69 responde `200` también cuando el aviso era un duplicado. Una pasarela que recibe un error
  reintenta para siempre.
- API-69 **no lleva CSRF** porque no hay navegador ni sesión: es una excepción deliberada a la
  regla 8 de `CLAUDE.md`, y queda escrita en el documento S7 (tarea 10).

- [ ] **Paso 2: Las pruebas de integración**

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
    public async Task API_66_no_devuelve_ni_un_dato_de_ninguna_persona()
    {
        var texto = await (await _cliente.GetAsync("/api/planes")).Content.ReadAsStringAsync();

        Assert.DoesNotContain("perfilPrestadorId", texto);
        Assert.DoesNotContain("usuarioId", texto);
        Assert.DoesNotContain("correo", texto);
    }

    [Fact]
    public async Task API_68_sin_sesion_no_inicia_nada()
    {
        var r = await _cliente.PostAsync("/api/mi/cobros", null);

        Assert.True(r.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task API_69_con_un_aviso_que_no_se_puede_leer_no_cambia_nada()
    {
        var antes = await Cupos();

        var r = await _cliente.PostAsync("/api/pagos/aviso",
            new StringContent("{}", Encoding.UTF8, "application/json"));

        Assert.True((int)r.StatusCode >= 400);
        Assert.Equal(antes, await Cupos());
    }
```

- [ ] **Paso 3: Correr integración entera y arreglar el arranque**

```
dotnet test pruebas/Zass.Integracion.Pruebas
```

Si el arranque falla por la pasarela, añade a `appsettings.Development.json` (sin secretos)
`"Zass": { "Pasarela": "simulada" }`.

- [ ] **Paso 4: La mutación**

Quita `.AddEndpointFilter<FiltroCsrf>()` del grupo `/api/mi` y comprueba que cae alguna prueba de
CSRF. Si no cae ninguna, escribe la de API-68: con sesión y sin token CSRF, `403`.

- [ ] **Paso 5: Commit**

```bash
git add backend/src/Zass.Api backend/pruebas/Zass.Integracion.Pruebas
git commit -m "API-66 a API-69: planes, cotizar, iniciar y el aviso de la pasarela"
```

---

### Task 9: La pantalla `/planes`, el cupón y la vuelta del pago

**Archivos:**
- Crear: `frontend/src/funciones/planes/servicio.ts`
- Crear: `frontend/src/paginas/Planes.tsx` y `Planes.prueba.tsx`
- Crear: `frontend/src/paginas/PagoSimulado.tsx`
- Modificar: `frontend/src/App.tsx` (`planes`, `planes/listo`, `pago/inscripcion`)

- [ ] **Paso 1: El servicio**

```ts
import { enviar, obtener } from '../../api/cliente'

export interface Plan {
  id: number
  codigo: string
  nombre: string
  descripcion: string | null
  precioMensual: number
  maxServicios: number
  maxTarjetas: number
  permiteCredenciales: boolean
}

export interface CatalogoDePlanes {
  planes: Plan[]
  cuposFundadorDisponibles: number
}

export interface Cotizacion {
  codigoPlan: string
  nombrePlan: string
  precioMensual: number
  descuento: number
  total: number
  cupon: string | null
  porcentajeDescuento: number | null
  esElPrimerMesQueSePaga: boolean
  detalle: string
}

export interface IntentoDePago {
  id: string
  total: number
  estado: string
  urlParaPagar: string | null
  nueva: boolean
}

export function planes(): Promise<CatalogoDePlanes> {
  return obtener<CatalogoDePlanes>('/api/planes')
}

export function cotizar(codigoPlan: string, codigoCupon?: string): Promise<Cotizacion> {
  return enviar<Cotizacion>('/api/mi/cobros/cotizar', { codigoPlan, codigoCupon })
}

/** La clave de idempotencia la genera el cliente al abrir la pantalla y la REPITE (CC-04). */
export function iniciar(
  codigoPlan: string, clave: string, codigoCupon?: string,
): Promise<IntentoDePago> {
  return enviar<IntentoDePago>(
    '/api/mi/cobros', { codigoPlan, codigoCupon }, 'POST', { 'Idempotency-Key': clave })
}
```

- [ ] **Paso 2: Las pruebas de la pantalla, que deben fallar**

Sigue el estilo de `MisResenas.prueba.tsx` (ayudante `json()`, `vi.stubGlobal('fetch', …)`,
`MemoryRouter`, `Contexto.Provider`, `olvidarCsrf()` en `beforeEach`). Los siete casos:

1. Muestra los dos planes y «quedan 37 de 50».
2. Con `cuposFundadorDisponibles: 0`, el plan fundador aparece **cerrado y sin botón**.
3. Sin sesión, enlace a crear cuenta que **vuelve a `/planes`**.
4. Con sesión pero sin ficha publicada, enlace a publicarla.
5. Un cupón válido muestra los tres números (precio, descuento, total).
6. Un cupón rechazado muestra el motivo **y deja escrito el código** para corregirlo, no lo borra.
7. **Dos clics seguidos en el botón hacen UNA sola llamada** (la clave de idempotencia se repite y
   el botón se deshabilita mientras está en vuelo).

- [ ] **Paso 3: Escribir la pantalla**

Requisitos, todos con prueba arriba:

- Pide API-66 al montar; mientras carga lo dice; si falla lo dice **y ofrece reintentar** (como
  `/mi-ficha` después del defecto D3: una pantalla muda es un defecto, no un estado).
- Precios con `new Intl.NumberFormat('es-CL')`.
- La clave de idempotencia se genera **una vez al montar** con `crypto.randomUUID()` y se reusa en
  cada reintento del mismo envío. Si se genera por clic, no protege de nada.
- Con la respuesta de API-68, manda a `urlParaPagar`.
- Si API-68 responde `409`, vuelve a pedir API-66 y pinta «cerrado»: alguien se llevó el último
  mientras mirabas.

**TEXTOS PROPUESTOS, pendientes de Pablo** (al PR, bajo «Textos nuevos»): «Quiero mi cupo» ·
«Quedan {n} de 50 cupos de fundador» · «Los 50 cupos de fundador ya están tomados. El plan mensual
sigue abierto.» · «Primero publica tu ficha: el cupo se asigna a tu perfil de prestador.» ·
«Tu primer mes es gratis. Después, $5.990 al mes mientras mantengas tu suscripción.» · «No pudimos
abrir el pago. Vuelve a intentarlo.»

- [ ] **Paso 4: La pantalla del pago simulado**

`PagoSimulado.tsx`: dice **con todas las letras que es una pasarela de prueba** —para que nadie la
confunda con un cobro real— y tiene dos botones, «Inscribir mi medio de pago» y «Rechazar», que
mandan el aviso a API-69. Los campos del aviso los arma el adaptador simulado: lee
`PasarelaSimulada.cs` para saber cuáles espera, **no los inventes**.

- [ ] **Paso 5: Las rutas y el enlace**

```tsx
        <Route path="planes" element={<Planes />} />
        <Route path="planes/listo" element={<PlanesListo />} />
        <Route path="pago/inscripcion" element={<PagoSimulado />} />
```

`PlanesListo` consulta `GET /api/mi/suscripcion` y espera a que aparezca el plan fundador. **No
confirma nada por su cuenta**: lo que confirma es el aviso. Y añade el enlace a `/planes` desde el
pie y desde Mi ficha.

- [ ] **Paso 6: Verde, tipos y lint**

```
cd frontend
npx vitest run src/paginas/Planes.prueba.tsx
npm run prueba
npx tsc -b
npx oxlint
```

Esperado: las 7 nuevas en verde, la suite entera en verde (eran 488), `tsc` limpio, 0 avisos
nuevos de lint.

- [ ] **Paso 7: Las mutaciones**

1. Genera la clave de idempotencia dentro del manejador del clic → debe caer la del doble clic.
2. Pinta el botón también con 0 cupos → debe caer la del plan cerrado.
3. Borra el estado de error con reintento → debe caer la de la pantalla muda.

- [ ] **Paso 8: Commit**

```bash
git add frontend/src
git commit -m "Pantalla /planes: dos planes, contador de cupos, cupon y la vuelta del pago"
```

---

### Task 10: El documento rector S7

Aquí la documentación **es** la especificación. Hoy no existe `subsistema-7`, no hay ninguna regla
`RN-S7-`, y `api-rest.md` no numera ningún endpoint de suscripción.

**Archivos** (en `C:\Users\pdani\OneDrive\Desktop\Repositorio ZASS\zass-documentacion`):
- Crear: `docs/01-casos-de-uso/subsistema-7-suscripciones-y-pagos.md`
- Modificar: `docs/.../api-rest.md` (API-66 a API-69) y el modelo de datos (TBL-34, TBL-36 y la
  tabla de canjes)
- Modificar: el índice, para que el documento nuevo no quede huérfano

- [ ] **Paso 1: Las siete reglas**

| Regla | Qué dice |
|---|---|
| RN-S7-01 | El cupo fundador se confirma al **inscribir el medio de pago**, no al reservarlo ni al primer pago |
| RN-S7-02 | La reserva dura **25 minutos**; vencida sin confirmar, el cupo vuelve a estar disponible |
| RN-S7-03 | El mes gratis son **30 días** en estado `Prueba` sobre el plan fundador |
| RN-S7-04 | Confirmar es **idempotente**: el intento ya resuelto no vuelve a extender nada, y el índice único `ux_pago_intento` lo garantiza en la base |
| RN-S7-05 | El monto confirmado **se compara** con el que se fijó en el servidor; si no coincide, no se extiende nada |
| RN-S7-06 | Al llegar a **50** el plan fundador se cierra y no se reabre; quien ya tiene su cupo conserva el precio |
| RN-S7-07 | La inscripción **cambia de plan** la suscripción existente y deja la vigencia en la fecha más lejana; nunca crea una segunda |

Y las dos excepciones que hay que dejar escritas: **API-69 no lleva CSRF** (no hay navegador; la
autenticidad la da el adaptador), y **el cupón descuenta el primer mes que se paga**, que no es el
primer mes de la cuenta, porque el primero es la prueba gratuita.

- [ ] **Paso 2: Regenerar el sitio y comprobar el HTML**

```
cd "C:\Users\pdani\OneDrive\Desktop\Repositorio ZASS\zass-documentacion"
python sitio\construir.py
```

El HTML **nunca** se edita a mano. Y comprueba el resultado como manda la lección del proyecto:
abre el HTML generado y busca el texto de **RN-S7-04 dentro de él**. Que el script no diera error
no prueba que la página tenga el contenido — aquí un error de sintaxis de Mermaid también dibujaba
un SVG, y dos diagramas ER estaban rotos.

- [ ] **Paso 3: Commit**

```bash
git add docs sitio
git commit -m "S7: el documento rector de suscripciones y pagos (RN-S7-01 a 07, API-66 a 69, TBL-34 y TBL-36)"
```

---

### Task 11: El camino completo, contra PostgreSQL y en un navegador

Un verde en memoria no prueba que la base aguante, y un verde en jsdom no es haber visto la
pantalla.

**Archivos:**
- Prueba: añadir a `backend/pruebas/Zass.Integracion.Pruebas/CuposFundadorIntegracionPruebas.cs`
- Crear: `frontend/pruebas-navegador/cupo-fundador.mjs`

- [ ] **Paso 1: La prueba que solo PostgreSQL puede responder**

Dos confirmaciones del **mismo** intento, **a la vez**, contra la base de verdad: una tiene que
quedarse esperando en el bloqueo de fila y, al entrar, encontrar el intento ya resuelto. Resultado
esperado: **un** pago y **una** extensión. Y una segunda prueba que compruebe que el índice
`ux_pago_intento` rechaza el segundo pago si alguien se salta la comprobación en C#.

- [ ] **Paso 2: Levantar todo y recorrerlo**

```
cd C:\Users\pdani\zass-app-1
.\iniciar-postgres.bat
.\ver-zass.bat
cd frontend
node pruebas-navegador/cupo-fundador.mjs
```

Recorrido: entrar con la cuenta de demostración (`demo-zass-2026`) → `/planes` → ver el contador →
«Quiero mi cupo» → pantalla de pago simulado → inscribir → volver → **y comprobar que el prestador
aparece en la búsqueda**. Captura en cada paso, como los otros siete guiones.

- [ ] **Paso 3: Comprobar en la base, no en la pantalla**

```sql
SELECT numero, perfil_prestador_id, reservado_hasta FROM cupos_fundador WHERE perfil_prestador_id IS NOT NULL;
SELECT tipo, monto, estado FROM intentos_pago ORDER BY creado_en DESC LIMIT 3;
SELECT plan_id, estado, vigente_hasta FROM suscripciones WHERE perfil_prestador_id = '<el del demo>';
SELECT count(*) FROM pagos;
```

Esperado: **un** cupo con `reservado_hasta` nulo, **un** intento de tipo `Inscripcion` con monto 0
y estado `Pagado`, **una** suscripción en plan fundador, estado `Prueba`, 30 días, y **ninguna**
fila nueva en `pagos` (no se cobró nada todavía).

- [ ] **Paso 4: Mandar el mismo aviso dos veces, a mano**

Repite la petición a `/api/pagos/aviso` con el mismo cuerpo. Esperado: `200`, y las cuatro
consultas de arriba **sin un solo cambio**.

- [ ] **Paso 5: Commit y PR**

```bash
git add backend/pruebas frontend/pruebas-navegador
git commit -m "El camino completo del cupo fundador, contra PostgreSQL y en un navegador"
```

PR `[N3]` con la plantilla llena: **Mutación** con las veintitantas mutaciones de este plan y qué
cayó en cada una, **Pruebas** con los números de las cuatro suites y el enlace a la corrida verde,
**Migración** con el nombre de la de la tarea 2 y el aviso que se dio, y **Textos nuevos** con la
lista literal.

---

## Qué queda fuera, dicho para que nadie lo busque

- **El cobro del día 31**: C3. El camino de cobro ya existe y está probado por este plan; lo que
  falta es **quién lo dispara** (un trabajo de fondo que cobre a quien se le termina el mes
  gratis). **Fecha límite: 23 de diciembre de 2026**, treinta días después del lanzamiento.
- **La pasarela real** (Transbank, Flow o Mercado Pago): un adaptador más y una línea en
  `AgregarZass`. Lo que el falso no ejercita y habrá que construir: la vuelta del navegador de esa
  pasarela y la consulta de confirmación.
- **Sembrar el cupón del 20 %**: la tabla existe tras la tarea 2 y el código lo aplica, pero nadie
  crea cupones todavía. Decidir si se siembran por migración o se crean a mano.
- **Dar de baja el medio de pago** desde el panel: deuda, va a `ESTADO.md`.
