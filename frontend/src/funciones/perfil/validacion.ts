/**
 * Los datos de contacto del perfil, validados en el navegador.
 *
 * Estas reglas ESPEJAN las de ValidadorEditarPerfilPrestador en el backend, y no las
 * reemplazan: el servidor manda y vuelve a validar todo. Estan aqui para que la persona se
 * entere mientras escribe y no despues de pulsar Guardar.
 */

export interface DatosEnFormulario {
  nombrePublico: string
  descripcion: string
  /** El valor del <select>: "" si todavia no eligio. */
  rubroId: string
  telefonoPublico: string
  correoPublico: string
  whatsapp: string
  sitioWeb: string
}

export type ErroresDelFormulario = Partial<Record<keyof DatosEnFormulario, string>>

/**
 * Los mismos numeros que el backend (Limites). La descripcion es 1500 y no 2000 por decision
 * de Pablo: una presentacion mas larga ya no se lee en una pantalla de celular.
 */
export const LIMITES = {
  nombrePublico: 150,
  descripcion: 1500,
  correo: 254,
  url: 300,
} as const

/**
 * Movil chileno, con o sin +56 y con o sin espacios. No se acepta un fijo a proposito:
 * ZASS! contacta por WhatsApp y llamada, y el movil es lo util. Es el mismo regex del
 * backend (Limites.MovilChileno).
 */
const MOVIL_CHILENO = /^(\+?56)?\s?9\s?\d{4}\s?\d{4}$/

/**
 * Solo pide un @ con algo a cada lado y sin espacios. FluentValidation.EmailAddress()
 * hace lo mismo y acepta "a@b": la pantalla es igual de permisiva a proposito, porque
 * rechazar lo que el servidor acepta dejaria a alguien con un correo valido sin poder
 * guardar.
 */
const CORREO = /^[^@\s]+@[^@\s]+$/

export function validarDatos(d: DatosEnFormulario): ErroresDelFormulario {
  const errores: ErroresDelFormulario = {}

  const nombre = d.nombrePublico.trim()
  if (nombre === '') errores.nombrePublico = 'Escribe el nombre con el que te van a ver.'
  else if (nombre.length > LIMITES.nombrePublico)
    errores.nombrePublico = `Usa ${LIMITES.nombrePublico} caracteres o menos.`

  if (d.descripcion.length > LIMITES.descripcion)
    errores.descripcion = `Usa ${LIMITES.descripcion} caracteres o menos.`

  const telefono = d.telefonoPublico.trim()
  if (telefono !== '' && !MOVIL_CHILENO.test(telefono))
    errores.telefonoPublico = 'Ese teléfono no parece un móvil chileno.'

  const whatsapp = d.whatsapp.trim()
  if (whatsapp !== '' && !MOVIL_CHILENO.test(whatsapp))
    errores.whatsapp = 'Ese WhatsApp no parece un móvil chileno.'

  const correo = d.correoPublico.trim()
  if (correo !== '') {
    if (!CORREO.test(correo)) errores.correoPublico = 'Ese correo no tiene forma de correo.'
    else if (correo.length > LIMITES.correo)
      errores.correoPublico = `Usa ${LIMITES.correo} caracteres o menos.`
  }

  const sitio = d.sitioWeb.trim()
  if (sitio !== '') {
    if (!/^https?:\/\//i.test(sitio))
      errores.sitioWeb = 'El sitio web tiene que empezar con http o https.'
    else if (sitio.length > LIMITES.url)
      errores.sitioWeb = `Usa ${LIMITES.url} caracteres o menos.`
  }

  return errores
}

const CAMPOS: (keyof DatosEnFormulario)[] = [
  'nombrePublico', 'descripcion', 'rubroId', 'telefonoPublico', 'correoPublico',
  'whatsapp', 'sitioWeb',
]

/**
 * El cliente HTTP deja los campos de un error de validacion en minuscula ("nombrepublico").
 * Aqui se llevan de vuelta al nombre del campo del formulario, para mostrarlos donde
 * corresponde. Lo que el formulario no tiene (version, latitud...) se ignora.
 */
export function erroresDelServidor(campos: Record<string, string>): ErroresDelFormulario {
  const errores: ErroresDelFormulario = {}
  for (const campo of CAMPOS) {
    const mensaje = campos[campo.toLowerCase()]
    if (mensaje) errores[campo] = mensaje
  }
  return errores
}
