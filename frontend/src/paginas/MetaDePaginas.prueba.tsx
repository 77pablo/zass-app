import { afterEach, describe, expect, it, vi } from 'vitest'
import { render } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { NoEncontrada } from './NoEncontrada'
import { Oficios } from './Oficios'
import { PaginaBuscar } from './PaginaBuscar'
import { Profesionales } from './Profesionales'

/** La de `index.html`, escrita a mano: si se derivara del archivo, cambiar ambos dejaria la prueba verde. */
const DESCRIPCION_DE_PORTADA =
  'Busca a alguien de tu ciudad: abogados, contadores, corredores, arquitectos y técnicos, cerca de ti. Buscar en ZASS! es gratis.'

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

  /** S2: /buscar heredaba la descripcion de la portada (la de index.html). */
  it('«buscar» tiene descripcion propia, indexable, distinta de la de la portada', () => {
    vi.stubGlobal('fetch', vi.fn(async () => new Response('[]', { status: 200, headers: { 'content-type': 'application/json' } })))
    render(<MemoryRouter initialEntries={['/buscar']}><PaginaBuscar /></MemoryRouter>)
    expect(descripcion()!.content).toMatch(/busca/i)
    expect(descripcion()!.content).not.toBe(DESCRIPCION_DE_PORTADA)
    expect(robots()).toBeNull()
    vi.unstubAllGlobals()
  })

  it('«oficios» tiene descripcion propia e indexable', () => {
    render(<MemoryRouter><Oficios /></MemoryRouter>)
    expect(descripcion()!.content).toMatch(/oficios/i)
    expect(robots()).toBeNull()
  })
})
