import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router-dom'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { olvidarCsrf } from '../api/cliente'
import type { ReporteEnCola } from '../funciones/interno/servicio'
import { ReportesDeResenas } from './ReportesDeResenas'

function json(cuerpo: unknown, estado = 200) {
  return new Response(JSON.stringify(cuerpo), { status: estado, headers: { 'content-type': 'application/json' } })
}

const NUEVO: ReporteEnCola = {
  reporteId: 'r-nuevo', resenaId: 'z-1', motivo: 'LenguajeDeOdio', detalle: 'Insulta al gasfíter.',
  estado: 'Abierto', creadoEn: '2026-11-19T15:00:00Z', calificacion: 1,
  comentario: 'Un comentario ofensivo de prueba con más de veinte letras.', estadoResena: 'OcultaEnModeracion',
  perfilNombre: 'Jorge Huenchullán',
}
const ANTIGUO: ReporteEnCola = {
  reporteId: 'r-antiguo', resenaId: 'z-2', motivo: 'Falsedad', detalle: null,
  estado: 'Abierto', creadoEn: '2026-11-10T12:00:00Z', calificacion: 5,
  comentario: 'Excelente trabajo, llegó a la hora y dejó todo limpio.', estadoResena: 'Publicada',
  perfilNombre: 'Rosa Millán',
}

let llamadas: { ruta: string; metodo: string; cuerpo?: unknown }[]
let alResolver: () => Response

beforeEach(() => {
  llamadas = []
  alResolver = () => new Response(null, { status: 204 })
  olvidarCsrf()
  vi.stubGlobal('fetch', vi.fn(async (ruta: string, opciones?: RequestInit) => {
    if (ruta === '/api/auth/csrf') return json({ token: 't' })
    const metodo = opciones?.method ?? 'GET'
    const cuerpo = typeof opciones?.body === 'string' ? JSON.parse(opciones.body) : undefined
    llamadas.push({ ruta, metodo, cuerpo })
    if (ruta === '/api/interno/reportes/cola') return json([NUEVO, ANTIGUO])
    if (ruta.endsWith('/resolver') && metodo === 'POST') return alResolver()
    return json({}, 404)
  }))
})

const pintar = () => render(<MemoryRouter><ReportesDeResenas /></MemoryRouter>)
const resoluciones = () => llamadas.filter((l) => l.ruta.endsWith('/resolver'))
const reporte = (prestador: string) => screen.findByRole('article', { name: `Reseña sobre ${prestador}` })

describe('JQ-12: la cola de reportes (API-52)', () => {
  it('el más antiguo primero, con la reseña completa y el motivo del reporte', async () => {
    pintar()
    const lista = await screen.findByRole('list', { name: 'Reseñas reportadas' })
    const articulos = within(lista).getAllByRole('article')
    expect(articulos.map((a) => a.getAttribute('aria-label'))).toEqual(['Reseña sobre Rosa Millán', 'Reseña sobre Jorge Huenchullán'])

    const ofensivo = articulos[1]
    expect(ofensivo).toHaveTextContent(NUEVO.comentario)
    expect(ofensivo).toHaveTextContent('1 estrella')
    expect(ofensivo).toHaveTextContent('Oculta mientras se revisa')
    // La misma etiqueta que vio quien reportó (funciones/resenas).
    expect(ofensivo).toHaveTextContent('Lenguaje de odio')
    expect(ofensivo).toHaveTextContent('Insulta al gasfíter.')

    expect(articulos[0]).toHaveTextContent('5 estrellas')
    expect(articulos[0]).toHaveTextContent('Es falsa')
    expect(articulos[0]).not.toHaveTextContent('Lo que escribió quien reportó')
  })

  it('vacía, lo dice', async () => {
    vi.mocked(fetch).mockImplementation(async () => json([]))
    pintar()
    expect(await screen.findByText('No hay reportes abiertos.')).toBeInTheDocument()
  })
})

describe('JQ-12: resolver (API-53)', () => {
  it('retirar manda la decisión y el motivo, y el reporte sale de la cola sin recargar', async () => {
    pintar()
    const r = await reporte('Jorge Huenchullán')
    await userEvent.click(within(r).getByRole('radio', { name: 'Retirar la reseña' }))
    await userEvent.type(within(r).getByLabelText('Motivo de la decisión'), 'Insulta sin aportar nada del servicio.')
    await userEvent.click(within(r).getByRole('button', { name: 'Guardar decisión' }))

    expect(await screen.findByRole('status')).toHaveTextContent('la reseña quedó retirada')
    expect(resoluciones()).toEqual([{
      ruta: '/api/interno/reportes/r-nuevo/resolver', metodo: 'POST',
      cuerpo: { decision: 'retirar', motivo: 'Insulta sin aportar nada del servicio.' },
    }])
    expect(screen.queryByRole('article', { name: 'Reseña sobre Jorge Huenchullán' })).toBeNull()
    expect(screen.getByRole('article', { name: 'Reseña sobre Rosa Millán' })).toBeInTheDocument()
    // Sin recargar: la cola se pidió una sola vez.
    expect(llamadas.filter((l) => l.ruta === '/api/interno/reportes/cola')).toHaveLength(1)
  })

  it('mantener también lleva su motivo', async () => {
    pintar()
    const r = await reporte('Rosa Millán')
    await userEvent.click(within(r).getByRole('radio', { name: 'Mantener la reseña' }))
    await userEvent.type(within(r).getByLabelText('Motivo de la decisión'), 'Es una opinión, no hay pruebas de falsedad.')
    await userEvent.click(within(r).getByRole('button', { name: 'Guardar decisión' }))
    await waitFor(() => expect(resoluciones()).toHaveLength(1))
    expect(resoluciones()[0].cuerpo).toEqual({ decision: 'mantener', motivo: 'Es una opinión, no hay pruebas de falsedad.' })
  })

  it.each([
    ['vacío', ''],
    ['solo espacios', '            '],
    ['más corto que el mínimo de MOD-11', 'Corto'],
  ])('sin motivo (%s) no se manda', async (_, texto) => {
    pintar()
    const r = await reporte('Jorge Huenchullán')
    await userEvent.click(within(r).getByRole('radio', { name: 'Retirar la reseña' }))
    if (texto) await userEvent.type(within(r).getByLabelText('Motivo de la decisión'), texto)
    await userEvent.click(within(r).getByRole('button', { name: 'Guardar decisión' }))

    expect(within(r).getByRole('alert')).toHaveTextContent('Escribe el motivo de la decisión (al menos 10 caracteres).')
    expect(resoluciones()).toHaveLength(0)
  })

  it('si la API lo rechaza (ya resuelto, 409), muestra su mensaje y el reporte se queda', async () => {
    alResolver = () => json({ title: 'Este reporte ya fue resuelto.', status: 409 }, 409)
    pintar()
    const r = await reporte('Jorge Huenchullán')
    await userEvent.click(within(r).getByRole('radio', { name: 'Mantener la reseña' }))
    await userEvent.type(within(r).getByLabelText('Motivo de la decisión'), 'Es una opinión legítima.')
    await userEvent.click(within(r).getByRole('button', { name: 'Guardar decisión' }))
    expect(await within(r).findByRole('alert')).toHaveTextContent('Este reporte ya fue resuelto.')
    expect(screen.getByRole('article', { name: 'Reseña sobre Jorge Huenchullán' })).toBeInTheDocument()
  })
})
