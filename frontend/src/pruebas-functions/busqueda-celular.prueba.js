// @vitest-environment node
//
// /buscar en el celular (18-nov-2026). Lo que jsdom no puede medir se midio en Chromium a 390 px
// (zass-tareas/capturas-interfaz/filtros.cjs): los resultados pasaron de empezar en 964 px a 593 px,
// y la pagina no se ensancha. Aqui se vigilan las reglas que lo hacen posible.
import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'
import { describe, expect, it } from 'vitest'

const css = readFileSync(resolve(import.meta.dirname, '..', 'index.css'), 'utf8').replace(/\/\*[\s\S]*?\*\//g, '')

/** El cuerpo de la media query de 600 px que trae las reglas de la busqueda. */
function bloqueCelular() {
  // Tolera espacios: al quitar los comentarios queda una linea en blanco antes de la primera regla.
  const inicio = css.search(/@media \(max-width: 600px\) \{\s*\.buscar-cuerpo/)
  expect(inicio, 'no esta la media query de la busqueda en el celular').toBeGreaterThan(-1)
  let nivel = 0
  for (let i = css.indexOf('{', inicio); i < css.length; i++) {
    if (css[i] === '{') nivel++
    else if (css[i] === '}' && --nivel === 0) return css.slice(inicio, i + 1)
  }
  throw new Error('media query sin cerrar')
}
function regla(selector) {
  const b = bloqueCelular()
  const i = b.indexOf(`${selector} {`)
  expect(i, `no esta la regla ${selector}`).toBeGreaterThan(-1)
  return b.slice(i, b.indexOf('}', i))
}

describe('busqueda en el celular', () => {
  it('cada fila de chips se desliza de lado en vez de crecer hacia abajo', () => {
    const r = regla('.buscar-cuerpo .orden-barra')
    expect(r).toMatch(/flex-wrap:\s*nowrap/)
    expect(r).toMatch(/overflow-x:\s*auto/)
  })

  // Sin esto, la etiqueta oculta «Comuna» (.solo-lector, con position: absolute) escapa del
  // recorte de la fila y la pagina entera mide 429 px en una pantalla de 390.
  it('la fila es el bloque contenedor de lo absoluto que lleva dentro', () => {
    expect(regla('.buscar-cuerpo .orden-barra')).toMatch(/position:\s*relative/)
  })

  it('las notas de los filtros se ocultan solo mientras su filtro no esta puesto', () => {
    const b = bloqueCelular()
    expect(b).toContain(":has([aria-describedby='nota-promedio'][aria-pressed='true'])) #nota-promedio")
    expect(b).toContain(":has([aria-describedby='nota-credencial'][aria-pressed='true'])) #nota-credencial")
  })
})
