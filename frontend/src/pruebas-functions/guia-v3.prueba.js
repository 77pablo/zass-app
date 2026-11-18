// @vitest-environment node
//
// La guia v3 (aprobada por Pablo el 17-nov-2026), vigilada. Lo que se rompe sin que nadie lo vea:
// un degradado que vuelve, una variable que ya no existe (el color cae al del navegador sin avisar)
// o una pareja de colores que pierde contraste. axe lo mide en un navegador; esto lo mide en cada
// `npm run prueba`.
import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'
import { describe, expect, it } from 'vitest'

const SRC = resolve(import.meta.dirname, '..')
const css = readFileSync(resolve(SRC, 'index.css'), 'utf8')
const legal = readFileSync(resolve(SRC, 'legal.css'), 'utf8')
const sinComentarios = (t) => t.replace(/\/\*[\s\S]*?\*\//g, '')

/** Las variables definidas en :root, con su valor. */
const tokens = Object.fromEntries(
  [...sinComentarios(css).matchAll(/^\s*(--[a-z0-9-]+):\s*([^;]+);/gm)].map((m) => [m[1], m[2].trim()]),
)
const hex = (nombre) => {
  const v = tokens[nombre]
  expect(v, `${nombre} no esta definida en :root`).toBeDefined()
  expect(v, `${nombre} tiene que ser un hex para medir su contraste`).toMatch(/^#[0-9a-f]{6}$/i)
  return v
}

// WCAG 2.x: luminancia relativa y razon de contraste.
function luminancia(h) {
  const [r, g, b] = [1, 3, 5].map((i) => parseInt(h.slice(i, i + 2), 16) / 255)
    .map((c) => (c <= 0.03928 ? c / 12.92 : ((c + 0.055) / 1.055) ** 2.4))
  return 0.2126 * r + 0.7152 * g + 0.0722 * b
}
function contraste(a, b) {
  const [x, y] = [luminancia(a), luminancia(b)].sort((p, q) => q - p)
  return (x + 0.05) / (y + 0.05)
}

describe('guia v3: la paleta', () => {
  it('trae los colores de la guia, con su valor exacto', () => {
    expect(hex('--blanco').toLowerCase()).toBe('#ffffff')
    expect(hex('--pizarra').toLowerCase()).toBe('#111827')
    expect(hex('--celeste').toLowerCase()).toBe('#00a6ff')
    expect(hex('--ambar').toLowerCase()).toBe('#ffb800')
    expect(hex('--esmeralda').toLowerCase()).toBe('#10b981')
    // La excepcion que decidio Pablo: rojo oscuro, solo para errores.
    expect(hex('--malo').toLowerCase()).toBe('#b42318')
  })

  // Cada pareja que la hoja de estilos usa para TEXTO. 4,5:1 es el minimo de WCAG AA para texto normal.
  it.each([
    ['--pizarra', '--blanco'],
    ['--suave', '--blanco'],
    ['--suave', '--gris'],
    ['--pizarra', '--celeste'],
    ['--pizarra', '--ambar'],
    ['--pizarra', '--esmeralda'],
    ['--malo', '--blanco'],
    ['--esmeralda-texto', '--blanco'],
    ['--esmeralda-texto', '--esmeralda-suave'],
  ])('texto %s sobre %s llega a 4,5:1', (texto, fondo) => {
    expect(contraste(hex(texto), hex(fondo))).toBeGreaterThanOrEqual(4.5)
  })

  // WCAG 1.4.11: el borde de un campo tiene que verse (3:1) para saber donde se escribe.
  it('el borde de los campos se ve sobre blanco (3:1)', () => {
    expect(contraste(hex('--borde-campo'), hex('--blanco'))).toBeGreaterThanOrEqual(3)
  })

  // Lo que la guia prohibe porque NO se lee: si alguien lo usa, que sea a sabiendas.
  it('el ambar y el blanco sobre celeste no se leen, y por eso no se usan como texto', () => {
    expect(contraste(hex('--blanco'), hex('--celeste'))).toBeLessThan(3)
    expect(contraste(hex('--ambar'), hex('--blanco'))).toBeLessThan(3)
    // Quien pinta con ambar o celeste. Permitido: el celeste en iconos (no son texto) y el ambar
    // solo sobre las escenas pizarra (el pie y la cabecera oscura), donde da 10,2:1.
    const reglas = [...sinComentarios(css).matchAll(/([^{}]+)\{([^}]*)\}/g)]
    const malas = reglas
      .filter(([, , cuerpo]) => /(?<![-\w])color:\s*var\(--(ambar|celeste)\)/.test(cuerpo))
      .map(([, selector, cuerpo]) => ({ selector: selector.trim(), color: cuerpo.match(/(?<![-\w])color:\s*var\(--(ambar|celeste)\)/)[1] }))
      .filter(({ selector, color }) => color === 'celeste'
        ? !selector.split(',').every((s) => /\.icono\s*$/.test(s.trim()))
        : !selector.split(',').every((s) => /^\.(pie-|pagina-cabeza--amarillo\b)/.test(s.trim())))
    expect(malas).toEqual([])
  })
})

describe('guia v3: sin lo «tecnologico»', () => {
  it.each([['index.css', css], ['legal.css', legal]])('%s no tiene degradados', (_n, hoja) => {
    expect(sinComentarios(hoja)).not.toMatch(/gradient\(/)
  })

  it('no carga ni usa la fuente de codigo', () => {
    expect(sinComentarios(css)).not.toMatch(/JetBrains|var\(--mono\)/)
    expect(readFileSync(resolve(SRC, '..', 'index.html'), 'utf8')).not.toMatch(/JetBrains/)
  })

  it('el unico vidrio es la cabecera', () => {
    const conVidrio = [...sinComentarios(css).matchAll(/([^{}]+)\{[^}]*(?<!-webkit-)backdrop-filter:\s*blur/g)]
      .map((m) => m[1].trim())
    expect(conVidrio).toEqual(['.cabecera-fila'])
  })

  it.each([['index.css', css], ['legal.css', legal]])('%s solo usa variables que existen', (_n, hoja) => {
    const usadas = [...sinComentarios(hoja).matchAll(/var\((--[a-z0-9-]+)/g)].map((m) => m[1])
    // Las que se ponen desde el codigo (style={{ '--d': ... }}) y llevan valor por omision en el var().
    const delCodigo = new Set(['--d', '--orden', '--x', '--y', '--mx', '--my'])
    const faltan = [...new Set(usadas)].filter((v) => !(v in tokens) && !delCodigo.has(v))
    expect(faltan).toEqual([])
  })
})
