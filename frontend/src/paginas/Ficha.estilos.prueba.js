// @vitest-environment node
//
// El `order` de la caja de contacto en movil no se puede comprobar pintando: jsdom no aplica
// media queries ni calcula layout. Lo que se vigila aqui es la REGLA en la hoja de estilos, que
// es justo lo que se borra sin darse cuenta. El efecto en un telefono se miro en Chromium (ver el
// PR). En JavaScript y no en TypeScript, como `pruebas-functions/cabeceras.prueba.js`: leer un
// archivo necesita Node, y el frontend no tiene sus tipos.
import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'
import { describe, expect, it } from 'vitest'

const css = readFileSync(resolve(import.meta.dirname, '..', 'index.css'), 'utf8')

describe('la hoja de estilos sube el contacto en movil', () => {
  it('bajo 960 px el lateral se adelanta con `order`, sin tocar el HTML', () => {
    const movil = css.match(/@media\s*\(max-width:\s*960px\)\s*\{[^}]*\.ficha-rejilla[\s\S]*?\n\}/)
    expect(movil, 'no esta la media query de 960 px de .ficha-rejilla').not.toBeNull()

    const bloque = movil[0]
    expect(bloque).toMatch(/\.lateral\s*\{[^}]*order:\s*-1/)
    // Una columna: si volviera a dos, el `order` no haria lo que dice este bloque.
    expect(bloque).toMatch(/grid-template-columns:\s*minmax\(0,\s*1fr\)/)
  })
})
