/**
 * Recorre el sitio multipagina en un navegador real, en escritorio y en celular, y mide lo que
 * un ojo se salta: desborde horizontal, errores de consola, objetivos tactiles de menos de
 * 44 px, fuente que no cargo y el titulo de cada pagina. Deja capturas en registros/capturas/.
 *
 * Hace falta tener arriba la API (https://localhost:7277) y Vite (https://localhost:5173), y
 * datos: frontend/pruebas-navegador/datos/sembrar-demo.sql.
 *
 *   node pruebas-navegador/mirar-sitio.mjs          (desde frontend/)
 */
import { mkdirSync } from 'node:fs'
import { resolve } from 'node:path'
import { chromium } from 'playwright'

const BASE = 'https://localhost:5173'
const carpeta = resolve(import.meta.dirname, '..', '..', 'registros', 'capturas')
mkdirSync(carpeta, { recursive: true })

const PAGINAS = [
  ['inicio', '/'],
  ['buscar', '/buscar'],
  ['buscar-abogado', '/buscar?q=abogado&orden=PrecioAsc'],
  ['ficha', '/prestadores/marcela-soto'],
  ['comunas', '/comunas'],
  ['oficios', '/oficios'],
  ['profesionales', '/profesionales'],
  ['entrar', '/entrar'],
  ['crear-cuenta', '/crear-cuenta?rol=prestador'],
  ['no-existe', '/esto-no-existe'],
  ['ficha-no-existe', '/prestadores/nadie-con-ese-nombre'],
]

const VISTAS = [
  ['escritorio', { width: 1280, height: 800 }],
  ['celular', { width: 390, height: 844 }],
]

const navegador = await chromium.launch()
let problemas = 0

for (const [vista, tamano] of VISTAS) {
  const contexto = await navegador.newContext({
    ignoreHTTPSErrors: true, viewport: tamano, deviceScaleFactor: 1,
  })
  const pagina = await contexto.newPage()
  const errores = []
  pagina.on('console', (m) => { if (m.type() === 'error') errores.push(m.text()) })
  pagina.on('pageerror', (e) => errores.push(String(e)))

  for (const [nombre, ruta] of PAGINAS) {
    errores.length = 0
    await pagina.goto(BASE + ruta, { waitUntil: 'networkidle' })
    await pagina.waitForTimeout(900)
    // Recorre la pagina: las secciones entran con animacion al aparecer, y una captura de pagina entera
    // sin pasar por ellas las mostraria todavia ocultas.
    await pagina.evaluate(async () => {
      const alto = document.documentElement.scrollHeight
      for (let y = 0; y < alto; y += 500) {
        // 'instant': la hoja de estilos pone scroll-behavior: smooth, y un scroll suave no llega a ningun sitio en 90 ms.
        window.scrollTo({ top: y, behavior: 'instant' })
        await new Promise((r) => setTimeout(r, 90))
      }
      window.scrollTo({ top: 0, behavior: 'instant' })
    })
    await pagina.waitForTimeout(1200)

    const medidas = await pagina.evaluate(() => {
      const ancho = document.documentElement.clientWidth
      const desborde = document.documentElement.scrollWidth - ancho
      // Que se pueda tocar: enlaces, botones y campos visibles de menos de 44 px de alto.
      const chicos = [...document.querySelectorAll('a, button, select, input:not([type=hidden])')]
        .filter((e) => {
          const r = e.getBoundingClientRect()
          const cs = getComputedStyle(e)
          return r.width > 0 && r.height > 0 && cs.visibility !== 'hidden'
            && !e.classList.contains('solo-lector') && !e.classList.contains('saltar')
            && e.type !== 'checkbox' && e.type !== 'radio' && r.height < 43.5
        })
        .map((e) => `${e.tagName.toLowerCase()} "${(e.textContent || e.name || '').trim().slice(0, 30)}" ${Math.round(e.getBoundingClientRect().height)}px`)
      return {
        titulo: document.title,
        h1: document.querySelector('h1')?.textContent?.trim().slice(0, 60) ?? null,
        desborde, chicos,
        fuente: [...document.fonts].filter((f) => f.status === 'loaded').map((f) => f.family),
      }
    })

    await pagina.screenshot({ path: resolve(carpeta, `sitio-${vista}-${nombre}.png`), fullPage: true })

    const malos = []
    if (medidas.desborde > 0) malos.push(`DESBORDE horizontal de ${medidas.desborde}px`)
    if (medidas.chicos.length) malos.push(`objetivos chicos: ${medidas.chicos.join('; ')}`)
    const reales = errores.filter((e) => !(nombre.includes('no-existe') && e.includes('404')))
    if (reales.length) malos.push(`errores de consola: ${[...new Set(reales)].join(' | ')}`)
    if (!medidas.fuente.some((f) => f.includes('Montserrat'))) malos.push('Montserrat NO cargo')
    problemas += malos.length
    console.log(`${malos.length ? 'XX' : 'ok'}  ${vista.padEnd(10)} ${nombre.padEnd(16)} «${medidas.titulo}» h1: ${medidas.h1}`)
    for (const m of malos) console.log(`      - ${m}`)
  }
  await contexto.close()
}

await navegador.close()
console.log(problemas === 0 ? '\nTodo en orden.' : `\n${problemas} problema(s).`)
process.exitCode = problemas === 0 ? 0 : 1
