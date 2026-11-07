import { StrictMode } from 'react'
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
    // Cada respuesta se entrega una sola vez, como un token de un solo uso.
    const r = respuestas[ruta]
    delete respuestas[ruta]
    return r ?? json({ title: 'No encontrado' }, 404)
  }))
})

function en(ruta: string, estricto = false) {
  const arbol = (
    <MemoryRouter initialEntries={[ruta]}>
      <ProveedorDeSesion><App /></ProveedorDeSesion>
    </MemoryRouter>
  )
  return render(estricto ? <StrictMode>{arbol}</StrictMode> : arbol)
}

describe('verificar el correo (API-13)', () => {
  it('manda el token del enlace y dice que quedo verificado', async () => {
    respuestas['/api/auth/correo/verificar'] = new Response(null, { status: 204 })
    en('/verificar-correo?token=abc123')

    expect(await screen.findByRole('heading', { name: 'Correo verificado' })).toBeInTheDocument()
    expect(llamadas).toEqual([{ ruta: '/api/auth/correo/verificar', cuerpo: { token: 'abc123' } }])
  })

  // StrictMode monta el efecto dos veces en desarrollo. Si cada montaje consumiera el token, el
  // segundo recibiria "enlace ya usado" y taparia el exito del primero.
  it('consume el token UNA sola vez aunque el efecto corra dos veces', async () => {
    respuestas['/api/auth/correo/verificar'] = new Response(null, { status: 204 })
    en('/verificar-correo?token=abc123', true)

    expect(await screen.findByRole('heading', { name: 'Correo verificado' })).toBeInTheDocument()
    expect(llamadas.filter((l) => l.ruta === '/api/auth/correo/verificar')).toHaveLength(1)
  })

  it('un enlace vencido muestra el motivo de la API', async () => {
    respuestas['/api/auth/correo/verificar'] = json({ title: 'x', detail: 'Este enlace ya venció.' }, 400)
    en('/verificar-correo?token=viejo')

    expect(await screen.findByRole('heading', { name: 'No pudimos verificar' })).toBeInTheDocument()
    expect(screen.getByText('Este enlace ya venció.')).toBeInTheDocument()
  })

  it('sin token no llama a la API y lo dice', async () => {
    en('/verificar-correo')

    expect(await screen.findByText(/enlace está incompleto/)).toBeInTheDocument()
    expect(llamadas).toHaveLength(0)
  })
})

describe('recuperar la contraseña (API-14)', () => {
  it('manda el correo y responde igual exista o no la cuenta', async () => {
    const u = userEvent.setup()
    respuestas['/api/auth/contrasena/recuperar'] = new Response(null, { status: 204 })
    en('/recuperar-contrasena')

    await u.type(screen.getByLabelText('Correo'), '  nadie@ejemplo.cl ')
    await u.click(screen.getByRole('button', { name: /mandarme el enlace/i }))

    expect(await screen.findByText(/Si hay una cuenta con ese correo/)).toBeInTheDocument()
    expect(llamadas[0].cuerpo).toEqual({ correo: 'nadie@ejemplo.cl' })
  })

  it('no llama a la API con un correo vacío o sin forma de correo', async () => {
    const u = userEvent.setup()
    en('/recuperar-contrasena')

    await u.click(screen.getByRole('button', { name: /mandarme el enlace/i }))
    expect(screen.getByText('Escribe tu correo.')).toBeInTheDocument()
    await u.type(screen.getByLabelText('Correo'), 'sin-arroba')
    await u.click(screen.getByRole('button', { name: /mandarme el enlace/i }))
    expect(screen.getByText('Ese correo no parece válido.')).toBeInTheDocument()
    expect(llamadas).toHaveLength(0)
  })
})

describe('elegir una contraseña nueva (API-15)', () => {
  it('manda el token y la contraseña, y avisa que se cerraron las otras sesiones', async () => {
    const u = userEvent.setup()
    respuestas['/api/auth/contrasena/restablecer'] = new Response(null, { status: 204 })
    en('/restablecer-contrasena?token=tok')

    await u.type(screen.getByLabelText('Contraseña nueva'), 'una-clave-bien-larga')
    await u.click(screen.getByRole('button', { name: /cambiar contraseña/i }))

    expect(await screen.findByRole('heading', { name: 'Contraseña cambiada' })).toBeInTheDocument()
    expect(llamadas[0]).toEqual({
      ruta: '/api/auth/contrasena/restablecer',
      cuerpo: { token: 'tok', contrasenaNueva: 'una-clave-bien-larga' },
    })
    expect(screen.getByText(/cerramos tus sesiones abiertas/)).toBeInTheDocument()
  })

  it('una contraseña corta no sale del navegador', async () => {
    const u = userEvent.setup()
    en('/restablecer-contrasena?token=tok')

    await u.type(screen.getByLabelText('Contraseña nueva'), 'corta')
    await u.click(screen.getByRole('button', { name: /cambiar contraseña/i }))

    expect(screen.getByText('Usa al menos 10 caracteres.')).toBeInTheDocument()
    expect(llamadas).toHaveLength(0)
  })

  it('si la API rechaza el token, muestra su motivo y deja reintentar', async () => {
    const u = userEvent.setup()
    respuestas['/api/auth/contrasena/restablecer'] = json({ title: 'x', detail: 'El enlace venció.' }, 400)
    en('/restablecer-contrasena?token=tok')

    await u.type(screen.getByLabelText('Contraseña nueva'), 'una-clave-bien-larga')
    await u.click(screen.getByRole('button', { name: /cambiar contraseña/i }))

    expect(await screen.findByText('El enlace venció.')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: /cambiar contraseña/i })).toBeEnabled()
  })

  it('sin token manda a pedir un enlace nuevo y no muestra el formulario', () => {
    en('/restablecer-contrasena')

    expect(screen.getByRole('link', { name: /pedir un enlace nuevo/i })).toHaveAttribute('href', '/recuperar-contrasena')
    expect(screen.queryByLabelText('Contraseña nueva')).not.toBeInTheDocument()
  })
})

describe('entrar', () => {
  it('ofrece recuperar la contraseña', () => {
    en('/entrar')

    expect(screen.getByRole('link', { name: /olvidé mi contraseña/i })).toHaveAttribute('href', '/recuperar-contrasena')
  })
})
