/**
 * El camino de quien llega buscando y decide ofrecer servicios, en un navegador real: crear la
 * cuenta en la pantalla de verdad, entrar, ver que «Mi agenda» y «Mis citas» le dicen lo que
 * corresponde a quien todavia no ofrece nada, y activar su ficha desde «Ofrecer mis servicios».
 *
 * Aqui el fetch no esta simulado: viajan cookies, token CSRF y HTTPS. Recoge errores de consola
 * y TODA peticion que falle, con su cuerpo.
 *
 * AVISO sobre PostGIS: crear un perfil de prestador escribe en perfiles_prestador.ubicacion_exacta,
 * que es de tipo geography. Si Windows tiene bloqueado postgis-3.dll (Smart App Control), ese
 * INSERT falla con 58P01 y la API responde 500. Este guion lo DISTINGUE de un defecto de la
 * aplicacion y lo dice: no es lo mismo «la pantalla esta rota» que «la maquina no deja cargar
 * la extension».
 *
 * Necesita arriba la API (https://localhost:7277) y Vite (https://localhost:5173).
 *
 *   node pruebas-navegador/mirar-prestador.mjs [carpeta-de-capturas]     (desde frontend/)
 */
import { execFileSync } from 'node:child_process'
import { mkdirSync, readFileSync } from 'node:fs'
import { resolve } from 'node:path'
import { chromium } from 'playwright'

const BASE = 'https://localhost:5173'
const raiz = resolve(import.meta.dirname, '..', '..')
const carpeta = process.argv[2] ?? resolve(raiz, 'registros', 'capturas')
mkdirSync(carpeta, { recursive: true })

const correo = `camino.${Date.now()}@zass.local`
const clave = 'una-clave-bien-larga'

function sql(consulta) {
  return execFileSync(
    resolve(raiz, 'herramientas/pgsql/bin/psql.exe'),
    ['-h', '127.0.0.1', '-p', '5433', '-U', 'zass', '-d', 'zass', '-At', '-c', consulta],
    {
      env: {
        ...process.env,
        PGPASSWORD: readFileSync(resolve(raiz, '.clave-pg.txt'), 'utf8').trim(),
        PGCLIENTENCODING: 'WIN1252',
      },
    },
  ).toString().trim()
}

let fallos = 0
function comprobar(texto, bien, detalle = '') {
  console.log(`${bien ? 'ok  ' : 'MAL '} ${texto}${detalle ? ` — ${detalle}` : ''}`)
  if (!bien) { fallos++; process.exitCode = 1 }
}
function bloqueado(texto, detalle) {
  console.log(`--  ${texto} — BLOQUEADO POR EL ENTORNO, no por la aplicacion: ${detalle}`)
}

const navegador = await chromium.launch()
const contexto = await navegador.newContext({ viewport: { width: 1280, height: 900 }, ignoreHTTPSErrors: true, locale: 'es-CL' })
const pagina = await contexto.newPage()

const errores = []
const fallidas = []
pagina.on('pageerror', (e) => errores.push(`pageerror: ${e.message}`))
pagina.on('console', (m) => { if (m.type() === 'error') errores.push(m.text()) })
pagina.on('requestfailed', (p) => fallidas.push(`ABORTADA ${p.method()} ${p.url()} -> ${p.failure()?.errorText}`))
pagina.on('response', async (r) => {
  if (r.status() < 400) return
  let cuerpo = ''
  try { cuerpo = (await r.text()).slice(0, 250).replace(/\s+/g, ' ') } catch { cuerpo = '(sin cuerpo)' }
  fallidas.push(`${r.status()} ${r.request().method()} ${r.url().replace(BASE, '')} :: ${cuerpo}`)
})

const foto = (n) => pagina.screenshot({ path: resolve(carpeta, `prestador-${n}.png`), fullPage: true })

/** ¿La extension PostGIS se puede cargar en esta maquina? Si no, media aplicacion no es medible. */
function hayPostgis() {
  try { sql('SELECT postgis_version()'); return true } catch { return false }
}
const postgis = hayPostgis()
if (!postgis) console.log('--  AVISO: PostGIS no carga en esta maquina. Lo que escriba geography no se puede medir hoy.\n')

try {
  // --- 1. Crear la cuenta, en la pantalla de verdad ---------------------------------------
  await pagina.goto(`${BASE}/crear-cuenta`, { waitUntil: 'networkidle' })
  await pagina.getByLabel('Nombre').fill('Camilo del Camino')
  await pagina.getByLabel('Correo').fill(correo)
  await pagina.getByLabel('Contraseña').fill(clave)
  await foto('01-crear-cuenta')
  await pagina.getByRole('button', { name: /crear cuenta/i }).click()
  await pagina.getByRole('heading', { name: /revisa tu correo/i }).waitFor({ timeout: 20000 })
  comprobar('crear cuenta responde «Revisa tu correo»', true)
  await foto('02-revisa-correo')

  const roles = sql(`SELECT coalesce(string_agg(r.nombre, ','), '(ninguno)') FROM usuario_roles ur
    JOIN roles r ON r.id = ur.rol_id JOIN usuarios u ON u.id = ur.usuario_id
    WHERE u.correo::text = '${correo}'`)
  comprobar('la cuenta nace solo como buscador', roles === 'Buscador', `roles=${roles}`)

  // --- 2. Sin verificar el correo, no se entra --------------------------------------------
  await pagina.goto(`${BASE}/entrar`, { waitUntil: 'networkidle' })
  await pagina.getByLabel('Correo').fill(correo)
  await pagina.getByLabel('Contraseña').fill(clave)
  await pagina.getByRole('button', { name: /^entrar$/i }).click()
  await pagina.waitForTimeout(3000)
  const dentroSinVerificar = !pagina.url().includes('entrar')
  console.log(`     (con el correo SIN verificar, entrar deja en ${pagina.url().replace(BASE, '')})`)
  await foto('03-entrar-sin-verificar')

  sql(`UPDATE usuarios SET correo_verificado = true WHERE correo::text = '${correo}'`)
  if (!dentroSinVerificar) {
    await pagina.goto(`${BASE}/entrar`, { waitUntil: 'networkidle' })
    await pagina.getByLabel('Correo').fill(correo)
    await pagina.getByLabel('Contraseña').fill(clave)
    await pagina.getByRole('button', { name: /^entrar$/i }).click()
  } else {
    await pagina.goto(`${BASE}/`, { waitUntil: 'networkidle' })
  }
  await pagina.waitForURL((u) => !u.pathname.includes('entrar'), { timeout: 20000 })
  comprobar('quien busca entra y cae en la portada', new URL(pagina.url()).pathname === '/',
    pagina.url().replace(BASE, ''))

  // --- 3. La cookie de sesion no se ve desde JavaScript -----------------------------------
  const enJs = await pagina.evaluate(() => document.cookie)
  const galletas = await contexto.cookies()
  comprobar('ninguna cookie de sesion es visible para document.cookie',
    !/sesion|acceso|refresco/i.test(enJs),
    `document.cookie="${enJs}" | del navegador: ${galletas.map((c) => `${c.name}(httpOnly=${c.httpOnly},secure=${c.secure},sameSite=${c.sameSite})`).join(' ')}`)

  // --- 4. Lo que ve quien todavia no ofrece servicios -------------------------------------
  await pagina.goto(`${BASE}/mi-agenda`, { waitUntil: 'networkidle' })
  await pagina.getByRole('heading', { level: 1 }).waitFor({ timeout: 15000 })
  await foto('04-mi-agenda-sin-ficha')
  // `.first()`: la barra de navegacion tiene otro enlace con el mismo nombre, y sin acotar
  // el localizador Playwright corta por ambiguo y el rojo seria del arnes, no de la pantalla.
  comprobar('Mi agenda explica que es para quien ofrece servicios y ofrece el camino',
    await pagina.locator('main, .ficha-pagina').getByRole('link', { name: /ofrecer mis servicios/i })
      .first().isVisible().catch(() => false))

  // Lo que /mi-ficha le dice a quien todavia no ofrece servicios tiene que concordar con el
  // resto del producto: una sola cuenta sirve para buscar y para ofrecer.
  await pagina.goto(`${BASE}/mi-ficha`, { waitUntil: 'networkidle' })
  await pagina.waitForTimeout(1200)
  const textoFicha = await pagina.locator('.no-encontrada, main').first().innerText().catch(() => '')
  comprobar('/mi-ficha NO manda a crear una segunda cuenta con otro correo',
    !/otro correo/i.test(textoFicha), textoFicha.replace(/\n+/g, ' | ').slice(0, 220))

  await pagina.goto(`${BASE}/mis-citas`, { waitUntil: 'networkidle' })
  await pagina.getByRole('heading', { level: 1 }).waitFor({ timeout: 15000 })
  await foto('05-mis-citas-vacias')
  const textoCitas = await pagina.locator('main, body').first().innerText()
  comprobar('Mis citas carga y dice algo a quien no tiene ninguna',
    /cita|hora/i.test(textoCitas), textoCitas.split('\n').filter(Boolean).slice(1, 4).join(' | '))

  await pagina.goto(`${BASE}/mi-ficha`, { waitUntil: 'networkidle' })
  await pagina.waitForTimeout(1500)
  await foto('06-mi-ficha-sin-ser-prestador')
  console.log(`     (quien no es prestador abre /mi-ficha y queda en ${pagina.url().replace(BASE, '')})`)

  // --- 5. Ofrecer mis servicios -----------------------------------------------------------
  await pagina.goto(`${BASE}/ofrecer-servicios`, { waitUntil: 'networkidle' })
  await pagina.getByRole('heading', { level: 1, name: /ofrece tus servicios/i }).waitFor({ timeout: 15000 })
  await foto('07-ofrecer-servicios')
  comprobar('con sesion, «Ofrecer mis servicios» ofrece el boton de activar',
    await pagina.getByRole('button', { name: /activar mi ficha/i }).isVisible())

  fallidas.length = 0
  await pagina.getByRole('button', { name: /activar mi ficha/i }).click()
  await pagina.waitForTimeout(6000)
  await foto('08-despues-de-activar')
  const activo = pagina.url().includes('mi-ficha')
  const fallo500 = fallidas.find((f) => f.startsWith('500') && f.includes('activar-prestador'))

  if (activo) {
    comprobar('activar la ficha lleva a Mi ficha', true)
    comprobar('la ficha queda creada en la base',
      sql(`SELECT count(*) FROM perfiles_prestador p JOIN usuarios u ON u.id = p.usuario_id
        WHERE u.correo::text = '${correo}'`) === '1')
    await pagina.goto(`${BASE}/mi-agenda`, { waitUntil: 'networkidle' })
    await pagina.getByRole('heading', { level: 1, name: 'Mi agenda' }).waitFor({ timeout: 15000 })
    comprobar('ya como prestador, Mi agenda es la de verdad', true)
    await foto('09-mi-agenda-de-prestador')
  } else if (fallo500 && !postgis) {
    bloqueado('activar la ficha', 'POST /api/auth/activar-prestador responde 500 porque el INSERT en '
      + 'perfiles_prestador toca la columna geography ubicacion_exacta y postgis-3.dll no carga')
    console.log(`     ${fallo500.slice(0, 220)}`)
    const aviso = await pagina.locator('.aviso, [role="alert"]').first().innerText().catch(() => '(no hay aviso)')
    comprobar('cuando activar falla, la pantalla se lo DICE a la persona en castellano',
      aviso !== '(no hay aviso)' && !/An error occurred while processing your request/i.test(aviso),
      `lo que se muestra: «${aviso.replace(/\n+/g, ' ')}»`)
  } else {
    comprobar('activar la ficha lleva a Mi ficha', false, `quedo en ${pagina.url().replace(BASE, '')}`)
  }

  // --- 6. A ancho de telefono --------------------------------------------------------------
  await pagina.setViewportSize({ width: 390, height: 844 })
  for (const [n, ruta] of [['10-movil-mis-citas', '/mis-citas'], ['11-movil-ofrecer', '/ofrecer-servicios'], ['12-movil-mi-agenda', '/mi-agenda']]) {
    await pagina.goto(BASE + ruta, { waitUntil: 'networkidle' })
    await pagina.waitForTimeout(1200)
    const m = await pagina.evaluate(() => ({ doc: document.documentElement.scrollWidth, ven: window.innerWidth }))
    comprobar(`${ruta} a 390 px no desborda`, m.doc <= m.ven + 1, `${m.doc} vs ${m.ven}`)
    await foto(n)
  }

  // --- 7. Salir ----------------------------------------------------------------------------
  await pagina.setViewportSize({ width: 1280, height: 900 })
  await pagina.goto(`${BASE}/`, { waitUntil: 'networkidle' })
  const barra = pagina.getByRole('navigation', { name: 'Principal' }).first()
  await barra.getByRole('button', { name: 'Salir' }).click()
  await barra.getByRole('link', { name: 'Entrar' }).waitFor({ timeout: 15000 })
  await pagina.goto(`${BASE}/mis-citas`, { waitUntil: 'networkidle' })
  comprobar('despues de salir, Mis citas pide entrar',
    await pagina.getByRole('heading', { level: 1, name: 'Entrar' }).isVisible().catch(() => false))

  // El pie de pagina no sabe si hay sesion: se comprueba estando dentro.
  await pagina.goto(`${BASE}/entrar`, { waitUntil: 'networkidle' })
  await pagina.getByLabel('Correo').fill(correo)
  await pagina.getByLabel('Contraseña').fill(clave)
  await pagina.getByRole('button', { name: /^entrar$/i }).click()
  await pagina.waitForURL((u) => !u.pathname.includes('entrar'), { timeout: 20000 })
  const pie = pagina.locator('footer.pie-sitio')
  comprobar('estando dentro, el pie NO ofrece «Entrar» ni «Crear una cuenta»',
    (await pie.getByRole('link', { name: 'Entrar' }).count()) === 0
    && (await pie.getByRole('link', { name: 'Crear una cuenta' }).count()) === 0,
    `en el pie: ${(await pie.innerText()).replace(/\n+/g, ' / ')}`)
  await foto('13-pie-con-sesion')
} catch (e) {
  console.log('MAL  el recorrido se detuvo:', e.message.split('\n').slice(0, 6).join(' | '))
  await foto('99-error')
  process.exitCode = 1
} finally {
  console.log('\n--- errores de consola ---')
  for (const e of new Set(errores)) console.log('  ', e.slice(0, 250))
  console.log('--- peticiones fallidas ---')
  for (const f of new Set(fallidas)) console.log('  ', f.slice(0, 300))
  console.log(`--- comprobaciones en rojo: ${fallos} ---`)
  sql(`DELETE FROM usuarios WHERE correo::text = '${correo}'`)
  console.log('limpieza: cuenta de prueba borrada')
  await navegador.close()
}
