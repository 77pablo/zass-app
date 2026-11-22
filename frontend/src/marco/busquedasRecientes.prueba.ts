import { beforeEach, describe, expect, it, vi } from 'vitest'
import { busquedasRecientes, MAXIMO_DE_RECIENTES, olvidarBusquedas, recordarBusqueda } from './busquedasRecientes'

beforeEach(() => {
  localStorage.clear()
  vi.restoreAllMocks()
})

describe('busquedas recientes', () => {
  it('la ultima va primero', () => {
    recordarBusqueda('abogado')
    recordarBusqueda('gasfiter')
    expect(busquedasRecientes()).toEqual(['gasfiter', 'abogado'])
  })

  it('no repite: la misma busqueda (mayusculas o espacios aparte) sube al principio', () => {
    recordarBusqueda('abogado')
    recordarBusqueda('gasfiter')
    recordarBusqueda('  Abogado ')
    expect(busquedasRecientes()).toEqual(['Abogado', 'gasfiter'])
  })

  it(`guarda como mucho ${MAXIMO_DE_RECIENTES}`, () => {
    for (const b of ['a1', 'a2', 'a3', 'a4', 'a5', 'a6', 'a7']) recordarBusqueda(b)
    expect(busquedasRecientes()).toEqual(['a7', 'a6', 'a5', 'a4', 'a3'])
  })

  it('no guarda una busqueda vacia', () => {
    recordarBusqueda('   ')
    expect(busquedasRecientes()).toEqual([])
  })

  it('olvidar las borra todas', () => {
    recordarBusqueda('abogado')
    olvidarBusquedas()
    expect(busquedasRecientes()).toEqual([])
  })

  it('si alguien dejo basura en el almacenamiento, no se cae ni la muestra', () => {
    localStorage.setItem('zass-busquedas', '{no es json')
    expect(busquedasRecientes()).toEqual([])
    localStorage.setItem('zass-busquedas', JSON.stringify(['ok', 42, '', 'x'.repeat(500)]))
    expect(busquedasRecientes()).toEqual(['ok'])
  })

  it('sin almacenamiento (modo privado) funciona igual, sin recientes', () => {
    vi.spyOn(Storage.prototype, 'getItem').mockImplementation(() => { throw new Error('bloqueado') })
    vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => { throw new Error('bloqueado') })
    expect(() => recordarBusqueda('abogado')).not.toThrow()
    expect(busquedasRecientes()).toEqual([])
  })
})
