import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router-dom'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { olvidarCsrf } from '../api/cliente'
import { olvidarCaches } from '../api/useCargado'
import type { EntornoPush } from '../funciones/avisos/push'
import { ProveedorDeSesion } from '../sesion/Sesion'
import { Avisos } from './Avisos'

/* ES-3 · La pagina «Avisos en este dispositivo»: que muestre el estado real y que el boton active. */

function json(cuerpo: unknown, estado = 200) {
  return new Response(JSON.stringify(cuerpo), { status: estado, headers: { 'content-type': 'application/json' } })
}

let registrados: unknown[]
let hayClave: boolean

beforeEach(() => {
  registrados = []
  hayClave = true
  olvidarCsrf()
  olvidarCaches()
  localStorage.clear()
  localStorage.setItem('zass-sesion', '1')
  vi.stubGlobal('fetch', vi.fn(async (entrada: RequestInfo | URL, opciones?: RequestInit) => {
    const ruta = String(entrada)
    if (ruta.includes('/api/auth/csrf')) return json({ token: 't' })
    if (ruta.includes('/api/notificaciones/push/clave')) {
      return hayClave ? json({ clave: 'BEl62iUYgUivxIkv69yViEuiBIa-Ib9-SkvMeAtA3LFgDzkrxZJjSgSnfckjBJuBkr3qBUYIHBQFLXYp5Nksh8U' }) : json({}, 404)
    }
    if (ruta.includes('/api/mi/notificaciones/push')) {
      registrados.push(JSON.parse(String(opciones?.body)))
      return new Response(null, { status: 204 })
    }
    if (ruta.includes('/api/mi/cuenta')) {
      return json({ id: 'u1', correo: 'ana@correo.cl', nombre: 'Ana', correoVerificado: true, roles: ['buscador'] })
    }
    return json([])
  }))
})

function navegador(permisoInicial: NotificationPermission = 'default', soportado = true): EntornoPush {
  let permiso = permisoInicial
  let suscripcion: PushSubscription | null = null
  const nueva = {
    endpoint: 'https://fcm.googleapis.com/fcm/send/abc',
    toJSON: () => ({ keys: { p256dh: 'p', auth: 'a' } }),
    unsubscribe: async () => true,
  } as unknown as PushSubscription
  const registro = {
    pushManager: {
      getSubscription: async () => suscripcion,
      subscribe: async () => { suscripcion = nueva; return nueva },
    },
  } as unknown as ServiceWorkerRegistration
  return {
    serviceWorker: soportado ? { getRegistration: async () => registro } : undefined,
    permiso: () => permiso,
    pedirPermiso: async () => { permiso = 'granted'; return permiso },
  }
}

function pintar(entorno: EntornoPush) {
  return render(
    <MemoryRouter initialEntries={['/avisos']}>
      <ProveedorDeSesion>
        <Avisos entorno={entorno} />
      </ProveedorDeSesion>
    </MemoryRouter>,
  )
}

describe('Avisos en este dispositivo (ES-3)', () => {
  it('activa los avisos con el boton y queda activo', async () => {
    pintar(navegador())

    await userEvent.click(await screen.findByRole('button', { name: 'Activar avisos en este dispositivo' }))

    expect(await screen.findByText('Los avisos están activados en este dispositivo.')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Desactivar en este dispositivo' })).toBeInTheDocument()
    expect(registrados).toHaveLength(1)
  })

  it('con los avisos bloqueados explica como permitirlos y no ofrece el boton', async () => {
    pintar(navegador('denied'))

    expect(await screen.findByText(/Bloqueaste los avisos de ZASS!/)).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /Activar/ })).toBeNull()
  })

  it('en un navegador sin push lo dice y no ofrece el boton', async () => {
    pintar(navegador('default', false))

    expect(await screen.findByText(/Este navegador no puede recibir avisos/)).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /Activar/ })).toBeNull()
  })

  it('si el servidor no tiene push, no ofrece el boton', async () => {
    hayClave = false
    pintar(navegador())

    expect(await screen.findByText(/todavía no están disponibles/)).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /Activar/ })).toBeNull()
  })

  it('siempre aclara que los correos siguen llegando', async () => {
    pintar(navegador())
    await waitFor(() => expect(screen.getByText('Los correos te siguen llegando igual.')).toBeInTheDocument())
  })
})
