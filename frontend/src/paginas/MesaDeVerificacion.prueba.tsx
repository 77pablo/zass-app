import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router-dom'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { olvidarCsrf } from '../api/cliente'
import type { CredencialEnCola } from '../funciones/interno/servicio'
import { MesaDeVerificacion } from './MesaDeVerificacion'

function json(cuerpo: unknown, estado = 200) {
  return new Response(JSON.stringify(cuerpo), { status: estado, headers: { 'content-type': 'application/json' } })
}

const enMinutos = (m: number) => new Date(Date.now() + m * 60_000).toISOString()

const NUEVA: CredencialEnCola = {
  id: 'c-nueva', prestador: 'Rosa Millán', prestadorSlug: 'rosa-millan', tipo: 'Curso',
  nombre: 'Primeros auxilios', entidad: 'Cruz Roja', entidadReconocida: true,
  obtenidaEn: '2025-03-01', venceEn: null, vencimientoPasado: false, documentos: 1, sinDocumentos: false,
  enColaDesde: '2026-11-19T15:00:00Z', reservadaPorMi: false, reservadaHasta: null,
}
const ANTIGUA: CredencialEnCola = {
  id: 'c-antigua', prestador: 'Luis Catrileo', prestadorSlug: 'luis-catrileo', tipo: 'Titulo',
  nombre: 'Técnico en electricidad', entidad: 'Instituto Sur', entidadReconocida: false,
  obtenidaEn: '2019-12-01', venceEn: null, vencimientoPasado: false, documentos: 0, sinDocumentos: true,
  enColaDesde: '2026-11-10T12:00:00Z', reservadaPorMi: false, reservadaHasta: null,
}
const DOC = {
  id: 'd-1', nombre: 'certificado.pdf', tipoMime: 'application/pdf', tamanoBytes: 1000,
  estado: 'Disponible', subidoEn: '2026-11-19T14:00:00Z',
}

let cola: CredencialEnCola[]
let llamadas: { ruta: string; metodo: string; cuerpo?: unknown }[]
let alTomar: (id: string) => Response
let alResolver: () => Response

beforeEach(() => {
  // La API la manda en otro orden: la pantalla promete «la más antigua primero».
  cola = [NUEVA, ANTIGUA]
  llamadas = []
  alTomar = (id) => {
    const c = cola.find((x) => x.id === id)!
    return json({ ...c, reservadaPorMi: true, reservadaHasta: enMinutos(24.5), respaldo: c.sinDocumentos ? [] : [DOC] })
  }
  alResolver = () => json({ id: 'x', estado: 'Verificada', esperandoPrestador: false })
  olvidarCsrf()
  vi.stubGlobal('fetch', vi.fn(async (ruta: string, opciones?: RequestInit) => {
    if (ruta === '/api/auth/csrf') return json({ token: 't' })
    const metodo = opciones?.method ?? 'GET'
    const cuerpo = typeof opciones?.body === 'string' ? JSON.parse(opciones.body) : undefined
    llamadas.push({ ruta, metodo, cuerpo })
    if (ruta === '/api/interno/credenciales/cola') return json(cola)
    const tomar = ruta.match(/^\/api\/interno\/credenciales\/([^/]+)\/tomar$/)
    if (tomar && metodo === 'POST') return alTomar(tomar[1])
    if (ruta.endsWith('/resolver') && metodo === 'POST') return alResolver()
    return json({}, 404)
  }))
})

function pintar() {
  return render(<MemoryRouter><MesaDeVerificacion /></MemoryRouter>)
}

const resoluciones = () => llamadas.filter((l) => l.ruta.endsWith('/resolver'))

async function tomarLaPrimera(nombre = /Técnico en electricidad/) {
  pintar()
  await userEvent.click(await screen.findByRole('button', { name: new RegExp(`Tomar: ${nombre.source}`) }))
  await screen.findByRole('heading', { level: 2, name: 'Resolver' })
}

describe('JQ-11: la cola (API-29)', () => {
  it('la más antigua primero, con lo que el Verificador tiene que saber antes de tomarla', async () => {
    pintar()
    const lista = await screen.findByRole('list', { name: 'Verificación de credenciales' })
    const filas = within(lista).getAllByRole('listitem').filter((li) => li.parentElement === lista)
    expect(filas).toHaveLength(2)

    expect(within(filas[0]).getByRole('heading', { name: 'Título: Técnico en electricidad' })).toBeInTheDocument()
    expect(within(filas[0]).getByRole('link', { name: 'Luis Catrileo' })).toHaveAttribute('href', '/prestadores/luis-catrileo')
    expect(filas[0]).toHaveTextContent('Instituto Sur')
    expect(filas[0]).toHaveTextContent('Entidad no reconocida')
    expect(filas[0]).toHaveTextContent('Sin documentos')

    expect(within(filas[1]).getByRole('heading', { name: 'Curso: Primeros auxilios' })).toBeInTheDocument()
    expect(filas[1]).toHaveTextContent('1 documento')
    expect(filas[1]).not.toHaveTextContent('Entidad no reconocida')
    expect(filas[1]).not.toHaveTextContent('Sin documentos')
  })

  it('vacía, lo dice', async () => {
    cola = []
    pintar()
    expect(await screen.findByText('No hay credenciales esperando revisión.')).toBeInTheDocument()
  })

  it('si falla, se puede reintentar', async () => {
    let primera = true
    vi.mocked(fetch).mockImplementation(async (ruta) => {
      if (ruta === '/api/interno/credenciales/cola' && primera) { primera = false; return json({}, 500) }
      return json(cola)
    })
    pintar()
    await userEvent.click(await screen.findByRole('button', { name: 'Reintentar' }))
    expect(await screen.findByRole('heading', { name: 'Título: Técnico en electricidad' })).toBeInTheDocument()
  })
})

describe('JQ-11: tomar (API-30)', () => {
  it('muestra cuánto queda de la reserva de 30 minutos', async () => {
    await tomarLaPrimera()
    expect(screen.getByRole('timer')).toHaveTextContent('Te quedan 25 minutos de reserva.')
    expect(llamadas).toContainEqual(expect.objectContaining({
      ruta: '/api/interno/credenciales/c-antigua/tomar', metodo: 'POST',
    }))
  })

  it('si otro la tomó (409), lo dice con el mensaje de la API y vuelve a pedir la cola', async () => {
    alTomar = () => json({ title: 'Otro verificador ya la tomó.', status: 409 }, 409)
    pintar()
    await userEvent.click(await screen.findByRole('button', { name: /Tomar: Técnico en electricidad/ }))
    expect(await screen.findByRole('alert')).toHaveTextContent('Otro verificador ya la tomó.')
    await waitFor(() =>
      expect(llamadas.filter((l) => l.ruta === '/api/interno/credenciales/cola')).toHaveLength(2))
    expect(screen.queryByRole('heading', { name: 'Resolver' })).toBeNull()
  })

  it('con la reserva vencida no deja resolver', async () => {
    alTomar = (id) => json({ ...cola.find((x) => x.id === id)!, reservadaPorMi: true, reservadaHasta: enMinutos(-1) })
    pintar()
    await userEvent.click(await screen.findByRole('button', { name: /Tomar: Técnico en electricidad/ }))
    expect(await screen.findByRole('timer')).toHaveTextContent('Tu reserva venció.')
    expect(screen.queryByRole('heading', { name: 'Resolver' })).toBeNull()
  })
})

describe('JQ-11: los documentos de respaldo (API-74)', () => {
  it('cada uno se abre en una pestaña nueva, desde la ruta interna', async () => {
    await tomarLaPrimera(/Primeros auxilios/)
    const abrir = screen.getByRole('link', { name: /Abrir certificado\.pdf/ })
    expect(abrir).toHaveAttribute('href', '/api/interno/credenciales/c-nueva/documentos/d-1')
    expect(abrir).toHaveAttribute('target', '_blank')
    expect(abrir.getAttribute('rel')).toContain('noopener')
  })

  it('sin documentos, lo avisa y no muestra enlaces', async () => {
    await tomarLaPrimera()
    expect(screen.getAllByText(/llegó sin documentos/).length).toBeGreaterThan(0)
    expect(screen.queryByRole('link', { name: /Abrir/ })).toBeNull()
  })
})

describe('JQ-11: resolver (API-31)', () => {
  it('verificar manda el resultado y vuelve a la cola con el aviso', async () => {
    await tomarLaPrimera(/Primeros auxilios/)
    await userEvent.click(screen.getByRole('radio', { name: 'Verificar' }))
    await userEvent.type(screen.getByLabelText('Detalle'), 'Revisado contra el registro.')
    await userEvent.click(screen.getByRole('button', { name: 'Guardar resolución' }))

    expect(await screen.findByRole('status')).toHaveTextContent('la credencial quedó verificada')
    expect(resoluciones()).toEqual([{
      ruta: '/api/interno/credenciales/c-nueva/resolver', metodo: 'POST',
      cuerpo: { resultado: 'Verificada', detalle: 'Revisado contra el registro.' },
    }])
    expect(screen.getByRole('list', { name: 'Verificación de credenciales' })).toBeInTheDocument()
  })

  it('un rechazo sin motivo no se manda', async () => {
    await tomarLaPrimera()
    await userEvent.click(screen.getByRole('radio', { name: 'Rechazar' }))
    await userEvent.type(screen.getByLabelText('Detalle'), 'El documento no corresponde.')
    await userEvent.click(screen.getByRole('button', { name: 'Guardar resolución' }))

    expect(screen.getByRole('alert')).toHaveTextContent('Elige el motivo del rechazo.')
    expect(resoluciones()).toHaveLength(0)
  })

  it('un rechazo con motivo lleva el motivo del enum', async () => {
    alResolver = () => json({ id: 'x', estado: 'Rechazada', esperandoPrestador: false })
    await tomarLaPrimera()
    await userEvent.click(screen.getByRole('radio', { name: 'Rechazar' }))
    await userEvent.selectOptions(screen.getByLabelText('Motivo del rechazo'), 'EntidadNoReconocida')
    await userEvent.type(screen.getByLabelText('Detalle'), 'No figura en el registro.')
    await userEvent.click(screen.getByRole('button', { name: 'Guardar resolución' }))

    await screen.findByRole('status')
    expect(resoluciones()[0].cuerpo).toEqual({
      resultado: 'Rechazada', motivo: 'EntidadNoReconocida', detalle: 'No figura en el registro.',
    })
  })

  it('el mensaje de la API se muestra tal cual (sin documentos, detalle corto)', async () => {
    const mensaje = 'Esta credencial no tiene documentos: explica cómo la comprobaste (al menos 20 caracteres).'
    alResolver = () => json({ title: mensaje, status: 422 }, 422)
    await tomarLaPrimera()
    await userEvent.click(screen.getByRole('radio', { name: 'Verificar' }))
    await userEvent.type(screen.getByLabelText('Detalle'), 'Lo llamé.')
    await userEvent.click(screen.getByRole('button', { name: 'Guardar resolución' }))

    expect(await screen.findByRole('alert')).toHaveTextContent(mensaje)
    expect(screen.getByRole('heading', { name: 'Resolver' })).toBeInTheDocument()
  })

  it('en un 400 de validación muestra el mensaje del campo', async () => {
    alResolver = () => json({
      title: 'One or more validation errors occurred.', status: 400,
      errors: { Detalle: ['Escribe el detalle.'] },
    }, 400)
    await tomarLaPrimera()
    await userEvent.click(screen.getByRole('radio', { name: 'Pedir antecedentes' }))
    await userEvent.click(screen.getByRole('button', { name: 'Guardar resolución' }))
    expect(await screen.findByRole('alert')).toHaveTextContent('Escribe el detalle.')
    expect(screen.getByRole('alert')).not.toHaveTextContent(/validation/)
  })
})
