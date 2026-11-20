import type { MotivoDeRechazo, ResultadoDeVerificacion, TipoDeCredencialInterna } from './servicio'

/*
 * JQ-11. Textos del panel interno. PROPUESTA: los aprueba Joaquín (van en «Textos nuevos» del PR).
 * Los mensajes de error de la API (409, 422, validación) NO se reescriben aquí: se muestran tal cual.
 */

export const ENLACE_MENU = 'Panel interno'

export const TITULO_PANEL = 'Panel interno'
export const INTRO_PANEL = 'Herramientas para operar ZASS!. Lo que hagas aquí queda registrado.'
export const SECCION_VERIFICACION = 'Verificación de credenciales'
export const SECCION_VERIFICACION_BAJADA = 'Revisa los títulos y certificados que mandan los prestadores.'

export const TITULO_MESA = 'Verificación de credenciales'
export const ERROR_COLA = 'No pudimos cargar la cola.'
export const REINTENTAR = 'Reintentar'
export const COLA_VACIA = 'No hay credenciales esperando revisión.'
export const VOLVER_AL_PANEL = 'Volver al panel'

export const ETIQUETA_EN_COLA_DESDE = 'En cola desde'
export const ETIQUETA_ENTIDAD = 'Entidad'
export const SIN_ENTIDAD = 'Sin entidad'
export const ENTIDAD_NO_RECONOCIDA = 'Entidad no reconocida'
export const SIN_DOCUMENTOS = 'Sin documentos'
export const VENCIMIENTO_PASADO = 'Vencimiento pasado'
export const documentosEnCola = (n: number) => (n === 1 ? '1 documento' : `${n} documentos`)

export const BOTON_TOMAR = 'Tomar'
export const BOTON_SEGUIR = 'Seguir revisando'
export const TOMANDO = 'Tomando…'

export const reservaRestante = (min: number) =>
  min === 1 ? 'Te queda 1 minuto de reserva.' : `Te quedan ${min} minutos de reserva.`
export const RESERVA_VENCIDA = 'Tu reserva venció. Vuelve a la cola y tómala de nuevo para resolverla.'
export const VOLVER_A_LA_COLA = 'Volver a la cola'

export const TITULO_DOCUMENTOS = 'Documentos de respaldo'
export const SIN_DOCUMENTOS_DETALLE =
  'Esta credencial llegó sin documentos. Si la verificas, explica cómo la comprobaste.'
export const DOCUMENTOS_NO_LLEGARON =
  'La credencial tiene documentos, pero no llegaron en la respuesta. Revisa que esté al día la versión de la API.'
export const ABRIR_DOCUMENTO = 'Abrir'
export const ABRE_EN_PESTANA = '(se abre en una pestaña nueva)'

export const TITULO_RESOLVER = 'Resolver'
export const ETIQUETA_RESULTADO = '¿Qué haces con esta credencial?'
export const ETIQUETA_MOTIVO = 'Motivo del rechazo'
export const ELEGIR_MOTIVO = 'Elige un motivo'
export const ETIQUETA_DETALLE = 'Detalle'
export const AYUDA_DETALLE_RECHAZO = 'El prestador lo va a leer.'
export const AYUDA_DETALLE_ANTECEDENTES = 'Escribe qué le falta mandar.'
export const AYUDA_DETALLE_VERIFICAR = 'Opcional.'
export const FALTA_MOTIVO = 'Elige el motivo del rechazo.'
export const BOTON_RESOLVER = 'Guardar resolución'
export const RESOLVIENDO = 'Guardando…'

export const RESULTADOS: { valor: ResultadoDeVerificacion; texto: string }[] = [
  { valor: 'Verificada', texto: 'Verificar' },
  { valor: 'Rechazada', texto: 'Rechazar' },
  { valor: 'AntecedentesSolicitados', texto: 'Pedir antecedentes' },
]

export const MOTIVO: Record<MotivoDeRechazo, string> = {
  Ilegible: 'El documento no se lee',
  NoCorresponde: 'El documento no corresponde a la credencial',
  EntidadNoReconocida: 'La entidad no está reconocida',
  DocumentoVencido: 'El documento está vencido',
  SospechaAdulteracion: 'Sospecha de adulteración',
}

export const TIPO: Record<TipoDeCredencialInterna, string> = {
  Titulo: 'Título',
  Certificacion: 'Certificación',
  Curso: 'Curso',
  Colegiatura: 'Colegiatura',
}

export const RESUELTA: Record<ResultadoDeVerificacion, string> = {
  Verificada: 'Listo: la credencial quedó verificada.',
  Rechazada: 'Listo: la credencial quedó rechazada y el prestador recibirá el motivo.',
  AntecedentesSolicitados: 'Listo: le pedimos antecedentes al prestador.',
}
