/**
 * Barrido de las paginas publicas en un navegador real, en escritorio y a ancho de telefono.
 * No afirma que "se ve bien": recoge para cada ruta el titulo, el h1, el codigo HTTP del
 * documento, los errores de consola, TODA peticion de red que falle (con su cuerpo) y si hay
 * desborde horizontal. Deja una captura por pantalla y vista.
 *
 * Necesita arriba la API (https://localhost:7277) y Vite (https://localhost:5173), y los datos
 * de pruebas-navegador/datos/sembrar-demo.sql.
 *
 *   node pruebas-navegador/mirar-publicas.mjs [carpeta-de-capturas]     (desde frontend/)
 */
import { mkdirSync, writeFileSync } from 'node:fs'
import { resolve } from 'node:path'
import { chromium } from 'playwright'

const BASE = 'https://localhost:5173'
const carpeta = process.argv[2] ?? resolve(import.meta.dirname, '..', '..', 'registros', 'capturas')
mkdirSync(carpeta, { recursive: true })

const PAGINAS = [
  ['portada', '/'],
  ['buscar', '/buscar'],
  ['buscar-rubro', '/buscar?q=abogado'],
  ['buscar-sin-resultados', '/buscar?q=zzzznoexiste'],
  ['comunas', '/comunas'],
  ['oficios', '/oficios'],
  ['profesionales', '/profesionales'],
  ['entrar', '/entrar'],
  ['crear-cuenta', '/crear-cuenta'],
  ['crear-cuenta-prestador', '/crear-cuenta?rol=prestador'],
  ['terminos', '/terminos'],
  ['privacidad', '/privacidad'],
  ['ficha-publica', '/prestadores/marcela-soto'],
  ['ficha-inexistente', '/prestadores/nadie-con-ese-nombre'],
  ['ruta-inexistente', '/esto-no-existe-de-ninguna-manera'],
  ['barrios-redirige', '/barrios'],
]

const VISTAS = [
  ['escritorio', { width: 1280, height: 900 }],
  ['celular', { width: 390, height: 844 }],
]

const informe = []
const navegador = await chromium.launch()

for (const [vista, tamano] of VISTAS) {
  const contexto = await navegador.newContext({ ignoreHTTPSErrors: true, viewport: tamano })
  const pagina = await contexto.newPage()
  const errores = []
  const fallidas = []
  pagina.on('console', (m) => { if (m.type() === 'error') errores.push(m.text()) })
  pagina.on('pageerror', (e) => errores.push(`pageerror: ${e.message}`))
  pagina.on('requestfailed', (p) =>
    fallidas.push(`ABORTADA ${p.method()} ${p.url()} -> ${p.failure()?.errorText}`))
  pagina.on('response', async (r) => {
    if (r.status() < 400) return
    let cuerpo = ''
    try { cuerpo = (await r.text()).slice(0, 300).replace(/\s+/g, ' ') } catch { cuerpo = '(sin cuerpo)' }
    fallidas.push(`${r.status()} ${r.request().method()} ${r.url()} :: ${cuerpo}`)
  })

  for (const [nombre, ruta] of PAGINAS) {
    errores.length = 0
    fallidas.length = 0
    const respuesta = await pagina.goto(BASE + ruta, { waitUntil: 'networkidle' }).catch((e) => ({ status: () => `NAVEGACION FALLO: ${e.message}` }))
    // Las secciones entran con animacion al aparecer: sin recorrer la pagina la captura las
    // mostraria todavia invisibles y pareceria una pantalla en blanco.
    await pagina.evaluate(async () => {
      const alto = document.documentElement.scrollHeight
      for (let y = 0; y < alto; y += 500) {
        window.scrollTo({ top: y, behavior: 'instant' })
        await new Promise((r) => setTimeout(r, 60))
      }
      window.scrollTo({ top: 0, behavior: 'instant' })
    })
    await pagina.waitForTimeout(400)

    const medida = await pagina.evaluate(() => ({
      anchoDocumento: document.documentElement.scrollWidth,
      anchoVentana: window.innerWidth,
      h1: [...document.querySelectorAll('h1')].map((h) => h.textContent?.trim()),
      // "Pantalla en blanco": el contenedor de React monta pero sin texto visible.
      largoTexto: (document.querySelector('#raiz, #root, body')?.innerText ?? '').trim().length,
      titulo: document.title,
      meta: document.querySelector('meta[name="robots"]')?.getAttribute('content') ?? null,
    }))

    await pagina.screenshot({ path: resolve(carpeta, `${nombre}-${vista}.png`), fullPage: true })

    informe.push({
      vista,
      nombre,
      ruta,
      urlFinal: pagina.url().replace(BASE, ''),
      estadoHttp: typeof respuesta?.status === 'function' ? respuesta.status() : null,
      titulo: medida.titulo,
      h1: medida.h1,
      robots: medida.meta,
      largoTexto: medida.largoTexto,
      desborde: medida.anchoDocumento > medida.anchoVentana + 1
        ? `${medida.anchoDocumento} > ${medida.anchoVentana}` : null,
      errores: [...new Set(errores)],
      fallidas: [...new Set(fallidas)],
    })
    const mal = errores.length + fallidas.length
    console.log(`${mal ? 'MAL ' : 'ok  '} ${vista.padEnd(10)} ${ruta.padEnd(42)} h1=${JSON.stringify(medida.h1)} texto=${medida.largoTexto}${medida.anchoDocumento > medida.anchoVentana + 1 ? ' DESBORDE' : ''}`)
    for (const e of new Set(errores)) console.log(`       consola: ${e.slice(0, 220)}`)
    for (const f of new Set(fallidas)) console.log(`       red:     ${f.slice(0, 260)}`)
  }
  await contexto.close()
}

// Los archivos que no son la aplicacion: se piden crudos, no por el router.
const contexto = await navegador.newContext({ ignoreHTTPSErrors: true })
for (const archivo of ['/robots.txt', '/sitemap.xml', '/favicon.svg']) {
  const r = await contexto.request.get(BASE + archivo)
  const cuerpo = (await r.text()).slice(0, 200).replace(/\s+/g, ' ')
  const tipo = r.headers()['content-type'] ?? ''
  console.log(`arch ${archivo.padEnd(14)} ${r.status()} ${tipo} :: ${cuerpo.slice(0, 120)}`)
  informe.push({ archivo, estadoHttp: r.status(), tipo, muestra: cuerpo })
}
await contexto.close()

writeFileSync(resolve(carpeta, 'informe-publicas.json'), JSON.stringify(informe, null, 2))
console.log(`\ninforme: ${resolve(carpeta, 'informe-publicas.json')}`)
await navegador.close()
