import { render, screen } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { describe, expect, it } from 'vitest'
import { PlanesListo } from './Planes'

function enLaVuelta(busqueda: string) {
  return render(
    <MemoryRouter initialEntries={[`/planes/listo${busqueda}`]}>
      <PlanesListo />
    </MemoryRouter>,
  )
}

describe('La vuelta de inscribir la tarjeta (API-75)', () => {
  it('con «aprobado» dice que la tarjeta quedó inscrita y manda a Mi ficha', () => {
    enLaVuelta('?resultado=aprobado')

    expect(screen.getByRole('heading', { name: 'Listo: tu tarjeta quedó inscrita' })).toBeInTheDocument()
    expect(screen.getByText(/No se cobró nada/)).toBeInTheDocument()
    expect(screen.getByRole('link', { name: 'Mi ficha' })).toHaveAttribute('href', '/mi-ficha')
  })

  it('con «rechazado» dice que no se cobró nada, que el cupo sigue siendo suyo, y ofrece otra tarjeta', () => {
    enLaVuelta('?resultado=rechazado')

    expect(screen.getByRole('heading', { name: 'No pudimos inscribir tu tarjeta' })).toBeInTheDocument()
    expect(screen.getByText(/no se cobró nada/)).toBeInTheDocument()
    expect(screen.getByText(/Tu cupo sigue siendo tuyo/)).toBeInTheDocument()
    expect(screen.getByRole('link', { name: 'Probar con otra tarjeta' })).toHaveAttribute('href', '/mi-ficha')
  })

  it('sin resultado espera la confirmación, sin prometer nada', () => {
    enLaVuelta('')

    expect(screen.getByRole('heading', { name: 'Estamos confirmando tu tarjeta' })).toBeInTheDocument()
  })

  // Lo que manda es la API. Un valor que no es uno de los tres (escrito a mano en la URL, o
  // uno que la API agregue mañana) NUNCA puede terminar diciendo «listo».
  it.each(['Aprobado', 'aprobado ', 'ok', 'https://malo.example'])(
    'con un valor desconocido («%s») no dice «listo»', (valor) => {
      enLaVuelta(`?resultado=${encodeURIComponent(valor)}`)

      expect(screen.getByRole('heading', { name: 'Estamos confirmando tu tarjeta' })).toBeInTheDocument()
      expect(screen.queryByText(/quedó inscrita/)).toBeNull()
    })
})
