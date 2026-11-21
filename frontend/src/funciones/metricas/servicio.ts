import { obtener } from '../../api/cliente'

/**
 * JQ-8 · MI-METRICAS. Contactos recibidos y escaneos de TODAS las tarjetas del prestador. Las
 * vistas de la ficha no están: nada las registra todavía (pregunta abierta en el PR).
 */
export interface ConteoEnVentanas { ultimos7Dias: number; ultimos30Dias: number; total: number }

export interface MisMetricas {
  contactos: ConteoEnVentanas
  contactosPorCanal30Dias: { whatsapp: number; telefono: number; correo: number; formulario: number }
  escaneos: ConteoEnVentanas
  calculadoEn: string
}

export const misMetricas = () => obtener<MisMetricas>('/api/mi/metricas')
