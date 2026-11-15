import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { olvidarCsrf } from '../api/cliente'
import * as T from '../funciones/tarjetas/textos'
import { Contexto, type EstadoDeSesion, type Usuario } from '../sesion/contexto'
import { MisTarjetas } from './MisTarjetas'

/** Lo que el QR recibe: el doble de la librería anota cada `addData`. */
const datosDelQr: string[] = []
vi.mock('qrcode-generator', () => ({
  default: () => ({
    addData: (d: string) => { datosDelQr.push(d) },
    make: () => {},
    getModuleCount: () => 21,
    isDark: (f: number, c: number) => (f + c) % 2 === 0,
  }),
}))

function json(cuerpo: unknown, estado = 200) {
  return new Response(JSON.stringify(cuerpo), { status: estado, headers: { 'content-type': 'application/json' } })
}

const ACTIVA = {
  id: 't-activa', urlEstable: '7KQ2-9XRM', estado: 'Activa',
  activadaEn: '2026-10-01T15:00:00Z', desactivadaEn: null,
}
const MUERTA = {
  id: 't-muerta', urlEstable: 'ABCD-2345', estado: 'Desactivada',
  activadaEn: '2026-08-01T15:00:00Z', desactivadaEn: '2026-09-01T15:00:00Z',
}
const CIFRAS = {
  ultimos7Dias: { nfc: 3, qr: 1, total: 4 },
  ultimos30Dias: { nfc: 4, qr: 2, total: 6 },
  total: { nfc: 5, qr: 3, total: 8 },
}

let tarjetas: unknown[]
let llamadas: { ruta: string; metodo: string }[]
let alListar: () => Response
let alDesactivar: () => Response

beforeEach(() => {
  tarjetas = [ACTIVA, MUERTA]
  llamadas = []
  datosDelQr.length = 0
  alListar = () => json(tarjetas)
  alDesactivar = () => new Response(null, { status: 204 })
  olvidarCsrf()
  vi.stubGlobal('fetch', vi.fn(async (ruta: string, opciones?: RequestInit) => {
    if (ruta === '/api/auth/csrf') return json({ token: 't' })
    const metodo = opciones?.method ?? 'GET'
    llamadas.push({ ruta, metodo })
    if (ruta === '/api/mi/tarjetas') return alListar()
    if (ruta.endsWith('/estadisticas')) return json(CIFRAS)
    if (ruta.endsWith('/desactivar') && metodo === 'POST') return alDesactivar()
    return json({}, 404)
  }))
})

function pintar(sesion: EstadoDeSesion = 'dentro', roles = ['buscador', 'prestador']) {
  const usuario: Usuario | null = sesion === 'dentro'
    ? { id: 'u', correo: 'a@b.cl', nombre: 'Ana', correoVerificado: true, roles } : null
  return render(
    <MemoryRouter initialEntries={['/mis-tarjetas']}>
      <Contexto.Provider value={{ estado: sesion, usuario, recargar: async () => {}, salir: async () => {} }}>
        <Routes>
          <Route path="/mis-tarjetas" element={<MisTarjetas />} />
          <Route path="/entrar" element={<p>pantalla de entrar</p>} />
        </Routes>
      </Contexto.Provider>
    </MemoryRouter>,
  )
}

const tarjeta = async (url: string) => screen.findByRole('article', { name: url })
const posts = () => llamadas.filter((l) => l.metodo === 'POST')

describe('Mis tarjetas', () => {
  it('sin sesión manda a entrar', () => {
    pintar('fuera')
    expect(screen.getByText('pantalla de entrar')).toBeInTheDocument()
  })

  it('a quien no es prestador le explica y lo lleva a ofrecer servicios, sin pedir nada', () => {
    pintar('dentro', ['buscador'])
    expect(screen.getByRole('link', { name: T.NO_PRESTADOR_ENLACE })).toHaveAttribute('href', '/ofrecer-servicios')
    expect(llamadas).toHaveLength(0)
  })

  it('muestra el enlace zass.cl/t/… como texto, no como enlace que contaría un escaneo propio', async () => {
    pintar()
    const activa = await tarjeta('7KQ2-9XRM')
    expect(within(activa).getByText('zass.cl/t/7KQ2-9XRM')).toBeInTheDocument()
    expect(within(activa).queryByRole('link')).toBeNull()
    expect(within(activa).getByText(/Activa · Activada el 1 de octubre de 2026/)).toBeInTheDocument()
  })

  it('el QR codifica exactamente la URL estable con ?m=qr', async () => {
    pintar()
    const activa = await tarjeta('7KQ2-9XRM')
    await userEvent.click(within(activa).getByRole('button', { name: T.MOSTRAR_QR }))

    expect(await within(activa).findByRole('img', { name: T.altDelQr('7KQ2-9XRM') })).toBeInTheDocument()
    expect(datosDelQr).toEqual(['https://zass.cl/t/7KQ2-9XRM?m=qr'])
    expect(within(activa).getByRole('button', { name: T.OCULTAR_QR })).toBeInTheDocument()
  })

  it('cancelar la confirmación no manda nada', async () => {
    pintar()
    const activa = await tarjeta('7KQ2-9XRM')
    await userEvent.click(within(activa).getByRole('button', { name: T.DESACTIVAR }))
    expect(within(activa).getByText(T.confirmarDesactivar('7KQ2-9XRM'))).toBeInTheDocument()

    await userEvent.click(within(activa).getByRole('button', { name: T.CANCELAR }))

    expect(posts()).toHaveLength(0)
    expect(within(activa).getByRole('button', { name: T.DESACTIVAR })).toBeInTheDocument()
  })

  it('el primer clic solo pregunta; al confirmar desactiva, avisa y recarga la lista', async () => {
    pintar()
    const activa = await tarjeta('7KQ2-9XRM')
    await userEvent.click(within(activa).getByRole('button', { name: T.DESACTIVAR }))
    expect(posts()).toHaveLength(0)

    tarjetas = [{ ...ACTIVA, estado: 'Desactivada', desactivadaEn: '2026-11-14T21:00:00Z' }, MUERTA]
    await userEvent.click(within(activa).getByRole('button', { name: T.SI_DESACTIVAR }))

    expect(posts()).toEqual([{ ruta: '/api/mi/tarjetas/t-activa/desactivar', metodo: 'POST' }])
    expect(await screen.findByText(T.DESACTIVADA_OK)).toBeInTheDocument()
    await waitFor(() => expect(llamadas.filter((l) => l.ruta === '/api/mi/tarjetas')).toHaveLength(2))
    expect(await screen.findByText(T.SIN_TARJETAS)).toBeInTheDocument()
  })

  it('una tarjeta desactivada no ofrece desactivar ni QR', async () => {
    pintar()
    const muerta = await tarjeta('ABCD-2345')
    expect(within(muerta).getByText(/Desactivada · Desactivada el 1 de septiembre de 2026/)).toBeInTheDocument()
    expect(within(muerta).queryByRole('button', { name: T.DESACTIVAR })).toBeNull()
    expect(within(muerta).queryByRole('button', { name: T.MOSTRAR_QR })).toBeNull()
  })

  it('muestra los escaneos por medio y periodo, con la aclaración de que no se sabe quién', async () => {
    pintar()
    const activa = await tarjeta('7KQ2-9XRM')
    const fila = await within(activa).findByRole('row', { name: new RegExp(T.ULTIMOS_7) })
    expect(within(fila).getAllByRole('cell').map((c) => c.textContent)).toEqual(['3', '1', '4'])
    expect(within(activa).getByText(T.QUE_CONTAMOS)).toBeInTheDocument()
  })

  it('sin tarjetas activas lo dice', async () => {
    tarjetas = [MUERTA]
    pintar()
    expect(await screen.findByText(T.SIN_TARJETAS)).toBeInTheDocument()
  })

  it('si no carga, lo dice y deja reintentar', async () => {
    alListar = () => json({ title: 'x' }, 500)
    pintar()
    expect(await screen.findByRole('alert')).toHaveTextContent(T.ERROR_CARGAR)

    alListar = () => json(tarjetas)
    await userEvent.click(screen.getByRole('button', { name: 'Reintentar' }))
    expect(await tarjeta('7KQ2-9XRM')).toBeInTheDocument()
  })

  it('si desactivar falla, lo dice y la tarjeta sigue ahí', async () => {
    alDesactivar = () => json({ title: 'x' }, 500)
    pintar()
    const activa = await tarjeta('7KQ2-9XRM')
    await userEvent.click(within(activa).getByRole('button', { name: T.DESACTIVAR }))
    await userEvent.click(within(activa).getByRole('button', { name: T.SI_DESACTIVAR }))

    expect(await within(activa).findByRole('alert')).toHaveTextContent(T.ERROR_DESACTIVAR)
    expect(screen.queryByText(T.DESACTIVADA_OK)).toBeNull()
  })
})
