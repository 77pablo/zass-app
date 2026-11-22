import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { olvidarCsrf } from '../api/cliente'
import { ProveedorDeSesion } from './Sesion'
import { useSesion } from './contexto'

/*
 * ES-3 (decision del 21-nov): al cerrar sesion, este navegador deja de recibir los push de la
 * cuenta. La baja tiene que ir ANTES del cierre: despues ya no hay sesion para autorizarla.
 */
const orden: string[] = []

vi.mock('../funciones/avisos/push', () => ({
  darDeBajaAlSalir: vi.fn(async () => { orden.push('baja push') }),
}))

function json(cuerpo: unknown, estado = 200) {
  return new Response(JSON.stringify(cuerpo), { status: estado, headers: { 'content-type': 'application/json' } })
}

function BotonSalir() {
  const { estado, salir } = useSesion()
  return <button type="button" onClick={() => void salir()}>{estado}</button>
}

beforeEach(() => {
  orden.length = 0
  olvidarCsrf()
  localStorage.clear()
  localStorage.setItem('zass-sesion', '1')
  vi.stubGlobal('fetch', vi.fn(async (entrada: RequestInfo | URL) => {
    const ruta = String(entrada)
    if (ruta.includes('/api/auth/csrf')) return json({ token: 't' })
    if (ruta.includes('/api/mi/cuenta')) {
      return json({ id: 'u1', correo: 'ana@correo.cl', nombre: 'Ana', correoVerificado: true, roles: ['buscador'] })
    }
    if (ruta.includes('/api/auth/')) { orden.push('cerrar sesion'); return new Response(null, { status: 204 }) }
    return json({})
  }))
})

describe('cerrar sesion y push', () => {
  it('da de baja el push de este navegador antes de cerrar la sesion', async () => {
    render(<ProveedorDeSesion><BotonSalir /></ProveedorDeSesion>)
    await userEvent.click(await screen.findByRole('button', { name: 'dentro' }))

    await waitFor(() => expect(screen.getByRole('button', { name: 'fuera' })).toBeInTheDocument())
    expect(orden).toEqual(['baja push', 'cerrar sesion'])
  })
})
