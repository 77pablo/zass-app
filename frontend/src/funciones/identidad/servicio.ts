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

/** API-08. Crea la cuenta y dispara el correo de verificacion. */
export async function registrarse(
  correo: string, contrasena: string, nombre: string, rol: RolPublico,
): Promise<void> {
  await enviar('/api/auth/registro', { correo, contrasena, nombre, rol })
}

/** API-11. */
export async function cerrarSesion(): Promise<void> {
  await enviar('/api/auth/sesion', undefined, 'DELETE')
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

/** API-14. Responde igual exista o no la cuenta. */
export async function pedirRecuperacion(correo: string): Promise<void> {
  await enviar('/api/auth/contrasena/recuperar', { correo })
}

/** API-15. Cambia la contrasena y la API cierra las demas sesiones. */
export async function restablecerContrasena(token: string, contrasenaNueva: string): Promise<void> {
  await enviar('/api/auth/contrasena/restablecer', { token, contrasenaNueva })
}
