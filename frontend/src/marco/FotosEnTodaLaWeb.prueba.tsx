import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router-dom'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { App } from '../App'
import { olvidarCsrf } from '../api/cliente'
import { olvidarCaches } from '../api/useCargado'
import { olvidarSugerencias } from '../api/useSugerencias'
import { ProveedorDeSesion } from '../sesion/Sesion'
import { BarraDeBusqueda } from './BarraDeBusqueda'

/*
 * JQ-15. La foto de JQ-9 donde todavía faltaba: «Recién llegados» de la portada y las sugerencias
 * de la barra de búsqueda. Quien subió foto se ve con su foto; quien no, con sus iniciales.
 */

const FOTO = '/archivos/fotos/rosa-288.webp'

function json(cuerpo: unknown, estado = 200) {
  return new Response(JSON.stringify(cuerpo), { status: estado, headers: { 'content-type': 'application/json' } })
}

beforeEach(() => {
  olvidarCsrf()
  olvidarCaches()
  olvidarSugerencias()
  localStorage.clear()
})
afterEach(() => vi.unstubAllGlobals())

describe('JQ-15: la portada muestra la foto de los recién llegados', () => {
  it('con foto, la foto; sin foto, las iniciales', async () => {
    vi.stubGlobal('fetch', vi.fn(async (ruta: string) => {
      if (ruta === '/api/rubros' || ruta === '/api/zonas') return json([])
      if (ruta === '/api/comunidad') return json({
        total: 2, porZona: [], porRegion: [], porRubro: [],
        recienLlegados: [
          { slug: 'rosa-millan', nombrePublico: 'Rosa Millán', rubro: 'Gasfitería', zona: 'Centro', verificada: false, urlFoto: FOTO },
          { slug: 'luis-catrileo', nombrePublico: 'Luis Catrileo', rubro: 'Electricidad', zona: 'Centro', verificada: false, urlFoto: null },
        ],
      })
      return json({ title: 'No autorizado' }, 401)
    }))
    const { container } = render(
      <MemoryRouter initialEntries={['/']}><ProveedorDeSesion><App /></ProveedorDeSesion></MemoryRouter>,
    )

    // La tarjeta flotante de «Gente que se sumó hace poco», que es la que lleva la cara.
    await screen.findAllByText('Rosa Millán')
    const flotantes = container.querySelector('[aria-label="Gente que se sumó hace poco"]') as HTMLElement
    const rosa = within(flotantes).getByText('Rosa Millán').closest('a')!
    expect(rosa.querySelector('img.monograma--foto')).toHaveAttribute('src', FOTO)

    const luis = within(flotantes).getByText('Luis Catrileo').closest('a')!
    expect(luis.querySelector('img')).toBeNull()
    expect(luis.querySelector('.monograma')).toHaveTextContent('LC')

    // Y la rejilla «Se sumaron hace poco», más abajo: la misma regla.
    const rejilla = screen.getByRole('heading', { name: 'Se sumaron hace poco' }).closest('section')!
    const rosaAbajo = within(rejilla).getByText('Rosa Millán').closest('li')!
    expect(rosaAbajo.querySelector('img.monograma--foto')).toHaveAttribute('src', FOTO)
    const luisAbajo = within(rejilla).getByText('Luis Catrileo').closest('li')!
    expect(luisAbajo.querySelector('img')).toBeNull()
    expect(luisAbajo.querySelector('.monograma')).toHaveTextContent('LC')
  })
})

describe('JQ-15: las sugerencias de la barra muestran la foto', () => {
  const persona = (slug: string, nombre: string, urlFoto: string | null) => ({
    perfilId: slug, slug, nombrePublico: nombre, descripcion: null, urlFoto, rubro: 'Gasfitería',
    zonaPrincipal: 'Centro', distancia: null, tieneCredencialVerificada: false,
    promedioCalificacion: null, totalResenas: 0,
  })

  it('con foto, la foto; sin foto, las iniciales', async () => {
    vi.stubGlobal('fetch', vi.fn(async (ruta: string) => {
      if (ruta.startsWith('/api/buscar')) return json({
        resultados: [persona('rosa-millan', 'Rosa Millán', FOTO), persona('raul-ruiz', 'Raúl Ruiz', null)],
        total: 2, pagina: 1, hayMas: false, ordenAplicado: 'Relevancia',
      })
      return json([])
    }))
    render(<MemoryRouter><BarraDeBusqueda /></MemoryRouter>)

    await userEvent.type(screen.getByRole('combobox'), 'ra')
    const lista = await screen.findByRole('listbox', { name: 'Sugerencias' })
    const rosa = (await within(lista).findByText('Rosa Millán')).closest('[role="option"]')!
    expect(rosa.querySelector('img.monograma--foto')).toHaveAttribute('src', FOTO)

    const raul = within(lista).getByText('Raúl Ruiz').closest('[role="option"]')!
    expect(raul.querySelector('img')).toBeNull()
    expect(raul.querySelector('.monograma')).toHaveTextContent('RR')
  })
})
