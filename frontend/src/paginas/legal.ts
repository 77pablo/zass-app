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
export const CORREO_PRIVACIDAD = 'somoszaschile@gmail.com'

/**
 * Cuanto tiempo se guardan los registros de seguridad (IP y fecha de acciones como entrar o cambiar la
 * contrasena). Hoy el sistema NO los borra solo: hay que decidir el plazo y construir el borrado.
 */
export const PLAZO_REGISTROS_DE_SEGURIDAD = '[plazo por completar]'

/**
 * Se actualiza cada vez que cambia el texto de alguna de las dos paginas.
 * PROPUESTO (18-nov-2026, PR [N1] Textos legales): la fecha sube con los textos corregidos de
 * /privacidad, que esperan el visto bueno de Pablo y de su abogado. Antes: '7 de noviembre de 2026'.
 */
export const ULTIMA_ACTUALIZACION = '18 de noviembre de 2026'

/** Un dato por completar se reconoce porque va entre corchetes. */
export function estaPendiente(valor: string): boolean {
  return valor.startsWith('[')
}

/**
 * Los proveedores de infraestructura que tratan datos por encargo (alojamiento, base de datos, correo).
 *
 * PROPUESTO (18-nov-2026), pendiente de Pablo y de su abogado: se agrega Brevo. El backend puede mandar
 * los correos con Brevo o con Resend (`ProveedorDeCorreo` en backend/.../Correo/OpcionesCorreo.cs:6 y
 * AgregarZass.cs:282); produccion usa Resend (TRASPASO.md:68 y :89). El pais de Brevo NO esta
 * verificado, por eso va entre corchetes.
 */
export const PROVEEDORES =
  'Render (alojamiento de la API, Estados Unidos), Neon (base de datos PostgreSQL, Estados '
  + 'Unidos), Cloudflare (sitio y red de entrega, global) y Resend (envío de correos, Brasil). '
  + 'Para el correo, el sistema también puede usar Brevo ([país por confirmar]), que hoy no está activo'

/**
 * PROPUESTO (18-nov-2026): con que garantias se hace la transferencia internacional de datos (Ley
 * 21.719). Lo decide el abogado; aqui no se inventa.
 */
export const GARANTIAS_TRANSFERENCIA = '[garantías de la transferencia internacional por completar con el abogado]'
