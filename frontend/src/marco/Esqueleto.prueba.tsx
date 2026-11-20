import { render, screen } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { describe, expect, it } from 'vitest'
import { ResultadosEsqueleto, TarjetaPersonaEsqueleto } from './Esqueleto'
import { PaginaCargando } from './PaginaCargando'
import { TarjetaPersona } from './TarjetaPersona'

/*
 * Lo que jsdom no puede medir (que el esqueleto ocupe los mismos pixeles que la tarjeta real)
 * se midio en Chromium el 18-nov-2026: a 390 px las tarjetas caen en 964 y 1223 px, igual que sus
 * esqueletos (zass-tareas/capturas-interfaz/esqueleto.cjs). Lo que se vigila aqui es lo que lo
 * hace posible: que el esqueleto use la MISMA estructura de clases que la tarjeta, y que no le
 * hable al lector de pantalla.
 */

/** La forma de una tarjeta: las clases de sus hijos directos, en orden. */
function forma(raiz: Element) {
  return [...raiz.children].map((h) => [...h.classList].filter((c) => !c.endsWith('--esqueleto')).join('.'))
}

describe('esqueletos', () => {
  it('la tarjeta-esqueleto tiene la misma estructura que la tarjeta real', () => {
    const { container: real } = render(
      <MemoryRouter>
        <TarjetaPersona p={{ slug: 'a', nombre: 'Ana Pérez', rubro: 'Derecho', zona: 'Centro', verificada: true, detalle: '4,8 de 5', precioDesde: 30000 }} />
      </MemoryRouter>,
    )
    const { container: esqueleto } = render(<TarjetaPersonaEsqueleto />)

    expect(forma(esqueleto.firstElementChild!)).toEqual(forma(real.firstElementChild!))
    expect(esqueleto.firstElementChild).toHaveClass('tarjeta-persona')
  })

  it('los resultados-esqueleto no le dicen nada al lector de pantalla', () => {
    const { container } = render(<ResultadosEsqueleto />)

    for (const hijo of container.children) expect(hijo).toHaveAttribute('aria-hidden', 'true')
    expect(screen.queryAllByRole('listitem')).toHaveLength(0)
  })

  it('reservan el lugar del recuento: la rejilla va pegada a el, como en los resultados', () => {
    const { container } = render(<ResultadosEsqueleto cuantas={2} />)

    expect(container.children[0]).toHaveClass('resultados-cuenta')
    expect(container.children[1]).toHaveClass('tarjetas-rejilla')
    expect(container.querySelectorAll('.tarjeta-persona--esqueleto')).toHaveLength(2)
  })

  it('la pantalla del panel que carga: titulo real, aviso al lector y un esqueleto a la vista', () => {
    const { container } = render(<PaginaCargando titulo="Mis citas" aviso="Cargando tus citas…" />)

    expect(screen.getByRole('heading', { level: 1 })).toHaveTextContent('Mis citas')
    expect(screen.getByRole('status')).toHaveTextContent('Cargando tus citas…')
    const esqueleto = container.querySelector('.lista-esqueleto')
    expect(esqueleto).not.toBeNull()
    expect(esqueleto).toHaveAttribute('aria-hidden', 'true')
  })
})
