import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router-dom'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { App } from './App'
import { olvidarCsrf } from './api/cliente'
import { olvidarCaches } from './api/useCargado'
import { ProveedorDeSesion } from './sesion/Sesion'

const RUBROS = [
  { id: 1, codigo: 'corretaje', nombre: 'Corretaje de propiedades', icono: null },
  { id: 2, codigo: 'derecho', nombre: 'Derecho', icono: null },
]
const ZONAS = [
  { id: 1, nombre: 'Centro', sector: 'centro', comuna: 'Temuco', centroLatitud: -38.7, centroLongitud: -72.6 },
  { id: 5, nombre: 'Pedro de Valdivia', sector: 'norte', comuna: 'Temuco', centroLatitud: -38.7, centroLongitud: -72.6 },
]
const COMUNIDAD = {
  total: 3,
  porZona: [{ zonaId: 1, personas: 2 }],
  porRubro: [{ rubroId: 1, personas: 3 }],
  recienLlegados: [
    { slug: 'marcela-soto', nombrePublico: 'Marcela Soto', rubro: 'Corretaje de propiedades', zona: 'Centro', verificada: true },
  ],
}
const FICHA = {
  id: 'x', slug: 'marcela-soto', nombrePublico: 'Marcela Soto', descripcion: 'Quince años en el centro.',
  urlFoto: null, rubro: { id: 1, codigo: 'corretaje', nombre: 'Corretaje de propiedades', icono: null },
  zonas: [{ nombre: 'Centro', sector: 'centro', esPrincipal: true }],
  servicios: [{ nombre: 'Tasación comercial', duracionMinutos: 90, precioReferencia: 60000 }],
  promedioCalificacion: null, totalResenas: 2,
  contacto: { telefono: null, whatsapp: '+56 9 5550 0101', correo: null, sitioWeb: 'javascript:alert(1)' },
  publicadoEn: null,
}
const BUSQUEDA = {
  resultados: [{
    perfilId: 'p1', slug: 'marcela-soto', nombrePublico: 'Marcela Soto', descripcion: null, urlFoto: null,
    rubroId: 1, zonaPrincipal: 'Centro', distancia: null, tieneCredencialVerificada: false,
    promedioCalificacion: null, totalResenas: 0, esTuPerfil: false,
  }],
  cursorSiguiente: null, ordenadoPorDistancia: false, zonaBuscada: 'Centro', aviso: null,
}

let llamadas: string[]

function json(cuerpo: unknown, estado = 200) {
  return new Response(JSON.stringify(cuerpo), { status: estado, headers: { 'content-type': 'application/json' } })
}

beforeEach(() => {
  llamadas = []
  olvidarCsrf()
  olvidarCaches()
  localStorage.clear()
  vi.stubGlobal('fetch', vi.fn(async (ruta: string) => {
    llamadas.push(ruta)
    if (ruta === '/api/rubros') return json(RUBROS)
    if (ruta === '/api/zonas') return json(ZONAS)
    if (ruta === '/api/comunidad') return json(COMUNIDAD)
    if (ruta === '/api/prestadores/marcela-soto') return json(FICHA)
    if (ruta.startsWith('/api/prestadores/')) return json({ title: 'No encontrado' }, 404)
    if (ruta.startsWith('/api/busqueda')) return json(BUSQUEDA)
    return json({ title: 'No autorizado' }, 401)
  }))
})
afterEach(() => vi.unstubAllGlobals())

function en(ruta: string) {
  return render(
    <MemoryRouter initialEntries={[ruta]}>
      <ProveedorDeSesion><App /></ProveedorDeSesion>
    </MemoryRouter>,
  )
}

describe('el sitio de varias paginas', () => {
  it('la portada muestra a quien se sumo, y su tarjeta lleva a la ficha', async () => {
    en('/')

    const enlaces = await screen.findAllByRole('link', { name: /Marcela Soto/ })
    expect(enlaces.some((a) => a.getAttribute('href') === '/prestadores/marcela-soto')).toBe(true)
    expect(screen.getByText(/Ya somos 3 personas/)).toBeInTheDocument()
  })

  it('un visitante que nunca entro no le pregunta a la API quien es', async () => {
    en('/')
    await screen.findAllByRole('link', { name: /Marcela Soto/ })

    expect(llamadas).not.toContain('/api/mi/cuenta')
    expect(screen.getAllByRole('link', { name: 'Entrar' }).length).toBeGreaterThan(0)
  })

  it('la frase de la portada lleva a la busqueda con lo elegido en la direccion', async () => {
    const u = userEvent.setup()
    en('/')
    await screen.findByRole('option', { name: 'Derecho' })

    await u.selectOptions(screen.getByLabelText('Oficio'), 'derecho')
    await u.selectOptions(screen.getByLabelText('Barrio'), '1')
    await u.click(screen.getByRole('button', { name: /Ver quién hay/ }))

    await waitFor(() => expect(llamadas.some((l) => l.startsWith('/api/busqueda'))).toBe(true))
    const q = new URLSearchParams(llamadas.find((l) => l.startsWith('/api/busqueda'))!.split('?')[1])
    expect(q.get('rubro')).toBe('derecho')
    expect(q.get('zona')).toBe('1')
  })

  it('la busqueda sale de la direccion y muestra tarjetas que enlazan a la ficha', async () => {
    en('/buscar?rubro=corretaje&zona=1')

    const tarjeta = await screen.findByRole('link', { name: /Marcela Soto/ })
    expect(tarjeta).toHaveAttribute('href', '/prestadores/marcela-soto')
    expect(screen.getByRole('heading', { level: 1 })).toHaveTextContent('Corretaje de propiedades en Centro')
  })

  it('sin oficio ofrece los oficios como botones y no busca nada', async () => {
    en('/buscar')

    expect(await screen.findByRole('button', { name: 'Derecho' })).toBeInTheDocument()
    expect(llamadas.some((l) => l.startsWith('/api/busqueda'))).toBe(false)
  })

  it('con oficio pero sin barrio pide el barrio, y tampoco busca', async () => {
    en('/buscar?rubro=derecho')

    expect(await screen.findByText(/Ahora elige un barrio/)).toBeInTheDocument()
    expect(llamadas.some((l) => l.startsWith('/api/busqueda'))).toBe(false)
  })

  it('la ficha muestra servicios con precio y contacto, y no vuelve enlace un javascript:', async () => {
    en('/prestadores/marcela-soto')

    expect(await screen.findByRole('heading', { level: 1, name: 'Marcela Soto' })).toBeInTheDocument()
    expect(screen.getByText(/desde \$60\.000/)).toBeInTheDocument()
    expect(screen.getByRole('link', { name: /WhatsApp/ })).toHaveAttribute('href', 'https://wa.me/56955500101')
    expect(screen.queryByRole('link', { name: /sitio web/ })).not.toBeInTheDocument()
    // 2 reseñas: el promedio no es publico (RN-S5-11), se dice el recuento y no se inventa uno.
    expect(screen.getByText(/El promedio se muestra desde la tercera/)).toBeInTheDocument()
  })

  it('una ficha que no existe lo dice y ofrece seguir buscando', async () => {
    en('/prestadores/nadie')

    expect(await screen.findByText(/no existe, o ya no está publicada/)).toBeInTheDocument()
    expect(screen.getAllByRole('link', { name: /Buscar a alguien/ }).length).toBeGreaterThan(0)
  })

  it('los barrios muestran cuanta gente hay, y "Aun nadie" donde no hay', async () => {
    en('/barrios')

    const centro = await screen.findByRole('link', { name: /Centro/ })
    expect(within(centro).getByText('2 personas')).toBeInTheDocument()
    expect(within(screen.getByRole('link', { name: /Pedro de Valdivia/ })).getByText('Aún nadie')).toBeInTheDocument()
  })

  it('los oficios enlazan a la busqueda de ese oficio', async () => {
    en('/oficios')

    const fila = await screen.findByRole('link', { name: /Derecho/ })
    expect(fila).toHaveAttribute('href', '/buscar?rubro=derecho')
  })

  it('una direccion que no existe muestra la pagina de no encontrada', async () => {
    en('/esto-no-existe')

    expect(await screen.findByRole('heading', { level: 1, name: 'Esta página no existe.' })).toBeInTheDocument()
  })

  it('Mi ficha, sin sesion, manda a entrar', async () => {
    en('/mi-ficha')

    expect(await screen.findByRole('heading', { level: 1, name: 'Entrar' })).toBeInTheDocument()
  })

  it('con la marca de sesion pregunta quien es, y si responde 401 la borra', async () => {
    localStorage.setItem('zass-sesion', '1')
    en('/')

    await screen.findAllByRole('link', { name: 'Entrar' })
    expect(llamadas).toContain('/api/mi/cuenta')
    expect(localStorage.getItem('zass-sesion')).toBeNull()
  })

  it('el titulo de la pestana cambia con la pagina', async () => {
    en('/profesionales')

    await screen.findByRole('heading', { level: 1 })
    expect(document.title).toBe('Para profesionales · ZASS!')
  })
})
