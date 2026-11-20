/*
 * ZASS!: el service worker. Hace dos cosas, y nada mas:
 *
 *  1. SIN CONEXION, una pagina amable (/sin-conexion.html) en vez del error del navegador.
 *  2. Guarda los archivos de /assets/, que traen un hash en el nombre y por eso NUNCA cambian:
 *     la segunda visita abre mas rapido, tambien con mala señal.
 *
 * Lo que NO hace, a proposito:
 *  - No guarda la API (/api/): una cita o un perfil viejos serian peor que no tenerlos.
 *  - No guarda las paginas: se piden SIEMPRE a la red primero. Asi, despues de cada despliegue
 *    nadie se queda con la version anterior, y el aviso de mantenimiento (503) llega igual.
 *  - No toca otros dominios ni lo que no sea GET.
 *
 * Es la base que pide PLAN-APP.md antes de envolver la web en Capacitor.
 */
const VERSION = 'zass-v1'
const CACHE = `${VERSION}-estaticos`
const SIN_CONEXION = '/sin-conexion.html'
const PRECARGA = [SIN_CONEXION, '/favicon.svg']
/** Tope de archivos guardados: cada despliegue trae nombres nuevos, y los viejos no se piden mas. */
const MAXIMO_EN_CACHE = 60

self.addEventListener('install', (evento) => {
  evento.waitUntil(
    caches.open(CACHE).then((c) => c.addAll(PRECARGA)).then(() => self.skipWaiting()),
  )
})

self.addEventListener('activate', (evento) => {
  evento.waitUntil(
    caches.keys()
      .then((nombres) => Promise.all(
        nombres.filter((n) => n.startsWith('zass-') && n !== CACHE).map((n) => caches.delete(n)),
      ))
      .then(() => self.clients.claim()),
  )
})

self.addEventListener('fetch', (evento) => {
  const peticion = evento.request
  if (peticion.method !== 'GET') return
  const url = new URL(peticion.url)
  if (url.origin !== self.location.origin) return
  if (url.pathname.startsWith('/api/')) return

  // Una pagina: la red primero; sin red, la pagina de «sin conexion».
  if (peticion.mode === 'navigate') {
    evento.respondWith(
      fetch(peticion).catch(() => caches.match(SIN_CONEXION).then((r) => r ?? Response.error())),
    )
    return
  }

  // Un archivo con hash: lo guardado primero; si no esta, la red, y se guarda para la proxima.
  if (url.pathname.startsWith('/assets/')) {
    evento.respondWith(
      caches.match(peticion).then((guardada) => guardada ?? fetch(peticion).then((respuesta) => {
        if (respuesta.ok) {
          const copia = respuesta.clone()
          evento.waitUntil(caches.open(CACHE).then((c) => c.put(peticion, copia).then(() => recortar(c))))
        }
        return respuesta
      })),
    )
  }
})

/** Deja solo los MAXIMO_EN_CACHE mas nuevos (las claves salen en el orden en que se guardaron). */
function recortar(cache) {
  return cache.keys().then((claves) => {
    const sobran = claves.length - MAXIMO_EN_CACHE
    return sobran > 0 ? Promise.all(claves.slice(0, sobran).map((k) => cache.delete(k))) : undefined
  })
}
