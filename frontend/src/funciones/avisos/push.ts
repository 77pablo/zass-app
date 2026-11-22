import { enviar, ErrorApi, obtener } from '../../api/cliente'

/*
 * ES-3 · Avisos push en ESTE navegador (API-45). El permiso lo da la persona en el navegador; la
 * suscripcion la crea el navegador con la clave publica VAPID del servidor, y se registra en la API
 * para que el trabajo de fondo sepa adonde mandar.
 *
 * El service worker se registra solo en produccion (registrarServiceWorker.ts): en desarrollo
 * esto responde «no-soportado», y para probarlo se usa `npm run build && npm run preview`.
 */

export type EstadoPush =
  | 'no-soportado'   // el navegador no puede (o no hay service worker, como en desarrollo)
  | 'sin-clave'      // el servidor no tiene claves VAPID: el push esta apagado
  | 'bloqueado'      // la persona bloqueo los avisos de ZASS! en el navegador
  | 'inactivo'       // puede activarlos
  | 'activo'         // ya estan activos en este navegador

/** Lo del navegador que se usa, para poder probarlo sin uno de verdad. */
export interface EntornoPush {
  serviceWorker?: Pick<ServiceWorkerContainer, 'getRegistration'>
  permiso: () => NotificationPermission | undefined
  pedirPermiso: () => Promise<NotificationPermission>
}

function entornoReal(): EntornoPush {
  const hayNotificaciones = typeof Notification !== 'undefined'
  const hayPush = typeof window !== 'undefined' && 'PushManager' in window
  return {
    serviceWorker: hayPush && 'serviceWorker' in navigator ? navigator.serviceWorker : undefined,
    permiso: () => (hayNotificaciones ? Notification.permission : undefined),
    pedirPermiso: () => Notification.requestPermission(),
  }
}

/** La clave publica VAPID, o null si el servidor no tiene push (responde 404). */
export async function clavePublica(): Promise<string | null> {
  try {
    const r = await obtener<{ clave: string }>('/api/notificaciones/push/clave')
    return r.clave
  } catch (e) {
    if (e instanceof ErrorApi && e.estado === 404) return null
    throw e
  }
}

/** base64url → bytes, que es como lo pide `pushManager.subscribe`. */
export function claveABytes(base64url: string): Uint8Array<ArrayBuffer> {
  const relleno = '='.repeat((4 - (base64url.length % 4)) % 4)
  const base64 = (base64url + relleno).replace(/-/g, '+').replace(/_/g, '/')
  const binario = atob(base64)
  const bytes = new Uint8Array(new ArrayBuffer(binario.length))
  for (let i = 0; i < binario.length; i++) bytes[i] = binario.charCodeAt(i)
  return bytes
}

async function registro(entorno: EntornoPush): Promise<ServiceWorkerRegistration | undefined> {
  if (!entorno.serviceWorker || entorno.permiso() === undefined) return undefined
  try {
    return await entorno.serviceWorker.getRegistration()
  } catch {
    return undefined
  }
}

/**
 * Como estan los avisos en este navegador. Si ya hay suscripcion, ademas la VUELVE A REGISTRAR en
 * la API (API-45 es idempotente): que el navegador la tenga no prueba que el servidor tambien. El
 * servidor pudo borrarla (410, el tope de 10) o puede estar a nombre de OTRA cuenta (un computador
 * compartido donde la baja al salir no alcanzo a llegar); en los dos casos la persona veria
 * «activo» y no le llegaria nada, o le llegaria a otro. Si el registro falla, se informa
 * «inactivo» para que pueda volver a activarlos.
 */
export async function estadoPush(entorno: EntornoPush = entornoReal()): Promise<EstadoPush> {
  const reg = await registro(entorno)
  if (!reg?.pushManager) return 'no-soportado'
  if ((await clavePublica()) === null) return 'sin-clave'
  if (entorno.permiso() === 'denied') return 'bloqueado'
  const suscripcion = await reg.pushManager.getSubscription()
  if (suscripcion === null || entorno.permiso() !== 'granted') return 'inactivo'
  try {
    await enviar('/api/mi/notificaciones/push', aCmd(suscripcion))
    return 'activo'
  } catch {
    return 'inactivo'
  }
}

/** Lo que la API espera (RegistrarPushCmd): el endpoint y las dos claves del navegador. */
function aCmd(s: PushSubscription) {
  const json = s.toJSON()
  return { endpoint: s.endpoint, p256dh: json.keys?.p256dh ?? '', auth: json.keys?.auth ?? '' }
}

/** Pide el permiso, suscribe este navegador y lo registra en la API. Devuelve el estado final. */
export async function activarPush(entorno: EntornoPush = entornoReal()): Promise<EstadoPush> {
  const reg = await registro(entorno)
  if (!reg?.pushManager) return 'no-soportado'
  const clave = await clavePublica()
  if (clave === null) return 'sin-clave'

  const permiso = await entorno.pedirPermiso()
  if (permiso === 'denied') return 'bloqueado'
  if (permiso !== 'granted') return 'inactivo'   // cerro la pregunta sin responder

  const suscripcion = (await reg.pushManager.getSubscription())
    ?? (await reg.pushManager.subscribe({ userVisibleOnly: true, applicationServerKey: claveABytes(clave) }))
  try {
    await enviar('/api/mi/notificaciones/push', aCmd(suscripcion))
  } catch (e) {
    // Si la API no lo acepto, no se deja una suscripcion huerfana en el navegador.
    await suscripcion.unsubscribe().catch(() => {})
    throw e
  }
  return 'activo'
}

/** Deja de recibir avisos en este navegador: lo da de baja en la API y en el navegador. */
export async function desactivarPush(entorno: EntornoPush = entornoReal()): Promise<EstadoPush> {
  const reg = await registro(entorno)
  const suscripcion = await reg?.pushManager?.getSubscription()
  if (suscripcion) {
    await enviar('/api/mi/notificaciones/push', { endpoint: suscripcion.endpoint }, 'DELETE')
    await suscripcion.unsubscribe().catch(() => {})
  }
  return 'inactivo'
}

/**
 * Al cerrar sesion (decision del 21-nov): este navegador deja de recibir los avisos de esta cuenta.
 * Nunca falla ni tarda mas de unos segundos: cerrar sesion no puede quedar esperando al push.
 */
export async function darDeBajaAlSalir(entorno: EntornoPush = entornoReal(), esperaMaximaMs = 3000): Promise<void> {
  let reloj: ReturnType<typeof setTimeout> | undefined
  const plazo = new Promise<void>((listo) => { reloj = setTimeout(listo, esperaMaximaMs) })
  const trabajo = desactivarPush(entorno).then(() => undefined, () => undefined)
  await Promise.race([trabajo, plazo])
  clearTimeout(reloj)
}
