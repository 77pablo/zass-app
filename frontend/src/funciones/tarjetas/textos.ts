/**
 * «Mis tarjetas». Textos aprobados por Joaquín el 14-nov-2026 (PR de JQ-4, parte A). Si se
 * cambia uno, va en «Textos nuevos» del PR.
 */
export const TITULO = 'Mis tarjetas'

export const INTRODUCCION =
  'Tu tarjeta ZASS! abre tu ficha cuando la acercas a un teléfono. Si la pierdes, ' +
  'desactívala aquí: dejará de funcionar al instante.'

export const SIN_TARJETAS = 'Todavía no tienes tarjetas activas.'

/** Va partido porque el medio es un enlace a /ofrecer-servicios, el mismo que usa Mi agenda. */
export const NO_PRESTADOR_ANTES = 'Las tarjetas son para quienes ofrecen servicios. Puedes '
export const NO_PRESTADOR_ENLACE = 'ofrecer tus servicios'
export const NO_PRESTADOR_DESPUES = ' con esta misma cuenta.'

export const ACTIVA = 'Activa'
export const DESACTIVADA = 'Desactivada'
export const activadaEl = (fecha: string) => `Activada el ${fecha}`
export const desactivadaEl = (fecha: string) => `Desactivada el ${fecha}`

export const MOSTRAR_QR = 'Mostrar código QR'
export const OCULTAR_QR = 'Ocultar código QR'
export const AYUDA_QR = 'Si el teléfono no lee la tarjeta, puede escanear este código.'
export const altDelQr = (urlEstable: string) => `Código QR de la tarjeta ${urlEstable}`

export const DESACTIVAR = 'Desactivar tarjeta'
export const confirmarDesactivar = (urlEstable: string) =>
  `¿Desactivar la tarjeta ${urlEstable}? Dejará de abrir tu ficha de inmediato y no se puede volver a activar.`
export const SI_DESACTIVAR = 'Sí, desactivar'
export const CANCELAR = 'Cancelar'

export const DESACTIVADA_OK = 'Tarjeta desactivada. Ya no abre tu ficha.'

export const ERROR_CARGAR = 'No pudimos cargar tus tarjetas. Inténtalo de nuevo.'
export const ERROR_DESACTIVAR = 'No pudimos desactivar la tarjeta. Inténtalo de nuevo.'

export const ESCANEOS = 'Escaneos'
export const ULTIMOS_7 = 'Últimos 7 días'
export const ULTIMOS_30 = 'Últimos 30 días'
export const DESDE_SIEMPRE = 'Desde que la activaste'
export const CON_NFC = 'Con NFC'
export const CON_QR = 'Con QR'

export const QUE_CONTAMOS = 'Contamos cuántas veces se abrió, no quién la abrió.'
