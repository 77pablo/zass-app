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
})
