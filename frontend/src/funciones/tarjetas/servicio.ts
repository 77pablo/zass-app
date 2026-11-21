import { enviar, obtener } from '../../api/cliente'

/**
 * MOD-12, lo del dueño: activar (API-55) y mis tarjetas (API-56 a API-58). La emisión (API-59)
 * es del panel interno.
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

/**
 * Formato del segmento impreso (`7KQ2-9XRM`): el mismo alfabeto que la API, sin 0, O, 1, I ni L.
 * Solo sirve para no ofrecer el formulario con una dirección que ninguna tarjeta puede tener;
 * quien decide si la tarjeta existe es la API.
 */
const FORMA_DE_SEGMENTO = /^[A-HJKMNP-Z2-9]{4}-[A-HJKMNP-Z2-9]{4}$/

/** El segmento en mayúsculas si tiene la forma de uno impreso; si no, null. */
export function segmentoValido(texto: string | undefined): string | null {
  const s = (texto ?? '').trim().toUpperCase()
  return FORMA_DE_SEGMENTO.test(s) ? s : null
}

const LETRA_DE_CODIGO = /^[A-HJKMNP-Z2-9]$/

/**
 * El código de activación como lo espera la API (`ABCD-EFGH-JKMN`), o null si no son 12 letras
 * y números del alfabeto. Acepta lo que la persona escriba: minúsculas, espacios, con o sin
 * guiones, porque en el papel va en grupos y cada uno lo copia a su manera.
 */
export function normalizarCodigo(texto: string): string | null {
  const limpio = texto.toUpperCase().replace(/[\s-]/g, '')
  if (limpio.length !== 12 || ![...limpio].every((c) => LETRA_DE_CODIGO.test(c))) return null
  return `${limpio.slice(0, 4)}-${limpio.slice(4, 8)}-${limpio.slice(8)}`
}

/**
 * API-55. Va por /api (con sesión y token CSRF, como todo lo que escribe) y no por /t/: en
 * producción /t/ lo atiende una Function que solo deja pasar GET y nunca reenvía cookies.
 */
export const activarTarjeta = (segmento: string, codigo: string) =>
  enviar<{ mensaje: string; urlFicha: string }>(
    `/api/mi/tarjetas/${encodeURIComponent(segmento)}/activar`, { codigo },
  )

/** Lo que «Mis tarjetas» recibe en `state` al llegar desde una activación que salió bien. */
export const LLEGA_ACTIVADA = 'tarjeta-activada'

export const llegaActivada = (estado: unknown): boolean =>
  (estado as { aviso?: unknown } | null)?.aviso === LLEGA_ACTIVADA
