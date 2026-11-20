// @vitest-environment node
//
// Las cabeceras de seguridad del sitio estatico las pone public/_headers, que Cloudflare
// Pages aplica a las respuestas de /*. Esta prueba lee el archivo y cae si alguien quita una
// de las cuatro. No mide zass.cl en vivo: eso se hace con curl, cuando el sitio esté abierto.
import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'
import { describe, expect, it, vi } from 'vitest'
import { CABECERAS_DE_SEGURIDAD } from '../../functions/_comun/cabeceras.js'
import { onRequest as middleware } from '../../functions/_middleware.js'

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

// Las lineas «Nombre: valor» del bloque /* de public/_headers, como objeto.
function cabecerasDelBloque(texto, ruta) {
  return Object.fromEntries(bloque(texto, ruta)
    .filter((l) => !l.startsWith('#'))
    .map((l) => [l.slice(0, l.indexOf(':')).trim(), l.slice(l.indexOf(':') + 1).trim()]))
}

describe('las Functions llevan las mismas cabeceras que public/_headers (116)', () => {
  it('functions/_comun/cabeceras.js es IGUAL al bloque /* de public/_headers', () => {
    expect(CABECERAS_DE_SEGURIDAD).toEqual(cabecerasDelBloque(HEADERS, '/*'))
  })

  it('la pagina de mantenimiento (503) sale con todas', async () => {
    const respuesta = await middleware({
      request: new Request('https://zass.cl/', { headers: { accept: 'text/html' } }),
      env: {},
      next: vi.fn(),
    })

    expect(respuesta.status).toBe(503)
    for (const [nombre, valor] of Object.entries(CABECERAS_DE_SEGURIDAD)) {
      expect(respuesta.headers.get(nombre)).toBe(valor)
    }
  })

  it('con el sitio abierto, a lo que arma una Function se le agregan', async () => {
    const respuesta = await middleware({
      request: new Request('https://zass.cl/prestadores/ana'),
      env: { MANTENIMIENTO: '0' },
      next: async () => new Response('ficha', { status: 200, headers: { 'content-type': 'text/html' } }),
    })

    expect(respuesta.status).toBe(200)
    expect(await respuesta.text()).toBe('ficha')
    expect(respuesta.headers.get('X-Frame-Options')).toBe('DENY')
    expect(respuesta.headers.get('content-type')).toBe('text/html')
  })

  it('no pisa la politica que ya trae la respuesta (la de la API, por el proxy)', async () => {
    const respuesta = await middleware({
      request: new Request('https://zass.cl/api/salud'),
      env: { MANTENIMIENTO: '0' },
      next: async () => new Response('{}', { headers: { 'Content-Security-Policy': "default-src 'none'" } }),
    })

    expect(respuesta.headers.get('Content-Security-Policy')).toBe("default-src 'none'")
    expect(respuesta.headers.get('X-Content-Type-Options')).toBe('nosniff')
  })
})