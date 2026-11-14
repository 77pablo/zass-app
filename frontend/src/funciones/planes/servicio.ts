import { enviar, obtener } from '../../api/cliente'

/** API-66. Un plan del catálogo, tal como lo manda el servidor. */
export interface Plan {
  id: number
  codigo: string
  nombre: string
  descripcion: string | null
  precioMensual: number
  diasPrueba: number
  maxServicios: number
  maxTarjetas: number
  permiteCredenciales: boolean
}

/** API-66. Público: el catálogo y cuántos cupos de fundador quedan. */
export interface CatalogoDePlanes {
  planes: Plan[]
  cuposFundadorDisponibles: number
}

/**
 * API-67. Los tres números van separados a propósito: un total sin el precio de lista y el
 * descuento al lado no se puede comprobar.
 */
export interface Cotizacion {
  codigoPlan: string
  nombrePlan: string
  precioMensual: number
  descuento: number
  total: number
  cupon: string | null
  porcentajeDescuento: number | null
  esElPrimerMesQueSePaga: boolean
  detalle: string
}

/** API-68 y API-69. El cobro o la inscripción ya empezados. */
export interface IntentoDePago {
  id: string
  codigoPlan: string
  precioMensual: number
  descuento: number
  total: number
  cupon: string | null
  estado: string
  urlParaPagar: string | null
  referenciaPasarela: string | null
  /** Falso cuando es la respuesta a un envío repetido: la pantalla no dice dos veces lo mismo. */
  nueva: boolean
}

/** Los 50 del plan fundador. El contador de la página se compara contra esto. */
export const CUPOS_FUNDADOR_TOTALES = 50

export function planes(): Promise<CatalogoDePlanes> {
  return obtener<CatalogoDePlanes>('/api/planes')
}

export function cotizar(codigoPlan: string, codigoCupon?: string): Promise<Cotizacion> {
  return enviar<Cotizacion>('/api/mi/cobros/cotizar', { codigoPlan, codigoCupon })
}

/**
 * La clave de idempotencia (CC-04) la genera la PANTALLA al montarse y la repite en cada
 * reintento del mismo envío. Si se generara por clic, no protegería de nada: dos clics
 * traerían dos claves y el servidor los tomaría por dos intenciones distintas.
 */
export function iniciarCobro(
  codigoPlan: string, clave: string, codigoCupon?: string,
): Promise<IntentoDePago> {
  return enviar<IntentoDePago>(
    '/api/mi/cobros', { codigoPlan, codigoCupon }, 'POST', { 'Idempotency-Key': clave })
}

/** API-69. Toma el cupo inscribiendo el medio de pago. Sin cuerpo: no hay nada que elegir. */
export function tomarCupoFundador(clave: string): Promise<IntentoDePago> {
  return enviar<IntentoDePago>(
    '/api/mi/suscripcion/fundador', undefined, 'POST', { 'Idempotency-Key': clave })
}

/** Pesos chilenos, como se escriben en Chile: $5.990. */
export function pesos(monto: number): string {
  return `$${new Intl.NumberFormat('es-CL').format(monto)}`
}
