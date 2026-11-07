import { beforeEach, describe, expect, it, vi } from 'vitest'
import { render, screen } from '@testing-library/react'
import usuario from '@testing-library/user-event'
import { App } from './App'

const iniciarSesion = vi.fn()
const cerrarSesion = vi.fn()
vi.mock('./funciones/identidad/servicio', () => ({
  iniciarSesion: (...a: unknown[]) => iniciarSesion(...a),
  cerrarSesion: () => cerrarSesion(),
  registrarse: vi.fn(),
  miCuenta: vi.fn(),
}))

beforeEach(() => {
  iniciarSesion.mockReset()
  cerrarSesion.mockReset()
})

describe('aplicacion', () => {
  it('abre en la pantalla de entrar', () => {
    render(<App />)
    expect(screen.getByRole('heading', { name: /entrar/i })).toBeInTheDocument()
  })

  it('se puede pasar a crear cuenta y volver', async () => {
    render(<App />)

    await usuario.click(screen.getByRole('button', { name: /crear una/i }))
    expect(screen.getByRole('heading', { name: /crear cuenta/i })).toBeInTheDocument()

    await usuario.click(screen.getByRole('button', { name: /entrar/i }))
    expect(screen.getByRole('heading', { name: /entrar/i })).toBeInTheDocument()
  })

  it('tras entrar saluda y ofrece salir', async () => {
    iniciarSesion.mockResolvedValueOnce({
      usuarioId: 'u1', roles: ['prestador'], correoVerificado: true,
    })
    render(<App />)

    await usuario.type(screen.getByLabelText(/correo/i), 'pablo@zass.cl')
    await usuario.type(screen.getByLabelText(/contrase/i), 'una-clave-larga')
    await usuario.click(screen.getByRole('button', { name: /^entrar$/i }))

    expect(await screen.findByText(/prestador/i)).toBeInTheDocument()
    expect(screen.getByRole('button', { name: /salir/i })).toBeInTheDocument()
  })

  // RN-S1: hasta no verificar el correo la cuenta esta a medias. Si la pantalla
  // no lo dice, la persona se entera cuando algo le falla sin explicacion.
  it('avisa si el correo todavia no esta verificado', async () => {
    iniciarSesion.mockResolvedValueOnce({
      usuarioId: 'u1', roles: ['buscador'], correoVerificado: false,
    })
    render(<App />)

    await usuario.type(screen.getByLabelText(/correo/i), 'pablo@zass.cl')
    await usuario.type(screen.getByLabelText(/contrase/i), 'una-clave-larga')
    await usuario.click(screen.getByRole('button', { name: /^entrar$/i }))

    expect(await screen.findByText(/sin verificar/i)).toBeInTheDocument()
  })
})
