import { render, screen } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { olvidarCaches } from '../api/useCargado'
import { Oficios } from './Oficios'

/*
 * Un oficio sin nadie no es un callejon sin salida: es una invitacion a quien lo ejerce.
 * Lo que tiene gente sigue llevando a la busqueda.
 */

function json(cuerpo: unknown) {
  return new Response(JSON.stringify(cuerpo), { status: 200, headers: { 'content-type': 'application/json' } })
}

const RUBROS = [
  { id: 1, codigo: 'derecho', nombre: 'derecho', icono: null },
  { id: 2, codigo: 'arquitectura', nombre: 'arquitectura', icono: null },
]
const COMUNIDAD = { total: 2, porZona: [], porRegion: [], recienLlegados: [], porRubro: [{ rubroId: 1, personas: 2 }] }

function pintar(comunidad: () => Promise<Response>) {
  vi.stubGlobal('fetch', vi.fn(async (ruta: string) => {
    if (ruta === '/api/rubros') return json(RUBROS)
    if (ruta === '/api/zonas') return json([])
    if (ruta === '/api/comunidad') return comunidad()
    throw new Error(`ruta inesperada: ${ruta}`)
  }))
  render(<MemoryRouter><Oficios /></MemoryRouter>)
}

beforeEach(() => olvidarCaches())
afterEach(() => vi.unstubAllGlobals())

describe('Oficios: el oficio sin nadie', () => {
  it('invita a ofrecerlo y lleva a la pagina para profesionales', async () => {
    pintar(async () => json(COMUNIDAD))

    const invitacion = await screen.findByRole('link', { name: /Arquitectura.*Sé el primero\s*en ofrecerlo/ })
    expect(invitacion).toHaveAttribute('href', '/profesionales')
    expect(screen.queryByText('Aún nadie')).not.toBeInTheDocument()
  })

  it('el que tiene gente sigue llevando a la busqueda', async () => {
    pintar(async () => json(COMUNIDAD))

    const conGente = await screen.findByRole('link', { name: /Derecho.*2 personas/ })
    expect(conGente).toHaveAttribute('href', '/buscar?q=derecho')
  })

  it('mientras la comunidad no llega, no invita a nadie: todavia no se sabe si esta vacio', async () => {
    pintar(() => new Promise<Response>(() => {}))

    const fila = await screen.findByRole('link', { name: /Arquitectura/ })
    expect(fila).toHaveAttribute('href', '/buscar?q=arquitectura')
    expect(screen.queryByText(/Sé el primero/)).not.toBeInTheDocument()
  })
})
