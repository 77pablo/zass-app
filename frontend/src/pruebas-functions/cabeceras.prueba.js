// @vitest-environment node
//
// Las cabeceras de seguridad del sitio estatico las pone public/_headers, que Cloudflare
// Pages aplica a las respuestas de /*. Esta prueba lee el archivo y cae si alguien quita una
// de las cuatro. No mide zass.cl en vivo: eso se hace con curl, cuando el sitio esté abierto.
import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'
import { describe, expect, it } from 'vitest'

const RAIZ = resolve(import.meta.dirname, '..', '..')
const HEADERS = readFileSync(resolve(RAIZ, 'public', '_headers'), 'utf8')

const CABECERAS = [
  'Strict-Transport-Security',
  'X-Content-Type-Options',
  'Referrer-Policy',
  'Permissions-Policy',
]

// Lineas indentadas que cuelgan de una ruta del archivo, sin tocar el bloque siguiente.
function bloque(texto, ruta) {
  const lineas = texto.split(/\r?\n/)
  const inicio = lineas.findIndex((l) => l.trim() === ruta)
  if (inicio < 0) return []
  const resto = lineas.slice(inicio + 1)
  const fin = resto.findIndex((l) => l.length > 0 && !/^\s/.test(l) && !l.startsWith('#'))
  return (fin < 0 ? resto : resto.slice(0, fin)).map((l) => l.trim()).filter(Boolean)
}

describe('public/_headers', () => {
  it('tiene un bloque /* que cubre todas las paginas', () => {
    expect(bloque(HEADERS, '/*').length).toBeGreaterThan(0)
  })

  it.each(CABECERAS)('el bloque /* declara %s', (nombre) => {
    const lineas = bloque(HEADERS, '/*').map((l) => l.toLowerCase())
    expect(lineas.some((l) => l.startsWith(`${nombre.toLowerCase()}:`))).toBe(true)
  })
})