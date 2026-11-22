import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router-dom'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { duracion, porcentaje, type Indicadores as Datos } from '../funciones/interno/servicio'
import { Indicadores } from './Indicadores'

function json(cuerpo: unknown, estado = 200) {
  return new Response(JSON.stringify(cuerpo), { status: estado, headers: { 'content-type': 'application/json' } })
}

const DATOS: Datos = {
  verificacion: {
    enCola: 4, masAntiguaDesde: '2026-11-10T15:00:00Z', resueltas: 3, rechazadas: 1,
    tasaDeRechazo: 1 / 3, horasPromedioDesdeCreacion: 20,
  },
  avisos: { correosFallidos: 2 },
  reportesAbiertos: 5,
  tarjetasEmitidasSinActivar: 7,
  calculadoEn: '2026-11-20T15:00:00Z',
}

let respuesta: () => Response
beforeEach(() => {
  respuesta = () => json(DATOS)
  vi.stubGlobal('fetch', vi.fn(async (ruta: string) =>
    ruta === '/api/interno/indicadores' ? respuesta() : json({}, 404)))
})

const pintar = () => render(<MemoryRouter><Indicadores /></MemoryRouter>)

/** El valor que acompaña a una etiqueta en la lista de cifras. */
function valorDe(etiqueta: string): string {
  const dt = screen.getByText(etiqueta, { selector: 'dt' })
  return within(dt.parentElement!).getAllByRole('definition')[0].textContent ?? ''
}

describe('JQ-14: indicadores (API-60)', () => {
  it('muestra cada cifra con su etiqueta', async () => {
    pintar()
    await screen.findByRole('heading', { level: 2, name: 'Verificación' })
    expect(valorDe('Credenciales en cola')).toBe('4')
    expect(valorDe('Tasa de rechazo (30 días)')).toBe('33 %')
    expect(screen.getByText('1 de 3 resueltas')).toBeInTheDocument()
    expect(valorDe('Tiempo promedio desde que se creó la credencial hasta que se resolvió (30 días)')).toBe('20 h')
    expect(valorDe('Correos fallidos (7 días)')).toBe('2')
    expect(valorDe('Reportes de reseñas sin resolver')).toBe('5')
    expect(valorDe('Tarjetas emitidas sin activar')).toBe('7')
    // El rótulo no promete lo que la cifra no mide.
    expect(screen.getByText(/el envío a revisión no queda con fecha/)).toBeInTheDocument()
  })

  it('sin resoluciones dice «Sin datos todavía» en vez de 0 % o NaN', async () => {
    respuesta = () => json({
      ...DATOS,
      verificacion: { ...DATOS.verificacion, enCola: 0, masAntiguaDesde: null, resueltas: 0, rechazadas: 0, tasaDeRechazo: null, horasPromedioDesdeCreacion: null },
    })
    pintar()
    await screen.findByRole('heading', { level: 2, name: 'Verificación' })
    expect(valorDe('Tasa de rechazo (30 días)')).toBe('Sin datos todavía')
    expect(screen.queryByText(/NaN/)).toBeNull()
    expect(screen.queryByText('La más antigua en cola, creada el')).toBeNull()
  })

  it('si falla, se puede reintentar', async () => {
    let primera = true
    respuesta = () => { if (primera) { primera = false; return json({}, 500) } return json(DATOS) }
    pintar()
    await userEvent.click(await screen.findByRole('button', { name: 'Reintentar' }))
    expect(await screen.findByRole('heading', { level: 2, name: 'Verificación' })).toBeInTheDocument()
  })
})

describe('JQ-14: formato de las cifras', () => {
  it.each([[0, '0 %'], [1 / 3, '33 %'], [1, '100 %']])('porcentaje(%s) = %s', (x, texto) => {
    expect(porcentaje(x)).toBe(texto)
  })
  it.each([[0, 'menos de 1 h'], [0.4, 'menos de 1 h'], [0.99, 'menos de 1 h'], [1, '1 h'], [20, '20 h'], [47, '47 h'], [48, '2 días'], [84, '3,5 días']])('duracion(%s) = %s', (h, texto) => {
    expect(duracion(h)).toBe(texto)
  })
})
