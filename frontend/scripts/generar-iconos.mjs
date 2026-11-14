/**
 * Genera los iconos instalables de la PWA a partir de los SVG de public/, que son la fuente:
 *
 *   public/icono-zass.svg           ->  icono-zass-192.png, icono-zass-512.png
 *   public/icono-zass-maskable.svg  ->  icono-zass-maskable-512.png, apple-touch-icon.png (180)
 *
 * Uso, desde frontend/:   node scripts/generar-iconos.mjs
 *
 * Igual que generar-imagen-og.mjs: hace falta el Chromium de Playwright
 * (`npx playwright install chromium`) y conexion, porque Montserrat se pide a Google Fonts.
 * Sin conexion la Z sale con la letra de respaldo y el icono queda distinto al del sitio.
 */
import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'
import { chromium } from 'playwright'

const aqui = import.meta.dirname
const publico = resolve(aqui, '..', 'public')

const SALIDAS = [
  { fuente: 'icono-zass.svg', destino: 'icono-zass-192.png', lado: 192 },
  { fuente: 'icono-zass.svg', destino: 'icono-zass-512.png', lado: 512 },
  { fuente: 'icono-zass-maskable.svg', destino: 'icono-zass-maskable-512.png', lado: 512 },
  { fuente: 'icono-zass-maskable.svg', destino: 'apple-touch-icon.png', lado: 180 },
]

/** El SVG, incrustado en una pagina del lado justo y con Montserrat pedida a Google Fonts. */
function pagina(svg, lado) {
  return `<!doctype html><html><head><meta charset="UTF-8">
<link rel="preconnect" href="https://fonts.googleapis.com">
<link rel="preconnect" href="https://fonts.gstatic.com" crossorigin>
<link href="https://fonts.googleapis.com/css2?family=Montserrat:wght@800&display=swap" rel="stylesheet">
<style>*{margin:0;padding:0}html,body{width:${lado}px;height:${lado}px;overflow:hidden}
svg{display:block;width:${lado}px;height:${lado}px}</style></head><body>${svg}</body></html>`
}

const navegador = await chromium.launch()
try {
  for (const { fuente, destino, lado } of SALIDAS) {
    const svg = readFileSync(resolve(publico, fuente), 'utf8')
    const p = await navegador.newPage({ viewport: { width: lado, height: lado }, deviceScaleFactor: 1 })
    await p.setContent(pagina(svg, lado), { waitUntil: 'networkidle' })
    await p.evaluate(() => document.fonts.ready)
    if (!await p.evaluate(() => document.fonts.check('800 100px Montserrat')))
      console.warn(`AVISO: Montserrat no cargo; ${destino} usa la letra del sistema.`)
    await p.screenshot({ path: resolve(publico, destino), type: 'png', clip: { x: 0, y: 0, width: lado, height: lado } })
    await p.close()
    console.log(`Escrito public/${destino} (${lado}x${lado})`)
  }
} finally {
  await navegador.close()
}
