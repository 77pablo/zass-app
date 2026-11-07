import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter, useLocation } from 'react-router-dom'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { App } from './App'
import { olvidarCsrf } from './api/cliente'
import { olvidarCaches } from './api/useCargado'
import { olvidarSugerencias } from './api/useSugerencias'
import { olvidarUbicacion, ubicacionGuardada } from './marco/ubicacion'
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
  credenciales: [
    { tipo: 'Titulo', nombre: 'Abogado', entidad: 'Universidad de La Frontera', obtenidaEn: '2010-03-01', etiqueta: 'Verificada', verificada: true },
    { tipo: 'Curso', nombre: 'Curso de tasación', entidad: null, obtenidaEn: '2021-05-10', etiqueta: 'Declarada por el usuario', verificada: false },
  ],
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

const TEXTO = {
  resultados: [{
    perfilId: 'p1', slug: 'marcela-soto', nombrePublico: 'Marcela Soto', descripcion: null, urlFoto: null,
    rubro: 'Corretaje de propiedades', zonaPrincipal: 'Centro', distancia: null, tieneCredencialVerificada: true,
    promedioCalificacion: 4.8, totalResenas: 12, precioDesde: 60000,
  }],
  total: 1, pagina: 1, hayMas: false, ordenAplicado: 'Recomendados', conUbicacion: false, aviso: null,
}

let llamadas: string[]

function json(cuerpo: unknown, estado = 200) {
  return new Response(JSON.stringify(cuerpo), { status: estado, headers: { 'content-type': 'application/json' } })
}

beforeEach(() => {
  llamadas = []
  olvidarCsrf()
  olvidarCaches()
  olvidarSugerencias()
  olvidarUbicacion()
  localStorage.clear()
  vi.stubGlobal('fetch', vi.fn(async (ruta: string) => {
    llamadas.push(ruta)
    if (ruta === '/api/rubros') return json(RUBROS)
    if (ruta === '/api/zonas') return json(ZONAS)
    if (ruta === '/api/comunidad') return json(COMUNIDAD)
    if (ruta === '/api/prestadores/marcela-soto') return json(FICHA)
    if (ruta.startsWith('/api/prestadores/')) return json({ title: 'No encontrado' }, 404)
    if (ruta.startsWith('/api/busqueda')) return json(BUSQUEDA)
    if (ruta.startsWith('/api/buscar')) {
      // La API devuelve el orden que aplico: aqui, el pedido.
      const orden = new URLSearchParams(ruta.split('?')[1] ?? '').get('orden') ?? 'Recomendados'
      return json({ ...TEXTO, ordenAplicado: orden })
    }
    return json({ title: 'No autorizado' }, 401)
  }))
})
afterEach(() => vi.unstubAllGlobals())

/** Muestra la direccion actual, para comprobar lo que NO tiene que estar en ella. */
function Sonda() {
  const l = useLocation()
  return <output data-testid="url">{l.pathname + l.search}</output>
}

function en(ruta: string) {
  return render(
    <MemoryRouter initialEntries={[ruta]}>
      <Sonda />
      <ProveedorDeSesion><App /></ProveedorDeSesion>
    </MemoryRouter>,
  )
}

describe('el sitio de varias paginas', () => {
  it('la portada muestra a quien se sumo, y su tarjeta lleva a la ficha', async () => {
    en('/')

    const enlaces = await screen.findAllByRole('link', { name: /Marcela Soto/ })
    expect(enlaces.some((a) => a.getAttribute('href') === '/prestadores/marcela-soto')).toBe(true)
    expect(screen.getByText(/3 personas conectadas/)).toBeInTheDocument()
  })

  it('el titular invita a buscar a alguien de la ciudad, no del barrio', async () => {
    en('/')

    const h1 = await screen.findByRole('heading', { level: 1 })
    expect(h1).toHaveTextContent('Busca a alguien de tu ciudad.')
    expect(h1).not.toHaveTextContent(/barrio/i)
  })

  it('un visitante que nunca entro no le pregunta a la API quien es', async () => {
    en('/')
    await screen.findAllByRole('link', { name: /Marcela Soto/ })

    expect(llamadas).not.toContain('/api/mi/cuenta')
    expect(screen.getAllByRole('link', { name: 'Entrar' }).length).toBeGreaterThan(0)
  })

  it('escribir en la barra de la portada y pulsar Enter lleva a los resultados de ese texto', async () => {
    const u = userEvent.setup()
    en('/')

    await u.type(await screen.findByRole('combobox'), 'abogado{Enter}')

    await vi.waitFor(() => expect(llamadas.some((l) => l === '/api/buscar?q=abogado&orden=Recomendados')).toBe(true))
  })

  it('un atajo de la portada busca ese oficio', async () => {
    en('/')

    const atajo = await screen.findByRole('link', { name: 'gasfiter' })
    expect(atajo).toHaveAttribute('href', '/buscar?q=gasfiter')
  })

  it('mientras se escribe aparecen personas y elegir una lleva a su ficha', async () => {
    const u = userEvent.setup()
    en('/')

    await u.type(await screen.findByRole('combobox'), 'marcela')
    const opcion = await screen.findByRole('option', { name: /Marcela Soto/ }, { timeout: 3000 })
    await u.click(opcion)

    expect(await screen.findByRole('heading', { level: 1, name: 'Marcela Soto' })).toBeInTheDocument()
  })

  it('con el teclado: flecha abajo elige la primera sugerencia y Enter la abre', async () => {
    const u = userEvent.setup()
    en('/')
    const barra = await screen.findByRole('combobox')

    await u.type(barra, 'marcela')
    await screen.findByRole('option', { name: /Marcela Soto/ }, { timeout: 3000 })
    await u.keyboard('{ArrowDown}{Enter}')

    expect(await screen.findByRole('heading', { level: 1, name: 'Marcela Soto' })).toBeInTheDocument()
  })

  it('Escape cierra las sugerencias', async () => {
    const u = userEvent.setup()
    en('/')
    const barra = await screen.findByRole('combobox')

    await u.type(barra, 'marcela')
    await screen.findByRole('option', { name: /Marcela Soto/ }, { timeout: 3000 })
    await u.keyboard('{Escape}')

    expect(screen.queryByRole('listbox')).not.toBeInTheDocument()
  })

  it('con menos de dos letras no se le pregunta nada a la API', async () => {
    const u = userEvent.setup()
    en('/')

    await u.type(await screen.findByRole('combobox'), 'a')
    await new Promise((r) => setTimeout(r, 500))

    expect(llamadas.some((l) => l.startsWith('/api/buscar'))).toBe(false)
  })

  it('la busqueda sale de la direccion y muestra tarjetas que enlazan a la ficha, con su precio', async () => {
    en('/buscar?q=corredor')

    const tarjeta = await screen.findByRole('link', { name: /Marcela Soto/ })
    expect(tarjeta).toHaveAttribute('href', '/prestadores/marcela-soto')
    expect(within(tarjeta).getByText('$60.000')).toBeInTheDocument()
    expect(within(tarjeta).getByText('Verificado')).toBeInTheDocument()
    expect(llamadas).toContain('/api/buscar?q=corredor&orden=Recomendados')
  })

  it('sin texto muestra a toda la comunidad', async () => {
    en('/buscar')

    expect(await screen.findByText(/Toda la comunidad/)).toBeInTheDocument()
    expect(llamadas).toContain('/api/buscar?orden=Recomendados')
  })

  it('el orden se elige con botones y viaja en la busqueda y en la direccion', async () => {
    const u = userEvent.setup()
    en('/buscar?q=corredor')
    await screen.findByRole('link', { name: /Marcela Soto/ })

    await u.click(screen.getByRole('button', { name: 'Menor precio' }))

    await vi.waitFor(() => expect(llamadas).toContain('/api/buscar?q=corredor&orden=PrecioAsc'))
    expect(screen.getByRole('button', { name: 'Menor precio' })).toHaveAttribute('aria-pressed', 'true')
  })

  it('«Más cerca» pide la ubicacion al pulsarlo, la manda a la API y no la pone en la direccion', async () => {
    const u = userEvent.setup()
    vi.stubGlobal('navigator', {
      ...navigator,
      geolocation: { getCurrentPosition: (ok: PositionCallback) => ok({ coords: { latitude: -38.7, longitude: -72.6 } } as GeolocationPosition) },
    })
    en('/buscar?q=corredor')
    await screen.findByRole('link', { name: /Marcela Soto/ })

    await u.click(screen.getByRole('button', { name: 'Más cerca' }))

    await vi.waitFor(() => expect(llamadas.some((l) => l.includes('orden=Cercania') && l.includes('lat=-38.7') && l.includes('lon=-72.6'))).toBe(true))
    expect(screen.getByRole('button', { name: /Quitar mi ubicación/ })).toBeInTheDocument()
    // La ubicacion de una persona no va en la direccion: se comparte, se guarda en el historial.
    const url = screen.getByTestId('url').textContent ?? ''
    expect(url).toContain('orden=Cercania')
    expect(url).not.toMatch(/-38|-72|lat|lon/i)
  })

  it('«Quitar mi ubicación» vuelve a buscar sin ubicacion y sin orden por cercania', async () => {
    const u = userEvent.setup()
    vi.stubGlobal('navigator', {
      ...navigator,
      geolocation: { getCurrentPosition: (ok: PositionCallback) => ok({ coords: { latitude: -38.7, longitude: -72.6 } } as GeolocationPosition) },
    })
    en('/buscar?q=corredor')
    await screen.findByRole('link', { name: /Marcela Soto/ })
    await u.click(screen.getByRole('button', { name: 'Más cerca' }))
    await screen.findByRole('button', { name: /Quitar mi ubicación/ })
    llamadas.length = 0

    await u.click(screen.getByRole('button', { name: /Quitar mi ubicación/ }))

    await vi.waitFor(() => expect(llamadas.some((l) => l.startsWith('/api/buscar'))).toBe(true))
    expect(llamadas.every((l) => !l.includes('lat=') && !l.includes('Cercania'))).toBe(true)
  })

  it('escribir lo mismo dos veces en la barra pregunta una sola vez a la API', async () => {
    const u = userEvent.setup()
    en('/')
    const barra = await screen.findByRole('combobox')

    await u.type(barra, 'marcela')
    await screen.findByRole('option', { name: /Marcela Soto/ }, { timeout: 3000 })
    await u.clear(barra)
    await u.type(barra, 'marcela')
    await screen.findByRole('option', { name: /Marcela Soto/ }, { timeout: 3000 })
    // Sin cache, la segunda pregunta sale despues de la espera de la barra: se deja pasar ese tiempo.
    await new Promise((r) => setTimeout(r, 500))

    expect(llamadas.filter((l) => l === '/api/buscar?q=marcela')).toHaveLength(1)
  })

  /** Escribir es rapido y la red no: la respuesta de «ab» puede llegar DESPUES que la de «abo». */
  it('la respuesta lenta de lo que ya no es lo escrito no pisa a la de lo actual', async () => {
    const u = userEvent.setup()
    const original = fetch
    const persona = (nombre: string) => ({
      ...TEXTO, resultados: [{ ...TEXTO.resultados[0], perfilId: nombre, slug: nombre, nombrePublico: nombre }],
    })
    vi.stubGlobal('fetch', vi.fn(async (ruta: string) => {
      if (ruta === '/api/buscar?q=ab') {
        await new Promise((r) => setTimeout(r, 900))      // la lenta
        return json(persona('Persona Vieja'))
      }
      if (ruta === '/api/buscar?q=abo') return json(persona('Persona Nueva'))
      return original(ruta)
    }))
    en('/')
    const barra = await screen.findByRole('combobox')

    await u.type(barra, 'ab')
    await new Promise((r) => setTimeout(r, 400))          // ya salio la pregunta de «ab»
    await u.type(barra, 'o')
    await screen.findByRole('option', { name: /Persona Nueva/ }, { timeout: 3000 })
    await new Promise((r) => setTimeout(r, 1100))         // llega la respuesta vieja

    expect(screen.queryByRole('option', { name: /Persona Vieja/ })).not.toBeInTheDocument()
    expect(screen.getByRole('option', { name: /Persona Nueva/ })).toBeInTheDocument()
  })

  it('una direccion con «mas cerca» pero sin ubicacion en memoria (recarga) no la pide a ciegas: ordena por recomendados', async () => {
    en('/buscar?q=corredor&orden=Cercania&cerca=1')

    await screen.findByRole('link', { name: /Marcela Soto/ })
    expect(llamadas).toContain('/api/buscar?q=corredor&orden=Recomendados')
    expect(llamadas.some((l) => l.includes('orden=Cercania'))).toBe(false)
    expect(screen.queryByRole('button', { name: /Quitar mi ubicación/ })).not.toBeInTheDocument()
  })

  it('«Quitar mi ubicación» la borra de la memoria, no solo de la pantalla', async () => {
    const u = userEvent.setup()
    vi.stubGlobal('navigator', {
      ...navigator,
      geolocation: { getCurrentPosition: (ok: PositionCallback) => ok({ coords: { latitude: -38.7, longitude: -72.6 } } as GeolocationPosition) },
    })
    en('/buscar?q=corredor')
    await screen.findByRole('link', { name: /Marcela Soto/ })
    await u.click(screen.getByRole('button', { name: 'Más cerca' }))
    await screen.findByRole('button', { name: /Quitar mi ubicación/ })
    expect(ubicacionGuardada()).toEqual({ lat: -38.7, lon: -72.6 })

    await u.click(screen.getByRole('button', { name: /Quitar mi ubicación/ }))

    expect(ubicacionGuardada()).toBeNull()
  })

  it('volver a «Más recomendados» deja la direccion limpia, sin orden', async () => {
    const u = userEvent.setup()
    en('/buscar?q=corredor')
    await screen.findByRole('link', { name: /Marcela Soto/ })

    await u.click(screen.getByRole('button', { name: 'Menor precio' }))
    await vi.waitFor(() => expect(screen.getByTestId('url').textContent).toContain('orden=PrecioAsc'))
    await u.click(screen.getByRole('button', { name: 'Más recomendados' }))

    await vi.waitFor(() => expect(screen.getByTestId('url').textContent).toBe('/buscar?q=corredor'))
  })

  it('con el campo vacio ofrece atajos y elegir uno busca ese oficio', async () => {
    const u = userEvent.setup()
    en('/')

    await u.click(await screen.findByRole('combobox'))
    await u.click(await screen.findByRole('option', { name: /contadora/ }))

    await vi.waitFor(() => expect(llamadas).toContain('/api/buscar?q=contadora&orden=Recomendados'))
  })

  it('sin coincidencias la barra ofrece buscar igual, y la pagina dice que no hay nadie', async () => {
    const u = userEvent.setup()
    const original = fetch
    vi.stubGlobal('fetch', vi.fn(async (ruta: string) =>
      ruta.startsWith('/api/buscar') ? json({ ...TEXTO, resultados: [], total: 0 }) : original(ruta)))
    en('/')

    await u.type(await screen.findByRole('combobox'), 'zzzzz')
    expect(await screen.findByRole('option', { name: 'Buscar «zzzzz»' }, { timeout: 3000 })).toBeInTheDocument()
    await u.keyboard('{Enter}')

    expect(await screen.findByText(/Todavía no encontramos a nadie/)).toBeInTheDocument()
  })

  it('si se niega la ubicacion, no se ordena por cercania y se avisa', async () => {
    const u = userEvent.setup()
    vi.stubGlobal('navigator', {
      ...navigator,
      geolocation: { getCurrentPosition: (_ok: PositionCallback, mal: PositionErrorCallback) => mal({} as GeolocationPositionError) },
    })
    en('/buscar?q=corredor')
    await screen.findByRole('link', { name: /Marcela Soto/ })

    await u.click(screen.getByRole('button', { name: 'Más cerca' }))

    expect(await screen.findByText(/No pudimos usar tu ubicación/)).toBeInTheDocument()
    expect(llamadas.some((l) => l.includes('orden=Cercania'))).toBe(false)
  })

  it('un barrio filtra la busqueda', async () => {
    const u = userEvent.setup()
    en('/buscar?q=corredor')
    await screen.findByRole('link', { name: /Marcela Soto/ })

    await u.selectOptions(screen.getByRole('combobox', { name: 'Barrio' }), '5')

    await vi.waitFor(() => expect(llamadas).toContain('/api/buscar?q=corredor&orden=Recomendados&zona=5'))
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

  it('la ficha muestra las credenciales con la etiqueta publica, sin estado interno', async () => {
    en('/prestadores/marcela-soto')

    await screen.findByRole('heading', { level: 1, name: 'Marcela Soto' })
    expect(screen.getByRole('heading', { name: 'Credenciales' })).toBeInTheDocument()
    expect(screen.getByText('Título · Universidad de La Frontera · 2010')).toBeInTheDocument()
    expect(screen.getByText('Verificada', { selector: '.sello' })).toBeInTheDocument()
    expect(screen.getByText('Declarada por el usuario', { selector: '.dato' })).toBeInTheDocument()
    expect(screen.getByText('Curso · 2021')).toBeInTheDocument()
  })

  it('sin credenciales la ficha no muestra la seccion', async () => {
    const original = fetch
    vi.stubGlobal('fetch', vi.fn(async (ruta: string) =>
      ruta === '/api/prestadores/marcela-soto' ? json({ ...FICHA, credenciales: [] }) : original(ruta)))
    en('/prestadores/marcela-soto')

    await screen.findByRole('heading', { level: 1, name: 'Marcela Soto' })
    expect(screen.queryByRole('heading', { name: 'Credenciales' })).not.toBeInTheDocument()
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
    expect(fila).toHaveAttribute('href', '/buscar?q=Derecho')
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
