import { describe, expect, it } from 'vitest'
import type { Zona } from '../api/catalogo'
import { agruparPorRegion, etiquetaDeZona } from './zonas'

const comuna = (id: number, nombre: string, region: string, lat: number): Zona => ({
  id, nombre, sector: region, comuna: nombre, centroLatitud: lat, centroLongitud: -70,
  tipo: 'Comuna', region, padreId: null,
})
const barrio = (id: number, nombre: string, padre: Zona): Zona => ({
  id, nombre, sector: 'centro', comuna: padre.nombre, centroLatitud: padre.centroLatitud, centroLongitud: -70,
  tipo: 'Barrio', region: padre.region, padreId: padre.id,
})

const ARICA = comuna(1, 'Arica', 'Arica y Parinacota', -18.5)
const SANTIAGO = comuna(2, 'Santiago', 'Metropolitana de Santiago', -33.4)
const TEMUCO = comuna(3, 'Temuco', 'La Araucanía', -38.7)
const PUNTA = comuna(4, 'Punta Arenas', 'Magallanes y la Antártica Chilena', -53.2)
const CENTRO = barrio(10, 'Centro', TEMUCO)

describe('las zonas de Chile', () => {
  it('un barrio se nombra con su comuna y una comuna va sola', () => {
    expect(etiquetaDeZona(CENTRO)).toBe('Centro, Temuco')
    expect(etiquetaDeZona(TEMUCO)).toBe('Temuco')
  })

  it('las regiones van de norte a sur, sin importar el orden en que lleguen', () => {
    const grupos = agruparPorRegion([PUNTA, TEMUCO, ARICA, SANTIAGO])

    expect(grupos.map((g) => g.region)).toEqual([
      'Arica y Parinacota', 'Metropolitana de Santiago', 'La Araucanía', 'Magallanes y la Antártica Chilena',
    ])
  })

  it('un barrio cuelga de su comuna y no aparece como comuna aparte', () => {
    const [grupo] = agruparPorRegion([TEMUCO, CENTRO, barrio(11, 'Amanecer', TEMUCO)])

    expect(grupo.comunas).toHaveLength(1)
    expect(grupo.comunas[0].comuna.nombre).toBe('Temuco')
    expect(grupo.comunas[0].barrios.map((b) => b.nombre)).toEqual(['Amanecer', 'Centro'])
  })

  it('las comunas de una region van por orden alfabetico, con tildes donde corresponde', () => {
    const a = comuna(5, 'Ñiquén', 'Ñuble', -36.3)
    const b = comuna(6, 'Chillán', 'Ñuble', -36.6)
    const c = comuna(7, 'Coihueco', 'Ñuble', -36.6)

    const [grupo] = agruparPorRegion([a, c, b])

    expect(grupo.comunas.map((x) => x.comuna.nombre)).toEqual(['Chillán', 'Coihueco', 'Ñiquén'])
  })

  it('sin zonas no hay grupos', () => {
    expect(agruparPorRegion([])).toEqual([])
  })
})
