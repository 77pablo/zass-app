/**
 * JQ-7 · El camino del pago, visto en un navegador de verdad.
 *
 * Recorre /planes en sus cuatro estados (sin sesión, sin ficha, puede tomar el cupo, agotado),
 * el cupón, la pasarela SIMULADA (inscribir y rechazar), la reserva vencida, el doble clic y el
 * volver a tomar un cupo ya confirmado. Captura cada pantalla en escritorio y en celular.
 *
 * A diferencia de `cupo-fundador.mjs`, este guion NO toca la base: no lee ninguna clave ni corre
 * psql. Comprueba por la API (`GET /api/planes`, `GET /api/mi/suscripcion`). Cuando hace falta
 * forzar un estado en la base, se DETIENE y dice qué bloque de `datos/camino-del-pago/` hay que
 * correr. Esos bloques los corre una persona, a mano.
 *
 * No sale a ninguna pasarela real: no existe ninguna en el código. Solo habla con ZASS_FRONT
 * (por omisión http://localhost:5173) y con la API local en https://localhost:7277.
 *
 * Tres niveles de resultado:
 *   OK       la comprobación pasó.
 *   DEFECTO  algo del producto está mal. Se captura (DEFECTO-<fase>-<n>.png) y la fase SIGUE.
 *   ERROR    algo inesperado impide seguir (página que no carga, API caída, error de JavaScript,
 *            5xx, no se puede entrar). Se captura y la fase se DETIENE.
 * Al final de cada fase se imprime «Resumen <fase>: N OK · N DEFECTO · N ERROR».
 *
 * Antes, levantar a mano (ver-zass.bat no sirve desde un worktree):
 *   cd C:\Users\joaqu\zass-app ; .\iniciar-postgres.bat
 *   cd C:\Users\joaqu\zass-app-pago\backend\src\Zass.Api ; $env:ASPNETCORE_URLS="https://localhost:7277" ; $env:ASPNETCORE_ENVIRONMENT="Development" ; $env:Zass__Tasa__A="100" ; dotnet run --no-launch-profile
 *   cd C:\Users\joaqu\zass-app-pago\frontend ; npm run dev
 * y correr una vez el bloque datos/camino-del-pago/00-inicio.sql.
 *
 * Uso, desde frontend/:
 *   node pruebas-navegador/camino-del-pago.mjs todo            (todas las fases, con pausas)
 *   node pruebas-navegador/camino-del-pago.mjs <fase>          (una sola)
 *
 * Fases, en este orden: sin-sesion, sin-ficha, cupon, rechazo, vencida (→ vencida-seguir),
 * agotado (→ agotado-seguir → agotado-fin), tomar, retomar, tardia (→ tardia-reservar →
 * tardia-seguir: la confirmación tardía de un cupo que ya tomó otra persona).
 * agotado-pantalla: si 20-ocupar.sql abortó por falta de perfiles, recorre solo la pantalla
 * de agotado interceptando /api/planes, sin tocar la base.
 *
 * Salida: 0 sin ERROR (puede haber DEFECTO) · 1 hubo un ERROR · 2 pausa sin terminal (correr el
 * bloque y seguir con la fase que indica) · 3 el front o la API no responden.
 */
import { mkdirSync, readFileSync, writeFileSync } from 'node:fs'
import { resolve } from 'node:path'
import { createInterface } from 'node:readline/promises'
import { chromium, request } from 'playwright'

const FRONT = (process.env.ZASS_FRONT ?? 'http://localhost:5173').replace(/\/$/, '')
const API = 'https://localhost:7277'
const raiz = resolve(import.meta.dirname, '..', '..')
const carpeta = resolve(raiz, 'registros', 'capturas', 'jq7')
const archivoPendiente = resolve(carpeta, 'pendiente.json')
mkdirSync(carpeta, { recursive: true })

// Cuentas INVENTADAS de la base local. La de Marcela viene del sembrado demo (sembrar-demo.sql);
// la del buscador la crea la fase sin-ficha con un dominio reservado (.test), nunca uno real.
const MARCELA = { correo: 'demo-marcela-soto@zass.local', clave: 'demo-zass-2026', slug: 'marcela-soto' }
const BUSCADOR = { correo: 'buscador-jq7@example.test', clave: 'jq7-buscador-local-2026', nombre: 'Buscador JQ7' }
const CUPON = 'LANZAMIENTO30'
const BLOQUES = 'C:\\Users\\joaqu\\zass-app-pago\\frontend\\pruebas-navegador\\datos\\camino-del-pago'
const PSQL = 'C:\\Users\\joaqu\\zass-app\\herramientas\\pgsql\\bin\\psql.exe'

const ESCRITORIO = { width: 1100, height: 900 }
const CELULAR = { width: 390, height: 844 }
const MARGEN_MINIMO = 16

// ------------------------------------------------------------------ resultados

/** Lo que detiene una fase: un ERROR, o una pausa sin terminal. */
class Parada extends Error {
  constructor(tipo, mensaje) { super(mensaje); this.tipo = tipo }
}

const total = { ok: 0, defecto: 0, error: 0 }
let fase = { nombre: '', ok: 0, defecto: 0, error: 0 }

function ok(texto) {
  console.log(`OK       ${texto}`)
  fase.ok++
}

/**
 * Un DEFECTO del producto: se anota, se captura y la fase sigue. `pagina` es opcional: sin
 * ella no hay captura (por ejemplo, cuando lo que falla es una respuesta de la API).
 */
async function defecto(texto, bien, pagina) {
  if (bien) { ok(texto); return }
  fase.defecto++
  const nombre = `DEFECTO-${fase.nombre}-${fase.defecto}`
  console.log(`DEFECTO  ${texto}${pagina ? `   [${nombre}.png]` : ''}`)
  if (pagina) await capturar(pagina, nombre).catch(() => {})
}

/** Algo que el recorrido necesita para poder seguir. Si no se cumple, ERROR y la fase para. */
async function exigir(texto, bien, pagina) {
  if (bien) { ok(texto); return }
  await fallar(texto, pagina)
}

async function fallar(texto, pagina) {
  fase.error++
  const nombre = `ERROR-${fase.nombre}-${fase.error}`
  console.log(`ERROR    ${texto}${pagina ? `   [${nombre}.png]` : ''}`)
  if (pagina) await capturar(pagina, nombre).catch(() => {})
  throw new Parada('error', texto)
}

function observar(texto) {
  console.log(`OBS      ${texto}`)
}

function titulo(nombre, texto) {
  fase = { nombre, ok: 0, defecto: 0, error: 0 }
  // Cada fase vigila solo sus propias pestañas: lo de la fase anterior ya se informó.
  paginas.length = 0
  console.log(`\n━━━ ${nombre} · ${texto} ━━━`)
}

function resumenDeFase() {
  console.log(`\nResumen ${fase.nombre}: ${fase.ok} OK · ${fase.defecto} DEFECTO · ${fase.error} ERROR`)
  total.ok += fase.ok
  total.defecto += fase.defecto
  total.error += fase.error
}

// ------------------------------------------------------------------ servicios arriba

async function revisarServicios() {
  const ctx = await request.newContext({ ignoreHTTPSErrors: true, timeout: 5000 })
  try {
    try {
      await ctx.get(`${FRONT}/`)
    } catch {
      console.error(`\n✗ El front no responde en ${FRONT}.`)
      console.error('  ¿Está corriendo `npm run dev` en C:\\Users\\joaqu\\zass-app-pago\\frontend?')
      console.error('  Si tu Vite levantó en otra dirección, ponla en ZASS_FRONT.')
      process.exit(3)
    }
    try {
      const r = await ctx.get(`${API}/api/planes`)
      if (!r.ok()) throw new Error(String(r.status()))
    } catch {
      console.error(`\n✗ La API no responde en ${API}.`)
      console.error('  Levántala con:')
      console.error('  cd C:\\Users\\joaqu\\zass-app-pago\\backend\\src\\Zass.Api ; $env:ASPNETCORE_URLS="https://localhost:7277" ;'
        + ' $env:ASPNETCORE_ENVIRONMENT="Development" ; $env:Zass__Tasa__A="100" ; dotnet run --no-launch-profile')
      process.exit(3)
    }
    try {
      const r = await ctx.get(`${FRONT}/api/planes`)
      if (!r.ok()) throw new Error(String(r.status()))
    } catch {
      console.error(`\n✗ El front responde, pero ${FRONT}/api/planes no: el proxy de Vite no alcanza a la API.`)
      process.exit(3)
    }
  } finally {
    await ctx.dispose()
  }
}

// ------------------------------------------------------------------ pausas

/**
 * Se detiene hasta que alguien corra el bloque SQL. Con terminal, espera Enter. Sin terminal
 * (por ejemplo, cuando lo corre otra herramienta), termina con código 2 y dice cómo seguir:
 * nada avanza sin que el bloque se haya corrido.
 */
async function pausa(bloque, archivo, siguiente, nota = '') {
  console.log('\n' + '>'.repeat(78))
  console.log(`>>> CORRE AHORA EL BLOQUE ${bloque}:`)
  console.log(`>>> $env:PGCLIENTENCODING="UTF8"; & "${PSQL}" -h 127.0.0.1 -p 5433 -U zass -d zass -f "${BLOQUES}\\${archivo}"`)
  if (nota !== '') console.log(`>>> ${nota}`)
  if (process.stdin.isTTY) {
    console.log('>>> y aprieta Enter cuando haya terminado (revisa que su SELECT final diga lo esperado).')
    console.log('>'.repeat(78))
    const rl = createInterface({ input: process.stdin, output: process.stdout })
    await rl.question('')
    rl.close()
    return
  }
  console.log(siguiente
    ? `>>> y después sigue con: node pruebas-navegador/camino-del-pago.mjs ${siguiente}`
    : '>>> No hay terminal para esperar Enter: el guion termina aquí.')
  console.log('>'.repeat(78))
  throw new Parada('pausa', bloque)
}

// ------------------------------------------------------------------ navegador

let navegador = null
async function cerrar() {
  if (navegador !== null) await navegador.close()
  navegador = null
}

const paginas = []

async function nuevaPagina() {
  navegador ??= await chromium.launch()
  const contexto = await navegador.newContext({ viewport: ESCRITORIO, ignoreHTTPSErrors: true })
  const pagina = await contexto.newPage()
  pagina.errores = []
  pagina.fallidas = []
  pagina.on('pageerror', (e) => pagina.errores.push(e.message))
  pagina.on('console', (m) => {
    // Los 4xx que la propia pantalla provoca a propósito (cupón inexistente, 409 del plan
    // cerrado) se ven aquí como «Failed to load resource». No son errores de JavaScript.
    if (m.type() === 'error' && !/\b(400|401|404|409|422)\b/.test(m.text())) pagina.errores.push(m.text())
  })
  pagina.on('response', async (r) => {
    if (r.status() < 400) return
    let cuerpo = ''
    try { cuerpo = (await r.text()).slice(0, 200).replace(/\s+/g, ' ') } catch { cuerpo = '(sin cuerpo)' }
    pagina.fallidas.push({ estado: r.status(), linea: `${r.status()} ${r.request().method()} ${r.url().replace(FRONT, '')} :: ${cuerpo}` })
  })
  paginas.push(pagina)
  return pagina
}

/** Escritorio y celular, con las contraseñas tapadas. Nunca se captura la terminal. */
async function capturar(pagina, nombre) {
  const mask = [pagina.locator('input[type="password"]')]
  await pagina.setViewportSize(ESCRITORIO)
  await pagina.screenshot({ path: resolve(carpeta, `${nombre}.png`), fullPage: true, mask })
  await pagina.setViewportSize(CELULAR)
  await pagina.waitForTimeout(300)
  await pagina.screenshot({ path: resolve(carpeta, `${nombre}-celular.png`), fullPage: true, mask })
  await pagina.setViewportSize(ESCRITORIO)
}

/** Errores de JavaScript y 5xx de todas las pestañas de la fase. Los 4xx solo se listan. */
async function vigilar() {
  for (const p of paginas) {
    for (const f of p.fallidas) console.log(`     ${f.estado >= 500 ? '5xx  ' : '4xx  '} ${f.linea}`)
    for (const e of p.errores) console.log(`     JS    ${e}`)
  }
  const cincos = paginas.flatMap((p) => p.fallidas.filter((f) => f.estado >= 500))
  const js = paginas.flatMap((p) => p.errores)
  if (cincos.length > 0) await fallar(`hubo ${cincos.length} respuesta(s) 5xx`, paginas.at(-1))
  if (js.length > 0) await fallar(`hubo ${js.length} error(es) de JavaScript en la consola`, paginas.at(-1))
  ok('sin errores de JavaScript ni respuestas 5xx')
}

async function irA(pagina, ruta) {
  let r
  try {
    r = await pagina.goto(`${FRONT}${ruta}`, { waitUntil: 'networkidle', timeout: 30000 })
  } catch (e) {
    await fallar(`${ruta} no cargó: ${e.message.split('\n')[0]}`, pagina)
  }
  if (r !== null && r !== undefined && r.status() >= 400) await fallar(`${ruta} respondió ${r.status()}`, pagina)
}

async function entrar(pagina, cuenta) {
  await irA(pagina, '/entrar')
  await pagina.getByLabel('Correo').fill(cuenta.correo)
  await pagina.getByLabel('Contraseña').fill(cuenta.clave)
  await pagina.getByRole('button', { name: /^entrar$/i }).click()
  try {
    await pagina.waitForURL((u) => !u.pathname.startsWith('/entrar'), { timeout: 20000 })
  } catch {
    await fallar(`no se pudo entrar como ${cuenta.correo}. Si la contraseña es la correcta, la sospecha es la`
      + ` cookie Secure con el front en ${FRONT}`, pagina)
  }
}

// ------------------------------------------------------------------ la API, desde la página

async function cuposQueQuedan(pagina) {
  return pagina.evaluate(async () => {
    const r = await fetch('/api/planes', { credentials: 'include' })
    return (await r.json()).cuposFundadorDisponibles
  })
}

async function miSuscripcion(pagina) {
  return pagina.evaluate(async () => {
    const r = await fetch('/api/mi/suscripcion', { credentials: 'include' })
    return r.ok ? r.json() : { error: r.status }
  })
}

const resumen = (s) => `${s.plan}|${s.estado}|${s.vigenteHasta}|${s.diasRestantes} días`
const esFundador = (s) => /fundador/i.test(String(s.plan))

/** API-69 con CSRF e Idempotency-Key, como la pantalla. Para forzar el 409 con el plan cerrado. */
async function pedirCupoPorApi(pagina) {
  return pagina.evaluate(async () => {
    const csrf = (await (await fetch('/api/auth/csrf', { credentials: 'include' })).json()).token
    const r = await fetch('/api/mi/suscripcion/fundador', {
      method: 'POST',
      credentials: 'include',
      headers: { 'X-CSRF-Token': csrf, 'Idempotency-Key': crypto.randomUUID() },
    })
    let cuerpo = ''
    try { cuerpo = await r.text() } catch { /* sin cuerpo */ }
    return { estado: r.status, cuerpo: cuerpo.slice(0, 300) }
  })
}

/**
 * Aprieta «Quiero mi cupo» y espera la pasarela simulada. Si la API devuelve una dirección
 * absoluta que no es la del front (Zass:UrlPublica apuntando a otro sitio o a otro esquema), lo
 * dice sin mostrar el valor de la configuración.
 */
async function tomarCupo(pagina, { dobleClic = false } = {}) {
  // Cada respuesta se anota AL LLEGAR, y su cuerpo se lee aparte. Antes se anotaba después de
  // leer el cuerpo, y la función devolvía la lista antes de que terminara: salía vacía y el
  // control del doble clic daba un falso OK («salieron 0 intentos»).
  const respuestas = []
  const lecturas = []
  const oir = (r) => {
    if (!r.url().includes('/api/mi/suscripcion/fundador')) return
    const anotada = { estado: r.status(), cuerpo: null }
    respuestas.push(anotada)
    lecturas.push(r.json().then((c) => { anotada.cuerpo = c }).catch(() => {}))
  }
  pagina.on('response', oir)

  const boton = pagina.getByRole('button', { name: /quiero mi cupo/i })
  await exigir('aparece el botón «Quiero mi cupo»', await boton.isVisible(), pagina)
  if (dobleClic) await boton.dblclick()
  else await boton.click()

  try {
    await pagina.waitForURL(/pago\/inscripcion/, { timeout: 20000 })
  } catch {
    const url = respuestas[0]?.cuerpo?.urlParaPagar
    if (typeof url === 'string' && /^https?:/.test(url) && !url.startsWith(FRONT)) {
      await fallar('la API devolvió una dirección de pago absoluta que NO es la del front. Lo más probable es'
        + ` Zass:UrlPublica (User Secrets) con otro esquema o puerto: ajústala a ${FRONT}. No se muestra su valor`, pagina)
    }
    await fallar(`«Quiero mi cupo» no llevó a la pasarela. Respuestas de API-69: ${JSON.stringify(respuestas.map((r) => r.estado))}`, pagina)
  } finally {
    // Un momento para que llegue la respuesta del segundo clic, si es que hubo uno.
    await pagina.waitForTimeout(1000)
    pagina.off('response', oir)
    await Promise.all(lecturas)
  }
  return respuestas
}

const intentoDe = (url) => new URL(url, FRONT).searchParams.get('intento')

// ------------------------------------------------------------------ /planes, lo que se ve

/**
 * Lo que Joaquín vio a mano el 19-nov y que este guion vigila desde entonces: el margen del
 * contenido, las tildes de los textos que vienen de la base y si el Fundador dice que el primer
 * mes es gratis. Todo es DEFECTO: la fase sigue.
 */
async function revisarAspectoDePlanes(pagina, { conGratis }) {
  for (const [nombre, tam] of [['escritorio', ESCRITORIO], ['celular', CELULAR]]) {
    await pagina.setViewportSize(tam)
    await pagina.waitForTimeout(300)
    const x = async (sel) => (await pagina.locator(sel).first().boundingBox())?.x ?? null
    const h1 = await x('main h1')
    const contador = await x('.planes-cupos')
    const precio = await x('.plan-precio')
    const menu = await x('header .contenedor') ?? await x('header')
    observar(`${nombre}: el menú empieza a ${menu?.toFixed(0)} px; el h1 a ${h1?.toFixed(0)}, el contador a ${contador?.toFixed(0)}, el primer precio a ${precio?.toFixed(0)}`)
    await defecto(`${nombre}: el título «Planes y precios» tiene al menos ${MARGEN_MINIMO} px de margen (tiene ${h1?.toFixed(0)})`,
      h1 !== null && h1 >= MARGEN_MINIMO, pagina)
    if (contador !== null) {
      await defecto(`${nombre}: el contador tiene al menos ${MARGEN_MINIMO} px de margen (tiene ${contador.toFixed(0)})`,
        contador >= MARGEN_MINIMO, pagina)
    }
    if (precio !== null) {
      await defecto(`${nombre}: el precio tiene al menos ${MARGEN_MINIMO} px de margen (tiene ${precio.toFixed(0)})`,
        precio >= MARGEN_MINIMO, pagina)
    }
  }
  await pagina.setViewportSize(ESCRITORIO)

  const texto = await pagina.locator('main').innerText()
  const sinTilde = [...new Set((texto.match(/\b(suscripcion|busquedas?|dias|periodo|cancelacion)\b/gi) ?? []).map((p) => p.toLowerCase()))]
  await defecto(`los textos visibles llevan tilde (sin tilde: ${sinTilde.join(', ') || 'ninguno'})`, sinTilde.length === 0, pagina)

  if (conGratis) {
    const fundador = pagina.locator('.plan-tarjeta--destacada')
    const dice = (await fundador.count()) > 0 && /gratis/i.test(await fundador.innerText())
    await defecto('la tarjeta del Fundador dice que el primer mes es gratis', dice, pagina)
  }
}

// ================================================================== fases

async function sinSesion() {
  titulo('sin-sesion', '/planes sin sesión')
  const pagina = await nuevaPagina()
  await irA(pagina, '/planes')
  await capturar(pagina, '1-planes-sin-sesion')

  const quedan = await cuposQueQuedan(pagina)
  const texto = await pagina.locator('main').innerText()
  await defecto('dice el precio del fundador (5.990)', texto.includes('5.990'), pagina)
  await defecto('dice el precio mensual (7.990)', texto.includes('7.990'), pagina)
  await defecto(`el contador dice «Quedan ${quedan} de 50», lo mismo que /api/planes`,
    new RegExp(`Quedan\\s+${quedan}\\s+de\\s+50`).test(texto), pagina)

  const enlace = pagina.getByRole('link', { name: /crear mi cuenta/i })
  await defecto('ofrece «Crear mi cuenta»', await enlace.isVisible(), pagina)
  await defecto('y no el botón del cupo', !(await pagina.getByRole('button', { name: /quiero mi cupo/i }).isVisible()), pagina)
  await defecto('no muestra el cupón sin sesión', (await pagina.locator('#cupon').count()) === 0, pagina)
  await defecto('«Crear mi cuenta» lleva a /crear-cuenta?volver=/planes',
    (await enlace.getAttribute('href')) === '/crear-cuenta?volver=/planes', pagina)

  await revisarAspectoDePlanes(pagina, { conGratis: true })

  await exigir('«Crear mi cuenta» se puede apretar', await enlace.isVisible(), pagina)
  await enlace.click()
  await pagina.waitForURL(/crear-cuenta/, { timeout: 10000 }).catch(() => {})
  await exigir('y abre /crear-cuenta', pagina.url().includes('/crear-cuenta'), pagina)
  await capturar(pagina, '1-crear-cuenta-desde-planes')
}

async function sinFicha() {
  titulo('sin-ficha', 'con sesión de buscador, sin perfil de prestador')
  const pagina = await nuevaPagina()

  // Se intenta crear la cuenta. Si ya existe de una corrida anterior, el registro puede responder
  // igual (para no decir quién tiene cuenta) o con error: en los dos casos se sigue a entrar.
  await irA(pagina, '/crear-cuenta?volver=/planes')
  await pagina.getByLabel('Nombre').fill(BUSCADOR.nombre)
  await pagina.getByLabel('Correo').fill(BUSCADOR.correo)
  await pagina.getByLabel('Contraseña').fill(BUSCADOR.clave)
  await pagina.getByRole('button', { name: /crear cuenta/i }).click()
  await pagina.waitForTimeout(2000)
  await capturar(pagina, '2-tras-crear-cuenta')
  const tras = await pagina.locator('main').innerText()
  observar(`tras crear la cuenta la pantalla dice: «${tras.replace(/\s+/g, ' ').slice(0, 160)}»`)
  await defecto('tras crear la cuenta desde /planes hay un camino de vuelta a /planes (especificación §8)',
    // La ruta, no la dirección entera: /crear-cuenta?volver=/planes contiene «/planes» y daría un falso OK.
    new URL(pagina.url()).pathname === '/planes' || (await pagina.locator('main a[href="/planes"]').count()) > 0, pagina)

  await entrar(pagina, BUSCADOR)
  observar(`al entrar, el buscador cae en ${new URL(pagina.url()).pathname}`)
  await defecto('al entrar después de crear la cuenta desde /planes, vuelve a /planes',
    new URL(pagina.url()).pathname === '/planes', pagina)

  await irA(pagina, '/planes')
  await capturar(pagina, '2-planes-sin-ficha')
  const publicar = pagina.getByRole('link', { name: /publicar mi ficha primero/i })
  await defecto('ofrece «Publicar mi ficha primero»', await publicar.isVisible(), pagina)
  await defecto('y lleva a /ofrecer-servicios', (await publicar.getAttribute('href').catch(() => null)) === '/ofrecer-servicios', pagina)
  await defecto('no ofrece el botón del cupo', !(await pagina.getByRole('button', { name: /quiero mi cupo/i }).isVisible()), pagina)
  await defecto('no muestra el cupón', (await pagina.locator('#cupon').count()) === 0, pagina)
  await revisarAspectoDePlanes(pagina, { conGratis: true })
}

async function cupon() {
  titulo('cupon', 'LANZAMIENTO30 sobre el plan mensual')
  const pagina = await nuevaPagina()
  await entrar(pagina, MARCELA)
  await irA(pagina, '/planes')

  const campo = pagina.locator('#cupon')
  const aplicar = pagina.getByRole('button', { name: /^aplicar$/i })
  await exigir('con ficha aparece el campo del código de descuento', await campo.isVisible(), pagina)

  const desglose = pagina.locator('.cupon-desglose')
  const error = pagina.locator('.planes-cupon [role="alert"]')

  await campo.fill(CUPON)
  await aplicar.click()
  await desglose.waitFor({ timeout: 10000 }).catch(() => {})
  await capturar(pagina, '3-cupon-valido')
  const d = (await desglose.count()) > 0 ? await desglose.innerText() : ''
  observar(`desglose: «${d.replace(/\s+/g, ' ')}»`)
  await defecto('el desglose dice el precio del plan, 7.990', d.includes('7.990'), pagina)
  await defecto('dice el 30 %', /30\s*%/.test(d), pagina)
  await defecto('descuenta 2.397', d.includes('2.397'), pagina)
  await defecto('y se pagan 5.593', d.includes('5.593'), pagina)
  const hayComoPagar = (await pagina.locator('.planes-cupon button, .planes-cupon a').filter({ hasNotText: /aplicar/i }).count()) > 0
  await defecto('después de aplicar el código hay una forma de pagar el plan con ese descuento', hayComoPagar, pagina)

  await campo.fill('  lanzamiento30 ')
  await aplicar.click()
  await pagina.waitForTimeout(1500)
  await capturar(pagina, '3-cupon-minusculas-espacios')
  await defecto('en minúsculas y con espacios también sirve',
    (await desglose.count()) > 0 && (await desglose.innerText()).includes('5.593'), pagina)

  await campo.fill('NOEXISTE99')
  await aplicar.click()
  await error.waitFor({ timeout: 10000 }).catch(() => {})
  await capturar(pagina, '3-cupon-inexistente')
  const e = (await error.count()) > 0 ? await error.innerText() : ''
  observar(`con un código inexistente dice: «${e}»`)
  await defecto('un código inexistente muestra un error', e.length > 0, pagina)
  await defecto('y no deja el desglose del código anterior a la vista', (await desglose.count()) === 0, pagina)
  await defecto('el código mal escrito NO se borra del campo', (await campo.inputValue()) === 'NOEXISTE99', pagina)
  const estiloError = (await error.count()) > 0
    ? await error.evaluate((n) => { const s = getComputedStyle(n); return `${s.backgroundColor}|${s.borderTopStyle}|${s.color}` })
    : ''
  observar(`estilo del error del cupón (fondo|borde|color): ${estiloError}`)
  await defecto('el error del cupón tiene estilo de aviso (fondo o borde)',
    estiloError !== '' && !/^rgba\(0, 0, 0, 0\)\|none/.test(estiloError), pagina)

  await campo.fill('')
  const antes = pagina.fallidas.length
  await aplicar.click()
  await pagina.waitForTimeout(800)
  await defecto('con el campo vacío no se manda nada', pagina.fallidas.length === antes, pagina)
  await capturar(pagina, '3-cupon-vacio')

  observar('«Esta cuenta ya usó un código» no se puede recorrer: hace falta pagar un mes con el cupón y ninguna pantalla llama a API-68')
}

async function rechazo() {
  titulo('rechazo', '«Simular un rechazo» en la pasarela de prueba')
  const pagina = await nuevaPagina()
  await entrar(pagina, MARCELA)
  await irA(pagina, '/planes')
  const antes = await cuposQueQuedan(pagina)

  await tomarCupo(pagina)
  await capturar(pagina, '4-pasarela-de-prueba')
  const textoPasarela = await pagina.locator('main').innerText()
  await defecto('la pasarela dice que es de PRUEBA', /prueba/i.test(textoPasarela), pagina)
  await defecto('y que no se cobra nada', /no se cobra/i.test(textoPasarela), pagina)

  const aviso = pagina.locator('[role="note"]').first()
  const estilo = (await aviso.count()) > 0
    ? await aviso.evaluate((n) => { const s = getComputedStyle(n); return { fondo: s.backgroundColor, borde: s.borderTopStyle, letra: s.fontSize } })
    : null
  observar(`estilo del aviso de PRUEBA: ${JSON.stringify(estilo)}`)
  await defecto('el aviso «Esta es una pasarela de PRUEBA» se ve como aviso (con fondo o borde)',
    estilo !== null && (estilo.fondo !== 'rgba(0, 0, 0, 0)' || estilo.borde !== 'none'), pagina)
  await revisarMargen(pagina, 'la pasarela de prueba')

  const durante = await cuposQueQuedan(pagina)
  await defecto(`con la reserva el contador baja de ${antes} a ${antes - 1} (fue ${durante})`, durante === antes - 1)

  // Lo que ve otra persona mientras tanto, en otra pestaña.
  const otra = await nuevaPagina()
  await irA(otra, '/planes')
  await capturar(otra, '4-planes-durante-la-reserva')

  await pagina.getByRole('button', { name: /simular un rechazo/i }).click()
  await pagina.waitForURL(/planes\/listo/, { timeout: 20000 }).catch(() => {})
  await capturar(pagina, '4-vuelta-tras-rechazo')
  const vuelta = await pagina.locator('main').innerText()
  observar(`tras el rechazo la persona llega a ${new URL(pagina.url()).pathname} y lee: «${vuelta.replace(/\s+/g, ' ').slice(0, 200)}»`)
  await defecto('tras un RECHAZO la pantalla no dice que el medio de pago quedó inscrito', !/quedó inscrito/i.test(vuelta), pagina)
  await defecto('tras un rechazo la pantalla dice que no se pudo', /no se (pudo|complet)|rechaz/i.test(vuelta), pagina)

  const despues = await cuposQueQuedan(pagina)
  await defecto(`el rechazo devuelve el cupo al tiro: el contador vuelve a ${antes} (fue ${despues})`, despues === antes)

  const s = await miSuscripcion(pagina)
  observar(`suscripción de Marcela tras el rechazo: ${resumen(s)}`)
  await defecto('el rechazo no la pasó al plan fundador', !esFundador(s))
}

/** El margen de cualquier pantalla del camino del pago (pasarela, vuelta). */
async function revisarMargen(pagina, donde) {
  for (const [nombre, tam] of [['escritorio', ESCRITORIO], ['celular', CELULAR]]) {
    await pagina.setViewportSize(tam)
    await pagina.waitForTimeout(300)
    const h1 = (await pagina.locator('main h1').first().boundingBox())?.x ?? null
    await defecto(`${nombre}: en ${donde} el título tiene al menos ${MARGEN_MINIMO} px de margen (tiene ${h1?.toFixed(0)})`,
      h1 !== null && h1 >= MARGEN_MINIMO, pagina)
  }
  await pagina.setViewportSize(ESCRITORIO)
}

async function vencida() {
  titulo('vencida', 'la reserva de 25 minutos, vencida')
  const pagina = await nuevaPagina()
  await entrar(pagina, MARCELA)
  await irA(pagina, '/planes')
  const antes = await cuposQueQuedan(pagina)

  await tomarCupo(pagina)
  await capturar(pagina, '5-pasarela-sin-inscribir')
  const durante = await cuposQueQuedan(pagina)
  await defecto(`con la reserva el contador baja de ${antes} a ${antes - 1} (fue ${durante})`, durante === antes - 1)

  writeFileSync(archivoPendiente, JSON.stringify({ urlPasarela: pagina.url(), antes }))
  await vigilar()
  resumenDeFase()

  await pausa('VENCIDA', '30-vencida.sql', 'vencida-seguir')
  await vencidaSeguir()
}

async function vencidaSeguir() {
  titulo('vencida-seguir', 'después de 30-vencida.sql')
  let pendiente
  try { pendiente = JSON.parse(readFileSync(archivoPendiente, 'utf8')) } catch {
    await fallar('no hay una reserva pendiente de la fase vencida. Corre primero: camino-del-pago.mjs vencida')
  }
  const pagina = await nuevaPagina()
  await entrar(pagina, MARCELA)

  const ahora = await cuposQueQuedan(pagina)
  await defecto(`con la reserva vencida el contador vuelve a ${pendiente.antes} sin que corra el diario (fue ${ahora})`,
    ahora === pendiente.antes)
  await irA(pagina, '/planes')
  await capturar(pagina, '5-planes-con-reserva-vencida')
  await defecto('y la pantalla lo dice',
    new RegExp(`Quedan\\s+${pendiente.antes}\\s+de\\s+50`).test(await pagina.locator('main').innerText()), pagina)

  // ¿Qué pasa si la persona vuelve a la pestaña de la pasarela, 25 minutos después, y aprieta
  // «Inscribir»? RN-S7-02 dice que el cupo vuelve a estar disponible; no dice qué pasa con un
  // aviso que llega tarde. Se observa, no se juzga.
  const avisos = []
  pagina.on('response', async (r) => {
    if (r.url().includes('/api/pagos/aviso')) {
      let cuerpo = ''
      try { cuerpo = (await r.text()).slice(0, 200) } catch { /* */ }
      avisos.push(`${r.status()} ${cuerpo}`)
    }
  })
  await pagina.goto(pendiente.urlPasarela, { waitUntil: 'networkidle' })
  await pagina.getByRole('button', { name: /inscribir mi medio de pago/i }).click()
  await pagina.waitForTimeout(2500)
  await capturar(pagina, '5-inscribir-tras-vencer')
  observar(`inscribir con la reserva vencida: el aviso respondió ${avisos.join(' | ') || '(nada)'}`)
  const s = await miSuscripcion(pagina)
  observar(`suscripción de Marcela tras inscribir tarde: ${resumen(s)}`)
  observar(`contador tras inscribir tarde: ${await cuposQueQuedan(pagina)}`)
  await vigilar()
  resumenDeFase()

  await pausa('REINICIO-MARCELA', '10-reinicio-marcela.sql', 'agotado')
}

async function agotado() {
  titulo('agotado', 'los 50 cupos tomados')
  resumenDeFase()
  await pausa('OCUPAR', '20-ocupar.sql', 'agotado-seguir')
  await agotadoSeguir()
}

async function agotadoSeguir() {
  titulo('agotado-seguir', 'después de 20-ocupar.sql')
  const pagina = await nuevaPagina()
  await irA(pagina, '/planes')
  const quedan = await cuposQueQuedan(pagina)
  await exigir(`/api/planes dice 0 cupos (dice ${quedan}). Si 20-ocupar abortó por falta de perfiles, usa agotado-pantalla`,
    quedan === 0, pagina)
  await revisarAgotado(pagina, '6')

  await entrar(pagina, MARCELA)
  await irA(pagina, '/planes')
  await capturar(pagina, '6-agotado-con-sesion')
  await defecto('con sesión de prestadora tampoco hay botón del cupo',
    !(await pagina.getByRole('button', { name: /quiero mi cupo/i }).isVisible()), pagina)

  const r = await pedirCupoPorApi(pagina)
  observar(`API-69 con el plan cerrado respondió ${r.estado}: ${r.cuerpo.replace(/\s+/g, ' ')}`)
  await defecto('API-69 con el plan cerrado responde 409', r.estado === 409)
  const mensual = pagina.locator('.plan-tarjeta').filter({ hasText: /mensual/i })
  const conBoton = (await mensual.locator('button, a.boton').count()) > 0
  await defecto('el 409 dice «Puedes suscribirte al plan mensual»: la tarjeta del mensual tiene cómo hacerlo', conBoton, pagina)
  await vigilar()
  resumenDeFase()

  await pausa('DEVOLVER', '21-devolver.sql', 'agotado-fin')
  await agotadoFin()
}

async function agotadoFin() {
  titulo('agotado-fin', 'después de 21-devolver.sql')
  const pagina = await nuevaPagina()
  await irA(pagina, '/planes')
  const quedan = await cuposQueQuedan(pagina)
  await exigir(`los cupos volvieron (quedan ${quedan})`, quedan > 0, pagina)
  await capturar(pagina, '6-planes-tras-devolver')
}

/** Sin tocar la base: se intercepta /api/planes para ver solo la pantalla de agotado. */
async function agotadoPantalla() {
  titulo('agotado-pantalla', 'solo la pantalla, con /api/planes interceptado (sin base)')
  const pagina = await nuevaPagina()
  await pagina.route('**/api/planes', async (ruta) => {
    const r = await ruta.fetch()
    const cuerpo = await r.json()
    await ruta.fulfill({ response: r, json: { ...cuerpo, cuposFundadorDisponibles: 0 } })
  })
  await irA(pagina, '/planes')
  await revisarAgotado(pagina, '6p')
  observar('el 409 real de API-69 NO se recorrió: 20-ocupar.sql no tenía perfiles suficientes')
}

async function revisarAgotado(pagina, prefijo) {
  await capturar(pagina, `${prefijo}-agotado-sin-sesion`)
  const texto = await pagina.locator('main').innerText()
  await defecto('dice que los 50 cupos de fundador ya están tomados', /Los 50 cupos de fundador ya están tomados/i.test(texto), pagina)
  await defecto('la tarjeta del fundador dice «Cerrado»', /Cerrado: ya no quedan cupos/i.test(texto), pagina)
  await defecto('sin sesión, el fundador cerrado ya no ofrece «Crear mi cuenta»',
    (await pagina.locator('.plan-tarjeta--destacada').getByRole('link', { name: /crear mi cuenta/i }).count()) === 0, pagina)
  await defecto('el plan mensual sigue a la vista', /7\.990/.test(texto), pagina)
}

async function tomar() {
  titulo('tomar', 'doble clic, inscribir y aparecer en la búsqueda')
  const pagina = await nuevaPagina()
  await entrar(pagina, MARCELA)
  await irA(pagina, '/planes')
  await capturar(pagina, '7-planes-con-sesion')
  await revisarAspectoDePlanes(pagina, { conGratis: true })
  const antes = await cuposQueQuedan(pagina)

  const respuestas = await tomarCupo(pagina, { dobleClic: true })
  const intentos = new Set(respuestas.map((r) => r.cuerpo?.urlParaPagar).filter(Boolean).map(intentoDe))
  observar(`el doble clic mandó ${respuestas.length} petición(es) a API-69`)
  await exigir('se vio al menos una respuesta de API-69 (si no, este control no mide nada)', respuestas.length > 0, pagina)
  await defecto(`el doble clic deja UN solo intento (salieron ${intentos.size})`, intentos.size === 1)
  await defecto('y el intento de la pasarela es ese', intentos.has(intentoDe(pagina.url())))
  const durante = await cuposQueQuedan(pagina)
  await defecto(`el doble clic toma UN solo cupo: de ${antes} a ${antes - 1} (fue ${durante})`, durante === antes - 1)

  await pagina.getByRole('button', { name: /inscribir mi medio de pago/i }).click()
  try {
    await pagina.waitForURL(/planes\/listo/, { timeout: 20000 })
  } catch {
    await fallar('«Inscribir mi medio de pago» no llevó a /planes/listo', pagina)
  }
  await capturar(pagina, '7-planes-listo')
  await revisarMargen(pagina, '/planes/listo')

  // La vuelta NO confirma nada; lo que confirma es el aviso. Se le da un momento.
  await pagina.waitForTimeout(1500)
  const s = await miSuscripcion(pagina)
  observar(`suscripción tras inscribir: ${resumen(s)}`)
  await defecto('quedó en el plan fundador', esFundador(s))
  await defecto('en estado Prueba', s.estado === 'Prueba')
  await defecto(`con 30 días (dice ${s.diasRestantes})`, s.diasRestantes === 30)
  await defecto(`el contador quedó en ${antes - 1}`, (await cuposQueQuedan(pagina)) === antes - 1)

  await irA(pagina, '/mi-ficha')
  await pagina.waitForTimeout(1000)
  await capturar(pagina, '7-mi-ficha-fundador')

  await irA(pagina, '/buscar?q=corredora')
  await pagina.waitForTimeout(1500)
  await capturar(pagina, '7-fundador-en-la-busqueda')
  await defecto('aparece en la búsqueda', (await pagina.locator(`a[href="/prestadores/${MARCELA.slug}"]`).count()) > 0, pagina)
}

async function retomar() {
  titulo('retomar', 'volver a /planes con el cupo ya confirmado')
  const pagina = await nuevaPagina()
  await entrar(pagina, MARCELA)
  const antes = await miSuscripcion(pagina)
  await exigir(`Marcela tiene el cupo confirmado (si no, corre antes la fase tomar): ${resumen(antes)}`, esFundador(antes), pagina)
  const cuposAntes = await cuposQueQuedan(pagina)
  await irA(pagina, '/planes')
  await capturar(pagina, '8-planes-ya-fundadora')

  const boton = pagina.getByRole('button', { name: /quiero mi cupo/i })
  const loOfrece = await boton.isVisible()
  await defecto('a quien ya es fundadora no le vuelve a ofrecer «Quiero mi cupo»', !loOfrece, pagina)

  if (loOfrece) {
    await tomarCupo(pagina)
    await capturar(pagina, '8-pasarela-por-segunda-vez')
    await pagina.getByRole('button', { name: /inscribir mi medio de pago/i }).click()
    await pagina.waitForURL(/planes\/listo/, { timeout: 20000 }).catch(() => {})
    await pagina.waitForTimeout(1500)
    const despues = await miSuscripcion(pagina)
    observar(`antes:   ${resumen(antes)}`)
    observar(`después: ${resumen(despues)}`)
    await defecto('volver a tomar el cupo no cambia la vigencia', despues.vigenteHasta === antes.vigenteHasta)
    await defecto('ni el estado', despues.estado === antes.estado)
    await defecto('ni el contador', (await cuposQueQuedan(pagina)) === cuposAntes)
  }
  await vigilar()
  resumenDeFase()

  await pausa('REINICIO-MARCELA', '10-reinicio-marcela.sql', null)
}

// ------------------------------------------------------------------ la confirmación tardía

/**
 * El caso de los «51 fundadores» (pedido de Joaquín, 19-nov). La reserva de Marcela vence,
 * Cristóbal se lleva ESE cupo (TomarCupoLibreAsync cuenta las reservas vencidas como libres) y
 * después llega el aviso tardío de Marcela. Si ServicioCobros.cs:732 no mira que ya no tiene
 * cupo, Marcela queda como fundadora sin cupo y hay más fundadores que cupos.
 */
const CRISTOBAL = { correo: 'demo-cristobal-henriquez@zass.local', clave: 'demo-zass-2026', slug: 'cristobal-henriquez' }
const archivoTardia = resolve(carpeta, 'pendiente-tardia.json')

async function tardia() {
  titulo('tardia', 'la confirmación tardía de un cupo que ya tomó otra persona')
  resumenDeFase()
  // Antes de tocar a Cristóbal: el bloque guarda sus valores exactos y aborta si no puede empezar.
  await pausa('TARDIA-ANTES', '40-tardia-antes.sql', 'tardia-reservar',
    'Si el bloque ABORTA («DETÉN LA FASE TARDIA»), no sigas: avísame. Si no, GUARDA la línea «linea_para_51».')
  await tardiaReservar()
}

async function tardiaReservar() {
  titulo('tardia-reservar', 'Marcela reserva y no inscribe')
  const cristobal = await nuevaPagina()
  await entrar(cristobal, CRISTOBAL)
  const sc = await miSuscripcion(cristobal)
  observar(`Cristóbal antes: ${resumen(sc)}`)
  await exigir('Cristóbal está en Prueba y no es fundador', sc.estado === 'Prueba' && !esFundador(sc), cristobal)

  const marcela = await nuevaPagina()
  await entrar(marcela, MARCELA)
  const sm = await miSuscripcion(marcela)
  observar(`Marcela antes: ${resumen(sm)}`)
  await exigir('Marcela está en Prueba y no es fundadora', sm.estado === 'Prueba' && !esFundador(sm), marcela)

  await irA(marcela, '/planes')
  const antes = await cuposQueQuedan(marcela)
  await tomarCupo(marcela)
  await capturar(marcela, '9-marcela-reserva-sin-inscribir')
  await defecto(`con la reserva de Marcela el contador baja de ${antes} a ${antes - 1}`, (await cuposQueQuedan(marcela)) === antes - 1)

  writeFileSync(archivoTardia, JSON.stringify({ urlPasarela: marcela.url(), antes }))
  await vigilar()
  resumenDeFase()

  await pausa('VENCIDA', '30-vencida.sql', 'tardia-seguir')
  await tardiaSeguir()
}

async function tardiaSeguir() {
  titulo('tardia-seguir', 'Cristóbal toma el cupo vencido y después llega el aviso de Marcela')
  let pendiente
  try { pendiente = JSON.parse(readFileSync(archivoTardia, 'utf8')) } catch {
    await fallar('no hay una reserva pendiente de la fase tardia. Corre primero: camino-del-pago.mjs tardia')
  }

  const cristobal = await nuevaPagina()
  await entrar(cristobal, CRISTOBAL)
  await irA(cristobal, '/planes')
  const libres = await cuposQueQuedan(cristobal)
  await exigir(`la reserva de Marcela ya cuenta como libre: el contador volvió a ${pendiente.antes} (dice ${libres})`,
    libres === pendiente.antes, cristobal)

  await tomarCupo(cristobal)
  await cristobal.getByRole('button', { name: /inscribir mi medio de pago/i }).click()
  try {
    await cristobal.waitForURL(/planes\/listo/, { timeout: 20000 })
  } catch {
    await fallar('la inscripción de Cristóbal no llevó a /planes/listo', cristobal)
  }
  await cristobal.waitForTimeout(1500)
  const sc = await miSuscripcion(cristobal)
  observar(`Cristóbal tras inscribir: ${resumen(sc)}`)
  await exigir('Cristóbal quedó como fundador (si no, el caso no se puede armar)', esFundador(sc), cristobal)
  const conCristobal = await cuposQueQuedan(cristobal)
  observar(`contador con Cristóbal confirmado: ${conCristobal}`)

  // Ahora el aviso tardío de Marcela, desde su pestaña vieja de la pasarela.
  const marcela = await nuevaPagina()
  await entrar(marcela, MARCELA)
  const avisos = []
  marcela.on('response', async (r) => {
    if (!r.url().includes('/api/pagos/aviso')) return
    let cuerpo = ''
    try { cuerpo = (await r.text()).slice(0, 220) } catch { /* */ }
    avisos.push(`${r.status()} ${cuerpo}`)
  })
  await marcela.goto(pendiente.urlPasarela, { waitUntil: 'networkidle' })
  await marcela.getByRole('button', { name: /inscribir mi medio de pago/i }).click()
  await marcela.waitForTimeout(2500)
  await capturar(marcela, '9-marcela-inscribe-tarde')
  observar(`aviso tardío de Marcela: ${avisos.join(' | ') || '(no se vio respuesta)'}`)

  const sm = await miSuscripcion(marcela)
  observar(`Marcela tras el aviso tardío: ${resumen(sm)}`)
  observar(`contador tras el aviso tardío: ${await cuposQueQuedan(marcela)}`)
  await defecto('POSIBLE DEFECTO, prioridad alta: un aviso tardío, de un cupo que ya tomó otra persona, '
    + 'no deja a Marcela como fundadora', !esFundador(sm), marcela)

  await vigilar()
  resumenDeFase()

  await pausa('TARDIA-REVISAR', '50-tardia-revisar.sql', null,
    'Después corre 51-reinicio-tardia.sql con la línea «linea_para_51» que imprimió 40-tardia-antes.sql.')
}

// ================================================================== main

const FASES = {
  'sin-sesion': sinSesion,
  'sin-ficha': sinFicha,
  cupon,
  rechazo,
  vencida,
  'vencida-seguir': vencidaSeguir,
  agotado,
  'agotado-seguir': agotadoSeguir,
  'agotado-fin': agotadoFin,
  'agotado-pantalla': agotadoPantalla,
  tomar,
  retomar,
  tardia,
  'tardia-reservar': tardiaReservar,
  'tardia-seguir': tardiaSeguir,
}
// Estas fases ya llaman a vigilar() y resumenDeFase() por dentro, antes de cada pausa.
const SE_RESUMEN_SOLAS = new Set(['vencida', 'vencida-seguir', 'agotado', 'agotado-seguir', 'retomar',
  'tardia', 'tardia-reservar', 'tardia-seguir'])
// vencida, agotado y tardia ya encadenan sus continuaciones.
const TODO = ['sin-sesion', 'sin-ficha', 'cupon', 'rechazo', 'vencida', 'agotado', 'tomar', 'retomar', 'tardia']

const pedida = process.argv[2]
if (pedida === undefined || (pedida !== 'todo' && !(pedida in FASES))) {
  console.error(`Uso: node pruebas-navegador/camino-del-pago.mjs <todo|${Object.keys(FASES).join('|')}>`)
  process.exit(1)
}

console.log(`Front ${FRONT} · API ${API}`)
await revisarServicios()

let salida = 0
try {
  for (const nombre of pedida === 'todo' ? TODO : [pedida]) {
    await FASES[nombre]()
    if (!SE_RESUMEN_SOLAS.has(nombre)) {
      await vigilar()
      resumenDeFase()
    }
  }
} catch (e) {
  if (e instanceof Parada && e.tipo === 'pausa') {
    salida = 2
  } else {
    if (!(e instanceof Parada)) {
      // Una excepción que nadie esperaba (un selector que no existe, un tiempo agotado): ERROR.
      fase.error++
      console.log(`ERROR    inesperado: ${e.message.split('\n')[0]}`)
      if (paginas.length > 0) await capturar(paginas.at(-1), `ERROR-${fase.nombre}-inesperado`).catch(() => {})
    }
    resumenDeFase()
    salida = 1
  }
} finally {
  await cerrar()
}

console.log(`\nTotal: ${total.ok} OK · ${total.defecto} DEFECTO · ${total.error} ERROR. Capturas en ${carpeta}`)
if (salida === 0 && pedida === 'retomar') {
  console.log(`Al terminar todas las fases, corre ${BLOQUES}\\99-estado-final.sql y compáralo con el de 00-inicio.`)
}
process.exit(salida)
