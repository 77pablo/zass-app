// @vitest-environment node
//
// P-11: TODA Function que llama a la API manda X-Origen-Zass cuando existe la variable
// SECRETO_ORIGEN, y no la manda cuando no existe. Lo que mande el visitante con ese nombre no
// se reenvia nunca. Request/Response de verdad y un fetch simulado (la API); NO prueba Cloudflare.
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { onRequest as proxyApi } from '../../functions/api/[[ruta]].js'
import { onRequest as tarjeta } from '../../functions/t/[segmento].js'
import { onRequest as ficha } from '../../functions/prestadores/[slug].js'
import { onRequest as sitemap } from '../../functions/sitemap.xml.js'
import { CABECERA_DE_ORIGEN, ponerSecretoDeOrigen, secretoDeOrigen } from '../../functions/_comun/origen.js'

const SECRETO = 'a1b2c3d4e5f60718293a4b5c6d7e8f90a1b2c3d4e5f60718293a4b5c6d7e8f9'
const API = 'https://api.ejemplo.test'

/** Las cabeceras con que cada llamada salio hacia la API, venga como Request o como (url, opciones). */
let enviadas

beforeEach(() => {
  enviadas = []
  vi.stubGlobal('fetch', vi.fn(async (entrada, opciones) => {
    enviadas.push(entrada instanceof Request ? entrada.headers : new Headers(opciones?.headers))
    return new Response(JSON.stringify({ prestadores: [], cursorSiguiente: null }), {
      status: 200, headers: { 'content-type': 'application/json' },
    })
  }))
})
afterEach(() => vi.unstubAllGlobals())

function entorno(extra = {}) {
  return {
    API_ORIGEN: API,
    ASSETS: { fetch: async () => new Response('<!doctype html><html><head><!--seo:inicio--><!--seo:fin--></head></html>') },
    ...extra,
  }
}

/** Cada Function que llama a la API, con un visitante que intenta colar su propia cabecera. */
const FUNCTIONS = {
  'proxy /api/*': (env) => proxyApi({
    request: new Request('https://zass.cl/api/planes', { headers: { [CABECERA_DE_ORIGEN]: 'del-visitante' } }),
    env,
  }),
  'tarjeta /t/:segmento': (env) => tarjeta({
    request: new Request('https://zass.cl/t/7KQ2-9XRM', { headers: { [CABECERA_DE_ORIGEN]: 'del-visitante' } }),
    env,
    params: { segmento: '7KQ2-9XRM' },
  }),
  'ficha /prestadores/:slug': (env) => ficha({
    request: new Request('https://zass.cl/prestadores/marcela-soto', { headers: { [CABECERA_DE_ORIGEN]: 'del-visitante' } }),
    env,
    params: { slug: 'marcela-soto' },
  }),
  'sitemap /sitemap.xml': (env) => sitemap({
    request: new Request('https://zass.cl/sitemap.xml', { headers: { [CABECERA_DE_ORIGEN]: 'del-visitante' } }),
    env,
    params: {},
  }),
}

describe.each(Object.entries(FUNCTIONS))('%s', (_nombre, llamar) => {
  it('con SECRETO_ORIGEN manda la cabecera con ese valor (y no el del visitante)', async () => {
    await llamar(entorno({ SECRETO_ORIGEN: SECRETO }))

    expect(enviadas.length).toBeGreaterThan(0)
    for (const h of enviadas) expect(h.get(CABECERA_DE_ORIGEN)).toBe(SECRETO)
  })

  it('sin SECRETO_ORIGEN no la manda, ni reenvia la del visitante', async () => {
    await llamar(entorno())

    expect(enviadas.length).toBeGreaterThan(0)
    for (const h of enviadas) expect(h.get(CABECERA_DE_ORIGEN)).toBeNull()
  })

  it('con SECRETO_ORIGEN vacio es como si no estuviera', async () => {
    await llamar(entorno({ SECRETO_ORIGEN: '   ' }))

    for (const h of enviadas) expect(h.get(CABECERA_DE_ORIGEN)).toBeNull()
  })
})

describe('_comun/origen.js', () => {
  it('lee el secreto sin espacios alrededor, y null si no hay', () => {
    expect(secretoDeOrigen({ SECRETO_ORIGEN: ` ${SECRETO}\n` })).toBe(SECRETO)
    expect(secretoDeOrigen({})).toBeNull()
    expect(secretoDeOrigen(undefined)).toBeNull()
    expect(secretoDeOrigen({ SECRETO_ORIGEN: '' })).toBeNull()
  })

  it('en un objeto simple quita la del visitante aunque venga con otras mayusculas', () => {
    const cabeceras = ponerSecretoDeOrigen({ 'x-origen-zass': 'del-visitante', accept: 'application/json' }, {})
    expect(cabeceras).toEqual({ accept: 'application/json' })
  })
})
