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

// --- Activar una tarjeta (CU-19). Textos nuevos del 21-nov-2026: van en «Textos nuevos» del PR.

export const ACTIVAR_PESTANA = 'Activar tarjeta'

export const ACTIVAR_PARA_QUIEN =
  'Si te la acaban de entregar, actívala aquí con el código que venía con ella. ' +
  'Si no es tuya, todavía no lleva a ningún perfil.'

export const ENTRAR_PARA_ACTIVAR = 'Entra para activarla'

export const NO_OFRECE_SERVICIOS = 'Esta cuenta no ofrece servicios.'

export const FICHA_SIN_PUBLICAR =
  'Para activar la tarjeta, primero publica tu ficha: la tarjeta necesita una ficha a la que llevar. ' +
  'Cuando esté publicada, vuelve a acercar la tarjeta al teléfono.'
export const IR_A_MI_FICHA = 'Ir a mi ficha'

export const ETIQUETA_CODIGO = 'Código de activación'
export const AYUDA_CODIGO =
  'Son 12 letras y números, en el papel que venía aparte de la tarjeta. Puedes escribirlo con o sin guiones.'
export const CODIGO_INCOMPLETO = 'El código tiene 12 letras y números. Revisa que esté completo.'

export const ACTIVAR = 'Activar tarjeta'
export const ACTIVANDO = 'Activando…'

/**
 * El único texto para un código que no sirve, sea cual sea la razón: la API responde igual a
 * una tarjeta que no existe, ya activada, con el código vencido o equivocado, o bloqueada por
 * intentos (CU-19 4B.3), y la pantalla no puede saber más que la API.
 */
export const CODIGO_NO_VALIDO =
  'El código no es válido. Revisa que esté escrito igual que en el papel e inténtalo de nuevo.'

/** Va siempre a la vista, no solo cuando algo falla: así no insinúa nada de esta tarjeta. */
export const REGLAS_DEL_CODIGO =
  'Por seguridad, después de 5 intentos fallidos la tarjeta no se puede activar durante una hora. ' +
  'Si perdiste el código o ya pasaron 30 días desde que te lo dieron, pide uno nuevo a quien te entregó la tarjeta.'

/** El 429 del límite por IP (tasa A): ese sí llega distinto, y no dice nada de la tarjeta. */
export const MUCHOS_INTENTOS =
  'Hiciste muchos intentos seguidos. Por seguridad, espera unos 15 minutos y vuelve a intentarlo.'

export const ERROR_ACTIVAR = 'No pudimos activar la tarjeta en este momento. Inténtalo de nuevo en un rato.'

export const ACTIVADA_OK =
  'Listo: tu tarjeta quedó activa. Pruébala acercándola a otro teléfono; debería abrir tu ficha.'
