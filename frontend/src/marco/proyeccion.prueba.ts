import { describe, expect, it } from 'vitest'
import type { Zona } from '../api/catalogo'
import { CENTRO, RADIO_MAXIMO, SEPARACION, proyectarBarrios } from './proyeccion'

const z = (id: number, nombre: string, lat: number, lon: number): Zona =>
  ({ id, nombre, sector: 'x', comuna: 'Temuco', centroLatitud: lat, centroLongitud: lon })

// Centros reales de varios barrios de Temuco (los mismos que siembra la migracion).
const ZONAS = [
  z(1, 'Centro', -38.7400362, -72.5903905),
  z(2, 'Santa Rosa', -38.7358, -72.5645),
  z(3, 'Parque Costanera', -38.7405, -72.5760),
  z(4, 'Las Mariposas', -38.7438, -72.5712),
  z(5, 'Pedro de Valdivia', -38.7172, -72.5946),
  z(14, 'Labranza', -38.7665376, -72.7534177),
  z(16, 'Pichicautín', -38.8020, -72.6010),
]

describe('el radar de los barrios', () => {
  it('sin barrios no hay nada que dibujar', () => {
    expect(proyectarBarrios([], () => 0)).toEqual([])
  })

  it('el Centro queda en el medio del radar', () => {
    const c = proyectarBarrios(ZONAS, () => 0).find((p) => p.nombre === 'Centro')!
    expect(c.x).toBeCloseTo(CENTRO, 5)
    expect(c.y).toBeCloseTo(CENTRO, 5)
  })

  it('respeta las direcciones reales: Labranza al poniente, Pichicautin al sur, Pedro de Valdivia al norte, Santa Rosa al oriente', () => {
    const p = Object.fromEntries(proyectarBarrios(ZONAS, () => 0).map((q) => [q.nombre, q]))
    expect(p['Labranza'].x).toBeLessThan(CENTRO - 100)
    expect(p['Pichicautín'].y).toBeGreaterThan(CENTRO + 50)
    expect(p['Pedro de Valdivia'].y).toBeLessThan(CENTRO - 20)
    expect(p['Santa Rosa'].x).toBeGreaterThan(CENTRO + 20)
  })

  it('un barrio lejano no aplasta al resto en un racimo: todos quedan separados y se pueden tocar', () => {
    const puntos = proyectarBarrios(ZONAS, () => 0)
    for (let i = 0; i < puntos.length; i++)
      for (let j = i + 1; j < puntos.length; j++)
        expect(Math.hypot(puntos[i].x - puntos[j].x, puntos[i].y - puntos[j].y)).toBeGreaterThan(SEPARACION - 4)
  })

  it('ninguno se sale del circulo del radar', () => {
    for (const p of proyectarBarrios(ZONAS, () => 0))
      expect(Math.hypot(p.x - CENTRO, p.y - CENTRO)).toBeLessThanOrEqual(RADIO_MAXIMO + 9)
  })

  it('lleva la cantidad de gente de cada barrio', () => {
    const p = proyectarBarrios(ZONAS, (id) => (id === 1 ? 13 : 0))
    expect(p.find((q) => q.nombre === 'Centro')!.n).toBe(13)
    expect(p.find((q) => q.nombre === 'Labranza')!.n).toBe(0)
  })

  it('es determinista: las mismas zonas dan los mismos puntos', () => {
    expect(proyectarBarrios(ZONAS, () => 1)).toEqual(proyectarBarrios(ZONAS, () => 1))
  })

  it('dos barrios en el mismo punto exacto tambien se separan', () => {
    const mismo = [z(1, 'Centro', -38.74, -72.59), z(2, 'A', -38.7, -72.55), z(3, 'B', -38.7, -72.55)]
    const [, a, b] = proyectarBarrios(mismo, () => 0)
    expect(Math.hypot(a.x - b.x, a.y - b.y)).toBeGreaterThan(SEPARACION - 4)
  })
})
