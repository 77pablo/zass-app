/**
 * El RUT chileno, igual que `Rut.cs` del backend: cuerpo + dígito verificador por módulo 11.
 * Aquí se valida para avisar antes de enviar; el que manda es el backend.
 */

export function digitoVerificador(cuerpo: number): string {
  let suma = 0
  let factor = 2
  for (let n = cuerpo; n > 0; n = Math.floor(n / 10)) {
    suma += (n % 10) * factor
    factor = factor === 7 ? 2 : factor + 1
  }
  const resto = 11 - (suma % 11)
  return resto === 11 ? '0' : resto === 10 ? 'K' : String(resto)
}

/** «12345678-5», o null si no es un RUT válido. Acepta puntos, espacios y la k en minúscula. */
export function normalizarRut(texto: string): string | null {
  const limpio = texto.replace(/[\s.-]/g, '').toUpperCase()
  if (!/^\d{7,8}[\dK]$/.test(limpio)) return null
  const cuerpo = Number(limpio.slice(0, -1))
  if (cuerpo < 1_000_000) return null
  return digitoVerificador(cuerpo) === limpio.slice(-1) ? `${cuerpo}-${limpio.slice(-1)}` : null
}

/** Teléfono chileno de 9 dígitos, móvil o fijo, con o sin +56. Mismo patrón que el backend. */
export function esTelefonoChileno(texto: string): boolean {
  return /^(\+?56)?\s?\d(\s?\d){8}$/.test(texto.trim())
}
