import { beforeEach, describe, expect, it, vi } from 'vitest'
import {
  agendar, cancelarCita, claveDeDia, horaDeChile, nuevaClaveDeIdempotencia, porDia,
  reprogramarCita, calendarioDe, miDisponibilidad, guardarDisponibilidad, crearBloqueo,
  listarBloqueos, quitarBloqueo, DIAS, inicioDeDiaEnChile, agruparPorDiaChileno,
  sePuedeReprogramar, horasHasta, type Bloque, type Cita,
} from './servicio'

const obtener = vi.fn()
const enviar = vi.fn()
vi.mock('../../api/cliente', () => ({
  obtener: (...a: unknown[]) => obtener(...a),
  enviar: (...a: unknown[]) => enviar(...a),
}))

beforeEach(() => {
  obtener.mockReset().mockResolvedValue({})
  enviar.mockReset().mockResolvedValue({})
})

const bloque = (inicioEn: string): Bloque => ({
  id: inicioEn, inicioEn, finEn: inicioEn, estado: 'libre',
})

describe('agendar', () => {
  it('manda la clave en la cabecera Idempotency-Key y no en el cuerpo', async () => {
    await agendar('b1', 'intencion-1', 'consulta')

    const [ruta, cuerpo, metodo, cabeceras] = enviar.mock.calls[0]
    expect(ruta).toBe('/api/citas')
    expect(metodo).toBe('POST')
    expect(cabeceras).toEqual({ 'Idempotency-Key': 'intencion-1' })
    expect(cuerpo).toEqual({ bloqueId: 'b1', notaBuscador: 'consulta' })
    expect(JSON.stringify(cuerpo)).not.toContain('intencion-1')
  })

  it('una nota vacia o solo espacios se manda como nula', async () => {
    await agendar('b1', 'k', '   ')
    expect(enviar.mock.calls[0][1].notaBuscador).toBeNull()
  })

  it('la misma clave en dos intentos del mismo envio: es lo que hace al doble clic inofensivo', async () => {
    const clave = nuevaClaveDeIdempotencia()
    await agendar('b1', clave, null)
    await agendar('b1', clave, null)

    expect(enviar.mock.calls[0][3]).toEqual(enviar.mock.calls[1][3])
  })
})

describe('claves de idempotencia', () => {
  it('cada intencion nueva trae una clave distinta', () => {
    expect(nuevaClaveDeIdempotencia()).not.toBe(nuevaClaveDeIdempotencia())
  })

  it('cumple lo que exige el filtro del servidor: de 8 a 120 caracteres, solo letras, digitos y guiones', () => {
    const k = nuevaClaveDeIdempotencia()
    expect(k.length).toBeGreaterThanOrEqual(8)
    expect(k.length).toBeLessThanOrEqual(120)
    expect(k).toMatch(/^[A-Za-z0-9_:-]+$/)
  })
})

describe('cancelar y reprogramar', () => {
  it('llevan la clave en la cabecera', async () => {
    await cancelarCita('c1', 'k1', 'viaje')
    await reprogramarCita('c1', 'b2', 'k2', null)

    expect(enviar.mock.calls[0][3]).toEqual({ 'Idempotency-Key': 'k1' })
    expect(enviar.mock.calls[1][3]).toEqual({ 'Idempotency-Key': 'k2' })
    expect(enviar.mock.calls[1][1]).toEqual({ bloqueNuevoId: 'b2', motivo: null })
  })
})

describe('calendario', () => {
  it('pide al servicio por la ruta del prestador y no con el id en la ruta', async () => {
    await calendarioDe('estudio espinoza', 'svc-1')

    const url = new URL(obtener.mock.calls[0][0] as string, 'https://zass.test')
    expect(url.pathname).toBe('/api/prestadores/estudio%20espinoza/bloques')
    expect(url.searchParams.get('servicio')).toBe('svc-1')
    expect(Date.parse(url.searchParams.get('hasta')!)).toBeGreaterThan(Date.parse(url.searchParams.get('desde')!))
  })
})

describe('hora de Chile', () => {
  // 12:00 UTC de noviembre es 09:00 en Chile (UTC-3); 13:00 UTC de julio tambien (UTC-4).
  it('muestra las 09:00 de Chile en verano y en invierno', () => {
    expect(horaDeChile('2026-11-09T12:00:00Z')).toBe('09:00')
    expect(horaDeChile('2026-07-09T13:00:00Z')).toBe('09:00')
  })

  it('un bloque de las 22:00 de Chile cae en su dia aunque en UTC ya sea el siguiente', () => {
    // 01:00 UTC del martes 10 = 22:00 del lunes 9 en Chile.
    expect(claveDeDia('2026-11-10T01:00:00Z')).toBe('2026-11-09')
  })

  it('agrupa por dia chileno y en orden, aunque lleguen desordenados', () => {
    const grupos = porDia([
      bloque('2026-11-10T01:00:00Z'), // lunes 22:00
      bloque('2026-11-09T12:00:00Z'), // lunes 09:00
      bloque('2026-11-10T13:00:00Z'), // martes 10:00
    ])

    expect(grupos.map((g) => g.dia)).toEqual(['2026-11-09', '2026-11-10'])
    expect(grupos[0].bloques.map((b) => horaDeChile(b.inicioEn))).toEqual(['09:00', '22:00'])
  })
})

describe('mi disponibilidad (API-32 a API-34)', () => {
  it('lee mis reglas de atencion servicio por servicio', async () => {
    await miDisponibilidad()

    expect(obtener).toHaveBeenCalledWith('/api/mi/disponibilidad')
  })

  it('guarda las franjas de UN servicio con PUT y las horas como HH:mm:ss', async () => {
    await guardarDisponibilidad('ps1', [
      { diaSemana: 1, horaInicio: '09:00', horaFin: '13:00', duracionBloqueMin: 30 },
    ])

    const [ruta, cuerpo, metodo] = enviar.mock.calls[0]
    expect(ruta).toBe('/api/mi/disponibilidad')
    expect(metodo).toBe('PUT')
    // El servidor lee TimeOnly: «09:00» sin segundos no es un TimeOnly valido para el.
    expect(cuerpo).toEqual({
      prestadorServicioId: 'ps1',
      franjas: [{ diaSemana: 1, horaInicio: '09:00:00', horaFin: '13:00:00', duracionBloqueMin: 30 }],
    })
  })

  it('una lista vacia tambien se manda: asi se quitan todas las horas de un servicio', async () => {
    await guardarDisponibilidad('ps1', [])

    expect(enviar.mock.calls[0][1]).toEqual({ prestadorServicioId: 'ps1', franjas: [] })
  })

  it('crea un bloqueo con POST y sin motivo manda null, no una cadena vacia', async () => {
    await crearBloqueo('2026-12-24T03:00:00.000Z', '2026-12-26T03:00:00.000Z', '  ')

    const [ruta, cuerpo, metodo] = enviar.mock.calls[0]
    expect(ruta).toBe('/api/mi/bloqueos')
    expect(metodo).toBe('POST')
    expect(cuerpo).toEqual({ desdeEn: '2026-12-24T03:00:00.000Z', hastaEn: '2026-12-26T03:00:00.000Z', motivo: null })
  })

  it('los dias van de 1 (lunes) a 7 (domingo), como TBL-16', () => {
    expect(DIAS.map((d) => d.valor)).toEqual([1, 2, 3, 4, 5, 6, 7])
    expect(DIAS[0].nombre).toBe('Lunes')
    expect(DIAS[6].nombre).toBe('Domingo')
  })
})

describe('inicioDeDiaEnChile (para bloquear dias completos)', () => {
  // Chile esta en UTC-3 en verano y UTC-4 en invierno: la medianoche chilena NO es siempre la misma hora UTC.
  it('en verano la medianoche de Chile son las 03:00 UTC', () => {
    expect(inicioDeDiaEnChile('2026-12-24')).toBe('2026-12-24T03:00:00.000Z')
  })

  it('en invierno son las 04:00 UTC', () => {
    expect(inicioDeDiaEnChile('2026-07-15')).toBe('2026-07-15T04:00:00.000Z')
  })

  it('sigue valiendo el dia chileno aunque el navegador este en otra zona', () => {
    // Si se usara la zona del navegador, quien este en Madrid o Tokio bloquearia otro dia.
    const iso = inicioDeDiaEnChile('2026-12-25')
    expect(claveDeDia(iso)).toBe('2026-12-25')
    expect(horaDeChile(iso)).toBe('00:00')
  })

  it('el domingo del cambio de hora de septiembre no se va al dia anterior', () => {
    // Ese domingo la medianoche chilena NO EXISTE: el reloj salta de 00:00 a 01:00. Corrigiendo el
    // desfase dos veces el resultado oscilaba y se quedaba en las 23:00 del SABADO, asi que bloquear
    // desde el domingo se llevaba la ultima hora del sabado. El primer instante que existe es la 01:00.
    expect(inicioDeDiaEnChile('2026-09-06')).toBe('2026-09-06T04:00:00.000Z')
    expect(claveDeDia(inicioDeDiaEnChile('2026-09-06'))).toBe('2026-09-06')
    expect(horaDeChile(inicioDeDiaEnChile('2026-09-06'))).toBe('01:00')

    // Y no es cosa de 2026: el salto cae en el primer domingo de septiembre de cada anio.
    expect(claveDeDia(inicioDeDiaEnChile('2027-09-05'))).toBe('2027-09-05')
  })

  it('el domingo del cambio de hora de abril (el dia que dura 25 h) tampoco se mueve', () => {
    expect(claveDeDia(inicioDeDiaEnChile('2026-04-05'))).toBe('2026-04-05')
    expect(horaDeChile(inicioDeDiaEnChile('2026-04-05'))).toBe('00:00')
  })

  it('ningun dia de 2026 ni de 2027 empieza fuera de su propio dia chileno', () => {
    // El fallo de septiembre existia porque las pruebas miraban un dia de verano y uno de invierno.
    // Esta recorre los 730 y compara con la zona de Chile, que es lo que el defecto habria delatado.
    const malos: string[] = []
    for (let t = Date.UTC(2026, 0, 1); t < Date.UTC(2028, 0, 1); t += 86400000) {
      const dia = new Date(t).toISOString().slice(0, 10)
      if (claveDeDia(inicioDeDiaEnChile(dia)) !== dia) malos.push(dia)
    }
    expect(malos).toEqual([])
  })
})

describe('mis bloqueos (API-34)', () => {
  it('lista los bloqueos con GET y sin cuerpo', async () => {
    await listarBloqueos()

    expect(obtener).toHaveBeenCalledWith('/api/mi/bloqueos')
    expect(enviar).not.toHaveBeenCalled()
  })

  it('quita un bloqueo con DELETE por su id y sin cuerpo', async () => {
    await quitarBloqueo('b7')

    const [ruta, cuerpo, metodo] = enviar.mock.calls[0]
    expect(ruta).toBe('/api/mi/bloqueos/b7')
    expect(cuerpo).toBeUndefined()
    expect(metodo).toBe('DELETE')
  })

  it('codifica el id en la ruta: un id con slash no debe abrir otra ruta', async () => {
    await quitarBloqueo('a/b')

    expect(enviar.mock.calls[0][0]).toBe('/api/mi/bloqueos/a%2Fb')
  })
})

describe('agrupar citas por dia chileno', () => {
  const laCita = (id: string, inicioEn: string) => ({ id, inicioEn })

  it('una cita de las 23:30 de Chile cae en SU dia, aunque en UTC ya sea el siguiente', () => {
    // 2026-11-20T02:30Z son las 23:30 del jueves 19 en Chile (UTC-3 en noviembre).
    const dias = agruparPorDiaChileno([laCita('a', '2026-11-20T02:30:00Z')])

    expect(dias).toHaveLength(1)
    expect(dias[0].dia).toBe('2026-11-19')
    expect(dias[0].titulo).toContain('19 de noviembre')
  })

  it('junta en un solo grupo las del mismo dia chileno y las ordena por hora', () => {
    const dias = agruparPorDiaChileno([
      laCita('tarde', '2026-11-20T20:00:00Z'),
      laCita('manana', '2026-11-20T13:00:00Z'),
    ])

    expect(dias).toHaveLength(1)
    expect(dias[0].items.map((c) => c.id)).toEqual(['manana', 'tarde'])
  })

  it('en orden descendente el dia mas reciente va primero, y dentro del dia la hora mas tarde', () => {
    const dias = agruparPorDiaChileno([
      laCita('vieja', '2026-11-18T13:00:00Z'),
      laCita('manana', '2026-11-20T13:00:00Z'),
      laCita('tarde', '2026-11-20T20:00:00Z'),
    ], 'desc')

    expect(dias.map((d) => d.dia)).toEqual(['2026-11-20', '2026-11-18'])
    expect(dias[0].items.map((c) => c.id)).toEqual(['tarde', 'manana'])
  })
})

describe('quien puede mover una hora', () => {
  const ahora = Date.parse('2026-11-20T12:00:00Z')
  const laCita = (estado: string, inicioEn: string) =>
    ({ estado, inicioEn } as Pick<Cita, 'estado' | 'inicioEn'>)

  it('el buscador puede con mas de 2 horas por delante', () => {
    expect(sePuedeReprogramar(laCita('Confirmada', '2026-11-20T15:00:00Z'), false, ahora)).toBe(true)
  })

  it('el buscador NO puede dentro de las 2 horas (5B de CU-14)', () => {
    expect(sePuedeReprogramar(laCita('Confirmada', '2026-11-20T13:30:00Z'), false, ahora)).toBe(false)
  })

  it('el prestador si puede dentro de las 2 horas: es su agenda', () => {
    expect(sePuedeReprogramar(laCita('Confirmada', '2026-11-20T13:30:00Z'), true, ahora)).toBe(true)
  })

  /** El servidor solo acepta reprogramar desde `Confirmada`; ofrecerlo en otra seria mentir. */
  it('ningun estado que no sea Confirmada se puede mover, ni siquiera para el prestador', () => {
    for (const estado of ['Solicitada', 'PendienteAsistencia', 'Realizada', 'Cancelada']) {
      expect(sePuedeReprogramar(laCita(estado, '2026-11-25T13:00:00Z'), true, ahora)).toBe(false)
    }
  })

  it('horasHasta es negativo cuando la hora ya paso', () => {
    expect(horasHasta('2026-11-20T10:00:00Z', ahora)).toBe(-2)
  })
})
