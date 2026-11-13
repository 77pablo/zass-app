import { COMENTARIO_MAXIMO, COMENTARIO_MINIMO, largo } from './servicio'

/** Lo que se esta escribiendo, antes de que exista una reseña. */
export interface Borrador {
  calificacion: number | null
  comentario: string
  aspectos: string[]
}

export const BORRADOR_VACIO: Borrador = { calificacion: null, comentario: '', aspectos: [] }

/**
 * Lo que falta para poder enviar, en palabras. Null cuando no falta nada.
 *
 * Es la MISMA regla que RN-S5-03 aplica el servidor (calificacion de 1 a 5, comentario de 20 a
 * 1.500 caracteres contados como puntos de codigo). Aqui solo sirve para avisar ANTES de enviar
 * y para decir cuanto falta; el servidor la revalida igual, porque un control de pantalla no
 * protege de nada.
 */
export function queFalta(borrador: Borrador): string | null {
  if (borrador.calificacion === null) return 'Elige cuántas estrellas le pones.'
  const escrito = largo(borrador.comentario.trim())
  if (escrito < COMENTARIO_MINIMO) {
    return `Cuenta un poco más: faltan ${COMENTARIO_MINIMO - escrito} caracteres para los ${COMENTARIO_MINIMO} mínimos.`
  }
  if (escrito > COMENTARIO_MAXIMO) {
    return `Te pasaste por ${escrito - COMENTARIO_MAXIMO} caracteres del máximo de ${COMENTARIO_MAXIMO}.`
  }
  return null
}
