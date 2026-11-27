// @vitest-environment node
//
// La pagina de mantenimiento con la guia v3, vigilada. mantenimiento.prueba.js cuida que el sitio
// no se abra por accidente; esta cuida lo que se VE mientras esta cerrado: que no vuelva un color
// fuera de la guia (el amarillo y la crema de antes, un naranja, un degradado), que lo que se lee
// tenga contraste, y que la pagina no pida nada al propio sitio, porque con el sitio cerrado este
// mismo middleware contesta cada recurso con un 503 en HTML.
import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'
import { describe, expect, it } from 'vitest'
import { onRequest } from '../../functions/_middleware.js'

const FRONTEND = resolve(import.meta.dirname, '..', '..')
const sinComentarios = (t) => t.replace(/\/\*[\s\S]*?\*\//g, '')

/** Los tokens de la guia, tal como estan en src/index.css. */
const tokensDelSitio = Object.fromEntries(
  [...sinComentarios(readFileSync(resolve(FRONTEND, 'src', 'index.css'), 'utf8'))
    .matchAll(/^\s*(--[a-z0-9-]+):\s*(#[0-9a-f]{6});/gim)]
    .map((m) => [m[1], m[2].toLowerCase()]),
)

async function paginaHtml() {
  const respuesta = await onRequest({
    request: new Request('https://zass.cl/', { headers: { accept: 'text/html' } }),
    env: {},
    next: async () => new Response('no deberia llegar aqui'),
  })
  expect(respuesta.status).toBe(503)
  return respuesta.text()
}

const estilo = (html) => sinComentarios(html.match(/<style>([\s\S]*?)<\/style>/)[1])

/** Los tokens que define la propia pagina en su :root. */
function tokensDeLaPagina(html) {
  const raiz = estilo(html).match(/:root\s*\{([^}]*)\}/)[1]
  return Object.fromEntries([...raiz.matchAll(/(--[a-z0-9-]+):\s*([^;]+);/g)].map((m) => [m[1], m[2].trim().toLowerCase()]))
}

function luminancia(h) {
  const [r, g, b] = [1, 3, 5].map((i) => parseInt(h.slice(i, i + 2), 16) / 255)
    .map((c) => (c <= 0.03928 ? c / 12.92 : ((c + 0.055) / 1.055) ** 2.4))
  return 0.2126 * r + 0.7152 * g + 0.0722 * b
}
function contraste(a, b) {
  const [x, y] = [luminancia(a), luminancia(b)].sort((p, q) => q - p)
  return (x + 0.05) / (y + 0.05)
}

describe('mantenimiento: la guia v3', () => {
  it('solo usa colores de la guia (sin el rojo de error, que aqui no hay errores)', async () => {
    const html = await paginaHtml()
    const permitidos = new Set(Object.entries(tokensDelSitio)
      .filter(([nombre]) => !nombre.startsWith('--malo'))
      .map(([, valor]) => valor))
    expect(permitidos.size).toBeGreaterThan(5)

    // Los hex del HTML entero: el <style>, el icono en linea y el theme-color.
    const usados = [...sinComentarios(html).matchAll(/#([0-9a-f]{6}|[0-9a-f]{3})\b/gi)]
      .map((m) => (m[1].length === 3 ? `#${[...m[1]].map((c) => c + c).join('')}` : `#${m[1]}`).toLowerCase())
    expect(usados.length).toBeGreaterThan(0)
    expect([...new Set(usados)].filter((c) => !permitidos.has(c))).toEqual([])
  })

  it('no trae degradados, ni colores por nombre, ni rgb/hsl sueltos', async () => {
    const css = estilo(await paginaHtml())
    expect(css).not.toMatch(/gradient\(/)
    expect(css).not.toMatch(/\b(rgb|rgba|hsl|hsla)\(/)
    expect(css).not.toMatch(/:\s*(orange|red|yellow|gold|crimson|tomato|coral)\b/i)
  })

  it('cada token de la pagina vale lo mismo que en src/index.css, y no usa variables que no define', async () => {
    const html = await paginaHtml()
    const propios = tokensDeLaPagina(html)
    for (const [nombre, valor] of Object.entries(propios)) {
      if (!nombre.startsWith('--')) continue
      expect(valor, `${nombre} no coincide con index.css`).toBe(tokensDelSitio[nombre])
    }
    const usadas = new Set([...estilo(html).matchAll(/var\((--[a-z0-9-]+)/g)].map((m) => m[1]))
    expect([...usadas].filter((v) => !(v in propios))).toEqual([])
  })

  it('el ambar y el celeste nunca son color de texto', async () => {
    expect(estilo(await paginaHtml())).not.toMatch(/(?<![-\w])color:\s*var\(--(ambar|ambar-hondo|celeste)\)/)
  })

  // Cada pareja texto/fondo que la pagina usa. 4,5:1 es el minimo de WCAG AA para texto normal.
  it.each([
    ['--pizarra', '--blanco'], // titulo
    ['--suave', '--blanco'], // cuerpo
    ['--pizarra', '--gris'], // recuadro de prestadores
    ['--blanco', '--pizarra'], // la franja con el logo
    ['--pizarra', '--ambar'], // el boton del correo y el «!»
    ['--pizarra', '--ambar-hondo'], // el boton al pasar el mouse
    ['--blanco', '--pizarra'], // el pie
  ])('texto %s sobre %s llega a 4,5:1', async (texto, fondo) => {
    const t = tokensDeLaPagina(await paginaHtml())
    expect(contraste(t[texto], t[fondo])).toBeGreaterThanOrEqual(4.5)
  })

  it('no pide nada al propio sitio: con el sitio cerrado, eso tambien contestaria 503', async () => {
    const html = await paginaHtml()
    const referencias = [...html.matchAll(/\b(?:src|href)="([^"]*)"/g)].map((m) => m[1])
    expect(referencias.length).toBeGreaterThan(0)
    const malas = referencias.filter((r) => !/^(mailto:|data:|https:\/\/fonts\.(googleapis|gstatic)\.com\/?)/.test(r))
    expect(malas).toEqual([])
    expect(html).not.toMatch(/url\(\s*['"]?\//)
  })

  // Pablo, 26-nov-2026: «el logo de zass es amarillo, eso no se cambia por nada».
  it('el logo es el amarillo de la marca, sobre la franja pizarra, y tiene nombre', async () => {
    const html = await paginaHtml()
    expect(html).toMatch(/<svg class="logo"[^>]*role="img"[^>]*aria-label="ZASS!"/)
    expect(html).toMatch(/fill="#F6C81E" fill-rule="evenodd"/)
    expect(tokensDeLaPagina(html)['--logo']).toBe('#f6c81e')
    expect(estilo(html)).toMatch(/\.franja\s*\{[^}]*background:\s*var\(--pizarra\)/)
    // El amarillo sobre pizarra se ve; sobre el celeste de antes no (1,6:1).
    const t = tokensDeLaPagina(html)
    expect(contraste(t['--logo'], t['--pizarra'])).toBeGreaterThanOrEqual(4.5)
  })

  it('el icono de la pestaña es el mismo dibujo que public/favicon.svg', async () => {
    const html = await paginaHtml()
    const favicon = decodeURIComponent(html.match(/<link rel="icon" type="image\/svg\+xml" href="data:image\/svg\+xml,([^"]+)"/)[1])
    const delArchivo = readFileSync(resolve(FRONTEND, 'public', 'favicon.svg'), 'utf8')
    for (const trazo of delArchivo.matchAll(/<path d="([^"]+)"/g)) expect(favicon).toContain(trazo[1])
  })

  it('se puede hacer zoom en el celular', async () => {
    const viewport = (await paginaHtml()).match(/<meta name="viewport" content="([^"]+)"/)[1]
    expect(viewport).not.toMatch(/user-scalable\s*=\s*no|maximum-scale\s*=\s*1(?!\d)/)
  })
})
