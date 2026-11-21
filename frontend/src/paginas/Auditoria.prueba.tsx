import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router-dom'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { Auditoria } from './Auditoria'

function json(cuerpo: unknown, estado = 200) {
  return new Response(JSON.stringify(cuerpo), { status: estado, headers: { 'content-type': 'application/json' } })
}

const TARJETA = '11111111-2222-3333-4444-555555555555'
const ACTOR = 'aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee'

const fila = (id: number, accion: string, ocurridoEn: string) => ({
  id, accion, entidad: 'Tarjeta', entidadId: TARJETA, actorId: ACTOR,
  contexto: '{"medio":"nfc"}', ipOrigen: '190.20.1.4', ocurridoEn,
})

let pedidas: string[]
let paginas: Record<string, unknown>

beforeEach(() => {
  pedidas = []
  paginas = {
    primera: { items: [fila(3, 'TarjetaActivada', '2026-11-19T15:00:00Z'), fila(2, 'TarjetaEmitida', '2026-11-18T15:00:00Z')], siguiente: 'CUR1' },
    segunda: { items: [fila(1, 'TarjetaCreada', '2026-11-17T15:00:00Z')], siguiente: null },
  }
  vi.stubGlobal('fetch', vi.fn(async (ruta: string) => {
    pedidas.push(ruta)
    const q = new URL(ruta, 'https://zass.local').searchParams
    return json(q.get('cursor') === 'CUR1' ? paginas.segunda : paginas.primera)
  }))
})

const pintar = () => render(<MemoryRouter><Auditoria /></MemoryRouter>)

async function buscarPorEntidad() {
  pintar()
  await userEvent.type(screen.getByLabelText('Entidad'), 'Tarjeta')
  await userEvent.type(screen.getByLabelText('Identificador del registro'), TARJETA)
  await userEvent.click(screen.getByRole('button', { name: 'Buscar' }))
  return screen.findByRole('table')
}

describe('JQ-13: auditoría (API-61)', () => {
  it('por entidad: manda entidad + entidadId y muestra solo las columnas que da la API', async () => {
    const tabla = await buscarPorEntidad()
    expect(pedidas).toEqual([`/api/interno/auditoria?entidad=Tarjeta&entidadId=${TARJETA}`])

    const encabezados = within(tabla).getAllByRole('columnheader').map((c) => c.textContent)
    expect(encabezados).toEqual(['Cuándo', 'Acción', 'Entidad', 'Quién', 'IP', 'Contexto'])
    const filas = within(tabla).getAllByRole('row').slice(1)
    expect(filas).toHaveLength(2)
    expect(filas[0]).toHaveTextContent('TarjetaActivada')
    expect(filas[0]).toHaveTextContent('190.20.1.4')
    expect(filas[0]).toHaveTextContent('{"medio":"nfc"}')
  })

  it('por actor: manda solo actorId', async () => {
    pintar()
    await userEvent.click(screen.getByRole('radio', { name: 'Persona que actuó' }))
    await userEvent.type(screen.getByLabelText('Identificador de la persona'), ACTOR)
    await userEvent.click(screen.getByRole('button', { name: 'Buscar' }))
    await screen.findByRole('table')
    expect(pedidas).toEqual([`/api/interno/auditoria?actorId=${ACTOR}`])
  })

  it('pagina hacia atrás con el cursor de la API, sumando las filas, hasta que no hay más', async () => {
    await buscarPorEntidad()
    await userEvent.click(screen.getByRole('button', { name: 'Ver más antiguos' }))
    expect(await screen.findByText('TarjetaCreada')).toBeInTheDocument()

    expect(pedidas[1]).toBe(`/api/interno/auditoria?entidad=Tarjeta&entidadId=${TARJETA}&cursor=CUR1`)
    expect(within(screen.getByRole('table')).getAllByRole('row').slice(1)).toHaveLength(3)
    expect(screen.queryByRole('button', { name: 'Ver más antiguos' })).toBeNull()
    expect(screen.getByText('No hay registros más antiguos.')).toBeInTheDocument()
  })

  it('una búsqueda nueva empieza de cero, sin el cursor de la anterior', async () => {
    await buscarPorEntidad()
    await userEvent.click(screen.getByRole('button', { name: 'Buscar' }))
    await screen.findByRole('table')
    expect(pedidas[1]).not.toContain('cursor')
    expect(within(screen.getByRole('table')).getAllByRole('row').slice(1)).toHaveLength(2)
  })

  it('sin resultados, lo dice', async () => {
    paginas.primera = { items: [], siguiente: null }
    pintar()
    await userEvent.type(screen.getByLabelText('Entidad'), 'Tarjeta')
    await userEvent.type(screen.getByLabelText('Identificador del registro'), TARJETA)
    await userEvent.click(screen.getByRole('button', { name: 'Buscar' }))
    expect(await screen.findByText('No hay registros de auditoría para esa búsqueda.')).toBeInTheDocument()
  })

  it.each([
    ['sin entidad', '', TARJETA, 'Escribe la entidad.'],
    ['con un identificador mal escrito', 'Tarjeta', '1234', 'Pega el identificador completo'],
  ])('%s no pregunta a la API', async (_, entidad, id, mensaje) => {
    pintar()
    if (entidad) await userEvent.type(screen.getByLabelText('Entidad'), entidad)
    await userEvent.type(screen.getByLabelText('Identificador del registro'), id)
    await userEvent.click(screen.getByRole('button', { name: 'Buscar' }))
    expect(screen.getByRole('alert')).toHaveTextContent(mensaje)
    expect(pedidas).toHaveLength(0)
  })

  it('el 400 de la API (que viene como {titulo, detalle}) muestra su detalle', async () => {
    vi.mocked(fetch).mockImplementation(async () =>
      json({ titulo: 'Cursor invalido', detalle: 'El cursor no es valido. Vuelve a buscar.' }, 400))
    pintar()
    await userEvent.type(screen.getByLabelText('Entidad'), 'Tarjeta')
    await userEvent.type(screen.getByLabelText('Identificador del registro'), TARJETA)
    await userEvent.click(screen.getByRole('button', { name: 'Buscar' }))
    expect(await screen.findByRole('alert')).toHaveTextContent('El cursor no es valido. Vuelve a buscar.')
  })
})
