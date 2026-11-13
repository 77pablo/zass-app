import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { olvidarCsrf } from '../api/cliente'
import { Contexto, type EstadoDeSesion, type Usuario } from '../sesion/contexto'
import { MiAgenda } from './MiAgenda'

function json(cuerpo: unknown, estado = 200) {
  return new Response(JSON.stringify(cuerpo), { status: estado, headers: { 'content-type': 'application/json' } })
}

const CORTE = {
  prestadorServicioId: 'ps-corte', servicio: 'Corte de pelo', modoAgenda: 'Automatico',
  franjas: [{ id: 'f1', diaSemana: 1, horaInicio: '09:00:00', horaFin: '13:00:00', duracionBloqueMin: 30 }],
}
const TINTURA = { prestadorServicioId: 'ps-tintura', servicio: 'Tintura', modoAgenda: 'Manual', franjas: [] }

let disponibilidad: unknown[]
let llamadas: { ruta: string; metodo: string; cuerpo?: unknown }[]
let alGuardar: () => Response
let alBloquear: () => Response
let bloqueos: unknown[]
let alQuitar: () => Response

beforeEach(() => {
  disponibilidad = [CORTE, TINTURA]
  llamadas = []
  alGuardar = () => json({ bloquesGenerados: 16 })
  alBloquear = () => json({ id: 'b1', desdeEn: '', hastaEn: '', motivo: null }, 201)
  bloqueos = [{ id: 'b1', desdeEn: '2026-12-24T03:00:00.000Z', hastaEn: '2026-12-26T03:00:00.000Z', motivo: 'Navidad' }]
  alQuitar = () => new Response(null, { status: 204 })
  olvidarCsrf()
  vi.stubGlobal('fetch', vi.fn(async (ruta: string, opciones?: RequestInit) => {
    if (ruta === '/api/auth/csrf') return json({ token: 't' })
    llamadas.push({
      ruta, metodo: opciones?.method ?? 'GET',
      cuerpo: opciones?.body ? JSON.parse(String(opciones.body)) : undefined,
    })
    if (ruta === '/api/mi/disponibilidad' && (opciones?.method ?? 'GET') === 'GET') return json(disponibilidad)
    if (ruta === '/api/mi/disponibilidad') return alGuardar()
    if (ruta === '/api/mi/bloqueos' && (opciones?.method ?? 'GET') === 'GET') return json(bloqueos)
    if (ruta.startsWith('/api/mi/bloqueos/') && (opciones?.method ?? 'GET') === 'DELETE') return alQuitar()
    if (ruta === '/api/mi/bloqueos') return alBloquear()
    return json({}, 404)
  }))
})

function pintar(sesion: EstadoDeSesion = 'dentro', roles = ['buscador', 'prestador']) {
  const usuario: Usuario | null = sesion === 'dentro'
    ? { id: 'u', correo: 'a@b.cl', nombre: 'Ana', correoVerificado: true, roles } : null
  return render(
    <MemoryRouter initialEntries={['/mi-agenda']}>
      <Contexto.Provider value={{ estado: sesion, usuario, recargar: async () => {}, salir: async () => {} }}>
        <Routes>
          <Route path="/mi-agenda" element={<MiAgenda />} />
          <Route path="/entrar" element={<p>pantalla de entrar</p>} />
        </Routes>
      </Contexto.Provider>
    </MemoryRouter>,
  )
}

const tarjeta = (servicio: string) => screen.getByRole('region', { name: servicio })
const guardados = () => llamadas.filter((l) => l.metodo === 'PUT')
const creados = () => llamadas.filter((l) => l.ruta === '/api/mi/bloqueos' && l.metodo === 'POST')
const borrados = () => llamadas.filter((l) => l.ruta.startsWith('/api/mi/bloqueos/') && l.metodo === 'DELETE')

describe('mi agenda (prestador)', () => {
  it('sin sesion manda a entrar', async () => {
    pintar('fuera')

    expect(await screen.findByText('pantalla de entrar')).toBeInTheDocument()
    expect(llamadas).toEqual([])
  })

  it('sin ficha de prestador ofrece activarla y no pide nada a la API', () => {
    pintar('dentro', ['buscador'])

    expect(screen.getByRole('link', { name: /ofrecer mis servicios/i })).toHaveAttribute('href', '/ofrecer-servicios')
    expect(llamadas).toEqual([])
  })

  it('lista cada servicio con su horario, y dice cuando uno aun no tiene horas', async () => {
    pintar()

    await screen.findByRole('region', { name: 'Corte de pelo' })
    // Las horas se editan como hora local de Chile, sin segundos.
    expect(within(tarjeta('Corte de pelo')).getByLabelText('Desde, franja 1')).toHaveValue('09:00')
    expect(within(tarjeta('Corte de pelo')).getByLabelText('Hasta, franja 1')).toHaveValue('13:00')
    expect(within(tarjeta('Tintura')).getByText(/todavía no tiene horario/i)).toBeInTheDocument()
    expect(within(tarjeta('Tintura')).getByText(/confirmas tú cada hora/i)).toBeInTheDocument()
  })

  it('agrega una franja y la guarda con las horas como HH:mm:ss y el id de ESE servicio', async () => {
    pintar()
    await screen.findByRole('region', { name: 'Tintura' })
    const t = tarjeta('Tintura')

    await userEvent.click(within(t).getByRole('button', { name: /agregar horario/i }))
    await userEvent.selectOptions(within(t).getByLabelText('Día, franja 1'), '3')
    await userEvent.clear(within(t).getByLabelText('Desde, franja 1'))
    await userEvent.type(within(t).getByLabelText('Desde, franja 1'), '10:00')
    await userEvent.clear(within(t).getByLabelText('Hasta, franja 1'))
    await userEvent.type(within(t).getByLabelText('Hasta, franja 1'), '12:00')
    await userEvent.selectOptions(within(t).getByLabelText('Duración de cada hora, franja 1'), '60')
    await userEvent.click(within(t).getByRole('button', { name: /guardar horario/i }))

    await waitFor(() => expect(guardados()).toHaveLength(1))
    expect(guardados()[0].cuerpo).toEqual({
      prestadorServicioId: 'ps-tintura',
      franjas: [{ diaSemana: 3, horaInicio: '10:00:00', horaFin: '12:00:00', duracionBloqueMin: 60 }],
    })
    expect(await within(t).findByText(/16 horas/i)).toBeInTheDocument()
    // El otro servicio no se toco.
    expect(guardados().every((g) => (g.cuerpo as { prestadorServicioId: string }).prestadorServicioId === 'ps-tintura')).toBe(true)
  })

  /**
   * El texto VISIBLE de los dos botones de quitar, no solo su aria-label. Las demas pruebas los
   * buscan por aria-label, y cuando un boton tiene aria-label ese pasa a ser su nombre accesible:
   * el texto de dentro deja de comprobarse. Se descubrio mutandolo (quedaba en «Quitar» y las 19
   * pruebas seguian verdes). «Quitar este horario» y no «Quitar horario» porque lo segundo se lee
   * como borrar el horario entero, y lo que se quita es solo esa fila.
   */
  it('los botones de quitar dicen en pantalla de QUE se quitan, no solo «Quitar»', async () => {
    bloqueos = [{ id: 'b1', desdeEn: '2027-01-01T03:00:00.000Z', hastaEn: '2027-01-02T03:00:00.000Z', motivo: null }]
    pintar()
    await screen.findByRole('region', { name: 'Corte de pelo' })

    const t = tarjeta('Corte de pelo')
    expect(within(t).getByRole('button', { name: /^Quitar este horario 1/ })).toHaveTextContent('Quitar este horario')

    const b = await screen.findByRole('region', { name: /bloquear fechas/i })
    await within(b).findByText('El 01-01-2027')
    expect(within(b).getByRole('button', { name: /^Quitar este bloqueo 1/ })).toHaveTextContent('Quitar este bloqueo')
  })

  it('quitar la ultima franja y guardar manda la lista vacia: asi se quitan todas las horas', async () => {
    pintar()
    await screen.findByRole('region', { name: 'Corte de pelo' })
    const t = tarjeta('Corte de pelo')

    await userEvent.click(within(t).getByRole('button', { name: 'Quitar este horario 1 (lunes de 09:00 a 13:00)' }))
    await userEvent.click(within(t).getByRole('button', { name: /guardar horario/i }))

    await waitFor(() => expect(guardados()).toHaveLength(1))
    expect(guardados()[0].cuerpo).toEqual({ prestadorServicioId: 'ps-corte', franjas: [] })
  })

  it('no llama a la API si el termino no es posterior al inicio, y lo dice junto a la franja', async () => {
    pintar()
    await screen.findByRole('region', { name: 'Corte de pelo' })
    const t = tarjeta('Corte de pelo')

    await userEvent.clear(within(t).getByLabelText('Hasta, franja 1'))
    await userEvent.type(within(t).getByLabelText('Hasta, franja 1'), '08:00')
    await userEvent.click(within(t).getByRole('button', { name: /guardar horario/i }))

    expect(guardados()).toHaveLength(0)
    expect(await within(t).findByText(/término tiene que ser posterior/i)).toBeInTheDocument()
  })

  it('no llama a la API si la franja es mas corta que una hora de atencion', async () => {
    pintar()
    await screen.findByRole('region', { name: 'Corte de pelo' })
    const t = tarjeta('Corte de pelo')

    await userEvent.selectOptions(within(t).getByLabelText('Duración de cada hora, franja 1'), '120')
    await userEvent.clear(within(t).getByLabelText('Hasta, franja 1'))
    await userEvent.type(within(t).getByLabelText('Hasta, franja 1'), '10:00')
    await userEvent.click(within(t).getByRole('button', { name: /guardar horario/i }))

    expect(guardados()).toHaveLength(0)
    expect(await within(t).findByText(/más corta que una hora/i)).toBeInTheDocument()
  })

  it('si el servidor rechaza, conserva lo escrito, lo dice y deja reintentar', async () => {
    alGuardar = () => json({ title: 'Conflicto', detail: 'Hay citas en esas horas.' }, 409)
    pintar()
    await screen.findByRole('region', { name: 'Corte de pelo' })
    const t = tarjeta('Corte de pelo')

    await userEvent.clear(within(t).getByLabelText('Hasta, franja 1'))
    await userEvent.type(within(t).getByLabelText('Hasta, franja 1'), '14:00')
    await userEvent.click(within(t).getByRole('button', { name: /guardar horario/i }))

    expect(await within(t).findByRole('alert')).toHaveTextContent(/hay citas en esas horas/i)
    expect(within(t).getByLabelText('Hasta, franja 1')).toHaveValue('14:00')   // no se perdio lo escrito
    await waitFor(() => expect(within(t).getByRole('button', { name: /guardar horario/i })).toBeEnabled())

    alGuardar = () => json({ bloquesGenerados: 20 })
    await userEvent.click(within(t).getByRole('button', { name: /guardar horario/i }))
    expect(await within(t).findByText(/20 horas/i)).toBeInTheDocument()
  })

  it('bloquea dias completos mandando el inicio del primero y el inicio del dia SIGUIENTE al ultimo', async () => {
    pintar()
    await screen.findByRole('region', { name: 'Corte de pelo' })
    const b = screen.getByRole('region', { name: /bloquear fechas/i })

    await userEvent.type(within(b).getByLabelText('Desde el día'), '2026-12-24')
    await userEvent.type(within(b).getByLabelText('Hasta el día'), '2026-12-25')
    await userEvent.type(within(b).getByLabelText(/motivo/i), 'Navidad')
    await userEvent.click(within(b).getByRole('button', { name: /bloquear/i }))

    await waitFor(() => expect(creados()).toHaveLength(1))
    const envio = creados()[0]
    // Verano en Chile = UTC-3: el 24 empieza a las 03:00Z y el bloqueo llega hasta el inicio del 26.
    expect(envio.cuerpo).toEqual({
      desdeEn: '2026-12-24T03:00:00.000Z', hastaEn: '2026-12-26T03:00:00.000Z', motivo: 'Navidad',
    })
    expect(await within(b).findByText(/nadie podrá agendar contigo/i)).toBeInTheDocument()
  })

  it('el aviso de bloqueo usa la fecha como en la lista, y con un solo dia lo dice como un dia', async () => {
    pintar()
    await screen.findByRole('region', { name: 'Corte de pelo' })
    const b = screen.getByRole('region', { name: /bloquear fechas/i })

    await userEvent.type(within(b).getByLabelText('Desde el día'), '2026-12-24')
    await userEvent.type(within(b).getByLabelText('Hasta el día'), '2026-12-25')
    await userEvent.click(within(b).getByRole('button', { name: /bloquear/i }))
    expect(await within(b).findByText('Listo: del 24-12-2026 al 25-12-2026 nadie podrá agendar contigo.'))
      .toBeInTheDocument()

    await userEvent.type(within(b).getByLabelText('Desde el día'), '2027-01-01')
    await userEvent.type(within(b).getByLabelText('Hasta el día'), '2027-01-01')
    await userEvent.click(within(b).getByRole('button', { name: /bloquear/i }))
    expect(await within(b).findByText('Listo: el 01-01-2027 nadie podrá agendar contigo.')).toBeInTheDocument()
  })

  it('no bloquea si el ultimo dia es anterior al primero', async () => {
    pintar()
    await screen.findByRole('region', { name: 'Corte de pelo' })
    const b = screen.getByRole('region', { name: /bloquear fechas/i })

    await userEvent.type(within(b).getByLabelText('Desde el día'), '2026-12-25')
    await userEvent.type(within(b).getByLabelText('Hasta el día'), '2026-12-24')
    await userEvent.click(within(b).getByRole('button', { name: /bloquear/i }))

    expect(creados()).toHaveLength(0)
    expect(await within(b).findByText(/no puede ser anterior/i)).toBeInTheDocument()
  })

  it('lista los bloqueos vigentes con sus fechas en dia chileno y el motivo', async () => {
    pintar()
    const b = await screen.findByRole('region', { name: /bloquear fechas/i })

    // hastaEn es el inicio del dia SIGUIENTE al ultimo: el 26 no debe aparecer.
    expect(await within(b).findByText('Del 24-12-2026 al 25-12-2026')).toBeInTheDocument()
    expect(within(b).getByText(/navidad/i)).toBeInTheDocument()
  })

  it('cuando no hay ningun bloqueo lo dice', async () => {
    bloqueos = []
    pintar()
    const b = await screen.findByRole('region', { name: /bloquear fechas/i })

    expect(await within(b).findByText(/no tienes fechas bloqueadas/i)).toBeInTheDocument()
  })

  it('un bloqueo de un solo dia se muestra como un dia, no como un rango', async () => {
    bloqueos = [{ id: 'b2', desdeEn: '2027-01-01T03:00:00.000Z', hastaEn: '2027-01-02T03:00:00.000Z', motivo: null }]
    pintar()
    const b = await screen.findByRole('region', { name: /bloquear fechas/i })

    expect(await within(b).findByText('El 01-01-2027')).toBeInTheDocument()
    expect(within(b).queryByText(/del .* al /i)).not.toBeInTheDocument()
  })

  it('quitar un bloqueo lo manda con DELETE y lo saca de la lista', async () => {
    bloqueos = [
      { id: 'b1', desdeEn: '2026-12-24T03:00:00.000Z', hastaEn: '2026-12-26T03:00:00.000Z', motivo: 'Navidad' },
      { id: 'b2', desdeEn: '2027-01-01T03:00:00.000Z', hastaEn: '2027-01-02T03:00:00.000Z', motivo: null },
    ]
    pintar()
    const b = await screen.findByRole('region', { name: /bloquear fechas/i })
    await within(b).findByText('Del 24-12-2026 al 25-12-2026')

    await userEvent.click(within(b).getByRole('button', { name: /quitar este bloqueo 1/i }))

    await waitFor(() => expect(borrados()).toHaveLength(1))
    expect(borrados()[0].ruta).toBe('/api/mi/bloqueos/b1')
    expect(await within(b).findByText('El 01-01-2027')).toBeInTheDocument()
    expect(within(b).queryByText('Del 24-12-2026 al 25-12-2026')).not.toBeInTheDocument()
    expect(await within(b).findByText(/bloqueo quitado/i)).toBeInTheDocument()
  })

  it('si no puede quitarlo lo dice, deja reintentar y el bloqueo sigue en la lista', async () => {
    alQuitar = () => json({ title: 'Conflicto', detail: 'Ese bloqueo ya no existe.' }, 404)
    pintar()
    const b = await screen.findByRole('region', { name: /bloquear fechas/i })
    await within(b).findByText('Del 24-12-2026 al 25-12-2026')

    await userEvent.click(within(b).getByRole('button', { name: /quitar este bloqueo 1/i }))

    expect(await within(b).findByRole('alert')).toHaveTextContent(/ese bloqueo ya no existe/i)
    expect(within(b).getByText('Del 24-12-2026 al 25-12-2026')).toBeInTheDocument()
    await waitFor(() => expect(within(b).getByRole('button', { name: /quitar este bloqueo 1/i })).toBeEnabled())
  })

  it('si no puede cargar los bloqueos lo dice y deja reintentar sin tumbar la pagina', async () => {
    vi.stubGlobal('fetch', vi.fn(async (ruta: string) => {
      if (ruta === '/api/auth/csrf') return json({ token: 't' })
      if (ruta === '/api/mi/disponibilidad') return json(disponibilidad)
      return json({ title: 'x', detail: 'Servidor caído.' }, 500)
    }))
    pintar()

    expect(await screen.findByRole('alert')).toHaveTextContent(/servidor caído/i)
    expect(screen.getByRole('button', { name: /reintentar/i })).toBeInTheDocument()
    // El horario no se cae por una pestaña que fallo.
    expect(screen.getByRole('region', { name: 'Corte de pelo' })).toBeInTheDocument()
  })

  it('las solicitudes de hora se atienden en Mis citas: enlaza ahi y no las duplica', async () => {
    pintar()
    await screen.findByRole('region', { name: 'Corte de pelo' })

    expect(screen.getByRole('link', { name: /mis citas/i })).toHaveAttribute('href', '/mis-citas')
  })

  it('si no puede cargar el horario lo dice y deja reintentar', async () => {
    vi.stubGlobal('fetch', vi.fn(async (ruta: string) =>
      ruta === '/api/auth/csrf' ? json({ token: 't' }) : json({ title: 'x', detail: 'Servidor caído.' }, 500)))
    pintar()

    expect(await screen.findByRole('alert')).toHaveTextContent(/servidor caído/i)
    expect(screen.getByRole('button', { name: /reintentar/i })).toBeInTheDocument()
    expect(screen.getByRole('heading', { level: 1, name: 'Mi agenda' })).toBeInTheDocument()
  })
})
