import { afterEach, describe, expect, it } from 'vitest'
import { MemoryRouter } from 'react-router-dom'
import { render, screen } from '@testing-library/react'
import { NoEncontrada } from './NoEncontrada'

afterEach(() => { document.head.innerHTML = '' })

describe('NoEncontrada', () => {
  it('muestra el 404 con salidas a buscar, oficios, comunas e inicio', () => {
    render(<MemoryRouter><NoEncontrada /></MemoryRouter>)

    expect(screen.getByRole('heading', { level: 1, name: 'Esta página no existe.' })).toBeInTheDocument()
    expect(screen.getByRole('link', { name: /Buscar a alguien/i })).toHaveAttribute('href', '/buscar')
    expect(screen.getByRole('link', { name: 'Ver oficios' })).toHaveAttribute('href', '/oficios')
    expect(screen.getByRole('link', { name: 'Ver comunas' })).toHaveAttribute('href', '/comunas')
    expect(screen.getByRole('link', { name: 'Ir al inicio' })).toHaveAttribute('href', '/')
  })

  it('pide noindex a los buscadores', () => {
    render(<MemoryRouter><NoEncontrada /></MemoryRouter>)

    const robots = document.head.querySelector<HTMLMetaElement>('meta[name="robots"]')
    expect(robots?.content).toBe('noindex, nofollow')
  })

  it('permite personalizar titulo y texto manteniendo los enlaces de salida', () => {
    render(
      <MemoryRouter>
        <NoEncontrada titulo="Ficha no disponible." texto="Esta persona ya no está." />
      </MemoryRouter>,
    )

    expect(screen.getByRole('heading', { level: 1, name: 'Ficha no disponible.' })).toBeInTheDocument()
    expect(screen.getByText('Esta persona ya no está.')).toBeInTheDocument()
    expect(screen.getByRole('link', { name: 'Ver oficios' })).toHaveAttribute('href', '/oficios')
    expect(screen.getByRole('link', { name: 'Ver comunas' })).toHaveAttribute('href', '/comunas')
  })
})
