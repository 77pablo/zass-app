import { obtener } from './cliente'

/** API-62. Lo que la portada, los barrios y los oficios necesitan para mostrar gente real. */
export interface ResumenDeComunidad {
  total: number
  porZona: { zonaId: number; personas: number }[]
  porRubro: { rubroId: number; personas: number }[]
  recienLlegados: RecienLlegado[]
}

export interface RecienLlegado {
  slug: string
  nombrePublico: string
  rubro: string | null
  zona: string | null
  verificada: boolean
}

export function resumenDeComunidad(): Promise<ResumenDeComunidad> {
  return obtener<ResumenDeComunidad>('/api/comunidad')
}

/** Cuantas personas hay en una zona; 0 si el resumen no la menciona (o aun no cargo). */
export function personasEnZona(r: ResumenDeComunidad | null, zonaId: number): number {
  return r?.porZona.find((z) => z.zonaId === zonaId)?.personas ?? 0
}

/** Cuantas personas hay en un rubro; 0 si el resumen no lo menciona (o aun no cargo). */
export function personasEnRubro(r: ResumenDeComunidad | null, rubroId: number): number {
  return r?.porRubro.find((x) => x.rubroId === rubroId)?.personas ?? 0
}
