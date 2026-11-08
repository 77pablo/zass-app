/**
 * Los datos que solo Pablo puede llenar, y el interruptor del aviso de borrador.
 *
 * ESTO NO ES ASESORIA LEGAL. Los textos de /terminos y /privacidad son un borrador honesto de lo que el
 * producto hace HOY, escrito para que un abogado lo revise. Mientras `BORRADOR_LEGAL` sea `true`, las dos
 * paginas muestran «Borrador: pendiente de revision legal» arriba. Cuando un abogado los apruebe, se
 * completan las constantes de abajo y se cambia `BORRADOR_LEGAL` a `false`.
 */
export const BORRADOR_LEGAL = true

/** Quien es el responsable del tratamiento de los datos. Llenar con la razon social y el RUT reales. */
export const RESPONSABLE = '[Razón social y RUT por completar]'

/** Casilla a la que se escribe para ejercer los derechos de la Ley 21.719. */
export const CORREO_PRIVACIDAD = '[correo de privacidad por completar]'

/**
 * Cuanto tiempo se guardan los registros de seguridad (IP y fecha de acciones como entrar o cambiar la
 * contrasena). Hoy el sistema NO los borra solo: hay que decidir el plazo y construir el borrado.
 */
export const PLAZO_REGISTROS_DE_SEGURIDAD = '[plazo por completar]'

/** Se actualiza cada vez que cambia el texto de alguna de las dos paginas. */
export const ULTIMA_ACTUALIZACION = '7 de noviembre de 2026'

/** Un dato por completar se reconoce porque va entre corchetes. */
export function estaPendiente(valor: string): boolean {
  return valor.startsWith('[')
}

/** Los proveedores de infraestructura que tratan datos por encargo (alojamiento, base de datos, correo). */
export const PROVEEDORES = '[lista de proveedores por completar]'
