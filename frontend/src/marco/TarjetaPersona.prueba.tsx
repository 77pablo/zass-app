import { render, screen, within } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { describe, expect, it } from 'vitest'
import { TarjetaPersona, type DatosDeTarjeta } from './TarjetaPersona'

const BASE: DatosDeTarjeta = {
  slug: 'marcela-soto',
  nombre: 'Marcela Soto',
  rubro: 'Corretaje de propiedades',
  zona: 'Temuco',
  verificada: false,
}

function tarjeta(extra: Partial<DatosDeTarjeta> = {}) {
  render(<MemoryRouter><TarjetaPersona p={{ ...BASE, ...extra }} /></MemoryRouter>)
  return within(screen.getByRole('link', { name: /Marcela Soto/ }))
}

describe('TarjetaPersona', () => {
  it('con distancia sigue mostrando la comuna: «a 3 km» dice cuanto, no donde', () => {
    const t = tarjeta({ distancia: 'a 3 km', zona: 'Temuco' })

    expect(t.getByText('a 3 km')).toBeInTheDocument()
    // El candado de esta tarea: quien comparte su ubicacion NO pierde el nombre de la comuna.
    expect(t.getByText('Temuco')).toBeInTheDocument()
  })

  it('sin distancia muestra la comuna igual', () => {
    const t = tarjeta({ zona: 'Villarrica' })

    expect(t.getByText('Villarrica')).toBeInTheDocument()
    expect(t.queryByText(/km/)).toBeNull()
  })

  it('el pin no se repite: con las dos etiquetas, un solo icono de ubicacion', () => {
    const t = tarjeta({ distancia: 'a 3 km', zona: 'Temuco' })

    expect(t.getByText('a 3 km').querySelectorAll('svg')).toHaveLength(1)
    expect(t.getByText('Temuco').querySelectorAll('svg')).toHaveLength(0)
  })

  it('sin comuna no inventa una etiqueta vacia', () => {
    const t = tarjeta({ zona: null, distancia: 'a 3 km' })

    expect(t.getByText('a 3 km')).toBeInTheDocument()
    expect(t.getByText('a 3 km').querySelectorAll('svg')).toHaveLength(1)
  })
})
