import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { olvidarCsrf } from '../api/cliente'
import { Contexto, type EstadoDeSesion, type Usuario } from '../sesion/contexto'
import { OfrecerServicios } from './OfrecerServicios'

function json(cuerpo: unknown, estado = 200) {
  return new Response(JSON.stringify(cuerpo), { status: estado, headers: { 'content-type': 'application/json' } })
}

let llamadas: string[]
let respuestaActivar: () => Response
const recargar = vi.fn(async () => {})

beforeEach(() => {
  llamadas = []
  recargar.mockClear()
  respuestaActivar = () => new Response(null, { status: 204 })
  olvidarCsrf()
  vi.stubGlobal('fetch', vi.fn(async (ruta: string) => {
    if (ruta === '/api/auth/csrf') return json({ token: 't' })
    llamadas.push(ruta)
    return respuestaActivar()
  }))
})

function pintar(estado: EstadoDeSesion, roles: string[] = ['buscador']) {
  const usuario: Usuario | null = estado === 'dentro'
    ? { id: 'u', correo: 'a@b.cl', nombre: 'Ana', correoVerificado: true, roles } : null
  return render(
    <Contexto.Provider value={{ estado, usuario, recargar, salir: async () => {} }}>
      <MemoryRouter initialEntries={['/ofrecer-servicios']}>
        <Routes>
          <Route path="/ofrecer-servicios" element={<OfrecerServicios />} />
          <Route path="/mi-ficha" element={<p>PANTALLA MI FICHA</p>} />
        </Routes>
      </MemoryRouter>
    </Contexto.Provider>,
  )
}

describe('ofrecer mis servicios', () => {
  it('quien ya tiene cuenta activa su ficha y va a ella, sin crear otra cuenta', async () => {
    pintar('dentro')

    await userEvent.click(screen.getByRole('button', { name: /activar mi ficha/i }))

    expect(await screen.findByText('PANTALLA MI FICHA')).toBeInTheDocument()
    expect(llamadas).toEqual(['/api/mi/activar-prestador'])
    // Los roles viajan en la sesion: se vuelve a leer para que «Mi ficha» exista de inmediato.
    expect(recargar).toHaveBeenCalledTimes(1)
  })

  it('si el servidor falla, lo dice y deja reintentar', async () => {
    respuestaActivar = () => json({ title: 'x', detail: 'No pudimos activarlo ahora.' }, 500)
    pintar('dentro')

    await userEvent.click(screen.getByRole('button', { name: /activar mi ficha/i }))

    expect(await screen.findByRole('alert')).toBeInTheDocument()
    expect(recargar).not.toHaveBeenCalled()
    await waitFor(() => expect(screen.getByRole('button', { name: /activar mi ficha/i })).toBeEnabled())
  })

  it('quien ya es prestador va directo a su ficha', () => {
    pintar('dentro', ['buscador', 'prestador'])

    expect(screen.getByText('PANTALLA MI FICHA')).toBeInTheDocument()
  })

  it('sin sesion pide la cuenta en vez de un boton que no podria funcionar', () => {
    pintar('fuera')

    expect(screen.queryByRole('button', { name: /activar/i })).not.toBeInTheDocument()
    expect(screen.getByRole('link', { name: /crear mi cuenta/i })).toHaveAttribute('href', '/crear-cuenta?rol=prestador')
  })
})
