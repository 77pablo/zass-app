import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { useState } from 'react'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { olvidarCsrf } from '../api/cliente'
import * as T from '../funciones/identidad/textosDeCuenta'
import { Contexto, type EstadoDeSesion, type Usuario } from '../sesion/contexto'
import { MiCuenta } from './MiCuenta'

/*
 * Mi cuenta: eliminar la cuenta (CS-04, API-17). Lo que decidió Pablo el 17-nov-2026 tiene que
 * verse ANTES de confirmar: se pide la contraseña, se cancelan las horas futuras y, si ofrece
 * servicios, la suscripción, sin reembolso.
 */

const BUSCADORA: Usuario = {
  id: 'u1', correo: 'ana@zass.local', nombre: 'Ana Pérez', correoVerificado: true, roles: ['buscador'],
}
const PRESTADORA: Usuario = { ...BUSCADORA, roles: ['buscador', 'prestador'] }

function json(cuerpo: unknown, estado = 200) {
  return new Response(JSON.stringify(cuerpo), { status: estado, headers: { 'content-type': 'application/problem+json' } })
}

let llamadas: { ruta: string; metodo: string; cuerpo: unknown }[]
let alEliminar: () => Response

beforeEach(() => {
  llamadas = []
  alEliminar = () => new Response(null, { status: 204 })
  olvidarCsrf()
  vi.stubGlobal('fetch', vi.fn(async (ruta: string, opciones?: RequestInit) => {
    if (ruta === '/api/auth/csrf') return json({ token: 't' })
    const metodo = opciones?.method ?? 'GET'
    llamadas.push({ ruta, metodo, cuerpo: opciones?.body ? JSON.parse(String(opciones.body)) : undefined })
    if (ruta === '/api/mi/cuenta' && metodo === 'DELETE') return alEliminar()
    return json({}, 404)
  }))
})

/**
 * La sesion de verdad cambia: al eliminar, `recargar` la deja en 'fuera'. Este proveedor hace lo
 * mismo, para comprobar que la pantalla NO manda a /entrar cuando la cuenta se acaba de eliminar.
 */
function pintar(usuario: Usuario | null = BUSCADORA, estadoInicial: EstadoDeSesion = 'dentro') {
  const recargar = vi.fn()
  function Proveedor() {
    const [estado, setEstado] = useState<EstadoDeSesion>(estadoInicial)
    recargar.mockImplementation(async () => setEstado('fuera'))
    return (
      <Contexto.Provider value={{ estado, usuario: estado === 'dentro' ? usuario : null, recargar, salir: async () => {} }}>
        <Routes>
          <Route path="/mi-cuenta" element={<MiCuenta />} />
          <Route path="/entrar" element={<p>pagina de entrar</p>} />
        </Routes>
      </Contexto.Provider>
    )
  }
  render(<MemoryRouter initialEntries={['/mi-cuenta']}><Proveedor /></MemoryRouter>)
  return { recargar }
}

describe('Mi cuenta', () => {
  it('muestra los datos y, antes de confirmar, todo lo que pasa al eliminar', () => {
    pintar(PRESTADORA)

    expect(screen.getByRole('heading', { level: 1, name: T.TITULO })).toBeInTheDocument()
    expect(screen.getByText('ana@zass.local')).toBeInTheDocument()
    expect(screen.getByText(T.CONSECUENCIA_CITAS)).toBeVisible()
    // Quien ofrece servicios sabe que pierde la suscripcion y que no hay reembolso.
    expect(screen.getByText(T.CONSECUENCIA_PRESTADOR)).toBeVisible()
  })

  it('a quien solo busca no le habla de una suscripcion que no tiene', () => {
    pintar(BUSCADORA)

    expect(screen.queryByText(T.CONSECUENCIA_PRESTADOR)).not.toBeInTheDocument()
  })

  it('no deja eliminar sin escribir la contraseña', async () => {
    const u = userEvent.setup()
    pintar()

    const boton = screen.getByRole('button', { name: T.BOTON_ELIMINAR })
    expect(boton).toBeDisabled()
    await u.type(screen.getByLabelText(T.ETIQUETA_CONTRASENA), 'x')
    expect(boton).toBeEnabled()
  })

  it('con otra contraseña dice por que, junto al campo, y la cuenta sigue', async () => {
    const u = userEvent.setup()
    alEliminar = () => json({ title: 'Regla de negocio', status: 422, detail: 'La contraseña no es correcta.' }, 422)
    const { recargar } = pintar()

    await u.type(screen.getByLabelText(T.ETIQUETA_CONTRASENA), 'no-era-esta')
    await u.click(screen.getByRole('button', { name: T.BOTON_ELIMINAR }))

    expect(await screen.findByText('La contraseña no es correcta.')).toBeVisible()
    expect(screen.queryByText(T.ELIMINADA_TITULO)).not.toBeInTheDocument()
    expect(recargar).not.toHaveBeenCalled()
  })

  it('con la contraseña correcta manda DELETE con ella, y dice que se elimino aunque la sesion ya no exista', async () => {
    const u = userEvent.setup()
    const { recargar } = pintar()

    await u.type(screen.getByLabelText(T.ETIQUETA_CONTRASENA), 'contrasena-larga-1')
    await u.click(screen.getByRole('button', { name: T.BOTON_ELIMINAR }))

    expect(await screen.findByRole('heading', { level: 1, name: T.ELIMINADA_TITULO })).toBeInTheDocument()
    const borrado = llamadas.find((l) => l.ruta === '/api/mi/cuenta' && l.metodo === 'DELETE')
    expect(borrado?.cuerpo).toEqual({ contrasena: 'contrasena-larga-1' })
    // La sesion paso a 'fuera': la pagina NO manda a /entrar, sigue diciendo que se elimino.
    await waitFor(() => expect(recargar).toHaveBeenCalled())
    expect(screen.queryByText('pagina de entrar')).not.toBeInTheDocument()
  })

  it('sin sesion manda a entrar', () => {
    pintar(null, 'fuera')

    expect(screen.getByText('pagina de entrar')).toBeInTheDocument()
  })
})
