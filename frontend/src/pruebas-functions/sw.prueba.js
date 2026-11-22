// @vitest-environment node
//
// public/sw.js de verdad, ejecutado con un `self`, unas cachés y una red de mentira. Lo que se
// vigila es lo que haría daño si se rompe: guardar la API, servir una página vieja después de
// un despliegue, o dejar a alguien sin nada cuando no hay señal.
import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'
import { beforeEach, describe, expect, it, vi } from 'vitest'

const RAIZ = resolve(import.meta.dirname, '..', '..')
const codigo = readFileSync(resolve(RAIZ, 'public', 'sw.js'), 'utf8')
const ORIGEN = 'https://zass.cl'

/** Una caché en memoria con la misma forma que la del navegador (Map conserva el orden). */
function crearCaches() {
  const almacen = new Map()
  const abrir = (nombre) => {
    if (!almacen.has(nombre)) almacen.set(nombre, new Map())
    const m = almacen.get(nombre)
    return {
      addAll: async (urls) => { for (const u of urls) m.set(new URL(u, ORIGEN).href, new Response(`precargado ${u}`)) },
      put: async (req, res) => { m.set(req.url, res) },
      keys: async () => [...m.keys()].map((u) => new Request(u)),
      delete: async (req) => m.delete(typeof req === 'string' ? req : req.url),
    }
  }
  return {
    almacen,
    open: async (n) => abrir(n),
    keys: async () => [...almacen.keys()],
    delete: async (n) => almacen.delete(n),
    match: async (req) => {
      const url = new URL(typeof req === 'string' ? req : req.url, ORIGEN).href
      for (const m of almacen.values()) if (m.has(url)) return m.get(url).clone()
      return undefined
    },
  }
}

let manejadores, caches, red, self

beforeEach(() => {
  manejadores = {}
  caches = crearCaches()
  red = vi.fn(async (req) => new Response(`red ${req.url}`, { status: 200 }))
  self = {
    location: { origin: ORIGEN },
    addEventListener: (tipo, fn) => { manejadores[tipo] = fn },
    skipWaiting: vi.fn(async () => {}),
    clients: { claim: vi.fn(async () => {}) },
  }
  new Function('self', 'caches', 'fetch', codigo)(self, caches, (...a) => red(...a))
})

/** Dispara un evento y espera todo lo que el service worker deje pendiente. */
async function evento(tipo, extra = {}) {
  const pendientes = []
  let respuesta
  const e = { ...extra, waitUntil: (p) => pendientes.push(p), respondWith: (p) => { respuesta = p } }
  manejadores[tipo](e)
  const r = await respuesta
  await Promise.all(pendientes)
  return { respondio: respuesta !== undefined, respuesta: r }
}
const pedir = (ruta, init = {}) => evento('fetch', { request: Object.assign(new Request(ORIGEN + ruta, init), init.mode ? { mode: init.mode } : {}) })
const navegar = (ruta) => {
  const request = new Request(ORIGEN + ruta)
  Object.defineProperty(request, 'mode', { value: 'navigate' })
  return evento('fetch', { request })
}

describe('service worker', () => {
  it('al instalarse guarda la pagina de «sin conexion»', async () => {
    await evento('install')
    expect(await caches.match('/sin-conexion.html')).toBeDefined()
    expect(self.skipWaiting).toHaveBeenCalled()
  })

  it('NUNCA toca la API: ni la responde ni la guarda', async () => {
    await evento('install')
    const { respondio } = await pedir('/api/prestadores/marcela-soto')
    expect(respondio).toBe(false)
    // Tampoco si alguien abre la API directo en el navegador (una navegacion): sin red no tiene
    // que ver «sin conexion» como si fuera una pagina, ni nada guardado.
    const directo = await navegar('/api/salud')
    expect(directo.respondio).toBe(false)
  })

  it('una pagina se pide SIEMPRE a la red primero (despues de un despliegue nadie ve la vieja)', async () => {
    await evento('install')
    const { respuesta } = await navegar('/buscar?q=abogado')
    expect(await respuesta.text()).toBe(`red ${ORIGEN}/buscar?q=abogado`)
    expect(red).toHaveBeenCalledTimes(1)
  })

  it('sin red, una pagina muestra «sin conexion» en vez del error del navegador', async () => {
    await evento('install')
    red.mockRejectedValue(new TypeError('Failed to fetch'))
    const { respuesta } = await navegar('/prestadores/marcela-soto')
    expect(await respuesta.text()).toBe('precargado /sin-conexion.html')
  })

  it('un archivo de /assets/ se guarda, y la segunda vez no sale a la red', async () => {
    await evento('install')
    const primera = await pedir('/assets/index-abc123.js')
    expect(await primera.respuesta.text()).toBe(`red ${ORIGEN}/assets/index-abc123.js`)
    const segunda = await pedir('/assets/index-abc123.js')
    expect(await segunda.respuesta.text()).toBe(`red ${ORIGEN}/assets/index-abc123.js`)
    expect(red).toHaveBeenCalledTimes(1)
  })

  it('no guarda una respuesta con error (un 404 no se queda pegado)', async () => {
    await evento('install')
    red.mockResolvedValueOnce(new Response('no', { status: 404 }))
    await pedir('/assets/falta.js')
    await pedir('/assets/falta.js')
    expect(red).toHaveBeenCalledTimes(2)
  })

  it('no crece sin fin: guarda como mucho 60 archivos, y bota los mas viejos', async () => {
    await evento('install')
    for (let i = 0; i < 70; i++) await pedir(`/assets/a${i}.js`)
    const nombre = [...caches.almacen.keys()].find((n) => n.startsWith('zass-'))
    const guardados = [...caches.almacen.get(nombre).keys()]
    expect(guardados).toHaveLength(60)
    expect(guardados).not.toContain(`${ORIGEN}/assets/a0.js`)
    expect(guardados).toContain(`${ORIGEN}/assets/a69.js`)
  })

  it('al activarse borra las cachés de versiones anteriores, y no las de otros', async () => {
    caches.almacen.set('zass-v0-estaticos', new Map())
    caches.almacen.set('otra-cosa', new Map())
    await evento('install')
    await evento('activate')
    expect([...caches.almacen.keys()].sort()).toEqual(['otra-cosa', 'zass-v1-estaticos'])
    expect(self.clients.claim).toHaveBeenCalled()
  })

  it('no toca otros dominios ni lo que no sea GET', async () => {
    await evento('install')
    // Otro dominio, aunque la ruta parezca de las nuestras.
    const otro = await evento('fetch', { request: new Request('https://cdn.ejemplo.com/assets/x.js') })
    const post = await pedir('/assets/x.js', { method: 'POST', body: 'a' })
    expect(otro.respondio).toBe(false)
    expect(post.respondio).toBe(false)
  })
})

// ES-3 · Avisos push: se muestran con el texto que manda el servidor y, al tocarlos, abren una
// ruta DE ESTE SITIO (nunca otro dominio, aunque el aviso lo pida).
describe('avisos push', () => {
  const datos = (obj) => ({ data: { json: () => obj } })

  beforeEach(() => {
    self.registration = { showNotification: vi.fn(async () => {}) }
  })

  it('muestra el aviso con su titulo, texto, etiqueta y la ruta a abrir', async () => {
    await evento('push', datos({ titulo: 'Tu hora quedó confirmada', cuerpo: 'Con Ana el martes', url: '/mis-citas', etiqueta: 'zass-cita-1' }))

    expect(self.registration.showNotification).toHaveBeenCalledWith('Tu hora quedó confirmada', expect.objectContaining({
      body: 'Con Ana el martes', tag: 'zass-cita-1', data: { url: '/mis-citas' }, icon: '/icono-zass-192.png',
    }))
  })

  it('una url que no es de este sitio se cambia por la portada', async () => {
    for (const url of ['https://atacante.cl', '//atacante.cl', '/' + String.fromCharCode(92) + 'atacante.cl', 'javascript:alert(1)', 42]) {
      self.registration.showNotification.mockClear()
      await evento('push', datos({ titulo: 'x', url }))
      expect(self.registration.showNotification.mock.calls[0][1].data.url).toBe('/')
    }
  })

  it('un aviso sin datos o con datos rotos se muestra igual, con un texto generico', async () => {
    await evento('push', { data: { json: () => { throw new Error('no es json') } } })
    await evento('push', {})

    expect(self.registration.showNotification).toHaveBeenCalledTimes(2)
    expect(self.registration.showNotification.mock.calls[0][0]).toBe('ZASS!')
  })

  it('al tocarlo, usa la pestaña de ZASS! que ya estaba abierta', async () => {
    const ventana = { url: ORIGEN + '/buscar', navigate: vi.fn(async () => null), focus: vi.fn(async () => {}) }
    self.clients.matchAll = vi.fn(async () => [{ url: 'https://otro.cl/' }, ventana])
    self.clients.openWindow = vi.fn()
    const cerrar = vi.fn()

    await evento('notificationclick', { notification: { close: cerrar, data: { url: '/mis-citas' } } })

    expect(cerrar).toHaveBeenCalled()
    expect(ventana.navigate).toHaveBeenCalledWith(ORIGEN + '/mis-citas')
    expect(ventana.focus).toHaveBeenCalled()
    expect(self.clients.openWindow).not.toHaveBeenCalled()
  })

  it('al tocarlo, sin ZASS! abierto, abre una pestaña nueva en la ruta del aviso', async () => {
    self.clients.matchAll = vi.fn(async () => [])
    self.clients.openWindow = vi.fn(async () => null)

    await evento('notificationclick', { notification: { close: vi.fn(), data: { url: 'https://atacante.cl' } } })

    expect(self.clients.openWindow).toHaveBeenCalledWith(ORIGEN + '/')
  })
})
