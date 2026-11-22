import { enviar, obtener, olvidarCsrf } from '../../api/cliente'

export interface Sesion {
  usuarioId: string
  roles: string[]
  correoVerificado: boolean
}

export type RolPublico = 'buscador' | 'prestador'

/**
 * API-09. La API deja las cookies de sesion; aqui no se guarda ningun token en
 * JavaScript a proposito: una cookie httpOnly no se puede robar con un XSS.
 */
export async function iniciarSesion(correo: string, contrasena: string): Promise<Sesion> {
  const sesion = await enviar<Sesion>('/api/auth/sesion', { correo, contrasena })
  // El token CSRF quedo atado a la identidad anonima. Tras entrar ya no sirve.
  olvidarCsrf()
  return sesion
}

export type TipoDeCuenta = 'particular' | 'empresa'

/** Lo que pide API-08 desde el 20-nov-2026 (registro completo). */
export interface DatosDeRegistro {
  correo: string
  contrasena: string
  /** De pila si es particular; el de la persona de contacto si es empresa. */
  nombre: string
  rol: RolPublico
  tipo: TipoDeCuenta
  apellido?: string
  razonSocial?: string
  rut: string
  telefono: string
  mayorDeEdad: boolean
}

/** API-08. Crea la cuenta y dispara el correo de verificacion. */
export async function registrarse(datos: DatosDeRegistro): Promise<void> {
  await enviar('/api/auth/registro', datos)
}

/** ES-2. Los datos del registro completo, para una cuenta creada antes del 20-nov-2026. */
export interface DatosDeCuenta {
  tipo: TipoDeCuenta
  apellido?: string
  razonSocial?: string
  rut: string
  telefono: string
  mayorDeEdad: boolean
}

/** ES-2. Completa los datos que le faltan a la cuenta. Mismas validaciones que API-08. */
export async function completarDatos(datos: DatosDeCuenta): Promise<void> {
  await enviar('/api/mi/cuenta/datos', datos, 'PUT')
}

/** CS-04. «Ofrecer mis servicios»: agrega el rol de prestador a la cuenta que ya tienes. */
export async function activarPrestador(): Promise<void> {
  await enviar('/api/mi/activar-prestador', undefined)
}

/** API-11. */
export async function cerrarSesion(): Promise<void> {
  await enviar('/api/auth/sesion', undefined, 'DELETE')
  olvidarCsrf()
}

/**
 * API-17 (CS-04). Elimina la cuenta: pide la contraseña, cancela las horas futuras (avisando a
 * la otra persona) y la suscripción, y anonimiza. Responde 204 y cierra la sesión del lado del
 * servidor; el token CSRF de esa sesión ya no sirve, así que se olvida.
 */
export async function eliminarMiCuenta(contrasena: string): Promise<void> {
  await enviar('/api/mi/cuenta', { contrasena }, 'DELETE')
  olvidarCsrf()
}

/** API-13. Sirve para saber si la sesion sigue viva al cargar la pagina. */
export function miCuenta<T>(): Promise<T> {
  return obtener<T>('/api/mi/cuenta')
}

/** API-13. Consume el enlace de verificacion que llego por correo. */
export async function verificarCorreo(token: string): Promise<void> {
  await enviar('/api/auth/correo/verificar', { token })
}

/** Reenvia el enlace de verificacion. Responde igual exista o no la cuenta. */
export async function reenviarVerificacion(correo: string): Promise<void> {
  await enviar('/api/auth/correo/reenviar', { correo })
}

/** API-14. Responde igual exista o no la cuenta. */
export async function pedirRecuperacion(correo: string): Promise<void> {
  await enviar('/api/auth/contrasena/recuperar', { correo })
}

/** API-15. Cambia la contrasena y la API cierra las demas sesiones. */
export async function restablecerContrasena(token: string, contrasenaNueva: string): Promise<void> {
  await enviar('/api/auth/contrasena/restablecer', { token, contrasenaNueva })
}
