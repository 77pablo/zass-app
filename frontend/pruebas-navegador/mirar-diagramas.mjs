/**
 * Comprueba que los diagramas del sitio de documentacion RENDERIZAN, no que esten.
 *
 * Por que existe: el 7 de noviembre de 2026 dos diagramas ER estaban rotos y la
 * comprobacion «hay un SVG en cada diagrama» dio todo correcto. Mentia, porque **un error
 * de sintaxis de Mermaid tambien dibuja un SVG**: dibuja una caja roja que dice «Syntax
 * error in text». Un grep sobre el HTML generado no distingue una cosa de la otra.
 *
 * Esto si: abre cada pagina en un navegador de verdad, espera a que Mermaid corra, y falla
 * si aparece un error de sintaxis, si hay un error de JavaScript, o si una pagina que trae
 * bloques ```mermaid no dibujo ningun SVG.
 *
 *   node pruebas-navegador/mirar-diagramas.mjs                      (desde frontend/)
 *   node pruebas-navegador/mirar-diagramas.mjs "D:/otra/ruta/sitio"
 *
 * Sin argumento busca el sitio donde vive hoy, dentro de OneDrive.
 */
import { readdirSync, readFileSync, existsSync } from 'node:fs'
import { join, resolve } from 'node:path'
import { pathToFileURL } from 'node:url'
import { chromium } from 'playwright'

const PREDETERMINADO = join(
  process.env.USERPROFILE ?? process.env.HOME ?? '.',
  'OneDrive', 'Desktop', 'Repositorio ZASS', 'zass-documentacion', 'sitio',
)
const carpeta = resolve(process.argv[2] ?? PREDETERMINADO)

if (!existsSync(carpeta)) {
  console.error(`MAL  no encuentro el sitio en ${carpeta}`)
  console.error('     pasa la ruta como argumento, o genera el sitio con: python sitio/construir.py')
  process.exit(1)
}

const paginas = readdirSync(carpeta)
  .filter((f) => f.endsWith('.html'))
  .sort()

if (paginas.length === 0) {
  console.error(`MAL  no hay ningun .html en ${carpeta}`)
  process.exit(1)
}

const navegador = await chromium.launch()
let malas = 0
let diagramas = 0

for (const nombre of paginas) {
  const archivo = join(carpeta, nombre)
  // Cuantos bloques de diagrama trae el HTML, para saber cuantos SVG esperar.
  const fuente = readFileSync(archivo, 'utf8')
  const esperados = (fuente.match(/flowchart |erDiagram|sequenceDiagram|stateDiagram|classDiagram/g) ?? []).length

  const pagina = await navegador.newPage({ viewport: { width: 1400, height: 1000 } })
  const erroresJs = []
  pagina.on('pageerror', (e) => erroresJs.push(e.message))

  await pagina.goto(pathToFileURL(archivo).href, { waitUntil: 'networkidle' })
  await pagina.waitForTimeout(esperados > 0 ? 6000 : 500)

  const svgs = await pagina.locator('svg').count()
  const texto = await pagina.evaluate(() => document.body.innerText)
  const sintaxis = /syntax error|error in text|parse error/i.test(texto)

  const problemas = []
  if (sintaxis) problemas.push('ERROR DE SINTAXIS de Mermaid')
  if (esperados > 0 && svgs === 0) problemas.push(`${esperados} diagramas declarados y 0 dibujados`)
  if (erroresJs.length > 0) problemas.push(`JavaScript: ${erroresJs.join(' | ')}`)

  diagramas += esperados
  if (problemas.length > 0) {
    malas += 1
    console.log(`MAL  ${nombre}: ${problemas.join('; ')}`)
  } else {
    console.log(`OK   ${nombre}  (${esperados} declarados, ${svgs} svg)`)
  }

  await pagina.close()
}

await navegador.close()

console.log(`
${paginas.length} paginas, ${diagramas} diagramas declarados, ${malas} con problemas.`)
if (malas > 0) {
  console.log('Un error de sintaxis de Mermaid DIBUJA un SVG igual: por eso esto mira el texto y no el artefacto.')
  process.exitCode = 1
}
