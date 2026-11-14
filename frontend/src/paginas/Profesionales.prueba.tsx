import { render, screen } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { describe, expect, it } from 'vitest'
import { Profesionales } from './Profesionales'

/*
 * Lo que esta prueba puede y lo que NO puede.
 *
 * NO puede medir 44 px: jsdom no calcula tamaños, así que cualquier `getBoundingClientRect`
 * aquí devuelve ceros y un verde no diría nada. Quien mide de verdad es
 * `pruebas-navegador/mirar-sitio.mjs`, en un navegador con layout.
 *
 * Lo que sí puede, y es lo que hace: vigilar la DECISIÓN. La llamada a la acción hacia
 * `/planes` apareció el 14-nov-2026 como texto plano de 20 px de alto —no es un enlace
 * dentro de una frase, así que la excepción «Inline» de WCAG 2.5.8 no la cubre— y va como
 * botón, que es lo que le da los 44. Si alguien le quita la clase, esto cae y el guion de
 * navegador lo confirma.
 */
describe('Para profesionales', () => {
  it('la llamada a la acción hacia los planes es un botón, no texto suelto', () => {
    render(<MemoryRouter><Profesionales /></MemoryRouter>)

    const enlace = screen.getByRole('link', { name: /ver los planes/i })

    expect(enlace).toHaveClass('boton')
    expect(enlace.getAttribute('href')).toBe('/planes')
  })
})
