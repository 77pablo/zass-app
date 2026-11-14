// @vitest-environment node
//
// sitemap.xml, los archivos del indice y robots.txt. La API es un fetch simulado que pagina por cursor
// igual que GET /api/prestadores/mapa.
import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { onRequest as sitemapRaiz } from '../../functions/sitemap.xml.js'
import { onRequest as sitemapNumerado } from '../../functions/sitemaps/[n].js'
import { entradas, fechaCorta, MAXIMO_URLS, partir, RUTAS_ESTATICAS, servirSitemap } from '../../functions/_comun/sitemap.js'

const RAIZ = resolve(import.meta.dirname, '..', '..')
const ENV = { API_ORIGEN: 'https://api.ejemplo.test' }

let slugs
let llamadas
let modoApi

/** Una API de mentira con la misma forma que API-64: ordena por slug, pagina por cursor, corrige el limite. */
function instalarApi() {
  vi.stubGlobal('fetch', vi.fn(async (url) => {
    const u = new URL(String(url))
    llamadas.push(u)
    if (modoApi === 'caida') return new Response('x', { status: 503 })
    if (modoApi === 'revienta') throw new TypeError('red')
    if (modoApi === 'basura') return new Response('<html>', { status: 200 })
    const limite = Math.min(Math.max(Number(u.searchParams.get('limite')) || 500, 1), 1000)
    const cursor = u.searchParams.get('cursor')
    const ordenados = [...slugs].sort()
    const desde = cursor ? ordenados.filter((s) => s > cursor) : ordenados
    const pagina = desde.slice(0, limite)
    return new Response(JSON.stringify({
      prestadores: pagina.map((slug) => ({ slug, publicadoEn: '2026-11-01T10:30:00Z' })),
      cursorSiguiente: desde.length > limite ? pagina.at(-1) : null,
    }), { status: 200, headers: { 'content-type': 'application/json' } })
  }))
}

function pedir(ruta = '/sitemap.xml', env = ENV, metodo = 'GET') {
  const request = new Request(`https://zass.cl${ruta}`, { method: metodo })
  if (ruta.startsWith('/sitemaps/')) return sitemapNumerado({ request, env, params: { n: ruta.split('/').pop() } })
  return sitemapRaiz({ request, env, params: {} })
}

const locs = (xml) => [...xml.matchAll(/<loc>([^<]*)<\/loc>/g)].map((m) => m[1])

beforeEach(() => { slugs = ['marcela-soto', 'ana-perez']; llamadas = []; modoApi = 'ok'; instalarApi() })
afterEach(() => vi.unstubAllGlobals())

describe('/sitemap.xml', () => {
  it('lista la portada, las paginas fijas y todas las fichas', async () => {
    const r = await pedir()
    expect(r.status).toBe(200)
    expect(r.headers.get('content-type')).toMatch(/application\/xml/)
    expect(r.headers.get('x-content-type-options')).toBe('nosniff')
    expect(r.headers.get('cache-control')).toMatch(/s-maxage=3600/)
    const xml = await r.text()
    expect(xml).toContain('<urlset xmlns="http://www.sitemaps.org/schemas/sitemap/0.9">')
    const urls = locs(xml)
    expect(urls).toContain('https://zass.cl/')
    for (const ruta of ['/buscar', '/comunas', '/oficios', '/profesionales', '/terminos', '/privacidad'])
      expect(urls).toContain(`https://zass.cl${ruta}`)
    expect(urls).toContain('https://zass.cl/prestadores/marcela-soto')
    expect(urls).toContain('https://zass.cl/prestadores/ana-perez')
    expect(urls).toHaveLength(RUTAS_ESTATICAS.length + 2)
  })

  it('no lista paginas privadas', async () => {
    const xml = await (await pedir()).text()
    for (const privada of ['/mi-ficha', '/interno', '/entrar', '/crear-cuenta', '/restablecer-contrasena', '/verificar-correo', '/recuperar-contrasena', '/api'])
      expect(xml).not.toContain(privada)
  })

  it('pone lastmod con la fecha de publicacion y no inventa fechas', async () => {
    const xml = await (await pedir()).text()
    expect(xml).toContain('<loc>https://zass.cl/prestadores/ana-perez</loc><lastmod>2026-11-01</lastmod>')
    expect(xml).toMatch(/<loc>https:\/\/zass\.cl\/buscar<\/loc><\/url>/)
    expect(fechaCorta('no es fecha')).toBeNull()
    expect(fechaCorta(null)).toBeNull()
    expect(fechaCorta('2026-11-01T23:59:59Z')).toBe('2026-11-01')
  })

  it('recorre TODAS las paginas del cursor (mas de 1000 fichas)', async () => {
    slugs = Array.from({ length: 2503 }, (_, i) => `persona-${String(i).padStart(5, '0')}`)
    const urls = locs(await (await pedir()).text())
    expect(urls).toHaveLength(RUTAS_ESTATICAS.length + 2503)
    expect(llamadas).toHaveLength(3)
    expect(llamadas[0].searchParams.get('cursor')).toBeNull()
    expect(llamadas[1].searchParams.get('cursor')).toBe('persona-00999')
    expect(llamadas[0].searchParams.get('limite')).toBe('1000')
    expect(new Set(urls).size).toBe(urls.length)
  })

  it('codifica y escapa un slug raro en vez de romper el XML', async () => {
    slugs = ['a&b<c>"d\'e f']
    const xml = await (await pedir()).text()
    expect(xml).not.toContain('a&b')
    expect(xml).not.toContain('<c>')
    expect(xml).toContain('prestadores/a%26b%3Cc%3E%22d&#39;e%20f')
  })

  it('un HEAD no lleva cuerpo y otros metodos dan 405', async () => {
    expect(await (await pedir('/sitemap.xml', ENV, 'HEAD')).text()).toBe('')
    expect((await pedir('/sitemap.xml', ENV, 'POST')).status).toBe(405)
  })
})

describe('la API no esta', () => {
  it.each(['caida', 'revienta', 'basura'])('responde 503 con Retry-After, nunca 500 ni un sitemap a medias (%s)', async (modo) => {
    modoApi = modo
    const r = await pedir()
    expect(r.status).toBe(503)
    expect(r.headers.get('retry-after')).toBe('300')
    expect(r.headers.get('cache-control')).toBe('no-store')
    expect(await r.text()).not.toContain('<urlset')
  })

  it('sin API_ORIGEN tambien 503', async () => {
    expect((await pedir('/sitemap.xml', {})).status).toBe(503)
  })

  it('si falla a mitad del recorrido no entrega lo recorrido hasta ahi', async () => {
    slugs = Array.from({ length: 2500 }, (_, i) => `p-${String(i).padStart(5, '0')}`)
    let n = 0
    const buena = globalThis.fetch
    vi.stubGlobal('fetch', vi.fn(async (...a) => (++n === 2 ? new Response('x', { status: 500 }) : buena(...a))))
    expect((await pedir()).status).toBe(503)
  })
})

describe('mas de 50 000 URLs: indice de sitemaps', () => {
  it('partir() corta en trozos de a lo mas el maximo', () => {
    expect(MAXIMO_URLS).toBe(50_000)
    expect(partir([1, 2, 3, 4, 5], 2)).toEqual([[1, 2], [3, 4], [5]])
    expect(partir([], 2)).toEqual([[]])
    expect(partir([1, 2], 2)).toEqual([[1, 2]])
  })

  it('con un maximo chico, /sitemap.xml pasa a ser un indice y cada archivo trae su parte', async () => {
    slugs = Array.from({ length: 20 }, (_, i) => `p-${String(i).padStart(3, '0')}`)
    const total = RUTAS_ESTATICAS.length + 20
    const indice = await (await servirSitemap({ request: new Request('https://zass.cl/sitemap.xml'), env: ENV }, 0, 10)).text()
    expect(indice).toContain('<sitemapindex')
    const trozos = Math.ceil(total / 10)
    expect(locs(indice)).toEqual(Array.from({ length: trozos }, (_, i) => `https://zass.cl/sitemaps/${i + 1}.xml`))

    const todas = []
    for (let i = 1; i <= trozos; i++) {
      const r = await servirSitemap({ request: new Request(`https://zass.cl/sitemaps/${i}.xml`), env: ENV }, i, 10)
      expect(r.status).toBe(200)
      const urls = locs(await r.text())
      expect(urls.length).toBeLessThanOrEqual(10)
      todas.push(...urls)
    }
    expect(todas).toHaveLength(total)
    expect(new Set(todas).size).toBe(total)
    const fuera = await servirSitemap({ request: new Request('https://zass.cl/sitemaps/99.xml'), env: ENV }, 99, 10)
    expect(fuera.status).toBe(404)
  })

  it('de punta a punta: con 51 000 fichas (plan de pago) hay indice y ningun archivo pasa de 50 000', async () => {
    slugs = Array.from({ length: 51_000 }, (_, i) => `persona-${String(i).padStart(6, '0')}`)
    const env = { ...ENV, SITEMAP_MAX_PAGINAS: '100' }
    const indice = await (await pedir('/sitemap.xml', env)).text()
    expect(indice).toContain('<sitemapindex')
    expect(locs(indice)).toHaveLength(2)
    const primero = locs(await (await pedir('/sitemaps/1.xml', env)).text())
    const segundo = locs(await (await pedir('/sitemaps/2.xml', env)).text())
    expect(primero).toHaveLength(50_000)
    expect(primero.length + segundo.length).toBe(RUTAS_ESTATICAS.length + 51_000)
  })

  it('sin indice, /sitemaps/1.xml no existe', async () => {
    expect((await pedir('/sitemaps/1.xml')).status).toBe(404)
  })

  it('el presupuesto de subpeticiones se respeta: por omision 45 paginas como maximo', async () => {
    slugs = Array.from({ length: 46_000 }, (_, i) => `persona-${String(i).padStart(6, '0')}`)
    await pedir()
    expect(llamadas).toHaveLength(45)
  })

  it('entradas() separa fijas y fichas', () => {
    const e = entradas('https://zass.cl', [{ slug: 'x', publicadoEn: null }])
    expect(e.at(-1)).toEqual({ loc: 'https://zass.cl/prestadores/x', lastmod: null })
  })
})

describe('robots.txt', () => {
  const robots = readFileSync(resolve(RAIZ, 'public', 'robots.txt'), 'utf8')

  it('apunta al sitemap', () => {
    expect(robots).toMatch(/^Sitemap: https:\/\/zass\.cl\/sitemap\.xml$/m)
  })

  const RUTAS_DEL_PANEL = ['/mi-ficha', '/mi-agenda', '/mis-citas', '/mis-resenas']

  it.each([...RUTAS_DEL_PANEL, '/interno', '/entrar', '/crear-cuenta', '/restablecer-contrasena', '/verificar-correo'])(
    'no deja indexar %s', (ruta) => {
      expect(robots).toMatch(new RegExp(`^Disallow: ${ruta}$`, 'm'))
    })

  // El hermano del candado. Esta lista se escribia a mano, y `/mi-agenda`, `/mis-citas` y `/mis-resenas`
  // se anadieron a App.tsx sin volver a mirar aqui: quedaron indexables. Ahora la lista se compara con
  // las rutas que App.tsx declara de verdad, asi que la proxima pantalla del panel no se puede escapar.
  it('cubre TODAS las rutas del panel que declara App.tsx', () => {
    const app = readFileSync(resolve(RAIZ, 'src', 'App.tsx'), 'utf8')
    const enApp = [...app.matchAll(/path="(mis?-[^"]*)"/g)].map((m) => `/${m[1]}`)
    expect(enApp.length).toBeGreaterThan(0)
    expect(enApp.filter((r) => !RUTAS_DEL_PANEL.includes(r))).toEqual([])
  })

  it('deja indexar lo publico', () => {
    for (const ruta of ['/', '/buscar', '/comunas', '/oficios', '/profesionales', '/prestadores', '/terminos', '/privacidad'])
      expect(robots).not.toMatch(new RegExp(`^Disallow: ${ruta}$`, 'm'))
    expect(robots).not.toMatch(/^Disallow: \/$/m)
  })
})
