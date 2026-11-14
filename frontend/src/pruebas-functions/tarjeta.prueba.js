// @vitest-environment node
//
// La Function de /t/:segmento con Request/Response de verdad y un fetch simulado (la API).
// NO prueba Cloudflare: prueba la logica del reenvio.
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { onRequest } from '../../functions/t/[segmento].js'
import { CABECERAS_DE_SEGURIDAD } from '../../functions/_comun/seo.js'

const API = 'https://api.ejemplo.test'
let llamadas
let respuestaApi

beforeEach(() => {
  llamadas = []
  respuestaApi = () => new Response(null, { status: 302, headers: { Location: '/prestadores/marcela-soto' } })
  vi.stubGlobal('fetch', vi.fn(async (url, opciones) => {
    llamadas.push({ url: String(url), opciones })
    return respuestaApi()
  }))
})
afterEach(() => vi.unstubAllGlobals())

function pedir(ruta, { env = { API_ORIGEN: API }, metodo = 'GET', cabeceras = {} } = {}) {
  const request = new Request(`https://zass.cl${ruta}`, { method: metodo, headers: cabeceras })
  const segmento = decodeURIComponent(new URL(request.url).pathname.split('/')[2])
  return onRequest({ request, env, params: { segmento } })
}

describe('Function /t/:segmento', () => {
  it('reenvia a la API con el segmento y la consulta, sin seguir la redireccion', async () => {
    await pedir('/t/7KQ2-9XRM?m=qr')

    expect(llamadas).toHaveLength(1)
    expect(llamadas[0].url).toBe(`${API}/t/7KQ2-9XRM?m=qr`)
    expect(llamadas[0].opciones.redirect).toBe('manual')
    expect(llamadas[0].opciones.method).toBe('GET')
  })

  it('fija X-Forwarded-For con la IP de Cloudflare y no reenvia cookies ni lo que mando el visitante', async () => {
    await pedir('/t/7KQ2-9XRM', { cabeceras: {
      'CF-Connecting-IP': '203.0.113.7', 'X-Forwarded-For': '6.6.6.6', Cookie: 'zass_sesion=abc',
    } })

    const enviadas = llamadas[0].opciones.headers
    expect(enviadas.get('X-Forwarded-For')).toBe('203.0.113.7')
    expect(enviadas.get('X-Forwarded-Proto')).toBe('https')
    expect(enviadas.get('Cookie')).toBeNull()
  })

  it('sin CF-Connecting-IP no inventa X-Forwarded-For', async () => {
    await pedir('/t/7KQ2-9XRM', { cabeceras: { 'X-Forwarded-For': '6.6.6.6' } })

    expect(llamadas[0].opciones.headers.get('X-Forwarded-For')).toBeNull()
  })

  it('devuelve el 302 con la Location relativa, sin cache, sin referer y con las cabeceras de seguridad', async () => {
    const r = await pedir('/t/7KQ2-9XRM')

    expect(r.status).toBe(302)
    expect(r.headers.get('Location')).toBe('/prestadores/marcela-soto')
    expect(r.headers.get('Cache-Control')).toBe('no-store')
    expect(r.headers.get('Referrer-Policy')).toBe('no-referrer')
    expect(r.headers.get('Content-Security-Policy')).toBe(CABECERAS_DE_SEGURIDAD['Content-Security-Policy'])
  })

  it('nunca deja pasar un Set-Cookie de la API', async () => {
    respuestaApi = () => new Response(null, { status: 302, headers: {
      Location: '/tarjeta/no-valida', 'Set-Cookie': 'rastreo=1',
    } })

    const r = await pedir('/t/7KQ2-9XRM')

    expect(r.headers.get('Set-Cookie')).toBeNull()
  })

  it.each(['https://otro.test/x', '//otro.test/x', '/\\otro.test/x', ''])(
    'una Location que no es ruta propia (%s) da 502', async (ubicacion) => {
      respuestaApi = () => new Response(null, { status: 302, headers: ubicacion ? { Location: ubicacion } : {} })

      const r = await pedir('/t/7KQ2-9XRM')

      expect(r.status).toBe(502)
      expect(r.headers.get('Location')).toBeNull()
    })

  it('el 429 de la API pasa con su Retry-After', async () => {
    respuestaApi = () => new Response('{}', { status: 429, headers: { 'Retry-After': '900' } })

    const r = await pedir('/t/7KQ2-9XRM')

    expect(r.status).toBe(429)
    expect(r.headers.get('Retry-After')).toBe('900')
  })

  it('con la API caida responde 503 y no se cuelga', async () => {
    respuestaApi = () => { throw new TypeError('fetch failed') }

    const r = await pedir('/t/7KQ2-9XRM')

    expect(r.status).toBe(503)
    expect(r.headers.get('Cache-Control')).toBe('no-store')
  })

  it('sin API_ORIGEN responde 503 sin llamar a nadie', async () => {
    const r = await pedir('/t/7KQ2-9XRM', { env: {} })

    expect(r.status).toBe(503)
    expect(llamadas).toHaveLength(0)
  })

  it('un metodo que no es GET da 405 sin llamar a la API', async () => {
    const r = await pedir('/t/7KQ2-9XRM', { metodo: 'POST' })

    expect(r.status).toBe(405)
    expect(llamadas).toHaveLength(0)
  })
})
