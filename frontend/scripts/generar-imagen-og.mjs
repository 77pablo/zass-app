/**
 * Genera frontend/public/og-zass.png (1200x630), la imagen que se ve al compartir un enlace de ZASS!.
 * Se dibuja con una pagina HTML de marca (og-plantilla.html) y Playwright le saca la foto.
 *
 * Uso, desde frontend/:   node scripts/generar-imagen-og.mjs
 * Hace falta tener un Chromium de Playwright (`npx playwright install chromium`) y conexion, porque la
 * tipografia Montserrat se pide a Google Fonts; sin conexion saldria con la letra del sistema.
 */
import { pathToFileURL } from 'node:url'
import { resolve } from 'node:path'
import { chromium } from 'playwright'

const aqui = import.meta.dirname
const destino = resolve(aqui, '..', 'public', 'og-zass.png')

const navegador = await chromium.launch()
try {
  const pagina = await navegador.newPage({ viewport: { width: 1200, height: 630 }, deviceScaleFactor: 1 })
  await pagina.goto(pathToFileURL(resolve(aqui, 'og-plantilla.html')).href, { waitUntil: 'networkidle' })
  // Sin esto la foto puede salir con la letra de respaldo si la fuente aun no termino de bajar.
  await pagina.evaluate(() => document.fonts.ready)
  const montserrat = await pagina.evaluate(() => document.fonts.check('800 40px Montserrat'))
  if (!montserrat) console.warn('AVISO: Montserrat no cargo; la imagen usa la letra del sistema.')
  await pagina.screenshot({ path: destino, type: 'png', clip: { x: 0, y: 0, width: 1200, height: 630 } })
  console.log(`Escrito ${destino}`)
} finally {
  await navegador.close()
}
