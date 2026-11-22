// @vitest-environment node
//
// Las «Web Interface Guidelines» de Vercel aplicadas a ZASS! (18-nov-2026). Se vigilan las reglas
// globales del CSS que hacen que el sitio se sienta como una app y que lo fijo no tape el foco.
import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'
import { describe, expect, it } from 'vitest'

const SRC = resolve(import.meta.dirname, '..')
const css = readFileSync(resolve(SRC, 'index.css'), 'utf8').replace(/\/\*[\s\S]*?\*\//g, '')

describe('guias de interfaz', () => {
  it('lo que se toca responde sin la demora del doble toque', () => {
    expect(css).toMatch(/a, button, input, label, select, summary, textarea \{ touch-action: manipulation; \}/)
  })

  // La cabecera es sticky arriba y, en el telefono, la barra inferior es fija abajo. Sin el
  // relleno, tabular hasta un campo del final lo dejaba DEBAJO de la barra.
  it('lo fijo no tapa lo que tiene el foco: arriba la cabecera, abajo la barra del telefono', () => {
    expect(css).toMatch(/html \{ scroll-padding-top: \d+px; \}/)
    expect(css).toMatch(/@media \(max-width: 880px\) \{ html \{ scroll-padding-bottom: calc\(\d+px \+ env\(safe-area-inset-bottom\)\); \} \}/)
  })

  it('con mouse la pagina no rebota, con el dedo si (tirar para recargar)', () => {
    expect(css).toMatch(/@media \(pointer: fine\) \{ html \{ overscroll-behavior: none; \} \}/)
    expect(css).not.toMatch(/(^|\n)html \{[^}]*overscroll-behavior: none/)
  })

  it('las listas que se desplazan no arrastran la pagina de atras', () => {
    expect(css).toMatch(/\.barra-lista \{ overscroll-behavior: contain; \}/)
  })

  // El logo «ZAS<em>S!</em>» con translate="no" ya no existe: con el estilo iPhone (#131) la
  // cabecera no pinta la marca, solo la nombra para el lector de pantalla (aria-label="ZASS!").
})
