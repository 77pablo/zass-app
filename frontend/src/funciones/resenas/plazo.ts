/**
 * RN-S5-05 y CS-16: una reseña se edita durante 72 h desde que se publico, y despues queda fija.
 * La fecha limite la manda el servidor (`editableHasta`), que es quien tiene el reloj bueno; aqui
 * solo se traduce a algo que una persona entienda.
 */

const HORA = 3600 * 1000

/**
 * Lo que queda de plazo, en palabras, o null si ya se acabo. Null es la respuesta que importa:
 * con el plazo vencido el formulario de edicion NO se muestra (CU-16 4D.5), y si se mostrara,
 * el servidor rechazaria el envio y la persona perderia lo que escribio.
 */
export function plazoDeEdicion(editableHasta: string, ahora: Date = new Date()): string | null {
  const resta = new Date(editableHasta).getTime() - ahora.getTime()
  if (Number.isNaN(resta) || resta <= 0) return null

  const horas = Math.floor(resta / HORA)
  if (horas >= 1) return horas === 1 ? '1 hora' : `${horas} horas`

  const minutos = Math.max(1, Math.floor(resta / 60000))
  return minutos === 1 ? '1 minuto' : `${minutos} minutos`
}
