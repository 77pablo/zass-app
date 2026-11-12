import { render, screen } from '@testing-library/react'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { describe, expect, it, vi } from 'vitest'
import { AvisoDeTarjeta, TarjetaEnMayusculas } from './AvisoDeTarjeta'
import { TEXTOS_DE_TARJETA, type Aviso } from './textosDeTarjeta'

describe('AvisoDeTarjeta', () => {
  it.each(['no-valida', 'no-disponible', 'sin-activar'] as Aviso[])('%s muestra su titulo y por donde seguir', (aviso) => {
    render(<MemoryRouter><AvisoDeTarjeta aviso={aviso} /></MemoryRouter>)

    expect(screen.getByRole('heading', { level: 1 })).toHaveTextContent(TEXTOS_DE_TARJETA[aviso].titulo)
    expect(screen.getByRole('link', { name: /Buscar a alguien/ })).toHaveAttribute('href', '/buscar')
    expect(screen.getByRole('link', { name: 'Ir al inicio' })).toHaveAttribute('href', '/')
    expect(document.querySelector('meta[name="robots"]')?.getAttribute('content')).toBe('noindex, nofollow')
  })
})

describe('TarjetaEnMayusculas', () => {
  function enRuta(ruta: string, navegar: (url: string) => void) {
    render(
      <MemoryRouter initialEntries={[ruta]}>
        <Routes><Route path="t/:segmento" element={<TarjetaEnMayusculas navegar={navegar} />} /></Routes>
      </MemoryRouter>,
    )
  }

  it('/T/ en mayusculas va a /t/ con la consulta intacta', () => {
    const navegar = vi.fn()
    enRuta('/T/7KQ2-9XRM?M=QR', navegar)

    expect(navegar).toHaveBeenCalledWith('/t/7KQ2-9XRM?M=QR')
  })

  it('/t/ en minuscula NO redirige (seria un ciclo): muestra no encontrada', () => {
    const navegar = vi.fn()
    enRuta('/t/7KQ2-9XRM', navegar)

    expect(navegar).not.toHaveBeenCalled()
    expect(screen.getByRole('heading', { level: 1 })).toHaveTextContent('Esta página no existe.')
  })
})
