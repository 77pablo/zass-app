import { render, screen } from '@testing-library/react'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { describe, expect, it, vi } from 'vitest'
import { AvisoDeTarjeta, TarjetaEnMayusculas } from './AvisoDeTarjeta'
import { TEXTOS_DE_TARJETA, type Aviso } from './textosDeTarjeta'

describe('AvisoDeTarjeta', () => {
  it.each(['no-valida', 'no-disponible', 'sin-activar'] as Aviso[])('%s muestra su titulo y por donde seguir', (aviso) => {
    render(<MemoryRouter><AvisoDeTarjeta aviso={aviso} /></MemoryRouter>)

    expect(screen.getByRole('heading', { level: 1 })).toHaveTextContent(TEXTOS_DE_TARJETA[aviso].titulo)
    // El cuerpo tambien se pinta: antes no lo comprobaba nadie y podia desaparecer en silencio.
    expect(screen.getByText(TEXTOS_DE_TARJETA[aviso].texto)).toBeInTheDocument()
    expect(screen.getByRole('link', { name: /Buscar a alguien/ })).toHaveAttribute('href', '/buscar')
    expect(screen.getByRole('link', { name: 'Ir al inicio' })).toHaveAttribute('href', '/')
    expect(document.querySelector('meta[name="robots"]')?.getAttribute('content')).toBe('noindex, nofollow')
  })

  /**
   * Las comprobaciones de arriba leen TEXTOS_DE_TARJETA, la misma constante que deberian
   * vigilar: si alguien repite una entrada o apunta dos claves al mismo objeto, siguen verdes
   * y los tres casos dicen lo mismo. Estas son las propiedades que de verdad importan y que
   * no se pueden comprobar contra la constante.
   */
  it('los tres avisos dicen cosas distintas y ninguno deja un texto vacio', () => {
    const avisos: Aviso[] = ['no-valida', 'no-disponible', 'sin-activar']
    const titulos = avisos.map((a) => TEXTOS_DE_TARJETA[a].titulo)
    const pestanas = avisos.map((a) => TEXTOS_DE_TARJETA[a].pestana)

    expect(new Set(titulos).size).toBe(3)
    expect(new Set(pestanas).size).toBe(3)
    for (const a of avisos) {
      const { pestana, titulo, texto } = TEXTOS_DE_TARJETA[a]
      expect(pestana.trim().length).toBeGreaterThan(0)
      expect(titulo.trim().length).toBeGreaterThan(0)
      // El cuerpo explica que hacer: una sola palabra no sirve de nada a quien acerco el telefono.
      expect(texto.trim().split(/\s+/).length).toBeGreaterThan(8)
    }
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
