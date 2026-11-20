import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { olvidarCsrf } from '../api/cliente'
import { PagoSimulado } from './PagoSimulado'

/*
 * La firma del aviso (T48, P16) cubre intento|monto|referencia TAL COMO los puso el
 * adaptador en la dirección. Si la pantalla manda otra referencia u otro monto, la API
 * responde 400 y el fundador no puede inscribir su medio de pago. Las pruebas de C# no lo
 * veían: firmaban sus propios avisos. Esta mira lo que sale del navegador.
 */

let avisos: Record<string, unknown>[]

function json(cuerpo: unknown, estado = 200) {
  return new Response(JSON.stringify(cuerpo), { status: estado, headers: { 'content-type': 'application/json' } })
}

beforeEach(() => {
  avisos = []
  olvidarCsrf()
  vi.stubGlobal('fetch', vi.fn(async (ruta: string, opciones?: RequestInit) => {
    if (ruta === '/api/auth/csrf') return json({ token: 't' })
    if (ruta === '/api/pagos/aviso') avisos.push(JSON.parse(String(opciones?.body)))
    return json({})
  }))
})

const INTENTO = '3f2a9c1e-7b4d-4e8a-9c2b-1d5e6f7a8b9c'

function pintar(direccion: string) {
  render(
    <MemoryRouter initialEntries={[direccion]}>
      <Routes>
        <Route path="/pago/inscripcion" element={<PagoSimulado />} />
        <Route path="/planes/listo" element={<p>listo</p>} />
      </Routes>
    </MemoryRouter>,
  )
}

describe('PagoSimulado', () => {
  it('devuelve intento, monto, referencia y firma tal como vinieron en la dirección', async () => {
    pintar(`/pago/inscripcion?intento=${INTENTO}&referencia=sim-0a1b2c3d4e5f6a7b&monto=0&firma=abc_DEF-123`)

    await userEvent.click(screen.getByRole('button', { name: 'Inscribir mi medio de pago' }))

    await waitFor(() => expect(avisos).toHaveLength(1))
    expect(avisos[0]).toMatchObject({
      intento: INTENTO,
      referencia: 'sim-0a1b2c3d4e5f6a7b',
      monto: '0',
      firma: 'abc_DEF-123',
      estado: 'aprobado',
    })
    expect(await screen.findByText('listo')).toBeInTheDocument()
  })

  it('no inventa el monto: si la dirección trae otro, lo devuelve igual', async () => {
    pintar(`/pago/inscripcion?intento=${INTENTO}&referencia=sim-x&monto=15000&firma=f`)

    await userEvent.click(screen.getByRole('button', { name: 'Inscribir mi medio de pago' }))

    await waitFor(() => expect(avisos).toHaveLength(1))
    expect(avisos[0]).toMatchObject({ monto: '15000', referencia: 'sim-x' })
  })
})
