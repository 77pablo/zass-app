// @vitest-environment node
//
// JQ-17. jsdom no calcula media queries, así que el orden de las columnas de /planes en el
// celular (Profesional, Esencial, Equipo) se vigila leyendo la hoja de estilos: el orden se hace
// con `order` en CSS, sin duplicar el DOM, y en pantalla ancha vuelve a Esencial, Profesional, Equipo.
import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'
import { describe, expect, it } from 'vitest'

const css = readFileSync(resolve(import.meta.dirname, '..', 'index.css'), 'utf8')

describe('JQ-17: el orden de las columnas de /planes', () => {
  it('en el celular el Profesional va primero, luego el Esencial y luego el Equipo', () => {
    expect(css).toMatch(/\.plan-columna--profesional\s*\{\s*order:\s*1;/)
    expect(css).toMatch(/\.plan-columna--mensual\s*\{\s*order:\s*2;/)
    expect(css).toMatch(/\.plan-columna--equipo\s*\{\s*order:\s*3;/)
  })

  it('en pantalla ancha se restablece el orden Esencial, Profesional, Equipo', () => {
    const ancha = css.slice(css.search(/@media \(min-width: 900px\) \{\s*\.planes-rejilla/))
    expect(ancha).toMatch(/\.plan-columna--mensual,\s*\.plan-columna--profesional,\s*\.plan-columna--equipo\s*\{\s*order:\s*0;/)
  })
})
