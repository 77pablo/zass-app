import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router-dom'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { App } from '../../App'
import { olvidarCsrf } from '../../api/cliente'
import { olvidarCaches } from '../../api/useCargado'
import { ProveedorDeSesion } from '../../sesion/Sesion'
import { faltanDatos } from './datosDeCuenta'

/*
 * ES-2 · Cuentas creadas antes del registro completo (20-nov-2026). Se vigila: que la pantalla
 * vaya EN LUGAR de cualquier página mientras falten datos (no se puede saltar), que los textos
 * legales sigan abiertos, que valide con los mismos mensajes del registro, que al guardar vuelva
 * la página, y que un 428 de la API la haga aparecer aunque la sesión se cargara sin pendientes.
 */

function json(cuerpo: unknown, estado = 200) {
  return new Response(JSON.stringify(cuerpo), { status: estado, headers: { 'content-type': 'application/json' } })
}

const CUENTA = {
  id: 'u1', correo: 'ana@correo.cl', nombre: 'Ana', correoVerificado: true, roles: ['buscador'],
}

let pendientes: string[]
let alCompletar: () => Response
let enviados: { ruta: string; metodo: string; cuerpo: unknown }[]
let responder428: boolean
let cuentaAntesDel428: number

beforeEach(() => {
  cuentaAntesDel428 = 0
  pendientes = ['rut', 'telefono', 'mayorDeEdad']
  alCompletar = () => { pendientes = []; return new Response(null, { status: 204 }) }
  enviados = []
  responder428 = false
  olvidarCsrf()
  olvidarCaches()
  localStorage.clear()
  localStorage.setItem('zass-sesion', '1')
  vi.stubGlobal('fetch', vi.fn(async (entrada: RequestInfo | URL, opciones?: RequestInit) => {
    const ruta = String(entrada)
    const metodo = opciones?.method ?? 'GET'
    if (ruta.includes('/api/auth/csrf')) return json({ token: 't' })
    if (ruta.includes('/api/mi/cuenta/datos')) {
      enviados.push({ ruta, metodo, cuerpo: JSON.parse(String(opciones?.body ?? '{}')) })
      return alCompletar()
    }
    if (ruta.includes('/api/mi/cuenta')) return json({ ...CUENTA, datosPendientes: pendientes })
    if (responder428 && ruta.includes('/api/mi/')) {
      // Desde ESTE momento la cuenta tiene pendientes: si la pantalla aparece, es porque el
      // 428 hizo recargar la sesión, no porque ya los trajera al cargar.
      cuentaAntesDel428 += 1
      pendientes = ['rut', 'telefono', 'mayorDeEdad']
      return json({ title: 'Completa los datos de tu cuenta', codigo: 'datos_de_cuenta_pendientes' }, 428)
    }
    return json([])
  }))
})

function pintar(ruta = '/mis-citas') {
  return render(
    <MemoryRouter initialEntries={[ruta]}>
      <ProveedorDeSesion>
        <App />
      </ProveedorDeSesion>
    </MemoryRouter>,
  )
}

const TITULO = 'Completa los datos de tu cuenta'

describe('faltanDatos', () => {
  it('solo cuando la API manda pendientes', () => {
    expect(faltanDatos(['rut'])).toBe(true)
    expect(faltanDatos([])).toBe(false)
    expect(faltanDatos(undefined)).toBe(false)   // una API vieja no lo manda: nadie queda bloqueado
  })
})

describe('Completa los datos de tu cuenta (ES-2)', () => {
  it('con datos pendientes va en lugar de la página y no se puede saltar', async () => {
    pintar('/mis-citas')

    expect(await screen.findByRole('heading', { name: TITULO })).toBeInTheDocument()
    expect(screen.queryByRole('heading', { name: /mis citas/i })).toBeNull()
  })

  it('aparece en cualquier ruta, no solo en una', async () => {
    pintar('/ofrecer-servicios')
    expect(await screen.findByRole('heading', { name: TITULO })).toBeInTheDocument()
  })

  it('los textos legales siguen abiertos', async () => {
    pintar('/terminos')
    await waitFor(() => expect(localStorage.getItem('zass-sesion')).toBe('1'))
    // Se espera a que cargue la sesión antes de afirmar que NO aparece.
    await screen.findAllByText(/términos/i)
    expect(screen.queryByRole('heading', { name: TITULO })).toBeNull()
  })

  it('sin pendientes no aparece', async () => {
    pendientes = []
    pintar('/terminos')
    await screen.findAllByText(/términos/i)
    expect(screen.queryByRole('heading', { name: TITULO })).toBeNull()
  })

  it('revisa con los mismos mensajes del registro y no envía nada malo', async () => {
    pintar()
    await screen.findByRole('heading', { name: TITULO })

    await userEvent.type(screen.getByLabelText('RUT'), '11.111.111-2')
    await userEvent.click(screen.getByRole('button', { name: 'Guardar y seguir' }))

    expect(screen.getByText('Ese RUT no es válido. Revisa el número y el dígito verificador.')).toBeInTheDocument()
    expect(screen.getByText('Escribe tu apellido.')).toBeInTheDocument()
    expect(screen.getByText('Escribe un teléfono.')).toBeInTheDocument()
    expect(screen.getByText('Para crear una cuenta tienes que ser mayor de 18 años.')).toBeInTheDocument()
    expect(enviados).toHaveLength(0)
  })

  it('al guardar manda los datos a la API y vuelve la página', async () => {
    pintar()
    await screen.findByRole('heading', { name: TITULO })

    await userEvent.type(screen.getByLabelText('RUT'), '11.111.111-1')
    await userEvent.type(screen.getByLabelText('Apellido'), 'Rojas')
    await userEvent.type(screen.getByLabelText('Teléfono'), '+56 9 1234 5678')
    await userEvent.click(screen.getByLabelText('Soy mayor de 18 años'))
    await userEvent.click(screen.getByRole('button', { name: 'Guardar y seguir' }))

    await waitFor(() => expect(screen.queryByRole('heading', { name: TITULO })).toBeNull())
    expect(enviados).toEqual([{
      ruta: expect.stringContaining('/api/mi/cuenta/datos'), metodo: 'PUT',
      cuerpo: { tipo: 'particular', apellido: 'Rojas', rut: '11.111.111-1', telefono: '+56 9 1234 5678', mayorDeEdad: true },
    }])
  })

  it('una empresa manda su razón social y no un apellido', async () => {
    pintar()
    await screen.findByRole('heading', { name: TITULO })

    await userEvent.click(screen.getByRole('button', { name: 'Una empresa' }))
    await userEvent.type(screen.getByLabelText('Razón social'), 'Gasfitería Rojas SpA')
    await userEvent.type(screen.getByLabelText('RUT de la empresa'), '76.086.428-5')
    await userEvent.type(screen.getByLabelText('Teléfono'), '+56 9 1234 5678')
    await userEvent.click(screen.getByLabelText('Soy mayor de 18 años y represento a esta empresa'))
    await userEvent.click(screen.getByRole('button', { name: 'Guardar y seguir' }))

    await waitFor(() => expect(enviados).toHaveLength(1))
    expect(enviados[0].cuerpo).toEqual({
      tipo: 'empresa', razonSocial: 'Gasfitería Rojas SpA', rut: '76.086.428-5', telefono: '+56 9 1234 5678', mayorDeEdad: true,
    })
  })

  it('si el RUT ya lo usa otra cuenta, muestra el mensaje de la API y sigue pidiendo los datos', async () => {
    alCompletar = () => json(
      { title: 'Conflicto', detail: 'Ya existe una cuenta con ese RUT. Si es tuya, entra con tu correo.' }, 409)
    pintar()
    await screen.findByRole('heading', { name: TITULO })

    await userEvent.type(screen.getByLabelText('RUT'), '11.111.111-1')
    await userEvent.type(screen.getByLabelText('Apellido'), 'Rojas')
    await userEvent.type(screen.getByLabelText('Teléfono'), '+56 9 1234 5678')
    await userEvent.click(screen.getByLabelText('Soy mayor de 18 años'))
    await userEvent.click(screen.getByRole('button', { name: 'Guardar y seguir' }))

    expect(await screen.findByText(/Ya existe una cuenta con ese RUT/)).toBeInTheDocument()
    expect(screen.getByRole('heading', { name: TITULO })).toBeInTheDocument()
  })

  it('un 428 de la API hace aparecer la pantalla aunque la sesión se cargara sin pendientes', async () => {
    // La sesión se carga «completa» (por ejemplo, antes del despliegue) y la API dice que no:
    // Mis citas pide datos a /api/mi/..., recibe 428, y la sesión se recarga, ahora con pendientes.
    pendientes = []
    responder428 = true
    pintar('/mis-citas')

    expect(await screen.findByRole('heading', { name: TITULO })).toBeInTheDocument()
    expect(cuentaAntesDel428).toBeGreaterThan(0)
  })

  it('se puede cerrar la sesión desde la pantalla', async () => {
    pintar()
    await screen.findByRole('heading', { name: TITULO })

    await userEvent.click(screen.getByRole('button', { name: 'Cerrar sesión' }))

    await waitFor(() => expect(screen.queryByRole('heading', { name: TITULO })).toBeNull())
    expect(localStorage.getItem('zass-sesion')).toBeNull()
  })
})
