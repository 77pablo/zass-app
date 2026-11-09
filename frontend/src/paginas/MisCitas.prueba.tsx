import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { olvidarCsrf } from '../api/cliente'
import { Contexto, type EstadoDeSesion, type Usuario } from '../sesion/contexto'
import { MisCitas } from './MisCitas'

function json(cuerpo: unknown, estado = 200) {
  return new Response(JSON.stringify(cuerpo), { status: estado, headers: { 'content-type': 'application/json' } })
}

const MI_PERFIL = 'perfil-mio'

function cita(extra: Record<string, unknown>) {
  return {
    id: 'c', codigo: 'ZS-0001', estado: 'Confirmada', modo: 'Automatico',
    inicioEn: '2026-11-20T13:00:00Z', finEn: '2026-11-20T13:30:00Z', servicio: 'Consulta inicial',
    prestadorServicioId: 's', perfilPrestadorId: 'perfil-otro', prestador: 'Estudio Espinoza',
    notaBuscador: null, expiraEn: null, nueva: false, ...extra,
  }
}

let citas: unknown[]
let llamadas: { ruta: string; cabeceras: Record<string, string>; cuerpo?: unknown }[]
let alAccionar: () => Response

beforeEach(() => {
  citas = []
  llamadas = []
  alAccionar = () => json({}, 200)
  olvidarCsrf()
  vi.stubGlobal('fetch', vi.fn(async (ruta: string, opciones?: RequestInit) => {
    if (ruta === '/api/auth/csrf') return json({ token: 't' })
    if (ruta === '/api/mi/citas') return json(citas)
    if (ruta === '/api/mi/perfil-prestador') return json({ id: MI_PERFIL })
    llamadas.push({
      ruta, cabeceras: (opciones?.headers ?? {}) as Record<string, string>,
      cuerpo: opciones?.body ? JSON.parse(String(opciones.body)) : undefined,
    })
    return alAccionar()
  }))
})

function pintar(sesion: EstadoDeSesion = 'dentro', roles = ['buscador']) {
  const usuario: Usuario | null = sesion === 'dentro'
    ? { id: 'u', correo: 'a@b.cl', nombre: 'Ana', correoVerificado: true, roles } : null
  return render(
    <MemoryRouter initialEntries={['/mis-citas']}>
      <Contexto.Provider value={{ estado: sesion, usuario, recargar: async () => {}, salir: async () => {} }}>
        <Routes>
          <Route path="/mis-citas" element={<MisCitas />} />
          <Route path="/entrar" element={<p>pantalla de entrar</p>} />
        </Routes>
      </Contexto.Provider>
    </MemoryRouter>,
  )
}

describe('mis citas', () => {
  it('sin sesion manda a entrar', async () => {
    pintar('fuera')

    expect(await screen.findByText('pantalla de entrar')).toBeInTheDocument()
  })

  it('quien ofrece servicios ve el acceso a su agenda; quien solo busca, no', async () => {
    const { unmount } = pintar('dentro', ['buscador', 'prestador'])
    expect(await screen.findByRole('link', { name: /mi agenda/i })).toHaveAttribute('href', '/mi-agenda')
    unmount()

    pintar('dentro', ['buscador'])
    await screen.findByRole('heading', { name: /mis citas/i })
    expect(screen.queryByRole('link', { name: /mi agenda/i })).not.toBeInTheDocument()
  })

  it('sin citas invita a buscar', async () => {
    pintar()

    expect(await screen.findByText(/Todavía no tienes citas/)).toBeInTheDocument()
  })

  it('separa proximas de anteriores y muestra la hora en Chile con el codigo', async () => {
    citas = [
      cita({ id: 'a', codigo: 'ZS-AAAA' }),
      cita({ id: 'b', codigo: 'ZS-BBBB', estado: 'Cancelada' }),
    ]
    pintar()

    const proximas = (await screen.findByRole('heading', { name: 'Próximas' })).closest('section')!
    expect(within(proximas).getByText('ZS-AAAA')).toBeInTheDocument()
    expect(within(proximas).getByText(/10:00 h/)).toBeInTheDocument()   // 13:00 UTC en noviembre
    const anteriores = screen.getByRole('heading', { name: 'Anteriores' }).closest('section')!
    expect(within(anteriores).getByText('ZS-BBBB')).toBeInTheDocument()
    expect(within(anteriores).getByText('Cancelada')).toBeInTheDocument()
  })
})

describe('cada lado ve lo suyo', () => {
  it('quien pidio la hora puede cancelarla pero NO confirmarla ni marcar asistencia', async () => {
    citas = [cita({ id: 'a', estado: 'Solicitada' })]
    pintar()

    await screen.findByText('Esperando confirmación')
    expect(screen.getByRole('button', { name: 'Cancelar' })).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Confirmar' })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Rechazar' })).not.toBeInTheDocument()
  })

  /** El prestador responde las solicitudes: es lo que hace que el modo manual tenga sentido. */
  it('el prestador ve Confirmar y Rechazar en una solicitud que le hicieron', async () => {
    citas = [cita({ id: 'a', estado: 'Solicitada', perfilPrestadorId: MI_PERFIL })]
    pintar('dentro', ['prestador'])

    expect(await screen.findByRole('button', { name: 'Confirmar' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Rechazar' })).toBeInTheDocument()
    expect(screen.getByText('Te pidieron esta hora')).toBeInTheDocument()
  })

  it('el prestador marca asistencia solo en las que ya pasaron la hora', async () => {
    citas = [cita({ id: 'a', estado: 'PendienteAsistencia', perfilPrestadorId: MI_PERFIL })]
    pintar('dentro', ['prestador'])

    expect(await screen.findByRole('heading', { name: 'Por marcar asistencia' })).toBeInTheDocument()
    await userEvent.click(screen.getByRole('button', { name: 'Asistió' }))

    await waitFor(() => expect(llamadas.some((l) => l.ruta === '/api/citas/a/asistencia')).toBe(true))
    expect(llamadas.find((l) => l.ruta === '/api/citas/a/asistencia')!.cuerpo).toEqual({ asistio: true })
  })

  /** Quien pidio la hora no marca su propia asistencia (la defensa de CU-16, tambien en pantalla). */
  it('el buscador no ve los botones de asistencia en su propia cita', async () => {
    citas = [cita({ id: 'a', estado: 'PendienteAsistencia' })]
    pintar()

    await screen.findByText('Por marcar asistencia')
    expect(screen.queryByRole('button', { name: 'Asistió' })).not.toBeInTheDocument()
  })

  it('una cita terminada no ofrece acciones', async () => {
    citas = [cita({ id: 'a', estado: 'Realizada' })]
    pintar()

    await screen.findByText('Realizada')
    expect(screen.queryByRole('button')).not.toBeInTheDocument()
  })
})

describe('cancelar', () => {
  it('manda el motivo y la clave de idempotencia en la cabecera', async () => {
    citas = [cita({ id: 'a' })]
    pintar()

    await userEvent.click(await screen.findByRole('button', { name: 'Cancelar' }))
    await userEvent.type(screen.getByRole('textbox'), 'Me surgió un viaje')
    await userEvent.click(screen.getByRole('button', { name: 'Cancelar la hora' }))

    await waitFor(() => expect(llamadas).toHaveLength(1))
    expect(llamadas[0].ruta).toBe('/api/citas/a/cancelar')
    expect(llamadas[0].cuerpo).toEqual({ motivo: 'Me surgió un viaje' })
    expect(llamadas[0].cabeceras['Idempotency-Key']).toMatch(/^[0-9a-f-]{36}$/)
  })

  /** CC-04: tras un fallo de red el reintento es el MISMO envio y lleva la misma clave. */
  it('reintentar tras un fallo reusa la misma clave', async () => {
    citas = [cita({ id: 'a' })]
    alAccionar = () => json({ title: 'caida' }, 500)
    pintar()

    await userEvent.click(await screen.findByRole('button', { name: 'Cancelar' }))
    await userEvent.click(screen.getByRole('button', { name: 'Cancelar la hora' }))
    await screen.findByRole('alert')
    await userEvent.click(screen.getByRole('button', { name: 'Cancelar la hora' }))

    await waitFor(() => expect(llamadas).toHaveLength(2))
    expect(llamadas[0].cabeceras['Idempotency-Key']).toBe(llamadas[1].cabeceras['Idempotency-Key'])
  })

  /** RN-S4-04: con menos de 12 h el servidor exige motivo; el mensaje tiene que llegar. */
  it('muestra el mensaje del servidor cuando falta el motivo de una cancelacion tardia', async () => {
    citas = [cita({ id: 'a' })]
    alAccionar = () => json({ title: 'Regla', detail: 'Faltan menos de 12 horas: escribe el motivo de la cancelación.' }, 422)
    pintar()

    await userEvent.click(await screen.findByRole('button', { name: 'Cancelar' }))
    await userEvent.click(screen.getByRole('button', { name: 'Cancelar la hora' }))

    expect(await screen.findByRole('alert')).toHaveTextContent(/Faltan menos de 12 horas/)
  })

  it('al terminar bien recarga la lista', async () => {
    citas = [cita({ id: 'a' })]
    pintar()
    await userEvent.click(await screen.findByRole('button', { name: 'Cancelar' }))
    citas = [cita({ id: 'a', estado: 'Cancelada' })]
    await userEvent.click(screen.getByRole('button', { name: 'Cancelar la hora' }))

    expect(await screen.findByRole('heading', { name: 'Anteriores' })).toBeInTheDocument()
  })
})
