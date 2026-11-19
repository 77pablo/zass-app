// @vitest-environment node
//
// «Sábado, 21 De Noviembre» salió en Mis citas porque el CSS tenía `text-transform: capitalize`,
// que sube la primera letra de CADA palabra. En castellano solo va con mayúscula la primera
// («Sábado, 21 de noviembre»), y eso lo hace `tituloDeDia` en el texto mismo. jsdom no aplica el
// CSS, así que las pruebas de las páginas no lo verían volver: esta mira las hojas y los estilos
// en línea de todo `src`.
import { readFileSync, readdirSync, statSync } from 'node:fs'
import { join, relative, resolve } from 'node:path'
import { describe, expect, it } from 'vitest'

const SRC = resolve(import.meta.dirname, '..')
const sinComentarios = (t) => t.replace(/\/\*[\s\S]*?\*\//g, '')

function archivos(dir) {
  return readdirSync(dir).flatMap((n) => {
    const p = join(dir, n)
    return statSync(p).isDirectory() ? archivos(p) : [p]
  })
}

describe('ningún texto se pasa a «Título De Inglés» con el CSS', () => {
  it('ninguna hoja de estilos usa text-transform: capitalize', () => {
    const hojas = archivos(SRC).filter((p) => p.endsWith('.css'))
    expect(hojas.length).toBeGreaterThan(0)
    const conCapitalize = hojas.filter((p) => /text-transform\s*:\s*capitalize/i.test(sinComentarios(readFileSync(p, 'utf8'))))
    expect(conCapitalize.map((p) => relative(SRC, p))).toEqual([])
  })

  it('ningún componente lo pone en línea (textTransform: "capitalize")', () => {
    const fuentes = archivos(SRC).filter((p) => /\.(tsx|ts|jsx)$/.test(p) && !/\.prueba\./.test(p))
    expect(fuentes.length).toBeGreaterThan(0)
    const conCapitalize = fuentes.filter((p) => /textTransform\s*:\s*['"]capitalize['"]/.test(readFileSync(p, 'utf8')))
    expect(conCapitalize.map((p) => relative(SRC, p))).toEqual([])
  })
})
