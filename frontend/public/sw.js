/*
 * ZASS!: el service worker. Hace tres cosas, y nada mas:
 *
 *  1. SIN CONEXION, una pagina amable (/sin-conexion.html) en vez del error del navegador.
 *  2. Guarda los archivos de /assets/, que traen un hash en el nombre y por eso NUNCA cambian:
 *     la segunda visita abre mas rapido, tambien con mala señal.
 *  3. ES-3: muestra los avisos push y, al tocarlos, abre la pagina del aviso (siempre de ZASS!).
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

/*
 * ES-3 · Avisos push. El servidor manda { titulo, cuerpo, url, etiqueta } cifrado; aqui solo se
 * muestra. La url es SIEMPRE una ruta de este sitio: si llegara otra cosa se abre la portada,
 * para que un aviso nunca pueda llevar a otro dominio.
 */
const ICONO = '/icono-zass-192.png'

function rutaPropia(url) {
  // Empieza con «/» y lo que sigue no es otra «/» ni «\» (los navegadores leen «/\» como «//»).
  return typeof url === 'string' && /^\/(?![/\\])/.test(url) ? url : '/'
}

function texto(valor, porDefecto) {
  return typeof valor === 'string' && valor.trim() !== '' ? valor.slice(0, 300) : porDefecto
}

self.addEventListener('push', (evento) => {
  let datos = {}
  try {
    datos = evento.data ? evento.data.json() : {}
  } catch {
    datos = {}
  }
  if (datos === null || typeof datos !== 'object') datos = {}
  const opciones = {
    body: texto(datos.cuerpo, 'Tienes una novedad en ZASS!.'),
    icon: ICONO,
    badge: ICONO,
    data: { url: rutaPropia(datos.url) },
  }
  if (typeof datos.etiqueta === 'string') opciones.tag = datos.etiqueta.slice(0, 100)
  evento.waitUntil(self.registration.showNotification(texto(datos.titulo, 'ZASS!'), opciones))
})

self.addEventListener('notificationclick', (evento) => {
  evento.notification.close()
  const destino = new URL(rutaPropia(evento.notification.data && evento.notification.data.url), self.location.origin).href
  evento.waitUntil(
    self.clients.matchAll({ type: 'window', includeUncontrolled: true }).then((ventanas) => {
      // Si ZASS! ya esta abierto, se usa esa pestaña en vez de abrir otra.
      const abierta = ventanas.find((v) => new URL(v.url).origin === self.location.origin)
      if (abierta) return abierta.navigate(destino).then((v) => (v || abierta).focus())
      return self.clients.openWindow(destino)
    }),
  )
})
