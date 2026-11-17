// @vitest-environment node
//
// La rejilla de la ficha, vigilada en la hoja de estilos: jsdom no aplica media queries ni calcula
// layout, asi que pintando no se ve. El efecto en un telefono y en escritorio se miro en Chromium
// (ver el PR). En JavaScript y no en TypeScript, como `pruebas-functions/cabeceras.prueba.js`:
// leer un archivo necesita Node, y el frontend no tiene sus tipos.
import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'
import { describe, expect, it } from 'vitest'

const css = readFileSync(resolve(import.meta.dirname, '..', 'index.css'), 'utf8')

/** Las declaraciones de un selector exacto fuera de cualquier media query (la primera vez que aparece). */
function regla(selector) {
  const escapado = selector.replace(/[.*+?^${}()|[\]\\>]/g, '\\$&')
  const m = css.match(new RegExp(`(?:^|\\n)${escapado}\\s*\\{([^}]*)\\}`))
  return m ? m[1] : null
}

describe('la rejilla de la ficha', () => {
  it('en escritorio el contacto va a la derecha y ocupa las dos filas (sigue siendo sticky)', () => {
    expect(regla('.ficha-rejilla > .lateral')).toMatch(/grid-column:\s*2/)
    expect(regla('.ficha-rejilla > .lateral')).toMatch(/grid-row:\s*1\s*\/\s*span 2/)
    expect(regla('.ficha-rejilla > .persona')).toMatch(/grid-column:\s*1/)
    expect(regla('.ficha-rejilla > .ficha-cuerpo')).toMatch(/grid-column:\s*1/)
  })

  it('en el telefono todo va en una columna, en el orden del HTML: sin `order`', () => {
    const movil = css.match(/@media\s*\(max-width:\s*960px\)\s*\{[^}]*\.ficha-rejilla[\s\S]*?\n\}/)
    expect(movil, 'no esta la media query de 960 px de .ficha-rejilla').not.toBeNull()

    const bloque = movil[0]
    expect(bloque).toMatch(/grid-template-columns:\s*minmax\(0,\s*1fr\)/)
    expect(bloque).toMatch(/grid-row:\s*auto/)
    // `order` desordenaria lo que se ve respecto de lo que se recorre con el teclado.
    expect(bloque).not.toMatch(/order\s*:/)
  })
})
