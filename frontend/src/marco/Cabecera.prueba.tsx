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
  // Desde el 19-nov-2026 la barra de escritorio ya no existe: todo lo de la cuenta esta en la hoja
  // «Más», en el teléfono y en escritorio (allí como popover bajo la cápsula de pestañas).
  it('el prestador la tiene en la hoja «Más»', async () => {
    pintar(['buscador', 'prestador'])
    // La hoja solo existe abierta.
    expect(screen.queryByRole('link', { name: /Mis tarjetas/ })).toBeNull()
    await userEvent.click(screen.getByRole('button', { name: 'Más' }))
    expect(screen.getByRole('link', { name: /Mis tarjetas/ })).toHaveAttribute('href', '/mis-tarjetas')
  })

  it('quien solo busca no la ve, ni con la hoja abierta', async () => {
    pintar(['buscador'])
    await userEvent.click(screen.getByRole('button', { name: 'Más' }))
    expect(screen.getByRole('link', { name: 'Comunas' })).toBeInTheDocument()
    expect(screen.queryByRole('link', { name: /Mis tarjetas/ })).toBeNull()
  })
})

describe('Cabecera: Mi plan', () => {
  it('el prestador lo ve en la hoja «Más» y lleva a /planes', async () => {
    pintar(['buscador', 'prestador'])
    await userEvent.click(screen.getByRole('button', { name: 'Más' }))
    expect(screen.getByRole('link', { name: /Mi plan/ })).toHaveAttribute('href', '/planes')
  })

  it('quien solo busca no lo ve', async () => {
    pintar(['buscador'])
    await userEvent.click(screen.getByRole('button', { name: 'Más' }))
    expect(screen.queryByRole('link', { name: /Mi plan/ })).toBeNull()
  })

  it('el visitante (sin sesion) no lo ve', async () => {
    render(
      <MemoryRouter>
        <Contexto.Provider value={{ estado: 'fuera', usuario: null, recargar: async () => {}, salir: async () => {} }}>
          <ProveedorDeMenuMovil><Cabecera /><BarraInferior /></ProveedorDeMenuMovil>
        </Contexto.Provider>
      </MemoryRouter>,
    )
    await userEvent.click(screen.getByRole('button', { name: 'Más' }))
    expect(screen.queryByRole('link', { name: /Mi plan/ })).toBeNull()
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

  it('una sola navegación: la de pestañas, con «Inicio» donde estaba el logo (mismo destino)', () => {
    pintar(['buscador'])
    // Sin la hoja abierta, la única navegación es la cápsula de pestañas (abajo o arriba según el ancho).
    expect(screen.getAllByRole('navigation')).toHaveLength(1)
    const pestanas = screen.getByRole('navigation', { name: 'Accesos rápidos' })
    expect(within(pestanas).getByRole('link', { name: 'Inicio' })).toHaveAttribute('href', '/')
  })

  it('con la hoja abierta están todos los enlaces que tenía la barra de escritorio', async () => {
    pintar(['buscador', 'prestador'])
    await userEvent.click(screen.getByRole('button', { name: 'Más' }))
    const hoja = within(screen.getByRole('navigation', { name: 'Principal' }))
    for (const [nombre, a] of [['Buscar', '/buscar'], ['Comunas', '/comunas'], ['Oficios', '/oficios'],
      ['Para profesionales', '/profesionales'], ['Mis citas', '/mis-citas'], ['Mis reseñas', '/mis-resenas'],
      ['Mi ficha', '/mi-ficha'], ['Mi agenda', '/mi-agenda'], ['Mis tarjetas', '/mis-tarjetas']]) {
      expect(hoja.getByRole('link', { name: nombre })).toHaveAttribute('href', a)
    }
    expect(hoja.getByRole('button', { name: 'Salir' })).toBeInTheDocument()
  })
})
