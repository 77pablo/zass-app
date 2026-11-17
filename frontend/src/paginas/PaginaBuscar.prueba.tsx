import { render, screen, within } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { App } from '../App'
import { olvidarCsrf } from '../api/cliente'
import { olvidarCaches } from '../api/useCargado'
import { olvidarSugerencias } from '../api/useSugerencias'
import { olvidarUbicacion } from '../marco/ubicacion'
import { ProveedorDeSesion } from '../sesion/Sesion'

/*
 * La busqueda, oida con un lector de pantalla y usada con el dedo.
 *
 * Cuatro cosas que estaban mal y se ven solo asi:
 *  - los filtros vivian DENTRO del grupo «Ordenar por», y se anunciaban como formas de ordenar;
 *  - lo que hace cada filtro estaba en un `title`, que en un telefono no existe (no hay hover);
 *  - el bloque entero era una region viva: cada cambio releia la lista completa;
 *  - faltaba el `h2` entre el `h1` de la pagina y el `h3` de cada tarjeta.
 */

const TEXTO = {
  resultados: [{
    perfilId: 'p1', slug: 'marcela-soto', nombrePublico: 'Marcela Soto', descripcion: null, urlFoto: null,
    rubro: 'Corretaje de propiedades', zonaPrincipal: 'Centro', distancia: null, tieneCredencialVerificada: true,
    promedioCalificacion: 4.8, totalResenas: 12, precioDesde: 60000,
  }],
  total: 1, pagina: 1, hayMas: false, ordenAplicado: 'Recomendados', conUbicacion: false, aviso: null,
}

function json(cuerpo: unknown, estado = 200) {
  return new Response(JSON.stringify(cuerpo), { status: estado, headers: { 'content-type': 'application/json' } })
}

beforeEach(() => {
  olvidarCsrf()
  olvidarCaches()
  olvidarSugerencias()
  olvidarUbicacion()
  localStorage.clear()
  vi.stubGlobal('fetch', vi.fn(async (ruta: string) => {
    if (ruta === '/api/rubros') return json([{ id: 1, codigo: 'corretaje', nombre: 'Corretaje de propiedades', icono: null }])
    if (ruta === '/api/zonas') return json([])
    if (ruta === '/api/comunidad') return json({ total: 1, porZona: [], porRegion: [], porRubro: [], recienLlegados: [] })
    if (ruta.startsWith('/api/buscar')) return json(TEXTO)
    return json({ title: 'No autorizado' }, 401)
  }))
})
afterEach(() => vi.unstubAllGlobals())

async function buscar() {
  render(
    <MemoryRouter initialEntries={['/buscar?q=marcela']}>
      <ProveedorDeSesion><App /></ProveedorDeSesion>
    </MemoryRouter>,
  )
  await screen.findAllByRole('link', { name: /Marcela Soto/ })
}

describe('la busqueda con lector de pantalla y con el dedo', () => {
  it('los filtros tienen su propio grupo y no se anuncian como formas de ordenar', async () => {
    await buscar()

    const ordenar = screen.getByRole('group', { name: 'Ordenar por' })
    const filtrar = screen.getByRole('group', { name: 'Filtrar los resultados' })

    for (const filtro of ['Solo verificados', '4 estrellas o más']) {
      expect(within(filtrar).getByRole('button', { name: filtro })).toBeInTheDocument()
      expect(within(ordenar).queryByRole('button', { name: filtro })).not.toBeInTheDocument()
    }
  })

  it('lo que hace cada filtro se lee en pantalla y lo lee el lector, no vive en un title', async () => {
    await buscar()

    const verificados = screen.getByRole('button', { name: 'Solo verificados' })
    const estrellas = screen.getByRole('button', { name: '4 estrellas o más' })

    expect(verificados).toHaveAccessibleDescription(/Verificamos títulos y certificaciones/)
    expect(estrellas).toHaveAccessibleDescription(/desde la tercera reseña/)
    // En texto visible: un `title` no existe con el dedo.
    expect(screen.getByText(/desde la tercera reseña/)).toBeVisible()
    expect(estrellas).not.toHaveAttribute('title')
  })

  it('solo el recuento se anuncia; la lista de resultados no es una region viva', async () => {
    await buscar()

    const recuento = screen.getByRole('heading', { level: 2, name: /Marcela|persona/i })
    expect(recuento).toHaveAttribute('aria-live', 'polite')

    const lista = screen.getByRole('list', { name: 'Resultados de la búsqueda' })
    // Ningun antepasado de la lista anuncia cambios: si lo hiciera, cada filtro la releeria entera.
    expect(lista.closest('[aria-live]')).toBeNull()
  })

  it('entre el h1 de la pagina y el h3 de cada tarjeta hay un h2', async () => {
    await buscar()

    const niveles = screen.getAllByRole('heading').map((h) => Number(h.tagName.slice(1)))
    const primerH3 = niveles.indexOf(3)
    expect(primerH3).toBeGreaterThan(-1)
    expect(niveles.slice(0, primerH3)).toContain(2)
  })
})
