import { beforeEach, describe, expect, it, vi } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import usuario from '@testing-library/user-event'
import { Registrarse } from './Registrarse'
import { ErrorApi } from '../../api/cliente'

const registrarse = vi.fn()
vi.mock('./servicio', () => ({
  registrarse: (...args: unknown[]) => registrarse(...args),
  iniciarSesion: vi.fn(),
}))

beforeEach(() => registrarse.mockReset())

async function llenar() {
  await usuario.type(screen.getByLabelText(/nombre/i), 'Pablo Espinoza')
  await usuario.type(screen.getByLabelText(/correo/i), 'pablo@zass.cl')
  await usuario.type(screen.getByLabelText(/contrase/i), 'una-clave-larga')
}

describe('pantalla de registro', () => {
  it('pide elegir entre buscar y ofrecer servicios', () => {
    render(<Registrarse alRegistrarse={vi.fn()} />)

    expect(screen.getByRole('radio', { name: /busco/i })).toBeInTheDocument()
    expect(screen.getByRole('radio', { name: /ofrezco/i })).toBeInTheDocument()
  })

  it('viene con buscador marcado: es el rol que no paga', () => {
    render(<Registrarse alRegistrarse={vi.fn()} />)

    expect(screen.getByRole('radio', { name: /busco/i })).toBeChecked()
  })

  // Aqui SI se dice el minimo, al reves que al ingresar: sin el, la persona no
  // puede cumplir la regla. La regla es RN del backend, ContrasenaMinima = 10.
  it('exige 10 caracteres y no llama a la API si son menos', async () => {
    render(<Registrarse alRegistrarse={vi.fn()} />)

    await usuario.type(screen.getByLabelText(/nombre/i), 'Pablo')
    await usuario.type(screen.getByLabelText(/correo/i), 'pablo@zass.cl')
    await usuario.type(screen.getByLabelText(/contrase/i), 'corta')
    await usuario.click(screen.getByRole('button', { name: /crear/i }))

    expect(registrarse).not.toHaveBeenCalled()
    expect(await screen.findByText(/10 caracteres/i)).toBeInTheDocument()
  })

  it('manda el rol elegido, no siempre buscador', async () => {
    registrarse.mockResolvedValueOnce(undefined)
    render(<Registrarse alRegistrarse={vi.fn()} />)

    await llenar()
    await usuario.click(screen.getByRole('radio', { name: /ofrezco/i }))
    await usuario.click(screen.getByRole('button', { name: /crear/i }))

    await waitFor(() => expect(registrarse).toHaveBeenCalledWith(
      'pablo@zass.cl', 'una-clave-larga', 'Pablo Espinoza', 'prestador'))
  })

  it('al crearse la cuenta dice que hay que revisar el correo', async () => {
    registrarse.mockResolvedValueOnce(undefined)
    render(<Registrarse alRegistrarse={vi.fn()} />)

    await llenar()
    await usuario.click(screen.getByRole('button', { name: /crear/i }))

    expect(await screen.findByText(/revisa tu correo/i)).toBeInTheDocument()
  })

  it('muestra el conflicto cuando el correo ya tiene cuenta', async () => {
    registrarse.mockRejectedValueOnce(new ErrorApi(409, 'Ese correo ya tiene una cuenta.'))
    render(<Registrarse alRegistrarse={vi.fn()} />)

    await llenar()
    await usuario.click(screen.getByRole('button', { name: /crear/i }))

    expect(await screen.findByRole('alert')).toHaveTextContent(/ya tiene una cuenta/i)
  })

  // El backend valida de nuevo, y puede rechazar algo que la pantalla dejo pasar.
  // Ese mensaje tiene que caer en el campo que corresponde, no en un aviso suelto.
  it('pone los errores por campo de la API junto a su campo', async () => {
    registrarse.mockRejectedValueOnce(new ErrorApi(400, 'Datos invalidos', {
      correo: 'Ese correo no parece valido.',
    }))
    render(<Registrarse alRegistrarse={vi.fn()} />)

    await llenar()
    await usuario.click(screen.getByRole('button', { name: /crear/i }))

    const campo = await screen.findByLabelText(/correo/i)
    await waitFor(() => expect(campo).toHaveAccessibleDescription(/no parece valido/i))
  })
})
