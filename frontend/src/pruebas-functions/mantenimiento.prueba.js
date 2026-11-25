/*
 * El interruptor de mantenimiento. Lo que se vigila aqui no es que la pagina sea bonita:
 * es que NO SE PUEDA abrir el sitio por accidente, y que cuando se abra, se abra entero.
 *
 * El caso que mas importa es el primero: sin ninguna variable configurada, el sitio esta
 * CERRADO. Si alguna vez esta prueba se pone verde al reves, significa que un despliegue
 * sin variables deja ZASS! a la vista antes de tiempo.
 */

import { describe, expect, it, vi } from 'vitest'
import { onRequest } from '../../functions/_middleware.js'

const PASE = 'pase-largo-de-prueba-123456'

function peticion(url = 'https://zass.cl/', { cabeceras = {} } = {}) {
  return new Request(url, { headers: { accept: 'text/html', ...cabeceras } })
}

async function pedir(peticionDada, env = {}) {
  const siguiente = vi.fn(async () => new Response('el sitio de verdad', {
    status: 200,
    headers: { 'content-type': 'text/html' },
  }))
  const respuesta = await onRequest({ request: peticionDada, env, next: siguiente })
  return { respuesta, siguiente }
}

describe('mantenimiento', () => {
  it('sin ninguna variable configurada el sitio esta CERRADO', async () => {
    const { respuesta, siguiente } = await pedir(peticion())

    expect(respuesta.status).toBe(503)
    expect(siguiente).not.toHaveBeenCalled()
  })

  it('la pagina dice que volvemos y no pide ser indexada', async () => {
    const { respuesta } = await pedir(peticion())
    const html = await respuesta.text()

    expect(html).toContain('ZAS')
    expect(html.toLowerCase()).toContain('muy pronto')
    expect(respuesta.headers.get('x-robots-tag')).toContain('noindex')
    expect(respuesta.headers.get('retry-after')).toBe('3600')
    expect(respuesta.headers.get('cache-control')).toContain('no-store')
  })

  it('con MANTENIMIENTO=0 el sitio se abre entero', async () => {
    const { respuesta, siguiente } = await pedir(peticion(), { MANTENIMIENTO: '0' })

    expect(respuesta.status).toBe(200)
    expect(siguiente).toHaveBeenCalledTimes(1)
    expect(await respuesta.text()).toBe('el sitio de verdad')
  })

  it('cualquier otro valor deja el sitio cerrado, incluido el vacio', async () => {
    for (const valor of ['', '1', 'si', 'true', 'off', 'mantenimiento']) {
      const { respuesta } = await pedir(peticion(), { MANTENIMIENTO: valor })
      expect(respuesta.status, `MANTENIMIENTO=${valor}`).toBe(503)
    }
  })

  it('lo que no es un navegador recibe problem+json y no una pagina', async () => {
    const { respuesta } = await pedir(
      peticion('https://zass.cl/api/planes', { cabeceras: { accept: 'application/json' } }),
    )

    expect(respuesta.status).toBe(503)
    expect(respuesta.headers.get('content-type')).toContain('application/problem+json')
    const cuerpo = await respuesta.json()
    expect(cuerpo.status).toBe(503)
  })

  it('el pase correcto deja una cookie y redirige sin el parametro', async () => {
    const { respuesta, siguiente } = await pedir(
      peticion(`https://zass.cl/buscar?q=abogado&pase=${PASE}`),
      { MANTENIMIENTO_PASE: PASE },
    )

    expect(respuesta.status).toBe(302)
    // El pase no queda en el historial ni en un enlace compartido.
    expect(respuesta.headers.get('location')).toBe('/buscar?q=abogado')
    const cookie = respuesta.headers.get('set-cookie')
    expect(cookie).toContain('zass_pase=')
    expect(cookie).toContain('HttpOnly')
    expect(cookie).toContain('Secure')
    expect(siguiente).not.toHaveBeenCalled()
  })

  it('con la cookie del pase se ve el sitio de verdad', async () => {
    const { respuesta, siguiente } = await pedir(
      peticion('https://zass.cl/', { cabeceras: { cookie: `zass_pase=${PASE}` } }),
      { MANTENIMIENTO_PASE: PASE },
    )

    expect(respuesta.status).toBe(200)
    expect(siguiente).toHaveBeenCalledTimes(1)
  })

  it('un pase equivocado no abre nada', async () => {
    const { respuesta } = await pedir(
      peticion('https://zass.cl/?pase=no-es-el-pase'),
      { MANTENIMIENTO_PASE: PASE },
    )

    expect(respuesta.status).toBe(503)
  })

  it('SIN pase configurado, una cookie inventada no sirve de nada', async () => {
    const { respuesta, siguiente } = await pedir(
      peticion('https://zass.cl/', { cabeceras: { cookie: 'zass_pase=cualquier-cosa' } }),
    )

    // No hay puerta de atras por omision.
    expect(respuesta.status).toBe(503)
    expect(siguiente).not.toHaveBeenCalled()
  })

  it('una cookie vacia tampoco sirve cuando el pase esta configurado', async () => {
    const { respuesta } = await pedir(
      peticion('https://zass.cl/', { cabeceras: { cookie: 'zass_pase=' } }),
      { MANTENIMIENTO_PASE: PASE },
    )

    expect(respuesta.status).toBe(503)
  })
  describe('MANTENIMIENTO_PAGINAS_PUBLICAS', () => {
    const ABRIR = { MANTENIMIENTO_PAGINAS_PUBLICAS: '1' }
    const pedirM = (url, metodo = 'GET', env = ABRIR) =>
      pedir(new Request(url, { method: metodo, headers: { accept: 'text/html' } }), env)

    it('sin la variable, /planes sigue en 503', async () => {
      const { respuesta, siguiente } = await pedirM('https://zass.cl/planes', 'GET', {})
      expect(respuesta.status).toBe(503)
      expect(siguiente).not.toHaveBeenCalled()
    })

    it('con valores que no son 1/true/si no se abre nada', async () => {
      for (const valor of ['', '0', 'no', 'false', 'x']) {
        const { respuesta } = await pedirM('https://zass.cl/planes', 'GET', { MANTENIMIENTO_PAGINAS_PUBLICAS: valor })
        expect(respuesta.status, valor).toBe(503)
      }
    })

    it('con la variable pasan las paginas, los recursos y GET /api/planes', async () => {
      for (const ruta of ['/planes', '/planes/', '/terminos', '/privacidad', '/assets/index-abc.js',
        '/icono-zass-512.png', '/favicon.svg', '/manifest.webmanifest', '/robots.txt', '/api/planes']) {
        const { respuesta, siguiente } = await pedirM(`https://zass.cl${ruta}`)
        expect(respuesta.status, ruta).toBe(200)
        expect(siguiente, ruta).toHaveBeenCalledTimes(1)
      }
      for (const valor of ['true', 'si', 'SI']) {
        const { siguiente } = await pedirM('https://zass.cl/planes', 'GET', { MANTENIMIENTO_PAGINAS_PUBLICAS: valor })
        expect(siguiente, valor).toHaveBeenCalledTimes(1)
      }
      const { siguiente } = await pedirM('https://zass.cl/planes', 'HEAD')
      expect(siguiente).toHaveBeenCalledTimes(1)
    })

    it('con la variable todo lo demas sigue en 503', async () => {
      const casos = [
        ['GET', '/'], ['GET', '/buscar'], ['GET', '/planes/listo'], ['GET', '/PLANES'],
        ['GET', '/api/yo'], ['GET', '/api/planes/x'], ['POST', '/api/planes'], ['POST', '/planes'],
        ['GET', '/sw.js'], ['GET', '/planes/../interno'], ['GET', '/%70lanes'],
        ['GET', '/assets/../sw.js'], ['GET', '//planes'], ['GET', '/assets/'], ['GET', '/terminos/x'],
        ['GET', '/sin-conexion.html'],
      ]
      for (const [metodo, ruta] of casos) {
        const { respuesta, siguiente } = await pedirM(`https://zass.cl${ruta}`, metodo)
        expect(respuesta.status, `${metodo} ${ruta}`).toBe(503)
        expect(siguiente, `${metodo} ${ruta}`).not.toHaveBeenCalled()
      }
    })

    it('el pase sigue funcionando con la variable encendida', async () => {
      const env = { ...ABRIR, MANTENIMIENTO_PASE: PASE }
      const conCookie = await pedir(
        peticion('https://zass.cl/buscar', { cabeceras: { cookie: `zass_pase=${PASE}` } }), env)
      expect(conCookie.respuesta.status).toBe(200)
      const conPase = await pedir(peticion(`https://zass.cl/?pase=${PASE}`), env)
      expect(conPase.respuesta.status).toBe(302)
    })
  })
})
