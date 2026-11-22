import { render, screen } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import { Campo } from './Campo'

const base = { valor: '', alCambiar: () => {} }

describe('Campo: correos, telefonos y webs, como los espera el telefono', () => {
  it.each(['email', 'tel', 'url'])('tipo %s: sin corrector ni mayuscula automatica', (tipo) => {
    render(<Campo id="c" etiqueta="Dato" tipo={tipo} {...base} />)
    const campo = screen.getByLabelText('Dato')
    expect(campo).toHaveAttribute('spellcheck', 'false')
    expect(campo).toHaveAttribute('autocapitalize', 'none')
  })

  it('un texto normal deja el corrector como venga (un nombre si se corrige)', () => {
    render(<Campo id="c" etiqueta="Nombre" {...base} />)
    const campo = screen.getByLabelText('Nombre')
    expect(campo).not.toHaveAttribute('spellcheck')
    expect(campo).not.toHaveAttribute('autocapitalize')
  })

  it('el teclado se elige sin cambiar el tipo (el navegador no valida por su cuenta)', () => {
    render(<Campo id="c" etiqueta="Correo" teclado="email" {...base} />)
    const campo = screen.getByLabelText('Correo')
    expect(campo).toHaveAttribute('type', 'text')
    expect(campo).toHaveAttribute('inputmode', 'email')
    expect(campo).toHaveAttribute('spellcheck', 'false')
  })
})
