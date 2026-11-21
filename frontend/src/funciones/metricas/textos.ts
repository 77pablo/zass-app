/* JQ-8 · «Mi actividad». PROPUESTA: los aprueba Joaquín en «Textos nuevos» del PR. */
export const TITULO = 'Mi actividad'
export const ENLACE_MENU = 'Mi actividad'
export const INTRODUCCION = 'Cuánta gente te contactó desde ZASS! y cuántas veces escanearon tus tarjetas.'
export const ERROR_CARGAR = 'No pudimos cargar tu actividad.'
export const REINTENTAR = 'Reintentar'
export const NO_PRESTADOR_ANTES = 'Esta pantalla es para quienes ofrecen servicios. Puedes '
export const NO_PRESTADOR_ENLACE = 'ofrecer tus servicios'
export const NO_PRESTADOR_DESPUES = ' con esta misma cuenta.'
export const CONTACTOS = 'Contactos recibidos'
// API-46 deja un solo contacto abierto por persona y prestador: tocar dos veces no suma dos.
export const CONTACTOS_AYUDA =
  'Las personas que te escribieron, llamaron o mandaron un correo desde tu ficha. Si alguien toca dos veces, cuenta una.'
export const ESCANEOS = 'Escaneos de tus tarjetas'
export const ULTIMOS_7 = 'Últimos 7 días'
export const ULTIMOS_30 = 'Últimos 30 días'
export const DESDE_SIEMPRE = 'Desde el comienzo'
export const POR_CANAL = 'Por canal, últimos 30 días'
export const CANAL = { whatsapp: 'WhatsApp', telefono: 'Teléfono', correo: 'Correo', formulario: 'Formulario' } as const
