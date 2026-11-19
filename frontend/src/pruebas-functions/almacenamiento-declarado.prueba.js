// @vitest-environment node
//
// Lo que el sitio guarda en el navegador tiene que estar nombrado en la politica de privacidad (seccion 4,
// «Cookies y almacenamiento en tu navegador»). La auditoria T35 (18-nov-2026) encontro que una clave nueva
// de localStorage (zass-busquedas, del #98) llegaba sin que la politica la declarara. Esto lee el CODIGO:
// cada archivo de src/ que toca localStorage o sessionStorage, y cada clave 'zass-...' / 'zass_...' que
// aparece en el. Hoy es solo zass-sesion; el dia que llegue otra, cae hasta que la politica la nombre.
import { readdirSync, readFileSync, statSync } from 'node:fs'
import { join, resolve } from 'node:path'
import { describe, expect, it } from 'vitest'

const SRC = resolve(import.meta.dirname, '..')

function archivos(dir) {
  return readdirSync(dir).flatMap((n) => {
    const ruta = join(dir, n)
    if (statSync(ruta).isDirectory()) return archivos(ruta)
    return /\.(ts|tsx|js)$/.test(n) && !/\.prueba\./.test(n) ? [ruta] : []
  })
}

/** El texto de la seccion 4 de Privacidad.tsx: desde su <h2> hasta el <h2> siguiente. */
function seccionDeAlmacenamiento() {
  const fuente = readFileSync(resolve(SRC, 'paginas', 'Privacidad.tsx'), 'utf8')
  const desde = fuente.indexOf('Cookies y almacenamiento en tu navegador')
  expect(desde, 'no encuentro la seccion 4 en Privacidad.tsx').toBeGreaterThan(0)
  const hasta = fuente.indexOf('<h2>', desde)
  return fuente.slice(desde, hasta > 0 ? hasta : undefined)
}

describe('el almacenamiento del navegador esta declarado en la politica', () => {
  it('cada clave de localStorage o sessionStorage del codigo aparece en la seccion 4', () => {
    const claves = new Set()
    for (const ruta of archivos(SRC)) {
      const codigo = readFileSync(ruta, 'utf8')
      if (!/\b(localStorage|sessionStorage)\b/.test(codigo)) continue
      for (const m of codigo.matchAll(/['"`](zass[-_][a-z0-9_-]+)['"`]/gi)) claves.add(m[1])
    }
    // Sin esto, una prueba que no encuentra nada (por una ruta mal puesta) daria verde.
    expect(claves.size).toBeGreaterThan(0)
    const seccion = seccionDeAlmacenamiento()
    expect([...claves].filter((c) => !seccion.includes(`<strong>${c}</strong>`))).toEqual([])
  })
})
