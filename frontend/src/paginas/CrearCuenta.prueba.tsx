import { fireEvent, render, screen } from '@testing-library/react'
import { MemoryRouter, Route, Routes, useLocation } from 'react-router-dom'
import { describe, expect, it } from 'vitest'
import { CrearCuenta } from './CrearCuenta'

/**
 * D2 del recorrido en navegador del 21-nov: «Guardar» (o agendar) sin sesión lleva a crear cuenta
 * con `state.despues`, y el enlace a «Entrar» lo perdía: tras entrar, la persona caía en la
 * portada en vez de volver a la ficha. Entrar ya sabe volver (destinoDe); faltaba pasárselo.
 */

/** Hace de «Entrar»: muestra el estado con que se llegó, que es lo que Entrar usaría para volver. */
export function EntrarDePrueba() {
  const { state } = useLocation()
  return <p data-testid="estado-en-entrar">{JSON.stringify(state ?? null)}</p>
}

function pintar(state: unknown) {
  return render(
    <MemoryRouter initialEntries={[{ pathname: '/crear-cuenta', state }]}>
      <Routes>
        <Route path="crear-cuenta" element={<CrearCuenta />} />
        <Route path="entrar" element={<EntrarDePrueba />} />
      </Routes>
    </MemoryRouter>,
  )
}

describe('crear cuenta conserva a dónde quería volver la persona', () => {
  it('«¿Ya tienes cuenta? Entra» lleva el destino a Entrar', () => {
    pintar({ despues: '/prestadores/marcela-soto' })

    fireEvent.click(screen.getByRole('link', { name: 'Entra' }))

    expect(JSON.parse(screen.getByTestId('estado-en-entrar').textContent!))
      .toEqual({ despues: '/prestadores/marcela-soto' })
  })

  it('sin destino, Entrar llega sin estado (y decide por el rol, como siempre)', () => {
    pintar(null)

    fireEvent.click(screen.getByRole('link', { name: 'Entra' }))

    expect(screen.getByTestId('estado-en-entrar').textContent).toBe('null')
  })

  it.each([['//otro-sitio.example'], ['https://otro-sitio.example'], [42]])(
    'un destino que no es una ruta propia (%s) no se reenvía',
    (despues) => {
      pintar({ despues })

      fireEvent.click(screen.getByRole('link', { name: 'Entra' }))

      expect(screen.getByTestId('estado-en-entrar').textContent).toBe('null')
    },
  )
})
