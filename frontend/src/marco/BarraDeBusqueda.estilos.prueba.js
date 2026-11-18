// @vitest-environment node
//
// La lista del buscador en el celular, vigilada en la hoja de estilos: jsdom no aplica media queries
// ni calcula layout. El efecto (390x844 y 390x600, con 5 recientes) se midio en Chromium (ver el PR #98).
// En JavaScript, como `paginas/Ficha.estilos.prueba.js`: leer un archivo necesita Node.
import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'
import { describe, expect, it } from 'vitest'

// Sin comentarios: una regla comentada no rige, y no debe contar como presente.
const css = readFileSync(resolve(import.meta.dirname, '..', 'index.css'), 'utf8').replace(/\/\*[\s\S]*?\*\//g, '')

/** Lo de los bloques `@media (max-width: 880px)`: el ancho en que aparece la barra inferior. */
const movil = [...css.matchAll(/@media\s*\(max-width:\s*880px\)\s*\{([\s\S]*?)\n\}/g)].map((m) => m[1]).join('\n')

function regla(selector) {
  const escapado = selector.replace(/[.*+?^${}()|[\]\\>]/g, '\\$&')
  const m = movil.match(new RegExp(`(?:^|\\n)\\s*${escapado}\\s*\\{([^}]*)\\}`))
  return m ? m[1] : null
}

describe('la lista del buscador no baja por detras de la barra inferior', () => {
  it('su alto llega solo hasta encima de la barra (la pantalla menos cabecera, campo y barra)', () => {
    const lista = regla('.barra-lista')
    expect(lista, 'no esta .barra-lista en la media query de 880 px').not.toBeNull()
    // Literal y no derivado: 88 de cabecera + 72 del campo + 10 de aire + 78 de la barra inferior.
    expect(lista).toMatch(/max-height:\s*calc\(100dvh - 248px - env\(safe-area-inset-bottom\)\)/)
    // Al llegar al final de la lista, el dedo no arrastra la pagina de atras.
    expect(lista).toMatch(/overscroll-behavior:\s*contain/)
  })

  it('el buscador sube hasta quedar bajo la cabecera, no debajo de ella', () => {
    expect(regla('.barra')).toMatch(/scroll-margin-top:\s*88px/)
  })
})
