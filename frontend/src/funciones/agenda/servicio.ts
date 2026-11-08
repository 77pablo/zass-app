import { enviar, obtener } from '../../api/cliente'

/** Los enums viajan como texto (JsonStringEnumConverter), no como numero. */
export type EstadoDeCita =
  | 'Solicitada' | 'Confirmada' | 'Rechazada' | 'Cancelada'
  | 'Reprogramada' | 'PendienteAsistencia' | 'Realizada' | 'NoAsistio'

export type ModoDeAgenda = 'Automatico' | 'Manual'

/**
 * `libre`, `ocupado` o `bloqueado`. Nunca con quien esta ocupado: CU-12 paso 3 lo prohibe, y
 * el backend ni siquiera tiene el campo.
 */
export type EstadoDeBloque = 'libre' | 'ocupado' | 'bloqueado'

/** API-35. Los instantes vienen en UTC; se muestran en hora de Chile (ver `horaDeChile`). */
export interface Bloque {
  id: string
  inicioEn: string
  finEn: string
  estado: EstadoDeBloque
}

export interface Calendario {
  prestadorServicioId: string
  perfilPrestadorId: string
  servicio: string
  modoAgenda: ModoDeAgenda
  duracionMinutos: number
  /** RN-S4-02. Los bloques mas cercanos que esto ya vienen como `ocupado`. */
  anticipacionMinimaHoras: number
  bloques: Bloque[]
}

/** API-36 y API-37. */
export interface Cita {
  id: string
  codigo: string
  estado: EstadoDeCita
  modo: ModoDeAgenda
  inicioEn: string
  finEn: string
  servicio: string
  prestadorServicioId: string
  perfilPrestadorId: string
  prestador: string
  notaBuscador: string | null
  /** Solo en las manuales: si el prestador no responde antes, la hora se libera (RN-S4-03). */
  expiraEn: string | null
  /** False cuando la clave ya existia: el servidor devolvio la misma cita (flujo 4B). */
  nueva: boolean
}

/**
 * Una clave por INTENCION: se genera al abrir el formulario y se reusa en cada intento del mismo
 * envio. Generarla en el clic haria que un doble clic fueran dos intenciones distintas, que es
 * justo lo que la clave existe para distinguir (CC-04).
 */
export function nuevaClaveDeIdempotencia(): string {
  return globalThis.crypto.randomUUID()
}

const IDEMPOTENCIA = 'Idempotency-Key'

/** API-35. Publica: elegir hora no exige cuenta. */
export function calendarioDe(slug: string, prestadorServicioId: string, dias = 56): Promise<Calendario> {
  const desde = new Date()
  const hasta = new Date(desde.getTime() + dias * 24 * 3600 * 1000)
  const q = new URLSearchParams({
    servicio: prestadorServicioId,
    desde: desde.toISOString(),
    hasta: hasta.toISOString(),
  })
  return obtener<Calendario>(`/api/prestadores/${encodeURIComponent(slug)}/bloques?${q}`)
}

/** API-36. La clave la pone quien llama, porque tiene que sobrevivir a los reintentos. */
export function agendar(bloqueId: string, clave: string, nota: string | null): Promise<Cita> {
  return enviar<Cita>(
    '/api/citas',
    { bloqueId, notaBuscador: nota && nota.trim() !== '' ? nota.trim() : null },
    'POST',
    { [IDEMPOTENCIA]: clave },
  )
}

/** API-37. Las que pedi y las que me pidieron, ordenadas por fecha. */
export function misCitas(): Promise<Cita[]> {
  return obtener<Cita[]>('/api/mi/citas')
}

/** API-38. Solo el prestador. */
export const confirmarCita = (id: string) => enviar<Cita>(`/api/citas/${id}/confirmar`)

/** API-39. Solo el prestador. El motivo es opcional. */
export const rechazarCita = (id: string, motivo: string | null) =>
  enviar<Cita>(`/api/citas/${id}/rechazar`, { motivo })

/** API-40. Con menos de 12 h el motivo es obligatorio (RN-S4-04). */
export const cancelarCita = (id: string, clave: string, motivo: string | null) =>
  enviar<Cita>(`/api/citas/${id}/cancelar`, { motivo }, 'POST', { [IDEMPOTENCIA]: clave })

/** API-41. Cancela y crea una nueva ligada a la primera de la cadena (RN-S4-05). */
export const reprogramarCita = (id: string, bloqueNuevoId: string, clave: string, motivo: string | null) =>
  enviar<Cita>(`/api/citas/${id}/reprogramar`, { bloqueNuevoId, motivo }, 'POST', { [IDEMPOTENCIA]: clave })

/** API-42. Solo el prestador, y solo despues de la hora. */
export const registrarAsistencia = (id: string, asistio: boolean) =>
  enviar<Cita>(`/api/citas/${id}/asistencia`, { asistio })

// ---------------------------------------------------------------- hora de Chile

const ZONA = 'America/Santiago'

/**
 * Los instantes viajan en UTC y el prestador publico «lunes 09:00» en hora de pared de Chile
 * (TBL-16). Mostrarlos con la zona del NAVEGADOR le diria a alguien en otro pais una hora que no
 * es la del prestador, asi que se fija la zona.
 */
export function horaDeChile(iso: string): string {
  return new Intl.DateTimeFormat('es-CL', { timeZone: ZONA, hour: '2-digit', minute: '2-digit', hour12: false })
    .format(new Date(iso))
}

export function diaDeChile(iso: string): string {
  return new Intl.DateTimeFormat('es-CL', { timeZone: ZONA, weekday: 'long', day: 'numeric', month: 'long' })
    .format(new Date(iso))
}

/** `2026-11-09`, el dia del calendario CHILENO: sirve de clave para agrupar bloques por dia. */
export function claveDeDia(iso: string): string {
  return new Intl.DateTimeFormat('en-CA', { timeZone: ZONA, year: 'numeric', month: '2-digit', day: '2-digit' })
    .format(new Date(iso))
}

/** Agrupa por dia chileno, en orden. Un bloque de las 22:00 de Chile cae en su dia aunque en UTC sea el siguiente. */
export function porDia(bloques: Bloque[]): { dia: string; titulo: string; bloques: Bloque[] }[] {
  const grupos = new Map<string, Bloque[]>()
  for (const b of [...bloques].sort((x, y) => x.inicioEn.localeCompare(y.inicioEn))) {
    const k = claveDeDia(b.inicioEn)
    grupos.set(k, [...(grupos.get(k) ?? []), b])
  }
  return [...grupos].map(([dia, bs]) => ({ dia, titulo: diaDeChile(bs[0].inicioEn), bloques: bs }))
}
