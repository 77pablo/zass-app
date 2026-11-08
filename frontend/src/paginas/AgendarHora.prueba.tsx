import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router-dom'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { olvidarCsrf } from '../api/cliente'
import { Contexto, type EstadoDeSesion, type Usuario } from '../sesion/contexto'
import { AgendarHora } from './AgendarHora'

type Servicio = Parameters<typeof AgendarHora>[0]['servicios'][number]

const SERVICIO: Servicio = {
  id: 'svc-1', nombre: 'Consulta inicial', duracionMinutos: 30, precioReferencia: null, modoAgenda: 'Automatico',
}
const MANUAL: Servicio = { ...SERVICIO, id: 'svc-2', nombre: 'Tasación en terreno', modoAgenda: 'Manual' }

// 12:00 UTC de noviembre = 09:00 en Chile; 13:00 UTC = 10:00; 15:00 UTC = 12:00. Un lunes y un martes,
// con horas distintas para que cada boton se pueda encontrar por su nombre.
const BLOQUES = [
  { id: 'b1', inicioEn: '2026-11-09T12:00:00Z', finEn: '2026-11-09T12:30:00Z', estado: 'libre' },
  { id: 'b2', inicioEn: '2026-11-09T13:00:00Z', finEn: '2026-11-09T13:30:00Z', estado: 'libre' },
  { id: 'b3', inicioEn: '2026-11-09T14:00:00Z', finEn: '2026-11-09T14:30:00Z', estado: 'ocupado' },
  { id: 'b4', inicioEn: '2026-11-10T15:00:00Z', finEn: '2026-11-10T15:30:00Z', estado: 'libre' },   // martes 12:00
]

let llamadas: { ruta: string; metodo?: string; cabeceras: Record<string, string>; cuerpo?: unknown }[]
let alAgendar: () => Response

function json(cuerpo: unknown, estado = 200) {
  return new Response(JSON.stringify(cuerpo), { status: estado, headers: { 'content-type': 'application/json' } })
}

const CITA = {
  id: 'c1', codigo: 'ZS-4K7M', estado: 'Confirmada', modo: 'Automatico',
  inicioEn: '2026-11-09T12:00:00Z', finEn: '2026-11-09T12:30:00Z', servicio: 'Consulta inicial',
  prestadorServicioId: 'svc-1', perfilPrestadorId: 'p1', prestador: 'Pablo Espinoza',
  notaBuscador: null, expiraEn: null, nueva: true,
}

beforeEach(() => {
  llamadas = []
  alAgendar = () => json(CITA, 201)
  olvidarCsrf()
  vi.stubGlobal('fetch', vi.fn(async (ruta: string, opciones?: RequestInit) => {
    if (ruta === '/api/auth/csrf') return json({ token: 't' })
    llamadas.push({
      ruta, metodo: opciones?.method,
      cabeceras: (opciones?.headers ?? {}) as Record<string, string>,
      cuerpo: opciones?.body ? JSON.parse(String(opciones.body)) : undefined,
    })
    if (ruta.includes('/bloques')) {
      return json({
        prestadorServicioId: 'svc-1', perfilPrestadorId: 'p1', servicio: 'Consulta inicial',
        modoAgenda: 'Automatico', duracionMinutos: 30, anticipacionMinimaHoras: 2, bloques: BLOQUES,
      })
    }
    return alAgendar()
  }))
})

const USUARIO: Usuario = { id: 'u1', correo: 'a@b.cl', nombre: 'Ana', correoVerificado: true, roles: ['buscador'] }

function pintar(opciones: {
  sesion?: EstadoDeSesion; usuario?: Usuario | null; servicios?: Servicio[]
  elegido?: { servicio: string | null; bloque: string | null }
} = {}) {
  const { sesion = 'dentro', usuario = USUARIO, servicios = [SERVICIO], elegido } = opciones
  return render(
    <MemoryRouter>
      <Contexto.Provider value={{ estado: sesion, usuario, recargar: async () => {}, salir: async () => {} }}>
        <AgendarHora slug="estudio-espinoza" nombre="Pablo Espinoza" servicios={servicios} elegidoInicial={elegido} />
      </Contexto.Provider>
    </MemoryRouter>,
  )
}

describe('el calendario', () => {
  it('muestra solo las horas libres, agrupadas por dia y en hora de Chile', async () => {
    pintar()

    expect(await screen.findByRole('button', { name: '09:00' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: '10:00' })).toBeInTheDocument()
    // b3 esta ocupado: no se ofrece. Y no se dice con quien (CU-12 paso 3).
    expect(screen.queryByRole('button', { name: '11:00' })).not.toBeInTheDocument()
    expect(screen.getByText(/lunes, 9 de noviembre/i)).toBeInTheDocument()
    expect(screen.getByText(/martes, 10 de noviembre/i)).toBeInTheDocument()
  })

  it('con un solo servicio no hace elegirlo: ya muestra las horas', async () => {
    pintar()

    await screen.findByRole('button', { name: '09:00' })
    expect(screen.queryByRole('group', { name: 'Servicio' })).not.toBeInTheDocument()
  })

  it('con varios, pide elegir el servicio antes de mostrar horas', async () => {
    pintar({ servicios: [SERVICIO, MANUAL] })

    expect(screen.getByText(/Elige un servicio/)).toBeInTheDocument()
    expect(llamadas.some((l) => l.ruta.includes('/bloques'))).toBe(false)
  })

  it('avisa ANTES de elegir hora que el servicio manual espera confirmacion (flujo 4C)', async () => {
    pintar({ servicios: [SERVICIO, MANUAL] })

    await userEvent.click(screen.getByRole('button', { name: /Tasación en terreno/ }))

    expect(await screen.findByRole('note')).toHaveTextContent(/confirma cada hora a mano/)
    expect(screen.getByRole('note')).toHaveTextContent(/24 horas/)
  })
})

describe('agendar', () => {
  it('manda la hora elegida con la clave en la cabecera y confirma con el codigo', async () => {
    pintar()

    await userEvent.click(await screen.findByRole('button', { name: '09:00' }))
    await userEvent.click(screen.getByRole('button', { name: 'Confirmar esta hora' }))

    expect(await screen.findByText('Tu hora está confirmada')).toBeInTheDocument()
    expect(screen.getByText('ZS-4K7M')).toBeInTheDocument()

    const envio = llamadas.find((l) => l.ruta === '/api/citas')!
    expect(envio.cuerpo).toMatchObject({ bloqueId: 'b1' })
    expect(envio.cabeceras['Idempotency-Key']).toMatch(/^[0-9a-f-]{36}$/)
  })

  /**
   * CC-04 desde la pantalla: un fallo de red y el reintento manual tienen que llevar la MISMA
   * clave, porque son el mismo envio. Si cada clic generara una, el servidor crearia dos citas.
   */
  it('reintentar tras un fallo reusa la misma clave', async () => {
    alAgendar = () => json({ title: 'El servidor tuvo un problema.' }, 500)
    pintar()

    await userEvent.click(await screen.findByRole('button', { name: '09:00' }))
    await userEvent.click(screen.getByRole('button', { name: 'Confirmar esta hora' }))
    await screen.findByRole('alert')
    await userEvent.click(screen.getByRole('button', { name: 'Confirmar esta hora' }))

    const claves = llamadas.filter((l) => l.ruta === '/api/citas').map((l) => l.cabeceras['Idempotency-Key'])
    expect(claves).toHaveLength(2)
    expect(claves[0]).toBe(claves[1])
  })

  it('elegir OTRA hora es otra intencion y cambia la clave', async () => {
    alAgendar = () => json({ title: 'caida' }, 500)
    pintar()

    await userEvent.click(await screen.findByRole('button', { name: '09:00' }))
    await userEvent.click(screen.getByRole('button', { name: 'Confirmar esta hora' }))
    await screen.findByRole('alert')
    await userEvent.click(screen.getByRole('button', { name: '10:00' }))
    await userEvent.click(screen.getByRole('button', { name: 'Confirmar esta hora' }))
    await waitFor(() => expect(llamadas.filter((l) => l.ruta === '/api/citas')).toHaveLength(2))

    const claves = llamadas.filter((l) => l.ruta === '/api/citas').map((l) => l.cabeceras['Idempotency-Key'])
    expect(claves[0]).not.toBe(claves[1])
  })

  /** Flujo 4A: alguien se adelanto. Se avisa, se suelta la eleccion y se recarga el calendario. */
  it('si la hora ya la tomaron, lo dice, suelta la eleccion y recarga el calendario', async () => {
    alAgendar = () => json({ title: 'Conflicto', detail: 'Alguien tomó esa hora recién. Elige otra, por favor.' }, 409)
    pintar()

    await userEvent.click(await screen.findByRole('button', { name: '09:00' }))
    await userEvent.click(screen.getByRole('button', { name: 'Confirmar esta hora' }))

    expect(await screen.findByRole('alert')).toHaveTextContent(/Alguien tomó esa hora/)
    expect(screen.queryByRole('button', { name: 'Confirmar esta hora' })).not.toBeInTheDocument()
    await waitFor(() => expect(llamadas.filter((l) => l.ruta.includes('/bloques')).length).toBeGreaterThan(1))
  })

  it('una solicitud manual dice que queda pendiente, no que esta confirmada', async () => {
    alAgendar = () => json({ ...CITA, estado: 'Solicitada', modo: 'Manual', expiraEn: '2026-11-10T12:00:00Z' }, 201)
    pintar({ servicios: [MANUAL] })

    await userEvent.click(await screen.findByRole('button', { name: '09:00' }))
    await userEvent.click(screen.getByRole('button', { name: 'Pedir esta hora' }))

    expect(await screen.findByText('Pedimos tu hora')).toBeInTheDocument()
    expect(screen.getByText(/Si no responde en 24 horas/)).toBeInTheDocument()
  })
})

describe('sin sesion o sin correo verificado', () => {
  /** Flujo 4G: la ruta de regreso lleva el servicio y la hora, para retomar en confirmar. */
  it('manda a entrar o a crear cuenta con la hora elegida en la direccion de regreso', async () => {
    pintar({ sesion: 'fuera', usuario: null })

    await userEvent.click(await screen.findByRole('button', { name: '09:00' }))

    const entrar = screen.getByRole('link', { name: 'Entrar' })
    expect(entrar).toHaveAttribute('href', '/entrar')
    expect(screen.getByRole('link', { name: 'crear mi cuenta' })).toHaveAttribute('href', '/crear-cuenta')
    expect(screen.queryByRole('button', { name: /Confirmar esta hora/ })).not.toBeInTheDocument()
  })

  it('vuelve con la hora ya elegida cuando la direccion trae servicio y bloque', async () => {
    pintar({ elegido: { servicio: 'svc-1', bloque: 'b2' } })

    expect(await screen.findByRole('button', { name: 'Confirmar esta hora' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: '10:00', pressed: true })).toBeInTheDocument()
  })

  it('con el correo sin verificar no deja confirmar y dice por que', async () => {
    pintar({ usuario: { ...USUARIO, correoVerificado: false } })

    await userEvent.click(await screen.findByRole('button', { name: '09:00' }))

    expect(screen.getByRole('note')).toHaveTextContent(/Verifica tu correo/)
    expect(screen.queryByRole('button', { name: /Confirmar esta hora/ })).not.toBeInTheDocument()
  })
})
