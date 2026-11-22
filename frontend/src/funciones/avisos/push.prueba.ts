import { beforeEach, describe, expect, it, vi } from 'vitest'
import { olvidarCsrf } from '../../api/cliente'
import { activarPush, claveABytes, darDeBajaAlSalir, desactivarPush, estadoPush, type EntornoPush } from './push'

/*
 * ES-3 · Activar y desactivar los avisos push en este navegador. El navegador es de mentira
 * (EntornoPush), la API tambien (fetch). Se vigila: que se registre en la API lo que entrega el
 * navegador, que un permiso negado no registre nada, que no queden suscripciones huerfanas, y que
 * cerrar sesion nunca quede esperando al push.
 */

const CLAVE = 'BEl62iUYgUivxIkv69yViEuiBIa-Ib9-SkvMeAtA3LFgDzkrxZJjSgSnfckjBJuBkr3qBUYIHBQFLXYp5Nksh8U'
const ENDPOINT = 'https://fcm.googleapis.com/fcm/send/abc'

let llamadas: { ruta: string; metodo: string; cuerpo: unknown }[]
let respuestaClave: () => Response
let respuestaRegistro: () => Response

function json(cuerpo: unknown, estado = 200) {
  return new Response(JSON.stringify(cuerpo), { status: estado, headers: { 'content-type': 'application/json' } })
}

beforeEach(() => {
  olvidarCsrf()
  llamadas = []
  respuestaClave = () => json({ clave: CLAVE })
  respuestaRegistro = () => new Response(null, { status: 204 })
  vi.stubGlobal('fetch', vi.fn(async (entrada: RequestInfo | URL, opciones?: RequestInit) => {
    const ruta = String(entrada)
    const metodo = opciones?.method ?? 'GET'
    if (ruta.includes('/api/auth/csrf')) return json({ token: 't' })
    llamadas.push({ ruta, metodo, cuerpo: opciones?.body ? JSON.parse(String(opciones.body)) : undefined })
    if (ruta.includes('/api/notificaciones/push/clave')) return respuestaClave()
    if (ruta.includes('/api/mi/notificaciones/push')) return respuestaRegistro()
    return json({})
  }))
})

function suscripcionFalsa() {
  return {
    endpoint: ENDPOINT,
    toJSON: () => ({ endpoint: ENDPOINT, keys: { p256dh: 'clave-p256', auth: 'secreto' } }),
    unsubscribe: vi.fn(async () => true),
  }
}

function navegador(opciones: {
  permiso?: NotificationPermission
  responde?: NotificationPermission
  suscrito?: boolean
  sinServiceWorker?: boolean
} = {}) {
  let suscripcion = opciones.suscrito ? suscripcionFalsa() : null
  let permiso = opciones.permiso ?? 'default'
  const subscribe = vi.fn(async () => { suscripcion = suscripcionFalsa(); return suscripcion })
  const registro = {
    pushManager: { getSubscription: vi.fn(async () => suscripcion), subscribe },
  } as unknown as ServiceWorkerRegistration
  const entorno: EntornoPush = {
    serviceWorker: opciones.sinServiceWorker ? undefined : { getRegistration: async () => registro },
    permiso: () => permiso,
    pedirPermiso: vi.fn(async () => { permiso = opciones.responde ?? 'granted'; return permiso }),
  }
  return { entorno, subscribe, actual: () => suscripcion }
}

describe('claveABytes', () => {
  it('convierte la clave publica VAPID (base64url) a sus 65 bytes', () => {
    const bytes = claveABytes(CLAVE)
    expect(bytes).toHaveLength(65)
    expect(bytes[0]).toBe(0x04)
  })
})

describe('estadoPush', () => {
  it('sin service worker (como en desarrollo) no esta soportado y ni pregunta a la API', async () => {
    expect(await estadoPush(navegador({ sinServiceWorker: true }).entorno)).toBe('no-soportado')
    expect(llamadas).toHaveLength(0)
  })

  it('si el servidor no tiene claves, dice que no esta disponible', async () => {
    respuestaClave = () => json({ title: 'No encontrado' }, 404)
    expect(await estadoPush(navegador().entorno)).toBe('sin-clave')
  })

  it('con el permiso negado, dice que esta bloqueado', async () => {
    expect(await estadoPush(navegador({ permiso: 'denied' }).entorno)).toBe('bloqueado')
  })

  it('con permiso y suscripcion, esta activo; sin suscripcion, inactivo', async () => {
    expect(await estadoPush(navegador({ permiso: 'granted', suscrito: true }).entorno)).toBe('activo')
    expect(await estadoPush(navegador({ permiso: 'granted' }).entorno)).toBe('inactivo')
  })
})

describe('activarPush', () => {
  it('pide permiso, suscribe con la clave del servidor y registra el navegador en la API', async () => {
    const n = navegador()

    expect(await activarPush(n.entorno)).toBe('activo')

    expect(n.subscribe).toHaveBeenCalledWith({ userVisibleOnly: true, applicationServerKey: claveABytes(CLAVE) })
    expect(llamadas.filter((l) => l.metodo === 'POST')).toEqual([{
      ruta: expect.stringContaining('/api/mi/notificaciones/push'), metodo: 'POST',
      cuerpo: { endpoint: ENDPOINT, p256dh: 'clave-p256', auth: 'secreto' },
    }])
  })

  it('si la persona niega el permiso, no suscribe ni registra nada', async () => {
    const n = navegador({ responde: 'denied' })

    expect(await activarPush(n.entorno)).toBe('bloqueado')

    expect(n.subscribe).not.toHaveBeenCalled()
    expect(llamadas.filter((l) => l.metodo === 'POST')).toHaveLength(0)
  })

  it('si cierra la pregunta sin responder, queda inactivo', async () => {
    expect(await activarPush(navegador({ responde: 'default' }).entorno)).toBe('inactivo')
  })

  it('si la API no acepta el registro, no deja la suscripcion huerfana en el navegador', async () => {
    respuestaRegistro = () => json({ title: 'Solicitud no valida' }, 400)
    const n = navegador()

    await expect(activarPush(n.entorno)).rejects.toThrow()

    expect(n.actual()?.unsubscribe).toHaveBeenCalled()
  })
})

describe('desactivarPush y cerrar sesion', () => {
  it('desactivar da de baja el navegador en la API y en el navegador', async () => {
    const n = navegador({ permiso: 'granted', suscrito: true })
    const suscripcion = n.actual()

    expect(await desactivarPush(n.entorno)).toBe('inactivo')

    expect(llamadas.filter((l) => l.metodo === 'DELETE')).toEqual([{
      ruta: expect.stringContaining('/api/mi/notificaciones/push'), metodo: 'DELETE', cuerpo: { endpoint: ENDPOINT },
    }])
    expect(suscripcion?.unsubscribe).toHaveBeenCalled()
  })

  it('sin push activo, cerrar sesion no llama a la API', async () => {
    await darDeBajaAlSalir(navegador().entorno)
    expect(llamadas).toHaveLength(0)
  })

  it('cerrar sesion no falla aunque la API falle', async () => {
    respuestaRegistro = () => json({ title: 'Error' }, 500)
    await expect(darDeBajaAlSalir(navegador({ permiso: 'granted', suscrito: true }).entorno)).resolves.toBeUndefined()
  })

  it('cerrar sesion no espera mas del plazo aunque la API no responda', async () => {
    vi.stubGlobal('fetch', vi.fn((entrada: RequestInfo | URL) =>
      String(entrada).includes('/api/auth/csrf') ? Promise.resolve(json({ token: 't' })) : new Promise<Response>(() => {})))
    const inicio = Date.now()

    await darDeBajaAlSalir(navegador({ permiso: 'granted', suscrito: true }).entorno, 50)

    expect(Date.now() - inicio).toBeLessThan(1000)
  })
})
