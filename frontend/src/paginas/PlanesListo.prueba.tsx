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

describe('La vuelta del pago (API-75)', () => {
  it('con «aprobado» dice que el cupo quedó tomado y manda a Mi ficha', () => {
    enLaVuelta('?resultado=aprobado')

    expect(screen.getByRole('heading', { name: 'Listo: tu cupo quedó tomado' })).toBeInTheDocument()
    expect(screen.getByRole('link', { name: 'Mi ficha' })).toHaveAttribute('href', '/mi-ficha')
  })

  it('con «rechazado» dice que no se cobró nada y ofrece volver a intentarlo', () => {
    enLaVuelta('?resultado=rechazado')

    expect(screen.getByRole('heading', { name: 'No pudimos inscribir tu tarjeta' })).toBeInTheDocument()
    expect(screen.getByText(/no se cobró nada/)).toBeInTheDocument()
    expect(screen.getByRole('link', { name: 'Volver a intentarlo' })).toHaveAttribute('href', '/planes')
  })

  it('sin resultado espera la confirmación, sin prometer nada', () => {
    enLaVuelta('')

    expect(screen.getByRole('heading', { name: 'Estamos confirmando tu cupo' })).toBeInTheDocument()
  })

  // Lo que manda es la API. Un valor que no es uno de los tres (escrito a mano en la URL, o
  // uno que la API agregue mañana) NUNCA puede terminar diciendo «listo».
  it.each(['Aprobado', 'aprobado ', 'ok', 'https://malo.example'])(
    'con un valor desconocido («%s») no dice «listo»', (valor) => {
      enLaVuelta(`?resultado=${encodeURIComponent(valor)}`)

      expect(screen.getByRole('heading', { name: 'Estamos confirmando tu cupo' })).toBeInTheDocument()
      expect(screen.queryByText(/quedó tomado/)).toBeNull()
    })
})
