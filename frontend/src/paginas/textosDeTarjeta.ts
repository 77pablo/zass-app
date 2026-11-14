export type Aviso = 'no-valida' | 'no-disponible' | 'sin-activar'

/**
 * Textos aprobados por Pablo el 12-nov-2026 (delego los seis que bloqueaban J3 y J4).
 * Ninguno dice de quien es la tarjeta (RN-S6-06), y ninguno promete un canal de contacto:
 * hoy el sitio no tiene ninguno (`legal.ts` sigue con el correo por completar).
 *
 * En su propio archivo y no en AvisoDeTarjeta.tsx: un archivo de componentes que tambien exporta
 * constantes deja de recargarse en caliente (regla only-export-components de oxlint).
 */
export const TEXTOS_DE_TARJETA: Record<Aviso, { pestana: string; titulo: string; texto: string }> = {
  'no-valida': {
    pestana: 'Tarjeta no encontrada',
    titulo: 'No encontramos esta tarjeta.',
    texto: 'Puede que la hayan dado de baja, que la hayan reemplazado por otra, o que el código no se haya leído completo. Si buscas a alguien en concreto, puedes encontrarlo en ZASS!.',
  },
  'no-disponible': {
    pestana: 'Perfil no disponible',
    titulo: 'Este perfil no está disponible por ahora.',
    texto: 'La persona de esta tarjeta no tiene su perfil publicado en este momento. Si lo que buscas no puede esperar, en ZASS! puedes encontrar a alguien más.',
  },
  'sin-activar': {
    pestana: 'Tarjeta sin activar',
    titulo: 'Esta tarjeta todavía no está activa.',
    texto: 'Una tarjeta ZASS! empieza a funcionar cuando se activa, y esta todavía no lo está: por eso no lleva a ningún perfil. Si es tuya, consúltalo con quien te la entregó.',
  },
}
