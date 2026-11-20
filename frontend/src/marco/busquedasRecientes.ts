/**
 * Lo que la persona busco antes, para ofrecerselo de nuevo con un toque (como el buscador de
 * cualquier app). Vive SOLO en este navegador (localStorage): no viaja al servidor ni a otra
 * persona. Si el almacenamiento no esta (modo privado, bloqueado), no pasa nada: no hay recientes.
 */
const CLAVE = 'zass-busquedas'
export const MAXIMO_DE_RECIENTES = 5
/** Lo que entra en el campo (maxLength=80): algo mas largo no salio del buscador. */
const LARGO_MAXIMO = 80

export function busquedasRecientes(): string[] {
  try {
    const crudo = JSON.parse(localStorage.getItem(CLAVE) ?? '[]')
    return Array.isArray(crudo)
      ? crudo.filter((b): b is string => typeof b === 'string' && b.trim() !== '' && b.length <= LARGO_MAXIMO).slice(0, MAXIMO_DE_RECIENTES)
      : []
  } catch {
    return []
  }
}

/** La pone primera. Si ya estaba (sin importar mayusculas ni espacios), no la repite. */
export function recordarBusqueda(texto: string): void {
  const limpio = texto.trim().replace(/\s+/g, ' ')
  if (limpio === '' || limpio.length > LARGO_MAXIMO) return
  const igual = (b: string) => b.toLocaleLowerCase('es') === limpio.toLocaleLowerCase('es')
  const nuevas = [limpio, ...busquedasRecientes().filter((b) => !igual(b))].slice(0, MAXIMO_DE_RECIENTES)
  try { localStorage.setItem(CLAVE, JSON.stringify(nuevas)) } catch { /* sin almacenamiento: no se recuerda */ }
}

export function olvidarBusquedas(): void {
  try { localStorage.removeItem(CLAVE) } catch { /* nada que olvidar */ }
}
