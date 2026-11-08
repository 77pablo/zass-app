import { obtener } from './cliente'

/** API-02. */
export interface Rubro {
  id: number
  codigo: string
  nombre: string
  icono: string | null
}

/** API-04. El centroide es dato publico: el centro de un barrio, no la casa de nadie. */
export interface Zona {
  id: number
  nombre: string
  sector: string
  comuna: string
  centroLatitud: number
  centroLongitud: number
  /** La comuna es la unidad nacional; el barrio es un nivel opcional dentro de una comuna grande. */
  tipo: 'Comuna' | 'Barrio'
  region: string
  /** De que comuna es este barrio; nulo para una comuna. */
  padreId: number | null
}

/** API-03. Un servicio que ZASS! sugiere dentro de un rubro. */
export interface ServicioDeCatalogo {
  id: number
  nombre: string
  descripcion: string | null
  esCatalogo: boolean
}

export function listarRubros(): Promise<Rubro[]> {
  return obtener<Rubro[]>('/api/rubros')
}

export function listarZonas(): Promise<Zona[]> {
  return obtener<Zona[]>('/api/zonas')
}

export function serviciosDeRubro(rubroId: number): Promise<ServicioDeCatalogo[]> {
  return obtener<ServicioDeCatalogo[]>(`/api/rubros/${rubroId}/servicios`)
}
