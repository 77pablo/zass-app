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
    prestadorSlug: 'estudio-espinoza',
    notaBuscador: null, expiraEn: null, nueva: false, ...extra,
  }
}

const MI_SLUG = 'estudio-mio'

/** Un bloque libre del calendario (API-35). El id se usa tal cual al reprogramar. */
function bloque(id: string, inicioEn: string, estado = 'libre') {
  return { id, inicioEn, finEn: inicioEn, estado }
}

let citas: unknown[]
let llamadas: { ruta: string; cabeceras: Record<string, string>; cuerpo?: unknown }[]
let alAccionar: () => Response
/** Lo que devuelve API-35, y las rutas con que se pidio: sirve para ver que se recargo. */
let bloquesLibres: ReturnType<typeof bloque>[]
let calendariosPedidos: string[]
/** Lo que devuelve el buscador publico (API-63) al resolver el slug de un prestador ajeno. */
let encontrados: { perfilId: string; slug: string }[]
let busquedasHechas: number

beforeEach(() => {
  citas = []
  llamadas = []
  alAccionar = () => json({}, 200)
  bloquesLibres = []
  calendariosPedidos = []
  encontrados = []
  busquedasHechas = 0
  olvidarCsrf()
  vi.stubGlobal('fetch', vi.fn(async (ruta: string, opciones?: RequestInit) => {
    if (ruta === '/api/auth/csrf') return json({ token: 't' })
    if (ruta === '/api/mi/citas') return json(citas)
    if (ruta === '/api/mi/perfil-prestador') return json({ id: MI_PERFIL, slug: MI_SLUG })
    if (ruta.startsWith('/api/buscar')) {
      busquedasHechas++
      return json({ resultados: encontrados, total: encontrados.length, pagina: 1, hayMas: false })
    }
    if (ruta.includes('/bloques')) {
      calendariosPedidos.push(ruta)
      return json({
        prestadorServicioId: 's', perfilPrestadorId: 'perfil-otro', servicio: 'Consulta inicial',
        modoAgenda: 'Automatico', duracionMinutos: 30, anticipacionMinimaHoras: 2,
        bloques: bloquesLibres,
      })
    }
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

/** Horas contadas desde AHORA: la regla de las 2 horas se mide contra el reloj, no contra una fecha. */
const enHoras = (n: number) => new Date(Date.now() + n * 3_600_000).toISOString()

describe('las citas se agrupan por dia CHILENO', () => {
  it('dos citas del mismo dia chileno van bajo un solo titulo aunque en UTC sean dos dias', async () => {
    citas = [
      // 02:30Z del 20 son las 23:30 del jueves 19 en Chile; 20:00Z del 19, las 17:00 del 19.
      cita({ id: 'a', codigo: 'ZS-NOCHE', estado: 'Cancelada', inicioEn: '2026-11-20T02:30:00Z' }),
      cita({ id: 'b', codigo: 'ZS-TARDE', estado: 'Cancelada', inicioEn: '2026-11-19T20:00:00Z' }),
    ]
    pintar()

    const anteriores = (await screen.findByRole('heading', { name: 'Anteriores' })).closest('section')!
    const dias = within(anteriores).getAllByRole('heading', { level: 3 })
    expect(dias).toHaveLength(1)
    expect(dias[0]).toHaveTextContent(/19 de noviembre/)
    expect(within(anteriores).getByText(/23:30 h/)).toBeInTheDocument()
  })

  it('en el historial el dia mas reciente va primero', async () => {
    citas = [
      cita({ id: 'a', codigo: 'ZS-VIEJA', estado: 'Cancelada', inicioEn: '2026-11-10T13:00:00Z' }),
      cita({ id: 'b', codigo: 'ZS-NUEVA', estado: 'Cancelada', inicioEn: '2026-11-19T13:00:00Z' }),
    ]
    pintar()

    const anteriores = (await screen.findByRole('heading', { name: 'Anteriores' })).closest('section')!
    const dias = within(anteriores).getAllByRole('heading', { level: 3 })
    expect(dias.map((d) => d.textContent)).toEqual([
      expect.stringContaining('19 de noviembre'),
      expect.stringContaining('10 de noviembre'),
    ])
  })
})

describe('reprogramar (CU-14)', () => {
  const LIBRES = [
    bloque('b1', '2026-12-10T18:00:00Z'),   // 15:00 en Chile
    bloque('b2', '2026-12-10T19:00:00Z'),   // 16:00 en Chile
    bloque('b3', '2026-12-10T20:00:00Z', 'ocupado'),
  ]

  async function abrirElPanel() {
    await userEvent.click(await screen.findByRole('button', { name: 'Reprogramar' }))
    return screen.findByRole('button', { name: '15:00' })
  }

  it('el cliente elige otra hora del mismo servicio y el cambio viaja con su clave de idempotencia', async () => {
    citas = [cita({ id: 'a', inicioEn: enHoras(48) })]
    bloquesLibres = LIBRES
    alAccionar = () => json({ ...cita({ id: 'nueva', codigo: 'ZS-NUEVA' }), nueva: true }, 201)
    pintar()

    await abrirElPanel()
    await userEvent.click(screen.getByRole('button', { name: '16:00' }))
    await userEvent.click(screen.getByRole('button', { name: 'Confirmar el cambio' }))

    await waitFor(() => expect(llamadas).toHaveLength(1))
    expect(llamadas[0].ruta).toBe('/api/citas/a/reprogramar')
    expect(llamadas[0].cuerpo).toEqual({ bloqueNuevoId: 'b2', motivo: null })
    expect(llamadas[0].cabeceras['Idempotency-Key']).toMatch(/^[0-9a-f-]{36}$/)
    expect(calendariosPedidos[0]).toContain('/api/prestadores/estudio-espinoza/bloques')
    // El slug viene en la cita (API-37): no se sale a buscar al prestador por su nombre.
    expect(busquedasHechas).toBe(0)
  })

  it('solo ofrece las horas libres: una ocupada no se puede elegir', async () => {
    citas = [cita({ id: 'a', inicioEn: enHoras(48) })]
    bloquesLibres = LIBRES
    pintar()

    await abrirElPanel()
    expect(screen.queryByRole('button', { name: '17:00' })).not.toBeInTheDocument()
  })

  it('al terminar muestra el codigo nuevo y dice que la resena sigue en pie (RN-S4-05)', async () => {
    citas = [cita({ id: 'a', inicioEn: enHoras(48) })]
    bloquesLibres = LIBRES
    alAccionar = () => json({ ...cita({ id: 'nueva', codigo: 'ZS-NUEVA' }), nueva: true }, 201)
    pintar()

    await abrirElPanel()
    await userEvent.click(screen.getByRole('button', { name: '15:00' }))
    await userEvent.click(screen.getByRole('button', { name: 'Confirmar el cambio' }))

    expect(await screen.findByText('ZS-NUEVA')).toBeInTheDocument()
    expect(screen.getByText(/no te quita la posibilidad de reseñar/)).toBeInTheDocument()
  })

  /** Flujo 5A: nada cambio, la cita vieja sigue en pie, y el por que tiene que quedar a la vista. */
  it('si la hora se tomo recien recarga el calendario y deja el aviso visible', async () => {
    citas = [cita({ id: 'a', inicioEn: enHoras(48) })]
    bloquesLibres = LIBRES
    alAccionar = () => json({ title: 'Conflicto', detail: 'Alguien tomó esa hora recién. Elige otra, por favor.' }, 409)
    pintar()

    await abrirElPanel()
    await userEvent.click(screen.getByRole('button', { name: '15:00' }))
    await userEvent.click(screen.getByRole('button', { name: 'Confirmar el cambio' }))

    expect(await screen.findByRole('alert')).toHaveTextContent(/Alguien tomó esa hora recién/)
    await waitFor(() => expect(calendariosPedidos).toHaveLength(2))
    expect(screen.queryByRole('button', { name: 'Confirmar el cambio' })).not.toBeInTheDocument()
  })

  /** CC-04: el reintento es el MISMO envio; elegir otra hora es otra intencion. */
  it('reintentar reusa la clave, y cambiar de hora estrena una nueva', async () => {
    citas = [cita({ id: 'a', inicioEn: enHoras(48) })]
    bloquesLibres = LIBRES
    alAccionar = () => json({ title: 'caida' }, 500)
    pintar()

    await abrirElPanel()
    await userEvent.click(screen.getByRole('button', { name: '15:00' }))
    await userEvent.click(screen.getByRole('button', { name: 'Confirmar el cambio' }))
    await screen.findByRole('alert')
    await userEvent.click(screen.getByRole('button', { name: 'Confirmar el cambio' }))
    await waitFor(() => expect(llamadas).toHaveLength(2))

    await userEvent.click(screen.getByRole('button', { name: '16:00' }))
    await userEvent.click(screen.getByRole('button', { name: 'Confirmar el cambio' }))
    await waitFor(() => expect(llamadas).toHaveLength(3))

    const clave = (n: number) => llamadas[n].cabeceras['Idempotency-Key']
    expect(clave(1)).toBe(clave(0))
    expect(clave(2)).not.toBe(clave(0))
  })

  it('a menos de 2 horas el cliente ya no puede moverla, y se le dice que le queda', async () => {
    citas = [cita({ id: 'a', inicioEn: enHoras(1) })]
    pintar()

    expect(await screen.findByText(/Faltan menos de 2 horas/)).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Reprogramar' })).not.toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Cancelar' })).toBeInTheDocument()
  })

  it('el prestador si mueve su hora dentro de las 2 h, y abre su calendario sin salir a buscarlo', async () => {
    citas = [cita({ id: 'a', inicioEn: enHoras(1), perfilPrestadorId: MI_PERFIL, prestadorSlug: MI_SLUG })]
    bloquesLibres = LIBRES
    pintar('dentro', ['prestador'])

    await abrirElPanel()
    expect(calendariosPedidos[0]).toContain(`/api/prestadores/${MI_SLUG}/bloques`)
    expect(busquedasHechas).toBe(0)
    expect(screen.queryByText(/Faltan menos de 2 horas/)).not.toBeInTheDocument()
  })

  it('una cita que todavia no esta confirmada no se puede mover', async () => {
    citas = [cita({ id: 'a', estado: 'Solicitada', inicioEn: enHoras(48) })]
    pintar()

    await screen.findByText('Esperando confirmación')
    expect(screen.queryByRole('button', { name: 'Reprogramar' })).not.toBeInTheDocument()
  })

  /** Sin slug no hay calendario publico que abrir: se dice, no se deja un panel vacio. */
  it('si la cita viene sin slug del prestador, lo dice y ofrece buscarlo', async () => {
    citas = [cita({ id: 'a', inicioEn: enHoras(48), prestadorSlug: '' })]
    pintar()

    await userEvent.click(await screen.findByRole('button', { name: 'Reprogramar' }))

    expect(await screen.findByText(/No pudimos abrir el calendario/)).toBeInTheDocument()
    expect(calendariosPedidos).toHaveLength(0)
  })
})
