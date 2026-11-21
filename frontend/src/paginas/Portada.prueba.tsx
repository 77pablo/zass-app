import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router-dom'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { App } from '../App'
import { olvidarCsrf } from '../api/cliente'
import { olvidarCaches } from '../api/useCargado'
import { ProveedorDeSesion } from '../sesion/Sesion'

/*
 * La portada mientras no hay datos.
 *
 * Casi todas sus secciones viven tras un `&&` (el mapa, las cifras, los recien
 * llegados). Antes, sin datos, desaparecian sin una palabra: la primera visita del dia, con la API
 * dormida en Render, veia media portada muda y no sabia si esperar o irse.
 */

const COMUNIDAD = {
  total: 1,
  porZona: [], porRegion: [], porRubro: [],
  recienLlegados: [
    { slug: 'marcela-soto', nombrePublico: 'Marcela Soto', rubro: 'Corretaje de propiedades', zona: 'Centro', verificada: false },
  ],
}

function json(cuerpo: unknown, estado = 200) {
  return new Response(JSON.stringify(cuerpo), { status: estado, headers: { 'content-type': 'application/json' } })
}

/** La API contesta bien: lo que la portada pide al cargar. */
const responder = async (ruta: string) => {
  if (ruta === '/api/rubros') return json([{ id: 1, codigo: 'corretaje', nombre: 'Corretaje de propiedades', icono: null }])
  if (ruta === '/api/zonas') return json([])
  if (ruta === '/api/comunidad') return json(COMUNIDAD)
  return json({ title: 'No autorizado' }, 401)
}

function portada() {
  return render(
    <MemoryRouter initialEntries={['/']}>
      <ProveedorDeSesion><App /></ProveedorDeSesion>
    </MemoryRouter>,
  )
}

beforeEach(() => {
  olvidarCsrf()
  olvidarCaches()
  localStorage.clear()
})
afterEach(() => vi.unstubAllGlobals())

describe('la portada mientras no hay datos', () => {
  it('con la API dormida dice que esta cargando, en vez de quedarse muda', async () => {
    // Nunca contesta: es la API despertando.
    vi.stubGlobal('fetch', vi.fn(() => new Promise<Response>(() => {})))

    portada()

    // Por el texto y no por el rol: la App tiene otros `status` (los avisos de sesion).
    const aviso = await screen.findByText(/Cargando el mapa y la comunidad/)
    expect(aviso.closest('[role="status"]')).not.toBeNull()
    expect(screen.queryByRole('alert')).not.toBeInTheDocument()
  })

  it('si la API no contesta, lo dice UNA vez aunque fallen las dos cargas, y deja reintentar', async () => {
    const u = userEvent.setup()
    let caida = true
    const fetchFalso = vi.fn(async (ruta: string) => {
      if (caida) throw new TypeError('Failed to fetch')
      return responder(ruta)
    })
    vi.stubGlobal('fetch', fetchFalso)

    portada()

    const alerta = await screen.findByRole('alert')
    // Las dos cargas (catalogo y comunidad) fallan con el mismo mensaje: se ve una vez, no dos.
    const texto = alerta.textContent ?? ''
    expect(texto).toMatch(/No se pudo conectar/)
    expect(texto.match(/No se pudo conectar/g)).toHaveLength(1)

    // La API desperto: reintentar vuelve a preguntar y la portada se llena.
    caida = false
    const antes = fetchFalso.mock.calls.length
    await u.click(screen.getByRole('button', { name: 'Reintentar' }))

    expect((await screen.findAllByRole('link', { name: /Marcela Soto/ })).length).toBeGreaterThan(0)
    expect(fetchFalso.mock.calls.length).toBeGreaterThan(antes)
    await waitFor(() => expect(screen.queryByRole('alert')).not.toBeInTheDocument())
  })

  it('con los datos cargados no queda ningun aviso de carga ni de error', async () => {
    vi.stubGlobal('fetch', vi.fn(responder))

    portada()

    await screen.findAllByRole('link', { name: /Marcela Soto/ })
    expect(screen.queryByText(/Cargando el mapa y la comunidad/)).not.toBeInTheDocument()
    expect(screen.queryByRole('alert')).not.toBeInTheDocument()
  })
})
