import { enviar, obtener } from '../../api/cliente'
import type { Usuario } from '../../sesion/contexto'
import type { MotivoDeReporte } from '../resenas/servicio'

/**
 * JQ-11. Lo que usa el panel interno: la mesa del Verificador (API-29, API-30, API-31) y los
 * documentos de respaldo de una credencial tomada (API-74, que llega con JQ-9).
 *
 * Todo vive bajo /api/interno, y la API responde 403 a quien no tiene el rol. Esconder el panel
 * en el frontend NO es la protección: es solo para no anunciar que existe.
 */

/** API-86 (25-nov-2026): quitar la verificación en dos pasos de una cuenta, con su motivo. */
export async function quitarVerificacion(correo: string, motivo: string): Promise<void> {
  await enviar('/api/interno/cuentas/quitar-mfa', { correo, motivo })
}

export const ROL_VERIFICADOR = 'verificador'
export const ROL_MODERADOR = 'moderador'
export const ROL_ADMINISTRADOR = 'administrador'

/**
 * Cada sección se muestra solo a quien tiene el rol que su API exige, para que nadie vea una
 * pantalla que le respondería 403:
 * - verificación (API-29 a 31, 74): política `Verificador` → rol `verificador`;
 * - moderación (API-52, 53): `RequireRole(Roles.Moderador)` → rol `moderador`;
 * - tarjetas y auditoría (API-59, 61): política `Administrador` → rol `administrador`.
 * Ninguno incluye a otro: un administrador sin `verificador` recibe 403 en la cola.
 */
export type SeccionInterna = 'verificacion' | 'moderacion' | 'administracion'

const ROL_DE: Record<SeccionInterna, string> = {
  verificacion: ROL_VERIFICADOR,
  moderacion: ROL_MODERADOR,
  administracion: ROL_ADMINISTRADOR,
}

export function puedeUsar(usuario: Usuario | null, seccion: SeccionInterna): boolean {
  return (usuario?.roles ?? []).includes(ROL_DE[seccion])
}

/** Quién puede ver el panel: quien pueda usar al menos una de sus secciones. */
export function puedeEntrarAlPanel(usuario: Usuario | null): boolean {
  return (Object.keys(ROL_DE) as SeccionInterna[]).some((s) => puedeUsar(usuario, s))
}

export type TipoDeCredencialInterna = 'Titulo' | 'Certificacion' | 'Curso' | 'Colegiatura'
export type ResultadoDeVerificacion = 'Verificada' | 'Rechazada' | 'AntecedentesSolicitados'
export type MotivoDeRechazo =
  | 'Ilegible' | 'NoCorresponde' | 'EntidadNoReconocida' | 'DocumentoVencido' | 'SospechaAdulteracion'

/** Los motivos del enum `MotivoRechazo` de la API, en el orden en que se ofrecen. */
export const MOTIVOS_DE_RECHAZO: MotivoDeRechazo[] = [
  'Ilegible', 'NoCorresponde', 'EntidadNoReconocida', 'DocumentoVencido', 'SospechaAdulteracion',
]

/** Un documento de respaldo (JQ-9, `MiDocumentoDto`). Nunca dice dónde está guardado. */
export interface DocumentoDeRespaldo {
  id: string
  nombre: string
  tipoMime: string
  tamanoBytes: number
  estado: 'Subiendo' | 'EnAnalisis' | 'Disponible' | 'RechazadoSeguridad'
  subidoEn: string
}

/** `CredencialEnColaDto`: vista interna, nunca pública. */
export interface CredencialEnCola {
  id: string
  prestador: string
  prestadorSlug: string
  tipo: TipoDeCredencialInterna
  nombre: string
  entidad: string | null
  entidadReconocida: boolean | null
  obtenidaEn: string
  venceEn: string | null
  vencimientoPasado: boolean
  documentos: number
  sinDocumentos: boolean
  enColaDesde: string
  reservadaPorMi: boolean
  reservadaHasta: string | null
  /** JQ-9: solo viene lleno cuando la credencial está tomada por quien pregunta. */
  respaldo?: DocumentoDeRespaldo[]
}

export interface Resolucion {
  id: string
  estado: string
  esperandoPrestador: boolean
}

export interface PedidoDeResolucion {
  resultado: ResultadoDeVerificacion
  motivo?: MotivoDeRechazo
  detalle?: string
}

/** API-29. La cola, la más antigua primero (se ordena aquí también: el orden es lo que se promete). */
export async function colaDeVerificacion(): Promise<CredencialEnCola[]> {
  const cola = await obtener<CredencialEnCola[]>('/api/interno/credenciales/cola')
  return [...cola].sort((a, b) => a.enColaDesde.localeCompare(b.enColaDesde))
}

/** API-30. Reserva por 30 minutos. Si otro la tomó, la API responde 409. */
export const tomarCredencial = (id: string) =>
  enviar<CredencialEnCola>(`/api/interno/credenciales/${encodeURIComponent(id)}/tomar`)

/** API-31. */
export const resolverCredencial = (id: string, pedido: PedidoDeResolucion) =>
  enviar<Resolucion>(`/api/interno/credenciales/${encodeURIComponent(id)}/resolver`, pedido)

/**
 * API-74. La dirección para ABRIR un documento en una pestaña nueva: la API lo sirve con la
 * cookie de sesión y solo a quien tiene la credencial tomada.
 */
export const urlDeDocumentoInterno = (credencialId: string, documentoId: string): string =>
  `/api/interno/credenciales/${encodeURIComponent(credencialId)}/documentos/${encodeURIComponent(documentoId)}`

/** Minutos enteros que quedan de la reserva, redondeados hacia arriba; 0 si ya venció. */
export function minutosRestantes(reservadaHasta: string | null, ahora: number): number {
  if (!reservadaHasta) return 0
  const ms = new Date(reservadaHasta).getTime() - ahora
  return ms <= 0 ? 0 : Math.ceil(ms / 60_000)
}

// ---------------------------------------------------------------- JQ-12 · moderación


/**
 * `FilaDeCola` de API-52. Trae la reseña y el motivo del reporte, pero NO quién reportó: el plan lo
 * pide («quién reportó, sin su correo») y la API no lo da. Queda como pregunta en el PR.
 */
export interface ReporteEnCola {
  reporteId: string
  resenaId: string
  motivo: MotivoDeReporte
  detalle: string | null
  estado: 'Abierto' | 'EnRevision' | 'ResueltoMantiene' | 'ResueltoRetira'
  creadoEn: string
  calificacion: number
  comentario: string
  estadoResena: 'Publicada' | 'OcultaEnModeracion' | 'Retirada'
  /** El prestador reseñado. */
  perfilNombre: string
}

export type DecisionDeModeracion = 'mantener' | 'retirar'

/** Lo que exige MOD-11 al motivo de la decisión (ReglasDeResenas.MotivoResolucionMinimo). */
export const MOTIVO_MODERACION_MINIMO = 10

/** API-52. Los reportes abiertos, sin los propios, el más antiguo primero. */
export async function colaDeReportes(): Promise<ReporteEnCola[]> {
  const cola = await obtener<ReporteEnCola[]>('/api/interno/reportes/cola')
  return [...cola].sort((a, b) => a.creadoEn.localeCompare(b.creadoEn))
}

/** API-53. Responde 204. */
export const resolverReporte = (id: string, decision: DecisionDeModeracion, motivo: string) =>
  enviar<void>(`/api/interno/reportes/${encodeURIComponent(id)}/resolver`, { decision, motivo })

/** «19 de noviembre, 14:05», en hora de Chile. */
export function fechaYHora(iso: string): string {
  return new Intl.DateTimeFormat('es-CL', {
    day: 'numeric', month: 'long', hour: '2-digit', minute: '2-digit', timeZone: 'America/Santiago',
  }).format(new Date(iso))
}

// ---------------------------------------------------------------- JQ-14 · indicadores (API-60)

export interface Indicadores {
  verificacion: {
    enCola: number
    /** `CreadaEn` de la más antigua en cola (el envío a revisión no tiene fecha). */
    masAntiguaDesde: string | null
    resueltas: number
    rechazadas: number
    /** Entre 0 y 1; nula si no hubo resoluciones en 30 días. */
    tasaDeRechazo: number | null
    /** Desde la CREACIÓN de la credencial hasta su resolución, no desde el envío. */
    horasPromedioDesdeCreacion: number | null
  }
  avisos: { correosFallidos: number }
  reportesAbiertos: number
  tarjetasEmitidasSinActivar: number
  calculadoEn: string
}

export const indicadores = () => obtener<Indicadores>('/api/interno/indicadores')

/** «33 %», redondeado a entero. */
export const porcentaje = (x: number) => `${Math.round(x * 100)} %`

/** Horas como «14 h» o, desde 48 h, «3,5 días». */
export function duracion(horas: number): string {
  // Bajo una hora, «0 h» parece un dato vacío o un error.
  if (horas < 1) return 'menos de 1 h'
  if (horas < 48) return `${Math.round(horas)} h`
  const dias = Math.round((horas / 24) * 10) / 10
  return `${dias.toLocaleString('es-CL')} días`
}
// ---------------------------------------------------------------- JQ-13 · tarjetas y auditoría

/**
 * API-59. El código viaja UNA vez: la API guarda solo su hash y no lo vuelve a dar. Quien lo
 * recibe lo tiene solo en memoria (estado de React): nada de localStorage, sessionStorage ni URL.
 */
export interface TarjetaEmitida {
  segmento: string
  codigo: string
  venceEn: string
}

export const emitirTarjeta = () => enviar<TarjetaEmitida>('/api/interno/tarjetas')

/** Una fila de API-61, con lo que la API devuelve y nada más. */
export interface FilaDeAuditoria {
  id: number
  accion: string
  entidad: string
  entidadId: string | null
  actorId: string | null
  /** jsonb, tal como lo guardó quien auditó. */
  contexto: string | null
  ipOrigen: string | null
  ocurridoEn: string
}

export interface PaginaDeAuditoria {
  items: FilaDeAuditoria[]
  /** Cursor para la página siguiente (más antigua); nulo si no hay más. */
  siguiente: string | null
}

/** API-61 exige EXACTAMENTE uno de los dos filtros. */
export type FiltroDeAuditoria =
  | { por: 'entidad'; entidad: string; entidadId: string }
  | { por: 'actor'; actorId: string }

export function consultarAuditoria(filtro: FiltroDeAuditoria, cursor: string | null = null): Promise<PaginaDeAuditoria> {
  const q = new URLSearchParams()
  if (filtro.por === 'entidad') {
    q.set('entidad', filtro.entidad)
    q.set('entidadId', filtro.entidadId)
  } else {
    q.set('actorId', filtro.actorId)
  }
  if (cursor) q.set('cursor', cursor)
  return obtener<PaginaDeAuditoria>(`/api/interno/auditoria?${q.toString()}`)
}

/** Un Guid como lo acepta la ruta de la API (`Guid?`): se revisa antes de mandarlo. */
export const esGuid = (s: string) => /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(s.trim())
