import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router-dom'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { olvidarCsrf } from '../api/cliente'
import { QuitarVerificacion } from './QuitarVerificacion'

function json(cuerpo: unknown, estado = 200) {
  return new Response(JSON.stringify(cuerpo), { status: estado, headers: { 'content-type': 'application/json' } })
}

let pedidos: { cuerpo: unknown; csrf: string | null }[]
let respuesta: () => Response

beforeEach(() => {
  pedidos = []
  respuesta = () => new Response(null, { status: 204 })
  olvidarCsrf()
  vi.stubGlobal('fetch', vi.fn(async (ruta: string, opciones?: RequestInit) => {
    if (ruta === '/api/auth/csrf') return json({ token: 't' })
    if (ruta === '/api/interno/cuentas/quitar-mfa' && opciones?.method === 'POST') {
      pedidos.push({
        cuerpo: JSON.parse(String(opciones.body)),
        csrf: new Headers(opciones.headers).get('X-CSRF-Token'),
      })
      return respuesta()
    }
    return json({}, 404)
  }))
})

afterEach(() => { vi.restoreAllMocks() })

const pintar = () => render(<MemoryRouter><QuitarVerificacion /></MemoryRouter>)

async function llenar(correo: string, motivo: string) {
  await userEvent.type(screen.getByLabelText('Correo de la cuenta'), correo)
  await userEvent.type(screen.getByLabelText('Motivo'), motivo)
  await userEvent.click(screen.getByRole('button', { name: 'Quitar la verificación' }))
}

describe('quitar la verificación en dos pasos (API-86)', () => {
  it('pide confirmación, manda correo y motivo con CSRF y avisa que salió el correo', async () => {
    const confirmar = vi.spyOn(window, 'confirm').mockReturnValue(true)
    pintar()

    await llenar('rosa@correo.cl', 'Llamó y confirmó su RUT y su última cita.')

    expect(confirmar).toHaveBeenCalledWith(expect.stringContaining('rosa@correo.cl'))
    expect(await screen.findByRole('status')).toHaveTextContent('Le llegará un correo')
    expect(pedidos).toEqual([{
      cuerpo: { correo: 'rosa@correo.cl', motivo: 'Llamó y confirmó su RUT y su última cita.' },
      csrf: 't',
    }])
  })

  it('si no confirma, no manda nada', async () => {
    vi.spyOn(window, 'confirm').mockReturnValue(false)
    pintar()

    await llenar('rosa@correo.cl', 'Llamó y confirmó su RUT y su última cita.')

    expect(pedidos).toHaveLength(0)
  })

  it('un motivo corto no se manda ni pide confirmación', async () => {
    const confirmar = vi.spyOn(window, 'confirm').mockReturnValue(true)
    pintar()

    await llenar('rosa@correo.cl', 'corto')

    expect(confirmar).not.toHaveBeenCalled()
    expect(pedidos).toHaveLength(0)
  })

  it('muestra el motivo de la API cuando no se puede (por ejemplo, la propia cuenta)', async () => {
    vi.spyOn(window, 'confirm').mockReturnValue(true)
    respuesta = () => json({ title: 'Regla', detail: 'No puedes quitar la verificación de tu propia cuenta.' }, 422)
    pintar()

    await llenar('admin@zass.cl', 'Perdí el teléfono, me la quito yo.')

    expect(await screen.findByRole('alert')).toHaveTextContent('tu propia cuenta')
  })
})
