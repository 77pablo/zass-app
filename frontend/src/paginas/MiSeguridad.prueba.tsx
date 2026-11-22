import { render, screen, waitFor } from '@testing-library/react'
import usuario from '@testing-library/user-event'
import { MemoryRouter } from 'react-router-dom'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { Contexto, type Usuario } from '../sesion/contexto'
import { MiSeguridad } from './MiSeguridad'

const estadoMfa = vi.fn()
const iniciarAltaMfa = vi.fn()
const confirmarAltaMfa = vi.fn()
const desactivarMfa = vi.fn()
vi.mock('../funciones/identidad/servicio', () => ({
  estadoMfa: () => estadoMfa(),
  iniciarAltaMfa: () => iniciarAltaMfa(),
  confirmarAltaMfa: (c: string) => confirmarAltaMfa(c),
  desactivarMfa: (a: string, b: string) => desactivarMfa(a, b),
}))
vi.mock('../componentes/tarjetas/CodigoQr', () => ({
  CodigoQr: ({ texto }: { texto: string }) => <p data-testid="qr">{texto}</p>,
}))

const INACTIVO = { activo: false, activadoEn: null, codigosDeRespaldoRestantes: 0, obligatorio: false }
const ACTIVO = { activo: true, activadoEn: '2026-11-22T00:00:00Z', codigosDeRespaldoRestantes: 10, obligatorio: false }

beforeEach(() => {
  for (const f of [estadoMfa, iniciarAltaMfa, confirmarAltaMfa, desactivarMfa]) f.mockReset()
})

function pintar() {
  const u: Usuario = { id: 'u', correo: 'a@b.cl', nombre: 'Ana', correoVerificado: true, roles: ['buscador'] }
  return render(
    <MemoryRouter>
      <Contexto.Provider value={{ estado: 'dentro', usuario: u, recargar: async () => {}, salir: async () => {} }}>
        <MiSeguridad />
      </Contexto.Provider>
    </MemoryRouter>,
  )
}

describe('Mi cuenta: verificación en dos pasos', () => {
  it('activa: muestra el QR, confirma con el código y entrega los 10 códigos de respaldo', async () => {
    estadoMfa.mockResolvedValueOnce(INACTIVO).mockResolvedValue(ACTIVO)
    iniciarAltaMfa.mockResolvedValue({ secreto: 'ABCDEF234567', uriOtpauth: 'otpauth://totp/ZASS%21:a@b.cl?secret=ABCDEF234567' })
    const codigos = Array.from({ length: 10 }, (_, i) => `AAAA${i}-BBBBB`)
    confirmarAltaMfa.mockResolvedValue(codigos)
    pintar()

    await usuario.click(await screen.findByRole('button', { name: 'Activar la verificación en dos pasos' }))
    expect(await screen.findByTestId('qr')).toHaveTextContent('otpauth://totp/ZASS%21')
    expect(screen.getByTestId('clave-mfa')).toHaveTextContent('ABCDEF234567')

    await usuario.type(screen.getByLabelText('Código'), '123456')
    await usuario.click(screen.getByRole('button', { name: 'Confirmar y activar' }))

    const lista = await screen.findByRole('list', { name: 'Códigos de respaldo' })
    expect(lista.querySelectorAll('li')).toHaveLength(10)
    expect(confirmarAltaMfa).toHaveBeenCalledWith('123456')
    expect(screen.getByRole('button', { name: 'Copiar los códigos' })).toBeInTheDocument()

    await usuario.click(screen.getByRole('button', { name: 'Ya los guardé' }))
    expect(await screen.findByText('activa')).toBeInTheDocument()
  })

  it('una cuenta del panel ve que es obligatoria', async () => {
    estadoMfa.mockResolvedValue({ ...INACTIVO, obligatorio: true })
    pintar()
    expect(await screen.findByText(/para usarlo tienes que activar/i)).toBeInTheDocument()
  })

  it('desactivar pide contraseña y código', async () => {
    estadoMfa.mockResolvedValueOnce(ACTIVO).mockResolvedValue(INACTIVO)
    desactivarMfa.mockResolvedValue(undefined)
    pintar()

    await usuario.click(await screen.findByRole('button', { name: 'Desactivar' }))
    await usuario.click(screen.getByRole('button', { name: 'Desactivar la verificación en dos pasos' }))
    expect(await screen.findByText('Escribe tu contraseña y un código.')).toBeInTheDocument()
    expect(desactivarMfa).not.toHaveBeenCalled()

    await usuario.type(screen.getByLabelText('Contraseña'), 'mi-clave-larga')
    await usuario.type(screen.getByLabelText('Código de la app o de respaldo'), '654321')
    await usuario.click(screen.getByRole('button', { name: 'Desactivar la verificación en dos pasos' }))

    await waitFor(() => expect(desactivarMfa).toHaveBeenCalledWith('mi-clave-larga', '654321'))
    expect(await screen.findByText('desactivada')).toBeInTheDocument()
  })
})
