import { beforeEach, describe, expect, it, vi } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import usuario from '@testing-library/user-event'
import { Ingresar } from './Ingresar'
import { ErrorApi } from '../../api/cliente'

const iniciarSesion = vi.fn()
vi.mock('./servicio', () => ({
  iniciarSesion: (...args: unknown[]) => iniciarSesion(...args),
  registrarse: vi.fn(),
}))

beforeEach(() => {
  iniciarSesion.mockReset()
})

describe('pantalla de ingreso', () => {
  it('muestra los dos campos y el boton', () => {
    render(<Ingresar alEntrar={vi.fn()} />)

    expect(screen.getByLabelText(/correo/i)).toBeInTheDocument()
    expect(screen.getByLabelText(/contrase/i)).toBeInTheDocument()
    expect(screen.getByRole('button', { name: /entrar/i })).toBeInTheDocument()
  })

  it('no llama a la API si falta el correo', async () => {
    render(<Ingresar alEntrar={vi.fn()} />)

    await usuario.click(screen.getByRole('button', { name: /entrar/i }))

    expect(iniciarSesion).not.toHaveBeenCalled()
    expect(await screen.findByText(/escribe tu correo/i)).toBeInTheDocument()
  })

  // El validador del backend NO exige largo minimo al ingresar, a proposito: decirlo
  // aqui le regala al atacante el minimo del sistema. La pantalla hace lo mismo.
  it('no revela el largo minimo de la contrasena', async () => {
    render(<Ingresar alEntrar={vi.fn()} />)

    await usuario.type(screen.getByLabelText(/correo/i), 'pablo@zass.cl')
    await usuario.type(screen.getByLabelText(/contrase/i), 'corta')
    await usuario.click(screen.getByRole('button', { name: /entrar/i }))

    expect(screen.queryByText(/10 caracteres/i)).not.toBeInTheDocument()
    await waitFor(() => expect(iniciarSesion).toHaveBeenCalled())
  })

  it('envia lo escrito y avisa a quien la monta', async () => {
    const alEntrar = vi.fn()
    iniciarSesion.mockResolvedValueOnce({ usuarioId: 'u1', roles: ['buscador'], correoVerificado: true })
    render(<Ingresar alEntrar={alEntrar} />)

    await usuario.type(screen.getByLabelText(/correo/i), 'pablo@zass.cl')
    await usuario.type(screen.getByLabelText(/contrase/i), 'una-clave-larga')
    await usuario.click(screen.getByRole('button', { name: /entrar/i }))

    await waitFor(() =>
      expect(iniciarSesion).toHaveBeenCalledWith('pablo@zass.cl', 'una-clave-larga'))
    expect(alEntrar).toHaveBeenCalledWith(
      expect.objectContaining({ usuarioId: 'u1' }))
  })

  it('muestra el mensaje de la API cuando las credenciales no sirven', async () => {
    iniciarSesion.mockRejectedValueOnce(
      new ErrorApi(401, 'Correo o contrasena incorrectos.'))
    render(<Ingresar alEntrar={vi.fn()} />)

    await usuario.type(screen.getByLabelText(/correo/i), 'pablo@zass.cl')
    await usuario.type(screen.getByLabelText(/contrase/i), 'equivocada')
    await usuario.click(screen.getByRole('button', { name: /entrar/i }))

    expect(await screen.findByRole('alert')).toHaveTextContent(/incorrectos/i)
  })

  it('bloquea el boton mientras envia, para no crear dos sesiones de un doble clic', async () => {
    let soltar: (v: unknown) => void = () => {}
    iniciarSesion.mockReturnValueOnce(new Promise((r) => { soltar = r }))
    render(<Ingresar alEntrar={vi.fn()} />)

    await usuario.type(screen.getByLabelText(/correo/i), 'pablo@zass.cl')
    await usuario.type(screen.getByLabelText(/contrase/i), 'una-clave-larga')
    await usuario.click(screen.getByRole('button', { name: /entrar/i }))

    await waitFor(() => expect(screen.getByRole('button', { name: /entrando/i })).toBeDisabled())
    soltar({ usuarioId: 'u1', roles: [], correoVerificado: true })
  })

  it('al agotarse los intentos muestra el 429 y no un error generico', async () => {
    iniciarSesion.mockRejectedValueOnce(
      new ErrorApi(429, 'Demasiados intentos. Espera 15 minutos.'))
    render(<Ingresar alEntrar={vi.fn()} />)

    await usuario.type(screen.getByLabelText(/correo/i), 'pablo@zass.cl')
    await usuario.type(screen.getByLabelText(/contrase/i), 'una-clave-larga')
    await usuario.click(screen.getByRole('button', { name: /entrar/i }))

    expect(await screen.findByRole('alert')).toHaveTextContent(/15 minutos/i)
  })
})
