// @vitest-environment node
//
// La interfaz de ZASS! se usa en la calle, desde el celular, despues de tocar una tarjeta NFC:
// «Agregar a la pantalla de inicio» tiene que salir con el nombre y el icono de la marca, no con
// los genericos del navegador. Esta prueba vigila la cadena completa de la instalacion: el
// manifiesto existe y tiene los campos obligatorios, index.html lo enlaza, los iconos que declara
// son archivos reales del tamano que dicen, y ni `_headers` ni `robots.txt` los estorban.
//
// Limite deliberado: aqui NO hay service worker ni cache offline. Es otra tarea.
import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'
import { describe, expect, it } from 'vitest'

const RAIZ = resolve(import.meta.dirname, '..', '..')
const publico = (nombre) => resolve(RAIZ, 'public', nombre)
const texto = (ruta) => readFileSync(ruta, 'utf8')

const AMARILLO = '#F6C81E'
const TINTA = '#1A1A1A'

const RUTA_MANIFIESTO = '/manifest.webmanifest'
const manifiesto = JSON.parse(texto(publico('manifest.webmanifest')))
const indice = texto(resolve(RAIZ, 'index.html'))

/** Ancho y alto de un PNG, leidos del IHDR (bytes 16..24). No vale con que el archivo exista. */
function tamanoPng(nombre) {
  const b = readFileSync(publico(nombre))
  expect(b.subarray(1, 4).toString('latin1')).toBe('PNG')
  return { ancho: b.readUInt32BE(16), alto: b.readUInt32BE(20) }
}

describe('manifiesto web (PWA instalable)', () => {
  it('lleva la marca: nombre con doble S y el lema', () => {
    expect(manifiesto.name).toBe('ZASS!')
    expect(manifiesto.short_name).toBe('ZASS!')
    expect(manifiesto.description).toContain('Conecta con un toque')
  })

  it.each(['name', 'short_name', 'start_url', 'scope', 'display', 'background_color', 'theme_color', 'icons'])(
    'declara el campo obligatorio %s', (campo) => {
      expect(manifiesto[campo], `falta "${campo}" en manifest.webmanifest`).toBeTruthy()
    })

  it('se abre en la raiz, en modo aplicacion y con todo el sitio dentro del alcance', () => {
    expect(manifiesto.start_url).toBe('/')
    expect(manifiesto.scope).toBe('/')
    expect(manifiesto.display).toBe('standalone')
    expect(manifiesto.lang).toBe('es-CL')
  })

  // El sitio es oscuro. Si la pantalla de arranque fuera crema, al abrir desde el icono se veria
  // un fogonazo claro antes de la aplicacion: por eso las dos van en tinta, y el crema de la marca
  // se queda fuera del manifiesto (se usa en superficies claras del sitio, no aqui).
  it('usa los colores de la marca y no inventa otros', () => {
    expect(manifiesto.theme_color.toUpperCase()).toBe(TINTA)
    expect(manifiesto.background_color.toUpperCase()).toBe(TINTA)
  })

  it('declara 192, 512 y el maskable, y los archivos miden lo que dicen', () => {
    const porTamano = Object.fromEntries(manifiesto.icons.map((i) => [`${i.sizes} ${i.purpose ?? 'any'}`, i]))
    expect(Object.keys(porTamano).sort()).toEqual(['192x192 any', '512x512 any', '512x512 maskable'])
    for (const icono of manifiesto.icons) {
      expect(icono.type).toBe('image/png')
      expect(icono.src.startsWith('/')).toBe(true)
      const [ancho, alto] = icono.sizes.split('x').map(Number)
      expect(tamanoPng(icono.src.slice(1)), `${icono.src} no mide ${icono.sizes}`).toEqual({ ancho, alto })
    }
  })

  it('el icono nace del amarillo de la marca que ya estaba en el repo, no de una marca nueva', () => {
    for (const fuente of ['icono-zass.svg', 'icono-zass-maskable.svg']) {
      const svg = texto(publico(fuente)).toUpperCase()
      expect(svg).toContain(AMARILLO)
      expect(svg).toContain(TINTA)
    }
  })
})

describe('index.html enlaza el manifiesto', () => {
  it('tiene <link rel="manifest">', () => {
    expect(indice).toMatch(/<link[^>]+rel="manifest"[^>]+href="\/manifest\.webmanifest"/)
  })

  it('tiene un apple-touch-icon que existe de verdad (iOS ignora el manifiesto)', () => {
    const m = indice.match(/<link[^>]+rel="apple-touch-icon"[^>]+href="([^"]+)"/)
    expect(m, 'falta <link rel="apple-touch-icon"> en index.html').not.toBeNull()
    expect(tamanoPng(m[1].slice(1))).toEqual({ ancho: 180, alto: 180 })
  })

  it('el theme-color de la etiqueta y el del manifiesto dicen lo mismo', () => {
    const m = indice.match(/<meta name="theme-color" content="([^"]+)"/)
    expect(m).not.toBeNull()
    expect(m[1].toUpperCase()).toBe(manifiesto.theme_color.toUpperCase())
  })

  it('el enlace queda FUERA de las marcas seo, que la Function de las fichas reemplaza entero', () => {
    const entreMarcas = indice.slice(indice.indexOf('<!--seo:inicio-->'), indice.indexOf('<!--seo:fin-->'))
    expect(entreMarcas).not.toContain('rel="manifest"')
    expect(entreMarcas).not.toContain('apple-touch-icon')
  })
})

describe('Cloudflare sirve el manifiesto como corresponde', () => {
  const cabeceras = texto(publico('_headers'))

  it('_headers le da el tipo de contenido del manifiesto', () => {
    const bloque = cabeceras.split(/\r?\n\r?\n/)
      .find((b) => b.split(/\r?\n/).some((l) => l.trim() === RUTA_MANIFIESTO))
    expect(bloque, `falta el bloque ${RUTA_MANIFIESTO} en public/_headers`).toBeDefined()
    expect(bloque).toMatch(/^\s+Content-Type: application\/manifest\+json(; charset=utf-8)?$/m)
  })

  it('robots.txt no estorba al manifiesto ni a los iconos', () => {
    const robots = texto(publico('robots.txt'))
    const prohibidos = [...robots.matchAll(/^Disallow: (\S+)$/gm)].map((m) => m[1])
    const rutas = [RUTA_MANIFIESTO, ...manifiesto.icons.map((i) => i.src)]
    for (const ruta of rutas)
      expect(prohibidos.filter((p) => ruta.startsWith(p)), `robots.txt bloquea ${ruta}`).toEqual([])
  })
})
