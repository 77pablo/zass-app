import { esTelefonoChileno, normalizarRut } from './rut'

/** Los mismos de Limites, en Zass.Aplicacion/Identidad/Validadores.cs. */
export const APELLIDO_MAXIMO = 100
export const RAZON_SOCIAL_MAXIMA = 200

/** ES-2. Si la sesión tiene datos pendientes. Una API vieja no manda el campo: nadie queda bloqueado. */
export function faltanDatos(pendientes: string[] | undefined): boolean {
  return Array.isArray(pendientes) && pendientes.length > 0
}

export interface DatosAValidar {
  empresa: boolean
  razonSocial: string
  apellido: string
  rut: string
  telefono: string
  mayorDeEdad: boolean
}

/**
 * Los datos de la cuenta del registro completo (20-nov-2026): razón social o apellido, RUT,
 * teléfono y la casilla de mayoría de edad. Una sola revisión para «Crear cuenta» y para
 * «Completa tus datos» (ES-2), igual que en la API (ValidadorDatosDeCuenta): los mismos
 * mensajes en los dos lados.
 */
export function revisarDatosDeCuenta(d: DatosAValidar): Record<string, string> {
  const malos: Record<string, string> = {}
  if (d.empresa && d.razonSocial.trim() === '') malos.razonsocial = 'Escribe la razón social.'
  if (!d.empresa && d.apellido.trim() === '') malos.apellido = 'Escribe tu apellido.'
  if (d.rut.trim() === '') malos.rut = 'Escribe el RUT.'
  else if (normalizarRut(d.rut) === null) malos.rut = 'Ese RUT no es válido. Revisa el número y el dígito verificador.'
  if (d.telefono.trim() === '') malos.telefono = 'Escribe un teléfono.'
  else if (!esTelefonoChileno(d.telefono)) malos.telefono = 'Escribe un teléfono chileno de 9 dígitos, por ejemplo +56 9 1234 5678.'
  if (!d.mayorDeEdad) malos.mayordeedad = 'Para crear una cuenta tienes que ser mayor de 18 años.'
  return malos
}
