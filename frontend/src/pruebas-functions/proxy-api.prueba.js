// @vitest-environment node
//
// La Function de /api/* (functions/api/[[ruta]].js) con Request/Response de verdad y un fetch
// simulado (la API). NO prueba Cloudflare: prueba la logica del reenvio.
//
// Desde el 21-nov-2026 la activacion de la tarjeta (API-55, CU-19) pasa por aqui y no por /t/: es
// el unico camino de produccion que reenvia la cookie de sesion y el token CSRF, y el POST que
// la activa depende de que lleguen los dos. Antes ninguna prueba miraba este proxy.
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { onRequest } from '../../functions/api/[[ruta]].js'

const API = 'https://api.ejemplo.test'
let recibidas

beforeEach(() => {
  recibidas = []
  vi.stubGlobal('fetch', vi.fn(async (peticion) => {
    recibidas.push(peticion)
    return new Response('{"mensaje":"Tarjeta activada"}', { status: 200, headers: { 'content-type': 'application/json' } })
  }))
})
afterEach(() => vi.unstubAllGlobals())

const ACTIVAR = '/api/mi/tarjetas/7KQ2-9XRM/activar'

function pedir(ruta, { env = { API_ORIGEN: API }, metodo = 'POST', cabeceras = {}, cuerpo } = {}) {
  const request = new Request(`https://zass.cl${ruta}`, { method: metodo, headers: cabeceras, body: cuerpo })
  return onRequest({ request, env })
}

describe('Function /api/* (proxy hacia la API)', () => {
  it('reenvia el POST de activar a la misma ruta de la API, con la cookie, el token CSRF y el cuerpo', async () => {
    const r = await pedir(ACTIVAR, {
      cabeceras: {
        Cookie: 'zass_acceso=abc; zass_csrf=xyz',
        'X-CSRF-Token': 'token-del-formulario',
        'Content-Type': 'application/json',
      },
      cuerpo: JSON.stringify({ codigo: 'ABCD-EFGH-JKMN' }),
    })

    expect(r.status).toBe(200)
    expect(recibidas).toHaveLength(1)
    const p = recibidas[0]
    expect(p.url).toBe(`${API}${ACTIVAR}`)
    expect(p.method).toBe('POST')
    expect(p.headers.get('Cookie')).toBe('zass_acceso=abc; zass_csrf=xyz')
    expect(p.headers.get('X-CSRF-Token')).toBe('token-del-formulario')
    expect(await p.json()).toEqual({ codigo: 'ABCD-EFGH-JKMN' })
  })

  it('fija X-Forwarded-For/Proto/Host y no cree lo que mando el visitante en ellas', async () => {
    await pedir(ACTIVAR, { cabeceras: {
      'CF-Connecting-IP': '203.0.113.7',
      'X-Forwarded-For': '6.6.6.6', 'X-Forwarded-Proto': 'http', 'X-Forwarded-Host': 'otro.test',
    } })

    const p = recibidas[0]
    expect(p.headers.get('X-Forwarded-For')).toBe('203.0.113.7')
    expect(p.headers.get('X-Forwarded-Proto')).toBe('https')
    expect(p.headers.get('X-Forwarded-Host')).toBe('zass.cl')
  })

  it('conserva la consulta', async () => {
    await pedir('/api/buscar?q=gasfiter&pagina=2', { metodo: 'GET' })

    expect(recibidas[0].url).toBe(`${API}/api/buscar?q=gasfiter&pagina=2`)
  })

  it('sin API_ORIGEN responde 503 sin llamar a nadie', async () => {
    const r = await pedir(ACTIVAR, { env: {} })

    expect(r.status).toBe(503)
    expect(recibidas).toHaveLength(0)
  })
})
