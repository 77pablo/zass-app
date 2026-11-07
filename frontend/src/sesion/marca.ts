/**
 * Una pista de que este navegador tuvo una sesion abierta. NO es la sesion (esa vive en una
 * cookie httpOnly que JavaScript no puede leer) ni un dato sensible: solo evita preguntarle
 * a la API "quien eres" a quien nunca entro, cosa que respondia 401 en cada visita y llenaba la
 * consola de un error que no lo es.
 *
 * Si la marca falta pero la cookie sigue viva (otro navegador, datos borrados), la persona ve
 * "Entrar" y al entrar todo queda como estaba. Si la marca sobra, la API responde 401 y se borra.
 */
const CLAVE = 'zass-sesion'

export function hayMarcaDeSesion(): boolean {
  try {
    return localStorage.getItem(CLAVE) === '1'
  } catch {
    return false
  }
}

export function ponerMarcaDeSesion(): void {
  try { localStorage.setItem(CLAVE, '1') } catch { /* sin almacenamiento: se pregunta siempre que se recarga */ }
}

export function quitarMarcaDeSesion(): void {
  try { localStorage.removeItem(CLAVE) } catch { /* nada que quitar */ }
}
