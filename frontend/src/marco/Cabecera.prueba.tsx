import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router-dom'
import { describe, expect, it } from 'vitest'
import { Contexto, type Usuario } from '../sesion/contexto'
import { Cabecera } from './Cabecera'

function pintar(roles: string[]) {
  const usuario: Usuario = { id: 'u', correo: 'a@b.cl', nombre: 'Ana', correoVerificado: true, roles }
  return render(
    <MemoryRouter>
      <Contexto.Provider value={{ estado: 'dentro', usuario, recargar: async () => {}, salir: async () => {} }}>
        <Cabecera />
      </Contexto.Provider>
    </MemoryRouter>,
  )
}

describe('Cabecera: Mis tarjetas', () => {
  it('el prestador la tiene en la barra y en el menú', async () => {
    pintar(['buscador', 'prestador'])
    expect(screen.getByRole('link', { name: /Mis tarjetas/ })).toHaveAttribute('href', '/mis-tarjetas')

    // El menú del teléfono solo existe abierto.
    await userEvent.click(screen.getByRole('button', { name: 'Menú' }))
    const enlaces = screen.getAllByRole('link', { name: /Mis tarjetas/ })
    expect(enlaces).toHaveLength(2)
    for (const a of enlaces) expect(a).toHaveAttribute('href', '/mis-tarjetas')
  })

  it('quien solo busca no la ve', () => {
    pintar(['buscador'])
    expect(screen.queryByRole('link', { name: /Mis tarjetas/ })).toBeNull()
  })
})
