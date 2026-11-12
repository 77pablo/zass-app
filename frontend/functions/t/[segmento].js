/**
 * Cloudflare Pages Function para /t/:segmento (API-54, CS-18): la URL impresa en la tarjeta NFC/QR.
 *
 * Solo reenvia a la API y devuelve su respuesta. Las reglas (que tarjeta lleva a donde) viven en la API.
 *  - redirect: 'manual'. Sin esto fetch seguiria el 302 por su cuenta, y el telefono recibiria el HTML que
 *    devuelva la ruta destino en el ORIGEN DE LA API, no una redireccion.
 *  - X-Forwarded-For con CF-Connecting-IP, igual que el proxy de /api: si no, la tasa P de la API contaria a
 *    Cloudflare entero como un solo visitante.
 *  - NO se reenvian las cookies ni las demas cabeceras del visitante: el escaneo no exige sesion (RN-S6-07).
 *  - De la respuesta de la API no pasa nada salvo el estado, la Location y Retry-After. Nunca un Set-Cookie.
 *  - La Location tiene que ser una ruta propia ("/..."). Una absoluta, "//otro-sitio" o "/\otro-sitio" (que
 *    algunos navegadores leen como "//") no se sigue: 502.
 *  - _headers no se aplica a las Functions: las cabeceras de seguridad van a mano (CABECERAS_DE_SEGURIDAD).
 *
 * /T/ (QR que pasan la URL a mayusculas) no llega aqui: lo atiende la app (paginas/AvisoDeTarjeta.tsx).
 */
import { CABECERAS_DE_SEGURIDAD } from '../_comun/seo.js'

const ESPERA_MAXIMA_MS = 4000

/** Una barra y despues cualquier cosa que no sea otra barra ni una barra invertida. */
const RUTA_PROPIA = /^\/(?![/\\])/

function responder(estado, extra = {}) {
  return new Response(null, {
    status: estado,
    headers: {
      ...CABECERAS_DE_SEGURIDAD,
      'Cache-Control': 'no-store',
      // La ficha no necesita saber por que tarjeta se llego.
      'Referrer-Policy': 'no-referrer',
      ...extra,
    },
  })
}

export async function onRequest({ request, env, params }) {
  if (request.method !== 'GET') return responder(405, { Allow: 'GET' })
  // Sin API_ORIGEN no se intenta nada: mejor un error claro que reenviar a un sitio equivocado.
  if (!env.API_ORIGEN) return responder(503, { 'Retry-After': '60' })

  const entrada = new URL(request.url)
  const destino = new URL(`/t/${encodeURIComponent(params.segmento)}${entrada.search}`, env.API_ORIGEN)

  // Cabeceras NUEVAS, no las del visitante: lo que haya mandado en X-Forwarded-* no se cree.
  const cabeceras = new Headers({ 'X-Forwarded-Proto': 'https', 'X-Forwarded-Host': entrada.host })
  const ip = request.headers.get('CF-Connecting-IP')
  if (ip) cabeceras.set('X-Forwarded-For', ip)

  let respuesta
  try {
    respuesta = await fetch(destino, {
      method: 'GET', headers: cabeceras, redirect: 'manual',
      signal: AbortSignal.timeout(ESPERA_MAXIMA_MS),
    })
  } catch {
    return responder(503, { 'Retry-After': '30' })
  }

  if (respuesta.status === 302) {
    const ubicacion = respuesta.headers.get('Location') ?? ''
    return RUTA_PROPIA.test(ubicacion) ? responder(302, { Location: ubicacion }) : responder(502)
  }
  if (respuesta.status === 429) {
    return responder(429, { 'Retry-After': respuesta.headers.get('Retry-After') ?? '900' })
  }
  return responder(502)
}
