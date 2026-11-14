/**
 * Humo contra el sitio DE VERDAD, el que ve la gente.
 *
 * Los otros guiones de esta carpeta miran `localhost` con la base de desarrollo. Este mira
 * lo que hay publicado: Cloudflare delante, la API en Render, los datos de Neon. Son cosas
 * distintas, y la diferencia ya nos mordió —en producción la búsqueda devolvía 500 mientras
 * `/api/salud` decía que todo estaba bien, porque faltaba PostGIS—.
 *
 * Es de SOLO LECTURA a propósito: no crea cuentas, no agenda nada, no toma cupos. Se puede
 * correr el día del lanzamiento, con gente dentro, sin ensuciar ni un dato.
 *
 *   node pruebas-navegador/humo-produccion.mjs                      (contra https://zass.cl)
 *   node pruebas-navegador/humo-produccion.mjs https://zass-59w.pages.dev
 *   node pruebas-navegador/humo-produccion.mjs https://localhost:5173
 *
 * Salida: una línea por comprobación y un veredicto. Devuelve código 1 si algo está mal, así
 * que sirve en un workflow.
 *
 * Si el sitio está CERRADO (mantenimiento), lo dice en una línea y no intenta nada más: 20
 * rojos por un cierre que es a propósito solo enseñan a no mirar el resultado.
 */
import { chromium } from 'playwright'

const BASE = (process.argv[2] ?? 'https://zass.cl').replace(/\/$/, '')

/*
 * Contra `localhost` hay cosas que NO existen y que no tiene sentido dar por rotas: las
 * cabeceras de seguridad y los archivos `robots.txt`/`sitemap.xml` los sirve Cloudflare (el
 * `_headers` y las Functions), y Vite no hace ni una ni otra. Marcarlas en rojo ahi seria
 * dejar rojos permanentes, que es la forma mas segura de que nadie mire el resultado.
 */
const EN_LOCAL = /^https?:\/\/(localhost|127\.0\.0\.1)/.test(BASE)

let malos = 0
let avisos = 0

function ok(texto, detalle = '') {
  console.log(`ok   ${texto}${detalle ? ` — ${detalle}` : ''}`)
}

function mal(texto, detalle = '') {
  malos += 1
  console.log(`MAL  ${texto}${detalle ? ` — ${detalle}` : ''}`)
}

function comprobar(texto, bien, detalle = '') {
  if (bien) ok(texto, detalle)
  else mal(texto, detalle)
}

/** Lo que solo existe detras de Cloudflare: en local se informa, no se reprueba. */
function comprobarSoloPublicado(texto, bien, detalle = '') {
  if (bien) ok(texto, detalle)
  else if (EN_LOCAL) aviso(`${texto}: no se puede comprobar en local (lo sirve Cloudflare) — ${detalle}`)
  else mal(texto, detalle)
}

function aviso(texto) {
  avisos += 1
  console.log(`  i  ${texto}`)
}

/** Una petición HTTP sin navegador: para cabeceras, JSON y archivos. */
async function pedir(contexto, ruta, cabeceras = {}) {
  const r = await contexto.request.get(`${BASE}${ruta}`, { headers: cabeceras, failOnStatusCode: false })
  return { estado: r.status(), cabeceras: r.headers(), texto: await r.text() }
}

const navegador = await chromium.launch()
// `ignoreHTTPSErrors` solo hace falta para el certificado de desarrollo de localhost; contra
// producción no cambia nada y deja que el guion sirva para los dos sitios.
const ctx = await navegador.newContext({ ignoreHTTPSErrors: true })

console.log(`\nHumo contra ${BASE}\n${'='.repeat(40 + BASE.length)}\n`)

// ---------------------------------------------------------------- 0. ¿Está abierto?
const portada = await pedir(ctx, '/', { accept: 'text/html' })

if (portada.estado === 503) {
  console.log('El sitio está CERRADO (mantenimiento). Es lo que corresponde antes del lanzamiento.')
  console.log(`Devolvió 503 y ${/terminando de construir/i.test(portada.texto) ? 'la página de aviso.' : 'algo que no es la página de aviso: revísalo.'}`)
  console.log('\nPara mirarlo de verdad: MANTENIMIENTO_PASE en Cloudflare Pages y entrar con ?pase=…')
  console.log('Para abrirlo: MANTENIMIENTO=0 y volver a desplegar.\n')
  await navegador.close()
  process.exit(/terminando de construir/i.test(portada.texto) ? 0 : 1)
}

comprobar('la portada responde 200', portada.estado === 200, `HTTP ${portada.estado}`)

// ---------------------------------------------------------------- 1. La API, por donde entra la gente
// Se pide a través del sitio, no al origen: es el camino real, y el que ya falló una vez.
const salud = await pedir(ctx, '/api/salud/lista')
comprobar('la API está lista PARA SERVIR (base y PostGIS), por el sitio',
  salud.estado === 200, `HTTP ${salud.estado} ${salud.texto.slice(0, 60)}`)

const zonas = await pedir(ctx, '/api/zonas')
let cuantasZonas = 0
try { cuantasZonas = JSON.parse(zonas.texto).length } catch { /* se informa abajo */ }
comprobar('las zonas vienen de la base real', zonas.estado === 200 && cuantasZonas > 100,
  `HTTP ${zonas.estado}, ${cuantasZonas} zonas`)

// La búsqueda con coordenadas es la que de verdad usa PostGIS (ST_Distance sobre geography).
// Ahí estaba el 500 que `/api/salud` no veía.
const busqueda = await pedir(ctx, '/api/buscar?lat=-38.7359&lon=-72.5904&q=abogado')
let resultados = null
try { resultados = JSON.parse(busqueda.texto) } catch { /* se informa abajo */ }
comprobar('la búsqueda por cercanía responde (es la que usa PostGIS)',
  busqueda.estado === 200, `HTTP ${busqueda.estado}`)

const cuantos = Array.isArray(resultados?.resultados) ? resultados.resultados.length
  : Array.isArray(resultados) ? resultados.length : 0
if (busqueda.estado === 200 && cuantos === 0) {
  aviso('la búsqueda funciona pero no devolvió a nadie: o no hay prestadores publicados, o no hay ninguno cerca de Temuco')
}

// ---------------------------------------------------------------- 2. Lo que indexa Google
const robots = await pedir(ctx, '/robots.txt')
comprobarSoloPublicado('robots.txt se sirve como texto', robots.estado === 200
  && (robots.cabeceras['content-type'] ?? '').includes('text/plain'),
  `HTTP ${robots.estado}, ${robots.cabeceras['content-type'] ?? 'sin tipo'}`)

const mapa = await pedir(ctx, '/sitemap.xml')
const esXml = (mapa.cabeceras['content-type'] ?? '').includes('xml')
comprobarSoloPublicado('el sitemap se sirve como XML y no como la página de la app',
  mapa.estado === 200 && esXml, `HTTP ${mapa.estado}, ${mapa.cabeceras['content-type'] ?? 'sin tipo'}`)

if (mapa.estado === 200 && esXml) {
  const dominios = [...new Set([...mapa.texto.matchAll(/<loc>(https?:\/\/[^/]+)/g)].map((m) => m[1]))]
  comprobar('todas las direcciones del sitemap apuntan a este sitio',
    dominios.length > 0 && dominios.every((d) => d === BASE), dominios.join(' ') || 'ninguna')
}

// ---------------------------------------------------------------- 3. Las cabeceras de seguridad
const seguridad = {
  'strict-transport-security': (v) => /max-age=\d+/.test(v ?? ''),
  'x-content-type-options': (v) => (v ?? '').toLowerCase() === 'nosniff',
  'referrer-policy': (v) => (v ?? '').length > 0,
}
for (const [cabecera, vale] of Object.entries(seguridad)) {
  comprobarSoloPublicado(`la portada manda ${cabecera}`, vale(portada.cabeceras[cabecera]),
    portada.cabeceras[cabecera] ?? 'no viene')
}

// ---------------------------------------------------------------- 4. Las pantallas, en el navegador
const RUTAS = [
  ['portada', '/', /./],
  ['buscar', '/buscar', /./],
  ['comunas', '/comunas', /comuna/i],
  ['oficios', '/oficios', /./],
  ['para profesionales', '/profesionales', /./],
  ['planes', '/planes', /cupo|plan/i],
  ['términos', '/terminos', /términos/i],
  ['privacidad', '/privacidad', /privacidad/i],
  ['una ruta que no existe', '/esto-no-existe-de-ninguna-manera', /no existe/i],
]

const pagina = await ctx.newPage()
const erroresDeConsola = []
const peticionesFallidas = []
pagina.on('console', (m) => { if (m.type() === 'error') erroresDeConsola.push(m.text()) })
pagina.on('requestfailed', (p) => {
  // Una navegación cancela las peticiones en vuelo de la pantalla anterior: eso no es un fallo.
  if (!(p.failure()?.errorText ?? '').includes('ABORTED')) {
    peticionesFallidas.push(`${p.method()} ${p.url()} ${p.failure()?.errorText ?? ''}`)
  }
})
pagina.on('response', (r) => {
  if (r.status() >= 500) peticionesFallidas.push(`HTTP ${r.status()} ${r.url()}`)
})

for (const [nombre, ruta, esperado] of RUTAS) {
  try {
    const r = await pagina.goto(`${BASE}${ruta}`, { waitUntil: 'networkidle', timeout: 30000 })
    const h1 = await pagina.locator('h1').first().innerText().catch(() => '')
    const texto = await pagina.locator('body').innerText()
    const estado = r?.status() ?? 0
    // La 404 de la app responde 200 a propósito: es una aplicación de una sola página.
    comprobar(`${nombre.padEnd(22)} carga y dice algo`,
      estado === 200 && h1.length > 0 && esperado.test(texto), `HTTP ${estado}, h1="${h1}"`)
  } catch (e) {
    mal(`${nombre.padEnd(22)} no cargó`, e.message.split('\n')[0])
  }
}

// ---------------------------------------------------------------- 5. Una ficha de verdad
// El slug NO se inventa: se saca de lo que el sitio mismo publica, porque un slug a mano
// quizá no exista en producción. Primero del sitemap; si no lo sirve (en local lo atiende
// Vite y devuelve la app), del primer resultado de la búsqueda.
{
  const delMapa = (mapa.estado === 200 && esXml)
    ? [...mapa.texto.matchAll(/<loc>[^<]*\/prestadores\/([^<]+)<\/loc>/g)][0]?.[1]
    : undefined
  const deLaBusqueda = (Array.isArray(resultados?.resultados) ? resultados.resultados : [])[0]?.slug
  const ficha = delMapa ?? deLaBusqueda

  if (!ficha) {
    aviso('no se pudo sacar ninguna ficha ni del sitemap ni de la búsqueda: nadie tiene perfil publicado')
  } else {
    const r = await pagina.goto(`${BASE}/prestadores/${ficha}`, { waitUntil: 'networkidle' })
    const h1 = await pagina.locator('h1').first().innerText().catch(() => '')
    comprobar(`una ficha real (${delMapa ? 'del sitemap' : 'de la búsqueda'}) carga`, (r?.status() ?? 0) === 200 && h1.length > 0,
      `/${ficha} → h1="${h1}"`)
    const cuerpo = await pagina.locator('body').innerText()
    comprobar('y el contacto queda detrás de la cuenta, sin sesión',
      /crea tu cuenta|inicia sesión|entrar/i.test(cuerpo), 'pide cuenta para contactar')
  }
}

// ---------------------------------------------------------------- Veredicto
console.log('')
if (erroresDeConsola.length) {
  console.log(`errores de consola (${erroresDeConsola.length}):`)
  for (const e of erroresDeConsola.slice(0, 5)) console.log(`     ${e.slice(0, 160)}`)
  malos += erroresDeConsola.length
}
if (peticionesFallidas.length) {
  console.log(`peticiones fallidas o 5xx (${peticionesFallidas.length}):`)
  for (const p of peticionesFallidas.slice(0, 5)) console.log(`     ${p.slice(0, 160)}`)
  malos += peticionesFallidas.length
}

console.log(malos === 0
  ? `\nTodo en orden${avisos ? ` (${avisos} aviso${avisos === 1 ? '' : 's'} que mirar)` : ''}.\n`
  : `\n${malos} problema(s).\n`)

await navegador.close()
process.exit(malos === 0 ? 0 : 1)
