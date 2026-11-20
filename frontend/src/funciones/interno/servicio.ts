import { enviar, obtener } from '../../api/cliente'
import type { Usuario } from '../../sesion/contexto'

/**
 * JQ-11. Lo que usa el panel interno: la mesa del Verificador (API-29, API-30, API-31) y los
 * documentos de respaldo de una credencial tomada (API-74, que llega con JQ-9).
 *
 * Todo vive bajo /api/interno, y la API responde 403 a quien no tiene el rol. Esconder el panel
 * en el frontend NO es la protección: es solo para no anunciar que existe.
 */

export const ROL_VERIFICADOR = 'verificador'
export const ROL_ADMINISTRADOR = 'administrador'

/** Quién puede ver el panel: el mismo criterio que la política `Verificador` de la API, más el administrador. */
export function puedeEntrarAlPanel(usuario: Usuario | null): boolean {
  const roles = usuario?.roles ?? []
  return roles.includes(ROL_VERIFICADOR) || roles.includes(ROL_ADMINISTRADOR)
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
