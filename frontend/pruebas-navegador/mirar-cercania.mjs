/**
 * La busqueda «Mas cerca» con una ubicacion de verdad concedida en el navegador. Es lo unico
 * que puede decir si el permiso de geolocalizacion, el orden por distancia y el control de
 * radio funcionan: en jsdom no hay `navigator.geolocation` ni permisos que conceder.
 *
 * Comprueba ademas si la pantalla ofrece elegir hasta que distancia buscar. ESTADO.md da por
 * arreglado el flujo 4B de CU-08 («ampliar el radio»); esto lo mide contra la pantalla de hoy.
 *
 * Necesita arriba la API (https://localhost:7277) y Vite (https://localhost:5173), y los datos
 * de pruebas-navegador/datos/sembrar-demo.sql.
 *
 *   node pruebas-navegador/mirar-cercania.mjs [carpeta-de-capturas]     (desde frontend/)
 */
import { mkdirSync } from 'node:fs'
import { resolve } from 'node:path'
import { chromium } from 'playwright'

const BASE = 'https://localhost:5173'
const carpeta = process.argv[2] ?? resolve(import.meta.dirname, '..', '..', 'registros', 'capturas')
mkdirSync(carpeta, { recursive: true })

/** El centro de Temuco, que es donde vive la gente de sembrar-demo.sql. */
const TEMUCO = { latitude: -38.7359, longitude: -72.5904 }

let fallos = 0
function comprobar(texto, bien, detalle = '') {
  console.log(`${bien ? 'ok  ' : 'MAL '} ${texto}${detalle ? ` — ${detalle}` : ''}`)
  if (!bien) { fallos++; process.exitCode = 1 }
}

const errores = []
const fallidas = []
const navegador = await chromium.launch()
const contexto = await navegador.newContext({
  ignoreHTTPSErrors: true,
  viewport: { width: 1280, height: 900 },
  locale: 'es-CL',
  geolocation: TEMUCO,
  permissions: ['geolocation'],
})
const pagina = await contexto.newPage()
pagina.on('pageerror', (e) => errores.push(`pageerror: ${e.message}`))
pagina.on('console', (m) => { if (m.type() === 'error') errores.push(m.text()) })
pagina.on('requestfailed', (p) => fallidas.push(`ABORTADA ${p.method()} ${p.url()} -> ${p.failure()?.errorText}`))
const urlsBuscar = []
pagina.on('response', async (r) => {
  if (r.url().includes('/api/buscar')) urlsBuscar.push(`${r.status()} ${r.url().replace(BASE, '')}`)
  if (r.status() < 400) return
  let cuerpo = ''
  try { cuerpo = (await r.text()).slice(0, 250).replace(/\s+/g, ' ') } catch { cuerpo = '(sin cuerpo)' }
  fallidas.push(`${r.status()} ${r.request().method()} ${r.url().replace(BASE, '')} :: ${cuerpo}`)
})
const foto = (n) => pagina.screenshot({ path: resolve(carpeta, `cercania-${n}.png`), fullPage: true })

try {
  await pagina.goto(`${BASE}/buscar?q=abogado`, { waitUntil: 'networkidle' })
  await pagina.locator('.tarjeta-persona').first().waitFor({ timeout: 20000 })
  const antes = await pagina.locator('.tarjeta-persona').count()
  comprobar('la busqueda por texto devuelve gente', antes > 0, `resultados=${antes}`)
  await foto('01-por-texto')

  // --- «Mas cerca»: el navegador concede la ubicacion -------------------------------------
  urlsBuscar.length = 0
  await pagina.getByRole('button', { name: /Más cerca/i }).click()
  await pagina.waitForTimeout(4000)
  await foto('02-mas-cerca')

  const conCerca = urlsBuscar.filter((u) => u.includes('cerca=1') || u.includes('lat'))
  comprobar('al pedir «Más cerca» la busqueda viaja con la ubicacion',
    conCerca.length > 0, urlsBuscar.join(' | ') || '(ninguna llamada a /api/buscar)')

  const despues = await pagina.locator('.tarjeta-persona').count()
  comprobar('«Más cerca» sigue devolviendo gente', despues > 0, `resultados=${despues}`)

  // Con ubicacion, cada tarjeta deberia decir a que distancia esta.
  const conDistancia = await pagina.getByText(/\ba \d+(,\d+)? (km|m)\b/).count()
  comprobar('con la ubicacion, los resultados dicen a que distancia esta cada persona',
    conDistancia > 0, `tarjetas con distancia=${conDistancia}`)

  // --- ¿Se puede elegir hasta que distancia buscar? ----------------------------------------
  const textoPagina = await pagina.locator('body').innerText()
  const hayControlDeRadio = /hasta qué distancia|hasta que distancia/i.test(textoPagina)
    || (await pagina.getByRole('button', { name: /\b(5|10|20|50)\s*km\b/ }).count()) > 0
  comprobar('la pantalla deja elegir hasta que distancia buscar (flujo 4B de CU-08)',
    hayControlDeRadio,
    hayControlDeRadio ? '' : 'no aparece «¿Hasta qué distancia?» ni ningun boton de km')
  // Antes esto solo miraba si ALGUNA llamada llevaba `radio=`, y por omision ninguna lo lleva: la
  // barra de busqueda es nacional y solo limita la distancia cuando se le pide. Asi que ahora se
  // PULSA el control y se mira si el radio llega a la API, que es lo que de verdad importa.
  urlsBuscar.length = 0
  await pagina.getByRole('button', { name: /^20 km$/ }).click()
  await pagina.waitForTimeout(3000)
  await foto('02b-radio-20km')
  const mandaRadio = urlsBuscar.some((u) => u.includes('radio=20'))
  comprobar('al elegir 20 km, la busqueda lo manda a la API', mandaRadio,
    mandaRadio ? '' : `ninguna llamada lleva radio=20: ${urlsBuscar.join(' | ')}`)

  const marcado = await pagina.getByRole('button', { name: /^20 km$/ }).getAttribute('aria-pressed')
  comprobar('el boton de 20 km queda marcado', marcado === 'true', `aria-pressed=${marcado}`)

  const enLaUrl = new URL(pagina.url())
  comprobar('el radio queda en la direccion, para poder compartir la busqueda',
    enLaUrl.searchParams.get('radio') === '20', `url=${enLaUrl.search}`)
  comprobar('la ubicacion NO queda en la direccion',
    !/lat|lon|-38|-72/i.test(enLaUrl.search), `url=${enLaUrl.search}`)

  // --- Sin resultados y con ubicacion: ¿se ofrece ampliar? ----------------------------------
  // SIN recargar: la ubicacion vive solo en una variable del modulo (marco/ubicacion.ts), a
  // proposito, para que no viaje en una URL compartida. Un pagina.goto() la borraria y la
  // pantalla caeria a «Mas recomendados»: se estaria midiendo el arnes, no la aplicacion.
  // Con un radio chico puesto: es el caso del flujo 4B, «no hay nadie TAN cerca, amplia».
  urlsBuscar.length = 0
  await pagina.getByRole('button', { name: /^5 km$/ }).click()
  await pagina.waitForTimeout(2000)
  const caja = pagina.getByRole('searchbox').or(pagina.getByRole('combobox')).first()
  await caja.fill('zzzznoexiste')
  await pagina.getByRole('button', { name: /^Buscar$/ }).click()
  await pagina.waitForTimeout(3000)
  await foto('03-sin-resultados-con-ubicacion')
  const siguePorCercania = urlsBuscar.some((u) => u.includes('lat='))
  comprobar('al buscar de nuevo sin recargar, se conserva la busqueda por cercania',
    siguePorCercania, urlsBuscar.join(' | ') || '(ninguna llamada)')
  const textoVacio = await pagina.locator('body').innerText()
  comprobar('sin resultados y con ubicacion se ofrece ampliar la busqueda',
    /ampliar/i.test(textoVacio),
    textoVacio.split('\n').filter(Boolean).slice(3, 9).join(' | '))

  // --- A ancho de telefono ------------------------------------------------------------------
  await pagina.setViewportSize({ width: 390, height: 844 })
  await pagina.goto(`${BASE}/buscar?q=abogado&cerca=1&orden=Cercania`, { waitUntil: 'networkidle' })
  await pagina.waitForTimeout(2500)
  const m = await pagina.evaluate(() => ({ doc: document.documentElement.scrollWidth, ven: window.innerWidth }))
  comprobar('«Más cerca» a 390 px no desborda', m.doc <= m.ven + 1, `${m.doc} vs ${m.ven}`)
  await foto('04-movil-mas-cerca')
} catch (e) {
  console.log('MAL  el recorrido se detuvo:', e.message.split('\n').slice(0, 6).join(' | '))
  await foto('99-error')
  process.exitCode = 1
} finally {
  console.log('\n--- llamadas a /api/buscar ---')
  for (const u of urlsBuscar) console.log('  ', u.slice(0, 220))
  console.log('--- errores de consola ---')
  for (const e of new Set(errores)) console.log('  ', e.slice(0, 250))
  console.log('--- peticiones fallidas ---')
  for (const f of new Set(fallidas)) console.log('  ', f.slice(0, 300))
  console.log(`--- comprobaciones en rojo: ${fallos} ---`)
  await navegador.close()
}
