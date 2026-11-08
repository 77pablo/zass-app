import { afterEach, describe, expect, it } from 'vitest'
import { render } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { NoEncontrada } from './NoEncontrada'
import { Oficios } from './Oficios'
import { Profesionales } from './Profesionales'

afterEach(() => { document.head.innerHTML = '' })

const robots = () => document.head.querySelector<HTMLMetaElement>('meta[name="robots"]')
const descripcion = () => document.head.querySelector<HTMLMetaElement>('meta[name="description"]')

describe('meta de las paginas', () => {
  it('«no encontrada» queda fuera del indice', () => {
    render(<MemoryRouter><NoEncontrada /></MemoryRouter>)
    expect(robots()!.content).toBe('noindex, nofollow')
  })

  it('«para profesionales» tiene descripcion propia, indexable, y no promete lo que no existe', () => {
    render(<MemoryRouter><Profesionales /></MemoryRouter>)
    expect(descripcion()!.content).toMatch(/mensualidad/)
    expect(descripcion()!.content).not.toMatch(/agenda|pago en l[ií]nea|sello/i)
    expect(robots()).toBeNull()
  })

  it('«oficios» tiene descripcion propia e indexable', () => {
    render(<MemoryRouter><Oficios /></MemoryRouter>)
    expect(descripcion()!.content).toMatch(/oficios/i)
    expect(robots()).toBeNull()
  })
})
