import type {
  DecisionDeModeracion, MotivoDeRechazo, ResultadoDeVerificacion, TipoDeCredencialInterna,
} from './servicio'
import { MOTIVOS_DE_REPORTE } from '../resenas/servicio'

/*
 * JQ-11. Textos del panel interno. PROPUESTA: los aprueba Joaquín (van en «Textos nuevos» del PR).
 * Los mensajes de error de la API (409, 422, validación) NO se reescriben aquí: se muestran tal cual.
 */

export const ENLACE_MENU = 'Panel interno'

export const TITULO_PANEL = 'Panel interno'
export const INTRO_PANEL = 'Herramientas para operar ZASS! Todo lo que hagas aquí queda registrado.'
export const SECCION_VERIFICACION = 'Verificación de credenciales'
export const SECCION_VERIFICACION_BAJADA = 'Revisa los títulos y certificados que mandan los prestadores.'

export const TITULO_MESA = 'Verificación de credenciales'
export const ERROR_COLA = 'No pudimos cargar la cola.'
export const REINTENTAR = 'Reintentar'
export const COLA_VACIA = 'No hay credenciales esperando revisión.'
export const VOLVER_AL_PANEL = 'Volver al panel'

export const ETIQUETA_EN_COLA_DESDE = 'Creada el'
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
  'No pudimos cargar los documentos. Recarga la página y, si sigue igual, avisa al equipo técnico.'
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

// ---------------------------------------------------------------- JQ-12 · moderación (PROPUESTA)

export const SECCION_MODERACION = 'Reseñas reportadas'
export const SECCION_MODERACION_BAJADA = 'Decide si una reseña reportada se mantiene o se retira.'
export const SIN_SECCIONES = 'Tu cuenta no tiene ninguna herramienta asignada.'

export const TITULO_REPORTES = 'Reseñas reportadas'
export const ERROR_REPORTES = 'No pudimos cargar los reportes.'
export const REPORTES_VACIOS = 'No hay reportes abiertos.'
export const RESENA_DE = (prestador: string) => `Reseña sobre ${prestador}`
export const estrellas = (n: number) => (n === 1 ? '1 estrella' : `${n} estrellas`)
export const REPORTADA_EL = 'Reportada el'
export const ETIQUETA_MOTIVO_REPORTE = 'Motivo del reporte'
export const ETIQUETA_DETALLE_REPORTE = 'Lo que escribió quien reportó'
export const RESENA_OCULTA = 'Oculta mientras se revisa'
export const ETIQUETA_DECISION = 'Decisión'
export const ETIQUETA_MOTIVO_DECISION = 'Motivo de la decisión'
export const ayudaMotivoDecision = (min: number) => `Al menos ${min} caracteres. Queda registrado.`
export const faltaMotivoDecision = (min: number) => `Escribe el motivo de la decisión (al menos ${min} caracteres).`
export const BOTON_DECIDIR = 'Guardar decisión'

export const DECISIONES: { valor: DecisionDeModeracion; texto: string }[] = [
  { valor: 'mantener', texto: 'Mantener la reseña' },
  { valor: 'retirar', texto: 'Retirar la reseña' },
]

export const DECIDIDA: Record<DecisionDeModeracion, string> = {
  mantener: 'Listo: la reseña se mantiene.',
  retirar: 'Listo: la reseña quedó retirada.',
}


/** Las mismas etiquetas que ve quien reporta (funciones/resenas, RN-S5-07): no se inventan otras. */
export const etiquetaDeMotivoDeReporte = (m: string) =>
  MOTIVOS_DE_REPORTE.find((x) => x.valor === m)?.etiqueta ?? m

// ---------------------------------------------------------------- JQ-14 · indicadores (PROPUESTA)

export const SECCION_INDICADORES = 'Indicadores'
export const SECCION_INDICADORES_BAJADA = 'Cómo va la operación: cola, rechazos, avisos y tarjetas.'
export const TITULO_INDICADORES = 'Indicadores'
export const ERROR_INDICADORES = 'No pudimos cargar los indicadores.'
export const CALCULADO_EL = 'Calculado el'
export const GRUPO_VERIFICACION = 'Verificación'
export const GRUPO_OPERACION = 'Operación'
export const IND_EN_COLA = 'Credenciales en cola'
export const IND_MAS_ANTIGUA = 'La más antigua en cola, creada el'
export const IND_TIEMPO = 'Tiempo promedio desde que se creó la credencial hasta que se resolvió (30 días)'
export const IND_TIEMPO_NOTA = 'Incluye el tiempo en borrador: el envío a revisión no queda con fecha.'
export const IND_TASA = 'Tasa de rechazo (30 días)'
export const indResueltas = (r: number, n: number) => `${r} de ${n} resueltas`
export const IND_FALLIDOS = 'Correos fallidos (7 días)'
export const IND_REPORTES = 'Reportes de reseñas sin resolver'
export const IND_TARJETAS = 'Tarjetas emitidas sin activar'
export const SIN_DATOS_AUN = 'Sin datos todavía'
