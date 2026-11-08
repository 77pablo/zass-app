import { describe, expect, it } from 'vitest'
import type { Zona } from '../api/catalogo'
import { ALTO, ANCHO, proyectarChile } from './proyeccionChile'

const z = (id: number, nombre: string, lat: number, lon: number, tipo: Zona['tipo'] = 'Comuna'): Zona => ({
  id, nombre, sector: 'x', comuna: nombre, centroLatitud: lat, centroLongitud: lon, tipo, region: 'R', padreId: null,
})

const ARICA = z(1, 'Arica', -18.48, -70.31)
const SANTIAGO = z(2, 'Santiago', -33.45, -70.67)
const TEMUCO = z(3, 'Temuco', -38.74, -72.59)
const PUNTA = z(4, 'Punta Arenas', -53.16, -70.91)
const PASCUA = z(5, 'Isla de Pascua', -27.11, -109.35)
const FERNANDEZ = z(6, 'Juan Fernández', -33.64, -78.83)
const ANTARTICA = z(7, 'Antártica', -64.0, -60.0)
const BARRIO = z(8, 'Centro', -38.74, -72.59, 'Barrio')

describe('el mapa de Chile', () => {
  it('el norte queda arriba y el sur abajo, en su orden real', () => {
    const p = Object.fromEntries(proyectarChile([PUNTA, ARICA, TEMUCO, SANTIAGO], () => 0).map((q) => [q.nombre, q]))

    expect(p['Arica'].y).toBeLessThan(p['Santiago'].y)
    expect(p['Santiago'].y).toBeLessThan(p['Temuco'].y)
    expect(p['Temuco'].y).toBeLessThan(p['Punta Arenas'].y)
  })

  it('Temuco queda al poniente de Santiago, como en el mapa', () => {
    const p = Object.fromEntries(proyectarChile([SANTIAGO, TEMUCO], () => 0).map((q) => [q.nombre, q]))

    expect(p['Temuco'].x).toBeLessThan(p['Santiago'].x)
  })

  it('las islas lejanas y la Antartida se quedan fuera: no estiran el mapa', () => {
    const nombres = proyectarChile([ARICA, PASCUA, FERNANDEZ, ANTARTICA], () => 0).map((q) => q.nombre)

    expect(nombres).toEqual(['Arica'])
  })

  it('los barrios no son puntos del mapa: una comuna es un punto', () => {
    expect(proyectarChile([TEMUCO, BARRIO], () => 0).map((q) => q.nombre)).toEqual(['Temuco'])
  })

  it('todos los puntos caben dentro del dibujo', () => {
    for (const p of proyectarChile([ARICA, SANTIAGO, TEMUCO, PUNTA], () => 0)) {
      expect(p.x).toBeGreaterThan(0)
      expect(p.x).toBeLessThan(ANCHO)
      expect(p.y).toBeGreaterThan(0)
      expect(p.y).toBeLessThan(ALTO)
    }
  })

  it('lleva la cantidad de gente de cada comuna', () => {
    const p = proyectarChile([TEMUCO, SANTIAGO], (id) => (id === 3 ? 27 : 0))

    expect(p.find((q) => q.nombre === 'Temuco')!.n).toBe(27)
    expect(p.find((q) => q.nombre === 'Santiago')!.n).toBe(0)
  })

  it('sin zonas no hay puntos', () => {
    expect(proyectarChile([], () => 0)).toEqual([])
  })
})
