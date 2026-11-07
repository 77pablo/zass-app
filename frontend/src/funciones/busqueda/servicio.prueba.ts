import { beforeEach, describe, expect, it, vi } from 'vitest'
import { buscar, listarRubros, listarZonas } from './servicio'

const obtener = vi.fn()
vi.mock('../../api/cliente', () => ({
  obtener: (...a: unknown[]) => obtener(...a),
}))

beforeEach(() => {
  obtener.mockReset()
  obtener.mockResolvedValue({})
})

/** La URL con la que se llamo, ya separada para poder afirmar sobre cada parametro. */
function ultimaUrl(): URL {
  const ruta = obtener.mock.calls.at(-1)![0] as string
  return new URL(ruta, 'https://zass.test')
}

describe('servicio de busqueda', () => {
  it('pide los rubros a API-02 y las zonas a API-04', async () => {
    await listarRubros()
    expect(obtener).toHaveBeenLastCalledWith('/api/rubros')

    await listarZonas()
    expect(obtener).toHaveBeenLastCalledWith('/api/zonas')
  })

  it('busca por zona sin mandar coordenadas', async () => {
    await buscar({ rubro: 'corretaje', zona: 1 })

    const url = ultimaUrl()
    expect(url.pathname).toBe('/api/busqueda')
    expect(url.searchParams.get('rubro')).toBe('corretaje')
    expect(url.searchParams.get('zona')).toBe('1')
    expect(url.searchParams.has('lat')).toBe(false)
    expect(url.searchParams.has('lon')).toBe(false)
  })

  it('busca por punto sin mandar zona', async () => {
    await buscar({ rubro: 'corretaje', lat: -38.7359, lon: -72.5904 })

    const url = ultimaUrl()
    expect(url.searchParams.get('lat')).toBe('-38.7359')
    expect(url.searchParams.get('lon')).toBe('-72.5904')
    expect(url.searchParams.has('zona')).toBe(false)
  })

  // El radio solo significa algo con punto: con zona no hay desde donde medirlo. Mandarlo
  // igual dejaria una URL que dice "radio=20" y una lista que no lo respeta.
  it('manda el radio cuando se busca por punto', async () => {
    await buscar({ rubro: 'corretaje', lat: -38.7359, lon: -72.5904, radio: 20 })

    expect(ultimaUrl().searchParams.get('radio')).toBe('20')
  })

  it('no manda el radio cuando se busca por zona', async () => {
    await buscar({ rubro: 'corretaje', zona: 1, radio: 20 })

    expect(ultimaUrl().searchParams.has('radio')).toBe(false)
  })

  it('con punto y sin radio, no inventa uno: la API tiene el suyo por omision', async () => {
    await buscar({ rubro: 'corretaje', lat: -38.7359, lon: -72.5904 })

    expect(ultimaUrl().searchParams.has('radio')).toBe(false)
  })

  // Si llegan las dos cosas, el punto es mas preciso y manda. Mandar ambas dejaria a la
  // API decidir, y la pantalla ya no sabria si la lista viene ordenada por distancia.
  it('si llegan punto y zona, manda el punto y no envia la zona', async () => {
    await buscar({ rubro: 'corretaje', zona: 1, lat: -38.7359, lon: -72.5904 })

    const url = ultimaUrl()
    expect(url.searchParams.get('lat')).toBe('-38.7359')
    expect(url.searchParams.has('zona')).toBe(false)
  })

  // Un parametro `verificados=false` no cambia nada para la API, pero ensucia la URL
  // de una busqueda que se comparte. Solo viaja lo que de verdad se pidio.
  it('no manda parametros que no se pidieron', async () => {
    await buscar({ rubro: 'derecho', zona: 3 })

    const claves = [...ultimaUrl().searchParams.keys()].sort()
    expect(claves).toEqual(['rubro', 'zona'])
  })

  it('manda los filtros de CS-09 solo cuando estan puestos', async () => {
    await buscar({ rubro: 'derecho', zona: 3, verificados: true, calificacion: 4 })

    const url = ultimaUrl()
    expect(url.searchParams.get('verificados')).toBe('true')
    expect(url.searchParams.get('calificacion')).toBe('4')
  })

  it('manda el cursor para pedir la pagina siguiente', async () => {
    await buscar({ rubro: 'derecho', zona: 3, cursor: 'ZHxhYmM=' })

    expect(ultimaUrl().searchParams.get('cursor')).toBe('ZHxhYmM=')
  })

  // El cursor es Base64: lleva `=` y puede llevar `+` y `/`. Sin codificar, un `+`
  // llega al servidor como un espacio y el cursor "no es valido" sin que se vea por que.
  it('codifica el cursor para que sobreviva a la URL', async () => {
    const cursor = 'a+b/c=='
    await buscar({ rubro: 'derecho', zona: 3, cursor })

    expect(obtener.mock.calls.at(-1)![0]).toContain('cursor=a%2Bb%2Fc%3D%3D')
    expect(ultimaUrl().searchParams.get('cursor')).toBe(cursor)
  })

  it('devuelve lo que contesta la API sin tocarlo', async () => {
    const pagina = { resultados: [], cursorSiguiente: null, ordenadoPorDistancia: true }
    obtener.mockResolvedValueOnce(pagina)

    expect(await buscar({ rubro: 'derecho', zona: 3 })).toBe(pagina)
  })
})
