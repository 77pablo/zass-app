import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router-dom'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { olvidarCsrf } from '../api/cliente'
import { EmitirTarjetas } from './EmitirTarjetas'

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

const CODIGO = 'K7Q2-M9XR-4TPA'
const EMITIDA = { segmento: '7KQ2-9XRM', codigo: CODIGO, venceEn: '2026-12-20T15:00:00Z' }

let emisiones: number
let alEmitir: () => Response

beforeEach(() => {
  emisiones = 0
  datosDelQr.length = 0
  alEmitir = () => json(EMITIDA, 201)
  localStorage.clear()
  sessionStorage.clear()
  olvidarCsrf()
  vi.stubGlobal('fetch', vi.fn(async (ruta: string, opciones?: RequestInit) => {
    if (ruta === '/api/auth/csrf') return json({ token: 't' })
    if (ruta === '/api/interno/tarjetas' && opciones?.method === 'POST') { emisiones++; return alEmitir() }
    return json({}, 404)
  }))
})

afterEach(() => { vi.restoreAllMocks() })

const pintar = () => render(<MemoryRouter><EmitirTarjetas /></MemoryRouter>)

/** Todo lo guardado en el navegador, como un solo texto. */
function todoLoGuardado(): string {
  const leer = (s: Storage) => Array.from({ length: s.length }, (_, i) => `${s.key(i)}=${s.getItem(s.key(i)!)}`).join(';')
  return `${leer(localStorage)}|${leer(sessionStorage)}|${document.cookie}|${window.location.href}`
}

async function emitir() {
  pintar()
  await userEvent.click(screen.getByRole('button', { name: 'Emitir una tarjeta' }))
  await screen.findByText(CODIGO)
}

describe('JQ-13: emitir una tarjeta (API-59)', () => {
  it('muestra el código, el enlace, el vencimiento, el QR y el aviso de que es una sola vez', async () => {
    await emitir()
    expect(emisiones).toBe(1)
    expect(screen.getByText(CODIGO)).toBeInTheDocument()
    expect(screen.getByText('zass.cl/t/7KQ2-9XRM')).toBeInTheDocument()
    expect(screen.getByText('20 de diciembre de 2026')).toBeInTheDocument()
    expect(screen.getByRole('alert')).toHaveTextContent('no lo volveremos a mostrar')
    expect(await screen.findByRole('img', { name: 'Código QR de zass.cl/t/7KQ2-9XRM' })).toBeInTheDocument()
    // El QR lleva el enlace de la tarjeta (contado como QR), no el código de activación.
    expect(datosDelQr).toEqual(['https://zass.cl/t/7KQ2-9XRM?m=qr'])
  })

  it('el código no queda guardado en ningún lado: ni localStorage, ni sessionStorage, ni cookie, ni URL', async () => {
    await emitir()
    expect(todoLoGuardado()).not.toContain(CODIGO)
  })

  it('al salir de la pantalla y volver, el código ya no está', async () => {
    const primera = pintar()
    await userEvent.click(screen.getByRole('button', { name: 'Emitir una tarjeta' }))
    await screen.findByText(CODIGO)
    primera.unmount()

    pintar()
    expect(screen.queryByText(CODIGO)).toBeNull()
    expect(screen.getByRole('button', { name: 'Emitir una tarjeta' })).toBeInTheDocument()
    expect(todoLoGuardado()).not.toContain(CODIGO)
    expect(emisiones).toBe(1)
  })

  it('mientras el código está en pantalla, el navegador avisa antes de cerrar', async () => {
    await emitir()
    const evento = new Event('beforeunload', { cancelable: true })
    window.dispatchEvent(evento)
    expect(evento.defaultPrevented).toBe(true)
  })

  it('«Ya lo guardé» pide confirmación; si se confirma, el código desaparece', async () => {
    const confirmar = vi.spyOn(window, 'confirm').mockReturnValueOnce(false).mockReturnValueOnce(true)
    await emitir()
    await userEvent.click(screen.getByRole('button', { name: 'Ya lo guardé, emitir otra' }))
    expect(screen.getByText(CODIGO)).toBeInTheDocument()
    await userEvent.click(screen.getByRole('button', { name: 'Ya lo guardé, emitir otra' }))
    expect(screen.queryByText(CODIGO)).toBeNull()
    expect(confirmar).toHaveBeenCalledTimes(2)

    // Sin código en pantalla, cerrar ya no pide nada.
    const evento = new Event('beforeunload', { cancelable: true })
    window.dispatchEvent(evento)
    expect(evento.defaultPrevented).toBe(false)
  })

  it('imprimir llama al diálogo del navegador', async () => {
    const imprimir = vi.spyOn(window, 'print').mockImplementation(() => {})
    await emitir()
    await userEvent.click(screen.getByRole('button', { name: 'Imprimir la etiqueta' }))
    expect(imprimir).toHaveBeenCalledOnce()
  })

  it('si la API falla, muestra su mensaje y no hay código', async () => {
    alEmitir = () => json({ title: 'Demasiadas solicitudes.', status: 429 }, 429)
    pintar()
    await userEvent.click(screen.getByRole('button', { name: 'Emitir una tarjeta' }))
    expect(await screen.findByRole('alert')).toHaveTextContent('Demasiadas solicitudes.')
    expect(screen.queryByText(CODIGO)).toBeNull()
  })
})
