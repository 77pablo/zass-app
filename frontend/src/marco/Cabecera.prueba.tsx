import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router-dom'
import { describe, expect, it } from 'vitest'
import { Contexto, type Usuario } from '../sesion/contexto'
import { BarraInferior } from './BarraInferior'
import { Cabecera } from './Cabecera'
import { ProveedorDeMenuMovil } from './ProveedorDeMenuMovil'

function pintar(roles: string[]) {
  const usuario: Usuario = { id: 'u', correo: 'a@b.cl', nombre: 'Ana', correoVerificado: true, roles }
  return render(
    <MemoryRouter>
      <Contexto.Provider value={{ estado: 'dentro', usuario, recargar: async () => {}, salir: async () => {} }}>
        {/* Como en el Marco: la hoja vive en la cabecera y la abre la pestaña «Más» de abajo. */}
        <ProveedorDeMenuMovil>
          <Cabecera />
          <BarraInferior />
        </ProveedorDeMenuMovil>
      </Contexto.Provider>
    </MemoryRouter>,
  )
}

describe('Cabecera: Mis tarjetas', () => {
  it('el prestador la tiene en la barra y en el menú', async () => {
    pintar(['buscador', 'prestador'])
    expect(screen.getByRole('link', { name: /Mis tarjetas/ })).toHaveAttribute('href', '/mis-tarjetas')

    // El menú del teléfono (la hoja «Más») solo existe abierto.
    await userEvent.click(screen.getByRole('button', { name: 'Más' }))
    const enlaces = screen.getAllByRole('link', { name: /Mis tarjetas/ })
    expect(enlaces).toHaveLength(2)
    for (const a of enlaces) expect(a).toHaveAttribute('href', '/mis-tarjetas')
  })

  it('quien solo busca no la ve', () => {
    pintar(['buscador'])
    expect(screen.queryByRole('link', { name: /Mis tarjetas/ })).toBeNull()
  })
})

describe('Cabecera: sin la marca arriba (estilo iPhone, 18-nov-2026)', () => {
  it('no muestra «ZASS!» ni el botón «Menú», pero el encabezado conserva el nombre del sitio', () => {
    pintar(['buscador'])
    const encabezado = screen.getByRole('banner', { name: 'ZASS!' })
    expect(encabezado).not.toHaveTextContent(/ZAS/)
    expect(screen.queryByRole('link', { name: /ZASS!/ })).toBeNull()
    expect(screen.queryByRole('button', { name: 'Menú' })).toBeNull()
  })

  it('«Inicio» ocupa el lugar del logo en la barra de escritorio y lleva al mismo lugar', () => {
    pintar(['buscador'])
    const nav = screen.getByRole('navigation', { name: 'Principal' })
    expect(within(nav).getByRole('link', { name: 'Inicio' })).toHaveAttribute('href', '/')
  })
})
