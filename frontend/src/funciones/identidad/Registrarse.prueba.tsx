import { beforeEach, describe, expect, it, vi } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import usuario from '@testing-library/user-event'
import { MemoryRouter } from 'react-router-dom'
import { Registrarse } from './Registrarse'
import { ErrorApi } from '../../api/cliente'
import { digitoVerificador, esTelefonoChileno, normalizarRut } from './rut'

const registrarse = vi.fn()
vi.mock('./servicio', () => ({
  registrarse: (...args: unknown[]) => registrarse(...args),
  iniciarSesion: vi.fn(),
}))

beforeEach(() => registrarse.mockReset())

/** Un particular con todo bien. */
async function llenar() {
  await usuario.type(screen.getByLabelText('RUT'), '12.345.678-5')
  await usuario.type(screen.getByLabelText('Nombre'), 'Pablo')
  await usuario.type(screen.getByLabelText('Apellido'), 'Espinoza')
  await usuario.type(screen.getByLabelText('Teléfono'), '+56 9 1234 5678')
  await usuario.type(screen.getByLabelText('Correo'), 'pablo@zass.cl')
  await usuario.type(screen.getByLabelText('Contraseña'), 'una-clave-larga')
  await usuario.click(screen.getByLabelText(/mayor de 18/))
}

const crear = () => usuario.click(screen.getByRole('button', { name: /crear cuenta/i }))

describe('pantalla de registro', () => {
  it('no pide elegir un rol: una sola cuenta sirve para buscar y para ofrecer', () => {
    render(<Registrarse alRegistrarse={vi.fn()} />)

    expect(screen.queryByRole('radio')).not.toBeInTheDocument()
    expect(screen.getByText(/una sola cuenta/i)).toBeInTheDocument()
  })

  // Aqui SI se dice el minimo, al reves que al ingresar: sin el, la persona no
  // puede cumplir la regla. La regla es RN del backend, ContrasenaMinima = 10.
  it('exige 10 caracteres y no llama a la API si son menos', async () => {
    render(<Registrarse alRegistrarse={vi.fn()} />)

    await llenar()
    await usuario.clear(screen.getByLabelText('Contraseña'))
    await usuario.type(screen.getByLabelText('Contraseña'), 'corta')
    await crear()

    expect(registrarse).not.toHaveBeenCalled()
    expect(await screen.findByText(/10 caracteres/i)).toBeInTheDocument()
  })

  it('un particular manda nombre, apellido, RUT, teléfono y que es mayor de edad; por defecto como buscador', async () => {
    registrarse.mockResolvedValueOnce(undefined)
    render(<Registrarse alRegistrarse={vi.fn()} />)

    await llenar()
    await crear()

    await waitFor(() => expect(registrarse).toHaveBeenCalledWith({
      correo: 'pablo@zass.cl', contrasena: 'una-clave-larga', nombre: 'Pablo', rol: 'buscador',
      tipo: 'particular', apellido: 'Espinoza', razonSocial: undefined, rut: '12.345.678-5',
      telefono: '+56 9 1234 5678', mayorDeEdad: true,
    }))
  })

  it('una empresa pide razón social y persona de contacto, y no pide apellido', async () => {
    registrarse.mockResolvedValueOnce(undefined)
    render(<Registrarse alRegistrarse={vi.fn()} rolInicial="prestador" />)

    await usuario.click(screen.getByRole('button', { name: 'Una empresa' }))
    expect(screen.queryByLabelText('Apellido')).toBeNull()

    await usuario.type(screen.getByLabelText('Razón social'), 'Gasfitería Rojas SpA')
    await usuario.type(screen.getByLabelText('RUT de la empresa'), '76.086.428-5')
    await usuario.type(screen.getByLabelText('Nombre de la persona de contacto'), 'Ana Rojas')
    await usuario.type(screen.getByLabelText('Teléfono'), '22 345 6789')
    await usuario.type(screen.getByLabelText('Correo'), 'ana@rojas.cl')
    await usuario.type(screen.getByLabelText('Contraseña'), 'una-clave-larga')
    await usuario.click(screen.getByLabelText(/represento a esta empresa/))
    await crear()

    await waitFor(() => expect(registrarse).toHaveBeenCalledWith(expect.objectContaining({
      tipo: 'empresa', razonSocial: 'Gasfitería Rojas SpA', nombre: 'Ana Rojas',
      apellido: undefined, rut: '76.086.428-5', rol: 'prestador',
    })))
  })

  it('un RUT con el dígito verificador equivocado no sale del navegador', async () => {
    render(<Registrarse alRegistrarse={vi.fn()} />)

    await llenar()
    await usuario.clear(screen.getByLabelText('RUT'))
    await usuario.type(screen.getByLabelText('RUT'), '12.345.678-4')
    await crear()

    expect(registrarse).not.toHaveBeenCalled()
    expect(screen.getByLabelText('RUT')).toHaveAccessibleDescription(/no es válido/)
  })

  it('sin marcar que es mayor de 18 no se crea la cuenta', async () => {
    render(<Registrarse alRegistrarse={vi.fn()} />)

    await llenar()
    await usuario.click(screen.getByLabelText(/mayor de 18/))   // la desmarca
    await crear()

    expect(registrarse).not.toHaveBeenCalled()
    expect(await screen.findByText(/tienes que ser mayor de 18/)).toBeInTheDocument()
  })

  it('un teléfono que no es chileno se avisa antes de enviar', async () => {
    render(<Registrarse alRegistrarse={vi.fn()} />)

    await llenar()
    await usuario.clear(screen.getByLabelText('Teléfono'))
    await usuario.type(screen.getByLabelText('Teléfono'), '1234')
    await crear()

    expect(registrarse).not.toHaveBeenCalled()
    expect(screen.getByLabelText('Teléfono')).toHaveAccessibleDescription(/9 dígitos/)
  })

  it('al crearse la cuenta dice que hay que revisar el correo', async () => {
    registrarse.mockResolvedValueOnce(undefined)
    render(<Registrarse alRegistrarse={vi.fn()} />)

    await llenar()
    await crear()

    expect(await screen.findByText(/revisa tu correo/i)).toBeInTheDocument()
  })

  it('muestra el conflicto cuando el correo ya tiene cuenta', async () => {
    registrarse.mockRejectedValueOnce(new ErrorApi(409, 'Ya existe una cuenta con ese correo.'))
    render(<Registrarse alRegistrarse={vi.fn()} />)

    await llenar()
    await crear()

    expect(await screen.findByRole('alert')).toHaveTextContent(/ya existe una cuenta/i)
    expect(screen.queryByRole('link', { name: 'Recuperar contraseña' })).toBeNull()
  })

  // Decisión de Pablo (21-nov-2026): el RUT con cuenta no es un error sino una salida. Se
  // muestra el texto del servidor como aviso informativo (no rojo, no role="alert") con los
  // enlaces a «Entrar» y a «Recuperar contraseña», y no se dice que la cuenta se creó.
  it('un RUT con cuenta se muestra como aviso informativo con Entrar y Recuperar contraseña', async () => {
    const texto = 'Este RUT ya tiene una cuenta en ZASS!. Enviamos un mensaje al correo con que se creó esa cuenta.'
    registrarse.mockRejectedValueOnce(new ErrorApi(409, texto, {}, { codigo: 'rut_con_cuenta', detail: texto }))
    render(<MemoryRouter><Registrarse alRegistrarse={vi.fn()} /></MemoryRouter>)

    await llenar()
    await crear()

    const aviso = await screen.findByRole('status')
    expect(aviso).toHaveTextContent(texto)
    expect(aviso).toHaveClass('aviso-linea')
    expect(aviso).not.toHaveClass('aviso--malo')
    expect(screen.queryByRole('alert')).toBeNull()
    expect(screen.getByRole('link', { name: 'Entrar' })).toHaveAttribute('href', '/entrar')
    expect(screen.getByRole('link', { name: 'Recuperar contraseña' })).toHaveAttribute('href', '/recuperar-contrasena')
    expect(screen.queryByText(/revisa tu correo/i)).toBeNull()
  })

  // El backend valida de nuevo, y puede rechazar algo que la pantalla dejo pasar.
  // Ese mensaje tiene que caer en el campo que corresponde, no en un aviso suelto.
  it('pone los errores por campo de la API junto a su campo', async () => {
    registrarse.mockRejectedValueOnce(new ErrorApi(400, 'Datos invalidos', {
      correo: 'Ese correo no parece valido.',
    }))
    render(<Registrarse alRegistrarse={vi.fn()} />)

    await llenar()
    await crear()

    const campo = await screen.findByLabelText('Correo')
    await waitFor(() => expect(campo).toHaveAccessibleDescription(/no parece valido/i))
  })
})

describe('RUT y teléfono', () => {
  it('el dígito verificador es el del módulo 11 (calculado a mano)', () => {
    expect(digitoVerificador(12345678)).toBe('5')
    expect(digitoVerificador(8765432)).toBe('K')
    expect(digitoVerificador(10000004)).toBe('0')
  })

  it('normaliza las formas en que se escribe un RUT y rechaza los inválidos', () => {
    expect(normalizarRut('12.345.678-5')).toBe('12345678-5')
    expect(normalizarRut('8.765.432-k')).toBe('8765432-K')
    expect(normalizarRut('12.345.678-4')).toBeNull()
    expect(normalizarRut('abc')).toBeNull()
  })

  it('acepta móvil y fijo chilenos de 9 dígitos', () => {
    expect(esTelefonoChileno('+56 9 1234 5678')).toBe(true)
    expect(esTelefonoChileno('22 345 6789')).toBe(true)
    expect(esTelefonoChileno('1234')).toBe(false)
  })
})
