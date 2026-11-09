import { describe, expect, it } from 'vitest'
import { describirSuscripcion } from './estadoSuscripcion'
import type { Suscripcion } from './servicio'

const prueba: Suscripcion = {
  plan: 'prueba', estado: 'Prueba', vigente: true, enPrueba: true,
  vigenteHasta: '2026-12-12T15:00:00Z', diasRestantes: 12, renovacionAutomatica: false,
}
const activa: Suscripcion = { ...prueba, plan: 'mensual', estado: 'Activa', enPrueba: false }

describe('describirSuscripcion', () => {
  it('sin dato no dice nada', () => {
    expect(describirSuscripcion(null)).toBeNull()
  })

  it('"sin suscripcion" (perfil que aun no publica) no se cuenta como vencida', () => {
    const s: Suscripcion = {
      plan: 'ninguno', estado: 'sin suscripcion', vigente: false, enPrueba: false,
      vigenteHasta: null, diasRestantes: 0, renovacionAutomatica: false,
    }
    expect(describirSuscripcion(s)).toBeNull()
  })

  it('en prueba vigente muestra los dias que quedan, sin alerta', () => {
    expect(describirSuscripcion(prueba)).toEqual({ texto: 'Prueba · 12 días', alerta: false })
  })

  it('con un solo dia lo dice en singular', () => {
    expect(describirSuscripcion({ ...prueba, diasRestantes: 1 })?.texto).toBe('Prueba · 1 día')
  })

  it('activa y vigente muestra hasta cuando, sin alerta', () => {
    expect(describirSuscripcion(activa)).toEqual({
      texto: 'Suscripción activa · hasta el 12 de diciembre de 2026', alerta: false,
    })
  })

  it('activa sin fecha no inventa una', () => {
    expect(describirSuscripcion({ ...activa, vigenteHasta: null })?.texto).toBe('Suscripción activa')
  })

  it('vencida avisa que la ficha no aparece en las busquedas', () => {
    const r = describirSuscripcion({ ...prueba, estado: 'Vencida', vigente: false, enPrueba: false, diasRestantes: 0 })
    expect(r?.alerta).toBe(true)
    expect(r?.texto).toContain('vencida')
    expect(r?.texto).toContain('no aparece en las búsquedas')
  })

  it('una prueba o una activa con la fecha pasada cuenta como vencida aunque nadie la haya marcado', () => {
    expect(describirSuscripcion({ ...prueba, vigente: false, enPrueba: false })?.texto).toContain('vencida')
    expect(describirSuscripcion({ ...activa, vigente: false })?.texto).toContain('vencida')
  })

  it('cancelada lo dice como cancelada, no como vencida', () => {
    const r = describirSuscripcion({ ...activa, estado: 'Cancelada', vigente: false })
    expect(r?.alerta).toBe(true)
    expect(r?.texto).toContain('cancelada')
    expect(r?.texto).not.toContain('vencida')
  })
})