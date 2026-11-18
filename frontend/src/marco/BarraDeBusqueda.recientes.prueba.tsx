import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter, Route, Routes, useLocation } from 'react-router-dom'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { olvidarSugerencias } from '../api/useSugerencias'
import { BarraDeBusqueda } from './BarraDeBusqueda'
import { busquedasRecientes, recordarBusqueda } from './busquedasRecientes'

/** Muestra la direccion actual. Exportado solo para que el lint no avise (recarga en caliente). */
export function Donde() {
  const l = useLocation()
  return <output data-testid="url">{l.pathname + l.search}</output>
}

function pintar() {
  render(
    <MemoryRouter initialEntries={['/']}>
      <Routes><Route path="*" element={<><BarraDeBusqueda variante="portada" /><Donde /></>} /></Routes>
    </MemoryRouter>,
  )
  return screen.getByRole('combobox')
}

beforeEach(() => {
  localStorage.clear()
  olvidarSugerencias()
  vi.stubGlobal('fetch', vi.fn(async () => new Response(JSON.stringify({ personas: [], total: 0 }), { status: 200, headers: { 'content-type': 'application/json' } })))
})

describe('el buscador recuerda lo que buscaste', () => {
  it('con el campo vacio, lo que buscaste antes va primero', async () => {
    recordarBusqueda('abogado')
    recordarBusqueda('gasfiter')
    const u = userEvent.setup()
    await u.click(pintar())

    const opciones = screen.getAllByRole('option').map((o) => o.textContent)
    expect(opciones[0]).toContain('gasfiter')
    expect(opciones[0]).toContain('Buscaste antes')
    expect(opciones[1]).toContain('abogado')
    expect(opciones[2]).toContain('Borrar búsquedas recientes')
  })

  it('un ejemplo que ya buscaste no aparece dos veces', async () => {
    recordarBusqueda('Contadora')
    const u = userEvent.setup()
    await u.click(pintar())

    const textos = screen.getAllByRole('option').map((o) => o.textContent ?? '')
    expect(textos.filter((t) => t.toLowerCase().startsWith('contadora'))).toHaveLength(1)
  })

  it('sin recientes, solo los ejemplos de siempre', async () => {
    const u = userEvent.setup()
    await u.click(pintar())

    expect(screen.queryByText('Buscaste antes')).not.toBeInTheDocument()
    expect(screen.getAllByRole('option')).toHaveLength(5)
  })

  it('buscar algo lo recuerda, y elegir una reciente la busca de nuevo', async () => {
    const u = userEvent.setup()
    const campo = pintar()
    await u.type(campo, 'contadora{Enter}')
    expect(busquedasRecientes()).toEqual(['contadora'])
    expect(screen.getByTestId('url')).toHaveTextContent('/buscar?q=contadora')
  })

  it('se elige con el teclado, como las demas opciones', async () => {
    recordarBusqueda('arquitecto')
    const u = userEvent.setup()
    await u.click(pintar())
    await u.keyboard('{ArrowDown}{Enter}')

    expect(screen.getByTestId('url')).toHaveTextContent('/buscar?q=arquitecto')
  })

  it('«Borrar búsquedas recientes» las olvida, en la lista y en el telefono', async () => {
    recordarBusqueda('abogado')
    const u = userEvent.setup()
    await u.click(pintar())
    await u.pointer({ keys: '[MouseLeft>]', target: screen.getByText('Borrar búsquedas recientes') })

    expect(busquedasRecientes()).toEqual([])
    expect(screen.queryByText('Buscaste antes')).not.toBeInTheDocument()
  })
})
