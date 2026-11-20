import { render, screen } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { describe, expect, it } from 'vitest'
import { TarjetaPersona, type DatosDeTarjeta } from './TarjetaPersona'

const BASE: DatosDeTarjeta = { slug: 'ana', nombre: 'Ana Pérez', rubro: 'Derecho', zona: 'Temuco', verificada: false }

function pintar(extra: Partial<DatosDeTarjeta>) {
  return render(<MemoryRouter><TarjetaPersona p={{ ...BASE, ...extra }} /></MemoryRouter>)
}

/*
 * B3 (revision del 13-nov): con la distancia puesta se ocultaba la comuna. En Chile se decide por
 * la comuna («atiende en Temuco»), no por los kilometros: los dos datos tienen que verse juntos.
 */
describe('la tarjeta de una persona', () => {
  it('con distancia muestra tambien la comuna', () => {
    pintar({ distancia: 'a 3 km' })

    expect(screen.getByText('Temuco')).toBeInTheDocument()
    expect(screen.getByText('a 3 km')).toBeInTheDocument()
  })

  it('sin distancia muestra la comuna sola', () => {
    pintar({ distancia: null })

    expect(screen.getByText('Temuco')).toBeInTheDocument()
    expect(screen.queryByText(/km/)).not.toBeInTheDocument()
  })
})
