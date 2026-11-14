import { describe, expect, it } from 'vitest'
import { plazoDeEdicion } from './plazo'

const AHORA = new Date('2026-11-12T12:00:00Z')

describe('plazo de edicion (RN-S5-05)', () => {
  it('dice las horas que quedan', () => {
    expect(plazoDeEdicion('2026-11-15T12:00:00Z', AHORA)).toBe('72 horas')
    expect(plazoDeEdicion('2026-11-12T13:00:00Z', AHORA)).toBe('1 hora')
  })

  it('baja a minutos en la ultima hora', () => {
    expect(plazoDeEdicion('2026-11-12T12:30:00Z', AHORA)).toBe('30 minutos')
    expect(plazoDeEdicion('2026-11-12T12:00:30Z', AHORA)).toBe('1 minuto')
  })

  // El borde es lo que importa: justo en la fecha limite el plazo YA termino. Un "0 horas" que
  // se lea como que todavia se puede haria escribir una edicion que el servidor va a rechazar.
  it('vencido es null, y el borde exacto cuenta como vencido', () => {
    expect(plazoDeEdicion('2026-11-12T12:00:00Z', AHORA)).toBeNull()
    expect(plazoDeEdicion('2026-11-12T11:59:59Z', AHORA)).toBeNull()
    expect(plazoDeEdicion('2026-11-09T12:00:00Z', AHORA)).toBeNull()
  })

  it('una fecha que no se entiende no abre el plazo', () => {
    expect(plazoDeEdicion('cualquier cosa', AHORA)).toBeNull()
  })
})
