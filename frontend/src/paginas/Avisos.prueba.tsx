import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router-dom'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { olvidarCsrf } from '../api/cliente'
import { olvidarCaches } from '../api/useCargado'
import type { EntornoPush } from '../funciones/avisos/push'
import { ProveedorDeSesion } from '../sesion/Sesion'
import { Avisos } from './Avisos'

/* ES-3 · La pagina «Avisos»: el push de este dispositivo (API-45) y por que canal llega cada grupo (API-43). */

function json(cuerpo: unknown, estado = 200) {
  return new Response(JSON.stringify(cuerpo), { status: estado, headers: { 'content-type': 'application/json' } })
}

let registrados: unknown[]
let hayClave: boolean
let preferencias: { tipo: string; grupo: string; correo: boolean; push: boolean; correoFijo: boolean }[]
let guardadas: unknown[]
let fallaGuardar: boolean

const TIPOS: [string, string, boolean][] = [
  ['CitaSolicitada', 'citas', false], ['CitaConfirmada', 'citas', false],
  ['Recordatorio24h', 'recordatorios', false],
  ['CobroRechazado', 'cobros', true], ['TarjetaActivada', 'cobros', true],
]

beforeEach(() => {
  registrados = []
  hayClave = true
  guardadas = []
  fallaGuardar = false
  preferencias = TIPOS.map(([tipo, grupo, correoFijo]) => ({ tipo, grupo, correo: true, push: true, correoFijo }))
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
    if (ruta.includes('/api/mi/notificaciones/preferencias')) {
      if (opciones?.method === 'PUT') {
        if (fallaGuardar) return json({ title: 'Los avisos de tu plan siempre llegan por correo.' }, 400)
        const cuerpo = JSON.parse(String(opciones.body)) as { preferencias: { tipo: string; correo: boolean; push: boolean }[] }
        guardadas.push(cuerpo)
        for (const c of cuerpo.preferencias) {
          const p = preferencias.find((x) => x.tipo === c.tipo)!
          p.correo = c.correo
          p.push = c.push
        }
      }
      return json({ preferencias })
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

  it('muestra un bloque por grupo, con el correo fijo deshabilitado en cobros', async () => {
    pintar(navegador())

    const citas = await screen.findByRole('region', { name: 'Citas' })
    expect(within(citas).getByRole('checkbox', { name: 'Correo' })).toBeEnabled()
    const cobros = screen.getByRole('region', { name: 'Plan, cobros y tarjeta' })
    expect(within(cobros).getByRole('checkbox', { name: 'Correo' })).toBeDisabled()
    expect(within(cobros).getByRole('checkbox', { name: 'Correo' })).toBeChecked()
    expect(within(cobros).getByText('Estos avisos siempre te llegan por correo.')).toBeInTheDocument()
    expect(within(citas).queryByText('Estos avisos siempre te llegan por correo.')).toBeNull()
  })

  it('apagar el push de un grupo manda todos sus tipos juntos y deja la casilla como responde el servidor', async () => {
    pintar(navegador())

    const citas = await screen.findByRole('region', { name: 'Citas' })
    await userEvent.click(within(citas).getByRole('checkbox', { name: 'Push' }))

    await waitFor(() => expect(within(citas).getByRole('checkbox', { name: 'Push' })).not.toBeChecked())
    expect(guardadas).toEqual([{ preferencias: [
      { tipo: 'CitaSolicitada', correo: true, push: false },
      { tipo: 'CitaConfirmada', correo: true, push: false },
    ] }])
    expect(within(screen.getByRole('region', { name: 'Recordatorios' })).getByRole('checkbox', { name: 'Push' })).toBeChecked()
  })

  it('con correo y push apagados avisa que no llegara nada de ese grupo', async () => {
    preferencias[0].push = false
    preferencias[1].push = false
    pintar(navegador())

    const citas = await screen.findByRole('region', { name: 'Citas' })
    expect(within(citas).queryByText(/no te llegará ningún aviso/)).toBeNull()
    await userEvent.click(within(citas).getByRole('checkbox', { name: 'Correo' }))

    expect(await within(citas).findByText(/no te llegará ningún aviso/)).toBeInTheDocument()
  })

  it('si el servidor no tiene push, no ofrece la casilla Push y si el correo', async () => {
    hayClave = false
    pintar(navegador())

    const citas = await screen.findByRole('region', { name: 'Citas' })
    expect(within(citas).getByRole('checkbox', { name: 'Correo' })).toBeInTheDocument()
    expect(within(citas).queryByRole('checkbox', { name: 'Push' })).toBeNull()
    expect(screen.queryByText(/El push solo te llega/)).toBeNull()
  })

  it('un grupo con un solo tipo apagado se ve apagado', async () => {
    preferencias[1].correo = false
    pintar(navegador())

    const citas = await screen.findByRole('region', { name: 'Citas' })
    expect(within(citas).getByRole('checkbox', { name: 'Correo' })).not.toBeChecked()
  })

  it('si guardar falla, muestra el mensaje y la casilla vuelve a como estaba', async () => {
    fallaGuardar = true
    pintar(navegador())

    const citas = await screen.findByRole('region', { name: 'Citas' })
    await userEvent.click(within(citas).getByRole('checkbox', { name: 'Push' }))

    expect(await screen.findByRole('alert')).toHaveTextContent('Los avisos de tu plan siempre llegan por correo.')
    expect(within(citas).getByRole('checkbox', { name: 'Push' })).toBeChecked()
  })
})
