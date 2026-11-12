export type Aviso = 'no-valida' | 'no-disponible' | 'sin-activar'

/**
 * TEXTOS PROPUESTOS, pendientes de aprobacion de Pablo. Ninguno dice de quien es la tarjeta (RN-S6-06).
 *
 * En su propio archivo y no en AvisoDeTarjeta.tsx: un archivo de componentes que tambien exporta
 * constantes deja de recargarse en caliente (regla only-export-components de oxlint).
 */
export const TEXTOS_DE_TARJETA: Record<Aviso, { pestana: string; titulo: string; texto: string }> = {
  'no-valida': {
    pestana: 'Tarjeta no válida',
    titulo: 'Esta tarjeta no es válida.',
    texto: 'Puede que la hayan dado de baja, que la hayan reemplazado por otra o que el enlace esté incompleto. Si buscas a alguien, puedes encontrarlo en ZASS!.',
  },
  'no-disponible': {
    pestana: 'Perfil no disponible',
    titulo: 'Este perfil no está disponible por ahora.',
    texto: 'La persona de esta tarjeta no tiene su perfil publicado en este momento. Mientras tanto, puedes buscar a otras personas que ofrecen lo que necesitas.',
  },
  'sin-activar': {
    pestana: 'Tarjeta sin activar',
    titulo: 'Esta tarjeta todavía no está activa.',
    texto: 'Las tarjetas se activan cuando se entregan. Si es tuya y la recibiste hace poco, pide ayuda a quien te la entregó.',
  },
}
