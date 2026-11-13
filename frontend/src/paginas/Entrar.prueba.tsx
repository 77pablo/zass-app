import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { olvidarCsrf } from '../api/cliente'
import { Contexto, type EstadoDeSesion, type Usuario } from '../sesion/contexto'
import { Entrar } from './Entrar'

function json(cuerpo: unknown, estado = 200) {
  return new Response(JSON.stringify(cuerpo), { status: estado, headers: { 'content-type': 'application/json' } })
}

const USUARIO: Usuario = {
  id: 'u1', correo: 'ana@zass.local', nombre: 'Ana', correoVerificado: true, roles: ['buscador'],
}

/** Lo que pasa cuando la pantalla llama a `recargar`: el segundo paso del ingreso. */
let alLeerLaCuenta: () => Promise<void>

beforeEach(() => {
  olvidarCsrf()
  alLeerLaCuenta = async () => {}
  vi.stubGlobal('fetch', vi.fn(async (ruta: string) => {
    if (ruta === '/api/auth/csrf') return json({ token: 't' })
    // El POST que abre la sesion: siempre sale bien en estas pruebas. Lo que se mide es lo de despues.
    if (ruta === '/api/auth/sesion') return json({ usuario: USUARIO })
    return json({}, 404)
  }))
})

afterEach(() => { document.head.innerHTML = ''; document.title = '' })

function pintar(estadoInicial: EstadoDeSesion = 'fuera') {
  let estado = estadoInicial
  const vista = render(
    <MemoryRouter initialEntries={['/entrar']}>
      <Contexto.Provider
        value={{
          estado, usuario: estado === 'dentro' ? USUARIO : null,
          recargar: () => alLeerLaCuenta(), salir: async () => {},
        }}
      >
        <Routes>
          <Route path="/entrar" element={<Entrar />} />
          <Route path="/" element={<p>la portada</p>} />
        </Routes>
      </Contexto.Provider>
    </MemoryRouter>,
  )
  // Para repintar con la sesion ya abierta, como hace el proveedor de verdad.
  const dentro = () => {
    estado = 'dentro'
    vista.rerender(
      <MemoryRouter initialEntries={['/entrar']}>
        <Contexto.Provider
          value={{ estado, usuario: USUARIO, recargar: () => alLeerLaCuenta(), salir: async () => {} }}
        >
          <Routes>
            <Route path="/entrar" element={<Entrar />} />
            <Route path="/" element={<p>la portada</p>} />
          </Routes>
        </Contexto.Provider>
      </MemoryRouter>,
    )
  }
  return { dentro }
}

async function entrar() {
  await userEvent.type(screen.getByLabelText('Correo'), 'ana@zass.local')
  await userEvent.type(screen.getByLabelText('Contraseña'), 'contrasena-larga-1')
  await userEvent.click(screen.getByRole('button', { name: 'Entrar' }))
}

describe('entrar: cuando la contrasena es correcta pero no se puede leer la cuenta (D3)', () => {
  it('lo dice, en vez de dejar la pantalla muda', async () => {
    // El GET /api/mi/cuenta falla; el proveedor de sesion se lo come y deja el estado en 'fuera'.
    alLeerLaCuenta = async () => {}
    pintar()

    await entrar()

    const aviso = await screen.findByRole('alert')
    expect(aviso).toHaveTextContent('Tu contraseña era correcta, pero no pudimos cargar tu cuenta.')
    expect(screen.getByRole('button', { name: 'Reintentar' })).toBeInTheDocument()
  })

  it('reintentar vuelve a pedir la cuenta', async () => {
    const intentos = vi.fn(async () => {})
    alLeerLaCuenta = intentos
    pintar()

    await entrar()
    await userEvent.click(await screen.findByRole('button', { name: 'Reintentar' }))

    expect(intentos).toHaveBeenCalledTimes(2)
  })

  it('cuando la cuenta SI se lee, no aparece ningun aviso: se va a su pagina', async () => {
    const { dentro } = pintar()
    alLeerLaCuenta = async () => { dentro() }

    await entrar()

    expect(await screen.findByText('la portada')).toBeInTheDocument()
    expect(screen.queryByRole('alert')).toBeNull()
  })
})
