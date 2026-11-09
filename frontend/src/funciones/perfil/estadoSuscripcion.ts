import type { Suscripcion } from './servicio'

/** Lo que el prestador ve de su suscripcion. `alerta` pide un aviso aparte en vez de una etiqueta. */
export interface EstadoDeSuscripcion {
  texto: string
  alerta: boolean
}

/** Lo que el servidor devuelve (200, no 404) cuando el perfil todavia no publico. */
const SIN_SUSCRIPCION = 'sin suscripcion'

function fechaLarga(iso: string | null): string | null {
  if (iso === null) return null
  const f = new Date(iso)
  if (Number.isNaN(f.getTime())) return null
  return new Intl.DateTimeFormat('es-CL', {
    day: 'numeric', month: 'long', year: 'numeric', timeZone: 'America/Santiago',
  }).format(f)
}

/**
 * Traduce la suscripcion a lo que se muestra en Mi ficha. Devuelve null cuando no hay nada que
 * decir (sin suscripcion todavia, o sin dato). Mientras el estado no este vigente la busqueda
 * esconde la ficha (RN-S3-09), y el texto lo dice.
 */
export function describirSuscripcion(s: Suscripcion | null): EstadoDeSuscripcion | null {
  if (s === null || s.estado === SIN_SUSCRIPCION) return null

  if (s.estado === 'Cancelada') {
    return { texto: 'Suscripción cancelada: tu ficha no aparece en las búsquedas', alerta: true }
  }
  if (!s.vigente) {
    return { texto: 'Suscripción vencida: tu ficha no aparece en las búsquedas', alerta: true }
  }
  if (s.enPrueba) {
    const n = s.diasRestantes
    return { texto: `Prueba · ${n} ${n === 1 ? 'día' : 'días'}`, alerta: false }
  }
  if (s.estado === 'Activa') {
    const hasta = fechaLarga(s.vigenteHasta)
    return { texto: hasta ? `Suscripción activa · hasta el ${hasta}` : 'Suscripción activa', alerta: false }
  }
  return null
}