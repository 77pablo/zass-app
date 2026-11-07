import { beforeEach, afterEach, describe, expect, it, vi } from 'vitest'
import { render, screen, waitFor, within } from '@testing-library/react'
import usuario from '@testing-library/user-event'
import { Buscar } from './Buscar'
import { ErrorApi } from '../../api/cliente'
import type { PaginaDeBusqueda, Resultado, Rubro, Zona } from './servicio'

const listarRubros = vi.fn()
const listarZonas = vi.fn()
const buscar = vi.fn()
vi.mock('./servicio', () => ({
  listarRubros: () => listarRubros(),
  listarZonas: () => listarZonas(),
  buscar: (...a: unknown[]) => buscar(...a),
}))

const rubros: Rubro[] = [
  { id: 1, codigo: 'corretaje', nombre: 'Corretaje de propiedades', icono: null },
  { id: 2, codigo: 'derecho', nombre: 'Derecho', icono: null },
]

const zonas: Zona[] = [
  { id: 1, nombre: 'Centro', sector: 'centro', comuna: 'Temuco', centroLatitud: -38.74, centroLongitud: -72.59 },
  { id: 11, nombre: 'Amanecer', sector: 'poniente', comuna: 'Temuco', centroLatitud: -38.75, centroLongitud: -72.62 },
  { id: 14, nombre: 'Labranza', sector: 'poniente', comuna: 'Temuco', centroLatitud: -38.76, centroLongitud: -72.75 },
]

function resultado(sobre: Partial<Resultado> = {}): Resultado {
  return {
    perfilId: crypto.randomUUID(),
    slug: 'estudio-espinoza',
    nombrePublico: 'Estudio Espinoza',
    descripcion: 'Corretaje en Temuco',
    urlFoto: null,
    rubroId: 1,
    zonaPrincipal: 'Centro',
    distancia: 'a 3 km',
    tieneCredencialVerificada: false,
    promedioCalificacion: null,
    totalResenas: 0,
    esTuPerfil: false,
    ...sobre,
  }
}

function pagina(sobre: Partial<PaginaDeBusqueda> = {}): PaginaDeBusqueda {
  return {
    resultados: [resultado()],
    cursorSiguiente: null,
    ordenadoPorDistancia: true,
    zonaBuscada: 'Centro',
    aviso: null,
    ...sobre,
  }
}

function diferida<T>() {
  let resolver!: (v: T) => void
  const promesa = new Promise<T>((r) => { resolver = r })
  return { promesa, resolver }
}

const botonBuscar = () => screen.getByRole('button', { name: /^busca/i })

async function montar() {
  render(<Buscar />)
  // Las opciones llegan de la API: hasta que estan, no hay nada que elegir.
  await screen.findByRole('option', { name: 'Corretaje de propiedades' })
}

async function elegir(rubro = 'corretaje', zona = '1') {
  await usuario.selectOptions(screen.getByLabelText(/qué necesitas/i), rubro)
  await usuario.selectOptions(screen.getByLabelText(/en qué zona/i), zona)
}

beforeEach(() => {
  listarRubros.mockReset().mockResolvedValue(rubros)
  listarZonas.mockReset().mockResolvedValue(zonas)
  buscar.mockReset().mockResolvedValue(pagina())
})

afterEach(() => {
  vi.unstubAllGlobals()
})

describe('al abrir', () => {
  it('carga los rubros y las zonas', async () => {
    await montar()

    expect(screen.getByRole('option', { name: 'Derecho' })).toBeInTheDocument()
    expect(screen.getByRole('option', { name: 'Labranza' })).toBeInTheDocument()
  })

  // Con 16 zonas en una lista plana, en un celular hay que recorrerlas todas.
  it('agrupa las zonas por sector', async () => {
    await montar()

    const grupos = [...document.querySelectorAll('optgroup')].map((g) => g.label)
    expect(grupos).toEqual(expect.arrayContaining(['Centro', 'Poniente']))
    const poniente = document.querySelector('optgroup[label="Poniente"]')!
    expect(within(poniente as HTMLElement).getByRole('option', { name: 'Amanecer' })).toBeInTheDocument()
  })

  it('si no se pudieron cargar, lo dice y deja reintentar', async () => {
    listarRubros.mockReset().mockRejectedValueOnce(new ErrorApi(0, 'No se pudo conectar.'))
    listarRubros.mockResolvedValue(rubros)
    render(<Buscar />)

    expect(await screen.findByRole('alert')).toHaveTextContent(/no se pudo conectar/i)

    await usuario.click(screen.getByRole('button', { name: /reintentar/i }))

    expect(await screen.findByRole('option', { name: 'Derecho' })).toBeInTheDocument()
    expect(screen.queryByRole('alert')).not.toBeInTheDocument()
  })

  it('atribuye las zonas a OpenStreetMap, como pide su licencia', async () => {
    await montar()

    const enlace = screen.getByRole('link', { name: /openstreetmap/i })
    expect(enlace).toHaveAttribute('href', expect.stringContaining('openstreetmap.org'))
    expect(enlace).toHaveAttribute('rel', expect.stringContaining('noopener'))
  })
})

describe('antes de buscar', () => {
  it('no busca sin rubro', async () => {
    await montar()
    await usuario.selectOptions(screen.getByLabelText(/en qué zona/i), '1')

    await usuario.click(botonBuscar())

    expect(buscar).not.toHaveBeenCalled()
    expect(await screen.findByText(/elige qué necesitas/i)).toBeInTheDocument()
  })

  it('no busca sin decir dónde', async () => {
    await montar()
    await usuario.selectOptions(screen.getByLabelText(/qué necesitas/i), 'corretaje')

    await usuario.click(botonBuscar())

    expect(buscar).not.toHaveBeenCalled()
    expect(await screen.findByText(/elige una zona o usa tu ubicación/i)).toBeInTheDocument()
  })
})

describe('buscar por zona', () => {
  it('manda el rubro por código y la zona elegida', async () => {
    await montar()
    await elegir('corretaje', '14')

    await usuario.click(botonBuscar())

    await waitFor(() => expect(buscar).toHaveBeenCalledTimes(1))
    expect(buscar).toHaveBeenCalledWith(
      expect.objectContaining({ rubro: 'corretaje', zona: 14 }))
    expect(buscar.mock.calls[0][0]).not.toHaveProperty('lat')
  })

  it('manda los filtros de verificados y de 4 estrellas cuando están puestos', async () => {
    await montar()
    await elegir()

    await usuario.click(screen.getByLabelText(/solo verificados/i))
    await usuario.click(screen.getByLabelText(/4 estrellas o más/i))
    await usuario.click(botonBuscar())

    await waitFor(() => expect(buscar).toHaveBeenCalled())
    expect(buscar).toHaveBeenCalledWith(
      expect.objectContaining({ verificados: true, calificacion: 4 }))
  })

  // RN-S5-11: el filtro solo mira a quien ya tiene 3 resenas. Si no se dice, parece
  // que "4 estrellas o mas" esconde perfiles buenos sin motivo.
  it('aclara que el filtro de estrellas solo cuenta a quien ya tiene 3 reseñas', async () => {
    await montar()

    expect(screen.getByText(/3 reseñas/i)).toBeInTheDocument()
  })

  it('no manda filtros que no se pidieron', async () => {
    await montar()
    await elegir()

    await usuario.click(botonBuscar())

    await waitFor(() => expect(buscar).toHaveBeenCalled())
    const enviado = buscar.mock.calls[0][0]
    expect(enviado.verificados).toBeFalsy()
    expect(enviado.calificacion).toBeUndefined()
  })
})

describe('los resultados', () => {
  it('muestran nombre, zona y la distancia tal como llega', async () => {
    buscar.mockResolvedValue(pagina({
      resultados: [resultado({ nombrePublico: 'Corretaje Araucanía', zonaPrincipal: 'Amanecer', distancia: 'a 12 km' })],
    }))
    await montar()
    await elegir()

    await usuario.click(botonBuscar())

    expect(await screen.findByRole('heading', { name: 'Corretaje Araucanía' })).toBeInTheDocument()
    expect(screen.getByText(/amanecer/i, { selector: '.resultado-meta' })).toBeInTheDocument()
    expect(screen.getByText(/a 12 km/i)).toBeInTheDocument()
  })

  it('dan el sello solo a quien tiene credencial verificada', async () => {
    buscar.mockResolvedValue(pagina({
      resultados: [
        resultado({ nombrePublico: 'Con sello', tieneCredencialVerificada: true }),
        resultado({ nombrePublico: 'Sin sello', tieneCredencialVerificada: false }),
      ],
    }))
    await montar()
    await elegir()

    await usuario.click(botonBuscar())

    const conSello = (await screen.findByRole('heading', { name: 'Con sello' })).closest('li')!
    const sinSello = screen.getByRole('heading', { name: 'Sin sello' }).closest('li')!
    expect(within(conSello).getByText(/verificado/i)).toBeInTheDocument()
    expect(within(sinSello).queryByText(/verificado/i)).not.toBeInTheDocument()
  })

  // RN-S5-11. El promedio con menos de 3 resenas no es publico: se muestra el
  // recuento. Y no se puede inventar un promedio en la pantalla a partir de nada.
  it('con menos de 3 reseñas muestran el recuento y no un promedio', async () => {
    buscar.mockResolvedValue(pagina({
      resultados: [resultado({ promedioCalificacion: null, totalResenas: 2 })],
    }))
    await montar()
    await elegir()

    await usuario.click(botonBuscar())

    expect(await screen.findByText(/2 reseñas/i)).toBeInTheDocument()
    expect(screen.queryByText(/de 5/i)).not.toBeInTheDocument()
  })

  it('con promedio lo muestran con coma decimal', async () => {
    buscar.mockResolvedValue(pagina({
      resultados: [resultado({ promedioCalificacion: 4.6, totalResenas: 12 })],
    }))
    await montar()
    await elegir()

    await usuario.click(botonBuscar())

    expect(await screen.findByText(/4,6 de 5/i)).toBeInTheDocument()
    expect(screen.getByText(/12 reseñas/i)).toBeInTheDocument()
  })

  it('dicen "sin reseñas todavía" y respetan el singular', async () => {
    buscar.mockResolvedValue(pagina({
      resultados: [
        resultado({ nombrePublico: 'Nuevo', totalResenas: 0 }),
        resultado({ nombrePublico: 'Uno', totalResenas: 1 }),
      ],
    }))
    await montar()
    await elegir()

    await usuario.click(botonBuscar())

    expect(await screen.findByText(/sin reseñas todavía/i)).toBeInTheDocument()
    expect(screen.getByText(/^1 reseña$/i)).toBeInTheDocument()
  })

  it('marcan el propio perfil como "este eres tú"', async () => {
    buscar.mockResolvedValue(pagina({
      resultados: [
        resultado({ nombrePublico: 'Yo', esTuPerfil: true }),
        resultado({ nombrePublico: 'Otro', esTuPerfil: false }),
      ],
    }))
    await montar()
    await elegir()

    await usuario.click(botonBuscar())

    const yo = (await screen.findByRole('heading', { name: 'Yo' })).closest('li')!
    const otro = screen.getByRole('heading', { name: 'Otro' }).closest('li')!
    expect(within(yo).getByText(/este eres tú/i)).toBeInTheDocument()
    expect(within(otro).queryByText(/este eres tú/i)).not.toBeInTheDocument()
  })

  it('cuentan cuántos hay', async () => {
    buscar.mockResolvedValue(pagina({
      resultados: [resultado({ nombrePublico: 'A' }), resultado({ nombrePublico: 'B' })],
    }))
    await montar()
    await elegir()

    await usuario.click(botonBuscar())

    expect(await screen.findByText(/2 resultados/i)).toBeInTheDocument()
  })

  // El aviso de la API ("mostramos los de Centro", "el radio maximo es 50 km") no es
  // un error: es informacion. Con role="alert" un lector de pantalla lo gritaria.
  it('muestran el aviso de la API como información, no como error', async () => {
    buscar.mockResolvedValue(pagina({ aviso: 'No ordenamos por distancia. Mostramos los de Centro.' }))
    await montar()
    await elegir()

    await usuario.click(botonBuscar())

    const aviso = await screen.findByText(/mostramos los de centro/i)
    expect(aviso).toHaveAttribute('role', 'status')
    expect(screen.queryByRole('alert')).not.toBeInTheDocument()
  })

  it('sin resultados lo dicen y sugieren otra zona', async () => {
    buscar.mockResolvedValue(pagina({ resultados: [] }))
    await montar()
    await elegir()

    await usuario.click(botonBuscar())

    expect(await screen.findByText(/no encontramos a nadie/i)).toBeInTheDocument()
    expect(screen.getByText(/prueba con otra zona/i)).toBeInTheDocument()
  })

  it('sin resultados y con filtros puestos sugieren quitarlos', async () => {
    buscar.mockResolvedValue(pagina({ resultados: [] }))
    await montar()
    await elegir()
    await usuario.click(screen.getByLabelText(/solo verificados/i))

    await usuario.click(botonBuscar())

    expect(await screen.findByText(/prueba quitando los filtros/i)).toBeInTheDocument()
  })

  // Una busqueda nueva que falla no puede dejar los resultados de la anterior debajo de
  // un error: parecerian la respuesta a lo que se acaba de pedir.
  it('una búsqueda nueva que falla no deja los resultados de la anterior', async () => {
    buscar
      .mockResolvedValueOnce(pagina({ resultados: [resultado({ nombrePublico: 'Anterior' })] }))
      .mockRejectedValueOnce(new ErrorApi(500, 'El servidor tuvo un problema.'))
    await montar()
    await elegir('corretaje', '1')
    await usuario.click(botonBuscar())
    await screen.findByRole('heading', { name: 'Anterior' })

    await usuario.selectOptions(screen.getByLabelText(/en qué zona/i), '14')
    await usuario.click(botonBuscar())

    expect(await screen.findByRole('alert')).toHaveTextContent(/el servidor tuvo un problema/i)
    expect(screen.queryByRole('heading', { name: 'Anterior' })).not.toBeInTheDocument()
  })

  // Distinto de la anterior: aqui los resultados SI son de lo que se pidio, y perderlos
  // porque falle la pagina siguiente seria castigar a quien ya los estaba mirando.
  it('si falla "ver más", conserva lo que ya se mostraba y deja reintentar', async () => {
    buscar
      .mockResolvedValueOnce(pagina({
        resultados: [resultado({ nombrePublico: 'Ya visto' })], cursorSiguiente: 'CURSOR-1',
      }))
      .mockRejectedValueOnce(new ErrorApi(0, 'No se pudo conectar.'))
    await montar()
    await elegir()
    await usuario.click(botonBuscar())
    await screen.findByRole('heading', { name: 'Ya visto' })

    await usuario.click(screen.getByRole('button', { name: /ver más/i }))

    expect(await screen.findByRole('alert')).toHaveTextContent(/no se pudo conectar/i)
    expect(screen.getByRole('heading', { name: 'Ya visto' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: /ver más/i })).toBeEnabled()
  })

  it('un error de la API se muestra como alerta', async () => {
    buscar.mockRejectedValue(new ErrorApi(400, 'Ese rubro no existe.'))
    await montar()
    await elegir()

    await usuario.click(botonBuscar())

    expect(await screen.findByRole('alert')).toHaveTextContent(/ese rubro no existe/i)
  })
})

describe('paginación', () => {
  it('"ver más" pide la página siguiente con el cursor y la agrega', async () => {
    buscar
      .mockResolvedValueOnce(pagina({
        resultados: [resultado({ nombrePublico: 'Primero' })], cursorSiguiente: 'CURSOR-1',
      }))
      .mockResolvedValueOnce(pagina({
        resultados: [resultado({ nombrePublico: 'Segundo' })], cursorSiguiente: null,
      }))
    await montar()
    await elegir()
    await usuario.click(botonBuscar())
    await screen.findByRole('heading', { name: 'Primero' })

    await usuario.click(screen.getByRole('button', { name: /ver más/i }))

    expect(await screen.findByRole('heading', { name: 'Segundo' })).toBeInTheDocument()
    expect(screen.getByRole('heading', { name: 'Primero' })).toBeInTheDocument()
    expect(buscar).toHaveBeenLastCalledWith(expect.objectContaining({ cursor: 'CURSOR-1' }))
    // Ya no hay mas: el boton no puede quedarse ofreciendo algo que no existe.
    expect(screen.queryByRole('button', { name: /ver más/i })).not.toBeInTheDocument()
  })

  it('no ofrece "ver más" cuando no hay página siguiente', async () => {
    await montar()
    await elegir()

    await usuario.click(botonBuscar())
    await screen.findByRole('heading', { name: 'Estudio Espinoza' })

    expect(screen.queryByRole('button', { name: /ver más/i })).not.toBeInTheDocument()
  })

  // El cursor solo vale para la busqueda que lo genero: su orden y su posicion son de
  // ESA consulta. Si "ver mas" tomara el rubro que el formulario muestre ahora, el
  // servidor recibiria un cursor de una busqueda con los criterios de otra.
  it('"ver más" usa los criterios de la búsqueda, no los que el formulario muestre ahora', async () => {
    buscar.mockResolvedValue(pagina({
      resultados: [resultado({ nombrePublico: 'Primero' })], cursorSiguiente: 'CURSOR-1',
    }))
    await montar()
    await elegir('corretaje', '1')
    await usuario.click(botonBuscar())
    await screen.findByRole('heading', { name: 'Primero' })

    // La persona toca el formulario pero NO vuelve a buscar.
    await usuario.selectOptions(screen.getByLabelText(/qué necesitas/i), 'derecho')
    await usuario.selectOptions(screen.getByLabelText(/en qué zona/i), '14')
    await usuario.click(screen.getByRole('button', { name: /ver más/i }))

    await waitFor(() => expect(buscar).toHaveBeenCalledTimes(2))
    expect(buscar).toHaveBeenLastCalledWith(
      expect.objectContaining({ rubro: 'corretaje', zona: 1, cursor: 'CURSOR-1' }))
  })

  it('una búsqueda nueva reemplaza los resultados y no reusa el cursor', async () => {
    buscar
      .mockResolvedValueOnce(pagina({
        resultados: [resultado({ nombrePublico: 'Vieja' })], cursorSiguiente: 'CURSOR-1',
      }))
      .mockResolvedValueOnce(pagina({
        resultados: [resultado({ nombrePublico: 'Nueva' })], cursorSiguiente: null,
      }))
    await montar()
    await elegir('corretaje', '1')
    await usuario.click(botonBuscar())
    await screen.findByRole('heading', { name: 'Vieja' })

    await usuario.selectOptions(screen.getByLabelText(/en qué zona/i), '14')
    await usuario.click(botonBuscar())

    expect(await screen.findByRole('heading', { name: 'Nueva' })).toBeInTheDocument()
    expect(screen.queryByRole('heading', { name: 'Vieja' })).not.toBeInTheDocument()
    expect(buscar.mock.calls[1][0].cursor).toBeUndefined()
  })

  // Dos busquedas seguidas: la lenta termina DESPUES de la rapida. Sin proteccion, la
  // vieja pisaria a la nueva y la pantalla mostraria resultados de lo que ya no se pidio.
  it('ignora la respuesta de una búsqueda vieja que llega tarde', async () => {
    const lenta = diferida<PaginaDeBusqueda>()
    buscar
      .mockReturnValueOnce(lenta.promesa)
      .mockResolvedValueOnce(pagina({ resultados: [resultado({ nombrePublico: 'Rápida' })] }))
    await montar()
    await elegir('corretaje', '1')
    await usuario.click(botonBuscar())          // la lenta queda pendiente

    await usuario.selectOptions(screen.getByLabelText(/en qué zona/i), '14')
    await usuario.click(botonBuscar())
    await screen.findByRole('heading', { name: 'Rápida' })

    lenta.resolver(pagina({ resultados: [resultado({ nombrePublico: 'Lenta' })] }))
    await new Promise((r) => setTimeout(r, 20))

    expect(screen.getByRole('heading', { name: 'Rápida' })).toBeInTheDocument()
    expect(screen.queryByRole('heading', { name: 'Lenta' })).not.toBeInTheDocument()
  })
})

describe('buscar con la ubicación', () => {
  function geolocalizacion(
    comportamiento: 'acepta' | 'niega',
  ) {
    const getCurrentPosition = vi.fn((ok: PositionCallback, mal: PositionErrorCallback) => {
      if (comportamiento === 'acepta') {
        ok({ coords: { latitude: -38.7359, longitude: -72.5904 } } as GeolocationPosition)
      } else {
        mal({ code: 1, message: 'denegado' } as GeolocationPositionError)
      }
    })
    vi.stubGlobal('navigator', { ...navigator, geolocation: { getCurrentPosition } })
    return getCurrentPosition
  }

  it('busca por punto y no manda la zona', async () => {
    geolocalizacion('acepta')
    await montar()
    await usuario.selectOptions(screen.getByLabelText(/qué necesitas/i), 'corretaje')

    await usuario.click(screen.getByRole('button', { name: /usar mi ubicación/i }))
    await usuario.click(botonBuscar())

    await waitFor(() => expect(buscar).toHaveBeenCalled())
    const enviado = buscar.mock.calls[0][0]
    expect(enviado.lat).toBeCloseTo(-38.7359)
    expect(enviado.lon).toBeCloseTo(-72.5904)
    expect(enviado.zona).toBeUndefined()
  })

  it('dice que está usando la ubicación y deja quitarla', async () => {
    geolocalizacion('acepta')
    await montar()

    await usuario.click(screen.getByRole('button', { name: /usar mi ubicación/i }))

    expect(await screen.findByText(/usando tu ubicación/i)).toBeInTheDocument()
    await usuario.click(screen.getByRole('button', { name: /quitar/i }))
    expect(screen.queryByText(/usando tu ubicación/i)).not.toBeInTheDocument()
  })

  // Flujo 4A de CU-08: si la persona niega el permiso, el sistema no insiste ni vuelve
  // a pedirlo; ofrece elegir una zona de la lista.
  it('si la persona la niega, lo dice, no insiste y deja elegir una zona', async () => {
    const pedir = geolocalizacion('niega')
    await montar()

    await usuario.click(screen.getByRole('button', { name: /usar mi ubicación/i }))

    expect(await screen.findByText(/no pudimos usar tu ubicación/i)).toBeInTheDocument()
    expect(screen.getByText(/elige tu zona de la lista/i)).toBeInTheDocument()
    expect(pedir).toHaveBeenCalledTimes(1)

    // Y se puede seguir buscando por zona.
    await elegir('corretaje', '1')
    await usuario.click(botonBuscar())
    await waitFor(() => expect(buscar).toHaveBeenCalledWith(
      expect.objectContaining({ zona: 1 })))
    expect(pedir).toHaveBeenCalledTimes(1)
  })

  it('si el navegador no tiene geolocalización, lo dice en vez de no hacer nada', async () => {
    vi.stubGlobal('navigator', { ...navigator, geolocation: undefined })
    await montar()

    await usuario.click(screen.getByRole('button', { name: /usar mi ubicación/i }))

    expect(await screen.findByText(/no permite usar la ubicación/i)).toBeInTheDocument()
  })

  // ---- el radio (hallazgo 15 de ESTADO.md)

  const radio = () => screen.queryByLabelText(/hasta qué distancia/i)

  // Con zona no hay desde donde medir: ofrecer un radio ahi seria un control que no hace
  // nada, y la persona lo moveria esperando que cambie algo.
  it('el control de radio solo aparece cuando se usa la ubicación', async () => {
    geolocalizacion('acepta')
    await montar()
    expect(radio()).not.toBeInTheDocument()

    await usuario.click(screen.getByRole('button', { name: /usar mi ubicación/i }))

    expect(await screen.findByLabelText(/hasta qué distancia/i)).toBeInTheDocument()
  })

  it('empieza en 10 km, que es lo que la API usa por omisión', async () => {
    geolocalizacion('acepta')
    await montar()
    await usuario.click(screen.getByRole('button', { name: /usar mi ubicación/i }))

    expect(await screen.findByLabelText(/hasta qué distancia/i)).toHaveValue('10')
  })

  it('busca con el radio elegido', async () => {
    geolocalizacion('acepta')
    await montar()
    await usuario.selectOptions(screen.getByLabelText(/qué necesitas/i), 'corretaje')
    await usuario.click(screen.getByRole('button', { name: /usar mi ubicación/i }))
    await usuario.selectOptions(await screen.findByLabelText(/hasta qué distancia/i), '20')

    await usuario.click(botonBuscar())

    await waitFor(() => expect(buscar).toHaveBeenCalled())
    expect(buscar).toHaveBeenCalledWith(expect.objectContaining({ radio: 20 }))
  })

  it('en una búsqueda por zona no manda radio', async () => {
    await montar()
    await elegir()

    await usuario.click(botonBuscar())

    await waitFor(() => expect(buscar).toHaveBeenCalled())
    expect(buscar.mock.calls[0][0].radio).toBeUndefined()
  })

  it('quitar la ubicación esconde el control de radio', async () => {
    geolocalizacion('acepta')
    await montar()
    await usuario.click(screen.getByRole('button', { name: /usar mi ubicación/i }))
    await screen.findByLabelText(/hasta qué distancia/i)

    await usuario.click(screen.getByRole('button', { name: /quitar/i }))

    expect(radio()).not.toBeInTheDocument()
  })

  // Flujo 4B de CU-08: sin resultados, lo primero que se propone es ampliar el radio. Y el
  // boton lo hace, para que la persona no tenga que buscar el control, moverlo y volver a
  // pulsar Buscar.
  it('sin resultados y con radio menor a 50 km, ofrece ampliarlo y lo hace', async () => {
    geolocalizacion('acepta')
    buscar
      .mockResolvedValueOnce(pagina({ resultados: [] }))
      .mockResolvedValueOnce(pagina({ resultados: [resultado({ nombrePublico: 'Más lejos' })] }))
    await montar()
    await usuario.selectOptions(screen.getByLabelText(/qué necesitas/i), 'corretaje')
    await usuario.click(screen.getByRole('button', { name: /usar mi ubicación/i }))
    await usuario.click(botonBuscar())
    expect(await screen.findByText(/prueba ampliando el radio/i)).toBeInTheDocument()

    await usuario.click(screen.getByRole('button', { name: /ampliar a 50 km/i }))

    expect(await screen.findByRole('heading', { name: 'Más lejos' })).toBeInTheDocument()
    expect(buscar).toHaveBeenLastCalledWith(expect.objectContaining({ radio: 50 }))
    // Y el control del formulario refleja lo que se busco, no lo viejo.
    expect(screen.getByLabelText(/hasta qué distancia/i)).toHaveValue('50')
  })

  // Ampliar repite la busqueda QUE SE HIZO, no lo que el formulario diga ahora: misma
  // razon que en "ver mas".
  it('ampliar conserva el rubro y los filtros de la búsqueda que se hizo', async () => {
    geolocalizacion('acepta')
    buscar.mockResolvedValue(pagina({ resultados: [] }))
    await montar()
    await usuario.selectOptions(screen.getByLabelText(/qué necesitas/i), 'corretaje')
    await usuario.click(screen.getByRole('button', { name: /usar mi ubicación/i }))
    await usuario.click(screen.getByLabelText(/solo verificados/i))
    await usuario.click(botonBuscar())
    await screen.findByText(/prueba ampliando el radio/i)

    // La persona toca el rubro pero no vuelve a buscar.
    await usuario.selectOptions(screen.getByLabelText(/qué necesitas/i), 'derecho')
    await usuario.click(screen.getByRole('button', { name: /ampliar a 50 km/i }))

    await waitFor(() => expect(buscar).toHaveBeenCalledTimes(2))
    expect(buscar).toHaveBeenLastCalledWith(
      expect.objectContaining({ rubro: 'corretaje', verificados: true, radio: 50 }))
  })

  it('con el radio ya en 50 km no ofrece ampliar, y cae a quitar los filtros', async () => {
    geolocalizacion('acepta')
    buscar.mockResolvedValue(pagina({ resultados: [] }))
    await montar()
    await usuario.selectOptions(screen.getByLabelText(/qué necesitas/i), 'corretaje')
    await usuario.click(screen.getByRole('button', { name: /usar mi ubicación/i }))
    await usuario.selectOptions(await screen.findByLabelText(/hasta qué distancia/i), '50')
    await usuario.click(screen.getByLabelText(/solo verificados/i))

    await usuario.click(botonBuscar())

    expect(await screen.findByText(/prueba quitando los filtros/i)).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /ampliar a 50 km/i })).not.toBeInTheDocument()
  })

  it('por zona, sin resultados, no ofrece ampliar un radio que no existe', async () => {
    buscar.mockResolvedValue(pagina({ resultados: [] }))
    await montar()
    await elegir()

    await usuario.click(botonBuscar())

    expect(await screen.findByText(/prueba con otra zona/i)).toBeInTheDocument()
    expect(screen.queryByText(/ampliando el radio/i)).not.toBeInTheDocument()
  })

  it('elegir una zona después de usar la ubicación descarta el punto', async () => {
    geolocalizacion('acepta')
    await montar()
    await usuario.selectOptions(screen.getByLabelText(/qué necesitas/i), 'corretaje')
    await usuario.click(screen.getByRole('button', { name: /usar mi ubicación/i }))
    await screen.findByText(/usando tu ubicación/i)

    await usuario.selectOptions(screen.getByLabelText(/en qué zona/i), '14')
    await usuario.click(botonBuscar())

    await waitFor(() => expect(buscar).toHaveBeenCalled())
    const enviado = buscar.mock.calls[0][0]
    expect(enviado.zona).toBe(14)
    expect(enviado.lat).toBeUndefined()
    expect(screen.queryByText(/usando tu ubicación/i)).not.toBeInTheDocument()
  })
})
