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

    // RN-S3-02 sobre lo que de verdad llega al navegador: ni coordenadas, ni correos internos.
    // Heredado de mirar-busqueda.mjs, que medía la pantalla de búsqueda antigua y se borró: esto
    // era lo único que no cubría ningún otro guion, y es lo más delicado de todo lo que medía.
    const html = await pagina.content()
    const filtrado = /@zass\.local|"latitud"|"longitud"|latitud=|longitud=/i.exec(html)

    const medidas = await pagina.evaluate(() => {
      const ancho = document.documentElement.clientWidth
      const desborde = document.documentElement.scrollWidth - ancho
      // Que se pueda tocar: enlaces, botones y campos visibles de menos de 44 px de alto.
      //
      // Con una excepcion, y es la del propio criterio WCAG 2.5.8 («Inline»): un enlace metido
      // dentro de una frase, cuyo alto lo decide la altura de linea del texto que lo rodea, no
      // cuenta. Si contara, la unica forma de cumplir seria sacar el enlace de la frase o
      // separar las palabras, que es peor para leer. «Al crear tu cuenta aceptas los Terminos
      // de uso» es ese caso. Lo que SI cuenta es un enlace que es una accion por si solo,
      // aunque este en una linea de texto: a ese se le da cuerpo (ver .letra-chica a).
      //
      // Se listan igual, aparte, para que no desaparezcan de la vista: un dia puede colarse un
      // boton de verdad dentro de un parrafo.
      const esEnLinea = (e) => {
        if (getComputedStyle(e).display !== 'inline') return false
        const p = e.parentElement
        if (!p) return false
        // Hay texto que no es el enlace alrededor: entonces es una frase, no una accion suelta.
        const textoAlrededor = [...p.childNodes]
          .filter((n) => n.nodeType === Node.TEXT_NODE)
          .map((n) => n.textContent.trim())
          .join('')
        return textoAlrededor.length > 0
      }
      const medir = (e) => `${e.tagName.toLowerCase()} "${(e.textContent || e.name || '').trim().slice(0, 30)}" ${Math.round(e.getBoundingClientRect().height)}px`
      const candidatos = [...document.querySelectorAll('a, button, select, input:not([type=hidden])')]
        .filter((e) => {
          const r = e.getBoundingClientRect()
          const cs = getComputedStyle(e)
          return r.width > 0 && r.height > 0 && cs.visibility !== 'hidden'
            && !e.classList.contains('solo-lector') && !e.classList.contains('saltar')
            && e.type !== 'checkbox' && e.type !== 'radio' && r.height < 43.5
        })
      const chicos = candidatos.filter((e) => !esEnLinea(e)).map(medir)
      const chicosEnFrase = candidatos.filter(esEnLinea).map(medir)
      return {
        titulo: document.title,
        h1: document.querySelector('h1')?.textContent?.trim().slice(0, 60) ?? null,
        desborde, chicos, chicosEnFrase,
        fuente: [...document.fonts].filter((f) => f.status === 'loaded').map((f) => f.family),
        // El amarillo de la marca, MEDIDO: en jsdom no hay CSS que medir, y si alguien pisa la
        // variable esto lo dice. Se mira el VALOR CALCULADO de --amarillo y no el fondo del sitio:
        // el fondo cambia con la paleta (hoy es oscuro) y el amarillo es la marca, no una decision
        // de tema. Primera version de esta comprobacion daba 22 rojos porque esperaba la crema de
        // la paleta vieja: la medicion estaba bien, lo que estaba viejo era lo que yo esperaba.
        amarillo: getComputedStyle(document.documentElement).getPropertyValue('--amarillo').trim(),
      }
    })

    await pagina.screenshot({ path: resolve(carpeta, `sitio-${vista}-${nombre}.png`), fullPage: true })

    const malos = []
    if (medidas.desborde > 0) malos.push(`DESBORDE horizontal de ${medidas.desborde}px`)
    if (medidas.chicos.length) malos.push(`objetivos chicos: ${medidas.chicos.join('; ')}`)
    const reales = errores.filter((e) => !(nombre.includes('no-existe') && e.includes('404')))
    if (reales.length) malos.push(`errores de consola: ${[...new Set(reales)].join(' | ')}`)
    if (!medidas.fuente.some((f) => f.includes('Montserrat'))) malos.push('Montserrat NO cargo')
    if (filtrado) malos.push(`el HTML lleva algo que no debe salir: «${filtrado[0]}»`)
    if (medidas.amarillo.toLowerCase() !== '#f6c81e') malos.push(`el amarillo de la marca cambio: ${medidas.amarillo}`)
    problemas += malos.length
    console.log(`${malos.length ? 'XX' : 'ok'}  ${vista.padEnd(10)} ${nombre.padEnd(16)} «${medidas.titulo}» h1: ${medidas.h1}`)
    for (const m of malos) console.log(`      - ${m}`)
    // Informativo, no problema: enlaces dentro de una frase (excepcion «Inline» de WCAG 2.5.8).
    if (medidas.chicosEnFrase.length) {
      console.log(`      i enlaces en frase (exentos 2.5.8): ${medidas.chicosEnFrase.join('; ')}`)
    }
  }
  await contexto.close()
}

await navegador.close()
console.log(problemas === 0 ? '\nTodo en orden.' : `\n${problemas} problema(s).`)
process.exitCode = problemas === 0 ? 0 : 1
