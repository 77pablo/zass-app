/**
 * Cloudflare Pages Function para /prestadores/:slug: devuelve la app (index.html) con las etiquetas de la
 * ficha ya puestas —titulo, descripcion, canonical, Open Graph, Twitter Card y JSON-LD—, porque Google ve
 * casi vacia una SPA que solo pinta la ficha en el navegador.
 *
 * Reglas, en orden de importancia:
 *  - Nunca un 500 hacia Google. Si la API falla o tarda, se responde la app normal (sin etiquetas) con
 *    cache cortisima, y el siguiente rastreo lo reintenta.
 *  - Ficha inexistente o despublicada (404 de la API): la app, con `noindex` y estado 404.
 *  - Todo texto de la API se escapa (ver _comun/seo.js). Solo se usa una lista blanca de campos.
 */
import {
  cabecerasHtml, datosDeFicha, inyectar, metaDeFicha, META_NOINDEX, META_SIN_INDEXAR, origenDelSitio,
} from '../_comun/seo.js'

/** Lo que genera el generador de slugs del backend (minusculas, digitos, guiones) con margen. */
const FORMA_DE_SLUG = /^[a-z0-9][a-z0-9-]{0,119}$/

const CACHE_FICHA = 'public, max-age=0, s-maxage=300, stale-while-revalidate=600'
const CACHE_NO_EXISTE = 'public, max-age=0, s-maxage=60'
const CACHE_API_CAIDA = 'public, max-age=0, s-maxage=10'
const ESPERA_MAXIMA_MS = 4000

async function htmlDeLaApp(request, env) {
  const respuesta = await env.ASSETS.fetch(new URL('/', request.url))
  return respuesta.ok ? await respuesta.text() : null
}

export async function onRequest({ request, env, params }) {
  if (request.method !== 'GET' && request.method !== 'HEAD') return env.ASSETS.fetch(request)

  const html = await htmlDeLaApp(request, env)
  // Sin index.html no hay nada que decorar: que lo resuelva el servido normal de archivos.
  if (html === null) return env.ASSETS.fetch(request)

  const responder = (bloque, estado, cache) =>
    new Response(request.method === 'HEAD' ? null : (bloque === null ? html : inyectar(html, bloque)), {
      status: estado,
      headers: cabecerasHtml(cache),
    })

  const slug = Array.isArray(params.slug) ? params.slug[0] : params.slug
  if (typeof slug !== 'string' || !FORMA_DE_SLUG.test(slug)) {
    return responder(META_SIN_INDEXAR, 200, CACHE_NO_EXISTE)
  }

  if (!env.API_ORIGEN) return responder(null, 200, CACHE_API_CAIDA)

  let respuestaApi
  try {
    const cabeceras = { accept: 'application/json' }
    // La IP real del visitante, para que el limite de la API no cuente a Cloudflare entero como uno solo.
    const ip = request.headers.get('CF-Connecting-IP')
    if (ip) cabeceras['X-Forwarded-For'] = ip
    respuestaApi = await fetch(new URL(`/api/prestadores/${encodeURIComponent(slug)}`, env.API_ORIGEN), {
      headers: cabeceras,
      signal: AbortSignal.timeout(ESPERA_MAXIMA_MS),
    })
  } catch {
    return responder(null, 200, CACHE_API_CAIDA)
  }

  if (respuestaApi.status === 404) return responder(META_NOINDEX, 404, CACHE_NO_EXISTE)
  if (!respuestaApi.ok) return responder(null, 200, CACHE_API_CAIDA)

  let datos = null
  try {
    datos = datosDeFicha(await respuestaApi.json())
  } catch {
    datos = null
  }
  if (datos === null) return responder(null, 200, CACHE_API_CAIDA)

  return responder(metaDeFicha(datos, slug, origenDelSitio(env)), 200, CACHE_FICHA)
}
