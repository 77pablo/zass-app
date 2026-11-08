import type { Zona } from '../api/catalogo'

/** Como se dice en voz alta: «Centro, Temuco» para un barrio, y la comuna sola cuando ya es unica. */
export function etiquetaDeZona(z: Pick<Zona, 'nombre' | 'comuna' | 'tipo'>): string {
  return z.tipo === 'Barrio' ? `${z.nombre}, ${z.comuna}` : z.nombre
}

export interface ComunaConBarrios {
  comuna: Zona
  barrios: Zona[]
}

export interface GrupoDeRegion {
  region: string
  comunas: ComunaConBarrios[]
}

const alfabeto = new Intl.Collator('es-CL', { sensitivity: 'base' })

/**
 * Las zonas agrupadas por region, de norte a sur. El orden sale de la LATITUD de las comunas y no de una
 * lista escrita a mano: asi no hay una segunda lista de regiones que mantener ni que se desfase.
 * Dentro de cada region las comunas van por orden alfabetico, con sus barrios debajo.
 */
export function agruparPorRegion(zonas: Zona[]): GrupoDeRegion[] {
  const barriosDe = new Map<number, Zona[]>()
  for (const z of zonas) {
    if (z.tipo === 'Barrio' && z.padreId !== null) {
      barriosDe.set(z.padreId, [...(barriosDe.get(z.padreId) ?? []), z])
    }
  }

  const porRegion = new Map<string, ComunaConBarrios[]>()
  for (const z of zonas) {
    if (z.tipo !== 'Comuna') continue
    porRegion.set(z.region, [
      ...(porRegion.get(z.region) ?? []),
      { comuna: z, barrios: (barriosDe.get(z.id) ?? []).sort((a, b) => alfabeto.compare(a.nombre, b.nombre)) },
    ])
  }

  const latitudMedia = (cs: ComunaConBarrios[]) =>
    cs.reduce((suma, c) => suma + c.comuna.centroLatitud, 0) / cs.length

  return [...porRegion.entries()]
    .map(([region, comunas]) => ({
      region,
      comunas: comunas.sort((a, b) => alfabeto.compare(a.comuna.nombre, b.comuna.nombre)),
    }))
    // Latitud mayor = mas al norte (-18 esta mas al norte que -54).
    .sort((a, b) => latitudMedia(b.comunas) - latitudMedia(a.comunas))
}
