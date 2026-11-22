import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import type { MisMetricas } from '../funciones/metricas/servicio'
import { BarraInferior } from '../marco/BarraInferior'
import { Cabecera } from '../marco/Cabecera'
import { ProveedorDeMenuMovil } from '../marco/ProveedorDeMenuMovil'
import { Contexto, type EstadoDeSesion, type Usuario } from '../sesion/contexto'
import { MiActividad } from './MiActividad'

function json(cuerpo: unknown, estado = 200) {
  return new Response(JSON.stringify(cuerpo), { status: estado, headers: { 'content-type': 'application/json' } })
}

const DATOS: MisMetricas = {
  contactos: { ultimos7Dias: 1, ultimos30Dias: 2, total: 3 },
  contactosPorCanal30Dias: { whatsapp: 1, telefono: 1, correo: 0, formulario: 0 },
  escaneos: { ultimos7Dias: 4, ultimos30Dias: 5, total: 6 },
  calculadoEn: '2026-11-20T15:00:00Z',
}

let pedidas: string[]
let respuesta: () => Response
beforeEach(() => {
  pedidas = []
  respuesta = () => json(DATOS)
  vi.stubGlobal('fetch', vi.fn(async (ruta: string) => {
    pedidas.push(ruta)
    return ruta === '/api/mi/metricas' ? respuesta() : json({}, 404)
  }))
})

function sesion(estado: EstadoDeSesion, roles: string[]) {
  const usuario: Usuario | null = estado === 'dentro'
    ? { id: 'u', correo: 'a@b.cl', nombre: 'Ana', correoVerificado: true, roles } : null
  return { estado, usuario, recargar: async () => {}, salir: async () => {} }
}

function pintar(estado: EstadoDeSesion = 'dentro', roles = ['buscador', 'prestador']) {
  return render(
    <MemoryRouter initialEntries={['/mi-actividad']}>
      <Contexto.Provider value={sesion(estado, roles)}>
        <Routes>
          <Route path="/mi-actividad" element={<MiActividad />} />
          <Route path="/entrar" element={<p>pantalla de entrar</p>} />
        </Routes>
      </Contexto.Provider>
    </MemoryRouter>,
  )
}

describe('JQ-8: Mi actividad (MI-METRICAS)', () => {
  it('muestra contactos y escaneos en sus tres ventanas, y los contactos por canal', async () => {
    pintar()
    const contactos = (await screen.findByRole('heading', { level: 2, name: 'Contactos recibidos' })).closest('section')!
    const cifras = within(contactos).getAllByRole('definition').map((d) => d.textContent)
    // 7 días, 30 días, desde siempre; luego WhatsApp, Teléfono, Correo, Formulario.
    expect(cifras).toEqual(['1', '2', '3', '1', '1', '0', '0'])

    const escaneos = screen.getByRole('heading', { level: 2, name: 'Escaneos de tus tarjetas' }).closest('section')!
    expect(within(escaneos).getAllByRole('definition').map((d) => d.textContent)).toEqual(['4', '5', '6'])
    expect(pedidas).toEqual(['/api/mi/metricas'])
  })

  it('quien no es prestador no pide nada y recibe la invitación a ofrecer servicios', () => {
    pintar('dentro', ['buscador'])
    expect(screen.getByRole('link', { name: 'ofrecer tus servicios' })).toHaveAttribute('href', '/ofrecer-servicios')
    expect(pedidas).toEqual([])
  })

  it('sin sesión manda a entrar', () => {
    pintar('fuera', [])
    expect(screen.getByText('pantalla de entrar')).toBeInTheDocument()
  })

  it('si falla, se puede reintentar', async () => {
    let primera = true
    respuesta = () => { if (primera) { primera = false; return json({}, 500) } return json(DATOS) }
    pintar()
    await userEvent.click(await screen.findByRole('button', { name: 'Reintentar' }))
    expect(await screen.findByRole('heading', { level: 2, name: 'Contactos recibidos' })).toBeInTheDocument()
  })
})

describe('JQ-8: el enlace en «Más»', () => {
  function pintarMenu(roles: string[]) {
    return render(
      <MemoryRouter>
        <Contexto.Provider value={sesion('dentro', roles)}>
          <ProveedorDeMenuMovil><Cabecera /><BarraInferior /></ProveedorDeMenuMovil>
        </Contexto.Provider>
      </MemoryRouter>,
    )
  }

  it('el prestador lo tiene', async () => {
    pintarMenu(['buscador', 'prestador'])
    await userEvent.click(screen.getByRole('button', { name: 'Más' }))
    expect(screen.getByRole('link', { name: /Mi actividad/ })).toHaveAttribute('href', '/mi-actividad')
  })

  it('quien solo busca, no', async () => {
    pintarMenu(['buscador'])
    await userEvent.click(screen.getByRole('button', { name: 'Más' }))
    expect(screen.queryByRole('link', { name: /Mi actividad/ })).toBeNull()
  })
})
