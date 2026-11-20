import { enviar, obtener } from '../../api/cliente'

/**
 * MOD-12, lo del dueño (API-56 a API-58). La activación y la emisión (API-55, API-59) no
 * existen todavía.
 */

/** La API nunca manda el hash ni nada del código de activación: este tipo no tiene dónde. */
export interface MiTarjeta {
  id: string
  urlEstable: string
  estado: 'Activa' | 'Desactivada' | 'Emitida'
  activadaEn: string | null
  desactivadaEn: string | null
}

export interface ConteoPorMedio { nfc: number; qr: number; total: number }

export interface EstadisticasDeTarjeta {
  ultimos7Dias: ConteoPorMedio
  ultimos30Dias: ConteoPorMedio
  total: ConteoPorMedio
}

/**
 * Siempre zass.cl, nunca el origen de la página: el QR y el enlace son lo que va impreso o lo
 * que escanea otra persona, y en local o en una vista previa apuntarían a donde no deben.
 * No hay otra constante con la URL pública en el frontend (la del backend es solo del correo).
 */
export const URL_DE_TARJETAS = 'https://zass.cl'

/** Lo que se muestra: `zass.cl/t/XXXX-XXXX`, sin el https. */
export const enlaceVisible = (urlEstable: string) => `zass.cl/t/${urlEstable}`

export const enlaceDeTarjeta = (urlEstable: string) =>
  `${URL_DE_TARJETAS}/t/${encodeURIComponent(urlEstable)}`

/** Lo que codifica el QR: `?m=qr` es lo que API-54 lee para contar el escaneo como QR. */
export const enlaceQr = (urlEstable: string) => `${enlaceDeTarjeta(urlEstable)}?m=qr`

/** «14 de noviembre de 2026», en hora de Chile: el mismo formato que la suscripción en Mi ficha. */
export function fechaDeTarjeta(iso: string): string {
  return new Intl.DateTimeFormat('es-CL', {
    day: 'numeric', month: 'long', year: 'numeric', timeZone: 'America/Santiago',
  }).format(new Date(iso))
}

export const misTarjetas = () => obtener<MiTarjeta[]>('/api/mi/tarjetas')

export const desactivarTarjeta = (id: string) =>
  enviar<void>(`/api/mi/tarjetas/${encodeURIComponent(id)}/desactivar`)

export const estadisticasDeTarjeta = (id: string) =>
  obtener<EstadisticasDeTarjeta>(`/api/mi/tarjetas/${encodeURIComponent(id)}/estadisticas`)
