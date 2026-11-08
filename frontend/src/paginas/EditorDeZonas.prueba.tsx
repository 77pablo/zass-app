import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router-dom'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import type { Zona } from '../api/catalogo'
import { olvidarCsrf } from '../api/cliente'
import type { ZonaCubierta } from '../funciones/perfil/servicio'
import { MapaDeChile } from '../marco/MapaDeChile'
import { EditorDeZonas, MAXIMO_DE_ZONAS } from './EditorDeZonas'

const comuna = (id: number, nombre: string, region = 'La Araucanía', lat = -38.7): Zona => ({
  id, nombre, sector: region, comuna: nombre, centroLatitud: lat, centroLongitud: -72.6, tipo: 'Comuna', region, padreId: null,
})
const TEMUCO = comuna(200, 'Temuco')
const CENTRO: Zona = { id: 1, nombre: 'Centro', sector: 'centro', comuna: 'Temuco', centroLatitud: -38.7, centroLongitud: -72.6, tipo: 'Barrio', region: 'La Araucanía', padreId: 200 }
const ARICA = comuna(100, 'Arica', 'Arica y Parinacota', -18.5)
const MUCHAS = Array.from({ length: 12 }, (_, i) => comuna(300 + i, `Comuna ${String.fromCharCode(65 + i)}`, 'Ñuble', -36.6))

let llamadas: { ruta: string; cuerpo: unknown }[]

beforeEach(() => {
  llamadas = []
  olvidarCsrf()
  vi.stubGlobal('fetch', vi.fn(async (ruta: string, opciones?: RequestInit) => {
    if (ruta === '/api/auth/csrf') return new Response(JSON.stringify({ token: 't' }), { status: 200, headers: { 'content-type': 'application/json' } })
    llamadas.push({ ruta, cuerpo: opciones?.body ? JSON.parse(String(opciones.body)) : undefined })
    return new Response(null, { status: 204 })
  }))
})

function editor(zonas: Zona[], actuales: ZonaCubierta[] = [], alGuardar = () => {}) {
  return render(<EditorDeZonas zonas={zonas} actuales={actuales} alGuardar={alGuardar} alCancelar={() => {}} />)
}

describe('el editor de comunas', () => {
  it('la primera comuna que se elige queda de principal, y se puede cambiar', async () => {
    const u = userEvent.setup()
    editor([TEMUCO, ARICA])
    await u.type(screen.getByRole('searchbox'), 'c')       // abre las regiones: «c» esta en Temuco y en Arica

    await u.click(screen.getByRole('checkbox', { name: 'Temuco' }))
    expect(screen.getByRole('radio', { name: /Principal: Temuco/ })).toBeChecked()

    await u.click(screen.getByRole('checkbox', { name: 'Arica' }))
    expect(screen.getByRole('radio', { name: /Principal: Temuco/ })).toBeChecked()
    await u.click(screen.getByRole('radio', { name: /Principal: Arica/ }))
    expect(screen.getByRole('radio', { name: /Principal: Arica/ })).toBeChecked()
  })

  it('guarda las elegidas con exactamente una principal', async () => {
    const u = userEvent.setup()
    const alGuardar = vi.fn()
    editor([TEMUCO, ARICA], [], alGuardar)
    await u.type(screen.getByRole('searchbox'), 'c')
    await u.click(screen.getByRole('checkbox', { name: 'Temuco' }))
    await u.click(screen.getByRole('checkbox', { name: 'Arica' }))
    await u.click(screen.getByRole('radio', { name: /Principal: Arica/ }))

    await u.click(screen.getByRole('button', { name: 'Guardar comunas' }))

    await vi.waitFor(() => expect(alGuardar).toHaveBeenCalledTimes(1))
    expect(llamadas).toEqual([{
      ruta: '/api/mi/perfil-prestador/cobertura',
      cuerpo: { zonas: [{ zonaId: 200, esPrincipal: false }, { zonaId: 100, esPrincipal: true }] },
    }])
  })

  it('quitar la principal deja sin principal y obliga a elegir otra antes de guardar', async () => {
    const u = userEvent.setup()
    editor([TEMUCO, ARICA], [
      { zonaId: 200, nombre: 'Temuco', sector: 'x', esPrincipal: true, desdeEn: '' },
      { zonaId: 100, nombre: 'Arica', sector: 'x', esPrincipal: false, desdeEn: '' },
    ])

    await u.click(screen.getByRole('button', { name: 'Quitar Temuco' }))
    await u.click(screen.getByRole('button', { name: 'Guardar comunas' }))

    expect(screen.getByText('Marca cuál es tu comuna principal.')).toBeInTheDocument()
    expect(llamadas).toHaveLength(0)
  })

  it('sin ninguna comuna no guarda', async () => {
    const u = userEvent.setup()
    editor([TEMUCO])

    await u.click(screen.getByRole('button', { name: 'Guardar comunas' }))

    expect(screen.getByText('Elige al menos una comuna.')).toBeInTheDocument()
    expect(llamadas).toHaveLength(0)
  })

  it(`no deja pasar de ${MAXIMO_DE_ZONAS} y lo dice`, async () => {
    const u = userEvent.setup()
    editor(MUCHAS)
    await u.type(screen.getByRole('searchbox'), 'comuna')

    for (const z of MUCHAS.slice(0, MAXIMO_DE_ZONAS)) await u.click(screen.getByRole('checkbox', { name: z.nombre }))

    expect(screen.getByText(`${MAXIMO_DE_ZONAS}`, { selector: 'strong' })).toBeInTheDocument()
    expect(screen.getByRole('checkbox', { name: MUCHAS[10].nombre })).toBeDisabled()
    expect(screen.getByRole('checkbox', { name: MUCHAS[11].nombre })).toBeDisabled()
    // las ya elegidas siguen pudiendo quitarse
    expect(screen.getByRole('checkbox', { name: MUCHAS[0].nombre })).toBeEnabled()
  })

  it('los barrios cuelgan de su comuna y se nombran con ella', async () => {
    const u = userEvent.setup()
    editor([TEMUCO, CENTRO])
    await u.type(screen.getByRole('searchbox'), 'temuco')

    const lista = screen.getByRole('list', { name: 'Barrios de Temuco' })
    await u.click(within(lista).getByRole('checkbox', { name: 'Centro, Temuco' }))

    expect(screen.getByRole('button', { name: 'Quitar Centro, Temuco' })).toBeInTheDocument()
  })

  it('las regiones de lo ya elegido empiezan abiertas, y las demas cerradas', () => {
    editor([TEMUCO, ARICA], [{ zonaId: 200, nombre: 'Temuco', sector: 'x', esPrincipal: true, desdeEn: '' }])

    expect(screen.getByText('La Araucanía', { selector: '.region-nombre' }).closest('details')!.open).toBe(true)
    expect(screen.getByText('Arica y Parinacota', { selector: '.region-nombre' }).closest('details')!.open).toBe(false)
  })
})

describe('el mapa de Chile', () => {
  const ZONAS = [comuna(200, 'Temuco', 'La Araucanía', -38.74), comuna(100, 'Arica', 'Arica y Parinacota', -18.48), comuna(201, 'Villarrica', 'La Araucanía', -39.28)]
  const conteo = (id: number) => (id === 200 ? 27 : 0)

  function mapa() {
    return render(<MemoryRouter><MapaDeChile zonas={ZONAS} conteo={conteo} /></MemoryRouter>)
  }

  it('solo las comunas con gente son enlaces, y llevan a la busqueda de esa comuna', () => {
    mapa()

    const enlaces = screen.getAllByRole('link')
    expect(enlaces).toHaveLength(1)
    expect(enlaces[0]).toHaveAttribute('href', '/buscar?zona=200')
    expect(enlaces[0]).toHaveAccessibleName('Temuco, La Araucanía: 27 personas. Ver quién hay.')
  })

  it('la leyenda dice que es Chile y cambia al tocar una comuna', async () => {
    const u = userEvent.setup()
    mapa()
    expect(screen.getByText('Chile, en vivo')).toBeInTheDocument()

    await u.hover(screen.getByRole('link'))

    expect(screen.getByText('La Araucanía · 27 personas')).toBeInTheDocument()
  })

  it('sin gente en ningun lado no hay enlaces y la leyenda lo dice con honestidad', () => {
    render(<MemoryRouter><MapaDeChile zonas={ZONAS} conteo={() => 0} /></MemoryRouter>)

    expect(screen.queryAllByRole('link')).toHaveLength(0)
    expect(screen.getByText('Pronto, tu comuna')).toBeInTheDocument()
  })
})
