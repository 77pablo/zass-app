import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router-dom'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { App } from '../App'
import { olvidarCsrf } from '../api/cliente'
import { ProveedorDeSesion } from '../sesion/Sesion'

let llamadas: { ruta: string; cuerpo: unknown }[]
let respuestas: Record<string, Response>

function json(cuerpo: unknown, estado = 200) {
  return new Response(JSON.stringify(cuerpo), { status: estado, headers: { 'content-type': 'application/json' } })
}

beforeEach(() => {
  llamadas = []
  respuestas = {}
  olvidarCsrf()
  localStorage.clear()
  vi.stubGlobal('fetch', vi.fn(async (ruta: string, opciones?: RequestInit) => {
    if (ruta === '/api/auth/csrf') return json({ token: 't' })
    llamadas.push({ ruta, cuerpo: opciones?.body ? JSON.parse(String(opciones.body)) : undefined })
    const r = respuestas[ruta]
    delete respuestas[ruta]
    return r ?? json({ title: 'No encontrado' }, 404)
  }))
})

function en(ruta: string) {
  return render(
    <MemoryRouter initialEntries={[ruta]}>
      <ProveedorDeSesion><App /></ProveedorDeSesion>
    </MemoryRouter>,
  )
}

describe('«No fui yo» (API-85)', () => {
  // Los antivirus del correo abren los enlaces solos: abrir la página no puede quitar nada.
  it('abrir el enlace NO llama a la API; solo el botón', async () => {
    en('/no-fui-yo?token=abc123')

    expect(await screen.findByRole('heading', { name: /No activaste tú/ })).toBeInTheDocument()
    expect(llamadas.filter((l) => l.ruta === '/api/auth/mfa/no-fui-yo')).toHaveLength(0)
  })

  it('con el botón manda el token y lleva a elegir la contraseña nueva con el que devuelve', async () => {
    respuestas['/api/auth/mfa/no-fui-yo'] = json({ tokenRestablecer: 'r+/=9' })
    en('/no-fui-yo?token=abc123')

    await userEvent.click(await screen.findByRole('button', { name: 'No fui yo: quitarla' }))

    expect(await screen.findByRole('heading', { name: 'Elige una contraseña nueva' })).toBeInTheDocument()
    expect(llamadas.find((l) => l.ruta === '/api/auth/mfa/no-fui-yo')?.cuerpo).toEqual({ token: 'abc123' })

    // Y el token que llega a restablecer es el de la API, no el del enlace.
    respuestas['/api/auth/contrasena/restablecer'] = new Response(null, { status: 204 })
    await userEvent.type(screen.getByLabelText('Contraseña nueva'), 'una-clave-nueva-77')
    await userEvent.click(screen.getByRole('button', { name: 'Cambiar contraseña' }))
    expect(await screen.findByRole('heading', { name: 'Contraseña cambiada' })).toBeInTheDocument()
    expect(llamadas.find((l) => l.ruta === '/api/auth/contrasena/restablecer')?.cuerpo)
      .toEqual({ token: 'r+/=9', contrasenaNueva: 'una-clave-nueva-77' })
  })

  it('un enlace usado o vencido muestra el motivo de la API y no sale de la página', async () => {
    respuestas['/api/auth/mfa/no-fui-yo'] = json({ title: 'No encontrado', detail: 'Este enlace ya no sirve.' }, 404)
    en('/no-fui-yo?token=viejo')

    await userEvent.click(await screen.findByRole('button', { name: 'No fui yo: quitarla' }))

    expect(await screen.findByText(/Este enlace ya no sirve/)).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'No fui yo: quitarla' })).toBeEnabled()
  })

  it('sin token dice que el enlace está incompleto y no llama a nada', async () => {
    en('/no-fui-yo')

    expect(await screen.findByRole('heading', { name: 'Enlace incompleto' })).toBeInTheDocument()
    expect(llamadas.filter((l) => l.ruta === '/api/auth/mfa/no-fui-yo')).toHaveLength(0)
  })
})
