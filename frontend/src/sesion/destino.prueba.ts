import { describe, expect, it } from 'vitest'
import { destinoDe } from './destino'

// Una redireccion abierta deja que un enlace a ZASS! mande a quien entra a otro sitio, ya con la
// confianza de haber iniciado sesion aqui. Solo se acepta una ruta del propio sitio.
describe('a donde volver tras entrar', () => {
  it('acepta una ruta del propio sitio', () => {
    expect(destinoDe({ despues: '/mi-ficha' })).toBe('/mi-ficha')
  })

  it.each([
    ['https://malo.example', 'una URL completa'],
    ['//malo.example', 'una URL sin esquema'],
    ['javascript:alert(1)', 'un javascript:'],
    ['mi-ficha', 'una ruta sin barra'],
  ])('rechaza %s (%s)', (valor) => {
    expect(destinoDe({ despues: valor })).toBeNull()
  })

  it('rechaza lo que no es texto y la falta de estado', () => {
    expect(destinoDe({ despues: 5 })).toBeNull()
    expect(destinoDe(null)).toBeNull()
    expect(destinoDe(undefined)).toBeNull()
  })
})
