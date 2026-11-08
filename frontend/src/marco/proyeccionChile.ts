import type { Zona } from '../api/catalogo'

export const ANCHO = 280
export const ALTO = 560
const MARGEN = 24

/** Lo que cabe en el mapa: la franja continental. Isla de Pascua, Juan Fernandez y la Antartida se quedan fuera. */
const LAT_NORTE = -17.4
const LAT_SUR = -56.2
const LON_OESTE = -76.6
const LON_ESTE = -65.5
const LON_CENTRO = -71.3

/** El ancho real de Chile es una linea (200 a 400 km contra 4.300 de largo): se ensancha para que se vea. */
const ENSANCHE = 2.4

export interface PuntoDeChile {
  id: number
  nombre: string
  region: string
  x: number
  y: number
  n: number
}

/**
 * Chile hecho de puntos: una comuna, un punto, en su lugar real (norte arriba). La proyeccion es la mas
 * simple que no miente: la latitud va a la altura, y la longitud al ancho corregida por el coseno de la
 * latitud (un grado de longitud mide menos cuanto mas lejos del ecuador).
 */
export function proyectarChile(zonas: Zona[], conteo: (zonaId: number) => number): PuntoDeChile[] {
  const alto = ALTO - 2 * MARGEN
  const pxPorGrado = alto / (LAT_NORTE - LAT_SUR)

  return zonas
    .filter((z) =>
      z.tipo === 'Comuna'
      && z.centroLatitud <= LAT_NORTE && z.centroLatitud >= LAT_SUR
      && z.centroLongitud >= LON_OESTE && z.centroLongitud <= LON_ESTE)
    .map((z) => ({
      id: z.id,
      nombre: z.nombre,
      region: z.region,
      y: MARGEN + (LAT_NORTE - z.centroLatitud) * pxPorGrado,
      x: ANCHO / 2 + (z.centroLongitud - LON_CENTRO) * pxPorGrado * Math.cos((z.centroLatitud * Math.PI) / 180) * ENSANCHE,
      n: conteo(z.id),
    }))
}
